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
    private Terrain _terrain = null!;
    private FlightHud _hud = null!;
    private Label _debugLabel = null!;
    private bool _showDebug;

    public override void _Ready()
    {
        QualityTier.DetectAndApply();

        BuildSky();
        BuildLighting();

        _terrain = new Terrain { Name = "Terrain" };
        AddChild(_terrain);

        AddChild(new Scatter
        {
            Name = "Scatter",
            TerrainPath = "../Terrain",
            Radius = QualityTier.Current >= QualityTier.Tier.High ? 1800f : 1200f,
            ScrubDensity = QualityTier.Current >= QualityTier.Tier.Medium ? 0.020f : 0.010f,
        });

        _heli = BuildHelicopter();
        AddChild(_heli);

        _camera = new ChaseCamera { Name = "Camera", TargetPath = _heli.GetPath() };
        AddChild(_camera);

        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _hud = new FlightHud { Name = "FlightHud", HelicopterPath = _heli.GetPath() };
        layer.AddChild(_hud);

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
                AddChild(new GodotBridgeSelfTest(_heli) { Name = "SelfTest" });
                break;
            }
            if (arg == "--screenshot")
            {
                GD.Print("[main] running the screenshot pass");
                _hud.Visible = false;
                AddChild(new ScreenshotDirector(_heli, _camera, "res://../builds/screenshots")
                { Name = "Screenshots" });
                break;
            }
        }

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
            AmbientLightSkyContribution = 0.85f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.05f,
            SsaoEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            GlowEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            FogEnabled = true,
            FogLightColor = new Color(0.66f, 0.65f, 0.60f),
            FogDensity = 0.0011f,
            FogAerialPerspective = 0.7f,
            FogSkyAffect = 0.35f,

            // Global grade. The world is drying out and coming apart, so the image is
            // pulled off full saturation and warmed slightly. Applied here rather than in
            // every material so one knob moves the whole look.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.86f,
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
            LightEnergy = 1.15f,
            LightColor = new Color(1.0f, 0.96f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = QualityTier.Current >= QualityTier.Tier.High ? 700 : 300,
            ShadowBias = 0.04f,
        };
        sun.RotationDegrees = new Vector3(-38, 128, 0);
        AddChild(sun);
    }

    // ------------------------------------------------------------- aircraft

    private HelicopterController BuildHelicopter()
    {
        var heli = new HelicopterController
        {
            Name = "Helicopter",
            TerrainPath = "../Terrain",
            StartAltitude = 140f,
        };
        heli.Position = new Vector3(0, 200, 0);

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

        // --- Placeholder airframe geometry, built from primitives ---------------
        var body = new Color(0.27f, 0.30f, 0.26f);
        var dark = new Color(0.13f, 0.14f, 0.13f);

        heli.AddChild(Box("Cabin", new Vector3(2.5f, 2.2f, 5.4f), new Vector3(0, 0.15f, -0.4f), body));
        heli.AddChild(Box("Nose", new Vector3(2.0f, 1.5f, 1.8f), new Vector3(0, -0.15f, -3.3f), body));
        heli.AddChild(Box("Boom", new Vector3(0.7f, 0.75f, 6.6f), new Vector3(0, 0.55f, 5.2f), body));
        heli.AddChild(Box("Fin", new Vector3(0.18f, 1.7f, 1.1f), new Vector3(0, 1.4f, 8.2f), body));
        heli.AddChild(Box("Stabiliser", new Vector3(3.0f, 0.14f, 0.85f), new Vector3(0, 0.55f, 6.4f), body));
        heli.AddChild(Box("Mast", new Vector3(0.36f, 1.1f, 0.36f), new Vector3(0, 1.55f, 0), dark));
        heli.AddChild(Box("SkidLeft", new Vector3(0.12f, 0.12f, 3.7f), new Vector3(-1.3f, -1.22f, 0), dark));
        heli.AddChild(Box("SkidRight", new Vector3(0.12f, 0.12f, 3.7f), new Vector3(1.3f, -1.22f, 0), dark));
        heli.AddChild(Box("StrutFwd", new Vector3(2.7f, 0.1f, 0.12f), new Vector3(0, -0.7f, -1.4f), dark));
        heli.AddChild(Box("StrutAft", new Vector3(2.7f, 0.1f, 0.12f), new Vector3(0, -0.7f, 1.4f), dark));

        // Main rotor: a hub node the controller rotates, carrying the blades. Named
        // "MainRotor" because HelicopterController looks it up by name.
        var rotor = new Node3D { Name = "MainRotor", Position = new Vector3(0, 2.2f, -0.05f) };
        for (int i = 0; i < af.MainRotor.NumBlades; i++)
        {
            float a = Mathf.Tau * i / af.MainRotor.NumBlades;
            var blade = Box($"Blade{i}", new Vector3((float)af.MainRotor.Chord, 0.10f, rotorR),
                            new Vector3(0, 0, -rotorR * 0.5f), dark);
            var pivot = new Node3D { Name = $"BladePivot{i}" };
            pivot.Rotation = new Vector3(0, a, 0);
            pivot.AddChild(blade);
            rotor.AddChild(pivot);
        }
        // The blurred disc, shown instead of the blades once the rotor is up to speed.
        var disc = new MeshInstance3D
        {
            Name = "RotorDisc",
            Mesh = ProceduralProps.RotorDisc(rotorR, 0.10f, 64),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ProceduralProps.RotorDiscTexture(128, af.MainRotor.NumBlades),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = false,
                AlbedoColor = new Color(0.22f, 0.22f, 0.21f),
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        rotor.AddChild(disc);
        heli.AddChild(rotor);

        var tail = new Node3D { Name = "TailRotor", Position = new Vector3(0.42f, 1.15f, 8.55f) };
        for (int i = 0; i < af.TailRotor.NumBlades; i++)
        {
            float a = Mathf.Tau * i / af.TailRotor.NumBlades;
            float tr = (float)af.TailRotor.Radius;
            var blade = Box($"TBlade{i}", new Vector3(0.06f, tr, (float)af.TailRotor.Chord),
                            new Vector3(0, tr * 0.5f, 0), dark);
            var pivot = new Node3D { Name = $"TPivot{i}" };
            pivot.Rotation = new Vector3(a, 0, 0);
            pivot.AddChild(blade);
            tail.AddChild(pivot);
        }
        heli.AddChild(tail);

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
