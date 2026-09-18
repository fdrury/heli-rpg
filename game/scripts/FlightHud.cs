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
    private Font _font = null!;
    private double _warnBlink;

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
    }

    public override void _Process(double delta)
    {
        _warnBlink += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_heli is null) return;
        var t = _heli.Sim.Telemetry;
        var sim = _heli.Sim;
        Vector2 size = Size;

        DrawAttitude(size, sim);
        DrawLeftPanel(new Vector2(28, size.Y * 0.30f), t, sim);
        DrawRightPanel(new Vector2(size.X - 232, size.Y * 0.30f), t, sim);
        DrawControlPositions(new Vector2(size.X - 190, size.Y - 190), sim);
        DrawLandingPanel(new Vector2(size.X * 0.5f - 150, 26), t);
        DrawDamagePanel(new Vector2(28, size.Y * 0.30f + 210), sim);
        DrawRwr(new Vector2(size.X - 116, size.Y - 322));
        DrawSitePanel(new Vector2(size.X * 0.5f - 250, size.Y - 300));
        DrawWarnings(new Vector2(size.X * 0.5f, size.Y - 122), t);
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
    /// What can be done here. Only ever shown when the aircraft is actually shut down at
    /// a place, because landing is meant to be the act that pays (D-003a).
    /// </summary>
    private void DrawSitePanel(Vector2 origin)
    {
        if (_play is null) return;

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
        var kindSize = _font.GetStringSize(site.Kind.ToString(), HorizontalAlignment.Left, -1, 12);
        DrawString(_font, origin + new Vector2(484 - kindSize.X, 24), site.Kind.ToString(),
                   HorizontalAlignment.Left, -1, 12, Dim);

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

        Caption("VORTEX RING", t.VrsSeverity > 0.25, Danger, true);
        Caption("LOW ROTOR", t.RotorRpmPercent < 92, Danger, true);
        Caption("AUTOROTATE", t.Autorotating && t.Engine != EngineState.Running, Warn);
        Caption("TORQUE", t.TorqueLimited, Warn);
        Caption("TAIL AUTH", t.TailRotorSaturated, Warn);
        Caption("BLADE STALL", t.StalledFraction > 0.18, Warn);
    }

    private void DrawFooter(Vector2 size)
    {
        string help = "W/S or throttle: collective   arrows or stick: cyclic   A/D: pedals   " +
                      "C: camera   TAB: kneeboard   1-4: actions   Z/X: chaff/flares   R: respawn";
        DrawString(_font, new Vector2(20, size.Y - 16), help, HorizontalAlignment.Left, -1, 12,
                   Dim * new Color(1, 1, 1, 0.55f));
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
