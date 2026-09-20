using System;
using System.Collections.Generic;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Puts the threat field into the world, and answers the only question the sim cannot:
/// is there a hill in the way.
///
/// Terrain masking is the whole mechanic, so line of sight is computed properly - a walk
/// along the ray from the emitter to the aircraft, checking whether the ground rises
/// above it. It is sampled a few times a second rather than every frame, because the
/// answer changes on the timescale of a helicopter moving, not a frame.
///
/// Placement follows D-003b: threats guard the things worth reaching, and the tier of the
/// region decides how much is there. Which means the map is gated by what it costs to get
/// somewhere, not by an invisible wall or a level requirement.
/// </summary>
public sealed partial class ThreatWorld : Node
{
    [Export] public NodePath HelicopterPath { get; set; } = "";
    [Export] public NodePath InteractionPath { get; set; } = "";

    /// <summary>Metres between samples when walking the line of sight.</summary>
    [Export] public float LosStep { get; set; } = 45f;

    /// <summary>Seconds between line-of-sight recomputations.</summary>
    [Export] public float LosInterval { get; set; } = 0.30f;

    public ThreatField Field { get; } = new();
    public AlertState Alert { get; } = new();

    /// <summary>
    /// Switch the whole field off. Used by the bridge self-test, which measures control
    /// derivatives and cannot do that while being shot at - the first run of it after
    /// threats went in reported the cyclic working backwards, because the aircraft was
    /// being hit mid-measurement.
    /// </summary>
    public bool Disabled { get; set; }

    private HelicopterController _heli = null!;
    private SiteInteraction? _play;
    private readonly Dictionary<int, bool> _losCache = new();
    private readonly Dictionary<int, int> _emitterRegion = new();
    private double _losTimer;
    private double _rwrSilenceTimer;

    /// <summary>Last launch warning, for the HUD to shout about.</summary>
    public ThreatTrack? IncomingWarning { get; private set; }
    public double IncomingAge { get; private set; } = 999;

    public override void _Ready()
    {
        _heli = GetNode<HelicopterController>(HelicopterPath);
        _play = GetNodeOrNull<SiteInteraction>(InteractionPath);

        Place();

        Field.Alert = Alert;
        Field.EmitterRegion = eid => _emitterRegion.GetValueOrDefault(eid, 0);

        Field.Struck += OnStruck;
        Field.Launch += t =>
        {
            IncomingWarning = t;
            IncomingAge = 0;
            GD.Print($"[threat] LAUNCH from {t.Emitter.Name} ({t.Emitter.Kind}) at {t.Range:F0} m");
            // Being engaged is loud — the region is now very awake.
            if (_emitterRegion.TryGetValue(t.Emitter.Id, out int rid))
                Alert.Engaged(rid);
        };
        Field.FirstDetection += OnFirstDetection;

        GD.Print($"[threat] {Field.Tracks.Count} emitters placed");
    }

    /// <summary>
    /// Emitters guard the things worth having. Deterministic from the site id, so the
    /// threat map is a fixed fact about the world that the player can learn.
    /// </summary>
    private void Place()
    {
        var rng = new RandomNumberGenerator { Seed = 60607 };
        int id = 0;

        foreach (Site site in WorldMap.Sites)
        {
            if (site.Tier == 0) continue;           // the starting country is safe

            // The bigger the prize and the deeper the region, the more is watching it.
            int count = site.Kind switch
            {
                SiteKind.Airfield => site.Tier + 1,
                SiteKind.Depot => site.Tier,
                SiteKind.Settlement => site.Tier - 1,
                SiteKind.Relay => 1,
                _ => 0,
            };
            if (count <= 0) continue;

            for (int i = 0; i < count; i++)
            {
                ThreatKind kind = PickKind(rng, site.Tier, i);
                float a = rng.Randf() * Mathf.Tau;
                float r = rng.RandfRange(site.Radius * 0.8f, site.Radius * 2.6f);
                var pos = site.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

                // Sim world is NED: north = -godot Z, east = +godot X.
                int eid = id++;
                _emitterRegion[eid] = (int)site.Region;
                Field.Add(ThreatField.Make(eid, $"{site.Name} {Label(kind)}", kind, -pos.Y, pos.X));
            }
        }

        // A handful of aerostats, on high ground, guarding the deep regions. These are the
        // answer to the player learning to live in the valleys, so they are deliberately
        // few and deliberately somewhere you can see coming.
        foreach (Region region in WorldMap.Regions)
        {
            if (region.Tier < 3) continue;
            Site? host = WorldMap.Nearest(region.Centre, SiteKind.Overlook);
            if (host is null) continue;
            int aeid = id++;
            _emitterRegion[aeid] = (int)region.Kind;
            Field.Add(ThreatField.Make(aeid, $"{region.Name} aerostat", ThreatKind.Aerostat,
                                       -host.Position.Y, host.Position.X));
        }
    }

    private static ThreatKind PickKind(RandomNumberGenerator rng, int tier, int index) => tier switch
    {
        1 => index == 0 ? ThreatKind.Gun : ThreatKind.Manpads,
        2 => rng.Randf() < 0.45f ? ThreatKind.Manpads : ThreatKind.Gun,
        _ => index switch
        {
            0 => ThreatKind.Sam,
            1 => ThreatKind.Manpads,
            _ => rng.Randf() < 0.5f ? ThreatKind.Gun : ThreatKind.SearchRadar,
        },
    };

    private static string Label(ThreatKind k) => k switch
    {
        ThreatKind.Gun => "gun",
        ThreatKind.Manpads => "MANPADS",
        ThreatKind.Sam => "SAM",
        ThreatKind.SearchRadar => "search radar",
        _ => "aerostat",
    };

    public override void _PhysicsProcess(double delta)
    {
        if (Disabled) return;
        IncomingAge += delta;
        _rwrSilenceTimer = Math.Max(0, _rwrSilenceTimer - delta);

        Vector3 p = _heli.GlobalPosition;
        float agl = _heli.HeightAgl();

        _losTimer -= delta;
        if (_losTimer <= 0)
        {
            _losTimer = LosInterval;
            RecomputeLineOfSight(p);
        }

        Field.Update(
            north: -p.Z, east: p.X,
            altitudeAgl: agl,
            heading: _heli.Sim.State.Orientation.Yaw,
            speed: _heli.Sim.Telemetry.AirspeedTrue,
            dt: delta,
            lineOfSight: e => _losCache.GetValueOrDefault(e.Id));

        // Alert: emitters that see you raise their region's readiness.
        // Real-time dt for detection (calibrated against actual exposure), game-time dt
        // for decay (so a six-hour half-life is six game-hours, not six real-hours).
        foreach (ThreatTrack t in Field.Tracks)
        {
            if (t.LineOfSight && t.Confidence > 0.05 && t.InAltitudeBand
                && _emitterRegion.TryGetValue(t.Emitter.Id, out int rid))
            {
                Alert.Detected(rid, delta);
            }
        }
        Alert.Decay(delta * 12.0);
    }

    /// <summary>
    /// Walk the ray from each emitter to the aircraft and see whether the ground gets in
    /// the way. Only emitters that could plausibly matter are tested - beyond their
    /// detection range the answer cannot change anything.
    /// </summary>
    private void RecomputeLineOfSight(Vector3 aircraft)
    {
        foreach (ThreatTrack t in Field.Tracks)
        {
            ThreatEmitter e = t.Emitter;
            // Emitter position back into Godot coordinates.
            float ex = (float)e.East, ez = (float)-e.North;
            float eGround = WorldHeight.At(ex, ez);
            // Sit the sensor a few metres up, or at the end of its tether.
            float ey = eGround + (e.Kind == ThreatKind.Aerostat ? 620f : 6f);

            float dx = aircraft.X - ex, dz = aircraft.Z - ez;
            float horizontal = Mathf.Sqrt(dx * dx + dz * dz);

            if (horizontal > e.DetectionRange * 1.05f) { _losCache[e.Id] = false; continue; }
            if (horizontal < 1f) { _losCache[e.Id] = true; continue; }

            int steps = Mathf.Clamp(Mathf.CeilToInt(horizontal / LosStep), 4, 220);
            bool clear = true;
            for (int i = 1; i < steps; i++)
            {
                float f = i / (float)steps;
                float x = ex + dx * f, z = ez + dz * f;
                float rayY = Mathf.Lerp(ey, aircraft.Y, f);
                // A small tolerance: a ridge has to actually block, not merely graze.
                if (WorldHeight.At(x, z) > rayY + 2.0f) { clear = false; break; }
            }
            _losCache[e.Id] = clear;
        }
    }

    // ---------------------------------------------------------------- events

    /// <summary>
    /// The payout from D-005a: every emitter that paints you gets written down, with where
    /// it is and what it is. The sortie that nearly killed you is how you find out where it
    /// lives - but only if you had a warning receiver fitted to notice.
    /// </summary>
    private void OnFirstDetection(ThreatTrack t)
    {
        if (_play is null) return;

        bool haveRwr = Field.Fitted.HasFlag(Countermeasure.RadarWarning);
        // A gun and a heat seeker emit nothing. Without a receiver you learn about a radar
        // the same way you learn about them: it shoots at you.
        if (!haveRwr || !t.Emitter.Emits) return;

        string id = $"threat.{t.Emitter.Id}";
        var k = new Knowledge(KnowledgeKind.ThreatSite, id,
            $"{t.Emitter.Name}",
            $"{t.Emitter.Kind}, reaches {t.Emitter.DetectionRange / 1000:F0} km, " +
            $"band {t.Emitter.MinAltitude:F0}-{t.Emitter.MaxAltitude:F0} m");
        if (_play.Progress.Learn(k))
            GD.Print($"[threat] logged {t.Emitter.Name} - RWR had it at {t.Range:F0} m");
    }

    private void OnStruck(ThreatEvent e)
    {
        if (e.Severity <= 0)
        {
            GD.Print($"[threat] {e.Note}");
            return;
        }
        _heli.Sim.Damage.Apply(e.Hit, e.Severity, DamageCause.Gunfire, e.Note);
        _play?.Progress.Journal($"Hit by {e.Name}. {e.Hit} is {_heli.Sim.Damage.Health(e.Hit):P0}.");
        GD.PrintErr($"[threat] HIT: {e.Note} (severity {e.Severity:F2})");
    }

    // ------------------------------------------------------------- interface

    /// <summary>Tracks the warning receiver can actually see. Nothing without the box.</summary>
    public IEnumerable<ThreatTrack> VisibleTracks()
    {
        bool haveRwr = Field.Fitted.HasFlag(Countermeasure.RadarWarning);
        foreach (ThreatTrack t in Field.Tracks)
        {
            if (t.State == TrackState.Idle) continue;
            if (!haveRwr) continue;
            if (!t.Emitter.Emits) continue;     // nothing warns you about a heat seeker
            yield return t;
        }
    }

    public bool DispenseChaff() => Field.DispenseChaff();
    public bool DispenseFlares() => Field.DispenseFlares();

    /// <summary>Fit a countermeasure. Called by the refit system (D-011) on install.</summary>
    public void Fit(Countermeasure c, int rounds = 0)
    {
        Field.Fitted |= c;
        if (c.HasFlag(Countermeasure.Chaff)) Field.ChaffRemaining += rounds;
        if (c.HasFlag(Countermeasure.Flares)) Field.FlaresRemaining += rounds;
    }

    /// <summary>Remove a countermeasure. Called by the refit system (D-011) on removal.</summary>
    public void Unfit(Countermeasure c)
    {
        Field.Fitted &= ~c;
    }
}
