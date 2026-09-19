using Godot;

namespace Rotorwash;

/// <summary>
/// The world's height field, as a pure function of position.
///
/// Deliberately a static, allocation-free function rather than a baked heightmap or a
/// node, because five different systems need the answer and they must never disagree:
/// the terrain mesh, the collision shape, the prop scatter, the flight model's
/// ground-effect and radar-altimeter queries, and the map. A single function called from
/// all five cannot drift.
///
/// It is also why the world can stream: any chunk, at any level of detail, at any time,
/// can be generated from nothing but its coordinates.
///
/// Godot world axes throughout: X east, Z south, Y up.
/// </summary>
public static class WorldHeight
{
    public const int Seed = 1977;

    /// <summary>
    /// Half-extent of the STREAMED world, metres. The terrain streamer builds nothing
    /// past this, so it is where the sea finally stops existing.
    ///
    /// It is 250 km, which is not a playable distance and is not meant to be: the world is
    /// an island, and the thing that stops a pilot flying off the edge of it is fuel, not
    /// geometry (D-004). At cruise this is over an hour of open water beyond the coast in
    /// a machine that cannot carry an hour of fuel, so the wall exists only in the sense
    /// that the horizon exists. Nobody will ever reach it, and nothing else in the game
    /// uses this number to mean "the world" - see <see cref="InBounds"/>, which is what
    /// the prop scatter and the threat survey actually ask.
    /// </summary>
    public const float WorldHalfExtent = 250000f;

    /// <summary>
    /// Bounding half-extent of all LAND, metres. Everything past this is open sea.
    ///
    /// Used by <see cref="InBounds"/>, which is what the prop scatter and the threat survey
    /// ask when they mean "the world" - neither of them wants to be told that the world is
    /// 250 km across, because neither of them has anything to say about the ocean.
    /// </summary>
    public const float IslandHalfExtent = 7600f;

    private static readonly FastNoiseLite Continent = new()
    {
        Seed = Seed,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.000075f,
        FractalOctaves = 3,
        FractalGain = 0.5f,
    };

    private static readonly FastNoiseLite Hills = new()
    {
        Seed = Seed + 101,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.00042f,
        FractalOctaves = 5,
        FractalLacunarity = 2.05f,
        FractalGain = 0.47f,
    };

    private static readonly FastNoiseLite Ridges = new()
    {
        Seed = Seed + 211,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.00095f,
        FractalOctaves = 4,
        FractalGain = 0.52f,
    };

    private static readonly FastNoiseLite Detail = new()
    {
        Seed = Seed + 977,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.0085f,
        FractalOctaves = 3,
        FractalGain = 0.44f,
    };

    /// <summary>Warps the hill field so ridges meander instead of running in straight bands.</summary>
    private static readonly FastNoiseLite Warp = new()
    {
        Seed = Seed + 555,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.00031f,
        FractalOctaves = 2,
    };

    public const float SeaLevel = 0f;

    /// <summary>Height of standing water in valleys. Terrain below this is underwater.</summary>
    public const float WaterLevel = -5f;

    // ------------------------------------------------------------------ the basin
    //
    // The one authored coordinate in this file, and it is authored on purpose.
    //
    // The Drowning is a named region with a story beat in it - an aircraft lying half in
    // the water, tail boom up - and a beat cannot depend on whether the noise happened to
    // put a basin there. So the basin is placed, and the placement has to agree with the
    // region's own centre in WorldMap.
    //
    // It is duplicated rather than read from WorldMap because RawAt is the hottest
    // function in the game (every terrain vertex, every collision triangle, every radar
    // altimeter query) and because WorldMap's site placement calls RawAt, which makes the
    // dependency circular in the direction that matters. So the number is copied, and
    // BasinCheck below is what stops the copy drifting - the world report prints it.

    /// <summary>Centre of the drowned basin. Must match WorldMap's Wetland region centre.</summary>
    public static readonly Vector2 BasinCentre = new(-1600f, 4700f);

    /// <summary>Carves the valley network. Ridged noise inverted becomes drainage.</summary>
    private static readonly FastNoiseLite Valleys = new()
    {
        Seed = Seed + 733,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.00075f,
        FractalOctaves = 3,
        FractalGain = 0.5f,
    };

    /// <summary>A second, tighter drainage network. Gullies, not valleys.</summary>
    private static readonly FastNoiseLite Gullies = new()
    {
        Seed = Seed + 1481,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.0021f,
        FractalOctaves = 2,
        FractalGain = 0.5f,
    };

    /// <summary>
    /// The river network.
    ///
    /// Separate from <see cref="Valleys"/>, and separate for a measured reason. The valley
    /// field is three octaves, and the world report put the median distance from anywhere
    /// on the map to one of its zero crossings at 181 m: it is not a network of valleys,
    /// it is a TEXTURE of them, which is exactly what it should be for carving relief and
    /// exactly what it must not be for carrying water. Cutting a 460 m terrace to that
    /// field terraced 82% of the map.
    ///
    /// One low frequency and almost no second octave, so the zero set is a handful of long
    /// lines across the whole world rather than a mesh. That is the shape a river system
    /// has, and it is the shape a pilot can navigate by: few enough to tell apart, long
    /// enough to follow somewhere.
    /// </summary>
    private static readonly FastNoiseLite Rivers = new()
    {
        Seed = Seed + 2287,
        NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
        Frequency = 0.00026f,
        FractalOctaves = 2,
        FractalGain = 0.33f,
    };

    // ------------------------------------------------------------- drainage tuning
    //
    // Every one of these is in METRES or in continental-field units, and every one of
    // them was set by reading the world report back rather than by taste. The numbers
    // that matter are printed by the report's water section: the fraction of the map
    // under water, the distance from anywhere to the nearest water, and the lowest
    // ground in each region.

    /// <summary>Continental value the drowned basin is pushed down to. Sets how low it lies.</summary>
    private const float BasinFloor = 0.135f;
    private const float BasinInner = 1400f;    // full depth within this radius
    private const float BasinOuter = 2500f;   // natural ground again beyond it

    // The drainage cross-section, as three nested cuts. A real river valley is not a
    // trench: it is a wide vale, with a flood plain in the bottom of it, with a channel in
    // the bottom of THAT. Cutting all three from the same centreline is what makes the
    // result read as a river system from 300 m rather than as a groove in a hillside.
    private const float TerraceHalfWidth = 460f;
    private const float PlainHalfWidth = 190f;
    private const float ChannelHalfWidth = 52f;

    /// <summary>How far the terrace floor sits below the continental datum, metres.</summary>
    private const float TerraceDrop = 58f;
    /// <summary>...and how far it stays above the water, so it is dry buildable ground.</summary>
    private const float TerraceClearance = 30f;
    /// <summary>How far the flood plain sits below the datum. Deeper than the terrace.</summary>
    private const float PlainDrop = 76f;

    private const float ChannelDryDepth = 9f;
    private const float ChannelWetDepth = 42f;

    // Which country is low enough to hold water. Below the first value the drainage is
    // cut to base level and floods; above the second it is dry and the terrain here is
    // left exactly as it was before any of this existed.
    private const float LowlandStart = 0.30f;
    private const float LowlandEnd = 0.45f;

    /// <summary>How far the drowned basin reaches up its own drainage lines, metres.</summary>
    private const float BasinReach = 600f;

    // ------------------------------------------------------------------ the coast
    //
    // THE ARCHIPELAGO TABLE. This is the shape of the world above water, and it is the one
    // piece of geography that is authored rather than generated, for the same reason the
    // regions are: where the land stops is a design decision, not a dice roll.
    //
    // Land is the UNION of the capsules below - a signed distance field. Everything
    // outside them is sea, shelving away from the shore. Capsules rather than circles so
    // a landmass can be drawn as a spine rather than assembled out of blobs, and a union
    // of signed distances rather than a heightmap so two lobes that overlap become one
    // coastline with a bay in it instead of two circles with a seam.
    //
    // WHY IT IS CURRENTLY ONE ISLAND, which is not what was asked for:
    //
    // Every site the generator places has to be on land, and the world report measures how
    // far each region's sites reach from its own centre. The answer is much further than
    // the region radius, because WorldMap throws a region's wreck field 1.3 to 2.6 radii
    // out and then scatters it 560 m around that - Fenmoor's wrecks land 3.7 km from
    // Fenmoor, Ashmount's 3.5 km from Ashmount, and both of them end up nearer The Pan
    // than their own region. Clustered sites alone need about 0.66 R + 1100 m of land.
    //
    // Work that through against WorldMap's region centres and every adjacent pair overlaps:
    // the closest pairs are 2.4 to 3.7 km apart and each needs a 2.0 to 2.6 km lobe. There
    // is no cut anywhere in the current layout that leaves a channel wider than a few
    // hundred metres, which is inside the aircraft's 2:1 glide from 500 m and therefore
    // not a crossing at all. Splitting them anyway would put sites in the sea, and site
    // shortfalls are the one number that has to stay at zero.
    //
    // So: the machinery is the archipelago, the table is one island with deep bays, and
    // the report carries the exact WorldMap.BuildRegions() layout that makes the gaps real
    // along with the table that goes with it. Moving to it is a data change to both.
    private readonly record struct Lobe(Vector2 A, Vector2 B, float R);

    private static readonly Lobe[] Land =
    {
        // Each region's own ground. Radii are sized from the measured reach of the sites
        // each region actually generates, plus enough margin that a site on the edge gets
        // its graded pad on dry land rather than half down the beach.
        new(new Vector2(  200,  1100), new Vector2(  200,  1100), 2400),  // The Pan
        new(new Vector2(-3100,  -500), new Vector2(-3100,  -500), 2450),  // Long Acre
        new(new Vector2( 3000, -1400), new Vector2( 3000, -1400), 2350),  // Fenmoor
        new(new Vector2(-1600,  4700), new Vector2(-1600,  4700), 2350),  // The Drowning
        new(new Vector2( 1500, -4400), new Vector2( 1500, -4400), 2550),  // Cold Shoulder
        new(new Vector2(-4800, -3300), new Vector2(-4800, -3300), 2650),  // Sawtooth Works
        new(new Vector2( 4600,  3100), new Vector2( 4600,  3100), 2600),  // Ashmount
        new(new Vector2(-4000,  4300), new Vector2(-4000,  4300), 2250),  // The Scald
    };

    /// <summary>How much the shoreline wanders in or out, as a fraction of a lobe radius.</summary>
    private const float CoastWobble = 0.085f;

    /// <summary>Metres of shore over which the land comes down to the water: a cliff...</summary>
    private const float CliffSurf = 130f;
    /// <summary>...and a strand.</summary>
    private const float BeachSurf = 900f;

    private const float ShelfDepth = 11f;      // the shallows, close in
    private const float ShelfScale = 240f;
    private const float AbyssDepth = 64f;      // and the long slope away from them
    private const float AbyssScale = 2600f;

    /// <summary>
    /// How much longer the basin is north-south than east-west.
    ///
    /// A drowned VALLEY, not a drowned crater. Round would have been simpler and is wrong
    /// twice over: it is not what flooded ground looks like, and it forces a choice the
    /// region cannot afford. The wetland's own plan asks for a relay mast and an overlook,
    /// both of which need ground above 110 m within 2.4 km of the same centre, and it asks
    /// for a wreck field which the site generator scatters 1.8-3.6 km OUT from that centre
    /// - so the water has to reach a long way in one direction while leaving hills
    /// standing in another. A long basin does that; a big round one drowns the mast.
    /// </summary>
    private const float BasinStretchZ = 1.45f;

    /// <summary>Step for the numerical gradient of the drainage field, metres.</summary>
    private const float GradientStep = 30f;

    /// <summary>
    /// How strongly the drowned basin claims this point, 0 outside to 1 in the middle.
    ///
    /// The outline is perturbed by the warp field and by the hill field, both of which are
    /// already being sampled, so an organic shoreline costs nothing. A circle of water
    /// would be the single most obviously generated thing in the world.
    /// </summary>
    private static float BasinAt(float x, float z, float wx, float wz, float hills, float dC)
    {
        float dx = x + wx * 0.30f - BasinCentre.X;
        float dz = (z + wz * 0.30f - BasinCentre.Y) / BasinStretchZ;
        float d = Mathf.Sqrt(dx * dx + dz * dz) + (hills - 0.5f) * 900f;

        // The flood reaches up the rivers that feed it. Widening the basin instead would
        // have worked too, and would have drowned the high ground the region's relay mast
        // and overlook have to stand on - both of which are placed inside 2.4 km of the
        // same centre. Reaching along the drainage puts the water where water goes and
        // leaves the hills between the arms alone.
        d -= BasinReach * Section(dC, PlainHalfWidth * 1.8f);
        return 1f - Mathf.SmoothStep(BasinInner, BasinOuter, d);
    }

    /// <summary>
    /// Distance to the nearest drainage line, metres.
    ///
    /// The drainage network is the zero set of the valley field, and the distance to it is
    /// the field value divided by the field's gradient - the standard first-order estimate,
    /// and the reason it is worth two extra noise samples. Banding on the raw value instead
    /// gives channels whose width depends on how fast the noise happens to be changing,
    /// which means a river that swells to half a kilometre wherever the field flattens out.
    /// A river you can follow at fifty feet has to have a width you chose.
    ///
    /// The gradient is taken in the warped coordinates the field is sampled in rather than
    /// in world space, which leaves the warp's own stretch in. That is deliberate: it costs
    /// two more samples to remove and what it produces is a river that widens and pinches
    /// along its length, which is what rivers do.
    /// </summary>
    private static float ChannelDistance(float rx, float rz, float rRaw)
    {
        float dvx = (Rivers.GetNoise2D(rx + GradientStep, rz) - rRaw) / GradientStep;
        float dvz = (Rivers.GetNoise2D(rx, rz + GradientStep) - rRaw) / GradientStep;
        float grad = Mathf.Sqrt(dvx * dvx + dvz * dvz);
        return grad < 1e-7f ? float.MaxValue : Mathf.Abs(rRaw) / grad;
    }

    /// <summary>Distance from a point to a capsule's spine.</summary>
    private static float SpineDistance(float px, float pz, in Lobe l)
    {
        float ax = l.B.X - l.A.X, az = l.B.Y - l.A.Y;
        float qx = px - l.A.X, qz = pz - l.A.Y;
        float len2 = ax * ax + az * az;
        float t = len2 > 1e-4f ? Mathf.Clamp((qx * ax + qz * az) / len2, 0f, 1f) : 0f;
        float dx = qx - ax * t, dz = qz - az * t;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Signed distance to the coastline, metres: negative on land, positive at sea.
    ///
    /// The union of the land lobes, which for signed distances is the minimum. The wobble
    /// scales every lobe's radius together, so the shoreline breathes in and out along its
    /// whole length instead of each lobe wobbling independently and pulling the joins
    /// apart - the joins are where the bays and the isthmuses are, and they have to be
    /// stable or a peninsula becomes an island depending on the noise.
    /// </summary>
    private static float ShoreDistance(float x, float z, float wobble)
    {
        float best = float.MaxValue;
        foreach (Lobe l in Land)
        {
            float d = SpineDistance(x, z, l) - l.R * (1f + wobble);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>
    /// Cross-section profile: 1 on the centreline, 0 at the given half-width, smooth at
    /// both ends. Smooth at the centre gives a flat bottom you could put an aircraft on;
    /// smooth at the edge means the bank meets the hillside without a crease.
    /// </summary>
    private static float Section(float distance, float halfWidth)
    {
        float t = 1f - Mathf.Min(1f, distance / halfWidth);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Terrain height at a world XZ position, metres.
    ///
    /// Four layers, and the order matters:
    ///
    ///   CONTINENT decides where the land is high and where the basins are.
    ///   RIDGES are ridged noise, which creases rather than blobs, and they only appear
    ///     where the continent is already high - so mountains sit on uplands and the
    ///     lowlands stay flat enough to land on and to build a town on.
    ///   VALLEYS are carved downward through everything, following their own warped
    ///     network. Cutting valleys out is what produces terrain a helicopter can fly
    ///     *through* rather than merely over, and it is the single biggest difference
    ///     between a landscape and a lumpy plain.
    ///   DETAIL is small, and mostly exists so the surface is not glassy up close.
    ///
    /// An earlier version flattened the basins so hard that the whole map came out at a
    /// median slope of 2.9 degrees. Measured with the world report, that is a prairie: no
    /// dead ground to hide in, no masking, nothing to look at, and every landing site
    /// identical. Relief is not decoration here - it is the terrain-masking mechanic that
    /// D-010 depends on.
    /// </summary>
    public static float RawAt(float x, float z)
    {
        float wx = Warp.GetNoise2D(x, z) * 1100f;
        float wz = Warp.GetNoise2D(x + 4000f, z - 2500f) * 1100f;

        float continent = Continent.GetNoise2D(x, z) * 0.5f + 0.5f;          // 0..1
        continent = Mathf.Pow(Mathf.Clamp(continent, 0f, 1f), 1.15f);

        float hills = Hills.GetNoise2D(x + wx, z + wz) * 0.5f + 0.5f;

        float ridge = 1.0f - Mathf.Abs(Ridges.GetNoise2D(x + wx * 0.6f, z + wz * 0.6f));
        ridge = Mathf.Pow(ridge, 2.6f);

        float detail = Detail.GetNoise2D(x, z);

        // The drainage network, sampled early because the basin below reaches up it. Its
        // zero set is the line every river in the world runs along; dC is how far this
        // point is from that line, in metres.
        float vx = x + wx * 0.35f, vz = z + wz * 0.35f;
        float vRaw = Valleys.GetNoise2D(vx, vz);
        float rx = x + wx * 0.60f, rz = z + wz * 0.60f;
        float dC = ChannelDistance(rx, rz, Rivers.GetNoise2D(rx, rz));

        // --- The Drowning ------------------------------------------------------
        // A hole in the CONTINENTAL field, not a hole dug in the finished terrain.
        //
        // Digging a crater would have left a rim, and the rim would have been the most
        // visible thing about a region that is supposed to be the least visible thing in
        // the world. Pushing the continental value down instead takes the relief with it,
        // because every other term here is already scaled by that value: no hills, no
        // ridges, almost no gullies, and the upland field goes to zero. Low and flat and
        // wet is not three changes, it is one.
        float basin = BasinAt(x, z, wx, wz, hills, dC);
        continent = Mathf.Lerp(continent, Mathf.Min(continent, BasinFloor), basin);

        // The important structural choice: relief is a strong function of the continental
        // field, not a constant. Lowlands are genuinely flat - flat enough to put a town
        // or a runway on - and uplands are genuinely steep. Scaling everything uniformly
        // gives either a prairie or a mountain range, and the first two attempts here
        // produced exactly one of each.
        // Bimodal, not gradual. With a plain power curve the median of the continental
        // field sits mid-range, so "somewhat upland" describes most of the map and every
        // basin gets carved up too. A smoothstep gives genuinely flat low country and
        // genuinely broken high country, with a transition between them - which is the
        // "cities, wilderness and everything in between" the brief asked for, and also
        // the trade the threat system needs: the easy country to fly in is the exposed one.
        float upland = Mathf.SmoothStep(0.34f, 0.72f, continent);
        float h = continent * 300f
                + hills * hills * 280f * upland
                + ridge * 400f * Mathf.Max(0f, continent - 0.30f)
                // Extra hummock inside the basin. With the continental value pushed down
                // the wetland has no other relief at all, and a dead-flat sheet is not a
                // marsh - a marsh is reed banks and sand bars with water between them, and
                // those few metres are the difference between islands and a swimming pool.
                + detail * (5.0f + basin * 3.5f);

        // --- Carve the valleys ------------------------------------------------
        // A narrow band around the zero crossing of the valley field becomes a cut. The
        // power shapes the cross-section: high exponent gives a V, low gives a bowl.
        // Narrow and deep, not broad and shallow. The threat-coverage report measured the
        // first version at 96-100% visibility from 150 m, which made terrain masking - the
        // mechanic the entire world-scale argument rests on - into decoration. A valley
        // only hides an aircraft if it is deep relative to its width.
        float vBand = 1.0f - Mathf.Min(1.0f, Mathf.Abs(vRaw) / 0.17f);
        float cut = Mathf.Pow(vBand, 1.35f) * (10f + upland * 265f);
        h -= cut;

        // Gullies: tighter, shallower, and everywhere. These are what turn a smooth
        // hillside into ground a pilot can actually use.
        float gRaw = Gullies.GetNoise2D(x + wx * 0.2f, z + wz * 0.2f);
        float gBand = 1.0f - Mathf.Min(1.0f, Mathf.Abs(gRaw) / 0.19f);
        // Scaled almost entirely by the upland field, so the basins stay flat enough to
        // land on and build in. The consequence is a real trade the player will feel: the
        // easy country to operate in is the country with nowhere to hide.
        h -= Mathf.Pow(gBand, 1.5f) * (3f + upland * 88f);

        // Floor the deepest cuts into flat valley bottoms rather than knife edges: that
        // is where the rivers, the roads and the places people live all end up, and a
        // pilot needs somewhere the ground is level.
        float floorAt = 22f;
        if (h < floorAt) h = floorAt - (floorAt - h) * 0.22f;

        h -= 30f;

        // --- Make the drainage drain -------------------------------------------
        //
        // Everything above this line produces a landscape whose lowest ground, measured,
        // sat 112 m above the waterline. The valley field was carving a shape that LOOKED
        // like drainage from the air and carried no water anywhere, which meant a region
        // called The Drowning was dry, the story beat with an aircraft "half in the water"
        // had nowhere to happen, and a pilot had no rivers to navigate by.
        //
        // The fix is not a water plane laid over the terrain. It is to give the drainage a
        // BASE LEVEL and let it cut down to it, which is what real drainage does. The
        // whole world shares one water level - the streamer draws water as a flat quad at
        // exactly WaterLevel, so it has to - and that single constraint decides everything
        // below: a channel holds water where its bed is under that level and is a dry
        // gorge where it is not. Which country gets which is the continental field's
        // business, so the low country floods and the high country gets canyons, and the
        // transition between them is a river you can follow downhill until it becomes one.
        float lowland = 1f - Mathf.SmoothStep(LowlandStart, LowlandEnd, continent);
        float marsh = 1f - Mathf.SmoothStep(0.14f, 0.30f, continent);

        // The reference surface the terraces are cut DOWN TO. It is the continental field
        // alone - no hills, no ridges, no gullies - which makes it very smooth, and that
        // is the entire point: cutting to a smooth surface leaves a FLAT floor, whereas
        // subtracting a bump-shaped dent from the hillside just tilts the hillside.
        //
        // The first attempt did the latter and the world report said so immediately:
        // ground flat enough for a settlement fell from 18.8% of the map to 9.7% and
        // ground flat enough for a runway from 5.4% to 2.8%, because every river in the
        // world had put a slope where there used to be a plain. Valley floors are the
        // flattest ground in real country, not the least flat.
        float datum = continent * 300f - 30f;

        // THE TERRACE. Just under a kilometre across, cut to a flat floor about sixty
        // metres below the surrounding country and held well clear of the waterline, so
        // what it produces is dry level ground beside the river - which is exactly where
        // people build, and the reason the placement rules find MORE room after this
        // change rather than less. Gated on the lowland field so the uplands, whose
        // valleys are already tuned, are not touched at all.
        // Where the country is at its lowest the terrace floor itself goes under, and the
        // river stops being a river and becomes a chain of lakes. This is where most of
        // the standing water in the world is: a 900 m wide flat floor holds far more of it
        // than a 100 m channel ever could, and a lake is the landmark a pilot actually
        // navigates by.
        float terraceFloor = Mathf.Lerp(WaterLevel + TerraceClearance, WaterLevel - 9f,
                                        Mathf.SmoothStep(0.50f, 0.90f, lowland));
        float terrace = Mathf.Max(terraceFloor, datum - Mathf.Lerp(TerraceDrop, 105f, lowland));
        h = Mathf.Lerp(h, Mathf.Min(h, terrace), Section(dC, TerraceHalfWidth) * lowland);

        // THE FLOOD PLAIN. The wet ground either side of the channel. The clamp is
        // perturbed by the detail field rather than being a constant, so the plain is not
        // a machined surface: parts of it dip under the water and become marsh, which is
        // what gives a river an edge instead of an outline.
        float plain = Mathf.Max(WaterLevel + 1f + detail * 2.5f, datum - PlainDrop);
        h = Mathf.Lerp(h, Mathf.Min(h, plain), Section(dC, PlainHalfWidth) * lowland);

        // THE CHANNEL. About a hundred metres across, which is the narrowest a river can
        // be and still survive the streamer's coarse levels of detail - past two km the
        // terrain is sampled every 32 m, and a channel thinner than this is simply not
        // there when you look at it from height.
        //
        // Unlike the two above, this one is NOT gated on the lowland field. It runs the
        // whole network, everywhere, and its depth is relative rather than absolute: in
        // the high country it is a dry wash a few metres into the valley floor, and it
        // deepens as the country falls until its bed passes under the waterline and it
        // starts carrying water. That continuity is the navigation aid - follow the dry
        // line downhill and it turns into a river.
        float channel = Mathf.Max(WaterLevel - 6f + detail * 1.5f,
                                  h - Mathf.Lerp(ChannelDryDepth, ChannelWetDepth, lowland));
        h = Mathf.Lerp(h, Mathf.Min(h, channel), Section(dC, ChannelHalfWidth));

        // BRAIDING, in the wettest country only. One channel through a wetland is a canal;
        // what makes The Drowning read as drowned is the second, finer network cutting
        // across the first, so the ground becomes islands rather than banks. Rides on the
        // gully field, which is already sampled, and is gated hard on the continental
        // value so it cannot turn the rest of the map into a swamp.
        if (marsh > 0.002f)
        {
            float braid = Mathf.Min(1f, Mathf.Abs(gRaw) / 0.175f);
            braid = 1f - braid;
            braid = braid * braid * (3f - 2f * braid);
            h = Mathf.Lerp(h, Mathf.Min(h, WaterLevel - 5.0f + detail * 2.0f), braid * marsh);
        }

        // --- The coast ----------------------------------------------------------
        //
        // Last, and overriding: past the shoreline nothing above matters, because there is
        // nothing there but sea floor.
        //
        // The width of the shore is driven by the hill field, so the coast is beaches
        // where the country behind it is soft and bluffs where it is not, rather than the
        // same ramp all the way round - and because the hill field runs at a two-kilometre
        // scale, those alternate in stretches you could name.
        float shore = ShoreDistance(x, z, (wx + wz) / 2200f * CoastWobble);
        float surf = Mathf.Lerp(CliffSurf, BeachSurf, hills);
        if (shore < -surf) return h;

        // The bed: a shelf close in, then a long slope away. Both are saturating curves
        // rather than straight lines, which is the shape a real shelf has and, more
        // usefully here, one that never runs away however far out to sea you go.
        float s = Mathf.Max(0f, shore);
        float seabed = WaterLevel
                     - ShelfDepth * s / (s + ShelfScale)
                     - AbyssDepth * s / (s + AbyssScale)
                     // Bars and runnels in the surf line, fading out as it deepens. Without
                     // this the waterline is a drawn curve; with it, it is a beach.
                     + detail * 2.2f * Mathf.Max(0f, 1f - s / 300f);

        return Mathf.Lerp(h, seabed, Mathf.SmoothStep(-surf, 60f, shore));
    }

    /// <summary>
    /// Terrain height including the graded pads under every named place.
    ///
    /// Everything except site placement itself calls this. Placement uses
    /// <see cref="RawAt"/>, because the pads are defined in terms of where the sites are
    /// and asking this function during placement would be circular.
    /// </summary>
    public static float At(float x, float z) => SitePads.Apply(x, z, RawAt(x, z));

    /// <summary>Surface normal by central difference.</summary>
    public static Vector3 NormalAt(float x, float z, float e = 2.0f)
    {
        float hL = At(x - e, z), hR = At(x + e, z);
        float hD = At(x, z - e), hU = At(x, z + e);
        return new Vector3(hL - hR, 2.0f * e, hD - hU).Normalized();
    }

    /// <summary>Steepness, 0 = flat, 1 = vertical.</summary>
    public static float SlopeAt(float x, float z) => 1.0f - NormalAt(x, z).Y;

    /// <summary>
    /// Distance from a point to the nearest drainage line, metres. Report-only.
    ///
    /// The three cross-section widths above are only meaningful against the spacing of the
    /// network they are cut into: a terrace half a kilometre wide is a river terrace if the
    /// rivers are three kilometres apart and is the whole landscape if they are one. This
    /// is how that spacing gets measured instead of assumed.
    /// </summary>
    public static float DrainageDistance(float x, float z)
    {
        float wx = Warp.GetNoise2D(x, z) * 1100f;
        float wz = Warp.GetNoise2D(x + 4000f, z - 2500f) * 1100f;
        float rx = x + wx * 0.60f, rz = z + wz * 0.60f;
        return ChannelDistance(rx, rz, Rivers.GetNoise2D(rx, rz));
    }

    /// <summary>
    /// Does the authored basin still sit under the region it was authored for?
    ///
    /// <see cref="BasinCentre"/> is a copy of WorldMap's Wetland region centre, and a copy
    /// is a bug waiting for someone to move the original. The world report prints this
    /// line every run so the drift is loud on the day it happens rather than on the day a
    /// story beat stops binding.
    /// </summary>
    public static string BasinCheck(Vector2 wetlandCentre)
    {
        float d = BasinCentre.DistanceTo(wetlandCentre);
        return d < 1f
            ? $"basin check: the drowned basin is under the wetland region, at ({BasinCentre.X:F0}, {BasinCentre.Y:F0})."
            : $"*** BASIN DRIFT: WorldHeight.BasinCentre ({BasinCentre.X:F0}, {BasinCentre.Y:F0}) is {d:F0} m " +
              $"from the Wetland region centre ({wetlandCentre.X:F0}, {wetlandCentre.Y:F0}). " +
              "The water and the region have come apart - move BasinCentre to match. ***";
    }

    /// <summary>
    /// True if the position is inside the authored world bounds - meaning the LAND.
    ///
    /// Deliberately not <see cref="WorldHalfExtent"/> any more. The two callers are the
    /// prop scatter and the threat survey, and neither has anything to put in 250 km of
    /// ocean: the scatter would spend its whole per-chunk budget rejecting sea floor, and
    /// the threat survey would average its terrain-masking figure over open water where
    /// there is by definition no masking. Both want "the island", and this is it.
    /// </summary>
    public static bool InBounds(float x, float z) =>
        Mathf.Abs(x) < IslandHalfExtent && Mathf.Abs(z) < IslandHalfExtent;

    // ------------------------------------------------------- is there water here?
    //
    // The world drains to one level, so every water question reduces to a height query.
    // These exist so that nothing else has to know that: a slung bucket, a ditching check,
    // a "can I land here" test and a HUD all want different answers out of the same fact,
    // and all four of them getting it from WaterLevel by hand is how two systems end up
    // disagreeing about where the sea is.

    /// <summary>Depth of standing water at a point, metres. Zero or less means dry land.</summary>
    public static float WaterDepthAt(float x, float z) => WaterLevel - At(x, z);

    /// <summary>True if there is standing water here at all.</summary>
    public static bool IsWater(float x, float z) => At(x, z) < WaterLevel;

    /// <summary>
    /// Whether a slung bucket could actually be filled here.
    ///
    /// A bucket needs water it can sink into, not a damp patch: a rotor-downwash-blasted
    /// two feet of marsh gives you mud and reeds. It also needs the aircraft to be clear
    /// of the bank, so the test is a small ring rather than a point - the same reason the
    /// landing assessment samples the skid footprint instead of one spot under the belly.
    /// </summary>
    public static bool CanDipBucket(float x, float z, float minDepth = 1.2f, float clearance = 9f)
    {
        if (WaterDepthAt(x, z) < minDepth) return false;
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f;
            if (WaterDepthAt(x + Mathf.Cos(a) * clearance, z + Mathf.Sin(a) * clearance) < minDepth * 0.5f)
                return false;
        }
        return true;
    }

    /// <summary>
    /// How far out to sea this position is, metres. Negative on land, and then it is how
    /// far you are from the nearest coast.
    ///
    /// This is the number a pilot over water needs, and per D-005a it is a FACT rather
    /// than an instruction: divided by ground speed it is minutes to the beach, which is
    /// the thing to hold the fuel gauge next to. It measures to the shoreline of the
    /// nearest landmass, which over open water is the one you would turn towards.
    /// </summary>
    public static float OffshoreDistance(float x, float z)
    {
        float wx = Warp.GetNoise2D(x, z) * 1100f;
        float wz = Warp.GetNoise2D(x + 4000f, z - 2500f) * 1100f;
        return ShoreDistance(x, z, (wx + wz) / 2200f * CoastWobble);
    }
}
