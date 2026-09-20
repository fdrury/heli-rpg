using System;
using System.Collections.Generic;
using System.Linq;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does knocking a place down mean anything, and does it stop meaning something?
///
/// An attrition model has two ways to be useless and they are opposites. If everything comes
/// back on a timer, violence is free and the world is a set of props that reset. If nothing
/// ever comes back, a long campaign grinds the map down to rubble and the player has removed
/// their own content. The numbers below sit between those, and these are what stop them
/// drifting either way.
/// </summary>
public static class RebuildTests
{
    private const double Day = 86400.0;

    /// <summary>A game day away from the site, for a settlement of this size.</summary>
    private static IReadOnlyList<int> DaysAway(SiteDamage d, double days, int workers)
        => d.Advance(days * Day, workers);

    public static string? PlacesComeBack()
    {
        var problems = new List<string>();

        // A settlement of fifty, with four houses flattened.
        const int Population = 50;
        var d = new SiteDamage();
        for (int i = 0; i < 4; i++) d.Level(i, SiteDamage.HouseWorkDays);

        Console.WriteLine($"  four houses down, {Population} people, checking back each game day");
        Console.WriteLine("     day   ruins left   oldest ruin");
        double day = 0;
        int guard = 0;
        while (d.Ruins.Count > 0 && guard++ < 200)
        {
            day += 1;
            DaysAway(d, 1, d.Workers(Population));
            double frac = d.Ruins.Count > 0
                ? d.RebuildFraction(d.Ruins[0], SiteDamage.HouseWorkDays) : 1.0;
            if (day <= 6 || d.Ruins.Count == 0 || day % 5 == 0)
                Console.WriteLine($"    {day,5:F0}   {d.Ruins.Count,10}   {frac * 100,10:F0}%");
        }
        Console.WriteLine($"  the village was whole again after {day:F0} game days " +
                          $"({day * 24 * 60 / 30:F0} minutes of play)");

        if (d.Ruins.Count > 0) problems.Add("a village of fifty never finished rebuilding four houses");
        if (day < 4) problems.Add($"four houses went back up in {day:F0} game days - that is a respawn");
        if (day > 60) problems.Add($"four houses took {day:F0} game days - the player will never see it");

        // Fred's requirement: coming back part-way through has to SHOW something. One day
        // after levelling it, the first house must be visibly under way and not finished.
        var partial = new SiteDamage();
        for (int i = 0; i < 4; i++) partial.Level(i, SiteDamage.HouseWorkDays);
        DaysAway(partial, 1, partial.Workers(Population));
        double first = partial.RebuildFraction(partial.Ruins[0], SiteDamage.HouseWorkDays);
        Console.WriteLine($"  one game day later the first house is {first * 100:F0}% up");
        if (first <= 0.02)
            problems.Add($"a day later the first house is {first * 100:F0}% built - " +
                         "a return visit shows the player nothing");
        if (first >= 0.98)
            problems.Add("a day later the house is finished - there is no construction to catch");

        // And they do them ONE AT A TIME. From the air, twelve houses at 8% is twelve
        // ruins; one house at 90% is a village rebuilding.
        int started = partial.Ruins.Count(i => partial.RebuildFraction(i, SiteDamage.HouseWorkDays) > 0.001);
        Console.WriteLine($"  {started} of {partial.Ruins.Count} ruins have any work in them");
        if (started > 1)
            problems.Add($"{started} ruins are part-built at once - from the air that reads " +
                         "as rubble everywhere rather than as a place rebuilding");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with how places come back";
    }

    public static string? EmptyPlacesStayDown()
    {
        var problems = new List<string>();

        Console.WriteLine("  the same four houses, against how many people are left");
        Console.WriteLine("    survivors   days to rebuild all four");
        foreach (int survivors in new[] { 50, 25, 10, 3, 0 })
        {
            var d = new SiteDamage();
            for (int i = 0; i < 4; i++) d.Level(i, SiteDamage.HouseWorkDays);
            d.LosePeople(50 - survivors);

            double day = 0;
            while (d.Ruins.Count > 0 && day < 400) { day += 1; DaysAway(d, 1, d.Workers(50)); }
            Console.WriteLine($"    {survivors,9}   " +
                              (d.Ruins.Count > 0 ? "never" : $"{day:F0}"));

            if (survivors == 0 && d.Ruins.Count == 0)
                problems.Add("a site with nobody left rebuilt itself - " +
                             "the labour has to come from somewhere");
        }

        // Halving the population must roughly double the time, or population is decoration.
        double Time(int survivors)
        {
            var d = new SiteDamage();
            d.Level(0, SiteDamage.HouseWorkDays);
            d.LosePeople(50 - survivors);
            double day = 0;
            while (d.Ruins.Count > 0 && day < 400) { day += 1; DaysAway(d, 1, d.Workers(50)); }
            return day;
        }
        double full = Time(50), half = Time(25);
        Console.WriteLine($"  one house: {full:F0} days at fifty people, {half:F0} at twenty-five");
        if (half < full * 1.5)
            problems.Add($"halving the population changed the rebuild from {full:F0} to " +
                         $"{half:F0} days - killing people is not costing the place anything");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s): population is not the budget";
    }

    public static string? NobodyWatchesAWallGrow()
    {
        // The whole of the proximity rule is that the caller stops feeding time in. What
        // this checks is that the model has no hidden clock of its own - that it advances
        // on the seconds it is GIVEN and on nothing else - because if it reads a clock
        // internally, "halt while proximate" is unimplementable from outside.
        var watched = new SiteDamage();
        watched.Level(0, SiteDamage.HouseWorkDays);

        var away = new SiteDamage();
        away.Level(0, SiteDamage.HouseWorkDays);

        // Ten days pass for both. Only one of them is told about it.
        DaysAway(away, 10, 50);

        double w = watched.RebuildFraction(0, SiteDamage.HouseWorkDays);
        double a = away.RebuildFraction(0, SiteDamage.HouseWorkDays);
        Console.WriteLine($"  hovering over it for ten days: {w * 100:F0}% built");
        Console.WriteLine($"  ten days away:                 {a * 100:F0}% built");

        if (w > 0.001)
            return "a ruin rebuilt itself while the player was watching - the model has its " +
                   "own clock and the proximity rule cannot be applied from outside";
        if (a <= 0.001)
            return "ten days of work put nothing into the ruin";
        return null;
    }

    public static string? TheCousins()
    {
        var problems = new List<string>();
        var firstNames = new[] { "Sal", "Mick", "Jen", "Drew", "Kit", "Nora", "Ruth", "Corin" };

        Console.WriteLine("  killing the same job four times over, in a place of thirty");
        string name = "Del Fincher";
        double killedAt = 100 * Day;
        for (int ordinal = 1; ordinal <= 4; ordinal++)
        {
            bool tooSoon = Replacement.Ready(killedAt, killedAt + Day, 30);
            bool ready = Replacement.Ready(killedAt, killedAt + Replacement.DelaySeconds, 30);
            string next = Replacement.NameFor(name, ordinal, firstNames);
            Console.WriteLine($"    {name,-16} -> {next,-16}  {Replacement.Describe("Fincher", ordinal)}");

            if (tooSoon) problems.Add("a replacement turned up the day after - that is a respawn");
            if (!ready) problems.Add($"nobody took the job over after " +
                                     $"{Replacement.DelaySeconds / Day:F0} days with thirty people left");
            if (!next.EndsWith("Fincher", StringComparison.Ordinal))
                problems.Add($"the replacement for {name} was {next} - the surname is the joke " +
                             "and the information");
            name = next;
        }

        // Deterministic: a save reloads the same cousin, not a new one.
        string a = Replacement.NameFor("Del Fincher", 2, firstNames);
        string b = Replacement.NameFor("Del Fincher", 2, firstNames);
        if (a != b) problems.Add($"the same replacement resolved to {a} then {b}");

        // And it runs out. An emptied settlement has no cousins left.
        if (Replacement.Ready(killedAt, killedAt + 99 * Day, 0))
            problems.Add("somebody took over the workshop in a settlement with nobody left in it");
        Console.WriteLine("  with nobody left alive: nobody takes it on");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with who turns up next";
    }
}
