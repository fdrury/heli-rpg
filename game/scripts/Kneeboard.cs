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
///
/// MAP page (D-037): pillar 4 says "the map starts near-empty". The map reveals itself
/// through flight — your track paints the fog away. Sites appear when visited or learned
/// about. Threat envelopes show for detected emitters.
/// </summary>
public sealed partial class Kneeboard : Control
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public NodePath InteractionPath { get; set; } = "";

    private HelicopterController _heli = null!;
    private SiteInteraction _play = null!;
    private Font _font = null!;
    private int _page;

    private static readonly string[] Pages = { "AIRCRAFT", "KNOWN", "LOG", "MAP", "JOBS", "THREAD" };

    private static readonly Color Ink = new(0.84f, 0.86f, 0.78f);
    private static readonly Color Faint = new(0.55f, 0.58f, 0.52f);
    private static readonly Color Good = new(0.63f, 0.84f, 0.60f);
    private static readonly Color Warn = new(0.95f, 0.74f, 0.32f);
    private static readonly Color Bad = new(0.93f, 0.42f, 0.36f);
    private static readonly Color Paper = new(0.07f, 0.08f, 0.07f, 0.93f);

    // MAP page state
    private FogOfWar? _fog;
    private ThreatWorld? _threats;
    private ImageTexture? _terrainTex;
    private Image? _fogImage;
    private ImageTexture? _fogTex;
    private int _lastRevealedCount = -1;

    private const int MapRes = 256;

    /// <summary>Set by Main after construction.</summary>
    public void SetFog(FogOfWar fog) => _fog = fog;
    public void SetThreats(ThreatWorld threats) => _threats = threats;

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
            case 2: DrawLog(body, sheet); break;
            case 3: DrawMap(body, sheet); break;
            case 4: DrawJobs(body, sheet); break;
            case 5: DrawThread(body, sheet); break;
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
        // Driven by real Loadout state (D-011), not hardcoded booleans.
        var loadout = _play.Loadout;
        foreach (var mod in Loadout.All)
        {
            bool fitted = loadout.IsInstalled(mod.Id);
            bool inBag = loadout.InBag(mod.Id);
            string status = fitted ? "fitted" : inBag ? "in bag" : "empty bay";
            Color col = fitted ? Good : inBag ? Warn : Faint;
            Text(new Vector2(col2 + 6, y2), mod.Name, col, 13);
            Text(new Vector2(col2 + 170, y2), status, col, 12);
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

    // ------------------------------------------------------------------ map

    /// <summary>
    /// Pillar 4: "the map starts near-empty."
    ///
    /// Terrain is rendered once as a topo image. Fog is a second image updated when
    /// new cells are revealed. Sites and threats are drawn as markers on top. The
    /// aircraft is a chevron.
    /// </summary>
    private void DrawMap(Vector2 o, Rect2 sheet)
    {
        if (_fog is null) return;

        Text(o, "CHART", Ink, 17);

        // Alert readout: which regions are expecting you
        string alertLine = "";
        if (_threats?.Alert is AlertState alert)
        {
            foreach (var kv in alert.Raised(0.05))
            {
                int rid = kv.Key;
                if (rid >= 0 && rid < WorldMap.Regions.Count)
                {
                    string name = WorldMap.Regions[rid].Name;
                    string desc = AlertState.Describe(kv.Value);
                    alertLine += (alertLine.Length > 0 ? " · " : "") + $"{name}: {desc}";
                }
            }
        }

        string subtitle = $"{_fog.RevealedFraction:P0} surveyed";
        if (alertLine.Length > 0) subtitle += "    " + alertLine;
        Text(o + new Vector2(0, 22), subtitle, Faint, 12);

        // Fit a square map into the available body area
        float bodyW = sheet.Size.X - 60;
        float bodyH = sheet.End.Y - o.Y - 80;
        float mapSide = Math.Min(bodyW, bodyH);
        float mapX = o.X + (bodyW - mapSide) * 0.5f;
        float mapY = o.Y + 50;

        var mapRect = new Rect2(mapX, mapY, mapSide, mapSide);

        // Background
        DrawRect(mapRect, new Color(0.03f, 0.04f, 0.03f));

        // Terrain texture (generated once)
        EnsureTerrainTexture();
        if (_terrainTex is not null)
            DrawTextureRect(_terrainTex, mapRect, false);

        // Fog overlay (updated when cells change)
        UpdateFogTexture();
        if (_fogTex is not null)
            DrawTextureRect(_fogTex, mapRect, false);

        // Border
        DrawRect(mapRect, Faint * new Color(1, 1, 1, 0.5f), false, 1.2f);

        // Threat envelopes (detected emitters only)
        DrawThreatCircles(mapRect);

        // Site markers (visited or known)
        DrawSiteMarkers(mapRect);

        // Aircraft marker
        DrawAircraftMarker(mapRect);

        // D-082: bearing lines from aircraft to active contract targets
        DrawBearingLines(mapRect);

        // Scale bar
        DrawScaleBar(mapRect);
    }

    private void EnsureTerrainTexture()
    {
        if (_terrainTex is not null) return;

        var img = Image.CreateEmpty(MapRes, MapRes, false, Image.Format.Rgb8);
        float extent = FogOfWar.WorldExtent;

        for (int py = 0; py < MapRes; py++)
        {
            for (int px = 0; px < MapRes; px++)
            {
                // Image coords → world coords (Godot: X east, Z south)
                float worldX = (px / (float)(MapRes - 1)) * extent * 2f - extent;
                float worldZ = (py / (float)(MapRes - 1)) * extent * 2f - extent;

                float h = WorldHeight.RawAt(worldX, worldZ);
                Color c = HeightToColor(h);
                img.SetPixel(px, py, c);
            }
        }

        _terrainTex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Military-chart-style height colouring. Low ground is dark olive, high ground
    /// is pale tan, ridges go to grey. Water would be dark blue-green but the world
    /// has none below the floor.
    /// </summary>
    private static Color HeightToColor(float h)
    {
        // Terrain ranges roughly -10 to 300
        float t = Mathf.Clamp((h + 10f) / 310f, 0f, 1f);

        // Four-stop ramp: dark olive → olive → tan → pale grey
        if (t < 0.25f)
        {
            float f = t / 0.25f;
            return Lerp(new Color(0.12f, 0.14f, 0.08f), new Color(0.20f, 0.24f, 0.14f), f);
        }
        if (t < 0.5f)
        {
            float f = (t - 0.25f) / 0.25f;
            return Lerp(new Color(0.20f, 0.24f, 0.14f), new Color(0.32f, 0.30f, 0.20f), f);
        }
        if (t < 0.75f)
        {
            float f = (t - 0.5f) / 0.25f;
            return Lerp(new Color(0.32f, 0.30f, 0.20f), new Color(0.40f, 0.38f, 0.30f), f);
        }
        {
            float f = (t - 0.75f) / 0.25f;
            return Lerp(new Color(0.40f, 0.38f, 0.30f), new Color(0.52f, 0.50f, 0.44f), f);
        }
    }

    private static Color Lerp(Color a, Color b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private void UpdateFogTexture()
    {
        if (_fog is null) return;
        if (_fog.RevealedCount == _lastRevealedCount && _fogTex is not null) return;

        _lastRevealedCount = _fog.RevealedCount;

        int fogRes = FogOfWar.GridSize;   // 128
        _fogImage ??= Image.CreateEmpty(fogRes, fogRes, false, Image.Format.Rgba8);

        var fogColor = new Color(0.03f, 0.04f, 0.03f, 0.92f);
        var clear = new Color(0, 0, 0, 0);

        for (int y = 0; y < fogRes; y++)
            for (int x = 0; x < fogRes; x++)
                _fogImage.SetPixel(x, y, _fog.IsRevealed(x, y) ? clear : fogColor);

        if (_fogTex is null)
            _fogTex = ImageTexture.CreateFromImage(_fogImage);
        else
            _fogTex.Update(_fogImage);
    }

    /// <summary>World east/south → map pixel position.</summary>
    private Vector2 WorldToMap(Rect2 mapRect, float worldX, float worldZ)
    {
        float extent = FogOfWar.WorldExtent;
        float nx = (worldX + extent) / (extent * 2f);
        float ny = (worldZ + extent) / (extent * 2f);
        return new Vector2(mapRect.Position.X + nx * mapRect.Size.X,
                           mapRect.Position.Y + ny * mapRect.Size.Y);
    }

    private void DrawThreatCircles(Rect2 mapRect)
    {
        if (_threats is null) return;

        var threatColor = new Color(0.85f, 0.25f, 0.20f, 0.18f);
        var threatEdge = new Color(0.85f, 0.25f, 0.20f, 0.45f);

        foreach (var track in _threats.Field.Tracks)
        {
            if (!track.EverDetected) continue;

            var em = track.Emitter;
            // Emitter position: sim north/east → Godot X=east, Z=-north (south)
            float gx = (float)em.East;
            float gz = (float)-em.North;
            Vector2 centre = WorldToMap(mapRect, gx, gz);

            // Engagement range as a circle
            float pixelsPerMetre = mapRect.Size.X / (FogOfWar.WorldExtent * 2f);
            float radius = (float)em.EngagementRange * pixelsPerMetre;

            DrawCircle(centre, radius, threatColor);
            DrawArc(centre, radius, 0, Mathf.Tau, 32, threatEdge, 1.2f);
        }
    }

    private void DrawSiteMarkers(Rect2 mapRect)
    {
        Progress p = _play.Progress;

        foreach (Site site in WorldMap.Sites)
        {
            bool visited = p.HasVisited(site.Id);
            bool known = p.Knows($"contact.{site.Id}") || p.Knows($"site.{site.Id}");
            if (!visited && !known) continue;

            Vector2 pos = WorldToMap(mapRect, site.Position.X, site.Position.Y);

            // Marker shape and colour by site kind
            Color col = SiteColor(site.Kind);
            float sz = SiteMarkerSize(site.Kind);

            // Diamond marker
            var pts = new Vector2[]
            {
                pos + new Vector2(0, -sz),
                pos + new Vector2(sz, 0),
                pos + new Vector2(0, sz),
                pos + new Vector2(-sz, 0),
            };
            DrawColoredPolygon(pts, col);

            // Hostile indicator: red ring for uncleared hostile sites, dimmed for cleared.
            bool hostile = Encounter.IsHostile(site.Id, (int)site.Kind, site.Tier);
            if (hostile && visited)
            {
                var rec = p.Record(site.Id);
                Color ring = rec.Cleared
                    ? new Color(0.50f, 0.55f, 0.50f, 0.6f)
                    : new Color(0.95f, 0.30f, 0.25f, 0.9f);
                float rr = sz + 3;
                var hostPts = new Vector2[]
                {
                    pos + new Vector2(0, -rr),
                    pos + new Vector2(rr, 0),
                    pos + new Vector2(0, rr),
                    pos + new Vector2(-rr, 0),
                    pos + new Vector2(0, -rr), // close the shape
                };
                DrawPolyline(hostPts, ring, 1.4f);
            }

            // Label (only if map area is large enough for readability)
            if (mapRect.Size.X > 350)
            {
                string label = Truncate(site.Name, 12);
                Text(pos + new Vector2(sz + 3, 4), label, col * new Color(1, 1, 1, 0.8f), 10);
            }
        }
    }

    private static Color SiteColor(SiteKind kind) => kind switch
    {
        SiteKind.FuelCache => new Color(0.90f, 0.75f, 0.30f),
        SiteKind.Settlement => new Color(0.63f, 0.84f, 0.60f),
        SiteKind.Workshop => new Color(0.55f, 0.75f, 0.90f),
        SiteKind.Wreck => new Color(0.65f, 0.55f, 0.45f),
        SiteKind.Relay => new Color(0.80f, 0.80f, 0.80f),
        SiteKind.Depot => new Color(0.75f, 0.60f, 0.40f),
        SiteKind.Airfield => new Color(0.55f, 0.75f, 0.90f),
        SiteKind.Farmstead => new Color(0.50f, 0.65f, 0.45f),
        SiteKind.Overlook => new Color(0.70f, 0.70f, 0.65f),
        _ => Ink,
    };

    private static float SiteMarkerSize(SiteKind kind) => kind switch
    {
        SiteKind.Airfield or SiteKind.Settlement => 5f,
        SiteKind.Workshop or SiteKind.Depot => 4f,
        _ => 3f,
    };

    private void DrawAircraftMarker(Rect2 mapRect)
    {
        Vector3 pos = _heli.GlobalPosition;
        Vector2 mapPos = WorldToMap(mapRect, pos.X, pos.Z);

        // Heading: sim yaw is radians from north, clockwise. On the map, north is up (-Y).
        float yaw = (float)_heli.Sim.State.Orientation.Yaw;

        // Draw a chevron pointing in the heading direction
        float sz = 7f;
        float cos = Mathf.Cos(yaw);
        float sin = Mathf.Sin(yaw);

        // Chevron: nose, left wing, tail notch, right wing
        Vector2 nose = mapPos + new Vector2(sin, -cos) * sz;
        Vector2 left = mapPos + new Vector2(-cos - sin * 0.5f, -sin + cos * 0.5f) * (sz * 0.7f);
        Vector2 tail = mapPos + new Vector2(-sin, cos) * (sz * 0.3f);
        Vector2 right = mapPos + new Vector2(cos - sin * 0.5f, sin + cos * 0.5f) * (sz * 0.7f);

        var heliColor = new Color(0.95f, 0.95f, 0.85f);
        DrawColoredPolygon(new[] { nose, left, tail, right }, heliColor);
    }

    /// <summary>
    /// Draw a dashed line from the aircraft to each active contract target, so the
    /// player can see the bearing on the map and plan a route around threat circles.
    /// </summary>
    private void DrawBearingLines(Rect2 mapRect)
    {
        Vector3 acPos = _heli.GlobalPosition;
        Vector2 acMap = WorldToMap(mapRect, acPos.X, acPos.Z);

        var lineColor = new Color(0.95f, 0.82f, 0.35f, 0.45f);

        foreach (var c in _play.Progress.ActiveContracts)
        {
            var site = WorldMap.SiteById(c.TargetSiteId);
            if (site is null) continue;

            Vector2 tgtMap = WorldToMap(mapRect, site.Position.X, site.Position.Y);

            // Dashed line: segments of 8px with 5px gaps
            Vector2 dir = tgtMap - acMap;
            float len = dir.Length();
            if (len < 2f) continue;
            dir /= len;

            float drawn = 0;
            while (drawn < len)
            {
                float segEnd = Math.Min(drawn + 8f, len);
                Vector2 a = acMap + dir * drawn;
                Vector2 b = acMap + dir * segEnd;

                // Clip to map bounds
                if (mapRect.HasPoint(a) || mapRect.HasPoint(b))
                    DrawLine(a, b, lineColor, 1.2f);

                drawn = segEnd + 5f;
            }
        }
    }

    private void DrawScaleBar(Rect2 mapRect)
    {
        float pixelsPerMetre = mapRect.Size.X / (FogOfWar.WorldExtent * 2f);

        // 2 km scale bar
        float barLen = 2000f * pixelsPerMetre;
        float bx = mapRect.Position.X + 8;
        float by = mapRect.End.Y - 14;

        DrawLine(new Vector2(bx, by), new Vector2(bx + barLen, by), Faint, 1.5f);
        DrawLine(new Vector2(bx, by - 3), new Vector2(bx, by + 3), Faint, 1.2f);
        DrawLine(new Vector2(bx + barLen, by - 3), new Vector2(bx + barLen, by + 3), Faint, 1.2f);
        Text(new Vector2(bx + barLen + 5, by + 4), "2 km", Faint, 10);
    }

    // ----------------------------------------------------------------- jobs

    /// <summary>
    /// Active contracts and the main search thread.
    ///
    /// This is the answer to "where should I fly next?" — the critical gap that benchmark
    /// pass 2 identified. The search thread gives the long-term pull; the contracts give
    /// the per-sortie purpose.
    /// </summary>
    private void DrawJobs(Vector2 o, Rect2 sheet)
    {
        Progress p = _play.Progress;
        float maxW = sheet.Size.X - 72;

        // ---------- Search thread (the main quest)
        Text(o, "THE SEARCH", Ink, 17);
        Text(o + new Vector2(0, 22), $"Stage {p.Search.Stage} of {SearchThread.Beats.Length}", Faint, 12);

        float y = o.Y + 48;
        string hint = p.Search.CurrentHint;
        float hintWidth = maxW - 14;
        y = WrapText(o.X + 6, y, hint, Warn, 13, hintWidth);

        if (p.Search.Stage > 0 && p.Search.LastClue.Length > 0)
        {
            y += 6;
            Text(new Vector2(o.X + 6, y), "Last clue:", Faint, 11);
            y += 16;
            string lastClue = p.Search.LastClue;
            if (lastClue.Length > 120) lastClue = lastClue[..117] + "...";
            y = WrapText(o.X + 14, y, lastClue, Faint, 12, hintWidth - 8);
        }

        // ---------- Active contracts
        y += 18;
        DrawLine(new Vector2(o.X, y), new Vector2(o.X + maxW, y),
                 Faint * new Color(1, 1, 1, 0.4f), 1f);
        y += 12;

        var active = new List<Contract>();
        foreach (var c in p.ActiveContracts) active.Add(c);

        Text(new Vector2(o.X, y), $"ACTIVE JOBS ({active.Count})", Faint, 12);
        y += 20;

        if (active.Count == 0)
        {
            Text(new Vector2(o.X + 6, y), "No active contracts. Visit a settlement to find work.", Faint, 12);
            y += 20;
        }
        else
        {
            foreach (var c in active)
            {
                if (y > sheet.End.Y - 80) break;

                // Contract kind icon
                string kindTag = c.Kind switch
                {
                    ContractKind.Deliver => "DELIVER",
                    ContractKind.Survey => "SCOUT",
                    ContractKind.Recover => "SEARCH",
                    ContractKind.Relay => "MESSAGE",
                    ContractKind.Clear => "CLEAR",
                    _ => "JOB",
                };
                Color kindCol = c.Kind switch
                {
                    ContractKind.Deliver => new Color(0.90f, 0.75f, 0.30f),
                    ContractKind.Survey => new Color(0.55f, 0.75f, 0.90f),
                    ContractKind.Recover => new Color(0.75f, 0.60f, 0.40f),
                    ContractKind.Relay => new Color(0.63f, 0.84f, 0.60f),
                    ContractKind.Clear => new Color(0.90f, 0.40f, 0.35f),
                    _ => Ink,
                };

                Text(new Vector2(o.X + 6, y), kindTag, kindCol, 11);
                Text(new Vector2(o.X + 80, y), c.Title, Ink, 13);
                y += 18;
                Text(new Vector2(o.X + 80, y), $"→ {c.TargetName}", Faint, 12);
                Text(new Vector2(o.X + maxW - 80, y), $"+{c.RewardAmount:F0} {c.RewardKind}", Faint, 11);
                y += 22;
            }
        }

        // ---------- Completed count
        y += 6;
        DrawLine(new Vector2(o.X, y), new Vector2(o.X + maxW, y),
                 Faint * new Color(1, 1, 1, 0.4f), 1f);
        y += 12;
        Text(new Vector2(o.X, y), $"COMPLETED: {p.ContractsCompleted}", Faint, 12);
    }

    // --------------------------------------------------------------- thread

    /// <summary>
    /// The search thread's structure: the four ferry-route legs, the airband hunt,
    /// and the rotor ceiling. Story.md §4.4. Facts, not inferences — no next-objective
    /// marker, no percentage. The legs close themselves as evidence arrives.
    /// </summary>
    private void DrawThread(Vector2 o, Rect2 sheet)
    {
        Progress p = _play.Progress;
        var sim = _heli.Sim;
        float maxW = sheet.Size.X - 72;

        // Header: the person and the aircraft
        Text(o, "THREAD", Ink, 17);
        float y = o.Y + 30;

        Text(new Vector2(o.X + 6, y), "WRAY, SERA", Ink, 14);
        Text(new Vector2(o.X + 130, y), "\u2014 flight engineer", Faint, 12);
        y += 20;

        Text(new Vector2(o.X + 6, y),
             "SIERRA-FOUR-THREE, four legs, last plan filed 11 March", Faint, 12);
        y += 30;

        // The four legs
        DrawLine(new Vector2(o.X, y), new Vector2(o.X + maxW, y),
                 Faint * new Color(1, 1, 1, 0.4f), 1f);
        y += 14;

        for (int i = 0; i < SearchThread.Legs.Length; i++)
        {
            var (from, to, _) = SearchThread.Legs[i];
            bool closed = SearchThread.IsLegClosed(i, p);
            double closedAt = p.Search.LegClosedAt[i];

            string legLabel = $"LEG {i + 1}";
            string route = $"{from}  \u2192  {to}";
            string status;
            Color statusCol;

            if (closed)
            {
                int day = closedAt > 0 ? (int)(closedAt / 86400) + 1 : 0;
                status = day > 0 ? $"closed  D{day}" : "closed";
                statusCol = Good;
            }
            else
            {
                status = "open";
                statusCol = Faint;
            }

            Text(new Vector2(o.X + 6, y), legLabel, closed ? Good : Ink, 13);
            Text(new Vector2(o.X + 64, y), route, closed ? Good : Ink, 13);

            float statusX = o.X + maxW - _font.GetStringSize(status, HorizontalAlignment.Left, -1, 12).X;
            Text(new Vector2(statusX, y), status, statusCol, 12);
            y += 22;
        }

        // Airband candidates
        y += 12;
        DrawLine(new Vector2(o.X, y), new Vector2(o.X + maxW, y),
                 Faint * new Color(1, 1, 1, 0.4f), 1f);
        y += 14;

        int freqCount = p.CountKnown(KnowledgeKind.Frequency);
        Color freqCol = freqCount >= 34 ? Good : freqCount > 0 ? Ink : Faint;
        Text(new Vector2(o.X + 6, y), "AIRBAND CANDIDATES", Faint, 12);
        Text(new Vector2(o.X + 200, y), $"{freqCount} of 34 logged", freqCol, 13);

        // Main rotor condition
        y += 28;
        double rotorHealth = sim.Damage.Health(Component.MainRotor);
        double floor = DamageState.MainRotorFloor;
        double ceiling = (rotorHealth - floor) / (1.0 - floor);
        if (ceiling < 0) ceiling = 0;
        double hours = sim.Damage.TotalFlightHours;

        Color rotorCol = ceiling > 0.6 ? Good : ceiling > 0.3 ? Warn : Bad;
        Text(new Vector2(o.X + 6, y), "MAIN ROTOR", Faint, 12);
        Text(new Vector2(o.X + 200, y),
             $"ceiling {ceiling:P0}   \u00b7   {hours:F0} h since track", rotorCol, 13);

        Bar(new Vector2(o.X + 6, y + 22), maxW - 12, (float)ceiling, rotorCol);

        // Current hint at the bottom
        y += 56;
        DrawLine(new Vector2(o.X, y), new Vector2(o.X + maxW, y),
                 Faint * new Color(1, 1, 1, 0.4f), 1f);
        y += 14;

        Text(new Vector2(o.X + 6, y), "CURRENT", Faint, 11);
        y += 16;
        string hint = p.Search.CurrentHint;
        WrapText(o.X + 6, y, hint, Warn, 13, maxW - 12);
    }

    /// <summary>Word-wrap text into a given width, returns the Y after the last line.</summary>
    private float WrapText(float x, float y, string text, Color col, int size, float maxWidth)
    {
        string[] words = text.Split(' ');
        string line = "";
        foreach (string word in words)
        {
            string test = line.Length > 0 ? line + " " + word : word;
            float w = _font.GetStringSize(test, HorizontalAlignment.Left, -1, size).X;
            if (w > maxWidth && line.Length > 0)
            {
                Text(new Vector2(x, y), line, col, size);
                y += size + 4;
                line = word;
            }
            else
            {
                line = test;
            }
        }
        if (line.Length > 0)
        {
            Text(new Vector2(x, y), line, col, size);
            y += size + 4;
        }
        return y;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + ".";

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
