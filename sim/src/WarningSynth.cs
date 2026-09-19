using System;

namespace Rotorwash.Sim;

/// <summary>
/// The sound of the caution and warning system: a low-rotor horn, a master warning beeper,
/// and a master caution chime.
///
/// Engine-free and deterministic, like <see cref="RotorSynth"/> and
/// <see cref="WeatherSynth"/>, so it can be rendered into an array and measured rather than
/// listened to. That matters more here than anywhere else in the audio: the whole purpose
/// of these tones is that a pilot recognises them <i>without looking</i>, and "are these
/// two sounds actually distinguishable" is a question with a number for an answer.
///
/// Three voices, separated on the two axes an ear uses first - pitch and rhythm - so that
/// no two of them can be confused even through a headset and a rotor:
///
/// <list type="bullet">
/// <item><b>Low-rotor horn</b>, 400 Hz, continuous, with odd harmonics so it reads as a
/// horn rather than a test tone. This is the one every helicopter pilot knows, and on the
/// real aircraft there is no button that stops it - only putting the rotor back does.</item>
/// <item><b>Master warning</b>, 950 Hz, pulsed at 3.3 Hz with a 45% duty cycle. More than
/// an octave above the horn and rhythmically the opposite of it.</item>
/// <item><b>Master caution</b>, a decaying two-tone chime at 660 and 990 Hz - a perfect
/// fifth, which is consonant where the other two are deliberately not. It fires once per
/// new caution and gets out of the way; a caution that nags is a caution that gets
/// ignored.</item>
/// </list>
///
/// <b>What did not work.</b> The first horn was a pure 400 Hz sine, and against the rotor
/// synth's own output it simply vanished - a helicopter cabin is broadband noise with a
/// strong low end, and one sine in the middle of it is not an alert. Odd harmonics at 1/3
/// and 1/5 amplitude (a softened square) put energy at 1.2 and 2.0 kHz where there is
/// room, and the measured 400 Hz fundamental is unchanged. The second attempt at the
/// master warning used 440 Hz to sit near the horn "so they would feel like a family",
/// which is exactly wrong: the two ended up a measured 40 Hz apart in dominant frequency
/// and the only thing separating them was the rhythm. They are now 400 and 950.
/// </summary>
public sealed class WarningSynth
{
    public int SampleRate { get; }

    /// <summary>Overall level. Loud enough to cut through the rotor, not loud enough to hurt.</summary>
    public double Volume { get; set; } = 0.62;

    // --- Voice constants, quoted because the tests assert against them ---------

    /// <summary>Fundamental of the low-rotor horn, Hz.</summary>
    public const double HornHz = 400.0;

    /// <summary>Fundamental of the master warning beeper, Hz.</summary>
    public const double WarningHz = 950.0;

    /// <summary>Repetitions per second of the master warning beeper.</summary>
    public const double WarningRepHz = 3.3;

    /// <summary>Fraction of each repetition the master warning beeper is on.</summary>
    public const double WarningDuty = 0.45;

    /// <summary>The two tones of the master caution chime, Hz.</summary>
    public const double CautionLowHz = 660.0;
    public const double CautionHighHz = 990.0;

    private double _hornPhase, _warnPhase, _warnRepPhase;
    private double _chimeLowPhase, _chimeHighPhase, _chimeEnv;
    private double _gateHorn, _gateWarn;
    private double _dcX1, _dcY1;

    public WarningSynth(int sampleRate = 44100) => SampleRate = sampleRate;

    /// <summary>
    /// Fire the master caution chime. Call once per newly raised caution; it decays on its
    /// own, the same way <see cref="WeatherSynth.TriggerThunder"/> does.
    /// </summary>
    public void TriggerCaution(double intensity = 1.0)
    {
        // Restart the tones so a retrigger has an attack rather than a step in level.
        _chimeLowPhase = _chimeHighPhase = 0;
        _chimeEnv = Math.Clamp(intensity, 0, 1);
    }

    /// <summary>Silence everything and forget the phases. For a spawn or a scene change.</summary>
    public void Reset()
    {
        _hornPhase = _warnPhase = _warnRepPhase = 0;
        _chimeLowPhase = _chimeHighPhase = _chimeEnv = 0;
        _gateHorn = _gateWarn = 0;
        _dcX1 = _dcY1 = 0;
    }

    /// <summary>
    /// Take the gates and the chime trigger straight off the system. The caller still has
    /// to call <see cref="Render"/>; this only decides what it will render.
    /// </summary>
    public void Follow(CautionWarningSystem cws)
    {
        _followHorn = cws.LowRotorHorn;
        _followWarn = cws.WarningTone;
        // One chime per raise, not one per frame: the raise list is empty on every update
        // except the one where a condition actually crossed. This is the same latching
        // discipline as the panel, applied to the speaker.
        for (int i = 0; i < cws.Raised.Count; i++)
            if (cws.Raised[i].Severity >= WarningSeverity.Caution) TriggerCaution();
    }

    private bool _followHorn, _followWarn;

    /// <summary>Render a block using whatever <see cref="Follow"/> last saw.</summary>
    public void Render(Span<float> buffer, int count, double blockSeconds)
        => Render(buffer, count, _followHorn, _followWarn, blockSeconds);

    /// <summary>
    /// Render <paramref name="count"/> mono samples.
    /// </summary>
    /// <param name="lowRotorHorn">Rotor RPM is below the warning threshold.</param>
    /// <param name="masterWarning">
    /// Any other Warning-severity condition. Mutually exclusive with the horn at the
    /// source, because two continuous tones at once is mush rather than information.
    /// </param>
    public void Render(Span<float> buffer, int count, bool lowRotorHorn, bool masterWarning,
                       double blockSeconds)
    {
        double dt = 1.0 / SampleRate;

        // Gate ramps. 12 ms is fast enough to feel instant and slow enough that a tone
        // starting or stopping does not click - and a click on a warning tone is the one
        // artefact a player would hear every single time.
        double gateK = 1.0 - Math.Exp(-dt / 0.012);
        double hornTarget = lowRotorHorn ? 1.0 : 0.0;
        double warnTarget = masterWarning ? 1.0 : 0.0;

        for (int i = 0; i < count; i++)
        {
            _gateHorn += (hornTarget - _gateHorn) * gateK;
            _gateWarn += (warnTarget - _gateWarn) * gateK;

            // --- Low-rotor horn ---------------------------------------------
            // Fundamental plus third and fifth at 1/3 and 1/5 amplitude: a softened
            // square, which is what a real annunciator horn is.
            _hornPhase += HornHz * dt;
            if (_hornPhase >= 1.0) _hornPhase -= Math.Floor(_hornPhase);
            double horn = (Math.Sin(_hornPhase * Math.Tau)
                         + Math.Sin(_hornPhase * Math.Tau * 3.0) / 3.0
                         + Math.Sin(_hornPhase * Math.Tau * 5.0) / 5.0) * _gateHorn * 0.42;

            // --- Master warning beeper --------------------------------------
            _warnRepPhase += WarningRepHz * dt;
            if (_warnRepPhase >= 1.0) _warnRepPhase -= Math.Floor(_warnRepPhase);
            double beep = PulseEnvelope(_warnRepPhase, WarningDuty, 0.035);

            _warnPhase += WarningHz * dt;
            if (_warnPhase >= 1.0) _warnPhase -= Math.Floor(_warnPhase);
            double warn = (Math.Sin(_warnPhase * Math.Tau)
                         + Math.Sin(_warnPhase * Math.Tau * 2.0) * 0.22)
                          * beep * _gateWarn * 0.40;

            // --- Master caution chime ---------------------------------------
            double chime = 0;
            if (_chimeEnv > 1e-5)
            {
                _chimeLowPhase += CautionLowHz * dt;
                if (_chimeLowPhase >= 1.0) _chimeLowPhase -= Math.Floor(_chimeLowPhase);
                _chimeHighPhase += CautionHighHz * dt;
                if (_chimeHighPhase >= 1.0) _chimeHighPhase -= Math.Floor(_chimeHighPhase);

                chime = (Math.Sin(_chimeLowPhase * Math.Tau)
                       + Math.Sin(_chimeHighPhase * Math.Tau) * 0.75) * _chimeEnv * 0.34;

                // ~0.35 s time constant: audible for about a second, gone before the
                // player has finished turning their head.
                _chimeEnv *= 1.0 - dt / 0.35;
                if (_chimeEnv < 1e-5) _chimeEnv = 0;
            }

            double sample = Math.Tanh((horn + warn + chime) * 1.15) * Volume;

            // Same DC blocker as the other two synths. Nothing here is badly offset, but
            // tanh of an asymmetric sum is offset a little, and a stream that starts and
            // stops as often as this one does must not thump when it does.
            double blocked = sample - _dcX1 + 0.9985 * _dcY1;
            _dcX1 = sample;
            _dcY1 = blocked;
            buffer[i] = (float)blocked;
        }
    }

    /// <summary>
    /// The on/off envelope of one beep, with raised-cosine edges.
    ///
    /// A hard gate on a 950 Hz tone is a click at both ends, and the click is louder and
    /// more annoying than the tone. <paramref name="edge"/> is a fraction of the whole
    /// repetition period, so the ramps scale with the rate rather than needing retuning.
    /// </summary>
    private static double PulseEnvelope(double phase, double duty, double edge)
    {
        if (phase >= duty) return 0.0;
        if (phase < edge) return 0.5 - 0.5 * Math.Cos(Math.PI * phase / edge);
        if (phase > duty - edge) return 0.5 - 0.5 * Math.Cos(Math.PI * (duty - phase) / edge);
        return 1.0;
    }
}
