using Godot;

namespace Rotorwash;

/// <summary>
/// Graphics quality tiers.
///
/// The project has a hard floor and a high ceiling: it must be playable at 1080p60 on a
/// GTX 1080 (Pascal - no hardware ray tracing, no DLSS), and it should use realtime
/// global illumination on hardware that can afford it. Rather than authoring twice, every
/// scene is lit with real physical lights and looks correct under both paths; the tier
/// only decides how much of the light transport is computed at runtime.
///
/// Detection is a starting guess. The player can always override it, and the setting is
/// read from user://graphics.cfg so it survives a rebuild.
/// </summary>
public static class QualityTier
{
    public enum Tier { Low = 0, Medium = 1, High = 2, Ultra = 3 }

    public static Tier Current { get; private set; } = Tier.High;

    private const string ConfigPath = "user://graphics.cfg";

    public static void DetectAndApply()
    {
        Current = Load() ?? Detect();
        Apply();
        GD.Print($"[gfx] {RenderingServer.GetVideoAdapterName()} -> tier {Current}");
    }

    public static void Set(Tier tier)
    {
        Current = tier;
        Apply();
        Save();
    }

    private static Tier Detect()
    {
        string gpu = RenderingServer.GetVideoAdapterName().ToLowerInvariant();
        long vram = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);

        // Crude but honest: the things that actually predict whether realtime GI is
        // affordable are generation and class, and the name carries both.
        if (gpu.Contains("rtx 40") || gpu.Contains("rtx 50") || gpu.Contains("rx 7") || gpu.Contains("rx 9"))
            return Tier.Ultra;
        if (gpu.Contains("rtx 30") || gpu.Contains("rtx 20") || gpu.Contains("rx 6"))
            return Tier.High;
        if (gpu.Contains("gtx 10") || gpu.Contains("gtx 16") || gpu.Contains("rx 5"))
            return Tier.Medium;
        if (gpu.Contains("intel") && !gpu.Contains("arc"))
            return Tier.Low;
        return Tier.Medium;
    }

    private static void Apply()
    {
        var vp = Engine.GetMainLoop() as SceneTree;
        var viewport = vp?.Root;
        if (viewport is null) return;

        switch (Current)
        {
            case Tier.Low:
                viewport.Msaa3D = Viewport.Msaa.Disabled;
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                viewport.Scaling3DScale = 0.75f;
                RenderingServer.DirectionalShadowAtlasSetSize(2048, true);
                break;
            case Tier.Medium:
                viewport.Msaa3D = Viewport.Msaa.Msaa2X;
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                viewport.Scaling3DScale = 1.0f;
                RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
                break;
            case Tier.High:
                viewport.Msaa3D = Viewport.Msaa.Msaa4X;
                viewport.Scaling3DScale = 1.0f;
                RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
                break;
            case Tier.Ultra:
                viewport.Msaa3D = Viewport.Msaa.Msaa4X;
                viewport.Scaling3DScale = 1.0f;
                RenderingServer.DirectionalShadowAtlasSetSize(8192, true);
                break;
        }
    }

    private static Tier? Load()
    {
        if (!FileAccess.FileExists(ConfigPath)) return null;
        using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Read);
        string s = f?.GetAsText().Trim() ?? "";
        return System.Enum.TryParse(s, out Tier t) ? t : null;
    }

    private static void Save()
    {
        using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Write);
        f?.StoreString(Current.ToString());
    }
}
