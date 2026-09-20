using System;
using System.Collections.Generic;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Can the gun pod level a building, does the attrition model pick it up, and does the
/// announcer hear about it?
///
/// These are the sim-layer side of D-087. The game layer is where the round checks site
/// proximity and decides which structure to hit; what lives here is the constants, the
/// CanStrafe filter, the workday classification, and the round-trip through the rebuild
/// and deed systems.
/// </summary>
public static class BuildingGunneryTests
{
    /// <summary>Eight rounds on target levels one structure.</summary>
    public static string? HitsToLevel()
    {
        var problems = new List<string>();

        var d = new SiteDamage();
        Console.WriteLine($"  HitsToLevel = {BuildingGunnery.HitsToLevel}");

        // Simulate a strafing run: 20 rounds on a settlement with 10 structures.
        // At 8 rounds per building, that should level 2 structures.
        int levelled = 0;
        int hits = 0;
        for (int round = 0; round < 20; round++)
        {
            hits++;
            if (hits % BuildingGunnery.HitsToLevel != 0) continue;

            // Find the next standing structure.
            for (int i = 0; i < 10; i++)
            {
                if (d.IsRuined(i)) continue;
                d.Level(i, SiteDamage.HouseWorkDays);
                d.LosePeople(BuildingGunnery.CasualtiesPerBuilding);
                levelled++;
                break;
            }
        }

        Console.WriteLine($"  20 rounds on target -> {levelled} structures levelled, " +
                          $"{d.PopulationLost} casualties");

        if (levelled != 2)
            problems.Add($"20 rounds levelled {levelled} structures, expected 2");
        if (d.PopulationLost != 2 * BuildingGunnery.CasualtiesPerBuilding)
            problems.Add($"casualties = {d.PopulationLost}, expected " +
                         $"{2 * BuildingGunnery.CasualtiesPerBuilding}");
        if (d.Ruins.Count != 2)
            problems.Add($"{d.Ruins.Count} ruins, expected 2");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with hits-to-level";
    }

    /// <summary>Wrecks, fuel caches, and overlooks cannot be strafed.</summary>
    public static string? SafeKinds()
    {
        var problems = new List<string>();
        var yes = new[] { SiteKindTag.Settlement, SiteKindTag.Farmstead, SiteKindTag.Workshop,
                          SiteKindTag.Depot, SiteKindTag.Airfield, SiteKindTag.Relay };
        var no  = new[] { SiteKindTag.FuelCache, SiteKindTag.Wreck, SiteKindTag.Overlook };

        foreach (var k in yes)
            if (!BuildingGunnery.CanStrafe(k))
                problems.Add($"{k} is not strafeable but has buildings");
        foreach (var k in no)
            if (BuildingGunnery.CanStrafe(k))
                problems.Add($"{k} is strafeable but should not be");

        Console.Write("  strafeable: ");
        foreach (var k in yes) Console.Write($"{k} ");
        Console.WriteLine();
        Console.Write("  protected:  ");
        foreach (var k in no) Console.Write($"{k} ");
        Console.WriteLine();

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with strafeable site kinds";
    }

    /// <summary>The work-days classification puts masts and water towers at TallWorkDays.</summary>
    public static string? WorkDayClassification()
    {
        var problems = new List<string>();

        // Settlement: last structure is a water tower.
        double wt = BuildingGunnery.WorkDays(SiteKindTag.Settlement, 9, 10);
        double house = BuildingGunnery.WorkDays(SiteKindTag.Settlement, 3, 10);
        Console.WriteLine($"  settlement: house = {house} days, water tower (last) = {wt} days");
        if (wt != SiteDamage.TallWorkDays)
            problems.Add($"water tower at {wt} work-days, expected {SiteDamage.TallWorkDays}");
        if (house != SiteDamage.HouseWorkDays)
            problems.Add($"house at {house} work-days, expected {SiteDamage.HouseWorkDays}");

        // Relay: first structure is a mast.
        double mast = BuildingGunnery.WorkDays(SiteKindTag.Relay, 0, 2);
        double shack = BuildingGunnery.WorkDays(SiteKindTag.Relay, 1, 2);
        Console.WriteLine($"  relay: mast (first) = {mast} days, shack = {shack} days");
        if (mast != SiteDamage.TallWorkDays)
            problems.Add($"mast at {mast} work-days, expected {SiteDamage.TallWorkDays}");
        if (shack != SiteDamage.HouseWorkDays)
            problems.Add($"relay shack at {shack} work-days, expected {SiteDamage.HouseWorkDays}");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with work-day classification";
    }

    /// <summary>
    /// A levelled site feeds into the rebuild system and comes back to life.
    ///
    /// This ties the gun pod to the attrition model end-to-end: strafe, damage, population
    /// loss, rebuild, and the rebuild rate reflects how many people are left.
    /// </summary>
    public static string? StrafeThenRebuild()
    {
        var problems = new List<string>();
        const int Population = 50;
        const double Day = 86400.0;

        // Strafe a settlement: level 3 buildings, kill 6 people.
        var d = new SiteDamage();
        for (int i = 0; i < 3; i++)
        {
            d.Level(i, SiteDamage.HouseWorkDays);
            d.LosePeople(BuildingGunnery.CasualtiesPerBuilding);
        }

        int workers = d.Workers(Population);
        Console.WriteLine($"  3 structures down, {d.PopulationLost} dead, " +
                          $"{workers} workers left");

        // Let it rebuild.
        double day = 0;
        while (d.Ruins.Count > 0 && day < 200)
        {
            day += 1;
            d.Advance(Day, d.Workers(Population));
        }
        Console.WriteLine($"  rebuilt after {day:F0} game days with {workers} workers");

        if (d.Ruins.Count > 0)
            problems.Add("the site never finished rebuilding");
        if (day < 3)
            problems.Add($"three buildings rebuilt in {day:F0} days - too fast");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with strafe-then-rebuild";
    }

    /// <summary>The Levelled deed reaches the announcer with correct newsworthiness.</summary>
    public static string? DeedReachesAnnouncer()
    {
        var problems = new List<string>();

        double nw = RadioDj.Newsworthiness(DjDeedKind.Levelled);
        Console.WriteLine($"  Levelled newsworthiness: {nw:F1}");

        // It should be the most newsworthy thing — strafing a settlement is bigger than
        // anything else the player has done.
        double rescued = RadioDj.Newsworthiness(DjDeedKind.Rescued);
        if (nw <= rescued)
            problems.Add($"Levelled ({nw}) is not more newsworthy than Rescued ({rescued})");

        // The deed score should be high for a fresh deed.
        var deed = new DjDeed(DjDeedKind.Levelled, 1000.0, "Long Acre", 2, 30);
        double score = RadioDj.DeedScore(deed, 1000.0, 0);
        Console.WriteLine($"  fresh deed score: {score:F2}");
        if (score <= 0)
            problems.Add("a fresh Levelled deed scored zero");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with the announcer deed";
    }

    /// <summary>Damage from strafing survives a save/load round-trip.</summary>
    public static string? StrafeDamageSaves()
    {
        var problems = new List<string>();

        var progress = Progress.NewGame();
        progress.Clock = 1000.0;

        // Strafe: level 2 structures, kill 4 people.
        var d = progress.Damage(42);
        d.Level(0, SiteDamage.HouseWorkDays);
        d.Level(3, SiteDamage.TallWorkDays);
        d.LosePeople(4);

        // Record the deed.
        progress.RecordDeed(DjDeedKind.Levelled, "Salt Sink", 2, 15);

        // Save and load.
        var save = new SaveData();
        save.CaptureProgress(progress);
        string json = System.Text.Json.JsonSerializer.Serialize(save);
        SaveData? back = System.Text.Json.JsonSerializer.Deserialize<SaveData>(json);
        if (back is null) return "the save did not deserialise";
        Progress loaded = back.ApplyProgress();

        // Check damage round-trip.
        var dl = loaded.DamageOrNull(42);
        if (dl is null)
        {
            problems.Add("damage for site 42 was lost in the save");
        }
        else
        {
            if (!dl.IsRuined(0)) problems.Add("structure 0 not ruined after load");
            if (!dl.IsRuined(3)) problems.Add("structure 3 not ruined after load");
            if (dl.IsRuined(1)) problems.Add("structure 1 ruined after load (never levelled)");
            if (dl.PopulationLost != 4) problems.Add($"population lost = {dl.PopulationLost}, expected 4");
        }

        // Check deed round-trip.
        bool foundDeed = false;
        foreach (var deed in loaded.Deeds)
            if (deed.Kind == DjDeedKind.Levelled && deed.Place == "Salt Sink")
                foundDeed = true;
        if (!foundDeed) problems.Add("the Levelled deed was lost in the save");

        Console.WriteLine($"  round-trip: {(problems.Count == 0 ? "clean" : problems.Count + " problems")}");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with save round-trip";
    }
}
