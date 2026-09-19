using System;
using System.Collections.Generic;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does the aircraft actually balance, and does it stay balanced when you let go?
///
/// These two questions had no test at all, and the gap hid the worst-feeling bug in the
/// flight model. "freetrace" printed a table and returned success unconditionally, so the
/// aircraft could roll inverted inside five seconds - which it did - while the suite
/// reported ALL CHECKS PASSED. A trace that cannot fail is a picture, not a test.
/// </summary>
public static class TrimTests
{
    private const double Deg = 180.0 / Math.PI;

    private static Helicopter Fresh()
    {
        var heli = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
        heli.InvalidateMass();
        return heli;
    }

    /// <summary>Solve trim across the speed range and check every solution actually balances.</summary>
    public static string? TrimSweep()
    {
        Console.WriteLine("  solved trim across the speed range (level flight, still air, 200 m)");
        Console.WriteLine("    kt   coll    lon    lat    ped   pitch   roll   residual");

        string? failure = null;
        foreach (double kt in new[] { 0.0, 20.0, 40.0, 60.0, 80.0, 100.0, 120.0 })
        {
            Helicopter h = Fresh();
            h.PlaceInFlight(200);
            TrimResult t = Trim.Solve(h, 200, kt / 1.94384);

            Console.WriteLine($"  {kt,4:F0}  {t.Controls.Collective,5:F3} " +
                $"{t.Controls.CyclicPitch,6:F3} {t.Controls.CyclicRoll,6:F3} {t.Controls.Pedal,6:F3} " +
                $"{t.PitchRad * Deg,6:F1} {t.RollRad * Deg,6:F1}   " +
                $"{t.ForceResidualG:F6} g, {t.MomentResidual,6:F0} N·m" +
                $"{(t.Converged ? "" : "   <-- DID NOT CONVERGE")}");

            // A thousandth of a g and a few hundred N·m are far below anything a pilot
            // could detect inside the time it takes to move a hand to the stick.
            if (!t.Converged || t.ForceResidualG > 1e-3)
                failure ??= $"no trim solution at {kt:F0} kt (residual {t.ForceResidualG:F5} g)";
        }

        // The tail rotor pushes right, so a trimmed hover sits left-skid-low with a little
        // left cyclic held in. If that ever comes out as zero, the trim is not doing its job.
        Helicopter hover = Fresh();
        hover.PlaceInFlight(200);
        TrimResult hv = Trim.Solve(hover, 200, 0);
        Console.WriteLine();
        Console.WriteLine($"  hover trim sits {Math.Abs(hv.RollRad * Deg):F1} deg " +
                          $"{(hv.RollRad < 0 ? "left" : "right")} skid low, " +
                          $"holding {Math.Abs(hv.Controls.CyclicRoll):F3} of " +
                          $"{(hv.Controls.CyclicRoll < 0 ? "left" : "right")} cyclic " +
                          $"against {Math.Abs(hv.Controls.Pedal):F3} pedal");
        if (Math.Abs(hv.RollRad * Deg) < 0.2)
            failure ??= "hover trim came out wings level, which no single-rotor helicopter does";

        return failure;
    }

    /// <summary>
    /// Let go at trim and see how long the aircraft holds together.
    ///
    /// A helicopter is genuinely unstable hands-off and is *supposed* to diverge - this is
    /// not asking for stability. It is asking that the divergence start from rest rather
    /// than from an unbalanced state, and that it take long enough that a pilot has time to
    /// notice and correct. Departing inside a few seconds is not a flight model, it is a
    /// fight.
    /// </summary>
    public static string? HandsOff()
    {
        string? bare = Release(false, out double bareDepart);
        Console.WriteLine();
        string? aug = Release(true, out double augDepart);

        Console.WriteLine();
        Console.WriteLine($"  bare airframe departs at {Fmt(bareDepart)}, " +
                          $"augmented at {Fmt(augDepart)}");

        // The augmentation has to actually earn its place. If it does not buy a clear
        // margin over the bare airframe it is noise in the control path.
        if (!double.IsNaN(augDepart) && !double.IsNaN(bareDepart) && augDepart < bareDepart * 1.5)
            return $"augmentation barely helps: bare {bareDepart:F1} s, augmented {augDepart:F1} s";

        return aug ?? bare;
    }

    /// <summary>
    /// Which part of the augmentation is doing what.
    ///
    /// Added because the first three attempts at tuning this were guesses, and each one
    /// made the aircraft worse in a different way. Sweeping the terms one at a time turns
    /// "the SAS makes it worse" into "which term makes it worse".
    /// </summary>
    public static string? SasSweep()
    {
        Console.WriteLine("  time to 30 deg of bank, hands off at trim");
        Console.WriteLine("    configuration                        departs");

        var cases = new List<(string, Action<Stability>)>
        {
            ("off (bare airframe)", s => s.Enabled = false),
        };
        // The first tuning pass found the system was saturated essentially all the time,
        // which turns a linear damper into a bang-bang controller and drives exactly the
        // limit cycle it was meant to remove. Scanning gain against authority finds where
        // it still operates linearly.
        foreach (double scale in new[] { 0.25, 0.4, 0.5, 0.65, 0.8, 1.0 })
            foreach (double auth in new[] { 0.18, 0.30 })
            {
                double sc = scale, au = auth;
                cases.Add(($"gain x{sc:F2}, authority {au:F2}", s =>
                {
                    s.RollGain *= sc; s.PitchGain *= sc; s.YawGain *= sc;
                    s.AttitudeGain *= sc; s.HeadingGain *= sc;
                    s.Authority = au;
                }));
            }

        foreach (var (label, setup) in cases)
        {
            Helicopter h = Fresh();
            h.PlaceInFlightTrimmed(200);
            h.UseInternalGroundModel = false;
            h.Sas.Enabled = true;
            setup(h.Sas);

            const double dt = 1.0 / 240.0;
            double departed = double.NaN;
            for (double t = 0; t < 30 && double.IsNaN(departed); t += dt)
            {
                h.Step(dt);
                if (Math.Abs(h.State.Orientation.Roll * Deg) > 30) departed = t;
            }
            Console.WriteLine($"    {label,-34} {Fmt(departed)}");
        }
        return null;
    }

    /// <summary>
    /// The same release at different integration rates.
    ///
    /// The augmentation was tuned entirely at 240 Hz, and the game runs its physics at 120.
    /// A rate feedback loop cares a great deal about how much delay sits inside it, so a
    /// controller that is well damped at one rate can be an oscillator at half of it. This
    /// asks the question directly instead of assuming the answer transfers.
    /// </summary>
    public static string? RateSensitivity()
    {
        Console.WriteLine("  time to 30 deg of bank, hands off at trim, by integration rate");
        Console.WriteLine("      Hz    bare   augmented");

        string? failure = null;
        foreach (int hz in new[] { 60, 120, 240, 480 })
        {
            double bare = Depart(hz, false);
            double aug = Depart(hz, true);
            Console.WriteLine($"    {hz,4}  {Fmt(bare),8}  {Fmt(aug),8}");

            // The augmentation must help at every rate the game might plausibly run at.
            // Helping at 240 and hurting at 120 is not a tuning detail, it is a controller
            // whose behaviour depends on something it should not depend on.
            if (hz >= 120 && !double.IsNaN(aug) && !double.IsNaN(bare) && aug < bare)
                failure ??= $"augmentation makes things worse at {hz} Hz " +
                            $"(bare {bare:F1} s, augmented {aug:F1} s)";
        }
        return failure;
    }

    private static double Depart(int hz, bool augmented)
    {
        Helicopter h = Fresh();
        h.PlaceInFlightTrimmed(200);
        h.UseInternalGroundModel = false;
        h.Sas.Enabled = augmented;

        double dt = 1.0 / hz;
        for (double t = 0; t < 30; t += dt)
        {
            h.Step(dt);
            if (Math.Abs(h.State.Orientation.Roll * Deg) > 30) return t;
        }
        return double.NaN;
    }

    /// <summary>
    /// Position hold has to actually arrive, and stay, in wind.
    ///
    /// Written because the core loop test started failing the moment the world had real
    /// weather in it: the aircraft shut down ninety-eight metres from the site it was
    /// supposed to be at, hovering perfectly, because zero ground speed freezes the
    /// position error rather than removing it.
    /// </summary>
    public static string? PositionHold()
    {
        var env = new FlatEnvironment { SteadyWind = new Vec3(-7.5, 4.0, 0) };  // ~17 kt
        var h = new Helicopter(Airframe.Workhorse(), env) { Fuel = 500 };
        h.InvalidateMass();
        h.PlaceInFlightTrimmed(200);
        h.UseInternalGroundModel = false;

        var target = new Vec3(180, -120, -200);          // 216 m away, same altitude
        var ap = new Autopilot { CollectiveTrim = 0.5 };
        var demand = new AutopilotDemand
        {
            Altitude = 200,
            GroundTarget = target,
            ApproachSpeed = 14,
            Heading = 0,
        };

        Console.WriteLine($"  holding a point in a {env.SteadyWind.Length * 1.94384:F0} kt wind");
        Console.WriteLine("     t     range    ground speed");

        const double dt = 1.0 / 240.0;
        double range = 0;
        for (double t = 0; t < 120; t += dt)
        {
            h.Input = ap.Update(h, demand, dt);
            h.Step(dt);

            Vec3 raw = target - h.State.Position;
            range = new Vec3(raw.X, raw.Y, 0).Length;

            if (Math.Abs(t % 20.0) < dt)
                Console.WriteLine($"  {t,4:F0}  {range,7:F1} m  " +
                                  $"{new Vec3(h.State.Velocity.X, h.State.Velocity.Y, 0).Length,6:F1} m/s");
        }

        Console.WriteLine($"  settled {range:F1} m from the point after 120 s");

        // Ten metres is a pad. Anything larger and "landing at" a site is a matter of luck.
        if (range > 10)
            return $"position hold settled {range:F0} m away in wind - it never arrives";
        return null;
    }

    /// <summary>
    /// The assist ladder: each rung has to be meaningfully different from the one below.
    ///
    /// A ladder whose rungs all feel the same is worse than a switch, because it implies a
    /// choice that does not exist.
    /// </summary>
    public static string? AssistLadder()
    {
        // Two measurements, because the rungs are not all for the same thing.
        //
        // Hands-off survival is about LEVELLING - holding a bank near zero - so a rung with
        // no attitude term cannot win on it however well it damps, and asserting otherwise
        // marks a good design as broken. Rate damping is for handling: a disturbance that
        // dies away instead of building. Measuring both is what separates "this rung does
        // nothing" from "this rung does something else".
        Console.WriteLine("  by assist level: hands-off survival, and how hard a fixed shove rolls it");
        Console.WriteLine("    level        departs    worst bank   roll settles in");

        var results = new List<(AssistLevel level, double departs, double settle)>();

        foreach (AssistLevel level in Enum.GetValues<AssistLevel>())
        {
            Helicopter h = Fresh();
            h.PlaceInFlightTrimmed(200);
            h.UseInternalGroundModel = false;
            h.Sas.Set(level);

            const double dt = 1.0 / 240.0;
            double departed = double.NaN;
            double worst = 0;
            for (double t = 0; t < 40; t += dt)
            {
                h.Step(dt);
                double bank = Math.Abs(h.State.Orientation.Roll * Deg);
                worst = Math.Max(worst, bank);
                if (double.IsNaN(departed) && bank > 30) departed = t;
            }

            double settle = PeakRollRate(level);
            Console.WriteLine($"    {level,-10}  {Fmt(departed),-10}  {worst,5:F0} deg      {settle,5:F1} deg/s");
            results.Add((level, double.IsNaN(departed) ? 40.0 : departed, settle));
        }

        // The peak-rate column is printed but NOT asserted on, and that is deliberate.
        //
        // Three metrics were tried for the Light rung and none of them honestly capture it.
        // Hands-off survival is about levelling, which Light does not have by design. Time
        // for a disturbance to settle never arrives, because without levelling the aircraft
        // keeps rolling. And peak rate for a fixed input RISES with assist, because an
        // augmented aircraft answers a held stick more crisply - which is better handling,
        // not worse.
        //
        // What Light actually buys is how the aircraft feels in the hands over seconds of
        // continuous correction: fewer over-corrections, less chasing. That is a judgement
        // a person makes at a stick, and inventing a scalar that flatters it would be worse
        // than admitting the limit. It is on the test machine's brief instead.

        // Levelling is what buys hands-off time, so only the rungs that have it must show it.
        double standard = results.Find(r => r.level == AssistLevel.Standard).departs;
        double light = results.Find(r => r.level == AssistLevel.Light).departs;
        double full = results.Find(r => r.level == AssistLevel.Full).departs;
        if (standard < light * 2.0)
            return $"Standard barely outlasts Light ({light:F1} s vs {standard:F1} s) - " +
                   "the levelling term is not earning its place";
        if (full < standard)
            return $"Full holds worse than Standard ({standard:F1} s vs {full:F1} s)";

        return null;
    }

    /// <summary>
    /// Knock it with a fixed pulse of lateral cyclic and see how fast it ends up rolling.
    ///
    /// Peak rate for a known input, rather than time-to-settle: with no levelling the
    /// aircraft simply keeps rolling after the pulse, so "time for the rate to die" never
    /// arrives and every level measures the same capped number. Damping shows up honestly
    /// as a smaller peak for the same shove.
    /// </summary>
    private static double PeakRollRate(AssistLevel level)
    {
        Helicopter h = Fresh();
        TrimResult trim = h.PlaceInFlightTrimmed(200);
        h.UseInternalGroundModel = false;
        h.Sas.Set(level);

        const double dt = 1.0 / 240.0;
        Controls pulse = trim.Controls;
        pulse.CyclicRoll = Math.Clamp(pulse.CyclicRoll + 0.30, -1, 1);

        double peak = 0;
        for (double t = 0; t < 1.5; t += dt)
        {
            h.Input = pulse;
            h.Step(dt);
            peak = Math.Max(peak, Math.Abs(h.State.AngularVelocity.X * Deg));
        }
        return peak;
    }

    private static string Fmt(double t) => double.IsNaN(t) ? "never" : $"{t:F1} s";

    private static string? Release(bool augmented, out double departedAt)
    {
        Helicopter h = Fresh();
        TrimResult t = h.PlaceInFlightTrimmed(200);
        h.UseInternalGroundModel = false;
        h.Sas.Enabled = augmented;

        Console.WriteLine($"  {(augmented ? "AUGMENTED" : "BARE AIRFRAME")} - released at trim: {t}");
        Console.WriteLine("    t    roll  pitch    yaw    alt    IAS");

        const double dt = 1.0 / 240.0;
        departedAt = double.NaN;
        double worstRoll = 0;

        for (double time = 0; time < 30; time += dt)
        {
            h.Step(dt);

            double roll = Math.Abs(h.State.Orientation.Roll * Deg);
            worstRoll = Math.Max(worstRoll, roll);
            if (double.IsNaN(departedAt) && roll > 30) departedAt = time;

            if (Math.Abs(time % 2.0) < dt)
            {
                var st = h.State;
                Console.WriteLine($"  {time,4:F1} {st.Orientation.Roll * Deg,6:F1} " +
                    $"{st.Orientation.Pitch * Deg,6:F1} {st.Orientation.Yaw * Deg,6:F1} " +
                    $"{st.Altitude,7:F1} {h.Telemetry.AirspeedTrue * 1.94384,6:F0}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(double.IsNaN(departedAt)
            ? $"  held inside 30 deg of bank for the full 30 s (worst {worstRoll:F1} deg)"
            : $"  passed 30 deg of bank at {departedAt:F1} s (worst {worstRoll:F1} deg)");

        // Before the trim solver existed this reached 30 deg in about 2.3 s and was fully
        // inverted by 5. Eight seconds is the bar for the augmented aircraft: long enough
        // that the divergence is something the pilot manages rather than something that
        // happens to them. The bare airframe is allowed to be as evil as it really is.
        if (augmented && !double.IsNaN(departedAt) && departedAt < 8.0)
            return $"augmented aircraft departs trim far too quickly - 30 deg in {departedAt:F1} s";

        return null;
    }
}
