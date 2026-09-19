using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The light, the air and the grade — everything that decides what the world feels like
/// rather than what is in it.
///
/// This began as three fixed presets (afternoon, overcast, dusk) written while re-deriving
/// the lighting grade after D-016. Those are gone. The sky is now driven continuously by a
/// real solar position and a real weather model out of <see cref="Rotorwash.Sim.Weather"/>,
/// so there is no list of looks to choose from: there is a time and a set of conditions,
/// and the sky is whatever those imply.
///
/// The grade values are still the ones derived in D-023, and the reasoning behind them
/// still holds — ambient stays low enough that shaded ground reads as shaded, and the sun
/// does the work. What changes here is that "the sun" now moves.
/// </summary>
public static class SceneMood
{
    /// <summary>The world's weather. Deterministic from its seed.</summary>
    public static Weather Weather { get; } = new();

    /// <summary>Game clock, seconds. Starts mid-morning on day one.</summary>
    public static double Clock { get; set; } = 9.25 * 3600.0;

    /// <summary>
    /// Game seconds per real second.
    ///
    /// Thirty puts a full day in forty-eight minutes of play. Slow enough that the light
    /// does not visibly crawl across the ground while you hover, fast enough that a long
    /// sortie can leave in the morning and arrive in failing light — which is the whole
    /// point of having a clock at all.
    /// </summary>
    public static double TimeScale { get; set; } = 30.0;

    /// <summary>Conditions right now.</summary>
    public static Weather.Conditions Now => Weather.At(Clock);

    /// <summary>Where the sun is right now.</summary>
    public static SunPosition SunNow => Weather.Sun(Clock);

    public static void Apply(Node3D root)
    {
        var env = BuildEnvironment();
        var sun = BuildSun();
        var moon = BuildMoon();
        root.AddChild(env);
        root.AddChild(sun);
        root.AddChild(moon);
        root.AddChild(new SceneMoodDriver(env, sun, moon) { Name = "SceneMoodDriver" });
        root.AddChild(new WeatherEffects { Name = "WeatherEffects" });
        root.AddChild(new WeatherAudio { Name = "WeatherAudio" });
    }

    private static WorldEnvironment BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.24f, 0.36f, 0.50f),
            SkyHorizonColor = new Color(0.74f, 0.71f, 0.62f),
            GroundBottomColor = new Color(0.14f, 0.14f, 0.12f),
            GroundHorizonColor = new Color(0.60f, 0.58f, 0.51f),
            SunAngleMax = 26f,
            SunCurve = 0.12f,
        };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,
            AmbientLightEnergy = 0.66f,

            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,

            SsaoEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            SsaoIntensity = 1.4f,
            SsaoRadius = 3.0f,

            GlowEnabled = QualityTier.Current >= QualityTier.Tier.Medium,
            GlowIntensity = 0.28f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.05f,

            // Aerial perspective is most of what sells distance from a helicopter. Ridge
            // lines stacking back into haze is the strongest depth cue the game has.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color(0.68f, 0.68f, 0.64f),
            FogLightEnergy = 1.0f,
            FogDensity = 0.00095f,
            FogAerialPerspective = 0.85f,
            FogSkyAffect = 0.30f,
            FogDepthBegin = 260f,
            FogDepthEnd = 7000f,

            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.93f,
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

    private static DirectionalLight3D BuildSun() => new()
    {
        Name = "Sun",
        LightEnergy = 1.65f,
        LightColor = new Color(1.0f, 0.965f, 0.90f),
        ShadowEnabled = true,
        DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        DirectionalShadowMaxDistance = QualityTier.Current >= QualityTier.Tier.High ? 900 : 420,
        DirectionalShadowSplit1 = 0.06f,
        DirectionalShadowSplit2 = 0.16f,
        DirectionalShadowSplit3 = 0.42f,
        ShadowBias = 0.03f,
        ShadowNormalBias = 1.4f,
        // No sky disc. Godot draws it from this light's colour and energy, and at dusk the
        // light is dimmer than the horizon glow the sky gradient paints behind it - so the
        // "sun" rendered as a dark circular hole sitting on the horizon. The gradient does
        // a better job of selling a sunset than a disc does anyway.
        SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
    };

    /// <summary>
    /// A second directional light for the night.
    ///
    /// Moonlight is not dim sunlight - it is cooler, softer, and comes from somewhere else
    /// entirely. Running it as its own light makes dusk two sources crossing over rather
    /// than one source changing colour, which is what actually happens.
    /// </summary>
    private static DirectionalLight3D BuildMoon() => new()
    {
        Name = "Moon",
        LightEnergy = 0f,
        LightColor = new Color(0.62f, 0.70f, 0.92f),
        ShadowEnabled = false,
        SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
    };
}

/// <summary>
/// Advances the clock and pushes the sky at it, once a frame.
///
/// A node rather than a call from Main, deliberately: everything about the sky then lives
/// in one file, and adding weather did not require touching the scene assembly at all.
/// </summary>
public sealed partial class SceneMoodDriver : Node
{
    private readonly WorldEnvironment _worldEnv;
    private readonly DirectionalLight3D _sun;
    private readonly DirectionalLight3D _moon;

    public SceneMoodDriver(WorldEnvironment env, DirectionalLight3D sun, DirectionalLight3D moon)
    {
        _worldEnv = env;
        _sun = sun;
        _moon = moon;
    }

    public override void _Ready() => Push();

    public override void _Process(double delta)
    {
        SceneMood.Clock += delta * SceneMood.TimeScale;
        Push();
    }

    private void Push()
    {
        SunPosition s = SceneMood.SunNow;
        Weather.Conditions c = SceneMood.Now;
        Godot.Environment env = _worldEnv.Environment;

        float day = (float)s.DaylightFraction;
        float cover = (float)c.Cover;

        // --- Where the sun is --------------------------------------------------
        // The world uses sim north = -Z and east = +X. Light travels FROM the sun, so it
        // points along the negative of the direction the sun sits in.
        float el = (float)s.ElevationRad, az = (float)s.AzimuthRad;
        var toSun = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el),
                                -Mathf.Cos(az) * Mathf.Cos(el));
        PointAlong(_sun, -toSun);

        // The moon sits opposite the sun. Crude, but it puts it in the sky when the sun is
        // not, which is the only property that matters here.
        PointAlong(_moon, toSun);

        // --- How strong, and what colour ---------------------------------------
        // Low sun is redder and weaker: the light comes through far more atmosphere.
        float low = 1f - Mathf.Clamp(Mathf.Sin(Mathf.Max(el, 0f)) * 3.2f, 0f, 1f);
        var warm = new Color(1.0f, 0.965f - low * 0.26f, 0.90f - low * 0.45f);

        // Overcast kills the directional component and hands the job to the sky.
        // Overcast dims the sun but must not switch it off. At 0.86 a rainy morning
        // rendered with a bright sky over near-black ground, which is what a night looks
        // like, not a wet day - an overcast day is FLAT and bright, not dark.
        float clear = 1f - cover * 0.70f;
        _sun.LightColor = warm;
        _sun.LightEnergy = 1.65f * day * clear;
        _sun.ShadowEnabled = _sun.LightEnergy > 0.06f;
        _sun.ShadowBlur = 1.0f + cover * 1.8f;
        _sun.Visible = _sun.LightEnergy > 0.005f;

        _moon.LightEnergy = 0.45f * (1f - day) * (1f - cover * 0.45f);
        _moon.Visible = _moon.LightEnergy > 0.005f;

        // Ambient never reaches zero: a clear night still has sky glow, and a player who
        // cannot see the horizon cannot fly. Overcast raises it, because a cloud deck is a
        // very large soft light source.
        // The night floor is a game decision, not a physical one, and it is a large one:
        // at a physically honest 0.055 the night render was PURE BLACK - not moody, not
        // dim, black, with no horizon and no aircraft. You cannot fly what you cannot see.
        // This is high enough to read terrain silhouettes and the airframe, low enough that
        // flying at night is still something you would rather not have to do.
        env.AmbientLightEnergy = Mathf.Lerp(0.24f, 0.62f + cover * 0.95f, day);

        // --- The sky itself -----------------------------------------------------
        var skyMat = (ProceduralSkyMaterial)env.Sky.SkyMaterial;

        Color dayTop = new Color(0.24f, 0.36f, 0.50f).Lerp(new Color(0.40f, 0.42f, 0.45f), cover);
        Color dayHorizon = new Color(0.74f, 0.71f, 0.62f).Lerp(new Color(0.62f, 0.62f, 0.60f), cover);
        var duskHorizon = new Color(0.80f, 0.48f, 0.30f);
        var nightTop = new Color(0.035f, 0.047f, 0.092f);
        var nightHorizon = new Color(0.10f, 0.115f, 0.165f);

        // Dusk is a narrow band around the horizon crossing, not a blend of day and night.
        // Interpolating straight from one to the other skips the part people remember.
        float dusk = Mathf.Clamp(1f - Mathf.Abs((float)s.ElevationDeg + 2f) / 9f, 0f, 1f)
                     * (1f - cover * 0.7f);

        skyMat.SkyTopColor = nightTop.Lerp(dayTop, day);
        skyMat.SkyHorizonColor = nightHorizon.Lerp(dayHorizon, day).Lerp(duskHorizon, dusk * 0.75f);
        skyMat.GroundBottomColor = new Color(0.045f, 0.048f, 0.060f)
            .Lerp(new Color(0.14f, 0.14f, 0.12f), day);
        skyMat.GroundHorizonColor = skyMat.SkyHorizonColor * 0.82f;
        skyMat.SkyEnergyMultiplier = Mathf.Lerp(0.50f, 1.0f, day);
        skyMat.SunAngleMax = Mathf.Lerp(26f, 60f, cover);
        skyMat.SunCurve = Mathf.Lerp(0.12f, 0.45f, cover);

        // --- Air ----------------------------------------------------------------
        // Density from reported visibility: fog transmittance falls to about 5% at the
        // visibility range, which is ln(20)/V, near enough 3/V.
        env.FogDensity = Mathf.Clamp(3.0f / (float)c.Visibility, 0.00008f, 0.02f);
        env.FogLightColor = new Color(0.68f, 0.68f, 0.64f)
            .Lerp(new Color(0.64f, 0.65f, 0.66f), cover)
            .Lerp(new Color(0.05f, 0.06f, 0.09f), 1f - day);
        env.FogDepthBegin = Mathf.Lerp(90f, 260f, day);

        env.AdjustmentSaturation = Mathf.Lerp(0.62f, 0.93f - cover * 0.11f, day);
        env.TonemapExposure = Mathf.Lerp(1.55f, 1.0f + cover * 0.16f, day);
    }

    /// <summary>Aim a directional light along a direction of travel.</summary>
    private static void PointAlong(Node3D light, Vector3 direction)
    {
        if (direction.LengthSquared() < 1e-6f) return;
        direction = direction.Normalized();
        // Basis.LookingAt puts -Z on the target, which is the direction a light emits.
        // Up has to change near the vertical or the basis is degenerate at local noon.
        Vector3 up = Mathf.Abs(direction.Y) > 0.98f ? Vector3.Forward : Vector3.Up;
        light.Basis = Basis.LookingAt(direction, up);
    }
}
