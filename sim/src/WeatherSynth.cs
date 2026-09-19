using System;

namespace Rotorwash.Sim;

/// <summary>
/// The sound of the weather: rain, and wind over the airframe.
///
/// Separate from <see cref="RotorSynth"/> on purpose. The rotor is the aircraft and follows
/// its telemetry; this follows the world, carries on when the engine is shut down, and has
/// to be audible from inside a cockpit whose own machine has gone quiet. Mixing it into the
/// rotor voice would tie the weather's volume to the rotor's.
///
/// Both voices are filtered noise, because that is what they are. Rain is a dense hiss with
/// a slow irregular swell, band-limited so it does not read as tape hiss; wind is much lower
/// and much slower, and gusts are the part the ear actually notices - steady wind fades into
/// the background within seconds, while a gust arriving is information.
/// </summary>
public sealed class WeatherSynth
{
    public int SampleRate { get; }

    /// <summary>Overall level. The world should never drown the aircraft.</summary>
    public double Volume { get; set; } = 0.55;

    private readonly Random _rng = new(7717);

    // Rain: two one-pole filters make a band-pass, plus a slow swell.
    private double _rainLp, _rainHp, _swellPhase;

    // Wind: a much slower low-pass, plus a gust envelope.
    private double _windLp, _windLp2, _gustPhase;

    // Smoothed inputs, so a weather change does not step the level.
    private double _sRain, _sWind, _sGust, _sShelter;

    private double _dcX1, _dcY1;

    public WeatherSynth(int sampleRate = 44100) => SampleRate = sampleRate;

    /// <summary>
    /// Render a block.
    /// </summary>
    /// <param name="precipitation">0..1 from the weather model.</param>
    /// <param name="windSpeed">Steady wind, m/s.</param>
    /// <param name="gust">Gust intensity, m/s.</param>
    /// <param name="shelter">
    /// 0 outside, 1 fully enclosed. Inside the cockpit the rain is on the other side of the
    /// glass: quieter, and with the top end taken off it, which is most of what "inside"
    /// sounds like.
    /// </param>
    public void Render(Span<float> buffer, int count, double precipitation, double windSpeed,
                       double gust, double shelter, double blockSeconds)
    {
        double k = 1.0 - Math.Exp(-blockSeconds * 6.0);
        _sRain += (Math.Clamp(precipitation, 0, 1) - _sRain) * k;
        _sWind += (Math.Clamp(windSpeed / 20.0, 0, 1.4) - _sWind) * k;
        _sGust += (Math.Clamp(gust / 14.0, 0, 1.2) - _sGust) * k;
        _sShelter += (Math.Clamp(shelter, 0, 1) - _sShelter) * k;

        double dt = 1.0 / SampleRate;
        double muffle = 1.0 - _sShelter * 0.72;
        double rainLevel = _sRain * (1.0 - _sShelter * 0.45);
        double windLevel = _sWind * (1.0 - _sShelter * 0.35);

        for (int i = 0; i < count; i++)
        {
            // --- Rain -------------------------------------------------------
            double n = _rng.NextDouble() * 2.0 - 1.0;
            // Band-pass: low-pass then subtract a slower low-pass.
            _rainLp += (n - _rainLp) * (0.42 * muffle + 0.08);
            _rainHp += (_rainLp - _rainHp) * 0.010;
            double rain = (_rainLp - _rainHp);

            // Slow swell, so a downpour breathes instead of sitting still.
            _swellPhase += 0.21 * dt;
            if (_swellPhase >= 1.0) _swellPhase -= 1.0;
            double swell = 0.78 + 0.22 * Math.Sin(_swellPhase * Math.Tau)
                                 * Math.Sin(_swellPhase * Math.Tau * 2.7);
            rain *= rainLevel * swell * 0.55;

            // --- Wind -------------------------------------------------------
            double wn = _rng.NextDouble() * 2.0 - 1.0;
            _windLp += (wn - _windLp) * 0.045;
            _windLp2 += (_windLp - _windLp2) * 0.020;

            _gustPhase += 0.13 * dt;
            if (_gustPhase >= 1.0) _gustPhase -= 1.0;
            double gustEnv = 1.0 + _sGust * 1.6 *
                             Math.Max(0, Math.Sin(_gustPhase * Math.Tau)
                                       * Math.Sin(_gustPhase * Math.Tau * 1.61));
            double wind = _windLp2 * windLevel * gustEnv * 0.42;

            double sample = Math.Tanh((rain + wind) * 1.2) * Volume;

            double blocked = sample - _dcX1 + 0.9985 * _dcY1;
            _dcX1 = sample;
            _dcY1 = blocked;
            buffer[i] = (float)blocked;
        }
    }

    /// <summary>Jump the smoothed inputs straight to a condition, so the first block is right.</summary>
    public void Prime(double precipitation, double windSpeed, double gust, double shelter)
    {
        _sRain = Math.Clamp(precipitation, 0, 1);
        _sWind = Math.Clamp(windSpeed / 20.0, 0, 1.4);
        _sGust = Math.Clamp(gust / 14.0, 0, 1.2);
        _sShelter = Math.Clamp(shelter, 0, 1);
    }
}
