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

    private static readonly (double At, string Name, Key[] Keys)[] Plan =
    {
        (2.5, "menu-title", Array.Empty<Key>()),
        (3.4, "menu-settings", new[] { Key.Down, Key.Down, Key.Enter }),
        (4.2, "menu-pause", new[] { Key.Escape }),
    };

    public override void _Process(double delta)
    {
        _t += delta;
        if (_shot >= Plan.Length) { GetTree().Quit(0); return; }

        (double at, string name, Key[] keys) = Plan[_shot];
        if (_t < at) return;

        foreach (Key k in keys)
        {
            var ev = new InputEventKey { Keycode = k, Pressed = true };
            _menu._UnhandledInput(ev);
        }
        _menu.QueueRedraw();

        // One frame for the redraw to land before the viewport is read.
        CallDeferred(nameof(Snap), name);
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
