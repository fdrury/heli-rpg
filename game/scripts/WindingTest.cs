using Godot;

namespace Rotorwash;

/// <summary>
/// Settles which triangle winding Godot treats as front-facing, by rendering it.
///
/// This matters more than it sounds. Winding decides two separate things:
///   * what backface culling throws away, and
///   * which way <c>SurfaceTool.GenerateNormals()</c> points the normals it derives.
///
/// Get it backwards and a closed mesh still draws a silhouette - you are simply looking
/// at the inside of the far wall - but every surface is lit from behind, so the whole
/// model reads as too dark and nobody can say why. That is a much harder bug to see than
/// a missing object, which is exactly why it wants a measurement rather than an argument.
///
///     godot --path game -- --windingtest
/// </summary>
public sealed partial class WindingTest : Node3D
{
    private int _frames;

    public override void _Ready()
    {
        // A flat, unlit, known background so the sampled pixels are unambiguous.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0, 0, 0),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1, 1, 1),
            AmbientLightEnergy = 1,
        };
        AddChild(new WorldEnvironment { Environment = env });

        // Camera at +Z looking back at the origin. Set the basis directly rather than
        // calling LookAt, which needs the node to already be in the tree.
        var cam = new Camera3D { Current = true };
        AddChild(cam);
        cam.Position = new Vector3(0, 1.6f, 4);
        cam.LookAt(new Vector3(0, -0.6f, 0), Vector3.Up);

        // Both quads face the camera at z = 0. The only difference is vertex order.
        AddChild(Quad(new Vector3(-1.1f, 0, 0), counterClockwise: true, new Color(1, 0, 0)));
        AddChild(Quad(new Vector3(1.1f, 0, 0), counterClockwise: false, new Color(0, 0, 1)));

        // A third surface, built with the EXACT index order the terrain streamer uses,
        // lying flat and viewed from above. Reasoning about the terrain from the two
        // quads above gave an answer that contradicts what the game actually renders, so
        // this measures the real thing instead of a model of it.
        AddChild(GroundQuad(new Vector3(0, -2.2f, 0), new Color(0, 1, 0)));
    }

    /// <summary>
    /// A quad in the XY plane facing +Z (toward the camera).
    ///
    /// "Counter-clockwise" here means: seen from the camera, the vertices run
    /// anticlockwise, which is the order for which (v1-v0) x (v2-v0) points AT the camera.
    /// </summary>
    private static MeshInstance3D Quad(Vector3 at, bool counterClockwise, Color colour)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        var a = new Vector3(-0.8f, -0.8f, 0);
        var b = new Vector3(0.8f, -0.8f, 0);
        var c = new Vector3(0.8f, 0.8f, 0);
        var d = new Vector3(-0.8f, 0.8f, 0);

        void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            if (counterClockwise) { st.AddVertex(p0); st.AddVertex(p1); st.AddVertex(p2); }
            else { st.AddVertex(p2); st.AddVertex(p1); st.AddVertex(p0); }
        }
        Tri(a, b, c);
        Tri(a, c, d);

        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            Position = at,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = colour,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Back,   // the default, under test
            },
        };
    }

    /// <summary>
    /// A flat patch built exactly as TerrainStreamer builds one: a grid in XZ, with
    /// indices (a, d, c) and (a, c, b) where a=(i,j) b=(i+1,j) c=(i+1,j+1) d=(i,j+1).
    /// </summary>
    private static MeshInstance3D GroundQuad(Vector3 at, Color colour)
    {
        const float cell = 1.4f;
        var verts = new Vector3[4];
        verts[0] = new Vector3(0, 0, 0);            // a  (i,   j)
        verts[1] = new Vector3(cell, 0, 0);         // b  (i+1, j)
        verts[2] = new Vector3(cell, 0, cell);      // c  (i+1, j+1)
        verts[3] = new Vector3(0, 0, cell);         // d  (i,   j+1)

        var normals = new[] { Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up };
        var uvs = new[] { Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down };
        // a, d, c   then   a, c, b   - the terrain's order, verbatim.
        var indices = new[] { 0, 3, 2, 0, 2, 1 };

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Mesh = mesh,
            Position = at - new Vector3(cell * 0.5f, 0, cell * 0.5f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = colour,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Back,
            },
        };
    }

    public override void _Process(double delta)
    {
        if (++_frames < 6) return;
        SetProcess(false);
        CallDeferred(nameof(Measure));
    }

    private async void Measure()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();

        Vector2I size = img.GetSize();

        // Scan each half for its colour rather than sampling one pixel, and save the frame
        // so a wrong test can be SEEN instead of reasoned about.
        int red = 0, blue = 0, green = 0, lit = 0;
        for (int y = 0; y < size.Y; y += 2)
        {
            for (int x = 0; x < size.X; x += 2)
            {
                Color c = img.GetPixel(x, y);
                if (c.R + c.G + c.B < 0.15f) continue;
                lit++;
                if (c.G > 0.5f && c.R < 0.5f && c.B < 0.5f) green++;
                else if (c.R > c.B) red++; else blue++;
            }
        }
        string outDir = ProjectSettings.GlobalizePath("res://../builds/screenshots");
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        img.SavePng(outDir + "/winding.png");

        GD.Print($"  frame {size.X}x{size.Y}, {lit} lit: {red} red (ccw), {blue} blue (cw), " +
                 $"{green} green (terrain order, seen from ABOVE)");
        GD.Print($"  saved {outDir}/winding.png");

        bool ccwVisible = red > 200;
        bool cwVisible = blue > 200;

        GD.Print("=== Winding test ==================================================");
        GD.Print($"  counter-clockwise quad (red, left):  {(ccwVisible ? "VISIBLE" : "culled")}");
        GD.Print($"  clockwise quad (blue, right):        {(cwVisible ? "VISIBLE" : "culled")}");
        GD.Print("");

        if (ccwVisible && !cwVisible)
            GD.Print("  Godot treats COUNTER-CLOCKWISE as front-facing.");
        else if (cwVisible && !ccwVisible)
            GD.Print("  Godot treats CLOCKWISE as front-facing.");
        else if (ccwVisible && cwVisible)
            GD.Print("  Both visible - culling is not doing anything. Check the material.");
        else
            GD.Print("  Neither visible - the test itself is wrong.");

        GD.Print($"  terrain-order quad seen from above:   {(green > 60 ? "VISIBLE" : "culled")}");
        GD.Print("");
        GD.Print("  This project builds meshes so that (v1-v0) x (v2-v0) points OUTWARD,");
        GD.Print("  which is the counter-clockwise convention. If the line above says");
        GD.Print("  clockwise, every hand-built mesh here is inside out and every normal");
        GD.Print("  from GenerateNormals() points the wrong way.");
        GD.Print("===================================================================");

        GetTree().Quit(0);
    }
}
