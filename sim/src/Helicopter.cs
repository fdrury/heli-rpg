namespace Rotorwash.Sim;

/// <summary>Rigid-body state in world NED axes.</summary>
public struct FlightState
{
    public Vec3 Position;        // NED, m. Altitude = -Z
    public Vec3 Velocity;        // NED, m/s
    public Quat Orientation;     // body -> world
    public Vec3 AngularVelocity; // body axes, rad/s

    public double Altitude => -Position.Z;

    public static FlightState AtRest(double altitude = 0) => new()
    {
        Position = new Vec3(0, 0, -altitude),
        Velocity = Vec3.Zero,
        Orientation = Quat.Identity,
        AngularVelocity = Vec3.Zero,
    };
}

/// <summary>Everything the HUD, the audio system and the tuning tools want to know.</summary>
public struct FlightTelemetry
{
    public double RotorRpmPercent;
    public double TorquePercent;
    public double CollectivePitchDeg;
    public double AirspeedTrue;      // m/s
    public double GroundSpeed;       // m/s
    public double VerticalSpeed;     // m/s, positive up
    public double HeightAgl;
    public double DensityAltitude;
    public double Thrust;
    public double PowerRequired;
    public double PowerAvailable;

    // --- Where the power goes -----------------------------------------------
    // Broken out because "PowerRequired" as a single number cannot tell you whether an
    // autorotation is descending too fast because the rotor is inefficient, because the
    // tail is dragging, or because the fuselage is. The envelope test measured a glide
    // ratio half the real aircraft's (D-041) and could not close the energy books without
    // these, and a pilot's torque gauge is a single number for the same reason a diagnosis
    // needs more than one.
    /// <summary>Shaft power the main rotor demands, W. Negative when autorotating.</summary>
    public double MainRotorPower;
    /// <summary>Shaft power the tail rotor demands, referred to the main shaft, W.</summary>
    public double TailRotorPower;
    /// <summary>Drivetrain losses, W.</summary>
    public double DrivetrainPower;
    /// <summary>Power spent dragging the fuselage through the air, W.</summary>
    public double ParasitePower;
    /// <summary>Of the main rotor's demand, the part spent making lift, W.</summary>
    public double InducedPower;
    /// <summary>Of the main rotor's demand, the part spent dragging blades round, W.</summary>
    public double ProfilePower;
    public double FuelKg;
    public double FuelFlow;          // kg/s
    public double Sideslip;          // rad
    public double AngleOfAttack;     // rad
    public double VrsSeverity;
    public double BladeLoading;      // Ct/sigma
    public double TipMach;
    public double StalledFraction;
    public double Coning;
    public double FlapBack;
    public double FlapSide;
    public bool Autorotating;
    public bool TailRotorSaturated;
    public bool TorqueLimited;
    public bool OnGround;
    public EngineState Engine;
    public double LoadFactor;        // g
}

/// <summary>
/// Assembles rotors, engine and airframe into a flying machine.
///
/// Two ways to use it:
///   * <see cref="ComputeWrench"/> - advance the internal rotor/engine state and get the
///     force and moment to hand to an external rigid-body solver. This is what the Godot
///     layer calls, so collisions and contacts stay the engine's job.
///   * <see cref="Step"/> - do all that and integrate the rigid body internally. Used by
///     the headless test suite and the tuning harness, where launching an engine to find
///     out whether the rotor can hover would be absurd.
/// </summary>
public sealed class Helicopter
{
    public Airframe Airframe { get; }
    public MainRotor Rotor { get; }
    public TailRotor Tail { get; }
    public Powerplant Engine { get; }
    public IEnvironment Env { get; set; }

    public FlightState State;
    public Controls Input;

    /// <summary>Main rotor speed, rad/s.</summary>
    public double RotorOmega { get; set; }

    /// <summary>Fuel remaining, kg.</summary>
    public double Fuel { get; set; }

    /// <summary>
    /// Component health. Every effect it has is a multiplier on something the flight model
    /// already computes, so damage changes how the aircraft flies rather than applying a
    /// penalty on top of it.
    /// </summary>
    public DamageState Damage { get; } = new();

    /// <summary>Last computed telemetry.</summary>
    public FlightTelemetry Telemetry;

    /// <summary>Force and moment computed on the last call, body axes. Diagnostics only.</summary>
    public Vec3 LastForceBody { get; private set; }
    public Vec3 LastMomentBody { get; private set; }

    /// <summary>
    /// Control positions actually reaching the swashplate, after actuator rate limiting.
    /// Pilot input is a demand; hydraulic actuators and a human wrist both take time,
    /// and without this an autopilot (or a digital joystick) can slam full cyclic in a
    /// single tick and generate a hub moment no real rotor could ever see.
    /// </summary>
    public Controls Actual => _actual;

    /// <summary>
    /// Put the actuators somewhere directly, bypassing the rate limiter.
    ///
    /// Only two things have any business doing this: spawning the aircraft into a known
    /// condition, and the trim solver, which needs the surfaces exactly where it asked
    /// for them rather than wherever 0.35 s of actuator travel has got to.
    /// </summary>
    public void ForceActuators(Controls c) { _actual = c; Input = c; }

    private Controls _actual = Controls.Neutral;

    /// <summary>Seconds for a control to travel its full range. Roughly a real servo.</summary>
    public double ActuatorFullTravelTime { get; set; } = 0.35;

    /// <summary>
    /// Limited-authority stability augmentation, between the pilot's hands and the
    /// actuators. Switchable; see <see cref="Stability"/> for what it does and does not do.
    /// </summary>
    public Stability Sas { get; } = new();

    /// <summary>Enable the simple spring-damper ground model (headless use only).</summary>
    public bool UseInternalGroundModel { get; set; } = true;

    private Vec3 _lastAccelBody;
    private double _pMain, _pTail, _pDrive, _pPara, _pInduced, _pProfile;
    private double _cachedMass;
    private Vec3 _cachedCg;
    private Mat3 _inertia, _inertiaInv;
    private bool _massDirty = true;

    public Helicopter(Airframe airframe, IEnvironment? env = null)
    {
        Airframe = airframe;
        Rotor = new MainRotor(airframe.MainRotor);
        Tail = new TailRotor(airframe.TailRotor);
        Engine = new Powerplant(airframe.Engine);
        Env = env ?? new FlatEnvironment();
        Fuel = airframe.FuelCapacity * 0.6;
        RotorOmega = 0;
        State = FlightState.AtRest();
        Input = Controls.Neutral;
    }

    /// <summary>Call after changing masses, fuel load or the build.</summary>
    public void InvalidateMass() => _massDirty = true;

    public double TotalMass { get { EnsureMass(); return _cachedMass; } }
    public Vec3 CentreOfGravity { get { EnsureMass(); return _cachedCg; } }
    public Mat3 Inertia { get { EnsureMass(); return _inertia; } }

    private void EnsureMass()
    {
        if (!_massDirty) return;
        var mp = Airframe.Mass;
        mp.Remove("fuel");
        mp.Add("fuel", Airframe.FuelPosition, Math.Max(Fuel, 0.0));
        _cachedMass = mp.TotalMass;
        _cachedCg = mp.CentreOfGravity;
        _inertia = mp.InertiaAboutCg();
        _inertiaInv = _inertia.Inverse();
        _massDirty = false;
    }

    /// <summary>Spawn the aircraft running and governed, sitting on its skids.</summary>
    public void PlaceOnGround(double north = 0, double east = 0, double headingRad = 0, bool running = true)
    {
        double ground = Env.GroundHeight(north, east);
        EnsureMass();
        double skidZ = 0;
        foreach (var c in Airframe.ContactPoints) skidZ = Math.Max(skidZ, c.Z - _cachedCg.Z);
        State = new FlightState
        {
            Position = new Vec3(north, east, -(ground + skidZ)),
            Velocity = Vec3.Zero,
            Orientation = Quat.FromEuler(0, 0, headingRad),
            AngularVelocity = Vec3.Zero,
        };
        Rotor.Reset();
        Tail.Reset();
        if (running)
        {
            Engine.SetRunning();
            RotorOmega = Airframe.MainRotor.NominalOmega;
            SeedRotorForWeight();
        }
        else { Engine.Reset(); RotorOmega = 0; }
    }

    /// <summary>Spawn the aircraft airborne, trimmed straight and level at a given speed.</summary>
    public void PlaceInFlight(double altitude, double forwardSpeed = 0, double headingRad = 0)
    {
        State = new FlightState
        {
            Position = new Vec3(0, 0, -altitude),
            Velocity = new Vec3(Math.Cos(headingRad) * forwardSpeed, Math.Sin(headingRad) * forwardSpeed, 0),
            Orientation = Quat.FromEuler(0, 0, headingRad),
            AngularVelocity = Vec3.Zero,
        };
        Rotor.Reset();
        Tail.Reset();
        Engine.SetRunning();
        RotorOmega = Airframe.MainRotor.NominalOmega;
        SeedRotorForWeight();
    }

    /// <summary>
    /// Spawn the aircraft airborne and actually in balance: controls at trim, attitude at
    /// trim. <see cref="PlaceInFlight"/> puts it wings-level with the stick centred, which
    /// is not a trim state - the tail rotor's side force starts pushing on frame one.
    /// </summary>
    public TrimResult PlaceInFlightTrimmed(double altitude, double forwardSpeed = 0,
                                           double headingRad = 0)
    {
        Rotor.Reset();
        Tail.Reset();
        Engine.SetRunning();
        RotorOmega = Airframe.MainRotor.NominalOmega;
        SeedRotorForWeight();

        TrimResult t = Trim.Solve(this, altitude, forwardSpeed);

        // Solved at heading zero; rotate the whole solution onto the requested heading.
        State.Position = new Vec3(0, 0, -altitude);
        State.Orientation = Quat.FromEuler(t.RollRad, t.PitchRad, headingRad);
        State.Velocity = new Vec3(Math.Cos(headingRad) * forwardSpeed,
                                  Math.Sin(headingRad) * forwardSpeed, 0);
        State.AngularVelocity = Vec3.Zero;
        ForceActuators(t.Controls);
        Sas.TrimRollRad = t.RollRad;
        Sas.TrimPitchRad = t.PitchRad;
        Sas.TrimControls = t.Controls;
        return t;
    }

    /// <summary>
    /// Put the rotor, tail rotor and engine back to a known, repeatable state.
    ///
    /// The trim solver needs its residual to be a pure function of the six unknowns. It is
    /// not one by default: rotor inflow, blade flap, azimuth and governor state all carry
    /// over from whatever was evaluated before, so the same candidate evaluated twice gives
    /// two different answers and the numerical Jacobian ends up measuring drift instead of
    /// gradient. That is precisely what stopped the first version of the solver converging
    /// from any starting guess.
    /// </summary>
    public void ResetRotorState()
    {
        Rotor.Reset();
        Tail.Reset();
        Engine.SetRunning();
        RotorOmega = Airframe.MainRotor.NominalOmega;
        SeedRotorForWeight();
    }

    /// <summary>Seed the rotor inflow and coning for a rotor already carrying the aircraft.</summary>
    private void SeedRotorForWeight()
    {
        // Put the actuators where a hovering aircraft would have them. Otherwise the
        // rate limiter starts at full-down collective and the aircraft drops a rotor
        // disc's worth of altitude before the controls even reach the trim position.
        _actual = new Controls { Collective = 0.5, Throttle = 1.0 };
        Input = _actual;

        EnsureMass();
        double rho = Env.Atmosphere.DensityAt(State.Altitude);
        double weight = _cachedMass * Atmosphere.Gravity;
        Rotor.Seed(weight, rho, RotorOmega);

        // Estimate the anti-torque the tail will be carrying, so it does not spike either.
        var cfg = Airframe.MainRotor;
        double vh = Math.Sqrt(weight / (2 * rho * cfg.DiscArea));
        double hoverPower = weight * vh / 0.65;
        double torque = hoverPower / Math.Max(RotorOmega, 1.0);
        double arm = Math.Abs(Airframe.TailRotor.Position.X - _cachedCg.X);
        Tail.Seed(torque / Math.Max(arm, 1.0), rho, RotorOmega * Airframe.TailRotor.GearRatio);
    }

    /// <summary>
    /// Advance rotors, engine and drivetrain, and return the total force and moment
    /// about the centre of gravity, in body axes, excluding gravity and ground contact.
    /// </summary>
    public void ComputeWrench(double dt, out Vec3 forceBody, out Vec3 momentBody)
    {
        EnsureMass();
        var af = Airframe;
        var cfg = af.MainRotor;

        Input.ClampToRange();
        RateLimitControls(dt);
        Controls cmd = _actual;

        double altitude = State.Altitude;
        var atmo = Env.Atmosphere;
        double rho = atmo.DensityAt(altitude);
        double soundSpeed = atmo.SpeedOfSoundAt(altitude);

        Vec3 wind = Env.Wind(State.Position);
        Vec3 vAirWorld = State.Velocity - wind;
        Vec3 vAirBody = State.Orientation.InverseRotate(vAirWorld);
        Vec3 omega = State.AngularVelocity;

        double groundZ = Env.GroundHeight(State.Position.X, State.Position.Y);
        double heightAgl = altitude - groundZ;
        double hubAgl = heightAgl + (_cachedCg.Z - cfg.HubPosition.Z)
                        * Math.Cos(State.Orientation.Pitch) * Math.Cos(State.Orientation.Roll);

        // --- Control mapping -------------------------------------------------
        double collective = cfg.CollectiveMin + (cfg.CollectiveMax - cfg.CollectiveMin) * cmd.Collective;
        double cyclicFwd = cmd.CyclicPitch * cfg.CyclicRange;
        double cyclicRight = cmd.CyclicRoll * cfg.CyclicRange;

        var tailCfg = af.TailRotor;
        double pedalCentre = (tailCfg.PitchMax + tailCfg.PitchMin) * 0.5;
        double pedalHalf = (tailCfg.PitchMax - tailCfg.PitchMin) * 0.5;
        // Torque reaction yaws the nose right on an anticlockwise rotor, so the tail
        // rotor is always working to yaw it left, and LEFT pedal is the one that asks
        // for more tail rotor thrust. Right pedal unloads it. Getting this backwards
        // makes the aircraft depart the moment anybody touches the pedals.
        double tailPitch = pedalCentre - cmd.Pedal * pedalHalf * cfg.SpinSign;

        Engine.FlightIdle = cmd.Throttle < 0.5;

        // --- Rotors ----------------------------------------------------------
        var mr = Rotor.Update(vAirBody, omega, RotorOmega, collective, cyclicFwd, cyclicRight,
                              rho, soundSpeed, hubAgl, dt, _cachedCg);

        // A damaged rotor loses a little thrust and gains a lot of vibration. The shake is
        // the part the pilot notices, and it is what makes flying a hurt aircraft feel
        // different rather than just perform worse.
        double rotorFactor = Damage.RotorThrustFactor;
        if (rotorFactor < 0.999)
        {
            mr.Force = mr.Force * rotorFactor;
            mr.Thrust *= rotorFactor;
            mr.ShaftTorque *= 0.92 + 0.08 * rotorFactor;
        }
        double imbalance = Damage.RotorImbalance;
        if (imbalance > 1e-4)
        {
            double phase = Rotor.Azimuth;
            double mag = Math.Abs(mr.Thrust) * imbalance * cfg.Radius * 0.35;
            mr.Moment += new Vec3(Math.Cos(phase) * mag, Math.Sin(phase) * mag, 0);
        }

        var tr = Tail.Update(vAirBody, omega, RotorOmega, tailPitch, rho, soundSpeed, dt, _cachedCg);
        double tailFactor = Damage.TailRotorFactor;
        if (tailFactor < 0.999)
        {
            tr.Force = tr.Force * tailFactor;
            tr.Moment = tr.Moment * tailFactor;
            tr.Thrust *= tailFactor;
            tr.ShaftTorque *= tailFactor;
        }

        // --- Drivetrain ------------------------------------------------------
        double tailTorqueAtMain = tr.ShaftTorque * tailCfg.GearRatio;
        double losses = Math.Abs(mr.ShaftTorque) * af.DrivetrainLoss;
        double loadTorque = mr.ShaftTorque + tailTorqueAtMain + losses;

        double densityRatio = rho / Atmosphere.SeaLevelDensity;
        var pp = Engine.Update(RotorOmega, cfg.NominalOmega, loadTorque, densityRatio, dt,
                               Damage.EnginePowerFactor, Damage.TransmissionFactor);

        double brakeTorque = cmd.Brake * 4000.0 * Math.Sign(RotorOmega);
        double netTorque = pp.ShaftTorque - loadTorque - brakeTorque;
        RotorOmega = Math.Max(0.0, RotorOmega + netTorque / Math.Max(cfg.RotorInertia, 1.0) * dt);

        // --- Assemble the wrench ---------------------------------------------
        forceBody = mr.Force + tr.Force;

        // The shaft-axis component of the MAIN ROTOR moment is not transmitted to the
        // airframe directly - it spins the rotor. What the airframe feels instead is the
        // reaction to the torque the engine puts through the mast, which is why a
        // helicopter in autorotation needs almost no pedal.
        //
        // This substitution has to happen on the main rotor moment alone. Doing it on
        // the combined moment also deletes the tail rotor's yaw authority, and the
        // aircraft then spins up no matter what the pilot does with the pedals.
        Vec3 spinAxis = Rotor.ShaftAxisDown * -cfg.SpinSign;   // rotor angular velocity direction
        Vec3 rotorMoment = mr.Moment;
        rotorMoment -= spinAxis * Vec3.Dot(rotorMoment, spinAxis);
        rotorMoment += spinAxis * -pp.ShaftTorque;

        Vec3 moment = rotorMoment + tr.Moment;

        // --- Airframe aerodynamics -------------------------------------------
        double downwash = Rotor.Inflow.Lambda0 * RotorOmega * cfg.Radius * 2.0;
        double advanceRatio = RotorOmega > 1 ? vAirBody.Length / (RotorOmega * cfg.Radius) : 0;

        foreach (var s in af.Surfaces)
        {
            s.Compute(vAirBody, omega, rho, soundSpeed, downwash, advanceRatio, out var sf, out var sm, _cachedCg);
            forceBody += sf;
            moment += sm;
        }

        // Parasite drag, per axis, plus the download the rotor wake presses onto the
        // fuselage - a real and annoying few percent of thrust you never get back.
        double q = 0.5 * rho * Damage.DragFactor;
        Vec3 vb = vAirBody;
        var parasite = new Vec3(
            -q * af.DragArea.X * vb.X * Math.Abs(vb.X),
            -q * af.DragArea.Y * vb.Y * Math.Abs(vb.Y),
            -q * af.DragArea.Z * vb.Z * Math.Abs(vb.Z));
        forceBody += parasite;

        // Work rate against that drag: what the airframe costs simply to move.
        double parasitePower = -Vec3.Dot(parasite, vb);
        forceBody += Vec3.Down * (Math.Max(mr.Thrust, 0) * af.VerticalDrag / (1.0 + advanceRatio * 12.0));

        // Fuselage static moments: a helicopter fuselage is aerodynamically unstable in
        // pitch and mildly stabilising in yaw, and the player should feel both.
        double speed = vb.Length;
        if (speed > 2.0)
        {
            double qbar = 0.5 * rho * speed * speed * af.ReferenceLength;
            double alpha = Math.Atan2(vb.Z, Math.Max(vb.X, 0.5));
            double beta = Math.Asin(Math.Clamp(vb.Y / speed, -1, 1));
            moment += new Vec3(0, qbar * af.FuselageCmAlpha * alpha * 0.02,
                                  qbar * af.FuselageCnBeta * beta * 0.02);
        }

        // --- Damping from air on the fuselage in rotation ---------------------
        moment += new Vec3(-omega.X * 900.0, -omega.Y * 2200.0, -omega.Z * 1500.0) * (0.3 + densityRatio);

        momentBody = moment;
        LastForceBody = forceBody;
        LastMomentBody = moment;

        // --- Fuel ------------------------------------------------------------
        if (Fuel > 0)
        {
            Fuel = Math.Max(0, Fuel - (pp.FuelFlow + Damage.FuelLeakRate) * dt);
            _massDirty = true;
            if (Fuel <= 0) Engine.FuelAvailable = false;
        }

        // --- Telemetry --------------------------------------------------------
        Telemetry.RotorRpmPercent = RotorOmega / cfg.NominalOmega * 100.0;
        Telemetry.TorquePercent = pp.TorquePercent * 100.0;
        Telemetry.CollectivePitchDeg = collective * 180.0 / Math.PI;
        Telemetry.AirspeedTrue = vAirBody.Length;
        Telemetry.GroundSpeed = new Vec3(State.Velocity.X, State.Velocity.Y, 0).Length;
        Telemetry.VerticalSpeed = -State.Velocity.Z;
        Telemetry.HeightAgl = heightAgl;
        Telemetry.DensityAltitude = atmo.DensityAltitude(altitude);
        Telemetry.Thrust = mr.Thrust;
        Telemetry.PowerRequired = loadTorque * RotorOmega;
        Telemetry.PowerAvailable = pp.PowerAvailable;
        // Averaged over a rotor revolution, not sampled.
        //
        // A two-bladed rotor puts a violent 2/rev into shaft torque: the instantaneous main
        // rotor power in a steady autorotation swings between -347 and +464 kW depending
        // purely on where the blades happen to be, which makes the number worse than
        // useless - it looks like a diagnosis and is actually a phase reading. The
        // aircraft's own telemetry already had to learn this lesson once for the tip-path
        // plane; this is the same aliasing in a different gauge.
        double rev = 2 * Math.PI / Math.Max(RotorOmega, 1.0);
        double kp = 1.0 - Math.Exp(-dt / rev);
        _pMain += (mr.ShaftTorque * RotorOmega - _pMain) * kp;
        _pTail += (tailTorqueAtMain * RotorOmega - _pTail) * kp;
        _pDrive += (losses * RotorOmega - _pDrive) * kp;
        _pPara += (parasitePower - _pPara) * kp;
        _pInduced += (mr.InducedPower - _pInduced) * kp;
        _pProfile += (mr.ProfilePower - _pProfile) * kp;

        Telemetry.MainRotorPower = _pMain;
        Telemetry.TailRotorPower = _pTail;
        Telemetry.DrivetrainPower = _pDrive;
        Telemetry.ParasitePower = _pPara;
        Telemetry.InducedPower = _pInduced;
        Telemetry.ProfilePower = _pProfile;
        Telemetry.FuelKg = Fuel;
        Telemetry.FuelFlow = pp.FuelFlow;
        Telemetry.Sideslip = speed > 1 ? Math.Asin(Math.Clamp(vb.Y / speed, -1, 1)) : 0;
        Telemetry.AngleOfAttack = speed > 1 ? Math.Atan2(vb.Z, Math.Max(vb.X, 0.5)) : 0;
        Telemetry.VrsSeverity = Rotor.Inflow.VrsSeverity;
        Telemetry.BladeLoading = mr.CtOverSigma;
        Telemetry.TipMach = mr.TipMachAdvancing;
        Telemetry.StalledFraction = mr.StalledFraction;
        Telemetry.Coning = mr.Coning;
        Telemetry.FlapBack = mr.FlapBack;
        Telemetry.FlapSide = mr.FlapSide;
        Telemetry.Autorotating = !pp.FreewheelEngaged && Engine.State != EngineState.Running
                                 || (pp.ShaftTorque <= 1.0 && RotorOmega > cfg.NominalOmega * 0.4);
        Telemetry.TailRotorSaturated = tr.Saturated;
        Telemetry.TorqueLimited = pp.TorqueLimited;
        Telemetry.Engine = Engine.State;

        // --- Systems, fluids and consequential damage --------------------------
        //
        // Last, because it wants what the aircraft actually did this step rather than what
        // it was asked to do. One frame of lag on the multipliers is invisible at 240 Hz
        // and is the price of not having to guess at the engine's output before running it.
        //
        // The heat source is the power that went THROUGH the gearbox, so the cascade from a
        // sick engine to a cooked transmission needs no wiring: the governor holds more
        // collective, more power crosses the mesh, and the oil gets hotter. Nothing in
        // Damage.cs knows the engine is sick.
        Damage.UpdateSystems(new SystemLoad(
            RotorFraction: RotorOmega / Math.Max(cfg.NominalOmega, 1e-6),
            TorqueFraction: pp.TorquePercent,
            ShaftPowerW: Math.Max(pp.PowerDelivered, 0),
            PowerFraction: pp.PowerDelivered / Math.Max(pp.PowerAvailable, 1.0),
            AmbientTempC: atmo.TemperatureAt(altitude) - 273.15,
            EngineRunning: Engine.State == EngineState.Running), dt);
    }

    /// <summary>Move the actual control positions toward the pilot demand at a finite rate.</summary>
    private void RateLimitControls(double dt)
    {
        // Failing hydraulics make the controls slow long before they fail completely.
        double maxStep = dt / Math.Max(ActuatorFullTravelTime * Damage.ActuatorSlowdown, 1e-3);
        static double Move(double current, double target, double step)
        {
            double d = target - current;
            return Math.Abs(d) <= step ? target : current + Math.Sign(d) * step;
        }
        // The augmentation commands the actuators, it does not bypass them: it is a series
        // actuator upstream of the same lag the pilot feels, not a torque applied to the
        // rigid body. That distinction is why a saturated SAS degrades gracefully.
        Controls demand = Sas.Augment(Input, State, Damage.ActuatorEffectiveness);

        _actual.Collective = Move(_actual.Collective, demand.Collective, maxStep);
        _actual.CyclicPitch = Move(_actual.CyclicPitch, demand.CyclicPitch, maxStep * 2.0);
        _actual.CyclicRoll = Move(_actual.CyclicRoll, demand.CyclicRoll, maxStep * 2.0);
        _actual.Pedal = Move(_actual.Pedal, demand.Pedal, maxStep * 2.0);
        _actual.Throttle = Input.Throttle;
        _actual.Brake = Input.Brake;
    }

    /// <summary>
    /// Full step including gravity, the internal ground model and rigid-body integration.
    /// Used headless; in the game Godot integrates instead.
    /// </summary>
    public void Step(double dt)
    {
        EnsureMass();
        ComputeWrench(dt, out Vec3 fBody, out Vec3 mBody);

        Vec3 forceWorld = State.Orientation.Rotate(fBody);
        forceWorld += new Vec3(0, 0, _cachedMass * Atmosphere.Gravity);

        bool onGround = false;
        if (UseInternalGroundModel)
            ApplyGroundContact(ref forceWorld, ref mBody, dt, out onGround);
        Telemetry.OnGround = onGround;

        Vec3 accelWorld = forceWorld / _cachedMass;
        _lastAccelBody = State.Orientation.InverseRotate(accelWorld);
        Telemetry.LoadFactor = (-_lastAccelBody.Z) / Atmosphere.Gravity;

        // Angular: I w' = M - w x (I w)
        Vec3 iw = _inertia * State.AngularVelocity;
        Vec3 angAccel = _inertiaInv * (mBody - Vec3.Cross(State.AngularVelocity, iw));

        // Semi-implicit Euler: stable enough at 240 Hz for a vehicle this size, and it
        // matches what Godot does internally so the two paths behave the same.
        State.Velocity += accelWorld * dt;
        State.Position += State.Velocity * dt;
        State.AngularVelocity += angAccel * dt;
        State.Orientation = State.Orientation.Integrated(State.AngularVelocity, dt);

        if (!State.Position.IsFinite || !State.Velocity.IsFinite)
            throw new InvalidOperationException("Flight state diverged - the integrator produced a non-finite value.");
    }

    /// <summary>
    /// Crude but adequate skid/wheel contact for headless work: a spring-damper per
    /// contact point plus Coulomb friction. The game uses Godot collision shapes instead.
    /// </summary>
    private void ApplyGroundContact(ref Vec3 forceWorld, ref Vec3 momentBody, double dt, out bool onGround)
    {
        onGround = false;
        var pts = Airframe.ContactPoints;
        if (pts.Count == 0) return;

        double k = _cachedMass * 40.0;
        double c = _cachedMass * 8.0;

        foreach (var pLocal in pts)
        {
            Vec3 rBody = pLocal - _cachedCg;
            Vec3 rWorld = State.Orientation.Rotate(rBody);
            Vec3 pWorld = State.Position + rWorld;
            double ground = Env.GroundHeight(pWorld.X, pWorld.Y);
            double penetration = pWorld.Z - (-ground);   // positive when below the surface
            if (penetration <= 0) continue;

            onGround = true;
            Vec3 vPoint = State.Velocity + State.Orientation.Rotate(Vec3.Cross(State.AngularVelocity, rBody));
            double vDown = vPoint.Z;

            double normalForce = k * penetration + (vDown > 0 ? c * vDown : 0);
            normalForce = Math.Max(0, normalForce) / pts.Count * 4.0;

            Vec3 fw = new(0, 0, -normalForce);

            Vec3 vTangent = new(vPoint.X, vPoint.Y, 0);
            double vt = vTangent.Length;
            if (vt > 1e-4)
            {
                double mu = 0.6;
                double friction = Math.Min(mu * normalForce, _cachedMass * vt / Math.Max(dt, 1e-4) * 0.25);
                fw += -vTangent / vt * friction;
            }

            forceWorld += fw;
            momentBody += Vec3.Cross(rBody, State.Orientation.InverseRotate(fw));
        }
    }
}
