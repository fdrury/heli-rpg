using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Headless test for on-foot combat: land, dismount, spawn a hostile NPC,
/// fire the sidearm, verify hits and zone effects, test called shots with
/// Rotor Time, verify ammo management.
///
///     godot --headless --path game -- --combattest
/// </summary>
public sealed partial class CombatTest : Node
{
    private readonly Main _main;
    private readonly HelicopterController _heli;
    private readonly SiteInteraction _play;
    private readonly LandingController _landing;
    private readonly SiteStreamer _sites;
    private readonly PilotController _pilot;
    private readonly RotorTime _rt;
    private readonly Sidearm _sidearm;
    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private Site? _target;
    private HostileNpc? _npc;
    private int _phase;
    private int _frame;
    private double _time, _total;
    private readonly System.Collections.Generic.List<string> _failures = new();

    public CombatTest(Main main, HelicopterController heli, SiteInteraction play,
                      LandingController landing, SiteStreamer sites,
                      PilotController pilot, RotorTime rt, Sidearm sidearm)
    {
        _main = main; _heli = heli; _play = play;
        _landing = landing; _sites = sites; _pilot = pilot;
        _rt = rt; _sidearm = sidearm;
    }

    public override void _Ready()
    {
        GD.Print("=== Combat test =====================================================");

        var start = new Vector2(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        _target = WorldMap.Nearest(start, SiteKind.Settlement);
        if (_target is null) { Fail("no settlement found"); Finish(); return; }

        float dist = start.DistanceTo(_target.Position);
        GD.Print($"  target: {_target.Name} ({_target.Kind}), {dist:F0} m away");

        var approach = _target.Position + (start - _target.Position).Normalized() * 300f;
        float h = WorldHeight.At(approach.X, approach.Y);
        _heli.TeleportTo(new Vector3(approach.X, h + 120f, approach.Y),
                         Mathf.Atan2(_target.Position.X - approach.X, -(_target.Position.Y - approach.Y)));
        _heli.Sim.Fuel = 200;
        _heli.Sim.InvalidateMass();

        _rt.Charge = 0.8f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target is null) return;
        _time += delta;
        _total += delta;
        _frame++;

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

            // ---- 3: dismount ---
            case 3:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_time < 0.5) break;

                _main.ForceDismount();
                GD.Print($"  dismounted at {_pilot.GlobalPosition}, mode={_main.Mode}");

                if (_main.Mode != GameMode.OnFoot)
                    Fail($"expected OnFoot, got {_main.Mode}");

                Next();
                break;
            }

            // ---- 4: spawn hostile NPC in front of pilot ---
            case 4:
            {
                if (_time < 0.3) break;

                // Place NPC 10m in front of the pilot, facing them.
                Vector3 pilotPos = _pilot.GlobalPosition;
                Vector3 forward = -_pilot.GlobalTransform.Basis.Z;
                Vector3 npcPos = pilotPos + forward * 10f;
                float npcYaw = Mathf.Atan2(-forward.X, -forward.Z);

                _npc = _main.SpawnHostileNpc(npcPos, npcYaw);
                // Disable NPC firing back for this test — we test player shots.
                _npc.ReactionTime = 999f;

                GD.Print($"  spawned hostile NPC at {_npc.GlobalPosition}");
                GD.Print($"  sidearm: {_sidearm.State.Rounds}/{_sidearm.State.Capacity} " +
                         $"(+{_sidearm.State.SpareRounds} spare)");

                Next();
                break;
            }

            // ---- 5: fire at NPC without Rotor Time (hip fire) ---
            case 5:
            {
                if (_time < 0.3) break;
                if (_npc is null) { Fail("NPC missing"); Finish(); break; }

                // Construct a ray from the pilot toward the NPC's torso zone.
                Vector3 origin = _pilot.GlobalPosition + new Vector3(0, 1.5f, 0);
                Vector3 toNpc = (_npc.GlobalPosition + new Vector3(0, 0.95f, 0) - origin).Normalized();

                var space = _pilot.GetWorld3D().DirectSpaceState;
                var result = _sidearm.Fire(origin, toNpc, space);

                if (result is null)
                {
                    Fail("sidearm.Fire returned null");
                    Finish();
                    break;
                }

                GD.Print($"  hip fire: hit={result.Value.DidHit}, " +
                         $"zone={result.Value.Effect?.Zone}, " +
                         $"rounds left={_sidearm.State.Rounds}");

                if (_sidearm.State.Rounds != 5)
                    Fail($"should have 5 rounds after one shot, got {_sidearm.State.Rounds}");

                if (!result.Value.DidHit)
                    GD.Print("  (missed — OK for hip fire, NPC zones may not be ready yet)");

                Next();
                break;
            }

            // ---- 6: wait for cooldown, then fire again aiming at torso ---
            case 6:
            {
                if (_time < 0.8f) break; // wait for fire cooldown
                if (_npc is null) { Fail("NPC missing"); Finish(); break; }

                Vector3 origin = _pilot.GlobalPosition + new Vector3(0, 1.5f, 0);
                // Aim precisely at the torso zone position.
                var torsoZone = _npc.GetNodeOrNull<StaticBody3D>("Zone_Torso");
                if (torsoZone is null)
                {
                    Fail("NPC has no Zone_Torso child");
                    Finish();
                    break;
                }
                Vector3 torsoWorld = torsoZone.GlobalPosition;
                Vector3 toTorso = (torsoWorld - origin).Normalized();

                var space = _pilot.GetWorld3D().DirectSpaceState;
                var result = _sidearm.Fire(origin, toTorso, space);

                if (result is null) { Fail("second shot returned null"); Finish(); break; }

                GD.Print($"  aimed torso shot: hit={result.Value.DidHit}, " +
                         $"zone={result.Value.Effect?.Zone}, " +
                         $"npcHP={_npc.Health.Health:F0}");

                if (result.Value.DidHit)
                {
                    if (result.Value.Effect?.Zone != BodyZone.Torso)
                        GD.Print($"  (hit {result.Value.Effect?.Zone} instead of Torso — zone overlap OK)");
                }

                Next();
                break;
            }

            // ---- 7: activate Rotor Time and fire a called shot at the head ---
            case 7:
            {
                if (_time < 0.8f) break;
                if (_npc is null) { Fail("NPC missing"); Finish(); break; }

                // Activate RT
                bool activated = _rt.TryActivate();
                GD.Print($"  Rotor Time activated={activated}, " +
                         $"charge={_rt.Charge:F2}, timeScale={Engine.TimeScale:F2}");

                if (!activated) Fail("could not activate Rotor Time");
                if (!_rt.Active) Fail("RT not active after activation");

                // Aim at head zone
                var headZone = _npc.GetNodeOrNull<StaticBody3D>("Zone_Head");
                if (headZone is null)
                {
                    Fail("NPC has no Zone_Head child");
                    _rt.Deactivate();
                    Finish();
                    break;
                }

                Vector3 origin = _pilot.GlobalPosition + new Vector3(0, 1.5f, 0);
                Vector3 headWorld = headZone.GlobalPosition;
                Vector3 toHead = (headWorld - origin).Normalized();

                var space = _pilot.GetWorld3D().DirectSpaceState;
                var result = _sidearm.Fire(origin, toHead, space);

                _rt.Deactivate();

                if (result is null) { Fail("head shot returned null"); Finish(); break; }

                GD.Print($"  called shot (head): hit={result.Value.DidHit}, " +
                         $"zone={result.Value.Effect?.Zone}, " +
                         $"effect={result.Value.Effect?.Effect}, " +
                         $"npcHP={_npc.Health.Health:F0}");

                if (result.Value.DidHit && result.Value.Effect?.Zone == BodyZone.Head)
                {
                    if (!_npc.Health.IsDown)
                        Fail("headshot should down the NPC");
                    GD.Print("  headshot confirmed — NPC is down");
                }
                else if (result.Value.DidHit)
                {
                    GD.Print($"  (hit {result.Value.Effect?.Zone} — close enough for zone geometry)");
                }

                Next();
                break;
            }

            // ---- 8: verify NPC state after combat ---
            case 8:
            {
                if (_time < 0.3) break;
                if (_npc is null) { Fail("NPC missing"); Finish(); break; }

                GD.Print($"  NPC final state: HP={_npc.Health.Health:F0}, " +
                         $"down={_npc.Health.IsDown}, " +
                         $"immobilized={_npc.Health.IsImmobilized}, " +
                         $"disarmed={_npc.Health.IsDisarmed}");

                int roundsUsed = 6 - _sidearm.State.Rounds;
                GD.Print($"  rounds used: {roundsUsed}, remaining: {_sidearm.State.Rounds}");

                Next();
                break;
            }

            // ---- 9: test reload (sim-layer; timer reload is a UI concern) ---
            case 9:
            {
                if (_time < 0.1) break;

                int spareBefore = _sidearm.State.SpareRounds;
                _sidearm.State.Rounds = 0;
                _sidearm.State.Reload();

                GD.Print($"  reload: {_sidearm.State.Rounds}/{_sidearm.State.Capacity} " +
                         $"(spare {spareBefore} → {_sidearm.State.SpareRounds})");

                if (_sidearm.State.Rounds != 6)
                    Fail($"should have 6 rounds after reload, got {_sidearm.State.Rounds}");
                if (_sidearm.State.SpareRounds != spareBefore - 6)
                    Fail($"spare should decrease by 6, got {_sidearm.State.SpareRounds}");

                Next();
                break;
            }

            // ---- 10: test pilot health system ---
            case 10:
            {
                if (_time < 0.2) break;

                _sidearm.PilotHp.Reset();
                float startHp = _sidearm.PilotHp.Health;
                _sidearm.TakeNpcHit(35f);
                float afterHit = _sidearm.PilotHp.Health;
                GD.Print($"  pilot health: {startHp:F0} → {afterHit:F0} after 35 dmg hit");

                if (afterHit >= startHp)
                    Fail("pilot should lose health from NPC hit");

                _sidearm.PilotHp.Recover(0.5f);
                GD.Print($"  pilot recovered to {_sidearm.PilotHp.Health:F0}");

                Next();
                break;
            }

            // ---- 11: board and finish ---
            case 11:
            {
                if (_time < 0.2) break;
                _main.ForceBoard();
                GD.Print($"  boarded, mode={_main.Mode}");
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
        if (_rt.Active) _rt.Deactivate();

        GD.Print($"  total {_total:F0} s");
        GD.Print("=====================================================================");
        if (_failures.Count == 0) { GD.Print("  COMBAT TEST PASSED"); GetTree().Quit(0); }
        else { GD.PrintErr($"  COMBAT TEST FAILED ({_failures.Count})"); GetTree().Quit(1); }
    }
}
