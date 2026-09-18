using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Rotorwash;

/// <summary>
/// Streams rocks, dead trees and scrub around the player.
///
/// Placement is a pure function of world position and seed, exactly like
/// <see cref="WorldHeight"/>, so a chunk that unloads and reloads produces an identical
/// landscape and nothing ever pops into a different arrangement. Instance transforms are
/// computed on worker threads; only the MultiMesh upload happens on the main thread.
///
/// Scrub is dense and short-ranged, trees are sparse and long-ranged, and rocks sit in
/// between - which matches both what the eye notices from a helicopter and what the GPU
/// can afford.
/// </summary>
public sealed partial class PropScatter : Node3D
{
    [Export] public float ChunkSize { get; set; } = 128f;
    [Export] public int Seed { get; set; } = 90210;

    /// <summary>Chunk radius for each prop class: scrub, rocks, trees.</summary>
    [Export] public int ScrubRadius { get; set; } = 3;
    [Export] public int RockRadius { get; set; } = 8;
    [Export] public int TreeRadius { get; set; } = 14;

    [Export] public float ScrubDensity { get; set; } = 0.022f;
    [Export] public float RockDensity { get; set; } = 0.0012f;
    [Export] public float TreeDensity { get; set; } = 0.00050f;

    [Export] public int MaxApplyPerFrame { get; set; } = 2;

    public Node3D? Target { get; set; }

    private enum PropKind { Scrub = 0, Rock = 1, Tree = 2 }

    private readonly List<Mesh> _rockMeshes = new();
    private readonly List<Mesh> _treeMeshes = new();
    private Mesh _scrubMesh = null!;
    private Material _rockMaterial = null!, _treeMaterial = null!, _scrubMaterial = null!;
    private FastNoiseLite _clumpNoise = null!;

    private readonly Dictionary<(Vector2I, PropKind), Node3D> _active = new();
    private readonly HashSet<(Vector2I, PropKind)> _pending = new();
    private readonly Queue<Built> _ready = new();
    private readonly object _lock = new();
    private Vector2I _lastCentre = new(int.MinValue, int.MinValue);

    private sealed class Built
    {
        public Vector2I Coord;
        public PropKind Kind;
        public int Variant;
        public Transform3D[] Transforms = Array.Empty<Transform3D>();
    }

    public override void _Ready()
    {
        for (int i = 0; i < 4; i++) _rockMeshes.Add(ProceduralProps.Rock(Seed + i * 31, 1.0f, 2));
        for (int i = 0; i < 5; i++) _treeMeshes.Add(ProceduralProps.DeadTree(Seed + 700 + i * 17, 9f));
        _scrubMesh = ProceduralProps.ScrubCard(1.8f, 1.15f, 3);

        _clumpNoise = new FastNoiseLite
        {
            Seed = Seed + 5,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.0014f,
            FractalOctaves = 3,
        };

        var rockTex = GD.Load<Texture2D>("res://assets/terrain/rock_col.jpg");
        var rockNrm = GD.Load<Texture2D>("res://assets/terrain/rock_nrm.jpg");
        _rockMaterial = new StandardMaterial3D
        {
            AlbedoTexture = rockTex,
            NormalEnabled = rockNrm is not null,
            NormalTexture = rockNrm,
            AlbedoColor = new Color(0.70f, 0.68f, 0.64f),
            Roughness = 0.95f,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.45f, 0.45f, 0.45f),
        };

        _treeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.27f, 0.245f, 0.215f),
            Roughness = 0.92f,
        };

        _scrubMaterial = new StandardMaterial3D
        {
            AlbedoTexture = ProceduralProps.ScrubTexture(256, Seed + 3),
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.36f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            Roughness = 1.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            BacklightEnabled = true,
            Backlight = new Color(0.34f, 0.31f, 0.20f),
            AlbedoColor = new Color(1.05f, 1.02f, 0.92f),
        };
    }

    public override void _Process(double delta)
    {
        if (Target is null) return;

        var centre = new Vector2I(
            Mathf.FloorToInt(Target.GlobalPosition.X / ChunkSize),
            Mathf.FloorToInt(Target.GlobalPosition.Z / ChunkSize));

        if (centre != _lastCentre)
        {
            _lastCentre = centre;
            Refresh(centre);
        }
        ApplyReady();
    }

    private void Refresh(Vector2I centre)
    {
        var wanted = new HashSet<(Vector2I, PropKind)>();
        Collect(wanted, centre, PropKind.Scrub, ScrubRadius);
        Collect(wanted, centre, PropKind.Rock, RockRadius);
        Collect(wanted, centre, PropKind.Tree, TreeRadius);

        var stale = new List<(Vector2I, PropKind)>();
        foreach (var key in _active.Keys) if (!wanted.Contains(key)) stale.Add(key);
        foreach (var key in stale) { _active[key].QueueFree(); _active.Remove(key); }

        var requests = new List<((Vector2I, PropKind) key, float dist)>();
        foreach (var key in wanted)
        {
            if (_active.ContainsKey(key) || _pending.Contains(key)) continue;
            float d = new Vector2(key.Item1.X - centre.X, key.Item1.Y - centre.Y).Length();
            requests.Add((key, d));
        }
        requests.Sort((a, b) => a.dist.CompareTo(b.dist));

        foreach (var (key, _) in requests)
        {
            _pending.Add(key);
            var k = key;
            Task.Run(() => BuildChunk(k.Item1, k.Item2));
        }
    }

    private void Collect(HashSet<(Vector2I, PropKind)> into, Vector2I centre, PropKind kind, int radius)
    {
        for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz > radius * radius) continue;
                into.Add((new Vector2I(centre.X + dx, centre.Y + dz), kind));
            }
    }

    private void BuildChunk(Vector2I coord, PropKind kind)
    {
        try
        {
            int variants = kind switch
            {
                PropKind.Rock => _rockMeshes.Count,
                PropKind.Tree => _treeMeshes.Count,
                _ => 1,
            };
            float density = kind switch
            {
                PropKind.Scrub => ScrubDensity,
                PropKind.Rock => RockDensity,
                _ => TreeDensity,
            } / variants;

            for (int variant = 0; variant < variants; variant++)
            {
                Transform3D[] transforms = Place(coord, kind, variant, density);
                if (transforms.Length == 0) continue;
                lock (_lock)
                {
                    _ready.Enqueue(new Built
                    {
                        Coord = coord, Kind = kind, Variant = variant, Transforms = transforms,
                    });
                }
            }

            // A chunk with nothing in it still has to clear its pending flag, or it will
            // be requested again on every single camera move.
            lock (_lock) { _ready.Enqueue(new Built { Coord = coord, Kind = kind, Variant = -1 }); }
        }
        catch (Exception e)
        {
            GD.PushError($"[scatter] {kind} chunk {coord} failed: {e.Message}");
            lock (_lock) { _pending.Remove((coord, kind)); }
        }
    }

    private Transform3D[] Place(Vector2I coord, PropKind kind, int variant, float density)
    {
        ulong seed = (ulong)HashCode.Combine(Seed, coord.X, coord.Y, (int)kind, variant);
        var rng = new RandomNumberGenerator { Seed = seed };

        int attempts = Mathf.RoundToInt(ChunkSize * ChunkSize * density);
        if (attempts <= 0) return Array.Empty<Transform3D>();

        var result = new List<Transform3D>(attempts);
        float ox = coord.X * ChunkSize, oz = coord.Y * ChunkSize;

        for (int i = 0; i < attempts; i++)
        {
            float lx = rng.RandfRange(0, ChunkSize), lz = rng.RandfRange(0, ChunkSize);
            float wx = ox + lx, wz = oz + lz;
            if (!WorldHeight.InBounds(wx, wz)) continue;

            float h = WorldHeight.At(wx, wz);
            Vector3 n = WorldHeight.NormalAt(wx, wz, 2.5f);
            float slope = n.Y;

            // Large-scale clumping: stands of trees, scree fields, bare ground. Uniform
            // scatter is the clearest possible tell that a world was generated.
            float clump = _clumpNoise.GetNoise2D(wx, wz) * 0.5f + 0.5f;

            bool keep = kind switch
            {
                PropKind.Scrub => slope > 0.80f && h < 210f && clump > 0.44f,
                PropKind.Rock => slope > 0.40f && (slope < 0.92f || clump > 0.62f),
                PropKind.Tree => slope > 0.88f && h < 180f && clump > 0.56f,
                _ => false,
            };
            if (!keep) continue;

            float scale = kind switch
            {
                PropKind.Scrub => rng.RandfRange(0.8f, 2.1f),
                PropKind.Rock => rng.RandfRange(0.4f, 3.0f),
                _ => rng.RandfRange(0.65f, 1.40f),
            };

            Basis basis;
            float sink = 0;
            if (kind == PropKind.Rock)
            {
                // Rocks follow the surface and settle into it. Trees stand upright even on
                // a slope, because trees grow toward the light, not along the ground.
                basis = AlignToNormal(n, rng.Randf() * Mathf.Tau).Scaled(Vector3.One * scale);
                sink = -scale * rng.RandfRange(0.15f, 0.45f);
            }
            else
            {
                basis = Basis.Identity.Rotated(Vector3.Up, rng.Randf() * Mathf.Tau)
                                      .Scaled(Vector3.One * scale);
            }

            result.Add(new Transform3D(basis, new Vector3(lx, h + sink, lz)));
        }

        return result.ToArray();
    }

    private void ApplyReady()
    {
        for (int applied = 0; applied < MaxApplyPerFrame;)
        {
            Built built;
            lock (_lock)
            {
                if (_ready.Count == 0) return;
                built = _ready.Dequeue();
            }

            var key = (built.Coord, built.Kind);

            if (built.Variant < 0)
            {
                _pending.Remove(key);
                continue;   // sentinel, not real work
            }

            if (!_active.TryGetValue(key, out Node3D? holder))
            {
                holder = new Node3D
                {
                    Name = $"{built.Kind}_{built.Coord.X}_{built.Coord.Y}",
                    Position = new Vector3(built.Coord.X * ChunkSize, 0, built.Coord.Y * ChunkSize),
                };
                AddChild(holder);
                _active[key] = holder;
            }

            (Mesh mesh, Material mat, float range) = built.Kind switch
            {
                PropKind.Scrub => (_scrubMesh, _scrubMaterial, 300f),
                PropKind.Rock => (_rockMeshes[built.Variant], _rockMaterial, 1400f),
                _ => (_treeMeshes[built.Variant], _treeMaterial, 2600f),
            };

            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = built.Transforms.Length,
            };
            for (int i = 0; i < built.Transforms.Length; i++) mm.SetInstanceTransform(i, built.Transforms[i]);

            holder.AddChild(new MultiMeshInstance3D
            {
                Name = $"v{built.Variant}",
                Multimesh = mm,
                MaterialOverride = mat,
                VisibilityRangeEnd = range,
                VisibilityRangeEndMargin = range * 0.12f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                CastShadow = built.Kind == PropKind.Scrub
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.On,
            });

            applied++;
        }
    }

    private static Basis AlignToNormal(Vector3 normal, float yaw)
    {
        Vector3 up = normal.Normalized();
        Vector3 reference = Mathf.Abs(up.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        Vector3 right = reference.Cross(up).Normalized();
        Vector3 forward = up.Cross(right);
        return new Basis(right, up, forward).Rotated(up, yaw);
    }

    public int ActiveGroups => _active.Count;
}
