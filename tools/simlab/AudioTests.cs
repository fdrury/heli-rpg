using System;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does the helicopter actually sound like the helicopter it is?
///
/// Audio is the one part of this project nobody can check by looking at a screenshot, and
/// it had no test at all. That is a bad combination: a synthesiser can be silent, clipped,
/// full of NaN, or simply modulating at the wrong rate, and every one of those survives
/// indefinitely if the only way to notice is to put headphones on.
///
/// <see cref="RotorSynth"/> is engine-free, so the waveform can be rendered into an array
/// and measured. The measurement that matters most is the **blade-pass rate**: the slap of
/// a helicopter is its blades passing, at rotor speed times blade count, and if that number
/// is wrong the aircraft sounds like a different machine no matter how good the timbre is.
/// </summary>
public static class AudioTests
{
    private const int Rate = 44100;

    /// <summary>
    /// A hover at a given power setting.
    ///
    /// Torque and blade loading move TOGETHER, because that is the only way they occur.
    /// The first version of this test held loading fixed while varying torque, which is not
    /// a flight condition any aircraft can be in, and it made the synthesiser look deaf to
    /// power when the real answer was that the test was asking an impossible question.
    /// </summary>
    private static SynthState Hover(double omegaFraction = 1.0, double torque = 0.75)
    {
        var af = Airframe.Workhorse();
        return new SynthState
        {
            RotorOmega = af.MainRotor.NominalOmega * omegaFraction,
            NominalOmega = af.MainRotor.NominalOmega,
            MainBlades = af.MainRotor.NumBlades,
            TailBlades = af.TailRotor.NumBlades,
            TailGearRatio = af.TailRotor.GearRatio,
            Collective = Math.Clamp(0.25 + 0.5 * torque, 0, 1),
            TorqueFraction = torque,
            Airspeed = 0,
            N1 = 0.9,
            EngineRunning = true,
            BladeLoading = 0.035 + 0.075 * torque,
            TipMach = 0.62,
            TailRotorHealth = 1.0,
        };
    }

    private static float[] Render(SynthState s, double seconds)
    {
        var synth = new RotorSynth(Rate);
        synth.Prime(s);
        int total = (int)(Rate * seconds);
        var all = new float[total];
        const int block = 512;
        for (int i = 0; i < total; i += block)
        {
            int n = Math.Min(block, total - i);
            synth.Render(all.AsSpan(i, n), n, s, (double)n / Rate);
        }
        return all;
    }

    private static double Rms(float[] x)
    {
        double sum = 0;
        foreach (float v in x) sum += (double)v * v;
        return Math.Sqrt(sum / Math.Max(x.Length, 1));
    }

    /// <summary>
    /// Find the dominant modulation rate of the signal's envelope, in Hz.
    ///
    /// Rectify, smooth, remove the mean, then autocorrelate. The blade slap is amplitude
    /// modulation rather than a tone - at two blades and 324 rpm it sits near 11 Hz, which
    /// is below hearing as a pitch and is felt entirely as a rate - so looking for a
    /// spectral peak at 11 Hz in the raw audio finds nothing. The envelope is where it is.
    /// </summary>
    private static double EnvelopeRateHz(float[] x, double minHz, double maxHz)
    {
        // Rectified envelope with a one-pole smoother at roughly 60 Hz.
        var env = new double[x.Length];
        double y = 0, a = 1.0 - Math.Exp(-2 * Math.PI * 60.0 / Rate);
        for (int i = 0; i < x.Length; i++) { y += a * (Math.Abs(x[i]) - y); env[i] = y; }

        double mean = 0;
        foreach (double v in env) mean += v;
        mean /= env.Length;
        for (int i = 0; i < env.Length; i++) env[i] -= mean;

        int minLag = (int)(Rate / maxHz), maxLag = (int)(Rate / minHz);
        double best = 0; int bestLag = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = 0; i + lag < env.Length; i += 4) sum += env[i] * env[i + lag];
            if (sum > best) { best = sum; bestLag = lag; }
        }
        return bestLag > 0 ? (double)Rate / bestLag : 0;
    }

    public static string? Waveform()
    {
        SynthState s = Hover();
        float[] audio = Render(s, 2.0);

        double peak = 0, dc = 0;
        int bad = 0;
        foreach (float v in audio)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) bad++;
            peak = Math.Max(peak, Math.Abs(v));
            dc += v;
        }
        dc /= audio.Length;
        double rms = Rms(audio);

        Console.WriteLine($"  hover, 2 s: rms {rms:F4}, peak {peak:F3}, dc {dc:+0.0000;-0.0000}, " +
                          $"{bad} non-finite samples");

        if (bad > 0) return $"{bad} non-finite samples in the output";
        if (peak > 1.0) return $"clipping: peak {peak:F3}";
        if (rms < 0.005) return $"effectively silent: rms {rms:F5}";
        if (Math.Abs(dc) > 0.02) return $"large DC offset {dc:F4} - will thump on start and stop";

        return null;
    }

    public static string? BladePass()
    {
        var af = Airframe.Workhorse();
        Console.WriteLine("  blade-pass rate against rotor speed");
        Console.WriteLine("     Nr    expected    measured");

        string? failure = null;
        foreach (double frac in new[] { 1.0, 0.85, 0.7 })
        {
            SynthState s = Hover(frac);
            double expected = s.RotorOmega / (2 * Math.PI) * s.MainBlades;
            float[] audio = Render(s, 3.0);
            double measured = EnvelopeRateHz(audio, 4, 40);

            Console.WriteLine($"   {frac * 100,4:F0}%   {expected,6:F2} Hz   {measured,6:F2} Hz");

            // Within 8%: the envelope detector is coarse and the slap is not a pure tone.
            if (Math.Abs(measured - expected) / expected > 0.08)
                failure ??= $"blade-pass rate at {frac * 100:F0}% Nr is {measured:F2} Hz, " +
                            $"expected {expected:F2} Hz";
        }

        double nominal = af.MainRotor.NominalOmega / (2 * Math.PI) * af.MainRotor.NumBlades;
        Console.WriteLine($"  ({af.MainRotor.NumBlades} blades at " +
                          $"{af.MainRotor.NominalOmega * 60 / (2 * Math.PI):F0} rpm = {nominal:F1} Hz)");
        return failure;
    }

    public static string? RespondsToState()
    {
        double quiet = Rms(Render(Hover(torque: 0.25), 1.0));
        double loud = Rms(Render(Hover(torque: 1.15), 1.0));
        Console.WriteLine($"  rms at 25% torque {quiet:F4}, at 115% torque {loud:F4}");
        if (loud <= quiet * 1.05)
            return $"pulling power changes nothing: {quiet:F4} -> {loud:F4}";

        // Everything stopped must be silent. A synthesiser that hums with the rotor stopped
        // is one the player hears through every conversation and every shutdown.
        SynthState dead = Hover(0.0, 0.0);
        dead.EngineRunning = false;
        dead.N1 = 0;
        double still = Rms(Render(dead, 1.0));
        Console.WriteLine($"  rms shut down {still:F5}");
        if (still > 0.01) return $"audible with the engine stopped and the rotor still: {still:F4}";

        return null;
    }
}
