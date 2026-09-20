using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Photograph the interface.
///
/// <para><b>The gap this closes.</b> The screenshot set exists to compare how the world
/// looks over time, and the very first thing it does is <c>_hud.Visible = false</c> -
/// correctly, because a HUD sitting on top of every frame makes the terrain impossible to
/// judge. The consequence went unnoticed: <see cref="FlightHud"/> is the single largest
/// script in the game and not one pixel of it had ever been looked at, nor had any of the
/// kneeboard's six pages beyond the map.</para>
///
/// <para>Everything else in this project gets measured. The interface cannot be - there is
/// no number for "the torque gauge is behind the airspeed tape" - so it gets photographed
/// instead, on the same principle: anything visual gets looked at rather than assumed.</para>
///
/// <para>The aircraft is put into a state worth photographing first. A HUD shot of a
/// healthy aircraft in clear air with nothing on the radio shows almost none of the HUD;
/// the panels that matter are the ones that only appear when something is wrong.</para>
///
///     godot --path game -- --hudshot
/// </summary>
public sealed partial class HudShots : Node
{
    private readonly HelicopterController _heli;
    private readonly ChaseCamera _camera;
    private readonly FlightHud _hud;
    private readonly Kneeboard _kneeboard;
    private readonly string _dir;

    private int _shot = -1;
    private double _t;
    private bool _busy;
    private string _resolvedDir = "";

    public HudShots(HelicopterController heli, ChaseCamera camera, FlightHud hud,
                    Kneeboard kneeboard, string dir)
    {
        _heli = heli;
        _camera = camera;
        _hud = hud;
        _kneeboard = kneeboard;
        _dir = dir;
    }

    private sealed record Shot(string Name, CameraMode Mode, bool Kneeboard, int Page = 0);

    private static readonly Shot[] Shots =
    {
        new("hud_01_chase",           CameraMode.Chase,   false),
        new("hud_02_cockpit",         CameraMode.Cockpit, false),
        new("hud_03_orbit",           CameraMode.Orbit,   false),

        // The six kneeboard pages. Only the map had ever been rendered.
        new("hud_10_kneeboard_aircraft", CameraMode.Chase, true, 0),
        new("hud_11_kneeboard_known",    CameraMode.Chase, true, 1),
        new("hud_12_kneeboard_log",      CameraMode.Chase, true, 2),
        new("hud_13_kneeboard_map",      CameraMode.Chase, true, 3),
        new("hud_14_kneeboard_jobs",     CameraMode.Chase, true, 4),
        new("hud_15_kneeboard_thread",   CameraMode.Chase, true, 5),
    };

    public override void _Ready()
    {
        _resolvedDir = ProjectSettings.GlobalizePath(_dir);
        if (_resolvedDir.Contains("://")) _resolvedDir = ProjectSettings.GlobalizePath("user://screenshots");
        DirAccess.MakeDirRecursiveAbsolute(_resolvedDir);
        GD.Print($"[hudshots] capturing {Shots.Length} frames into {_resolvedDir}");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t < 4.0) return;       // let the world stream in and the aircraft trim
        if (_busy) return;

        if (_shot < 0)
        {
            SetUpSomethingWorthLookingAt();
            _shot = 0;
        }

        if (_shot >= Shots.Length) return;

        _busy = true;
        Capture(Shots[_shot]);
    }

    /// <summary>
    /// Damage it, threaten it, load it up and give it somewhere to be.
    ///
    /// A HUD only shows what there is to show. Photographing a clean aircraft proves the
    /// HUD compiles and nothing else - the damage panel, the warnings, the RWR and the
    /// jobs page are all empty by default and all four are the ones worth checking.
    /// </summary>
    private void SetUpSomethingWorthLookingAt()
    {
        Helicopter sim = _heli.Sim;

        // Fly it, rather than photographing a cold airframe on the ground.
        float ground = WorldHeight.At(_heli.GlobalPosition.X, _heli.GlobalPosition.Z);
        _heli.GlobalTransform = new Transform3D(
            Basis.Identity, new Vector3(_heli.GlobalPosition.X, ground + 180f, _heli.GlobalPosition.Z));
        _heli.LinearVelocity = Vector3.Zero;
        _heli.AngularVelocity = Vector3.Zero;
        sim.PlaceInFlightTrimmed(ground + 180f, 38);

        // Enough damage that the damage panel and the warnings have something to say, and
        // not so much that the aircraft is on its way down during the shot.
        sim.Damage.Apply(Component.Engine, 0.22, DamageCause.Gunfire, "photographed");
        sim.Damage.Apply(Component.MainRotor, 0.14, DamageCause.Vibration, "photographed");
        sim.Damage.Apply(Component.FuelSystem, 0.30, DamageCause.Gunfire, "photographed");
        sim.Fuel = sim.Airframe.FuelCapacity * 0.18;    // low enough to warn about

        GD.Print($"[hudshots] set up: fuel {sim.Fuel:F0} kg, " +
                 $"engine {sim.Damage.Health(Component.Engine):P0}, " +
                 $"rotor {sim.Damage.Health(Component.MainRotor):P0}");
    }

    private async void Capture(Shot shot)
    {
        _camera.Mode = shot.Mode;
        _hud.Visible = true;
        _kneeboard.Visible = shot.Kneeboard;
        if (shot.Kneeboard) _kneeboard.SetPage(shot.Page);

        // Two frames: one for the layout to settle, one for it to actually be drawn.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();
        string path = $"{_resolvedDir}/{shot.Name}.png";
        Error err = img.SavePng(path);
        GD.Print($"[hudshots] {shot.Name}: {(err == Error.Ok ? "saved" : err.ToString())}");

        _shot++;
        _busy = false;

        if (_shot >= Shots.Length)
        {
            GD.Print("[hudshots] done");
            GetTree().Quit(0);
        }
    }
}
