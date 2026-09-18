using Rotorwash.Sim;

namespace Rotorwash.SimLab;

public static class ThreatTests
{
    const double Dt = 0.05;

    /// <summary>Run a field for a while at a fixed state and report what happened.</summary>
    static (double exposure, bool locked, int hits) Fly(
        ThreatField field, double north, double east, double agl, double speed,
        bool los, double seconds, DamageState? damage = null)
    {
        int hits = 0;
        field.Struck += e => { if (e.Severity > 0) { hits++; damage?.Apply(e.Hit, e.Severity, DamageCause.Gunfire, e.Note); } };
        for (double t = 0; t < seconds; t += Dt)
            field.Update(north, east, agl, 0, speed, Dt, _ => los);
        return (field.Exposure, field.AnyLocked, hits);
    }

    public static string? Masking()
    {
        Console.WriteLine("  the same aircraft, in the same place, seen and unseen");
        Console.WriteLine("   condition                       exposure   locked");

        var seen = new ThreatField();
        seen.Add(ThreatField.Make(1, "Bitter Head SAM", ThreatKind.Sam, 0, 0));
        var a = Fly(seen, 4000, 0, 400, 50, los: true, 14);
        Console.WriteLine($"  {"4 km, 400 m agl, clear view",-30} {a.exposure,8:F2}   {a.locked}");

        var masked = new ThreatField();
        masked.Add(ThreatField.Make(2, "Bitter Head SAM", ThreatKind.Sam, 0, 0));
        var b = Fly(masked, 4000, 0, 400, 50, los: false, 14);
        Console.WriteLine($"  {"same spot, behind a ridge",-30} {b.exposure,8:F2}   {b.locked}");

        // Break the line of sight after being tracked: it should decay, not vanish.
        var breaking = new ThreatField();
        breaking.Add(ThreatField.Make(3, "Bitter Head SAM", ThreatKind.Sam, 0, 0));
        for (double t = 0; t < 8; t += Dt) breaking.Update(4000, 0, 400, 0, 50, Dt, _ => true);
        double atBreak = breaking.Exposure;
        for (double t = 0; t < 1.0; t += Dt) breaking.Update(4000, 0, 400, 0, 50, Dt, _ => false);
        double after1s = breaking.Exposure;
        for (double t = 0; t < 4.0; t += Dt) breaking.Update(4000, 0, 400, 0, 50, Dt, _ => false);
        Console.WriteLine($"  {"drop behind the ridge",-30} {atBreak,8:F2} -> {after1s:F2} after 1 s -> {breaking.Exposure:F2} after 3 s");

        if (a.exposure < 0.9) return $"a clear view at 4 km only reached {a.exposure:F2} exposure";
        if (b.exposure > 0.01) return $"terrain masking leaked {b.exposure:F2} exposure";
        if (after1s >= atBreak) return "breaking line of sight did not reduce the track";
        if (breaking.Exposure > 0.02) return $"the track was still at {breaking.Exposure:F2} five seconds after losing sight";
        return null;
    }

    /// <summary>Seconds until this emitter has a firing solution, or -1 if it never does.</summary>
    static double TimeToLock(ThreatKind kind, double range, double agl, double speed,
                             Countermeasure fitted = Countermeasure.None)
    {
        var f = new ThreatField { Fitted = fitted };
        f.Add(ThreatField.Make(1, "probe", kind, 0, 0));
        for (double t = 0; t < 120; t += Dt)
        {
            f.Update(range, 0, agl, 0, speed, Dt, _ => true);
            if (f.AnyLocked) return t;
        }
        return -1;
    }

    public static string? AltitudeBands()
    {
        Console.WriteLine("  each threat owns an altitude band - that is the level design");
        Console.WriteLine("  (seconds of exposure before it has a firing solution; - means never)");
        Console.WriteLine("   threat          band agl        10 m     150 m     800 m    2500 m");

        foreach (ThreatKind kind in new[] { ThreatKind.Gun, ThreatKind.Manpads, ThreatKind.Sam, ThreatKind.Aerostat })
        {
            ThreatEmitter probe = ThreatField.Make(0, "probe", kind, 0, 0);
            Console.Write($"  {kind,-14} {probe.MinAltitude,4:F0}-{probe.MaxAltitude,-9:F0}");
            foreach (double agl in new[] { 10.0, 150.0, 800.0, 2500.0 })
            {
                // Sit at half of detection range so range is not the variable.
                double ttl = TimeToLock(kind, probe.DetectionRange * 0.5, agl, 50);
                Console.Write(ttl < 0 ? $"  {"-",8}" : $"  {ttl,8:F1}");
            }
            Console.WriteLine();
        }

        // Low flying must buy TIME against a ground radar, and must not against the aerostat.
        double low = TimeToLock(ThreatKind.Sam, 7000, 130, 22);
        double high = TimeToLock(ThreatKind.Sam, 7000, 900, 22);
        double aLow = TimeToLock(ThreatKind.Aerostat, 8000, 130, 50);
        double aHigh = TimeToLock(ThreatKind.Aerostat, 8000, 900, 50);
        double fast = TimeToLock(ThreatKind.Sam, 7000, 900, 58);

        Console.WriteLine();
        Console.WriteLine($"  ground radar   {low,5:F1} s at 130 m vs {high,5:F1} s at 900 m   -  low flying buys time");
        Console.WriteLine($"  aerostat       {aLow,5:F1} s at 130 m vs {aHigh,5:F1} s at 900 m   -  and against this, it does not");
        Console.WriteLine($"  speed          {fast,5:F1} s at 113 kt vs {high,5:F1} s at 43 kt, both at 900 m");

        if (low <= high * 1.3) return $"flying low bought only {low - high:F1} s against a ground radar";
        if (aLow > aHigh * 1.3) return "the aerostat was fooled by low flying, which is the one thing it should not be";
        if (fast <= high * 1.1) return $"flying fast bought only {fast - high:F1} s";
        return null;
    }

    public static string? Countermeasures()
    {
        Console.WriteLine("  a countermeasure has to be the RIGHT countermeasure");

        (int hits, string label) Trial(ThreatKind kind, Countermeasure fitted, bool dispenseChaff, bool dispenseFlares)
        {
            int hits = 0;
            var f = new ThreatField { Fitted = fitted, ChaffRemaining = 120, FlaresRemaining = 120 };
            f.Add(ThreatField.Make(1, "probe", kind, 0, 0));
            f.Struck += e => { if (e.Severity > 0) hits++; };

            // A pilot does not fire one cartridge; they fire a sequence while the weapon
            // is in the air. Firing once and hoping is not a countermeasure, it is a wish.
            double nextDispense = 0;
            for (double t = 0; t < 180; t += Dt)
            {
                f.Update(1500, 0, 400, 0, 45, Dt, _ => true);
                if (f.AnyEngaging && t >= nextDispense)
                {
                    if (dispenseChaff) f.DispenseChaff();
                    if (dispenseFlares) f.DispenseFlares();
                    nextDispense = t + 1.5;
                }
            }
            return (hits, $"{kind} vs {fitted}");
        }

        var samNone = Trial(ThreatKind.Sam, Countermeasure.None, false, false);
        var samChaff = Trial(ThreatKind.Sam, Countermeasure.Chaff, true, false);
        var samFlare = Trial(ThreatKind.Sam, Countermeasure.Flares, false, true);
        var manNone = Trial(ThreatKind.Manpads, Countermeasure.None, false, false);
        var manFlare = Trial(ThreatKind.Manpads, Countermeasure.Flares, false, true);
        var manChaff = Trial(ThreatKind.Manpads, Countermeasure.Chaff, true, false);

        Console.WriteLine($"   {"trial",-34} hits in 180 s");
        foreach (var (hits, label) in new[] { samNone, samChaff, samFlare, manNone, manFlare, manChaff })
            Console.WriteLine($"  {label,-34} {hits,6}");

        if (samNone.hits < 3) return $"a SAM with no countermeasures only hit {samNone.hits} times in three minutes";
        if (samChaff.hits >= samNone.hits) return "chaff did not help against a radar SAM";
        if (samFlare.hits < samNone.hits) return "flares defeated a radar SAM, which they should not";
        if (manNone.hits < 2) return $"a MANPADS only hit {manNone.hits} times in three minutes";
        if (manFlare.hits >= manNone.hits) return "flares did not help against a heat seeker";
        if (manChaff.hits < manNone.hits) return "chaff defeated a heat seeker, which it should not";
        return null;
    }

    public static string? Survivability()
    {
        // D-005a: a hit takes a system and leaves an autorotation. It must not delete the
        // aircraft, because total unearned punishment reads as irritation, not as a gate.
        Console.WriteLine("  taking hits, and checking the aircraft is still something a pilot can save");

        int stillAirworthy = 0, tookDamage = 0, trials = 60;
        var componentTally = new Dictionary<Component, int>();

        for (int i = 0; i < trials; i++)
        {
            var dmg = new DamageState();
            // A different seed per trial. The first version of this test reused one seed,
            // so all forty "trials" were the same trial run forty times.
            var f = new ThreatField(9000 + i * 137);
            f.Add(ThreatField.Make(1, "gun", ThreatKind.Gun, 0, 0));
            f.Add(ThreatField.Make(2, "manpads", ThreatKind.Manpads, 400, 0));
            f.Struck += e =>
            {
                if (e.Severity <= 0) return;
                dmg.Apply(e.Hit, e.Severity, DamageCause.Gunfire, e.Note);
                componentTally[e.Hit] = componentTally.GetValueOrDefault(e.Hit) + 1;
            };
            for (double t = 0; t < 45; t += Dt) f.Update(600, 0, 250, 0, 45, Dt, _ => true);
            if (dmg.Airworthy) stillAirworthy++;
            if (dmg.ToString() != "serviceable") tookDamage++;
        }

        double forcedDown = (trials - stillAirworthy) * 100.0 / trials;
        Console.WriteLine($"  45 s inside a gun AND a MANPADS envelope, {trials} times over:");
        Console.WriteLine($"    took damage        {tookDamage,3} of {trials}");
        Console.WriteLine($"    still airworthy    {stillAirworthy,3} of {trials}");
        Console.WriteLine($"    forced down        {trials - stillAirworthy,3} of {trials}  ({forcedDown:F0} %)");
        Console.Write("  hits landed on: ");
        foreach (var kv in componentTally.OrderByDescending(k => k.Value)) Console.Write($"{kv.Key} x{kv.Value}  ");
        Console.WriteLine();

        if (componentTally.Count == 0) return "forty-five seconds in two envelopes produced no hits at all";
        if (tookDamage < trials) return $"{trials - tookDamage} aircraft walked out of two envelopes untouched";
        if (componentTally.Count < 4) return "hits only ever landed on a handful of components";
        // Dangerous but survivable: a gate, not a punishment.
        if (forcedDown < 5) return $"only {forcedDown:F0} percent were forced down - the threat has no teeth";
        if (forcedDown > 80) return $"{forcedDown:F0} percent were forced down - that is punishment, not a gate";
        return null;
    }

    public static string? RouteInflation()
    {
        // The claim from D-003b that justifies building any of this: going around makes the
        // map bigger. Two routes between the same two points, one straight through a SAM
        // envelope and one that stays outside it. Both endpoints are outside the envelope,
        // or the comparison means nothing - which is what was wrong with the first version.
        ThreatEmitter site = ThreatField.Make(1, "The Scald SAM", ThreatKind.Sam, 0, 0);
        double r = site.DetectionRange;

        double fromN = -r - 500, toN = r + 500;

        var direct = new ThreatField();
        direct.Add(site);
        double directLen = 0, directExposure = 0, directSeconds = 0;
        const int steps = 700;
        for (int i = 0; i <= steps; i++)
        {
            double f = i / (double)steps;
            double n = fromN + (toN - fromN) * f;
            double len = (toN - fromN) / steps;
            direct.Update(n, 0, 300, 0, 55, len / 55.0, _ => true);
            directExposure = Math.Max(directExposure, direct.Exposure);
            if (direct.Exposure > 0.45) directSeconds += len / 55.0;
            directLen += len;
        }

        var around = new ThreatField();
        around.Add(site);
        double aroundLen = 0, aroundExposure = 0, aroundSeconds = 0;
        double standoff = r + 900;
        double prevN = fromN, prevE = 0;
        for (int i = 0; i <= steps; i++)
        {
            // A half circle at standoff radius: what "go around it" means on flat ground
            // with nothing to hide behind.
            double ang = Math.PI * i / steps - Math.PI / 2;
            double n = Math.Sin(ang) * standoff;
            double e = Math.Cos(ang) * standoff;
            double len = Math.Sqrt((n - prevN) * (n - prevN) + (e - prevE) * (e - prevE));
            prevN = n; prevE = e;
            around.Update(n, e, 300, 0, 55, Math.Max(len, 1e-3) / 55.0, _ => true);
            aroundExposure = Math.Max(aroundExposure, around.Exposure);
            if (around.Exposure > 0.45) aroundSeconds += len / 55.0;
            aroundLen += len;
        }

        double inflation = aroundLen / directLen;
        Console.WriteLine($"  a {r / 1000:F0} km SAM sitting between two points {directLen / 1000:F1} km apart");
        Console.WriteLine();
        Console.WriteLine("   route      length    peak exposure   seconds under threat");
        Console.WriteLine($"   straight  {directLen / 1000,6:F1} km {directExposure,14:F2} {directSeconds,20:F0}");
        Console.WriteLine($"   around    {aroundLen / 1000,6:F1} km {aroundExposure,14:F2} {aroundSeconds,20:F0}");
        Console.WriteLine();
        Console.WriteLine($"  route inflation {inflation:F2}x, on flat ground with nothing to hide behind.");
        Console.WriteLine("  With terrain the detour is shorter and the map is still bigger.");

        if (directExposure < 0.9) return "the direct route through a SAM envelope was not dangerous";
        if (aroundExposure > 0.2) return $"going around still reached {aroundExposure:F2} exposure";
        if (inflation < 1.3) return $"the detour only inflated the route {inflation:F2}x";
        return null;
    }
}
