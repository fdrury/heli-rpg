using System;

namespace Rotorwash.Sim;

/// <summary>The controls and attitude that hold a given flight condition.</summary>
public readonly struct TrimResult
{
    public readonly Controls Controls;
    /// <summary>Bank angle, rad. Positive is right wing down.</summary>
    public readonly double RollRad;
    /// <summary>Pitch attitude, rad. Positive is nose up.</summary>
    public readonly double PitchRad;
    /// <summary>Residual specific force, in g. Zero is a perfect trim.</summary>
    public readonly double ForceResidualG;
    /// <summary>Residual moment, N·m.</summary>
    public readonly double MomentResidual;
    public readonly bool Converged;
    public readonly int Iterations;

    public TrimResult(Controls c, double roll, double pitch, double fRes, double mRes,
                      bool converged, int iters)
    {
        Controls = c; RollRad = roll; PitchRad = pitch;
        ForceResidualG = fRes; MomentResidual = mRes;
        Converged = converged; Iterations = iters;
    }

    public override string ToString() =>
        $"coll {Controls.Collective:F3}  lon {Controls.CyclicPitch:+0.000;-0.000}  " +
        $"lat {Controls.CyclicRoll:+0.000;-0.000}  ped {Controls.Pedal:+0.000;-0.000}  " +
        $"att {PitchRad * 180 / Math.PI:+0.0;-0.0} pitch / {RollRad * 180 / Math.PI:+0.0;-0.0} roll  " +
        $"[{(Converged ? "converged" : "NOT CONVERGED")} in {Iterations}, " +
        $"residual {ForceResidualG:F5} g, {MomentResidual:F0} N·m]";
}

/// <summary>
/// Solves for the controls and attitude that actually balance the aircraft.
///
/// This exists because the aircraft did not have one, and the absence was the single
/// biggest thing wrong with how it felt to fly. A helicopter's tail rotor pushes sideways
/// - on this airframe about 3.6 kN of it - and nothing in the world cancels that except
/// the pilot banking slightly into it and holding a little lateral cyclic. The aircraft
/// used to be spawned wings-level with the stick centred, which is not a trim state at
/// all: it began accelerating sideways and rolling on the first frame, every frame, and
/// the pilot's whole job became chasing a divergence that was never going to stop. Chasing
/// a divergence with a lagged actuator is the textbook recipe for pilot-induced
/// oscillation, which is exactly what it felt like.
///
/// So: six unknowns (collective, both cyclics, pedal, pitch attitude, bank angle) against
/// six residuals (three forces, three moments, body axes, gravity included). Damped Newton
/// with a numerical Jacobian. Nothing clever - the model is the real one, evaluated by
/// running the actual simulation with the flight state pinned, which is the software
/// equivalent of a wind tunnel sting.
///
/// The pinning is the part worth understanding. Each evaluation steps the real
/// <see cref="Helicopter"/> forward but resets the flight state before every step, so the
/// rigid body never moves while the rotor's slow states - inflow lag, blade flapping,
/// coning - relax to whatever they would settle at in that condition. Reading the wrench
/// straight out of a single call would instead read a transient, and the solver would
/// converge on a trim for an aircraft that does not exist.
/// </summary>
public static class Trim
{
    private const int Unknowns = 6;

    /// <summary>
    /// Solve for trim in level flight at the given airspeed.
    ///
    /// Solved at heading zero in still air: yaw orientation does not change the balance,
    /// so the result applies at any heading. The aircraft is left placed at the solution,
    /// which is what every caller wants anyway.
    /// </summary>
    public static TrimResult Solve(Helicopter h, double altitude = 200, double forwardSpeed = 0,
                                   int maxIterations = 40, double tolerance = 1e-6)
    {
        // Continuation. Newton finds hover from a cold start easily, but at 100 kt it stalls
        // in a flat patch of the residual surface and never recovers. Marching up in speed
        // and seeding each solve from the previous answer keeps every step inside the region
        // where the linearisation is any good. Trim is solved rarely enough that the extra
        // work costs nothing anyone can perceive.
        const double stride = 10.0;                      // m/s between continuation steps
        double[]? seed = null;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(forwardSpeed) / stride));
        TrimResult result = default;
        for (int i = 1; i <= steps; i++)
        {
            double v = forwardSpeed * i / steps;
            result = SolveFrom(h, altitude, v, seed, maxIterations, tolerance, out seed);
        }
        return result;
    }

    private static TrimResult SolveFrom(Helicopter h, double altitude, double forwardSpeed,
                                        double[]? seed, int maxIterations, double tolerance,
                                        out double[] solution)
    {
        bool groundModel = h.UseInternalGroundModel;
        bool sas = h.Sas.Enabled;
        h.UseInternalGroundModel = false;
        // Trim the AIRCRAFT, not the aircraft-plus-augmentation. With the SAS live the
        // solver would find a balance that only exists while the system has authority left,
        // and the trim would quietly move when it saturated.
        h.Sas.Enabled = false;
        try
        {
            // Start from a hover-ish guess. Collective near half travel and everything else
            // centred is close enough for Newton to find its way from any sane airframe.
            var x = new double[Unknowns];
            if (seed is not null) Array.Copy(seed, x, Unknowns); else x[0] = 0.5;

            double mg = h.TotalMass * Atmosphere.Gravity;
            double momentScale = Math.Max(mg * h.Airframe.MainRotor.Radius, 1.0);

            var r = new double[Unknowns];
            Residuals(h, x, altitude, forwardSpeed, mg, momentScale, r);
            double norm = Norm(r);

            int iter = 0;
            var jac = new double[Unknowns, Unknowns];
            var perturbed = new double[Unknowns];
            var trial = new double[Unknowns];

            for (; iter < maxIterations && norm > tolerance; iter++)
            {
                // Numerical Jacobian, forward differences. Central differences would be
                // more accurate and twice the cost, and Newton does not need the accuracy
                // when it is only choosing a direction.
                for (int col = 0; col < Unknowns; col++)
                {
                    double step = col < 4 ? 2e-4 : 2e-4;   // control units / radians
                    var xp = (double[])x.Clone();
                    xp[col] += step;
                    Residuals(h, xp, altitude, forwardSpeed, mg, momentScale, perturbed);
                    for (int row = 0; row < Unknowns; row++)
                        jac[row, col] = (perturbed[row] - r[row]) / step;
                }

                if (!SolveLinear(jac, r, out double[] dx)) break;

                // Backtracking line search. The rotor model is stiff in places - notably
                // once blade stall starts - and an undamped Newton step can walk straight
                // into a region where the residual is worse than where it started.
                double alpha = 1.0;
                bool improved = false;
                for (int back = 0; back < 12; back++)
                {
                    for (int i = 0; i < Unknowns; i++) trial[i] = x[i] - alpha * dx[i];
                    Clamp(trial);
                    Residuals(h, trial, altitude, forwardSpeed, mg, momentScale, perturbed);
                    double trialNorm = Norm(perturbed);
                    if (trialNorm < norm)
                    {
                        Array.Copy(trial, x, Unknowns);
                        Array.Copy(perturbed, r, Unknowns);
                        norm = trialNorm;
                        improved = true;
                        break;
                    }
                    alpha *= 0.5;
                }
                if (!improved) break;
            }

            var controls = ControlsFrom(x);
            solution = (double[])x.Clone();
            Apply(h, x, altitude, forwardSpeed);

            // Report the residual in units a person can judge: a force residual in g, and
            // a raw moment. Anything under a thousandth of a g is far below what a pilot
            // could feel over the time it takes to move a hand.
            double fRes = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]);
            double mRes = Math.Sqrt(r[3] * r[3] + r[4] * r[4] + r[5] * r[5]) * momentScale;
            return new TrimResult(controls, x[5], x[4], fRes, mRes, norm <= tolerance * 50, iter);
        }
        finally { h.UseInternalGroundModel = groundModel; h.Sas.Enabled = sas; }
    }

    // ------------------------------------------------------------------ residuals

    private static Controls ControlsFrom(double[] x) => new()
    {
        Collective = x[0],
        CyclicPitch = x[1],
        CyclicRoll = x[2],
        Pedal = x[3],
        Throttle = 1.0,
    };

    private static void Clamp(double[] x)
    {
        x[0] = Math.Clamp(x[0], 0.0, 1.0);
        x[1] = Math.Clamp(x[1], -1.0, 1.0);
        x[2] = Math.Clamp(x[2], -1.0, 1.0);
        x[3] = Math.Clamp(x[3], -1.0, 1.0);
        x[4] = Math.Clamp(x[4], -0.8, 0.8);
        x[5] = Math.Clamp(x[5], -0.8, 0.8);
    }

    /// <summary>Pin the aircraft into the candidate condition.</summary>
    private static void Apply(Helicopter h, double[] x, double altitude, double forwardSpeed)
    {
        h.State.Position = new Vec3(0, 0, -altitude);
        h.State.Orientation = Quat.FromEuler(x[5], x[4], 0);
        h.State.Velocity = new Vec3(forwardSpeed, 0, 0);
        h.State.AngularVelocity = Vec3.Zero;
        Controls c = ControlsFrom(x);
        h.Input = c;
        h.ForceActuators(c);
    }

    /// <summary>
    /// Net specific force and moment in body axes, non-dimensionalised, after letting the
    /// rotor states relax at a pinned flight condition.
    /// </summary>
    private static void Residuals(Helicopter h, double[] x, double altitude, double forwardSpeed,
                                  double mg, double momentScale, double[] r)
    {
        const double dt = 1.0 / 240.0;
        const int relax = 360;

        // One rotor revolution's worth of samples. A two-bladed rotor puts a strong 2/rev
        // component into the hub moments; averaging over a whole revolution removes it
        // rather than letting the solver chase a number that depends on blade azimuth.
        double omega = Math.Max(h.RotorOmega, 1.0);
        int average = Math.Max(8, (int)Math.Round(2 * Math.PI / omega / dt));

        // Start every evaluation from the same rotor state, and hold the fuel load fixed
        // so the mass does not creep between evaluations. Without this the residual is a
        // function of evaluation ORDER as well as of the unknowns, and Newton is being fed
        // noise dressed up as a derivative.
        double fuel = h.Fuel;
        Apply(h, x, altitude, forwardSpeed);
        h.ResetRotorState();

        Vec3 fSum = Vec3.Zero, mSum = Vec3.Zero;
        int samples = 0;

        for (int i = 0; i < relax; i++)
        {
            h.Fuel = fuel;
            Apply(h, x, altitude, forwardSpeed);   // pin: the body never moves
            h.Step(dt);
            if (i >= relax - average)
            {
                fSum += h.LastForceBody;
                mSum += h.LastMomentBody;
                samples++;
            }
        }

        Vec3 force = fSum / samples;
        Vec3 moment = mSum / samples;

        // Gravity, resolved into body axes. It is applied in the world frame inside Step,
        // so the wrench the model reports is aerodynamic only.
        var q = Quat.FromEuler(x[5], x[4], 0);
        Vec3 gravityBody = q.InverseRotate(new Vec3(0, 0, mg));
        force += gravityBody;

        r[0] = force.X / mg;
        r[1] = force.Y / mg;
        r[2] = force.Z / mg;
        r[3] = moment.X / momentScale;
        r[4] = moment.Y / momentScale;
        r[5] = moment.Z / momentScale;
    }

    private static double Norm(double[] v)
    {
        double s = 0;
        for (int i = 0; i < v.Length; i++) s += v[i] * v[i];
        return Math.Sqrt(s);
    }

    // ------------------------------------------------------------ 6x6 linear solve

    /// <summary>Gaussian elimination with partial pivoting. Six unknowns does not justify more.</summary>
    private static bool SolveLinear(double[,] a, double[] b, out double[] x)
    {
        int n = b.Length;
        var m = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n] = b[i];
        }

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;

            if (Math.Abs(m[pivot, col]) < 1e-14) { x = new double[n]; return false; }

            if (pivot != col)
                for (int j = col; j <= n; j++)
                    (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);

            for (int row = col + 1; row < n; row++)
            {
                double f = m[row, col] / m[col, col];
                for (int j = col; j <= n; j++) m[row, j] -= f * m[col, j];
            }
        }

        x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double s = m[row, n];
            for (int j = row + 1; j < n; j++) s -= m[row, j] * x[j];
            x[row] = s / m[row, row];
        }
        return true;
    }
}
