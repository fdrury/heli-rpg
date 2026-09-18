using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Measures how much cover the real terrain actually gives.
///
/// The unit tests in simlab prove the threat model behaves correctly given a line-of-sight
/// answer. They cannot prove the world supplies interesting answers - a map of billiard
/// table flatness would pass every one of them and be no fun at all. This walks the real
/// height field and reports what fraction of the ground each emitter can actually see, at
/// each altitude band.
///
///     godot --headless --path game -- --threatreport
/// </summary>
public static class ThreatReport
{
    public static void Run(ThreatWorld world)
    {
        GD.Print("=== Threat coverage over the real terrain =========================");
        GD.Print($"  {world.Field.Tracks.Count} emitters placed");

        var byKind = new Dictionary<ThreatKind, int>();
        foreach (ThreatTrack t in world.Field.Tracks)
            byKind[t.Emitter.Kind] = byKind.GetValueOrDefault(t.Emitter.Kind) + 1;
        foreach (var kv in byKind) GD.Print($"    {kv.Value,3} x {kv.Key}");

        GD.Print("");
        GD.Print("  Fraction of ground inside each emitter's reach that it can actually SEE.");
        GD.Print("  Low numbers mean the terrain is doing its job: there is somewhere to hide.");
        GD.Print("");
        GD.Print($"  {"emitter",-34} {"reach",6}   {"at 50 m",8} {"150 m",8} {"400 m",8} {"900 m",8}");

        double[] altitudes = { 50, 150, 400, 900 };
        var totals = new double[altitudes.Length];
        int counted = 0;

        foreach (ThreatTrack t in world.Field.Tracks)
        {
            ThreatEmitter e = t.Emitter;
            // Only report a representative sample; every emitter would be pages of output.
            if (counted >= 10 && e.Kind != ThreatKind.Aerostat) { counted++; continue; }

            float ex = (float)e.East, ez = (float)-e.North;
            float ey = WorldHeight.At(ex, ez) + (e.Kind == ThreatKind.Aerostat ? 620f : 6f);

            var line = $"  {Trim(e.Name, 34),-34} {e.DetectionRange / 1000,5:F0}km ";

            for (int ai = 0; ai < altitudes.Length; ai++)
            {
                double alt = altitudes[ai];
                int seen = 0, total = 0;

                // Sample a ring of rays out to the detection range.
                for (int bearing = 0; bearing < 36; bearing++)
                {
                    float b = Mathf.Tau * bearing / 36f;
                    for (float range = 400; range < e.DetectionRange; range += (float)e.DetectionRange / 18f)
                    {
                        float tx = ex + Mathf.Cos(b) * range;
                        float tz = ez + Mathf.Sin(b) * range;
                        if (!WorldHeight.InBounds(tx, tz)) continue;
                        float ty = WorldHeight.At(tx, tz) + (float)alt;
                        total++;
                        if (Clear(ex, ey, ez, tx, ty, tz)) seen++;
                    }
                }
                double frac = total > 0 ? seen / (double)total : 0;
                totals[ai] += frac;
                line += $"{frac * 100,7:F0} %";
            }
            GD.Print(line);
            counted++;
        }

        GD.Print("");
        int n = Math.Min(counted, 10) + byKind.GetValueOrDefault(ThreatKind.Aerostat);
        GD.Print($"  {"AVERAGE",-34} {"",6}   " +
                 string.Join(" ", Array.ConvertAll(totals, v => $"{v / Math.Max(n, 1) * 100,7:F0} %")));
        GD.Print("");
        GD.Print("  A number near 100 % at 50 m would mean the terrain gives no cover at all,");
        GD.Print("  and the whole masking mechanic would be decoration.");
        GD.Print("===================================================================");
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + ".";

    /// <summary>Walk the ray and see whether the ground gets in the way.</summary>
    private static bool Clear(float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
        int steps = Mathf.Clamp(Mathf.CeilToInt(horizontal / 60f), 4, 180);
        for (int i = 1; i < steps; i++)
        {
            float f = i / (float)steps;
            if (WorldHeight.At(ax + dx * f, az + dz * f) > Mathf.Lerp(ay, by, f) + 2f) return false;
        }
        return true;
    }
}
