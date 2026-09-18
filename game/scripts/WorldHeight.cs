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

    /// <summary>
    /// Terrain height at a world XZ position, metres.
    ///
    /// Shaped in three layers: a continental field that decides where the land is high and
    /// where the basins are, a hill field warped so it meanders, and ridged noise that only
    /// appears where the continental field is already high - so mountains sit on uplands
    /// and the lowlands stay flat enough to land on and build on.
    /// </summary>
    public static float At(float x, float z)
    {
        float wx = Warp.GetNoise2D(x, z) * 900f;
        float wz = Warp.GetNoise2D(x + 4000f, z - 2500f) * 900f;

        float continent = Continent.GetNoise2D(x, z) * 0.5f + 0.5f;          // 0..1
        continent = Mathf.Pow(Mathf.Clamp(continent, 0f, 1f), 1.35f);

        float hills = Hills.GetNoise2D(x + wx, z + wz) * 0.5f + 0.5f;

        // Ridged: 1 - |noise| makes creases rather than blobs, which is what reads as
        // eroded rock from the air.
        float ridge = 1.0f - Mathf.Abs(Ridges.GetNoise2D(x + wx * 0.6f, z + wz * 0.6f));
        ridge *= ridge;

        float detail = Detail.GetNoise2D(x, z);

        float h = continent * 260f
                + hills * hills * 145f * (0.35f + continent)
                + ridge * 190f * Mathf.Max(0f, continent - 0.34f)
                + detail * 4.5f;

        // Basins flatten out into valley floors: somewhere to land, somewhere to put a
        // town, and somewhere the eye can rest.
        float basin = Mathf.Clamp(1.0f - continent * 2.3f, 0f, 1f);
        h = Mathf.Lerp(h, h * 0.22f + 6f, basin * 0.8f);

        return h - 34f;
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
