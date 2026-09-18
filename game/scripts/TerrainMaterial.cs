using Godot;

namespace Rotorwash;

/// <summary>
/// Builds the terrain surface material once and shares it across every streamed chunk.
/// One material for the whole world keeps the draw calls batchable and means a change to
/// the look is a single edit rather than a sweep.
/// </summary>
public static class TerrainMaterial
{
    private static ShaderMaterial? _cached;

    public static Material Build()
    {
        if (_cached is not null) return _cached;

        var shader = GD.Load<Shader>("res://assets/terrain/terrain.gdshader");
        if (shader is null)
        {
            GD.PushWarning("[terrain] shader missing, falling back to flat material");
            return new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.32f, 0.25f), Roughness = 0.95f };
        }

        var mat = new ShaderMaterial { Shader = shader };
        foreach (string name in new[] { "grass", "dirt", "rock", "gravel" })
        {
            Assign(mat, $"{name}_col", $"res://assets/terrain/{name}_col.jpg");
            Assign(mat, $"{name}_nrm", $"res://assets/terrain/{name}_nrm.jpg");
            Assign(mat, $"{name}_rgh", $"res://assets/terrain/{name}_rgh.jpg");
        }

        _cached = mat;
        return mat;
    }

    private static void Assign(ShaderMaterial mat, string param, string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex is null) { GD.PushWarning($"[terrain] missing texture {path}"); return; }
        mat.SetShaderParameter(param, tex);
    }
}
