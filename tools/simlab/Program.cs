using System.Reflection;
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

        var registered = new HashSet<string>();

        void Run(string name, Func<string?> test)
        {
            // Record what has been registered, so the sweep at the bottom can prove that
            // nothing has been written and then quietly forgotten.
            registered.Add($"{test.Method.DeclaringType?.Name}.{test.Method.Name}");

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
        Run("range_crossings", EnvelopeTests.CrossingRange);
        Run("rollsense", EnvelopeTests.RollSenseAtAltitude);
        Run("autoglide", EnvelopeTests.Autorotation);
        Run("autotrim", EnvelopeTests.AutorotationTrim);
        Run("autobalance", EnvelopeTests.AutorotationBalance);
        Run("radialsweep", EnvelopeTests.RadialInflowSweep);
        Run("driving", EnvelopeTests.DrivingRegion);
        Run("alertrise", AlertTests.RiseAndDecay);
        Run("alertsat", AlertTests.Saturates);
        Run("alertlocal", AlertTests.StaysLocal);
        Run("alerteffect", AlertTests.EffectsAreBounded);
        Run("warn_thresholds", WarningTests.Thresholds);
        Run("warn_hysteresis", WarningTests.Hysteresis);
        Run("warn_latch", WarningTests.Latching);
        Run("warn_priority", WarningTests.Priority);
        Run("warn_tones", WarningTests.Tones);
        Run("warn_inflight", WarningTests.InFlight);
        Run("xmsn_oil", DamageCascadeTests.TransmissionOilLoss);
        Run("xmsn_shutdown", DamageCascadeTests.ShutdownStopsTheClock);
        Run("hyd_leak", DamageCascadeTests.HydraulicLeak);
        Run("hot_section", DamageCascadeTests.EngineHotSection);
        Run("cascade_flight", DamageCascadeTests.InFlightCascade);
        Run("warning_panel", DamageCascadeTests.WarningPanelLadder);
        Run("gating", GatingTests.CountermeasureGating);
        Run("altgating", GatingTests.AltitudeGating);
        Run("dragk", EnvelopeTests.DragKSweep);
        Run("assist", TrimTests.AssistLadder);
        Run("sassweep", TrimTests.SasSweep);
        Run("ratesens", TrimTests.RateSensitivity);
        Run("audiowave", AudioTests.Waveform);
        Run("bladepass", AudioTests.BladePass);
        Run("audiostate", AudioTests.RespondsToState);
        Run("weatheraudio", AudioTests.WeatherAudio);
        Run("voicewave", VoiceTests.Waveform);
        Run("voicehiss", VoiceTests.CarrierHiss);
        Run("voicelevel", VoiceTests.VoiceAboveHiss);
        Run("voiceduration", VoiceTests.Duration);
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
        Run("salvage_catalog", SalvageTests.Catalog);
        Run("salvage_sites", SalvageTests.SiteCharacter);
        Run("salvage_wear", SalvageTests.ConditionCliff);
        Run("salvage_fit", SalvageTests.FitDecision);
        Run("salvage_weight", SalvageTests.WeightCost);
        Run("salvage_take", SalvageTests.WhatToTake);
        Run("salvage_tiers", SalvageTests.TierScarcity);
        Run("salvage_determinism", SalvageTests.Determinism);
        Run("masking", ThreatTests.Masking);
        Run("altbands", ThreatTests.AltitudeBands);
        Run("countermeasures", ThreatTests.Countermeasures);
        Run("survivability", ThreatTests.Survivability);
        Run("routeinflation", ThreatTests.RouteInflation);
        Run("citadel_ring", ThreatTests.CitadelRing);
        Run("combat_zones", CombatTests.ZoneResolution);
        Run("combat_npc", CombatTests.NpcHealthEffects);
        Run("combat_attrition", CombatTests.NpcAttrition);
        Run("combat_sidearm", CombatTests.SidearmMechanics);
        Run("combat_pilot", CombatTests.PilotHealthCycle);
        Run("encounter_tier0", EncounterTests.Tier0Safe);
        Run("encounter_safe", EncounterTests.SafeKinds);
        Run("encounter_hostile", EncounterTests.HostilesExist);
        Run("encounter_counts", EncounterTests.NpcCounts);
        Run("encounter_determ", EncounterTests.Determinism);
        Run("encounter_cleared", EncounterTests.ClearedRoundTrip);
        Run("contract_gen", ContractTests.Generation);
        Run("contract_complete", ContractTests.Completion);
        Run("contract_payout", ContractTests.Payout);
        Run("contract_delivery", ContractTests.DeliveryCompletion);
        Run("contract_roundtrip", ContractTests.RoundTrip);
        Run("search_advance", ContractTests.SearchAdvance);
        Run("search_legs", ContractTests.SearchLegs);
        Run("contract_determinism", ContractTests.Determinism);
        Run("contract_clear", ContractTests.ClearContract);
        Run("contract_danger_pay", ContractTests.DangerPay);
        Run("contract_recover_hostile", ContractTests.RecoverHostile);
        Run("contract_clear_roundtrip", ContractTests.ClearRoundTrip);
        Run("contract_integration", ContractTests.ProgressIntegration);
        Run("save_progress", SaveTests.ProgressRoundTrip);
        Run("save_damage", SaveTests.DamageRoundTrip);
        Run("save_loadout", SaveTests.LoadoutRoundTrip);
        Run("save_npc", SaveTests.NpcRoundTrip);
        Run("save_fog", SaveTests.FogRoundTrip);
        Run("save_alert", SaveTests.AlertRoundTrip);
        Run("save_full", SaveTests.FullRoundTrip);
        Run("salvage_hours", SalvageTests.FlightHours);
        Run("salvage_cargo", SalvageTests.CargoWeighs);
        Run("salvage_cargosave", SalvageTests.CargoSurvivesSave);
        Run("dialogue_long", DialogueTests.LongSession);
        Run("dialogue_generic", DialogueTests.GenericRegister);
        Run("dialogue_voices", DialogueTests.Voices);
        Run("dialogue_knowledge", DialogueTests.KnowledgeGating);
        Run("dialogue_premise", DialogueTests.PremiseGuard);
        Run("dialogue_rewards", DialogueTests.Rewards);
        Run("governor_droop", GovernorTests.Droop);
        Run("governor_degraded", GovernorTests.Degraded);
        Run("governor_manual", GovernorTests.ManualThrottle);
        Run("governor_cold", GovernorTests.ColdStart);
        Run("governor_hot", GovernorTests.HotStart);
        Run("governor_density", GovernorTests.DensityAltitude);
        Run("radio_reception", RadioTests.Reception);
        Run("radio_plays", RadioTests.KeepsPlaying);
        Run("radio_volume", RadioTests.VolumeStaysSet);
        Run("radio_band", RadioTests.Band);
        Run("radio_level", RadioTests.Levelling);
        Run("radio_ducking", RadioTests.Ducking);
        Run("radio_equipment", RadioTests.AsEquipment);
        Run("dj_volume", RadioDjTests.Volume);
        Run("dj_norepeat", RadioDjTests.EnoughToNotRepeat);
        Run("dj_seen", RadioDjTests.OnlySpeaksOfWhatWasSeen);
        Run("dj_alertband", RadioDjTests.MatchesTheAlertBand);
        Run("dj_world", RadioDjTests.ReactsToTheWorld);
        Run("dj_deeds", RadioDjTests.TalksAboutWhatYouDid);
        Run("dj_ledger", RadioDjTests.DeedLedger);
        Run("dj_newsworthy", RadioDjTests.TheInterestingOneWins);
        Run("dj_guard", RadioDjTests.PremiseAndAdviceGuard);
        Run("ditching", DitchingTests.IntoTheWater);
        Run("ditch_shape", DitchingTests.NotJustAHardLanding);
        Run("rebuild", RebuildTests.PlacesComeBack);
        Run("rebuild_empty", RebuildTests.EmptyPlacesStayDown);
        Run("rebuild_watched", RebuildTests.NobodyWatchesAWallGrow);
        Run("rebuild_cousins", RebuildTests.TheCousins);
        Run("rebuild_save", RebuildTests.DamageSurvivesSave);
        Run("strafe_hits", BuildingGunneryTests.HitsToLevel);
        Run("strafe_kinds", BuildingGunneryTests.SafeKinds);
        Run("strafe_work", BuildingGunneryTests.WorkDayClassification);
        Run("strafe_rebuild", BuildingGunneryTests.StrafeThenRebuild);
        Run("strafe_deed", BuildingGunneryTests.DeedReachesAnnouncer);
        Run("strafe_save", BuildingGunneryTests.StrafeDamageSaves);
        Run("search_guard", RadioDjTests.SearchThreadPremiseGuard);
        Run("dj_schedule", RadioDjTests.Scheduling);
        Run("dj_site", RadioDjTests.BroadcastSite);
        Run("dj_transcript", RadioDjTests.Transcript);
        Run("sling_pendulum", SlingTests.Pendulum);
        Run("sling_resonance", SlingTests.Resonance);
        Run("sling_hover", SlingTests.HoverCost);
        Run("sling_jettison", SlingTests.Jettison);
        Run("sling_bucket", SlingTests.Bucket);
        Run("gun_ammo", GunneryTests.AmmoDepletion);
        Run("gun_raypoint", GunneryTests.RayPointDistance);
        Run("gun_emitter", GunneryTests.EmitterDamage);
        Run("gun_destroyed", GunneryTests.DestroyedStopsTracking);
        Run("gun_save", GunneryTests.SaveRoundTrip);
        Run("gun_module", GunneryTests.GunPodModule);
        Run("acoustics_open", AcousticsTests.OpenGround);
        Run("acoustics_valley", AcousticsTests.Valley);
        Run("acoustics_water", AcousticsTests.OverWater);
        Run("acoustics_agl", AcousticsTests.HighAgl);
        Run("acoustics_ambience", AcousticsTests.SiteAmbience);
        Run("acoustics_wind", AcousticsTests.WindResponse);
        Run("nav_cardinals", NavigationTests.Cardinals);
        Run("nav_distance", NavigationTests.Distance);
        Run("nav_relbearing", NavigationTests.RelBearing);
        Run("nav_roundtrip", NavigationTests.RoundTrip);
        Run("ceiling_degrades", CeilingTests.Degrades);
        Run("ceiling_repair", CeilingTests.CapsRepair);
        Run("ceiling_repairall", CeilingTests.CapsRepairAll);
        Run("ceiling_reset", CeilingTests.Reset);
        Run("ceiling_save", CeilingTests.SaveRoundTrip);
        Run("strip_enqueue", RadioStripTests.Enqueue);
        Run("strip_wordreveal", RadioStripTests.WordReveal);
        Run("strip_hold", RadioStripTests.Hold);
        Run("strip_suppressed", RadioStripTests.Suppressed);
        Run("strip_queue", RadioStripTests.QueueOrder);
        Run("strip_voicebeat", RadioStripTests.VoiceBeat);
        Run("pax_mass", PassengerTests.WrayMass);
        Run("pax_cg", PassengerTests.CgShift);
        Run("pax_torque", PassengerTests.CalloutTorque);
        Run("pax_nr", PassengerTests.CalloutNr);
        Run("pax_alt", PassengerTests.CalloutAltitude);
        Run("pax_fuel", PassengerTests.CalloutFuel);
        Run("pax_save", PassengerTests.SaveRoundTrip);
        Run("pax_board", PassengerTests.BoardingDialogue);
        Run("bp_catalog", BladePairTests.Catalog);
        Run("bp_trim", BladePairTests.TrimEffect);
        Run("bp_ceiling", BladePairTests.CeilingCost);
        Run("bp_save", BladePairTests.SaveRoundTrip);
        Run("bp_finale", BladePairTests.Finale);
        Run("audio", AudioRender.Render);
        Run("perf", Scenarios.Performance);

        // --- Has anything been written and not wired up? ----------------------
        //
        // A test nobody runs is worse than no test: it looks like cover and provides none.
        // With several agents adding scenarios at once this stopped being hypothetical -
        // ten of them were sitting in the tree unregistered at one point, including the
        // entire salvage wear suite. Reflection over what EXISTS, checked against what was
        // registered above, makes that impossible to miss rather than something somebody
        // has to remember.
        if (scenario == "all")
        {
            var orphans = new List<string>();
            foreach (Type t in typeof(Program).Assembly.GetTypes())
            {
                if (!t.IsAbstract || !t.IsSealed) continue;              // static classes only
                if (t == typeof(Program)) continue;
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.ReturnType != typeof(string) || m.GetParameters().Length != 0) continue;
                    if (m.Name is "ToString") continue;
                    string id = $"{t.Name}.{m.Name}";
                    if (!registered.Contains(id)) orphans.Add(id);
                }
            }

            if (orphans.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"=== unregistered ".PadRight(74, '='));
                foreach (string o in orphans) Console.WriteLine($"  never runs: {o}");
                failures.Add($"{orphans.Count} scenario(s) exist but are not registered in Program.cs");
            }
        }

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
