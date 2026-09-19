using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Rotorwash;

/// <summary>
/// Streams the terrain as a ring of chunks around the player, at four levels of detail.
///
/// Why this shape:
///   * A helicopter at 60 kt crosses 512 m in seventeen seconds, and can see 8 km on a
///     clear day. The world therefore has to be both detailed underfoot and visible to the
///     horizon, which is exactly what a LOD ring is for.
///   * Chunk meshes are built on worker threads and only handed to the scene tree on the
///     main thread, a couple per frame, because a hitch while flying at 30 m is a crash.
///   * Collision is generated only for the chunks the aircraft could actually touch. A
///     heightfield collider for the whole ring would cost far more than it is worth.
///
/// Every chunk is a pure function of its coordinates and <see cref="WorldHeight"/>, so
/// nothing needs saving, chunks can be regenerated in any order, and two systems can never
/// disagree about where the ground is.
/// </summary>
public sealed partial class TerrainStreamer : Node3D
{
    /// <summary>Side length of one chunk, metres.</summary>
    [Export] public float ChunkSize { get; set; } = 512f;

    /// <summary>Vertices per side at the finest level of detail.</summary>
    [Export] public int BaseResolution { get; set; } = 65;

    /// <summary>Chunk radius for each LOD ring, in chunks.</summary>
    [Export] public int[] LodRings { get; set; } = { 2, 4, 7, 13 };

    /// <summary>Chunks within this radius get a collision shape.</summary>
    [Export] public int CollisionRadius { get; set; } = 2;

    /// <summary>Node the streaming follows. Usually the helicopter.</summary>
    public Node3D? Target { get; set; }

    private readonly Dictionary<Vector2I, Chunk> _active = new();
    private readonly HashSet<Vector2I> _pending = new();
    private readonly Queue<ChunkBuild> _ready = new();
    private readonly object _readyLock = new();

    private Material _terrainMaterial = null!;
    private Vector2I _lastCentre = new(int.MinValue, int.MinValue);
    private int _chunksBuilt;

    [Export] public int MaxApplyPerFrame { get; set; } = 2;
    [Export] public int MaxConcurrentBuilds { get; set; } = 4;


    private sealed class Chunk
    {
        public MeshInstance3D Mesh = null!;
        public StaticBody3D? Body;
        public int Lod = -1;
        public Node3D? Props;
    }

    private sealed class ChunkBuild
    {
        public Vector2I Coord;
        public int Lod;
        // Plain .NET arrays only. Godot.Collections.Array is an engine-owned object and
        // touching one from a worker thread crashes the process with an access violation
        // - reliably, but only sometimes visibly, which is the worst kind of bug.
        public Vector3[] Verts = Array.Empty<Vector3>();
        public Vector3[] Normals = Array.Empty<Vector3>();
        public Vector2[] Uvs = Array.Empty<Vector2>();
        public Color[] Colours = Array.Empty<Color>();
        public int[] Indices = Array.Empty<int>();
        public float[]? Heights;
        public int HeightRes;
        public Vector3[]? CollisionTris;
    }

    public override void _Ready()
    {
        _terrainMaterial = TerrainMaterial.Build();
    }

    public override void _Process(double delta)
    {
        if (Target is null) return;

        Vector2I centre = ToChunk(Target.GlobalPosition);
        if (centre != _lastCentre)
        {
            _lastCentre = centre;
            Refresh(centre);
        }

        ApplyReady();
    }

    private Vector2I ToChunk(Vector3 world) =>
        new(Mathf.FloorToInt(world.X / ChunkSize), Mathf.FloorToInt(world.Z / ChunkSize));

    /// <summary>Decide which chunks should exist at which LOD, and start or retire them.</summary>
    private void Refresh(Vector2I centre)
    {
        var wanted = new Dictionary<Vector2I, int>();
        int maxRing = LodRings[^1];

        for (int dz = -maxRing; dz <= maxRing; dz++)
        {
            for (int dx = -maxRing; dx <= maxRing; dx++)
            {
                var coord = new Vector2I(centre.X + dx, centre.Y + dz);
                int ring = Math.Max(Math.Abs(dx), Math.Abs(dz));

                int lod = -1;
                for (int i = 0; i < LodRings.Length; i++)
                {
                    if (ring <= LodRings[i]) { lod = i; break; }
                }
                if (lod < 0) continue;

                // Skip anything wholly outside the world.
                float cx = (coord.X + 0.5f) * ChunkSize, cz = (coord.Y + 0.5f) * ChunkSize;
                if (Mathf.Abs(cx) > WorldHeight.WorldHalfExtent + ChunkSize ||
                    Mathf.Abs(cz) > WorldHeight.WorldHalfExtent + ChunkSize) continue;

                wanted[coord] = lod;
            }
        }

        // Retire chunks that have left the ring.
        var toRemove = new List<Vector2I>();
        foreach (var kv in _active)
            if (!wanted.ContainsKey(kv.Key)) toRemove.Add(kv.Key);
        foreach (var coord in toRemove)
        {
            _active[coord].Mesh.QueueFree();
            _active[coord].Body?.QueueFree();
            _active[coord].Props?.QueueFree();
            _active.Remove(coord);
        }

        // Request anything missing or at the wrong LOD, nearest first so the ground under
        // the aircraft resolves before the horizon does.
        var requests = new List<(Vector2I coord, int lod, float dist)>();
        foreach (var kv in wanted)
        {
            if (_pending.Contains(kv.Key)) continue;
            if (_active.TryGetValue(kv.Key, out Chunk? existing) && existing.Lod == kv.Value) continue;
            float d = new Vector2(kv.Key.X - centre.X, kv.Key.Y - centre.Y).Length();
            requests.Add((kv.Key, kv.Value, d));
        }
        requests.Sort((a, b) => a.dist.CompareTo(b.dist));

        foreach (var (coord, lod, _) in requests)
        {
            _pending.Add(coord);
            Vector2I c = coord;
            int l = lod;
            bool needsCollision = Math.Max(Math.Abs(c.X - centre.X), Math.Abs(c.Y - centre.Y)) <= CollisionRadius;

            // A chunk has to know how detailed its neighbours are, because that is what
            // decides whether its edge has to be stitched down to match them.
            var neighbours = new int[4];
            neighbours[(int)Edge.North] = LodAt(new Vector2I(c.X, c.Y - 1), centre);
            neighbours[(int)Edge.South] = LodAt(new Vector2I(c.X, c.Y + 1), centre);
            neighbours[(int)Edge.West] = LodAt(new Vector2I(c.X - 1, c.Y), centre);
            neighbours[(int)Edge.East] = LodAt(new Vector2I(c.X + 1, c.Y), centre);

            Task.Run(() => BuildChunk(c, l, needsCollision, neighbours));
        }
    }

    /// <summary>Level of detail a chunk would be given, or the coarsest if out of range.</summary>
    private int LodAt(Vector2I coord, Vector2I centre)
    {
        int ring = Math.Max(Math.Abs(coord.X - centre.X), Math.Abs(coord.Y - centre.Y));
        for (int i = 0; i < LodRings.Length; i++)
            if (ring <= LodRings[i]) return i;
        return LodRings.Length - 1;
    }

    /// <summary>Worker-thread mesh generation. Touches no scene-tree state.</summary>
    private void BuildChunk(Vector2I coord, int lod, bool withCollision, int[] neighbourLods)
    {
        try
        {
            int res = Math.Max(5, ((BaseResolution - 1) >> lod) + 1);
            float cell = ChunkSize / (res - 1);
            float ox = coord.X * ChunkSize, oz = coord.Y * ChunkSize;

            var verts = new Vector3[res * res];
            var normals = new Vector3[res * res];
            var uvs = new Vector2[res * res];
            var colours = new Color[res * res];

            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float x = i * cell, z = j * cell;
                    float h = WorldHeight.At(ox + x, oz + z);
                    Vector3 nrm = WorldHeight.NormalAt(ox + x, oz + z, cell * 0.5f);
                    int idx = j * res + i;
                    verts[idx] = new Vector3(x, h, z);
                    normals[idx] = nrm;
                    uvs[idx] = new Vector2((ox + x) * 0.02f, (oz + z) * 0.02f);
                    colours[idx] = new Color(nrm.Y, 0f, 0f, 1f);
                }
            }

            // --- Stitch the edges to coarser neighbours -----------------------
            //
            // The honest fix for LOD cracks, and the one that costs nothing. Where this
            // chunk is finer than the chunk next to it, the neighbour draws a straight
            // line between vertices that this chunk has extra detail between - and the
            // gap between the two is the crack.
            //
            // So: collapse the extra vertices onto that straight line. The two meshes
            // then agree exactly along the shared edge and there is nothing to hide.
            //
            // The previous approach hung vertical skirts over the seam instead, and on
            // near-flat terrain a wall at every chunk boundary is visible for kilometres.
            // It drew a grid across the entire landscape. This draws nothing.
            for (int e = 0; e < 4; e++)
            {
                int step = 1 << Math.Max(0, neighbourLods[e] - lod);
                if (step <= 1) continue;

                for (int i = 0; i < res; i++)
                {
                    int rem = i % step;
                    if (rem == 0) continue;

                    int lo = i - rem, hi = Math.Min(lo + step, res - 1);
                    float t = (float)rem / step;

                    int idx = EdgeIndex((Edge)e, i, res);
                    int idxLo = EdgeIndex((Edge)e, lo, res);
                    int idxHi = EdgeIndex((Edge)e, hi, res);

                    Vector3 v = verts[idx];
                    v.Y = Mathf.Lerp(verts[idxLo].Y, verts[idxHi].Y, t);
                    verts[idx] = v;
                    normals[idx] = normals[idxLo].Lerp(normals[idxHi], t).Normalized();
                    colours[idx] = new Color(normals[idx].Y, 0f, 0f, 1f);
                }
            }

            var indices = new List<int>((res - 1) * (res - 1) * 6);
            for (int j = 0; j < res - 1; j++)
            {
                for (int i = 0; i < res - 1; i++)
                {
                    int a = j * res + i, b = a + 1, c2 = (j + 1) * res + i + 1, d = (j + 1) * res + i;
                    // Wound CLOCKWISE, which is what Godot treats as front-facing.
                    // Verified with --windingtest. The original order was the textbook
                    // counter-clockwise one and it inverted the lighting on every single
                    // surface in the game - the world looked muddy and dark for days and
                    // it was repeatedly mistaken for a palette problem.
                    indices.Add(a); indices.Add(c2); indices.Add(d);
                    indices.Add(a); indices.Add(b); indices.Add(c2);
                }
            }

            float[]? heights = null;
            Vector3[]? collisionTris = null;
            int heightRes = 0;
            if (withCollision)
            {
                // The collider is always built at a fixed resolution regardless of the
                // visual LOD, so the ground the skids touch never changes shape when the
                // mesh swaps detail level.
                heightRes = 65;
                heights = new float[heightRes * heightRes];
                float hcell = ChunkSize / (heightRes - 1);
                for (int j = 0; j < heightRes; j++)
                    for (int i = 0; i < heightRes; i++)
                        heights[j * heightRes + i] = WorldHeight.At(ox + i * hcell, oz + j * hcell);

                // A triangle soup at true world spacing, NOT a HeightMapShape3D.
                //
                // HeightMapShape3D samples one unit apart and the only way to widen that is
                // to scale the CollisionShape3D - here by (8, 1, 8), because chunks are
                // 512 m across and sampled 65 times. Godot tolerates that badly: RAYCASTS
                // against the scaled shape return correct hits at exactly the right height,
                // while bodies pass straight through it. That combination is what made this
                // so hard to see - every probe said the ground was there and correctly
                // placed, and the pilot fell through it anyway, accelerating until they
                // were 8.5 km below a helicopter they had been standing beside.
                //
                // A trimesh needs no scale, so there is nothing to get wrong. It costs
                // about 8k triangles per chunk for the two dozen chunks that carry
                // collision, which is a price worth paying for ground that is actually solid.
                int quads = heightRes - 1;
                collisionTris = new Vector3[quads * quads * 6];
                int tri = 0;
                for (int j = 0; j < quads; j++)
                {
                    for (int i = 0; i < quads; i++)
                    {
                        float x0 = i * hcell, x1 = (i + 1) * hcell;
                        float z0 = j * hcell, z1 = (j + 1) * hcell;
                        var v00 = new Vector3(x0, heights[j * heightRes + i], z0);
                        var v10 = new Vector3(x1, heights[j * heightRes + i + 1], z0);
                        var v11 = new Vector3(x1, heights[(j + 1) * heightRes + i + 1], z1);
                        var v01 = new Vector3(x0, heights[(j + 1) * heightRes + i], z1);
                        collisionTris[tri++] = v00; collisionTris[tri++] = v01; collisionTris[tri++] = v11;
                        collisionTris[tri++] = v00; collisionTris[tri++] = v11; collisionTris[tri++] = v10;
                    }
                }
            }

            lock (_readyLock)
            {
                _ready.Enqueue(new ChunkBuild
                {
                    Coord = coord, Lod = lod,
                    Verts = verts, Normals = normals, Uvs = uvs, Colours = colours,
                    Indices = indices.ToArray(),
                    Heights = heights, HeightRes = heightRes,
                    CollisionTris = collisionTris,
                });
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[terrain] chunk {coord} failed: {e.Message}");
            lock (_readyLock) { _pending.Remove(coord); }
        }
    }

    private enum Edge { North = 0, South = 1, West = 2, East = 3 }

    /// <summary>Index of the i-th vertex along one edge of a res x res grid.</summary>
    private static int EdgeIndex(Edge edge, int i, int res) => edge switch
    {
        Edge.North => i,                        // j = 0
        Edge.South => (res - 1) * res + i,      // j = res-1
        Edge.West => i * res,                   // i = 0
        _ => i * res + res - 1,                 // i = res-1
    };

    /// <summary>Hand a small number of finished chunks to the scene tree per frame.</summary>
    private void ApplyReady()
    {
        for (int applied = 0; applied < MaxApplyPerFrame; applied++)
        {
            ChunkBuild build;
            lock (_readyLock)
            {
                if (_ready.Count == 0) return;
                build = _ready.Dequeue();
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = build.Verts;
            arrays[(int)Mesh.ArrayType.Normal] = build.Normals;
            arrays[(int)Mesh.ArrayType.TexUV] = build.Uvs;
            arrays[(int)Mesh.ArrayType.Color] = build.Colours;
            arrays[(int)Mesh.ArrayType.Index] = build.Indices;

            var arrayMesh = new ArrayMesh();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            if (!_active.TryGetValue(build.Coord, out Chunk? chunk))
            {
                chunk = new Chunk();
                chunk.Mesh = new MeshInstance3D
                {
                    Name = $"Chunk_{build.Coord.X}_{build.Coord.Y}",
                    Position = new Vector3(build.Coord.X * ChunkSize, 0, build.Coord.Y * ChunkSize),
                    MaterialOverride = _terrainMaterial,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                };
                AddChild(chunk.Mesh);
                _active[build.Coord] = chunk;
            }

            chunk.Mesh.Mesh = arrayMesh;
            chunk.Lod = build.Lod;

            if (build.CollisionTris is not null && chunk.Body is null)
            {
                // Triangles are already in chunk-local space, so the body sits at the
                // chunk's CORNER and the collision shape carries no scale at all.
                var shape = new ConcavePolygonShape3D { Data = build.CollisionTris };
                var body = new StaticBody3D
                {
                    Name = $"Body_{build.Coord.X}_{build.Coord.Y}",
                    Position = new Vector3(build.Coord.X * ChunkSize, 0, build.Coord.Y * ChunkSize),
                };
                body.AddChild(new CollisionShape3D { Shape = shape });
                AddChild(body);
                chunk.Body = body;
            }

            _pending.Remove(build.Coord);
            _chunksBuilt++;

            ChunkReady?.Invoke(build.Coord, chunk.Mesh, build.Lod);
        }
    }

    /// <summary>Raised on the main thread when a chunk becomes live. Used by the scatter.</summary>
    public event Action<Vector2I, MeshInstance3D, int>? ChunkReady;

    public int ActiveChunks => _active.Count;
    public int PendingChunks => _pending.Count;
    public int ChunksBuilt => _chunksBuilt;
}
