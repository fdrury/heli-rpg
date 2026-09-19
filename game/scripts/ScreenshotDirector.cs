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
                               SiteKind? Over = null, double? ClockHours = null);

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
        new("10_settlement",     CameraMode.Chase,   70f,  0f,  0.4f, SiteKind.Settlement),
        new("11_airfield",       CameraMode.Chase,  130f,  0f,  1.1f, SiteKind.Airfield),
        new("12_relay",          CameraMode.Orbit,   90f,  0f,  2.2f, SiteKind.Relay),
        new("13_depot_low",      CameraMode.Chase,   70f, 18f,  3.1f, SiteKind.Depot),
        new("06_high_cruise",    CameraMode.Chase,  420f,  50f,  5.1f),

        // Time-pinned, so the day/night model can actually be LOOKED at. Without these the
        // whole set renders inside a ninety-minute window and a night sky could be broken
        // for weeks without anyone noticing.
        new("14_dawn",           CameraMode.Chase,  160f,  38f,  1.3f, null,  7.7),
        new("15_dusk",           CameraMode.Chase,  160f,  38f,  4.6f, null, 16.4),
        new("16_night",          CameraMode.Chase,   45f,  30f,  2.2f, null, 22.0),
        new("17_night_cockpit",  CameraMode.Cockpit,120f,  32f,  2.1f, null, 22.0),
        // 81.75 h is the first good daylight rain the weather model produces - found by
        // scanning it rather than by re-rendering until something looked wet.
        new("18_rain",           CameraMode.Chase,  120f,  40f,  0.8f, null, 81.75),
        new("19_rain_cockpit",   CameraMode.Cockpit,110f,  38f,  2.4f, null, 81.75),
    };

    private Site? _aimedAt;
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

    private static int CountDescendants(Node n)
    {
        int total = 1;
        foreach (Node c in n.GetChildren()) total += CountDescendants(c);
        return total;
    }

    private void Setup(Shot shot)
    {
        _camera.Mode = shot.Mode;

        // Hold the clock still for the duration of the set. Otherwise thirteen shots at
        // sixteen seconds of settle each advance the world by an hour and a half, and no
        // two pictures in the set are comparable.
        SceneMood.TimeScale = 0;
        SceneMood.Clock = (shot.ClockHours ?? 9.25) * 3600.0;

        Vector2 ground2;
        if (shot.Over is SiteKind kind)
        {
            // Park off the site so the chase camera looks ACROSS it rather than straight
            // down at the roofs.
            //
            // The stand-off has to scale with altitude, which a fixed 150 m did not. At the
            // 160 m the settlement shot flies at, 150 m of stand-off puts the site 47 deg
            // below the nose - well outside a 50 deg vertical field of view - so every site
            // shot in the set was a picture of empty terrain with the site underneath the
            // aircraft. Solving for a ~16 deg depression keeps the site in the lower third
            // of frame, which is where you would actually look at it from.
            //
            // Orbit shots are different: that camera tracks the aircraft, so the site has to
            // be close enough to sit behind it in frame.
            Site? site = WorldMap.Nearest(Vector2.Zero, kind);
            Vector2 centre = site?.Position ?? Vector2.Zero;
            // Tuned against what a building actually subtends rather than against the
            // depression angle alone. At 430 m a 9 m hut is about fourteen pixels, so a
            // village of them reads as scattered rocks - the settlement was rendering
            // correctly the whole time and simply too far away to be recognisable.
            float standOff = shot.Mode == CameraMode.Orbit ? 90f : shot.Altitude * 1.8f + 70f;

            // Moving shots fly during the settle, so they have to start further back by
            // exactly as far as they will travel. The depot shot cruises at 18 kt for 16 s,
            // which is 145 m - it was arriving 14 m past the site with the depot 98 deg
            // behind it.
            standOff += shot.Speed * 16f;
            // PLUS, not minus. Measured: with the stand-off subtracted the subject came
            // back 162 deg off axis - i.e. squarely behind the camera - which is why
            // every site picture was a photograph of empty scenery with the settlement
            // out of shot behind the aircraft.
            ground2 = centre + new Vector2(Mathf.Sin(shot.Heading), Mathf.Cos(shot.Heading)) * standOff;
            _aimedAt = site;
            GD.Print($"[shots] {shot.Name} -> {site?.Name ?? "nowhere"}");
        }
        else
        {
            _aimedAt = null;
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
        GD.Print($"[shots]     {Progress.FormatClock(SceneMood.Clock)}  " +
                 $"sun {SceneMood.SunNow.ElevationDeg:F0} deg  {SceneMood.Now.Describe()}");
        GD.Print($"[shots] {shot.Name}: {(err == Error.Ok ? "saved" : err.ToString())}  " +
                 $"{t.AirspeedTrue * 1.94384:F0} kt  {t.HeightAgl:F0} m agl  {Engine.GetFramesPerSecond()} fps");

        // Whether the subject is actually in shot. Three site pictures in a row came back
        // as empty scenery, and staring at them cannot distinguish "the site did not build"
        // from "the site is behind the camera". This can.
        if (_aimedAt is Site aim)
        {
            Camera3D cam = GetViewport().GetCamera3D();
            Vector3 g = aim.Ground;
            Vector3 rel = cam.GlobalTransform.Basis.Inverse() * (g - cam.GlobalPosition);
            // Camera looks down -Z in Godot, so a subject in front has rel.Z < 0.
            float range = new Vector2(g.X - cam.GlobalPosition.X, g.Z - cam.GlobalPosition.Z).Length();
            float offAxis = Mathf.RadToDeg(Mathf.Atan2(new Vector2(rel.X, rel.Y).Length(), -rel.Z));
            Node? sites = GetTree().Root.FindChild("Sites", true, false);
            int built = sites?.GetChildCount() ?? -1;

            // How much was built for THIS site, and whether the ground between here and
            // there is higher than the line of sight. "46 sites built" does not
            // distinguish a settlement that failed to generate from one sitting behind a
            // ridge, and those want completely different fixes.
            int parts = -1;
            if (sites is not null)
                foreach (Node child in sites.GetChildren())
                    if (child.Name.ToString().StartsWith($"Site_{aim.Id}_"))
                        parts = CountDescendants(child);

            float blocked = 0;
            for (int i = 1; i <= 24; i++)
            {
                float f = i / 24f;
                Vector3 on = cam.GlobalPosition.Lerp(g, f);
                float ground = WorldHeight.At(on.X, on.Z);
                if (ground > on.Y) blocked = Mathf.Max(blocked, ground - on.Y);
            }

            GD.Print($"[shots]     subject {aim.Name}: {range:F0} m away, " +
                     $"{(rel.Z < 0 ? "AHEAD" : "BEHIND")}, {offAxis:F0} deg off axis, " +
                     $"{parts} nodes in this site, {built} sites built, " +
                     $"{(blocked > 0 ? $"TERRAIN BLOCKS by {blocked:F0} m" : "clear line of sight")}");
        }

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
