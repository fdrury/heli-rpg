namespace Rotorwash.Sim;

/// <summary>
/// State snapshot the copilot reads from. Passed in by the game layer each tick.
/// All values are what the flight engineer can see or hear: instruments and sound.
/// </summary>
public readonly struct CopilotState
{
    public readonly double TorquePct;       // 0..100+
    public readonly double NrPct;           // rotor rpm percent, 0..100+
    public readonly double AltAglM;         // radar altimeter, metres
    public readonly double VerticalSpeedMs; // m/s, negative = descending
    public readonly double FuelFraction;    // 0..1
    public readonly bool ThreatTracking;    // any emitter tracking us

    public CopilotState(double torque, double nr, double altAgl, double vs,
                        double fuel, bool threat)
    {
        TorquePct = torque; NrPct = nr; AltAglM = altAgl;
        VerticalSpeedMs = vs; FuelFraction = fuel; ThreatTracking = threat;
    }
}

/// <summary>
/// Wray calls the torque, the Nr, the altitude, and the fuel — because that is what
/// a flight engineer does (story.md §5, §7.6). Pure .NET, no Godot. The game layer
/// calls Update every frame while the passenger is aboard and airborne, and enqueues
/// any returned message onto RadioStrip.
///
/// The callouts are in Wray's voice: short, diagnostic, no encouragement, just readings.
/// She does not say "careful" or "watch out" — she says "torque ninety-two" because
/// that is what the gauge says and because a flight engineer calls the gauge.
/// </summary>
public sealed class CopilotCallouts
{
    // --- Timing ---------------------------------------------------------------
    // Minimum gap between any two callouts, so she is not a chatterbox.
    private const double MinInterval = 8.0;

    // Category cooldowns: each kind of callout has its own timer so a torque call
    // does not suppress an altitude call, but the same category cannot fire twice
    // in quick succession.
    private const double TorqueCooldown = 15.0;
    private const double NrCooldown = 12.0;
    private const double AltCooldown = 10.0;
    private const double FuelCooldown = 60.0;
    private const double ThreatCooldown = 20.0;

    // --- Thresholds -----------------------------------------------------------
    private const double TorqueHighPct = 85.0;
    private const double TorqueLimitPct = 95.0;
    private const double NrLowPct = 95.0;
    private const double AltCallM = 36.6;      // ~120 ft
    private const double AltMidM = 15.2;       // ~50 ft
    private const double AltLowM = 9.1;        // ~30 ft
    private const double DescentRateMs = -0.5;  // must be descending

    private double _sinceAny;
    private double _sinceTorque;
    private double _sinceNr;
    private double _sinceAlt;
    private double _sinceFuel;
    private double _sinceThreat;

    private bool _fuelWarned25;
    private bool _fuelWarned10;

    private readonly string _speaker;

    /// <summary>Total callouts emitted. Useful for tests.</summary>
    public int CalloutCount { get; private set; }

    public CopilotCallouts(string speaker)
    {
        _speaker = speaker;
        Reset();
    }

    /// <summary>
    /// Clear all timers and state. Called on boarding. Category timers start warm so
    /// the first callout of each type fires after only the global minimum interval,
    /// not after a cooldown that was never triggered.
    /// </summary>
    public void Reset()
    {
        _sinceAny = 0;
        _sinceTorque = TorqueCooldown;
        _sinceNr = NrCooldown;
        _sinceAlt = AltCooldown;
        _sinceFuel = FuelCooldown;
        _sinceThreat = ThreatCooldown;
        _fuelWarned25 = _fuelWarned10 = false;
        CalloutCount = 0;
    }

    /// <summary>
    /// Tick the callout system. Returns a message to enqueue, or null.
    /// Priority: threat > Nr > torque > altitude > fuel.
    /// </summary>
    public RadioMessage? Update(double dt, in CopilotState state)
    {
        _sinceAny += dt;
        _sinceTorque += dt;
        _sinceNr += dt;
        _sinceAlt += dt;
        _sinceFuel += dt;
        _sinceThreat += dt;

        if (_sinceAny < MinInterval) return null;

        // --- Threat: highest priority, she cannot ignore being shot at. ---
        if (state.ThreatTracking && _sinceThreat >= ThreatCooldown)
        {
            _sinceThreat = 0;
            return Emit("Tracking.");
        }

        // --- Nr: a drooping rotor is the thing that kills you. ---
        if (state.NrPct < NrLowPct && _sinceNr >= NrCooldown)
        {
            _sinceNr = 0;
            int pct = (int)state.NrPct;
            return Emit($"Nr {pct}.");
        }

        // --- Torque: the reading she calls most, because it is her job. ---
        if (state.TorquePct > TorqueHighPct && _sinceTorque >= TorqueCooldown)
        {
            _sinceTorque = 0;
            int pct = (int)state.TorquePct;
            return pct >= TorqueLimitPct
                ? Emit($"Torque {pct}. That is the limit.")
                : Emit($"Torque {pct}.");
        }

        // --- Altitude: only when descending, so she is not calling "fifty feet" in the cruise. ---
        if (state.AltAglM < AltCallM && state.VerticalSpeedMs < DescentRateMs
            && _sinceAlt >= AltCooldown)
        {
            _sinceAlt = 0;
            if (state.AltAglM < AltLowM) return Emit("Twenty feet.");
            if (state.AltAglM < AltMidM) return Emit("Fifty feet.");
            return Emit("One hundred feet.");
        }

        // --- Fuel: once per threshold, not nagging. ---
        if (state.FuelFraction < 0.10 && !_fuelWarned10 && _sinceFuel >= FuelCooldown)
        {
            _sinceFuel = 0;
            _fuelWarned10 = true;
            return Emit("Fuel. You are on the reserve.");
        }
        if (state.FuelFraction < 0.25 && !_fuelWarned25 && _sinceFuel >= FuelCooldown)
        {
            _sinceFuel = 0;
            _fuelWarned25 = true;
            return Emit("Fuel state. Quarter tank.");
        }

        return null;
    }

    private RadioMessage Emit(string text)
    {
        _sinceAny = 0;
        CalloutCount++;
        return new RadioMessage(_speaker, text, RadioMessageKind.Copilot);
    }
}
