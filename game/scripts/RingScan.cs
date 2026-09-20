using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rotorwash;

/// <summary>
/// Which region belongs on which part of the ring, decided by the ground rather than by eye.
///
/// <para><b>The problem.</b> The layout says where a region IS; the continental noise field
/// decides what the ground is like once it gets there; and the two know nothing about each
/// other. Every region also has hard terrain requirements baked into
/// <c>WorldMap.TryPlace</c> - a relay needs ground above 110 m, an airfield needs slope under
/// 4.5 degrees, a settlement needs under 7 - so a region dropped on unsuitable noise silently
/// fails to place half its sites, and the failure looks like a placement bug rather than a
/// layout one.</para>
///
/// <para><b>Why this exists rather than a person picking angles.</b> Rotating the ring moves
/// all eight regions at once, so fixing one breaks another: at 0 degrees Cold Shoulder (the
/// Upland region, which needs a hill for its mast) topped out at 109 m against a 110 m
/// threshold; rotated to clear that, Fenmoor lost its relay and Long Acre its airfield. Three
/// rounds of that is enough to conclude that guessing is the wrong tool. This samples every
/// slot, scores every region against its OWN plan, and brute-forces the assignment.</para>
/// </summary>
public static class RingScan
{
    private const float Rho = 10500f;

    /// <summary>What one patch of ring can support, measured against the real placement rules.</summary>
    private readonly record struct Ground(
        float FlatFrac,      // slope < 7, 6 < h < 320   - settlements, farmsteads
        float RunwayFrac,    // slope < 4.5, 20 < h < 300 - airfields
        float ShopFrac,      // slope < 11, 6 < h < 340   - workshops
        float YardFrac,      // slope < 9, h > 4          - depots, fuel caches
        float HighFrac,      // h > 110, slope < 18       - relays, in the wider sample
        float LookFrac,      // slope < 12                - overlooks, in the wider sample
        float Wet);

    private static Ground Measure(float cx, float cz, float regionRadius)
    {
        int flat = 0, runway = 0, shop = 0, yard = 0, wet = 0, n = 0;
        int high = 0, look = 0, wide = 0;

        // Two radii, because the rules use two. Clustered kinds sit inside the region;
        // relays and overlooks are sampled out to 1.7x by TryPlace, which is how a wetland
        // whose middle is under water still finds a mast site on its rim.
        const int grid = 30;
        float wideR = regionRadius * 1.7f;
        for (int j = 0; j < grid; j++)
            for (int i = 0; i < grid; i++)
            {
                float px = cx + (i / (float)(grid - 1) - 0.5f) * 2 * wideR;
                float pz = cz + (j / (float)(grid - 1) - 0.5f) * 2 * wideR;
                float r = new Vector2(px - cx, pz - cz).Length();
                if (r > wideR) continue;

                float h = WorldHeight.RawAt(px, pz);
                Vector3 nm = WorldHeight.NormalAt(px, pz);
                float slope = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(nm.Y, -1f, 1f)));

                wide++;
                if (h > 110f && slope < 18f) high++;
                if (slope < 12f && h > 6f) look++;

                if (r > regionRadius) continue;
                n++;
                if (h < WorldHeight.WaterLevel) wet++;
                if (slope < 7f && h > 6f && h < 320f) flat++;
                if (slope < 4.5f && h > 20f && h < 300f) runway++;
                if (slope < 11f && h > 6f && h < 340f) shop++;
                if (slope < 9f && h > 4f) yard++;
            }

        if (n == 0 || wide == 0) return new Ground(0, 0, 0, 0, 0, 0, 1);
        return new Ground(flat / (float)n, runway / (float)n, shop / (float)n, yard / (float)n,
                          high / (float)wide, look / (float)wide, wet / (float)n);
    }

    /// <summary>What a region's site plan actually asks the ground for.</summary>
    private readonly record struct Need(string Name, RegionKind Kind, float Radius,
                                        int Flat, int Runway, int Shop, int Yard, int Relay, int Look);

    /// <summary>
    /// How well a patch serves a region, as the TIGHTEST of its requirements.
    ///
    /// The minimum rather than the sum on purpose: a region that can place everything except
    /// its relay is not 85% fine, it is a region with a missing relay and, if it is Cold
    /// Shoulder, a story beat with nowhere to happen. Scoring by the weakest requirement is
    /// what stops the solver trading a mast away for six extra farm sites.
    /// </summary>
    private static float Score(in Need need, in Ground g)
    {
        // Roughly how much of the disc one site of each kind consumes, from the spacing
        // rules in RequiredGap. Small numbers: these are sparse worlds.
        float Ratio(int wanted, float have, float per) =>
            wanted == 0 ? 1f : Mathf.Min(1f, have / (wanted * per));

        return Mathf.Min(Mathf.Min(Mathf.Min(Ratio(need.Flat, g.FlatFrac, 0.010f),
                                             Ratio(need.Runway, g.RunwayFrac, 0.020f)),
                                   Mathf.Min(Ratio(need.Shop, g.ShopFrac, 0.006f),
                                             Ratio(need.Yard, g.YardFrac, 0.006f))),
                         Mathf.Min(Ratio(need.Relay, g.HighFrac, 0.015f),
                                   Ratio(need.Look, g.LookFrac, 0.015f)));
    }

    public static void Run()
    {
        // Counts straight out of WorldMap.BuildSites' plan, collapsed onto the rules.
        var needs = new List<Need>
        {
            new("The Pan",        RegionKind.Basin,      1500, 3 + 2, 1, 2, 3 + 0, 1, 1),
            new("Fenmoor",        RegionKind.Exurb,      1400, 4 + 2, 1, 2, 2 + 2, 1, 1),
            new("The Drowning",   RegionKind.Wetland,    1400, 2 + 2, 0, 1, 2 + 1, 1, 1),
            new("Cold Shoulder",  RegionKind.Upland,     1700, 2 + 2, 0, 1, 2 + 1, 1, 1),
            new("Sawtooth Works", RegionKind.Industrial, 1300, 2 + 1, 1, 3, 2 + 3, 1, 0),
            new("Ashmount",       RegionKind.City,       1800, 4 + 1, 1, 3, 2 + 3, 1, 1),
        };
        // Long Acre rides with The Pan on the home island, so it is not a ring slot of its
        // own - it is scored against the same patch, offset radially outward.
        var longAcre = new Need("Long Acre", RegionKind.Farmland, 1600, 4 + 4, 1, 2, 2 + 0, 1, 1);

        GD.Print("=== ring scan ==========================================================");
        GD.Print($"  six slots 60 deg apart at {Rho / 1000f:F1} km, scored against each region's own plan");
        GD.Print("  score is the TIGHTEST requirement: 1.0 means every site kind has room,");
        GD.Print("  below 1.0 means that many of something will not place.");
        GD.Print("");

        int bestRot = -1;
        int[]? bestPerm = null;
        float bestScore = float.NegativeInfinity;

        var perms = Permutations(Enumerable.Range(0, 6).ToArray()).ToList();

        for (int rot = 0; rot < 60; rot += 5)
        {
            // Slot angles, in the order BuildRegions lists them round the ring.
            int[] slotDeg = { 180, 240, 120, 300, 60, 0 };

            // Precomputed: slot x region, because every region samples a different radius
            // and the permutation loop below runs 720 times. Measuring inside it instead
            // was 200 million height evaluations and did not finish.
            var fit = new float[6, 6];
            var homeFit = new float[6];
            for (int s = 0; s < 6; s++)
            {
                float a = Mathf.DegToRad(slotDeg[s] + rot);
                float cx = Mathf.Cos(a) * Rho, cz = Mathf.Sin(a) * Rho;
                for (int r = 0; r < needs.Count; r++)
                    fit[s, r] = Score(needs[r], Measure(cx, cz, needs[r].Radius));

                float k = (Rho + 3850f) / Rho;
                homeFit[s] = Score(longAcre, Measure(cx * k, cz * k, longAcre.Radius));
            }

            foreach (int[] perm in perms)
            {
                float worst = float.MaxValue;
                for (int s = 0; s < 6; s++)
                {
                    worst = Mathf.Min(worst, fit[s, perm[s]]);
                    // Long Acre shares whichever slot The Pan took.
                    if (needs[perm[s]].Name == "The Pan") worst = Mathf.Min(worst, homeFit[s]);
                }
                if (worst > bestScore) { bestScore = worst; bestRot = rot; bestPerm = (int[])perm.Clone(); }
            }
            GD.Print($"    rot {rot,3}: best assignment scores {bestScore:F2}");
        }

        GD.Print("");
        if (bestPerm is null) { GD.Print("  no assignment found"); return; }

        int[] deg = { 180, 240, 120, 300, 60, 0 };
        GD.Print($"  BEST: rotate {bestRot} deg, tightest requirement satisfied {bestScore:F2}");
        GD.Print("    slot   angle          x          z   region");
        for (int s = 0; s < 6; s++)
        {
            float a = Mathf.DegToRad(deg[s] + bestRot);
            GD.Print($"    {s,4}   {(deg[s] + bestRot) % 360,5}   {Mathf.Cos(a) * Rho,8:F0}   " +
                     $"{Mathf.Sin(a) * Rho,8:F0}   {needs[bestPerm[s]].Name}");
        }
        float kk = (Rho + 3850f) / Rho;
        int home = Array.FindIndex(bestPerm, i => needs[i].Name == "The Pan");
        float ha = Mathf.DegToRad(deg[home] + bestRot);
        GD.Print($"    home            {Mathf.Cos(ha) * Rho * kk,8:F0}   " +
                 $"{Mathf.Sin(ha) * Rho * kk,8:F0}   Long Acre (outward of The Pan)");
        GD.Print("========================================================================");
    }

    private static IEnumerable<int[]> Permutations(int[] a)
    {
        if (a.Length <= 1) { yield return a; yield break; }
        for (int i = 0; i < a.Length; i++)
        {
            int[] rest = a.Where((_, j) => j != i).ToArray();
            foreach (int[] p in Permutations(rest))
                yield return new[] { a[i] }.Concat(p).ToArray();
        }
    }
}
