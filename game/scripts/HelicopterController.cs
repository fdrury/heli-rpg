using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>Exposes the game world to the flight model.</summary>
public sealed class GodotEnvironment : IEnvironment
{
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

    public void Advance(double dt) => _time += dt;

    /// <summary>
    /// Terrain height, converted from the sim's north/east into Godot's XZ. Answered by
    /// the same pure function the mesh and the collider come from, so ground effect and
    /// the radar altimeter can never disagree with what the aircraft can actually hit.
    /// </summary>
    public double GroundHeight(double north, double east)
    {
        // sim north = -godot Z, sim east = +godot X
        return WorldHeight.At((float)east, (float)-north);
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
    [Export] public bool StartRunning { get; set; } = true;

    /// <summary>
    /// Whether the flight model feels the world's weather.
    ///
    /// On for play. Off for anything that measures the aircraft, because a derivative
    /// measured in gusts is a measurement of the gusts.
    /// </summary>
    [Export] public bool UseWorldWeather { get; set; } = true;
    [Export] public float StartAltitude { get; set; } = 120f;

    public Helicopter Sim { get; private set; } = null!;
    public GodotEnvironment Environment { get; private set; } = null!;
    public FlightInput Input { get; private set; } = null!;

    /// <summary>Stability augmentation, when the aircraft has the hardware for it.</summary>
    public Autopilot Sas { get; } = new();

    /// <summary>How much authority the stability augmentation has, 0..1. Gear dependent.</summary>
    [Export] public float SasAuthority { get; set; } = 0.0f;

    /// <summary>
    /// The sim's own limited-authority stability augmentation.
    ///
    /// Distinct from <see cref="SasAuthority"/> above, which blends in autopilot output and
    /// is a much blunter instrument. This one lives in the flight model, damps body rates
    /// through the actuators, and is what makes the aircraft pleasant to hand-fly. Off, the
    /// bare airframe leaves trim in about six seconds; on, about twenty-two.
    ///
    /// OFF for now, and the reason is measured rather than cautious. In the pure sim it
    /// behaves exactly as designed. Through the Godot bridge it does not: the bridge
    /// self-test's roll response goes from 61.6 deg/s (Godot 61.5, in close agreement) to
    /// 281.9 deg/s with Godot reading -156.0 - the sim and the rigid body disagreeing on
    /// both magnitude and sign, which they never do otherwise. The likely cause is that
    /// Godot integrates the body while Sim.Step integrates its own copy of the state, so a
    /// rate-feedback loop closed inside the sim sees its own correction applied twice.
    /// That wants fixing properly at the bridge, not papering over with lower gains.
    /// </summary>
    [Export] public bool StabilityAugmentation { get; set; } = true;

    /// <summary>
    /// When set, these controls are flown instead of the player's. Used by the headless
    /// self-test and, later, by cutscenes and the autopilot hold modes.
    /// </summary>
    public Controls? OverrideControls { get; set; }

    private Node3D? _rotorVisual;
    private Node3D? _tailRotorVisual;
    private MeshInstance3D? _rotorDisc;
    private readonly System.Collections.Generic.List<Node3D> _bladePivots = new();
    private double _lastDt = 1.0 / 120.0;
    private Transform3D _holdTransform;
    // True from the very first physics tick: the initial spawn has exactly the same
    // problem as a teleport, and it is the one every session starts with.
    private bool _awaitingGround = true;
    private double _groundWait;

    public override void _Ready()
    {
        Environment = new GodotEnvironment();

        var airframe = Airframe.Workhorse();
        Sim = new Helicopter(airframe, Environment)
        {
            Fuel = airframe.FuelCapacity * 0.75,
            UseInternalGroundModel = false,   // Godot owns contacts
        };
        Sim.InvalidateMass();
        Sim.Sas.Enabled = StabilityAugmentation && Sim.Sas.Level != AssistLevel.Off;

        Input = new FlightInput();
        AddChild(Input);

        ApplyMassProperties();
        PlaceAtStart();

        _rotorVisual = GetNodeOrNull<Node3D>("MainRotor");
        _tailRotorVisual = GetNodeOrNull<Node3D>("TailRotor");
        _rotorDisc = _rotorVisual?.GetNodeOrNull<MeshInstance3D>("RotorDisc");
        if (_rotorVisual is not null)
            foreach (Node child in _rotorVisual.GetChildren())
                if (child is Node3D pivot && child.Name.ToString().StartsWith("BladePivot"))
                    _bladePivots.Add(pivot);

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
        float ground = WorldHeight.At(GlobalPosition.X, GlobalPosition.Z);
        var p = GlobalPosition;
        p.Y = ground + StartAltitude;
        GlobalPosition = p;

        if (StartRunning)
        {
            TrimResult trim = Sim.PlaceInFlightTrimmed(p.Y);
            AdoptTrim(trim);
        }
        else
        {
            Sim.PlaceOnGround(running: false);
        }
    }

    /// <summary>
    /// Put the pilot's controls where the trim solution says they belong.
    ///
    /// Without this the aircraft is spawned in balance and then immediately commanded out
    /// of it, because a spring-centred stick asks for zero lateral cyclic and the trim
    /// wants 0.275 of it. Trimming the airframe and not trimming the CONTROLS fixes half
    /// the problem and leaves the half the pilot actually feels.
    /// </summary>
    private void AdoptTrim(TrimResult trim)
    {
        Input.SetTrim(trim.Controls.CyclicPitch, trim.Controls.CyclicRoll, trim.Controls.Pedal);
        Input.SetCollectivePosition((float)trim.Controls.Collective);
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        double dt = state.Step;
        _lastDt = dt;
        Environment.Advance(dt);

        // Hand the weather to the flight model. The wind vector and the gust field have
        // been plumbed through to the rotor since the beginning and nothing ever set them,
        // so every flight until now has been in dead calm air at standard temperature.
        //
        // Instruments opt out. A control-derivative measurement taken in gusty air is not a
        // measurement of the control - the bridge self-test started reporting +34.9 deg/s
        // of roll on one run and -56.3 on the next, from identical code, the moment the
        // world acquired weather.
        if (UseWorldWeather)
        {
            Weather.Conditions wx = SceneMood.Now;
            Environment.SteadyWind = wx.WindNed;
            Environment.GustIntensity = wx.Gust;
            Environment.Atmosphere.IsaDeviation = wx.IsaDeviation;
        }

        if (_pendingTeleport is Transform3D target)
        {
            state.Transform = target;
            state.LinearVelocity = Vector3.Zero;
            state.AngularVelocity = Vector3.Zero;
            _pendingTeleport = null;
            AdoptTrim(Sim.PlaceInFlightTrimmed(target.Origin.Y, 0, -target.Basis.GetEuler().Y));
            _holdTransform = target;
            _awaitingGround = true;
        }

        // Do not let go until there is a world to fall onto.
        //
        // Terrain collision streams in per chunk, and after a teleport the ground beneath
        // the aircraft genuinely does not exist for about a third of a second. That is
        // easily long enough for a falling body to pass through where it is about to be,
        // and once through, it never comes back. Holding station until a ray finds
        // something is cheaper and far more predictable than trying to make the streamer
        // synchronous.
        if (_awaitingGround)
        {
            if (_holdTransform == default) _holdTransform = state.Transform;
            if (GroundExistsBelow(state)) { _awaitingGround = false; }
            else
            {
                state.Transform = _holdTransform;
                state.LinearVelocity = Vector3.Zero;
                state.AngularVelocity = Vector3.Zero;
                if ((_groundWait += dt) > 8.0)
                {
                    GD.PushWarning("[heli] no terrain collision after 8 s; releasing anyway");
                    _awaitingGround = false;
                }
                return;
            }
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
        // Stand the augmentation down whenever something else is already closing a loop
        // through the controls.
        //
        // Measured, not assumed. With the SAS live underneath the scripted autopilot, the
        // COMMANDED roll input swings the full +/-1 at about 2.5 Hz and the aircraft sits
        // in a limit cycle: 281 deg/s of roll rate at only 24 deg of attitude. The SAS is
        // not diverging - it is changing the plant. Rate damping is exactly what the
        // autopilot's gains were tuned against the absence of, so the outer loop overshoots
        // and the two controllers fight.
        //
        // A human is the slow outer loop the augmentation is FOR; another controller is
        // not, and it already does attitude hold itself. So: whoever is flying, only one
        // of them gets to be the damper.
        // AssistLevel.Off has to survive this line. Stability.Set(Off) clears Enabled, and
        // this runs every physics frame - so without the Level check the setting turned
        // itself back on again before the player let go of the key.
        Sim.Sas.Enabled = StabilityAugmentation
                          && Sim.Sas.Level != AssistLevel.Off
                          && OverrideControls is null
                          && SasAuthority <= 0.001f;

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

    /// <summary>Is there anything solid under the aircraft yet?</summary>
    private bool GroundExistsBelow(PhysicsDirectBodyState3D state)
    {
        Vector3 from = state.Transform.Origin + Vector3.Up * 4f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 4000f);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        return state.GetSpaceState().IntersectRay(query).Count > 0;
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

        // Above roughly 25% Nr the eye stops resolving individual blades and sees a disc.
        // Below it, a stopped or slowly turning rotor is one of the strongest signals that
        // something is badly wrong, so the blades stay visible.
        float nr = (float)(Sim.RotorOmega / Math.Max(Sim.Airframe.MainRotor.NominalOmega, 1e-3));
        float discVisibility = Mathf.Clamp((nr - 0.18f) / 0.30f, 0f, 1f);
        if (_rotorDisc is not null)
        {
            _rotorDisc.Visible = discVisibility > 0.01f;
            if (_rotorDisc.MaterialOverride is StandardMaterial3D m)
                m.AlbedoColor = new Color(0.22f, 0.22f, 0.21f, discVisibility);
        }
        foreach (Node3D pivot in _bladePivots) pivot.Visible = discVisibility < 0.92f;
    }

    /// <summary>Height above the terrain directly below, metres.</summary>
    public float HeightAgl()
    {
        float ground = WorldHeight.At(GlobalPosition.X, GlobalPosition.Z);
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
        t.Origin = new Vector3(t.Origin.X, WorldHeight.At(t.Origin.X, t.Origin.Z) + StartAltitude, t.Origin.Z);
        GlobalTransform = t;
        Sim.PlaceInFlightTrimmed(t.Origin.Y);
        Input.SetCollectivePosition(0.5f);
    }
}
