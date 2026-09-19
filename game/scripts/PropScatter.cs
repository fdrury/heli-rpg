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
    [Export] public int GreenTreeRadius { get; set; } = 14;

    [Export] public float ScrubDensity { get; set; } = 0.022f;
    [Export] public float RockDensity { get; set; } = 0.0012f;
    [Export] public float TreeDensity { get; set; } = 0.00050f;
    [Export] public float GreenTreeDensity { get; set; } = 0.0035f;

    [Export] public int MaxApplyPerFrame { get; set; } = 2;

    public Node3D? Target { get; set; }

    private enum PropKind { Scrub = 0, Rock = 1, Tree = 2, GreenTree = 3 }

    private readonly List<Mesh> _rockMeshes = new();
    private readonly List<Mesh> _treeMeshes = new();
    private readonly List<Mesh> _greenTreeMeshes = new();
    private Mesh _scrubMesh = null!;
    private Material _rockMaterial = null!, _treeMaterial = null!, _scrubMaterial = null!;
    private Material _greenTreeMaterial = null!;
    private FastNoiseLite _clumpNoise = null!;
    private FastNoiseLite _forestNoise = null!;

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
        for (int i = 0; i < 6; i++) _greenTreeMeshes.Add(ProceduralProps.GreenTree(Seed + 2000 + i * 23));
        _scrubMesh = ProceduralProps.ScrubCard(1.05f, 0.72f, 3);

        _clumpNoise = new FastNoiseLite
        {
            Seed = Seed + 5,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.0014f,
            FractalOctaves = 3,
        };

        _forestNoise = new FastNoiseLite
        {
            Seed = Seed + 42,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.00065f,
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
            VertexColorUseAsAlbedo = true,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.45f, 0.45f, 0.45f),
        };

        _treeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.27f, 0.245f, 0.215f),
            Roughness = 0.92f,
            VertexColorUseAsAlbedo = true,
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
            Backlight = new Color(0.26f, 0.24f, 0.16f),
            AlbedoColor = new Color(0.94f, 0.93f, 0.88f),
        };

        _greenTreeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.20f, 0.12f),
            VertexColorUseAsAlbedo = true,
            Roughness = 0.92f,
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
        Collect(wanted, centre, PropKind.GreenTree, GreenTreeRadius);

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
                PropKind.GreenTree => _greenTreeMeshes.Count,
                _ => 1,
            };
            float density = kind switch
            {
                PropKind.Scrub => ScrubDensity,
                PropKind.Rock => RockDensity,
                PropKind.GreenTree => GreenTreeDensity,
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
        var rng = new ThreadRng(seed);

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
            float forest = _forestNoise.GetNoise2D(wx, wz) * 0.5f + 0.5f;

            bool keep = kind switch
            {
                PropKind.Scrub => slope > 0.80f && h > WorldHeight.WaterLevel && h < 210f && clump > 0.44f,
                // The only rule here that had no waterline test, which stopped mattering
                // the moment the world gained a sea: without it every ocean chunk spends
                // its whole scatter budget placing boulders on the sea bed, where they are
                // invisible under an opaque water surface and still cost a draw call.
                PropKind.Rock => h > WorldHeight.WaterLevel - 1f
                                 && slope > 0.40f && (slope < 0.92f || clump > 0.62f),
                PropKind.Tree => slope > 0.88f && h > WorldHeight.WaterLevel && h < 180f && clump > 0.56f,
                PropKind.GreenTree => slope > 0.72f && h > WorldHeight.WaterLevel && h < 240f && forest > 0.46f,
                _ => false,
            };
            if (!keep) continue;

            float scale = kind switch
            {
                PropKind.Scrub => rng.RandfRange(0.65f, 1.55f),
                PropKind.Rock => rng.RandfRange(0.4f, 3.0f),
                PropKind.GreenTree => rng.RandfRange(0.7f, 1.5f),
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

    /// <summary>
    /// A deterministic generator that is safe to construct off the main thread.
    ///
    /// This replaced Godot's <c>RandomNumberGenerator</c>, which is a Godot object: building
    /// one on a worker thread is not allowed, and it crashed the process outright with an
    /// AccessViolationException from deep inside the binding layer. Scatter is generated on
    /// worker threads by design, so it can never use a Godot type for this.
    ///
    /// The same rule already cost this project once, when <c>Godot.Collections.Array</c>
    /// was being built on the terrain worker. Worth stating as a rule: nothing that derives
    /// from GodotObject gets CONSTRUCTED on a worker thread.
    ///
    /// splitmix64, which is small, fast and has no state to share.
    /// </summary>
    private struct ThreadRng
    {
        private ulong _state;
        public ThreadRng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        private ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Randf() => (float)((Next() >> 11) * (1.0 / 9007199254740992.0));

        public float RandfRange(float from, float to) => from + Randf() * (to - from);
    }

    /// <summary>A stable per-instance tint, hashed from where the thing stands.</summary>
    private static Color TintFor(PropKind kind, Vector3 at)
    {
        float h1 = Frac(Mathf.Sin(at.X * 12.9898f + at.Z * 78.233f) * 43758.5453f);
        float h2 = Frac(Mathf.Sin(at.X * 39.3468f + at.Z * 11.1357f) * 24634.6345f);

        // Brightness varies more than hue. Real stands differ mostly in how much light
        // each plant is getting and how healthy it is, not in being different colours.
        float v = 0.74f + h1 * 0.52f;

        return kind switch
        {
            // Green things range from dark and healthy to bleached and half-dead, which in
            // this world is most of them.
            PropKind.GreenTree => new Color(v * (0.88f + h2 * 0.34f), v, v * (0.80f + h2 * 0.22f)),
            PropKind.Scrub => new Color(v * (1.0f + h2 * 0.18f), v, v * 0.88f),
            // Rock leans grey-to-ochre.
            PropKind.Rock => new Color(v * (0.94f + h2 * 0.20f), v, v * (0.92f - h2 * 0.10f)),
            // Dead wood is grey where it is weathered and browner where it is not.
            _ => new Color(v * (1.0f + h2 * 0.16f), v * (0.97f + h2 * 0.06f), v * 0.92f),
        };
    }

    private static float Frac(float v) => v - Mathf.Floor(v);

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
                PropKind.GreenTree => (_greenTreeMeshes[built.Variant], _greenTreeMaterial, 2200f),
                _ => (_treeMeshes[built.Variant], _treeMaterial, 2600f),
            };

            // Per-instance tint.
            //
            // A MultiMesh draws one mesh many times, so without this every tree of a given
            // variant is pixel-identical to every other - and with only a handful of
            // variants a hillside of them reads as a field of repeated dark blobs rather
            // than as woodland. Tint costs one colour per instance and breaks the
            // repetition more effectively than adding more meshes would.
            //
            // Derived by hashing the instance's own position, so it is stable across
            // reloads and needs nothing carried through the worker thread.
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                UseColors = true,
                InstanceCount = built.Transforms.Length,
            };
            for (int i = 0; i < built.Transforms.Length; i++)
            {
                mm.SetInstanceTransform(i, built.Transforms[i]);
                mm.SetInstanceColor(i, TintFor(built.Kind, built.Transforms[i].Origin));
            }

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
