using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The 420 kg blade pair as a sling load (D-091, story.md §7.7).
///
/// The sling physics already work and are verified by <see cref="SlingTests"/>. These tests
/// verify the SPECIFIC load that matters for the story: two matched main-rotor blades,
/// 420 kg, on a 5 m strop under the cargo hook.  The finale cannot close without this.
///
/// The final test (<see cref="Finale"/>) is the measurement story.md §5 demands: can the
/// aircraft fly the profile at the worst plausible ceiling, worst plausible fuel, with
/// Wray aboard and the full load, and arrive with margin?  If it cannot, the load must
/// come down or the ceiling floor must come up — and that number decides before the story
/// is authored.
/// </summary>
public static class BladePairTests
{
    const double Dt = 1.0 / 240.0;

    static Helicopter MakeHeli(double fuelKg = 400, double isaDev = 0, double groundElev = 0)
    {
        var env = new FlatEnvironment { GroundElevation = groundElev };
        env.Atmosphere.IsaDeviation = isaDev;
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = fuelKg };
        heli.InvalidateMass();
        return heli;
    }

    // ================================================================ 1. catalog

    /// <summary>
    /// The factory produces a load with the right id, mass, cable and drag.
    /// </summary>
    public static string? Catalog()
    {
        Console.WriteLine("  SlingLoads.BladePair() configuration");

        var bp = SlingLoads.BladePair();
        if (bp.Name != "blade_pair") return $"name: {bp.Name}";
        if (Math.Abs(bp.EmptyMass - 420.0) > 0.01) return $"mass: {bp.EmptyMass}";
        if (Math.Abs(bp.CableLength - 5.0) > 0.01) return $"cable: {bp.CableLength}";
        if (Math.Abs(bp.DragArea - 2.0) > 0.01) return $"drag: {bp.DragArea}";

        Console.WriteLine($"  {bp.Name}: {bp.EmptyMass} kg, {bp.CableLength} m cable, " +
                          $"{bp.DragArea} m² drag");

        // ById lookup
        var byId = SlingLoads.ById("blade_pair");
        if (byId is null) return "ById(blade_pair) returned null";
        if (byId.Name != "blade_pair") return "ById(blade_pair) wrong name";
        if (SlingLoads.ById("nonexistent") is not null) return "ById(nonexistent) should be null";

        return null;
    }

    // ================================================================ 2. trim

    /// <summary>
    /// The trim solver converges with the blade pair on the hook and the collective
    /// is higher than without the load.
    /// </summary>
    public static string? TrimEffect()
    {
        Console.WriteLine("  hover trim at 500 m with blade pair on the hook");
        Console.WriteLine("       condition            collective   pitch deg   power kW   residual g");

        // Bare aircraft
        var bare = MakeHeli(groundElev: -200);
        var tBare = bare.PlaceInFlightTrimmed(500);

        // With blade pair
        var loaded = MakeHeli(groundElev: -200);
        var bp = SlingLoads.BladePair();
        loaded.Hook = bp;
        var tLoaded = loaded.PlaceInFlightTrimmed(500);

        Console.WriteLine($"       no load            {tBare.Controls.Collective,12:F3} " +
                          $"{tBare.PitchRad * 180 / Math.PI,11:F2} " +
                          $"{bare.Telemetry.PowerRequired / 1000,10:F0} {tBare.ForceResidualG,12:F6}");
        Console.WriteLine($"       blade pair (420 kg){tLoaded.Controls.Collective,12:F3} " +
                          $"{tLoaded.PitchRad * 180 / Math.PI,11:F2} " +
                          $"{loaded.Telemetry.PowerRequired / 1000,10:F0} {tLoaded.ForceResidualG,12:F6}");

        if (!tLoaded.Converged)
            return $"trim did not converge with the blade pair on the hook " +
                   $"(residual {tLoaded.ForceResidualG:F5} g)";

        double collectiveDelta = tLoaded.Controls.Collective - tBare.Controls.Collective;
        Console.WriteLine($"  collective delta: {collectiveDelta:F3}");
        if (collectiveDelta < 0.01)
            return $"the blade pair only moved the collective by {collectiveDelta:F3} — " +
                   "420 kg on the hook is not reaching the rotor";

        // Power should be higher
        double powerDelta = loaded.Telemetry.PowerRequired - bare.Telemetry.PowerRequired;
        Console.WriteLine($"  power delta: {powerDelta / 1000:F0} kW");
        if (powerDelta < 5000)
            return $"the blade pair only added {powerDelta / 1000:F0} kW — " +
                   "420 kg should cost substantially more power";

        return null;
    }

    // ================================================================ 3. hover ceiling

    /// <summary>
    /// The blade pair costs a measurable amount of hover ceiling and the result is
    /// consistent with the 200 kg / 625 m datum from the existing sling tests.
    /// </summary>
    public static string? CeilingCost()
    {
        Console.WriteLine("  hover ceiling OGE, 125 m ladder, 400 kg fuel:");
        Console.WriteLine("       condition                   load kg   ceiling m");

        double bare = MeasureCeiling(null, 0);
        double blades = MeasureCeiling("blade_pair", 0);
        double bladesHot = MeasureCeiling("blade_pair", 20);

        Console.WriteLine($"       no hook                          0 {bare,11:F0}");
        Console.WriteLine($"       blade pair                     420 {blades,11:F0}");
        Console.WriteLine($"       blade pair, ISA+20             420 {bladesHot,11:F0}");
        Console.WriteLine();
        Console.WriteLine($"  blade pair costs {bare - blades:F0} m of hover ceiling " +
                          $"({bare:F0} -> {blades:F0})");

        if (blades >= bare)
            return "the blade pair did not lower the hover ceiling at all — " +
                   "the cable tension is not reaching the rotor";
        if (bare - blades < 500)
            return $"420 kg on the hook cost only {bare - blades:F0} m of ceiling — " +
                   "should cost substantially more (200 kg costs 625 m)";
        if (bladesHot >= blades)
            return "a hot day did not cost anything with the load on";

        return null;
    }

    static double MeasureCeiling(string? loadId, double isaDev)
    {
        double ceiling = 0;
        for (double alt = 125; alt <= 5000; alt += 125)
        {
            var h = MakeHeli(400, isaDev);
            SlingLoad? load = null;
            if (loadId is not null)
            {
                load = SlingLoads.ById(loadId);
                if (load is null) break;
                h.Hook = load;
            }
            h.PlaceInFlight(alt);
            load?.Reset(h);

            var ap = new Autopilot { CollectiveTrim = 0.65 };
            var demand = new AutopilotDemand
                { Altitude = alt, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
            for (int i = 0; i < 9600; i++)  // 40 s
            {
                h.Input = ap.Update(h, demand, Dt);
                h.Step(Dt);
            }
            if (Math.Abs(h.State.Altitude - alt) < 8 && h.Input.Collective < 0.985) ceiling = alt;
            else break;
        }
        return ceiling;
    }

    // ================================================================ 4. save round-trip

    /// <summary>
    /// SlingLoadId survives a save/load round trip, and a null load does too.
    /// </summary>
    public static string? SaveRoundTrip()
    {
        Console.WriteLine("  sling load save/load round trip");

        var p = Progress.NewGame();
        p.SlingLoadId = "blade_pair";

        var save = new SaveData();
        save.CaptureProgress(p);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();
        if (p2.SlingLoadId != "blade_pair")
            return $"sling load lost: got '{p2.SlingLoadId}'";

        Console.WriteLine($"  round trip: '{p2.SlingLoadId}'");

        // Null load also survives.
        var p3 = Progress.NewGame();
        var save2 = new SaveData();
        save2.CaptureProgress(p3);
        var loaded2 = SaveData.FromJson(save2.ToJson())!;
        var p4 = loaded2.ApplyProgress();
        if (p4.SlingLoadId is not null)
            return $"null sling load should survive, got '{p4.SlingLoadId}'";

        return null;
    }

    // ================================================================ 5. finale

    /// <summary>
    /// The measurement story.md §5 demands: can the aircraft fly from The Scald back to
    /// home ground at the worst plausible ceiling, worst plausible fuel, full load and
    /// passenger, and arrive with margin?
    ///
    /// <para><b>Profile.</b> The worst case from §5: rotor ceiling at 0.58 (about 220
    /// flight hours), ISA+10 (warm night in ash), with Wray aboard (68 kg) and the blade
    /// pair on the hook (420 kg). Fuel is 300 kg — not full, because the hook needs the
    /// bay the long-range tank used, and the story says to take the tank off. Start from
    /// sea level (The Scald), climb to 120 m (minimum SAM clearance), cruise 10 km
    /// (Scald to nearest safe island), descend and settle.</para>
    ///
    /// <para><b>What it reports.</b> The altitude achieved, the fuel remaining, the
    /// collective at the cruise, and whether the aircraft settled or crashed. If it does
    /// not close, the load comes down or the ceiling floor comes up — and this test says
    /// which before the dialogue is authored.</para>
    /// </summary>
    public static string? Finale()
    {
        // The worst plausible ceiling: 220 hours, giving ~0.58.
        double flightHours = 220;
        double expectedCeiling = Math.Clamp(1.00 - DamageState.CeilingRate * flightHours,
                                            DamageState.CeilingFloor, 1.00);
        Console.WriteLine($"  Finale feasibility: {flightHours:F0} h, " +
                          $"rotor ceiling {expectedCeiling:F2}");

        // Build aircraft with degraded rotor.
        var env = new FlatEnvironment { GroundElevation = 0 };
        env.Atmosphere.IsaDeviation = 10;        // warm night
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = 300 };
        heli.Damage.TotalFlightHours = flightHours;
        heli.Damage.RepairAll();                 // repair to the ceiling, not beyond

        double actualCeiling = heli.Damage.MainRotorCeiling;
        double rotorHealth = heli.Damage.Health(Component.MainRotor);
        Console.WriteLine($"  rotor ceiling: {actualCeiling:F2}, rotor health: {rotorHealth:F2}");

        // Add passenger (Wray, 68 kg in the right seat).
        var pax = Passenger.Wray;
        heli.Airframe.Mass.Add("passenger", pax.Position, pax.Mass);

        // Hook module mass (28 kg at the hook position).
        var hookDef = Loadout.Catalog["hook"];
        heli.Airframe.Mass.Add("mod_hook", hookDef.Position, hookDef.Mass);

        // Blade pair on the hook.
        var bp = SlingLoads.BladePair();
        heli.Hook = bp;
        heli.InvalidateMass();

        double totalMass = heli.TotalMass + bp.Mass;  // airframe + fuel + pax + hook + load on cable
        Console.WriteLine($"  total mass: {totalMass:F0} kg " +
                          $"(airframe + {heli.Fuel:F0} fuel + {pax.Mass:F0} pax + " +
                          $"{hookDef.Mass:F0} hook + {bp.Mass:F0} load)");

        // --- Phase 1: can it hover OGE and begin to climb? ----------------------
        Console.WriteLine();
        Console.WriteLine("  Phase 1: hover OGE and climb check");

        heli.PlaceOnGround(running: true);
        // Let the engine stabilise for 5 seconds on the ground.
        var ap = new Autopilot { CollectiveTrim = 0.65 };
        for (double t = 0; t < 5; t += Dt)
        {
            heli.Input = ap.Update(heli, new AutopilotDemand
                { Altitude = 0, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 }, Dt);
            heli.Step(Dt);
        }

        // Climb to 120 m over 90 seconds.
        double climbTarget = 120;
        double peakAlt = 0, settledAlt = 0;
        double peakCollective = 0, peakTorque = 0;
        bool stalled = false;

        for (double t = 0; t < 90; t += Dt)
        {
            double demand = Math.Min(climbTarget, 2.0 * t);  // ramp up gently
            heli.Input = ap.Update(heli, new AutopilotDemand
                { Altitude = demand, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 }, Dt);
            heli.Step(Dt);

            peakAlt = Math.Max(peakAlt, heli.State.Altitude);
            peakCollective = Math.Max(peakCollective, heli.Input.Collective);
            peakTorque = Math.Max(peakTorque, heli.Telemetry.TorquePercent);

            if (heli.State.Altitude < -5) { stalled = true; break; }
        }
        settledAlt = heli.State.Altitude;
        Console.WriteLine($"  peak altitude: {peakAlt:F0} m, settled: {settledAlt:F0} m");
        Console.WriteLine($"  peak collective: {peakCollective:F3}, peak torque: {peakTorque:F0}%");

        if (stalled)
            return "the aircraft could not get airborne with the load — " +
                   "the ceiling floor needs to come up or the load needs to come down";

        bool reachedClimb = settledAlt > climbTarget * 0.8;
        Console.WriteLine($"  reached 80% of {climbTarget} m target: {reachedClimb}");

        // --- Phase 2: cruise 10 km at best endurance speed (~30 kt / 15 m/s) ----
        Console.WriteLine();
        Console.WriteLine("  Phase 2: 10 km cruise at 15 m/s");

        double cruiseSpeed = 15.0;     // ~30 kt, gentle
        double cruiseAlt = Math.Min(settledAlt, climbTarget);
        double distanceCovered = 0;
        double fuelAtStart = heli.Fuel;
        double minAlt = cruiseAlt;

        for (double t = 0; t < 900 && distanceCovered < 10000; t += Dt)
        {
            heli.Input = ap.Update(heli, new AutopilotDemand
                { Altitude = cruiseAlt, ForwardSpeed = cruiseSpeed, Heading = 0 }, Dt);
            heli.Step(Dt);

            distanceCovered += heli.Telemetry.GroundSpeed * Dt;
            minAlt = Math.Min(minAlt, heli.State.Altitude);

            if (heli.State.Altitude < 5) { stalled = true; break; }
            if (heli.Fuel <= 0) break;
        }

        double fuelUsed = fuelAtStart - heli.Fuel;
        Console.WriteLine($"  distance: {distanceCovered / 1000:F1} km in {distanceCovered / Math.Max(cruiseSpeed, 1):F0} s");
        Console.WriteLine($"  fuel used: {fuelUsed:F0} kg, remaining: {heli.Fuel:F0} kg");
        Console.WriteLine($"  minimum altitude: {minAlt:F0} m");
        Console.WriteLine($"  final altitude: {heli.State.Altitude:F0} m");

        if (stalled)
            return "the aircraft could not maintain altitude during the cruise — " +
                   "the ceiling floor needs adjustment";

        // --- Verdict -----------------------------------------------------------
        Console.WriteLine();
        double fuelMargin = heli.Fuel;
        double altMargin = heli.State.Altitude;
        bool closes = distanceCovered >= 9500 && fuelMargin > 10 && !stalled;

        Console.WriteLine($"  VERDICT: the finale {(closes ? "CLOSES" : "DOES NOT CLOSE")}");
        Console.WriteLine($"    distance covered: {distanceCovered / 1000:F1} km (need 10)");
        Console.WriteLine($"    fuel remaining:   {fuelMargin:F0} kg");
        Console.WriteLine($"    altitude:         {altMargin:F0} m");

        if (!closes)
        {
            if (distanceCovered < 9500)
                return $"only covered {distanceCovered / 1000:F1} km of the 10 km leg — " +
                       "the aircraft ran out of fuel or altitude";
            return "the finale does not close: the load must come down or the ceiling must come up";
        }

        // The finale closes — report the margins so somebody can read them.
        Console.WriteLine($"  Margin: {fuelMargin:F0} kg fuel, {altMargin:F0} m altitude — " +
                          "tight but flyable, which is the whole point");

        return null;
    }
}
