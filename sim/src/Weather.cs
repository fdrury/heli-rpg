using System;

namespace Rotorwash.Sim;

/// <summary>What the sky is doing, in the coarse terms a pilot would use.</summary>
public enum SkyCondition { Clear, Fair, Overcast, Rain, Storm }

/// <summary>
/// Where the sun is, for a given moment.
/// </summary>
public readonly struct SunPosition
{
    /// <summary>Above the horizon, rad. Negative is below.</summary>
    public readonly double ElevationRad;
    /// <summary>Compass bearing of the sun, rad, clockwise from north.</summary>
    public readonly double AzimuthRad;

    public SunPosition(double elevation, double azimuth)
    {
        ElevationRad = elevation;
        AzimuthRad = azimuth;
    }

    public double ElevationDeg => ElevationRad * 180 / Math.PI;
    public double AzimuthDeg => AzimuthRad * 180 / Math.PI;

    /// <summary>Daylight, twilight, or night — the three cases that change how you fly.</summary>
    public bool IsDay => ElevationDeg > 0;
    public bool IsTwilight => ElevationDeg <= 0 && ElevationDeg > -12;
    public bool IsNight => ElevationDeg <= -12;

    /// <summary>
    /// How much of full daylight is reaching the ground, 0..1.
    ///
    /// Fades through twilight rather than switching off at the horizon, because the half
    /// hour either side of sunset is the part worth flying in and the part where a hard
    /// cutoff would look worst.
    /// </summary>
    public double DaylightFraction
    {
        get
        {
            double d = ElevationDeg;
            if (d >= 6) return 1.0;
            if (d <= -12) return 0.0;
            double t = (d + 12) / 18.0;
            return t * t * (3 - 2 * t);       // smoothstep
        }
    }
}

/// <summary>
/// The weather, and the sun.
///
/// Wind and gusts were already plumbed all the way through to the rotor - the environment
/// interface has carried a wind vector and spatially correlated gusts since the beginning -
/// but nothing ever set them, so every flight in the game so far has been in dead calm air
/// at standard temperature. This is what puts something on the other end of that wire.
///
/// It is deterministic: everything is a pure function of the clock and a seed, with no
/// internal accumulation. That matters for three reasons. The same flight can be replayed;
/// a headless test can ask what the weather will be at 14:20 on day three without
/// simulating its way there; and a save file only needs to store the seed.
///
/// The evolution is built from sine terms with deliberately incommensurate periods rather
/// than from random walks. A random walk wanders, needs clamping, and eventually parks
/// itself against a limit; summed sines drift, return, and stay in range for free. The
/// periods are chosen to be long enough that weather is something you plan around - the
/// slowest term is most of a day - and short enough that a long sortie can fly into a
/// change.
/// </summary>
public sealed class Weather
{
    public int Seed { get; }

    /// <summary>Latitude used for the solar geometry, degrees. Temperate northern.</summary>
    public double LatitudeDeg { get; set; } = 42.0;

    public Weather(int seed = 20260918) => Seed = seed;

    // ------------------------------------------------------------------- the sun

    /// <summary>
    /// Sun elevation and bearing at a given moment on the game clock.
    ///
    /// A real solar position calculation, simplified: the declination follows the year,
    /// the hour angle follows the day, and the two combine through spherical trigonometry.
    /// It is not accurate enough to navigate by and it does not need to be - what it has to
    /// get right is that the sun rises in the east, sets in the west, tracks south of
    /// overhead in the northern hemisphere, and climbs higher in summer.
    /// </summary>
    public SunPosition Sun(double clockSeconds)
    {
        double days = clockSeconds / 86400.0;
        double hour = (clockSeconds / 3600.0) % 24.0;

        // Declination: +23.44 deg at midsummer, -23.44 at midwinter.
        double dayOfYear = days % 365.25;
        double decl = 23.44 * Math.PI / 180 *
                      Math.Sin(2 * Math.PI * (dayOfYear - 80.0) / 365.25);

        double lat = LatitudeDeg * Math.PI / 180;
        double hourAngle = (hour - 12.0) * 15.0 * Math.PI / 180;

        double sinEl = Math.Sin(decl) * Math.Sin(lat) +
                       Math.Cos(decl) * Math.Cos(lat) * Math.Cos(hourAngle);
        double elevation = Math.Asin(Math.Clamp(sinEl, -1, 1));

        // Azimuth measured clockwise from north. atan2 form, which stays well behaved
        // across noon where the naive acos version flips.
        double y = -Math.Sin(hourAngle) * Math.Cos(decl);
        double x = Math.Cos(lat) * Math.Sin(decl) -
                   Math.Sin(lat) * Math.Cos(decl) * Math.Cos(hourAngle);
        double azimuth = Math.Atan2(y, x);
        if (azimuth < 0) azimuth += 2 * Math.PI;

        return new SunPosition(elevation, azimuth);
    }

    // --------------------------------------------------------------- the weather

    /// <summary>A snapshot of conditions. Everything the world and the flight model need.</summary>
    public readonly struct Conditions
    {
        public readonly SkyCondition Sky;
        /// <summary>Steady wind, m/s.</summary>
        public readonly double WindSpeed;
        /// <summary>Direction the wind blows FROM, rad clockwise from north — as reported.</summary>
        public readonly double WindFromRad;
        /// <summary>Gust intensity, m/s RMS on top of the steady wind.</summary>
        public readonly double Gust;
        /// <summary>Horizontal visibility, m.</summary>
        public readonly double Visibility;
        /// <summary>Cloud base above ground, m. Large when there is no meaningful cloud.</summary>
        public readonly double CloudBase;
        /// <summary>Departure from standard temperature, K. Drives density altitude.</summary>
        public readonly double IsaDeviation;
        /// <summary>Rain, 0..1.</summary>
        public readonly double Precipitation;
        /// <summary>Cloud cover, 0..1.</summary>
        public readonly double Cover;
        /// <summary>How deep into storm territory, 0..1. Zero unless Sky == Storm.</summary>
        public readonly double StormIntensity;

        public Conditions(SkyCondition sky, double windSpeed, double windFrom, double gust,
                          double visibility, double cloudBase, double isaDeviation,
                          double precipitation, double cover, double stormIntensity = 0)
        {
            Sky = sky; WindSpeed = windSpeed; WindFromRad = windFrom; Gust = gust;
            Visibility = visibility; CloudBase = cloudBase; IsaDeviation = isaDeviation;
            Precipitation = precipitation; Cover = cover; StormIntensity = stormIntensity;
        }

        /// <summary>
        /// Steady wind as a velocity in world NED axes.
        ///
        /// Note the inversion: a "north wind" blows FROM the north, so it pushes things
        /// southward. Getting this backwards is a traditional way to make every approach
        /// in a game mysteriously easier than it should be.
        /// </summary>
        public Vec3 WindNed => new(-Math.Cos(WindFromRad) * WindSpeed,
                                   -Math.Sin(WindFromRad) * WindSpeed, 0);

        public string Describe() =>
            $"{Sky}, wind {WindSpeed * 1.94384:F0} kt from {WindFromRad * 180 / Math.PI:F0}, " +
            $"gusting {Gust * 1.94384:F0}, vis {Visibility / 1000:F1} km, " +
            $"cloud base {CloudBase:F0} m, ISA{IsaDeviation:+0;-0}";
    }

    /// <summary>Conditions at a moment on the game clock.</summary>
    public Conditions At(double clockSeconds)
    {
        double h = clockSeconds / 3600.0;

        // Three drivers on incommensurate periods. "Front" is the slow one that decides
        // whether this is a good day or a bad one; "band" moves weather across in hours;
        // "local" is the restlessness inside an afternoon.
        double front = Wave(h, 19.7, 0.11) * 0.6 + Wave(h, 7.3, 0.37) * 0.4;
        double band = Wave(h, 3.1, 0.71);
        double local = Wave(h, 0.83, 0.29);

        // Badness runs 0 (clear and still) to 1 (as bad as it gets).
        double badness = Clamp01(0.42 + front * 0.46 + band * 0.12);

        SkyCondition sky = badness switch
        {
            < 0.22 => SkyCondition.Clear,
            < 0.46 => SkyCondition.Fair,
            < 0.68 => SkyCondition.Overcast,
            < 0.86 => SkyCondition.Rain,
            _ => SkyCondition.Storm,
        };

        // Wind. The direction backs and veers slowly and is NOT tied to badness - a stiff
        // breeze on a clear day is a perfectly ordinary thing and makes a landing
        // interesting without the sky having to look dramatic.
        double windFrom = (Wave(h, 31.0, 0.53) + 1) * Math.PI;
        // Calibrated so an ordinary day is a light breeze rather than a gale. The first
        // pass sat at 15-20 kt as its BASELINE, which makes every hover a handful and
        // leaves no quiet days for the weather to be a contrast against.
        double windSpeed = 0.5 + badness * 10.5 + (band + 1) * 1.15;
        double gust = windSpeed * (0.18 + badness * 0.55) + Math.Abs(local) * 1.4;

        double cover = Clamp01(badness * 1.25 - 0.08);
        double precipitation = badness < 0.62 ? 0 : Clamp01((badness - 0.62) / 0.3);

        // Visibility collapses fast once it starts raining, which is most of why weather
        // matters to someone navigating by looking out of the window.
        double visibility = 24000 - badness * 9000 - precipitation * 12500;

        // Cloud base drops with badness. Kept well clear of the ground until it is
        // genuinely bad, because a low ceiling is the most restrictive thing weather does
        // to a helicopter and it should feel like an event.
        double cloudBase = 2600 - badness * 1900 - precipitation * 450;

        // Warm fronts are the wet ones, so the worst weather is not the coldest.
        double isa = Wave(h, 23.9, 0.19) * 7.0 + badness * 3.0 - 1.0;

        // How deep into Storm territory. Used by the game layer to scale lightning
        // frequency and thunder volume — a storm that just crossed the threshold is
        // not the same as one pegged at maximum.
        double stormIntensity = sky == SkyCondition.Storm
            ? Clamp01((badness - 0.86) / 0.14)
            : 0;

        return new Conditions(sky, windSpeed, windFrom, gust,
                              Math.Max(visibility, 700), Math.Max(cloudBase, 110),
                              isa, precipitation, cover, stormIntensity);
    }

    /// <summary>
    /// One smooth, bounded, seed-dependent oscillation in [-1, 1].
    ///
    /// Two sines at an irrational ratio, so the pair never repeats on any period a player
    /// could notice, and the phase is derived from the seed so different worlds get
    /// different weather.
    /// </summary>
    private double Wave(double hours, double periodHours, double phaseKey)
    {
        double phase = ((Seed * 2654435761u) % 100000) / 100000.0 + phaseKey;
        double a = Math.Sin(2 * Math.PI * (hours / periodHours + phase));
        double b = Math.Sin(2 * Math.PI * (hours / (periodHours * 1.6180339887) + phase * 1.7));
        return (a * 0.62 + b * 0.38);
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}
