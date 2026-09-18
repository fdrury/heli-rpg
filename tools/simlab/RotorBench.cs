using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Open-loop rotor bench: spin the rotor at a fixed speed in still air and sweep
/// collective. No autopilot, no rigid body, no feedback - just the blade element
/// model answering "how much thrust and how much torque".
/// </summary>
public static class RotorBench
{
    const double Deg = 180.0 / Math.PI;

    public static string? Sweep()
    {
        var af = Airframe.Workhorse();
        var cfg = af.MainRotor;
        var rotor = new MainRotor(cfg);
        double rho = 1.225, a = 340.3;
        double dt = 1.0 / 240.0;
        double weight = 3880 * Atmosphere.Gravity;

        Console.WriteLine($"  disc area {cfg.DiscArea:F1} m2, solidity {cfg.Solidity:F4}, " +
                          $"tip speed {cfg.NominalOmega * cfg.Radius:F0} m/s");
        Console.WriteLine($"  target thrust for hover: {weight:F0} N");
        Console.WriteLine();
        Console.WriteLine("   coll deg    thrust N   torque Nm    power kW    Ct/sigma   coning deg   lambda_i   vi m/s   FM");

        double hoverColl = double.NaN;
        for (double collDeg = 0; collDeg <= 18; collDeg += 1.5)
        {
            rotor.Reset();
            double coll = collDeg / Deg;
            RotorOutput o = default;
            // Let the flapping and inflow settle before reading anything.
            for (int i = 0; i < 1200; i++)
                o = rotor.Update(Vec3.Zero, Vec3.Zero, cfg.NominalOmega, coll, 0, 0, rho, a, 1e5, dt);

            double vi = rotor.Inflow.Lambda0 * cfg.NominalOmega * cfg.Radius;
            double idealPower = o.Thrust > 0 ? o.Thrust * Math.Sqrt(o.Thrust / (2 * rho * cfg.DiscArea)) : 0;
            double fm = o.PowerRequired > 1 ? idealPower / o.PowerRequired : 0;
            if (double.IsNaN(hoverColl) && o.Thrust >= weight) hoverColl = collDeg;

            Console.WriteLine($"  {collDeg,9:F1} {o.Thrust,11:F0} {o.ShaftTorque,11:F0} {o.PowerRequired / 1000,11:F0} " +
                              $"{o.CtOverSigma,11:F4} {o.Coning * Deg,12:F2} {rotor.Inflow.Lambda0,10:F4} {vi,8:F1} {fm,6:F2}");
        }

        Console.WriteLine();
        Console.WriteLine($"  collective needed for hover thrust: {hoverColl:F1} deg (real machines: 8-12 deg)");

        if (double.IsNaN(hoverColl)) return "the rotor cannot produce hover thrust at any collective";
        if (hoverColl > 14) return $"needs {hoverColl:F1} deg collective to hover - the rotor is far too weak";
        if (hoverColl < 5) return $"hovers at {hoverColl:F1} deg collective - the rotor is far too strong";
        return null;
    }

    /// <summary>Spin-up: hold collective and see whether the engine can accelerate the rotor.</summary>
    public static string? SpinUp()
    {
        var heli = new Helicopter(Airframe.Workhorse()) { Fuel = 500 };
        heli.InvalidateMass();
        heli.PlaceInFlight(100);
        heli.Input = new Controls { Collective = 0.55, Throttle = 1.0 };
        double dt = 1.0 / 240.0;

        Console.WriteLine("    t s     Nr %   torq%   power kW   thrust N   vert   roll   pitch   yawrate   u     v     w");
        for (double t = 0; t < 12; t += dt)
        {
            heli.Step(dt);
            if (Math.Abs(t % 0.5) < dt)
            {
                var tel = heli.Telemetry;
                var vb = heli.State.Orientation.InverseRotate(heli.State.Velocity);
                Console.WriteLine($"  {t,5:F2} {tel.RotorRpmPercent,8:F1} {tel.TorquePercent,7:F0} " +
                                  $"{tel.PowerRequired / 1000,10:F0} {tel.Thrust,10:F0} {-heli.State.Velocity.Z,6:F1} " +
                                  $"{heli.State.Orientation.Roll * Deg,6:F1} {heli.State.Orientation.Pitch * Deg,7:F1} " +
                                  $"{heli.State.AngularVelocity.Z * Deg,9:F1} {vb.X,5:F1} {vb.Y,5:F1} {vb.Z,5:F1}");
            }
        }
        double nr = heli.Telemetry.RotorRpmPercent;
        Console.WriteLine($"  final Nr {nr:F1} %");
        return Math.Abs(nr - 100) < 6 ? null : $"governor could not hold Nr with a fixed collective ({nr:F1} %)";
    }
}

public static class CyclicBench
{
    const double Deg = 180.0 / Math.PI;

    /// <summary>
    /// Step the cyclic on an isolated rotor and watch the tip path plane respond.
    /// A freely flapping rotor should tilt its disc by roughly the commanded amount
    /// within a revolution or two, and should then carry almost no hub moment.
    /// </summary>
    public static string? Step()
    {
        var cfg = Airframe.Workhorse().MainRotor;
        var rotor = new MainRotor(cfg);
        double rho = 1.225, a = 340.3, dt = 1.0 / 240.0;
        double coll = 8.0 / Deg;
        double omega = cfg.NominalOmega;
        double rev = Math.Tau / omega;

        rotor.Seed(37000, rho, omega);
        for (int i = 0; i < 2400; i++) rotor.Update(Vec3.Zero, Vec3.Zero, omega, coll, 0, 0, rho, a, 1e5, dt);

        Console.WriteLine($"  one revolution = {rev * 1000:F0} ms = {rev / dt:F1} physics steps");
        Console.WriteLine($"  settled hover: coning {rotor.Beta[0] * Deg:F2} deg, blades " +
                          string.Join(", ", rotor.Beta.Select(b => (b * Deg).ToString("F2"))));
        Console.WriteLine();
        Console.WriteLine("  applying 5 deg of forward cyclic");
        Console.WriteLine("    revs      a0      a1      b1    thrust      My       Mx    beta[0] beta[1]");

        double cyc = 5.0 / Deg;
        for (int i = 0; i <= 2400; i++)
        {
            var o = rotor.Update(Vec3.Zero, Vec3.Zero, omega, coll, cyc, 0, rho, a, 1e5, dt);
            if (i % 40 == 0 && i <= 1200)
                Console.WriteLine($"  {i * dt / rev,7:F2} {o.Coning * Deg,7:F2} {o.FlapBack * Deg,7:F2} " +
                                  $"{o.FlapSide * Deg,7:F2} {o.Thrust,9:F0} {o.Moment.Y,9:F0} {o.Moment.X,8:F0} " +
                                  $"{rotor.Beta[0] * Deg,8:F2} {rotor.Beta[Math.Min(1, rotor.Beta.Length - 1)] * Deg,7:F2}");
        }

        var final = rotor.Update(Vec3.Zero, Vec3.Zero, omega, coll, cyc, 0, rho, a, 1e5, dt);
        Console.WriteLine();
        Console.WriteLine($"  after 10 revolutions: a1 {final.FlapBack * Deg:F2} deg for 5 deg of cyclic, " +
                          $"hub moment My {final.Moment.Y:F0} N.m");

        double tilt = Math.Abs(final.FlapBack * Deg);
        if (tilt < 1.5) return $"disc only tilted {tilt:F2} deg for 5 deg of cyclic - the rotor is not flapping";
        return null;
    }
}
