using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Drives the world's acoustic character from the terrain around the listener.
///
/// Two effects on the "Reverb" audio bus:
///   1. AudioEffectReverb — early reflections and tail, sized by <see cref="AcousticSpace"/>.
///   2. AudioEffectLowPassFilter — distance character: the helicopter loses its top end
///      with distance, which is why you hear one minutes before you can place it.
///
/// Updated once per physics frame, smoothed so changes do not click. The reverb bus is
/// created in code rather than in the Godot editor so it does not require a .tres file
/// that another agent might be editing.
///
/// Added to the scene tree by <see cref="SceneMood.Apply"/>, same as WeatherAudio and
/// CockpitRadio: a node that finds what it needs by walking the tree and does not need
/// the scene assembly to know it exists.
/// </summary>
public sealed partial class EnvironmentAcoustics : Node
{
    private int _busIndex = -1;
    private AudioEffectReverb? _reverb;
    private AudioEffectLowPassFilter? _lowPass;

    // Smoothed parameters — the reverb must not step.
    private float _sRoom, _sDamp, _sWet, _sLpHz;

    private HelicopterController? _heli;
    private ChaseCamera? _camera;
    private bool _ready;

    public override void _Ready()
    {
        // Create the Reverb bus if it doesn't already exist.
        int masterIdx = AudioServer.GetBusIndex("Master");
        if (masterIdx < 0) return;

        _busIndex = AudioServer.GetBusIndex("Reverb");
        if (_busIndex < 0)
        {
            AudioServer.AddBus();
            _busIndex = AudioServer.BusCount - 1;
            AudioServer.SetBusName(_busIndex, "Reverb");
            AudioServer.SetBusSend(_busIndex, "Master");
        }

        // Clear existing effects on the bus and add ours.
        while (AudioServer.GetBusEffectCount(_busIndex) > 0)
            AudioServer.RemoveBusEffect(_busIndex, 0);

        _reverb = new AudioEffectReverb
        {
            RoomSize = 0.2f,
            Damping = 0.7f,
            Wet = 0f,
            Dry = 1.0f,
            Spread = 0.8f,
            Hipass = 0.05f,
            PredelayMsec = 30f,
        };
        AudioServer.AddBusEffect(_busIndex, _reverb);

        _lowPass = new AudioEffectLowPassFilter
        {
            CutoffHz = 16000f,
        };
        AudioServer.AddBusEffect(_busIndex, _lowPass);

        // Start with reverb inaudible so the first frame doesn't pop.
        _sRoom = 0.2f;
        _sDamp = 0.7f;
        _sWet = 0f;
        _sLpHz = 16000f;

        _ready = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_ready) return;

        // Lazy-find the helicopter and camera.
        _heli ??= GetTree().Root.FindChild("Helicopter", true, false) as HelicopterController;
        _camera ??= GetTree().Root.FindChild("Camera", true, false) as ChaseCamera;
        if (_heli is null) return;

        // Compute acoustic parameters from the terrain around the camera/listener.
        Vector3 camPos = _camera?.GlobalPosition ?? _heli.GlobalPosition;

        var acoustics = AcousticSpace.At(
            camPos.X, camPos.Z, camPos.Y,
            WorldHeight.At, WorldHeight.WaterLevel);

        // Smooth toward target values — 3 Hz time constant, no clicks.
        float k = (float)(1.0 - System.Math.Exp(-delta * 3.0));
        _sRoom += ((float)acoustics.RoomSize - _sRoom) * k;
        _sDamp += ((float)acoustics.Damping - _sDamp) * k;
        _sWet += ((float)acoustics.WetLevel - _sWet) * k;
        _sLpHz += ((float)acoustics.DistanceLpHz - _sLpHz) * k;

        // Apply to the bus effects.
        if (_reverb is not null)
        {
            _reverb.RoomSize = _sRoom;
            _reverb.Damping = _sDamp;
            _reverb.Wet = _sWet;
        }

        if (_lowPass is not null)
        {
            _lowPass.CutoffHz = Mathf.Clamp(_sLpHz, 200f, 20500f);
        }
    }

    /// <summary>
    /// Route an AudioStreamPlayer3D through the Reverb bus. Call this after creating
    /// any positional audio player that should pick up the environment.
    /// </summary>
    public void RouteToReverbBus(AudioStreamPlayer3D player)
    {
        if (_busIndex >= 0)
            player.Bus = "Reverb";
    }

    /// <summary>
    /// The bus name, for players created elsewhere that want to route themselves.
    /// </summary>
    public static StringName BusName => "Reverb";
}
