using System.Linq;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Photograph the parts of the interface that only exist when you are somewhere.
///
/// <para><see cref="HudShots"/> covers the flight HUD and the kneeboard, both of which can
/// be photographed from a hover. The rest cannot: the site action list needs the aircraft
/// parked and shut down at a real place, the dialogue panel needs somebody to talk to, and
/// the on-foot HUD needs the pilot out of the aircraft.</para>
///
/// <para>This matters more than a nice-to-have. When the interface was found to be drawing
/// off the edge of the screen, four panels were fixed - and only two of them could be
/// checked afterwards. <see cref="DialoguePanel"/> and <see cref="WarningPanel"/> were
/// corrected on the strength of reading the same bug in the same place, which is a good
/// reason to believe something and not the same as having seen it.</para>
///
///     godot --path game -- --uishot
/// </summary>
public sealed partial class UiShots : Node
{
    private readonly Main _main;
    private readonly HelicopterController _heli;
    private readonly SiteInteraction _play;
    private readonly LandingController _landing;
    private readonly SiteStreamer _sites;
    private readonly FlightHud _hud;
    private readonly string _dir;

    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private Site? _target;
    private int _step;
    private double _time;
    private string _resolvedDir = "";
    private bool _busy;
    private bool _talked;

    public UiShots(Main main, HelicopterController heli, SiteInteraction play,
                   LandingController landing, SiteStreamer sites, FlightHud hud, string dir)
    {
        _main = main;
        _heli = heli;
        _play = play;
        _landing = landing;
        _sites = sites;
        _hud = hud;
        _dir = dir;
    }

    public override void _Ready()
    {
        _resolvedDir = ProjectSettings.GlobalizePath(_dir);
        if (_resolvedDir.Contains("://")) _resolvedDir = ProjectSettings.GlobalizePath("user://screenshots");
        DirAccess.MakeDirRecursiveAbsolute(_resolvedDir);
    }

    private void Next() { _step++; _time = 0; }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_busy) return;

        Helicopter sim = _heli.Sim;
        Vector3 p = _heli.GlobalPosition;
        var flat = new Vector2(p.X, p.Z);

        switch (_step)
        {
            // ---- 0: pick somewhere with people in it ------------------------
            case 0:
            {
                if (_time < 2.0) return;
                // A settlement, because the dialogue panel needs somebody to talk to, and
                // the nearest one because fuel is finite and this is not a test of range.
                _target = WorldMap.Sites
                    .Where(s => s.Kind == SiteKind.Settlement)
                    .OrderBy(s => s.Position.DistanceTo(flat))
                    .FirstOrDefault();
                if (_target is null)
                {
                    GD.PrintErr("[uishots] no settlement in the world");
                    GetTree().Quit(1);
                    return;
                }
                GD.Print($"[uishots] flying to {_target.Name}, " +
                         $"{_target.Position.DistanceTo(flat) / 1000f:F1} km");
                // Start close. This is a shot list, not an endurance run.
                float g = WorldHeight.At(_target.Position.X, _target.Position.Y);
                _heli.GlobalTransform = new Transform3D(
                    Basis.Identity, new Vector3(_target.Position.X, g + 90f, _target.Position.Y + 160f));
                _heli.LinearVelocity = Vector3.Zero;
                _heli.AngularVelocity = Vector3.Zero;
                sim.PlaceInFlightTrimmed(g + 90f);
                Next();
                return;
            }

            // ---- 1: descend onto the pad ------------------------------------
            case 1:
            {
                float padGround = WorldHeight.At(_target!.Position.X, _target.Position.Y);
                float agl = _heli.HeightAgl();
                var demand = new AutopilotDemand
                {
                    VerticalSpeed = agl > 25 ? -3.0 : -1.1,
                    GroundTarget = SimBridge.PositionToSim(
                        new Vector3(_target.Position.X, padGround, _target.Position.Y)),
                    ApproachSpeed = agl > 25 ? 8 : 3,
                    Heading = sim.State.Orientation.Yaw,
                };
                _heli.OverrideControls = _ap.Update(sim, demand, delta);

                if (_landing.OnGround) { GD.Print("[uishots] on the ground"); Next(); }
                else if (_time > 120) { GD.PrintErr("[uishots] could not land"); GetTree().Quit(1); }
                return;
            }

            // ---- 2: shut down ------------------------------------------------
            case 2:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (sim.Telemetry.RotorRpmPercent < 65) { GD.Print("[uishots] shut down"); Next(); }
                else if (_time > 120) { GD.PrintErr("[uishots] rotor would not wind down"); GetTree().Quit(1); }
                return;
            }

            // ---- 3: the site action list -------------------------------------
            case 3:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_play.Parked is null || _time < 1.5) return;
                if (_play.Actions.Count == 0)
                {
                    GD.PrintErr($"[uishots] no actions offered at {_target!.Name}");
                    GetTree().Quit(1);
                    return;
                }
                GD.Print($"[uishots] at {_target!.Name}, {_play.Actions.Count} action(s): " +
                         string.Join(", ", _play.Actions.Select(a => a.Label)));
                Capture("ui_01_site_actions");
                return;
            }

            // ---- 4: talk to somebody ------------------------------------------
            case 4:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (!_talked)
                {
                    int talk = -1;
                    for (int i = 0; i < _play.Actions.Count; i++)
                        if (_play.Actions[i].Label == "Talk" && _play.Actions[i].Available) talk = i;
                    if (talk < 0)
                    {
                        GD.Print("[uishots] nobody to talk to here - skipping the dialogue shot");
                        _step = 6;
                        _time = 0;
                        return;
                    }
                    _play.Trigger(talk);
                    _talked = true;
                    _time = 0;
                    return;
                }
                // Let a line or two reveal itself before photographing it.
                if (_time < 2.5) return;
                Capture("ui_02_dialogue");
                return;
            }

            // ---- 5: further into the conversation ------------------------------
            case 5:
            {
                if (_time < 3.0) return;
                Capture("ui_03_dialogue_later");
                return;
            }

            // ---- 6: on foot ----------------------------------------------------
            case 6:
            {
                _heli.OverrideControls = new Controls { Collective = 0, Throttle = 0, Brake = 1 };
                if (_time < 1.0) return;
                _main.ForceDismount();
                Next();
                return;
            }

            case 7:
            {
                if (_time < 2.0) return;
                Capture("ui_04_on_foot");
                return;
            }

            default:
                GD.Print("[uishots] done");
                GetTree().Quit(0);
                return;
        }
    }

    private async void Capture(string name)
    {
        _busy = true;
        _hud.Visible = true;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();
        Error err = img.SavePng($"{_resolvedDir}/{name}.png");
        GD.Print($"[uishots] {name}: {(err == Error.Ok ? "saved" : err.ToString())}");

        Next();
        _busy = false;
    }
}
