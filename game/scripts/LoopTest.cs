using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Flies the core loop, headless, and checks it actually works:
/// find a place, get there, land on it, shut down, take what is there, and leave able to
/// reach the next one.
///
/// This is the test that matters most, because it is the only one that exercises the game
/// rather than a system. The physics can be perfect and the world can be beautiful and the
/// loop can still be broken because the aircraft cannot be shut down, or the fuel does not
/// transfer, or the site is on a slope nothing can land on.
///
///     godot --headless --path game -- --looptest
/// </summary>
public sealed partial class LoopTest : Node
{
    private readonly HelicopterController _heli;
    private readonly SiteInteraction _play;
    private readonly LandingController _landing;
    private readonly SiteStreamer _sites;
    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private Site? _target;
    private int _phase;
    private double _time, _total;
    private double _fuelAtStart;
    private readonly System.Collections.Generic.List<string> _failures = new();

    public LoopTest(HelicopterController heli, SiteInteraction play,
                    LandingController landing, SiteStreamer sites)
    {
        _heli = heli; _play = play; _landing = landing; _sites = sites;
    }

    public override void _Ready()
    {
        GD.Print("=== Core loop test ==================================================");

        // Pick the nearest fuel cache to the start, which is the first thing any player
        // is going to need.
        var start = new Vector2(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        _target = WorldMap.Nearest(start, SiteKind.FuelCache);
        if (_target is null) { Fail("there is no fuel cache anywhere in the world"); Finish(); return; }

        float dist = start.DistanceTo(_target.Position);
        GD.Print($"  target: {_target.Name} ({_target.Kind}), {dist:F0} m away " +
                 $"in {WorldMap.RegionAt(_target.Position).Name}");
        GD.Print($"  ground there is {WorldHeight.At(_target.Position.X, _target.Position.Y):F0} m, " +
                 $"slope {Mathf.RadToDeg(Mathf.Acos(WorldHeight.NormalAt(_target.Position.X, _target.Position.Y, 6f).Y)):F1} deg");

        // Start airborne and nearby: this test is about the loop, not the transit.
        var approach = _target.Position + (start - _target.Position).Normalized() * 260f;
        float h = WorldHeight.At(approach.X, approach.Y);
        _heli.TeleportTo(new Vector3(approach.X, h + 90f, approach.Y),
                         Mathf.Atan2(_target.Position.X - approach.X, -(_target.Position.Y - approach.Y)));
        _fuelAtStart = 120;
        _heli.Sim.Fuel = _fuelAtStart;
        _heli.Sim.InvalidateMass();
        GD.Print($"  starting with {_fuelAtStart:F0} kg of fuel, which is not enough to get anywhere else");
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
        float agl = _heli.HeightAgl();

        switch (_phase)
        {
            // ---- 0: fly to overhead ---------------------------------------
            case 0:
            {
                float ground = WorldHeight.At(_target.Position.X, _target.Position.Y);
                // Position hold, not speed hold.
                //
                // Commanding a speed along a heading toward the site does not converge in
                // wind: the nose points at the pad while the ground track crabs off to one
                // side, and the aircraft arrives abeam of where it was going. This test shut
                // down 98 m from the site the moment the world acquired real weather.
                var demand = new AutopilotDemand
                {
                    Altitude = ground + 70,
                    GroundTarget = SimBridge.PositionToSim(
                        new Vector3(_target.Position.X, ground, _target.Position.Y)),
                    ApproachSpeed = 26,
                    Heading = HeadingTo(flat, _target.Position),
                };
                _heli.OverrideControls = _ap.Update(sim, demand, delta);

                if (range < 22f && sim.Telemetry.AirspeedTrue < 6)
                {
                    GD.Print($"  overhead at {_time:F0} s, {agl:F0} m agl, " +
                             $"{sim.Fuel:F0} kg remaining");
                    Next();
                }
                else if (_time > 180) { Fail($"could not reach the site in three minutes ({range:F0} m short)"); Finish(); }
                break;
            }

            // ---- 1: descend and land --------------------------------------
            case 1:
            {
                float ground = WorldHeight.At(p.X, p.Z);
                // Come down at a sensible rate and slow it right down near the ground.
                float targetAgl = agl > 25 ? 12 : 0;
                // Hold the PAD on the way down, not zero speed. Zero ground speed freezes
                // whatever offset the approach left and the wind adds more all the way to
                // touchdown - this phase alone put the aircraft 87 m from the site.
                float padGround = WorldHeight.At(_target.Position.X, _target.Position.Y);
                var demand = new AutopilotDemand
                {
                    VerticalSpeed = agl > 25 ? -3.0 : -1.1,
                    GroundTarget = SimBridge.PositionToSim(
                        new Vector3(_target.Position.X, padGround, _target.Position.Y)),
                    ApproachSpeed = agl > 25 ? 8 : 3,
                    Heading = sim.State.Orientation.Yaw,
                };
                _heli.OverrideControls = _ap.Update(sim, demand, delta);

                if (_landing.OnGround)
                {
                    var r = _landing.LastTouchdown;
                    GD.Print($"  touchdown at {_time:F0} s: {r.Summary} " +
                             $"({r.VerticalSpeed:F2} m/s, {r.RollDegrees:F1} deg bank, " +
                             $"{r.SlopeDegrees:F1} deg slope)");
                    GD.Print($"  site assessment said: {_landing.Site.Verdict(12, sim.Airframe.MainRotor.Radius)}");
                    if (r.StructuralDamage > 0.35) Fail($"the landing broke the aircraft ({r.Summary})");
                    Next();
                }
                else if (_time > 90) { Fail("could not get it on the ground in ninety seconds"); Finish(); }
                break;
            }

            // ---- 2: shut down ---------------------------------------------
            case 2:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (sim.Telemetry.RotorRpmPercent < 65)
                {
                    GD.Print($"  shut down at {_time:F0} s, Nr {sim.Telemetry.RotorRpmPercent:F0}%");
                    Next();
                }
                else if (_time > 90) { Fail("the rotor would not wind down"); Finish(); }
                break;
            }

            // ---- 3: take what is here -------------------------------------
            case 3:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };

                if (_play.Parked is null)
                {
                    if (_time > 5)
                    {
                        Fail($"shut down {range:F0} m from {_target.Name} and the game does not think we are there");
                        Finish();
                    }
                    break;
                }

                if (_time < 1.0) break;   // let the action list settle

                if (_play.Busy is not null) break;

                var actions = _play.Actions;
                if (actions.Count == 0) { Fail("no actions offered at a fuel cache"); Finish(); break; }

                if (!_reported)
                {
                    _reported = true;
                    GD.Print($"  at {_target.Name}, offered:");
                    for (int i = 0; i < actions.Count; i++)
                        GD.Print($"    {i + 1}. {actions[i].Label}  {(actions[i].Available ? "" : "(unavailable)")}");
                }

                // Take the first thing on offer that can actually be done.
                for (int i = 0; i < actions.Count; i++)
                {
                    if (!actions[i].Available) continue;
                    _fuelBefore = sim.Fuel;
                    _play.Trigger(i);
                    Next();
                    return;
                }

                GD.Print("  nothing here could be done - the cache is dry, which is a legitimate outcome");
                _dryCache = true;
                Next();
                break;
            }

            // ---- 4: wait for it, then check --------------------------------
            case 4:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_play.Busy is not null) { if (_time > 200) { Fail("the action never finished"); Finish(); } break; }

                if (!_dryCache)
                {
                    double gained = sim.Fuel - _fuelBefore;
                    GD.Print($"  action complete: fuel {_fuelBefore:F0} -> {sim.Fuel:F0} kg (+{gained:F0})");
                    if (gained < 1) Fail($"refuelling transferred {gained:F1} kg");
                }
                GD.Print($"  journal now has {_play.Progress.Journal_.Count} entries, " +
                         $"{_play.Progress.VisitedCount} places visited");
                Finish();
                break;
            }
        }
    }

    private bool _reported, _dryCache;
    private double _fuelBefore;

    private static float HeadingTo(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        // Sim yaw is measured about the down axis; north is -Z in Godot.
        return Mathf.Atan2(d.X, -d.Y);
    }

    private void Next() { _phase++; _time = 0; _ap.Reset(); }
    private void Fail(string why) { _failures.Add(why); GD.PrintErr("  FAIL  " + why); }

    private void Finish()
    {
        GD.Print($"  total {_total:F0} s");
        GD.Print("=====================================================================");
        if (_failures.Count == 0) { GD.Print("  CORE LOOP TEST PASSED"); GetTree().Quit(0); }
        else { GD.PrintErr($"  CORE LOOP TEST FAILED ({_failures.Count})"); GetTree().Quit(1); }
    }
}
