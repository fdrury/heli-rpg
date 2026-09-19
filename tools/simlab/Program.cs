using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Headless flight-test bench.
///
/// Every claim the flight model makes - it can hover, it gains lift entering
/// translational lift, it will autorotate, it will settle in its own downwash - is
/// checked here without opening the game. Run it after touching anything in sim/.
///
///   dotnet run --project tools/simlab -- [scenario]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string scenario = args.Length > 0 ? args[0] : "all";
        var failures = new List<string>();

        void Run(string name, Func<string?> test)
        {
            if (scenario != "all" && scenario != name) return;
            Console.WriteLine();
            Console.WriteLine($"=== {name} ".PadRight(74, '='));
            try
            {
                string? err = test();
                if (err is null) Console.WriteLine($"  PASS  {name}");
                else { Console.WriteLine($"  FAIL  {name}: {err}"); failures.Add($"{name}: {err}"); }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR {name}: {ex.Message}");
                failures.Add($"{name}: {ex.Message}");
            }
        }

        Run("cyclicbench", CyclicBench.Step);
        Run("trim", TrimTests.TrimSweep);
        Run("handsoff", TrimTests.HandsOff);
        Run("poshold", TrimTests.PositionHold);
        Run("envelope", EnvelopeTests.Performance);
        Run("autoglide", EnvelopeTests.Autorotation);
        Run("sassweep", TrimTests.SasSweep);
        Run("ratesens", TrimTests.RateSensitivity);
        Run("audiowave", AudioTests.Waveform);
        Run("bladepass", AudioTests.BladePass);
        Run("audiostate", AudioTests.RespondsToState);
        Run("weatheraudio", AudioTests.WeatherAudio);
        Run("solar", WeatherTests.Solar);
        Run("weather", WeatherTests.Conditions);
        Run("windeffect", WeatherTests.WindEffect);
        Run("freetrace", Trace.FreeTrace);
        Run("hovertrace", Trace.HoverTrace);
        Run("rotorbench", RotorBench.Sweep);
        Run("spinup", RotorBench.SpinUp);
        Run("massprops", Scenarios.MassProperties);
        Run("hover", Scenarios.HoverTrim);
        Run("powercurve", Scenarios.PowerCurve);
        Run("etl", Scenarios.TranslationalLift);
        Run("groundeffect", Scenarios.GroundEffect);
        Run("cyclic", Scenarios.CyclicResponse);
        Run("pedal", Scenarios.PedalAuthority);
        Run("autorotation", Scenarios.Autorotation);
        Run("vrs", Scenarios.VortexRingState);
        Run("ceiling", Scenarios.ServiceCeiling);
        Run("landinggeom", LandingTests.Geometry);
        Run("touchdown", LandingTests.Touchdowns);
        Run("brownout", LandingTests.Brownout);
        Run("damage", LandingTests.DamageEffects);
        Run("dialogue", DialogueTests.Selection);
        Run("repetition", DialogueTests.Repetition);
        Run("codagating", DialogueTests.CodaGating);
        Run("codavalidation", DialogueTests.Validation);
        Run("codastreaming", DialogueTests.Streaming);
        Run("npcmemory", DialogueTests.Memory);
        Run("loadout_catalog", LoadoutTests.CatalogConsistency);
        Run("loadout_mass", LoadoutTests.InstallMass);
        Run("loadout_cg", LoadoutTests.CgShift);
        Run("loadout_drag", LoadoutTests.DragEffect);
        Run("loadout_fuel", LoadoutTests.FuelCapacity);
        Run("loadout_full", LoadoutTests.FullLoadout);
        Run("loadout_lifecycle", LoadoutTests.BagAndInstall);
        Run("loadout_distribution", LoadoutTests.ModuleDistribution);
        Run("masking", ThreatTests.Masking);
        Run("altbands", ThreatTests.AltitudeBands);
        Run("countermeasures", ThreatTests.Countermeasures);
        Run("survivability", ThreatTests.Survivability);
        Run("routeinflation", ThreatTests.RouteInflation);
        Run("combat_zones", CombatTests.ZoneResolution);
        Run("combat_npc", CombatTests.NpcHealthEffects);
        Run("combat_attrition", CombatTests.NpcAttrition);
        Run("combat_sidearm", CombatTests.SidearmMechanics);
        Run("combat_pilot", CombatTests.PilotHealthCycle);
        Run("save_progress", SaveTests.ProgressRoundTrip);
        Run("save_damage", SaveTests.DamageRoundTrip);
        Run("save_loadout", SaveTests.LoadoutRoundTrip);
        Run("save_npc", SaveTests.NpcRoundTrip);
        Run("save_fog", SaveTests.FogRoundTrip);
        Run("save_full", SaveTests.FullRoundTrip);
        Run("audio", AudioRender.Render);
        Run("perf", Scenarios.Performance);

        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        if (failures.Count == 0)
        {
            Console.WriteLine("ALL CHECKS PASSED");
            return 0;
        }
        Console.WriteLine($"{failures.Count} CHECK(S) FAILED:");
        foreach (var f in failures) Console.WriteLine("  - " + f);
        return 1;
    }
}
