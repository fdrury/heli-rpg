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

    private sealed record Shot(string Name, CameraMode Mode, float Altitude, float Speed, float Heading);

    private readonly List<Shot> _shots = new()
    {
        new("01_hover_chase",    CameraMode.Chase,   90f,   0f,  0.0f),
        new("02_cruise_chase",   CameraMode.Chase,  180f,  42f,  0.9f),
        new("03_cockpit",        CameraMode.Cockpit,120f,  32f,  2.1f),
        new("04_low_level",      CameraMode.Chase,   35f,  38f,  3.4f),
        new("05_orbit",          CameraMode.Orbit,  140f,   0f,  0.0f),
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
        float ground = _heli.HeightAgl() > -9000 ? 0 : 0;
        var pos = new Vector3(
            Mathf.Sin(shot.Heading) * 260f,
            shot.Altitude + 120f,
            Mathf.Cos(shot.Heading) * 260f);
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

        float ground = _heli.HeightAgl();
        var demand = new AutopilotDemand
        {
            Altitude = _heli.GlobalPosition.Y - ground + shot.Altitude,
            ForwardSpeed = shot.Speed,
            LateralSpeed = 0,
            Heading = -shot.Heading,
        };
        _heli.OverrideControls = _ap.Update(_heli.Sim, demand, delta);

        // Long enough for the autopilot to settle and the camera lag to catch up.
        if (_time > 16.0 && !_capturing)
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
