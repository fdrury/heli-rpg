using System;
using System.Collections.Generic;
using Godot;

namespace Rotorwash;

/// <summary>
/// Bind the four flight axes to whatever the player actually owns.
///
/// <para><b>Why this has to exist.</b> <see cref="ControllerPresets"/> covers the hardware
/// somebody thought of, which is the hardware that was on sale when it was written. It cannot
/// cover a home-built collective, a Bodnar board, a twist grip the driver reports on a
/// different axis than the last driver did, or the person who has wired a potentiometer to a
/// lever because that is the correct control for a helicopter and nobody sells one. For all
/// of those the preset is a guess, and a guess the player cannot override is worse than no
/// guess at all.</para>
///
/// <para><b>Bind by moving the thing.</b> Nobody knows what axis number their throttle is,
/// and asking them is asking them to go and find out. So the capture watches every axis on
/// every device and takes the one that MOVED MOST from where it was when capture started -
/// which is the only question the player can answer without a manual, because the answer is
/// "the one I just moved".</para>
///
/// <para>It also draws a live bar per axis. That is not decoration: half of all binding
/// problems are an axis that is bound correctly and reading backwards or sticking at one
/// end, and a number that moves while you move the stick is the fastest way to see which.</para>
/// </summary>
public sealed partial class BindingPanel : Control
{
    /// <summary>Opens and closes the screen.</summary>
    public const Key ToggleKey = Key.F8;

    /// <summary>How far an axis has to move during capture to count as the one you meant.</summary>
    private const float CaptureThreshold = 0.45f;

    /// <summary>Axes to scan. Godot exposes ten; sticks with more than that are rare.</summary>
    private const int AxisCount = 10;

    private readonly FlightInput _input;
    private Font _font = null!;

    private int _selected;
    private bool _capturing;
    private readonly Dictionary<(int Device, int Axis), float> _captureStart = new();
    private string _message = "";
    private double _messageAge = 99;

    public BindingPanel(FlightInput input) => _input = input;

    private static readonly (string Label, string Hint)[] Rows =
    {
        ("Cyclic — pitch",  "fore and aft. Nose down is forward stick."),
        ("Cyclic — roll",   "left and right."),
        ("Pedals",          "yaw. A twist grip, rudder pedals, or a stick's Z rotation."),
        ("Collective",      "the lever. A throttle axis is the right control for this."),
    };

    private AxisBinding BindingFor(int row) => row switch
    {
        0 => _input.Profile.CyclicPitch,
        1 => _input.Profile.CyclicRoll,
        2 => _input.Profile.Pedal,
        _ => _input.Profile.Collective,
    };

    private void SetBinding(int row, AxisBinding b)
    {
        switch (row)
        {
            case 0: _input.Profile.CyclicPitch = b; break;
            case 1: _input.Profile.CyclicRoll = b; break;
            case 2: _input.Profile.Pedal = b; break;
            default: _input.Profile.Collective = b; break;
        }
    }

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;

        if (k.Keycode == ToggleKey) { Visible = !Visible; _capturing = false; return; }
        if (!Visible) return;

        switch (k.Keycode)
        {
            case Key.Up: _selected = (_selected + Rows.Length - 1) % Rows.Length; break;
            case Key.Down: _selected = (_selected + 1) % Rows.Length; break;

            case Key.Enter:
            case Key.KpEnter:
                BeginCapture();
                break;

            case Key.I:
            {
                AxisBinding b = BindingFor(_selected);
                b.Invert = !b.Invert;
                Say(b.Invert ? "inverted" : "not inverted");
                break;
            }

            case Key.L:
            {
                // A lever holds its position; a stick springs back. Which one an axis is
                // cannot be detected - a throttle at rest looks exactly like a centred
                // stick - so it is the player's to say.
                AxisBinding b = BindingFor(_selected);
                b.UnipolarLever = !b.UnipolarLever;
                Say(b.UnipolarLever ? "treated as a lever, 0 to 1" : "treated as a centred axis");
                break;
            }

            case Key.Delete:
            case Key.Backspace:
                SetBinding(_selected, new AxisBinding { Axis = -1 });
                Say("unbound - the keyboard carries this axis now");
                break;

            case Key.S:
                _input.SaveProfile();
                Say("saved");
                break;

            case Key.R:
                _input.Redetect();
                Say("re-detected from what is plugged in");
                break;

            case Key.Escape:
                Visible = false;
                _capturing = false;
                break;
        }
    }

    private void Say(string m) { _message = m; _messageAge = 0; }

    private void BeginCapture()
    {
        _captureStart.Clear();
        foreach (int d in Input.GetConnectedJoypads())
            for (int a = 0; a < AxisCount; a++)
                _captureStart[(d, a)] = Input.GetJoyAxis(d, (JoyAxis)a);

        if (_captureStart.Count == 0) { Say("nothing is plugged in"); return; }
        _capturing = true;
        Say("move the axis you want");
    }

    public override void _Process(double delta)
    {
        _messageAge += delta;
        if (!Visible) return;
        QueueRedraw();
        if (!_capturing) return;

        // The axis that has moved furthest from where it was when capture began. Largest
        // movement rather than first-past-the-post, because a stick that is slightly off
        // centre nudges several axes at once and the one the player meant is the one they
        // actually pushed.
        float best = CaptureThreshold;
        (int Device, int Axis) winner = (-1, -1);
        foreach (((int d, int a), float start) in _captureStart)
        {
            float moved = Mathf.Abs(Input.GetJoyAxis(d, (JoyAxis)a) - start);
            if (moved > best) { best = moved; winner = (d, a); }
        }
        if (winner.Axis < 0) return;

        AxisBinding old = BindingFor(_selected);
        SetBinding(_selected, new AxisBinding
        {
            Device = winner.Device,
            Axis = winner.Axis,
            // Carry the player's own choices across a rebind. Re-inverting an axis every
            // time you move it to a different device is the kind of small thing that makes
            // a binding screen feel hostile.
            Invert = old.Invert,
            UnipolarLever = old.UnipolarLever,
            Deadzone = old.Deadzone,
            Expo = old.Expo,
        });
        _capturing = false;
        Say($"bound to device {winner.Device}, axis {winner.Axis}");
    }

    public override void _Draw()
    {
        if (!Visible) return;
        Vector2 size = GetViewportRect().Size;

        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.04f, 0.05f, 0.92f));

        float x = 60, y = 70;
        DrawString(_font, new Vector2(x, y), "FLIGHT CONTROLS",
                   HorizontalAlignment.Left, -1, 26, new Color(0.92f, 0.90f, 0.82f));
        y += 26;
        DrawString(_font, new Vector2(x, y), _input.Profile.Name,
                   HorizontalAlignment.Left, -1, 14, new Color(0.62f, 0.68f, 0.72f));
        y += 40;

        for (int i = 0; i < Rows.Length; i++)
        {
            AxisBinding b = BindingFor(i);
            bool sel = i == _selected;
            var ink = sel ? new Color(1.00f, 0.86f, 0.42f) : new Color(0.82f, 0.86f, 0.88f);

            if (sel) DrawRect(new Rect2(x - 14, y - 17, 860, 44), new Color(1, 1, 1, 0.05f));

            DrawString(_font, new Vector2(x, y), Rows[i].Label, HorizontalAlignment.Left, -1, 18, ink);

            string where = b.Bound
                ? $"device {b.Device}, axis {b.Axis}"
                : (i == 3 ? "unbound - left stick drives it as a rate" : "unbound - keyboard only");
            DrawString(_font, new Vector2(x + 210, y), where, HorizontalAlignment.Left, -1, 16,
                       b.Bound ? ink : new Color(0.60f, 0.62f, 0.64f));

            var flags = new List<string>();
            if (b.Invert) flags.Add("inverted");
            if (b.UnipolarLever) flags.Add("lever");
            if (flags.Count > 0)
                DrawString(_font, new Vector2(x + 430, y), string.Join(", ", flags),
                           HorizontalAlignment.Left, -1, 14, new Color(0.55f, 0.75f, 0.85f));

            // The live bar. Centre-zero for a stick, left-zero for a lever.
            float v = b.Bound ? b.Read() : 0f;
            var barAt = new Rect2(x + 560, y - 11, 240, 14);
            DrawRect(barAt, new Color(1, 1, 1, 0.07f));
            if (b.UnipolarLever)
                DrawRect(new Rect2(barAt.Position, new Vector2(240 * Mathf.Clamp(v, 0, 1), 14)),
                         new Color(0.35f, 0.80f, 0.55f, 0.85f));
            else
            {
                float half = 240 * 0.5f;
                float w = half * Mathf.Clamp(Mathf.Abs(v), 0, 1);
                float left = barAt.Position.X + half + (v < 0 ? -w : 0);
                DrawRect(new Rect2(new Vector2(left, barAt.Position.Y), new Vector2(w, 14)),
                         new Color(0.35f, 0.80f, 0.55f, 0.85f));
                DrawRect(new Rect2(new Vector2(barAt.Position.X + half - 1, barAt.Position.Y),
                                   new Vector2(2, 14)), new Color(1, 1, 1, 0.25f));
            }

            y += 22;
            DrawString(_font, new Vector2(x + 14, y), Rows[i].Hint, HorizontalAlignment.Left, -1, 13,
                       new Color(0.55f, 0.58f, 0.60f));
            y += 30;
        }

        y += 14;
        string help = _capturing
            ? "MOVE THE AXIS YOU WANT — push it to one end and back"
            : "up/down select   ENTER bind   I invert   L lever   DEL unbind   S save   R re-detect   F8 close";
        DrawString(_font, new Vector2(x, y), help, HorizontalAlignment.Left, -1, 15,
                   _capturing ? new Color(1.00f, 0.86f, 0.42f) : new Color(0.70f, 0.74f, 0.76f));

        if (_message.Length > 0 && _messageAge < 4.0)
            DrawString(_font, new Vector2(x, y + 26), _message, HorizontalAlignment.Left, -1, 15,
                       new Color(0.55f, 0.85f, 0.65f, (float)Math.Max(0, 1 - _messageAge / 4.0)));

        // Everything plugged in, with its live axes. When a binding will not take, this is
        // the panel that says why - usually because the device is not connected at all.
        y += 70;
        DrawString(_font, new Vector2(x, y), "CONNECTED", HorizontalAlignment.Left, -1, 15,
                   new Color(0.72f, 0.76f, 0.78f));
        y += 24;
        foreach (string line in FlightInput.DescribeDevices())
        {
            DrawString(_font, new Vector2(x, y), line, HorizontalAlignment.Left, -1, 13,
                       new Color(0.62f, 0.66f, 0.68f));
            y += 18;
        }
    }
}
