using System;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does the announcer sound like a man talking on the radio?
///
/// <see cref="VoiceSynth"/> is engine-free, so the waveform can be rendered and measured
/// exactly the way <see cref="AudioTests"/> checks the rotor: RMS, peak, DC, non-finite
/// samples, and the difference between speaking and not speaking.
/// </summary>
public static class VoiceTests
{
    private const int Rate = 44100;
    private const int Block = 512;

    private static float[] Render(VoiceSynth v, double seconds)
    {
        int total = (int)(Rate * seconds);
        var buf = new float[total];
        for (int i = 0; i < total; i += Block)
        {
            int n = Math.Min(Block, total - i);
            v.Render(buf.AsSpan(i, n), n);
        }
        return buf;
    }

    private static double Rms(float[] x)
    {
        double sum = 0;
        foreach (float v in x) sum += (double)v * v;
        return Math.Sqrt(sum / Math.Max(x.Length, 1));
    }

    /// <summary>Speaking voice: non-silent, not clipped, no NaN, low DC.</summary>
    public static string? Waveform()
    {
        var v = new VoiceSynth(Rate, 99);
        v.OnAir = true;
        v.Speak("This is the Upland Service with Hollis Kerr bringing you the afternoon report.",
                RadioDj.ReadSeconds("This is the Upland Service with Hollis Kerr bringing you the afternoon report."));

        float[] audio = Render(v, 3.0);

        double peak = 0, dc = 0;
        int bad = 0;
        foreach (float s in audio)
        {
            if (float.IsNaN(s) || float.IsInfinity(s)) bad++;
            peak = Math.Max(peak, Math.Abs(s));
            dc += s;
        }
        dc /= audio.Length;
        double rms = Rms(audio);

        Console.WriteLine($"  speaking, 3 s: rms {rms:F4}, peak {peak:F3}, dc {dc:+0.0000;-0.0000}, " +
                          $"{bad} non-finite samples");

        if (bad > 0) return $"{bad} non-finite samples in voice output";
        if (peak > 1.0) return $"clipping: peak {peak:F3}";
        if (rms < 0.005) return $"effectively silent while speaking: rms {rms:F5}";
        if (Math.Abs(dc) > 0.02) return $"large DC offset {dc:F4}";

        return null;
    }

    /// <summary>Off-air must be silent; on-air but not speaking must have carrier hiss.</summary>
    public static string? CarrierHiss()
    {
        // Off-air: completely silent
        var off = new VoiceSynth(Rate, 99);
        off.OnAir = false;
        float[] offAudio = Render(off, 1.0);
        double offRms = Rms(offAudio);
        Console.WriteLine($"  off-air rms {offRms:F5}");
        if (offRms > 0.001) return $"audible when off air: rms {offRms:F4}";

        // On-air but not speaking: faint carrier hiss
        var on = new VoiceSynth(Rate, 99);
        on.OnAir = true;
        float[] onAudio = Render(on, 1.0);
        double onRms = Rms(onAudio);
        Console.WriteLine($"  on-air hiss rms {onRms:F5}");
        if (onRms < 0.001) return $"no carrier hiss when on air: rms {onRms:F5}";
        if (onRms > 0.05) return $"carrier hiss too loud: rms {onRms:F4}";

        return null;
    }

    /// <summary>Speaking must be louder than carrier hiss.</summary>
    public static string? VoiceAboveHiss()
    {
        var v = new VoiceSynth(Rate, 77);
        v.OnAir = true;

        // Hiss only
        float[] hiss = Render(v, 1.0);
        double hissRms = Rms(hiss);

        // Now speak
        v.Speak("The weather remains clear with light winds from the south-west.",
                RadioDj.ReadSeconds("The weather remains clear with light winds from the south-west."));
        float[] voice = Render(v, 2.0);
        double voiceRms = Rms(voice);

        Console.WriteLine($"  hiss rms {hissRms:F4}, voice rms {voiceRms:F4}, " +
                          $"ratio {voiceRms / Math.Max(hissRms, 1e-6):F1}x");

        if (voiceRms <= hissRms * 3.0)
            return $"voice not clearly above hiss: {hissRms:F4} -> {voiceRms:F4}";

        return null;
    }

    /// <summary>Voice stops after the stated duration.</summary>
    public static string? Duration()
    {
        var v = new VoiceSynth(Rate, 55);
        v.OnAir = true;

        double dur = 2.5;
        v.Speak("Testing one two three four five.", dur);

        // Render exactly the duration
        float[] during = Render(v, dur);
        double duringRms = Rms(during);

        // The next half second should be hiss only
        float[] after = Render(v, 0.5);
        double afterRms = Rms(after);

        Console.WriteLine($"  during rms {duringRms:F4}, after rms {afterRms:F4}");

        if (v.Speaking) return "still speaking after stated duration elapsed";
        if (duringRms < 0.01) return $"silent during segment: rms {duringRms:F4}";
        if (afterRms > duringRms * 0.3)
            return $"voice did not stop: during {duringRms:F4}, after {afterRms:F4}";

        return null;
    }
}
