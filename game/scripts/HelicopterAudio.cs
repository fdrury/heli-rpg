using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Synthesises the aircraft, live, from the flight model.
///
/// Not samples. A helicopter's sound IS its state: the blade-slap rate is the blade-pass
/// frequency, the whine is the gas generator, the slap gets violent when the advancing
/// blade tip goes transonic or the disc is loaded up in a turn. All of those are numbers
/// the sim already computes every step, and a sample library cannot follow them - it can
/// only cross-fade between recordings of someone else's helicopter at someone else's
/// power setting.
///
/// Synthesising it means rotor speed decay is audible before the instrument moves, a
/// hard turn changes the slap, and an engine failure sounds like an engine failure
/// because the same freewheel logic that drives the physics drives the sound.
///
/// Five voices, all generated into one buffer:
///   BLADE SLAP   impulse train at blade-pass frequency - the "whop"
///   ROTOR WASH   broadband noise, amplitude-modulated at the same rate
///   TAIL ROTOR   a faster, thinner buzz
///   TURBINE      a harmonic stack that rises with the gas generator, plus combustion hiss
///   AIRFLOW      wind over the airframe, scaled by dynamic pressure
/// </summary>
public sealed partial class HelicopterAudio : Node3D
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public float MasterVolume { get; set; } = 0.75f;

    private HelicopterController _heli = null!;
    private AudioStreamPlayer3D _player = null!;
    private AudioStreamGeneratorPlayback _playback = null!;

    private const int SampleRate = 32000;

    /// <summary>
    /// The synthesiser itself lives in the sim, with no engine dependency, so the same
    /// code that makes the noise in game can be rendered to a WAV and listened to:
    ///     dotnet run --project tools/simlab -c Release -- audio
    /// </summary>
    private RotorSynth _synth = null!;
    private float[] _block = new float[2048];
    private bool _primed;

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);

        var generator = new AudioStreamGenerator
        {
            MixRate = SampleRate,
            BufferLength = 0.12f,      // short: audio should track the aircraft closely
        };

        _player = new AudioStreamPlayer3D
        {
            Name = "Engine",
            Stream = generator,
            UnitSize = 26f,
            MaxDistance = 2200f,
            VolumeDb = Mathf.LinearToDb(MasterVolume),
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance,
            // A helicopter is heard long before it is seen, and that is a gameplay fact:
            // anything with ears knows you are coming.
            MaxPolyphony = 1,
        };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _synth = new RotorSynth(SampleRate) { Volume = MasterVolume };
    }

    public override void _Process(double delta)
    {
        if (_playback is null) return;
        GlobalPosition = _heli.GlobalPosition;

        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        SynthState state = SynthState.From(_heli.Sim);
        if (!_primed) { _synth.Prime(state); _primed = true; }

        if (_block.Length < frames) _block = new float[frames];
        _synth.Render(_block, frames, state, frames / (double)SampleRate);

        var buffer = new Vector2[frames];
        for (int i = 0; i < frames; i++) buffer[i] = new Vector2(_block[i], _block[i]);
        _playback.PushBuffer(buffer);
    }
}
