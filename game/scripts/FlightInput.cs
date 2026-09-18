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
    public float TrimPitch { get; set; }
    public float TrimRoll { get; set; }

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
    private static InputProfile DetectProfile(out string description)
    {
        var pads = Input.GetConnectedJoypads();
        if (pads.Count == 0)
        {
            description = "keyboard only (no stick or pad detected)";
            return new InputProfile { Name = description };
        }

        int device = pads[0];
        string name = Input.GetJoyName(device);
        bool looksLikeGamepad = Input.IsJoyKnown(device) &&
            new[] { "xbox", "playstation", "ps4", "ps5", "dualshock", "dualsense", "nintendo", "gamepad", "controller" }
                .Any(k => name.ToLowerInvariant().Contains(k));

        if (looksLikeGamepad)
        {
            description = $"gamepad: {name}";
            return new InputProfile
            {
                Name = description,
                // Right stick is the cyclic. Godot reports stick up as negative.
                CyclicPitch = new AxisBinding { Device = device, Axis = (int)JoyAxis.RightY, Invert = true, Expo = 0.45f },
                CyclicRoll = new AxisBinding { Device = device, Axis = (int)JoyAxis.RightX, Expo = 0.45f },
                // Left stick horizontal is the pedals.
                Pedal = new AxisBinding { Device = device, Axis = (int)JoyAxis.LeftX, Expo = 0.3f },
                // Left stick vertical drives the collective as a RATE, integrated in
                // Poll(), because a self-centring stick cannot hold a lever position.
                Collective = new AxisBinding { Axis = -1 },
            };
        }

        description = $"flight stick: {name}";
        return new InputProfile
        {
            Name = description,
            CyclicPitch = new AxisBinding { Device = device, Axis = (int)JoyAxis.LeftY, Invert = true, Expo = 0.2f, Deadzone = 0.03f },
            CyclicRoll = new AxisBinding { Device = device, Axis = (int)JoyAxis.LeftX, Expo = 0.2f, Deadzone = 0.03f },
            // Twist grip. On most sticks this is the Z rotation, which Godot maps here.
            Pedal = new AxisBinding { Device = device, Axis = (int)JoyAxis.RightX, Expo = 0.25f, Deadzone = 0.08f },
            // Throttle lever as the collective: absolute position, no centring. This is
            // the correct control and the reason a HOTAS is worth having for this game.
            Collective = new AxisBinding { Device = device, Axis = (int)JoyAxis.TriggerLeft, UnipolarLever = true },
        };
    }

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
        c.CyclicPitch = Mathf.Clamp(pitch - _keyCyclicPitch + TrimPitch, -1, 1);
        c.CyclicRoll = Mathf.Clamp(roll + _keyCyclicRoll + TrimRoll, -1, 1);

        // --- Pedals ----------------------------------------------------------
        float pedal = Profile.Pedal.Read();
        float keyPedal = (Input.IsKeyPressed(Key.A) ? -1 : 0) + (Input.IsKeyPressed(Key.D) ? 1 : 0);
        c.Pedal = Mathf.Clamp(pedal + keyPedal, -1, 1);

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
