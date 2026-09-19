using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Tests the on-foot system headless: land, shut down, dismount, walk around,
/// activate Rotor Time, walk back, board, and lift off.
///
///     godot --headless --path game -- --foottest
/// </summary>
public sealed partial class FootTest : Node
{
    private readonly Main _main;
    private readonly HelicopterController _heli;
    private readonly SiteInteraction _play;
    private readonly LandingController _landing;
    private readonly SiteStreamer _sites;
    private readonly PilotController _pilot;
    private readonly RotorTime _rt;
    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private Site? _target;
    private int _phase;
    private int _frame;
    private double _time, _total;
    private Vector3 _dismountPos;
    private readonly System.Collections.Generic.List<string> _failures = new();

    public FootTest(Main main, HelicopterController heli, SiteInteraction play,
                    LandingController landing, SiteStreamer sites,
                    PilotController pilot, RotorTime rt)
    {
        _main = main; _heli = heli; _play = play;
        _landing = landing; _sites = sites; _pilot = pilot; _rt = rt;
    }

    public override void _Ready()
    {
        GD.Print("=== On-foot test ====================================================");

        var start = new Vector2(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        _target = WorldMap.Nearest(start, SiteKind.Settlement);
        if (_target is null) { Fail("no settlement found"); Finish(); return; }

        float dist = start.DistanceTo(_target.Position);
        GD.Print($"  target: {_target.Name} ({_target.Kind}), {dist:F0} m away");

        // Start nearby, well above terrain — same pattern as LoopTest.
        var approach = _target.Position + (start - _target.Position).Normalized() * 300f;
        float h = WorldHeight.At(approach.X, approach.Y);
        _heli.TeleportTo(new Vector3(approach.X, h + 120f, approach.Y),
                         Mathf.Atan2(_target.Position.X - approach.X, -(_target.Position.Y - approach.Y)));
        _heli.Sim.Fuel = 200;
        _heli.Sim.InvalidateMass();

        // Give some RT charge for testing.
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

        // Where is the pilot, and is there anything under them? Sampled on every phase,
        // because sampling only during the walk-back showed a pilot already sixteen metres
        // below the terrain and a ray that consequently hit nothing - which says where the
        // fall ENDED, not where it started.
        if (_pilot is not null && _pilot.IsInsideTree() && _frame++ % 90 == 0)
        {
            Vector3 fp = _pilot.GlobalPosition;
            var space = _pilot.GetWorld3D().DirectSpaceState;
            // Start the ray ABOVE the terrain, not above the PILOT. Anchoring it to the
            // pilot means that once they are below the surface the ray begins underground
            // and reports "NOTHING" - which says nothing at all about whether ground exists.
            float gh = WorldHeight.At(fp.X, fp.Z);
            var from = new Vector3(fp.X, gh + 25f, fp.Z);
            var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 120f);
            q.Exclude = new Godot.Collections.Array<Rid> { _pilot.GetRid() };
            var hit = space.IntersectRay(q);
            string what = hit.Count > 0
                ? $"{((Node)hit["collider"]).Name} at y={((Vector3)hit["position"]).Y:F1} " +
                  $"layer={(hit["collider"].As<CollisionObject3D>()?.CollisionLayer ?? 0)}"
                : "NOTHING";
            GD.Print($"  [foot] phase {_phase} y={fp.Y:F1} terrain={WorldHeight.At(fp.X, fp.Z):F1} " +
                     $"onFloor={_pilot.IsOnFloor()} pilotMask={_pilot.CollisionMask} below={what}");
        }

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
                    GD.Print($"  shut down at {_time:F0} s, Nr {sim.Telemetry.RotorRpmPercent:F0}%");
                    Next();
                }
                else if (_time > 90) { Fail("rotor would not wind down"); Finish(); }
                break;
            }

            // ---- 3: dismount ---
            case 3:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_time < 0.5) break;

                _main.ForceDismount();
                _dismountPos = _pilot.GlobalPosition;
                GD.Print($"  dismounted at {_dismountPos}, mode={_main.Mode}");

                if (_main.Mode != GameMode.OnFoot)
                    Fail($"expected OnFoot mode, got {_main.Mode}");
                if (!_pilot.Visible)
                    Fail("pilot is not visible after dismount");

                Next();
                break;
            }

            // ---- 4: walk away (simulate WASD by moving directly) ---
            case 4:
            {
                // Headless test: we cannot send real key events, so we move the pilot
                // directly by setting its velocity.
                Vector3 walkDir = (_pilot.GlobalTransform.Basis * Vector3.Forward).Normalized();
                _pilot.WalkToward(walkDir, 3f, delta);

                float walked = _pilot.GlobalPosition.DistanceTo(_dismountPos);
                if (walked > 8f)
                {
                    GD.Print($"  walked {walked:F1} m from helicopter");
                    Next();
                }
                else if (_time > 15) { Fail($"only walked {walked:F1} m in 15 seconds"); Finish(); }
                break;
            }

            // ---- 5: activate Rotor Time ---
            case 5:
            {
                if (_time < 0.1f) break;

                float chargeBefore = _rt.Charge;
                bool activated = _rt.TryActivate();
                GD.Print($"  Rotor Time: charge={chargeBefore:F2}, activated={activated}, " +
                         $"timeScale={Engine.TimeScale:F2}");

                if (!activated) Fail("could not activate Rotor Time");
                if (!_rt.Active) Fail("Rotor Time is not active after activation");
                if (Math.Abs(Engine.TimeScale - _rt.SlowScale) > 0.01)
                    Fail($"Engine.TimeScale is {Engine.TimeScale:F2}, expected {_rt.SlowScale:F2}");

                // Let it run for a moment then deactivate.
                Next();
                break;
            }

            // ---- 6: let RT drain briefly, then deactivate ---
            case 6:
            {
                _rt.UpdateDrain(delta);
                if (_time > 0.5)
                {
                    float chargeNow = _rt.Charge;
                    _rt.Deactivate();
                    GD.Print($"  Rotor Time deactivated, charge now {chargeNow:F2}, " +
                             $"timeScale restored to {Engine.TimeScale:F2}");

                    if (Math.Abs(Engine.TimeScale - 1.0) > 0.01)
                        Fail($"Engine.TimeScale not restored: {Engine.TimeScale:F2}");

                    Next();
                }
                break;
            }

            // ---- 7: walk back toward helicopter ---
            case 7:
            {
                Vector3 toHeli = (_heli.GlobalPosition - _pilot.GlobalPosition).Normalized();
                _pilot.WalkToward(toHeli, 4f, delta);

                float dist2 = _pilot.DistanceTo(_heli.GlobalPosition);

                if (_pilot.GlobalPosition.Y < _heli.GlobalPosition.Y - 200f)
                {
                    Fail($"pilot fell through the world: {_pilot.GlobalPosition.Y:F0} m " +
                         $"against a helicopter at {_heli.GlobalPosition.Y:F0} m");
                    Finish();
                    break;
                }
                if (dist2 < 6f)
                {
                    GD.Print($"  back near helicopter, dist={dist2:F1} m");
                    Next();
                }
                else if (_time > 20)
                {
                    Fail($"could not reach helicopter, dist={dist2:F1} m  " +
                         $"(pilot {_pilot.GlobalPosition}, heli {_heli.GlobalPosition}, " +
                         $"heli sleeping={_heli.Sleeping}, frozen={_heli.Freeze})");
                    Finish();
                }
                break;
            }

            // ---- 8: board ---
            case 8:
            {
                if (_time < 0.2f) break;
                _main.ForceBoard();
                GD.Print($"  boarded, mode={_main.Mode}");

                if (_main.Mode != GameMode.Flying)
                    Fail($"expected Flying mode, got {_main.Mode}");
                if (_pilot.Visible)
                    Fail("pilot is still visible after boarding");

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
        // Safety: always restore time scale.
        if (_rt.Active) _rt.Deactivate();

        GD.Print($"  total {_total:F0} s");
        GD.Print("=====================================================================");
        if (_failures.Count == 0) { GD.Print("  ON-FOOT TEST PASSED"); GetTree().Quit(0); }
        else { GD.PrintErr($"  ON-FOOT TEST FAILED ({_failures.Count})"); GetTree().Quit(1); }
    }
}
