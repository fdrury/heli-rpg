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

    /// <summary>
    /// How much slower the gas generator accelerates than it decelerates.
    ///
    /// A turbine can always shut fuel off instantly; it cannot add it instantly, because
    /// the fuel control's acceleration schedule exists precisely to stop the compressor
    /// surging and the turbine cooking. This asymmetry is the physical origin of droop:
    /// the rotor loses speed at the rate the load takes it away and gets it back at the
    /// rate the acceleration schedule allows.
    /// </summary>
    public double SpoolAccelFactor { get; init; } = 1.8;

    /// <summary>
    /// Governor proportional gain on rotor speed error (torque per rad/s).
    ///
    /// 5500, not the 9000 of the first pass, and the difference is the whole loop. The old
    /// governor was handed the measured load as an exact, instantaneous feed-forward, so
    /// its gains never had to close a loop around anything and could be any number at all.
    /// A real anticipator lags, the gas generator lags behind that, and with two lags in
    /// series 9000 put the crossover above 1 rad/s with about twenty degrees of phase
    /// margin: the healthy governor hunted Nr between 97 and 101% in the hover, with the
    /// torque needle swinging a quarter of the placard. Measured, not guessed.
    /// </summary>
    public double GovernorP { get; init; } = 5_500;

    /// <summary>
    /// Governor integral gain (torque per rad/s per second).
    ///
    /// Deliberately far below the proportional gain: the PI corner sits near 0.2 rad/s,
    /// well under the crossover, so the integrator is trimming out the anticipator's error
    /// rather than flying the rotor. It is what eventually puts Nr back on the reference
    /// after a pull, and scaling it down is a large part of what a tired governor feels
    /// like.
    /// </summary>
    public double GovernorI { get; init; } = 1_200;

    /// <summary>
    /// Droop law: rotor speed lost per unit of torque above <see cref="DroopTopTorque"/>,
    /// as a fraction of nominal.
    ///
    /// A turbine governor is a droop governor - it has to be, or two engines could never
    /// share a load - so governed Nr is not one number, it is a shallow ramp downward with
    /// power, and it is why the needle sags a little when the pilot asks for a lot even
    /// when nothing at all is wrong.
    /// </summary>
    public double DroopSlope { get; init; } = 0.035;

    /// <summary>
    /// Torque fraction at and below which the governor holds exactly 100%: the topping
    /// speed, above which the droop law takes over.
    ///
    /// Two reasons this exists rather than a droop law that runs the needle above 100% at
    /// low power, which is what a real speeder spring does and what the first version
    /// modelled. The first is that the topping function on a real fuel control is there
    /// precisely to stop that. The second is measured: an ungoverned upper side put the
    /// cruise at 100.5% instead of 100.0%, and half a percent of rotor speed at the moment
    /// of an engine failure was enough to flip a frozen-control autorotation entry from
    /// winding the rotor up to 110% to unwinding it to 65%. The entry is that
    /// knife-edged (D-041, D-045), the aerodynamics are not this file's to relocate, and
    /// half a percent of governed speed is not worth spending on it.
    /// </summary>
    public double DroopTopTorque { get; init; } = 0.68;

    /// <summary>
    /// Time constant of the collective anticipator, s, with the linkage in good order.
    /// The droop compensator is a cam on the collective that opens the fuel valve before
    /// the rotor has slowed down. It is the difference between a 3% droop and a 9% one.
    /// </summary>
    public double AnticipationLag { get; init; } = 0.30;

    /// <summary>Anticipator time constant with the compensator worn out, s.</summary>
    public double DegradedAnticipationLag { get; init; } = 3.0;

    /// <summary>Fuel valve response with a healthy governor, s.</summary>
    public double GovernorLag { get; init; } = 0.10;

    /// <summary>
    /// Fuel valve response with a sick one, s. Large enough that the integral term ends up
    /// chasing its own phase lag, which is what makes a tired governor hunt rather than
    /// simply being slow.
    /// </summary>
    public double DegradedGovernorLag { get; init; } = 1.30;

    /// <summary>
    /// Stiction in the fuel valve and its linkage at zero authority, as a fraction of the
    /// transmission torque limit. Zero on a governor in good order.
    ///
    /// This is what actually makes a tired governor hunt, and it is worth being precise
    /// about why, because the obvious answer is wrong. Simply making the loop slower and
    /// weaker produces a governor that is soft and perfectly steady - measured, on the
    /// bench: 0.02% of wander at four tenths authority. A worn valve does not behave like
    /// that. It sticks, so the integrator winds up against a valve that is not moving, and
    /// when it finally breaks free it is already past where it needed to be, and the whole
    /// thing repeats. The needle wanders a percent or two either side and will not settle,
    /// which is exactly the complaint pilots make about a governor on its way out.
    /// </summary>
    public double GovernorStiction { get; init; } = 0.11;

    /// <summary>Below this governor authority the fuel control is not holding Nr at all.</summary>
    public double GovernorFailAuthority { get; init; } = 0.12;

    /// <summary>Specific fuel consumption, kg per joule of shaft work.</summary>
    public double Sfc { get; init; } = 8.6e-8;     // ~0.31 kg/kW-h

    /// <summary>Seconds from starter engaged to idle, doing it properly.</summary>
    public double StartTime { get; init; } = 22.0;

    /// <summary>
    /// Gas generator speed the starter alone can hold. It cannot start the engine; all it
    /// can do is give the fuel some air to burn in.
    /// </summary>
    public double MotoringN1 { get; init; } = 0.30;

    /// <summary>Gas generator speed at which the starter drops out and combustion carries it.</summary>
    public double StarterDropoutN1 { get; init; } = 0.45;

    /// <summary>Gas generator speed at ground idle.</summary>
    public double IdleN1 { get; init; } = 0.62;

    /// <summary>Minimum gas generator speed at which fuel will light at all.</summary>
    public double LightOffN1 { get; init; } = 0.03;

    /// <summary>The N1 the checklist tells you to wait for before introducing fuel.</summary>
    public double StartFuelN1 { get; init; } = 0.20;

    /// <summary>
    /// Fuel on the start schedule, expressed as the N1 at which it is exactly the right
    /// amount of it. The start schedule meters a roughly fixed quantity of fuel and the air
    /// to burn it in goes as N1, so the mixture the combustor sees is this number over N1,
    /// and introducing fuel early is the entire failure mode.
    /// </summary>
    public double StartFuelFraction { get; init; } = 0.150;

    /// <summary>
    /// How sharply start temperature climbs as the mixture goes rich.
    ///
    /// Not linear, and it matters. A slightly rich combustor simply burns hotter; a grossly
    /// rich one cannot burn what it is given where it is given it, and the surplus goes on
    /// burning downstream, in and behind the turbine, which is the part that cannot take
    /// it. Modelling the ratio linearly gave a start that was 450 C whenever the fuel went
    /// in, because the only way to make the early case hot enough was to make the correct
    /// case hot as well.
    /// </summary>
    public double StartRichnessExponent { get; init; } = 1.6;

    /// <summary>
    /// How completely a rich mixture turns into shaft work, as a power of the mixture
    /// ratio. Fuel burning in the turbine instead of the combustor makes temperature, not
    /// work.
    /// </summary>
    public double StartCombustionExponent { get; init; } = 5.0;

    /// <summary>
    /// How hard a rich fire holds the gas generator back, in N1 per second, at the point
    /// where none of it is doing anything useful.
    ///
    /// This is the number that decides whether a hot start is a system or a flash on a
    /// gauge, and it is not a fudge: a combustor burning far more fuel than its air can
    /// take is a hot, high-pressure obstruction that the compressor is working against and
    /// the turbine is not paying for. That is what a hung start IS - the gas generator
    /// sitting at fifteen percent with the temperature climbing, going nowhere. Without it
    /// the engine simply accelerated out of its own mistake in two seconds and the worst
    /// start the model could produce peaked at 578 C, below the caution arc, where no pilot
    /// would ever see it.
    /// </summary>
    public double StartRichDrag { get; init; } = 0.018;

    /// <summary>
    /// Engine health consumed per second per 100 C above the TOT red line during a start.
    ///
    /// Separate from the slow erosion <c>DamageState</c> applies to a running engine over
    /// temperature, and steeper, because it is a different mechanism: a start overtemp is
    /// a thermal shock into cold turbine metal rather than a long soak in hot gas. Both
    /// are applied - the slow one through the TOT gauge like any other overtemperature,
    /// this one as the acute event - and the sum is what a hot start costs.
    /// </summary>
    public double HotStartWear { get; init; } = 0.0048;

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

    /// <summary>
    /// Torque the governor was <i>asking</i> for, as a fraction of the transmission limit,
    /// uncapped. Above 1.0 means the pilot is demanding more than the gearbox is placarded
    /// for and the ceiling is holding him back.
    ///
    /// Reported rather than delivered because the ceiling stays exactly where it was: this
    /// is a gauge, not a change to the power available. It exists because
    /// <see cref="PowerplantOutput.TorquePercent"/> is clamped at the limit and therefore
    /// can never tell the difference between an aircraft comfortably at 100% and an
    /// aircraft that would be at 130% if it could - which is the difference between a
    /// heavy day and an impossible one.
    /// </summary>
    public double TorqueDemandPercent;

    public double FuelFlow;            // kg/s
    public bool FreewheelEngaged;      // false = autorotating
    public bool TorqueLimited;
    public double N1;                  // gas generator, 0..1.15

    /// <summary>
    /// How hard the hot section is working, as the fraction <c>DamageState</c> turns into
    /// a turbine outlet temperature. In flight it is delivered over available power. During
    /// a start it is the start schedule's fuel divided by the air the compressor is moving,
    /// which is how a hot start gets onto the TOT gauge at all.
    /// </summary>
    public double TotFraction;

    /// <summary>
    /// Rotor speed the governor is currently holding, rad/s. Not nominal: a droop
    /// governor's reference falls with power.
    /// </summary>
    public double GovernedOmega;

    /// <summary>True while the governor is metering the fuel, false when the pilot is.</summary>
    public bool Governing;

    /// <summary>
    /// Engine health destroyed by an overtemperature start, reported once, on the step the
    /// start ends - whether it ended at idle or with the pilot pulling the fuel off.
    ///
    /// Reported rather than applied because the powerplant has no business holding a
    /// reference to the damage model, and reported as one lump rather than per-step because
    /// a hot start is one event in the aircraft's log, not four hundred.
    /// </summary>
    public double HotStartDamage;
}

/// <summary>
/// Turboshaft engine, governor, freewheel unit and the fuel it eats.
///
/// The freewheel is the single most important component in the game. It means the engine
/// can drive the rotor but the rotor can never drive the engine, so the instant the
/// engine quits the rotor is on its own - and the only thing keeping it turning is the
/// air coming up through the disc, which is to say the altitude you are spending. Every
/// autorotation in this game is that one line of logic plus honest aerodynamics.
///
/// <para><b>The governor is a component, not a law of nature.</b> It holds Nr by trimming
/// fuel, it anticipates the collective through a mechanical compensator, it has a droop
/// law, and every one of those can be worn out. A healthy governor makes the throttle a
/// switch; a sick one makes it a control; a failed one makes rotor speed the pilot's
/// problem, which is what it was before anybody fitted a governor. None of that is a
/// scripted penalty - the degraded governor is this same code with a slower valve, less
/// anticipation and less gain.</para>
///
/// <para><b>Starting is a sequence with a failure mode.</b> The starter can only motor the
/// compressor; combustion does the rest. Fuel introduced before there is air to burn it in
/// leaves as temperature instead of work, and a hot start is the one way to destroy this
/// engine while sitting still. The pilot's defences are the two a real pilot has: wait for
/// N1, and watch TOT with a hand on the fuel lever.</para>
/// </summary>
public sealed class Powerplant
{
    public EngineConfig Config { get; }
    public EngineState State { get; private set; } = EngineState.Off;

    /// <summary>Gas generator speed as a fraction of nominal, lagged behind demand.</summary>
    public double N1 { get; private set; }

    /// <summary>True while the pilot has the throttle rolled to flight idle.</summary>
    public bool FlightIdle { get; set; }

    /// <summary>
    /// Throttle lever, 0..1, as the pilot is holding it. Read only when the governor is not
    /// metering the fuel - which is the entire point of it.
    /// </summary>
    public double ThrottleLever { get; set; } = 1.0;

    /// <summary>
    /// Governor switch. AUTO by default, as it is on the real aircraft. Turning it off is
    /// how a pilot practises for the day it turns itself off.
    /// </summary>
    public bool GovernorSwitch { get; set; } = true;

    /// <summary>Set to false to kill the engine, e.g. fuel starvation or battle damage.</summary>
    public bool FuelAvailable { get; set; } = true;

    /// <summary>Starter engaged: the compressor is being motored.</summary>
    public bool StarterEngaged { get; private set; }

    /// <summary>Fuel lever off cutoff. During a start this is the pilot's one decision.</summary>
    public bool FuelValveOpen { get; private set; }

    /// <summary>Combustion. True from light-off onward, including during a start.</summary>
    public bool Lit { get; private set; }

    /// <summary>
    /// How much governor there is, 0..1, as the last call to <see cref="Update"/> was told.
    /// A fact, not a verdict: 1 is a fuel control in good order, 0 is a lever and a
    /// tachometer.
    /// </summary>
    public double GovernorAuthority { get; private set; } = 1.0;

    /// <summary>Whether the governor, rather than the pilot, is metering the fuel.</summary>
    public bool Governing { get; private set; } = true;

    /// <summary>
    /// Turbine outlet temperature as the powerplant sees it, deg C. The gauge the pilot
    /// reads is <c>DamageState.TurbineOutletTempC</c>, which lags the same fraction by the
    /// same three seconds; this copy exists so that a start overtemperature can be charged
    /// against the turbine by the component that knows a start is happening.
    /// </summary>
    public double TotC { get; private set; } = 15.0;

    private double _governorIntegral;
    private double _startTimer;
    private double _anticipated;      // lagged load torque, N.m
    private double _valve;            // fuel valve position, in torque units
    private double _lastTorqueFrac;
    private double _droopTorque;
    private bool _totPrimed;
    private double _startDamage;
    private bool _primed;
    private bool _valveStuck;
    private bool _autoFuel;

    public Powerplant(EngineConfig config) { Config = config; }

    /// <summary>
    /// Motor the compressor and introduce fuel when it is time: a start done by the book.
    /// The engine has no idea who is doing it, which is the point - this is the ground
    /// crew, or a pilot with a checklist, or the player pressing one button.
    /// </summary>
    public void Start()
    {
        EngageStarter();
        if (State == EngineState.Starting) _autoFuel = true;
    }

    /// <summary>Engage the starter. Nothing is burning yet.</summary>
    public void EngageStarter()
    {
        if (State is EngineState.Off or EngineState.Flameout)
        {
            State = EngineState.Starting;
            _startTimer = 0;
            _startDamage = 0;
            _autoFuel = false;
            StarterEngaged = true;
            FuelValveOpen = false;
            Lit = false;
        }
    }

    /// <summary>Fuel lever to idle. Light-off if there is air moving; a hot start if there is not enough.</summary>
    public void OpenFuelValve() => FuelValveOpen = true;

    /// <summary>Fuel lever to cutoff. The only thing that stops a hot start.</summary>
    public void CloseFuelValve()
    {
        FuelValveOpen = false;
        Lit = false;
        if (State == EngineState.Running) State = EngineState.Flameout;
    }

    /// <summary>Seconds since the starter was engaged. Zero when no start is in progress.</summary>
    public double StartElapsed => State == EngineState.Starting ? _startTimer : 0;

    public void Shutdown()
    {
        State = EngineState.Off;
        _governorIntegral = 0;
        StarterEngaged = false;
        FuelValveOpen = false;
        Lit = false;
    }

    public void Fail()
    {
        State = EngineState.Failed;
        Lit = false; FuelValveOpen = false; StarterEngaged = false;
    }

    public void Flameout()
    {
        if (State == EngineState.Running) { State = EngineState.Flameout; Lit = false; }
    }

    public void Reset()
    {
        State = EngineState.Off; N1 = 0; _governorIntegral = 0; _startTimer = 0;
        _anticipated = 0; _valve = 0; _lastTorqueFrac = 0; _droopTorque = 0; _primed = false;
        _startDamage = 0; TotC = 15.0; _totPrimed = false; _valveStuck = false;
        StarterEngaged = false; FuelValveOpen = false; Lit = false; _autoFuel = false;
        Governing = true; GovernorAuthority = 1.0;
    }

    /// <summary>Put the engine straight into a governed running state, for spawning in flight.</summary>
    public void SetRunning(double n1 = 0.75)
    {
        State = EngineState.Running;
        N1 = n1;
        _governorIntegral = 0;
        // Deliberately left unprimed. The first Update after this puts the anticipator and
        // the fuel valve wherever the load actually is, so a spawned aircraft starts in a
        // governed steady state rather than drooping its way into one. Without it, every
        // one of the hundreds of evaluations the trim solver makes would begin with the
        // fuel valve shut, and the solver would be differentiating a transient.
        _primed = false;
        _anticipated = 0; _valve = 0; _valveStuck = false;
        StarterEngaged = false; FuelValveOpen = true; Lit = true;
        _startDamage = 0; _autoFuel = false;
    }

    /// <param name="rotorOmega">Current main rotor speed, rad/s.</param>
    /// <param name="nominalOmega">Governed rotor speed at no load, rad/s.</param>
    /// <param name="loadTorque">Torque the rotors are currently demanding, N.m at the main shaft.</param>
    /// <param name="densityRatio">rho / rho_sea_level at the current altitude.</param>
    /// <param name="powerFactor">Engine health, 0..1, scaling available power.</param>
    /// <param name="torqueFactor">Transmission health, 0..1, scaling the torque limit.</param>
    /// <param name="governorAuthority">
    /// Fuel control health, 0..1. Below <see cref="EngineConfig.GovernorFailAuthority"/>
    /// the governor is out of the loop and the throttle lever is the fuel control.
    /// </param>
    /// <param name="ambientTempC">Outside air temperature, for turbine outlet temperature.</param>
    public PowerplantOutput Update(double rotorOmega, double nominalOmega, double loadTorque,
                                   double densityRatio, double dt,
                                   double powerFactor = 1.0, double torqueFactor = 1.0,
                                   double governorAuthority = 1.0, double ambientTempC = 15.0)
    {
        var cfg = Config;
        var o = new PowerplantOutput();

        if (!FuelAvailable && (State == EngineState.Running || Lit)) { Flameout(); Lit = false; }

        GovernorAuthority = Math.Clamp(governorAuthority, 0.0, 1.0);
        double q = GovernorAuthority;

        double powerAvailable = cfg.MaxContinuousPower
                                * Math.Pow(Math.Max(densityRatio, 0.05), cfg.DensityPowerExponent)
                                * Math.Clamp(powerFactor, 0.0, 1.2);
        double torqueLimit = cfg.TransmissionTorqueLimit * Math.Clamp(torqueFactor, 0.05, 1.2);
        o.PowerAvailable = powerAvailable;

        switch (State)
        {
            case EngineState.Starting:
                _startTimer += dt;
                StepStart(dt);
                break;
            case EngineState.Off:
            case EngineState.Flameout:
            case EngineState.Failed:
                N1 = Math.Max(0.0, N1 - dt / Math.Max(cfg.SpoolTime * 6.0, 1e-3));
                Lit = false;
                break;
        }

        // Turbine outlet temperature during a start is fuel over air, and nothing else.
        // In flight it is delivered over available power, filled in below once the torque
        // is known.
        double totFrac = State == EngineState.Starting && Lit ? StartMixture() : 0.0;

        if (State != EngineState.Running && State != EngineState.Starting)
        {
            AdvanceTot(totFrac, ambientTempC, dt);
            o.TotFraction = totFrac;
            o.ShaftTorque = 0;
            o.FreewheelEngaged = false;
            o.N1 = N1;
            o.GovernedOmega = nominalOmega;
            o.Governing = Governing = false;
            o.HotStartDamage = TakeStartDamage();
            return o;
        }

        // --- Governor --------------------------------------------------------
        //
        // A droop governor with a collective anticipator, which is what a turbine
        // helicopter actually has. Three things decide what the rotor does under a pull:
        // the anticipator (how much of the new load the fuel control knows about before
        // the rotor has slowed), the valve lag (how quickly it can act on what it knows),
        // and the acceleration schedule (how fast the gas generator can follow). Wearing
        // the first two out is precisely what "the governor is going" means, and it needs
        // no special case: it is these same three numbers with worse values.
        bool governed = GovernorSwitch && q > cfg.GovernorFailAuthority && State == EngineState.Running;
        bool idle = (governed && FlightIdle) || State == EngineState.Starting;
        Governing = governed && !idle;

        // Off the SMOOTHED torque, not the delivered one. A two-bladed rotor puts a
        // violent 2/rev into shaft torque (D-043), and feeding that into the governed
        // speed puts it into the rotor speed, into the thrust, and from there into every
        // residual the trim solver averages - which is exactly how a 60 kt trim that used
        // to null perfectly acquired a milli-g it could not get rid of.
        double governedOmega = nominalOmega
                               * (1.0 - cfg.DroopSlope
                                        * Math.Max(0.0, Math.Min(_droopTorque, 1.2) - cfg.DroopTopTorque));
        o.GovernedOmega = governedOmega;

        double torqueCommand;

        if (idle)
        {
            torqueCommand = cfg.IdlePowerFraction * powerAvailable / Math.Max(nominalOmega, 1.0);
            _governorIntegral = 0;
            _anticipated = loadTorque;
            _valve = torqueCommand;
        }
        else if (!governed)
        {
            // Manual throttle. The lever meters fuel, so what it buys is POWER, and torque
            // is whatever that power is worth at the rotor speed the pilot happens to have.
            // That one division is the whole of why manual throttle is work: droop the
            // rotor and the same lever makes more torque, which helps, but raise the
            // collective and nothing whatsoever comes to meet it.
            double frac = cfg.IdlePowerFraction
                          + (1.0 - cfg.IdlePowerFraction) * Math.Clamp(ThrottleLever, 0, 1);
            torqueCommand = frac * powerAvailable / Math.Max(rotorOmega, 1.0);
            _governorIntegral = 0;
            _anticipated = loadTorque;
            _valve = torqueCommand;
        }
        else
        {
            if (!_primed)
            {
                // Spawned into a governed steady state: put the valve and the anticipator
                // where a governor that had been carrying this load all along would have
                // them.
                _primed = true;
                _anticipated = loadTorque;
                _valve = loadTorque;
                // The droop law's input is a two-second-ish average, so it has to be
                // primed too, or a spawned aircraft spends the first seconds of its life
                // with a governed speed that is still finding out how hard it is working -
                // and the trim solver, which relaxes for a second and a half per
                // evaluation, would differentiate exactly that.
                _droopTorque = Math.Clamp(loadTorque / Math.Max(torqueLimit, 1e-6), 0, 1.2);
                // And so does the gas generator. SetRunning is told a plausible N1 by a
                // caller that cannot know what the rotor is about to ask for; if that
                // guess is low, the aircraft spawns with the engine spooling and the
                // rotor paying for it. Harmless in the air and ruinous in the trim
                // solver, which relaxes for a second and a half and would otherwise
                // solve every condition around a rotor that was still drooping - it
                // cost 0.013 of collective in the hover, all of it fictional.
                double ceilingNow = Math.Min(powerAvailable / Math.Max(rotorOmega, 1.0), torqueLimit);
                N1 = Math.Max(N1, 0.55 + 0.45 * Math.Clamp(loadTorque / Math.Max(ceilingNow, 1e-6), 0, 1));
            }

            double error = governedOmega - rotorOmega;

            double anticipationLag = Lerp(cfg.AnticipationLag, cfg.DegradedAnticipationLag, 1.0 - q);
            double valveLag = Lerp(cfg.GovernorLag, cfg.DegradedGovernorLag, 1.0 - q);
            // The gains barely move with condition; the lags do. That is deliberate and it
            // is what makes a tired governor hunt rather than merely sag: a fuel control
            // that still pushes as hard as it used to, through a valve that now takes a
            // second and a quarter to get there, is a loop with the same gain and far less
            // phase margin. Dropping the gain along with the valve speed - which the first
            // pass did - produces a governor that is soft and perfectly steady, which is
            // not a failure anybody has ever had.
            double gainP = cfg.GovernorP * (0.85 + 0.15 * q);
            double gainI = cfg.GovernorI * (0.30 + 0.70 * q);

            _governorIntegral += error * dt;
            _governorIntegral = Math.Clamp(_governorIntegral, -torqueLimit / gainI, torqueLimit / gainI);

            // The anticipator is a cam on the collective: it feeds the load forward before
            // the rotor has had time to slow down. A worn one lags by seconds and carries a
            // smaller share of the demand, and the rotor pays the difference in Nr.
            _anticipated += (loadTorque - _anticipated) * Rate(dt, anticipationLag);
            double demand = q * _anticipated + gainP * error + gainI * _governorIntegral;

            // Stick-slip on the way to the valve. Static friction holds it where it is
            // until the demand has walked far enough away to break it loose; once loose it
            // runs at the valve's own speed until it is nearly there, and sticks again.
            // The overshoot is the point: while it is stuck the integrator is still
            // winding, so by the time it moves it is chasing a number that has gone past.
            double stiction = cfg.GovernorStiction * (1.0 - q) * torqueLimit;
            double slip = demand - _valve;
            if (_valveStuck && Math.Abs(slip) > stiction) _valveStuck = false;
            if (!_valveStuck)
            {
                _valve += slip * Rate(dt, valveLag);
                if (Math.Abs(demand - _valve) < stiction * 0.15) _valveStuck = true;
            }
            torqueCommand = _valve;
        }

        double torqueCeilingPower = powerAvailable / Math.Max(rotorOmega, 1.0);
        double ceiling = Math.Min(torqueCeilingPower, torqueLimit);
        o.TorqueLimited = torqueCommand > ceiling;
        // Recorded before the clamp: what was asked for, not what was allowed.
        o.TorqueDemandPercent = Math.Max(0, torqueCommand) / Math.Max(torqueLimit, 1e-6);
        torqueCommand = Math.Clamp(torqueCommand, 0, ceiling);

        // --- Spool lag -------------------------------------------------------
        double n1Target = ceiling > 1e-6 ? Math.Clamp(torqueCommand / ceiling, 0, 1) : 0;
        n1Target = 0.55 + 0.45 * n1Target;
        // During a start the schedule owns N1 outright; the governor is not in the loop
        // until the engine is at idle.
        if (State == EngineState.Starting) n1Target = N1;
        double spool = n1Target > N1 ? cfg.SpoolTime * cfg.SpoolAccelFactor : cfg.SpoolTime;
        N1 += (n1Target - N1) * Rate(dt, spool);

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
        _lastTorqueFrac = o.TorquePercent;
        _droopTorque += (o.TorquePercent - _droopTorque) * Rate(dt, 0.8);
        o.FuelFlow = cfg.Sfc * Math.Max(o.PowerDelivered, powerAvailable * 0.05);
        o.N1 = N1;
        o.Governing = Governing;

        if (State == EngineState.Running)
            totFrac = o.PowerDelivered / Math.Max(powerAvailable, 1.0);

        AdvanceTot(totFrac, ambientTempC, dt);
        o.TotFraction = totFrac;
        o.HotStartDamage = TakeStartDamage();
        return o;
    }

    // -------------------------------------------------------------- the start

    /// <summary>
    /// One step of a start: the starter motors the compressor, fuel lights when it is
    /// introduced, and combustion takes the gas generator the rest of the way to idle.
    ///
    /// Everything about whether the start is a good one is in the order of those events.
    /// The starter gets N1 to about thirty percent and no further, so waiting costs
    /// nothing; fuel opened at fifteen percent burns in enough air to keep TOT near 500 C;
    /// fuel opened at five percent has a third of the air it needs and puts the needle past
    /// the red line inside three seconds.
    /// </summary>
    private void StepStart(double dt)
    {
        var cfg = Config;

        if (_autoFuel && !FuelValveOpen && N1 >= cfg.StartFuelN1) FuelValveOpen = true;

        if (StarterEngaged)
        {
            N1 += (cfg.MotoringN1 - N1) * Rate(dt, 0.45 * cfg.StartTime);
            if (N1 >= cfg.StarterDropoutN1) StarterEngaged = false;
        }

        if (!FuelValveOpen || !FuelAvailable) Lit = false;
        else if (!Lit && N1 >= cfg.LightOffN1) Lit = true;

        if (Lit)
        {
            // Only the part of the fire that is burning in the right place accelerates
            // anything.
            double useful = Math.Pow(Math.Clamp(N1 / cfg.StartFuelFraction, 0, 1),
                                     cfg.StartCombustionExponent);
            N1 += (cfg.IdleN1 * 1.13 - N1) * Rate(dt, 0.35 * cfg.StartTime) * useful;
            // And the part that is not accelerating anything is actively in the way.
            N1 = Math.Max(0.0, N1 - cfg.StartRichDrag * (1.0 - useful) * dt);
            if (N1 >= cfg.IdleN1)
            {
                State = EngineState.Running;
                StarterEngaged = false;
                _governorIntegral = 0;
                _primed = false;
            }
        }
        else if (!StarterEngaged)
        {
            // Starter dropped out or was never engaged, with nothing burning: the
            // compressor runs down and the start is over.
            N1 = Math.Max(0.0, N1 - dt / Math.Max(cfg.SpoolTime * 6.0, 1e-3));
            if (N1 <= 0.01) State = EngineState.Off;
        }
    }

    /// <summary>
    /// Lag the hot section's temperature and charge an overtemperature start against the
    /// turbine. Only during a start: a running engine over temperature is
    /// <c>DamageState</c>'s business, and it is already doing it.
    /// </summary>
    private void AdvanceTot(double totFrac, double ambientTempC, double dt)
    {
        // Lagged as a temperature from ambient, which is what DamageState does with the
        // same fraction, so that this copy and the needle on the panel are the same number.
        // Lagging the fraction instead - the first version - left this reading 265 C on a
        // cold aircraft, because a fraction of zero still means a lit engine at idle, and
        // charged a hot start against an engine whose gauge had not moved.
        double target = Lit ? DamageState.TotTargetC(totFrac, ambientTempC) : ambientTempC;
        if (!_totPrimed) { _totPrimed = true; TotC = target; }
        TotC += (target - TotC) * Rate(dt, 3.0);

        if (State == EngineState.Starting && Lit)
        {
            double over = TotC - DamageState.TotLimitC;
            if (over > 0) _startDamage += over / 100.0 * Config.HotStartWear * dt;
        }
    }

    /// <summary>
    /// Hand back the accumulated start damage on the step the start finishes, and only
    /// then, so that the aircraft's log gets one entry saying the engine was cooked rather
    /// than four hundred saying it is being cooked.
    /// </summary>
    private double TakeStartDamage()
    {
        if (_startDamage <= 0) return 0;
        if (State == EngineState.Starting && Lit) return 0;
        double d = _startDamage;
        _startDamage = 0;
        return d;
    }

    /// <summary>
    /// How rich the combustor is, as the fraction <c>DamageState</c> turns into a
    /// temperature. One at the schedule's design point, and climbing steeply below it.
    /// </summary>
    private double StartMixture()
        => Math.Pow(Config.StartFuelFraction / Math.Max(N1, 0.02), Config.StartRichnessExponent);

    private static double Rate(double dt, double tau) => 1.0 - Math.Exp(-dt / Math.Max(tau, 1e-4));
    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
}
