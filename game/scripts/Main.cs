using System.IO;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

public enum GameMode { Flying, OnFoot }

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
    private DialoguePanel _dialogue = null!;
    private CodaServer _codaServer = null!;
    private PilotController _pilot = null!;
    private RotorTime _rotorTime = null!;
    private Sidearm _sidearm = null!;
    private readonly System.Collections.Generic.List<HostileNpc> _hostileNpcs = new();
    private FogOfWar _fog = new();
    private Label _debugLabel = null!;
    private bool _showDebug;
    private GameMode _mode = GameMode.Flying;
    private bool _middleMouseHeld;

    /// <summary>Current game mode, read by HUD and other systems.</summary>
    public GameMode Mode => _mode;
    public RotorTime RotorTimeSystem => _rotorTime;
    public Sidearm Sidearm => _sidearm;
    public FogOfWar Fog => _fog;

    public override void _Ready()
    {
        // Runs before the world is built: it needs an empty scene and nothing else.
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a != "--windingtest") continue;
            AddChild(new WindingTest { Name = "WindingTest" });
            return;
        }

        QualityTier.DetectAndApply();

        SceneMood.Apply(this);

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

        AddChild(new Roads { Name = "Roads" });

        _heli = BuildHelicopter();
        AddChild(_heli);

        _terrain.Target = _heli;
        _scatter.Target = _heli;
        _sites.Target = _heli;

        _camera = new ChaseCamera { Name = "Camera", TargetPath = _heli.GetPath() };
        AddChild(_camera);

        _pilot = new PilotController { Name = "Pilot" };
        _pilot.Visible = false;
        AddChild(_pilot);
        _camera.SetPilot(_pilot);

        _rotorTime = new RotorTime { Name = "RotorTime" };
        _rotorTime.SetHelicopter(_heli);
        AddChild(_rotorTime);

        _sidearm = new Sidearm { Name = "Sidearm" };
        AddChild(_sidearm);

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

        _codaServer = new CodaServer { Name = "CodaServer" };
        AddChild(_codaServer);
        _play.CodaServer = _codaServer;

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
        _hud.SetRotorTime(_rotorTime);
        _hud.SetSidearm(_sidearm);
        _hud.SetCamera(_camera);

        _kneeboard = new Kneeboard
        {
            Name = "Kneeboard",
            HelicopterPath = _heli.GetPath(),
            InteractionPath = _play.GetPath(),
        };
        _kneeboard.SetFog(_fog);
        _kneeboard.SetThreats(_threats);
        layer.AddChild(_kneeboard);

        _dialogue = new DialoguePanel
        {
            Name = "Dialogue",
            InteractionPath = _play.GetPath(),
        };
        layer.AddChild(_dialogue);
        _play.Dialogue = _dialogue;

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
            if (arg == "--foottest")
            {
                GD.Print("[main] running the on-foot test");
                _hud.Visible = false;
                _threats.Disabled = true;
                AddChild(new FootTest(this, _heli, _play, _landing, _sites, _pilot, _rotorTime)
                { Name = "FootTest" });
                break;
            }
            if (arg == "--combattest")
            {
                GD.Print("[main] running the combat test");
                _hud.Visible = false;
                _threats.Disabled = true;
                AddChild(new CombatTest(this, _heli, _play, _landing, _sites, _pilot, _rotorTime, _sidearm)
                { Name = "CombatTest" });
                break;
            }
            if (arg == "--savetest")
            {
                GD.Print("[main] running the save/load test");
                _hud.Visible = false;
                _threats.Disabled = true;
                AddChild(new SaveTest(this, _heli, _play, _landing, _sites, _sidearm, _rotorTime)
                { Name = "SaveTest" });
                break;
            }
            if (arg == "--screenshot")
            {
                GD.Print("[main] running the screenshot pass");
                _hud.Visible = false;
                _threats.Disabled = true;
                AddChild(new ScreenshotDirector(_heli, _camera, "res://../builds/screenshots", _kneeboard)
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

        // Refit system (D-011): wire module-specific effects that span systems.
        // Physics (mass, drag, fuel capacity) is handled by SiteInteraction directly;
        // these hooks handle the game-system integrations.
        _play.ModuleInstalled += mod =>
        {
            GD.Print($"[refit] installed {mod.Name} (+{mod.Mass:F0} kg)");
            switch (mod.Id)
            {
                case "sas":
                    _heli.SasAuthority = 1f;
                    break;
                case "rwr":
                    _threats.Fit(Countermeasure.RadarWarning);
                    break;
                case "chaff":
                    _threats.Fit(Countermeasure.Chaff, 30);
                    break;
                case "flares":
                    _threats.Fit(Countermeasure.Flares, 30);
                    break;
                case "suppressor":
                    _threats.Fit(Countermeasure.ExhaustSuppressor);
                    break;
            }
        };

        _play.ModuleRemoved += mod =>
        {
            GD.Print($"[refit] removed {mod.Name}");
            switch (mod.Id)
            {
                case "sas":
                    _heli.SasAuthority = 0f;
                    break;
                case "rwr":
                    _threats.Unfit(Countermeasure.RadarWarning);
                    break;
                case "chaff":
                    _threats.Unfit(Countermeasure.Chaff);
                    break;
                case "flares":
                    _threats.Unfit(Countermeasure.Flares);
                    break;
                case "suppressor":
                    _threats.Unfit(Countermeasure.ExhaustSuppressor);
                    break;
            }
        };

        GD.Print("[main] Rotorwash flight test ready");
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
        AirframeBuilder.Build(heli, af, AirframeBuilder.DefaultMaterials(new Color(0.40f, 0.42f, 0.35f)));

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
        // Mouse motion: on foot it drives the camera; in cockpit with middle-mouse it
        // drives head-look.
        if (@event is InputEventMouseMotion motion)
        {
            if (_mode == GameMode.OnFoot)
            {
                _pilot.ApplyMouseMotion(motion.Relative);
                return;
            }
            if (_middleMouseHeld && _camera.Mode == CameraMode.Cockpit)
            {
                _camera.ApplyHeadLook(motion.Relative);
                return;
            }
            return;
        }

        // Middle mouse: held = head-look drag in cockpit.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Middle } mmb)
        {
            _middleMouseHeld = mmb.Pressed;
            return;
        }

        // Mouse button: right mouse activates/deactivates Rotor Time in both modes.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } mb)
        {
            if (mb.Pressed)
            {
                if (_rotorTime.TryActivate())
                    GD.Print("[rt] ROTOR TIME active");
            }
            else
            {
                if (_rotorTime.Active)
                {
                    _rotorTime.Deactivate();
                    GD.Print("[rt] Rotor Time off");
                }
            }
            return;
        }

        // Left click: fire sidearm when on foot.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && _mode == GameMode.OnFoot)
        {
            FireSidearm();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        // While dialogue is open, route input there instead of to the rest of the game.
        if (_dialogue.IsOpen)
        {
            switch (key.Keycode)
            {
                case Key.Key1: _dialogue.HandleOption(0); return;
                case Key.Key2: _dialogue.HandleOption(1); return;
                case Key.Escape: _dialogue.Close(); return;
                case Key.Space: _dialogue.Skip(); return;
            }
            return; // swallow all other keys during dialogue
        }

        // F key: transition between flying and on foot.
        if (key.Keycode == Key.F)
        {
            if (_mode == GameMode.Flying) TryDismount();
            else TryBoard();
            return;
        }

        // Save / load.
        if (key.Keycode == Key.F5) { TrySave(); return; }
        if (key.Keycode == Key.F9) { TryLoad(); return; }

        // Keys shared across both modes.
        switch (key.Keycode)
        {
            case Key.Tab:
                _kneeboard.Toggle();
                return;
            case Key.E:
                if (_kneeboard.Visible) _kneeboard.NextPage();
                return;
            case Key.F1:
                _showDebug = !_showDebug;
                _debugLabel.Visible = _showDebug;
                if (_mode == GameMode.Flying)
                    foreach (string line in FlightInput.DescribeDevices()) GD.Print("[input] " + line);
                return;
            case Key.Escape:
                if (_mode == GameMode.OnFoot && Input.MouseMode == Input.MouseModeEnum.Captured)
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    return;
                }
                GetTree().Quit();
                return;
        }

        // Site actions work in both modes — the pilot can trigger them from outside too.
        switch (key.Keycode)
        {
            case Key.Key1: _play.Trigger(0); return;
            case Key.Key2: _play.Trigger(1); return;
            case Key.Key3: _play.Trigger(2); return;
            case Key.Key4: _play.Trigger(3); return;
        }

        // Mode-specific keys.
        if (_mode == GameMode.Flying)
            HandleFlightKey(key);
        else if (_mode == GameMode.OnFoot)
            HandleOnFootKey(key);
    }

    private void HandleFlightKey(InputEventKey key)
    {
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
            case Key.Z:
                if (!_threats.DispenseChaff()) GD.Print("[threat] no chaff");
                break;
            case Key.X:
                if (!_threats.DispenseFlares()) GD.Print("[threat] no flares");
                break;
            case Key.F2:
                if (_play.Loadout.IsInstalled("sas"))
                {
                    _heli.SasAuthority = _heli.SasAuthority > 0.5f ? 0f : 1f;
                    GD.Print($"[heli] stability augmentation {(_heli.SasAuthority > 0 ? "ENGAGED" : "off")}");
                }
                else
                {
                    GD.Print("[heli] no attitude hold unit installed");
                }
                break;
            case Key.L:
                TogglePadlock();
                break;
            case Key.Kp5:
            case Key.Home:
                _camera.CenterHead();
                GD.Print("[camera] head centred");
                break;
        }
    }

    /// <summary>Spawn a hostile NPC at a position, wired to the combat system.</summary>
    public HostileNpc SpawnHostileNpc(Vector3 position, float yaw)
    {
        var npc = new HostileNpc { Name = $"Hostile_{_hostileNpcs.Count}" };
        AddChild(npc);
        npc.SpawnAt(position, yaw);
        npc.SetTarget(_pilot);

        npc.Fired += didHit =>
        {
            if (didHit && _mode == GameMode.OnFoot)
            {
                _sidearm.TakeNpcHit(npc.Damage);
                if (_sidearm.PilotHp.IsDown)
                {
                    GD.Print("[combat] pilot is down — retreating to Hugh");
                    _sidearm.PilotHp.Recover(0.5f);
                    TryBoard();
                }
            }
        };

        npc.Hit += result =>
            GD.Print($"[combat] {npc.Name}: {result.Description}");

        _hostileNpcs.Add(npc);
        return npc;
    }

    private void HandleOnFootKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.R:
                _sidearm.TryReload();
                break;
        }
    }

    /// <summary>Fire the sidearm from the camera centre.</summary>
    private void FireSidearm()
    {
        var space = GetWorld3D().DirectSpaceState;
        Vector2 screenCentre = GetViewport().GetVisibleRect().Size / 2;
        Vector3 origin = _camera.ProjectRayOrigin(screenCentre);
        Vector3 direction = _camera.ProjectRayNormal(screenCentre);
        _sidearm.Fire(origin, direction, space);
    }

    // ------------------------------------------------------- mode transitions

    /// <summary>Can the pilot get out? Settled, shut down, on the ground.</summary>
    private bool CanDismount =>
        _mode == GameMode.Flying
        && _landing.OnGround
        && _heli.LinearVelocity.Length() < 1.2f
        && _heli.Sim.Telemetry.RotorRpmPercent < 70;

    private void TryDismount()
    {
        if (!CanDismount)
        {
            GD.Print("[main] cannot dismount — land and shut down first");
            return;
        }
        _mode = GameMode.OnFoot;
        _pilot.SpawnAtDoor(_heli);
        _pilot.Visible = true;
        _camera.SetOnFoot(true);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _hud.SetOnFoot(true);
        GD.Print("[main] dismounted — on foot");
    }

    /// <summary>Dismount driven by code (for tests). Bypasses CanDismount check.</summary>
    public void ForceDismount()
    {
        _mode = GameMode.OnFoot;
        _pilot.SpawnAtDoor(_heli);
        _pilot.Visible = true;
        _camera.SetOnFoot(true);
        _hud.SetOnFoot(true);
    }

    private void TryBoard()
    {
        if (_mode != GameMode.OnFoot) return;
        float dist = _pilot.DistanceTo(_heli.GlobalPosition);
        if (dist > 8f)
        {
            GD.Print($"[main] too far from Hugh to board ({dist:F1} m)");
            return;
        }
        _mode = GameMode.Flying;
        _pilot.Visible = false;
        _camera.SetOnFoot(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _hud.SetOnFoot(false);
        GD.Print("[main] boarded — back in the cockpit");
    }

    /// <summary>Board driven by code (for tests). Bypasses distance check.</summary>
    public void ForceBoard()
    {
        _mode = GameMode.Flying;
        _pilot.Visible = false;
        _camera.SetOnFoot(false);
        _hud.SetOnFoot(false);
    }

    // -------------------------------------------------------------- per-frame

    public override void _PhysicsProcess(double delta)
    {
        // Fog of war: reveal the map as the aircraft flies.
        Vector3 p = _heli.GlobalPosition;
        _fog.Reveal(-p.Z, p.X);   // Godot X=east, -Z=north

        // Rotor Time: charge from flight, drain when active.
        if (_mode == GameMode.Flying)
            _rotorTime.UpdateCharge(delta);
        _rotorTime.UpdateDrain(delta);

        // FOV tracks Rotor Time zoom smoothly.
        _camera.Fov = _rotorTime.CurrentFov(_camera.Fov, (float)delta);

        // Head-look: poll hat switch / D-pad / numpad for continuous cockpit look.
        if (_mode == GameMode.Flying && _camera.Mode == CameraMode.Cockpit)
            PollHeadLook();

        // On foot: move the character. BeginFrame resets the per-frame guard so that
        // either Move (player input) or WalkToward (test script) runs, but not both.
        if (_mode == GameMode.OnFoot)
        {
            _pilot.BeginFrame();
            _pilot.Move(delta);
        }
    }

    /// <summary>
    /// Read hat switch, D-pad and numpad and feed them to the camera as head-look input.
    /// Called each physics frame so the pilot can look around in cockpit mode.
    /// </summary>
    private void PollHeadLook()
    {
        float yaw = 0, pitch = 0;

        // Hat / D-pad (works for HOTAS POV hat and gamepad D-pad alike)
        var pads = Input.GetConnectedJoypads();
        if (pads.Count > 0)
        {
            int dev = pads[0];
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadLeft)) yaw -= 1;
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadRight)) yaw += 1;
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadUp)) pitch += 1;
            if (Input.IsJoyButtonPressed(dev, JoyButton.DpadDown)) pitch -= 1;
        }

        // Numpad fallback
        if (Input.IsKeyPressed(Key.Kp4)) yaw -= 1;
        if (Input.IsKeyPressed(Key.Kp6)) yaw += 1;
        if (Input.IsKeyPressed(Key.Kp8)) pitch += 1;
        if (Input.IsKeyPressed(Key.Kp2)) pitch -= 1;

        _camera.SetLookInput(yaw, pitch);
    }

    public override void _Process(double delta)
    {
        if (!_showDebug) return;
        if (_mode == GameMode.OnFoot)
        {
            _debugLabel.Text =
                $"fps {Engine.GetFramesPerSecond()}   mode ON FOOT\n" +
                $"pos {_pilot.GlobalPosition}\n" +
                $"rt charge {_rotorTime.Charge:P0}  active {_rotorTime.Active}\n" +
                $"timescale {Engine.TimeScale:F2}\n" +
                $"sidearm {_sidearm.State.Rounds}/{_sidearm.State.Capacity} (+{_sidearm.State.SpareRounds})  " +
                $"hp {_sidearm.PilotHp.Health:F0}";
            return;
        }
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
            $"VRS {t.VrsSeverity:F2}  engine {t.Engine}  autorot {t.Autorotating}\n" +
            $"rt {_rotorTime.Charge:P0}" + (_rotorTime.Active ? "  ROTOR TIME" : "");
    }

    // -------------------------------------------------------------- save / load

    private static string SaveDir
    {
        get
        {
            string dir = Path.Combine(OS.GetUserDataDir(), "saves");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SavePath => Path.Combine(SaveDir, "save1.json");

    /// <summary>Can save: parked, shut down, not in dialogue or mid-activity.</summary>
    private bool CanSave =>
        _landing.OnGround
        && _heli.LinearVelocity.Length() < 1.2f
        && _heli.Sim.Telemetry.RotorRpmPercent < 70
        && _play.Parked is not null
        && _play.Busy is null
        && !_play.InDialogue;

    private void TrySave()
    {
        if (!CanSave)
        {
            GD.Print("[save] cannot save — land, shut down, and park at a site first");
            _play.Progress.Journal("Tried to save. Put down somewhere first.");
            return;
        }
        var data = CaptureState();
        string json = data.ToJson();
        File.WriteAllText(SavePath, json);
        GD.Print($"[save] saved to {SavePath} ({json.Length} bytes)");
        _play.Progress.Journal("Progress recorded.");
    }

    private void TryLoad()
    {
        string path = SavePath;
        if (!File.Exists(path))
        {
            GD.Print("[save] no save file found");
            return;
        }
        string json = File.ReadAllText(path);
        var data = SaveData.FromJson(json);
        if (data is null)
        {
            GD.Print("[save] save file is corrupt");
            return;
        }
        ApplyState(data);
        GD.Print("[save] loaded");
    }

    /// <summary>Capture all game state into a SaveData DTO.</summary>
    public SaveData CaptureState()
    {
        var sim = _heli.Sim;
        Vector3 pos = _heli.GlobalPosition;
        // Godot X = sim east, Godot -Z = sim north
        var data = new SaveData
        {
            North = -pos.Z,
            East = pos.X,
            Altitude = pos.Y,
            HeadingRad = sim.State.Orientation.Yaw,
            Fuel = sim.Fuel,
            PilotHealth = _sidearm.PilotHp.Health,
            SidearmRounds = _sidearm.State.Rounds,
            SidearmSpare = _sidearm.State.SpareRounds,
            RotorTimeCharge = _rotorTime.Charge,
            ChaffRemaining = _threats.Field.ChaffRemaining,
            FlaresRemaining = _threats.Field.FlaresRemaining,
        };

        data.CaptureDamage(sim.Damage);
        data.CaptureLoadout(_play.Loadout);
        data.CaptureProgress(_play.Progress);

        // NPCs
        foreach (var (siteId, (npc, bank)) in _play.AllNpcs)
            data.Npcs.Add(SaveData.CaptureNpc(siteId, npc, bank));

        // Threat detection history
        foreach (var track in _threats.Field.Tracks)
            if (track.EverDetected)
                data.DetectedEmitters.Add(track.Emitter.Id);

        // Fog of war
        data.FogGrid = _fog.ToBytes();

        return data;
    }

    /// <summary>Restore all game state from a SaveData DTO.</summary>
    public void ApplyState(SaveData data)
    {
        var sim = _heli.Sim;

        // Board if on foot
        if (_mode == GameMode.OnFoot) ForceBoard();

        // Aircraft position: teleport with engine off
        float gx = (float)data.East;
        float gz = (float)-data.North;
        float gy = (float)data.Altitude;
        _heli.LinearVelocity = Vector3.Zero;
        _heli.AngularVelocity = Vector3.Zero;
        _heli.TeleportTo(new Vector3(gx, gy, gz), (float)data.HeadingRad);

        // Flight state: parked, engine off, rotor stopped
        sim.Fuel = data.Fuel;
        sim.RotorOmega = 0;
        sim.Engine.Reset();
        sim.InvalidateMass();

        // Damage
        data.ApplyDamage(sim.Damage);

        // Loadout: restore the sets, then re-apply physics effects
        var oldInstalled = new System.Collections.Generic.HashSet<string>(_play.Loadout.Installed);
        data.ApplyLoadout(_play.Loadout);
        ReapplyLoadoutPhysics(oldInstalled);

        // Progress
        var progress = data.ApplyProgress();
        var loadout = new Loadout();
        data.ApplyLoadout(loadout);
        _play.RestoreState(progress, loadout);

        // NPCs
        var restoredNpcs = new System.Collections.Generic.Dictionary<int, (NpcMind, DialogueBank)>();
        foreach (var npcSave in data.Npcs)
        {
            var mind = SaveData.RestoreNpcMind(npcSave);
            // Recreate the dialogue bank with lines from the corpus, then restore usage.
            var site = WorldMap.SiteById(npcSave.SiteId);
            DialogueBank bank;
            if (site is not null && site.Kind == SiteKind.Settlement
                && site.Region == RegionKind.Basin && IsFirstSettlement(site))
                bank = DialogueCorpus.MattieLines();
            else
                bank = DialogueCorpus.SettlerLines();
            bank.RestoreUsage(npcSave.LineUsage);
            restoredNpcs[npcSave.SiteId] = (mind, bank);
        }
        _play.RestoreNpcs(restoredNpcs);

        // Combat
        _sidearm.PilotHp.RestoreHealth(data.PilotHealth);
        _sidearm.State.Rounds = data.SidearmRounds;
        _sidearm.State.SpareRounds = data.SidearmSpare;
        _rotorTime.Charge = data.RotorTimeCharge;

        // Threats: restore countermeasure quantities and detection history
        _threats.Field.ChaffRemaining = data.ChaffRemaining;
        _threats.Field.FlaresRemaining = data.FlaresRemaining;
        var detectedSet = new System.Collections.Generic.HashSet<int>(data.DetectedEmitters);
        foreach (var track in _threats.Field.Tracks)
            track.EverDetected = detectedSet.Contains(track.Emitter.Id);

        // Re-sync countermeasure fitted flags from loadout
        ReapplyThreatFittings();

        // Fog of war
        if (data.FogGrid is not null)
            _fog.FromBytes(data.FogGrid);
    }

    /// <summary>
    /// Remove old module physics and apply the new loadout's physics. On load, the
    /// airframe starts fresh and we re-add every installed module.
    /// </summary>
    private void ReapplyLoadoutPhysics(System.Collections.Generic.HashSet<string> oldInstalled)
    {
        var af = _heli.Sim.Airframe;

        // Remove old module effects
        foreach (string id in oldInstalled)
        {
            if (!Loadout.Catalog.TryGetValue(id, out var def)) continue;
            af.Mass.Remove($"mod_{id}");
            af.DragArea -= def.DragDelta;
            af.FuelCapacity -= def.FuelCapacityDelta;
        }

        // Apply new module effects
        foreach (string id in _play.Loadout.Installed)
        {
            if (!Loadout.Catalog.TryGetValue(id, out var def)) continue;
            af.Mass.Add($"mod_{id}", def.Position, def.Mass);
            af.DragArea += def.DragDelta;
            af.FuelCapacity += def.FuelCapacityDelta;
        }

        _heli.Sim.InvalidateMass();

        // SAS authority
        _heli.SasAuthority = _play.Loadout.IsInstalled("sas") ? 1f : 0f;
    }

    /// <summary>Re-sync threat field fitted flags from the current loadout.</summary>
    private void ReapplyThreatFittings()
    {
        var field = _threats.Field;
        field.Fitted = Countermeasure.None;
        if (_play.Loadout.IsInstalled("rwr")) field.Fitted |= Countermeasure.RadarWarning;
        if (_play.Loadout.IsInstalled("chaff")) field.Fitted |= Countermeasure.Chaff;
        if (_play.Loadout.IsInstalled("flares")) field.Fitted |= Countermeasure.Flares;
        if (_play.Loadout.IsInstalled("suppressor")) field.Fitted |= Countermeasure.ExhaustSuppressor;
    }

    // -------------------------------------------------------------- head-look / padlock

    /// <summary>
    /// Toggle padlock: if locked, release; if free, lock onto the nearest detected
    /// threat emitter, or failing that the nearest known site within 5 km.
    /// </summary>
    private void TogglePadlock()
    {
        if (_camera.Mode != CameraMode.Cockpit) return;

        if (_camera.IsPadlocked)
        {
            _camera.Padlock(null);
            GD.Print("[camera] padlock released");
            return;
        }

        Vector3 heliPos = _heli.GlobalPosition;
        Vector3? best = null;
        float bestDist = float.MaxValue;

        // Nearest detected threat emitter
        foreach (var track in _threats.Field.Tracks)
        {
            if (!track.EverDetected) continue;
            var em = track.Emitter;
            float gx = (float)em.East;
            float gz = (float)-em.North;
            float gy = WorldHeight.At(gx, gz);
            Vector3 pos = new(gx, gy, gz);
            float d = heliPos.DistanceTo(pos);
            if (d < bestDist) { bestDist = d; best = pos; }
        }

        // If no threats, try nearest known site within 5 km
        if (!best.HasValue)
        {
            bestDist = 5000f;
            var progress = _play.Progress;
            foreach (var site in WorldMap.Sites)
            {
                if (!progress.HasVisited(site.Id)
                    && !progress.Knows($"contact.{site.Id}")
                    && !progress.Knows($"site.{site.Id}"))
                    continue;
                Vector3 pos = site.Ground;
                float d = heliPos.DistanceTo(pos);
                if (d < bestDist) { bestDist = d; best = pos; }
            }
        }

        if (best.HasValue)
        {
            _camera.Padlock(best);
            GD.Print($"[camera] padlock on ({bestDist:F0} m)");
        }
        else
        {
            GD.Print("[camera] nothing to padlock");
        }
    }

    private bool IsFirstSettlement(Site site)
    {
        foreach (var s in WorldMap.Sites)
            if (s.Kind == SiteKind.Settlement && s.Region == RegionKind.Basin)
                return s.Id == site.Id;
        return false;
    }
}
