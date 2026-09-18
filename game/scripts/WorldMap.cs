using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

public enum RegionKind { Basin, Farmland, Exurb, City, Industrial, Upland, Wetland, Ashfield }

public enum SiteKind
{
    FuelCache,      // the reason to fly anywhere at all
    Settlement,     // people, trade, dialogue
    Workshop,       // repairs and parts
    Wreck,          // salvage, and the only other aircraft you will ever see
    Relay,          // radio masts: frequencies, and a reason to climb
    Depot,          // industrial salvage, usually guarded
    Airfield,       // the big prizes
    Farmstead,      // small, quiet, often empty
    Overlook,       // no loot, a view, and a survey point that fills in the chart
}

/// <summary>One named place in the world.</summary>
public sealed record Site(
    int Id,
    string Name,
    SiteKind Kind,
    Vector2 Position,       // world XZ
    RegionKind Region,
    float Radius,           // metres of footprint
    int Tier)               // 0 = starting area, 3 = deep in defended country
{
    public Vector3 Ground => new(Position.X, WorldHeight.At(Position.X, Position.Y), Position.Y);
}

public sealed record Region(RegionKind Kind, string Name, Vector2 Centre, float Radius, int Tier);

/// <summary>
/// The authored layer of the world: where the regions are and where the places are.
///
/// Deliberately a small amount of DATA over a large amount of procedural terrain. The
/// benchmark pass (D-003b) put the defensible target at ~124 named places over a 13 km
/// content envelope, on the grounds that no acclaimed open world in the comparison set
/// exceeds about 50 km² and ours is already three times that. Everything here is
/// generated deterministically from a seed and then *placed against the terrain*, so a
/// settlement sits in a valley and a relay mast sits on a ridge, because that is where
/// those things go.
///
/// Placement obeys the altitude-banded rule: every site is given a silhouette element
/// that reads at 500 m, an occupancy cue that reads at 150 m, and a landing problem that
/// only resolves at 15 m.
/// </summary>
public static class WorldMap
{
    /// <summary>Half-extent of the content envelope, metres. Smaller than the terrain grid.</summary>
    public const float ContentHalfExtent = 6500f;

    private static List<Region>? _regions;
    private static List<Site>? _sites;

    public static IReadOnlyList<Region> Regions => _regions ??= BuildRegions();
    public static IReadOnlyList<Site> Sites => _sites ??= BuildSites();

    // ----------------------------------------------------------------- regions

    private static List<Region> BuildRegions()
    {
        // Laid out by hand rather than generated: the shape of the country is the one
        // thing that should not be random. Tier rises with distance from the start.
        return new List<Region>
        {
            new(RegionKind.Basin,      "The Pan",          new Vector2(   0,  1400), 2300, 0),
            new(RegionKind.Farmland,   "Long Acre",        new Vector2(-2600,  -200), 2400, 0),
            new(RegionKind.Exurb,      "Fenmoor",          new Vector2( 2400,  -600), 2100, 1),
            new(RegionKind.Wetland,    "The Drowning",     new Vector2(-1200,  4200), 2200, 1),
            new(RegionKind.Upland,     "Cold Shoulder",    new Vector2( 1200, -3800), 2600, 2),
            new(RegionKind.Industrial, "Sawtooth Works",   new Vector2(-4200, -2800), 2000, 2),
            new(RegionKind.City,       "Ashmount",         new Vector2( 4100,  2600), 2500, 3),
            new(RegionKind.Ashfield,   "The Scald",        new Vector2(-3400,  3600), 1900, 3),
        };
    }

    public static Region RegionAt(Vector2 p)
    {
        Region best = Regions[0];
        float bestScore = float.MaxValue;
        foreach (Region r in Regions)
        {
            // Nearest centre, normalised by radius, so a big region reaches further.
            float score = p.DistanceTo(r.Centre) / Mathf.Max(r.Radius, 1f);
            if (score < bestScore) { bestScore = score; best = r; }
        }
        return best;
    }

    // ------------------------------------------------------------------- sites

    private static List<Site> BuildSites()
    {
        var sites = new List<Site>();
        var rng = new RandomNumberGenerator { Seed = 40404 };
        int id = 0;

        // How many of each kind each region wants. Fuel is everywhere because it is the
        // reason to go anywhere; workshops are rare because repair should be a journey.
        var plan = new Dictionary<RegionKind, (int fuel, int settle, int shop, int wreck, int relay, int depot, int field, int farm, int look)>
        {
            [RegionKind.Basin]      = (5, 2, 1, 3, 1, 0, 1, 4, 2),
            [RegionKind.Farmland]   = (4, 3, 1, 3, 1, 0, 1, 7, 2),
            [RegionKind.Exurb]      = (4, 3, 2, 4, 1, 1, 1, 3, 2),
            [RegionKind.Wetland]    = (3, 2, 1, 4, 1, 1, 0, 3, 2),
            [RegionKind.Upland]     = (3, 1, 1, 3, 2, 1, 0, 2, 4),
            [RegionKind.Industrial] = (3, 2, 2, 4, 1, 3, 1, 1, 1),
            [RegionKind.City]       = (4, 3, 2, 5, 2, 3, 1, 0, 2),
            [RegionKind.Ashfield]   = (3, 1, 0, 5, 1, 1, 0, 1, 2),
        };

        foreach (Region region in Regions)
        {
            var counts = plan[region.Kind];
            void Place(SiteKind kind, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    if (TryPlace(rng, region, kind, sites, out Vector2 pos))
                        sites.Add(new Site(id++, NameFor(kind, region, rng), kind, pos,
                                           region.Kind, RadiusFor(kind), region.Tier));
                }
            }

            // Order matters. The big, tightly constrained places go down first; if the
            // twenty-two fuel caches are scattered first, every remaining spot is inside
            // somebody else's exclusion radius and the airfields silently never appear -
            // which is exactly what happened the first time.
            Place(SiteKind.Airfield, counts.field);
            Place(SiteKind.Settlement, counts.settle);
            Place(SiteKind.Relay, counts.relay);
            Place(SiteKind.Depot, counts.depot);
            Place(SiteKind.Workshop, counts.shop);
            Place(SiteKind.Overlook, counts.look);
            Place(SiteKind.Farmstead, counts.farm);
            Place(SiteKind.FuelCache, counts.fuel);
            Place(SiteKind.Wreck, counts.wreck);
        }

        return sites;
    }

    /// <summary>
    /// Find somewhere this kind of place would actually be. Rejection sampling against the
    /// terrain: a relay wants a ridge, a settlement wants shelter and flat ground, a
    /// wetland wreck wants to be half in the water.
    /// </summary>
    private static bool TryPlace(RandomNumberGenerator rng, Region region, SiteKind kind,
                                 List<Site> placed, out Vector2 pos)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            float a = rng.Randf() * Mathf.Tau;
            float r = Mathf.Sqrt(rng.Randf()) * region.Radius;
            pos = region.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

            if (Mathf.Abs(pos.X) > ContentHalfExtent || Mathf.Abs(pos.Y) > ContentHalfExtent) continue;

            float h = WorldHeight.At(pos.X, pos.Y);
            float slope = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
                WorldHeight.NormalAt(pos.X, pos.Y, 6f).Y, -1, 1)));

            bool ok = kind switch
            {
                // A mast on a hilltop is visible from 8 km, which is the entire point of it.
                SiteKind.Relay => h > 110f && slope < 18f,
                SiteKind.Overlook => h > 130f && slope < 22f,
                // People build where it is flat and not underwater.
                SiteKind.Settlement or SiteKind.Workshop or SiteKind.Farmstead => slope < 7f && h > 6f && h < 150f,
                SiteKind.Airfield => slope < 4.5f && h > 20f && h < 130f,
                SiteKind.Depot or SiteKind.FuelCache => slope < 9f && h > 4f,
                // A wreck is wherever it came down, which is usually somewhere awkward.
                SiteKind.Wreck => slope < 26f,
                _ => slope < 12f,
            };
            if (!ok) continue;

            // Keep places apart. Two settlements in sight of each other read as one.
            bool tooClose = false;
            foreach (Site s in placed)
                if (s.Position.DistanceTo(pos) < RequiredGap(kind, s.Kind)) { tooClose = true; break; }
            if (tooClose) continue;

            return true;
        }
        pos = region.Centre;
        return false;
    }

    /// <summary>
    /// How far apart two kinds of place have to be.
    ///
    /// Pairwise, not "whichever needs more room". Two settlements in sight of each other
    /// read as one settlement, so they need kilometres - but a fuel cache half a mile
    /// from a settlement is a perfectly sensible thing for a fuel cache to be, and making
    /// it obey the settlement's spacing is what starved the map of airfields.
    /// </summary>
    private static float RequiredGap(SiteKind a, SiteKind b)
    {
        if (a == b) return SameKindGap(a);
        bool majorA = IsMajor(a), majorB = IsMajor(b);
        if (majorA && majorB) return 900f;
        if (majorA || majorB) return 240f;
        return 190f;
    }

    private static bool IsMajor(SiteKind k) =>
        k is SiteKind.Settlement or SiteKind.Airfield or SiteKind.Depot or SiteKind.Workshop or SiteKind.Relay;

    private static float SameKindGap(SiteKind kind) => kind switch
    {
        SiteKind.Settlement or SiteKind.Airfield => 1700f,
        SiteKind.Relay => 1900f,
        SiteKind.Workshop or SiteKind.Depot => 1100f,
        SiteKind.Overlook => 900f,
        SiteKind.Farmstead => 480f,
        _ => 330f,
    };

    public static float RadiusFor(SiteKind kind) => kind switch
    {
        SiteKind.Settlement => 95f,
        SiteKind.Airfield => 180f,
        SiteKind.Depot => 110f,
        SiteKind.Workshop => 55f,
        SiteKind.Farmstead => 45f,
        SiteKind.FuelCache => 28f,
        SiteKind.Relay => 35f,
        SiteKind.Wreck => 30f,
        _ => 22f,
    };

    // ------------------------------------------------------------------- names

    private static readonly string[] Prefix =
    {
        "Bitter", "Kettle", "Low", "Marrow", "Cinder", "Salt", "Hollow", "Grey", "Long",
        "Stone", "Cold", "Hanging", "Black", "Wither", "Slack", "Iron", "Pale", "Dead",
        "Quiet", "Broke", "Far", "Old", "Wind", "Rust",
    };

    private static readonly string[] SuffixLand = { "Field", "Acre", "Furrow", "Bottom", "Reach", "Moor", "Fold", "Green" };
    private static readonly string[] SuffixWorks = { "Works", "Yard", "Shed", "Line", "Cut", "Pit", "Stack", "Shop" };
    private static readonly string[] SuffixWater = { "Draw", "Ford", "Sink", "Wash", "Race", "Pool" };
    private static readonly string[] SuffixHigh = { "Rise", "Head", "Crag", "Tor", "Point", "Bluff", "Watch" };

    private static string NameFor(SiteKind kind, Region region, RandomNumberGenerator rng)
    {
        string p = Prefix[rng.RandiRange(0, Prefix.Length - 1)];
        string[] pool = kind switch
        {
            SiteKind.Relay or SiteKind.Overlook => SuffixHigh,
            SiteKind.Depot or SiteKind.Workshop or SiteKind.Airfield => SuffixWorks,
            _ => region.Kind switch
            {
                RegionKind.Wetland => SuffixWater,
                RegionKind.Industrial or RegionKind.City => SuffixWorks,
                RegionKind.Upland => SuffixHigh,
                _ => SuffixLand,
            },
        };
        string s = pool[rng.RandiRange(0, pool.Length - 1)];

        // A handful of places get a plainer, sadder name. Uniform poetry stops reading
        // as poetry.
        if (rng.Randf() < 0.22f)
        {
            string[] plain = { "the Depot", "the Old Pump", "Number Four", "the Turn",
                               "Mile Twelve", "the Sheds", "the Crossing", "the Tanks" };
            return plain[rng.RandiRange(0, plain.Length - 1)];
        }

        return $"{p} {s}";
    }

    // ------------------------------------------------------------------ lookup

    /// <summary>Sites whose footprint is within range of a point. Used by the streamer.</summary>
    public static IEnumerable<Site> Near(Vector2 p, float range)
    {
        foreach (Site s in Sites)
            if (s.Position.DistanceSquaredTo(p) < range * range) yield return s;
    }

    public static Site? Nearest(Vector2 p, SiteKind? kind = null)
    {
        Site? best = null;
        float bestD = float.MaxValue;
        foreach (Site s in Sites)
        {
            if (kind is SiteKind k && s.Kind != k) continue;
            float d = s.Position.DistanceSquaredTo(p);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    /// <summary>One-line summary, for logging and the eventual map screen.</summary>
    public static string Describe()
    {
        var byKind = new Dictionary<SiteKind, int>();
        foreach (Site s in Sites) byKind[s.Kind] = byKind.GetValueOrDefault(s.Kind) + 1;
        var parts = new List<string>();
        foreach (var kv in byKind) parts.Add($"{kv.Value} {kv.Key}");
        return $"{Sites.Count} sites across {Regions.Count} regions: {string.Join(", ", parts)}";
    }
}
