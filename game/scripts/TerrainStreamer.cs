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
        public Godot.Collections.Array Arrays = null!;
        public float[]? Heights;
        public int HeightRes;
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
            Task.Run(() => BuildChunk(c, l, needsCollision));
        }
    }

    /// <summary>Worker-thread mesh generation. Touches no scene-tree state.</summary>
    private void BuildChunk(Vector2I coord, int lod, bool withCollision)
    {
        try
        {
            int res = Math.Max(5, ((BaseResolution - 1) >> lod) + 1);
            float cell = ChunkSize / (res - 1);
            float ox = coord.X * ChunkSize, oz = coord.Y * ChunkSize;

            int skirtRing = res * 4;
            int vertCount = res * res + skirtRing;

            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            // Vertex colour carries two things the shader cannot work out for itself:
            //   R = the surface slope at this column, so a skirt blends exactly like the
            //       ground it hangs from instead of being read as a vertical rock face;
            //   G = a skirt flag, used to darken it into shadow.
            var colours = new Color[vertCount];

            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float x = i * cell, z = j * cell;
                    float h = WorldHeight.At(ox + x, oz + z);
                    int idx = j * res + i;
                    Vector3 nrm = WorldHeight.NormalAt(ox + x, oz + z, cell * 0.5f);
                    verts[idx] = new Vector3(x, h, z);
                    normals[idx] = nrm;
                    uvs[idx] = new Vector2((ox + x) * 0.02f, (oz + z) * 0.02f);
                    colours[idx] = new Color(nrm.Y, 0f, 0f, 1f);
                }
            }

            var indices = new List<int>((res - 1) * (res - 1) * 6 + skirtRing * 6);
            for (int j = 0; j < res - 1; j++)
            {
                for (int i = 0; i < res - 1; i++)
                {
                    int a = j * res + i, b = a + 1, c = (j + 1) * res + i + 1, d = (j + 1) * res + i;
                    indices.Add(a); indices.Add(d); indices.Add(c);
                    indices.Add(a); indices.Add(c); indices.Add(b);
                }
            }

            // Skirts: a vertical curtain dropped from every edge vertex. This is the
            // cheapest fix for the cracks that appear where two LODs meet, and from the
            // air the curtain is never visible.
            int s = res * res;
            AddSkirt(verts, normals, uvs, colours, indices, res, ref s, cell, ox, oz, Edge.North);
            AddSkirt(verts, normals, uvs, colours, indices, res, ref s, cell, ox, oz, Edge.South);
            AddSkirt(verts, normals, uvs, colours, indices, res, ref s, cell, ox, oz, Edge.West);
            AddSkirt(verts, normals, uvs, colours, indices, res, ref s, cell, ox, oz, Edge.East);

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Color] = colours;
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

            float[]? heights = null;
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
            }

            lock (_readyLock)
            {
                _ready.Enqueue(new ChunkBuild
                {
                    Coord = coord, Lod = lod, Arrays = arrays,
                    Heights = heights, HeightRes = heightRes,
                });
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[terrain] chunk {coord} failed: {e.Message}");
            lock (_readyLock) { _pending.Remove(coord); }
        }
    }

    private enum Edge { North, South, West, East }

    private static void AddSkirt(Vector3[] verts, Vector3[] normals, Vector2[] uvs, Color[] colours,
                                 List<int> indices, int res, ref int next,
                                 float cell, float originX, float originZ, Edge edge)
    {
        for (int i = 0; i < res; i++)
        {
            int top = edge switch
            {
                Edge.North => i,                        // j = 0
                Edge.South => (res - 1) * res + i,       // j = res-1
                Edge.West => i * res,                    // i = 0
                _ => i * res + res - 1,                  // i = res-1
            };

            // Depth comes from LOCAL RELIEF, not from cell size. The crack between two
            // levels of detail is about as deep as the height changes across one coarse
            // cell, which on flat ground is nothing. A fixed deep skirt instead puts a
            // wall at every chunk boundary, visible for kilometres from the air - which
            // is exactly what it looked like.
            Vector3 world = verts[top] + new Vector3(originX, 0, originZ);
            float h0 = verts[top].Y;
            float relief = 0f;
            for (int k = -2; k <= 2; k += 4)
            {
                relief = Math.Max(relief, Math.Abs(WorldHeight.At(world.X + cell * k, world.Z) - h0));
                relief = Math.Max(relief, Math.Abs(WorldHeight.At(world.X, world.Z + cell * k) - h0));
            }
            // On genuinely flat ground there is no crack, so the skirt collapses to
            // nothing and cannot be seen. Where there is relief it appears, and is tucked
            // back under the surface so it only shows through an actual gap rather than
            // catching a pixel of its own at grazing angles.
            float depth = Math.Clamp(relief * 1.6f, 0.0f, cell * 2.0f);
            Vector3 inward = edge switch
            {
                Edge.North => new Vector3(0, 0, cell * 0.30f),
                Edge.South => new Vector3(0, 0, -cell * 0.30f),
                Edge.West => new Vector3(cell * 0.30f, 0, 0),
                _ => new Vector3(-cell * 0.30f, 0, 0),
            };

            verts[next] = verts[top] + inward - new Vector3(0, depth + 0.15f, 0);
            normals[next] = normals[top];
            uvs[next] = uvs[top];
            colours[next] = new Color(colours[top].R, 1f, 0f, 1f);

            if (i > 0)
            {
                int prevTop = edge switch
                {
                    Edge.North => i - 1,
                    Edge.South => (res - 1) * res + i - 1,
                    Edge.West => (i - 1) * res,
                    _ => (i - 1) * res + res - 1,
                };
                int prevSkirt = next - 1;

                bool flip = edge is Edge.South or Edge.West;
                if (flip)
                {
                    indices.Add(prevTop); indices.Add(top); indices.Add(next);
                    indices.Add(prevTop); indices.Add(next); indices.Add(prevSkirt);
                }
                else
                {
                    indices.Add(prevTop); indices.Add(prevSkirt); indices.Add(next);
                    indices.Add(prevTop); indices.Add(next); indices.Add(top);
                }
            }
            next++;
        }
    }

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

            var arrayMesh = new ArrayMesh();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, build.Arrays);

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

            if (build.Heights is not null && chunk.Body is null)
            {
                var shape = new HeightMapShape3D
                {
                    MapWidth = build.HeightRes,
                    MapDepth = build.HeightRes,
                    MapData = build.Heights,
                };
                float hcell = ChunkSize / (build.HeightRes - 1);
                var body = new StaticBody3D
                {
                    Name = $"Body_{build.Coord.X}_{build.Coord.Y}",
                    // HeightMapShape3D centres itself on its origin, so the body sits at
                    // the middle of the chunk rather than its corner.
                    Position = new Vector3((build.Coord.X + 0.5f) * ChunkSize, 0,
                                           (build.Coord.Y + 0.5f) * ChunkSize),
                };
                var col = new CollisionShape3D { Shape = shape, Scale = new Vector3(hcell, 1, hcell) };
                body.AddChild(col);
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
