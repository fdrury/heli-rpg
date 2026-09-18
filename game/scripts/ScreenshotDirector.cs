using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Flies a scripted route and captures stills.
///
/// This exists so the visual quality of the game can be reviewed automatically and
/// compared over time - the same shots, the same light, every build. Comparing "how does
/// it look now" against "how did it look last week" is otherwise pure vibes, and vibes do
/// not survive a long project.
///
///     godot --path game -- --screenshot
/// </summary>
public sealed partial class ScreenshotDirector : Node
{
    private readonly HelicopterController _heli;
    private readonly ChaseCamera _camera;
    private readonly Autopilot _ap = new() { CollectiveTrim = 0.5 };

    private sealed record Shot(string Name, CameraMode Mode, float Altitude, float Speed, float Heading,
                               SiteKind? Over = null);

    private readonly List<Shot> _shots = new()
    {
        new("01_hover_chase",    CameraMode.Chase,   90f,   0f,  0.0f),
        new("02_cruise_chase",   CameraMode.Chase,  180f,  42f,  0.9f),
        new("03_cockpit",        CameraMode.Cockpit,120f,  32f,  2.1f),
        new("04_low_level",      CameraMode.Chase,   35f,  38f,  3.4f),
        new("05_orbit",          CameraMode.Orbit,  140f,   0f,  0.0f),
        new("07_close_orbit",    CameraMode.Orbit,   70f,   0f,  1.6f),
        new("08_brownout",       CameraMode.Orbit,    3.5f, 0f,  2.7f),
        new("09_brownout_cockpit", CameraMode.Cockpit, 3.5f, 0f, 2.7f),
        new("10_settlement",     CameraMode.Chase,  160f,  0f,  0.4f, SiteKind.Settlement),
        new("11_airfield",       CameraMode.Chase,  260f,  0f,  1.1f, SiteKind.Airfield),
        new("12_relay",          CameraMode.Orbit,   90f,  0f,  2.2f, SiteKind.Relay),
        new("13_depot_low",      CameraMode.Chase,   70f, 18f,  3.1f, SiteKind.Depot),
        new("06_high_cruise",    CameraMode.Chase,  420f,  50f,  5.1f),
    };

    private int _index;
    private double _time;
    private bool _capturing;
    private readonly string _outDir;

    public ScreenshotDirector(HelicopterController heli, ChaseCamera camera, string outDir)
    {
        _heli = heli;
        _camera = camera;
        _outDir = outDir;
    }

    private string _resolvedDir = "";

    public override void _Ready()
    {
        // res:// paths are read-only once exported and cannot escape the project anyway,
        // so resolve to a real filesystem path before writing.
        _resolvedDir = ProjectSettings.GlobalizePath(_outDir);
        if (_resolvedDir.Contains("://")) _resolvedDir = ProjectSettings.GlobalizePath("user://screenshots");
        DirAccess.MakeDirRecursiveAbsolute(_resolvedDir);
        GD.Print($"[shots] capturing {_shots.Count} frames into {_resolvedDir}");
        Setup(_shots[0]);
    }

    private void Setup(Shot shot)
    {
        _camera.Mode = shot.Mode;

        Vector2 ground2;
        if (shot.Over is SiteKind kind)
        {
            // Park just off the site so the chase camera looks across it rather than
            // straight down at the roofs.
            Site? site = WorldMap.Nearest(Vector2.Zero, kind);
            Vector2 centre = site?.Position ?? Vector2.Zero;
            ground2 = centre - new Vector2(Mathf.Sin(shot.Heading), Mathf.Cos(shot.Heading)) * 150f;
            GD.Print($"[shots] {shot.Name} -> {site?.Name ?? "nowhere"}");
        }
        else
        {
            ground2 = new Vector2(Mathf.Sin(shot.Heading) * 260f, Mathf.Cos(shot.Heading) * 260f);
        }

        var pos = new Vector3(ground2.X, WorldHeight.At(ground2.X, ground2.Y) + shot.Altitude, ground2.Y);
        _heli.TeleportTo(pos, shot.Heading);
        _ap.Reset();
        _ap.CollectiveTrim = 0.5;
        _time = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_index >= _shots.Count) return;
        var shot = _shots[_index];
        _time += delta;

        var demand = new AutopilotDemand
        {
            Altitude = WorldHeight.At(_heli.GlobalPosition.X, _heli.GlobalPosition.Z) + shot.Altitude,
            ForwardSpeed = shot.Speed,
            LateralSpeed = 0,
            Heading = -shot.Heading,
        };
        _heli.OverrideControls = _ap.Update(_heli.Sim, demand, delta);

        // Long enough for the autopilot to settle and the camera lag to catch up.
        double settle = shot.Altitude < 12f ? 26.0 : 16.0;
        if (_time > settle && !_capturing)
        {
            _capturing = true;
            CallDeferred(nameof(Capture));
        }
    }

    private async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var shot = _shots[_index];
        Image img = GetViewport().GetTexture().GetImage();
        string path = $"{_resolvedDir}/{shot.Name}.png";
        Error err = img.SavePng(path);

        var t = _heli.Sim.Telemetry;
        GD.Print($"[shots] {shot.Name}: {(err == Error.Ok ? "saved" : err.ToString())}  " +
                 $"{t.AirspeedTrue * 1.94384:F0} kt  {t.HeightAgl:F0} m agl  {Engine.GetFramesPerSecond()} fps");

        _index++;
        _capturing = false;
        if (_index < _shots.Count) Setup(_shots[_index]);
        else
        {
            GD.Print("[shots] done");
            GetTree().Quit(0);
        }
    }
}
