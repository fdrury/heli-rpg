using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Rain and wind, at the listener rather than at the aircraft.
///
/// A non-positional player, deliberately. Weather is not somewhere - it is everywhere the
/// player is - so attenuating it by distance to the helicopter would be wrong in every
/// situation that matters, most obviously the one where the player has climbed out and
/// walked away from it.
///
/// It also keeps running with the engine shut down, which is the whole point: standing next
/// to a cold aircraft in the rain should not be silent.
/// </summary>
public sealed partial class WeatherAudio : Node
{
    [Export] public float MasterVolume { get; set; } = 0.55f;

    private const int SampleRate = 44100;

    private AudioStreamPlayer _player = null!;
    private AudioStreamGeneratorPlayback? _playback;
    private WeatherSynth _synth = null!;
    private float[] _block = new float[2048];
    private ChaseCamera? _camera;
    private bool _primed;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer
        {
            Name = "Weather",
            Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = 0.25f },
            VolumeDb = Mathf.LinearToDb(MasterVolume),
        };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _synth = new WeatherSynth(SampleRate) { Volume = MasterVolume };
    }

    /// <summary>Trigger a thunder crack from a lightning strike.</summary>
    public void TriggerThunder(double intensity) => _synth?.TriggerThunder(intensity);

    public override void _Process(double delta)
    {
        if (_playback is null) return;

        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        Weather.Conditions c = SceneMood.Now;

        // Inside the cockpit the rain is on the other side of the glass. Finding the camera
        // lazily rather than wiring it in keeps this node independent of how the scene is
        // assembled, which is the only reason it can be added from SceneMood at all.
        _camera ??= GetTree().Root.FindChild("Camera", true, false) as ChaseCamera;
        double shelter = _camera?.Mode == CameraMode.Cockpit ? 1.0 : 0.0;

        if (!_primed)
        {
            _synth.Prime(c.Precipitation, c.WindSpeed, c.Gust, shelter);
            _primed = true;
        }

        if (_block.Length < frames) _block = new float[frames];
        _synth.Render(_block, frames, c.Precipitation, c.WindSpeed, c.Gust, shelter,
                      frames / (double)SampleRate);

        var buffer = new Vector2[frames];
        for (int i = 0; i < frames; i++) buffer[i] = new Vector2(_block[i], _block[i]);
        _playback.PushBuffer(buffer);
    }
}
