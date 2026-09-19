using System;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The caution and warning panel: the sim's <see cref="CautionWarningSystem"/> given a
/// driver, a face and a voice.
///
/// The system itself was complete and tested before anything in the game called it - eleven
/// conditions with filters, hysteresis, dwell, latching and priority, and three
/// distinguishable tones - and none of it reached the player. This node is the missing
/// half: it feeds the system live telemetry every frame, draws what comes out, sounds it,
/// and gives the pilot a key to acknowledge it.
///
/// <b>Self-contained, like <see cref="AircraftLights"/>.</b> It finds the aircraft and the
/// landing controller by walking the tree rather than being wired up in the scene assembly,
/// and it brings its own <see cref="CanvasLayer"/> rather than borrowing the HUD's. Several
/// processes share this checkout; a node that needs three edits to three other files in
/// order to exist is a node that cannot be added without a merge conflict.
///
/// <b>Where it draws, and why.</b> Top right, in a fixed place, and nothing at all when the
/// aircraft is healthy. Peripheral vision is poor at colour and detail and very good at
/// change, so the panel is built for the glance rather than the read: it appears where
/// there was nothing, severity is a SHAPE before it is a colour (a warning is a filled
/// block, a caution an outlined one, an advisory a bare tick), and the only thing that
/// moves is a warning that is true right now. Everything else holds still, because a panel
/// where four things blink is a panel with no foreground.
///
/// Per D-005a every line is a fact with its number attached - the strings come from the
/// sim, which never gives advice - and this node adds no words of its own to them.
/// </summary>
public sealed partial class WarningPanel : Control
{
    // ------------------------------------------------------------------- keys

    /// <summary>
    /// Master caution acknowledge. Drops every latched condition that has genuinely gone
    /// away and silences the amber light for the ones that have not; a warning that is
    /// still true keeps its tone, because acknowledging a fact does not change it.
    ///
    /// M for master caution. Chosen because it was free: the keys already spoken for are
    /// W/S, A/D, T, the arrows, C, R, Z, X, L, F, E, Tab, Escape, Space, 1-4, F1, F2, F5,
    /// F9, Home and the numeric keypad. The panel prints the binding on itself whenever
    /// there is something to acknowledge, so discovering it does not depend on a footer
    /// this process does not own.
    /// </summary>
    public const Key AcknowledgeKey = Key.M;

    // ------------------------------------------------------------------ audio

    private const int SampleRate = 44100;

    /// <summary>
    /// Player gain for the warning voices, linear. Measured, not chosen.
    ///
    /// Rendered offline against <see cref="RotorSynth"/> at the hover the aircraft actually
    /// trims to (75% torque, Nr 100%, Ct/sigma 0.0748), 2 s each at 44.1 kHz:
    ///
    /// <code>
    ///   rotor, hovering   RMS -22.1 dBFS   peak 0.349
    ///   low-rotor horn    RMS -13.3 dBFS   peak 0.268
    ///   master warning    RMS -17.9 dBFS   peak 0.286
    ///   caution chime     RMS -17.8 dBFS   peak 0.357
    /// </code>
    ///
    /// The rotor reaches the listener through an <see cref="AudioStreamPlayer3D"/> at 0.75
    /// (-2.5 dB) whose attenuation Godot clamps at its default max_db of +3 (checked at
    /// runtime, not assumed). With the rotor player's 26 m unit size and inverse-square
    /// law that clamp binds inside 18.4 m, and the camera sits at 16.1 m in chase and 2.2 m
    /// in the cockpit - so in both flying views the rotor is at about -21.6 dBFS.
    /// The horn at unity would therefore be +8.3 dB over it, which is not "audible over the
    /// rotor", it is instead of the rotor. At 0.68 it lands +5.0 dB over the hovering
    /// rotor: unmistakably the loudest thing in the cabin, with the aircraft still plainly
    /// audible underneath it - which is the point, because a pilot needs to hear the rotor
    /// decaying while the horn is telling them that it is.
    ///
    /// The other two voices land near +0.5 dB at the same gain, and that is the synth's own
    /// balance rather than an oversight: both are pulsed, so they cost far less RMS than
    /// the continuous horn while peaking slightly higher than it (0.286 and 0.357 against
    /// 0.268). Rhythm is doing the work there, exactly as designed.
    /// </summary>
    private const float AudioGain = 0.68f;

    // ---------------------------------------------------------------- colours

    private static readonly Color WarningColour = new(1.00f, 0.32f, 0.26f);
    private static readonly Color CautionColour = new(1.00f, 0.72f, 0.16f);
    private static readonly Color AdvisoryColour = new(0.70f, 0.82f, 0.88f);
    private static readonly Color Body = new(0.02f, 0.03f, 0.03f, 0.74f);

    private const float PanelWidth = 340f;
    private const float RowHeight = 30f;
    private const float TileHeight = 38f;
    private const int MaxRows = 7;

    // ------------------------------------------------------------------ state

    private readonly CautionWarningSystem _cws = new();
    private WarningSynth _synth = null!;
    private AudioStreamPlayer? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private float[] _block = new float[2048];

    private HelicopterController? _heli;
    private LandingController? _landing;
    private Font _font = null!;

    /// <summary>
    /// Distance from the centre of gravity down to the skids, metres. Measured at
    /// <see cref="_Ready"/> from the airframe the aircraft is actually built from.
    /// </summary>
    private double _skidHeight = 2.0;

    /// <summary>
    /// True once the sim has produced telemetry at all. Before the first physics step every
    /// field is zero, and a rotor at 0% under a running engine is not a fact about the
    /// aircraft - it is the absence of one, and it would light the horn on the loading
    /// screen.
    /// </summary>
    private bool _live;

    /// <summary>See <see cref="Condition"/>. The start-up cutout.</summary>
    private bool _rotorUpToSpeed;

    /// <summary>
    /// Seconds of power-up self-test left before the panel means anything.
    ///
    /// The aircraft is placed in the world already trimmed, and the bridge takes a moment
    /// to agree with the sim about it. Measured on a normal spawn: Nr comes up through
    /// 97, 98, 99 to 100% over the first four seconds and torque reads 85, 89, 90% before
    /// settling - which is three advisory lines on the panel for the first six seconds of
    /// every session, saying nothing about the aircraft and a lot about its initialisation.
    /// Real aircraft hold their annunciators through power-up for the same reason.
    ///
    /// It is spent, not merely counted: the system is not updated at all while it runs, so
    /// the filters prime on the first honest sample rather than on the transient.
    /// </summary>
    private double _arming;

    private const double ArmSeconds = 4.0;

    private double _lastHeight;
    private double _blink;
    private bool _ackHeld;
    private bool _quiet;          // headless test runs: no panel, no sound
    private bool _selfCheck;      // --warntest
    private double _age;

    // ------------------------------------------------------------- attachment

    /// <summary>
    /// Put a panel in the scene. Called from <see cref="HelicopterAudio"/>, which is the
    /// aircraft-side node this process owns; the add is deferred because the caller is
    /// itself being added from inside Main's _Ready, and a node cannot gain children while
    /// its parent is in the middle of gaining it.
    /// </summary>
    public static void Attach(Node context)
    {
        if (context.GetTree() is null) return;
        var layer = new CanvasLayer { Name = "CautionWarning", Layer = 2 };
        layer.AddChild(new WarningPanel { Name = "WarningPanel" });
        Node host = context.GetParent() ?? context.GetTree().Root;
        host.CallDeferred(Node.MethodName.AddChild, layer);
    }

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _heli = FindFirst<HelicopterController>(GetTree().Root);
        _landing = FindFirst<LandingController>(GetTree().Root);

        if (_heli is not null)
        {
            // How far the centre of gravity - which is the height the sim reports - sits
            // above the skids, straight off the airframe rather than guessed.
            double skid = 0;
            Vec3 cg = _heli.Sim.CentreOfGravity;
            foreach (Vec3 c in _heli.Sim.Airframe.ContactPoints) skid = Math.Max(skid, c.Z - cg.Z);
            if (skid > 0.2) _skidHeight = skid;
        }

        ReadCommandLine();

        _synth = new WarningSynth(SampleRate);
        if (!_quiet)
        {
            _player = new AudioStreamPlayer
            {
                Name = "WarningTones",
                Stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = 0.10f },
                VolumeDb = Mathf.LinearToDb(AudioGain),
            };
            AddChild(_player);
            _player.Play();
            _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        }

        Visible = !_quiet;
        if (_heli is null)
            GD.PushWarning("[cws] no helicopter in the tree; caution and warning panel is inert");
    }

    /// <summary>
    /// Test runs get no panel and no tones.
    ///
    /// The headless measurements drive the aircraft to places a pilot would not go - the
    /// self-test drops it on its skids to measure control derivatives, the loop test flies
    /// a whole sortie unattended - and a caution and warning system is exactly right to
    /// complain about all of it. None of that belongs in their transcripts, and pushing
    /// audio blocks into a dummy driver for the length of a loop test is work nobody asked
    /// for.
    /// </summary>
    private void ReadCommandLine()
    {
        foreach (string a in AllArgs())
        {
            switch (a)
            {
                case "--selftest":
                case "--looptest":
                case "--foottest":
                case "--combattest":
                case "--savetest":
                case "--windingtest":
                case "--screenshot":
                case "--threatreport":
                case "--worldreport":
                    _quiet = true;
                    break;
                case "--warntest":
                    _quiet = true;
                    _selfCheck = true;
                    break;
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<string> AllArgs()
    {
        foreach (string a in OS.GetCmdlineArgs()) yield return a;
        foreach (string a in OS.GetCmdlineUserArgs()) yield return a;
    }

    private static T? FindFirst<T>(Node from) where T : Node
    {
        if (from is T hit) return hit;
        foreach (Node child in from.GetChildren())
            if (FindFirst<T>(child) is T found) return found;
        return null;
    }

    // -------------------------------------------------------------- the driver

    /// <summary>
    /// Shape raw in-game telemetry into something the caution and warning system can be
    /// told the truth by. Two corrections, both load-bearing.
    ///
    /// <b>1. Nothing in the game writes Telemetry.OnGround.</b> It is set by the sim's own
    /// ground model, and the bridge runs with UseInternalGroundModel = false because Godot
    /// owns contacts - so in the game the flag reads false with the skids on the dirt and
    /// every inhibit hung off it is inoperative. The sim knows this and falls back to
    /// HeightAgl &lt; 1.5, and in the game that fallback is false too: the height the sim
    /// reports is the height of the CENTRE OF GRAVITY, and the Workhorse's centre of
    /// gravity sits about two metres above its skids. The parked aircraft is therefore two
    /// metres "above the ground" and nothing is inhibited at all.
    ///
    /// So the game answers the question the game can actually answer - Godot's own contact
    /// count, via <see cref="LandingController.OnGround"/> - and writes it into the copy of
    /// the telemetry it passes down, with the measured skid height as a backstop for the
    /// frames before the landing controller has run. The struct is copied rather than
    /// mutated in place: the sim's telemetry belongs to the sim.
    ///
    /// <b>2. Spool-up.</b> The rotor conditions are inhibited while the engine is Off or
    /// Starting, but the sim declares Running the moment the starter finishes - with the
    /// rotor still coming through 60% on its way to governed speed. On the ground that is
    /// twenty seconds of LOW ROTOR at warning severity, and a horn, on every single start.
    /// A real aircraft has a cutout for exactly this, and this is it: until the rotor has
    /// reached governed speed once, a start on the ground is still a start. The latch
    /// re-arms only when the aircraft is on the ground with the rotor stopped and the
    /// engine not running, so a rotor that droops in flight - or on the ground after a
    /// successful start - is reported, which is the entire purpose.
    /// </summary>
    internal static FlightTelemetry Condition(FlightTelemetry t, bool contact, double skidHeight,
                                              ref bool rotorUpToSpeed)
    {
        bool onGround = contact || t.HeightAgl <= skidHeight + 0.6;

        // Airborne always arms it: whatever the rotor did on the ground, an aircraft that
        // is flying gets told the truth about itself. On the ground, 98% rather than the
        // 95% it was first written at, because the low-rotor ADVISORY band arms at 97 and
        // clears at 98 - releasing the cutout at 95 handed the pilot a 0.4 s flash of
        // "A ROTOR RPM 96%" at the end of every single start, measured.
        if (!onGround || t.RotorRpmPercent >= 98.0) rotorUpToSpeed = true;
        else if (t.RotorRpmPercent < 40.0 && t.Engine != EngineState.Running)
            rotorUpToSpeed = false;

        t.OnGround = onGround;
        if (onGround && !rotorUpToSpeed && t.Engine == EngineState.Running)
            t.Engine = EngineState.Starting;
        return t;
    }

    public override void _Process(double delta)
    {
        _blink += delta;
        _age += delta;

        if (_selfCheck && _age > 1.5) { RunSelfCheck(); return; }
        if (_heli is null) return;

        FlightTelemetry t = _heli.Sim.Telemetry;

        if (!_live)
        {
            if (t.HeightAgl == 0 && t.RotorRpmPercent == 0) return;
            _live = true;
            _arming = ArmSeconds;
            _lastHeight = t.HeightAgl;
        }

        // A respawn or a fast travel is a different flight. Carrying a latched engine
        // failure across a teleport would be reporting the state of an aircraft that no
        // longer exists. 50 m in one frame is 3 km/s at 60 Hz: nothing but a teleport.
        if (Math.Abs(t.HeightAgl - _lastHeight) > 50.0)
        {
            _cws.Reset();
            _synth.Reset();
            _rotorUpToSpeed = false;
            _arming = ArmSeconds;    // a teleport re-places the aircraft trimmed: same transient
        }
        _lastHeight = t.HeightAgl;

        if (_arming > 0)
        {
            _arming -= delta;
            return;
        }

        // Acknowledge before the update, so a caution raised this frame still gets its
        // chime rather than being silenced by a key pressed a moment earlier.
        bool ack = !_quiet && Input.IsKeyPressed(AcknowledgeKey);
        if (ack && !_ackHeld && _cws.Items.Count > 0)
        {
            int dropped = _cws.Acknowledge();
            GD.Print($"[cws] acknowledged - {dropped} latched condition(s) cleared, " +
                     $"{_cws.Items.Count} still shown");
        }
        _ackHeld = ack;

        _cws.Update(Condition(t, _landing?.OnGround ?? false, _skidHeight, ref _rotorUpToSpeed),
                    delta);

        _synth.Follow(_cws);
        PushAudio();

        if (!_quiet) QueueRedraw();
    }

    private void PushAudio()
    {
        if (_playback is null) return;
        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        if (_block.Length < frames) _block = new float[frames];
        _synth.Render(_block, frames, frames / (double)SampleRate);

        var buffer = new Vector2[frames];
        for (int i = 0; i < frames; i++) buffer[i] = new Vector2(_block[i], _block[i]);
        _playback.PushBuffer(buffer);
    }

    // ----------------------------------------------------------------- drawing

    public override void _Draw()
    {
        if (!_live) return;
        var items = _cws.Items;
        if (items.Count == 0 && !_cws.MasterWarning && !_cws.MasterCaution) return;

        // Motion is the one thing peripheral vision is reliably good at, so it is spent on
        // one thing: a warning-severity condition that is true right now.
        bool flash = (int)(_blink * 2.6) % 2 == 0;

        float x = Size.X - 24f - PanelWidth;
        float y = 22f;

        if (_cws.MasterWarning || _cws.MasterCaution)
        {
            float half = (PanelWidth - 8f) / 2f;
            if (_cws.MasterWarning)
                Tile(new Rect2(x, y, half, TileHeight), "MASTER WARNING", WarningColour, flash);
            if (_cws.MasterCaution)
                Tile(new Rect2(x + half + 8f, y, half, TileHeight), "MASTER CAUTION",
                     CautionColour, flash);
            y += TileHeight + 8f;
        }

        // The HUD's right-hand gauge stack - torque and Nr, the two instruments a
        // helicopter pilot lives by - starts at 30% of screen height, and covering those
        // to report that something is wrong with them would be its own kind of joke. So
        // the list is bounded by the room above them rather than by a constant, and the
        // sort has already put the worst conditions at the top: what gets cut is always
        // the least of it, and the count says so.
        int room = Mathf.Max(3, (int)((Size.Y * 0.30f - 30f - y) / RowHeight));
        int shown = Math.Min(items.Count, Math.Min(MaxRows, room));
        for (int i = 0; i < shown; i++)
        {
            Row(new Rect2(x, y, PanelWidth, RowHeight - 4f), items[i], flash);
            y += RowHeight;
        }

        if (items.Count > shown)
        {
            DrawString(_font, new Vector2(x + 30, y + 14), $"+{items.Count - shown} MORE",
                       HorizontalAlignment.Left, -1, 13, AdvisoryColour with { A = 0.7f });
            y += 20;
        }

        // The binding, printed where the pilot is already looking and only while it does
        // something. A key nobody can discover is a key nobody has.
        if (_cws.MasterCaution)
            DrawString(_font, new Vector2(x + 30, y + 15), "M   ACKNOWLEDGE",
                       HorizontalAlignment.Left, -1, 12, CautionColour with { A = 0.75f });
    }

    /// <summary>A glareshield light: the loudest thing on the panel and the least detailed.</summary>
    private void Tile(Rect2 r, string text, Color colour, bool flash)
    {
        DrawRect(r, Body);
        if (!flash)
        {
            // Dark half of the blink. The box stays, so nothing below it moves - a list
            // that jumps by 46 px twice a second cannot be read at all.
            DrawRect(r, colour with { A = 0.25f }, false, 1.2f);
            return;
        }
        DrawRect(r, colour * new Color(1, 1, 1, 0.34f));
        DrawRect(r, colour, false, 2.2f);
        Vector2 sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 15);
        DrawString(_font, new Vector2(r.Position.X + (r.Size.X - sz.X) / 2, r.Position.Y + 25),
                   text, HorizontalAlignment.Left, -1, 15, new Color(1, 1, 1, 0.96f));
    }

    /// <summary>
    /// One line of the annunciator.
    ///
    /// Severity is carried three ways on purpose, because any one of them fails somewhere:
    /// colour (which fails in peripheral vision, and for a colour-blind player), fill (a
    /// warning is a solid block, a caution an outline, an advisory a bare tick), and a
    /// letter in the gutter. Latched-but-gone is the fourth state, and it is drawn as the
    /// ghost of the row it was - hollow, dimmed and tagged - because a latch exists to tell
    /// you what happened while you were looking somewhere else.
    /// </summary>
    private void Row(Rect2 r, WarningItem item, bool flash)
    {
        Color colour = item.Severity switch
        {
            WarningSeverity.Warning => WarningColour,
            WarningSeverity.Caution => CautionColour,
            _ => AdvisoryColour,
        };
        string letter = item.Severity switch
        {
            WarningSeverity.Warning => "W",
            WarningSeverity.Caution => "C",
            _ => "A",
        };

        bool live = !item.Stale;
        bool blinking = live && item.Severity == WarningSeverity.Warning;
        float dim = live ? 1.0f : 0.62f;

        if (item.Severity == WarningSeverity.Advisory)
        {
            // Advisories get no box at all. They are information, and information that
            // shouts is indistinguishable from trouble.
            DrawRect(new Rect2(r.Position, new Vector2(3, r.Size.Y)), colour with { A = 0.6f * dim });
        }
        else
        {
            DrawRect(r, Body);
            if (blinking && flash)
                DrawRect(r, colour * new Color(1, 1, 1, 0.30f));
            else if (live && item.Severity == WarningSeverity.Caution)
                DrawRect(r, colour * new Color(1, 1, 1, 0.14f));
            DrawRect(r, colour with { A = dim * (blinking && !flash ? 0.35f : 1.0f) }, false,
                     item.Severity == WarningSeverity.Warning ? 2.0f : 1.3f);
        }

        Color ink = colour with { A = dim };
        DrawString(_font, new Vector2(r.Position.X + 10, r.Position.Y + 19), letter,
                   HorizontalAlignment.Left, -1, 15, ink);
        DrawString(_font, new Vector2(r.Position.X + 30, r.Position.Y + 19), item.Text,
                   HorizontalAlignment.Left, -1, 17, ink);

        string? tag = item.Stale ? "LATCHED" : item.Acknowledged ? "ACK" : null;
        if (tag is null) return;
        Vector2 sz = _font.GetStringSize(tag, HorizontalAlignment.Left, -1, 11);
        DrawString(_font, new Vector2(r.Position.X + r.Size.X - sz.X - 10, r.Position.Y + 18),
                   tag, HorizontalAlignment.Left, -1, 11, colour with { A = 0.62f });
    }

    // -------------------------------------------------------------- self-check

    /// <summary>
    /// --warntest: the things about this node that cannot be seen in the source.
    ///
    /// <code>
    ///   Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game -- --warntest
    /// </code>
    ///
    /// It measures where the parked aircraft actually sits, drives a start-up on the skids
    /// through the system twice - once with the raw telemetry the game hands out and once
    /// through <see cref="Condition"/> - and prints what each produces. The raw run is the
    /// control, and it is the reason this node has the shape it has.
    /// </summary>
    private void RunSelfCheck()
    {
        _selfCheck = false;
        int failures = 0;

        // --- 1. Where is the ground? ------------------------------------------
        // Flat ground and still air, so this measures the airframe rather than the terrain
        // it happens to be parked on - and it brings no Godot objects with it.
        var parked = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 600 };
        parked.PlaceOnGround(running: true);
        parked.Step(1.0 / 240.0);
        FlightTelemetry p = parked.Telemetry;
        GD.Print($"[warntest] parked, sim ground model:  OnGround={p.OnGround}  " +
                 $"HeightAgl={p.HeightAgl:F2} m  Nr={p.RotorRpmPercent:F0}%");
        GD.Print($"[warntest] skid height from airframe: {_skidHeight:F2} m " +
                 "(the sim's in-game fallback only triggers below 1.50 m)");
        if (_skidHeight < 1.5)
        {
            GD.PrintErr("[warntest] FAIL: skid height is below the sim's fallback - the " +
                        "OnGround workaround would have been sufficient after all");
            failures++;
        }

        if (_heli is not null)
            GD.Print($"[warntest] live aircraft: HeightAgl={_heli.Sim.Telemetry.HeightAgl:F1} m  " +
                     $"Telemetry.OnGround={_heli.Sim.Telemetry.OnGround}  " +
                     $"LandingController.OnGround={_landing?.OnGround.ToString() ?? "n/a"}");

        // --- 2. A start on the skids ------------------------------------------
        // Engine off for 2 s, starter for 4, then the rotor winds up to governed speed
        // over 18 s with the aircraft sitting on the ground throughout.
        string worstRaw = "(clear)", worstFixed = "(clear)";
        int rawFrames = 0, fixedFrames = 0;
        var raw = new CautionWarningSystem();
        var conditioned = new CautionWarningSystem();
        bool up = false;
        const double Dt = 1.0 / 60.0;
        for (int i = 0; i < 60 * 24; i++)
        {
            double time = i * Dt;
            var t = new FlightTelemetry
            {
                HeightAgl = _skidHeight,
                RotorRpmPercent = time < 6 ? 0 : Math.Min(100.0, (time - 6) / 18.0 * 100.0),
                TorquePercent = time < 6 ? 0 : 22,
                FuelKg = 600,
                FuelFlow = time < 2 ? 0 : 0.03,
                Engine = time < 2 ? EngineState.Off
                       : time < 6 ? EngineState.Starting
                       : EngineState.Running,
            };

            raw.Update(t, Dt);
            if (raw.Items.Count > 0) { rawFrames++; worstRaw = raw.Summary(); }

            conditioned.Update(Condition(t, true, _skidHeight, ref up), Dt);
            if (conditioned.Items.Count > 0) { fixedFrames++; worstFixed = conditioned.Summary(); }
        }
        GD.Print($"[warntest] start-up on the skids, raw telemetry: {rawFrames} frames with " +
                 $"something on the panel - {worstRaw}");
        GD.Print($"[warntest] start-up on the skids, conditioned:   {fixedFrames} frames with " +
                 $"something on the panel - {worstFixed}");
        if (fixedFrames != 0)
        {
            GD.PrintErr("[warntest] FAIL: the panel is not clear through a normal start");
            failures++;
        }
        if (rawFrames == 0)
            GD.Print("[warntest] note: the raw run was clean too - the conditioning is belt " +
                     "and braces rather than a fix");

        // --- 3. It still warns when it should ----------------------------------
        // Same aircraft, in the air, rotor decaying through the horn threshold.
        var flying = new CautionWarningSystem();
        bool up2 = true;
        bool horn = false;
        for (int i = 0; i < 60 * 8; i++)
        {
            var t = new FlightTelemetry
            {
                HeightAgl = 300,
                RotorRpmPercent = 100.0 - i * Dt * 4.0,
                TorquePercent = 40,
                FuelKg = 600,
                FuelFlow = 0.03,
                Engine = EngineState.Running,
            };
            flying.Update(Condition(t, false, _skidHeight, ref up2), Dt);
            if (flying.LowRotorHorn) horn = true;
        }
        GD.Print($"[warntest] rotor decay in flight: horn={horn}  panel - {flying.Summary()}");
        if (!horn)
        {
            GD.PrintErr("[warntest] FAIL: no horn for a decaying rotor in flight");
            failures++;
        }

        // --- 4. Acknowledge -----------------------------------------------------
        // A warning that is still true is not silenced by acknowledging it.
        int before = flying.Items.Count;
        int droppedNow = flying.Acknowledge();
        GD.Print($"[warntest] acknowledge a live warning ({AcknowledgeKey}): {before} shown -> " +
                 $"{flying.Items.Count} shown, {droppedNow} dropped, horn={flying.LowRotorHorn}");
        if (!flying.LowRotorHorn)
        {
            GD.PrintErr("[warntest] FAIL: acknowledging silenced a condition that is still true");
            failures++;
        }

        // A caution that has latched and then gone away is what the key is FOR.
        var latched = new CautionWarningSystem();
        bool up3 = true;
        for (int i = 0; i < 60 * 14; i++)
        {
            var t = new FlightTelemetry
            {
                HeightAgl = 200,
                RotorRpmPercent = 100,
                TorquePercent = i < 60 * 6 ? 95 : 55,     // caution arms at 92, clears at 89
                FuelKg = 600,
                FuelFlow = 0.05,
                Engine = EngineState.Running,
            };
            latched.Update(Condition(t, false, _skidHeight, ref up3), Dt);
        }
        string held = latched.Summary();
        int dropped2 = latched.Acknowledge();
        GD.Print($"[warntest] latched caution: {held} -> acknowledge dropped {dropped2}, " +
                 $"{latched.Items.Count} shown, masterCaution={latched.MasterCaution}");
        if (dropped2 != 1 || latched.Items.Count != 0 || latched.MasterCaution)
        {
            GD.PrintErr("[warntest] FAIL: a cleared, latched caution did not acknowledge away");
            failures++;
        }

        // --- 5. Audio balance ---------------------------------------------------
        int n = SampleRate * 2;
        var block = new float[n];
        var af = Airframe.Workhorse();
        var hover = new SynthState
        {
            RotorOmega = af.MainRotor.NominalOmega,
            NominalOmega = af.MainRotor.NominalOmega,
            MainBlades = af.MainRotor.NumBlades,
            TailBlades = af.TailRotor.NumBlades,
            TailGearRatio = af.TailRotor.GearRatio,
            Collective = 0.62,
            TorqueFraction = 0.75,
            N1 = 1.0,
            EngineRunning = true,
            BladeLoading = 0.0748,
            TipMach = 0.703,
            TailRotorHealth = 1,
        };
        var rotor = new RotorSynth(SampleRate) { Volume = 0.75 };
        rotor.Prime(hover);
        rotor.Render(block, n, hover, n / (double)SampleRate);
        double rotorDb = Db(Rms(block, n)) - 2.5 + 3.0;   // player gain, then Godot's max_db clamp

        var voice = new WarningSynth(SampleRate);
        voice.Render(block, n, true, false, n / (double)SampleRate);
        double hornDb = Db(Rms(block, n)) + Mathf.LinearToDb(AudioGain);

        var voice2 = new WarningSynth(SampleRate);
        voice2.Render(block, n, false, true, n / (double)SampleRate);
        double warnDb = Db(Rms(block, n)) + Mathf.LinearToDb(AudioGain);

        GD.Print($"[warntest] at the listener: rotor(hover) {rotorDb:F1} dBFS, " +
                 $"horn {hornDb:F1} dBFS ({hornDb - rotorDb:+0.0;-0.0} dB), " +
                 $"master warning {warnDb:F1} dBFS ({warnDb - rotorDb:+0.0;-0.0} dB)");
        if (hornDb - rotorDb < 2.0 || hornDb - rotorDb > 9.0)
        {
            GD.PrintErr("[warntest] FAIL: the horn is not 2-9 dB over the hovering rotor");
            failures++;
        }

        GD.Print(failures == 0 ? "[warntest] PASS" : $"[warntest] FAILED ({failures})");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private static double Rms(float[] b, int n)
    {
        double s = 0;
        for (int i = 0; i < n; i++) s += (double)b[i] * b[i];
        return Math.Sqrt(s / n);
    }

    private static double Db(double x) => 20.0 * Math.Log10(Math.Max(x, 1e-12));
}
