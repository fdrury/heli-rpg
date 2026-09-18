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

/// <summary>How a component got hurt. Drives repair cost, repair skill and flavour.</summary>
public enum DamageCause { Wear, HardLanding, RotorStrike, Rollover, Impact, Gunfire, Fragment, Overtorque, Overspeed, Heat }

public readonly record struct DamageEvent(Component Component, double Amount, DamageCause Cause, string Note);

/// <summary>
/// Component health for one aircraft, and the effect that health has on how it flies.
///
/// This is the spine of the whole game. The aircraft is the second protagonist (pillar 1),
/// progression is the parts you find (D-005), the economy is what it costs to keep it
/// airworthy (D-007), and landing is the expensive act (D-003a). All of that is this class.
///
/// Every effect below is a multiplier or an offset applied to something the flight model
/// already computes. Nothing is a scripted penalty: a bent skid changes the geometry, a
/// nicked blade changes the mass balance, a tired engine changes available power - and the
/// aerodynamics work out the consequences.
/// </summary>
public sealed class DamageState
{
    private readonly double[] _health;
    private readonly List<DamageEvent> _log = new();

    public DamageState()
    {
        _health = new double[System.Enum.GetValues<Component>().Length];
        for (int i = 0; i < _health.Length; i++) _health[i] = 1.0;
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

    public void Repair(Component c, double amount)
        => _health[(int)c] = System.Math.Clamp(_health[(int)c] + amount, 0.0, 1.0);

    public void RepairAll() { for (int i = 0; i < _health.Length; i++) _health[i] = 1.0; }

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

    /// <summary>Available engine power.</summary>
    public double EnginePowerFactor => 0.25 + 0.75 * Health(Component.Engine);

    /// <summary>
    /// Transmission torque limit. A damaged gearbox cannot take what it used to, so the
    /// aircraft runs out of lift before it runs out of engine - which is exactly the
    /// trade that makes an overloaded aircraft dangerous.
    /// </summary>
    public double TransmissionFactor => 0.35 + 0.65 * Health(Component.Transmission);

    /// <summary>Fuel leak, kg/s, from a holed tank or a cracked line.</summary>
    public double FuelLeakRate => (1.0 - Health(Component.FuelSystem)) * 0.085;

    /// <summary>
    /// Actuator response. Failing hydraulics make the controls slow and heavy long before
    /// they fail completely, which is the warning a pilot actually gets.
    /// </summary>
    public double ActuatorSlowdown => 1.0 + (1.0 - Health(Component.Hydraulics)) * 3.5;

    /// <summary>Parasite drag multiplier from a torn-up airframe.</summary>
    public double DragFactor => 1.0 + (1.0 - Health(Component.Fuselage)) * 0.55;

    /// <summary>Whether the aircraft can be set down without the skids folding.</summary>
    public bool SkidsServiceable => Health(Component.Skids) > 0.25;

    /// <summary>Whether a given avionics box is powered and working.</summary>
    public bool AvionicsWorking => Health(Component.Avionics) > 0.35;

    /// <summary>True when something is wrong enough that the pilot should be looking for a field.</summary>
    public bool Airworthy =>
        Health(Component.MainRotor) > 0.25 &&
        Health(Component.Transmission) > 0.20 &&
        Health(Component.Engine) > 0.15;

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
