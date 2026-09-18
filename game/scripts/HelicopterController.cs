using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>Exposes the game world to the flight model.</summary>
public sealed class GodotEnvironment : IEnvironment
{
    private readonly Terrain? _terrain;
    public Atmosphere Atmosphere { get; } = new();
    public Vec3 SteadyWind { get; set; }
    public double GustIntensity { get; set; } = 1.2;

    private readonly FastNoiseLite _gustNoise = new()
    {
        Seed = 4242,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.04f,
        FractalOctaves = 2,
    };
    private double _time;

    public GodotEnvironment(Terrain? terrain) { _terrain = terrain; }

    public void Advance(double dt) => _time += dt;

    /// <summary>Terrain height, converted from the sim's north/east into Godot's XZ.</summary>
    public double GroundHeight(double north, double east)
    {
        if (_terrain is null) return 0;
        // sim north = -godot Z, sim east = +godot X
        return _terrain.HeightAt((float)east, (float)-north);
    }

    public Vec3 Wind(Vec3 positionNed)
    {
        if (GustIntensity <= 0) return SteadyWind;
        float t = (float)_time;
        // Smoothly varying gusts, correlated in space, so flying through a valley in wind
        // feels like a place rather than like random noise.
        float gx = _gustNoise.GetNoise3D((float)positionNed.X * 0.01f, (float)positionNed.Y * 0.01f, t);
        float gy = _gustNoise.GetNoise3D((float)positionNed.X * 0.01f + 91f, (float)positionNed.Y * 0.01f, t);
        float gz = _gustNoise.GetNoise3D((float)positionNed.X * 0.01f, (float)positionNed.Y * 0.01f + 57f, t);
        return SteadyWind + new Vec3(gx, gy, gz * 0.4) * GustIntensity;
    }
}

/// <summary>
/// Drives a Godot rigid body from the flight model.
///
/// Division of labour: the sim owns aerodynamics, the drivetrain and the rotor state;
/// Godot owns integration, collision and contacts. Every frame the body's state is
/// converted into the sim's axes, the sim produces a force and a moment about the centre
/// of gravity, and those go straight back to the solver. Gravity is left to Godot.
/// </summary>
public sealed partial class HelicopterController : RigidBody3D
{
    [Export] public NodePath TerrainPath { get; set; } = "../Terrain";
    [Export] public bool StartRunning { get; set; } = true;
    [Export] public float StartAltitude { get; set; } = 120f;

    public Helicopter Sim { get; private set; } = null!;
    public GodotEnvironment Environment { get; private set; } = null!;
    public FlightInput Input { get; private set; } = null!;

    /// <summary>Stability augmentation, when the aircraft has the hardware for it.</summary>
    public Autopilot Sas { get; } = new();

    /// <summary>How much authority the stability augmentation has, 0..1. Gear dependent.</summary>
    [Export] public float SasAuthority { get; set; } = 0.0f;

    /// <summary>
    /// When set, these controls are flown instead of the player's. Used by the headless
    /// self-test and, later, by cutscenes and the autopilot hold modes.
    /// </summary>
    public Controls? OverrideControls { get; set; }

    private Terrain? _terrain;
    private Node3D? _rotorVisual;
    private Node3D? _tailRotorVisual;
    private double _lastDt = 1.0 / 120.0;

    public override void _Ready()
    {
        _terrain = GetNodeOrNull<Terrain>(TerrainPath);
        Environment = new GodotEnvironment(_terrain);

        var airframe = Airframe.Workhorse();
        Sim = new Helicopter(airframe, Environment)
        {
            Fuel = airframe.FuelCapacity * 0.75,
            UseInternalGroundModel = false,   // Godot owns contacts
        };
        Sim.InvalidateMass();

        Input = new FlightInput();
        AddChild(Input);

        ApplyMassProperties();
        PlaceAtStart();

        _rotorVisual = GetNodeOrNull<Node3D>("MainRotor");
        _tailRotorVisual = GetNodeOrNull<Node3D>("TailRotor");

        GD.Print($"[heli] {airframe.Name}: {Sim.TotalMass:F0} kg, CG {Sim.CentreOfGravity}, " +
                 $"rotor {airframe.MainRotor.Radius:F2} m");
    }

    private void ApplyMassProperties()
    {
        Mass = (float)Sim.TotalMass;

        // Godot inertia is expressed in ITS body axes: x right, y up, z back.
        // The sim's are forward / right / down, so the principal moments swap around.
        var inertia = Sim.Inertia;
        Inertia = new Vector3(
            (float)inertia.M11,   // about Godot X (right)  = sim Y
            (float)inertia.M22,   // about Godot Y (up)     = sim Z
            (float)inertia.M00);  // about Godot Z (back)   = sim X

        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = SimBridge.ToGodot(Sim.CentreOfGravity);

        CanSleep = false;
        ContinuousCd = true;
        MaxContactsReported = 4;
        ContactMonitor = true;
    }

    private void PlaceAtStart()
    {
        float ground = _terrain?.HeightAt(GlobalPosition.X, GlobalPosition.Z) ?? 0f;
        var p = GlobalPosition;
        p.Y = ground + StartAltitude;
        GlobalPosition = p;

        if (StartRunning)
        {
            Sim.PlaceInFlight(p.Y);
            Input.SetCollectivePosition(0.5f);
        }
        else
        {
            Sim.PlaceOnGround(running: false);
        }
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        double dt = state.Step;
        _lastDt = dt;
        Environment.Advance(dt);

        if (_pendingTeleport is Transform3D target)
        {
            state.Transform = target;
            state.LinearVelocity = Vector3.Zero;
            state.AngularVelocity = Vector3.Zero;
            _pendingTeleport = null;
            Sim.PlaceInFlight(target.Origin.Y, 0, -target.Basis.GetEuler().Y);
        }

        // --- Read the body state into the sim ---------------------------------
        Basis basis = state.Transform.Basis;
        Vector3 comWorld = state.Transform * CenterOfMass;

        Sim.State.Position = SimBridge.PositionToSim(comWorld);
        Sim.State.Velocity = SimBridge.ToSim(state.LinearVelocity);
        Sim.State.Orientation = SimBridge.OrientationToSim(basis);
        Sim.State.AngularVelocity = SimBridge.ToSim(basis.Inverse() * state.AngularVelocity);

        // --- Pilot input ------------------------------------------------------
        Controls pilot = OverrideControls ?? Input.Poll(dt);
        if (OverrideControls is null && SasAuthority > 0.001f)
        {
            // Stability augmentation blends toward an attitude-hold solution. It is a
            // physical box on the airframe, so its authority is a property of the gear.
            var demand = new AutopilotDemand
            {
                PitchAttitude = pilot.CyclicPitch * 0.35,
                RollAttitude = pilot.CyclicRoll * 0.35,
                YawRate = pilot.Pedal * 1.2,
                Collective = pilot.Collective,
            };
            Controls augmented = Sas.Update(Sim, demand, dt);
            float a = Mathf.Clamp(SasAuthority, 0, 1);
            pilot.CyclicPitch = Mathf.Lerp((float)pilot.CyclicPitch, (float)augmented.CyclicPitch, a);
            pilot.CyclicRoll = Mathf.Lerp((float)pilot.CyclicRoll, (float)augmented.CyclicRoll, a);
            pilot.Pedal = Mathf.Lerp((float)pilot.Pedal, (float)augmented.Pedal, a);
        }
        Sim.Input = pilot;

        // --- Aerodynamics ------------------------------------------------------
        Sim.ComputeWrench(dt, out Vec3 forceBody, out Vec3 momentBody);

        Vector3 forceWorld = basis * SimBridge.ToGodot(forceBody);
        Vector3 torqueWorld = basis * SimBridge.ToGodot(momentBody);

        if (!forceWorld.IsFinite() || !torqueWorld.IsFinite())
        {
            GD.PushError("[heli] flight model produced a non-finite wrench; ignoring this step");
            return;
        }

        state.ApplyCentralForce(forceWorld);
        state.ApplyTorque(torqueWorld);

        // Keep the solver's mass properties in step with fuel burn and any refit.
        if (Mathf.Abs(Mass - (float)Sim.TotalMass) > 5f) ApplyMassProperties();
    }

    public override void _Process(double delta)
    {
        // Rotor visuals. The azimuth comes straight from the sim so the blades are where
        // the aerodynamics says they are, and the disc tilts with the real flapping.
        if (_rotorVisual is not null)
        {
            _rotorVisual.Rotation = new Vector3(
                (float)-Sim.Telemetry.FlapBack,
                (float)Sim.Rotor.Azimuth * -Sim.Airframe.MainRotor.SpinSign,
                (float)-Sim.Telemetry.FlapSide);
        }
        if (_tailRotorVisual is not null)
        {
            float tailAngle = (float)(Sim.Rotor.Azimuth * Sim.Airframe.TailRotor.GearRatio);
            _tailRotorVisual.Rotation = new Vector3(tailAngle, 0, 0);
        }
    }

    /// <summary>Height above the terrain directly below, metres.</summary>
    public float HeightAgl()
    {
        float ground = _terrain?.HeightAt(GlobalPosition.X, GlobalPosition.Z) ?? 0f;
        return GlobalPosition.Y - ground;
    }

    /// <summary>
    /// Teleport to a known clean state. Used by the self-test between measurements and by
    /// fast travel. The pending transform is applied on the next physics step so the
    /// solver never sees a discontinuity mid-integration.
    /// </summary>
    public void TeleportTo(Vector3 position, float headingRad = 0)
    {
        _pendingTeleport = new Transform3D(new Basis(Vector3.Up, headingRad), position);
    }

    private Transform3D? _pendingTeleport;

    public void Respawn()
    {
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        var t = GlobalTransform;
        t.Basis = Basis.Identity;
        t.Origin = new Vector3(t.Origin.X, (_terrain?.HeightAt(t.Origin.X, t.Origin.Z) ?? 0) + StartAltitude, t.Origin.Z);
        GlobalTransform = t;
        Sim.PlaceInFlight(t.Origin.Y);
        Input.SetCollectivePosition(0.5f);
    }
}
