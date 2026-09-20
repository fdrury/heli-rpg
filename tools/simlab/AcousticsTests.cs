using System;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Does the world have acoustics, and do they make sense?
///
/// <see cref="AcousticSpace"/> computes reverb parameters from the terrain around a
/// listener. These tests verify that the three situations the benchmark identified as
/// missing — a valley, open ground, and over water — each produce the right kind of
/// space, and that <see cref="AmbientSynth"/> produces audible, bounded output for
/// every site kind.
/// </summary>
public static class AcousticsTests
{
    /// <summary>
    /// A listener on flat ground with no terrain nearby should get minimal reverb.
    /// </summary>
    public static string? OpenGround()
    {
        // Flat terrain at 100 m everywhere
        static float Flat(float x, float z) => 100f;

        var p = AcousticSpace.At(0, 0, 100, Flat, 0f);

        Console.WriteLine($"  open ground: enclosure {p.Enclosure:F3}, room {p.RoomSize:F3}, " +
                          $"wet {p.WetLevel:F3}, water {p.WaterProximity:F3}");

        if (p.Enclosure > 0.1)
            return $"open ground should not be enclosed: {p.Enclosure:F3}";
        if (p.WetLevel > 0.15)
            return $"open ground should be nearly dry: wet {p.WetLevel:F3}";
        if (p.WaterProximity > 0.1)
            return $"open ground should not register water: {p.WaterProximity:F3}";

        return null;
    }

    /// <summary>
    /// A listener in a valley should get significant enclosure and reverb.
    /// </summary>
    public static string? Valley()
    {
        // Valley: terrain rises 60 m on either side within 50 m
        static float ValleyTerrain(float x, float z) =>
            50f + Math.Max(0, (Math.Abs(x) - 20f)) * 1.5f + Math.Max(0, (Math.Abs(z) - 20f)) * 1.0f;

        // Listener on the valley floor
        var p = AcousticSpace.At(0, 0, 50, ValleyTerrain, 0f);

        Console.WriteLine($"  valley floor: enclosure {p.Enclosure:F3}, room {p.RoomSize:F3}, " +
                          $"wet {p.WetLevel:F3}");

        if (p.Enclosure < 0.2)
            return $"valley should be enclosed: {p.Enclosure:F3}";
        if (p.WetLevel < 0.1)
            return $"valley should have reverb: wet {p.WetLevel:F3}";

        return null;
    }

    /// <summary>
    /// Over water, the listener should get water-driven reflections.
    /// </summary>
    public static string? OverWater()
    {
        // Terrain is below water level everywhere
        static float Seabed(float x, float z) => -10f;

        var p = AcousticSpace.At(0, 0, 20, Seabed, 0f);

        Console.WriteLine($"  over water: enclosure {p.Enclosure:F3}, water {p.WaterProximity:F3}, " +
                          $"wet {p.WetLevel:F3}, damp {p.Damping:F3}");

        if (p.WaterProximity < 0.5)
            return $"over water should register water: {p.WaterProximity:F3}";
        if (p.WetLevel < 0.1)
            return $"water should produce some reverb: wet {p.WetLevel:F3}";

        return null;
    }

    /// <summary>
    /// High AGL should suppress enclosure even if terrain walls exist below.
    /// </summary>
    public static string? HighAgl()
    {
        // Deep valley, but listener is 300 m above
        static float ValleyTerrain(float x, float z) =>
            50f + Math.Max(0, (Math.Abs(x) - 20f)) * 2.0f;

        // On the floor: should be enclosed
        var low = AcousticSpace.At(0, 0, 50, ValleyTerrain, 0f);
        // 300 m above: should not be enclosed
        var high = AcousticSpace.At(0, 0, 350, ValleyTerrain, 0f);

        Console.WriteLine($"  AGL test: floor enclosure {low.Enclosure:F3}, " +
                          $"300m above {high.Enclosure:F3}");

        if (high.Enclosure >= low.Enclosure * 0.5)
            return $"high AGL should reduce enclosure: floor {low.Enclosure:F3} vs high {high.Enclosure:F3}";

        return null;
    }

    /// <summary>
    /// Every site kind should produce audible, bounded ambient audio.
    /// </summary>
    public static string? SiteAmbience()
    {
        const int Rate = 44100;
        const int Duration = Rate; // 1 second

        string[] kinds = { "FuelCache", "Settlement", "Workshop", "Wreck", "Relay",
                           "Depot", "Airfield", "Farmstead", "Overlook" };

        Console.WriteLine("  site kind          rms       peak      dc");

        for (int k = 0; k < kinds.Length; k++)
        {
            var voice = AmbientSynth.SiteVoice.For(k);
            var synth = new AmbientSynth(Rate, k * 1000 + 42);
            synth.Prime(voice, 8.0, 0);

            var buf = new float[Duration];
            const int block = 512;
            for (int i = 0; i < Duration; i += block)
            {
                int n = Math.Min(block, Duration - i);
                synth.Render(buf.AsSpan(i, n), n, voice, 8.0, 0, (double)n / Rate);
            }

            double rms = 0, peak = 0, dc = 0;
            int bad = 0;
            foreach (float v in buf)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) bad++;
                peak = Math.Max(peak, Math.Abs(v));
                dc += v;
                rms += (double)v * v;
            }
            rms = Math.Sqrt(rms / Duration);
            dc /= Duration;

            Console.WriteLine($"  {kinds[k],-18} {rms:F5}    {peak:F4}    {dc:+0.0000;-0.0000}");

            if (bad > 0) return $"{kinds[k]}: {bad} non-finite samples";
            if (peak > 1.0) return $"{kinds[k]}: clipping (peak {peak:F3})";
            if (Math.Abs(dc) > 0.02) return $"{kinds[k]}: large DC offset {dc:F4}";

            // Overlooks can be silent (that's the point). Everything else should be audible.
            if (k != 8 && rms < 0.001)
                return $"{kinds[k]}: effectively silent (rms {rms:F5})";
        }

        return null;
    }

    /// <summary>
    /// The ambient synth should respond to wind: more wind = different/louder metal sounds.
    /// </summary>
    public static string? WindResponse()
    {
        const int Rate = 44100;
        const int Duration = Rate; // 1 second
        const int Block = 512;

        var voice = AmbientSynth.SiteVoice.For(3); // Wreck: high metal

        double RmsAt(double wind)
        {
            var synth = new AmbientSynth(Rate, 9999);
            synth.Prime(voice, wind, 0);
            var buf = new float[Duration];
            for (int i = 0; i < Duration; i += Block)
            {
                int n = Math.Min(Block, Duration - i);
                synth.Render(buf.AsSpan(i, n), n, voice, wind, 0, (double)n / Rate);
            }
            double sum = 0;
            foreach (float v in buf) sum += (double)v * v;
            return Math.Sqrt(sum / Duration);
        }

        double calm = RmsAt(1.0);
        double windy = RmsAt(15.0);

        Console.WriteLine($"  wreck: calm rms {calm:F5}, windy rms {windy:F5}");

        // Wind should make a wreck louder (more creaking, rattling)
        if (windy <= calm * 1.1)
            return $"wind should affect wreck ambience: calm {calm:F5} vs windy {windy:F5}";

        return null;
    }
}
