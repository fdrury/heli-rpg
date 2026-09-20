using System;
using System.Collections.Generic;
using System.Linq;
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
        Water();
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
            // Counted against the LAND, not against the map. Roughly a third of the
            // content envelope is sea now that the world is an island, and measuring a
            // placement rule against a denominator that includes the sea says only that
            // the sea is not flat enough to build on, which nobody needed to be told.
            int count = 0, dry = 0, total = 0;
            for (int j = 0; j < samples; j += 2)
            {
                for (int i = 0; i < samples; i += 2)
                {
                    float x = -half + i * step, z = -half + j * step;
                    float h = WorldHeight.At(x, z);
                    float sl = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(WorldHeight.NormalAt(x, z, 6f).Y, -1, 1)));
                    total++;
                    if (h < WorldHeight.WaterLevel) continue;
                    dry++;
                    if (ok(h, sl)) count++;
                }
            }
            GD.Print($"  {name,-28} {count * 100.0 / Mathf.Max(dry, 1),6:F2} % of the land" +
                     $"   ({count * 100.0 / total,6:F2} % of the envelope)");
        }

        Rule("relay (h>110, slope<18)", (h, s) => h > 110 && s < 18);
        Rule("overlook (h>130, slope<22)", (h, s) => h > 130 && s < 22);
        Rule("settlement (slope<7, 6<h<150)", (h, s) => s < 7 && h > 6 && h < 150);
        Rule("airfield (slope<3.5, 6<h<120)", (h, s) => s < 3.5f && h > 6 && h < 120);
        Rule("depot/fuel (slope<9, h>4)", (h, s) => s < 9 && h > 4);
        Rule("wreck (slope<26)", (h, s) => s < 26);
    }

    /// <summary>
    /// Where the water is.
    ///
    /// The whole world drains to ONE level (<see cref="WorldHeight.WaterLevel"/>), because
    /// the streamer draws water as a flat quad at exactly that height. So "is this wet?"
    /// is simply "is the ground below it?", which makes the question measurable - and that
    /// is the only reason this section can exist. The generator before it had a region
    /// called The Drowning whose lowest ground stood 112 m ABOVE its own waterline, and
    /// nothing in the report said so.
    ///
    /// Four numbers matter:
    ///   * how much of the map is under water - too little and there is nothing to see
    ///     from the air, too much and the world is a swamp;
    ///   * how far you typically are from water - a river is only a navigation aid if you
    ///     can find one;
    ///   * the lowest ground in each region, which is what decides whether a region that
    ///     is supposed to be drowned actually is;
    ///   * where the wetland's wrecks came down, because one of them has to be the ferry.
    /// </summary>
    private static void Water()
    {
        const int samples = 512;
        float half = WorldMap.ContentHalfExtent;
        float step = half * 2 / samples;

        var wet = new bool[samples * samples];
        int wetCount = 0, shoreCount = 0;
        float deepest = float.MaxValue;

        for (int j = 0; j < samples; j++)
        {
            for (int i = 0; i < samples; i++)
            {
                float x = -half + i * step, z = -half + j * step;
                float h = WorldHeight.At(x, z);
                bool w = h < WorldHeight.WaterLevel;
                wet[j * samples + i] = w;
                if (w) { wetCount++; deepest = Mathf.Min(deepest, h); }
                else if (h < WorldHeight.WaterLevel + 2f) shoreCount++;
            }
        }

        // Distance to the nearest water, by two-pass chamfer over the sample grid. The
        // grid is 25 m, far finer than anything a pilot navigates by, so the answer is
        // good to about half a cell and costs one pass instead of a search per point.
        var dist = new float[samples * samples];
        const float big = 1e9f;
        for (int k = 0; k < dist.Length; k++) dist[k] = wet[k] ? 0f : big;
        const float d1 = 1f, d2 = 1.41421356f;
        for (int j = 0; j < samples; j++)
            for (int i = 0; i < samples; i++)
            {
                int k = j * samples + i;
                if (j > 0)
                {
                    if (i > 0) dist[k] = Mathf.Min(dist[k], dist[k - samples - 1] + d2);
                    dist[k] = Mathf.Min(dist[k], dist[k - samples] + d1);
                    if (i < samples - 1) dist[k] = Mathf.Min(dist[k], dist[k - samples + 1] + d2);
                }
                if (i > 0) dist[k] = Mathf.Min(dist[k], dist[k - 1] + d1);
            }
        for (int j = samples - 1; j >= 0; j--)
            for (int i = samples - 1; i >= 0; i--)
            {
                int k = j * samples + i;
                if (j < samples - 1)
                {
                    if (i < samples - 1) dist[k] = Mathf.Min(dist[k], dist[k + samples + 1] + d2);
                    dist[k] = Mathf.Min(dist[k], dist[k + samples] + d1);
                    if (i > 0) dist[k] = Mathf.Min(dist[k], dist[k + samples - 1] + d2);
                }
                if (i < samples - 1) dist[k] = Mathf.Min(dist[k], dist[k + 1] + d1);
            }

        var dm = new List<float>(dist.Length);
        int near500 = 0, near1000 = 0;
        foreach (float d in dist)
        {
            float m = Mathf.Min(d, samples) * step;
            dm.Add(m);
            if (m < 500f) near500++;
            if (m < 1000f) near1000++;
        }
        dm.Sort();
        float Q(float pct) => dm[Mathf.Clamp((int)(dm.Count * pct), 0, dm.Count - 1)];

        // How far apart the drainage lines are. Every width in WorldHeight's drainage
        // block is only meaningful against this: a 460 m terrace is a river terrace if the
        // rivers are 3 km apart and is the entire landscape if they are 900 m apart.
        var drain = new List<float>();
        for (int j = 0; j < samples; j += 2)
            for (int i = 0; i < samples; i += 2)
                drain.Add(Mathf.Min(WorldHeight.DrainageDistance(-half + i * step, -half + j * step), 9999f));
        drain.Sort();
        float D(float pct) => drain[Mathf.Clamp((int)(drain.Count * pct), 0, drain.Count - 1)];
        int Under(float m) { int n = 0; foreach (float v in drain) if (v < m) n++; return n; }

        int total = samples * samples;
        GD.Print($"water: one level for the whole world at {WorldHeight.WaterLevel:F1} m, " +
                 $"{samples}x{samples} samples ({step:F0} m grid)");
        GD.Print($"  under water               {wetCount * 100.0 / total,6:F2} % of the envelope" +
                 (wetCount > 0 ? $"   deepest bed {deepest,7:F1} m" : "   *** NO WATER AT ALL ***"));
        GD.Print($"  shoreline (0-2 m above)   {shoreCount * 100.0 / total,6:F2} %");
        GD.Print($"  distance to water         median {Q(0.5f):F0} m   p90 {Q(0.9f):F0} m   worst {Q(0.999f):F0} m");
        GD.Print($"  within sight of water     {near500 * 100.0 / total:F0} % is under 500 m from it, " +
                 $"{near1000 * 100.0 / total:F0} % under 1 km");
        GD.Print($"  drainage line spacing     median {D(0.5f):F0} m   p90 {D(0.9f):F0} m   " +
                 $"(share of map within 60 m {Under(60f) * 100.0 / drain.Count:F1} %, " +
                 $"200 m {Under(200f) * 100.0 / drain.Count:F1} %, " +
                 $"460 m {Under(460f) * 100.0 / drain.Count:F1} %)");

        // Per region. The lowest ground is the number the story layer cares about: one
        // region being drowned is not the same thing as the whole map being a swamp, and
        // this table is where the difference shows.
        GD.Print("");
        GD.Print("  region             kind          lowest     p10   median  highest   wet     raw low");
        foreach (Region r in WorldMap.Regions)
        {
            const int rs = 96;
            float rstep = r.Radius * 2 / rs;
            var hs = new List<float>();
            int rw = 0, rn = 0;
            float lowRaw = float.MaxValue;
            for (int j = 0; j <= rs; j++)
                for (int i = 0; i <= rs; i++)
                {
                    float x = r.Centre.X - r.Radius + i * rstep;
                    float z = r.Centre.Y - r.Radius + j * rstep;
                    if (new Vector2(x, z).DistanceTo(r.Centre) > r.Radius) continue;
                    float h = WorldHeight.At(x, z);
                    hs.Add(h);
                    lowRaw = Mathf.Min(lowRaw, WorldHeight.RawAt(x, z));
                    rn++;
                    if (h < WorldHeight.WaterLevel) rw++;
                }
            hs.Sort();
            float R(float pct) => hs[Mathf.Clamp((int)(hs.Count * pct), 0, hs.Count - 1)];
            GD.Print($"  {r.Name,-18} {r.Kind,-12} {hs[0],7:F1} {R(0.1f),7:F1} {R(0.5f),8:F1} {hs[^1],8:F1}" +
                     $" {rw * 100.0 / Mathf.Max(rn, 1),6:F2} % {lowRaw,9:F1}");
        }

        // How much LAND each region actually needs.
        //
        // This is the table the archipelago is authored against, and it is not obvious
        // from WorldMap: a region is a centre and a radius, but the sites it generates
        // reach a long way outside that. Relay masts and overlooks go out to 1.7 region
        // radii, and wreck fields are deliberately dumped 1.3 to 2.6 radii out and then
        // scattered 560 m around THAT - so a region with a 1800 m radius can put a wreck
        // five kilometres from its own middle. Every one of those has to be on dry land or
        // the site is in the sea, so the sea can only go where this table says it can.
        GD.Print("");
        GD.Print("  land each region needs: how far its own sites reach from its centre");
        GD.Print("  region             radius   furthest site   what it is           wreck field at");
        foreach (Region r in WorldMap.Regions)
        {
            float far = 0;
            string what = "-";
            float wcx = 0, wcz = 0; int wn = 0;
            foreach (Site s in WorldMap.Sites)
            {
                if (s.Region != r.Kind) continue;
                float d = s.Position.DistanceTo(r.Centre);
                if (d > far) { far = d; what = $"{s.Kind} {s.Name}"; }
                if (s.Kind == SiteKind.Wreck) { wcx += s.Position.X; wcz += s.Position.Y; wn++; }
            }
            string wreck = wn == 0 ? "-"
                : $"({wcx / wn,6:F0},{wcz / wn,6:F0}) {new Vector2(wcx / wn, wcz / wn).DistanceTo(r.Centre),5:F0} m out";
            GD.Print($"  {r.Name,-18} {r.Radius,6:F0} {far,15:F0}   {Trim(what, 20),-20} {wreck}");
        }

        // The beat this all exists for: the drowned ferry. The story rule picks the LOWEST
        // wreck in the wetland, so every candidate is listed with where it came down - if
        // the wreck field landed 3 km outside the region, that is the finding, and no
        // amount of flooding the region centre would have fixed it.
        GD.Print("");
        Region wetland = WorldMap.Regions.First(x => x.Kind == RegionKind.Wetland);
        GD.Print($"  drowned-ferry candidates: wrecks in {wetland.Name}, " +
                 $"centre ({wetland.Centre.X:F0}, {wetland.Centre.Y:F0}) r {wetland.Radius:F0} m");
        foreach (Site s in WorldMap.Sites)
        {
            if (s.Region != RegionKind.Wetland || s.Kind != SiteKind.Wreck) continue;
            float rh = WorldHeight.RawAt(s.Position.X, s.Position.Y);
            string state = rh < WorldHeight.WaterLevel ? "IN THE WATER"
                         : rh < WorldHeight.WaterLevel + 8f ? "at the edge" : "dry";
            GD.Print($"    #{s.Id,-4} {s.Name,-14} ({s.Position.X,6:F0},{s.Position.Y,6:F0})" +
                     $"  {s.Position.DistanceTo(wetland.Centre),5:F0} m out   ground {rh,7:F1} m   {state}");
        }

        GD.Print($"  {WorldHeight.BasinCheck(wetland.Centre)}");
        GD.Print("");
        Coast();
    }

    /// <summary>
    /// The coastline, and what crossing it costs.
    ///
    /// The point of putting water between things is not scenery, it is CONSEQUENCE: over
    /// land an engine failure is an autorotation into a field and a walk home, and over
    /// water it is a swim and a lost aircraft. So the number that matters is not how much
    /// sea there is, it is how much of each leg a pilot actually flies over it, and
    /// whether the gaps are wider than the aircraft can glide.
    ///
    /// The aircraft glides about 2:1, so from 500 m it reaches a kilometre. A stretch of
    /// water materially wider than that is a committed crossing. Anything under it is a
    /// puddle and should not be mistaken for design.
    /// </summary>
    private static void Coast()
    {
        const float glide = 1000f;
        float half = WorldMap.ContentHalfExtent;

        // Land area and the size of each landmass, by flood fill over a coarse grid that
        // covers the whole island bounding box rather than just the content envelope.
        const int n = 300;
        float ext = WorldHeight.IslandHalfExtent;
        float cell = ext * 2 / n;
        var land = new bool[n * n];
        int landCells = 0, shallowCells = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -ext + i * cell, z = -ext + j * cell;
                float h = WorldHeight.At(x, z);
                bool dry = h >= WorldHeight.WaterLevel;
                land[j * n + i] = dry;
                if (dry) landCells++;
                else if (h > WorldHeight.WaterLevel - 6f) shallowCells++;
            }

        // How many separate landmasses there are, and how big the biggest is.
        var seen = new bool[n * n];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int k0 = 0; k0 < n * n; k0++)
        {
            if (!land[k0] || seen[k0]) continue;
            int size = 0;
            stack.Push(k0); seen[k0] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop(); size++;
                int ki = k % n, kj = k / n;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ki + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int nj = kj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int nk = nj * n + ni;
                    if (land[nk] && !seen[nk]) { seen[nk] = true; stack.Push(nk); }
                }
            }
            sizes.Add(size);
        }
        sizes.Sort((a, b) => b - a);
        float cellArea = cell * cell / 1e6f;

        GD.Print($"the coast, over the {ext * 2 / 1000f:F1} km island box");
        GD.Print($"  land                      {landCells * cellArea:F0} km2 " +
                 $"({landCells * 100.0 / (n * n):F0} % of the box)   " +
                 $"shallows (under 6 m) {shallowCells * cellArea:F0} km2");
        int big = 0;
        foreach (int sz in sizes) if (sz * cellArea > 0.5f) big++;
        GD.Print($"  landmasses over 0.5 km2   {big}   largest {sizes[0] * cellArea:F0} km2" +
                 (sizes.Count > 1 ? $", next {sizes[1] * cellArea:F1} km2" : ""));
        GD.Print($"  streamed world            +/- {WorldHeight.WorldHalfExtent / 1000f:F0} km, " +
                 $"so open water runs {(WorldHeight.WorldHalfExtent - ext) / 1000f:F0} km past the last beach " +
                 $"({(WorldHeight.WorldHalfExtent - ext) / 55f / 60f:F0} min at cruise) before anything stops");

        // Every leg of the story route, and how much of it is over water. This is the
        // table that says whether the archipelago is doing its job.
        GD.Print("");
        GD.Print("  crossings between regions, straight line: how much is over water");
        GD.Print("  from               to                  leg     over water   longest gap");
        var rs = WorldMap.Regions;
        for (int a = 0; a < rs.Count; a++)
        {
            for (int b = a + 1; b < rs.Count; b++)
            {
                float legLen = rs[a].Centre.DistanceTo(rs[b].Centre);
                // Neighbours only; the rest are not legs anybody flies. The threshold was
                // 6200 m, which was right when the regions were 2.4 to 6 km apart and
                // wrong the moment the archipelago spread them out - every real crossing
                // in the world was longer than that, so this table printed one row and
                // claimed there was nothing to report. If the layout moves again, check
                // this number against it.
                if (legLen > 13000f) continue;
                int steps = Mathf.Max(2, (int)(legLen / 25f));
                float wetLen = 0, run = 0, worst = 0;
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = rs[a].Centre.Lerp(rs[b].Centre, i / (float)steps);
                    if (WorldHeight.IsWater(p.X, p.Y)) { wetLen += legLen / steps; run += legLen / steps; }
                    else { worst = Mathf.Max(worst, run); run = 0; }
                }
                worst = Mathf.Max(worst, run);
                GD.Print($"  {rs[a].Name,-18} {rs[b].Name,-18} {legLen / 1000f,5:F1} km " +
                         $"{wetLen / legLen * 100,8:F0} %  {worst,8:F0} m" +
                         (worst > glide ? "   COMMITTED" : ""));
            }
        }

        // And the same question asked of the map as a whole rather than of the route.
        int overWater = 0, beyondGlide = 0, total2 = 0;
        const int gs = 200;
        float gstep = half * 2 / gs;
        for (int j = 0; j < gs; j++)
            for (int i = 0; i < gs; i++)
            {
                float x = -half + i * gstep, z = -half + j * gstep;
                total2++;
                if (!WorldHeight.IsWater(x, z)) continue;
                overWater++;
                // Out of glide: no dry land within a kilometre in any of eight directions.
                bool safe = false;
                for (int d = 0; d < 8 && !safe; d++)
                {
                    float a = Mathf.Tau * d / 8f;
                    if (!WorldHeight.IsWater(x + Mathf.Cos(a) * glide, z + Mathf.Sin(a) * glide)) safe = true;
                }
                if (!safe) beyondGlide++;
            }
        GD.Print($"  water inside the envelope {overWater * 100.0 / total2:F1} % of it, of which " +
                 $"{(overWater > 0 ? beyondGlide * 100.0 / overWater : 0):F0} % is further than a " +
                 $"{glide:F0} m glide from dry land");
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
        // Shortfalls first, and loudly. The census below shows what exists; this shows what
        // the design asked for and did not get, which is the thing that used to be invisible.
        if (WorldMap.Shortfalls.Count == 0)
        {
            GD.Print("  every region got everything the plan asked for.");
        }
        else
        {
            GD.Print($"  *** {WorldMap.Shortfalls.Count} SHORTFALL(S): the plan asked for places the terrain refused ***");
            foreach ((string region, SiteKind kind, int wanted, int got) in WorldMap.Shortfalls)
                GD.Print($"      {region,-18} {kind,-11} wanted {wanted}, got {got}");
        }
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

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + ".";

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
