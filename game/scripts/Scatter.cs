using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Populates the terrain with rocks, dead trees and scrub.
///
/// Organised into fixed-size chunks, each holding one MultiMesh per prop type, so Godot
/// frustum-culls whole chunks for free and distance ranges can retire the small stuff
/// early. Scrub is invisible past a couple of hundred metres and there is no point paying
/// for it; a dead tree on a ridge line is worth drawing from a kilometre away.
///
/// Placement is deterministic from the world seed and derived from the same height
/// function the terrain and the flight model use, so a tree is always in the same place
/// and never floats or sinks.
/// </summary>
public sealed partial class Scatter : Node3D
{
    [Export] public NodePath TerrainPath { get; set; } = "../Terrain";
    [Export] public float ChunkSize { get; set; } = 128f;
    [Export] public float Radius { get; set; } = 1400f;
    [Export] public int Seed { get; set; } = 90210;

    /// <summary>Instances per square metre.</summary>
    [Export] public float ScrubDensity { get; set; } = 0.020f;
    [Export] public float RockDensity { get; set; } = 0.0011f;
    [Export] public float TreeDensity { get; set; } = 0.00045f;

    private Terrain _terrain = null!;
    private readonly List<Mesh> _rockMeshes = new();
    private readonly List<Mesh> _treeMeshes = new();
    private Mesh _scrubMesh = null!;

    private Material _rockMaterial = null!;
    private Material _treeMaterial = null!;
    private Material _scrubMaterial = null!;

    private FastNoiseLite _densityNoise = null!;

    public override void _Ready()
    {
        _terrain = GetNode<Terrain>(TerrainPath);
        BuildMeshes();
        BuildChunks();
    }

    private void BuildMeshes()
    {
        for (int i = 0; i < 4; i++) _rockMeshes.Add(ProceduralProps.Rock(Seed + i * 31, 1.0f, 2));
        for (int i = 0; i < 5; i++) _treeMeshes.Add(ProceduralProps.DeadTree(Seed + 700 + i * 17, 9f));
        _scrubMesh = ProceduralProps.ScrubCard(1.8f, 1.15f, 3);

        _densityNoise = new FastNoiseLite
        {
            Seed = Seed + 5,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.0016f,
            FractalOctaves = 3,
        };

        var rockTex = GD.Load<Texture2D>("res://assets/terrain/rock_col.jpg");
        var rockNrm = GD.Load<Texture2D>("res://assets/terrain/rock_nrm.jpg");
        _rockMaterial = new StandardMaterial3D
        {
            AlbedoTexture = rockTex,
            NormalEnabled = rockNrm is not null,
            NormalTexture = rockNrm,
            AlbedoColor = new Color(0.72f, 0.70f, 0.66f),
            Roughness = 0.95f,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.45f, 0.45f, 0.45f),
        };

        // Bare standing timber: grey, silvered by weather, not brown. Brown reads as alive.
        _treeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.26f, 0.235f, 0.205f),
            Roughness = 0.92f,
            Metallic = 0.0f,
        };

        _scrubMaterial = new StandardMaterial3D
        {
            AlbedoTexture = ProceduralProps.ScrubTexture(256, Seed + 3),
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.36f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            // Scrub is thin and backlit half the time; without this it reads as cardboard.
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            Roughness = 1.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
        };
    }

    private void BuildChunks()
    {
        int half = Mathf.CeilToInt(Radius / ChunkSize);
        int chunks = 0, scrub = 0, rocks = 0, trees = 0;

        for (int cz = -half; cz <= half; cz++)
        {
            for (int cx = -half; cx <= half; cx++)
            {
                var centre = new Vector2(cx * ChunkSize, cz * ChunkSize);
                if (centre.Length() > Radius) continue;

                var chunk = new Node3D { Name = $"Chunk_{cx}_{cz}" };
                chunk.Position = new Vector3(centre.X, 0, centre.Y);

                int s = Populate(chunk, cx, cz, _scrubMesh, _scrubMaterial, ScrubDensity,
                                 PropKind.Scrub, 260f);
                int r = PopulateVariants(chunk, cx, cz, _rockMeshes, _rockMaterial, RockDensity,
                                         PropKind.Rock, 1800f);
                int t = PopulateVariants(chunk, cx, cz, _treeMeshes, _treeMaterial, TreeDensity,
                                         PropKind.Tree, 2600f);

                if (s + r + t == 0) continue;
                AddChild(chunk);
                chunks++; scrub += s; rocks += r; trees += t;
            }
        }

        GD.Print($"[scatter] {chunks} chunks: {scrub} scrub, {rocks} rocks, {trees} dead trees");
    }

    private enum PropKind { Scrub, Rock, Tree }

    private int PopulateVariants(Node3D chunk, int cx, int cz, List<Mesh> meshes,
                                 Material material, float density, PropKind kind, float visibleTo)
    {
        int total = 0;
        for (int v = 0; v < meshes.Count; v++)
            total += Populate(chunk, cx, cz, meshes[v], material, density / meshes.Count,
                              kind, visibleTo, v);
        return total;
    }

    private int Populate(Node3D chunk, int cx, int cz, Mesh mesh, Material material,
                         float density, PropKind kind, float visibleTo, int variant = 0)
    {
        // Deterministic per chunk, per prop type, per variant: the same world always
        // generates the same landscape, which matters as soon as anything is authored
        // relative to it.
        ulong seed = (ulong)HashCode.Combine(Seed, cx, cz, (int)kind, variant);
        var rng = new RandomNumberGenerator { Seed = seed };

        int attempts = Mathf.RoundToInt(ChunkSize * ChunkSize * density);
        if (attempts <= 0) return 0;

        var transforms = new List<Transform3D>(attempts);
        float originX = cx * ChunkSize, originZ = cz * ChunkSize;

        for (int i = 0; i < attempts; i++)
        {
            float lx = rng.RandfRange(-ChunkSize * 0.5f, ChunkSize * 0.5f);
            float lz = rng.RandfRange(-ChunkSize * 0.5f, ChunkSize * 0.5f);
            float wx = originX + lx, wz = originZ + lz;

            float h = _terrain.HeightAt(wx, wz);
            Vector3 n = _terrain.NormalAt(wx, wz);
            float slope = n.Y;

            // Large-scale density variation: stands of trees, scree fields, bare ground.
            // Uniform scatter is the single clearest tell that a world is generated.
            float clump = _densityNoise.GetNoise2D(wx, wz) * 0.5f + 0.5f;

            bool keep = kind switch
            {
                // Scrub wants gentle, low ground and thins out on the tops.
                PropKind.Scrub => slope > 0.78f && h < 185f && clump > 0.46f,
                // Rocks collect on slopes and high ground.
                PropKind.Rock => slope > 0.42f && (slope < 0.90f || clump > 0.62f),
                // Trees want shelter: not steep, not high, and strongly clumped.
                PropKind.Tree => slope > 0.87f && h < 150f && clump > 0.56f,
                _ => false,
            };
            if (!keep) continue;

            var basis = Basis.Identity;
            basis = basis.Rotated(Vector3.Up, rng.Randf() * Mathf.Tau);

            float scale = kind switch
            {
                PropKind.Scrub => rng.RandfRange(0.8f, 2.1f),
                PropKind.Rock => rng.RandfRange(0.4f, 2.6f),
                PropKind.Tree => rng.RandfRange(0.65f, 1.35f),
                _ => 1f,
            };
            basis = basis.Scaled(Vector3.One * scale);

            // Rocks sit into the ground and follow the slope; trees stand upright even on
            // a hillside, because trees grow toward the light and not along the surface.
            float sink = 0;
            if (kind == PropKind.Rock)
            {
                basis = AlignToNormal(n, rng.Randf() * Mathf.Tau).Scaled(Vector3.One * scale);
                sink = -scale * rng.RandfRange(0.15f, 0.45f);
            }

            var local = new Vector3(lx, h + sink - 0f, lz);
            transforms.Add(new Transform3D(basis, local));
        }

        if (transforms.Count == 0) return 0;

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
        };
        for (int i = 0; i < transforms.Count; i++) mm.SetInstanceTransform(i, transforms[i]);

        var mmi = new MultiMeshInstance3D
        {
            Name = $"{kind}_{variant}",
            Multimesh = mm,
            MaterialOverride = material,
            VisibilityRangeEnd = visibleTo,
            VisibilityRangeEndMargin = visibleTo * 0.12f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            CastShadow = kind == PropKind.Scrub
                ? GeometryInstance3D.ShadowCastingSetting.Off
                : GeometryInstance3D.ShadowCastingSetting.On,
        };
        chunk.AddChild(mmi);
        return transforms.Count;
    }

    /// <summary>Basis whose up axis follows the surface normal, rotated about it by yaw.</summary>
    private static Basis AlignToNormal(Vector3 normal, float yaw)
    {
        Vector3 up = normal.Normalized();
        Vector3 reference = Mathf.Abs(up.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        Vector3 right = reference.Cross(up).Normalized();
        Vector3 forward = up.Cross(right);
        return new Basis(right, up, forward).Rotated(up, yaw);
    }
}
