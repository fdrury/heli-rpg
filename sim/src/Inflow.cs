namespace Rotorwash.Sim;

/// <summary>
/// Induced-velocity (inflow) model for a lifting rotor.
///
/// This one class is responsible for three of the four flight behaviours that make a
/// helicopter feel like a helicopter rather than a drone:
///
///   * Translational lift  - induced velocity collapses as the rotor outruns its own
///                           wake, so the aircraft gains lift for free through ~15-25 kt.
///   * Vortex ring state   - momentum theory is invalid when descending into your own
///                           downwash; the empirical branch here reproduces the settling-
///                           with-power trap (and the fact that pulling collective makes
///                           it worse, while flying forward fixes it).
///   * Ground effect       - a cushion of reduced induced velocity near the surface.
///
/// The fourth (autorotation) falls out of the powerplant freewheel plus the windmill-
/// brake branch below.
///
/// State is carried between frames as a first-order lag (Pitt-Peters style dynamic
/// inflow), which is both more correct than an inner Newton loop and much cheaper.
/// </summary>
public sealed class Inflow
{
    /// <summary>Non-dimensional mean induced velocity, positive downward through the disc.</summary>
    public double Lambda0 { get; private set; }

    /// <summary>Dynamic-inflow time constant (s). Real rotors settle in ~0.1-0.3 s.</summary>
    public double TimeConstant { get; init; } = 0.12;

    /// <summary>Empirical correction for non-uniform inflow and tip loss. 1.10-1.20 typical.</summary>
    public double Kappa { get; init; } = 1.15;

    /// <summary>How violently VRS shakes the aircraft. 0 disables the buffet, not the lift loss.</summary>
    public double VrsBuffet { get; init; } = 1.0;

    /// <summary>Seconds for the vortex ring to build once the rotor enters the region.</summary>
    public double VrsFormTime { get; init; } = 0.7;

    /// <summary>
    /// Seconds for the ring to break down once the rotor leaves the region. Deliberately
    /// longer than the formation time: a developed vortex ring is a real flow structure
    /// with its own momentum, and it does not politely vanish the instant the descent
    /// rate changes. This asymmetry is what makes settling with power a trap rather than
    /// a transient - the pilot escapes the condition several seconds before the rotor does.
    /// </summary>
    public double VrsDecayTime { get; init; } = 2.6;

    /// <summary>Set when the rotor is inside the vortex-ring region (0..1 severity).</summary>
    public double VrsSeverity { get; private set; }

    /// <summary>Wake skew angle (rad), 0 = straight down (hover), pi/2 = fully swept back.</summary>
    public double WakeSkew { get; private set; }

    /// <summary>Drees longitudinal inflow gradient coefficient.</summary>
    public double Kx { get; private set; }

    /// <summary>Drees lateral inflow gradient coefficient.</summary>
    public double Ky { get; private set; }

    private readonly Random _rng = new(12345);
    private double _buffetPhase;

    /// <summary>
    /// Advance the inflow state.
    /// </summary>
    /// <param name="thrust">Current rotor thrust, N (may be negative).</param>
    /// <param name="rho">Air density, kg/m^3.</param>
    /// <param name="discArea">Rotor disc area, m^2.</param>
    /// <param name="tipSpeed">Omega * R, m/s. Must be greater than zero.</param>
    /// <param name="muX">In-plane advance ratio, |V_inplane| / tipSpeed.</param>
    /// <param name="lambdaC">Freestream inflow ratio through the disc, positive DOWN
    /// (positive in a climb, negative in a descent).</param>
    /// <param name="heightAgl">Height of the rotor above ground, m (large = out of ground effect).</param>
    /// <param name="radius">Rotor radius, m.</param>
    /// <param name="dt">Timestep, s.</param>
    public void Update(double thrust, double rho, double discArea, double tipSpeed,
                       double muX, double lambdaC, double heightAgl, double radius, double dt)
    {
        if (tipSpeed < 1.0 || discArea <= 0 || rho <= 0)
        {
            Lambda0 = ExpDecay(Lambda0, 0.0, TimeConstant, dt);
            VrsSeverity = 0;
            return;
        }

        // Hover induced velocity for this thrust: v_h = sqrt(T / 2 rho A).
        double signT = thrust >= 0 ? 1.0 : -1.0;
        double vh = Math.Sqrt(Math.Abs(thrust) / (2.0 * rho * discArea));
        double lambdaH = vh / tipSpeed;

        double target;
        double rawSeverity = 0;

        if (lambdaH < 1e-6)
        {
            target = 0.0;
            VrsSeverity = 0;
        }
        else
        {
            // Normalised climb rate: +1 means climbing at one hover-induced-velocity.
            double vBar = lambdaC / lambdaH;

            // The vortex-ring / turbulent-wake region only exists at low advance ratio.
            // Above mu ~ 0.10 (roughly 20 kt on a medium twin) the wake is swept clear
            // behind the disc and momentum theory is valid again - which is exactly the
            // real-world recovery technique: fly forward, do not pull up.
            double muBar = muX / Math.Max(lambdaH, 1e-6);
            double vrsWindow = 1.0 - Airfoil.Smoothstep(0.7, 1.6, muBar);

            bool inEmpirical = vBar > -2.0 && vBar < 0.0 && vrsWindow > 0.001;

            double lambdaIMomentum = SolveMomentum(lambdaH, muX, lambdaC);

            if (inEmpirical)
            {
                // Classical empirical fit to measured induced velocity through the
                // turbulent-wake and vortex-ring states (Johnson, Helicopter Theory).
                // Continuous with momentum theory at both ends of [-2, 0].
                double v = vBar;
                double viOverVh = 1.0
                                  - 1.125 * v
                                  - 1.372 * v * v
                                  - 1.718 * v * v * v
                                  - 0.655 * v * v * v * v;
                double lambdaIEmpirical = viOverVh * lambdaH;

                target = lambdaIEmpirical * vrsWindow + lambdaIMomentum * (1 - vrsWindow);

                // Severity peaks around vBar = -1.1, which is roughly -700 fpm at low
                // speed on a typical medium helicopter. That is the number pilots are
                // taught to fear.
                double sev = Math.Exp(-Math.Pow((vBar + 1.1) / 0.55, 2.0));
                rawSeverity = Math.Clamp(sev * vrsWindow, 0, 1);
            }
            else
            {
                target = lambdaIMomentum;
                rawSeverity = 0;
            }

            // Lag the severity, then re-blend toward the empirical branch using the
            // lagged value, so the ring persists after the flight condition has moved on.
            double tauV = rawSeverity > VrsSeverity ? VrsFormTime : VrsDecayTime;
            VrsSeverity += (rawSeverity - VrsSeverity) * (1.0 - Math.Exp(-dt / Math.Max(tauV, 1e-3)));

            if (VrsSeverity > rawSeverity + 0.02)
            {
                // Still inside a decaying ring: keep charging the rotor for it.
                double held = lambdaIMomentum * (1.0 + 1.1 * VrsSeverity);
                target = Math.Max(target, held);
            }

            target *= Kappa * signT;

            // Ground effect: the surface blocks wake contraction, so less induced
            // velocity is needed for the same thrust - free lift in the last rotor
            // radius of altitude. Cheeseman-Bennett, washed out with forward speed.
            if (heightAgl < radius * 3.0 && heightAgl > 0.05)
            {
                double zr = heightAgl / radius;
                double lam = Math.Max(Math.Abs(target), 1e-4);
                double speedWashout = 1.0 / (1.0 + Math.Pow(muX / lam, 2.0));
                double ige = 1.0 - (1.0 / (16.0 * zr * zr)) * speedWashout;
                target *= Math.Clamp(ige, 0.35, 1.0);
            }
        }

        // Dynamic inflow lag. Faster in forward flight (the wake convects away sooner).
        double tau = TimeConstant / (1.0 + 4.0 * muX);
        Lambda0 = ExpDecay(Lambda0, target, tau, dt);

        // Wake skew and the Drees linear inflow distribution: more inflow at the back
        // of the disc than the front in forward flight. This is what produces the
        // longitudinal flapping ("blowback") that makes a helicopter pitch up as it
        // accelerates, and it must be modelled or forward flight feels wrong.
        double lamTotal = Lambda0 + lambdaC;
        WakeSkew = Math.Atan2(muX, Math.Abs(lamTotal) + 1e-6);
        double chi = WakeSkew;
        if (muX > 1e-4)
        {
            Kx = (4.0 / 3.0) * ((1.0 - Math.Cos(chi) - 1.8 * muX * muX) / Math.Max(Math.Sin(chi), 1e-3));
            Kx = Math.Clamp(Kx, -2.0, 2.0);
            Ky = -2.0 * muX;
        }
        else { Kx = 0; Ky = 0; }

        _buffetPhase += dt * 11.0;
    }

    /// <summary>
    /// Thrust-fluctuation multiplier while in the vortex ring state. Applied by the
    /// rotor so the aircraft shakes, loses lift unpredictably, and refuses to arrest
    /// the descent with collective alone.
    /// </summary>
    public double BuffetFactor()
    {
        if (VrsSeverity <= 0.001 || VrsBuffet <= 0) return 1.0;
        double n = Math.Sin(_buffetPhase) * 0.6 + (_rng.NextDouble() - 0.5) * 0.8;
        return 1.0 + n * 0.12 * VrsSeverity * VrsBuffet;
    }

    /// <summary>Random roll/pitch upset factor while in VRS, as a fraction of rotor thrust.</summary>
    public double BuffetMoment() =>
        VrsSeverity <= 0.001 ? 0.0 : (_rng.NextDouble() - 0.5) * 0.06 * VrsSeverity * VrsBuffet;

    /// <summary>
    /// Momentum-theory inflow: lambda_i = lambda_h^2 / sqrt(mu^2 + (lambda_c + lambda_i)^2).
    /// Solved by damped fixed-point iteration, which converges in a handful of steps for
    /// every state we actually reach and never produces the sign flips that Newton can.
    /// </summary>
    private static double SolveMomentum(double lambdaH, double mu, double lambdaC)
    {
        double li = lambdaH;              // hover value is a good seed everywhere
        double lh2 = lambdaH * lambdaH;
        for (int i = 0; i < 24; i++)
        {
            double denom = Math.Sqrt(mu * mu + (lambdaC + li) * (lambdaC + li));
            double next = denom > 1e-8 ? lh2 / denom : lambdaH;
            double delta = next - li;
            li += delta * 0.5;            // damping: the windmill branch is stiff
            if (Math.Abs(delta) < 1e-7) break;
        }
        return double.IsFinite(li) ? li : lambdaH;
    }

    private static double ExpDecay(double current, double target, double tau, double dt)
    {
        if (tau <= 1e-6) return target;
        double a = 1.0 - Math.Exp(-dt / tau);
        return current + (target - current) * a;
    }

    public void Reset(double lambda0 = 0) { Lambda0 = lambda0; VrsSeverity = 0; }
}
