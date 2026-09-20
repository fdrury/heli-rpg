using System;

namespace Rotorwash.Sim;

/// <summary>
/// The sound a place makes.
///
/// A settlement with three hundred people has generators running, metal creaking, voices
/// carrying. A wreck field has wind through broken airframes and the tick of cooling metal.
/// A relay mast hums with electrical load. A fuel cache has pumps. An overlook has nothing
/// but wind and birds — and that silence IS the sound, because the player just left a place
/// that had all of those things.
///
/// Like every other synth in this project, everything is generated from parameters: site
/// kind, weather, time of day, and shelter. No samples. The voices are filtered noise,
/// resonant tones, and slow modulations — the same toolkit as <see cref="WeatherSynth"/>
/// and <see cref="RotorSynth"/>, applied to a different question.
///
/// Lives in the sim, no Godot dependency, testable from simlab.
/// </summary>
public sealed class AmbientSynth
{
    public int SampleRate { get; }
    public double Volume { get; set; } = 0.45;

    private readonly Random _rng;

    // Generator hum: a low-frequency resonance, the sound of a diesel or a turbine
    // running somewhere in the settlement. Irregular, because generators in a post-
    // apocalyptic world are not well-maintained.
    private double _genPhase, _genDrift;

    // Metal creaking: irregular impulsive sounds, like thermal expansion or wind-loaded
    // sheet metal. Random envelope hits, fast decay.
    private double _creakEnv, _creakLp;

    // Electrical hum: 50/60 Hz buzz from transformers and power lines.
    private double _humPhase;

    // Activity murmur: very low, filtered noise that reads as distant voices or movement.
    private double _murmurLp1, _murmurLp2;

    // Wind interaction: site-specific wind noise, different from WeatherSynth's open-air wind.
    // This is wind through structures, whistling through gaps, rattling loose panels.
    private double _whistleLp, _rattleEnv;
    private double _whistlePhase;

    // Cooling tick: regular metallic pings, like an engine block cooling after shutdown.
    private double _tickTimer;

    // Smoothed inputs
    private double _sActivity, _sWind, _sMetal, _sElectrical, _sShelter;
    private double _sTick;

    // DC blocker
    private double _dcX1, _dcY1;

    public AmbientSynth(int sampleRate = 44100, int seed = 55301)
    {
        SampleRate = sampleRate;
        _rng = new Random(seed);
    }

    /// <summary>Parameters that describe what kind of place this is.</summary>
    public struct SiteVoice
    {
        /// <summary>How much human activity: 0 = abandoned, 1 = busy settlement.</summary>
        public double Activity;

        /// <summary>How much metal: wrecks, sheds, industrial structures.</summary>
        public double Metal;

        /// <summary>How much electrical equipment: relays, powered sites.</summary>
        public double Electrical;

        /// <summary>Cooling tick intensity: freshly shut-down aircraft nearby.</summary>
        public double CoolingTick;

        /// <summary>
        /// Build a voice profile from a site kind.
        /// </summary>
        public static SiteVoice For(int siteKind)
        {
            // Matches the SiteKind enum order in WorldMap.cs:
            // FuelCache=0, Settlement=1, Workshop=2, Wreck=3, Relay=4, Depot=5, Airfield=6, Farmstead=7, Overlook=8
            return siteKind switch
            {
                0 => new SiteVoice { Activity = 0.05, Metal = 0.3, Electrical = 0.0 },     // FuelCache: pumps, drums
                1 => new SiteVoice { Activity = 0.7, Metal = 0.2, Electrical = 0.15 },      // Settlement: people, generators
                2 => new SiteVoice { Activity = 0.4, Metal = 0.5, Electrical = 0.2 },       // Workshop: tools, metal
                3 => new SiteVoice { Activity = 0.0, Metal = 0.7, Electrical = 0.0 },       // Wreck: creaking hulks
                4 => new SiteVoice { Activity = 0.0, Metal = 0.15, Electrical = 0.7 },      // Relay: transformer hum
                5 => new SiteVoice { Activity = 0.15, Metal = 0.6, Electrical = 0.1 },      // Depot: warehouses, metal
                6 => new SiteVoice { Activity = 0.2, Metal = 0.4, Electrical = 0.25 },      // Airfield: hangars, old equipment
                7 => new SiteVoice { Activity = 0.3, Metal = 0.15, Electrical = 0.0 },      // Farmstead: rural quiet
                8 => new SiteVoice { Activity = 0.0, Metal = 0.0, Electrical = 0.0 },       // Overlook: wind and sky
                _ => new SiteVoice(),
            };
        }
    }

    /// <summary>
    /// Render a block of ambient audio for a site.
    /// </summary>
    /// <param name="buffer">Output buffer, mono.</param>
    /// <param name="count">Number of samples to render.</param>
    /// <param name="voice">What kind of place this is.</param>
    /// <param name="windSpeed">Wind speed, m/s. Drives wind-through-structures sounds.</param>
    /// <param name="shelter">0 outside, 1 fully enclosed.</param>
    /// <param name="blockSeconds">Duration of this block in seconds.</param>
    public void Render(Span<float> buffer, int count, in SiteVoice voice,
                       double windSpeed, double shelter, double blockSeconds)
    {
        double k = 1.0 - Math.Exp(-blockSeconds * 4.0);
        _sActivity += (voice.Activity - _sActivity) * k;
        _sWind += (Math.Clamp(windSpeed / 15.0, 0, 1.2) - _sWind) * k;
        _sMetal += (voice.Metal - _sMetal) * k;
        _sElectrical += (voice.Electrical - _sElectrical) * k;
        _sShelter += (Math.Clamp(shelter, 0, 1) - _sShelter) * k;
        _sTick += (voice.CoolingTick - _sTick) * k;

        double dt = 1.0 / SampleRate;

        for (int i = 0; i < count; i++)
        {
            double sample = 0;

            // --- Generator hum --------------------------------------------------
            // A low, irregular drone. Two harmonics of a drifting fundamental around
            // 55-65 Hz, which is where small diesel generators live.
            if (_sActivity > 0.01)
            {
                _genDrift += (_rng.NextDouble() * 2.0 - 1.0) * dt * 2.0;
                _genDrift *= 0.9997;
                double freq = 58.0 + _genDrift * 8.0;
                _genPhase += freq * dt;
                if (_genPhase >= 1.0) _genPhase -= Math.Floor(_genPhase);

                double gen = Math.Sin(_genPhase * Math.Tau) * 0.5
                           + Math.Sin(_genPhase * Math.Tau * 2.0) * 0.25
                           + Math.Sin(_genPhase * Math.Tau * 3.0) * 0.12;
                sample += gen * _sActivity * 0.18;
            }

            // --- Activity murmur ------------------------------------------------
            // Very soft, slow-modulated noise that reads as distant voices, footsteps,
            // and the general hum of a working place.
            if (_sActivity > 0.01)
            {
                double n = _rng.NextDouble() * 2.0 - 1.0;
                _murmurLp1 += (n - _murmurLp1) * 0.035;
                _murmurLp2 += (_murmurLp1 - _murmurLp2) * 0.025;
                sample += _murmurLp2 * _sActivity * 0.22;
            }

            // --- Metal creak ----------------------------------------------------
            // Impulsive: random triggers, fast attack, ~0.3 s decay. Sheet metal
            // expanding in the sun or flexing in the wind.
            if (_sMetal > 0.01)
            {
                // Random trigger at a rate proportional to wind and metal presence
                double triggerRate = (0.3 + _sWind * 2.5) * _sMetal;
                if (_rng.NextDouble() < triggerRate * dt)
                    _creakEnv = 0.4 + _rng.NextDouble() * 0.6;

                _creakEnv *= 1.0 - dt * 4.5;
                if (_creakEnv < 0.001) _creakEnv = 0;

                double cn = _rng.NextDouble() * 2.0 - 1.0;
                _creakLp += (cn - _creakLp) * 0.25;
                sample += _creakLp * _creakEnv * _sMetal * 0.35;
            }

            // --- Electrical hum -------------------------------------------------
            // A clean 50 Hz buzz with harmonics. Relays and transformers.
            if (_sElectrical > 0.01)
            {
                _humPhase += 50.0 * dt;
                if (_humPhase >= 1.0) _humPhase -= Math.Floor(_humPhase);
                double hum = Math.Sin(_humPhase * Math.Tau) * 0.6
                           + Math.Sin(_humPhase * Math.Tau * 2.0) * 0.3
                           + Math.Sin(_humPhase * Math.Tau * 4.0) * 0.1;
                sample += hum * _sElectrical * 0.14;
            }

            // --- Wind through structures ----------------------------------------
            // Different from WeatherSynth's open-air wind. This is wind interacting
            // with buildings: whistling through gaps, rattling loose panels.
            if (_sWind > 0.01 && (_sMetal > 0.01 || _sActivity > 0.01))
            {
                double structures = Math.Max(_sMetal, _sActivity * 0.5);

                // Whistle: a resonant tone that drifts with the wind
                double wFreq = 380.0 + _sWind * 220.0 + Math.Sin(_whistlePhase * 0.13) * 60.0;
                _whistlePhase += dt;
                double wn = _rng.NextDouble() * 2.0 - 1.0;
                _whistleLp += (wn - _whistleLp) * Math.Clamp(wFreq / SampleRate * Math.Tau, 0, 0.5);
                sample += _whistleLp * _sWind * structures * 0.12;

                // Rattle: intermittent impulsive rattling
                double rattleRate = _sWind * structures * 1.5;
                if (_rng.NextDouble() < rattleRate * dt)
                    _rattleEnv = 0.3 + _rng.NextDouble() * 0.7;
                _rattleEnv *= 1.0 - dt * 8.0;
                if (_rattleEnv < 0.001) _rattleEnv = 0;
                double rn = _rng.NextDouble() * 2.0 - 1.0;
                sample += rn * _rattleEnv * _sWind * structures * 0.08;
            }

            // --- Cooling tick ---------------------------------------------------
            // Regular metallic pings, decelerating. The aircraft is cooling down.
            if (_sTick > 0.01)
            {
                _tickTimer -= dt;
                if (_tickTimer <= 0)
                {
                    _tickTimer = 0.8 + _rng.NextDouble() * 2.2;  // irregular interval
                    // A brief, high-frequency ping
                    _creakEnv = Math.Max(_creakEnv, _sTick * 0.8);
                }
            }

            // Shelter reduces everything except the generator (which is inside too)
            double shelterFade = 1.0 - _sShelter * 0.45;
            sample *= shelterFade;

            sample = Math.Tanh(sample * 1.4) * Volume;

            // DC blocker
            double blocked = sample - _dcX1 + 0.9985 * _dcY1;
            _dcX1 = sample;
            _dcY1 = blocked;
            buffer[i] = (float)blocked;
        }
    }

    /// <summary>Jump smoothed inputs to the given values for a cold start.</summary>
    public void Prime(in SiteVoice voice, double windSpeed, double shelter)
    {
        _sActivity = voice.Activity;
        _sWind = Math.Clamp(windSpeed / 15.0, 0, 1.2);
        _sMetal = voice.Metal;
        _sElectrical = voice.Electrical;
        _sShelter = Math.Clamp(shelter, 0, 1);
        _sTick = voice.CoolingTick;
    }
}
