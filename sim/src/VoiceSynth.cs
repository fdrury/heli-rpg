using System;

namespace Rotorwash.Sim;

/// <summary>
/// Synthesises the sound of a man talking on the radio.
///
/// Not speech: nobody will understand the words. The captions carry the meaning; this
/// carries the presence — the rhythm of syllables, the pitch of a voice, the hiss of a
/// carrier between sentences. Through a 300–3400 Hz radio band-pass, the difference
/// between this and real speech is smaller than you would expect: radio already strips
/// most of what makes words intelligible, and what remains is timing and timbre.
///
/// The payoff: landing in range of the Upland Service and hearing a voice between the
/// records is the single most direct proof that someone is up there. The radio is a
/// physical object in the cockpit. It should sound like one.
///
/// <b>How it works.</b> A glottal pulse train at ~105 Hz (low male, fifties) is fed
/// through two formant resonators whose frequencies wander slowly, giving vowel-like
/// colour. Amplitude is modulated at syllable rate, derived from the text's word count.
/// Brief noise bursts at syllable boundaries provide consonant texture. The whole signal
/// is band-passed to 300–3400 Hz (radio channel), soft-clipped, and sits on a faint
/// carrier hiss that is present whenever the station is on air.
///
/// Lives in sim/, with no engine dependency, matching <see cref="RotorSynth"/>,
/// <see cref="WeatherSynth"/> and <see cref="WarningSynth"/>.
/// </summary>
public sealed class VoiceSynth
{
    public int SampleRate { get; }

    /// <summary>Overall voice level. Set so the voice sits just under the rotor.</summary>
    public double Volume { get; set; } = 0.60;

    // ---------------------------------------------------------------- character

    // Hollis Kerr. Fifties, unhurried, a voice that has been talking to itself for years.
    private const double BaseF0 = 105.0;       // fundamental, Hz — low male
    private const double F0Drift = 6.0;        // slow pitch wander, ±Hz
    private const double JitterFrac = 0.022;   // cycle-to-cycle F0 jitter, fraction

    // Formant centres for a neutral male vowel, Hz. The wander around these gives colour.
    private const double F1Centre = 520.0;
    private const double F1Range = 160.0;
    private const double F2Centre = 1450.0;
    private const double F2Range = 380.0;
    private const double F3Freq = 2500.0;      // F3 is relatively stable

    // Syllable rhythm. 153 wpm at ~1.4 syllables/word ≈ 3.6 syl/s.
    private const double SylPerWord = 1.42;

    // Radio channel
    private const double HissLevel = 0.035;    // carrier hiss when on air

    // ---------------------------------------------------------------- state

    private readonly Random _rng;

    // Glottal source
    private double _glotPhase;
    private double _pulseEnv;
    private double _currentF0;
    private double _driftPhase;

    // Formant resonators (two-pole)
    private double _f1y1, _f1y2;
    private double _f2y1, _f2y2;
    private double _f3y1, _f3y2;
    private double _vowelPhase;

    // Syllable envelope
    private double _sylPhase;
    private double _sylRate;           // Hz, derived from text
    private double _envSmooth;         // smoothed envelope
    private double _phrasePhase;       // slower phrasing modulation

    // Speaking state
    private bool _speaking;
    private double _elapsed;
    private double _duration;
    private double _fadeOut;           // ramp to silence at segment end

    // Radio channel filters
    private double _hpX1, _hpY1;      // high-pass 300 Hz
    private double _lpState;           // low-pass 3400 Hz
    private double _hissLp;            // filtered carrier noise

    // DC blocker
    private double _dcX1, _dcY1;

    // On-air flag: carrier hiss even between sentences
    private bool _onAir;

    public VoiceSynth(int sampleRate = 44100, int seed = 42)
    {
        SampleRate = sampleRate;
        _rng = new Random(seed);
        _currentF0 = BaseF0;
    }

    /// <summary>True while a segment is being voiced.</summary>
    public bool Speaking => _speaking;

    /// <summary>Whether the station is on air (carrier hiss between sentences).</summary>
    public bool OnAir { get => _onAir; set => _onAir = value; }

    /// <summary>
    /// Begin voicing a segment. Duration is from <see cref="RadioDj.ReadSeconds"/>.
    /// Phases carry across segments so the transition is smooth.
    /// </summary>
    public void Speak(string text, double durationSeconds)
    {
        _speaking = true;
        _elapsed = 0;
        _duration = Math.Max(0.5, durationSeconds);
        _fadeOut = 1.0;

        // Derive syllable rate from text
        int words = 1;
        if (!string.IsNullOrWhiteSpace(text))
            for (int i = 0; i < text.Length; i++) if (text[i] == ' ') words++;
        double syllables = words * SylPerWord;
        _sylRate = syllables / _duration;
    }

    /// <summary>Stop voicing. The carrier hiss continues if <see cref="OnAir"/>.</summary>
    public void Stop()
    {
        _speaking = false;
        _elapsed = 0;
    }

    /// <summary>
    /// Render <paramref name="count"/> mono samples. Outputs carrier hiss when on air but
    /// not speaking, voice when speaking, and silence otherwise.
    /// </summary>
    public void Render(Span<float> buffer, int count)
    {
        double dt = 1.0 / SampleRate;

        // Prosody: pitch descends through a sentence (declarative contour)
        double pitchMul = _speaking && _duration > 0
            ? 1.0 - 0.06 * Math.Clamp(_elapsed / _duration, 0, 1)  // 6% drop
            : 1.0;

        // Slow drift in F0 (breathing, natural variation)
        double driftF0 = BaseF0 + F0Drift * Math.Sin(_driftPhase * Math.Tau);

        // Formant targets (wander through vowel space)
        double f1 = F1Centre + F1Range * Math.Sin(_vowelPhase * Math.Tau);
        double f2 = F2Centre + F2Range * Math.Sin(_vowelPhase * Math.Tau * 1.73 + 0.8);

        // Two-pole resonator coefficients: y[n] = x + 2r·cos(θ)·y[n-1] - r²·y[n-2]
        // Bandwidth controls resonance sharpness.
        double f1a, f1b, f2a, f2b, f3a, f3b;
        ResonatorCoeffs(f1, 100.0, out f1a, out f1b);
        ResonatorCoeffs(f2, 140.0, out f2a, out f2b);
        ResonatorCoeffs(F3Freq, 180.0, out f3a, out f3b);

        // High-pass coefficient for radio 300 Hz
        double hpAlpha = 1.0 / (1.0 + SampleRate / (Math.Tau * 300.0));
        // Low-pass coefficient for radio 3400 Hz
        double lpAlpha = 1.0 - Math.Exp(-Math.Tau * 3400.0 / SampleRate);

        bool active = _speaking || _onAir;

        for (int i = 0; i < count; i++)
        {
            double voice = 0;

            if (_speaking)
            {
                // --- Glottal source ------------------------------------------
                // A pulse train with harmonics. Each glottal cycle is a sharp
                // attack with fast decay, like blade slap but at speech pitch.
                double f0 = driftF0 * pitchMul;
                f0 *= 1.0 + (_rng.NextDouble() - 0.5) * JitterFrac * 2.0;
                _currentF0 = f0;

                _glotPhase += f0 * dt;
                if (_glotPhase >= 1.0)
                {
                    _glotPhase -= Math.Floor(_glotPhase);
                    _pulseEnv = 1.0;
                }

                // Glottal pulse: ~40% open quotient, fast decay
                _pulseEnv *= 1.0 - dt * f0 * 2.5;
                if (_pulseEnv < 0) _pulseEnv = 0;

                // Rich harmonics from a shaped pulse
                double glottal = _pulseEnv * _pulseEnv
                    * (Math.Sin(_glotPhase * Math.Tau)
                     + Math.Sin(_glotPhase * Math.Tau * 2.0) * 0.6
                     + Math.Sin(_glotPhase * Math.Tau * 3.0) * 0.35
                     + Math.Sin(_glotPhase * Math.Tau * 4.0) * 0.20);

                // --- Formant filtering ---------------------------------------
                double f1out = glottal + f1a * _f1y1 + f1b * _f1y2;
                _f1y2 = _f1y1; _f1y1 = f1out;

                double f2out = glottal + f2a * _f2y1 + f2b * _f2y2;
                _f2y2 = _f2y1; _f2y1 = f2out;

                double f3out = glottal + f3a * _f3y1 + f3b * _f3y2;
                _f3y2 = _f3y1; _f3y1 = f3out;

                voice = f1out * 0.48 + f2out * 0.32 + f3out * 0.20;

                // --- Syllable envelope ---------------------------------------
                // Oscillating at syllable rate with phrasing modulation. The
                // rectified sine gives attack-sustain-decay per syllable; the
                // slower phrase wave groups them into breath units.
                _sylPhase += _sylRate * dt;
                if (_sylPhase >= 1.0) _sylPhase -= Math.Floor(_sylPhase);

                _phrasePhase += 0.35 * dt;  // ~0.35 Hz phrasing
                if (_phrasePhase >= 1.0) _phrasePhase -= Math.Floor(_phrasePhase);

                // Syllable: a raised sine that never quite reaches zero
                double sylPulse = 0.30 + 0.70 * Math.Abs(Math.Sin(_sylPhase * Math.PI));

                // Phrase: gentle grouping, never cutting below 0.5
                double phrase = 0.65 + 0.35 * Math.Sin(_phrasePhase * Math.Tau);

                double targetEnv = sylPulse * phrase;
                _envSmooth += (targetEnv - _envSmooth) * (1.0 - Math.Exp(-dt * 35.0));

                // Consonant noise at syllable boundaries (near zero-crossings)
                double sylEdge = Math.Max(0, 1.0 - Math.Abs(Math.Sin(_sylPhase * Math.PI)) * 4.0);
                double noise = (_rng.NextDouble() * 2.0 - 1.0) * sylEdge * 0.18;

                // Fade out over the last 80ms to avoid a click at segment end
                double remain = _duration - _elapsed;
                _fadeOut = remain < 0.08 ? Math.Clamp(remain / 0.08, 0, 1) : 1.0;

                voice = (voice * _envSmooth + noise) * _fadeOut;

                _elapsed += dt;
                if (_elapsed >= _duration) _speaking = false;
            }
            else
            {
                // Resonators decay naturally; no need to zero them
                _f1y1 *= 0.9995; _f1y2 *= 0.9995;
                _f2y1 *= 0.9995; _f2y2 *= 0.9995;
                _f3y1 *= 0.9995; _f3y2 *= 0.9995;
                _envSmooth *= 0.999;
            }

            // --- Carrier hiss ------------------------------------------------
            double hiss = 0;
            if (active)
            {
                double hn = _rng.NextDouble() * 2.0 - 1.0;
                _hissLp += (hn - _hissLp) * 0.35;
                hiss = _hissLp * HissLevel;
            }

            double raw = voice + hiss;

            // --- Radio band-pass 300–3400 Hz ---------------------------------
            // High-pass at 300 Hz (one-pole)
            double hpOut = (1.0 - hpAlpha) * (_hpY1 + raw - _hpX1);
            _hpX1 = raw;
            _hpY1 = hpOut;

            // Low-pass at 3400 Hz (one-pole)
            _lpState += (hpOut - _lpState) * lpAlpha;

            double sample = Math.Tanh(_lpState * 1.8) * Volume;

            // DC blocker — same as every other synth in the project
            double blocked = sample - _dcX1 + 0.9985 * _dcY1;
            _dcX1 = sample;
            _dcY1 = blocked;
            buffer[i] = (float)blocked;
        }

        // Advance slow modulators
        double blockSec = count * dt;
        _driftPhase += 0.12 * blockSec;   // ~0.12 Hz pitch wander
        if (_driftPhase >= 1.0) _driftPhase -= Math.Floor(_driftPhase);
        _vowelPhase += 0.9 * blockSec;    // ~0.9 Hz vowel wander
        if (_vowelPhase >= 1.0) _vowelPhase -= Math.Floor(_vowelPhase);
    }

    private void ResonatorCoeffs(double freqHz, double bwHz,
                                  out double a, out double b)
    {
        double theta = Math.Tau * freqHz / SampleRate;
        double r = Math.Exp(-Math.PI * bwHz / SampleRate);
        a = 2.0 * r * Math.Cos(theta);
        b = -(r * r);
    }
}
