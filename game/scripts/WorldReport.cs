using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Headless survey of the generated world.
///
/// Site placement is rejection sampling against the terrain, which means it fails
/// silently: ask for an airfield on ground flatter than anything that exists and you
/// simply get no airfields, with no error. This prints what the terrain actually offers
/// so the placement rules can be written against reality instead of against a guess.
///
/// It also prints the story-places table: every authored role in
/// <see cref="StoryPlaces"/>, the generated site it bound to, and - loudly - any role
/// that failed to bind. A role that does not bind is a beat that cannot fire, and that
/// is a content bug rather than a missing feature, so it belongs in the same report as
/// an airfield that silently never appeared.
///
///     godot --headless --path game -- --worldreport
/// </summary>
public static class WorldReport
{
    public static void Run()
    {
        GD.Print("=== World report ====================================================");
        Terrain();
        GD.Print("");
        Sites();
        GD.Print("");
        Coverage();
        GD.Print("");
        StoryPlaces.Report();
        GD.Print("=====================================================================");
    }

    private static void Terrain()
    {
        const int samples = 220;
        float half = WorldMap.ContentHalfExtent;
        float step = half * 2 / samples;

        var heights = new List<float>(samples * samples);
        var slopes = new List<float>(samples * samples);
        float minH = float.MaxValue, maxH = float.MinValue;

        for (int j = 0; j < samples; j++)
        {
            for (int i = 0; i < samples; i++)
            {
                float x = -half + i * step, z = -half + j * step;
                float h = WorldHeight.At(x, z);
                float slope = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(WorldHeight.NormalAt(x, z, 6f).Y, -1, 1)));
                heights.Add(h);
                slopes.Add(slope);
                minH = Mathf.Min(minH, h);
                maxH = Mathf.Max(maxH, h);
            }
        }
        heights.Sort();
        slopes.Sort();

        float P(List<float> v, float pct) => v[Mathf.Clamp((int)(v.Count * pct), 0, v.Count - 1)];

        GD.Print($"terrain over the {half * 2 / 1000f:F1} km content envelope, {samples}x{samples} samples");
        GD.Print($"  height   min {minH,7:F1}   p10 {P(heights, 0.10f),7:F1}   median {P(heights, 0.5f),7:F1}" +
                 $"   p90 {P(heights, 0.90f),7:F1}   max {maxH,7:F1} m");
        GD.Print($"  slope    p10 {P(slopes, 0.10f),7:F1}   median {P(slopes, 0.5f),7:F1}" +
                 $"   p90 {P(slopes, 0.90f),7:F1}   p99 {P(slopes, 0.99f),7:F1} deg");

        // How much of the world satisfies each placement rule. This is the number that
        // actually decides whether a site type can exist at all.
        void Rule(string name, Func<float, float, bool> ok)
        {
            int n = 0;
            for (int i = 0; i < heights.Count; i++) { }
            int count = 0, total = 0;
            for (int j = 0; j < samples; j += 2)
            {
                for (int i = 0; i < samples; i += 2)
                {
                    float x = -half + i * step, z = -half + j * step;
                    float h = WorldHeight.At(x, z);
                    float sl = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(WorldHeight.NormalAt(x, z, 6f).Y, -1, 1)));
                    total++;
                    if (ok(h, sl)) count++;
                    n++;
                }
            }
            GD.Print($"  {name,-28} {count * 100.0 / total,6:F2} % of the map");
        }

        Rule("relay (h>110, slope<18)", (h, s) => h > 110 && s < 18);
        Rule("overlook (h>130, slope<22)", (h, s) => h > 130 && s < 22);
        Rule("settlement (slope<7, 6<h<150)", (h, s) => s < 7 && h > 6 && h < 150);
        Rule("airfield (slope<3.5, 6<h<120)", (h, s) => s < 3.5f && h > 6 && h < 120);
        Rule("depot/fuel (slope<9, h>4)", (h, s) => s < 9 && h > 4);
        Rule("wreck (slope<26)", (h, s) => s < 26);
    }

    private static void Sites()
    {
        GD.Print(WorldMap.Describe());
        var byRegion = new Dictionary<RegionKind, int>();
        foreach (Site s in WorldMap.Sites) byRegion[s.Region] = byRegion.GetValueOrDefault(s.Region) + 1;

        GD.Print("  region            tier   sites   example");
        foreach (Region r in WorldMap.Regions)
        {
            string example = "-";
            foreach (Site s in WorldMap.Sites)
                if (s.Region == r.Kind) { example = $"{s.Name} ({s.Kind})"; break; }
            GD.Print($"  {r.Name,-18} {r.Tier,3}   {byRegion.GetValueOrDefault(r.Kind),5}   {example}");
        }

        // The census, by region and kind. Placement is rejection sampling and it fails
        // SILENTLY - a region can ask for four settlements, get none, and say nothing.
        // The totals above hide that completely, because the farmsteads make up the
        // count. This table is what the story layer binds against, so it is the table
        // that decides whether a beat has anywhere to happen.
        var kinds = (SiteKind[])Enum.GetValues(typeof(SiteKind));
        GD.Print("");
        GD.Print("  what each region actually got (asked-for counts live in WorldMap.BuildSites)");
        var header = new System.Text.StringBuilder("  region            ");
        foreach (SiteKind k in kinds) header.Append($"{Abbrev(k),5}");
        GD.Print(header.ToString());
        foreach (Region r in WorldMap.Regions)
        {
            var row = new System.Text.StringBuilder($"  {r.Name,-18}");
            foreach (SiteKind k in kinds)
            {
                int n = 0;
                foreach (Site s in WorldMap.Sites) if (s.Region == r.Kind && s.Kind == k) n++;
                row.Append(n == 0 ? "    ." : $"{n,5}");
            }
            GD.Print(row.ToString());
        }
    }

    private static string Abbrev(SiteKind k) => k switch
    {
        SiteKind.FuelCache => "fuel",
        SiteKind.Settlement => "town",
        SiteKind.Workshop => "shop",
        SiteKind.Wreck => "wrek",
        SiteKind.Relay => "mast",
        SiteKind.Depot => "dpot",
        SiteKind.Airfield => "fild",
        SiteKind.Farmstead => "farm",
        _ => "look",
    };

    private static void Coverage()
    {
        // The number that decides whether the world feels inhabited: how far is it,
        // typically, from anywhere to the nearest named place?
        const int samples = 90;
        float half = WorldMap.ContentHalfExtent;
        float step = half * 2 / samples;
        var distances = new List<float>();

        for (int j = 0; j < samples; j++)
        {
            for (int i = 0; i < samples; i++)
            {
                var p = new Vector2(-half + i * step, -half + j * step);
                Site? nearest = WorldMap.Nearest(p);
                if (nearest is not null) distances.Add(p.DistanceTo(nearest.Position));
            }
        }
        distances.Sort();
        float P(float pct) => distances[Mathf.Clamp((int)(distances.Count * pct), 0, distances.Count - 1)];

        // Remoteness: how much of the country is genuinely away from anything. A map with
        // none of this has no journeys in it, only errands.
        int remote2 = 0, remote4 = 0;
        foreach (float d in distances) { if (d > 2000) remote2++; if (d > 4000) remote4++; }

        float area = Mathf.Pow(half * 2 / 1000f, 2);
        GD.Print($"coverage over {area:F0} km2");
        GD.Print($"  site density              {WorldMap.Sites.Count / area:F2} per km2");
        GD.Print($"  distance to nearest site  median {P(0.5f):F0} m   p90 {P(0.9f):F0} m   worst {P(0.999f):F0} m");
        GD.Print($"  at 55 m/s cruise          median {P(0.5f) / 55:F0} s   p90 {P(0.9f) / 55:F0} s");
        GD.Print($"  remote country            {remote2 * 100.0 / distances.Count:F0} % is over 2 km from anywhere, " +
                 $"{remote4 * 100.0 / distances.Count:F0} % is over 4 km");
        GD.Print("  (Skyrim-calibrated target, and CD Projekt Red's stated rule: one every ~40-48 s)");
    }
}
