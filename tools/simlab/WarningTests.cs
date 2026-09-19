using System;
using System.Collections.Generic;
using System.Linq;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The caution and warning system, checked the way the flight model is: by driving it and
/// measuring what comes out.
///
/// There are four things a warning system can get wrong, and every one of them survives a
/// code review:
///
/// <list type="number">
/// <item>It fires at the wrong number. Invisible unless somebody sweeps the threshold.</item>
/// <item>It flickers. Looks perfect in the source - one comparison against one constant -
/// and is unreadable in flight, because none of these signals is smooth. This is the one
/// that has already bitten this project: an unlatched check logged 1141 rotor strikes for
/// a single impact.</item>
/// <item>It orders things wrongly, so the pilot reads FUEL while the rotor unwinds.</item>
/// <item>It makes a noise nobody can tell apart from the other noise. This is the only
/// part of the project that cannot be checked by looking at a screenshot, so it gets
/// numbers instead: dominant frequency and duty cycle.</item>
/// </list>
/// </summary>
public static class WarningTests
{
    private const int Rate = 44100;

    // ------------------------------------------------------------------ helpers

    /// <summary>An aircraft with nothing wrong with it: cruise, everything inside limits.</summary>
    private static FlightTelemetry Nominal() => new()
    {
        RotorRpmPercent = 100.0,
        TorquePercent = 60.0,
        AirspeedTrue = 50.0,
        GroundSpeed = 50.0,
        VerticalSpeed = 0.0,
        HeightAgl = 300.0,
        FuelKg = 500.0,
        FuelFlow = 0.045,          // 162 kg/h -> 185 minutes
        VrsSeverity = 0.0,
        BladeLoading = 0.072,      // measured trimmed cruise
        StalledFraction = 0.025,   // measured trimmed cruise at 100 kt
        TipMach = 0.80,
        Engine = EngineState.Running,
        OnGround = false,
        TorqueLimited = false,
        TailRotorSaturated = false,
    };

    /// <summary>
    /// Put one condition's source signal at a chosen value. The inverse of
    /// <c>CautionWarningSystem.Source</c>, and the only reason the threshold sweep below
    /// can be table-driven rather than eleven copies of the same code.
    /// </summary>
    private static void SetSource(ref FlightTelemetry t, WarningId id, double v)
    {
        switch (id)
        {
            case WarningId.EngineOut:
                t.Engine = v >= 0.5 ? EngineState.Failed : EngineState.Running;
                break;
            case WarningId.RotorRpmLow:
            case WarningId.RotorRpmHigh:
                t.RotorRpmPercent = v;
                break;
            case WarningId.SinkRate:
                // Time to ground, at a fixed height: seconds = height / rate of descent.
                t.HeightAgl = 200.0;
                t.VerticalSpeed = -(200.0 / Math.Max(v, 0.1));
                break;
            case WarningId.VortexRing:
                t.VrsSeverity = v;
                break;
            case WarningId.TailRotorAuthority:
                t.TailRotorSaturated = v >= 0.5;
                break;
            case WarningId.TorqueHigh:
                t.TorquePercent = v;
                break;
            case WarningId.BladeStall:
                t.StalledFraction = v / 100.0;
                break;
            case WarningId.BladeLoading:
                t.BladeLoading = v;
                break;
            case WarningId.FuelLow:
                t.FuelFlow = t.FuelKg / Math.Max(v, 0.1) / 60.0;
                break;
            case WarningId.PowerLimit:
                t.TorqueLimited = v >= 0.5;
                break;
        }
    }

    private static void Drive(CautionWarningSystem cws, in FlightTelemetry t,
                              double seconds, double dt = 1.0 / 60.0)
    {
        int steps = (int)Math.Round(seconds / dt);
        for (int i = 0; i < steps; i++) cws.Update(t, dt);
    }

    /// <summary>Long enough for the filter to arrive and the dwell timer to expire.</summary>
    private static double SettleTime(WarningCondition c) => c.FilterTau * 7.0 + c.ArmSeconds + 1.0;

    // ------------------------------------------------------- 1. thresholds

    /// <summary>
    /// Every band of every condition, swept.
    ///
    /// Three claims per band, and the middle one is the interesting one:
    /// the condition fires just past <c>Arm</c>; it does NOT let go when the signal
    /// retreats to between <c>Clear</c> and <c>Arm</c>; it does let go past <c>Clear</c>.
    /// A threshold with no gap passes the first and third and fails the second, which is
    /// exactly the bug this file exists to catch.
    /// </summary>
    public static string? Thresholds()
    {
        string? failure = null;
        var cws = new CautionWarningSystem();

        Console.WriteLine("  each band of each condition, swept across its arm and clear thresholds");
        Console.WriteLine($"  {"condition",-20} {"band",-9} {"arm",8} {"clear",8}  " +
                          $"{"fires",-6} {"holds",-6} {"clears",-6}");

        foreach (WarningCondition c in cws.Conditions)
        {
            for (int bi = 0; bi < c.Bands.Count; bi++)
            {
                WarningBand band = c.Bands[bi];
                double gap = Math.Abs(band.Arm - band.Clear);
                double step = Math.Max(gap * 0.5, Math.Abs(band.Arm) * 0.02 + 1e-6);

                // Aim halfway to the NEXT band's arm rather than a fixed step past this
                // one. The first version used a fixed 2% step and overshot: rotor
                // overspeed has its Advisory at 103 and its Caution at 105, so "2% past
                // 103" is 105.06, and the test reported that the Advisory band never
                // fired when what had actually happened was that the sweep had walked
                // straight through it into the Caution.
                double armValue;
                if (bi + 1 < c.Bands.Count) armValue = (band.Arm + c.Bands[bi + 1].Arm) * 0.5;
                else armValue = c.HigherIsWorse ? band.Arm + step : band.Arm - step;

                double clearValue = c.HigherIsWorse ? band.Clear - step : band.Clear + step;
                double holdValue = (band.Arm + band.Clear) * 0.5;

                // --- fires ---------------------------------------------------
                cws.Reset();
                var t = Nominal();
                Drive(cws, t, 3.0);
                bool quietAtNominal = cws[c.Id].Active == WarningSeverity.None;

                SetSource(ref t, c.Id, armValue);
                Drive(cws, t, SettleTime(c));
                bool fires = cws[c.Id].Active == band.Severity;

                // --- holds, between clear and arm ----------------------------
                bool holds = true;
                if (gap > 1e-9)
                {
                    SetSource(ref t, c.Id, holdValue);
                    Drive(cws, t, SettleTime(c) + c.ClearSeconds);
                    holds = cws[c.Id].Active >= band.Severity;
                }

                // --- clears --------------------------------------------------
                SetSource(ref t, c.Id, clearValue);
                Drive(cws, t, SettleTime(c) + c.ClearSeconds);
                bool clears = cws[c.Id].Active < band.Severity;

                Console.WriteLine($"  {c.Id,-20} {band.Severity,-9} {band.Arm,8:F3} {band.Clear,8:F3}  " +
                                  $"{(fires ? "yes" : "NO"),-6} " +
                                  $"{(gap > 1e-9 ? holds ? "yes" : "NO" : "n/a"),-6} " +
                                  $"{(clears ? "yes" : "NO"),-6}");

                if (!quietAtNominal)
                    failure ??= $"{c.Id} is already active with a nominal aircraft";
                if (!fires)
                    failure ??= $"{c.Id} did not reach {band.Severity} at {armValue:F3} " +
                                $"(arm {band.Arm:F3}), got {cws[c.Id].Active}";
                if (!holds)
                    failure ??= $"{c.Id} dropped out of {band.Severity} at {holdValue:F3}, " +
                                $"which is inside the hysteresis gap {band.Clear:F3}..{band.Arm:F3}";
                if (!clears)
                    failure ??= $"{c.Id} would not clear {band.Severity} at {clearValue:F3} " +
                                $"(clear {band.Clear:F3})";
            }
        }

        // The inhibits. A cold aircraft on the skids has a stopped rotor, no fuel flow and
        // an engine that is off, and every one of those looks like an emergency to a naive
        // comparison. It must say nothing at all.
        cws.Reset();
        var cold = Nominal();
        cold.RotorRpmPercent = 0;
        cold.TorquePercent = 0;
        cold.Engine = EngineState.Off;
        cold.OnGround = true;
        cold.HeightAgl = 0;
        cold.FuelFlow = 0;
        cold.BladeLoading = 0;
        cold.StalledFraction = 0;
        Drive(cws, cold, 20.0);
        Console.WriteLine();
        Console.WriteLine($"  cold aircraft on the skids: {cws.Summary()}");
        if (cws.Items.Count != 0)
            failure ??= $"a cold, parked aircraft raises {cws.Items.Count} alert(s): {cws.Summary()}";

        // And a healthy aircraft in the cruise, for thirty seconds.
        cws.Reset();
        Drive(cws, Nominal(), 30.0);
        Console.WriteLine($"  healthy cruise, 30 s:       {cws.Summary()}");
        if (cws.Items.Count != 0)
            failure ??= $"a healthy cruise raises {cws.Summary()}";

        return failure;
    }

    // ------------------------------------------------------- 2. hysteresis

    /// <summary>
    /// Drive a signal slowly across a threshold with the noise it really has, and count
    /// how many times the alert changes state.
    ///
    /// Both signals here are the measured ones. Torque ripples 5.8 points peak-to-peak in
    /// a steady hover; the stalled fraction swings the whole 0-5% band at 2/rev, which at
    /// 324 rpm and two blades is 10.8 Hz. A bare comparison against the threshold is
    /// therefore not a condition detector at all - it is a blade-azimuth detector - and
    /// the ratio between the two counts below is the whole argument for this file.
    /// </summary>
    public static string? Hysteresis()
    {
        string? failure = null;
        const double dt = 1.0 / 60.0;

        // --- Torque: a slow ramp through the caution band, with measured ripple -------
        {
            var cws = new CautionWarningSystem();
            var rng = new Random(4242);
            var t = Nominal();
            int naive = 0;
            bool naiveState = false;
            double peak = 0;

            for (double time = 0; time < 40.0; time += dt)
            {
                // 88 -> 96 -> 88 over forty seconds: it crosses the 92 arm once and the
                // 89 clear once, and nothing else about it is ambiguous.
                double mean = time < 20 ? 88 + 8 * (time / 20) : 96 - 8 * ((time - 20) / 20);
                double raw = mean + (rng.NextDouble() - 0.5) * 5.8;   // measured hover ripple
                peak = Math.Max(peak, raw);
                SetSource(ref t, WarningId.TorqueHigh, raw);
                cws.Update(t, dt);

                bool n = raw >= 92.0;
                if (n != naiveState) { naive++; naiveState = n; }
            }

            WarningCondition c = cws[WarningId.TorqueHigh];
            Console.WriteLine("  TORQUE, ramped 88 -> 96 -> 88 over 40 s with the measured 5.8-point ripple");
            Console.WriteLine($"    naive `raw >= 92`:        {naive,5} transitions");
            Console.WriteLine($"    filtered + hysteresis:    {c.TransitionCount,5} transitions, " +
                              $"{c.RaiseCount} raise(s)");

            if (naive < 20)
                failure ??= $"the test signal is not noisy enough to prove anything " +
                            $"({naive} naive transitions)";
            if (c.TransitionCount > 4)
                failure ??= $"torque alert changed state {c.TransitionCount} times on one " +
                            $"crossing - it is flickering";
            if (c.RaiseCount != 1)
                failure ??= $"torque caution raised {c.RaiseCount} times on one crossing";
        }

        // --- Blade stall: the 2/rev swing, which is the pathological case -------------
        {
            var cws = new CautionWarningSystem();
            var t = Nominal();
            int naive = 0;
            bool naiveState = false;

            for (double time = 0; time < 40.0; time += dt)
            {
                double mean = time < 20 ? 14 + 8 * (time / 20) : 22 - 8 * ((time - 20) / 20);
                // Two-per-rev at 324 rpm and two blades: 10.8 Hz, +/- 2.5 points, which is
                // the swing measured in level flight at 100 kt.
                double raw = Math.Max(0, mean + 2.5 * Math.Sin(Math.Tau * 10.8 * time));
                SetSource(ref t, WarningId.BladeStall, raw);
                cws.Update(t, dt);

                bool n = raw >= 18.0;
                if (n != naiveState) { naive++; naiveState = n; }
            }

            WarningCondition c = cws[WarningId.BladeStall];
            Console.WriteLine("  BLADE STALL, ramped 14 -> 22 -> 14% with the measured 2/rev swing");
            Console.WriteLine($"    naive `raw >= 18`:        {naive,5} transitions");
            Console.WriteLine($"    filtered + hysteresis:    {c.TransitionCount,5} transitions, " +
                              $"{c.RaiseCount} raise(s)");

            if (naive < 20)
                failure ??= $"the 2/rev test signal is not crossing enough ({naive})";
            if (c.TransitionCount > 4)
                failure ??= $"blade stall alert changed state {c.TransitionCount} times on " +
                            $"one crossing - the 2/rev is getting through";
            if (c.RaiseCount != 1)
                failure ??= $"blade stall caution raised {c.RaiseCount} times on one crossing";
        }

        // --- A boolean, which has no value gap to hide in -----------------------------
        // TorqueLimited and TailRotorSaturated flicker on and off at frame rate when the
        // pedal or the governor is sitting exactly on its stop. Value hysteresis cannot
        // help; the dwell time is the entire defence, so it gets its own measurement.
        {
            var cws = new CautionWarningSystem();
            var rng = new Random(99);
            var t = Nominal();
            int naive = 0;
            bool naiveState = false;

            for (double time = 0; time < 30.0; time += dt)
            {
                // Saturated 70% of frames at random: unmistakably a saturated tail rotor,
                // and unmistakably not a steady signal.
                bool sat = rng.NextDouble() < 0.70;
                SetSource(ref t, WarningId.TailRotorAuthority, sat ? 1 : 0);
                cws.Update(t, dt);
                if (sat != naiveState) { naive++; naiveState = sat; }
            }

            WarningCondition c = cws[WarningId.TailRotorAuthority];
            Console.WriteLine("  TAIL ROTOR AUTHORITY, saturated on 70% of frames at random");
            Console.WriteLine($"    naive `flag`:             {naive,5} transitions");
            Console.WriteLine($"    dwell 0.5 s in / 3 s out: {c.TransitionCount,5} transitions, " +
                              $"{c.RaiseCount} raise(s)");

            if (naive < 100) failure ??= $"the boolean test signal is not flickering ({naive})";
            if (c.TransitionCount > 2)
                failure ??= $"tail rotor alert changed state {c.TransitionCount} times on a " +
                            $"flickering boolean";
            if (c.Active != WarningSeverity.Caution)
                failure ??= "a tail rotor saturated on 70% of frames raised nothing";
        }

        return failure;
    }

    // ------------------------------------------------------- 3. latching

    /// <summary>
    /// A warning that has fired stays visible until it is both gone and acknowledged.
    ///
    /// The reference failure is real: an unlatched impact check in this project logged
    /// 1141 separate rotor strikes for one collision, because it asked "is this true right
    /// now" on every physics step. The same shape of bug in a warning system produces a
    /// panel that blinks, a chime that machine-guns, and a condition that vanishes in the
    /// half-second the pilot spends looking outside.
    /// </summary>
    public static string? Latching()
    {
        string? failure = null;
        const double dt = 1.0 / 60.0;
        var cws = new CautionWarningSystem();
        var t = Nominal();

        // --- A transient: vortex ring builds, peaks, and goes ------------------------
        SetSource(ref t, WarningId.VortexRing, 0.60);
        Drive(cws, t, 4.0, dt);
        bool firedWarning = cws[WarningId.VortexRing].Active == WarningSeverity.Warning;
        bool master = cws.MasterWarning;

        SetSource(ref t, WarningId.VortexRing, 0.0);
        Drive(cws, t, 10.0, dt);
        WarningCondition vrs = cws[WarningId.VortexRing];
        bool goneButShown = vrs.Active == WarningSeverity.None &&
                            vrs.Displayed == WarningSeverity.Warning;
        bool stale = cws.Items.Count == 1 && cws.Items[0].Stale;

        Console.WriteLine($"  vortex ring 0.60 for 4 s, then clear for 10 s:");
        Console.WriteLine($"    fired warning        {firedWarning}");
        Console.WriteLine($"    master warning       {master} -> {cws.MasterWarning} (condition gone)");
        Console.WriteLine($"    still on the panel   {goneButShown}, marked stale {stale}");
        Console.WriteLine($"    panel: {cws.Summary()}");

        if (!firedWarning) failure ??= "vortex ring at 0.60 did not reach Warning";
        if (!master) failure ??= "master warning did not light";
        if (cws.MasterWarning) failure ??= "master warning stayed lit after the condition cleared";
        if (!goneButShown) failure ??= "the cleared warning dropped off the panel unacknowledged";
        if (!stale) failure ??= "a latched, cleared warning is not marked stale";

        // --- Acknowledge drops it ------------------------------------------------------
        int dropped = cws.Acknowledge();
        Console.WriteLine($"    acknowledge dropped  {dropped}, panel now: {cws.Summary()}");
        if (dropped != 1) failure ??= $"acknowledge dropped {dropped} conditions, expected 1";
        if (cws.Items.Count != 0) failure ??= "acknowledge left a cleared warning on the panel";

        // --- One episode is one raise, however ragged the signal -----------------------
        cws.Reset();
        var rng = new Random(7);
        t = Nominal();
        int naiveEdges = 0;
        bool naiveState = false;
        for (double time = 0; time < 20.0; time += dt)
        {
            // Severity chattering around the caution threshold for twenty seconds: this
            // is the 1141-rotor-strikes signal.
            double v = 0.26 + (rng.NextDouble() - 0.5) * 0.10;
            SetSource(ref t, WarningId.VortexRing, v);
            cws.Update(t, dt);
            bool n = v >= 0.25;
            if (n && !naiveState) naiveEdges++;
            naiveState = n;
        }
        Console.WriteLine();
        Console.WriteLine("  vortex ring chattering across 0.25 for 20 s:");
        Console.WriteLine($"    naive rising edges   {naiveEdges}");
        Console.WriteLine($"    raises               {cws[WarningId.VortexRing].RaiseCount}");
        if (naiveEdges < 50)
            failure ??= $"the chatter signal only produced {naiveEdges} naive edges";
        if (cws[WarningId.VortexRing].RaiseCount != 1)
            failure ??= $"one episode produced {cws[WarningId.VortexRing].RaiseCount} raises";

        // --- A warning that is still true cannot be acknowledged away ------------------
        cws.Reset();
        t = Nominal();
        SetSource(ref t, WarningId.RotorRpmLow, 90.0);
        Drive(cws, t, 4.0, dt);
        bool hornBefore = cws.LowRotorHorn;
        cws.Acknowledge();
        Drive(cws, t, 1.0, dt);
        bool hornAfter = cws.LowRotorHorn;
        Console.WriteLine();
        Console.WriteLine($"  rotor at 90%: horn {hornBefore}, horn after acknowledge {hornAfter}");
        Console.WriteLine($"    master caution after acknowledge: {cws.MasterCaution}");
        if (!hornBefore) failure ??= "rotor at 90% did not sound the horn";
        if (!hornAfter) failure ??= "acknowledging silenced a warning that is still true";
        if (cws.MasterCaution) failure ??= "master caution survived an acknowledge";

        // --- And it raises again if it comes back --------------------------------------
        SetSource(ref t, WarningId.RotorRpmLow, 100.0);
        Drive(cws, t, 6.0, dt);
        int before = cws[WarningId.RotorRpmLow].RaiseCount;
        SetSource(ref t, WarningId.RotorRpmLow, 90.0);
        Drive(cws, t, 4.0, dt);
        int after = cws[WarningId.RotorRpmLow].RaiseCount;
        Console.WriteLine($"  recovered, then decayed again: raises {before} -> {after}");
        if (after != before + 1)
            failure ??= $"a second, genuine episode raised {after - before} times";

        return failure;
    }

    // ------------------------------------------------------- 4. priority

    /// <summary>
    /// Nine things wrong at once. What does the pilot read first?
    ///
    /// The telemetry here is deliberately synthetic - an aircraft cannot really have a
    /// failed engine and 100% torque at the same time - because the point is the ordering
    /// rule, not the flight condition.
    /// </summary>
    public static string? Priority()
    {
        string? failure = null;
        var cws = new CautionWarningSystem();
        var t = Nominal();

        SetSource(ref t, WarningId.EngineOut, 1);            // warning
        SetSource(ref t, WarningId.RotorRpmLow, 89);         // warning
        SetSource(ref t, WarningId.SinkRate, 5);             // warning
        SetSource(ref t, WarningId.VortexRing, 0.30);        // caution
        SetSource(ref t, WarningId.TorqueHigh, 94);          // caution
        SetSource(ref t, WarningId.BladeStall, 20);          // caution
        SetSource(ref t, WarningId.BladeLoading, 0.105);     // advisory
        SetSource(ref t, WarningId.FuelLow, 8);              // warning
        SetSource(ref t, WarningId.TailRotorAuthority, 1);   // caution
        SetSource(ref t, WarningId.PowerLimit, 1);           // advisory

        Drive(cws, t, 45.0);

        Console.WriteLine("  ten conditions at once, in the order the panel puts them:");
        foreach (WarningItem i in cws.Items)
            Console.WriteLine($"    {i.Severity,-9} {i.Id,-20} {i.Text}");

        // The expected order: every Warning first, in WarningId (priority) order, then
        // every Caution, then every Advisory.
        var expected = new[]
        {
            WarningId.EngineOut,            // W - the cause
            WarningId.RotorRpmLow,          // W - seconds to unrecoverable
            WarningId.SinkRate,             // W - seconds to the ground
            WarningId.FuelLow,              // W - climbs the list on severity alone
            WarningId.VortexRing,           // C
            WarningId.TailRotorAuthority,   // C
            WarningId.TorqueHigh,           // C
            WarningId.BladeStall,           // C
            WarningId.BladeLoading,         // A
            WarningId.PowerLimit,           // A
        };

        if (cws.Items.Count != expected.Length)
            failure ??= $"{cws.Items.Count} conditions on the panel, expected {expected.Length}";
        else
            for (int i = 0; i < expected.Length; i++)
                if (cws.Items[i].Id != expected[i])
                    failure ??= $"position {i + 1} is {cws.Items[i].Id}, expected {expected[i]}";

        // Severity sorts before priority: FUEL is the second-lowest-ranked condition in
        // the enum and still outranks four cautions, because ten minutes of fuel is a
        // warning and 0.30 of vortex ring is not.
        int fuelPos = cws.Items.ToList().FindIndex(x => x.Id == WarningId.FuelLow);
        int vrsPos = cws.Items.ToList().FindIndex(x => x.Id == WarningId.VortexRing);
        Console.WriteLine();
        Console.WriteLine($"  FUEL (rank {(int)WarningId.FuelLow}) sits at position {fuelPos + 1}; " +
                          $"VORTEX RING (rank {(int)WarningId.VortexRing}) at {vrsPos + 1}");
        if (fuelPos > vrsPos)
            failure ??= "a caution outranked a warning";

        // The horn and the beeper are mutually exclusive: low rotor gets the horn, and
        // anything else at warning level gets the beeper, and never both for one cause.
        Console.WriteLine($"  horn {cws.LowRotorHorn}, warning tone {cws.WarningTone}, " +
                          $"master warning {cws.MasterWarning}, master caution {cws.MasterCaution}");
        if (!cws.LowRotorHorn) failure ??= "low rotor at 89% did not sound the horn";
        if (!cws.WarningTone) failure ??= "no warning tone with four warnings up";
        if (!cws.MasterCaution) failure ??= "master caution is not lit with four cautions up";

        // --- D-005a: facts, not instructions -------------------------------------------
        // Every string on the panel has to be a state of the aircraft. This is a cheap
        // check and it is the one that stops a well-meaning edit turning "ROTOR RPM 92%"
        // into "LOWER COLLECTIVE" six months from now.
        string[] imperatives =
        {
            "LOWER", "RAISE", "PULL", "PUSH", "INCREASE", "REDUCE", "APPLY",
            "CHECK", "LAND ", "ENTER", "RECOVER", "AVOID", "SHOULD", "MUST", "NOW",
        };
        foreach (WarningItem i in cws.Items)
            foreach (string bad in imperatives)
                if (i.Text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    failure ??= $"\"{i.Text}\" reads as an instruction, not a fact (D-005a)";

        return failure;
    }

    // ------------------------------------------------------- 5. tones

    /// <summary>
    /// Magnitude of one frequency in a buffer, Goertzel with a Hann window.
    ///
    /// A full FFT would work too, but the question here is narrow - how much energy is at
    /// 400 Hz, and how much at 950 - and Goertzel answers exactly that in twenty lines
    /// with no dependency.
    /// </summary>
    private static double Tone(float[] x, double freq)
    {
        double w = Math.Tau * freq / Rate;
        double coeff = 2.0 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        int n = x.Length;
        for (int i = 0; i < n; i++)
        {
            double win = 0.5 - 0.5 * Math.Cos(Math.Tau * i / (n - 1));
            double s0 = x[i] * win + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        return Math.Sqrt(Math.Max(s1 * s1 + s2 * s2 - coeff * s1 * s2, 0)) / n;
    }

    private static double Rms(float[] x)
    {
        double sum = 0;
        foreach (float v in x) sum += (double)v * v;
        return Math.Sqrt(sum / Math.Max(x.Length, 1));
    }

    /// <summary>
    /// What fraction of the buffer is sounding, measured in 10 ms frames against 25% of
    /// the loudest frame.
    ///
    /// This is the rhythm axis, and it is what separates the horn from the beeper even for
    /// a listener who cannot place a pitch: the horn is continuous and the beeper is not.
    /// </summary>
    private static double Duty(float[] x)
    {
        int frame = Rate / 100;
        var levels = new List<double>();
        for (int i = 0; i + frame <= x.Length; i += frame)
        {
            double s = 0;
            for (int k = 0; k < frame; k++) s += (double)x[i + k] * x[i + k];
            levels.Add(Math.Sqrt(s / frame));
        }
        double max = 0;
        foreach (double l in levels) max = Math.Max(max, l);
        if (max <= 1e-9) return 0;
        int on = 0;
        foreach (double l in levels) if (l > max * 0.25) on++;
        return (double)on / levels.Count;
    }

    private static float[] RenderTone(bool horn, bool warning, bool chime, double seconds)
    {
        var synth = new WarningSynth(Rate);
        if (chime) synth.TriggerCaution();
        var all = new float[(int)(Rate * seconds)];
        const int block = 512;
        for (int i = 0; i < all.Length; i += block)
        {
            int n = Math.Min(block, all.Length - i);
            synth.Render(all.AsSpan(i, n), n, horn, warning, (double)n / Rate);
        }
        return all;
    }

    /// <summary>
    /// The tones: audible, distinguishable, and silent when nothing is wrong.
    /// </summary>
    public static string? Tones()
    {
        string? failure = null;

        float[] silent = RenderTone(false, false, false, 1.0);
        float[] horn = RenderTone(true, false, false, 2.0);
        float[] warn = RenderTone(false, true, false, 2.0);
        float[] chime = RenderTone(false, false, true, 3.0);
        float[] both = RenderTone(true, true, true, 2.0);

        double peakSilent = 0;
        foreach (float v in silent) peakSilent = Math.Max(peakSilent, Math.Abs(v));

        Console.WriteLine($"  {"voice",-16} {"rms",7} {"duty",6}  " +
                          $"{"400 Hz",8} {"660",8} {"950",8} {"990",8}");
        void Row(string name, float[] x) =>
            Console.WriteLine($"  {name,-16} {Rms(x),7:F4} {Duty(x),6:F2}  " +
                              $"{Tone(x, 400),8:F5} {Tone(x, 660),8:F5} " +
                              $"{Tone(x, 950),8:F5} {Tone(x, 990),8:F5}");

        Row("silence", silent);
        Row("low rotor horn", horn);
        Row("master warning", warn);
        Row("master caution", chime);
        Row("all three", both);

        // --- Silent when nothing is wrong ---------------------------------------------
        // Exactly silent, not nearly. A warning synth that idles at any level at all is a
        // hiss the player hears through the entire game.
        Console.WriteLine();
        Console.WriteLine($"  peak with nothing wrong: {peakSilent:E2}");
        if (peakSilent != 0.0)
            failure ??= $"the warning synth is not silent when nothing is wrong (peak {peakSilent:E2})";

        // --- Audible -------------------------------------------------------------------
        if (Rms(horn) < 0.05) failure ??= $"the horn is inaudible: rms {Rms(horn):F4}";
        if (Rms(warn) < 0.03) failure ??= $"the master warning is inaudible: rms {Rms(warn):F4}";
        if (Rms(chime) < 0.01) failure ??= $"the caution chime is inaudible: rms {Rms(chime):F4}";

        // --- Distinguishable, on pitch -------------------------------------------------
        double hornAt400 = Tone(horn, 400), hornAt950 = Tone(horn, 950);
        double warnAt950 = Tone(warn, 950), warnAt400 = Tone(warn, 400);
        Console.WriteLine($"  horn:    400 Hz is {hornAt400 / Math.Max(hornAt950, 1e-9):F0}x " +
                          $"its energy at 950 Hz");
        Console.WriteLine($"  warning: 950 Hz is {warnAt950 / Math.Max(warnAt400, 1e-9):F0}x " +
                          $"its energy at 400 Hz");
        if (hornAt400 < hornAt950 * 10)
            failure ??= $"the horn is not clearly a 400 Hz tone ({hornAt400:F5} vs {hornAt950:F5})";
        if (warnAt950 < warnAt400 * 10)
            failure ??= $"the master warning is not clearly a 950 Hz tone " +
                        $"({warnAt950:F5} vs {warnAt400:F5})";

        // The chime is the two tones of a perfect fifth and neither of the other voices.
        double chimeAt660 = Tone(chime, 660), chimeAt990 = Tone(chime, 990);
        double chimeAt400 = Tone(chime, 400), chimeAt950 = Tone(chime, 950);
        Console.WriteLine($"  chime:   660 and 990 Hz are {chimeAt660 / Math.Max(chimeAt400, 1e-9):F0}x " +
                          $"and {chimeAt990 / Math.Max(chimeAt950, 1e-9):F0}x the horn's and the " +
                          $"beeper's frequencies");
        if (chimeAt660 < chimeAt400 * 8 || chimeAt990 < chimeAt950 * 8)
            failure ??= "the caution chime overlaps the horn or the warning beeper in pitch";

        // --- Distinguishable, on rhythm ------------------------------------------------
        double dHorn = Duty(horn), dWarn = Duty(warn), dChime = Duty(chime);
        Console.WriteLine($"  duty:    horn {dHorn:P0} continuous, warning {dWarn:P0} " +
                          $"at {WarningSynth.WarningRepHz:F1} Hz, chime {dChime:P0} of three seconds");
        if (dHorn < 0.95) failure ??= $"the horn is not continuous: duty {dHorn:P0}";
        if (dWarn is < 0.30 or > 0.65)
            failure ??= $"the master warning duty is {dWarn:P0}, expected near " +
                        $"{WarningSynth.WarningDuty:P0}";
        if (dChime > 0.35)
            failure ??= $"the caution chime does not get out of the way: duty {dChime:P0}";
        if (dHorn - dWarn < 0.3)
            failure ??= "the horn and the master warning have the same rhythm";

        // --- Well behaved --------------------------------------------------------------
        double peak = 0, dc = 0;
        int bad = 0;
        foreach (float v in both)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) bad++;
            peak = Math.Max(peak, Math.Abs(v));
            dc += v;
        }
        dc /= both.Length;
        Console.WriteLine($"  all three at once: peak {peak:F3}, dc {dc:+0.0000;-0.0000}, " +
                          $"{bad} non-finite samples");
        if (bad > 0) failure ??= $"{bad} non-finite samples";
        if (peak > 1.0) failure ??= $"clipping with every tone up: peak {peak:F3}";
        if (Math.Abs(dc) > 0.02) failure ??= $"DC offset {dc:F4} - it will thump on every start";

        // --- Deterministic ---------------------------------------------------------------
        float[] again = RenderTone(true, true, true, 2.0);
        double maxDiff = 0;
        for (int i = 0; i < both.Length; i++) maxDiff = Math.Max(maxDiff, Math.Abs(both[i] - again[i]));
        Console.WriteLine($"  two identical renders differ by at most {maxDiff:E2}");
        if (maxDiff != 0.0) failure ??= "the warning synth is not deterministic";

        return failure;
    }

    // ------------------------------------------------------- 6. in flight

    /// <summary>
    /// The whole thing, driven by the actual aircraft: a cruise, an engine failure, and a
    /// rotor left to decay.
    ///
    /// Everything above uses hand-built telemetry, which proves the state machine and
    /// proves nothing about whether it is wired to a helicopter. This one flies.
    /// </summary>
    public static string? InFlight()
    {
        string? failure = null;
        const double dt = 1.0 / 120.0;

        var heli = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
        heli.InvalidateMass();
        heli.PlaceInFlight(1200, 48.0);
        heli.UseInternalGroundModel = false;

        var ap = new Autopilot { CollectiveTrim = 0.5 };
        var cws = new CautionWarningSystem();
        var synth = new WarningSynth(Rate);
        var demand = new AutopilotDemand { Altitude = 1200, ForwardSpeed = 48, LateralSpeed = 0, Heading = 0 };

        // --- Thirty seconds of ordinary cruise ----------------------------------------
        for (double t = 0; t < 30; t += dt)
        {
            heli.Input = ap.Update(heli, demand, dt);
            heli.Step(dt);
            cws.Update(heli, dt);
        }

        Console.WriteLine($"  30 s of cruise at {heli.Telemetry.AirspeedTrue * 1.94384:F0} kt, " +
                          $"{heli.Telemetry.TorquePercent:F0}% torque: {cws.Summary()}");
        int cautionsInCruise = 0;
        foreach (WarningItem i in cws.Items)
            if (i.Severity >= WarningSeverity.Caution) cautionsInCruise++;
        if (cautionsInCruise > 0)
            failure ??= $"an ordinary cruise raised {cautionsInCruise} caution(s): {cws.Summary()}";

        // --- Engine failure, then the classic wrong reaction ---------------------------
        //
        // The first version of this test failed the engine and froze the collective,
        // expecting the rotor to unwind. It does the opposite: with the pitch left where
        // powered flight had it, the descent drives the rotor UP, to a measured 114% in
        // six seconds, and the system correctly reported ROTOR RPM HIGH while the test
        // sat waiting for a low-rotor horn that was never coming. That is the flight
        // model being right and the test being wrong - so the script now does what a
        // startled pilot does, which is pull.
        heli.Engine.Fail();
        Controls flown = heli.Input;

        double engineAt = -1, hornAt = -1, overspeedAt = -1, chimes = 0;
        var block = new float[512];
        string firstLine = "", topAtHorn = "";
        WarningId topIdAtHorn = WarningId.PowerLimit;

        Console.WriteLine();
        Console.WriteLine("    t     Nr%   collective   panel");
        for (double t = 0; t < 22 && heli.Telemetry.HeightAgl > 25; t += dt)
        {
            // Six seconds of doing nothing - long enough for the descent to drive the
            // rotor up past its overspeed thresholds - and then the collective comes up,
            // which in an autorotation is how a rotor gets killed.
            if (t >= 6.0) flown.Collective = Math.Min(0.95, flown.Collective + dt * 0.30);
            heli.Input = flown;
            heli.Step(dt);
            cws.Update(heli, dt);

            synth.Follow(cws);
            chimes += cws.Raised.Count;
            synth.Render(block, 8, 8.0 / Rate);

            if (engineAt < 0 && cws[WarningId.EngineOut].Active == WarningSeverity.Warning)
                engineAt = t;
            if (overspeedAt < 0 && cws[WarningId.RotorRpmHigh].Active >= WarningSeverity.Caution)
                overspeedAt = t;
            if (hornAt < 0 && cws.LowRotorHorn)
            {
                hornAt = t;
                topAtHorn = cws.Items.Count > 0 ? cws.Items[0].Text : "(clear)";
                topIdAtHorn = cws.Items.Count > 0 ? cws.Items[0].Id : WarningId.PowerLimit;
            }

            if (Math.Abs(t % 2.0) < dt * 0.5)
            {
                Console.WriteLine($"  {t,5:F1}  {heli.Telemetry.RotorRpmPercent,6:F1}   " +
                                  $"{heli.Actual.Collective,6:F3}      {cws.Summary()}");
                if (firstLine.Length == 0 && cws.Items.Count > 0) firstLine = cws.Items[0].Text;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  ENGINE FAILED at +{engineAt:F2} s, rotor overspeed at " +
                          $"+{overspeedAt:F2} s, low-rotor horn at +{hornAt:F2} s");
        Console.WriteLine($"  top line throughout: \"{firstLine}\"");
        Console.WriteLine($"  when the horn sounded, the top line was \"{topAtHorn}\"");
        Console.WriteLine($"  engine-out raises: {cws[WarningId.EngineOut].RaiseCount} " +
                          $"(a per-frame check would have logged {(int)(22 / dt)})");
        Console.WriteLine($"  chimes for the whole sequence: {chimes:F0}");

        if (engineAt < 0) failure ??= "the engine failed and nothing said so";
        if (engineAt > 1.0) failure ??= $"engine-out warning took {engineAt:F2} s";
        if (overspeedAt < 0)
            failure ??= "the rotor ran away in the autorotation and nothing said so";
        if (hornAt < 0) failure ??= "the rotor decayed and the low-rotor horn never sounded";
        if (hornAt <= engineAt) failure ??= "the horn beat the engine-out warning to it";
        // Priority, in the one moment it matters: with the engine gone, the rotor below
        // 92%, the ground coming up and the disc half stalled, the first line the pilot
        // reads is still the thing that caused all of it.
        if (topIdAtHorn != WarningId.EngineOut)
            failure ??= $"with the horn sounding, the top line was {topIdAtHorn}, " +
                        $"not the engine failure";
        if (cws[WarningId.EngineOut].RaiseCount != 1)
            failure ??= $"one engine failure raised {cws[WarningId.EngineOut].RaiseCount} times";
        if (chimes > 12)
            failure ??= $"{chimes:F0} chimes for one engine failure and its consequences";

        // The panel must be reporting the rotor it actually has, not a rounded guess.
        double reported = cws[WarningId.RotorRpmLow].Value;
        Console.WriteLine($"  panel says {reported:F1}% Nr, telemetry says " +
                          $"{heli.Telemetry.RotorRpmPercent:F1}%");
        if (Math.Abs(reported - heli.Telemetry.RotorRpmPercent) > 3.0)
            failure ??= $"the panel is reporting {reported:F1}% against a real " +
                        $"{heli.Telemetry.RotorRpmPercent:F1}%";

        return failure;
    }
}
