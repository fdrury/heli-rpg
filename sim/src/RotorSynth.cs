namespace Rotorwash.Sim;

/// <summary>Everything the synthesiser needs to know about the aircraft this instant.</summary>
public struct SynthState
{
    public double RotorOmega;        // rad/s
    public double NominalOmega;
    public int MainBlades;
    public int TailBlades;
    public double TailGearRatio;
    public double Collective;        // 0..1
    public double TorqueFraction;    // 0..1.4
    public double Airspeed;          // m/s
    public double VerticalSpeed;     // m/s, positive up
    public double N1;                // 0..1.15
    public bool EngineRunning;
    public double BladeLoading;      // Ct/sigma
    public double TipMach;
    public double VrsSeverity;
    public double RotorImbalance;    // 0..1
    public double TailRotorHealth;   // 0..1

    public static SynthState From(Helicopter heli)
    {
        var t = heli.Telemetry;
        return new SynthState
        {
            RotorOmega = heli.RotorOmega,
            NominalOmega = heli.Airframe.MainRotor.NominalOmega,
            MainBlades = heli.Airframe.MainRotor.NumBlades,
            TailBlades = heli.Airframe.TailRotor.NumBlades,
            TailGearRatio = heli.Airframe.TailRotor.GearRatio,
            Collective = heli.Actual.Collective,
            TorqueFraction = t.TorquePercent / 100.0,
            Airspeed = t.AirspeedTrue,
            VerticalSpeed = t.VerticalSpeed,
            N1 = heli.Engine.N1,
            EngineRunning = heli.Engine.State == EngineState.Running,
            BladeLoading = t.BladeLoading,
            TipMach = t.TipMach,
            VrsSeverity = t.VrsSeverity,
            RotorImbalance = heli.Damage.RotorImbalance,
            TailRotorHealth = heli.Damage.TailRotorFactor,
        };
    }
}

/// <summary>
/// Synthesises a helicopter from its own telemetry.
///
/// Not samples, and not for novelty. A helicopter's sound IS its state: the slap rate is
/// the blade-pass frequency, the whine is the gas generator, and the slap turns violent
/// exactly when the advancing tip approaches its drag rise or the rotor starts flying
/// through its own wake. Those are numbers the flight model already produces every step.
/// A sample library cannot follow them - it can only cross-fade between recordings of
/// somebody else's aircraft at somebody else's power setting.
///
/// The payoff is that rotor decay is audible before the gauge moves, a loaded turn
/// changes the note, and an engine failure sounds like one because the freewheel logic
/// driving the physics is the same logic driving the sound.
///
/// Lives in the sim, with no engine dependency, so it can be rendered to a file and
/// listened to without launching the game.
/// </summary>
public sealed class RotorSynth
{
    public int SampleRate { get; }

    private double _bladePhase, _tailPhase, _turbinePhase, _gearPhase;
    private double _slapEnvelope;
    private double _washLp, _windLp, _hissLp, _rumbleLp;
    private double _sRotor, _sCollective, _sTorque, _sAirspeed, _sN1, _sLoading, _sTipMach, _sVrs;
    private double _dcX1, _dcY1;
    private readonly Random _rng;

    public RotorSynth(int sampleRate = 32000, int seed = 20260918)
    {
        SampleRate = sampleRate;
        _rng = new Random(seed);
    }

    /// <summary>Master gain applied after the soft clip.</summary>
    public double Volume { get; set; } = 0.75;

    /// <summary>
    /// Render <paramref name="count"/> mono samples into <paramref name="buffer"/> for the
    /// given aircraft state, which is assumed constant across the block. Blocks are short
    /// (a few milliseconds), so that is not a meaningful approximation, and all oscillator
    /// phase carries across blocks so nothing clicks.
    /// </summary>
    public void Render(Span<float> buffer, int count, in SynthState s, double blockSeconds)
    {
        Smooth(s, blockSeconds);

        double dt = 1.0 / SampleRate;
        double nominal = Math.Max(s.NominalOmega, 1e-3);
        double nr = _sRotor / nominal;

        double bladePassHz = _sRotor / Math.Tau * Math.Max(1, s.MainBlades);
        double tailPassHz = _sRotor * s.TailGearRatio / Math.Tau * Math.Max(1, s.TailBlades);
        double turbineHz = 320.0 + _sN1 * 1450.0;
        double gearHz = _sRotor / Math.Tau * 64.0;

        double running = s.EngineRunning ? 1.0 : 0.0;

        // Blade slap. Loud when the blades are working, when the advancing tip nears its
        // drag rise, and above all when the rotor is flying through its own wake - a
        // descending turn, or the last part of an approach. That is the Huey sound, and
        // it is a flight condition rather than a recording.
        double slapFromLoad = Math.Clamp(_sLoading / 0.12 * 0.55, 0, 1.0);
        double slapFromMach = Math.Clamp((_sTipMach - 0.62) / 0.22, 0, 1.0);
        double slapFromVrs = Math.Clamp(_sVrs * 1.4, 0, 1.0);
        double slapFromDescent = Math.Clamp(-s.VerticalSpeed / 7.0, 0, 1.0) * 0.6;
        // Torque in its own right, not only through blade loading. The transmission and
        // the rotor are both working harder, and a Huey pulling power is unmistakably
        // louder and harder-edged than one loafing along. Without this, going from a
        // quarter torque to an overtorque changed the output level by two and a half per
        // cent, which is to say the player could not hear power at all.
        double slapFromTorque = Math.Clamp((_sTorque - 0.35) / 0.75, 0, 1.0) * 0.55;
        double slap = Math.Clamp(0.30 + slapFromLoad + slapFromMach * 0.9 + slapFromVrs
                                 + slapFromDescent + slapFromTorque, 0, 2.2);

        double washLevel = Math.Clamp(nr * nr * (0.35 + _sCollective * 0.9 + _sTorque * 0.40), 0, 1.8);
        double tailLevel = Math.Clamp(nr * nr * 0.5, 0, 1.0) * Math.Clamp(s.TailRotorHealth, 0, 1);
        double turbineLevel = Math.Clamp(_sN1 * _sN1, 0, 1.2) * (0.35 + running * 0.65);
        double gearLevel = Math.Clamp(nr * _sTorque * 0.5, 0, 0.8);
        double windLevel = Math.Clamp(_sAirspeed / 45.0, 0, 1.4);
        windLevel *= windLevel;
        double imbalance = s.RotorImbalance * 9.0;

        for (int i = 0; i < count; i++)
        {
            _bladePhase += bladePassHz * dt;
            if (_bladePhase >= 1.0) { _bladePhase -= Math.Floor(_bladePhase); _slapEnvelope = 1.0; }

            // Sharp attack, fast decay, with harmonics: that shape is what the ear reads
            // as a "whop" rather than a click.
            _slapEnvelope *= 0.9982;
            double slapTone = Math.Sin(_bladePhase * Math.Tau * 2.0) * 0.6
                            + Math.Sin(_bladePhase * Math.Tau * 3.0) * 0.25;
            double slapSample = _slapEnvelope * _slapEnvelope * slapTone * slap * 0.42;
            _rumbleLp += (slapSample - _rumbleLp) * 0.06;
            slapSample = slapSample * 0.45 + _rumbleLp * 1.5;

            double noise = _rng.NextDouble() * 2.0 - 1.0;
            _washLp += (noise - _washLp) * 0.10;
            double am = 0.55 + 0.45 * Math.Sin(_bladePhase * Math.Tau);
            double wash = _washLp * washLevel * am * 0.30;

            _tailPhase += tailPassHz * dt;
            if (_tailPhase >= 1.0) _tailPhase -= Math.Floor(_tailPhase);
            double tail = (Math.Sin(_tailPhase * Math.Tau) * 0.7
                         + Math.Sin(_tailPhase * Math.Tau * 2.0) * 0.3) * tailLevel * 0.10;

            _turbinePhase += turbineHz * dt;
            if (_turbinePhase >= 1.0) _turbinePhase -= Math.Floor(_turbinePhase);
            double turbine = (Math.Sin(_turbinePhase * Math.Tau) * 0.5
                            + Math.Sin(_turbinePhase * Math.Tau * 2.0) * 0.3
                            + Math.Sin(_turbinePhase * Math.Tau * 3.5) * 0.2) * turbineLevel * 0.055;

            double hissIn = _rng.NextDouble() * 2.0 - 1.0;
            _hissLp += (hissIn - _hissLp) * 0.55;
            turbine += _hissLp * turbineLevel * 0.035;

            _gearPhase += gearHz * dt;
            if (_gearPhase >= 1.0) _gearPhase -= Math.Floor(_gearPhase);
            double gear = Math.Sin(_gearPhase * Math.Tau) * gearLevel * 0.030;

            double windIn = _rng.NextDouble() * 2.0 - 1.0;
            _windLp += (windIn - _windLp) * 0.22;
            double wind = _windLp * windLevel * 0.16;

            double thump = imbalance > 0.001
                ? Math.Sin(_bladePhase * Math.Tau * 0.5) * imbalance * 0.3
                : 0.0;

            double sample = slapSample + wash + tail + turbine + gear + wind + thump;
            sample = Math.Tanh(sample * 1.35) * Volume;

            // Block DC before it leaves.
            //
            // Several of the voices above are not zero-mean - the rumble filter integrates
            // a squared envelope, and tanh of an offset signal is offset further - and the
            // measured output sat about 0.023 away from zero. A constant offset buys
            // nothing audible, eats headroom that the slap transients want, and thumps
            // whenever the stream starts or stops. One-pole high-pass at a fraction of a
            // hertz: inaudible, and the offset is gone.
            double blocked = sample - _dcX1 + 0.9985 * _dcY1;
            _dcX1 = sample;
            _dcY1 = blocked;
            buffer[i] = (float)blocked;
        }
    }

    private void Smooth(in SynthState s, double dt)
    {
        // The sim can step discontinuously - a teleport, an engine failure. Audio cannot.
        double k = 1.0 - Math.Exp(-dt * 11.0);
        double kSlow = 1.0 - Math.Exp(-dt * 4.0);

        _sRotor += (s.RotorOmega - _sRotor) * k;
        _sCollective += (s.Collective - _sCollective) * k;
        _sTorque += (Math.Clamp(s.TorqueFraction, 0, 1.4) - _sTorque) * kSlow;
        _sAirspeed += (s.Airspeed - _sAirspeed) * kSlow;
        _sN1 += (s.N1 - _sN1) * kSlow;
        _sLoading += (s.BladeLoading - _sLoading) * k;
        _sTipMach += (s.TipMach - _sTipMach) * kSlow;
        _sVrs += (s.VrsSeverity - _sVrs) * k;
    }

    /// <summary>Jump the smoothed state straight to the given values, for a cold start.</summary>
    public void Prime(in SynthState s)
    {
        _sRotor = s.RotorOmega;
        _sCollective = s.Collective;
        _sTorque = s.TorqueFraction;
        _sAirspeed = s.Airspeed;
        _sN1 = s.N1;
        _sLoading = s.BladeLoading;
        _sTipMach = s.TipMach;
        _sVrs = s.VrsSeverity;
    }
}
