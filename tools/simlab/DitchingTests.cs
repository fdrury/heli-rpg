using System;
using System.Collections.Generic;
using System.Linq;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does going in the water actually cost anything?
///
/// D-059 spread the world into islands on one argument: over land an engine failure is an
/// autorotation into a field and a walk home, over water it is a swim and the aircraft is
/// gone. D-077 then made 85% of the envelope sea on the strength of that sentence. Nothing
/// implemented it - water was a landing surface that scored zero and threw spray instead of
/// dust, and since the visual water has no collider, an aircraft that descended at sea fell
/// through the surface, sat on the seabed, and lifted off again.
///
/// These are the checks that stop that coming back. The headline one is the last: after a
/// ditching the aircraft must not be airworthy, because if it is, every crossing in the
/// world is free and the archipelago is decoration.
/// </summary>
public static class DitchingTests
{
    private static DamageState Wreck(Ditching.Report r)
    {
        var d = new DamageState();
        foreach ((Component part, double amount, string why) in Ditching.DamageFrom(r))
            d.Apply(part, amount, DamageCause.Impact, why);
        return d;
    }

    public static string? IntoTheWater()
    {
        var problems = new List<string>();

        // A textbook ditching: wings level, low rate of descent, nearly stopped, and the
        // rotor still turning because the engine was running until the moment it went in.
        Ditching.Report controlled = Ditching.Evaluate(
            depth: 0.8, verticalSpeed: 1.5, groundSpeed: 4.0, rotorFraction: 1.0);

        // And the other one.
        Ditching.Report hard = Ditching.Evaluate(
            depth: 2.5, verticalSpeed: 12.0, groundSpeed: 45.0, rotorFraction: 1.0);

        // An autorotation that ran out of options over the sea, flared, and stopped the
        // rotor before it touched. This is the best case that exists.
        Ditching.Report stopped = Ditching.Evaluate(
            depth: 0.6, verticalSpeed: 2.0, groundSpeed: 2.0, rotorFraction: 0.05);

        Console.WriteLine("  three ways to end up in the sea");
        foreach ((string label, Ditching.Report r) in new[]
                 { ("under control, rotor turning", controlled),
                   ("hard and fast", hard),
                   ("rotor stopped first", stopped) })
        {
            DamageState d = Wreck(r);
            Console.WriteLine($"    {label,-30} {(r.Survivable ? "walked away" : "did not")}" +
                              $"   rotor {d.Health(Component.MainRotor) * 100,5:F0}%" +
                              $"   engine {d.Health(Component.Engine) * 100,5:F0}%" +
                              $"   airworthy {(d.Airworthy ? "YES" : "no")}");
            Console.WriteLine($"        {r.Summary}");
        }
        Console.WriteLine();

        // --- 1. the crew ------------------------------------------------------
        if (!controlled.Survivable)
            problems.Add("a controlled ditching killed the crew - flying it into the water " +
                         "properly has to be survivable or there is no skill in it");
        if (hard.Survivable)
            problems.Add("arriving at 12 m/s and 45 m/s across was survivable - water does " +
                         "not compress at that rate, and if it did there would be no reason " +
                         "to fly the ditching at all");

        // --- 2. the rotor -----------------------------------------------------
        if (!controlled.RotorDestroyed)
            problems.Add("a turning rotor went into the water and survived");
        if (stopped.RotorDestroyed)
            problems.Add("a rotor at 5% Nr tore itself off - below the energy threshold it " +
                         "should simply stop");

        // A stopped rotor is the one thing the pilot can influence, so it has to be worth
        // something measurable. If it is not, the distinction is flavour text.
        double turningRotor = Wreck(controlled).Health(Component.MainRotor);
        double stoppedRotor = Wreck(stopped).Health(Component.MainRotor);
        Console.WriteLine($"  stopping the rotor first leaves it at {stoppedRotor * 100:F0}% " +
                          $"rather than {turningRotor * 100:F0}%");
        if (stoppedRotor <= turningRotor + 0.2)
            problems.Add($"stopping the rotor before the water bought {(stoppedRotor - turningRotor) * 100:F0} " +
                         "points of blade condition - the only decision available in a " +
                         "ditching has no consequence");

        // --- 3. the aircraft --------------------------------------------------
        //
        // This is the one that matters. If ANY ditching leaves a flyable aircraft, the
        // archipelago is scenery: you cross, you go in, you carry on.
        foreach ((string label, Ditching.Report r) in new[]
                 { ("under control", controlled), ("hard", hard), ("rotor stopped", stopped) })
        {
            DamageState d = Wreck(r);
            if (d.Airworthy)
                problems.Add($"a {label} ditching left the aircraft AIRWORTHY - " +
                             "every water crossing in the world is free");
        }

        // The engine specifically: an intake under water ingests it, and no arrival is
        // gentle enough to avoid that.
        double bestEngine = new[] { controlled, hard, stopped }.Max(r => Wreck(r).Health(Component.Engine));
        Console.WriteLine($"  the best the engine survives any ditching is {bestEngine * 100:F0}%");
        if (bestEngine > 0.30)
            problems.Add($"an engine came out of the water at {bestEngine * 100:F0}% - " +
                         "a submerged intake ingests water however well you flew it");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s) with what the water costs";
    }

    /// <summary>
    /// A ditching is a submersion, not a very hard landing, and the damage has to say so.
    ///
    /// If the two produce the same shape of damage then one of them is wrong, and the one
    /// that is wrong is the ditching - a hard landing is a shock load up through the skids
    /// and the mast, and going in the water is everything below the waterline at once,
    /// which on a helicopter is everything.
    /// </summary>
    public static string? NotJustAHardLanding()
    {
        Ditching.Report r = Ditching.Evaluate(0.8, 1.5, 4.0, 1.0);
        DamageState wet = Wreck(r);

        // The worst survivable arrival on land, for comparison.
        var dry = new DamageState();
        dry.Apply(Component.Skids, 0.9, DamageCause.HardLanding, "gear collapse");
        dry.Apply(Component.Transmission, 0.9 * 0.35, DamageCause.HardLanding, "");
        dry.Apply(Component.Fuselage, 0.9 * 0.45, DamageCause.HardLanding, "");

        Console.WriteLine("  component        after a bad landing   after a gentle ditching");
        var problems = new List<string>();
        foreach (Component c in new[] { Component.Skids, Component.Engine, Component.Avionics,
                                        Component.Hydraulics, Component.MainRotor })
            Console.WriteLine($"    {c,-14} {dry.Health(c) * 100,12:F0}% {wet.Health(c) * 100,20:F0}%");

        // The tell: a hard landing does not touch the avionics or the engine at all, and a
        // ditching ruins both. Skids are the opposite way round.
        if (wet.Health(Component.Avionics) > 0.05)
            problems.Add("the avionics survived a submersion");
        if (wet.Health(Component.Engine) >= dry.Health(Component.Engine))
            problems.Add("the engine came out of the water no worse than off a heavy landing - " +
                         "the two events are being modelled as the same thing");
        if (wet.Health(Component.Skids) < dry.Health(Component.Skids))
            problems.Add("the skids took more damage from water than from a gear collapse, " +
                         "which is the shock-load shape rather than the submersion one");

        if (problems.Count == 0) return null;
        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return $"{problems.Count} problem(s): a ditching is being modelled as a hard landing";
    }
}
