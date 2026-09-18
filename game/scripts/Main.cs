using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Builds the flight test world in code.
///
/// Scenes are authored as code during bring-up rather than as .tscn files, because the
/// layout changes every time the flight model does and a C# scene can be compiled,
/// diffed and reviewed. Authored scenes take over once the content stops moving.
/// </summary>
public sealed partial class Main : Node3D
{
    private HelicopterController _heli = null!;
    private ChaseCamera _camera = null!;
    private TerrainStreamer _terrain = null!;
    private PropScatter _scatter = null!;
    private LandingController _landing = null!;
    private RotorwashDust _dust = null!;
    private SiteStreamer _sites = null!;
    private SiteInteraction _play = null!;
    private Kneeboard _kneeboard = null!;
    private ThreatWorld _threats = null!;
    private FlightHud _hud = null!;
    private Label _debugLabel = null!;
    private bool _showDebug;

    public override void _Ready()
    {
        QualityTier.DetectAndApply();

        BuildSky();
        BuildLighting();

        _terrain = new TerrainStreamer
        {
            Name = "Terrain",
            LodRings = QualityTier.Current >= QualityTier.Tier.High
                ? new[] { 2, 5, 9, 16 }
                : new[] { 2, 4, 7, 12 },
        };
        AddChild(_terrain);

        _scatter = new PropScatter
        {
            Name = "Scatter",
            ScrubRadius = QualityTier.Current >= QualityTier.Tier.Medium ? 3 : 2,
            RockRadius = QualityTier.Current >= QualityTier.Tier.High ? 10 : 7,
            TreeRadius = QualityTier.Current >= QualityTier.Tier.High ? 18 : 12,
            ScrubDensity = QualityTier.Current >= QualityTier.Tier.Medium ? 0.022f : 0.011f,
        };
        AddChild(_scatter);

        _sites = new SiteStreamer { Name = "Sites" };
        AddChild(_sites);

        _heli = BuildHelicopter();
        AddChild(_heli);

        _terrain.Target = _heli;
        _scatter.Target = _heli;
        _sites.Target = _heli;

        _camera = new ChaseCamera { Name = "Camera", TargetPath = _heli.GetPath() };
        AddChild(_camera);

        _landing = new LandingController { Name = "Landing", HelicopterPath = _heli.GetPath() };
        AddChild(_landing);

        AddChild(new HelicopterAudio { Name = "Audio", HelicopterPath = _heli.GetPath() });

        _dust = new RotorwashDust
        {
            Name = "Rotorwash",
            HelicopterPath = _heli.GetPath(),
            LandingControllerPath = _landing.GetPath(),
        };
        AddChild(_dust);

        _play = new SiteInteraction
        {
            Name = "Play",
            HelicopterPath = _heli.GetPath(),
            SiteStreamerPath = _sites.GetPath(),
            LandingControllerPath = _landing.GetPath(),
        };
        AddChild(_play);

        _threats = new ThreatWorld
        {
            Name = "Threats",
            HelicopterPath = _heli.GetPath(),
            InteractionPath = _play.GetPath(),
        };
        AddChild(_threats);

        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _dust.AttachHaze(layer);
        _hud = new FlightHud
        {
            Name = "FlightHud",
            HelicopterPath = _heli.GetPath(),
            LandingControllerPath = _landing.GetPath(),
            InteractionPath = _play.GetPath(),
            ThreatWorldPath = _threats.GetPath(),
        };
        layer.AddChild(_hud);

        _kneeboard = new Kneeboard
        {
            Name = "Kneeboard",
            HelicopterPath = _heli.GetPath(),
            InteractionPath = _play.GetPath(),
        };
        layer.AddChild(_kneeboard);

        _debugLabel = new Label
        {
            Name = "Debug",
            Position = new Vector2(20, 20),
            Visible = false,
            Modulate = new Color(0.8f, 1.0f, 0.85f),
        };
        layer.AddChild(_debugLabel);

        // Arguments after a bare "--" arrive through GetCmdlineUserArgs; anything before
        // it comes through GetCmdlineArgs. Check both so the flag works either way.
        var args = new System.Collections.Generic.List<string>();
        args.AddRange(OS.GetCmdlineArgs());
        args.AddRange(OS.GetCmdlineUserArgs());
        foreach (string arg in args)
        {
            if (arg == "--selftest")
            {
                GD.Print("[main] running the bridge self-test");
                _hud.Visible = false;
                // This one measures control derivatives. It cannot do that while being
                // shot at, and the difference between "the cyclic is backwards" and "a
                // SAM hit the tail rotor" is not visible in the numbers.
                _threats.Disabled = true;
                AddChild(new GodotBridgeSelfTest(_heli) { Name = "SelfTest" });
                break;
            }
            if (arg == "--looptest")
            {
                GD.Print("[main] running the core loop test");
                _hud.Visible = false;
                AddChild(new LoopTest(_heli, _play, _landing, _sites) { Name = "LoopTest" });
                break;
            }
            if (arg == "--threatreport")
            {
                ThreatReport.Run(_threats);
                GetTree().Quit(0);
                return;
            }
            if (arg == "--worldreport")
            {
                WorldReport.Run();
                GetTree().Quit(0);
                return;
            }
            if (arg == "--screenshot")
            {
                GD.Print("[main] running the screenshot pass");
                _hud.Visible = false;
                _threats.Disabled = true;
                AddChild(new ScreenshotDirector(_heli, _camera, "res://../builds/screenshots")
                { Name = "Screenshots" });
                break;
            }
        }

        _landing.Touchdown += r =>
            GD.Print($"[landing] {r.Summary} - {r.VerticalSpeed:F2} m/s, {r.GroundSpeed:F1} m/s ground, " +
                     $"{r.RollDegrees:F1} deg bank on a {r.SlopeDegrees:F1} deg slope" +
                     (r.StructuralDamage > 0.01 ? $"  [damage {r.StructuralDamage:P0}]" : ""));
        _landing.RotorStrike += what => GD.PrintErr($"[landing] ROTOR STRIKE: {what}");
        _sites.Entered += s2 => GD.Print($"[world] over {s2.Name} ({s2.Kind}, {s2.Region}, tier {s2.Tier})");
        _play.Notice += n => GD.Print($"[play] {n}");
        _heli.Sim.Damage.Damaged += e =>
            GD.Print($"[damage] {e.Component} -{e.Amount:P0} ({e.Cause}) {e.Note}");

        GD.Print("[main] Rotorwash flight test ready");
    }

    // ------------------------------------------------------------ environment

    private void BuildSky()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.26f, 0.36f, 0.48f),
            SkyHorizonColor = new Color(0.72f, 0.70f, 0.62f),
            GroundBottomColor = new Color(0.14f, 0.14f, 0.12f),
            GroundHorizonColor = new Color(0.56f, 0.53f, 0.46f),
            SunAngleMax = 26f,
            SunCurve = 0.14f,
            // A dusty sky: the horizon is pale and warm, the zenith is a muted blue.
            // Clear deep-blue skies read as holiday; this one reads as weather.
            SkyEnergyMultiplier = 0.95f,
        };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,
            AmbientLightEnergy = 1.15f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.12f,
            SsaoEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            GlowEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            FogEnabled = true,
            FogLightColor = new Color(0.66f, 0.65f, 0.60f),
            FogDensity = 0.00085f,
            FogAerialPerspective = 0.7f,
            FogSkyAffect = 0.35f,

            // Global grade. The world is drying out and coming apart, so the image is
            // pulled off full saturation and warmed slightly. Applied here rather than in
            // every material so one knob moves the whole look.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.89f,
            AdjustmentContrast = 1.06f,
            AdjustmentBrightness = 1.0f,
        };

        // Realtime global illumination is the Ultra-tier feature. It is genuinely
        // expensive on anything without hardware ray tracing, so the floor hardware gets
        // the same scene lit by the same lights, just without the bounce.
        if (QualityTier.Current >= QualityTier.Tier.Ultra)
        {
            env.SdfgiEnabled = true;
            env.SdfgiUseOcclusion = true;
            env.SsilEnabled = true;
            env.VolumetricFogEnabled = true;
            env.VolumetricFogDensity = 0.012f;
        }

        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = env });
    }

    private void BuildLighting()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.25f,
            LightColor = new Color(1.0f, 0.955f, 0.875f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = QualityTier.Current >= QualityTier.Tier.High ? 700 : 300,
            ShadowBias = 0.04f,
        };
        // Mid-afternoon, sun over the shoulder of the default heading: enough elevation
        // to light the ground, low enough to give terrain and airframes real form shadows.
        sun.RotationDegrees = new Vector3(-46, 152, 0);
        AddChild(sun);
    }

    // ------------------------------------------------------------- aircraft

    private HelicopterController BuildHelicopter()
    {
        var heli = new HelicopterController
        {
            Name = "Helicopter",
            StartAltitude = 140f,
        };
        heli.Position = new Vector3(0, 200, 1200);

        var af = Airframe.Workhorse();
        float rotorR = (float)af.MainRotor.Radius;

        // Collision: a coarse hull plus the skids. Deliberately simple - the aerodynamics
        // are detailed, the collision does not need to be.
        var hull = new CollisionShape3D
        {
            Name = "Hull",
            Shape = new BoxShape3D { Size = new Vector3(2.6f, 2.4f, 9.5f) },
            Position = new Vector3(0, 0.1f, -1.2f),
        };
        heli.AddChild(hull);

        var skidBar = new CollisionShape3D
        {
            Name = "Skids",
            Shape = new BoxShape3D { Size = new Vector3(2.9f, 0.25f, 3.6f) },
            Position = new Vector3(0, -1.15f, 0.05f),
        };
        heli.AddChild(skidBar);

        // The airframe proper: a parametric loft rather than a pile of boxes.
        // See AirframeBuilder for why it is built this way.
        AirframeBuilder.Build(heli, af, AirframeBuilder.DefaultMaterials(new Color(0.33f, 0.36f, 0.30f)));

        return heli;
    }

    private static MeshInstance3D Box(string name, Vector3 size, Vector3 position, Color colour)
    {
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.8f },
        };
    }

    // ----------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            case Key.C:
                _camera.CycleMode();
                GD.Print($"[camera] {_camera.Mode}");
                break;
            case Key.R:
                _heli.Respawn();
                GD.Print("[main] respawned");
                break;
            case Key.F1:
                _showDebug = !_showDebug;
                _debugLabel.Visible = _showDebug;
                foreach (string line in FlightInput.DescribeDevices()) GD.Print("[input] " + line);
                break;
            case Key.Tab:
                _kneeboard.Toggle();
                break;
            case Key.E:
                if (_kneeboard.Visible) _kneeboard.NextPage();
                break;
            case Key.Key1: _play.Trigger(0); break;
            case Key.Key2: _play.Trigger(1); break;
            case Key.Key3: _play.Trigger(2); break;
            case Key.Key4: _play.Trigger(3); break;
            case Key.Z:
                if (!_threats.DispenseChaff()) GD.Print("[threat] no chaff");
                break;
            case Key.X:
                if (!_threats.DispenseFlares()) GD.Print("[threat] no flares");
                break;
            case Key.F3:
                // Bench fit, until the refit system exists. Every one of these is meant to
                // be a thing you find and bolt on, not a key you press.
                _threats.Fit(Countermeasure.RadarWarning);
                _threats.Fit(Countermeasure.Chaff, 30);
                _threats.Fit(Countermeasure.Flares, 30);
                GD.Print("[threat] bench-fitted RWR, chaff and flares");
                break;
            case Key.F2:
                _heli.SasAuthority = _heli.SasAuthority > 0.5f ? 0f : 1f;
                GD.Print($"[heli] stability augmentation {(_heli.SasAuthority > 0 ? "ENGAGED" : "off")}");
                break;
            case Key.Escape:
                GetTree().Quit();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_showDebug) return;
        var t = _heli.Sim.Telemetry;
        var s = _heli.Sim;
        _debugLabel.Text =
            $"fps {Engine.GetFramesPerSecond()}   quality {QualityTier.Current}\n" +
            $"input {_heli.Input.DeviceDescription}\n" +
            $"pos {_heli.GlobalPosition}  agl {_heli.HeightAgl():F1}\n" +
            $"mass {s.TotalMass:F0} kg  cg {s.CentreOfGravity}\n" +
            $"Nr {t.RotorRpmPercent:F1}%  Q {t.TorquePercent:F0}%  coll {s.Actual.Collective:F3}\n" +
            $"thrust {t.Thrust:F0} N  power {t.PowerRequired / 1000:F0}/{t.PowerAvailable / 1000:F0} kW\n" +
            $"coning {t.Coning * 57.3:F2}  a1 {t.FlapBack * 57.3:F2}  b1 {t.FlapSide * 57.3:F2}\n" +
            $"Ct/sigma {t.BladeLoading:F4}  stalled {t.StalledFraction * 100:F0}%  tipM {t.TipMach:F2}\n" +
            $"VRS {t.VrsSeverity:F2}  engine {t.Engine}  autorot {t.Autorotating}";
    }
}
