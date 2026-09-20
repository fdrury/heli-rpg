using System;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The flight envelope, measured and compared against the real aircraft.
///
/// The Workhorse is a UH-1H in all but name, and deliberately so: rotor radius 7.32 m,
/// 324 rpm, two blades, 1.30 m tail rotor, a 1400 shp turboshaft. Those are the Huey's
/// actual numbers, which means its published performance is a yardstick this model can be
/// held against rather than merely tuned by feel.
///
/// The existing scenarios check that individual mechanisms behave - that the power curve
/// has a bucket, that the rotor does not decay in autorotation. This asks a different and
/// harder question: does the whole aircraft, flown properly, go as fast, climb as quickly,
/// and glide as far as the machine it is modelled on? That is the question "physics close
/// to right" actually means, and nothing was asking it.
///
/// Reference figures are for a UH-1H at a normal operating weight, from the published
/// performance data. Tolerances are wide on purpose - this is a game, the airframe is not
/// an exact copy, and the point is to catch a model that is wrong by a factor, not one that
/// is wrong by five per cent.
/// </summary>
public static class EnvelopeTests
{
    private const double Kt = 1.94384;       // m/s -> kt
    private const double Fpm = 196.85;       // m/s -> ft/min

    private static Helicopter Fresh(double fuel = 500)
    {
        var h = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = fuel };
        h.InvalidateMass();
        return h;
    }

    /// <summary>Trim at a speed and read what the aircraft needs to hold it.</summary>
    private static (double powerReq, double powerAvail, double fuelFlow, bool ok)
        AtSpeed(double altitude, double kt)
    {
        Helicopter h = Fresh();
        h.PlaceInFlight(altitude);
        h.UseInternalGroundModel = false;
        TrimResult t = Trim.Solve(h, altitude, kt / Kt);
        if (!t.Converged) return (0, 0, 0, false);

        // Hold the aircraft PINNED at the trim condition while the engine and rotor settle.
        //
        // Letting it fly free for a second instead measures the departure, not the
        // condition: the airframe is unstable, it starts to go within a few tenths of a
        // second, and the governor chases it. That produced a fuel flow that jumped from
        // 202 kg/h to 304 between 110 and 120 kt - a discontinuity in what the source says
        // is a constant times power, which is the giveaway that the reading was wrong
        // rather than the model.
        var pinnedOrientation = Quat.FromEuler(t.RollRad, t.PitchRad, 0);
        var pinnedVelocity = new Vec3(kt / Kt, 0, 0);
        for (int i = 0; i < 360; i++)
        {
            h.State.Position = new Vec3(0, 0, -altitude);
            h.State.Orientation = pinnedOrientation;
            h.State.Velocity = pinnedVelocity;
            h.State.AngularVelocity = Vec3.Zero;
            h.ForceActuators(t.Controls);
            h.Step(1.0 / 240.0);
        }

        FlightTelemetry tel = h.Telemetry;
        Console.WriteLine($"          (at {kt,3:F0} kt level: induced {tel.InducedPower / 1000,6:F0}" +
                          $" profile {tel.ProfilePower / 1000,6:F0} kW," +
                          $" parasite {tel.ParasitePower / 1000,5:F0} kW)");
        return (tel.PowerRequired, tel.PowerAvailable, tel.FuelFlow, true);
    }

    public static string? Performance()
    {
        Console.WriteLine("  level flight at sea level, trimmed at each speed");
        Console.WriteLine("     kt    power req   power avail   fuel    spare    est ROC");

        string? failure = null;
        double bestRoc = 0, bestRocKt = 0;
        double minPower = double.MaxValue, minPowerKt = 0;
        double maxLevelKt = 0;
        double cruiseFlow = 0;

        Helicopter probe = Fresh();
        probe.InvalidateMass();
        double weight = probe.TotalMass * Atmosphere.Gravity;

        for (double kt = 0; kt <= 140; kt += 10)
        {
            var (req, avail, flow, ok) = AtSpeed(200, kt);
            if (!ok)
            {
                Console.WriteLine($"  {kt,5:F0}    (no trim solution)");
                continue;
            }

            double spare = avail - req;
            // Excess power buys climb. 0.80 covers the rotor's efficiency in climb and is
            // the usual rule of thumb for this sort of estimate.
            double roc = spare > 0 ? spare * 0.80 / weight : 0;

            Console.WriteLine($"  {kt,5:F0}   {req / 1000,7:F0} kW   {avail / 1000,7:F0} kW  " +
                              $"{flow * 3600,5:F0} kg/h  {spare / 1000,6:F0} kW  {roc * Fpm,6:F0} fpm");

            if (req < minPower) { minPower = req; minPowerKt = kt; }
            if (roc > bestRoc) { bestRoc = roc; bestRocKt = kt; }
            if (spare > 0) maxLevelKt = kt;
            if (Math.Abs(kt - 90) < 1) cruiseFlow = flow;
        }

        Console.WriteLine();
        Console.WriteLine($"  minimum power   {minPower / 1000:F0} kW at {minPowerKt:F0} kt");
        Console.WriteLine($"  best climb      {bestRoc * Fpm:F0} fpm at {bestRocKt:F0} kt");
        Console.WriteLine($"  max level speed at least {maxLevelKt:F0} kt");
        if (cruiseFlow > 0)
        {
            double endurance = 500.0 / (cruiseFlow * 3600.0);
            Console.WriteLine($"  at 90 kt: {cruiseFlow * 3600:F0} kg/h, " +
                              $"{endurance:F1} h on 500 kg, {endurance * 90 / Kt * 3.6:F0} km still-air range");
        }

        Console.WriteLine();
        Console.WriteLine("  reference, UH-1H: cruise 100-110 kt, Vne 124 kt, best climb about");
        Console.WriteLine("  1600 fpm, minimum power near 60-70 kt, roughly 300-360 kg/h in cruise");

        // Wide bands. The point is to catch a model that is wrong by a factor.
        if (minPowerKt is < 40 or > 90)
            failure ??= $"minimum-power speed is {minPowerKt:F0} kt; a Huey's is 60-70";
        if (maxLevelKt < 90)
            failure ??= $"cannot hold level flight past {maxLevelKt:F0} kt; a Huey cruises at 100+";
        if (bestRoc * Fpm < 900)
            failure ??= $"best climb only {bestRoc * Fpm:F0} fpm; a Huey manages about 1600";
        if (bestRoc * Fpm > 3500)
            failure ??= $"best climb {bestRoc * Fpm:F0} fpm is far beyond anything this airframe could do";
        if (cruiseFlow > 0 && (cruiseFlow * 3600 is < 150 or > 600))
            failure ??= $"cruise fuel flow {cruiseFlow * 3600:F0} kg/h is not a turboshaft this size";

        return failure;
    }

    /// <summary>
    /// What the section's lift-induced drag term costs, and what it buys.
    ///
    /// Cd = Cd0 + K·Cl². At K = 0.0216 and a working Cl, that term adds about as much drag
    /// as Cd0 itself - it roughly DOUBLES section drag. In blade-element theory the induced
    /// drag of the rotor is already carried by the lift vector tilting with the inflow
    /// (dL·sinφ), so a large K here is at risk of charging for the same thing twice.
    ///
    /// Sweeping it against the things that must not break: hover power, cruise power, and
    /// the autorotation equilibrium.
    /// </summary>
    public static string? DragKSweep()
    {
        Console.WriteLine("  section lift-induced drag: what it costs and what it buys");
        Console.WriteLine("      K      hover kW   60 kt kW    Nr=100% at");

        foreach (double k in new[] { 0.0216, 0.016, 0.011, 0.006 })
        {
            double hover = LevelPowerK(k, 0.0);
            double cruise = LevelPowerK(k, 60.0);

            double lo = 4.0, hi = 30.0;
            for (int i = 0; i < 12; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (NrAtDescentK(k, 60.0, mid) < 100.0) lo = mid; else hi = mid;
            }
            double eq = 0.5 * (lo + hi);

            Console.WriteLine($"   {k,6:F4}   {hover,7:F0}    {cruise,7:F0}     {eq * Fpm,6:F0} fpm");
        }

        Console.WriteLine();
        Console.WriteLine("  reference: hover about 800 kW, 60 kt about 440 kW, autorotation 1700 fpm");
        return null;
    }

    private static Airframe WithDragK(double k)
    {
        Airframe af = Airframe.Workhorse();
        af.MainRotor.Blade.DragK = k;
        return af;
    }

    private static double LevelPowerK(double k, double kt)
    {
        var h = new Helicopter(WithDragK(k), new FlatEnvironment()) { Fuel = 500 };
        h.InvalidateMass();
        h.PlaceInFlight(200);
        h.UseInternalGroundModel = false;
        TrimResult t = Trim.Solve(h, 200, kt / Kt);
        if (!t.Converged) return double.NaN;
        var att = Quat.FromEuler(t.RollRad, t.PitchRad, 0);
        var vel = new Vec3(kt / Kt, 0, 0);
        for (int i = 0; i < 360; i++)
        {
            h.State.Position = new Vec3(0, 0, -200);
            h.State.Orientation = att;
            h.State.Velocity = vel;
            h.State.AngularVelocity = Vec3.Zero;
            h.ForceActuators(t.Controls);
            h.Step(1.0 / 240.0);
        }
        return h.Telemetry.PowerRequired / 1000.0;
    }

    private static double NrAtDescentK(double k, double kt, double w)
    {
        var h = new Helicopter(WithDragK(k), new FlatEnvironment()) { Fuel = 500 };
        h.InvalidateMass();
        h.PlaceInFlightTrimmed(1500, kt / Kt);
        h.UseInternalGroundModel = false;
        h.Sas.Enabled = false;
        h.Engine.Fail();
        var c = new Controls { Collective = 0.0, Throttle = 0.0 };
        var vel = new Vec3(kt / Kt, 0, w);
        var att = Quat.FromEuler(0, 0, 0);
        double nr = 0; int n = 0;
        for (int i = 0; i < 2400; i++)
        {
            h.State.Position = new Vec3(0, 0, -1500);
            h.State.Orientation = att;
            h.State.Velocity = vel;
            h.State.AngularVelocity = Vec3.Zero;
            h.ForceActuators(c);
            h.Step(1.0 / 240.0);
            if (i > 1900) { nr += h.Telemetry.RotorRpmPercent; n++; }
        }
        return nr / Math.Max(n, 1);
    }

    /// <summary>
    /// Where along the blade the autorotative drive comes from.
    ///
    /// Negative bands drive the rotor, positive bands drag it. A healthy autorotation has a
    /// clear inboard driving region carrying a clear outboard dragging one; if the driving
    /// region is thin or weak, the rotor can only be sustained by descending harder, which
    /// is exactly the symptom in D-045.
    /// </summary>
    public static string? DrivingRegion()
    {
        Console.WriteLine("  shaft torque by tenth of radius (negative drives, positive drags)");
        Console.WriteLine("    condition            r/R:  .05  .15  .25  .35  .45  .55  .65  .75  .85  .95   net");

        // Rotor speed PINNED at 100% throughout.
        //
        // Letting it float confounds the comparison: at 1772 fpm the rotor had already
        // decayed to 67%, so every torque in that row was measured at half the dynamic
        // pressure of the others and the rows were not comparable. Holding Nr fixed asks
        // the clean question - at the speed the rotor is supposed to turn, what descent
        // rate makes net torque zero?
        Row("level 60 kt (powered)", 60.0, 0.0, powered: true);
        foreach (double w in new[] { 6.0, 9.0, 12.0, 15.0, 19.0 })
            Row($"auto 60 kt, {w * Fpm:F0} fpm", 60.0, w, powered: false);

        void Row(string label, double kt, double w, bool powered)
        {
            Helicopter h = Fresh();
            h.PlaceInFlightTrimmed(1500, kt / Kt);
            h.UseInternalGroundModel = false;
            h.Sas.Enabled = false;

            Controls c;
            if (powered)
            {
                TrimResult t = Trim.Solve(h, 1500, kt / Kt);
                c = t.Controls;
            }
            else
            {
                h.Engine.Fail();
                c = new Controls { Collective = 0.0, Throttle = 0.0 };
            }

            var vel = new Vec3(kt / Kt, 0, w);
            var att = Quat.FromEuler(0, 0, 0);
            var acc = new double[MainRotor.RadialBins];
            int n = 0;
            for (int i = 0; i < 2400; i++)
            {
                h.State.Position = new Vec3(0, 0, -1500);
                h.State.Orientation = att;
                h.State.Velocity = vel;
                h.State.AngularVelocity = Vec3.Zero;
                h.RotorOmega = h.Airframe.MainRotor.NominalOmega;   // pinned at 100%
                h.ForceActuators(c);
                h.Step(1.0 / 240.0);
                if (i > 1900)
                {
                    for (int k = 0; k < MainRotor.RadialBins; k++) acc[k] += h.Rotor.RadialTorque[k];
                    n++;
                }
            }

            var sb = new System.Text.StringBuilder($"    {label,-22}      ");
            double net = 0;
            for (int k = 0; k < MainRotor.RadialBins; k++)
            {
                double v = acc[k] / Math.Max(n, 1) / 1000.0;      // kN·m
                net += v;
                sb.Append($"{v,5:F1}");
            }
            sb.Append($"  {net,6:F1} kN·m  tipM {h.Telemetry.TipMach:F2}" +
                      $"  stall {h.Telemetry.StalledFraction:P0}");
            Console.WriteLine(sb.ToString());
        }

        return null;
    }

    /// <summary>
    /// What a radial inflow gradient buys, and what it costs.
    ///
    /// For each setting: the descent rate at which the rotor can hold 100% Nr with the
    /// collective down - which IS the autorotation equilibrium - against hover and cruise
    /// power, which is what the change risks breaking.
    /// </summary>
    public static string? RadialInflowSweep()
    {
        Console.WriteLine("  radial inflow gradient: autorotation against powered flight");
        Console.WriteLine("    k      Nr=100% at     hover kW   60 kt kW");

        foreach (double k in new[] { 0.0, 0.3, 0.6, 0.9 })
        {
            // Descent rate that sustains 100% Nr, found by bisection on the pinned rig.
            double lo = 4.0, hi = 30.0;
            for (int iter = 0; iter < 12; iter++)
            {
                double mid = 0.5 * (lo + hi);
                if (NrAtDescent(k, 60.0, mid) < 100.0) lo = mid; else hi = mid;
            }
            double equilibrium = 0.5 * (lo + hi);

            double hoverKw = LevelPower(k, 0.0);
            double cruiseKw = LevelPower(k, 60.0);

            Console.WriteLine($"  {k,4:F1}   {equilibrium * Fpm,6:F0} fpm      " +
                              $"{hoverKw,7:F0}    {cruiseKw,7:F0}");
        }

        Console.WriteLine();
        Console.WriteLine("  target: 100% Nr at about 1700 fpm, hover and cruise power unchanged");
        return null;
    }

    private static double NrAtDescent(double k, double kt, double w)
    {
        Airframe af = Airframe.Workhorse();
        af.MainRotor.RadialInflow = k;
        var h = new Helicopter(af, new FlatEnvironment()) { Fuel = 500 };
        h.InvalidateMass();
        h.PlaceInFlightTrimmed(1500, kt / Kt);
        h.UseInternalGroundModel = false;
        h.Sas.Enabled = false;
        h.Engine.Fail();

        var c = new Controls { Collective = 0.0, Throttle = 0.0 };
        var vel = new Vec3(kt / Kt, 0, w);
        var att = Quat.FromEuler(0, 0, 0);
        double nr = 0;
        int n = 0;
        for (int i = 0; i < 2400; i++)
        {
            h.State.Position = new Vec3(0, 0, -1500);
            h.State.Orientation = att;
            h.State.Velocity = vel;
            h.State.AngularVelocity = Vec3.Zero;
            h.ForceActuators(c);
            h.Step(1.0 / 240.0);
            if (i > 1900) { nr += h.Telemetry.RotorRpmPercent; n++; }
        }
        return nr / Math.Max(n, 1);
    }

    private static double LevelPower(double k, double kt)
    {
        Airframe af = Airframe.Workhorse();
        af.MainRotor.RadialInflow = k;
        var h = new Helicopter(af, new FlatEnvironment()) { Fuel = 500 };
        h.InvalidateMass();
        h.PlaceInFlight(200);
        h.UseInternalGroundModel = false;
        TrimResult t = Trim.Solve(h, 200, kt / Kt);
        if (!t.Converged) return double.NaN;

        var att = Quat.FromEuler(t.RollRad, t.PitchRad, 0);
        var vel = new Vec3(kt / Kt, 0, 0);
        for (int i = 0; i < 360; i++)
        {
            h.State.Position = new Vec3(0, 0, -200);
            h.State.Orientation = att;
            h.State.Velocity = vel;
            h.State.AngularVelocity = Vec3.Zero;
            h.ForceActuators(t.Controls);
            h.Step(1.0 / 240.0);
        }
        return h.Telemetry.PowerRequired / 1000.0;
    }

    /// <summary>
    /// Net shaft torque against descent rate, at a pinned condition.
    ///
    /// This is the experiment that actually settles where the autorotation equilibrium
    /// comes from. In a steady engine-off descent the net shaft torque is zero by
    /// definition - the driving region of the disc exactly balances the dragging region -
    /// so sweeping descent rate and finding the zero crossing gives the equilibrium
    /// directly, without waiting for a controller to hunt its way there. Printing the power
    /// split alongside it shows WHICH term moves the crossing.
    ///
    /// Pinned rather than free, for the reason D-042 keeps having to be relearned.
    /// </summary>
    public static string? AutorotationBalance()
    {
        const double kt = 60.0;
        const double alt = 1500;
        Console.WriteLine($"  net shaft torque against descent rate, pinned at {kt:F0} kt");
        Console.WriteLine("    ROD      shaft      induced   profile   parasite    Nr    stalled");

        foreach (double w in new[] { 6.0, 9.0, 12.0, 15.0, 19.0, 23.0 })
        {
            Helicopter h = Fresh();
            h.PlaceInFlightTrimmed(alt, kt / Kt);
            h.UseInternalGroundModel = false;
            h.Sas.Enabled = false;
            h.Engine.Fail();

            // Collective full down, which is where an autorotation is flown from.
            var c = new Controls { Collective = 0.0, Throttle = 0.0 };
            var vel = new Vec3(kt / Kt, 0, w);          // NED: +Z is down
            var att = Quat.FromEuler(0, 0, 0);

            double shaft = 0, ind = 0, prof = 0, para = 0, nr = 0, stall = 0;
            int n = 0;
            const double dt = 1.0 / 240.0;
            for (int i = 0; i < 2400; i++)              // 10 s
            {
                h.State.Position = new Vec3(0, 0, -alt);
                h.State.Orientation = att;
                h.State.Velocity = vel;
                h.State.AngularVelocity = Vec3.Zero;
                h.ForceActuators(c);
                h.Step(dt);

                if (i > 1900)
                {
                    shaft += h.Telemetry.MainRotorPower;
                    ind += h.Telemetry.InducedPower;
                    prof += h.Telemetry.ProfilePower;
                    para += h.Telemetry.ParasitePower;
                    nr += h.Telemetry.RotorRpmPercent;
                    stall += h.Telemetry.StalledFraction;
                    n++;
                }
            }

            Console.WriteLine($"  {w * Fpm,5:F0} fpm {shaft / n / 1000,8:F0} kW " +
                              $"{ind / n / 1000,9:F0} {prof / n / 1000,9:F0} {para / n / 1000,9:F0} kW" +
                              $"  {nr / n,5:F0}%  {stall / n,5:P0}");
        }

        Console.WriteLine();
        Console.WriteLine("  The equilibrium is where shaft power crosses zero with Nr at 100%.");
        return null;
    }

    /// <summary>
    /// How far it glides with the engine gone.
    ///
    /// The number that matters to a player is the glide ratio, because it decides how much
    /// of the map is reachable from any given height when the engine quits - which in a
    /// game about fuel and wear is a survival question, not a trivia one.
    /// </summary>
    public static string? Autorotation()
    {
        Console.WriteLine("  engine-off descent, trimmed and settled at each speed");
        Console.WriteLine("     kt     ROD        glide     Nr");

        string? failure = null;
        double bestRatio = 0, bestKt = 0, bestRod = 0, minRod = double.MaxValue;

        foreach (double kt in new[] { 40.0, 50.0, 60.0, 70.0, 80.0, 90.0 })
        {
            Helicopter h = Fresh();
            h.PlaceInFlightTrimmed(1500, kt / Kt);
            h.UseInternalGroundModel = false;
            h.Sas.Enabled = false;
            h.Engine.Fail();

            // Fly it the way an autorotation is actually flown: hold the airspeed, and
            // manage the COLLECTIVE to keep the rotor at about 100%.
            //
            // Pinning the collective full down instead is not how anyone lands a
            // helicopter without an engine, and it measures something else entirely - the
            // rotor overspeeds to 116%, the disc makes almost no lift, and the aircraft
            // arrives at 5400 fpm having "glided" 1.5:1. The pilot's job in the descent is
            // to keep Nr in the green, and the rate of descent is what falls out of that.
            var ap = new Autopilot { CollectiveTrim = 0.25 };
            // Averaged over the last five seconds, not sampled on the final frame. The
            // collective loop hunts a little, so a single sample lands wherever the hunt
            // happens to be and the numbers come out non-monotonic with speed - 60 kt
            // apparently gliding worse than both 50 and 80, which no aircraft does.
            double rodSum = 0, nrSum = 0, groundSum = 0;
            double pMain = 0, pTail = 0, pDrive = 0, pPara = 0, vrsSum = 0, inflowSum = 0;
            double stallSum = 0, clSum = 0, indSum = 0, profSum = 0;
            int samples = 0;
            const double dt = 1.0 / 240.0;
            for (double t = 0; t < 60; t += dt)
            {
                var demand = new AutopilotDemand
                {
                    ForwardSpeed = kt / Kt,
                    LateralSpeed = 0,
                    RotorRpmFraction = 1.0,
                    Heading = 0,
                };
                h.Input = ap.Update(h, demand, dt);
                h.Step(dt);

                if (t > 55)
                {
                    rodSum += h.State.Velocity.Z;             // NED: +Z is down
                    nrSum += h.Telemetry.RotorRpmPercent;
                    groundSum += Math.Sqrt(h.State.Velocity.X * h.State.Velocity.X +
                                           h.State.Velocity.Y * h.State.Velocity.Y);
                    // Averaged with everything else. The collective loop holding Nr hunts
                    // a little, so a single sample of shaft power catches the hunt rather
                    // than the condition - which is D-042 for the third time in one file.
                    pMain += h.Telemetry.MainRotorPower;
                    pTail += h.Telemetry.TailRotorPower;
                    pDrive += h.Telemetry.DrivetrainPower;
                    pPara += h.Telemetry.ParasitePower;
                    vrsSum += h.Telemetry.VrsSeverity;
                    inflowSum += h.Rotor.Inflow.Lambda0;
                    stallSum += h.Telemetry.StalledFraction;
                    clSum += h.Telemetry.BladeLoading;
                    indSum += h.Telemetry.InducedPower;
                    profSum += h.Telemetry.ProfilePower;
                    samples++;
                }
            }

            double rod = samples > 0 ? rodSum / samples : 0;
            double nr = samples > 0 ? nrSum / samples : 0;
            double ground = samples > 0 ? groundSum / samples : 0;
            double ratio = rod > 0.1 ? ground / rod : 0;
            // Close the energy books. Descent power is weight times rate of descent, and
            // everything it pays for is now itemised.
            double weight = h.TotalMass * Atmosphere.Gravity;
            double n = Math.Max(samples, 1);
            Console.WriteLine($"  {kt,5:F0}  {rod * Fpm,6:F0} fpm   {ratio,5:F2}:1  {nr,5:F0}%  " +
                              $"descent {weight * rod / 1000,5:F0} kW =" +
                              $" main {pMain / n / 1000,6:F0}" +
                              $" tail {pTail / n / 1000,5:F0}" +
                              $" drive {pDrive / n / 1000,5:F0}" +
                              $" para {pPara / n / 1000,5:F0} kW" +
                              $"  induced {indSum / n / 1000,6:F0} profile {profSum / n / 1000,6:F0} kW" +
                              $"  stalled {stallSum / n,4:P0}");

            if (ratio > bestRatio) { bestRatio = ratio; bestKt = kt; bestRod = rod; }
            if (rod > 0.1 && rod < minRod) minRod = rod;
            if (nr < 60) failure ??= $"rotor decayed to {nr:F0}% autorotating at {kt:F0} kt";
        }

        Console.WriteLine();
        Console.WriteLine($"  best glide {bestRatio:F2}:1 at {bestKt:F0} kt, {bestRod * Fpm:F0} fpm down");
        Console.WriteLine("  reference, UH-1H: best glide ~3.6:1 at 60-65 kt, ~1500-1700 fpm down");

        // --- Remaining gap -----------------------------------------------------
        // With the lateral channel active (D-054), the autoglide rig agrees with the
        // six-DOF trim to within 10%, confirming the measurement is clean. The model
        // reaches ~2.7-2.9:1 against a real ~3.6:1 — about 20-25% short. That is a real
        // physics gap, not a measurement artefact, but it is NOT "half as far" — the
        // original 2:1 reading was inflated by uncorrected sideways drift dragging 13.5 m²
        // of fuselage side area, and the 4:1 reference was a rule-of-thumb that includes
        // flare distance; 60 kt / 1700 fpm = 3.57:1 steady-state.
        //
        // The remaining ~20% likely lives in profile power (9-12% of blade elements past
        // stall, compressibility drag divergence at tip Mach 0.81 vs threshold 0.74) and
        // the inflow model (momentum theory vs a real wake). The powered envelope still
        // matches the real aircraft closely (505 km range, textbook power curve), so the
        // guards protect against regression at the measured level.
        Console.WriteLine($"  REMAINING GAP: {3.6 / Math.Max(bestRatio, 0.01):F0}% short of the reference.");
        Console.WriteLine("  See D-054. Guarded against regression.");

        Console.WriteLine($"  slowest descent in the sweep {minRod * Fpm:F0} fpm");

        // Guarded at 2.50 after D-054 fixed the lateral drift in the test rig.
        // Before the fix: best 1.98:1 (uncorrected sideslip dragging the fuselage sideways).
        // After: 2.66:1 — within 10% of the six-DOF trim (2.91:1).
        if (bestRatio < 2.50)
            failure ??= $"autorotation glide has REGRESSED to {bestRatio:F2}:1 (was 2.66)";
        if (bestRatio > 5.0)
            failure ??= $"glide ratio {bestRatio:F2}:1 is a sailplane, not a helicopter";
        // Guard the SLOWEST descent in the sweep rather than the descent at the best-glide
        // speed. They are not the same number, and the second one jumps discontinuously the
        // moment the best-glide speed shifts. A guard that fires on an across-the-board
        // improvement is measuring the sweep's argmax, not the aircraft.
        if (minRod * Fpm > 2700)
            failure ??= $"rate of descent has REGRESSED to {minRod * Fpm:F0} fpm (was 2490)";

        return failure;
    }

    /// <summary>
    /// The autorotation equilibrium solved as a TRIM, not flown by a controller.
    ///
    /// <see cref="Autorotation"/> flies the aircraft with the autopilot and reads what
    /// happens, which is the right question to ask on behalf of a player and the wrong one
    /// to ask of the flight model. After D-054 the autopilot rig does hold lateral drift to
    /// zero, and the two agree within ~10%. This trim is still useful because it is the
    /// exact answer: no controller dynamics, no averaging window, no settling time.
    ///
    /// Six unknowns (collective, both cyclics, pedal,
    /// pitch, bank) against six residuals (three forces, three moments) at a pinned
    /// descending condition with the engine failed and Nr held at 100%, then bisect the
    /// descent rate until net shaft power crosses zero. Straight flight, no sideslip, no
    /// controller, averaged over a whole revolution - what the disc can actually do.
    /// </summary>
    public static string? AutorotationTrim()
    {
        Console.WriteLine("  autorotation solved as a six-DOF trim: straight, no sideslip, Nr pinned 100%");
        Console.WriteLine("     kt     ROD       glide    lever   pitch   shaft kW   residual");

        string? failure = null;
        double bestRatio = 0, bestKt = 0, bestRod = 0;

        foreach (double kt in new[] { 40.0, 50.0, 60.0, 70.0, 80.0 })
        {
            Helicopter h = Fresh();
            h.PlaceInFlight(1500);
            h.UseInternalGroundModel = false;
            h.Sas.Enabled = false;
            h.Engine.Fail();

            double vx = kt / Kt;
            double lo = 3.0, hi = 30.0;
            double[]? seed = null;
            double[] x = new double[6];
            double shaft = 0, res = 0;
            for (int i = 0; i < 9; i++)
            {
                double mid = 0.5 * (lo + hi);
                (x, shaft, res) = TrimDescent(h, vx, mid, seed);
                seed = (double[])x.Clone();
                // Positive shaft power means the rotor is being braked: descend harder.
                if (shaft > 0) lo = mid; else hi = mid;
            }
            double w = 0.5 * (lo + hi);
            (x, shaft, res) = TrimDescent(h, vx, w, seed);

            double ratio = vx / w;
            Console.WriteLine($"   {kt,5:F0}  {w * Fpm,6:F0} fpm  {ratio,5:F2}:1  {x[0],6:F3}  " +
                              $"{x[4] * 180 / Math.PI,6:F1}  {shaft / 1000,8:F1}   {res:F5}");
            if (res > 5e-3) failure ??= $"descent trim did not converge at {kt:F0} kt (residual {res:F4})";
            if (ratio > bestRatio) { bestRatio = ratio; bestKt = kt; bestRod = w; }
        }

        Console.WriteLine();
        Console.WriteLine($"  best glide {bestRatio:F2}:1 at {bestKt:F0} kt, {bestRod * Fpm:F0} fpm down");
        Console.WriteLine("  reference, UH-1H: best glide ~3.6:1 at 60-65 kt, ~1500-1700 fpm down");
        Console.WriteLine("  The disc reaches ~2.9:1 — about 20% short. See D-054.");

        if (bestRatio < 2.9)
            failure ??= $"trimmed autorotation glide has REGRESSED to {bestRatio:F2}:1 (was 3.15)";
        if (bestRod * Fpm > 2800)
            failure ??= $"trimmed rate of descent has REGRESSED to {bestRod * Fpm:F0} fpm (was 2568)";
        return failure;
    }

    /// <summary>
    /// Net specific force and angular acceleration at a pinned descending condition, after
    /// letting inflow and flapping relax, averaged over a whole rotor revolution.
    /// </summary>
    private static double DescentResiduals(Helicopter h, double[] x, double vx, double w, double[] r)
    {
        const double dt = 1.0 / 240.0;
        var c = new Controls
        {
            Collective = Math.Clamp(x[0], 0, 1),
            CyclicPitch = Math.Clamp(x[1], -1, 1),
            CyclicRoll = Math.Clamp(x[2], -1, 1),
            Pedal = Math.Clamp(x[3], -1, 1),
            Throttle = 0,
        };
        var att = Quat.FromEuler(x[5], x[4], 0);
        var vel = new Vec3(vx, 0, w);
        double nom = h.Airframe.MainRotor.NominalOmega;

        // Start every evaluation from the same rotor state, or the residual is a function
        // of evaluation ORDER as well as of the unknowns and Newton is being fed noise.
        h.Rotor.Reset();
        h.RotorOmega = nom;
        for (int i = 0; i < 400; i++)
        {
            h.State.Position = new Vec3(0, 0, -1500);
            h.State.Orientation = att;
            h.State.Velocity = vel;
            h.State.AngularVelocity = Vec3.Zero;
            h.RotorOmega = nom;
            h.ForceActuators(c);
            h.Step(dt);
        }

        Vec3 f = Vec3.Zero, m = Vec3.Zero;
        double shaft = 0;
        int n = Math.Max(8, (int)Math.Round(Math.Tau / nom / dt));
        for (int i = 0; i < n; i++)
        {
            h.State.Position = new Vec3(0, 0, -1500);
            h.State.Orientation = att;
            h.State.Velocity = vel;
            h.State.AngularVelocity = Vec3.Zero;
            h.RotorOmega = nom;
            h.ForceActuators(c);
            h.Step(dt);
            f += (h.State.Velocity - vel) / dt;          // gravity included
            m += h.State.AngularVelocity / dt;
            shaft += h.Telemetry.MainRotorPower + h.Telemetry.TailRotorPower
                     + h.Telemetry.DrivetrainPower;
        }
        r[0] = f.X / n / Atmosphere.Gravity;
        r[1] = f.Y / n / Atmosphere.Gravity;
        r[2] = f.Z / n / Atmosphere.Gravity;
        r[3] = m.X / n * 0.05;
        r[4] = m.Y / n * 0.05;
        r[5] = m.Z / n * 0.05;
        return shaft / n;
    }

    /// <summary>Damped Newton on the six descent residuals, numerical Jacobian.</summary>
    private static (double[] x, double shaft, double residual)
        TrimDescent(Helicopter h, double vx, double w, double[]? seed)
    {
        var x = new double[6];
        if (seed is not null) Array.Copy(seed, x, 6); else x[0] = 0.2;

        var r = new double[6];
        var probe = new double[6];
        var trial = new double[6];
        double shaft = DescentResiduals(h, x, vx, w, r);
        double norm = Norm(r);
        var jac = new double[6, 6];

        for (int it = 0; it < 25 && norm > 2e-4; it++)
        {
            for (int col = 0; col < 6; col++)
            {
                var xp = (double[])x.Clone();
                xp[col] += 3e-4;
                DescentResiduals(h, xp, vx, w, probe);
                for (int row = 0; row < 6; row++) jac[row, col] = (probe[row] - r[row]) / 3e-4;
            }
            if (!SolveLinear(jac, r, out double[] dx)) break;

            double a = 1.0;
            bool improved = false;
            for (int back = 0; back < 14; back++)
            {
                for (int i = 0; i < 6; i++) trial[i] = x[i] - a * dx[i];
                trial[0] = Math.Clamp(trial[0], 0, 1);
                double s = DescentResiduals(h, trial, vx, w, probe);
                if (Norm(probe) < norm)
                {
                    Array.Copy(trial, x, 6);
                    Array.Copy(probe, r, 6);
                    norm = Norm(probe);
                    shaft = s;
                    improved = true;
                    break;
                }
                a *= 0.5;
            }
            if (!improved) break;
        }
        return (x, shaft, norm);
    }

    private static double Norm(double[] r)
    {
        double s = 0;
        foreach (double v in r) s += v * v;
        return Math.Sqrt(s);
    }

    private static bool SolveLinear(double[,] a, double[] b, out double[] x)
    {
        int n = b.Length;
        var m = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n] = b[i];
        }
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int i = c + 1; i < n; i++) if (Math.Abs(m[i, c]) > Math.Abs(m[p, c])) p = i;
            if (Math.Abs(m[p, c]) < 1e-12) { x = new double[n]; return false; }
            if (p != c) for (int j = 0; j <= n; j++) (m[c, j], m[p, j]) = (m[p, j], m[c, j]);
            for (int i = 0; i < n; i++)
            {
                if (i == c) continue;
                double f = m[i, c] / m[c, c];
                for (int j = c; j <= n; j++) m[i, j] -= f * m[c, j];
            }
        }
        x = new double[n];
        for (int i = 0; i < n; i++) x[i] = m[i, n] / m[i, i];
        return true;
    }

    /// <summary>
    /// Can the player actually make the crossings the world is now built out of?
    ///
    /// D-077 turned the map into eight islands with ten committed water crossings, the
    /// longest a shade under eight kilometres. That is a decision about the FLIGHT MODEL
    /// dressed up as a decision about level design, and nothing was checking it: if a
    /// realistic salvaged fuel load cannot cross and come back, the outer islands are not
    /// gated content, they are a wall with a coastline painted on it.
    ///
    /// The number that answers it is specific range - kilometres per kilogram of fuel -
    /// which is a different question from endurance and peaks at a different speed. Loiter
    /// slowly, travel faster.
    /// </summary>
    public static string? CrossingRange()
    {
        Console.WriteLine("  specific range against speed, 500 m, standard day");
        Console.WriteLine("     kt     fuel kg/h     km/h      km per kg");

        double bestKmPerKg = 0, bestKt = 0;
        for (double kt = 40; kt <= 120; kt += 10)
        {
            (double _, double _, double fuelFlow, bool ok) = AtSpeed(500, kt);
            if (!ok) { Console.WriteLine($"    {kt,5:F0}     did not trim"); continue; }

            double kgPerHour = fuelFlow * 3600.0;
            double kmPerHour = kt / Kt * 3.6;
            double kmPerKg = kgPerHour > 1e-6 ? kmPerHour / kgPerHour : 0;
            Console.WriteLine($"    {kt,5:F0}     {kgPerHour,9:F0}   {kmPerHour,6:F0}      {kmPerKg,9:F2}");
            if (kmPerKg > bestKmPerKg) { bestKmPerKg = kmPerKg; bestKt = kt; }
        }

        if (bestKmPerKg <= 0) return "nothing trimmed at any speed - there is no range to measure";

        Console.WriteLine();
        Console.WriteLine($"  best specific range {bestKmPerKg:F2} km/kg at {bestKt:F0} kt");

        // What that buys, against the actual crossings in WorldMap.BuildRegions. These are
        // the longest over-water runs --worldreport measures on each leg, not the leg
        // lengths: the number that matters is how far you are from land, not how far apart
        // the islands are.
        (string Leg, double Km)[] crossings =
        {
            ("home island (The Pan - Long Acre)", 0.0),
            ("home -> tier 1 (Fenmoor)", 3.1),
            ("tier 1 -> tier 2 (Cold Shoulder)", 3.4),
            ("tier 1 -> tier 2 (Sawtooth Works)", 3.7),
            ("home -> tier 3 (The Scald)", 7.8),
            ("tier 1 -> tier 3 (Ashmount)", 7.0),
        };

        Console.WriteLine();
        Console.WriteLine("  what each crossing costs, there and back, at best range:");
        Console.WriteLine("    leg                                  water    fuel there+back");
        double worst = 0;
        foreach ((string leg, double km) in crossings)
        {
            double kg = 2 * km / bestKmPerKg;
            Console.WriteLine($"    {leg,-36} {km,5:F1} km   {kg,7:F1} kg");
            worst = Math.Max(worst, kg);
        }

        // A salvaged tank, not a full one. D-004 makes fuel the scarce thing and the player
        // rarely has the 800 kg the aircraft can hold; 150 kg is a plausible bad day and is
        // the load the outer islands have to be reachable on.
        const double Salvaged = 150.0;
        Console.WriteLine();
        Console.WriteLine($"  on a salvaged {Salvaged:F0} kg the longest crossing costs " +
                          $"{worst / Salvaged * 100:F0}% of the tank in transit alone, " +
                          $"leaving {(Salvaged - worst) * bestKmPerKg:F0} km of endurance " +
                          "for everything else");

        if (worst >= Salvaged * 0.75)
            return $"the longest crossing burns {worst:F0} kg of a {Salvaged:F0} kg load " +
                   "just getting there and back - the outer islands are not gated, they are " +
                   "unreachable, and D-077's layout needs to come in";
        if (worst <= Salvaged * 0.05)
            return $"the longest crossing costs {worst:F1} kg of {Salvaged:F0} - it is not a " +
                   "commitment at all, and D-059's whole argument is that a crossing should " +
                   "be one";

        return null;
    }
}
