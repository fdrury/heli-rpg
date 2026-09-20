using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rotorwash;

/// <summary>
/// Known hardware, and what its axes are for.
///
/// <para><b>Why a table rather than a heuristic.</b> The first version asked two questions -
/// does the name look like a gamepad, and if not assume it is a flight stick - which covers
/// the two cases exactly and everything else approximately. Approximately is not good enough
/// for a control scheme: a Thrustmaster TWCS throttle and a Logitech Extreme 3D Pro are both
/// "not a gamepad" and share no axis layout at all, so one of them always came out wrong and
/// the player had no way to tell whether the game or their hardware was at fault.</para>
///
/// <para><b>The split-device case is the normal one.</b> A real HOTAS is two USB devices -
/// stick and throttle - and the collective belongs on the throttle. <see cref="AxisBinding"/>
/// already carries its own device id, so a profile can and should draw from both. Every
/// preset below is given the whole list of connected devices and picks what it needs from
/// it; that is the only way "stick in port 0, throttle in port 1" works, and it is what most
/// people with a stick actually have.</para>
///
/// <para><b>This is a starting point, not an answer.</b> Nothing here can cover custom or
/// home-built hardware, and the axis numbering on a device Godot does not recognise is
/// whatever the manufacturer felt like. So every preset is only a default for the binding
/// screen to start from, and the binding screen is the part that has to work for everybody.</para>
/// </summary>
public static class ControllerPresets
{
    /// <summary>What a device is, once its name has been matched.</summary>
    public enum Kind
    {
        /// <summary>Two thumbsticks and triggers. Xbox, PlayStation, Switch Pro, generic.</summary>
        Gamepad,
        /// <summary>A stick with a twist grip. Cyclic and pedals on one device.</summary>
        TwistStick,
        /// <summary>A stick with no twist. Needs a separate throttle or pedals for yaw.</summary>
        Stick,
        /// <summary>A throttle unit, on its own. Collective, and often a mini-stick.</summary>
        Throttle,
        /// <summary>Rudder pedals.</summary>
        Pedals,
        /// <summary>A yoke. Not ideal for a helicopter and people will try it anyway.</summary>
        Yoke,
        /// <summary>Recognised as nothing in particular.</summary>
        Unknown,
    }

    private readonly record struct Match(string Fragment, Kind Kind, string Label);

    /// <summary>
    /// Name fragments, longest and most specific first.
    ///
    /// Matched case-insensitively against <c>Input.GetJoyName</c>, which returns whatever
    /// the device reports over USB - so these are fragments rather than exact names, because
    /// the same stick reports itself differently on different drivers and firmware.
    /// </summary>
    private static readonly Match[] Table =
    {
        // --- gamepads ------------------------------------------------------
        new("xbox series",        Kind.Gamepad,    "Xbox Series controller"),
        new("xbox one",           Kind.Gamepad,    "Xbox One controller"),
        new("xbox 360",           Kind.Gamepad,    "Xbox 360 controller"),
        new("xbox",               Kind.Gamepad,    "Xbox controller"),
        new("xinput",             Kind.Gamepad,    "XInput controller"),
        new("dualsense",          Kind.Gamepad,    "DualSense"),
        new("dualshock",          Kind.Gamepad,    "DualShock"),
        new("ps5",                Kind.Gamepad,    "PlayStation 5 controller"),
        new("ps4",                Kind.Gamepad,    "PlayStation 4 controller"),
        new("playstation",        Kind.Gamepad,    "PlayStation controller"),
        new("nintendo",           Kind.Gamepad,    "Switch Pro controller"),
        new("switch pro",         Kind.Gamepad,    "Switch Pro controller"),
        new("steam",              Kind.Gamepad,    "Steam controller"),
        new("8bitdo",             Kind.Gamepad,    "8BitDo controller"),
        new("gamepad",            Kind.Gamepad,    "gamepad"),

        // --- throttles, checked BEFORE sticks -------------------------------
        // A "Thrustmaster Warthog Throttle" contains the word that would otherwise match
        // the stick, so the more specific thing has to be asked about first.
        new("twcs",               Kind.Throttle,   "Thrustmaster TWCS throttle"),
        new("warthog throttle",   Kind.Throttle,   "Warthog throttle"),
        new("throttle quadrant",  Kind.Throttle,   "throttle quadrant"),
        new("pro throttle",       Kind.Throttle,   "CH Pro Throttle"),
        new("x56 throttle",       Kind.Throttle,   "X56 throttle"),
        new("x52 throttle",       Kind.Throttle,   "X52 throttle"),
        new("throttle",           Kind.Throttle,   "throttle"),
        new("quadrant",           Kind.Throttle,   "throttle quadrant"),

        // --- pedals ---------------------------------------------------------
        new("rudder",             Kind.Pedals,     "rudder pedals"),
        new("pedal",              Kind.Pedals,     "pedals"),
        new("crosswind",          Kind.Pedals,     "CH Pro Pedals"),

        // --- VIRPIL, before the yokes -----------------------------------------
        // "VPC Constellation ALPHA" is a VIRPIL grip and contains "alpha", which the
        // Honeycomb Alpha yoke entry below would otherwise claim. Caught by the table
        // check on its first run, which is precisely the nesting this is all about.
        new("virpil",             Kind.Stick,      "VIRPIL stick"),
        new("vpc",                Kind.Stick,      "VIRPIL stick"),

        // --- yokes ----------------------------------------------------------
        new("yoke",               Kind.Yoke,       "yoke"),
        new("bravo",              Kind.Throttle,   "Honeycomb Bravo quadrant"),
        new("honeycomb alpha",    Kind.Yoke,       "Honeycomb Alpha yoke"),
        new("alpha flight",       Kind.Yoke,       "Honeycomb Alpha yoke"),

        // --- sticks with a twist grip ---------------------------------------
        new("t.16000m",           Kind.TwistStick, "Thrustmaster T.16000M"),
        new("t16000",             Kind.TwistStick, "Thrustmaster T.16000M"),
        new("extreme 3d",         Kind.TwistStick, "Logitech Extreme 3D Pro"),
        new("x52",                Kind.TwistStick, "Saitek X52"),
        new("x56",                Kind.TwistStick, "Logitech X56 Rhino"),
        new("sidewinder",         Kind.TwistStick, "Microsoft Sidewinder"),
        new("gladiator",          Kind.TwistStick, "VKB Gladiator"),
        new("t-flight",           Kind.TwistStick, "Thrustmaster T-Flight"),
        new("t flight",           Kind.TwistStick, "Thrustmaster T-Flight"),
        new("hotas",              Kind.TwistStick, "HOTAS"),

        // --- sticks with no twist -------------------------------------------
        new("warthog",            Kind.Stick,      "Thrustmaster Warthog stick"),
        new("fighterstick",       Kind.Stick,      "CH Fighterstick"),
        new("joystick",           Kind.TwistStick, "joystick"),
        new("stick",              Kind.TwistStick, "flight stick"),
    };

    /// <summary>
    /// What a device NAME is, with no hardware involved.
    ///
    /// Separated from <see cref="Identify"/> so the table can be checked without a stick
    /// plugged in - which matters, because the failure mode here is order-dependent and
    /// silent. "Thrustmaster Warthog Throttle" contains "warthog", so if the stick entry
    /// were listed first a throttle would be bound as a cyclic and the player would find
    /// out by taking off sideways.
    /// </summary>
    public static (Kind Kind, string Label) Classify(string name)
    {
        string lower = (name ?? "").ToLowerInvariant();
        foreach (Match m in Table)
            if (lower.Contains(m.Fragment, StringComparison.Ordinal))
                return (m.Kind, m.Label);
        return (Kind.Unknown, string.IsNullOrWhiteSpace(name) ? "unrecognised device" : name);
    }

    /// <summary>What we think a device is, and what to call it.</summary>
    public static (Kind Kind, string Label) Identify(int device)
    {
        string name = Input.GetJoyName(device) ?? "";
        (Kind kind, string label) = Classify(name);
        if (kind != Kind.Unknown) return (kind, $"{label} ({name})");

        // Godot only claims to KNOW a device when it has an SDL mapping for it, and those
        // exist for gamepads and essentially nothing else - so an unrecognised name that
        // Godot nonetheless knows is a gamepad of some sort.
        if (Input.IsJoyKnown(device)) return (Kind.Gamepad, $"gamepad ({name})");
        return (Kind.Unknown, string.IsNullOrWhiteSpace(name) ? $"device {device}" : name);
    }

    /// <summary>
    /// Build a profile from everything currently plugged in.
    ///
    /// Order of business: find the thing that should fly the aircraft, then find something
    /// for the collective, then something for the pedals - preferring a dedicated device for
    /// each where one exists, and falling back onto the primary device's own axes where it
    /// does not.
    /// </summary>
    public static InputProfile Build(IReadOnlyList<int> devices, out string description)
    {
        if (devices is null || devices.Count == 0)
        {
            description = "keyboard only (no stick or pad detected)";
            return new InputProfile { Name = description };
        }

        var found = devices.Select(d => (Device: d, Id: Identify(d))).ToList();

        int First(params Kind[] kinds)
        {
            foreach ((int dev, (Kind k, string _)) in found)
                if (kinds.Contains(k)) return dev;
            return -1;
        }

        int stick = First(Kind.TwistStick, Kind.Stick, Kind.Yoke);
        int pad = First(Kind.Gamepad);
        int throttle = First(Kind.Throttle);
        int pedals = First(Kind.Pedals);

        // A gamepad only gets to be the primary control if there is no stick. Somebody with
        // both plugged in wants to fly on the stick.
        int primary = stick >= 0 ? stick : pad >= 0 ? pad : devices[0];
        (Kind kind, string label) = Identify(primary);

        var parts = new List<string> { label };
        if (throttle >= 0 && throttle != primary) parts.Add(Identify(throttle).Label);
        if (pedals >= 0 && pedals != primary) parts.Add(Identify(pedals).Label);
        description = string.Join(" + ", parts);

        InputProfile p = kind switch
        {
            Kind.Gamepad => Gamepad(primary),
            Kind.Yoke => Yoke(primary),
            Kind.Throttle => Gamepad(primary),      // a throttle alone cannot fly it
            _ => FlightStick(primary, kind == Kind.TwistStick),
        };

        // A dedicated throttle beats whatever the stick had for the collective, always. It
        // is the correct control for a collective and it is most of why a HOTAS is worth
        // having for this game.
        if (throttle >= 0)
            p.Collective = new AxisBinding
            {
                Device = throttle,
                Axis = (int)JoyAxis.LeftY,
                Invert = true,
                UnipolarLever = true,
                Deadzone = 0f,
                Expo = 0f,
            };

        // And dedicated pedals beat a twist grip.
        if (pedals >= 0)
            p.Pedal = new AxisBinding { Device = pedals, Axis = (int)JoyAxis.LeftX, Expo = 0.2f, Deadzone = 0.04f };

        p.Name = description;
        return p;
    }

    private static InputProfile Gamepad(int d) => new()
    {
        // Right stick is the cyclic. Godot reports stick up as negative.
        CyclicPitch = new AxisBinding { Device = d, Axis = (int)JoyAxis.RightY, Invert = true, Expo = 0.45f },
        CyclicRoll = new AxisBinding { Device = d, Axis = (int)JoyAxis.RightX, Expo = 0.45f },
        Pedal = new AxisBinding { Device = d, Axis = (int)JoyAxis.LeftX, Expo = 0.3f },
        // Left stick vertical drives the collective as a RATE, integrated in Poll(),
        // because a self-centring stick cannot hold a lever position. Axis -1 is the
        // signal for that.
        Collective = new AxisBinding { Axis = -1 },
    };

    private static InputProfile FlightStick(int d, bool twist) => new()
    {
        CyclicPitch = new AxisBinding { Device = d, Axis = (int)JoyAxis.LeftY, Invert = true, Expo = 0.2f, Deadzone = 0.03f },
        CyclicRoll = new AxisBinding { Device = d, Axis = (int)JoyAxis.LeftX, Expo = 0.2f, Deadzone = 0.03f },
        // The twist grip. Godot maps a stick's Z rotation onto RightX on most drivers.
        // Without a twist grip this stays unbound and the keyboard pedals carry it until
        // the player binds something.
        Pedal = twist
            ? new AxisBinding { Device = d, Axis = (int)JoyAxis.RightX, Expo = 0.25f, Deadzone = 0.08f }
            : new AxisBinding { Axis = -1 },
        Collective = new AxisBinding { Device = d, Axis = (int)JoyAxis.TriggerLeft, UnipolarLever = true },
    };

    private static InputProfile Yoke(int d) => new()
    {
        // A yoke is the wrong shape for a cyclic - no roll authority to speak of and a
        // huge pitch throw - but somebody will have one, and a bad binding is worse than
        // an honest one.
        CyclicPitch = new AxisBinding { Device = d, Axis = (int)JoyAxis.LeftY, Invert = true, Expo = 0.15f, Deadzone = 0.02f },
        CyclicRoll = new AxisBinding { Device = d, Axis = (int)JoyAxis.LeftX, Expo = 0.15f, Deadzone = 0.02f },
        Pedal = new AxisBinding { Axis = -1 },
        Collective = new AxisBinding { Axis = -1 },
    };

    /// <summary>
    /// The layout a given kind of device would get, without one being plugged in.
    ///
    /// So the mapping can be reported and checked on a machine with no controller on it,
    /// which is the machine this was written on.
    /// </summary>
    public static InputProfile Preview(Kind kind, int device) => kind switch
    {
        Kind.Gamepad => Gamepad(device),
        Kind.Yoke => Yoke(device),
        Kind.Throttle => Gamepad(device),
        _ => FlightStick(device, kind == Kind.TwistStick),
    };

    /// <summary>Every preset name, for the binding screen's list and for a report.</summary>
    public static IEnumerable<string> Known => Table.Select(m => m.Label).Distinct();
}
