using Godot;

namespace Rotorwash;

/// <summary>
/// Placeholder world: a single deterministic heightfield, generated from noise, used for
/// both the visible mesh and the collision shape and queried directly by the flight model
/// for height above ground.
///
/// This is explicitly temporary. The real world is a streamed, authored region (see
/// docs/wiki/03-world.md). What matters right now is that the same function answers
/// "how high is the ground here" for the renderer, the physics and the rotor's ground
/// effect calculation, so those three can never disagree.
/// </summary>
public sealed partial class Terrain : StaticBody3D
{
    /// <summary>Side length of the generated patch, metres.</summary>
    [Export] public float Extent { get; set; } = 3072f;

    /// <summary>Horizontal spacing between height samples, metres.</summary>
    [Export] public float CellSize { get; set; } = 8f;

    [Export] public float MaxHeight { get; set; } = 220f;
    [Export] public int Seed { get; set; } = 1977;

    private FastNoiseLite _base = null!;
    private FastNoiseLite _detail = null!;
    private FastNoiseLite _ridge = null!;

    public int Resolution => Mathf.RoundToInt(Extent / CellSize) + 1;

    public override void _Ready()
    {
        BuildNoise();
        Generate();
    }

    private void BuildNoise()
    {
        _base = new FastNoiseLite
        {
            Seed = Seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.00035f,
            FractalOctaves = 5,
            FractalLacunarity = 2.1f,
            FractalGain = 0.48f,
        };
        _ridge = new FastNoiseLite
        {
            Seed = Seed + 101,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.0011f,
            FractalOctaves = 4,
            FractalGain = 0.5f,
        };
        _detail = new FastNoiseLite
        {
            Seed = Seed + 977,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.010f,
            FractalOctaves = 3,
            FractalGain = 0.42f,
        };
    }

    /// <summary>
    /// Terrain height in metres at a Godot world XZ position. Deterministic, allocation
    /// free and cheap enough to call from the physics step every frame.
    /// </summary>
    public float HeightAt(float x, float z)
    {
        if (_base is null) BuildNoise();

        float b = _base!.GetNoise2D(x, z) * 0.5f + 0.5f;          // 0..1 rolling base
        float r = 1.0f - Mathf.Abs(_ridge!.GetNoise2D(x, z));      // ridged
        float d = _detail!.GetNoise2D(x, z);

        // Flatten the low ground into a valley floor so there is somewhere to land, and
        // let the ridges rise out of it. A helicopter game needs readable landing sites
        // far more than it needs uniformly interesting terrain.
        float shaped = Mathf.Pow(b, 1.8f);
        float h = shaped * MaxHeight + r * r * MaxHeight * 0.55f * shaped + d * 5.5f;
        return h - 18f;
    }

    /// <summary>Upward surface normal, by finite difference. Used for landing checks.</summary>
    public Vector3 NormalAt(float x, float z)
    {
        const float e = 2.0f;
        float hL = HeightAt(x - e, z), hR = HeightAt(x + e, z);
        float hD = HeightAt(x, z - e), hU = HeightAt(x, z + e);
        return new Vector3(hL - hR, 2.0f * e, hD - hU).Normalized();
    }

    private void Generate()
    {
        int n = Resolution;
        float half = Extent * 0.5f;

        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                float x = -half + i * CellSize;
                float z = -half + j * CellSize;
                heights[j * n + i] = HeightAt(x, z);
            }
        }

        AddChild(BuildMesh(heights, n, half));

        // HeightMapShape3D samples on a unit grid, so the collision body is scaled up to
        // the real cell size rather than the heightfield being resampled.
        var shape = new HeightMapShape3D
        {
            MapWidth = n,
            MapDepth = n,
            MapData = heights,
        };
        var col = new CollisionShape3D { Shape = shape };
        col.Scale = new Vector3(CellSize, 1, CellSize);
        AddChild(col);

        GD.Print($"[terrain] {Extent:F0} m patch, {n}x{n} samples at {CellSize:F1} m");
    }

    private MeshInstance3D BuildMesh(float[] heights, int n, float half)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int j = 0; j < n - 1; j++)
        {
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = -half + i * CellSize, x1 = x0 + CellSize;
                float z0 = -half + j * CellSize, z1 = z0 + CellSize;

                var a = new Vector3(x0, heights[j * n + i], z0);
                var b = new Vector3(x1, heights[j * n + i + 1], z0);
                var c = new Vector3(x1, heights[(j + 1) * n + i + 1], z1);
                var d = new Vector3(x0, heights[(j + 1) * n + i], z1);

                float uvScale = 0.06f;
                AddTri(st, a, b, c, uvScale);
                AddTri(st, a, c, d, uvScale);
            }
        }

        st.GenerateNormals();
        st.GenerateTangents();

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.32f, 0.25f),
            Roughness = 0.95f,
            Metallic = 0.0f,
        };
        st.SetMaterial(mat);

        return new MeshInstance3D { Mesh = st.Commit(), Name = "TerrainMesh" };
    }

    private static void AddTri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, float uv)
    {
        st.SetUV(new Vector2(a.X * uv, a.Z * uv)); st.AddVertex(a);
        st.SetUV(new Vector2(b.X * uv, b.Z * uv)); st.AddVertex(b);
        st.SetUV(new Vector2(c.X * uv, c.Z * uv)); st.AddVertex(c);
    }
}
