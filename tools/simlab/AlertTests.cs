using System;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does being seen actually cost anything, and does it stop costing eventually?
///
/// An alert model has two ways to be useless and they are opposites. If it decays fast it
/// is a nuisance you wait out in a hover, and nobody plans around it. If it decays slowly
/// and stacks without limit it becomes a permanent tax that only ever goes up, and the
/// player's correct move is to stop flying. The numbers below are chosen to sit between
/// those, and these tests are what stop them drifting to either side.
/// </summary>
public static class AlertTests
{
    private const double Hour = 3600.0;

    public static string? RiseAndDecay()
    {
        var alert = new AlertState();
        const int region = 3;

        // Two minutes of being watched on a single pass.
        for (double t = 0; t < 120; t += 0.1) alert.Detected(region, 0.1);
        double afterPass = alert.Level(region);

        alert.Engaged(region);
        double afterShot = alert.Level(region);

        Console.WriteLine("  one region, over a couple of days");
        Console.WriteLine($"    after a two-minute overflight   {afterPass:F3}  ({AlertState.Describe(afterPass)})");
        Console.WriteLine($"    after being shot at             {afterShot:F3}  ({AlertState.Describe(afterShot)})");

        Console.WriteLine("       elapsed      level");
        double[] checkpoints = { 1, 3, 6, 12, 24, 48 };
        double last = afterShot;
        foreach (double hours in checkpoints)
        {
            var a2 = new AlertState();
            for (double t = 0; t < 120; t += 0.1) a2.Detected(region, 0.1);
            a2.Engaged(region);
            a2.Decay(hours * Hour);
            double v = a2.Level(region);
            Console.WriteLine($"    {hours,6:F0} h     {v:F3}  ({AlertState.Describe(v)})");
            if (v > last + 1e-9) return "alert rose while nothing was happening";
            last = v;
        }

        if (afterPass < 0.05)
            return $"a two-minute overflight barely registers ({afterPass:F3}) - " +
                   "being seen has to mean something";
        if (afterShot <= afterPass)
            return "being shot at did not raise readiness above merely being seen";

        // Six-hour half life: a night's rest should roughly halve it, a full day should
        // largely clear it. If it does not, the system is a permanent tax.
        var overnight = new AlertState();
        for (double t = 0; t < 120; t += 0.1) overnight.Detected(region, 0.1);
        overnight.Engaged(region);
        double before = overnight.Level(region);
        overnight.Decay(24 * Hour);
        double after = overnight.Level(region);
        if (after > before * 0.15)
            return $"a full day only took it from {before:F3} to {after:F3} - " +
                   "this is a tax, not a memory";

        return null;
    }

    public static string? Saturates()
    {
        // Twenty passes against five. If twenty is four times worse, the model has no top
        // end and repeated exposure becomes unbounded punishment.
        double Passes(int n)
        {
            var a = new AlertState();
            for (int i = 0; i < n; i++)
            {
                for (double t = 0; t < 120; t += 0.1) a.Detected(1, 0.1);
                a.Engaged(1);
            }
            return a.Level(1);
        }

        double five = Passes(5), twenty = Passes(20);
        Console.WriteLine($"  five passes {five:F3}, twenty passes {twenty:F3}");

        if (twenty <= five)
            return "more exposure did not raise readiness at all";
        if (twenty > five * 1.6)
            return $"readiness is not saturating ({five:F3} -> {twenty:F3}); " +
                   "repeated exposure becomes unbounded punishment";
        return null;
    }

    public static string? StaysLocal()
    {
        var alert = new AlertState();
        for (double t = 0; t < 180; t += 0.1) alert.Detected(2, 0.1);
        alert.Engaged(2);

        Console.WriteLine($"  stirred up region 2: {alert.Level(2):F3}, " +
                          $"region 5 meanwhile: {alert.Level(5):F3}");

        if (alert.Level(5) > 0.001)
            return "being seen in one region raised readiness in another - " +
                   "one mistake must not arm the whole world";
        return null;
    }

    public static string? EffectsAreBounded()
    {
        var alert = new AlertState();
        for (int i = 0; i < 40; i++)
        {
            for (double t = 0; t < 120; t += 0.1) alert.Detected(7, 0.1);
            alert.Engaged(7);
        }
        double full = alert.Level(7);

        double det = alert.DetectionScale(7);
        double react = alert.ReactionScale(7);
        Console.WriteLine($"  at full readiness ({full:F3}): detection x{det:F2}, reaction x{react:F2}");

        // Detection range is level design - the envelopes decide which country is crossable
        // at which altitude. Alert may bend that; it must not redraw it.
        if (det > 1.35)
            return $"alert stretches detection range by x{det:F2}, which quietly redraws " +
                   "routes the player has already learned";
        if (react > 0.95)
            return "alert does not speed up reaction at all, so it costs the player nothing " +
                   "where it matters";
        if (react < 0.35)
            return $"reaction time falls to x{react:F2} - masking behind terrain stops " +
                   "working entirely, which removes the skill the gate is built on";

        return null;
    }
}
