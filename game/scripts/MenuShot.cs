using System;
using Godot;

namespace Rotorwash;

/// <summary>
/// Photograph the menu pages, one after another.
///
/// The title screen is the only part of this game that cannot be checked by measuring
/// something, and the rule this project runs on is that anything visual gets looked at. So
/// this walks the menu through its pages and saves a PNG of each, which is a thing a person
/// on a laptop that cannot render the world at speed can still open and judge.
/// </summary>
public sealed partial class MenuShot : Node
{
    private readonly GameMenu _menu;
    private double _t;
    private int _shot;

    public MenuShot(GameMenu menu)
    {
        _menu = menu;
        ProcessMode = ProcessModeEnum.Always;   // the tree is paused behind the menu
    }

    /// <summary>
    /// A timeline of presses and photographs, kept SEPARATE.
    ///
    /// The first version pressed keys and took the picture in the same tick, and every shot
    /// after the first came out showing the previous page: the menu had changed state but
    /// the viewport had not been redrawn yet, so the capture read the old frame. Pressing
    /// on one step and shooting on a later one is the whole fix.
    /// </summary>
    private readonly record struct Step(double At, Key[] Keys, string? Shot);

    private static readonly Step[] Plan =
    {
        new(2.5, System.Array.Empty<Key>(), "menu-title"),
        new(3.0, new[] { Key.Down, Key.Down, Key.Enter }, null),
        new(3.4, System.Array.Empty<Key>(), "menu-load"),
        new(3.8, new[] { Key.Escape, Key.Down, Key.Down, Key.Down, Key.Enter }, null),
        new(4.2, System.Array.Empty<Key>(), "menu-settings"),
    };

    public override void _Process(double delta)
    {
        _t += delta;
        if (_shot >= Plan.Length) { GetTree().Quit(0); return; }

        Step step = Plan[_shot];
        if (_t < step.At) return;

        foreach (Key k in step.Keys)
            _menu._UnhandledInput(new InputEventKey { Keycode = k, Pressed = true });
        _menu.QueueRedraw();

        if (step.Shot is string name) CallDeferred(nameof(Snap), name);
        _shot++;
    }

    private void Snap(string name)
    {
        string dir = ProjectSettings.GlobalizePath("user://screenshots");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        Image img = GetViewport().GetTexture().GetImage();
        string path = $"{dir}/{name}.png";
        Error err = img.SavePng(path);
        GD.Print($"[menushot] {name} -> {path} ({err})");
    }
}
