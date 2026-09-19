using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Headless test for save/load: fly to a site, land, shut down, create
/// distinctive state, save, trash the state, load, and verify.
///
///     godot --headless --path game -- --savetest
/// </summary>
public sealed partial class SaveTest : Node
{
    private readonly Main _main;
    private readonly HelicopterController _heli;
    private readonly SiteInteraction _play;
    private readonly LandingController _landing;
    private readonly SiteStreamer _sites;
    private readonly Sidearm _sidearm;
    private readonly RotorTime _rt;
    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private Site? _target;
    private int _phase;
    private double _time, _total;
    private readonly System.Collections.Generic.List<string> _failures = new();

    // State captured at save time, for comparison after load.
    private SaveData? _saved;
    private double _savedFuel;
    private double _savedClock;
    private double _savedScrap;
    private int _savedKnowledgeCount;

    public SaveTest(Main main, HelicopterController heli, SiteInteraction play,
                    LandingController landing, SiteStreamer sites,
                    Sidearm sidearm, RotorTime rt)
    {
        _main = main; _heli = heli; _play = play;
        _landing = landing; _sites = sites; _sidearm = sidearm; _rt = rt;
    }

    public override void _Ready()
    {
        GD.Print("=== Save/load test ==================================================");

        var start = new Vector2(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        _target = WorldMap.Nearest(start, SiteKind.FuelCache);
        if (_target is null) { Fail("no fuel cache found"); Finish(); return; }

        float dist = start.DistanceTo(_target.Position);
        GD.Print($"  target: {_target.Name} ({_target.Kind}), {dist:F0} m away");

        var approach = _target.Position + (start - _target.Position).Normalized() * 300f;
        float h = WorldHeight.At(approach.X, approach.Y);
        _heli.TeleportTo(new Vector3(approach.X, h + 120f, approach.Y),
                         Mathf.Atan2(_target.Position.X - approach.X, -(_target.Position.Y - approach.Y)));
        _heli.Sim.Fuel = 200;
        _heli.Sim.InvalidateMass();

        _rt.Charge = 0.6f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target is null) return;
        _time += delta;
        _total += delta;

        var sim = _heli.Sim;
        Vector3 p = _heli.GlobalPosition;
        var flat = new Vector2(p.X, p.Z);
        float range = flat.DistanceTo(_target.Position);

        switch (_phase)
        {
            // ---- 0: fly to overhead ---
            case 0:
            {
                float ground = WorldHeight.At(_target.Position.X, _target.Position.Y);
                var demand = new AutopilotDemand
                {
                    Altitude = ground + 70,
                    ForwardSpeed = Mathf.Clamp(range * 0.22f, 2f, 26f),
                    LateralSpeed = 0,
                    Heading = HeadingTo(flat, _target.Position),
                };
                _heli.OverrideControls = _ap.Update(sim, demand, delta);

                if (range < 20f && sim.Telemetry.AirspeedTrue < 5)
                {
                    GD.Print($"  overhead at {_time:F0} s");
                    Next();
                }
                else if (_time > 120) { Fail("could not reach site"); Finish(); }
                break;
            }

            // ---- 1: land ---
            case 1:
            {
                var demand = new AutopilotDemand
                {
                    VerticalSpeed = _heli.HeightAgl() > 25 ? -3.0 : -1.1,
                    ForwardSpeed = 0,
                    LateralSpeed = 0,
                    Heading = sim.State.Orientation.Yaw,
                };
                _heli.OverrideControls = _ap.Update(sim, demand, delta);

                if (_landing.OnGround)
                {
                    GD.Print($"  touchdown at {_time:F0} s");
                    Next();
                }
                else if (_time > 90) { Fail("could not land"); Finish(); }
                break;
            }

            // ---- 2: shut down ---
            case 2:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (sim.Telemetry.RotorRpmPercent < 65)
                {
                    GD.Print($"  shut down at {_time:F0} s");
                    Next();
                }
                else if (_time > 90) { Fail("rotor would not wind down"); Finish(); }
                break;
            }

            // ---- 3: create distinctive state, then save ---
            case 3:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_time < 0.5) break;

                // Make the state distinctive so we can verify it survived the round trip.
                _play.Progress.Add(Stock.Scrap, 99);
                _play.Progress.Add(Stock.Ammunition, 42);
                _play.Progress.Learn(new Knowledge(KnowledgeKind.Chart, "chart.test",
                    "Test Chart", "Created by the save test."));
                _play.Progress.Journal("Save test marker entry.");

                sim.Damage.Apply(Component.Engine, 0.2, DamageCause.Wear, "test wear");
                sim.Fuel = 175;
                sim.InvalidateMass();

                _sidearm.State.Rounds = 3;
                _sidearm.State.SpareRounds = 9;
                _sidearm.PilotHp.TakeDamage(20);
                _rt.Charge = 0.42f;

                // Snapshot values for comparison.
                _savedFuel = sim.Fuel;
                _savedClock = _play.Progress.Clock;
                _savedScrap = _play.Progress.Amount(Stock.Scrap);
                _savedKnowledgeCount = _play.Progress.Known.Count;

                // Save.
                _saved = _main.CaptureState();
                string json = _saved.ToJson();
                GD.Print($"  saved ({json.Length} bytes, fuel={_savedFuel:F0}, " +
                         $"scrap={_savedScrap:F0}, knowledge={_savedKnowledgeCount})");

                Next();
                break;
            }

            // ---- 4: trash the state ---
            case 4:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_time < 0.3) break;

                // Wreck everything so the load has work to do.
                sim.Fuel = 50;
                sim.InvalidateMass();
                sim.Damage.RepairAll();
                _play.Progress.Add(Stock.Scrap, 500);
                _sidearm.State.Rounds = 6;
                _sidearm.State.SpareRounds = 12;
                _sidearm.PilotHp.Reset();
                _rt.Charge = 1.0f;

                GD.Print($"  trashed state (fuel={sim.Fuel:F0}, " +
                         $"scrap={_play.Progress.Amount(Stock.Scrap):F0}, " +
                         $"engine health={sim.Damage.Health(Component.Engine):F2})");

                Next();
                break;
            }

            // ---- 5: load the save ---
            case 5:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_time < 0.3) break;

                if (_saved is null) { Fail("no saved data"); Finish(); break; }

                // Round-trip through JSON to test serialization.
                string json = _saved.ToJson();
                var reloaded = SaveData.FromJson(json);
                if (reloaded is null) { Fail("JSON deserialisation failed"); Finish(); break; }

                _main.ApplyState(reloaded);
                GD.Print("  loaded save");

                Next();
                break;
            }

            // ---- 6: verify restored state ---
            case 6:
            {
                if (_time < 0.5) break; // give a tick for physics to settle

                // Fuel
                double fuel = sim.Fuel;
                if (Math.Abs(fuel - _savedFuel) > 1)
                    Fail($"fuel: expected {_savedFuel:F0}, got {fuel:F0}");
                else
                    GD.Print($"  fuel OK: {fuel:F0}");

                // Clock
                double clock = _play.Progress.Clock;
                if (Math.Abs(clock - _savedClock) > 60) // allow some clock advance during load
                    Fail($"clock: expected ~{_savedClock:F0}, got {clock:F0}");
                else
                    GD.Print($"  clock OK: {clock:F0}");

                // Scrap
                double scrap = _play.Progress.Amount(Stock.Scrap);
                if (Math.Abs(scrap - _savedScrap) > 1)
                    Fail($"scrap: expected {_savedScrap:F0}, got {scrap:F0}");
                else
                    GD.Print($"  scrap OK: {scrap:F0}");

                // Knowledge
                int knowCount = _play.Progress.Known.Count;
                if (knowCount != _savedKnowledgeCount)
                    Fail($"knowledge: expected {_savedKnowledgeCount}, got {knowCount}");
                else
                    GD.Print($"  knowledge OK: {knowCount}");

                if (!_play.Progress.Knows("chart.test"))
                    Fail("missing test chart knowledge");

                // Damage
                double engineHealth = sim.Damage.Health(Component.Engine);
                if (Math.Abs(engineHealth - 0.8) > 0.05)
                    Fail($"engine health: expected ~0.8, got {engineHealth:F2}");
                else
                    GD.Print($"  engine health OK: {engineHealth:F2}");

                // Sidearm
                if (_sidearm.State.Rounds != 3)
                    Fail($"sidearm rounds: expected 3, got {_sidearm.State.Rounds}");
                if (_sidearm.State.SpareRounds != 9)
                    Fail($"sidearm spare: expected 9, got {_sidearm.State.SpareRounds}");

                // Pilot health
                float pilotHp = _sidearm.PilotHp.Health;
                if (Math.Abs(pilotHp - 80) > 1)
                    Fail($"pilot health: expected ~80, got {pilotHp:F0}");
                else
                    GD.Print($"  pilot health OK: {pilotHp:F0}");

                // Rotor Time charge
                if (Math.Abs(_rt.Charge - 0.42f) > 0.02f)
                    Fail($"rotor time: expected ~0.42, got {_rt.Charge:F2}");
                else
                    GD.Print($"  rotor time OK: {_rt.Charge:F2}");

                // Position: aircraft should be near the site
                Vector3 pos = _heli.GlobalPosition;
                float posRange = new Vector2(pos.X, pos.Z).DistanceTo(_target.Position);
                if (posRange > 100)
                    Fail($"position: {posRange:F0} m from site (should be < 100)");
                else
                    GD.Print($"  position OK: {posRange:F0} m from site");

                // Journal: should contain test marker
                bool foundMarker = false;
                foreach (var entry in _play.Progress.Journal_)
                    if (entry.Contains("Save test marker")) foundMarker = true;
                if (!foundMarker)
                    Fail("journal missing save test marker");
                else
                    GD.Print($"  journal OK: {_play.Progress.Journal_.Count} entries");

                Finish();
                break;
            }
        }
    }

    private static float HeadingTo(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        return Mathf.Atan2(d.X, -d.Y);
    }

    private void Next() { _phase++; _time = 0; _ap.Reset(); }
    private void Fail(string why) { _failures.Add(why); GD.PrintErr("  FAIL  " + why); }

    private void Finish()
    {
        GD.Print($"  total {_total:F0} s");
        GD.Print("=====================================================================");
        if (_failures.Count == 0) { GD.Print("  SAVE TEST PASSED"); GetTree().Quit(0); }
        else { GD.PrintErr($"  SAVE TEST FAILED ({_failures.Count})"); GetTree().Quit(1); }
    }
}
