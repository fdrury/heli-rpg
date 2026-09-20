namespace Rotorwash.Sim;

public enum Component
{
    MainRotor,
    TailRotor,
    Engine,
    Transmission,
    Skids,
    FuelSystem,
    Hydraulics,
    Avionics,
    Fuselage,
}

/// <summary>
/// How a component got hurt. Drives repair cost, repair skill and flavour.
///
/// Appended to, never reordered: <c>SaveData.DamageEventSave</c> persists this enum, so a
/// value's numeric position is part of the save format.
/// </summary>
public enum DamageCause
{
    Wear, HardLanding, RotorStrike, Rollover, Impact, Gunfire, Fragment,
    Overtorque, Overspeed, Heat,
    /// <summary>Running a gearbox or an engine with the oil pressure gone.</summary>
    OilStarvation,
    /// <summary>Chafed lines and cracked fittings, from flying an out-of-track rotor.</summary>
    Vibration,
}

public readonly record struct DamageEvent(Component Component, double Amount, DamageCause Cause, string Note);

/// <summary>
/// One instrument, as the panel presents it: a number, a unit, and the two marks painted
/// on the dial. No verdict - the marks are where the manufacturer put them, and reading
/// across them is the pilot's job (D-005a).
/// </summary>
/// <param name="Caution">Where the yellow arc starts. NaN if the dial has no yellow.</param>
/// <param name="Limit">Where the red line is. NaN if the dial has no red.</param>
/// <param name="Rising">True if the marks are an upper bound, false if they are a lower one.</param>
public readonly record struct Gauge(string Name, double Value, string Unit,
                                    double Caution, double Limit, bool Rising)
{
    /// <summary>Past the yellow mark. A fact about the needle, not a judgement.</summary>
    public bool InCaution => !double.IsNaN(Caution) && (Rising ? Value >= Caution : Value <= Caution);

    /// <summary>Past the red line.</summary>
    public bool PastLimit => !double.IsNaN(Limit) && (Rising ? Value >= Limit : Value <= Limit);
}

/// <summary>
/// What the aircraft is being asked to do this instant. Everything that makes damage get
/// worse is a function of these, which is why shutting down stops it: with the rotor
/// stopped, every one of them is zero.
/// </summary>
/// <param name="RotorFraction">Nr / nominal. Drives the oil pumps and the accessory gearbox.</param>
/// <param name="TorqueFraction">Delivered shaft torque / transmission limit.</param>
/// <param name="ShaftPowerW">Power going through the main gearbox, W. This is the heat source.</param>
/// <param name="PowerFraction">Delivered / available engine power. This is what sets TOT.</param>
/// <param name="AmbientTempC">Outside air temperature at the current altitude.</param>
/// <param name="EngineRunning">Combustion, as opposed to windmilling or shut down.</param>
public readonly record struct SystemLoad(
    double RotorFraction,
    double TorqueFraction,
    double ShaftPowerW,
    double PowerFraction,
    double AmbientTempC,
    bool EngineRunning);

/// <summary>
/// Component health for one aircraft, the fluids and temperatures that health shows up
/// in, and the effect all of it has on how the machine flies.
///
/// This is the spine of the whole game. The aircraft is the second protagonist (pillar 1),
/// progression is the parts you find (D-005), the economy is what it costs to keep it
/// airworthy (D-007), and landing is the expensive act (D-003a). All of that is this class.
///
/// <para><b>Two layers, and the distinction matters.</b> <see cref="Health"/> is the slow,
/// permanent record of what has been done to a part; it only ever goes down in flight and
/// only a spanner puts it back. The <i>system state</i> - oil quantity, oil pressure, oil
/// temperature, turbine outlet temperature, hydraulic pressure, vibration - is fast, it is
/// what the gauges read, and most of it recovers when you land. Health is the diagnosis;
/// the system state is the symptom. The pilot never sees the diagnosis.</para>
///
/// <para><b>Why the system state exists at all.</b> Before it, every component failed
/// independently and linearly: a hit took 30% off a number and the number stayed there.
/// Nothing got worse, nothing caused anything else, and the only way to know how bad
/// things were was to read the health value - which is a score, and D-005a says no scores.
/// The fluids fix all three at once. A gearbox losing oil gets hotter over minutes, the
/// temperature is a fact on a dial, the heat then eats the gearbox, and stopping the rotor
/// stops all of it. That is a clock the pilot can act on rather than a verdict he can only
/// read.</para>
///
/// <para><b>Every effect is still a multiplier on something the flight model already
/// computes.</b> Nothing here is a scripted penalty: a bent skid changes the geometry, a
/// nicked blade changes the mass balance, a tired engine changes available power, an empty
/// hydraulic reservoir changes how fast the actuators move - and the aerodynamics work out
/// the consequences.</para>
///
/// <para><b>Not saved, on purpose.</b> <c>SaveData</c> persists health and the damage log,
/// not the fluids and temperatures. That follows from D-036: saving is parked-only, and a
/// parked aircraft is cold with whatever the mechanic put back into it. The leak itself
/// lives in the health number, so the clock restarts the moment the next sortie pulls
/// pitch. <see cref="ResetSystems"/> is the call that makes that true.</para>
/// </summary>
public sealed class DamageState
{
    // ----------------------------------------------------- unserviceable floors
    //
    // The health at which a component stops doing its job. Promoted out of the
    // expressions in Airworthy / SkidsServiceable / AvionicsWorking so that Salvage's
    // hours-remaining arithmetic and this file cannot drift apart: "hours left" has to
    // mean "hours until this stops doing its job" or it is a flavour number.

    public const double MainRotorFloor    = 0.25;
    public const double TailRotorFloor    = 0.25;   // below this it can only be flown fast
    public const double EngineFloor       = 0.15;
    public const double TransmissionFloor = 0.20;
    public const double SkidsFloor        = 0.25;
    public const double FuelSystemFloor   = 0.25;   // 0.064 kg/s out of the tank
    public const double HydraulicsFloor   = 0.30;
    public const double AvionicsFloor     = 0.35;
    public const double FuselageFloor     = 0.20;   // drag x1.44

    // ------------------------------------------------------ rotor ceiling
    //
    // The repair ceiling on the main rotor (story.md §1.3). Repair can
    // restore health up to this limit and no further. The ceiling falls
    // with total flight hours, flooring at 0.55 — a flyable, unpleasant
    // aircraft that never kills you and never locks you out.

    /// <summary>Health lost per rotor-turning hour. 240 h drains 0.45.</summary>
    public const double CeilingRate  = 0.0019;
    /// <summary>The ceiling never falls below this. Still flyable, just rough.</summary>
    public const double CeilingFloor = 0.55;

    /// <summary>The health at which a component stops doing its job.</summary>
    public static double UnserviceableAt(Component c) => c switch
    {
        Component.MainRotor    => MainRotorFloor,
        Component.TailRotor    => TailRotorFloor,
        Component.Engine       => EngineFloor,
        Component.Transmission => TransmissionFloor,
        Component.Skids        => SkidsFloor,
        Component.FuelSystem   => FuelSystemFloor,
        Component.Hydraulics   => HydraulicsFloor,
        Component.Avionics     => AvionicsFloor,
        Component.Fuselage     => FuselageFloor,
        _ => 0.20,
    };

    // ---------------------------------------------------------- panel markings
    //
    // Huey-class numbers, named rather than buried in expressions because the tests
    // assert against them and the warning panel prints them.

    /// <summary>Main gearbox oil, red line, deg C.</summary>
    public const double XmsnOilTempLimitC = 110.0;
    /// <summary>Main gearbox oil, yellow arc, deg C.</summary>
    public const double XmsnOilTempCautionC = 100.0;
    /// <summary>Main gearbox oil pressure caution switch, psi.</summary>
    public const double XmsnOilPressCautionPsi = 30.0;
    public const double XmsnOilPressNominalPsi = 65.0;

    /// <summary>Turbine outlet temperature, red line, deg C.</summary>
    public const double TotLimitC = 625.0;
    public const double TotCautionC = 590.0;

    public const double EngineOilTempLimitC = 107.0;
    public const double EngineOilPressCautionPsi = 25.0;
    public const double EngineOilPressNominalPsi = 90.0;

    public const double HydraulicPressNominalPsi = 3000.0;
    public const double HydraulicPressCautionPsi = 1000.0;

    /// <summary>Rotor track-and-balance limit, inches per second.</summary>
    public const double VibrationCautionIps = 0.60;
    /// <summary>Vibration above which lines chafe and fittings crack, inches per second.</summary>
    public const double VibrationDamagingIps = 0.60;

    // ------------------------------------------------------- tuning constants

    /// <summary>
    /// Thermal capacity of the oil and the metal it is in contact with, J/K. About 11 kg
    /// of oil plus the gear train it wets.
    ///
    /// Deliberately NOT the mass of the whole gearbox. The first pass used 75 kJ/K - oil
    /// plus the entire magnesium case - and it made the temperature gauge useless: a
    /// gearbox that had lost all its oil took thirty-four minutes to reach the red line,
    /// by which time the oil pressure switch had been the warning for twenty of them, and
    /// the intended ladder (temperature first, pressure much later) was exactly backwards.
    /// The case is cooled by 100 kt of air and is not part of the oil's thermal inertia.
    /// </summary>
    private const double XmsnThermalCapacity = 25_000.0;
    /// <summary>Fraction of transmitted power that ends up as heat in the oil. Gear meshes are ~98% efficient.</summary>
    private const double XmsnHeatFraction = 0.018;
    /// <summary>
    /// Heat the case sheds on its own, W/K, with no oil circulating. This is the number
    /// that decides how fast a dry gearbox runs away, and how fast it cools once stopped.
    /// </summary>
    private const double XmsnCaseCoolingWPerK = 45.0;
    /// <summary>Oil cooler capacity with the thermostat fully open and a full system, W/K.</summary>
    private const double XmsnCoolerWPerK = 420.0;
    /// <summary>Thermostat cracks here and is fully open 12 K later, deg C.</summary>
    private const double XmsnThermostatOpenC = 78.0;
    /// <summary>The pylon runs warmer than free air.</summary>
    private const double PylonTempRiseC = 10.0;
    /// <summary>Gearbox oil lost per second at zero health, as a fraction of capacity.</summary>
    private const double XmsnOilLossRate = 0.010;
    /// <summary>Oil fraction below which the cooler circuit starts to lose effectiveness.</summary>
    private const double XmsnCoolingKneeFraction = 0.55;
    /// <summary>Oil fraction below which the pump pickup unports and pressure falls.</summary>
    private const double XmsnPressureKneeFraction = 0.30;

    /// <summary>Hydraulic fluid lost per second at zero health, as a fraction of reservoir.</summary>
    private const double HydraulicLossRate = 0.020;
    /// <summary>Reservoir fraction below which pressure collapses. Above it the pump keeps up with the leak.</summary>
    private const double HydraulicPressureKneeFraction = 0.15;

    /// <summary>Torque fraction above which continuous running consumes gearbox life.</summary>
    private const double XmsnContinuousTorqueFraction = 0.90;

    /// <summary>Health lost before continuous wear is written into the log as one event.</summary>
    private const double LogGranularity = 0.01;

    // ------------------------------------------------------------------ state

    private readonly double[] _health;
    private readonly double[] _pending;
    private readonly DamageCause[] _pendingCause;
    private readonly string[] _pendingNote;
    private readonly List<DamageEvent> _log = new();

    private double _xmsnOil = 1.0;
    private double _xmsnOilTempC = 15.0;
    private double _xmsnTempTrendPerSec;
    private double _hydFluid = 1.0;
    private double _totC = 15.0;
    private double _engineOilTempC = 15.0;
    private double _engineN1;
    private double _vibrationIps;
    private double _lastRotorFraction;
    private double _rotorSeconds;
    private double _totalFlightHours;
    private bool _chipLight;
    private bool _primed;

    public DamageState()
    {
        int n = System.Enum.GetValues<Component>().Length;
        _health = new double[n];
        _pending = new double[n];
        _pendingCause = new DamageCause[n];
        _pendingNote = new string[n];
        for (int i = 0; i < n; i++) { _health[i] = 1.0; _pendingNote[i] = ""; }
    }

    /// <summary>0 = destroyed, 1 = serviceable.</summary>
    public double Health(Component c) => _health[(int)c];

    public IReadOnlyList<DamageEvent> Log => _log;

    /// <summary>Raised whenever a component takes damage. The game uses it for audio and UI.</summary>
    public event System.Action<DamageEvent>? Damaged;

    public void Apply(Component c, double amount, DamageCause cause, string note = "")
    {
        if (amount <= 0) return;
        double before = _health[(int)c];
        _health[(int)c] = System.Math.Clamp(before - amount, 0.0, 1.0);
        var e = new DamageEvent(c, amount, cause, note);
        _log.Add(e);
        Damaged?.Invoke(e);
    }

    /// <summary>
    /// Damage arriving continuously rather than as an event.
    ///
    /// Health moves immediately, because the flight model reads it every step and a
    /// staircase would be visible in the torque trace. The <i>log entry</i> waits until a
    /// hundredth of health has accumulated, because the alternative is 240 log lines a
    /// second, a save file that grows without bound, and a Damaged event that fires
    /// continuously and therefore tells the audio system nothing.
    /// </summary>
    private void Erode(Component c, double amount, DamageCause cause, string note)
    {
        if (amount <= 0) return;
        int i = (int)c;
        double before = _health[i];
        _health[i] = System.Math.Clamp(before - amount, 0.0, 1.0);
        double actual = before - _health[i];
        if (actual <= 0) return;

        _pending[i] += actual;
        _pendingCause[i] = cause;
        _pendingNote[i] = note;
        if (_pending[i] >= LogGranularity) FlushPending(i);
    }

    private void FlushPending(int i)
    {
        double amount = _pending[i];
        if (amount <= 0) return;
        _pending[i] = 0;
        var e = new DamageEvent((Component)i, amount, _pendingCause[i], _pendingNote[i]);
        _log.Add(e);
        Damaged?.Invoke(e);
    }

    /// <summary>
    /// Fit a part or do the work. Tops up the fluid that belongs to the component
    /// repaired, and nothing else - putting a radio in does not refill the gearbox.
    /// </summary>
    public void Repair(Component c, double amount)
    {
        double cap = c == Component.MainRotor ? MainRotorCeiling : 1.0;
        _health[(int)c] = System.Math.Clamp(_health[(int)c] + amount, 0.0, cap);
        _pending[(int)c] = 0;
        if (c == Component.Transmission)
        {
            _xmsnOil = 1.0;
            // A chip detector is cleared by opening the gearbox up, finding out what the
            // metal was, and signing it off. A top-up is not that.
            if (_health[(int)c] > 0.95) _chipLight = false;
        }
        if (c == Component.Hydraulics) _hydFluid = 1.0;
    }

    public void RepairAll()
    {
        double ceiling = MainRotorCeiling;
        for (int i = 0; i < _health.Length; i++)
        {
            double cap = i == (int)Component.MainRotor ? ceiling : 1.0;
            _health[i] = cap;
            _pending[i] = 0;
        }
        _chipLight = false;
        ResetSystems();
    }

    /// <summary>
    /// Put the fluids and temperatures back to a cold, serviced aircraft.
    ///
    /// Called after a repair and after a load. Nothing calls it in flight: if the oil is
    /// gone, the only way to get it back is to land somewhere that has oil.
    /// </summary>
    public void ResetSystems()
    {
        _xmsnOil = 1.0;
        _hydFluid = 1.0;
        _xmsnOilTempC = 15.0;
        _engineOilTempC = 15.0;
        _totC = 15.0;
        _engineN1 = 0.0;
        _xmsnTempTrendPerSec = 0;
        _vibrationIps = 0;
        _lastRotorFraction = 0;
        _rotorSeconds = 0;
        _primed = false;
    }

    // ------------------------------------------------------------- save / load

    /// <summary>Set a component's health directly. Save/load only.</summary>
    public void SetHealth(Component c, double h) => _health[(int)c] = System.Math.Clamp(h, 0.0, 1.0);

    /// <summary>Replace the damage log wholesale. Save/load only.</summary>
    public void RestoreLog(System.Collections.Generic.IEnumerable<DamageEvent> events)
    {
        _log.Clear();
        _log.AddRange(events);
        ResetSystems();
    }

    // ============================================================== the systems

    /// <summary>
    /// Advance the fluids, the temperatures, and the damage they cause. One call per
    /// flight-model step, at the end of it, with what the aircraft actually did.
    ///
    /// <para><b>The couplings modelled here, and why each one is real.</b></para>
    ///
    /// <para><b>1. Gearbox oil -> heat -> the gearbox.</b> A cracked case or a holed cooler
    /// line loses oil. Oil quantity is not on the panel of a machine this old, so the first
    /// thing the pilot sees is the temperature creeping up as the cooler loses circulation;
    /// much later the pressure switch trips as the pump unports; then the chip light comes
    /// on as the gears start making metal. Running a gearbox hot and dry <i>under load</i>
    /// is what destroys it. Running it hot and dry with the rotor stopped destroys nothing,
    /// which is exactly why the answer to this failure is to land.</para>
    ///
    /// <para><b>2. Engine -> collective -> gearbox.</b> Not scripted, deliberately. Gearbox
    /// heat is a fixed fraction of the power going <i>through</i> it, and a sick engine
    /// makes the pilot (or the governor) hold more collective for the same flight
    /// condition, which is more power through the gearbox, which is more heat. The coupling
    /// falls out of the flight model; all this file does is refuse to fake it.</para>
    ///
    /// <para><b>3. Engine -> its own hot section.</b> A tired turbine makes less power, so
    /// the same task needs a higher fraction of what is left, so TOT is higher; and TOT
    /// above the red line is precisely what eats turbine blades. That is a genuine
    /// self-reinforcing loop, and the way out of it is to ask for less power - a real
    /// decision with a real cost, because you give up climb, or speed, or cargo.</para>
    ///
    /// <para><b>4. Hydraulics: a cliff, not a slope.</b> The old model made a 40% hydraulic
    /// hit mean 40% slower controls immediately and forever. Real hydraulics do not behave
    /// like that: the pump keeps up with a leak until the reservoir unports, and then
    /// everything goes at once. So the leak is the clock, the pressure gauge is the
    /// warning, and the consequence - slow actuators and no stability augmentation, the
    /// pairing already in this file - arrives as a step. The leak only runs while the rotor
    /// turns, because the pump is on the transmission accessory pad.</para>
    ///
    /// <para><b>5. Gearbox -> hydraulics.</b> Same accessory pad. A gearbox coming apart
    /// takes the hydraulic pump drive with it, so a pilot who flies on with a dying
    /// transmission loses the controls as well. Applied as mechanical damage with a cause,
    /// not as a pressure fudge, because that is what is physically happening.</para>
    ///
    /// <para><b>6. Rotor -> everything bolted to the airframe.</b> An out-of-track rotor
    /// shakes the machine at 1/rev. Vibration is the classic killer of hydraulic lines,
    /// fuel lines and airframe fittings, and it is slow: a badly damaged rotor costs a few
    /// hundredths of health per ten minutes in the systems around it. Enough to make "get
    /// it home" the right call, not enough to be a death sentence (D-007).</para>
    ///
    /// <para><b>7. Fuel system -> engine.</b> Below about a quarter health the boost pumps
    /// and lines can no longer keep the fuel control satisfied and available power falls -
    /// which feeds straight back into couplings 2 and 3.</para>
    ///
    /// <para><b>8. Hours.</b> On top of all of the above, every component accumulates plain
    /// rotor-turning wear on <c>Salvage</c>'s curve. That is the baseline demand side of
    /// the parts economy, and it is <i>additional</i> to the consequential damage above: a
    /// gearbox that cooked itself has both the heat damage and the hours on it.</para>
    /// </summary>
    public void UpdateSystems(in SystemLoad load, double dt)
    {
        // dt is NOT clamped. The first pass clamped it to 0.5 s as a guard against a
        // pathological frame, and the effect was that the test bench, stepping this at one
        // second to cover twenty minutes, silently ran every clock at half speed - a
        // gearbox that should have been unserviceable in thirteen minutes took
        // thirty-seven, and the number looked plausible enough to believe. Nothing here
        // needs the guard: the lags are exponentials, which are unconditionally stable at
        // any step, and the one Euler integration (oil temperature) is stable to a step of
        // about a hundred seconds at the stiffest cooling this model can produce.
        if (dt <= 0) return;

        double rotorFrac = System.Math.Clamp(load.RotorFraction, 0, 1.5);
        double torqueFrac = System.Math.Max(0, load.TorqueFraction);
        double shaftPower = System.Math.Max(0, load.ShaftPowerW);
        // Clamped at 2.5, not 1.2. In flight nothing can exceed 1 - you cannot deliver more
        // power than the engine has - but during a start this is fuel over airflow, and the
        // whole of the hot-start failure mode lives above 1.
        double powerFrac = System.Math.Clamp(load.PowerFraction, 0, 4.0);
        double ambient = load.AmbientTempC;
        _lastRotorFraction = rotorFrac;

        // Cold start. A freshly constructed DamageState has its temperatures at 15 C, which
        // is a lie for an aircraft spawned into the cruise, and a lie that takes minutes to
        // work itself out of the gauges. Prime to normal operating temperature if the rotor
        // is already turning on the first step, and to ambient if it is not - an aircraft
        // that starts on the ramp really is cold.
        //
        // Primed to the thermostat band rather than to the steady state of the load, which
        // is what the first pass did: on the first step of a spawned-in-flight aircraft the
        // engine has only just been told it is running and the power through the gearbox is
        // whatever the first frame happened to produce, so that "steady state" was noise.
        if (!_primed)
        {
            _primed = true;
            bool turning = rotorFrac > 0.5;
            _xmsnOilTempC = turning ? XmsnThermostatOpenC + 4.0 : ambient;
            _totC = load.EngineRunning ? TotTargetC(powerFrac, ambient) : ambient;
            _engineOilTempC = ambient + 0.15 * (_totC - ambient);
            _engineN1 = load.EngineRunning ? System.Math.Max(0.55, powerFrac) : 0.0;
        }

        double xmsnHealth = _health[(int)Component.Transmission];
        double engineHealth = _health[(int)Component.Engine];
        double hydHealth = _health[(int)Component.Hydraulics];
        double rotorHealth = _health[(int)Component.MainRotor];
        double tailHealth = _health[(int)Component.TailRotor];

        // --- 1a. Gearbox oil quantity ----------------------------------------
        // Quadratic in the damage so that ordinary wear does not leak at all. The
        // difference between "this is wear" and "this is a problem" has to be visible in
        // the gauges, not only in a health number the player is never shown.
        double xmsnHole = 1.0 - xmsnHealth;
        double oilLoss = xmsnHole * xmsnHole * XmsnOilLossRate * (0.05 + 0.95 * rotorFrac);
        _xmsnOil = System.Math.Max(0.0, _xmsnOil - oilLoss * dt);

        // --- 1b. Gearbox temperature -----------------------------------------
        //
        // A thermostatic oil cooler, because without one the temperature gauge is just a
        // power gauge with different units: heat in is proportional to transmitted power,
        // so a fixed cooler would put the oil at 82 C in the cruise and 130 C at max
        // continuous, and a pilot would learn to read the needle as "how much collective
        // am I holding". Real oil systems bypass the cooler until the oil is warm and then
        // regulate, which is why a healthy gearbox sits in the same narrow band all day
        // whatever it is doing - and why the needle moving at all means something is wrong.
        double heatIn = XmsnHeatFraction * shaftPower;
        double ambientPylon = ambient + PylonTempRiseC * rotorFrac;
        double cooling = XmsnCooling(_xmsnOilTempC, _xmsnOil, rotorFrac);
        double dTemp = (heatIn - cooling * (_xmsnOilTempC - ambientPylon)) / XmsnThermalCapacity;
        _xmsnOilTempC += dTemp * dt;

        // Readable trend for the gauge. 15 s of smoothing: fast enough to show the needle
        // move when the oil goes, slow enough that it is not noise.
        _xmsnTempTrendPerSec += (dTemp - _xmsnTempTrendPerSec) * (1.0 - System.Math.Exp(-dt / 15.0));

        // --- 1c. What the heat and the missing oil do to the gearbox ----------
        // Both terms are multiplied by load, and that is the whole design. Metal comes off
        // gear teeth when they are pressed together without a film of oil between them.
        // With the rotor stopped there is no pressure on the teeth and the damage stops,
        // whatever the thermometer says. This is what makes "where can I put down" the
        // question the system is asking, instead of "how long until I lose".
        double overTemp = System.Math.Max(0, _xmsnOilTempC - XmsnOilTempLimitC) / 25.0;
        double starvation = System.Math.Max(0, 1.0 - XmsnOilPressurePsi / XmsnOilPressCautionPsi);
        // Rotor speed gates the whole thing, not just scales it. Stopped gears touching
        // each other with no load on them do not shed metal, however hot they are - and a
        // small residual "it is still a bit damaging" term would have made landing merely
        // slower than flying instead of a full stop, which is not the promise.
        double loadTerm = System.Math.Min(rotorFrac, 1.0) * (0.05 + 0.95 * System.Math.Min(torqueFrac, 1.2));
        double xmsnDistress = (overTemp * 0.00064 + starvation * 0.00096) * loadTerm;
        if (xmsnDistress > 0)
        {
            _chipLight = true;
            bool dry = starvation > overTemp;
            Erode(Component.Transmission, xmsnDistress * dt,
                  dry ? DamageCause.OilStarvation : DamageCause.Heat,
                  dry ? "gearbox run without oil pressure" : "gearbox run over temperature");
        }

        // --- 1d. Continuous operation at the placarded torque -----------------
        // The placard is a continuous rating, not a wall. Sitting on it is what consumes
        // gearbox life, and that is the price of an overloaded aircraft on a hot day.
        double overRated = System.Math.Max(0, System.Math.Min(torqueFrac, 1.3) - XmsnContinuousTorqueFraction);
        if (overRated > 0)
            Erode(Component.Transmission, overRated * 0.0012 * dt, DamageCause.Overtorque,
                  "held at the transmission limit");

        // --- 2. Rotor overspeed ------------------------------------------------
        if (rotorFrac > 1.12)
            Erode(Component.MainRotor, (rotorFrac - 1.12) * 0.05 * dt, DamageCause.Overspeed,
                  "rotor overspeed");

        // --- 3. Engine hot section ---------------------------------------------
        _engineN1 += ((load.EngineRunning ? System.Math.Max(0.55, powerFrac) : 0.0) - _engineN1)
                     * (1.0 - System.Math.Exp(-dt / 2.0));
        double totTarget = load.EngineRunning ? TotTargetC(powerFrac, ambient) : ambient;
        // A worn hot section runs hotter for the same work: tip clearances open up, the
        // compressor fouls, and more fuel has to be burned for the same shaft power.
        totTarget = ambient + (totTarget - ambient) * (1.0 + 0.25 * (1.0 - engineHealth));
        _totC += (totTarget - _totC) * (1.0 - System.Math.Exp(-dt / 3.0));

        // 0.00030, not the 0.00056 of the first pass. At the higher figure a 0.55 engine
        // held at full power was unserviceable in four minutes, and because the loop feeds
        // itself - hotter engine, worse engine, hotter still - it ran all the way to zero
        // inside a five minute measurement. Four minutes is a gotcha; D-007 asks for a
        // clock. Eight is long enough to notice the TOT gauge, decide, and act.
        double overTot = System.Math.Max(0, _totC - TotLimitC) / 25.0;
        if (overTot > 0 && load.EngineRunning)
            Erode(Component.Engine, overTot * 0.00030 * dt, DamageCause.Heat,
                  "turbine run over temperature");

        double engOilTarget = ambient + 0.15 * (_totC - ambient);
        _engineOilTempC += (engOilTarget - _engineOilTempC) * (1.0 - System.Math.Exp(-dt / 60.0));

        // --- 4. Hydraulic fluid -------------------------------------------------
        double hydHole = 1.0 - hydHealth;
        double hydLoss = hydHole * hydHole * HydraulicLossRate * rotorFrac;
        _hydFluid = System.Math.Max(0.0, _hydFluid - hydLoss * dt);

        // --- 5. A failing gearbox takes the accessory drive with it -------------
        if (xmsnHealth < TransmissionFloor + 0.05 && rotorFrac > 0.2)
            Erode(Component.Hydraulics, (TransmissionFloor + 0.05 - xmsnHealth) * 0.005 * dt,
                  DamageCause.Wear, "hydraulic pump drive, off a failing gearbox");

        // --- 6. Vibration -------------------------------------------------------
        double vib = (0.12
                      + 2.80 * System.Math.Pow(1.0 - rotorHealth, 1.4)
                      + 0.90 * (1.0 - xmsnHealth) * (1.0 - xmsnHealth)
                      + 0.60 * (1.0 - tailHealth) * (1.0 - tailHealth))
                     * rotorFrac * rotorFrac;
        _vibrationIps += (vib - _vibrationIps) * (1.0 - System.Math.Exp(-dt / 1.5));

        double chafe = System.Math.Max(0, _vibrationIps - VibrationDamagingIps);
        if (chafe > 0)
        {
            Erode(Component.Hydraulics, chafe * 0.000060 * dt, DamageCause.Vibration, "chafed hydraulic line");
            Erode(Component.FuelSystem, chafe * 0.000045 * dt, DamageCause.Vibration, "cracked fuel fitting");
            Erode(Component.Fuselage,   chafe * 0.000030 * dt, DamageCause.Vibration, "cracked airframe fitting");
        }

        // --- 8. Hours -----------------------------------------------------------
        // Counted here, charged at shutdown. See AccrueFlightHours for why.
        if (rotorFrac > 0.5) _rotorSeconds += dt;
        else if (rotorFrac < 0.2) AccrueFlightHours();
    }

    /// <summary>
    /// Rotor-turning time since the hours were last booked, seconds.
    /// This is the aircraft's Hobbs meter between entries in the log.
    /// </summary>
    public double RotorTurningSeconds => _rotorSeconds;

    /// <summary>
    /// Total flight hours since the rotor was last tracked. Accumulated in
    /// <see cref="AccrueFlightHours"/> and persisted through save/load. The kneeboard
    /// THREAD page shows this as "N h since track" alongside the rotor ceiling.
    /// </summary>
    public double TotalFlightHours
    {
        get => _totalFlightHours;
        set => _totalFlightHours = value;
    }

    /// <summary>
    /// The maximum health that <see cref="Repair"/> can restore the main rotor to.
    /// Falls with flight hours at <see cref="CeilingRate"/> per hour, flooring at
    /// <see cref="CeilingFloor"/>. New blades (<see cref="ResetRotorHours"/>) reset
    /// it to 1.0. Story.md §1.3.
    /// </summary>
    public double MainRotorCeiling
        => System.Math.Clamp(1.00 - CeilingRate * _totalFlightHours, CeilingFloor, 1.00);

    /// <summary>
    /// New blades installed: zero the hours, restore the ceiling to 1.0.
    /// This is the only event in the game that does this (story.md §1.3).
    /// </summary>
    public void ResetRotorHours()
    {
        _totalFlightHours = 0;
        _rotorSeconds = 0;
    }

    /// <summary>
    /// Charge the accumulated rotor-turning time to every component, on
    /// <c>Salvage</c>'s accelerating wear curve. Returns the hours booked.
    ///
    /// <para>Called automatically the moment the rotor stops, and callable by hand at a
    /// save point. <b>Not</b> applied continuously in flight, and that was learned the
    /// hard way.</para>
    ///
    /// <para>The first version ticked every thirty rotor-seconds from inside
    /// <see cref="UpdateSystems"/>. It looked harmless - after a minute of flying the main
    /// rotor was at 0.99985, a perturbation of 1.5 parts in ten thousand. It moved the
    /// measured autorotation rate of descent from 3193 to 3859 fpm, a fifth of the answer,
    /// and shifted best glide from 50 kt to 70. The autorotation equilibrium (D-041,
    /// D-045) turns out to be knife-edged enough that a rounding error in blade condition
    /// relocates it, which is worth knowing on its own account. It also meant
    /// <c>Trim.Solve</c> accrued wear, because trim solves by stepping the aircraft - so
    /// the amount of wear on an aircraft depended on how many iterations its trim took.</para>
    ///
    /// <para>Booking at shutdown fixes all of it and is the better design anyway: the wear
    /// arrives at exactly the moment D-003a says the game should charge the player, which
    /// is when he lands.</para>
    /// </summary>
    public double AccrueFlightHours()
    {
        if (_rotorSeconds < 1.0) return 0;
        double hours = _rotorSeconds / 3600.0;
        _rotorSeconds = 0;
        _totalFlightHours += hours;
        foreach (Component c in System.Enum.GetValues<Component>())
        {
            double lost = Salvage.WearOver(c, _health[(int)c], hours);
            if (lost > 0) Erode(c, lost, DamageCause.Wear, "flight hours");
        }
        return hours;
    }

    /// <summary>
    /// Turbine outlet temperature for a given fraction of the engine's work, deg C.
    ///
    /// Public, and clamped well above 1, because a start is the one time this fraction is
    /// not "how much power am I making". On the start schedule the fuel is roughly fixed
    /// and the airflow is whatever the starter has managed, so the ratio can be two or
    /// three - which is exactly a hot start, and the gauge has to be able to show it.
    /// <c>Powerplant</c> uses this same function so the needle and the damage agree.
    /// </summary>
    public static double TotTargetC(double powerFrac, double ambientC)
        => ambientC + 250.0 + 340.0 * System.Math.Clamp(powerFrac, 0, 4.0);

    /// <summary>
    /// Heat the gearbox can shed at this temperature, W/K.
    ///
    /// Two paths in parallel. The case always sheds something - that term is what lets a
    /// shut-down gearbox cool down and what stops a dry one predicting an infinite
    /// temperature. The cooler is worth ten times as much but needs two things: oil to
    /// circulate, and a thermostat that has opened.
    /// </summary>
    private static double XmsnCooling(double tempC, double oilFraction, double rotorFrac)
    {
        double coolFrac = System.Math.Clamp(oilFraction / XmsnCoolingKneeFraction, 0, 1);
        double open = System.Math.Clamp((tempC - XmsnThermostatOpenC) / 12.0, 0, 1);
        return (XmsnCaseCoolingWPerK + XmsnCoolerWPerK * open * coolFrac)
               * (0.30 + 0.70 * System.Math.Clamp(rotorFrac, 0, 1));
    }

    // ============================================================== the gauges
    //
    // Facts, in the units the dial is marked in, with no verdict attached (D-005a). A
    // health value is a score and the player never sees one; a temperature is a fact and
    // the player can act on it.

    /// <summary>Main gearbox oil temperature, deg C. Red line at <see cref="XmsnOilTempLimitC"/>.</summary>
    public double XmsnOilTempC => _xmsnOilTempC;

    /// <summary>
    /// Main gearbox oil pressure, psi. Caution switch at <see cref="XmsnOilPressCautionPsi"/>.
    ///
    /// A function of quantity and rotor speed only: the pump makes pressure out of the oil
    /// it can reach, and it can reach all of it until the level drops far enough to unport
    /// the pickup. That is why pressure is a late warning and temperature is an early one,
    /// and it is the reason the two gauges together tell the pilot something neither tells
    /// him alone.
    /// </summary>
    public double XmsnOilPressurePsi =>
        XmsnOilPressNominalPsi
        * System.Math.Clamp(_xmsnOil / XmsnPressureKneeFraction, 0, 1)
        * System.Math.Clamp(_lastRotorFraction / 0.60, 0, 1);

    /// <summary>Gearbox oil remaining, fraction of capacity. Not on the panel; for the mechanic and the tests.</summary>
    public double XmsnOilFraction => _xmsnOil;

    /// <summary>
    /// Gearbox oil temperature trend, deg C per minute, smoothed over 15 seconds.
    ///
    /// Real panels do not have this; real pilots read it off the needle over thirty
    /// seconds, and a game cannot ask the player to stare at a gauge. It is a measurement,
    /// not a prediction.
    /// </summary>
    public double XmsnOilTempTrendPerMin => _xmsnTempTrendPerSec * 60.0;

    /// <summary>
    /// Minutes until the gearbox oil reaches the red line at the present trend.
    /// PositiveInfinity when it is not rising; zero when it is already there.
    ///
    /// The same arithmetic a fuel totaliser does - present reading, present rate, one
    /// division - and it carries the same honesty warning: it is what would happen if
    /// nothing changed, and something always changes. It is not a verdict on the aircraft
    /// (D-005a); it is a subtraction the pilot would otherwise do in his head.
    /// </summary>
    public double XmsnMinutesToLimit
    {
        get
        {
            if (_xmsnOilTempC >= XmsnOilTempLimitC) return 0;
            double rate = XmsnOilTempTrendPerMin;
            if (rate <= 0.01) return double.PositiveInfinity;
            return (XmsnOilTempLimitC - _xmsnOilTempC) / rate;
        }
    }

    /// <summary>
    /// Metal in the gearbox oil. Latches until the gearbox is opened up and repaired.
    ///
    /// A chip detector is a plug with a magnet and a gap; when enough swarf bridges the gap
    /// a light comes on and stays on. It is the most purely factual instrument in the
    /// aircraft - it does not grade anything, it reports that there is metal where metal
    /// should not be - which is why it is the right way to tell the player that the damage
    /// has stopped being recoverable.
    /// </summary>
    public bool ChipLight => _chipLight;

    /// <summary>Turbine outlet temperature, deg C. Red line at <see cref="TotLimitC"/>.</summary>
    public double TurbineOutletTempC => _totC;

    /// <summary>Engine oil temperature, deg C.</summary>
    public double EngineOilTempC => _engineOilTempC;

    /// <summary>Engine oil pressure, psi. Caution at <see cref="EngineOilPressCautionPsi"/>.</summary>
    public double EngineOilPressurePsi =>
        EngineOilPressNominalPsi
        * System.Math.Clamp(_engineN1 / 0.55, 0, 1)
        * (0.35 + 0.65 * Health(Component.Engine));

    /// <summary>
    /// Hydraulic system pressure, psi.
    ///
    /// Holds at nominal while the reservoir has anything in it and collapses over the last
    /// 15%, which is what a real leak feels like: everything normal, and then the controls
    /// are suddenly heavy.
    ///
    /// Health contributes only 15% of the pressure, because a damaged pump does make
    /// slightly less, but the first pass gave it 55% and that wrecked the whole design: a
    /// 0.45 hydraulic system read 2092 psi the instant it was hit, so the controls were
    /// already a third slow before a drop of fluid had left the reservoir, and the cliff
    /// this system is built on was a slope again. The leak is the mechanism. The pump is a
    /// footnote.
    /// </summary>
    public double HydraulicPressurePsi =>
        HydraulicPressNominalPsi
        * System.Math.Clamp(_hydFluid / HydraulicPressureKneeFraction, 0, 1)
        * (0.85 + 0.15 * Health(Component.Hydraulics));

    /// <summary>Hydraulic reservoir remaining, fraction. For the mechanic and the tests.</summary>
    public double HydraulicFluidFraction => _hydFluid;

    /// <summary>
    /// Airframe vibration, inches per second - the unit a rotor track-and-balance set
    /// reads. A well-tracked two-bladed rotor lives near 0.12, 0.2 is the limit for release
    /// to service, and above 1.0 the instruments blur and things start coming loose.
    /// </summary>
    public double VibrationIps => _vibrationIps;

    /// <summary>
    /// The instruments, in the order a panel scan takes them. Allocates; call it for the
    /// warning panel and the test bench, not from the flight model.
    /// </summary>
    public IReadOnlyList<Gauge> Gauges() => new[]
    {
        new Gauge("XMSN OIL TEMP",  XmsnOilTempC,         "C",   XmsnOilTempCautionC,        XmsnOilTempLimitC,   true),
        new Gauge("XMSN OIL PRESS", XmsnOilPressurePsi,   "psi", XmsnOilPressCautionPsi,     double.NaN,          false),
        new Gauge("TOT",            TurbineOutletTempC,   "C",   TotCautionC,                TotLimitC,           true),
        new Gauge("ENG OIL TEMP",   EngineOilTempC,       "C",   EngineOilTempLimitC - 15.0, EngineOilTempLimitC, true),
        new Gauge("ENG OIL PRESS",  EngineOilPressurePsi, "psi", EngineOilPressCautionPsi,   double.NaN,          false),
        new Gauge("HYD PRESS",      HydraulicPressurePsi, "psi", HydraulicPressCautionPsi,   double.NaN,          false),
        new Gauge("VIBRATION",      VibrationIps,         "ips", VibrationCautionIps,        double.NaN,          true),
    };

    /// <summary>
    /// The captions currently lit on the warning panel.
    ///
    /// Each one names what tripped it, never how bad it is. "XMSN OIL PRESS" means a
    /// pressure switch opened below 30 psi; it does not mean "you are in trouble". The
    /// player does the arithmetic from the caption plus the gauge plus the trend, which is
    /// the difference between a game about flying a machine and a game about watching a
    /// health bar (D-005a).
    /// </summary>
    public IReadOnlyList<string> WarningPanel()
    {
        var lit = new List<string>();
        if (XmsnOilTempC >= XmsnOilTempLimitC) lit.Add("XMSN OIL HOT");
        if (_lastRotorFraction > 0.6 && XmsnOilPressurePsi < XmsnOilPressCautionPsi) lit.Add("XMSN OIL PRESS");
        if (_chipLight) lit.Add("CHIP DETECTOR");
        if (TurbineOutletTempC >= TotLimitC) lit.Add("TOT");
        if (EngineOilTempC >= EngineOilTempLimitC) lit.Add("ENG OIL HOT");
        if (_engineN1 > 0.5 && EngineOilPressurePsi < EngineOilPressCautionPsi) lit.Add("ENG OIL PRESS");
        if (HydraulicPressurePsi < HydraulicPressCautionPsi) lit.Add("HYD PRESSURE");
        if (VibrationIps >= VibrationCautionIps * 2.0) lit.Add("VIBRATION");
        return lit;
    }

    // ---------------------------------------------------------------- effects

    /// <summary>
    /// Multiplier on main rotor thrust. A damaged blade loses lift, but only a little -
    /// the dangerous part is the vibration and the imbalance, not the thrust.
    /// </summary>
    public double RotorThrustFactor => 0.80 + 0.20 * Health(Component.MainRotor);

    /// <summary>
    /// Once-per-revolution imbalance from a damaged or mismatched blade, as a fraction of
    /// rotor thrust. This is what a real out-of-track rotor does: it shakes the airframe
    /// at 1/rev hard enough to blur the instruments, and it gets worse with rotor speed.
    /// </summary>
    public double RotorImbalance => (1.0 - Health(Component.MainRotor)) * 0.09;

    /// <summary>Tail rotor authority. At zero the aircraft can only be flown fast and straight.</summary>
    public double TailRotorFactor => Health(Component.TailRotor);

    /// <summary>
    /// Available engine power.
    ///
    /// Two terms. The engine's own condition, and - below about a quarter health - the fuel
    /// system's ability to keep the fuel control satisfied. The second is what turns a
    /// holed tank from a range problem into a power problem, and it feeds straight back
    /// into the gearbox through the collective the pilot then has to hold.
    /// </summary>
    public double EnginePowerFactor
        => (0.25 + 0.75 * Health(Component.Engine))
           * (0.75 + 0.25 * System.Math.Clamp(Health(Component.FuelSystem) / 0.40, 0, 1));

    /// <summary>
    /// How much governor is left, 0..1.
    ///
    /// The fuel control and its droop compensator are bolted to the engine and fed by the
    /// fuel system, so they go the way those go - but they go <i>first</i>, and that is the
    /// point of the curve. Governing is gone by the time the engine is at 0.35 health,
    /// while the engine itself is still making half its power and is serviceable until
    /// <see cref="EngineFloor"/>. The band between them is an aircraft that still flies and
    /// whose rotor speed is suddenly the pilot's job, which is a far more interesting
    /// failure than one more multiplier on power.
    /// </summary>
    public double GovernorAuthority
        => System.Math.Clamp((Health(Component.Engine) - 0.35) / 0.40, 0, 1)
           * System.Math.Clamp(Health(Component.FuelSystem) / 0.50, 0, 1);

    /// <summary>
    /// Transmission torque limit. A damaged gearbox cannot take what it used to, so the
    /// aircraft runs out of lift before it runs out of engine - which is exactly the
    /// trade that makes an overloaded aircraft dangerous.
    /// </summary>
    public double TransmissionFactor => 0.35 + 0.65 * Health(Component.Transmission);

    /// <summary>Fuel leak, kg/s, from a holed tank or a cracked line.</summary>
    public double FuelLeakRate => (1.0 - Health(Component.FuelSystem)) * 0.085;

    /// <summary>
    /// Actuator response, as a multiplier on the time the controls take to travel.
    ///
    /// Driven by hydraulic pressure rather than by hydraulic health, which is what makes
    /// the failure a cliff instead of a slope: the pump keeps up with the leak until the
    /// reservoir unports, and then there is nothing. A pilot who takes a hydraulic hit gets
    /// a minute or two of normal controls to find somewhere to put down, and that minute is
    /// the game.
    /// </summary>
    public double ActuatorSlowdown
        => 1.0 + (1.0 - System.Math.Clamp(HydraulicPressurePsi / HydraulicPressNominalPsi, 0, 1)) * 3.5;

    /// <summary>
    /// How much of the stability augmentation still works, 0..1.
    ///
    /// It runs off the same hydraulics as the actuators, so a hydraulic hit both slows the
    /// controls down and takes the artificial damping away - which is the right pairing.
    /// Losing the augmentation is the moment the aircraft stops being forgiving.
    /// </summary>
    public double ActuatorEffectiveness
        => System.Math.Clamp(HydraulicPressurePsi / HydraulicPressNominalPsi, 0, 1);

    /// <summary>Parasite drag multiplier from a torn-up airframe.</summary>
    public double DragFactor => 1.0 + (1.0 - Health(Component.Fuselage)) * 0.55;

    /// <summary>Whether the aircraft can be set down without the skids folding.</summary>
    public bool SkidsServiceable => Health(Component.Skids) > SkidsFloor;

    /// <summary>Whether a given avionics box is powered and working.</summary>
    public bool AvionicsWorking => Health(Component.Avionics) > AvionicsFloor;

    /// <summary>True when something is wrong enough that the pilot should be looking for a field.</summary>
    public bool Airworthy =>
        Health(Component.MainRotor) > MainRotorFloor &&
        Health(Component.Transmission) > TransmissionFloor &&
        Health(Component.Engine) > EngineFloor;

    /// <summary>Single worst component, for the warning panel.</summary>
    public (Component Component, double Health) Worst()
    {
        int worst = 0;
        for (int i = 1; i < _health.Length; i++) if (_health[i] < _health[worst]) worst = i;
        return ((Component)worst, _health[worst]);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        foreach (Component c in System.Enum.GetValues<Component>())
            if (_health[(int)c] < 0.999) parts.Add($"{c} {_health[(int)c] * 100:F0}%");
        return parts.Count == 0 ? "serviceable" : string.Join(", ", parts);
    }
}
