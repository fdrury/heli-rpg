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

    /// <summary>Half-extent of the playable world, metres. The world is 2x this on a side.</summary>
    public const float WorldHalfExtent = 8192f;

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
    public static float At(float x, float z)
    {
        float wx = Warp.GetNoise2D(x, z) * 1100f;
        float wz = Warp.GetNoise2D(x + 4000f, z - 2500f) * 1100f;

        float continent = Continent.GetNoise2D(x, z) * 0.5f + 0.5f;          // 0..1
        continent = Mathf.Pow(Mathf.Clamp(continent, 0f, 1f), 1.15f);

        float hills = Hills.GetNoise2D(x + wx, z + wz) * 0.5f + 0.5f;

        float ridge = 1.0f - Mathf.Abs(Ridges.GetNoise2D(x + wx * 0.6f, z + wz * 0.6f));
        ridge = Mathf.Pow(ridge, 2.6f);

        float detail = Detail.GetNoise2D(x, z);

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
                + detail * 5.0f;

        // --- Carve the valleys ------------------------------------------------
        // A narrow band around the zero crossing of the valley field becomes a cut. The
        // power shapes the cross-section: high exponent gives a V, low gives a bowl.
        // Narrow and deep, not broad and shallow. The threat-coverage report measured the
        // first version at 96-100% visibility from 150 m, which made terrain masking - the
        // mechanic the entire world-scale argument rests on - into decoration. A valley
        // only hides an aircraft if it is deep relative to its width.
        float vRaw = Valleys.GetNoise2D(x + wx * 0.35f, z + wz * 0.35f);
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

        return h - 30f;
    }

    /// <summary>Surface normal by central difference.</summary>
    public static Vector3 NormalAt(float x, float z, float e = 2.0f)
    {
        float hL = At(x - e, z), hR = At(x + e, z);
        float hD = At(x, z - e), hU = At(x, z + e);
        return new Vector3(hL - hR, 2.0f * e, hD - hU).Normalized();
    }

    /// <summary>Steepness, 0 = flat, 1 = vertical.</summary>
    public static float SlopeAt(float x, float z) => 1.0f - NormalAt(x, z).Y;

    /// <summary>True if the position is inside the authored world bounds.</summary>
    public static bool InBounds(float x, float z) =>
        Mathf.Abs(x) < WorldHalfExtent && Mathf.Abs(z) < WorldHalfExtent;
}
