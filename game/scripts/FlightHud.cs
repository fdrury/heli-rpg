using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Flight instruments.
///
/// Drawn rather than authored as a scene, because during bring-up the instruments change
/// every time the flight model does, and a code HUD can be diffed and reviewed.
///
/// The choice of what to show is deliberate: rotor speed and torque are the two gauges a
/// helicopter pilot actually lives by, and neither means anything in most games. Here they
/// do - Nr decaying is the engine dying, torque pegged is the transmission about to be the
/// thing you have to scavenge a replacement for.
/// </summary>
public sealed partial class FlightHud : Control
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public NodePath LandingControllerPath { get; set; } = "";
    [Export] public NodePath InteractionPath { get; set; } = "";
    [Export] public NodePath ThreatWorldPath { get; set; } = "";

    private HelicopterController? _heli;
    private LandingController? _landing;
    private SiteInteraction? _play;
    private ThreatWorld? _threats;
    private RotorTime? _rotorTime;
    private Sidearm? _sidearm;
    private GunPodController? _gunpod;
    private Camera3D? _camera;
    private Font _font = null!;
    private double _warnBlink;
    private bool _onFoot;

    // Module delta card: shown briefly when a module is installed or removed (D-055).
    private string? _deltaTitle;
    private readonly List<string> _deltaLines = new();
    private double _deltaAge = 99;
    private const double DeltaDuration = 5.0;

    // Navigation compass (D-082): bearing guidance for active contracts.
    private NavTarget[] _navTargets = Array.Empty<NavTarget>();
    private static readonly Color NavMark = new(0.95f, 0.82f, 0.35f, 0.92f);

    // Radio strip (D-085): in-flight text for radio messages.
    private RadioStrip? _radioStrip;
    private static readonly Color RadioBroadcast = new(0.72f, 0.82f, 0.90f, 0.92f);
    private static readonly Color RadioDirected = new(0.65f, 0.92f, 0.72f, 0.92f);
    private static readonly Color RadioIntercepted = new(0.95f, 0.55f, 0.45f, 0.92f);

    private static readonly Color Dim = new(0.62f, 0.72f, 0.66f, 0.85f);
    private static readonly Color Bright = new(0.80f, 0.94f, 0.84f, 0.95f);
    private static readonly Color Warn = new(0.98f, 0.74f, 0.25f);
    private static readonly Color Danger = new(0.98f, 0.33f, 0.28f);
    private static readonly Color Panel = new(0.04f, 0.06f, 0.05f, 0.45f);

    public override void _Ready()
    {
        _heli = GetNodeOrNull<HelicopterController>(HelicopterPath);
        _landing = GetNodeOrNull<LandingController>(LandingControllerPath);
        _play = GetNodeOrNull<SiteInteraction>(InteractionPath);
        _threats = GetNodeOrNull<ThreatWorld>(ThreatWorldPath);
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        if (_play is not null)
        {
            _play.ModuleInstalled += mod => ShowDelta(mod, true);
            _play.ModuleRemoved += mod => ShowDelta(mod, false);
        }
    }

    public void SetRotorTime(RotorTime rt) => _rotorTime = rt;
    public void SetSidearm(Sidearm s) => _sidearm = s;
    public void SetGunPod(GunPodController g) => _gunpod = g;
    public void SetCamera(Camera3D cam) => _camera = cam;
    public void SetOnFoot(bool onFoot) => _onFoot = onFoot;
    public void SetNavTargets(NavTarget[] targets) => _navTargets = targets;
    public void SetRadioStrip(RadioStrip strip) => _radioStrip = strip;

    public override void _Process(double delta)
    {
        _warnBlink += delta;
        _deltaAge += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_heli is null) return;
        Vector2 size = Size;

        // Rotor Time bar is always visible in both modes.
        DrawRotorTimeBar(new Vector2(size.X * 0.5f - 100, size.Y - 52));

        if (_onFoot)
        {
            DrawCrosshair(size);
            DrawAmmoCounter(new Vector2(size.X - 180, size.Y - 100));
            DrawPilotHealth(new Vector2(28, size.Y - 100));
            DrawDamageFlash(size);
            DrawShotFeedback(size);
            DrawZoneMarkers(size);
            DrawSitePanel(new Vector2(size.X * 0.5f - 250, size.Y - 300));
            DrawRadioStrip(size);
            DrawFooterOnFoot(size);
            return;
        }

        var t = _heli.Sim.Telemetry;
        var sim = _heli.Sim;

        // D-055: instruments are items.  The left panel (airspeed, altitude, VS, heading)
        // and the control position display require the SAS module, because that is the
        // sensor package that provides air data.  Without it the pilot flies by visual
        // reference — the attitude indicator (horizon line) stays because it is the
        // windshield, not a sensor.
        bool hasSas = _heli!.SasAuthority > 0.001f;

        DrawAttitude(size, sim);

        if (hasSas)
            DrawLeftPanel(new Vector2(28, size.Y * 0.30f), t, sim);
        else
            DrawNoFlightData(new Vector2(28, size.Y * 0.30f));

        DrawRightPanel(new Vector2(size.X - 232, size.Y * 0.30f), t, sim);

        if (hasSas)
        {
            DrawCompass(new Vector2(size.X * 0.5f, 18), size.X, sim);
            DrawControlPositions(new Vector2(size.X - 190, size.Y - 190), sim);
        }

        DrawLandingPanel(new Vector2(size.X * 0.5f - 150, 26), t);
        DrawDamagePanel(new Vector2(28, size.Y * 0.30f + (hasSas ? 210 : 60)), sim);
        DrawRwr(new Vector2(size.X - 116, size.Y - 322));
        DrawSitePanel(new Vector2(size.X * 0.5f - 250, size.Y - 300));
        DrawWarnings(new Vector2(size.X * 0.5f, size.Y - 122), t);
        DrawDeltaCard(new Vector2(28, size.Y * 0.62f));
        DrawGunPod(size);
        DrawRadioStrip(size);
        DrawFooter(size);
    }

    // ------------------------------------------------------------- attitude

    private void DrawAttitude(Vector2 size, Helicopter sim)
    {
        Vector2 c = new(size.X * 0.5f, size.Y * 0.46f);
        float roll = (float)sim.State.Orientation.Roll;
        float pitch = (float)sim.State.Orientation.Pitch;

        // Horizon line, rolled and shifted by pitch. Kept small and unobtrusive: this is
        // a symbol on the glass, not a full artificial horizon ball.
        float pixelsPerRadian = 420f;
        Vector2 dir = new(Mathf.Cos(roll), -Mathf.Sin(roll));
        Vector2 up = new(Mathf.Sin(roll), Mathf.Cos(roll));
        Vector2 mid = c + up * (pitch * pixelsPerRadian);

        DrawLine(mid - dir * 230, mid - dir * 70, Dim, 1.6f, true);
        DrawLine(mid + dir * 70, mid + dir * 230, Dim, 1.6f, true);

        for (int deg = -30; deg <= 30; deg += 10)
        {
            if (deg == 0) continue;
            Vector2 m = c + up * ((pitch - Mathf.DegToRad(deg)) * pixelsPerRadian);
            float w = deg % 20 == 0 ? 44 : 26;
            DrawLine(m - dir * w, m + dir * w, Dim * new Color(1, 1, 1, 0.45f), 1.1f, true);
        }

        // Fixed aircraft symbol.
        DrawLine(c + new Vector2(-52, 0), c + new Vector2(-18, 0), Bright, 2.4f, true);
        DrawLine(c + new Vector2(18, 0), c + new Vector2(52, 0), Bright, 2.4f, true);
        DrawLine(c + new Vector2(-18, 0), c + new Vector2(0, 11), Bright, 2.4f, true);
        DrawLine(c + new Vector2(18, 0), c + new Vector2(0, 11), Bright, 2.4f, true);
        DrawRect(new Rect2(c - new Vector2(2, 2), new Vector2(4, 4)), Bright);

        // Slip/skid ball: the cheapest possible cue that tells a pilot they are flying
        // out of balance, which on a helicopter costs real speed and fuel.
        double slip = sim.Telemetry.Sideslip;
        float ballX = Mathf.Clamp((float)slip * 3.2f, -1, 1) * 46f;
        DrawLine(c + new Vector2(-52, 34), c + new Vector2(52, 34), Dim * new Color(1, 1, 1, 0.5f), 1.2f);
        DrawCircle(c + new Vector2(ballX, 34), 5.0f, Math.Abs(slip) > 0.14 ? Warn : Bright);
    }

    // -------------------------------------------------------- compass strip (D-082)

    /// <summary>
    /// A horizontal compass bar at the top of the screen. Centres on the aircraft's
    /// heading and shows ±60° of the compass rose with tick marks every 10° and cardinal
    /// labels. Active contract targets appear as gold chevrons with name and distance.
    ///
    /// Only drawn when SAS is fitted (D-055: instruments are items), because a heading
    /// tape requires sensors.
    /// </summary>
    private void DrawCompass(Vector2 centre, float screenWidth, Helicopter sim)
    {
        const float halfArc = 60f;                       // degrees visible each side
        float stripW = Math.Min(screenWidth - 80, 640);  // pixels wide
        float stripH = 28f;
        float left = centre.X - stripW / 2;
        float top = centre.Y;

        // Background
        DrawRect(new Rect2(left, top, stripW, stripH), Panel);

        double headingDeg = sim.State.Orientation.Yaw * 180.0 / Math.PI;
        if (headingDeg < 0) headingDeg += 360;

        float pixPerDeg = stripW / (halfArc * 2);

        // Tick marks and labels across the visible arc
        int startDeg = (int)Math.Floor(headingDeg - halfArc);
        int endDeg = (int)Math.Ceiling(headingDeg + halfArc);

        for (int deg = startDeg; deg <= endDeg; deg++)
        {
            int norm = ((deg % 360) + 360) % 360;
            float rel = (float)(deg - headingDeg);
            float px = centre.X + rel * pixPerDeg;
            if (px < left || px > left + stripW) continue;

            if (norm % 10 == 0)
            {
                float tickH = norm % 30 == 0 ? 10f : 5f;
                DrawLine(new Vector2(px, top + stripH - tickH), new Vector2(px, top + stripH),
                         Dim * new Color(1, 1, 1, 0.6f), 1.0f);
            }

            string? label = norm switch
            {
                0   => "N",
                45  => "NE",
                90  => "E",
                135 => "SE",
                180 => "S",
                225 => "SW",
                270 => "W",
                315 => "NW",
                _ when norm % 30 == 0 => $"{norm}",
                _ => null,
            };

            if (label is not null)
            {
                Color lc = norm == 0 ? Bright : Dim;
                var sz = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 11);
                DrawString(_font, new Vector2(px - sz.X / 2, top + 13), label,
                           HorizontalAlignment.Left, -1, 11, lc);
            }
        }

        // Centre reference mark (aircraft heading)
        DrawLine(new Vector2(centre.X, top), new Vector2(centre.X, top + 4), Bright, 1.8f);
        DrawLine(new Vector2(centre.X - 3, top), new Vector2(centre.X + 3, top), Bright, 1.8f);

        // Navigation targets: bearing chevrons
        if (_heli is null) return;
        double acN = sim.State.Position.X;
        double acE = sim.State.Position.Y;
        double hdgRad = sim.State.Orientation.Yaw;

        NavTarget? nearest = null;
        double nearestDist = double.MaxValue;

        foreach (var tgt in _navTargets)
        {
            double brg = Navigation.BearingRad(acN, acE, tgt.NorthM, tgt.EastM);
            double dist = Navigation.DistanceM(acN, acE, tgt.NorthM, tgt.EastM);
            double relDeg = Navigation.RelativeBearing(hdgRad, brg) * 180.0 / Math.PI;

            if (dist < nearestDist) { nearestDist = dist; nearest = tgt; }

            float px = centre.X + (float)relDeg * pixPerDeg;

            if (px >= left && px <= left + stripW)
            {
                // Chevron pointing down into the compass strip
                float cy = top - 2;
                DrawLine(new Vector2(px - 5, cy - 6), new Vector2(px, cy), NavMark, 1.6f);
                DrawLine(new Vector2(px + 5, cy - 6), new Vector2(px, cy), NavMark, 1.6f);

                // Target name above the chevron, truncated
                string name = tgt.Name.Length > 12 ? tgt.Name[..12] : tgt.Name;
                var nSz = _font.GetStringSize(name, HorizontalAlignment.Left, -1, 10);
                float labelX = Math.Clamp(px - nSz.X / 2, left, left + stripW - nSz.X);
                DrawString(_font, new Vector2(labelX, cy - 16), name,
                           HorizontalAlignment.Left, -1, 10, NavMark);
            }
            else
            {
                // Off-strip: draw an arrow at the edge pointing toward the target
                bool right = relDeg > 0;
                float edgeX = right ? left + stripW - 2 : left + 2;
                float my = top + stripH / 2;
                float dx = right ? -5 : 5;
                DrawLine(new Vector2(edgeX, my - 4), new Vector2(edgeX - dx, my), NavMark * new Color(1, 1, 1, 0.7f), 1.4f);
                DrawLine(new Vector2(edgeX, my + 4), new Vector2(edgeX - dx, my), NavMark * new Color(1, 1, 1, 0.7f), 1.4f);
            }
        }

        // Distance readout to nearest target, below the compass
        if (nearest is NavTarget nt && nearestDist < 50_000)
        {
            string dText = nearestDist >= 1000
                ? $"{nt.Name}  {nearestDist / 1000:F1} km"
                : $"{nt.Name}  {nearestDist:F0} m";
            var dSz = _font.GetStringSize(dText, HorizontalAlignment.Left, -1, 12);
            DrawString(_font, new Vector2(centre.X - dSz.X / 2, top + stripH + 4), dText,
                       HorizontalAlignment.Left, -1, 12, NavMark * new Color(1, 1, 1, 0.8f));
        }
    }

    // ----------------------------------------------------------- side panels

    private void DrawLeftPanel(Vector2 origin, FlightTelemetry t, Helicopter sim)
    {
        DrawRect(new Rect2(origin - new Vector2(10, 22), new Vector2(196, 210)), Panel);
        float y = origin.Y;

        Label(origin with { Y = y }, "AIRSPEED", Dim, 11); y += 15;
        Value(origin with { Y = y }, $"{t.AirspeedTrue * SimBridge.MetresPerSecondToKnots,5:F0}", "kt", Bright); y += 30;

        Label(origin with { Y = y }, "RADAR ALT", Dim, 11); y += 15;
        Color agl = t.HeightAgl < 25 ? Warn : Bright;
        Value(origin with { Y = y }, $"{t.HeightAgl,5:F0}", "m", agl); y += 30;

        Label(origin with { Y = y }, "VERT SPEED", Dim, 11); y += 15;
        double fpm = t.VerticalSpeed * SimBridge.MetresPerSecondToFpm;
        Value(origin with { Y = y }, $"{fpm,5:F0}", "fpm", Math.Abs(fpm) > 1800 ? Warn : Bright); y += 30;

        Label(origin with { Y = y }, "HEADING", Dim, 11); y += 15;
        double hdg = sim.State.Orientation.Yaw * 180.0 / Math.PI;
        if (hdg < 0) hdg += 360;
        Value(origin with { Y = y }, $"{hdg,5:F0}", "deg", Bright);
    }

    private void DrawRightPanel(Vector2 origin, FlightTelemetry t, Helicopter sim)
    {
        DrawRect(new Rect2(origin - new Vector2(10, 22), new Vector2(206, 232)), Panel);
        float y = origin.Y;

        // Rotor speed: the single most important number in the aircraft.
        Label(origin with { Y = y }, "ROTOR  Nr", Dim, 11); y += 15;
        Color nrColour = t.RotorRpmPercent < 90 || t.RotorRpmPercent > 108 ? Danger
                       : t.RotorRpmPercent < 95 || t.RotorRpmPercent > 104 ? Warn : Bright;
        Value(origin with { Y = y }, $"{t.RotorRpmPercent,5:F0}", "%", nrColour);
        Bar(origin + new Vector2(0, 18) with { Y = y + 18 }, 178, (float)(t.RotorRpmPercent / 120.0), nrColour);
        y += 44;

        Label(origin with { Y = y }, "TORQUE", Dim, 11); y += 15;
        Color tq = t.TorquePercent > 100 ? Danger : t.TorquePercent > 92 ? Warn : Bright;
        Value(origin with { Y = y }, $"{t.TorquePercent,5:F0}", "%", tq);
        Bar(origin + new Vector2(0, 18) with { Y = y + 18 }, 178, (float)(t.TorquePercent / 120.0), tq);
        y += 44;

        Label(origin with { Y = y }, "FUEL", Dim, 11); y += 15;
        double frac = t.FuelKg / Math.Max(sim.Airframe.FuelCapacity, 1);
        Color fc = frac < 0.10 ? Danger : frac < 0.22 ? Warn : Bright;
        Value(origin with { Y = y }, $"{t.FuelKg,5:F0}", "kg", fc);
        Bar(origin + new Vector2(0, 18) with { Y = y + 18 }, 178, (float)frac, fc);
        y += 42;

        double enduranceHours = t.FuelFlow > 1e-6 ? t.FuelKg / (t.FuelFlow * 3600) : 0;
        Label(origin with { Y = y }, $"ENDURANCE  {enduranceHours:F1} h   {t.FuelFlow * 3600:F0} kg/h", Dim, 11);
    }

    // ------------------------------------------------------ control position

    private void DrawControlPositions(Vector2 origin, Helicopter sim)
    {
        var a = sim.Actual;
        DrawRect(new Rect2(origin - new Vector2(8, 22), new Vector2(178, 172)), Panel);
        Label(origin - new Vector2(0, 8), "CONTROLS", Dim, 11);

        // Cyclic box: where the stick actually is, after actuator rate limiting. Useful
        // for a player learning that a helicopter is flown with tiny, continuous inputs.
        var box = new Rect2(origin + new Vector2(6, 8), new Vector2(96, 96));
        DrawRect(box, new Color(0, 0, 0, 0.30f));
        DrawRect(box, Dim * new Color(1, 1, 1, 0.5f), false, 1.0f);
        DrawLine(new Vector2(box.Position.X, box.Position.Y + box.Size.Y / 2),
                 new Vector2(box.End.X, box.Position.Y + box.Size.Y / 2), Dim * new Color(1, 1, 1, 0.25f));
        DrawLine(new Vector2(box.Position.X + box.Size.X / 2, box.Position.Y),
                 new Vector2(box.Position.X + box.Size.X / 2, box.End.Y), Dim * new Color(1, 1, 1, 0.25f));

        Vector2 stick = box.Position + box.Size * 0.5f
                        + new Vector2((float)a.CyclicRoll, (float)a.CyclicPitch) * (box.Size * 0.5f);
        DrawCircle(stick, 4.5f, Bright);

        // Collective lever.
        var lever = new Rect2(origin + new Vector2(116, 8), new Vector2(16, 96));
        DrawRect(lever, new Color(0, 0, 0, 0.30f));
        DrawRect(lever, Dim * new Color(1, 1, 1, 0.5f), false, 1.0f);
        float ly = lever.End.Y - (float)a.Collective * lever.Size.Y;
        DrawRect(new Rect2(lever.Position.X, ly - 2, lever.Size.X, 4), Bright);
        Label(origin + new Vector2(112, 120), "COLL", Dim, 10);

        // Pedals.
        var ped = new Rect2(origin + new Vector2(6, 112), new Vector2(96, 12));
        DrawRect(ped, new Color(0, 0, 0, 0.30f));
        DrawRect(ped, Dim * new Color(1, 1, 1, 0.5f), false, 1.0f);
        float px = ped.Position.X + ped.Size.X * 0.5f * (1 + (float)a.Pedal);
        DrawRect(new Rect2(px - 2, ped.Position.Y, 4, ped.Size.Y), Bright);
        Label(origin + new Vector2(6, 140), "PEDAL", Dim, 10);
    }

    // ------------------------------------------------------- landing assessment

    /// <summary>
    /// Shown while there is still time to go around. Everything here is a fact the pilot
    /// could work out by looking, presented so they do not have to: slope, roughness,
    /// rotor clearance and what the ground is made of.
    /// </summary>
    private void DrawLandingPanel(Vector2 origin, FlightTelemetry t)
    {
        if (_landing is null || _heli is null) return;
        var site = _landing.Site;
        double rotorR = _heli.Sim.Airframe.MainRotor.Radius;

        // Only when low enough for it to be a live question.
        if (t.HeightAgl > 70 || t.HeightAgl < -5) return;

        double q = site.Quality(12.0, rotorR);
        string verdict = site.Verdict(12.0, rotorR).ToUpperInvariant();
        Color c = q > 0.75 ? Bright : q > 0.45 ? Warn : Danger;

        DrawRect(new Rect2(origin, new Vector2(300, 92)), Panel);
        DrawRect(new Rect2(origin, new Vector2(300, 92)), c * new Color(1, 1, 1, 0.5f), false, 1.2f);

        Label(origin + new Vector2(12, 18), "LANDING SITE", Dim, 11);
        DrawString(_font, origin + new Vector2(12, 42), verdict, HorizontalAlignment.Left, -1, 19, c);

        Label(origin + new Vector2(12, 62), $"slope {site.SlopeDegrees,4:F0} deg", Dim, 12);
        Label(origin + new Vector2(112, 62), $"rough {site.RoughnessMetres,4:F2} m", Dim, 12);
        Label(origin + new Vector2(212, 62), site.Surface.ToString().ToLowerInvariant(), Dim, 12);

        double clearFactor = Math.Clamp(site.ClearanceRadius / (rotorR * 2.0), 0, 1);
        Label(origin + new Vector2(12, 80), "rotor clearance", Dim, 11);
        Bar(origin + new Vector2(112, 72), 176, (float)clearFactor,
            site.ClearanceRadius < rotorR * 1.15 ? Danger : Bright);

        // Brownout is the thing that will actually catch you out, so it gets its own line.
        if (_landing.Brownout > 0.05f)
        {
            var r = new Rect2(origin + new Vector2(0, 98), new Vector2(300, 26));
            DrawRect(r, Panel);
            Color bc = _landing.Brownout > 0.55f ? Danger : Warn;
            DrawRect(r, bc * new Color(1, 1, 1, 0.4f), false, 1.1f);
            Label(origin + new Vector2(12, 116), "BROWNOUT", bc, 12);
            Bar(origin + new Vector2(96, 108), 190, _landing.Brownout, bc);
        }

        // Recent touchdown verdict.
        if (_landing.LastTouchdownTime > 0 && _warnBlink - _landing.LastTouchdownTime < 6)
        {
            var r = _landing.LastTouchdown;
            Color tc = r.StructuralDamage > 0.3 ? Danger : r.StructuralDamage > 0.02 ? Warn : Bright;
            DrawString(_font, origin + new Vector2(12, 150), r.Summary.ToUpperInvariant(),
                       HorizontalAlignment.Left, -1, 15, tc);
        }
    }

    /// <summary>Component health, shown only for what is actually wrong.</summary>
    private void DrawDamagePanel(Vector2 origin, Helicopter sim)
    {
        var rows = new List<(Component c, double h)>();
        foreach (Component c in Enum.GetValues<Component>())
        {
            double h = sim.Damage.Health(c);
            if (h < 0.995) rows.Add((c, h));
        }
        if (rows.Count == 0) return;
        rows.Sort((a, b) => a.h.CompareTo(b.h));

        DrawRect(new Rect2(origin - new Vector2(10, 20), new Vector2(196, 24 + rows.Count * 18)), Panel);
        Label(origin, "AIRCRAFT", Dim, 11);

        float y = origin.Y + 18;
        foreach (var (c, h) in rows)
        {
            Color col = h < 0.35 ? Danger : h < 0.7 ? Warn : Bright;
            Label(new Vector2(origin.X, y + 10), c.ToString(), col, 12);
            Bar(new Vector2(origin.X + 104, y + 4), 72, (float)h, col);
            y += 18;
        }
    }

    /// <summary>
    /// Shown when the SAS module is not installed. The player sees where the flight data
    /// panel would be, matching the kneeboard's "empty bay" pattern — you cannot want a
    /// thing you do not know exists.
    /// </summary>
    private void DrawNoFlightData(Vector2 origin)
    {
        DrawRect(new Rect2(origin - new Vector2(10, 22), new Vector2(196, 48)), Panel);
        Label(origin with { Y = origin.Y + 4 }, "NO FLIGHT DATA", Dim * new Color(1, 1, 1, 0.65f), 12);
    }

    /// <summary>
    /// What can be done here. Only ever shown when the aircraft is actually shut down at
    /// a place, because landing is meant to be the act that pays (D-003a).
    /// </summary>
    private void DrawSitePanel(Vector2 origin)
    {
        if (_play is null || _play.InDialogue) return;

        if (_play.Busy is string busy)
        {
            var r = new Rect2(origin, new Vector2(500, 54));
            DrawRect(r, Panel);
            DrawRect(r, Bright * new Color(1, 1, 1, 0.5f), false, 1.2f);
            DrawString(_font, origin + new Vector2(16, 26), busy + "...", HorizontalAlignment.Left, -1, 16, Bright);
            Bar(origin + new Vector2(16, 34), 468, _play.BusyProgress, Bright);
            return;
        }

        if (_play.Parked is not Site site) return;
        var actions = _play.Actions;
        if (actions.Count == 0) return;

        float h = 42 + actions.Count * 24;
        var panel = new Rect2(origin, new Vector2(500, h));
        DrawRect(panel, Panel);
        DrawRect(panel, Bright * new Color(1, 1, 1, 0.45f), false, 1.2f);

        DrawString(_font, origin + new Vector2(16, 24), site.Name.ToUpperInvariant(),
                   HorizontalAlignment.Left, -1, 15, Bright);

        // Hostile/cleared tag next to the site kind.
        bool hostile = Encounter.IsHostile(site.Id, (int)site.Kind, site.Tier);
        bool cleared = hostile && _play.Progress.Record(site.Id).Cleared;
        string kindTag = hostile
            ? (cleared ? $"{site.Kind}  CLEARED" : $"{site.Kind}  HOSTILE")
            : site.Kind.ToString();
        Color kindColor = hostile && !cleared ? Danger : Dim;
        var kindSize = _font.GetStringSize(kindTag, HorizontalAlignment.Left, -1, 12);
        DrawString(_font, origin + new Vector2(484 - kindSize.X, 24), kindTag,
                   HorizontalAlignment.Left, -1, 12, kindColor);

        float y = origin.Y + 46;
        for (int i = 0; i < actions.Count; i++)
        {
            Color c = actions[i].Available ? Bright : Dim * new Color(1, 1, 1, 0.75f);
            DrawString(_font, new Vector2(origin.X + 16, y), $"{i + 1}", HorizontalAlignment.Left, -1, 13,
                       actions[i].Available ? Warn : Dim);
            DrawString(_font, new Vector2(origin.X + 34, y), actions[i].Label, HorizontalAlignment.Left, -1, 13, c);
            y += 24;
        }
    }

    /// <summary>
    /// The radar warning receiver: bearing to every emitter that is looking at you, and
    /// how hard. It only exists if the box is fitted - which is the point, because under
    /// D-005 the avionics ARE the skill tree, and this one turns the sky from a coin flip
    /// into information.
    ///
    /// Nothing ever appears here for a gun or a heat seeker. They do not emit. You find
    /// out about those the way pilots always have.
    /// </summary>
    private void DrawRwr(Vector2 centre)
    {
        if (_threats is null) return;
        bool fitted = _threats.Field.Fitted.HasFlag(Countermeasure.RadarWarning);

        const float radius = 74f;
        DrawCircle(centre, radius + 8, Panel);
        DrawArc(centre, radius, 0, Mathf.Tau, 48, Dim * new Color(1, 1, 1, 0.55f), 1.2f);
        DrawArc(centre, radius * 0.5f, 0, Mathf.Tau, 32, Dim * new Color(1, 1, 1, 0.28f), 1.0f);

        // Own-ship symbol, nose up.
        DrawLine(centre + new Vector2(0, -7), centre + new Vector2(0, 5), Dim, 1.3f);
        DrawLine(centre + new Vector2(-5, 2), centre + new Vector2(5, 2), Dim, 1.3f);

        Label(centre + new Vector2(-16, radius + 24), "RWR", Dim, 11);

        if (!fitted)
        {
            var sz = _font.GetStringSize("NO RECEIVER", HorizontalAlignment.Left, -1, 12);
            DrawString(_font, centre - new Vector2(sz.X / 2, -radius * 0.55f), "NO RECEIVER",
                       HorizontalAlignment.Left, -1, 12, Dim * new Color(1, 1, 1, 0.8f));
            return;
        }

        foreach (ThreatTrack t in _threats.VisibleTracks())
        {
            // Range on the scope is normalised to the emitter's own reach, so a contact
            // at the rim is one that has only just found you and one at the centre is
            // about to shoot. That is the number the pilot actually needs.
            float r = (float)Mathf.Clamp(t.Range / Math.Max(t.Emitter.DetectionRange, 1), 0.06, 1.0);
            float b = (float)t.BearingFromAircraft;
            Vector2 p = centre + new Vector2(Mathf.Sin(b), -Mathf.Cos(b)) * (radius * r);

            Color c = t.State switch
            {
                TrackState.Engaging => Danger,
                TrackState.Locked => Danger,
                TrackState.Tracking => Warn,
                _ => Dim,
            };

            string sym = t.Emitter.Kind switch
            {
                ThreatKind.Sam => "S",
                ThreatKind.SearchRadar => "E",
                ThreatKind.Aerostat => "A",
                _ => "?",
            };

            if (t.State is TrackState.Locked or TrackState.Engaging)
                DrawArc(p, 10, 0, Mathf.Tau, 14, c, 1.4f);

            var sz = _font.GetStringSize(sym, HorizontalAlignment.Left, -1, 14);
            DrawString(_font, p - sz * 0.5f + new Vector2(0, sz.Y * 0.36f), sym,
                       HorizontalAlignment.Left, -1, 14, c);
        }

        // Stores.
        var f = _threats.Field;
        string stores = "";
        if (f.Fitted.HasFlag(Countermeasure.Chaff)) stores += $"CH {f.ChaffRemaining}   ";
        if (f.Fitted.HasFlag(Countermeasure.Flares)) stores += $"FL {f.FlaresRemaining}";
        if (stores.Length > 0)
            Label(centre + new Vector2(-radius + 6, radius + 24), stores,
                  f.ChaffActive || f.FlaresActive ? Warn : Dim, 12);
    }

    // ------------------------------------------------------------- warnings

    private void DrawWarnings(Vector2 centre, FlightTelemetry t)
    {
        bool blink = (int)(_warnBlink * 3) % 2 == 0;
        float x = centre.X - 300;

        void Caption(string text, bool active, Color colour, bool flashing = false)
        {
            if (!active) return;
            if (flashing && !blink) { x += 118; return; }
            var r = new Rect2(x, centre.Y, 110, 24);
            DrawRect(r, colour * new Color(1, 1, 1, 0.18f));
            DrawRect(r, colour, false, 1.2f);
            var sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 13);
            DrawString(_font, new Vector2(x + (110 - sz.X) / 2, centre.Y + 17), text,
                       HorizontalAlignment.Left, -1, 13, colour);
            x += 118;
        }

        // A launch is the only thing that outranks everything else on the panel.
        if (_threats?.IncomingWarning is ThreatTrack inbound && _threats.IncomingAge < 6)
        {
            string label = inbound.Emitter.Kind == ThreatKind.Sam ? "MISSILE  SAM" : "MISSILE";
            var big = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 26);
            var box = new Rect2(centre.X - big.X / 2 - 18, centre.Y - 52, big.X + 36, 40);
            if (blink)
            {
                DrawRect(box, Danger * new Color(1, 1, 1, 0.22f));
                DrawRect(box, Danger, false, 1.8f);
                DrawString(_font, new Vector2(box.Position.X + 18, box.Position.Y + 29), label,
                           HorizontalAlignment.Left, -1, 26, Danger);
            }
        }

        // The caution and warning panel owns everything below, and these six captions are
        // gone rather than merely duplicated.
        //
        // They were naive comparisons - `RotorRpmPercent < 92`, `StalledFraction > 0.18` -
        // against signals that are measurably noisy: torque swings 5.8 points in a steady
        // hover and stalled fraction swings 0 to 5% at 100 kt on the 2/rev. A bare
        // comparison on those reports blade azimuth, not a condition; the same drive-by
        // measurement counted 582 threshold transitions where the filtered system counts
        // two. Keeping them beside a properly hysteresised panel would have shown the pilot
        // two different answers to the same question, and the flickering one is the one the
        // eye goes to.
        //
        // AUTOROTATE stays: it is a state, not a threshold, so there is nothing to filter,
        // and it is the one a pilot most wants confirmed instantly.
        Caption("AUTOROTATE", t.Autorotating && t.Engine != EngineState.Running, Warn);
    }

    // ----------------------------------------------------------- radio strip (D-085)

    /// <summary>
    /// Two-line text strip at the bottom of the HUD for radio messages (story.md §4.3).
    /// Word-by-word reveal at reading pace. Broadcasts are cool blue, directed calls
    /// are green, intercepted traffic is warm red.
    /// </summary>
    private void DrawRadioStrip(Vector2 size)
    {
        if (_radioStrip is null || !_radioStrip.Active) return;

        string text = _radioStrip.CurrentText!;
        string? speaker = _radioStrip.CurrentSpeaker;
        Color textCol = _radioStrip.CurrentKind switch
        {
            RadioMessageKind.Directed => RadioDirected,
            RadioMessageKind.Intercepted => RadioIntercepted,
            _ => RadioBroadcast,
        };

        const int fontSize = 16;
        float maxWidth = Math.Min(size.X - 80, 720f);
        float x = (size.X - maxWidth) * 0.5f;
        float y = size.Y - 80;

        // Word-wrap into at most two lines.
        var lines = new List<string>(2);
        var line = new System.Text.StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (_font.GetStringSize(candidate, HorizontalAlignment.Left, -1, fontSize).X > maxWidth
                && line.Length > 0)
            {
                lines.Add(line.ToString());
                line.Clear();
                line.Append(word);
                if (lines.Count >= 2) break;
            }
            else
            {
                line.Clear();
                line.Append(candidate);
            }
        }
        if (line.Length > 0 && lines.Count < 2)
            lines.Add(line.ToString());

        // Background panel behind the text.
        float lineHeight = fontSize + 4;
        float panelH = lines.Count * lineHeight + 12;
        float speakerWidth = 0;
        if (!string.IsNullOrEmpty(speaker))
            speakerWidth = _font.GetStringSize(speaker + "  ", HorizontalAlignment.Left, -1, fontSize - 2).X;

        DrawRect(new Rect2(x - 8, y - 6, maxWidth + 16 + speakerWidth, panelH), Panel);

        // Speaker tag in dim, offset left.
        if (!string.IsNullOrEmpty(speaker))
        {
            Label(new Vector2(x, y + lineHeight * 0.5f), speaker,
                  Dim * new Color(1, 1, 1, 0.8f), fontSize - 2);
        }

        // Text lines.
        float textX = x + speakerWidth;
        for (int i = 0; i < lines.Count; i++)
        {
            DrawString(_font, new Vector2(textX, y + i * lineHeight + lineHeight * 0.75f),
                       lines[i], HorizontalAlignment.Left, -1, fontSize, textCol);
        }
    }

    private void DrawFooter(Vector2 size)
    {
        string help = "W/S or throttle: collective   arrows or stick: cyclic   A/D: pedals   " +
                      "C: camera   TAB: kneeboard   1-4: actions   Z/X: chaff/flares   LMB: gun   F: dismount   R: respawn";
        DrawString(_font, new Vector2(20, size.Y - 16), help, HorizontalAlignment.Left, -1, 12,
                   Dim * new Color(1, 1, 1, 0.55f));
    }

    private void DrawFooterOnFoot(Vector2 size)
    {
        string help = "WASD: move   LMB: fire   RMB: Rotor Time   R: reload   " +
                      "TAB: kneeboard   F: board Hugh";
        DrawString(_font, new Vector2(20, size.Y - 16), help, HorizontalAlignment.Left, -1, 12,
                   Dim * new Color(1, 1, 1, 0.55f));
    }

    // ------------------------------------------------------- module delta card

    /// <summary>
    /// D-005a §2: printed numeric delta on acquisition. Briefly shows mass, fuel and drag
    /// changes when a module is installed or removed, so the trade is legible.
    /// </summary>
    private void ShowDelta(ModuleDef mod, bool installed)
    {
        _deltaTitle = installed
            ? $"{mod.Name.ToUpperInvariant()} FITTED"
            : $"{mod.Name.ToUpperInvariant()} REMOVED";
        _deltaLines.Clear();

        double sign = installed ? 1 : -1;
        _deltaLines.Add($"Mass       {FormatDelta(sign * mod.Mass)} kg");

        if (Math.Abs(mod.FuelCapacityDelta) > 0.1)
            _deltaLines.Add($"Fuel cap   {FormatDelta(sign * mod.FuelCapacityDelta)} kg");

        if (Math.Abs(mod.DragDelta.X) > 0.001)
            _deltaLines.Add($"Drag       {FormatDelta(sign * mod.DragDelta.X)} m\u00B2");

        if (_heli is not null)
            _deltaLines.Add($"Total      {_heli.Sim.TotalMass:F0} kg");

        _deltaAge = 0;
    }

    private static string FormatDelta(double v) =>
        v >= 0 ? $"+{v:F0}" : $"{v:F0}";

    private void DrawDeltaCard(Vector2 origin)
    {
        if (_deltaTitle is null || _deltaAge >= DeltaDuration) return;

        float alpha = _deltaAge < DeltaDuration - 1.0
            ? 1f
            : (float)(DeltaDuration - _deltaAge);

        float h = 30 + _deltaLines.Count * 18 + 8;
        var panel = new Rect2(origin, new Vector2(250, h));
        DrawRect(panel, Panel * new Color(1, 1, 1, alpha));
        DrawRect(panel, Bright * new Color(1, 1, 1, 0.45f * alpha), false, 1.2f);

        DrawString(_font, origin + new Vector2(12, 20), _deltaTitle,
                   HorizontalAlignment.Left, -1, 13, Bright * new Color(1, 1, 1, alpha));

        float y = origin.Y + 36;
        foreach (var line in _deltaLines)
        {
            DrawString(_font, new Vector2(origin.X + 12, y), line,
                       HorizontalAlignment.Left, -1, 12, Dim * new Color(1, 1, 1, alpha));
            y += 18;
        }
    }

    // ---------------------------------------------------------- rotor time

    private static readonly Color RtFill = new(0.65f, 0.88f, 0.72f, 0.90f);
    private static readonly Color RtActive = new(0.98f, 0.74f, 0.25f, 0.95f);

    private void DrawRotorTimeBar(Vector2 origin)
    {
        if (_rotorTime is null) return;
        float charge = _rotorTime.Charge;
        if (charge < 0.005f && !_rotorTime.Active) return;

        float width = 200f;
        float height = 8f;
        var bg = new Rect2(origin, new Vector2(width, height));
        DrawRect(bg, new Color(0, 0, 0, 0.40f));

        Color fill = _rotorTime.Active ? RtActive : RtFill;
        DrawRect(new Rect2(origin.X, origin.Y, width * Mathf.Clamp(charge, 0, 1), height), fill);

        // Activation threshold tick.
        float threshX = origin.X + width * _rotorTime.ActivationThreshold;
        DrawLine(new Vector2(threshX, origin.Y - 2), new Vector2(threshX, origin.Y + height + 2),
                 Dim * new Color(1, 1, 1, 0.6f), 1.0f);

        string label = _rotorTime.Active ? "ROTOR TIME" : "RT";
        Label(origin + new Vector2(width + 8, height), label,
              _rotorTime.Active ? RtActive : Dim, 11);
    }

    private void DrawCrosshair(Vector2 size)
    {
        Vector2 c = size * 0.5f;
        Color col = _rotorTime is { Active: true } ? RtActive : Dim;
        float len = _rotorTime is { Active: true } ? 14f : 8f;
        float gap = 4f;

        DrawLine(c + new Vector2(-len - gap, 0), c + new Vector2(-gap, 0), col, 1.4f, true);
        DrawLine(c + new Vector2(gap, 0), c + new Vector2(len + gap, 0), col, 1.4f, true);
        DrawLine(c + new Vector2(0, -len - gap), c + new Vector2(0, -gap), col, 1.4f, true);
        DrawLine(c + new Vector2(0, gap), c + new Vector2(0, len + gap), col, 1.4f, true);

        if (_rotorTime is { Active: true })
            DrawCircle(c, 2f, col);
    }

    // -------------------------------------------------------- combat HUD

    /// <summary>
    /// Ammo counter: rounds in cylinder and spare. Bottom right, matches the
    /// instrument panel aesthetic.
    /// </summary>
    private void DrawAmmoCounter(Vector2 origin)
    {
        if (_sidearm is null) return;
        var s = _sidearm.State;

        DrawRect(new Rect2(origin, new Vector2(156, 58)), Panel);

        string status = _sidearm.IsReloading ? "RELOADING" : "SIDEARM";
        Label(origin + new Vector2(10, 16), status,
              _sidearm.IsReloading ? Warn : Dim, 11);

        // Cylinder dots: filled for loaded, hollow for empty.
        for (int i = 0; i < s.Capacity; i++)
        {
            Vector2 p = origin + new Vector2(10 + i * 16, 34);
            if (i < s.Rounds)
                DrawCircle(p, 5, Bright);
            else
            {
                DrawArc(p, 5, 0, Mathf.Tau, 12, Dim, 1.2f);
            }
        }

        // Spare count
        Label(origin + new Vector2(108, 42), $"+{s.SpareRounds}", Dim, 12);
    }

    private void DrawPilotHealth(Vector2 origin)
    {
        if (_sidearm is null) return;
        float hp = _sidearm.PilotHp.Health;
        float max = _sidearm.PilotHp.MaxHealth;
        float frac = hp / max;

        DrawRect(new Rect2(origin, new Vector2(156, 36)), Panel);
        Label(origin + new Vector2(10, 16), "PILOT", Dim, 11);
        Color c = frac < 0.35f ? Danger : frac < 0.6f ? Warn : Bright;
        Bar(origin + new Vector2(10, 24), 136, frac, c);
    }

    /// <summary>Red vignette flash when the pilot takes damage.</summary>
    private void DrawDamageFlash(Vector2 size)
    {
        if (_sidearm is null) return;
        float t = _sidearm.TimeSinceDamage;
        if (t > 0.6f) return;

        float alpha = (1f - t / 0.6f) * 0.35f;
        Color flash = new(0.9f, 0.1f, 0.05f, alpha);

        // Vignette: four edge strips
        float inset = 60;
        DrawRect(new Rect2(0, 0, size.X, inset), flash);                     // top
        DrawRect(new Rect2(0, size.Y - inset, size.X, inset), flash);        // bottom
        DrawRect(new Rect2(0, inset, inset, size.Y - inset * 2), flash);     // left
        DrawRect(new Rect2(size.X - inset, inset, inset, size.Y - inset * 2), flash); // right
    }

    /// <summary>Brief feedback text when a shot hits or misses.</summary>
    private void DrawShotFeedback(Vector2 size)
    {
        if (_sidearm is null) return;
        float t = _sidearm.TimeSinceLastShot;
        if (t > 1.5f) return;

        var shot = _sidearm.LastShot;
        if (shot is null) return;

        float alpha = Mathf.Clamp(1f - t / 1.5f, 0, 1);
        Vector2 pos = new(size.X * 0.5f, size.Y * 0.38f);

        if (shot.Value.DidHit && shot.Value.Effect is { } eff)
        {
            Color c = eff.Effect == ZoneEffect.Incapacitate ? Danger
                     : eff.Effect == ZoneEffect.Disarm ? Warn
                     : Bright;
            c.A *= alpha;
            string text = eff.Description.ToUpperInvariant();
            var sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 16);
            DrawString(_font, pos - new Vector2(sz.X / 2, 0), text,
                       HorizontalAlignment.Left, -1, 16, c);
        }
        else
        {
            Color c = Dim;
            c.A *= alpha;
            string text = "MISS";
            var sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 14);
            DrawString(_font, pos - new Vector2(sz.X / 2, 0), text,
                       HorizontalAlignment.Left, -1, 14, c);
        }
    }

    /// <summary>
    /// During Rotor Time, project body zone markers onto visible hostile NPCs.
    /// This is the "called shot" — the slow-motion highlights what you can aim at.
    /// </summary>
    private void DrawZoneMarkers(Vector2 size)
    {
        if (_rotorTime is not { Active: true }) return;
        if (_camera is null) return;

        // Find hostile NPCs in the scene.
        var main = GetNode<Main>("/root/Main");
        if (main is null) return;

        foreach (var child in main.GetChildren())
        {
            if (child is not HostileNpc npc) continue;
            if (npc.Health.IsDown) continue;

            float dist = _camera.GlobalPosition.DistanceTo(npc.GlobalPosition);
            if (dist > 60f) continue;

            // Project each zone's world position to screen space.
            foreach (var zoneChild in npc.GetChildren())
            {
                if (zoneChild is not StaticBody3D zoneBody) continue;
                if (!zoneBody.HasMeta("body_zone")) continue;

                var zone = (BodyZone)(int)zoneBody.GetMeta("body_zone");
                Vector3 worldPos = zoneBody.GlobalPosition;

                if (_camera.IsPositionBehind(worldPos)) continue;
                Vector2 screen = _camera.UnprojectPosition(worldPos);

                // Zone label
                string label = zone switch
                {
                    BodyZone.Head => "HEAD",
                    BodyZone.Torso => "BODY",
                    BodyZone.LeftArm => "L.ARM",
                    BodyZone.RightArm => "R.ARM",
                    BodyZone.LeftLeg => "L.LEG",
                    BodyZone.RightLeg => "R.LEG",
                    BodyZone.Weapon => "WEAPON",
                    _ => "?",
                };

                // Highlight the zone nearest to crosshair.
                float distToCenter = screen.DistanceTo(size * 0.5f);
                bool nearCross = distToCenter < 50f;
                Color c = nearCross ? RtActive : Dim * new Color(1, 1, 1, 0.75f);

                // Diamond marker
                float markerSize = nearCross ? 8f : 5f;
                DrawLine(screen + new Vector2(0, -markerSize), screen + new Vector2(markerSize, 0), c, 1.4f);
                DrawLine(screen + new Vector2(markerSize, 0), screen + new Vector2(0, markerSize), c, 1.4f);
                DrawLine(screen + new Vector2(0, markerSize), screen + new Vector2(-markerSize, 0), c, 1.4f);
                DrawLine(screen + new Vector2(-markerSize, 0), screen + new Vector2(0, -markerSize), c, 1.4f);

                // Label offset to the right
                DrawString(_font, screen + new Vector2(markerSize + 4, 4), label,
                           HorizontalAlignment.Left, -1, nearCross ? 12 : 10, c);
            }
        }
    }

    // --------------------------------------------------------- gun pod (D-081)

    /// <summary>
    /// Gun pod HUD: crosshair, ammo counter, and hit feedback while flying with
    /// the gun pod installed. During Rotor Time the crosshair expands and the
    /// ammo readout is more prominent.
    /// </summary>
    private void DrawGunPod(Vector2 size)
    {
        if (_gunpod is not { Installed: true }) return;

        // Gun crosshair — always visible when the pod is fitted, larger during RT.
        DrawGunCrosshair(size);
        DrawGunAmmo(new Vector2(size.X - 180, size.Y - 168));
        DrawGunHitFeedback(size);
    }

    private void DrawGunCrosshair(Vector2 size)
    {
        Vector2 c = size * 0.5f;
        bool rt = _rotorTime is { Active: true };
        Color col = rt ? RtActive : Bright * new Color(1, 1, 1, 0.70f);
        float len = rt ? 20f : 12f;
        float gap = rt ? 6f : 8f;

        // Four lines forming a gapped cross — same pattern as the sidearm crosshair
        // but wider and with a different gap to visually distinguish air mode.
        DrawLine(c + new Vector2(-len - gap, 0), c + new Vector2(-gap, 0), col, 1.6f, true);
        DrawLine(c + new Vector2(gap, 0), c + new Vector2(len + gap, 0), col, 1.6f, true);
        DrawLine(c + new Vector2(0, -len - gap), c + new Vector2(0, -gap), col, 1.6f, true);
        DrawLine(c + new Vector2(0, gap), c + new Vector2(0, len + gap), col, 1.6f, true);

        // During RT: centre dot and outer ring for "weapon active" emphasis.
        if (rt)
        {
            DrawCircle(c, 2.5f, col);
            DrawArc(c, len + gap + 4, 0, Mathf.Tau, 32, col * new Color(1, 1, 1, 0.45f), 1.2f);
        }
    }

    private void DrawGunAmmo(Vector2 origin)
    {
        if (_gunpod is null) return;
        var s = _gunpod.State;

        DrawRect(new Rect2(origin, new Vector2(156, 42)), Panel);
        Label(origin + new Vector2(10, 16), "GUN", Dim, 11);

        // Ammo bar: simple fill showing remaining rounds.
        float frac = (float)s.Rounds / s.MaxRounds;
        Color c = frac < 0.15f ? Danger : frac < 0.30f ? Warn : Bright;
        Bar(origin + new Vector2(50, 10), 96, frac, c);
        Label(origin + new Vector2(50, 32), $"{s.Rounds}", c, 12);
    }

    private void DrawGunHitFeedback(Vector2 size)
    {
        if (_gunpod is null) return;
        float t = _gunpod.TimeSinceLastShot;
        if (t > 1.5f) return;

        var hit = _gunpod.LastHit;
        if (hit is null) return;

        float alpha = Mathf.Clamp(1f - t / 1.5f, 0, 1);
        // Show slightly above centre — same position as sidearm feedback but offset
        // up to avoid overlapping with the attitude indicator.
        Vector2 pos = new(size.X * 0.5f, size.Y * 0.32f);

        if (hit.Value.Kind is GunHitKind.EmitterHit or GunHitKind.EmitterDestroyed
            or GunHitKind.BuildingHit or GunHitKind.BuildingDestroyed)
        {
            Color c = hit.Value.Kind is GunHitKind.EmitterDestroyed or GunHitKind.BuildingDestroyed
                ? Danger : Warn;
            c.A *= alpha;
            string text = hit.Value.Description.ToUpperInvariant();
            var sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 16);
            DrawString(_font, pos - new Vector2(sz.X / 2, 0), text,
                       HorizontalAlignment.Left, -1, 16, c);
        }
        // Misses are not shown — at 550 rpm, showing "MISS" for every round that
        // doesn't hit would be unreadable. The hits are the signal.
    }

    // --------------------------------------------------------------- helpers

    private void Label(Vector2 p, string s, Color c, int size) =>
        DrawString(_font, p, s, HorizontalAlignment.Left, -1, size, c);

    private void Value(Vector2 p, string v, string unit, Color c)
    {
        DrawString(_font, p + new Vector2(0, 18), v, HorizontalAlignment.Left, -1, 26, c);
        var w = _font.GetStringSize(v, HorizontalAlignment.Left, -1, 26);
        DrawString(_font, p + new Vector2(w.X + 6, 18), unit, HorizontalAlignment.Left, -1, 13, Dim);
    }

    private void Bar(Vector2 p, float width, float frac, Color c)
    {
        var bg = new Rect2(p.X, p.Y, width, 5);
        DrawRect(bg, new Color(0, 0, 0, 0.35f));
        DrawRect(new Rect2(p.X, p.Y, width * Mathf.Clamp(frac, 0, 1), 5), c);
        // 100% tick
        DrawLine(new Vector2(p.X + width / 1.2f, p.Y - 2), new Vector2(p.X + width / 1.2f, p.Y + 7),
                 Dim * new Color(1, 1, 1, 0.7f), 1.0f);
    }
}
