using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rotorwash;

/// <summary>
/// Does the hardware table say the right thing about real device names?
///
/// <para>The failure here is order-dependent and completely silent. Every entry is a
/// substring match, and real product names nest: "Thrustmaster Warthog Throttle" contains
/// "warthog", "Saitek X52 Throttle" contains "x52", "Logitech G Pro Throttle" contains
/// "pro throttle" and "throttle". Get the order wrong and a throttle is bound as a cyclic -
/// which the player discovers by lifting into a hover and rolling over, with nothing
/// anywhere saying why.</para>
///
/// <para>So the names below are the real strings these devices report over USB, and this
/// asserts what each one has to come out as. It needs no hardware, which is the point.</para>
/// </summary>
public static class InputReport
{
    private static readonly (string Name, ControllerPresets.Kind Want)[] Cases =
    {
        // Gamepads
        ("Xbox Series X Controller",                 ControllerPresets.Kind.Gamepad),
        ("Xbox One Controller",                      ControllerPresets.Kind.Gamepad),
        ("Xbox 360 Controller for Windows",          ControllerPresets.Kind.Gamepad),
        ("XInput Controller",                        ControllerPresets.Kind.Gamepad),
        ("Sony DualSense Wireless Controller",       ControllerPresets.Kind.Gamepad),
        ("Sony DualShock 4 Wireless Controller",     ControllerPresets.Kind.Gamepad),
        ("Nintendo Switch Pro Controller",           ControllerPresets.Kind.Gamepad),
        ("8BitDo Ultimate Controller",               ControllerPresets.Kind.Gamepad),

        // Sticks with a twist grip - cyclic and pedals on one device
        ("Thrustmaster T.16000M",                    ControllerPresets.Kind.TwistStick),
        ("T16000MFCS",                               ControllerPresets.Kind.TwistStick),
        ("Logitech Extreme 3D Pro",                  ControllerPresets.Kind.TwistStick),
        ("Saitek X52 Flight Controller",             ControllerPresets.Kind.TwistStick),
        ("Microsoft SideWinder Precision 2",         ControllerPresets.Kind.TwistStick),
        ("VKBsim Gladiator NXT",                     ControllerPresets.Kind.TwistStick),
        ("Thrustmaster T-Flight Hotas X",            ControllerPresets.Kind.TwistStick),

        // Sticks with no twist - need a separate yaw source
        ("Thrustmaster Warthog Joystick",            ControllerPresets.Kind.Stick),
        ("CH Fighterstick USB",                      ControllerPresets.Kind.Stick),
        ("VPC Constellation ALPHA",                  ControllerPresets.Kind.Stick),

        // THE TRAP. Every one of these contains a stick's fragment.
        ("Thrustmaster Warthog Throttle",            ControllerPresets.Kind.Throttle),
        ("Thrustmaster TWCS Throttle",               ControllerPresets.Kind.Throttle),
        ("Saitek X52 Throttle",                      ControllerPresets.Kind.Throttle),
        ("Logitech X56 Throttle",                    ControllerPresets.Kind.Throttle),
        ("CH Pro Throttle USB",                      ControllerPresets.Kind.Throttle),
        ("Honeycomb Bravo Throttle Quadrant",        ControllerPresets.Kind.Throttle),

        // Pedals and yokes
        ("Saitek Pro Flight Rudder Pedals",          ControllerPresets.Kind.Pedals),
        ("CH Pro Pedals USB",                        ControllerPresets.Kind.Pedals),
        ("Thrustmaster TFRP Rudder",                 ControllerPresets.Kind.Pedals),
        ("Honeycomb Alpha Flight Controls Yoke",     ControllerPresets.Kind.Yoke),

        // And something nobody has heard of
        ("Generic   USB  Joystick",                  ControllerPresets.Kind.TwistStick),
        ("BU0836A Interface",                        ControllerPresets.Kind.Unknown),
    };

    public static void Run()
    {
        GD.Print("=== input: hardware table ==============================================");
        GD.Print($"  {Cases.Length} real device names checked against {ControllerPresets.Known.Count()} presets");
        GD.Print("");
        GD.Print("    reported name                             -> kind         verdict");

        int bad = 0;
        foreach ((string name, ControllerPresets.Kind want) in Cases)
        {
            (ControllerPresets.Kind got, string label) = ControllerPresets.Classify(name);
            bool ok = got == want;
            if (!ok) bad++;
            GD.Print($"    {name,-42} -> {got,-12} {(ok ? "" : $"WRONG, wanted {want}")}");
        }

        GD.Print("");
        if (bad == 0)
        {
            GD.Print("  PROBLEMS: none. Every throttle is a throttle, which is the one that");
            GD.Print("  matters - a throttle bound as a cyclic is discovered in the hover.");
        }
        else
        {
            GD.Print($"  PROBLEMS: {bad} device name(s) classified wrongly. The table is ordered,");
            GD.Print("  most specific first; a name matching an earlier fragment wins.");
        }

        // And what is actually plugged into this machine, which on the dev laptop is
        // nothing and on the test machine is the thing being asked about.
        GD.Print("");
        GD.Print("  plugged in here:");
        foreach (string line in FlightInput.DescribeDevices()) GD.Print($"    {line}");

        var pads = Input.GetConnectedJoypads();
        InputProfile p = ControllerPresets.Build(pads, out string description);
        GD.Print("");
        GD.Print($"  profile that would be built: {description}");
        GD.Print($"    cyclic pitch  {Describe(p.CyclicPitch)}");
        GD.Print($"    cyclic roll   {Describe(p.CyclicRoll)}");
        GD.Print($"    pedals        {Describe(p.Pedal)}");
        GD.Print($"    collective    {Describe(p.Collective)}");
        GD.Print("========================================================================");
    }

    private static string Describe(AxisBinding b)
        => b.Bound
            ? $"device {b.Device} axis {b.Axis}" +
              (b.Invert ? ", inverted" : "") + (b.UnipolarLever ? ", lever" : "")
            : "unbound (keyboard, or a rate on the left stick)";
}
