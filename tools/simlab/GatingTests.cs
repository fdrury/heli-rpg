using System;
using System.Collections.Generic;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does the countermeasure progression actually gate the world?
///
/// D-010 makes air defence the gating system: better-guarded country is meant to open up as
/// the player finds chaff, flares, a radar warning receiver. That is a load-bearing design
/// claim — it is what gives the map an order and what makes a salvaged dispenser worth
/// flying a long way for — and **nothing was testing it**. `--threatreport` measures how
/// much cover the terrain gives, which is a different question: it says where you can hide,
/// not what happens when you cannot.
///
/// So: fly a fixed gauntlet, vary only the fit, and count what gets through.
///
/// The gauntlet is synthetic rather than taken from the real map, deliberately. A corridor
/// with a known mix of emitters isolates the countermeasures; sampling the real world mixes
/// in terrain masking, route choice and where the sites happen to have landed, and then a
/// change in any of those looks like a change in the countermeasures.
/// </summary>
public static class GatingTests
{
    /// <summary>
    /// A 16 km corridor with the same mix of emitters the real world places: roughly equal
    /// guns and MANPADS with a smaller number of radar SAMs, staggered either side of track.
    /// </summary>
    private static List<ThreatEmitter> Gauntlet()
    {
        var list = new List<ThreatEmitter>();
        int id = 1;
        // north is downrange, east is across track.
        (double n, double e, ThreatKind k)[] layout =
        {
            (1500,  -900, ThreatKind.Gun),
            (3000,   800, ThreatKind.Manpads),
            (4200, -1400, ThreatKind.Gun),
            (5600,  1100, ThreatKind.Sam),
            (7000,  -700, ThreatKind.Manpads),
            (8300,  1300, ThreatKind.Gun),
            (9700,  -500, ThreatKind.Manpads),
            (11000,  900, ThreatKind.Sam),
            (12400, -1200, ThreatKind.Gun),
            (13800,  600, ThreatKind.Manpads),
        };
        foreach ((double n, double e, ThreatKind k) in layout)
            list.Add(ThreatField.Make(id, $"{k} {id++}", k, n, e));
        return list;
    }

    /// <summary>
    /// One transit. Returns how much damage got through, and how many times it was hit.
    ///
    /// The countermeasure policy is deliberately simple and slightly dim: dispense when
    /// something locks on, with a cooldown so it cannot spam. A perfect policy would
    /// measure the dispenser rather than the fit, and the question here is what the FIT is
    /// worth to an ordinary pilot.
    /// </summary>
    private static (double damage, int hits, int launches) Transit(
        Countermeasure fit, double altitudeAgl, double speed, bool terrainCover)
    {
        var field = new ThreatField(seed: 4242) { Fitted = fit };
        foreach (ThreatEmitter e in Gauntlet()) field.Add(e);

        if (fit.HasFlag(Countermeasure.Chaff)) field.ChaffRemaining = 30;
        if (fit.HasFlag(Countermeasure.Flares)) field.FlaresRemaining = 30;

        double damage = 0;
        int hits = 0, launches = 0;
        bool sawLaunch = false;
        field.Struck += ev => { if (ev.Severity > 0) { damage += ev.Severity; hits++; } };
        // A launch is visible - smoke off the rail, in daylight, close. That is how a crew
        // with no warning receiver knows anything at all about a MANPADS.
        field.Launch += _ => { launches++; sawLaunch = true; };

        // Terrain cover as a simple proxy: down low, an emitter can only see you some of
        // the time. The real number from --threatreport is 63% visibility at 50 m and 95%
        // at 400 m, so this is calibrated to that rather than invented.
        var rng = new Random(99);
        double visible = terrainCover
            ? (altitudeAgl < 100 ? 0.63 : altitudeAgl < 300 ? 0.84 : 0.95)
            : 1.0;

        const double dt = 0.1;
        double north = 0;
        double cooldown = 0;

        for (double t = 0; north < 16000; t += dt)
        {
            north += speed * dt;
            cooldown -= dt;

            field.Update(north, 0, altitudeAgl, 0, speed, dt,
                         _ => rng.NextDouble() < visible);

            // Dispense the RIGHT countermeasure for what is actually known about.
            //
            // The first version fired everything at every known lock, and a fit of flares
            // plus a warning receiver came out WORSE than flares alone (18.80 against
            // 14.24) - the receiver told the crew about radar threats, and they spent their
            // flares, which do nothing to a radar missile, and had none left for the one
            // that mattered. Realistic as a mistake, useless as a measurement of the fit.
            bool radarLock = false;
            foreach (ThreatTrack tr in field.Tracks)
                if (field.Perceivable(tr) && tr.Emitter.RadarGuided &&
                    (tr.State == TrackState.Locked || tr.State == TrackState.Engaging))
                    radarLock = true;

            if (cooldown > 0) { sawLaunch = false; }
            else
            {
                bool used = false;
                if (radarLock && fit.HasFlag(Countermeasure.Chaff)) used |= field.DispenseChaff();
                if (sawLaunch && fit.HasFlag(Countermeasure.Flares)) used |= field.DispenseFlares();
                sawLaunch = false;
                if (used) cooldown = 2.0;
            }
        }

        return (damage, hits, launches);
    }

    public static string? CountermeasureGating()
    {
        Console.WriteLine("  one 16 km gauntlet at 250 m, 60 m/s, terrain cover off");
        Console.WriteLine("    fit                            launches   hits   damage");

        (string label, Countermeasure fit)[] fits =
        {
            ("nothing", Countermeasure.None),
            ("flares", Countermeasure.Flares),
            ("flares + RWR", Countermeasure.Flares | Countermeasure.RadarWarning),
            // Chaff with no receiver: the crew never learns a radar has locked them, so
            // the chaff stays in the bucket. This row is the whole argument for why the
            // two are a PAIR rather than two separate upgrades.
            ("chaff + flares, no RWR", Countermeasure.Chaff | Countermeasure.Flares),
            ("chaff + flares + RWR", Countermeasure.Chaff | Countermeasure.Flares |
                                     Countermeasure.RadarWarning),
            ("everything", Countermeasure.Chaff | Countermeasure.Flares |
                           Countermeasure.RadarWarning | Countermeasure.Jammer |
                           Countermeasure.ExhaustSuppressor),
        };

        var damages = new List<(string label, double damage)>();
        foreach ((string label, Countermeasure fit) in fits)
        {
            (double damage, int hits, int launches) = Transit(fit, 250, 60, terrainCover: false);
            Console.WriteLine($"    {label,-28}  {launches,7}  {hits,5}   {damage,6:F2}");
            damages.Add((label, damage));
        }

        Console.WriteLine();
        double bare = damages[0].damage;
        double best = damages[^1].damage;
        Console.WriteLine($"  a full fit takes {(bare > 0 ? (1 - best / bare) : 0):P0} " +
                          "less damage through the same gauntlet than nothing at all");

        if (bare <= 0.01)
            return "an unprotected aircraft flew a gauntlet of ten emitters unharmed - " +
                   "the threat system is not threatening";
        // The pairing: chaff is worth nothing without the receiver that tells you to use
        // it, and the receiver is worth nothing without the chaff. If either half helps on
        // its own, the dependency the progression is built on has quietly gone away.
        double flaresOnly = damages.Find(d => d.label == "flares").damage;
        double chaffNoRwr = damages.Find(d => d.label == "chaff + flares, no RWR").damage;
        double chaffRwr = damages.Find(d => d.label == "chaff + flares + RWR").damage;
        Console.WriteLine($"  chaff without a receiver: {chaffNoRwr:F2} vs {flaresOnly:F2} " +
                          $"for flares alone. With one: {chaffRwr:F2}.");
        if (chaffNoRwr < flaresOnly * 0.9)
            Console.WriteLine("  NOTE: chaff is helping without a receiver - check Perceivable().");

        if (best >= bare * 0.75)
            return $"a full countermeasure fit barely helps ({bare:F2} -> {best:F2} damage); " +
                   "D-010 rests on countermeasures opening up guarded country, and they do not";

        return null;
    }

    public static string? AltitudeGating()
    {
        Console.WriteLine("  the other half of the gate: how low you fly, with terrain cover on");
        Console.WriteLine("    altitude      nothing        full fit");

        string? failure = null;
        double lowBare = 0, highBare = 0;

        foreach (double alt in new[] { 50.0, 150.0, 400.0, 900.0 })
        {
            (double bare, _, _) = Transit(Countermeasure.None, alt, 60, terrainCover: true);
            (double full, _, _) = Transit(
                Countermeasure.Chaff | Countermeasure.Flares | Countermeasure.RadarWarning |
                Countermeasure.Jammer | Countermeasure.ExhaustSuppressor,
                alt, 60, terrainCover: true);

            Console.WriteLine($"    {alt,5:F0} m      {bare,7:F2}        {full,7:F2}");
            if (Math.Abs(alt - 50) < 1) lowBare = bare;
            if (Math.Abs(alt - 900) < 1) highBare = bare;
        }

        Console.WriteLine();
        Console.WriteLine("  (400 m and 900 m read the same because the cover proxy saturates at 0.95;");
        Console.WriteLine("   distinguishing them needs the real terrain, which --threatreport does.)");
        Console.WriteLine("  Flying low should be a real alternative to carrying countermeasures -");
        Console.WriteLine("  that is the trade the whole world layout is built on (D-010, D-012).");

        // If altitude does not matter, the terrain masking measured by --threatreport is
        // decoration and there is no reason ever to fly low.
        if (lowBare >= highBare * 0.8)
            failure ??= $"flying at 50 m is barely safer than 900 m ({lowBare:F2} vs " +
                        $"{highBare:F2} damage) - terrain masking is not buying anything";

        return failure;
    }
}
