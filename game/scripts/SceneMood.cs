using Godot;

namespace Rotorwash;

/// <summary>
/// The light, the air and the grade — everything that decides what the world feels like
/// rather than what is in it.
///
/// Pulled out of Main for two reasons. It is about to grow a great deal (time of day,
/// weather, the difference between a clear morning and flying into weather at dusk), and
/// every value in it needed re-deriving from scratch after D-016.
///
/// D-016 is the important context. Every mesh in the project was wound backwards, so every
/// surface was lit as though the sun were behind it, and this grade had been tuned to
/// compensate: ambient pushed up to rescue surfaces that should never have been dark,
/// exposure raised, saturation pulled down to hide the muddiness, macro brightness noise
/// added to fake variation the lighting should have provided. With the winding fixed all
/// of that became over-correction on top of a correct image.
///
/// So the numbers here are chosen the other way round: let the sun do the work, keep
/// ambient low enough that shaded ground is genuinely shaded, and let contrast carry the
/// form instead of a noise function.
/// </summary>
public static class SceneMood
{
    /// <summary>A named lighting condition. More of these arrive with weather and night.</summary>
    public enum Mood { Afternoon, Overcast, Dusk }

    public static Mood Current { get; private set; } = Mood.Afternoon;

    public static void Apply(Node3D root, Mood mood = Mood.Afternoon)
    {
        Current = mood;
        root.AddChild(BuildEnvironment(mood));
        root.AddChild(BuildSun(mood));
    }

    private static WorldEnvironment BuildEnvironment(Mood mood)
    {
        (Color top, Color horizon, Color ground, float skyEnergy) = mood switch
        {
            Mood.Overcast => (new Color(0.42f, 0.45f, 0.48f), new Color(0.62f, 0.62f, 0.60f),
                              new Color(0.20f, 0.20f, 0.19f), 0.85f),
            Mood.Dusk => (new Color(0.16f, 0.20f, 0.34f), new Color(0.78f, 0.52f, 0.34f),
                          new Color(0.10f, 0.09f, 0.10f), 0.70f),
            _ => (new Color(0.24f, 0.36f, 0.50f), new Color(0.74f, 0.71f, 0.62f),
                  new Color(0.14f, 0.14f, 0.12f), 1.0f),
        };

        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = top,
            SkyHorizonColor = horizon,
            GroundBottomColor = ground,
            GroundHorizonColor = horizon * 0.82f,
            SunAngleMax = mood == Mood.Overcast ? 60f : 26f,
            SunCurve = mood == Mood.Overcast ? 0.45f : 0.12f,
            SkyEnergyMultiplier = skyEnergy,
        };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,

            // Low. Ambient was at 1.15 to rescue surfaces that were dark because of the
            // winding bug; with the sun actually reaching them, this only has to fill
            // shadow, and shadow should stay readable as shadow.
            AmbientLightEnergy = mood == Mood.Overcast ? 1.05f : 0.66f,

            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = mood == Mood.Dusk ? 1.15f : 1.0f,

            SsaoEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            SsaoIntensity = 1.4f,
            SsaoRadius = 3.0f,

            GlowEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            GlowIntensity = 0.28f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.05f,

            // Aerial perspective is most of what sells distance from a helicopter. Ridge
            // lines stacking back into haze is the single strongest depth cue the game has.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = mood switch
            {
                Mood.Overcast => new Color(0.64f, 0.65f, 0.66f),
                Mood.Dusk => new Color(0.60f, 0.48f, 0.42f),
                _ => new Color(0.68f, 0.68f, 0.64f),
            },
            FogLightEnergy = 1.0f,
            FogDensity = mood == Mood.Overcast ? 0.0016f : 0.00095f,
            FogAerialPerspective = 0.85f,
            FogSkyAffect = 0.30f,
            FogDepthBegin = 260f,
            FogDepthEnd = 7000f,

            // A light hand now. The heavy desaturation was hiding a problem that no
            // longer exists, and taking it out put the greens and ochres back.
            AdjustmentEnabled = true,
            AdjustmentSaturation = mood == Mood.Overcast ? 0.82f : 0.93f,
            AdjustmentContrast = 1.07f,
            AdjustmentBrightness = 1.0f,
        };

        if (QualityTier.Current >= QualityTier.Tier.Ultra)
        {
            env.SdfgiEnabled = true;
            env.SdfgiUseOcclusion = true;
            env.SsilEnabled = true;
            env.VolumetricFogEnabled = true;
            env.VolumetricFogDensity = 0.010f;
        }

        return new WorldEnvironment { Name = "WorldEnvironment", Environment = env };
    }

    private static DirectionalLight3D BuildSun(Mood mood)
    {
        (float energy, Color colour, Vector3 angle) = mood switch
        {
            // Diffuse and weak: an overcast sky lights from everywhere, so the directional
            // contribution is small and the shadows are soft to the point of absence.
            Mood.Overcast => (0.55f, new Color(0.93f, 0.94f, 0.96f), new Vector3(-58, 140, 0)),
            // Low, long and warm. Every shadow is a hundred metres long.
            Mood.Dusk => (1.30f, new Color(1.0f, 0.78f, 0.58f), new Vector3(-9, 104, 0)),
            // Mid-afternoon: high enough to light the ground, low enough that terrain and
            // airframes have real form shadows rather than a flat top-down wash.
            _ => (1.65f, new Color(1.0f, 0.965f, 0.90f), new Vector3(-40, 148, 0)),
        };

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = energy,
            LightColor = colour,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = QualityTier.Current >= QualityTier.Tier.High ? 900 : 420,
            DirectionalShadowSplit1 = 0.06f,
            DirectionalShadowSplit2 = 0.16f,
            DirectionalShadowSplit3 = 0.42f,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.4f,
            ShadowBlur = mood == Mood.Overcast ? 2.4f : 1.0f,
        };
        sun.RotationDegrees = angle;
        return sun;
    }
}
