using System;
using System.Collections.Generic;

namespace Rotorwash.Sim;

/// <summary>How much of the pilot's attention a condition is entitled to.</summary>
public enum WarningSeverity
{
    None = 0,

    /// <summary>Worth knowing, not worth interrupting for. No light, no tone: it sits on the list.</summary>
    Advisory = 1,

    /// <summary>
    /// Master caution. Latches, chimes once, and stays on the panel until acknowledged -
    /// because the whole point of a caution is that it can happen while the pilot is
    /// looking somewhere else.
    /// </summary>
    Caution = 2,

    /// <summary>
    /// Master warning. Something is being damaged, or the aircraft is about to stop
    /// flying. Sounds continuously and cannot be silenced while it is still true.
    /// </summary>
    Warning = 3,
}

/// <summary>
/// Every condition the caution and warning system knows about.
///
/// <b>The declaration order IS the priority order.</b> When six things are wrong at once
/// the panel has to say which one matters, and the cheapest way to keep that ordering
/// honest is to make it a property of the type rather than a table somebody can forget to
/// update. <see cref="CautionWarningSystem.Items"/> sorts on severity first and this enum
/// second, so a caution never buries a warning and, within a severity, the list reads
/// top-down in order of how little time the pilot has.
///
/// The ordering argument, briefly:
///
/// <list type="bullet">
/// <item><see cref="EngineOut"/> first because it is the <i>cause</i> of most of what
/// follows, and a pilot who reads only the top line still learns the right thing.</item>
/// <item><see cref="RotorRpmLow"/> second: rotor inertia is the only thing keeping the
/// aircraft in the air once the engine has gone, and it is unrecoverable within seconds.</item>
/// <item><see cref="SinkRate"/> third, ranked by time-to-ground rather than by height,
/// because 200 m is comfortable at 300 fpm and fatal at 4000.</item>
/// <item>Then the conditions that degrade the aircraft before they end the flight.</item>
/// <item><see cref="FuelLow"/> near the bottom <i>by rank</i> - but it reaches Warning
/// severity at ten minutes, and severity sorts first, so it climbs the list on its own
/// when it has to.</item>
/// <item><see cref="PowerLimit"/> last: a statement about the engine's ceiling, which is
/// information rather than trouble.</item>
/// </list>
/// </summary>
public enum WarningId
{
    EngineOut,
    RotorRpmLow,
    SinkRate,
    RotorRpmHigh,
    VortexRing,
    TailRotorAuthority,
    TorqueHigh,
    BladeStall,
    BladeLoading,
    FuelLow,
    PowerLimit,
}

/// <summary>
/// One severity step of one condition, with the two thresholds that make it stable.
///
/// <see cref="Arm"/> is where the condition becomes true; <see cref="Clear"/> is where it
/// stops being true, and it sits deliberately on the safe side of <see cref="Arm"/>.
/// Without that gap every threshold in this file would chatter, because none of these
/// signals is smooth: measured in <i>steady</i> flight at 100 kt, blade loading swings
/// 0.059 to 0.085 and the stalled fraction swings 0 to 5% at twice rotor frequency. A bare
/// comparison against a fixed number does not report a condition, it reports blade azimuth.
/// </summary>
public readonly struct WarningBand
{
    public readonly WarningSeverity Severity;
    public readonly double Arm;
    public readonly double Clear;

    public WarningBand(WarningSeverity severity, double arm, double clear)
    {
        Severity = severity;
        Arm = arm;
        Clear = clear;
    }
}

/// <summary>One line on the annunciator panel, ready to draw.</summary>
public readonly record struct WarningItem(
    WarningId Id,
    WarningSeverity Severity,
    bool Active,
    bool Acknowledged,
    double Value,
    string Text)
{
    /// <summary>True when the condition has gone away but the latch is still holding it up.</summary>
    public bool Stale => !Active;
}

/// <summary>
/// One condition: a signal, a filter, a set of bands, and the state machine that turns
/// those into something a pilot can read without it flickering.
///
/// Four defences, in order, and all four earned:
///
/// <list type="number">
/// <item><b>A low-pass filter on the source.</b> D-043 already had to learn that a
/// two-bladed rotor's instantaneous shaft torque is a phase reading rather than a
/// diagnosis; the same aliasing is in every rotor-derived signal here.
/// <see cref="FilterTau"/> is set per condition against the measured ripple.</item>
/// <item><b>Hysteresis</b>, per band - see <see cref="WarningBand"/>.</item>
/// <item><b>A dwell time.</b> <see cref="ArmSeconds"/> before a condition escalates and
/// <see cref="ClearSeconds"/> - always longer - before it relaxes. This is the only
/// defence available to the boolean signals (<c>TorqueLimited</c>,
/// <c>TailRotorSaturated</c>), which have no value to put a gap around.</item>
/// <item><b>A latch.</b> Once a condition reaches Caution it stays on the panel until it
/// is both gone and acknowledged. This project has been bitten here before: an unlatched
/// check logged 1141 separate rotor strikes for one impact, because "is it true right now"
/// fired every single physics step.</item>
/// </list>
/// </summary>
public sealed class WarningCondition
{
    public WarningId Id { get; }

    /// <summary>Whether a larger number is worse. Fuel minutes and time-to-ground are not.</summary>
    public bool HigherIsWorse { get; }

    /// <summary>Time constant of the source filter, seconds.</summary>
    public double FilterTau { get; }

    /// <summary>How long a worse condition must persist before it is believed.</summary>
    public double ArmSeconds { get; }

    /// <summary>How long a better condition must persist before it is believed.</summary>
    public double ClearSeconds { get; }

    private readonly WarningBand[] _bands;

    /// <summary>The filtered source value - the number the panel prints.</summary>
    public double Value { get; private set; }

    /// <summary>The raw source value this update, before filtering. Diagnostics only.</summary>
    public double Raw { get; private set; }

    /// <summary>True when this condition cannot meaningfully be evaluated right now.</summary>
    public bool Inhibited { get; private set; }

    /// <summary>What is true this instant, after filtering, hysteresis and dwell.</summary>
    public WarningSeverity Active { get; private set; }

    /// <summary>
    /// The worst severity reached in this episode and not yet released by an
    /// acknowledgement. This is the latch, and it is what gets displayed.
    /// </summary>
    public WarningSeverity Displayed { get; private set; }

    public bool Acknowledged { get; private set; }

    /// <summary>How many times this condition has risen into Caution or worse. One per episode.</summary>
    public int RaiseCount { get; private set; }

    /// <summary>
    /// How many times <see cref="Active"/> has changed at all. The flicker metric: a
    /// condition crossed slowly once, with noise on it, must move this by one - not by the
    /// hundreds a naive comparison manages.
    /// </summary>
    public int TransitionCount { get; private set; }

    /// <summary>Seconds the condition has been continuously active.</summary>
    public double ActiveSeconds { get; private set; }

    /// <summary>
    /// The line the panel shows. Live while the condition is at least as bad as the
    /// severity it has latched at; frozen at the worst of the episode once it decays
    /// below that, because a latch exists to say what happened.
    /// </summary>
    public string Text { get; private set; } = "";

    private double _peakValue;
    private string _peakText = "";

    internal bool JustRaised { get; private set; }

    private WarningSeverity _pending;
    private double _pendingTime;
    private bool _primed;

    public WarningCondition(WarningId id, bool higherIsWorse, double filterTau,
                            double armSeconds, double clearSeconds, params WarningBand[] bands)
    {
        Id = id;
        HigherIsWorse = higherIsWorse;
        FilterTau = filterTau;
        ArmSeconds = armSeconds;
        ClearSeconds = clearSeconds;
        _bands = bands;
    }

    public IReadOnlyList<WarningBand> Bands => _bands;

    /// <summary>The arm threshold for a severity, or NaN if this condition has no such band.</summary>
    public double ArmFor(WarningSeverity s)
    {
        foreach (var b in _bands) if (b.Severity == s) return b.Arm;
        return double.NaN;
    }

    /// <summary>The clear threshold for a severity, or NaN if this condition has no such band.</summary>
    public double ClearFor(WarningSeverity s)
    {
        foreach (var b in _bands) if (b.Severity == s) return b.Clear;
        return double.NaN;
    }

    internal void Reset()
    {
        Value = Raw = 0;
        Active = Displayed = _pending = WarningSeverity.None;
        Acknowledged = false;
        RaiseCount = TransitionCount = 0;
        ActiveSeconds = _pendingTime = 0;
        JustRaised = false;
        _primed = false;
        Inhibited = false;
        Text = "";
        _peakText = "";
        _peakValue = 0;
    }

    /// <summary>
    /// Record what the condition looks like right now, and remember it if this is the
    /// worst the episode has been.
    ///
    /// The worst is what a cleared latch shows. Without it the panel produced
    /// "C ROTOR RPM 104% (clr)" after an autorotation: the low-rotor caution had latched
    /// at 93%, and because the clear dwell held it active for another 1.2 s while the
    /// rotor ran away upwards, the last live description it took was of an overspeed.
    /// A latched low-rotor caution quoting a high rotor is not a fact about anything.
    /// </summary>
    internal void SetLiveText(string s)
    {
        Text = s;
        bool worse = _peakText.Length == 0 ||
                     (HigherIsWorse ? Value > _peakValue : Value < _peakValue);
        if (worse) { _peakValue = Value; _peakText = s; }
    }

    /// <summary>Hold the worst of the episode, for a latch whose condition has decayed.</summary>
    internal void FreezeText()
    {
        if (_peakText.Length > 0) Text = _peakText;
    }

    /// <summary>
    /// Whether the panel line should still be taken from the live value.
    ///
    /// Two things have to be true: the condition is at least as bad as the severity it is
    /// displaying, and the value has not yet retreated past that band's clear threshold.
    /// The second one is not redundant, because the clear dwell keeps a condition active
    /// for a second or so after the value has left the band - long enough, measured in a
    /// real autorotation, for a Warning-severity rotor overspeed latched at 113% to
    /// relabel itself "ROTOR RPM 98%" on the way back down.
    /// </summary>
    internal bool TextIsLive
    {
        get
        {
            if (Inhibited || Active < Displayed) return false;
            double clear = ClearFor(Displayed);
            if (double.IsNaN(clear)) return true;
            return HigherIsWorse ? Value >= clear : Value <= clear;
        }
    }

    internal void Step(double raw, bool inhibited, double dt)
    {
        JustRaised = false;
        Raw = raw;
        Inhibited = inhibited;

        // The filter keeps running while inhibited, so the value is already honest the
        // instant the inhibit lifts. An unfiltered first sample after an inhibit is the
        // same bug as an unfiltered first sample after a spawn.
        if (!_primed) { Value = raw; _primed = true; }
        else if (dt > 0)
        {
            double k = FilterTau > 1e-6 ? 1.0 - Math.Exp(-dt / FilterTau) : 1.0;
            Value += (raw - Value) * k;
        }

        WarningSeverity target = inhibited ? WarningSeverity.None : Evaluate(Value);

        if (target == Active)
        {
            _pending = Active;
            _pendingTime = 0;
        }
        else
        {
            if (target != _pending) { _pending = target; _pendingTime = 0; }
            _pendingTime += dt;
            double need = target > Active ? ArmSeconds : ClearSeconds;
            if (_pendingTime >= need)
            {
                Active = target;
                _pendingTime = 0;
                TransitionCount++;
                ActiveSeconds = 0;
            }
        }

        if (Active != WarningSeverity.None) ActiveSeconds += dt;

        // --- The latch -------------------------------------------------------
        if (Active > Displayed)
        {
            bool raise = Active >= WarningSeverity.Caution;
            Displayed = Active;
            if (raise)
            {
                RaiseCount++;
                Acknowledged = false;
                JustRaised = true;
            }
        }

        // Advisories do not latch - information that will not go away is clutter - and an
        // acknowledged caution has had its latch released, so both follow the condition
        // back down. An unacknowledged caution or warning does not.
        if ((Displayed < WarningSeverity.Caution || Acknowledged) && Active < Displayed)
            Displayed = Active;

        if (Displayed == WarningSeverity.None)
        {
            Acknowledged = false;
            _peakText = "";                 // the episode is over; the next one is new
        }
    }

    /// <summary>
    /// Which band the filtered value is in, given where we already are.
    ///
    /// The subtlety is that a band already entered is tested against its <c>Clear</c>
    /// threshold and a band not yet entered against its <c>Arm</c>. Bands are ordered
    /// least to most severe and the thresholds are monotone, so the highest band that
    /// tests true is the answer.
    /// </summary>
    private WarningSeverity Evaluate(double v)
    {
        WarningSeverity result = WarningSeverity.None;
        foreach (var b in _bands)
        {
            double threshold = Active >= b.Severity ? b.Clear : b.Arm;
            bool on = HigherIsWorse ? v >= threshold : v <= threshold;
            if (on) result = b.Severity;
        }
        return result;
    }

    internal bool AcknowledgeLatch()
    {
        if (Displayed == WarningSeverity.None) return false;
        bool gone = Active == WarningSeverity.None;
        Acknowledged = true;
        Displayed = Active;
        if (Displayed == WarningSeverity.None) Acknowledged = false;
        return gone;
    }
}

/// <summary>
/// The caution and warning system: what the aircraft knows about itself, in the order the
/// pilot needs it.
///
/// <b>Why this exists.</b> The flight model matches a real UH-1H's still-air range to
/// within 1% and has a textbook power curve, and a player cannot see any of that. Every
/// number below already existed in <see cref="FlightTelemetry"/>; nothing surfaced it. A
/// model that is more faithful than the game around it can show is a model nobody can tell
/// is faithful.
///
/// <b>What it does not do.</b> Per D-005a this reports <i>facts</i>. "ROTOR RPM 92%", never
/// "LOWER COLLECTIVE"; "SINK 1850 FPM 140 M", never "PULL UP". The game does the
/// bookkeeping and the player does the thinking, and the difference between those two is
/// most of what separates a simulator from a tutorial.
///
/// <b>Where the thresholds come from.</b> Every one is anchored to a measurement rather
/// than a guess, and the measurements are written out on <see cref="Build"/>. Where the
/// real aircraft has a published number - the Huey's low-rotor horn at 300 rpm, which is
/// 92.6% of 324 - that is the number used.
///
/// Update this at frame rate or slower; it is not part of the physics step. It allocates
/// one string per <i>displayed</i> condition per update, which at a handful of conditions
/// and 60 Hz is nothing, and it does not allocate at all when the panel is clear.
/// </summary>
public sealed class CautionWarningSystem
{
    private readonly WarningCondition[] _conditions;
    private readonly List<WarningItem> _items = new();
    private readonly List<WarningItem> _raised = new();

    public CautionWarningSystem() => _conditions = Build();

    /// <summary>Every condition, in declaration (priority) order.</summary>
    public IReadOnlyList<WarningCondition> Conditions => _conditions;

    /// <summary>
    /// What to put on the panel, worst first. Severity sorts before priority, so a caution
    /// can never hide a warning; within a severity an active condition sorts before a
    /// latched one that has already cleared; within that, <see cref="WarningId"/> order.
    /// </summary>
    public IReadOnlyList<WarningItem> Items => _items;

    /// <summary>Conditions that reached Caution or worse <i>this update</i>. Drives the chime.</summary>
    public IReadOnlyList<WarningItem> Raised => _raised;

    /// <summary>Red light: something is true right now at Warning severity.</summary>
    public bool MasterWarning { get; private set; }

    /// <summary>Amber light: a caution has latched and has not been acknowledged.</summary>
    public bool MasterCaution { get; private set; }

    /// <summary>
    /// The horn every helicopter pilot knows. Bound to low rotor RPM at Warning severity
    /// and to nothing else, and deliberately not silenceable: on the real aircraft the
    /// only way to stop it is to put the rotor back.
    /// </summary>
    public bool LowRotorHorn { get; private set; }

    /// <summary>
    /// The master warning beeper: any Warning-severity condition <i>except</i> low rotor,
    /// which has the horn. Two continuous tones at once is mush, so they are mutually
    /// exclusive by construction.
    /// </summary>
    public bool WarningTone { get; private set; }

    public WarningCondition this[WarningId id] => _conditions[(int)id];

    public void Reset()
    {
        foreach (var c in _conditions) c.Reset();
        _items.Clear();
        _raised.Clear();
        MasterWarning = MasterCaution = LowRotorHorn = WarningTone = false;
    }

    /// <summary>Update from an aircraft. Convenience for the game and the test bench.</summary>
    public void Update(Helicopter heli, double dt) => Update(heli.Telemetry, dt);

    public void Update(in FlightTelemetry t, double dt)
    {
        // Telemetry.OnGround is only written by the headless ground model - in the game
        // Godot integrates the rigid body and nothing sets it, so it reads false forever.
        // Height is the backstop, and it is the honest test anyway: what the inhibits care
        // about is whether the skids are on something, not who noticed.
        bool onGround = t.OnGround || t.HeightAgl < 1.5;
        bool rotorTurning = t.RotorRpmPercent > 55.0;
        bool coldOrStarting = t.Engine == EngineState.Off || t.Engine == EngineState.Starting;

        foreach (var c in _conditions)
        {
            (double raw, bool inhibit) = Source(c.Id, t, onGround, rotorTurning, coldOrStarting);
            c.Step(raw, inhibit, dt);
        }

        _items.Clear();
        _raised.Clear();
        MasterWarning = MasterCaution = LowRotorHorn = WarningTone = false;

        foreach (var c in _conditions)
        {
            if (c.Displayed == WarningSeverity.None) continue;

            // Live while the condition still is what it says it is; otherwise the worst
            // of the episode. Never while inhibited: the filter keeps running so the
            // value is honest the moment the inhibit lifts, but the number it is tracking
            // means nothing in the meantime, and a stalled fraction of 51% measured on a
            // rotor turning at 33% is arithmetic rather than a fact.
            if (c.TextIsLive) c.SetLiveText(Describe(c.Id, c.Value, t));
            else c.FreezeText();
            if (c.Text.Length == 0) c.SetLiveText(Describe(c.Id, c.Value, t));
            var item = new WarningItem(c.Id, c.Displayed, c.Active != WarningSeverity.None,
                                       c.Acknowledged, c.Value, c.Text);
            _items.Add(item);
            if (c.JustRaised) _raised.Add(item);

            if (c.Active == WarningSeverity.Warning)
            {
                MasterWarning = true;
                if (c.Id == WarningId.RotorRpmLow) LowRotorHorn = true;
                else WarningTone = true;
            }
            if (c.Displayed >= WarningSeverity.Caution && !c.Acknowledged) MasterCaution = true;
        }

        _items.Sort(Compare);
    }

    private static int Compare(WarningItem a, WarningItem b)
    {
        int s = b.Severity.CompareTo(a.Severity);
        if (s != 0) return s;
        int live = b.Active.CompareTo(a.Active);
        if (live != 0) return live;
        return ((int)a.Id).CompareTo((int)b.Id);
    }

    /// <summary>
    /// Press to reset. Drops every latched condition that has genuinely gone away and
    /// silences the master caution for the ones that have not; a Warning that is still
    /// true keeps its tone, because acknowledging a fact does not change it.
    /// </summary>
    /// <returns>How many latched conditions were dropped.</returns>
    public int Acknowledge()
    {
        int dropped = 0;
        foreach (var c in _conditions)
            if (c.AcknowledgeLatch()) dropped++;

        _items.Clear();
        _raised.Clear();
        MasterCaution = false;
        foreach (var c in _conditions)
        {
            if (c.Displayed == WarningSeverity.None) continue;
            _items.Add(new WarningItem(c.Id, c.Displayed, c.Active != WarningSeverity.None,
                                       c.Acknowledged, c.Value, c.Text));
            if (c.Displayed >= WarningSeverity.Caution && !c.Acknowledged) MasterCaution = true;
        }
        _items.Sort(Compare);
        return dropped;
    }

    /// <summary>One line, for a log or a test transcript.</summary>
    public string Summary()
    {
        if (_items.Count == 0) return "(clear)";
        var sb = new System.Text.StringBuilder();
        foreach (var i in _items)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(i.Severity switch
            {
                WarningSeverity.Warning => "W ",
                WarningSeverity.Caution => "C ",
                _ => "A ",
            });
            sb.Append(i.Text);
            if (i.Stale) sb.Append(" (clr)");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ sources

    /// <summary>
    /// The one place a condition is tied to a telemetry field, plus the inhibit that says
    /// when the field means nothing.
    ///
    /// Inhibits are not cosmetic. Without them a cold aircraft on the skids screams LOW
    /// ROTOR through every conversation in the game, which is precisely how a caution and
    /// warning system gets ignored.
    /// </summary>
    private static (double value, bool inhibit) Source(
        WarningId id, in FlightTelemetry t, bool onGround, bool rotorTurning, bool coldOrStarting)
    {
        switch (id)
        {
            case WarningId.EngineOut:
                // Off is not a failure - it is a shutdown, and it happens on the ground.
                return (t.Engine is EngineState.Flameout or EngineState.Failed ? 1 : 0, onGround);

            case WarningId.RotorRpmLow:
                // Not inhibited on a flameout or a failure: that is exactly when it matters.
                return (t.RotorRpmPercent, coldOrStarting || (onGround && t.RotorRpmPercent < 55));

            case WarningId.RotorRpmHigh:
                return (t.RotorRpmPercent, coldOrStarting || !rotorTurning);

            case WarningId.SinkRate:
            {
                // Time to the ground, not height and not rate: 200 m is comfortable at
                // 300 fpm and fatal at 4000, and only the quotient knows the difference.
                double rod = -t.VerticalSpeed;                       // positive = descending
                double ttg = rod > 0.1 ? t.HeightAgl / rod : 60.0;
                // Clamped at 60 s rather than left unbounded, because the filter has to
                // walk down from wherever it is: starting at 999 s it would take several
                // seconds to reach a credible number, and the warning would arrive after
                // the ground did.
                return (Math.Min(ttg, 60.0), onGround || t.HeightAgl < 10.0 || rod < 3.0);
            }

            case WarningId.VortexRing:
                return (t.VrsSeverity, onGround || !rotorTurning);

            case WarningId.TailRotorAuthority:
                return (t.TailRotorSaturated ? 1 : 0, onGround || !rotorTurning);

            case WarningId.TorqueHigh:
                return (t.TorquePercent, !rotorTurning);

            case WarningId.BladeStall:
                return (t.StalledFraction * 100.0, onGround || !rotorTurning);

            case WarningId.BladeLoading:
                return (t.BladeLoading, onGround || !rotorTurning);

            case WarningId.FuelLow:
            {
                double minutes = t.FuelFlow > 1e-7 ? t.FuelKg / t.FuelFlow / 60.0 : 600.0;
                return (Math.Min(minutes, 600.0), t.FuelFlow <= 1e-7);
            }

            case WarningId.PowerLimit:
                return (t.TorqueLimited ? 1 : 0, !rotorTurning);

            default:
                return (0, true);
        }
    }

    /// <summary>Metres per second to feet per minute.</summary>
    private const double Fpm = 196.850394;

    /// <summary>
    /// Facts, per D-005a. Every string here is a state of the aircraft with its number
    /// attached. None of them is an instruction, and none of them is an inference about
    /// what the pilot should do next.
    /// </summary>
    private static string Describe(WarningId id, double v, in FlightTelemetry t) => id switch
    {
        WarningId.EngineOut => t.Engine switch
        {
            EngineState.Flameout => "ENGINE FLAMEOUT",
            EngineState.Failed => "ENGINE FAILED",
            _ => "ENGINE OUT",
        },
        WarningId.RotorRpmLow => $"ROTOR RPM {v:F0}%",
        WarningId.RotorRpmHigh => $"ROTOR RPM {v:F0}%",
        WarningId.SinkRate => $"SINK {-t.VerticalSpeed * Fpm:F0} FPM {t.HeightAgl:F0} M",
        WarningId.VortexRing => $"VORTEX RING {v:F2}",
        WarningId.TailRotorAuthority => "TAIL ROTOR AUTHORITY",
        WarningId.TorqueHigh => $"TORQUE {v:F0}%",
        WarningId.BladeStall => $"BLADE STALL {v:F0}%",
        WarningId.BladeLoading => $"BLADE LOADING {v:F3}",
        WarningId.FuelLow => v >= 99 ? $"FUEL {t.FuelKg:F0} KG" : $"FUEL {v:F0} MIN",
        WarningId.PowerLimit => $"POWER LIMIT {t.TorquePercent:F0}%",
        _ => id.ToString(),
    };

    // ------------------------------------------------------------- the condition set

    /// <summary>
    /// The conditions, their thresholds, and where each number came from.
    ///
    /// Everything below was measured on the Workhorse (the UH-1H analogue) in
    /// <c>tools/simlab</c> before it was written down. The measurements that set the filter
    /// time constants and the hysteresis gaps, over 4.2 s of <i>settled</i> flight at 240 Hz:
    ///
    /// <code>
    ///   signal              hover                    100 kt level
    ///   torque %            75.3 mean, 71.6-77.4     57.5 mean, 56.0-58.4
    ///   Nr %                99.99, swings 0.40       99.99, swings 0.12
    ///   stalled fraction    0%                       2.6% mean, swings 0-5%
    ///   Ct/sigma            0.0748, swings 0.0008    0.0723, swings 0.059-0.085
    ///   advancing tip Mach  0.703                    0.802, swings 0.709-0.852
    /// </code>
    ///
    /// Those swings are 2/rev on a two-bladed rotor. They are the reason every threshold
    /// here sits on a filtered value rather than a sampled one.
    ///
    /// And the trimmed level-flight envelope, which is what "normal" has to mean:
    ///
    /// <code>
    ///   kt            0     40     60    100    120    130
    ///   Ct/sigma   .075   .071   .072   .073   .075   .080
    ///   torque %     76     47     44     57     75     91
    ///   stalled      0%     0%     0%   2.6%   5.0%   5.8%
    ///   fuel kg/h   256    158    148    193    253    307
    /// </code>
    /// </summary>
    private static WarningCondition[] Build()
    {
        var conditions = new[]
        {
            // The engine is a boolean, so there is no value gap to put hysteresis in and
            // the dwell time is the whole defence. 0.4 s in: a flameout is not ambiguous.
            // 3 s out: a relight that lasts two seconds is not a recovery.
            new WarningCondition(WarningId.EngineOut, true, 0.05, 0.40, 3.0,
                new WarningBand(WarningSeverity.Warning, 0.5, 0.5)),

            // The Huey's low-rotor audio warning fires at 300 rpm against a nominal 324,
            // which is 92.6% - so 92% is the real aircraft's own number, not a guess, and
            // it is the threshold the HUD already draws LOW ROTOR at. Measured Nr ripple
            // in steady flight is 0.40 points at worst, so the 2-point hysteresis gaps are
            // five times the noise.
            new WarningCondition(WarningId.RotorRpmLow, false, 0.25, 0.25, 1.2,
                new WarningBand(WarningSeverity.Advisory, 97.0, 98.0),
                new WarningBand(WarningSeverity.Caution, 95.0, 96.5),
                new WarningBand(WarningSeverity.Warning, 92.0, 94.0)),

            // Seconds to the ground. Inhibited below 3 m/s of descent and below 10 m AGL,
            // so a normal approach and the flare do not trip it; 600 fpm at 30 ft does,
            // which is what any aircraft's sink-rate warning is for.
            // Short filter (0.25 s) because this one is genuinely urgent and the height
            // and rate feeding it are already smooth.
            new WarningCondition(WarningId.SinkRate, false, 0.25, 0.30, 1.5,
                new WarningBand(WarningSeverity.Caution, 12.0, 16.0),
                new WarningBand(WarningSeverity.Warning, 6.0, 9.0)),

            // Overspeed. The governor holds 100.0% to a tenth in every trimmed condition
            // measured, and the only things that move it are a collective dump, an
            // autorotation flare and a broken governor - all of which deserve saying.
            new WarningCondition(WarningId.RotorRpmHigh, true, 0.25, 0.25, 1.2,
                new WarningBand(WarningSeverity.Advisory, 103.0, 102.0),
                new WarningBand(WarningSeverity.Caution, 105.0, 103.5),
                new WarningBand(WarningSeverity.Warning, 110.0, 107.0)),

            // Vortex ring. The measured entry (simlab `vrs`) peaks at 0.88 severity and
            // takes 4.1 s to let go after the collective comes back down, which is why the
            // clear thresholds are generous: the pilot leaves the condition several
            // seconds before the rotor does, and a warning that clears on the pilot's
            // schedule rather than the rotor's is lying. 0.25 is the threshold the HUD
            // already draws VORTEX RING at.
            new WarningCondition(WarningId.VortexRing, true, 0.35, 0.30, 2.5,
                new WarningBand(WarningSeverity.Advisory, 0.12, 0.07),
                new WarningBand(WarningSeverity.Caution, 0.25, 0.15),
                new WarningBand(WarningSeverity.Warning, 0.50, 0.35)),

            // Pedal on the stop. Boolean, so dwell again: 0.5 s to arm, 3 s to clear,
            // because a tail rotor that saturates once a second is still saturated.
            new WarningCondition(WarningId.TailRotorAuthority, true, 0.10, 0.50, 3.0,
                new WarningBand(WarningSeverity.Caution, 0.5, 0.5)),

            // Torque. Measured maxima: 76% in a hover, 91% at Vne, and the sea-level
            // ceiling is power-bound at about 93% of the 30 kN.m transmission limit - so
            // at full health the Warning band is unreachable, and that is correct. It
            // becomes reachable exactly when the transmission is hurt: at 80% health the
            // measured figure is 99.9%, because the limit came down to meet the demand.
            // The 3-point gaps are half the measured 5.8-point hover ripple, which the
            // 0.8 s filter removes first. 92 and 100 are the thresholds the HUD already
            // colours at.
            new WarningCondition(WarningId.TorqueHigh, true, 0.8, 0.5, 2.0,
                new WarningBand(WarningSeverity.Advisory, 85.0, 82.0),
                new WarningBand(WarningSeverity.Caution, 92.0, 89.0),
                new WarningBand(WarningSeverity.Warning, 100.0, 97.0)),

            // Retreating blade stall, as a measured fraction of blade elements past stall
            // rather than as a speed limit. Trimmed level flight reaches 5.8% at 130 kt,
            // and the instantaneous figure swings 0-5% at 100 kt, so the Advisory band at
            // 8% needs the 1.2 s filter to sit still - the longest filter here, and
            // deliberately so. 18% is the threshold the HUD already draws BLADE STALL at.
            new WarningCondition(WarningId.BladeStall, true, 1.2, 0.6, 2.5,
                new WarningBand(WarningSeverity.Advisory, 8.0, 5.5),
                new WarningBand(WarningSeverity.Caution, 18.0, 13.0),
                new WarningBand(WarningSeverity.Warning, 35.0, 28.0)),

            // Blade loading: stall approaching, rather than stall arrived. 1 g level
            // flight never exceeds Ct/sigma 0.080 anywhere in the envelope, and a pulled
            // turn at 100 kt reaches 0.102 before the stalled fraction moves at all - so
            // 0.10 is the first honest sign that the disc is running out of margin.
            // No Warning band: by the time it would fire, BladeStall has already spoken.
            new WarningCondition(WarningId.BladeLoading, true, 1.0, 0.6, 2.5,
                new WarningBand(WarningSeverity.Advisory, 0.100, 0.090),
                new WarningBand(WarningSeverity.Caution, 0.130, 0.115)),

            // Endurance at the current fuel flow, in minutes. 800 kg of fuel is 3.1 h in a
            // hover and 5.4 h at 60 kt, so a fraction-of-tank light would mean two
            // completely different things in those two conditions; minutes remaining means
            // one thing. Long filter, because fuel flow steps with every collective input
            // and the number is useless if it jitters.
            new WarningCondition(WarningId.FuelLow, false, 6.0, 2.0, 5.0,
                new WarningBand(WarningSeverity.Advisory, 45.0, 50.0),
                new WarningBand(WarningSeverity.Caution, 20.0, 23.0),
                new WarningBand(WarningSeverity.Warning, 10.0, 12.0)),

            // The engine or the transmission is clipping the demand. Advisory only, and on
            // purpose: measured at 100% duty for the whole of a hard climb even at full
            // health, so as a caution it would be noise. As a fact - there is no more -
            // it is worth a line on the panel.
            new WarningCondition(WarningId.PowerLimit, true, 0.5, 1.0, 2.0,
                new WarningBand(WarningSeverity.Advisory, 0.5, 0.5)),
        };

        // The array is indexed by (int)WarningId throughout, so the declaration order here
        // must match the enum. Cheap to check, expensive to get wrong.
        for (int i = 0; i < conditions.Length; i++)
            if ((int)conditions[i].Id != i)
                throw new InvalidOperationException(
                    $"warning condition {conditions[i].Id} is at index {i}");
        if (conditions.Length != Enum.GetValues<WarningId>().Length)
            throw new InvalidOperationException("a WarningId has no condition");

        return conditions;
    }
}
