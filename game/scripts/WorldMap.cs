using System;
using System.Collections.Generic;
using System.Linq;
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
        // Smaller and further apart than the first layout, which gave regions of
        // 1900-2600 m across a 13 km envelope - covering roughly three quarters of it, so
        // nowhere was ever remote. Measured, you were within 3.1 km of something from ANY
        // point on the map. These are tighter, and the country between them is meant to be
        // empty: that emptiness is what turns a leg into a journey.
        return new List<Region>
        {
            new(RegionKind.Basin,      "The Pan",          new Vector2(  200,  1100), 1500, 0),
            new(RegionKind.Farmland,   "Long Acre",        new Vector2(-3100,  -500), 1600, 0),
            new(RegionKind.Exurb,      "Fenmoor",          new Vector2( 3000, -1400), 1400, 1),
            new(RegionKind.Wetland,    "The Drowning",     new Vector2(-1600,  4700), 1400, 1),
            new(RegionKind.Upland,     "Cold Shoulder",    new Vector2( 1500, -4400), 1700, 2),
            new(RegionKind.Industrial, "Sawtooth Works",   new Vector2(-4800, -3300), 1300, 2),
            new(RegionKind.City,       "Ashmount",         new Vector2( 4600,  3100), 1800, 3),
            new(RegionKind.Ashfield,   "The Scald",        new Vector2(-4000,  4300), 1200, 3),
        };
    }

    /// <summary>
    /// Where people actually settled inside a region.
    ///
    /// Scattering sites uniformly across a region gives a map where nowhere is remote -
    /// measured, the old layout put you within 3.1 km of something from ANY point on the
    /// map, so no leg was ever a journey. Real settlement is lumpy: a few places to live,
    /// with genuinely empty country between them.
    ///
    /// So human infrastructure clusters around a handful of anchors per region, and the
    /// gaps are left to the things that end up wherever they end up - a wreck, a mast on
    /// a hill, a survey point.
    /// </summary>
    private static List<Vector2> AnchorsFor(Region region)
    {
        if (_anchors.TryGetValue(region.Kind, out List<Vector2>? cached)) return cached;

        var rng = new RandomNumberGenerator { Seed = (ulong)(region.Kind.GetHashCode() * 7919 + 31) };
        int count = region.Radius > 2300 ? 3 : 2;
        var list = new List<Vector2>();

        for (int i = 0; i < count; i++)
        {
            Vector2 best = region.Centre;
            float bestScore = -1;
            // Pick the candidate furthest from the anchors already placed, so clusters do
            // not end up on top of each other and the empty ground between them is real.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float a = rng.Randf() * Mathf.Tau;
                float r = Mathf.Sqrt(rng.Randf()) * region.Radius * 0.66f;
                var c = region.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (Mathf.Abs(c.X) > ContentHalfExtent || Mathf.Abs(c.Y) > ContentHalfExtent) continue;

                // Prefer ground people would actually build on.
                float slope = RawSlope(c.X, c.Y, 10f);
                if (slope > 11f) continue;

                float score = list.Count == 0 ? rng.Randf()
                    : list.Min(x => x.DistanceTo(c));
                if (score > bestScore) { bestScore = score; best = c; }
            }
            list.Add(best);
        }

        _anchors[region.Kind] = list;
        return list;
    }

    private static readonly Dictionary<RegionKind, List<Vector2>> _anchors = new();
    private static readonly Dictionary<RegionKind, List<Vector2>> _incidents = new();

    /// <summary>
    /// Where things ended up that nobody chose - crash sites, vehicle jams, the line of a
    /// road that stopped working.
    ///
    /// These get anchors too, and that is the correction that made the map feel large.
    /// Scattering wrecks and masts uniformly meant 58 of 127 sites were spread evenly over
    /// the whole envelope, and they filled in every gap the settlement clusters left. Real
    /// wrecks come in fields: several in one place because several aircraft were going the
    /// same way, and then nothing for miles.
    /// </summary>
    private static List<Vector2> IncidentsFor(Region region)
    {
        if (_incidents.TryGetValue(region.Kind, out List<Vector2>? cached)) return cached;

        var rng = new RandomNumberGenerator { Seed = (ulong)(region.Kind.GetHashCode() * 104729 + 17) };
        var list = new List<Vector2>();
        int count = 1;
        for (int i = 0; i < count; i++)
        {
            float a = rng.Randf() * Mathf.Tau;
            // Well outside the settled ground: these are places people pass through, or
            // used to.
            float r = region.Radius * rng.RandfRange(1.3f, 2.6f);
            var c = region.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            c = new Vector2(Mathf.Clamp(c.X, -ContentHalfExtent, ContentHalfExtent),
                            Mathf.Clamp(c.Y, -ContentHalfExtent, ContentHalfExtent));
            list.Add(c);
        }
        _incidents[region.Kind] = list;
        return list;
    }

    /// <summary>
    /// Where the plan asked for more places than the terrain would take. Empty is the goal;
    /// anything in here is a region quietly missing something the design asked for.
    /// </summary>
    public static readonly List<(string Region, SiteKind Kind, int Wanted, int Got)> Shortfalls = new();

    /// <summary>
    /// Whether this kind of place belongs to a settlement cluster or ends up on its own.
    /// People build near people; a wreck is where it came down.
    /// </summary>
    private static bool Clusters(SiteKind kind) => kind switch
    {
        SiteKind.Wreck or SiteKind.Overlook or SiteKind.Relay => false,
        _ => true,
    };

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
            // Filler counts trimmed after the placement fix.
            //
            // These numbers were calibrated against a generator that was silently DROPPING
            // about a quarter of what it was asked for. Once placement actually worked the
            // world went from 126 sites to 164, and remote country - deliberately tuned to
            // 11% of the map being over 2 km from anywhere - collapsed to 5%. The design
            // intent was the OUTCOME, not the raw request, so the filler kinds come down
            // and the meaningful ones (towns, workshops, depots) stay up.
            [RegionKind.Basin]      = (3, 3, 2, 2, 1, 0, 1, 2, 1),
            [RegionKind.Farmland]   = (2, 4, 2, 2, 1, 0, 1, 4, 1),
            [RegionKind.Exurb]      = (2, 4, 2, 2, 1, 2, 1, 2, 1),
            [RegionKind.Wetland]    = (2, 2, 1, 3, 1, 1, 0, 2, 1),
            [RegionKind.Upland]     = (2, 2, 1, 2, 1, 1, 0, 2, 1),
            [RegionKind.Industrial] = (2, 2, 3, 2, 1, 3, 1, 1, 0),
            [RegionKind.City]       = (2, 4, 3, 3, 1, 3, 1, 1, 1),
            [RegionKind.Ashfield]   = (2, 1, 0, 3, 1, 2, 0, 1, 1),
        };

        foreach (Region region in Regions)
        {
            var counts = plan[region.Kind];
            void Place(SiteKind kind, int n)
            {
                int placed = 0;

                // Three passes at widening desperation.
                //
                // The spacing rule is an AESTHETIC one - two settlements in sight of each
                // other read as one - and it was being enforced as though it were physics,
                // with the result that three regions had no airfield and several had no
                // workshop. A region missing the only place you can repair an aircraft is a
                // far worse outcome than two workshops being closer together than ideal, so
                // once the ideal spacing has genuinely failed, the rule relaxes rather than
                // the place vanishing. Full spacing is still tried first, every time.
                foreach (float gapScale in new[] { 1.0f, 0.6f, 0.35f })
                {
                    while (placed < n && TryPlace(rng, region, kind, sites, out Vector2 pos, gapScale))
                    {
                        sites.Add(new Site(id++, NameFor(kind, region, rng), kind, pos,
                                           region.Kind, RadiusFor(kind), region.Tier));
                        placed++;
                    }
                    if (placed >= n) break;
                }

                // Record what the plan asked for against what the terrain allowed.
                //
                // Placement failing SILENTLY is the bug this generator keeps having. The
                // comment just below already records the time every airfield vanished, and
                // it happened again: the plan asks for four settlements in Long Acre and two
                // in Sawtooth Works and produced none of either, so two of eight regions had
                // nobody living in them and nothing said so. A shortfall is data now.
                if (placed < n) Shortfalls.Add((region.Name, kind, n, placed));
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
                                 List<Site> placed, out Vector2 pos, float gapScale = 1.0f)
    {
        List<Vector2> anchors = AnchorsFor(region);
        bool clustered = Clusters(kind);

        for (int attempt = 0; attempt < 160; attempt++)
        {
            float a = rng.Randf() * Mathf.Tau;

            if (clustered)
            {
                // Tight around a settlement anchor. A few of these deliberately sit
                // further out - an outlying farm or a fuel dump on the road - so the
                // cluster has an edge rather than a boundary.
                Vector2 anchor = anchors[rng.RandiRange(0, anchors.Count - 1)];
                float spread = rng.Randf() < 0.20f ? 1100f : 640f;
                float r = Mathf.Sqrt(rng.Randf()) * spread;
                pos = anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            else if (kind is SiteKind.Relay or SiteKind.Overlook)
            {
                // High ground, wherever it is. These are the landmarks you navigate by,
                // so they should be spread - but there are deliberately few of them.
                float r = Mathf.Sqrt(rng.Randf()) * region.Radius * 1.7f;
                pos = region.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            else
            {
                // Wreck fields: several together, then nothing for miles.
                List<Vector2> incidents = IncidentsFor(region);
                Vector2 anchor = incidents[rng.RandiRange(0, incidents.Count - 1)];
                float r = Mathf.Sqrt(rng.Randf()) * 560f;
                pos = anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }

            if (Mathf.Abs(pos.X) > ContentHalfExtent || Mathf.Abs(pos.Y) > ContentHalfExtent) continue;

            float h = WorldHeight.RawAt(pos.X, pos.Y);
            // Measured on the RAW terrain and at the scale a landing cares about, not a
            // coarse one. The two disagreeing is how a fuel cache ended up on a 40 degree
            // slope; the graded pad then makes the spot landable regardless.
            float slope = RawSlope(pos.X, pos.Y, 3f);

            bool ok = kind switch
            {
                // A mast on a hilltop is visible from 8 km, which is the entire point of it.
                SiteKind.Relay => h > 110f && slope < 18f,
                SiteKind.Overlook => h > 130f && slope < 22f,
                // People build where it is flat and not underwater.
                // Flatness is the real constraint; absolute height was not.
                //
                // The old 150 m ceiling excluded whole REGIONS. Sawtooth Works sits at
                // 275-280 m, so no settlement, workshop or farmstead could exist anywhere in
                // it however flat the ground, and the plan's request for two towns silently
                // produced none. The map runs to 425 m with a median of 133, and people
                // demonstrably live in high country - 320 m keeps towns off the peaks while
                // letting the uplands have somewhere to live.
                SiteKind.Settlement or SiteKind.Farmstead
                    => slope < 7f && h > 6f && h < 320f,
                // A workshop is ONE SHED, not a town, and it can sit on ground a village
                // could not. Sharing the settlement rule left Sawtooth Works - an industrial
                // region literally named "Works" - with no workshop at all, because the
                // towns placed first took every flat spot the region had.
                SiteKind.Workshop => slope < 11f && h > 6f && h < 340f,
                // Same correction as the settlements above: a runway needs FLAT ground, not
                // LOW ground, and the 130 m ceiling was leaving three regions with nowhere
                // to land a fixed-wing aircraft at all. The 4.5 degree slope limit is the
                // constraint that actually matters and it is unchanged.
                SiteKind.Airfield => slope < 4.5f && h > 20f && h < 300f,
                SiteKind.Depot or SiteKind.FuelCache => slope < 9f && h > 4f,
                // A wreck is wherever it came down, which is usually somewhere awkward.
                SiteKind.Wreck => slope < 26f,
                _ => slope < 12f,
            };
            if (!ok) continue;

            // Keep places apart. Two settlements in sight of each other read as one.
            bool tooClose = false;
            foreach (Site s in placed)
                if (s.Position.DistanceTo(pos) < RequiredGap(kind, s.Kind) * gapScale) { tooClose = true; break; }
            if (tooClose) continue;

            return true;
        }
        pos = region.Centre;
        return false;
    }

    /// <summary>Slope of the natural ground, before any site levelling is applied.</summary>
    private static float RawSlope(float x, float z, float e)
    {
        float hL = WorldHeight.RawAt(x - e, z), hR = WorldHeight.RawAt(x + e, z);
        float hD = WorldHeight.RawAt(x, z - e), hU = WorldHeight.RawAt(x, z + e);
        var n = new Vector3(hL - hR, 2f * e, hD - hU).Normalized();
        return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(n.Y, -1, 1)));
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

    public static Site? SiteById(int id)
    {
        foreach (Site s in Sites) if (s.Id == id) return s;
        return null;
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

    /// <summary>
    /// Estimated population of one site. Seed-varied so not every settlement is the same
    /// size, but deterministic so the count is stable across frames.
    ///
    /// The number is a rough head-count, not a census: it decides whether a deed was
    /// witnessed and by how many, which gates whether the radio announcer ever mentions
    /// it. Zero means nobody saw it and it never happened as far as the district is
    /// concerned (D-074).
    /// </summary>
    public static int PopulationAt(Site site)
    {
        // Seed from site id so the number is stable.
        uint h = (uint)(site.Id * 2654435761);
        float t = (h & 0xFFFF) / 65535f;   // 0..1
        return site.Kind switch
        {
            SiteKind.Settlement => 30 + (int)(t * 50),   // 30-80
            SiteKind.Farmstead  =>  3 + (int)(t * 10),   // 3-13
            SiteKind.Workshop   =>  5 + (int)(t * 10),   // 5-15
            SiteKind.Airfield   => 10 + (int)(t * 20),   // 10-30
            SiteKind.Depot      =>  2 + (int)(t *  6),   // 2-8
            SiteKind.FuelCache  =>      (int)(t *  3),   // 0-3
            SiteKind.Relay      =>      (int)(t *  2),   // 0-2
            _                   => 0,                     // wrecks, overlooks
        };
    }

    /// <summary>
    /// Total estimated population within <paramref name="radius"/> metres of a point.
    /// Used for the deed witness count (D-074): a delivery to a settlement surrounded by
    /// farmsteads has more witnesses than one to an isolated fuel cache.
    /// </summary>
    public static int PopulationNear(float x, float z, float radius = 3000f)
    {
        int total = 0;
        foreach (Site s in Near(new Vector2(x, z), radius))
            total += PopulationAt(s);
        return total;
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
