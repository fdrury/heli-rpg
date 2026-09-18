using System.Text;
using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Flies a scripted sortie and renders what it sounds like to a WAV file.
///
/// The point is that the audio can be judged without launching the game, and that the
/// judging can be done by ear. Everything else in this project is validated with numbers;
/// sound is the one thing where the number cannot tell you whether it is right.
///
///     dotnet run --project tools/simlab -c Release -- audio
///     -> builds/audio/sortie.wav
/// </summary>
public static class AudioRender
{
    const int SampleRate = 32000;

    public static string? Render()
    {
        var env = new FlatEnvironment();
        var heli = new Helicopter(Airframe.Workhorse(), env) { Fuel = 500 };
        heli.InvalidateMass();
        heli.PlaceOnGround(running: false);
        heli.UseInternalGroundModel = true;

        var ap = new Autopilot { CollectiveTrim = 0.15 };
        var synth = new RotorSynth(SampleRate);

        double dt = Scenarios.Dt;
        var samples = new List<float>(SampleRate * 130);
        var block = new float[4096];
        double audioDebt = 0;

        // The sortie, chosen so every voice gets exercised and every transition is one a
        // player will actually hear: a start, a lift, a cruise, a hard turn, an engine
        // failure, and an autorotation to the ground.
        var script = new (double until, string what)[]
        {
            (6,   "silence, cold aircraft"),
            (34,  "start: gas generator spools, rotor runs up"),
            (44,  "idle"),
            (58,  "lift to a 40 m hover"),
            (78,  "climb and accelerate to cruise"),
            (96,  "hard loaded turn - listen for the slap"),
            (104, "power off: ENGINE FAILURE"),
            (126, "autorotation"),
        };

        double t = 0;
        int scriptIndex = 0;
        Console.WriteLine("  rendering a 126 second sortie");
        Console.WriteLine($"  {"time",6}  {"Nr %",6} {"N1",5} {"IAS kt",7} {"alt m",7}  event");

        while (t < 126.0)
        {
            if (scriptIndex < script.Length && t >= (scriptIndex == 0 ? 0 : script[scriptIndex - 1].until))
            {
                Console.WriteLine($"  {t,6:F1}  {heli.Telemetry.RotorRpmPercent,6:F0} {heli.Engine.N1,5:F2} " +
                                  $"{heli.Telemetry.AirspeedTrue * 1.94384,7:F0} {heli.State.Altitude,7:F0}  " +
                                  script[scriptIndex].what);
                scriptIndex++;
            }

            // --- Fly the script ---------------------------------------------
            AutopilotDemand demand;
            if (t < 6) { heli.Input = new Controls { Collective = 0, Throttle = 0 }; demand = default; }
            else if (t < 34)
            {
                if (heli.Engine.State == EngineState.Off) heli.Engine.Start();
                heli.Input = new Controls { Collective = 0.02, Throttle = 1.0 };
                demand = default;
            }
            else if (t < 44)
            {
                heli.Input = new Controls { Collective = 0.08, Throttle = 1.0 };
                demand = default;
            }
            else if (t < 58)
            {
                demand = new AutopilotDemand { Altitude = 40, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
                heli.Input = ap.Update(heli, demand, dt);
            }
            else if (t < 78)
            {
                demand = new AutopilotDemand { Altitude = 420, ForwardSpeed = 48, LateralSpeed = 0, Heading = 0 };
                heli.Input = ap.Update(heli, demand, dt);
            }
            else if (t < 96)
            {
                // A hard turn loads the disc up, which is where the slap comes from.
                demand = new AutopilotDemand { Altitude = 420, ForwardSpeed = 48, RollAttitude = 0.55 };
                heli.Input = ap.Update(heli, demand, dt);
            }
            else
            {
                if (heli.Engine.State == EngineState.Running) { heli.Engine.Fail(); ap.RotorRpmLoop.Reset(); }
                demand = new AutopilotDemand { RotorRpmFraction = 1.0, ForwardSpeed = 30, LateralSpeed = 0, Heading = 0 };
                heli.Input = ap.Update(heli, demand, dt);
            }

            heli.Step(dt);
            t += dt;

            // --- Render the audio for this physics step ----------------------
            audioDebt += SampleRate * dt;
            int n = (int)audioDebt;
            if (n <= 0) continue;
            audioDebt -= n;

            var state = SynthState.From(heli);
            if (samples.Count == 0) synth.Prime(state);

            while (n > 0)
            {
                int take = Math.Min(n, block.Length);
                synth.Render(block, take, state, take / (double)SampleRate);
                for (int i = 0; i < take; i++) samples.Add(block[i]);
                n -= take;
            }
        }

        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "builds", "audio");
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "sortie.wav");
        WriteWav(path, samples, SampleRate);

        // Measure what came out, so a silent or clipped render fails rather than shipping.
        double peak = 0, sum = 0;
        int clipped = 0;
        foreach (float v in samples)
        {
            double a = Math.Abs(v);
            peak = Math.Max(peak, a);
            sum += v * v;
            if (a > 0.985) clipped++;
        }
        double rms = Math.Sqrt(sum / Math.Max(samples.Count, 1));

        Console.WriteLine();
        Console.WriteLine($"  wrote {path}");
        Console.WriteLine($"  {samples.Count / (double)SampleRate:F1} s, peak {peak:F3}, rms {rms:F4}, " +
                          $"{clipped * 100.0 / Math.Max(samples.Count, 1):F2} % at the clip point");

        if (samples.Count < SampleRate * 100) return "render is shorter than the sortie";
        if (peak < 0.05) return $"peak amplitude {peak:F3} - the aircraft is inaudible";
        if (rms < 0.005) return $"rms {rms:F4} - almost nothing came out";
        if (clipped > samples.Count * 0.02) return $"{clipped * 100.0 / samples.Count:F1} % clipped - it will sound harsh";
        return null;
    }

    private static void WriteWav(string path, List<float> samples, int sampleRate)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        int dataBytes = samples.Count * 2;      // 16-bit mono
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                 // PCM header size
        w.Write((short)1);           // PCM
        w.Write((short)1);           // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);     // byte rate
        w.Write((short)2);           // block align
        w.Write((short)16);          // bits
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);

        foreach (float v in samples)
            w.Write((short)Math.Clamp(v * 32767f, -32768f, 32767f));
    }
}
