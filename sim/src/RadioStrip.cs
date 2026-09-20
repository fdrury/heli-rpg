using System;
using System.Collections.Generic;

namespace Rotorwash.Sim;

/// <summary>Kind of radio message, drives HUD colour.</summary>
public enum RadioMessageKind
{
    /// <summary>Scheduled broadcast (the 06:40 weather sequence).</summary>
    Broadcast,
    /// <summary>Someone raises the player directly.</summary>
    Directed,
    /// <summary>Overheard hostile traffic while inside a threat envelope.</summary>
    Intercepted,
    /// <summary>Co-pilot callout — torque, altitude, fuel. Different HUD colour (story.md §7.6).</summary>
    Copilot,
}

/// <summary>One radio message to display on the HUD strip.</summary>
public sealed record RadioMessage(string Speaker, string Text, RadioMessageKind Kind);

/// <summary>
/// A two-line text queue on the HUD that reveals messages word by word at
/// reading pace (story.md §4.3, §7.4).
///
/// Three kinds of call:
/// 1. Scheduled broadcast — the 06:40 weather sequence (beat 5).
/// 2. Directed call — someone raises the player after they tuned a mast.
/// 3. Intercepted call — overheard traffic inside a threat envelope.
///
/// The player cannot reply in flight, ever. Replying means landing.
///
/// Pure .NET, no Godot dependency. The game layer calls Update each frame
/// and reads CurrentSpeaker / CurrentText / Active to draw.
/// </summary>
public sealed class RadioStrip
{
    /// <summary>Word reveal speed, matching RadioDj for natural caption feel.</summary>
    public const double WordsPerSecond = 2.55;

    /// <summary>How long fully revealed text stays on screen before clearing.</summary>
    public const double HoldSeconds = 4.0;

    private readonly Queue<RadioMessage> _pending = new();
    private RadioMessage? _active;
    private string[]? _words;
    private int _revealedCount;
    private double _wordTimer;
    private double _holdTimer;

    /// <summary>The speaker tag for the current message, or null.</summary>
    public string? CurrentSpeaker => _active?.Speaker;

    /// <summary>The revealed portion of the current message text, or null.</summary>
    public string? CurrentText { get; private set; }

    /// <summary>The kind of the current message, for HUD colour coding.</summary>
    public RadioMessageKind? CurrentKind => _active?.Kind;

    /// <summary>True when there is text being displayed.</summary>
    public bool Active => CurrentText is not null;

    /// <summary>Number of messages waiting in the queue.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// When true, new messages are deferred (during threat engagement).
    /// A message already revealing continues to completion.
    /// </summary>
    public bool Suppressed { get; set; }

    /// <summary>Add a message to the queue.</summary>
    public void Enqueue(RadioMessage msg) => _pending.Enqueue(msg);

    /// <summary>Add a message to the queue.</summary>
    public void Enqueue(string speaker, string text, RadioMessageKind kind)
        => _pending.Enqueue(new RadioMessage(speaker, text, kind));

    /// <summary>Advance word reveal and drain the queue.</summary>
    public void Update(double dt)
    {
        if (_active is not null)
        {
            if (_revealedCount < _words!.Length)
            {
                // Revealing words one at a time.
                _wordTimer += dt;
                double interval = 1.0 / WordsPerSecond;
                while (_wordTimer >= interval && _revealedCount < _words.Length)
                {
                    _revealedCount++;
                    _wordTimer -= interval;
                }
                CurrentText = string.Join(' ', _words, 0, _revealedCount);
            }
            else
            {
                // All words revealed — hold then clear.
                _holdTimer += dt;
                if (_holdTimer >= HoldSeconds)
                {
                    _active = null;
                    _words = null;
                    CurrentText = null;
                }
            }
            return;
        }

        // Try to start the next message.
        if (Suppressed || _pending.Count == 0) return;

        _active = _pending.Dequeue();
        _words = _active.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _revealedCount = 0;
        _wordTimer = 0;
        _holdTimer = 0;

        // Show the first word immediately.
        if (_words.Length > 0)
        {
            _revealedCount = 1;
            CurrentText = _words[0];
        }
    }

    /// <summary>Clear all pending and active messages.</summary>
    public void Clear()
    {
        _pending.Clear();
        _active = null;
        _words = null;
        CurrentText = null;
        _revealedCount = 0;
        _holdTimer = 0;
    }
}
