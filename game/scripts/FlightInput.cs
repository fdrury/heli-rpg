using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>One physical axis, with the shaping a flight control actually needs.</summary>
public sealed class AxisBinding
{
    public int Device { get; set; } = 0;
    public int Axis { get; set; } = -1;
    public bool Invert { get; set; }
    public float Deadzone { get; set; } = 0.05f;

    /// <summary>
    /// 0 = linear, 1 = heavily softened around centre. A helicopter cyclic has a very
    /// small useful range about trim and a lot of travel for large manoeuvres, so a
    /// linear stick feels twitchy on a short-throw gamepad and vague on a long-throw HOTAS.
    /// </summary>
    public float Expo { get; set; } = 0.35f;

    public float Scale { get; set; } = 1.0f;

    /// <summary>Treat the raw -1..1 range as a 0..1 lever, the way a throttle quadrant works.</summary>
    public bool UnipolarLever { get; set; }

    public bool Bound => Axis >= 0;

    public float Read()
    {
        if (!Bound) return 0;
        float raw = Input.GetJoyAxis(Device, (JoyAxis)Axis);
        if (Invert) raw = -raw;

        if (UnipolarLever)
        {
            // Throttle quadrants idle at -1 and are at 100% at +1.
            float lever = (raw + 1.0f) * 0.5f;
            return Mathf.Clamp(lever * Scale, 0, 1);
        }

        float mag = Mathf.Abs(raw);
        if (mag < Deadzone) return 0;
        mag = (mag - Deadzone) / (1.0f - Deadzone);
        mag = Mathf.Lerp(mag, mag * mag * mag, Mathf.Clamp(Expo, 0, 1));
        return Mathf.Clamp(Mathf.Sign(raw) * mag * Scale, -1, 1);
    }
}

public sealed class InputProfile
{
    public string Name { get; set; } = "default";
    public AxisBinding CyclicPitch { get; set; } = new();
    public AxisBinding CyclicRoll { get; set; } = new();
    public AxisBinding Pedal { get; set; } = new();
    public AxisBinding Collective { get; set; } = new();

    /// <summary>Seconds for the keyboard collective to travel its full range.</summary>
    public float KeyboardCollectiveRate { get; set; } = 0.6f;
    /// <summary>Seconds for a keyboard cyclic input to reach full deflection.</summary>
    public float KeyboardCyclicRate { get; set; } = 2.5f;
    /// <summary>Seconds for a released keyboard cyclic to spring back to centre.</summary>
    public float KeyboardCyclicReturn { get; set; } = 4.0f;
}

/// <summary>
/// Turns whatever the player has plugged in into <see cref="Controls"/>.
///
/// Three device classes are first-class, and all three can be active at once so nothing
/// ever feels dead:
///   * HOTAS - stick for cyclic, twist or rudder pedals for yaw, and crucially the
///     throttle lever for COLLECTIVE. A throttle quadrant is the correct control for a
///     collective: it is a lever that stays where you put it, which is exactly what a
///     real collective does and exactly what a self-centring gamepad stick cannot.
///   * Gamepad - left stick vertical for collective (non-centring, integrated), left
///     stick horizontal for pedals, right stick for cyclic.
///   * Keyboard - always live as a fallback, with a modelled spring return on the cyclic.
///
/// Bindings live in user://input.json so they can be edited without a rebuild, and the
/// defaults are chosen per detected device rather than assumed.
/// </summary>
public sealed partial class FlightInput : Node
{
    public InputProfile Profile { get; private set; } = new();

    private const string ConfigPath = "user://input.json";

    private float _keyboardCollective = 0.0f;
    private float _keyCyclicPitch, _keyCyclicRoll;
    private float _gamepadCollective = 0.0f;

    /// <summary>Trim offsets, adjusted by the player, applied to the cyclic.</summary>
    /// <summary>
    /// Where the stick's centre sits, per axis.
    ///
    /// This is the datum a real force-trim system holds, and it matters more than it
    /// sounds. A trimmed hover needs about +0.275 of lateral cyclic; a spring-centred
    /// joystick returns to zero. Without a trim datum, letting go of the stick is a large
    /// out-of-trim COMMAND, so the aircraft that was carefully balanced at spawn is pushed
    /// straight back out of balance the moment the pilot relaxes. Half of what reads as
    /// "oscillatory" is a pilot fighting their own centring spring.
    /// </summary>
    public float TrimPitch { get; set; }
    public float TrimRoll { get; set; }
    public float TrimPedal { get; set; }

    /// <summary>
    /// Put the datum at a known set of control positions - the trim solution, at spawn.
    ///
    /// Poll composes each axis as (raw stick) + (datum), so with the stick at rest the
    /// datum IS the commanded control position.
    /// </summary>
    public void SetTrim(double pitch, double roll, double pedal)
    {
        TrimPitch = (float)pitch;
        TrimRoll = (float)roll;
        TrimPedal = (float)pedal;
    }

    /// <summary>
    /// Force trim release: make whatever is being commanded right now the new neutral, the
    /// way holding the FTR button and releasing it does on the real aircraft.
    ///
    /// The commanded position is (raw + datum), so folding the raw stick into the datum
    /// leaves the command untouched at the instant of the press and moves the point the
    /// stick springs back to.
    /// </summary>
    public void ReTrimToCurrent()
    {
        TrimPitch = Mathf.Clamp(TrimPitch + (float)_rawPitch, -1, 1);
        TrimRoll = Mathf.Clamp(TrimRoll + (float)_rawRoll, -1, 1);
        TrimPedal = Mathf.Clamp(TrimPedal + (float)_rawPedal, -1, 1);
    }

    private double _rawPitch, _rawRoll, _rawPedal;
    private bool _ftrHeld;

    /// <summary>True when a stick or pad is providing the collective, so keys should not fight it.</summary>
    public bool HasPhysicalCollective => Profile.Collective.Bound;

    public string DeviceDescription { get; private set; } = "keyboard";

    public override void _Ready()
    {
        if (!LoadProfile())
        {
            Profile = DetectProfile(out string description);
            DeviceDescription = description;
            SaveProfile();
        }
        else
        {
            DeviceDescription = Profile.Name;
        }
        GD.Print($"[input] using profile: {DeviceDescription}");

        Input.JoyConnectionChanged += OnJoyChanged;
    }

    /// <summary>
    /// Throw the saved profile away and work it out again from what is plugged in.
    ///
    /// For the binding screen's "re-detect". Needed because the saved profile wins at
    /// startup - which is right, a player's own bindings must not be silently replaced -
    /// but that also means somebody who has made a mess of it has no way back without
    /// finding and deleting a file.
    /// </summary>
    public void Redetect()
    {
        Profile = DetectProfile(out string description);
        DeviceDescription = description;
        SaveProfile();
        GD.Print($"[input] re-detected: {DeviceDescription}");
    }

    private void OnJoyChanged(long device, bool connected)
    {
        GD.Print($"[input] joystick {device} {(connected ? "connected" : "disconnected")}: " +
                 (connected ? Input.GetJoyName((int)device) : ""));
        if (connected && !Profile.CyclicPitch.Bound)
        {
            Profile = DetectProfile(out string d);
            DeviceDescription = d;
            SaveProfile();
        }
    }

    /// <summary>
    /// Pick sensible defaults for whatever is plugged in. Godot normalises anything it
    /// recognises to the Xbox layout, so a recognised pad gets the gamepad mapping and
    /// anything else is treated as a flight stick with a separate throttle axis.
    /// </summary>
    /// <summary>
    /// Work out what the player has plugged in.
    ///
    /// The hardware table lives in <see cref="ControllerPresets"/> rather than here, because
    /// this class is about turning axes into <see cref="Controls"/> and that one is about
    /// which axes to read. It also takes EVERY connected device rather than just the first:
    /// a real HOTAS is two USB devices and the collective belongs on the throttle, which is
    /// impossible to express if you only ever look at joypad 0.
    /// </summary>
    private static InputProfile DetectProfile(out string description)
        => ControllerPresets.Build(Input.GetConnectedJoypads(), out description);

    /// <summary>Read every live device and produce the pilot's demand for this frame.</summary>
    public Controls Poll(double dt)
    {
        var c = new Controls { Throttle = 1.0 };

        // --- Cyclic ----------------------------------------------------------
        float pitch = Profile.CyclicPitch.Read();
        float roll = Profile.CyclicRoll.Read();

        // Keyboard cyclic, with a spring return so it behaves like a stick and not a switch.
        float keyPitchDemand = (Input.IsKeyPressed(Key.Down) ? -1 : 0) + (Input.IsKeyPressed(Key.Up) ? 1 : 0);
        float keyRollDemand = (Input.IsKeyPressed(Key.Left) ? -1 : 0) + (Input.IsKeyPressed(Key.Right) ? 1 : 0);
        _keyCyclicPitch = SpringAxis(_keyCyclicPitch, keyPitchDemand, dt);
        _keyCyclicRoll = SpringAxis(_keyCyclicRoll, keyRollDemand, dt);

        // Up arrow means nose up, which is aft cyclic - negative in the sim's sense.
        // Keep the raw stick separately: the force trim release needs to know how far the
        // pilot has moved from the current datum in order to shift the datum by that much.
        _rawPitch = pitch - _keyCyclicPitch;
        _rawRoll = roll + _keyCyclicRoll;

        c.CyclicPitch = Mathf.Clamp(pitch - _keyCyclicPitch + TrimPitch, -1, 1);
        c.CyclicRoll = Mathf.Clamp(roll + _keyCyclicRoll + TrimRoll, -1, 1);

        // --- Pedals ----------------------------------------------------------
        float pedal = Profile.Pedal.Read();
        float keyPedal = (Input.IsKeyPressed(Key.A) ? -1 : 0) + (Input.IsKeyPressed(Key.D) ? 1 : 0);
        // Force trim release. Tap it and wherever the stick is becomes the new neutral,
        // which is how you fly a helicopter any distance without holding a load all day.
        bool ftr = Input.IsKeyPressed(Key.T);
        if (ftr && !_ftrHeld) ReTrimToCurrent();
        _ftrHeld = ftr;

        _rawPedal = pedal + keyPedal;
        c.Pedal = Mathf.Clamp(pedal + keyPedal + TrimPedal, -1, 1);

        // --- Collective ------------------------------------------------------
        if (Profile.Collective.Bound)
        {
            c.Collective = Profile.Collective.Read();
        }
        else
        {
            // No lever: integrate a rate from the pad stick and the keyboard. The lever
            // holds its position when nothing is pressed, like the real thing.
            float rate = 0;
            var pads = Input.GetConnectedJoypads();
            if (pads.Count > 0) rate += -Input.GetJoyAxis(pads[0], JoyAxis.LeftY);
            if (Input.IsKeyPressed(Key.W)) rate += 1;
            if (Input.IsKeyPressed(Key.S)) rate -= 1;
            if (Mathf.Abs(rate) < 0.08f) rate = 0;

            _gamepadCollective = Mathf.Clamp(
                _gamepadCollective + rate * (float)dt / Mathf.Max(Profile.KeyboardCollectiveRate, 0.05f), 0, 1);
            c.Collective = _gamepadCollective;
        }

        c.ClampToRange();
        return c;
    }

    /// <summary>Force the collective lever to a position, e.g. when spawning trimmed.</summary>
    public void SetCollectivePosition(float value)
    {
        _gamepadCollective = Mathf.Clamp(value, 0, 1);
        _keyboardCollective = _gamepadCollective;
    }

    private float SpringAxis(float current, float demand, double dt)
    {
        float rate = demand != 0
            ? 1.0f / Mathf.Max(Profile.KeyboardCyclicRate, 0.05f)
            : 1.0f / Mathf.Max(Profile.KeyboardCyclicReturn, 0.05f);
        float target = demand;
        float step = rate * (float)dt;
        float d = target - current;
        return Mathf.Abs(d) <= step ? target : current + Mathf.Sign(d) * step;
    }

    // ---------------------------------------------------------------- config

    public void SaveProfile()
    {
        try
        {
            string json = JsonSerializer.Serialize(Profile, new JsonSerializerOptions { WriteIndented = true });
            using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Write);
            f?.StoreString(json);
        }
        catch (Exception e) { GD.PushWarning($"[input] could not save profile: {e.Message}"); }
    }

    private bool LoadProfile()
    {
        if (!FileAccess.FileExists(ConfigPath)) return false;
        try
        {
            using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Read);
            string json = f?.GetAsText() ?? "";
            var loaded = JsonSerializer.Deserialize<InputProfile>(json);
            if (loaded is null) return false;
            Profile = loaded;
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[input] could not read {ConfigPath}, falling back to detection: {e.Message}");
            return false;
        }
    }

    /// <summary>Report every axis on every connected device. The basis of the binding UI.</summary>
    public static List<string> DescribeDevices()
    {
        var lines = new List<string>();
        foreach (int d in Input.GetConnectedJoypads())
        {
            lines.Add($"device {d}: {Input.GetJoyName(d)} (known layout: {Input.IsJoyKnown(d)})");
            for (int a = 0; a < 10; a++)
            {
                float v = Input.GetJoyAxis(d, (JoyAxis)a);
                if (Mathf.Abs(v) > 0.001f) lines.Add($"    axis {a} = {v:F3}");
            }
        }
        if (lines.Count == 0) lines.Add("no joysticks connected");
        return lines;
    }
}
