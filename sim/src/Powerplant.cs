namespace Rotorwash.Sim;

public enum EngineState { Off, Starting, Running, Flameout, Failed }

public sealed class EngineConfig
{
    /// <summary>Maximum continuous shaft power at sea level, ISA, W.</summary>
    public double MaxContinuousPower { get; init; } = 1_100_000;

    /// <summary>Takeoff / 5-minute power rating, W.</summary>
    public double TakeoffPower { get; init; } = 1_300_000;

    /// <summary>Transmission torque limit at the main rotor shaft, N.m. Often the real ceiling.</summary>
    public double TransmissionTorqueLimit { get; init; } = 48_000;

    /// <summary>Gas generator spool time constant, s. Bigger = laggier, older engine.</summary>
    public double SpoolTime { get; init; } = 0.55;

    /// <summary>Governor proportional gain on rotor speed error (torque per rad/s).</summary>
    public double GovernorP { get; init; } = 9_000;
    /// <summary>Governor integral gain (torque per rad/s per second).</summary>
    public double GovernorI { get; init; } = 22_000;

    /// <summary>Specific fuel consumption, kg per joule of shaft work.</summary>
    public double Sfc { get; init; } = 8.6e-8;     // ~0.31 kg/kW-h

    /// <summary>Seconds from starter engaged to idle.</summary>
    public double StartTime { get; init; } = 22.0;

    /// <summary>Power fraction available at flight idle.</summary>
    public double IdlePowerFraction { get; init; } = 0.06;

    /// <summary>Exponent on density ratio for available power. Turbines lose a lot up high.</summary>
    public double DensityPowerExponent { get; init; } = 0.85;
}

public struct PowerplantOutput
{
    /// <summary>Torque delivered to the main rotor shaft, N.m. Never negative (freewheel).</summary>
    public double ShaftTorque;
    public double PowerDelivered;      // W
    public double PowerAvailable;      // W at current density altitude
    public double TorquePercent;       // of transmission limit
    public double FuelFlow;            // kg/s
    public bool FreewheelEngaged;      // false = autorotating
    public bool TorqueLimited;
    public double N1;                  // gas generator, 0..1.15
}

/// <summary>
/// Turboshaft engine, governor, freewheel unit and the fuel it eats.
///
/// The freewheel is the single most important component in the game. It means the engine
/// can drive the rotor but the rotor can never drive the engine, so the instant the
/// engine quits the rotor is on its own - and the only thing keeping it turning is the
/// air coming up through the disc, which is to say the altitude you are spending. Every
/// autorotation in this game is that one line of logic plus honest aerodynamics.
/// </summary>
public sealed class Powerplant
{
    public EngineConfig Config { get; }
    public EngineState State { get; private set; } = EngineState.Off;

    /// <summary>Gas generator speed as a fraction of nominal, lagged behind demand.</summary>
    public double N1 { get; private set; }

    /// <summary>True while the pilot has the throttle rolled to flight idle.</summary>
    public bool FlightIdle { get; set; }

    /// <summary>Set to false to kill the engine, e.g. fuel starvation or battle damage.</summary>
    public bool FuelAvailable { get; set; } = true;

    private double _governorIntegral;
    private double _startTimer;

    public Powerplant(EngineConfig config) { Config = config; }

    public void Start() { if (State is EngineState.Off or EngineState.Flameout) { State = EngineState.Starting; _startTimer = 0; } }
    public void Shutdown() { State = EngineState.Off; _governorIntegral = 0; }
    public void Fail() { State = EngineState.Failed; }
    public void Flameout() { if (State == EngineState.Running) State = EngineState.Flameout; }

    public void Reset()
    {
        State = EngineState.Off; N1 = 0; _governorIntegral = 0; _startTimer = 0;
    }

    /// <summary>Put the engine straight into a governed running state, for spawning in flight.</summary>
    public void SetRunning(double n1 = 0.75)
    {
        State = EngineState.Running;
        N1 = n1;
        _governorIntegral = 0;
    }

    /// <param name="rotorOmega">Current main rotor speed, rad/s.</param>
    /// <param name="nominalOmega">Governed rotor speed, rad/s.</param>
    /// <param name="loadTorque">Torque the rotors are currently demanding, N.m at the main shaft.</param>
    /// <param name="densityRatio">rho / rho_sea_level at the current altitude.</param>
    /// <param name="powerFactor">Engine health, 0..1, scaling available power.</param>
    /// <param name="torqueFactor">Transmission health, 0..1, scaling the torque limit.</param>
    public PowerplantOutput Update(double rotorOmega, double nominalOmega, double loadTorque,
                                   double densityRatio, double dt,
                                   double powerFactor = 1.0, double torqueFactor = 1.0)
    {
        var cfg = Config;
        var o = new PowerplantOutput();

        if (!FuelAvailable && State == EngineState.Running) Flameout();

        switch (State)
        {
            case EngineState.Starting:
                _startTimer += dt;
                N1 = Math.Min(0.62, _startTimer / cfg.StartTime * 0.62);
                if (_startTimer >= cfg.StartTime) { State = EngineState.Running; _governorIntegral = 0; }
                break;
            case EngineState.Off:
            case EngineState.Flameout:
            case EngineState.Failed:
                N1 = Math.Max(0.0, N1 - dt / Math.Max(cfg.SpoolTime * 6.0, 1e-3));
                break;
        }

        double powerAvailable = cfg.MaxContinuousPower
                                * Math.Pow(Math.Max(densityRatio, 0.05), cfg.DensityPowerExponent)
                                * Math.Clamp(powerFactor, 0.0, 1.2);
        double torqueLimit = cfg.TransmissionTorqueLimit * Math.Clamp(torqueFactor, 0.05, 1.2);
        o.PowerAvailable = powerAvailable;

        if (State != EngineState.Running && State != EngineState.Starting)
        {
            o.ShaftTorque = 0;
            o.FreewheelEngaged = false;
            o.N1 = N1;
            return o;
        }

        // --- Governor --------------------------------------------------------
        // Holds rotor speed by trimming torque. Its lag is why yanking the collective
        // makes Nr droop before it recovers, and why a sloppy pilot overspeeds the
        // rotor on a rapid collective dump.
        double error = nominalOmega - rotorOmega;
        double torqueCommand;

        if (FlightIdle || State == EngineState.Starting)
        {
            torqueCommand = cfg.IdlePowerFraction * powerAvailable / Math.Max(nominalOmega, 1.0);
            _governorIntegral = 0;
        }
        else
        {
            _governorIntegral += error * dt;
            double maxTorque = torqueLimit;
            _governorIntegral = Math.Clamp(_governorIntegral, -maxTorque / cfg.GovernorI, maxTorque / cfg.GovernorI);

            // Feed-forward on the measured load makes the governor behave like a real
            // one (which senses collective position) instead of chasing its own tail.
            torqueCommand = loadTorque + cfg.GovernorP * error + cfg.GovernorI * _governorIntegral;
        }

        double torqueCeilingPower = powerAvailable / Math.Max(rotorOmega, 1.0);
        double ceiling = Math.Min(torqueCeilingPower, torqueLimit);
        o.TorqueLimited = torqueCommand > ceiling;
        torqueCommand = Math.Clamp(torqueCommand, 0, ceiling);

        // --- Spool lag -------------------------------------------------------
        double n1Target = ceiling > 1e-6 ? Math.Clamp(torqueCommand / ceiling, 0, 1) : 0;
        n1Target = 0.55 + 0.45 * n1Target;
        double a = 1.0 - Math.Exp(-dt / Math.Max(cfg.SpoolTime, 1e-3));
        N1 += (n1Target - N1) * a;

        double deliverable = ceiling * Math.Clamp((N1 - 0.55) / 0.45, 0, 1);
        double torque = Math.Min(torqueCommand, deliverable);

        // --- Freewheel -------------------------------------------------------
        // Torque can only ever flow from engine to rotor. If the rotor is being driven
        // by the air faster than the engine can push it, the sprag clutch opens and the
        // aircraft is autorotating whether the pilot has noticed or not.
        if (torque <= 0.0) { torque = 0.0; o.FreewheelEngaged = false; }
        else o.FreewheelEngaged = true;

        o.ShaftTorque = torque;
        o.PowerDelivered = torque * rotorOmega;
        o.TorquePercent = torque / Math.Max(torqueLimit, 1e-6);
        o.FuelFlow = cfg.Sfc * Math.Max(o.PowerDelivered, powerAvailable * 0.05);
        o.N1 = N1;
        return o;
    }
}
