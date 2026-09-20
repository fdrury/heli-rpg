using System;
using System.Threading.Tasks;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The conversation UI.
///
/// Drawn to match the HUD aesthetic: dark panel, pale green text, the same colour
/// palette as the instruments. The dialogue system underneath is memory-first (D-015)
/// and the coda is gated on delivery time (D-006a), but from the player's side it is
/// simple: somebody says something, you can ask more or leave.
///
/// Text reveals word by word at roughly half the estimated speaking rate. This is fast
/// enough that reading never feels held back, but slow enough that the coda latency
/// (measured at 0.52 s on a 1080) is invisible behind even a short line.
/// </summary>
public sealed partial class DialoguePanel : Control
{
    [Export] public NodePath InteractionPath { get; set; } = "";

    private SiteInteraction? _play;
    private Font _font = null!;

    // ---- conversation state ----
    private NpcMind? _npc;
    private DialogueBank? _bank;
    private TalkContext _context;
    private CodaServer? _coda;
    private DialogueLine? _currentLine;

    private string _bakedText = "";
    private string _codaText = "";
    private double _elapsed;
    private double _deliveryTime;
    private double _codaDeliveryTime;
    private Task<string?>? _codaTask;
    private double _farewellLinger;

    private enum Phase { Closed, Speaking, Options, Farewell }
    private Phase _phase = Phase.Closed;

    // ---- colours (match FlightHud) ----
    private static readonly Color Dim = new(0.62f, 0.72f, 0.66f, 0.85f);
    private static readonly Color Bright = new(0.80f, 0.94f, 0.84f, 0.95f);
    private static readonly Color Warm = new(0.88f, 0.82f, 0.68f, 0.92f);
    private static readonly Color KeyHint = new(0.98f, 0.74f, 0.25f);
    private static readonly Color Panel = new(0.04f, 0.06f, 0.05f, 0.50f);

    public bool IsOpen => _phase != Phase.Closed;

    public override void _Ready()
    {
        _play = GetNodeOrNull<SiteInteraction>(InteractionPath);
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Begin a conversation. Called by SiteInteraction when the player triggers Talk.</summary>
    public void Open(NpcMind npc, DialogueBank bank, TalkContext context, CodaServer? coda)
    {
        _npc = npc;
        _bank = bank;
        _context = context;
        _coda = coda;

        var line = bank.Select(context, "greeting", context.Now);
        if (line is null) { Close(); return; }

        BeginLine(line);
        _phase = Phase.Speaking;
        Visible = true;
        GD.Print($"[dialogue] opened with {npc.Name}: \"{line.Text}\"");
    }

    public void Close()
    {
        if (_phase != Phase.Closed)
            GD.Print("[dialogue] closed");
        _phase = Phase.Closed;
        Visible = false;
        _codaTask = null;
    }

    /// <summary>Handle a player choice. Called by Main when a number key is pressed.</summary>
    public void HandleOption(int index)
    {
        if (_phase != Phase.Options || _bank is null || _npc is null) return;

        switch (index)
        {
            case 0: // Talk more
            {
                var line = _bank.Select(_context, "talk", _context.Now);
                if (line is null) return; // nothing left to say
                _npc.Standing = Math.Min(1.0, _npc.Standing + 0.02);
                BeginLine(line);
                _phase = Phase.Speaking;
                break;
            }
            case 1: // Leave
            {
                var farewell = _bank.Select(_context, "parting", _context.Now);
                if (farewell is null) { Close(); return; }
                BeginLine(farewell);
                _farewellLinger = 0;
                _phase = Phase.Farewell;
                break;
            }
        }
    }

    /// <summary>Skip the word reveal and show the full text immediately.</summary>
    public void Skip()
    {
        if (_phase is Phase.Speaking or Phase.Farewell)
            _elapsed = 999;
    }

    // ---------------------------------------------------------------- internals

    private void BeginLine(DialogueLine line)
    {
        _currentLine = line;
        _bakedText = line.Text;
        _codaText = "";
        _elapsed = 0;
        _deliveryTime = line.DeliverySeconds;
        _codaDeliveryTime = 0;
        _codaTask = null;

        // D-089: execute the reward when the line is delivered.
        if (line.Reward is not null && _play is not null)
            _play.DeliverReward(line.Reward);

        // Check the coda gate.
        if (_coda is not null && _npc is not null)
        {
            var decision = _coda.Policy.ShouldRequest(line, _context);
            if (decision == CodaDecision.Requested)
            {
                var req = new CodaRequest
                {
                    NpcName = _npc.Name,
                    Persona = _npc.Persona,
                    Wants = _npc.Wants,
                    Standing = _npc.Standing,
                    SpokenLine = line.Text,
                    BudgetMs = _deliveryTime * 1000,
                };
                req.Observations.AddRange(CodaPolicy.BuildObservations(_context));
                req.Memories.AddRange(DialogueCorpus.MemoryPhrases(_npc));
                req.Forbidden.AddRange(_npc.Forbidden);
                _codaTask = _coda.RequestCodaAsync(req);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_phase == Phase.Closed) return;

        // If the player lifts off, close.
        if (_play?.Parked is null) { Close(); return; }

        _elapsed += delta;

        if (_phase is Phase.Speaking or Phase.Farewell)
        {
            double revealDur = Math.Max(0.5, _deliveryTime * 0.5);

            if (_elapsed >= revealDur)
            {
                // Baked text fully revealed. Check for a coda result.
                if (_codaTask is { IsCompleted: true })
                {
                    try
                    {
                        string? raw = _codaTask.Result;
                        _codaTask = null;
                        if (raw is not null && _npc is not null && _currentLine is not null)
                        {
                            var policy = _coda?.Policy ?? new CodaPolicy();
                            var verdict = policy.Validate(raw, _context, _npc, _currentLine.Text);
                            if (verdict.Accepted)
                            {
                                _codaText = " " + verdict.Text;
                                _codaDeliveryTime = DialogueLine.EstimateDelivery(_codaText);
                            }
                            GD.Print($"[dialogue] coda: {verdict.Reason}");
                        }
                    }
                    catch { _codaTask = null; }
                }

                bool codaPending = _codaTask is { IsCompleted: false };
                double codaFrac = 1.0;
                if (_codaText.Length > 0)
                {
                    double codaRevealDur = Math.Max(0.3, _codaDeliveryTime * 0.5);
                    codaFrac = Math.Min(1.0, (_elapsed - revealDur) / codaRevealDur);
                }

                bool allDone = (_codaText.Length == 0 || codaFrac >= 1.0) && !codaPending;

                // Hard abandon: if the model is slow, let the baked line end naturally.
                if (codaPending && _elapsed > revealDur + 4.0)
                {
                    _codaTask = null;
                    allDone = true;
                }

                if (allDone)
                {
                    if (_phase == Phase.Farewell)
                    {
                        _farewellLinger += delta;
                        if (_farewellLinger > 1.5) Close();
                    }
                    else
                    {
                        _phase = Phase.Options;
                    }
                }
            }
        }

        QueueRedraw();
    }

    // ---------------------------------------------------------------- drawing

    public override void _Draw()
    {
        if (_phase == Phase.Closed || _npc is null) return;

        // The viewport, not Size.
        //
        // These panels are Controls parented to a CanvasLayer, and they ask for a full-rect
        // anchor preset in _Ready. The anchors are set correctly - 0,0,1,1 - and the rect
        // never resolves anyway: Size stays (0, 0) against a 1600x900 viewport, because
        // nothing triggers the layout pass that would turn those anchors into a size.
        //
        // Everything then drew relative to a zero-sized control. Anything positioned from
        // the right edge or the centre - the torque and Nr panel at Size.X - 232, the
        // compass, the warnings, the RWR, the footer - landed at a negative coordinate and
        // was simply not on the screen, and what was left piled into the top-left corner.
        // The game shipped its entire interface off the edge of the display.
        //
        // It survived because the screenshot pass hides the HUD by design, so the only
        // frame that ever showed any of this was the kneeboard capture, and nobody had
        // opened it. GameMenu and BindingPanel read GetViewportRect() and always looked
        // right, which is the comparison that found it.
        Vector2 size = GetViewportRect().Size;
        float pw = 550, ph = 220;
        float px = size.X * 0.5f - pw * 0.5f;
        float py = size.Y - 360;

        // Panel
        var rect = new Rect2(px, py, pw, ph);
        DrawRect(rect, Panel);
        DrawRect(rect, Bright * new Color(1, 1, 1, 0.45f), false, 1.2f);

        // NPC name
        float y = py + 24;
        DrawString(_font, new Vector2(px + 20, y), _npc.Name.ToUpperInvariant(),
                   HorizontalAlignment.Left, -1, 16, Bright);

        // Region (right-aligned)
        string region = _context.RegionName ?? "";
        var rgnSize = _font.GetStringSize(region, HorizontalAlignment.Left, -1, 12);
        DrawString(_font, new Vector2(px + pw - 20 - rgnSize.X, y), region,
                   HorizontalAlignment.Left, -1, 12, Dim);

        // Separator
        y += 14;
        DrawLine(new Vector2(px + 16, y), new Vector2(px + pw - 16, y),
                 Dim * new Color(1, 1, 1, 0.45f), 1.0f);

        // Text area
        y += 16;
        double revealDur = Math.Max(0.5, _deliveryTime * 0.5);
        double bakedFrac = Math.Min(1.0, _elapsed / revealDur);

        string revealedBaked = RevealWords(_bakedText, bakedFrac);
        y = DrawWrapped(new Vector2(px + 24, y), revealedBaked, pw - 48, 14, Bright);

        // Coda, revealed after the baked line
        if (_codaText.Length > 0 && _elapsed > revealDur)
        {
            double codaRevealDur = Math.Max(0.3, _codaDeliveryTime * 0.5);
            double codaFrac = Math.Min(1.0, (_elapsed - revealDur) / codaRevealDur);
            string revealedCoda = RevealWords(_codaText.TrimStart(), codaFrac);
            y = DrawWrapped(new Vector2(px + 24, y), revealedCoda, pw - 48, 14, Warm);
        }

        // Options
        if (_phase == Phase.Options)
        {
            float optY = py + ph - 28;
            DrawLine(new Vector2(px + 16, optY - 8), new Vector2(px + pw - 16, optY - 8),
                     Dim * new Color(1, 1, 1, 0.35f), 1.0f);
            DrawString(_font, new Vector2(px + 24, optY + 4), "1",
                       HorizontalAlignment.Left, -1, 14, KeyHint);
            DrawString(_font, new Vector2(px + 42, optY + 4), "Talk more",
                       HorizontalAlignment.Left, -1, 14, Bright);
            DrawString(_font, new Vector2(px + 200, optY + 4), "2",
                       HorizontalAlignment.Left, -1, 14, KeyHint);
            DrawString(_font, new Vector2(px + 218, optY + 4), "Leave",
                       HorizontalAlignment.Left, -1, 14, Bright);
            DrawString(_font, new Vector2(px + pw - 106, optY + 4), "Esc  Close",
                       HorizontalAlignment.Left, -1, 12, Dim);
        }

        // Typing indicator while text is revealing
        if (_phase is Phase.Speaking or Phase.Farewell && bakedFrac < 1.0)
        {
            bool dot = ((int)(_elapsed * 3)) % 2 == 0;
            if (dot) DrawString(_font, new Vector2(px + pw - 40, py + ph - 20), "...",
                                HorizontalAlignment.Left, -1, 14, Dim);
        }
    }

    // ---------------------------------------------------------------- text helpers

    private static string RevealWords(string text, double frac)
    {
        if (frac >= 1.0 || text.Length == 0) return text;
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int n = Math.Clamp((int)(words.Length * frac), 0, words.Length);
        return n == 0 ? "" : string.Join(' ', words[..n]);
    }

    /// <summary>Draw text wrapped within maxWidth, returning the Y below the last line.</summary>
    private float DrawWrapped(Vector2 origin, string text, float maxWidth, int fontSize, Color color)
    {
        if (text.Length == 0) return origin.Y;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float x = origin.X;
        float y = origin.Y;
        float space = _font.GetStringSize(" ", HorizontalAlignment.Left, -1, fontSize).X;
        float lineH = fontSize + 5;

        foreach (string word in words)
        {
            float w = _font.GetStringSize(word, HorizontalAlignment.Left, -1, fontSize).X;
            if (x + w > origin.X + maxWidth && x > origin.X)
            {
                x = origin.X;
                y += lineH;
            }
            DrawString(_font, new Vector2(x, y), word,
                       HorizontalAlignment.Left, -1, fontSize, color);
            x += w + space;
        }
        return y + lineH;
    }
}
