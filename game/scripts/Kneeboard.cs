using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The kneeboard: one screen that shows the player they are further along than they were.
///
/// D-005a is explicit about why this exists. Gear-and-knowledge progression has no
/// experience bar, and the benchmark pass found that every game which makes that model
/// work - Subnautica's PDA, Outer Wilds' rumour map, Death Stranding's chiral network -
/// pairs it with a single place where accumulated progress is visible at a glance. Without
/// one, the player cannot tell that anything is happening.
///
/// The rule it obeys, also from D-005a: **facts, not inferences**. There is no completion
/// percentage, no score, and no "next objective". It records what was found, where and
/// when. The game does the bookkeeping; the player does the thinking.
///
/// It also shows EMPTY BAYS, which is the other half of the trick: you cannot want a
/// thing you do not know exists.
/// </summary>
public sealed partial class Kneeboard : Control
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public NodePath InteractionPath { get; set; } = "";

    private HelicopterController _heli = null!;
    private SiteInteraction _play = null!;
    private Font _font = null!;
    private int _page;

    private static readonly string[] Pages = { "AIRCRAFT", "KNOWN", "LOG" };

    private static readonly Color Ink = new(0.84f, 0.86f, 0.78f);
    private static readonly Color Faint = new(0.55f, 0.58f, 0.52f);
    private static readonly Color Good = new(0.63f, 0.84f, 0.60f);
    private static readonly Color Warn = new(0.95f, 0.74f, 0.32f);
    private static readonly Color Bad = new(0.93f, 0.42f, 0.36f);
    private static readonly Color Paper = new(0.07f, 0.08f, 0.07f, 0.93f);

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);
        _play = GetNode<SiteInteraction>(InteractionPath);
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public void Toggle() { Visible = !Visible; QueueRedraw(); }
    public void NextPage() { _page = (_page + 1) % Pages.Length; QueueRedraw(); }

    public override void _Process(double delta) { if (Visible) QueueRedraw(); }

    public override void _Draw()
    {
        Vector2 size = Size;
        var sheet = new Rect2(size.X * 0.13f, size.Y * 0.08f, size.X * 0.74f, size.Y * 0.84f);
        DrawRect(sheet, Paper);
        DrawRect(sheet, Faint * new Color(1, 1, 1, 0.6f), false, 1.4f);

        var o = sheet.Position + new Vector2(30, 40);
        Text(o, "KNEEBOARD", Ink, 22);
        Text(o + new Vector2(0, 22), Progress.FormatClock(_play.Progress.Clock), Faint, 13);

        // Tabs
        float tx = sheet.End.X - 30;
        for (int i = Pages.Length - 1; i >= 0; i--)
        {
            string label = Pages[i];
            Vector2 sz = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 14);
            tx -= sz.X + 26;
            Text(new Vector2(tx, o.Y), label, i == _page ? Ink : Faint, 14);
            if (i == _page)
                DrawLine(new Vector2(tx, o.Y + 6), new Vector2(tx + sz.X, o.Y + 6), Ink, 1.5f);
        }

        DrawLine(new Vector2(sheet.Position.X + 26, o.Y + 40), new Vector2(sheet.End.X - 26, o.Y + 40),
                 Faint * new Color(1, 1, 1, 0.5f), 1.1f);

        Vector2 body = o + new Vector2(0, 74);
        switch (_page)
        {
            case 0: DrawAircraft(body, sheet); break;
            case 1: DrawKnown(body, sheet); break;
            default: DrawLog(body, sheet); break;
        }

        Text(new Vector2(sheet.Position.X + 30, sheet.End.Y - 18),
             "TAB close     E next page", Faint, 12);
    }

    // ------------------------------------------------------------- aircraft

    private void DrawAircraft(Vector2 o, Rect2 sheet)
    {
        var sim = _heli.Sim;
        float col2 = sheet.Position.X + sheet.Size.X * 0.52f;

        Text(o, "HUGH", Ink, 17);
        Text(o + new Vector2(0, 20), $"{sim.Airframe.Name} class  -  {sim.TotalMass:F0} kg all up", Faint, 12);

        float y = o.Y + 48;
        Text(new Vector2(o.X, y), "CONDITION", Faint, 12); y += 20;

        foreach (Component c in Enum.GetValues<Component>())
        {
            double h = sim.Damage.Health(c);
            Color col = h > 0.9 ? Good : h > 0.6 ? Ink : h > 0.3 ? Warn : Bad;
            Text(new Vector2(o.X + 6, y), c.ToString(), col, 13);
            Bar(new Vector2(o.X + 150, y - 9), 130, (float)h, col);
            Text(new Vector2(o.X + 292, y), h > 0.995 ? "serviceable" : $"{h:P0}", col, 12);
            y += 19;
        }

        y += 14;
        Text(new Vector2(o.X, y), "CONSUMABLES", Faint, 12); y += 20;
        double frac = sim.Fuel / Math.Max(sim.Airframe.FuelCapacity, 1);
        Text(new Vector2(o.X + 6, y), "Fuel", frac < 0.2 ? Warn : Ink, 13);
        Bar(new Vector2(o.X + 150, y - 9), 130, (float)frac, frac < 0.2 ? Warn : Good);
        Text(new Vector2(o.X + 292, y), $"{sim.Fuel:F0} / {sim.Airframe.FuelCapacity:F0} kg", Ink, 12);

        // --- Carried, and the EMPTY BAYS -------------------------------------
        float y2 = o.Y + 48;
        Text(new Vector2(col2, y2), "CARRIED", Faint, 12); y2 += 20;
        foreach (Stock s in Enum.GetValues<Stock>())
        {
            double n = _play.Progress.Amount(s);
            Text(new Vector2(col2 + 6, y2), s.ToString(), n > 0 ? Ink : Faint, 13);
            Text(new Vector2(col2 + 150, y2), n > 0 ? $"{n:F0}" : "-", n > 0 ? Ink : Faint, 13);
            y2 += 19;
        }
        Text(new Vector2(col2 + 6, y2 + 6), $"{_play.Progress.CarriedMass:F0} kg of load", Faint, 12);

        y2 += 40;
        Text(new Vector2(col2, y2), "FITTED", Faint, 12); y2 += 20;

        // Showing what is NOT installed is the point. You cannot want a thing you do not
        // know exists, and this is the only place the player finds out these bays are here.
        var bays = new (string name, bool fitted, string what)[]
        {
            ("Attitude hold", _heli.SasAuthority > 0.5f, "holds the aircraft where you put it"),
            ("Radar warning", false, "tells you when you are painted"),
            ("Chaff", false, "against radar"),
            ("Flares", false, "against heat"),
            ("Exhaust suppressor", false, "makes you cold"),
            ("Long range tank", false, "more fuel, less lift"),
            ("Cargo hook", false, "carry it underneath"),
            ("Rescue hoist", false, "no landing required"),
        };
        foreach (var (name, fitted, what) in bays)
        {
            Text(new Vector2(col2 + 6, y2), fitted ? name : name, fitted ? Good : Faint, 13);
            Text(new Vector2(col2 + 170, y2), fitted ? "fitted" : "empty bay", fitted ? Good : Faint, 12);
            y2 += 18;
        }
    }

    // ---------------------------------------------------------------- known

    private void DrawKnown(Vector2 o, Rect2 sheet)
    {
        Progress p = _play.Progress;

        Text(o, "WHAT YOU KNOW", Ink, 17);
        Text(o + new Vector2(0, 22),
             $"{p.VisitedCount} places put down at  -  {WorldMap.Sites.Count} exist, " +
             $"as far as anyone has told you", Faint, 12);

        float y = o.Y + 56;
        foreach (KnowledgeKind kind in Enum.GetValues<KnowledgeKind>())
        {
            var items = new List<Knowledge>();
            foreach (Knowledge k in p.Known) if (k.Kind == kind) items.Add(k);

            Text(new Vector2(o.X, y), $"{kind.ToString().ToUpperInvariant()}  ({items.Count})", Faint, 12);
            y += 19;

            if (items.Count == 0)
            {
                Text(new Vector2(o.X + 14, y), "nothing yet", Faint * new Color(1, 1, 1, 0.7f), 12);
                y += 20;
            }
            foreach (Knowledge k in items)
            {
                if (y > sheet.End.Y - 60) return;
                Text(new Vector2(o.X + 14, y), k.Label, Ink, 13);
                Text(new Vector2(o.X + 300, y), k.Detail, Faint, 12);
                y += 19;
            }
            y += 10;
        }
    }

    // ------------------------------------------------------------------ log

    private void DrawLog(Vector2 o, Rect2 sheet)
    {
        Text(o, "LOG", Ink, 17);
        Text(o + new Vector2(0, 22), "Facts, in the order they happened.", Faint, 12);

        IReadOnlyList<string> journal = _play.Progress.Journal_;
        int lines = (int)((sheet.End.Y - o.Y - 110) / 19);
        int start = Math.Max(0, journal.Count - lines);

        float y = o.Y + 56;
        for (int i = start; i < journal.Count; i++)
        {
            Text(new Vector2(o.X + 6, y), journal[i], i == journal.Count - 1 ? Ink : Faint, 13);
            y += 19;
        }
    }

    // --------------------------------------------------------------- helpers

    private void Text(Vector2 p, string s, Color c, int size) =>
        DrawString(_font, p, s, HorizontalAlignment.Left, -1, size, c);

    private void Bar(Vector2 p, float width, float frac, Color c)
    {
        DrawRect(new Rect2(p.X, p.Y, width, 9), new Color(0, 0, 0, 0.45f));
        DrawRect(new Rect2(p.X, p.Y, width * Mathf.Clamp(frac, 0, 1), 9), c);
        DrawRect(new Rect2(p.X, p.Y, width, 9), Faint * new Color(1, 1, 1, 0.5f), false, 1f);
    }
}
