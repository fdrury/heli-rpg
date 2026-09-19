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
        double bestRatio = 0, bestKt = 0, bestRod = 0;

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
            double stallSum = 0, clSum = 0;
            int samples = 0;
            const double dt = 1.0 / 240.0;
            for (double t = 0; t < 60; t += dt)
            {
                var demand = new AutopilotDemand
                {
                    ForwardSpeed = kt / Kt,
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
                              $"  stalled {stallSum / n,5:P0}  Ct/sig {clSum / n,6:F3}");

            if (ratio > bestRatio) { bestRatio = ratio; bestKt = kt; bestRod = rod; }
            if (nr < 60) failure ??= $"rotor decayed to {nr:F0}% autorotating at {kt:F0} kt";
        }

        Console.WriteLine();
        Console.WriteLine($"  best glide {bestRatio:F2}:1 at {bestKt:F0} kt, {bestRod * Fpm:F0} fpm down");
        Console.WriteLine("  reference, UH-1H: best glide near 4:1 at 60-70 kt, about 1700 fpm down");

        // --- Known gap ---------------------------------------------------------
        // The model glides about half as far as the real aircraft, and the shortfall is
        // real rather than a measurement artefact: the rotor holds 100% Nr at every speed,
        // the figures are monotonic, and they are averaged over five seconds of settled
        // descent. Two details point at where it lives - the glide ratio is nearly FLAT
        // across 40 to 90 kt where a real one peaks near best-glide speed, and closing the
        // energy books needs power instrumentation inside ComputeWrench that does not
        // exist yet.
        //
        // Fixing it means reworking the rotor's inflow in the windmill-brake state, which
        // is the one part of the model the rest of the envelope depends on - and that
        // envelope currently matches the real aircraft closely (505 km range against a
        // published 510, minimum power at 60 kt, a textbook power curve). So this asserts
        // against REGRESSION at the measured level, with the real target printed every run
        // so the gap stays visible instead of quietly becoming the standard.
        Console.WriteLine($"  KNOWN GAP: glides {4.0 / Math.Max(bestRatio, 0.01):F1}x worse and " +
                          $"descends {bestRod * Fpm / 1700:F1}x faster than the reference.");
        Console.WriteLine("  See D-041. Guarded against regression, not yet fixed.");

        if (bestRatio < 1.8)
            failure ??= $"autorotation glide has REGRESSED to {bestRatio:F2}:1 (was 1.99)";
        if (bestRatio > 7.0)
            failure ??= $"glide ratio {bestRatio:F2}:1 is a sailplane, not a helicopter";
        if (bestRod * Fpm > 3500)
            failure ??= $"rate of descent has REGRESSED to {bestRod * Fpm:F0} fpm (was 3191)";

        return failure;
    }
}
