namespace Rotorwash.Sim;

/// <summary>Everything the rotor produced this step, in airframe body axes (FRD).</summary>
public struct RotorOutput
{
    /// <summary>Resultant aerodynamic force at the centre of gravity, N.</summary>
    public Vec3 Force;
    /// <summary>Resultant moment about the centre of gravity, N.m.</summary>
    public Vec3 Moment;
    /// <summary>Torque the rotor demands from the shaft, N.m (positive = power required).</summary>
    public double ShaftTorque;

    // --- Diagnostics, for the HUD, the damage model and the tuning tools -----
    public double Thrust;          // N, along the negative shaft axis
    public double Ct;              // thrust coefficient
    public double CtOverSigma;     // blade loading - stall margin lives here
    public double Coning;          // a0, rad
    public double FlapBack;        // a1, rad: disc tilt aft (positive = tilted back)
    public double FlapSide;        // b1, rad: disc tilt right
    public double TipMachAdvancing;
    public double StalledFraction; // 0..1 of blade elements past stall
    public double PowerRequired;   // W
    /// <summary>Shaft power spent making lift, W: the lift vector tilted by the inflow.</summary>
    public double InducedPower;
    /// <summary>Shaft power spent dragging the blades round, W.</summary>
    public double ProfilePower;
}

/// <summary>
/// Individual-blade-element main rotor.
///
/// Each blade is carried as its own flapping oscillator and integrated around the
/// azimuth in substeps, with blade-element aerodynamics evaluated along the span.
/// Nothing about the helicopter feel is scripted on top of this: retreating blade
/// stall, blowback, cross-coupling, the pitch-up on entering translational lift, the
/// way a heavy machine settles in its own downwash - all of it is a consequence of
/// summing these elements honestly.
///
/// Cost is roughly (substeps x blades x stations) evaluations per physics step, around
/// 160 for the default configuration, which is nothing on any machine from the last
/// fifteen years.
/// </summary>
public sealed class MainRotor
{
    public RotorConfig Config { get; }
    public Inflow Inflow { get; }

    /// <summary>Flapping angle of each blade, radians, positive up.</summary>
    public double[] Beta { get; }
    /// <summary>Flapping rate of each blade, rad/s.</summary>
    public double[] BetaDot { get; }
    /// <summary>Azimuth of blade 0, radians, measured from the tail in the direction of rotation.</summary>
    public double Azimuth { get; private set; }

    private readonly Vec3 _xd, _yd, _zd;   // shaft (disc) axes expressed in body axes
    private double _lastThrust;
    private double _a1Filtered, _b1Filtered;

    public MainRotor(RotorConfig config)
    {
        Config = config;
        Inflow = new Inflow();
        Beta = new double[config.NumBlades];
        BetaDot = new double[config.NumBlades];

        // Shaft axes: start from body axes and tilt the mast. Small angles, so a pair
        // of successive rotations is exact enough and keeps the basis orthonormal.
        double tf = config.ShaftTiltForward;   // positive = mast leans forward
        double tr = config.ShaftTiltRight;
        // A mast leaning forward tilts the thrust vector forward, so the shaft DOWN
        // axis leans aft. Getting this backwards makes every helicopter in the game
        // want to fly tail first.
        Vec3 zd = new(-Math.Sin(tf), -Math.Sin(tr), Math.Cos(tf) * Math.Cos(tr));
        _zd = zd.Normalized;
        Vec3 xd = Vec3.Forward - _zd * Vec3.Dot(Vec3.Forward, _zd);
        _xd = xd.Normalized;
        _yd = Vec3.Cross(_zd, _xd).Normalized;
    }

    /// <summary>Disc-plane "down" axis in body coordinates (the direction thrust opposes).</summary>
    public Vec3 ShaftAxisDown => _zd;

    public void Reset()
    {
        Array.Clear(Beta);
        Array.Clear(BetaDot);
        Azimuth = 0;
        Inflow.Reset();
        _lastThrust = 0;
    }

    /// <summary>
    /// Pre-load the inflow and coning state for a given thrust, so a rotor that spawns
    /// already turning does not spend its first tenth of a second producing two and a
    /// half times hover thrust while the dynamic inflow catches up. Without this, every
    /// aircraft in the game would be launched skyward the instant it appeared.
    /// </summary>
    public void Seed(double thrust, double rho, double rotorOmega)
    {
        _lastThrust = thrust;
        double tipSpeed = Math.Max(rotorOmega * Config.Radius, 1e-3);
        double ct = thrust / Math.Max(rho * Config.DiscArea * tipSpeed * tipSpeed, 1e-9);
        double lambdaH = Math.Sqrt(Math.Max(ct, 0.0) * 0.5);
        Inflow.Reset(lambdaH * Inflow.Kappa);

        // Static coning: aerodynamic lift moment against centrifugal stiffening.
        double perBlade = thrust / Math.Max(Config.NumBlades, 1);
        double flapMoment = perBlade * Config.Radius * 0.6;
        double restoring = Config.BladeFlapInertia * rotorOmega * rotorOmega
                           * Config.FlapFrequencyRatio * Config.FlapFrequencyRatio;
        double beta0 = restoring > 1e-6 ? Math.Clamp(flapMoment / restoring, -0.1, 0.35) : 0;
        for (int i = 0; i < Beta.Length; i++) { Beta[i] = beta0; BetaDot[i] = 0; }
    }

    /// <summary>
    /// Advance the rotor one physics step and return the wrench it applies to the airframe.
    /// </summary>
    /// <param name="vAirBody">Velocity of the CG through the air, body FRD, m/s.</param>
    /// <param name="omegaBody">Angular velocity of the airframe, body FRD, rad/s.</param>
    /// <param name="rotorOmega">Rotor speed, rad/s (always positive).</param>
    /// <param name="collective">Collective pitch at 75% radius, radians.</param>
    /// <param name="cyclicForward">Commanded disc tilt forward, radians (stick forward positive).</param>
    /// <param name="cyclicRight">Commanded disc tilt right, radians (stick right positive).</param>
    /// <param name="rho">Air density, kg/m^3.</param>
    /// <param name="soundSpeed">Local speed of sound, m/s.</param>
    /// <param name="hubHeightAgl">Height of the hub above terrain, m.</param>
    /// <param name="dt">Physics timestep, s.</param>
    /// <param name="cg">Centre of gravity in the airframe datum frame, m. Component
    /// positions are stored against the datum, so moments are taken about this.</param>
    public RotorOutput Update(Vec3 vAirBody, Vec3 omegaBody, double rotorOmega,
                              double collective, double cyclicForward, double cyclicRight,
                              double rho, double soundSpeed, double hubHeightAgl, double dt,
                              Vec3 cg = default)
    {
        var cfg = Config;
        int nb = cfg.NumBlades;
        int ns = Math.Max(3, cfg.RadialStations);
        int sub = Math.Max(1, cfg.Substeps);
        double R = cfg.Radius;
        Vec3 hub = cfg.HubPosition - cg;
        double omega = Math.Max(rotorOmega, 0.0);
        double tipSpeed = omega * R;

        // --- Inflow ---------------------------------------------------------
        // Hub velocity through the air, including the contribution of body rotation.
        Vec3 vHub = vAirBody + Vec3.Cross(omegaBody, hub);
        double vPerp = Vec3.Dot(vHub, _zd);                    // positive = hub moving down
        Vec3 vInPlane = vHub - _zd * vPerp;
        double vInPlaneMag = vInPlane.Length;

        double muX = tipSpeed > 1.0 ? vInPlaneMag / tipSpeed : 0.0;
        // Freestream inflow through the disc, positive downward. Climbing (hub moving
        // up, vPerp negative) gives positive lambdaC, matching momentum theory.
        double lambdaC = tipSpeed > 1.0 ? -vPerp / tipSpeed : 0.0;

        Inflow.Update(_lastThrust, rho, cfg.DiscArea, Math.Max(tipSpeed, 1e-3),
                      muX, lambdaC, hubHeightAgl, R, dt);

        double vInduced = Inflow.Lambda0 * Math.Max(tipSpeed, 1e-3);

        // In-plane unit vector pointing along the oncoming flow, used to place the
        // Drees inflow gradient correctly relative to the direction of travel.
        Vec3 flowDir = vInPlaneMag > 1e-4 ? vInPlane / vInPlaneMag : _xd;
        double flowCos = Vec3.Dot(flowDir, _xd);
        double flowSin = Vec3.Dot(flowDir, _yd);

        // --- Integrate blades ------------------------------------------------
        double dtSub = dt / sub;
        double dr = R * (1.0 - cfg.RootCutout) / ns;
        double halfRhoC = 0.5 * rho * cfg.Chord;
        int s = cfg.SpinSign;

        // Prandtl-style tip loss: outboard of B*R the blade makes no lift.
        double ctGuess = Math.Abs(_lastThrust) / Math.Max(rho * cfg.DiscArea * tipSpeed * tipSpeed, 1e-6);
        double tipLossB = Math.Clamp(1.0 - Math.Sqrt(2.0 * Math.Max(ctGuess, 1e-6)) / nb, 0.90, 0.99);

        Vec3 forceAcc = Vec3.Zero;
        Vec3 momentAcc = Vec3.Zero;
        double torqueAcc = 0;
        double inducedTorqueAcc = 0, profileTorqueAcc = 0;
        double thrustAcc = 0;
        int stalled = 0, elementCount = 0;
        double maxTipMach = 0;
        double flapFreq2 = cfg.FlapFrequencyRatio * cfg.FlapFrequencyRatio;
        double Ib = cfg.BladeFlapInertia;
        double weightMoment = cfg.BladeMass * Atmosphere.Gravity * cfg.BladeCgRadius;

        for (int step = 0; step < sub; step++)
        {
            Azimuth += omega * dtSub;
            if (Azimuth > Math.Tau) Azimuth -= Math.Tau;

            for (int b = 0; b < nb; b++)
            {
                double psi = Azimuth + Math.Tau * b / nb;
                double cosPsi = Math.Cos(psi), sinPsi = Math.Sin(psi);

                // Blade radial and tangential unit vectors in body axes.
                Vec3 er = _xd * (-cosPsi) + _yd * (s * sinPsi);
                Vec3 et = _xd * sinPsi + _yd * (s * cosPsi);

                double beta = Beta[b], betaDot = BetaDot[b];
                double cosB = Math.Cos(beta), sinB = Math.Sin(beta);

                // Blade-normal direction (lift acts along this when the inflow angle
                // is zero): perpendicular to the flapped blade, tilted inboard by beta.
                Vec3 nrm = er * (-sinB) + _zd * (-cosB);

                // Cyclic pitch. A commanded disc tilt toward (fwd, right) needs peak
                // blade pitch 90 degrees of azimuth earlier, because the blade reaches
                // maximum flap a quarter turn after maximum lift. Deriving the phase
                // here rather than hard-coding a swashplate angle means the builder can
                // change rotation direction and the controls stay correct.
                double cyclic = -(cyclicForward * sinPsi + s * cyclicRight * cosPsi);

                double flapMoment = 0;

                for (int i = 0; i < ns; i++)
                {
                    double r = R * cfg.RootCutout + dr * (i + 0.5);
                    double rBar = r / R;

                    // Local blade pitch: collective is referenced to the 75% station.
                    double theta = collective + cfg.Twist * (rBar - 0.75) + cyclic - cfg.Delta3 * beta;

                    // Element position and velocity through the air.
                    Vec3 rElem = hub + (er * (r * cosB) + _zd * (-r * sinB));
                    Vec3 vElem = vAirBody + Vec3.Cross(omegaBody, rElem)
                                 + et * (omega * r * cosB)
                                 + _zd * (-r * betaDot);

                    double uT = Vec3.Dot(vElem, et);

                    // Local induced velocity with the Drees linear gradient: more inflow
                    // at the rear of the disc than the front in forward flight.
                    double along = -cosPsi * flowCos + s * sinPsi * flowSin; // cos of angle from flow
                    double across = sinPsi * flowCos + s * cosPsi * flowSin;
                    double viLocal = vInduced * (1.0 + Inflow.Kx * rBar * along + Inflow.Ky * rBar * across);

                    double uP = viLocal - Vec3.Dot(vElem, _zd);

                    double u2 = uT * uT + uP * uP;
                    if (u2 < 1e-6) continue;
                    double u = Math.Sqrt(u2);

                    double phi = Math.Atan2(uP, uT);
                    double alpha = theta - phi;
                    double mach = u / soundSpeed;
                    if (rBar > 0.9) maxTipMach = Math.Max(maxTipMach, mach);

                    cfg.Blade.Coefficients(alpha, mach, out double cl, out double cd);

                    // Tip loss: lift vanishes outboard of B*R, drag does not.
                    if (rBar > tipLossB) cl *= Math.Max(0.0, (1.0 - rBar) / Math.Max(1.0 - tipLossB, 1e-4));

                    double q = halfRhoC * u2 * dr;
                    double dL = q * cl;
                    double dD = q * cd;

                    double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
                    double dFn = dL * cosPhi - dD * sinPhi;        // along blade normal
                    double dFt = dL * sinPhi + dD * cosPhi;        // opposing blade motion

                    Vec3 dF = nrm * dFn - et * dFt;

                    forceAcc += dF * dtSub;
                    momentAcc += Vec3.Cross(rElem, dF) * dtSub;
                    torqueAcc += r * cosB * dFt * dtSub;

                    // The in-plane force splits exactly into the lift vector tilted by the
                    // inflow angle, and the section drag. That is precisely the induced /
                    // profile division, and having the two separately is the only way to
                    // tell an inefficient disc from a draggy blade - which is the open
                    // question in D-043.
                    inducedTorqueAcc += r * cosB * (dL * sinPhi) * dtSub;
                    profileTorqueAcc += r * cosB * (dD * cosPhi) * dtSub;
                    thrustAcc += (-Vec3.Dot(dF, _zd)) * dtSub;

                    flapMoment += r * dFn;

                    elementCount++;
                    if (Math.Abs(Airfoil.WrapPi(alpha)) > cfg.Blade.AlphaStall) stalled++;
                }

                // --- Flapping dynamics -------------------------------------
                // Centrifugal stiffening is what gives the rotor its 1/rev flapping
                // frequency; hinge offset raises it above 1/rev, which is exactly the
                // difference between a teetering Huey and an articulated hub that can
                // push the fuselage around with hub moments.
                double accel = (flapMoment - weightMoment * cosB) / Ib
                               - flapFreq2 * omega * omega * Math.Sin(beta)
                               - 0.02 * betaDot * omega;

                betaDot += accel * dtSub;
                beta += betaDot * dtSub;

                // Droop and flap stops.
                beta = Math.Clamp(beta, -0.12, 0.45);
                if (beta <= -0.12 || beta >= 0.45) betaDot *= -0.1;

                Beta[b] = beta;
                BetaDot[b] = betaDot;

                // Hub moment through the flapping hinge offset. Zero for a teetering
                // rotor, which is why those aircraft have to tilt the whole thrust
                // vector to manoeuvre and feel so much less crisp.
                if (cfg.HingeOffset > 1e-6)
                {
                    double kHub = cfg.HingeOffset * R * cfg.BladeMass * omega * omega * cfg.BladeCgRadius;
                    Vec3 axis = Vec3.Cross(_zd, er);
                    momentAcc += axis * (kHub * Math.Sin(beta) * dtSub);
                }
            }
        }

        double inv = 1.0 / dt;
        var outp = new RotorOutput
        {
            Force = forceAcc * inv,
            Moment = momentAcc * inv,
            ShaftTorque = torqueAcc * inv,
            Thrust = thrustAcc * inv,
        };

        // Vortex ring state: unsteady thrust and random upsets. The lift loss itself
        // already came out of the inflow model; this is the shake that tells the pilot.
        if (Inflow.VrsSeverity > 0.001)
        {
            double buffet = Inflow.BuffetFactor();
            outp.Force = outp.Force * buffet;
            outp.Thrust *= buffet;
            double m = Math.Abs(outp.Thrust) * R;
            outp.Moment += new Vec3(Inflow.BuffetMoment() * m, Inflow.BuffetMoment() * m, 0);
        }

        _lastThrust = outp.Thrust;

        // --- Harmonic analysis of the flapping, for instruments and telemetry ---
        // The multi-blade coordinate transform is only exact instantaneously for three
        // or more blades. A two-bladed rotor needs the result averaged over azimuth, so
        // the raw estimate goes through a low-pass with a time constant of about one
        // revolution. Without this a teetering rotor reports a disc tilt that swings
        // between zero and twice the truth every revolution.
        double a0 = 0, a1 = 0, b1 = 0;
        for (int b = 0; b < nb; b++)
        {
            double psi = Azimuth + Math.Tau * b / nb;
            a0 += Beta[b];
            a1 += -Beta[b] * Math.Cos(psi) * 2.0 / nb;
            b1 += Beta[b] * Math.Sin(psi) * 2.0 / nb;
        }
        a0 /= nb;
        if (nb < 3 && omega > 0.1)
        {
            double tau = Math.Tau / omega;
            double k = 1.0 - Math.Exp(-dt / tau);
            _a1Filtered += (a1 - _a1Filtered) * k;
            _b1Filtered += (b1 - _b1Filtered) * k;
            a1 = _a1Filtered * 2.0;   // the sampled estimate averages to half the amplitude
            b1 = _b1Filtered * 2.0;
        }
        outp.Coning = a0;
        outp.FlapBack = a1;
        outp.FlapSide = s * b1;

        double ts2 = Math.Max(tipSpeed * tipSpeed, 1e-6);
        outp.Ct = outp.Thrust / (rho * cfg.DiscArea * ts2);
        outp.CtOverSigma = outp.Ct / Math.Max(cfg.Solidity, 1e-6);
        outp.TipMachAdvancing = maxTipMach;
        outp.StalledFraction = elementCount > 0 ? (double)stalled / elementCount : 0;
        outp.PowerRequired = outp.ShaftTorque * omega;
        outp.InducedPower = inducedTorqueAcc * inv * omega;
        outp.ProfilePower = profileTorqueAcc * inv * omega;

        return outp;
    }
}
