using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rotorwash;

/// <summary>
/// Road network connecting settlements. Thin dirt-track mesh strips that follow the
/// terrain between nearby sites.
///
/// The network is the old pre-collapse road system, now overgrown and barely visible.
/// Each site connects to its three nearest neighbours within range, which naturally
/// produces clusters inside regions and sparse links between them. Wrecks, relays and
/// overlooks get no roads because nobody drove to those.
///
/// All geometry is batched into one mesh so it costs one draw call.
/// </summary>
public sealed partial class Roads : Node3D
{
    private const float HalfWidth = 3.5f;
    private const float SampleStep = 12f;
    private const float MaxLength = 3500f;
    private const float VerticalOffset = 0.12f;

    public override void _Ready()
    {
        var sites = WorldMap.Sites;
        if (sites.Count == 0) return;

        // Road-worthy site kinds. Wrecks, relays and overlooks were accessed by air.
        bool Roaded(SiteKind k) => k is SiteKind.Settlement or SiteKind.Workshop
            or SiteKind.Airfield or SiteKind.Depot or SiteKind.FuelCache or SiteKind.Farmstead;

        var edges = new HashSet<(int, int)>();
        foreach (var site in sites)
        {
            if (!Roaded(site.Kind)) continue;

            var nearest = sites
                .Where(s => s.Id != site.Id && Roaded(s.Kind)
                         && s.Position.DistanceTo(site.Position) < MaxLength)
                .OrderBy(s => s.Position.DistanceSquaredTo(site.Position))
                .Take(3);

            foreach (var n in nearest)
                edges.Add((Math.Min(site.Id, n.Id), Math.Max(site.Id, n.Id)));
        }

        if (edges.Count == 0) return;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        foreach (var (a, b) in edges)
            AppendSegment(sites[a].Position, sites[b].Position, verts, normals, indices);

        if (verts.Count == 0) return;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        AddChild(new MeshInstance3D
        {
            Name = "RoadMesh",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.26f, 0.22f, 0.17f),
                Roughness = 0.95f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        GD.Print($"[roads] {edges.Count} roads, {verts.Count} verts");
    }

    private static void AppendSegment(Vector2 from, Vector2 to,
        List<Vector3> verts, List<Vector3> normals, List<int> indices)
    {
        var dir = to - from;
        float len = dir.Length();
        if (len < 1f) return;
        dir /= len;
        var perp = new Vector2(-dir.Y, dir.X) * HalfWidth;

        int samples = Math.Max(2, (int)(len / SampleStep) + 1);
        int baseIdx = verts.Count;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / (samples - 1);
            var p = from + dir * len * t;
            float h = WorldHeight.At(p.X, p.Y) + VerticalOffset;
            Vector3 n = WorldHeight.NormalAt(p.X, p.Y);

            verts.Add(new Vector3(p.X - perp.X, h, p.Y - perp.Y));
            verts.Add(new Vector3(p.X + perp.X, h, p.Y + perp.Y));
            normals.Add(n);
            normals.Add(n);
        }

        for (int i = 0; i < samples - 1; i++)
        {
            int bl = baseIdx + i * 2, br = bl + 1, tl = bl + 2, tr = bl + 3;
            // CW winding for Godot (normal pointing up for flat ground)
            indices.Add(bl); indices.Add(tl); indices.Add(tr);
            indices.Add(bl); indices.Add(tr); indices.Add(br);
        }
    }
}
