using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Generates the landscape's small geometry - rocks, dead trees, scrub - in code.
///
/// This is a deliberate choice, not a shortcut. A dying, drying landscape wants bare
/// branches and broken stone, and both are far easier to generate convincingly than
/// foliage: no leaf cards, no alpha sorting, no photoscanned mesh to decimate, no LOD
/// chain to author, no licence to track. Variety is free, every prop is already
/// watertight and low-poly, and the whole set regenerates from a seed.
///
/// When real scanned assets are worth the pipeline cost, these become the LOD2.
/// </summary>
public static class ProceduralProps
{
    // ------------------------------------------------------------------ rocks

    /// <summary>
    /// A boulder: an icosphere pushed around by 3-D noise and flat shaded, so it catches
    /// light in facets the way weathered stone does rather than reading as a potato.
    /// </summary>
    public static ArrayMesh Rock(int seed, float radius = 1.6f, int subdivisions = 2)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.55f,
            FractalOctaves = 3,
            FractalGain = 0.5f,
        };

        BuildIcosphere(subdivisions, out List<Vector3> verts, out List<int> tris);

        // Squash it: real boulders are rarely round, and a consistent vertical squash
        // makes a field of them read as bedrock rather than marbles.
        var squash = new Vector3(
            rng.RandfRange(0.85f, 1.25f),
            rng.RandfRange(0.55f, 0.85f),
            rng.RandfRange(0.85f, 1.25f));

        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            float n = noise.GetNoise3D(v.X * 2.1f, v.Y * 2.1f, v.Z * 2.1f);
            float n2 = noise.GetNoise3D(v.X * 6.3f + 40, v.Y * 6.3f, v.Z * 6.3f - 17);
            float r = radius * (1.0f + n * 0.30f + n2 * 0.11f);
            verts[i] = v * r * squash;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // Flat shading: emit each triangle with its own face normal.
        for (int i = 0; i < tris.Count; i += 3)
        {
            Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
            Vector3 nrm = (b - a).Cross(c - a).Normalized();
            // Godot treats CLOCKWISE as front-facing, so the emission order is reversed
            // relative to the outward normal computed above. Measured with --windingtest.
            foreach (Vector3 v in new[] { a, c, b })
            {
                st.SetNormal(nrm);
                st.SetUV(new Vector2(v.X * 0.35f + 0.5f, v.Z * 0.35f + 0.5f));
                st.AddVertex(v);
            }
        }
        st.GenerateTangents();
        return st.Commit();
    }

    // ------------------------------------------------------------- dead trees

    /// <summary>
    /// A standing dead tree: a recursive tapered trunk with branches that thin, split and
    /// lean. No leaves, which is both the point and the reason it is cheap.
    /// </summary>
    public static ArrayMesh DeadTree(int seed, float height = 9f)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float baseRadius = height * rng.RandfRange(0.030f, 0.055f);
        Branch(st, rng, Vector3.Zero, Vector3.Up, height * 0.42f, baseRadius, 0, height);

        st.GenerateNormals();
        st.GenerateTangents();
        return st.Commit();
    }

    private static void Branch(SurfaceTool st, RandomNumberGenerator rng,
                               Vector3 origin, Vector3 dir, float length, float radius,
                               int depth, float treeHeight)
    {
        if (depth > 4 || radius < 0.012f || length < 0.22f) return;

        // A trunk that wanders slightly reads as grown; a straight one reads as a pole.
        Vector3 end = origin + dir * length;
        end += new Vector3(rng.RandfRange(-1f, 1f), 0, rng.RandfRange(-1f, 1f)) * length * 0.10f;
        Vector3 actual = (end - origin).Normalized();

        float tipRadius = radius * rng.RandfRange(0.55f, 0.72f);
        AddTaperedSegment(st, origin, end, radius, tipRadius, depth == 0 ? 7 : 5);

        // Occasionally a limb has simply snapped off. Broken silhouettes are most of what
        // makes a dead tree read as dead rather than as a tree waiting for spring.
        if (depth > 0 && rng.Randf() < 0.22f) return;

        int children = depth == 0 ? rng.RandiRange(2, 3) : rng.RandiRange(1, 3);
        for (int i = 0; i < children; i++)
        {
            float spread = Mathf.DegToRad(rng.RandfRange(18f, 52f));
            float around = rng.Randf() * Mathf.Tau;

            Vector3 perp = actual.Cross(Mathf.Abs(actual.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
            Vector3 axis = perp.Rotated(actual, around);
            Vector3 childDir = actual.Rotated(axis, spread).Normalized();

            Branch(st, rng, end, childDir,
                   length * rng.RandfRange(0.58f, 0.80f),
                   tipRadius * rng.RandfRange(0.62f, 0.85f),
                   depth + 1, treeHeight);
        }
    }

    private static void AddTaperedSegment(SurfaceTool st, Vector3 a, Vector3 b,
                                          float r0, float r1, int sides)
    {
        Vector3 axis = (b - a).Normalized();
        Vector3 u = axis.Cross(Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        Vector3 v = axis.Cross(u);

        for (int i = 0; i < sides; i++)
        {
            float t0 = Mathf.Tau * i / sides, t1 = Mathf.Tau * (i + 1) / sides;
            Vector3 d0 = u * Mathf.Cos(t0) + v * Mathf.Sin(t0);
            Vector3 d1 = u * Mathf.Cos(t1) + v * Mathf.Sin(t1);

            Vector3 p00 = a + d0 * r0, p01 = a + d1 * r0;
            Vector3 p10 = b + d0 * r1, p11 = b + d1 * r1;

            Quad(st, p00, p01, p11, p10);
        }
    }

    /// <summary>
    /// A quad wound for Godot, which treats CLOCKWISE as front-facing. The vertices are
    /// given in outward-normal (counter-clockwise) order and emitted reversed, so callers
    /// can keep thinking in normals rather than in winding.
    /// </summary>
    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
        st.SetUV(new Vector2(1, 0)); st.AddVertex(b);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(a);

        st.SetUV(new Vector2(0, 1)); st.AddVertex(d);
        st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
    }

    // ------------------------------------------------------------------ scrub

    /// <summary>
    /// A clump of dry scrub: crossed alpha quads. Cheap, and from any distance above
    /// walking height it is indistinguishable from something far more expensive.
    /// </summary>
    public static ArrayMesh ScrubCard(float width = 1.3f, float height = 0.9f, int blades = 3)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < blades; i++)
        {
            float angle = Mathf.Pi * i / blades;
            Vector3 right = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * width * 0.5f;

            Vector3 a = -right, b = right;
            Vector3 c = right + Vector3.Up * height, d = -right + Vector3.Up * height;

            // Double-sided: emit both windings so the card is lit from either side.
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(1, 1)); st.AddVertex(b);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(1, 0)); st.AddVertex(c);

            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(0, 0)); st.AddVertex(d);

            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(1, 1)); st.AddVertex(b);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(0, 0)); st.AddVertex(d);

            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(1, 1)); st.AddVertex(b);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(0, 0)); st.AddVertex(d);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
        }

        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>
    /// Paints a dry-grass clump into a texture: thin tapered blades, bleached at the tips.
    /// Generated rather than downloaded so the palette matches the terrain grade exactly.
    /// </summary>
    public static ImageTexture ScrubTexture(int size = 256, int seed = 7)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };

        for (int b = 0; b < 78; b++)
        {
            float x0 = rng.RandfRange(0.08f, 0.92f) * size;
            float lean = rng.RandfRange(-0.32f, 0.32f) * size;
            float h = rng.RandfRange(0.45f, 0.97f) * size;
            float thickness = rng.RandfRange(1.0f, 2.2f);

            // Straw and olive, and bright enough to sit in the same tonal range as the
            // terrain. Dark blades read as holes in the ground from the air, which is
            // exactly the failure the first version had.
            // Dry grass, not straw-gold. The first version was bright enough to read as
            // wheat from the air, which is the wrong crop and the wrong century.
            var baseCol = new Color(
                rng.RandfRange(0.30f, 0.39f),
                rng.RandfRange(0.30f, 0.37f),
                rng.RandfRange(0.19f, 0.25f));
            var tipCol = new Color(
                rng.RandfRange(0.46f, 0.56f),
                rng.RandfRange(0.44f, 0.52f),
                rng.RandfRange(0.31f, 0.38f));

            int steps = (int)h;
            for (int s2 = 0; s2 < steps; s2++)
            {
                float t = (float)s2 / steps;
                float x = x0 + lean * t * t;
                float y = size - 1 - s2;
                float w = thickness * (1.0f - t * 0.82f);
                Color c = baseCol.Lerp(tipCol, t);

                for (float dx = -w; dx <= w; dx += 0.5f)
                {
                    int px = Mathf.RoundToInt(x + dx);
                    int py = Mathf.RoundToInt(y);
                    if (px < 0 || px >= size || py < 0 || py >= size) continue;
                    float edge = 1.0f - Mathf.Abs(dx) / Mathf.Max(w, 0.001f);
                    float alpha = Mathf.Clamp(edge * 1.9f, 0, 1);
                    Color existing = img.GetPixel(px, py);
                    if (alpha > existing.A) img.SetPixel(px, py, new Color(c.R, c.G, c.B, alpha));
                }
            }
        }

        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// The rotor disc: a translucent annulus with a radial smear, drawn instead of the
    /// blades once the rotor is turning fast enough that a human eye would only see a
    /// disc. Every helicopter in every game does this, and without it a two-bladed rotor
    /// reads as a black plank.
    /// </summary>
    public static ArrayMesh RotorDisc(float radius, float innerFraction = 0.12f, int segments = 64)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float inner = radius * innerFraction;

        for (int i = 0; i < segments; i++)
        {
            float t0 = Mathf.Tau * i / segments, t1 = Mathf.Tau * (i + 1) / segments;
            Vector3 d0 = new(Mathf.Cos(t0), 0, Mathf.Sin(t0));
            Vector3 d1 = new(Mathf.Cos(t1), 0, Mathf.Sin(t1));

            Vector3 a = d0 * inner, b = d1 * inner, c = d1 * radius, d = d0 * radius;

            // V runs 0 at the hub to 1 at the tip so the material can fade the disc out
            // toward the centre, where a real rotor is mostly empty air.
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2((i + 1) / (float)segments, 1)); st.AddVertex(c);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2((i + 1) / (float)segments, 0)); st.AddVertex(b);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(i / (float)segments, 0)); st.AddVertex(a);

            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(i / (float)segments, 1)); st.AddVertex(d);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2((i + 1) / (float)segments, 1)); st.AddVertex(c);
            st.SetNormal(Vector3.Up); st.SetUV(new Vector2(i / (float)segments, 0)); st.AddVertex(a);
        }

        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>
    /// Texture for the rotor disc: mostly transparent, with a couple of denser smears
    /// where the blades spend their time, and a bright tip band. Read radially: U is
    /// azimuth, V is radius.
    /// </summary>
    public static ImageTexture RotorDiscTexture(int size = 128, int blades = 2)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 991 };

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / (size - 1);           // 0 hub, 1 tip
            // A rotor is more visible at the tips: more blade area per unit azimuth and
            // the tip vortices catch the light.
            float radial = Mathf.Lerp(0.02f, 0.20f, Mathf.Pow(v, 1.8f));
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / (size - 1);
                float smear = 0;
                for (int b = 0; b < blades; b++)
                {
                    float centre = (float)b / blades;
                    float d = Mathf.Abs(Mathf.Wrap(u - centre, -0.5f, 0.5f));
                    smear += Mathf.Exp(-d * d * 260.0f);
                }
                // A real rotor disc is a suggestion, not a plate. Too opaque and the aircraft
                // looks like it is wearing a hat.
                float alpha = Mathf.Clamp(radial + smear * 0.30f * v, 0, 0.38f);
                float grey = 0.10f + smear * 0.16f;
                img.SetPixel(x, y, new Color(grey, grey, grey * 0.96f, alpha));
            }
        }

        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // ------------------------------------------------------------- icosphere

    private static void BuildIcosphere(int subdivisions, out List<Vector3> outVerts, out List<int> outTris)
    {
        List<Vector3> verts;
        List<int> tris;
        float t = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;
        verts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].Normalized();

        tris = new List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };

        for (int s = 0; s < subdivisions; s++)
        {
            var midpoints = new Dictionary<long, int>();
            var next = new List<int>(tris.Count * 4);

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midpoints.TryGetValue(key, out int existing)) return existing;
                Vector3 m = ((verts[a] + verts[b]) * 0.5f).Normalized();
                verts.Add(m);
                int idx = verts.Count - 1;
                midpoints[key] = idx;
                return idx;
            }

            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = next;
        }

        outVerts = verts;
        outTris = tris;
    }
}
