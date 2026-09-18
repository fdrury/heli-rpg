namespace Rotorwash.Sim;

public sealed class TailRotorConfig
{
    public int NumBlades { get; init; } = 4;
    public double Radius { get; init; } = 1.68;
    public double Chord { get; init; } = 0.25;
    public double RootCutout { get; init; } = 0.2;
    public double Twist { get; init; } = -0.10;

    /// <summary>Gear ratio relative to the main rotor.</summary>
    public double GearRatio { get; init; } = 4.6;

    /// <summary>Position relative to the CG, body FRD, m. Negative X is aft.</summary>
    public Vec3 Position { get; init; } = new(-9.9, 0.0, -1.5);

    /// <summary>Which way its thrust points at positive pitch. +1 = to the right.</summary>
    public int ThrustSign { get; init; } = 1;

    public double PitchMin { get; init; } = -8.0 * Math.PI / 180.0;
    public double PitchMax { get; init; } = 22.0 * Math.PI / 180.0;

    /// <summary>Thrust lost to the fin sitting in the tail rotor wake. ~0.9 typical.</summary>
    public double FinBlockage { get; init; } = 0.92;

    public Airfoil Blade { get; init; } = new() { Cd0 = 0.011 };

    public int RadialStations { get; init; } = 6;

    public double DiscArea => Math.PI * Radius * Radius;
    public double Solidity => NumBlades * Chord * Radius * (1 - RootCutout) / DiscArea;
}

public struct TailRotorOutput
{
    public Vec3 Force;
    public Vec3 Moment;
    public double ShaftTorque;   // referred to the tail rotor shaft, N.m
    public double Thrust;
    public double PitchUsed;     // rad - how much authority is left matters in a crosswind
    public bool Saturated;
}

/// <summary>
/// Tail rotor. Blade element like the main rotor but without flapping dynamics, which
/// is a fair simplification: what matters for gameplay is that it runs out of authority
/// in a left crosswind, that it costs main-rotor power, that losing it is survivable
/// only by flying forward fast enough for the fin to take over, and that it stops
/// working when the drivetrain does.
/// </summary>
public sealed class TailRotor
{
    public TailRotorConfig Config { get; }
    public Inflow Inflow { get; }

    private double _lastThrust;

    public TailRotor(TailRotorConfig config)
    {
        Config = config;
        Inflow = new Inflow { TimeConstant = 0.06, Kappa = 1.25, VrsBuffet = 0.0 };
    }

    public void Reset() { Inflow.Reset(); _lastThrust = 0; }

    /// <summary>Pre-load the inflow so a tail rotor that spawns turning does not spike.</summary>
    public void Seed(double thrust, double rho, double tailOmega)
    {
        _lastThrust = thrust;
        double tipSpeed = Math.Max(tailOmega * Config.Radius, 1e-3);
        double ct = thrust / Math.Max(rho * Config.DiscArea * tipSpeed * tipSpeed, 1e-9);
        Inflow.Reset(Math.Sqrt(Math.Max(ct, 0.0) * 0.5) * Inflow.Kappa);
    }

    public TailRotorOutput Update(Vec3 vAirBody, Vec3 omegaBody, double mainRotorOmega,
                                  double pitch, double rho, double soundSpeed, double dt,
                                  Vec3 cg = default)
    {
        var cfg = Config;
        Vec3 pos = cfg.Position - cg;
        double omega = Math.Max(mainRotorOmega * cfg.GearRatio, 0.0);
        double R = cfg.Radius;
        double tipSpeed = omega * R;

        pitch = Math.Clamp(pitch, cfg.PitchMin, cfg.PitchMax);

        // The tail rotor thrust axis. Its "down" (thrust-opposing) axis is lateral.
        Vec3 axis = new(0, -cfg.ThrustSign, 0);   // positive thrust is along -axis

        Vec3 vHub = vAirBody + Vec3.Cross(omegaBody, pos);
        double vPerp = Vec3.Dot(vHub, axis);
        Vec3 vInPlane = vHub - axis * vPerp;
        double muX = tipSpeed > 1.0 ? vInPlane.Length / tipSpeed : 0.0;
        double lambdaC = tipSpeed > 1.0 ? -vPerp / tipSpeed : 0.0;

        Inflow.Update(_lastThrust, rho, cfg.DiscArea, Math.Max(tipSpeed, 1e-3),
                      muX, lambdaC, 1e6, R, dt);
        double vInduced = Inflow.Lambda0 * Math.Max(tipSpeed, 1e-3);

        int ns = cfg.RadialStations;
        double dr = R * (1 - cfg.RootCutout) / ns;
        double halfRhoC = 0.5 * rho * cfg.Chord;

        double thrust = 0, torque = 0;

        // Azimuthally averaged: sample a few azimuths so a crosswind still shows up.
        const int nPsi = 4;
        for (int j = 0; j < nPsi; j++)
        {
            double psi = Math.Tau * j / nPsi;
            for (int i = 0; i < ns; i++)
            {
                double r = R * cfg.RootCutout + dr * (i + 0.5);
                double rBar = r / R;
                double theta = pitch + cfg.Twist * (rBar - 0.75);

                double uT = omega * r + vInPlane.Length * Math.Sin(psi);
                double uP = vInduced - vPerp;

                double u2 = uT * uT + uP * uP;
                if (u2 < 1e-6) continue;
                double u = Math.Sqrt(u2);
                double phi = Math.Atan2(uP, uT);
                double alpha = theta - phi;

                cfg.Blade.Coefficients(alpha, u / soundSpeed, out double cl, out double cd);

                double q = halfRhoC * u2 * dr;
                double dL = q * cl, dD = q * cd;
                double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);

                thrust += (dL * cosPhi - dD * sinPhi) * cfg.NumBlades / nPsi;
                torque += r * (dL * sinPhi + dD * cosPhi) * cfg.NumBlades / nPsi;
            }
        }

        thrust *= cfg.FinBlockage;
        _lastThrust = thrust;

        Vec3 force = axis * -thrust;   // thrust acts opposite the inflow axis

        return new TailRotorOutput
        {
            Force = force,
            Moment = Vec3.Cross(pos, force),
            ShaftTorque = torque,
            Thrust = thrust,
            PitchUsed = pitch,
            Saturated = pitch >= cfg.PitchMax - 1e-4 || pitch <= cfg.PitchMin + 1e-4,
        };
    }
}
