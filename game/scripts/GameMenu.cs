using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The title screen, the pause menu and the settings screen.
///
/// <para><b>What was there before this: nothing.</b> The game booted straight into flight and
/// <c>Escape</c> called <c>GetTree().Quit()</c> immediately, with no confirmation and no
/// pause - one stray keypress ended the session. <c>GetTree().Paused</c> was not used
/// anywhere in the project, so there was no way to stop, no way to change a setting without
/// editing a file, and no way to save deliberately rather than hoping you remembered F5.
/// That is not a missing feature so much as a missing floor.</para>
///
/// <para><b>The title screen is the paused game, not a separate scene.</b> The world loads
/// behind the menu with the tree paused and a camera parked somewhere worth looking at. That
/// is less machinery than a scene transition - no second scene to keep in sync, no reload
/// when you press Start - and it means there is no loading wait between the menu and flying,
/// because the loading already happened while you were reading the menu.</para>
///
/// <para><b>It looks like the kneeboard on purpose.</b> This game has a UI language already -
/// dark paper, pale ink, rules and stamps, everything drawn in code - and a menu in some
/// other style would read as a different product bolted to the front. AAA polish here is not
/// gloss; it is that the menu belongs to the same world as the thing behind it.</para>
/// </summary>
public sealed partial class GameMenu : Control
{
    private enum Page { Hidden, Title, Paused, Settings, ConfirmQuit, SaveTo, LoadFrom }

    private static readonly Color Paper = new(0.06f, 0.07f, 0.065f, 0.96f);
    private static readonly Color Ink = new(0.86f, 0.88f, 0.80f);
    private static readonly Color Faint = new(0.46f, 0.50f, 0.47f);
    private static readonly Color Hot = new(1.00f, 0.86f, 0.42f);

    private readonly Main _main;
    private Font _font = null!;
    private Page _page = Page.Hidden;
    private int _row;
    private string _flash = "";
    private double _flashAge = 99;

    /// <summary>True while anything is on screen and the world is stopped.</summary>
    public bool Open => _page != Page.Hidden;

    public GameMenu(Main main) => _main = main;

    // ------------------------------------------------------------------ rows

    private readonly record struct Row(string Label, Action Act, Func<string>? Value = null);

    private List<Row> RowsFor(Page p) => p switch
    {
        Page.Title => new()
        {
            // CONTINUE takes the NEWEST save rather than a fixed slot, which is what the
            // word means everywhere else and saves the player from having to remember
            // which one they were on.
            new(Main.NewestSlot() == int.MinValue ? "CONTINUE (no saves)" : "CONTINUE", () =>
            {
                int slot = Main.NewestSlot();
                if (slot == int.MinValue) { Flash("nothing saved yet"); return; }
                string? why = _main.LoadGame(slot);
                if (why is null) Close(); else Flash(why);
            }),
            new("NEW SORTIE", Close),
            new("LOAD", () => Go(Page.LoadFrom)),
            new("SETTINGS", () => Go(Page.Settings)),
            new("QUIT", () => Go(Page.ConfirmQuit)),
        },
        Page.Paused => new()
        {
            new("RESUME", Close),
            new("SAVE", () => Go(Page.SaveTo)),
            new("LOAD", () => Go(Page.LoadFrom)),
            new("SETTINGS", () => Go(Page.Settings)),
            new("FLIGHT CONTROLS", () => { Close(); _main.OpenBindings(); }),
            new("QUIT", () => Go(Page.ConfirmQuit)),
        },
        Page.SaveTo => Slots(save: true),
        Page.LoadFrom => Slots(save: false),
        Page.Settings => new()
        {
            new("Master volume", () => { }, () => $"{Settings.Master * 100:F0}%"),
            new("Radio volume", () => { }, () => $"{Settings.Radio * 100:F0}%"),
            new("Graphics", () => { }, () => QualityTier.Current.ToString()),
            new("Field of view", () => { }, () => $"{Settings.Fov:F0} deg"),
            new("Look sensitivity", () => { }, () => $"{Settings.LookSensitivity:F2}"),
            new("Invert cyclic pitch", () => { }, () => Settings.InvertPitch ? "yes" : "no"),
            new("Stability assist", () => { }, () => Settings.Assist switch
            {
                AssistLevel.Off      => "off - bare airframe",
                AssistLevel.Light    => "light - rate damping",
                AssistLevel.Standard => "standard",
                _                    => "full - holds attitude",
            }),
            new("Back", () => Go(_main.Started ? Page.Paused : Page.Title)),
        },
        Page.ConfirmQuit => new()
        {
            new("Keep flying", () => Go(_main.Started ? Page.Paused : Page.Title)),
            new("Save and quit", () =>
            {
                string? why = _main.SaveGame(0);
                if (why is null) GetTree().Quit();
                else Flash($"cannot save - {why}");
            }),
            new("Quit without saving", () => GetTree().Quit()),
        },
        _ => new(),
    };

    /// <summary>
    /// The slot list, for saving or loading.
    ///
    /// The autosave is in the list for LOADING and absent from SAVING, which is the whole
    /// reason to have one: it is the copy the player cannot accidentally write over with a
    /// worse position. Empty slots are shown rather than hidden, because a list that grows
    /// as you use it is harder to navigate than one that is always the same shape.
    /// </summary>
    private List<Row> Slots(bool save)
    {
        var rows = new List<Row>();

        if (!save)
            rows.Add(new("Autosave", () => Use(-1, false), () => Main.DescribeSlot(-1)));

        for (int i = 0; i < Main.SlotCount; i++)
        {
            int slot = i;
            rows.Add(new($"Slot {slot + 1}", () => Use(slot, save), () => Main.DescribeSlot(slot)));
        }
        rows.Add(new("Back", () => Go(_main.Started ? Page.Paused : Page.Title)));
        return rows;
    }

    private void Use(int slot, bool save)
    {
        if (save)
        {
            string? why = _main.SaveGame(slot);
            Flash(why is null ? $"saved to slot {slot + 1}" : $"cannot save - {why}");
            return;
        }
        string? fail = _main.LoadGame(slot);
        if (fail is null) Close(); else Flash(fail);
    }

    // ------------------------------------------------------------------ life

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        // The whole point: this node keeps running while everything else is stopped.
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
    }

    /// <summary>Bring up the title. Called by Main once the world is built.</summary>
    public void ShowTitle()
    {
        Go(Page.Title);
        Settings.Apply();
    }

    /// <summary>Overlays hidden while the menu is up, and whether they were on before.</summary>
    private readonly List<(CanvasItem Node, bool Was)> _hidden = new();

    private void Go(Page p)
    {
        bool wasOpen = _page != Page.Hidden;
        bool open = p != Page.Hidden;

        _page = p;
        _row = 0;
        Visible = open;
        GetTree().Paused = open;
        if (open) Input.MouseMode = Input.MouseModeEnum.Visible;

        if (open && !wasOpen) TakeOverScreen(p == Page.Title);
        else if (!open && wasOpen) GiveScreenBack();

        QueueRedraw();
    }

    /// <summary>
    /// Get everything else off the screen, and point the camera at something worth seeing.
    ///
    /// The first cut drew the menu straight over the running HUD, and the result was faint
    /// warning captions and site labels showing through the wash behind the menu items - not
    /// obviously broken in a screenshot, just grubby, which is the failure mode a menu is
    /// least able to survive. So the overlays go away while the menu is up and come back
    /// exactly as they were.
    ///
    /// The camera only moves for the TITLE. Pausing should show you where you left the
    /// aircraft, because that is most of why anybody pauses; arriving at the title screen
    /// should show you the aircraft, because the alternative is the inside of a cockpit at
    /// whatever angle the last frame happened to be.
    /// </summary>
    private void TakeOverScreen(bool title)
    {
        _hidden.Clear();
        foreach (Node layer in GetTree().Root.GetChildren())
            Collect(layer);

        if (!title) return;
        if (GetTree().Root.FindChild("Camera", true, false) is ChaseCamera cam)
        {
            _restoreMode = cam.Mode;
            _restoredCamera = cam;
            cam.Mode = CameraMode.Orbit;
            // The tree is paused, so the camera's own _Process is not running and setting
            // the mode alone does nothing at all - the first attempt at this changed the
            // mode and produced a byte-identical screenshot. It has to be exempted from the
            // pause to orbit, which is also nicer: a title screen that drifts slowly round
            // the aircraft is worth more than a still of wherever the last frame was.
            cam.ProcessMode = ProcessModeEnum.Always;
        }

        void Collect(Node n)
        {
            foreach (Node child in n.GetChildren())
            {
                if (child == this) continue;
                if (child is CanvasItem ci && ci.Visible && child is not CanvasLayer)
                {
                    _hidden.Add((ci, true));
                    ci.Visible = false;
                    continue;               // its children go with it
                }
                Collect(child);
            }
        }
    }

    private ChaseCamera? _restoredCamera;
    private CameraMode _restoreMode = CameraMode.Chase;

    private void GiveScreenBack()
    {
        foreach ((CanvasItem node, bool was) in _hidden)
            if (GodotObject.IsInstanceValid(node)) node.Visible = was;
        _hidden.Clear();

        if (_restoredCamera is not null && GodotObject.IsInstanceValid(_restoredCamera))
        {
            _restoredCamera.Mode = _restoreMode;
            _restoredCamera.ProcessMode = ProcessModeEnum.Inherit;
        }
        _restoredCamera = null;
    }

    public void Close()
    {
        _main.Started = true;
        Go(Page.Hidden);
    }

    public void TogglePause()
    {
        if (_page == Page.Hidden) Go(Page.Paused);
        else if (_page == Page.Paused) Close();
        else Go(_main.Started ? Page.Paused : Page.Title);
    }

    private void Flash(string s) { _flash = s; _flashAge = 0; }

    // ----------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Open || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        List<Row> rows = RowsFor(_page);
        if (rows.Count == 0) return;

        switch (k.Keycode)
        {
            case Key.Up: _row = (_row + rows.Count - 1) % rows.Count; break;
            case Key.Down: _row = (_row + 1) % rows.Count; break;

            // Left and right adjust a value; on a plain menu row they do nothing, which is
            // the behaviour people expect from every menu they have ever used.
            case Key.Left: Adjust(-1); break;
            case Key.Right: Adjust(+1); break;

            case Key.Enter:
            case Key.KpEnter:
            case Key.Space:
                if (_page == Page.Settings && rows[_row].Value is not null) Adjust(+1);
                else rows[_row].Act();          // slot rows carry a value AND an action
                break;

            case Key.Delete:
                if (_page is Page.SaveTo or Page.LoadFrom)
                {
                    int slot = _page == Page.LoadFrom ? _row - 1 : _row;   // autosave is row 0 on load
                    if (slot >= 0 && slot < Main.SlotCount)
                    {
                        Main.DeleteSlot(slot);
                        Flash($"slot {slot + 1} cleared");
                    }
                    else Flash("the autosave is not yours to delete");
                }
                break;

            case Key.Escape:
                if (_page is Page.Settings or Page.ConfirmQuit or Page.SaveTo or Page.LoadFrom)
                    Go(_main.Started ? Page.Paused : Page.Title);
                else if (_page == Page.Paused) Close();
                break;
        }
        QueueRedraw();
    }

    private void Adjust(int dir)
    {
        if (_page != Page.Settings) return;
        switch (_row)
        {
            case 0: Settings.Master = Step(Settings.Master, dir); break;
            case 1: Settings.Radio = Step(Settings.Radio, dir); break;
            case 2:
            {
                int t = Mathf.Clamp((int)QualityTier.Current + dir, 0, 3);
                QualityTier.Set((QualityTier.Tier)t);
                break;
            }
            case 3: Settings.Fov = Mathf.Clamp(Settings.Fov + dir * 5f, 50f, 110f); break;
            case 4: Settings.LookSensitivity = Mathf.Clamp(Settings.LookSensitivity + dir * 0.05f, 0.1f, 3.0f); break;
            case 5: Settings.InvertPitch = !Settings.InvertPitch; break;
            case 6:
            {
                int a = Mathf.Clamp((int)Settings.Assist + dir, 0, 3);
                Settings.Assist = (AssistLevel)a;
                break;
            }
        }
        Settings.Apply();
        Settings.Save();
    }

    private static float Step(float v, int dir) => Mathf.Clamp(v + dir * 0.05f, 0f, 1f);

    public override void _Process(double delta)
    {
        _flashAge += delta;
        if (Open) QueueRedraw();
    }

    // ------------------------------------------------------------------ draw

    public override void _Draw()
    {
        if (!Open) return;
        Vector2 size = GetViewportRect().Size;

        // A GRADIENT wash, heavy under the text and light over the view.
        //
        // A flat wash was the first attempt and it crushed the scene to a smear: the whole
        // reason the title screen is the paused game is so there is something behind it
        // worth looking at, and then 86% of flat paper over the top threw that away. Heavy
        // where the words are, clear where the aircraft is, which is the oldest trick there
        // is and works because the eye needs contrast only where it is reading.
        float heavy = _page == Page.Title ? 0.88f : 0.80f;
        float light = _page == Page.Title ? 0.18f : 0.55f;
        // Drawn as vertex-coloured quads rather than a stack of DrawRects. The rects
        // version left visible vertical seams: each band was drawn one pixel wider than its
        // slot to avoid gaps, so every boundary got blended twice and read as a thin line
        // down the screen - thirty-two of them. Sharing the exact edge colour between
        // adjacent quads gives a continuous ramp with nothing to see.
        const int bands = 24;
        float Alpha(int i)
        {
            float t = Mathf.Clamp((i / (float)bands - 0.34f) / 0.42f, 0f, 1f);
            return Mathf.Lerp(heavy, light, t * t);
        }
        for (int i = 0; i < bands; i++)
        {
            float x0 = size.X * i / bands, x1 = size.X * (i + 1) / bands;
            var cl = new Color(Paper.R, Paper.G, Paper.B, Alpha(i));
            var cr = new Color(Paper.R, Paper.G, Paper.B, Alpha(i + 1));
            DrawPolygon(
                new[] { new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, size.Y), new Vector2(x0, size.Y) },
                new[] { cl, cr, cr, cl });
        }

        float x = size.X * 0.14f;
        float y = size.Y * 0.26f;

        if (_page == Page.Title)
        {
            DrawString(_font, new Vector2(x, y), "ROTORWASH", HorizontalAlignment.Left, -1, 64, Ink);
            y += 26;
            DrawString(_font, new Vector2(x + 4, y), "one aircraft, one pilot, and a district that talks",
                       HorizontalAlignment.Left, -1, 16, Faint);
            y += 70;
        }
        else
        {
            string heading = _page switch
            {
                Page.Settings => "SETTINGS",
                Page.ConfirmQuit => "QUIT?",
                Page.SaveTo => "SAVE TO",
                Page.LoadFrom => "LOAD FROM",
                _ => "PAUSED",
            };
            DrawString(_font, new Vector2(x, y), heading, HorizontalAlignment.Left, -1, 34, Ink);
            DrawLine(new Vector2(x, y + 10), new Vector2(x + 420, y + 10), Faint, 1.2f);
            y += 58;
        }

        List<Row> rows = RowsFor(_page);
        for (int i = 0; i < rows.Count; i++)
        {
            bool sel = i == _row;
            Color ink = sel ? Hot : Ink;

            if (sel)
                DrawString(_font, new Vector2(x - 26, y), ">", HorizontalAlignment.Left, -1, 22, Hot);
            DrawString(_font, new Vector2(x, y), rows[i].Label, HorizontalAlignment.Left, -1, 22, ink);

            if (rows[i].Value is Func<string> v)
            {
                string value = v();
                bool slotPage = _page is Page.SaveTo or Page.LoadFrom;
                DrawString(_font, new Vector2(x + (slotPage ? 150 : 340), y), value,
                           HorizontalAlignment.Left, -1, slotPage ? 16 : 20,
                           sel ? Hot : Faint);
                if (sel && !slotPage)
                    DrawString(_font, new Vector2(x + 340 + 150, y), "< >", HorizontalAlignment.Left, -1, 16, Faint);
            }
            y += 40;
        }

        y += 24;
        string help = _page switch
        {
            Page.Settings => "up/down choose   left/right change   ESC back",
            Page.SaveTo or Page.LoadFrom => "up/down choose   ENTER use   DEL clear   ESC back",
            _ => "up/down choose   ENTER select   ESC back",
        };
        DrawString(_font, new Vector2(x, y), help, HorizontalAlignment.Left, -1, 14, Faint);

        if (_flash.Length > 0 && _flashAge < 3.0)
            DrawString(_font, new Vector2(x, y + 24), _flash, HorizontalAlignment.Left, -1, 16,
                       new Color(0.55f, 0.85f, 0.65f, (float)Math.Max(0, 1 - _flashAge / 3.0)));

        // The version line, bottom right, which every game has and which is the first thing
        // anybody reporting a bug should be able to read off the screen.
        //
        // It said `Engine.GetVersionInfo()` for about ten minutes, which is GODOT's version -
        // so every bug report would have carried the engine build and nothing at all about
        // the game. There is no git SHA available at runtime, so this says "dev" rather than
        // inventing a number: a stamp that is vague is recoverable, one that is confidently
        // wrong is not.
        string stamp = $"ROTORWASH dev  ·  {QualityTier.Current}  ·  Godot {Engine.GetVersionInfo()["string"]}";
        Vector2 sz = _font.GetStringSize(stamp, HorizontalAlignment.Left, -1, 12);
        DrawString(_font, new Vector2(size.X - sz.X - 24, size.Y - 20), stamp,
                   HorizontalAlignment.Left, -1, 12, Faint * new Color(1, 1, 1, 0.8f));
    }
}

/// <summary>
/// The handful of things a player expects to be able to change, and a file to keep them in.
///
/// Deliberately small. Every entry here is something that cannot be worked out on the
/// player's behalf: how loud they want it, how much their machine can do, how wide they like
/// the view, and which way their stick should go. Anything that CAN be worked out - the
/// graphics tier's first guess, the control layout for known hardware - is worked out, and
/// only appears here so it can be overridden.
/// </summary>
public static class Settings
{
    private const string ConfigPath = "user://settings.cfg";

    public static float Master { get; set; } = 0.9f;
    public static float Radio { get; set; } = 0.55f;
    public static float Fov { get; set; } = 75f;
    public static float LookSensitivity { get; set; } = 1.0f;
    public static bool InvertPitch { get; set; }

    /// <summary>
    /// How much the aircraft helps its pilot.
    ///
    /// The ladder has existed in <see cref="Rotorwash.Sim.Stability"/> for a long time,
    /// with four measured rungs and simlab tests for three of them - and nothing in the
    /// game ever called Set(). The only caller in the repository was the test suite, so
    /// the level was whatever the field initialisers happened to be and the player had no
    /// say in it at all. visual-check.md asks a tester to "fly a minute at each of Off,
    /// Light and Standard", which could not be done from inside the game.
    /// </summary>
    public static AssistLevel Assist { get; set; } = AssistLevel.Standard;

    /// <summary>Set by Main so changing the assist level reaches the aircraft.</summary>
    public static Helicopter? Aircraft { get; set; }

    /// <summary>Set by Main so the FOV slider has something to move.</summary>
    public static Camera3D? Camera { get; set; }

    public static void Apply()
    {
        int master = AudioServer.GetBusIndex("Master");
        if (master >= 0)
            AudioServer.SetBusVolumeDb(master, Master <= 0.001f ? -80f : Mathf.LinearToDb(Master));

        if (Camera is not null && IsInstanceValid(Camera)) Camera.Fov = Fov;
        Aircraft?.Sas.Set(Assist);
    }

    private static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", Master);
        cfg.SetValue("audio", "radio", Radio);
        cfg.SetValue("view", "fov", Fov);
        cfg.SetValue("view", "look", LookSensitivity);
        cfg.SetValue("view", "invert_pitch", InvertPitch);
        cfg.SetValue("flight", "assist", (int)Assist);
        cfg.Save(ConfigPath);
    }

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok) return;
        Master = (float)cfg.GetValue("audio", "master", Master);
        Radio = (float)cfg.GetValue("audio", "radio", Radio);
        Fov = (float)cfg.GetValue("view", "fov", Fov);
        LookSensitivity = (float)cfg.GetValue("view", "look", LookSensitivity);
        InvertPitch = (bool)cfg.GetValue("view", "invert_pitch", InvertPitch);
        Assist = (AssistLevel)Mathf.Clamp((int)cfg.GetValue("flight", "assist", (int)Assist), 0, 3);
    }
}
