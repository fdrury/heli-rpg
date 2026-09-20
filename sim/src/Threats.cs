namespace Rotorwash.Sim;

public enum ThreatKind
{
    /// <summary>Sees a long way, shoots at nothing, and tells everything else where you are.</summary>
    SearchRadar,
    /// <summary>Short range, low altitude, no warning, and it does not care about chaff.</summary>
    Gun,
    /// <summary>Radar guided. Long reach, defeated by chaff and by not being visible.</summary>
    Sam,
    /// <summary>Heat seeking. Short reach, no radar warning at all, defeated by flares and by being cold.</summary>
    Manpads,
    /// <summary>A tethered balloon that looks DOWN into the dead ground everything else misses.</summary>
    Aerostat,
}

/// <summary>What the player can do about a threat. Every one is a physical box on the aircraft.</summary>
[Flags]
public enum Countermeasure
{
    None = 0,
    Chaff = 1 << 0,
    Flares = 1 << 1,
    ExhaustSuppressor = 1 << 2,
    RadarWarning = 1 << 3,
    Jammer = 1 << 4,
}

/// <summary>One emplacement. Static data; the live state is in <see cref="ThreatTrack"/>.</summary>
public sealed record ThreatEmitter(
    int Id,
    string Name,
    ThreatKind Kind,
    double North,
    double East,
    double DetectionRange,
    double EngagementRange,
    double MinAltitude,
    double MaxAltitude)
{
    /// <summary>Does this threat guide on radar? Decides whether chaff or flares is the answer.</summary>
    public bool RadarGuided => Kind is ThreatKind.Sam or ThreatKind.SearchRadar or ThreatKind.Aerostat;

    /// <summary>Does it emit, and therefore show up on a warning receiver at all?</summary>
    public bool Emits => Kind is not (ThreatKind.Gun or ThreatKind.Manpads);
}

public enum TrackState { Idle, Searching, Tracking, Locked, Engaging, Reloading }

/// <summary>Live state of one emitter's interest in the player.</summary>
public sealed class ThreatTrack
{
    public ThreatEmitter Emitter = null!;
    public TrackState State = TrackState.Idle;

    /// <summary>0 to 1. Above 1 it has a track; the rate depends on exposure.</summary>
    public double Confidence;

    public double Range;
    public double BearingFromAircraft;   // radians, 0 = ahead
    public bool LineOfSight;
    public bool InAltitudeBand;
    public double TimeInState;
    public double CooldownRemaining;

    /// <summary>True once the player has been painted by this emitter at least once.</summary>
    public bool EverDetected;

    /// <summary>Emitter health, 1.0 = operational, 0 = destroyed by gunfire.</summary>
    public double EmitterHealth = 1.0;

    /// <summary>True when the emitter has been destroyed and should stop tracking.</summary>
    public bool Destroyed => EmitterHealth <= 0;
}

/// <summary>What happened to the aircraft this step.</summary>
public readonly record struct ThreatEvent(int EmitterId, string Name, ThreatKind Kind,
                                          Component Hit, double Severity, string Note);

/// <summary>
/// The threat field: who can see you, who is shooting, and what to do about it.
///
/// This is the world-scale system, not just a combat one. The benchmark pass (D-003b) found
/// that effective world size is physical size multiplied by (flown path / straight-line
/// path), so a six-kilometre leg that has to become an eighteen-kilometre masked dogleg is
/// a threefold multiplier on the entire map, bought with a data file. Terrain masking is
/// therefore the point: the question is never "can I outrun it" but "is there a valley".
///
/// Three rules it obeys, all from the benchmark:
///
///   GRADED, NOT LETHAL (D-005a). A hit takes a system and leaves an autorotation. Total
///   unearned punishment reads as irritation rather than as a gate.
///
///   ALTITUDE IS THE MAP (D-003b). Each class owns a band. Guns own the deck, MANPADS own
///   low level, SAMs own the middle, and the aerostat owns the one thing nothing else can
///   do - looking down into the dead ground. There is no "climb above it" escape, because
///   the aircraft's measured hover ceiling is 3000 m.
///
///   THE BAD SORTIE PAYS (D-005a). Every emitter that paints you is logged, with a bearing
///   and a signature. The flight that nearly killed you is how you find out where it lives.
/// </summary>
public sealed class ThreatField
{
    private readonly List<ThreatTrack> _tracks = new();

    public IReadOnlyList<ThreatTrack> Tracks => _tracks;

    /// <summary>What is fitted to the aircraft right now.</summary>
    public Countermeasure Fitted { get; set; } = Countermeasure.None;

    public int ChaffRemaining { get; set; }
    public int FlaresRemaining { get; set; }

    /// <summary>
    /// Regional readiness. When set, detection range stretches and reaction time shrinks
    /// in regions that have seen the aircraft before. The mapping from emitter ID to region
    /// ID is supplied by the game layer via <see cref="EmitterRegion"/>.
    /// </summary>
    public AlertState? Alert { get; set; }

    /// <summary>Maps an emitter ID to its region ID, for alert lookups.</summary>
    public Func<int, int>? EmitterRegion { get; set; }

    /// <summary>Raised when something actually hits.</summary>
    public event Action<ThreatEvent>? Struck;

    /// <summary>Raised the first time each emitter paints the aircraft. This is the payout.</summary>
    public event Action<ThreatTrack>? FirstDetection;

    /// <summary>Raised when a shot is launched, so the game can warn and the player can react.</summary>
    public event Action<ThreatTrack>? Launch;

    private readonly Random _rng;
    private double _chaffTimer, _flareTimer;

    /// <param name="seed">Shot resolution is stochastic; the seed makes a trial repeatable.</param>
    public ThreatField(int seed = 77000) { _rng = new Random(seed); }

    public void Add(ThreatEmitter e) => _tracks.Add(new ThreatTrack { Emitter = e });
    public void Clear() => _tracks.Clear();

    /// <summary>Highest confidence anything currently has on the aircraft, 0 to 1.</summary>
    public double Exposure
    {
        get { double m = 0; foreach (var t in _tracks) m = Math.Max(m, Math.Min(t.Confidence, 1)); return m; }
    }

    /// <summary>True while something has a weapon in the air. The moment to react.</summary>
    public bool AnyEngaging
    {
        get { foreach (var t in _tracks) if (t.State == TrackState.Engaging) return true; return false; }
    }

    /// <summary>
    /// Can the crew perceive this track at all?
    ///
    /// This is what a radar warning receiver is FOR, and without it the box does nothing
    /// mechanically - measured: a fit of flares plus RWR took exactly the same damage
    /// through a gauntlet as flares alone, to the hundredth. An RWR does not make a missile
    /// miss; it tells you one is coming, and everything the pilot does next depends on
    /// knowing.
    ///
    /// A gun announces itself - tracer, noise, and it is close. A radar lock is silent
    /// without a receiver. A MANPADS is silent either way, which is the entire reason it is
    /// the frightening one.
    /// </summary>
    public bool Perceivable(ThreatTrack t) => t.Emitter.Kind switch
    {
        ThreatKind.Gun => true,
        ThreatKind.Manpads => false,
        _ => Fitted.HasFlag(Countermeasure.RadarWarning),
    };

    /// <summary>
    /// Locks the crew actually knows about. Anything reacting to a threat - the player, the
    /// HUD, an autopilot - must use this rather than <see cref="AnyLocked"/>, which is
    /// omniscient and only honest for a debug overlay.
    /// </summary>
    public bool AnyLockedKnown
    {
        get
        {
            foreach (ThreatTrack t in _tracks)
                if (Perceivable(t) && (t.State == TrackState.Locked || t.State == TrackState.Engaging))
                    return true;
            return false;
        }
    }

    public bool AnyLocked
    {
        get { foreach (var t in _tracks) if (t.State is TrackState.Locked or TrackState.Engaging) return true; return false; }
    }

    /// <summary>Fire chaff. Only defeats radar-guided threats, and only for a few seconds.</summary>
    public bool DispenseChaff()
    {
        if (!Fitted.HasFlag(Countermeasure.Chaff) || ChaffRemaining <= 0) return false;
        ChaffRemaining--;
        _chaffTimer = 5.0;
        return true;
    }

    /// <summary>Fire flares. Only defeats heat seekers.</summary>
    public bool DispenseFlares()
    {
        if (!Fitted.HasFlag(Countermeasure.Flares) || FlaresRemaining <= 0) return false;
        FlaresRemaining--;
        // A flare burns for several seconds, and it has to outlast the weapon's
        // time of flight or it is decoration. An earlier 3.5 s expired 0.07 s before a
        // MANPADS shot arrived, which made flares useless and looked like a balance issue.
        _flareTimer = 6.0;
        return true;
    }

    public bool ChaffActive => _chaffTimer > 0;
    public bool FlaresActive => _flareTimer > 0;

    /// <summary>
    /// Advance the whole field.
    /// </summary>
    /// <param name="north">Aircraft position, world NED.</param>
    /// <param name="east">Aircraft position, world NED.</param>
    /// <param name="altitudeAgl">Height above the ground beneath the aircraft, m.</param>
    /// <param name="heading">Aircraft heading, rad.</param>
    /// <param name="speed">True airspeed, m/s.</param>
    /// <param name="lineOfSight">Called with an emitter; returns whether terrain leaves it
    /// a clear view of the aircraft. Supplied by the world, because only the world knows
    /// where the hills are.</param>
    public void Update(double north, double east, double altitudeAgl, double heading, double speed,
                       double dt, Func<ThreatEmitter, bool> lineOfSight)
    {
        _chaffTimer = Math.Max(0, _chaffTimer - dt);
        _flareTimer = Math.Max(0, _flareTimer - dt);

        foreach (ThreatTrack t in _tracks)
        {
            // D-081: a destroyed emitter is wreckage — it stops scanning, tracking,
            // and engaging. The track state freezes at Idle so the RWR stops showing it.
            if (t.Destroyed)
            {
                t.State = TrackState.Idle;
                t.Confidence = 0;
                continue;
            }

            ThreatEmitter e = t.Emitter;
            t.TimeInState += dt;
            t.CooldownRemaining = Math.Max(0, t.CooldownRemaining - dt);

            double dn = north - e.North, de = east - e.East;
            t.Range = Math.Sqrt(dn * dn + de * de);
            t.BearingFromAircraft = Airfoil.WrapPi(Math.Atan2(-de, -dn) - heading);

            // Alert stretches detection range modestly — enough to feel, not enough to
            // redraw routes the player has already learned. See AlertState.DetectionScale.
            double detScale = 1.0;
            double reactScale = 1.0;
            if (Alert is not null && EmitterRegion is not null)
            {
                int rid = EmitterRegion(e.Id);
                detScale = Alert.DetectionScale(rid);
                reactScale = Alert.ReactionScale(rid);
            }

            bool inRange = t.Range < e.DetectionRange * detScale;
            t.InAltitudeBand = altitudeAgl >= e.MinAltitude && altitudeAgl <= e.MaxAltitude;
            t.LineOfSight = inRange && lineOfSight(e);

            // --- How fast does it build a track? ------------------------------
            double gain = 0;
            if (inRange && t.LineOfSight && t.InAltitudeBand)
            {
                // Closer is faster, and the last quarter of the range is where it gets
                // dangerous rather than merely uncomfortable.
                double closeness = 1.0 - t.Range / Math.Max(e.DetectionRange, 1);
                gain = 0.22 + closeness * closeness * 0.85;

                // Ground clutter: a radar looking level at something a few metres up has a
                // much harder time. This is why low flying works, and why the aerostat -
                // which looks down - is the threat that breaks the habit.
                if (e.RadarGuided && e.Kind != ThreatKind.Aerostat)
                    gain *= 0.25 + Math.Clamp(altitudeAgl / 260.0, 0, 1) * 0.75;

                // Heat seekers care about the exhaust, not the altitude.
                if (e.Kind == ThreatKind.Manpads && Fitted.HasFlag(Countermeasure.ExhaustSuppressor))
                    gain *= 0.35;

                // A fast, small-aspect target is harder to settle on. Normalised against
                // what this aircraft can actually do (Vne is about 60 m/s), not against an
                // arbitrary number - an earlier version saturated at 42 m/s, below cruise,
                // so speed made no difference anywhere in the usable range.
                gain *= 1.0 - Math.Clamp(speed / 62.0, 0, 1.0) * 0.34;

                if (Fitted.HasFlag(Countermeasure.Jammer) && e.RadarGuided) gain *= 0.45;
                if (_chaffTimer > 0 && e.RadarGuided) gain *= 0.15;
                if (_flareTimer > 0 && e.Kind == ThreatKind.Manpads) gain *= 0.15;
            }
            else
            {
                // Break line of sight and the track decays - but not instantly. Somebody is
                // still looking at where you were.
                gain = -0.42;
            }

            t.Confidence = Math.Clamp(t.Confidence + gain * dt, 0, 1.35);

            if (t.Confidence > 0.05 && !t.EverDetected && t.LineOfSight)
            {
                t.EverDetected = true;
                FirstDetection?.Invoke(t);
            }

            // --- State machine ------------------------------------------------
            // Engaging and Reloading are NOT derived from confidence - they are things the
            // emitter is doing, and they have to survive being recomputed. Deriving every
            // state from confidence each frame silently overwrote Engaging the instant it
            // was set, so nothing in the world ever actually fired a shot.
            TrackState was = t.State;
            if (t.CooldownRemaining > 0)
            {
                t.State = TrackState.Reloading;
            }
            else if (t.State != TrackState.Engaging)
            {
                t.State = t.Confidence switch
                {
                    < 0.04 => TrackState.Idle,
                    < 0.45 => TrackState.Searching,
                    < 1.0 => TrackState.Tracking,
                    _ => TrackState.Locked,
                };
            }
            if (t.State != was) t.TimeInState = 0;

            // --- Engagement ---------------------------------------------------
            if (t.State == TrackState.Locked
                && e.Kind != ThreatKind.SearchRadar
                && e.Kind != ThreatKind.Aerostat
                && t.Range < e.EngagementRange
                && t.TimeInState > LaunchDelay(e.Kind) * reactScale)
            {
                t.State = TrackState.Engaging;
                t.TimeInState = 0;
                Launch?.Invoke(t);
            }

            if (t.State == TrackState.Engaging && t.TimeInState > FlightTime(e.Kind, t.Range))
            {
                Resolve(t, altitudeAgl);
                t.CooldownRemaining = ReloadTime(e.Kind);
                t.Confidence *= 0.5;
                t.State = TrackState.Reloading;
                t.TimeInState = 0;
            }
        }
    }

    private static double LaunchDelay(ThreatKind k) => k switch
    {
        ThreatKind.Gun => 0.6,
        ThreatKind.Manpads => 2.2,
        ThreatKind.Sam => 3.0,
        _ => 99,
    };

    private static double FlightTime(ThreatKind k, double range) => k switch
    {
        ThreatKind.Gun => 0.4,
        ThreatKind.Manpads => Math.Clamp(range / 420.0, 0.8, 8),
        ThreatKind.Sam => Math.Clamp(range / 600.0, 1.5, 14),
        _ => 99,
    };

    private static double ReloadTime(ThreatKind k) => k switch
    {
        ThreatKind.Gun => 3.5,
        ThreatKind.Manpads => 26.0,
        ThreatKind.Sam => 18.0,
        _ => 30,
    };

    /// <summary>
    /// Decide what a shot actually does. Never an instant kill: it takes a system and
    /// leaves the pilot an autorotation and a decision, which is the whole difference
    /// between a gate and a punishment.
    /// </summary>
    private void Resolve(ThreatTrack t, double altitudeAgl)
    {
        ThreatEmitter e = t.Emitter;

        // Last chance for countermeasures - a flare fired as it comes off the rail works.
        if (_chaffTimer > 0 && e.RadarGuided) { Miss(t, "chaff"); return; }
        if (_flareTimer > 0 && e.Kind == ThreatKind.Manpads) { Miss(t, "flares"); return; }

        double pk = e.Kind switch
        {
            ThreatKind.Gun => 0.45,
            ThreatKind.Manpads => 0.62,
            ThreatKind.Sam => 0.70,
            _ => 0,
        };
        // Manoeuvring low and fast helps, as it should.
        pk *= 1.0 - Math.Clamp((260 - altitudeAgl) / 260.0, 0, 1) * 0.30;

        if (_rng.NextDouble() > pk) { Miss(t, "it went wide"); return; }

        // Where it hits. Heat seekers chase the exhaust, which is where the engine is.
        (Component part, double severity) = e.Kind switch
        {
            ThreatKind.Manpads => (Component.Engine, 0.55 + _rng.NextDouble() * 0.35),
            ThreatKind.Sam => PickSamHit(),
            _ => PickGunHit(),
        };

        Struck?.Invoke(new ThreatEvent(e.Id, e.Name, e.Kind, part, severity,
            $"{e.Name} put a round through the {part}"));
    }

    private (Component, double) PickSamHit()
    {
        double r = _rng.NextDouble();
        if (r < 0.30) return (Component.TailRotor, 0.6 + _rng.NextDouble() * 0.4);
        if (r < 0.55) return (Component.Engine, 0.5 + _rng.NextDouble() * 0.4);
        if (r < 0.75) return (Component.Transmission, 0.35 + _rng.NextDouble() * 0.3);
        if (r < 0.90) return (Component.Hydraulics, 0.5 + _rng.NextDouble() * 0.5);
        return (Component.MainRotor, 0.3 + _rng.NextDouble() * 0.3);
    }

    private (Component, double) PickGunHit()
    {
        double r = _rng.NextDouble();
        if (r < 0.32) return (Component.Fuselage, 0.15 + _rng.NextDouble() * 0.25);
        if (r < 0.52) return (Component.FuelSystem, 0.25 + _rng.NextDouble() * 0.4);
        if (r < 0.68) return (Component.Hydraulics, 0.2 + _rng.NextDouble() * 0.35);
        if (r < 0.82) return (Component.Engine, 0.2 + _rng.NextDouble() * 0.3);
        if (r < 0.93) return (Component.TailRotor, 0.25 + _rng.NextDouble() * 0.35);
        return (Component.Avionics, 0.4 + _rng.NextDouble() * 0.5);
    }

    private void Miss(ThreatTrack t, string why)
        => Struck?.Invoke(new ThreatEvent(t.Emitter.Id, t.Emitter.Name, t.Emitter.Kind,
                                          Component.Fuselage, 0, $"defeated: {why}"));

    // ------------------------------------------------------------------ setup

    /// <summary>Standard envelopes. Altitude bands are AGL, and they are the level design.</summary>
    public static ThreatEmitter Make(int id, string name, ThreatKind kind, double north, double east)
        => kind switch
        {
            // Owns the deck. Terrifying inside 1200 m, irrelevant outside it.
            ThreatKind.Gun => new ThreatEmitter(id, name, kind, north, east, 1600, 1200, 0, 900),
            // Owns low level. No radar warning at all, which is what makes it frightening.
            ThreatKind.Manpads => new ThreatEmitter(id, name, kind, north, east, 3800, 3200, 30, 3000),
            // Owns the middle. The reason to stay low, and the reason chaff exists.
            ThreatKind.Sam => new ThreatEmitter(id, name, kind, north, east, 14000, 11000, 120, 6000),
            // Shoots at nothing, but hands you to everything else.
            ThreatKind.SearchRadar => new ThreatEmitter(id, name, kind, north, east, 22000, 0, 60, 8000),
            // Looks DOWN. The one thing that sees into the dead ground you have been using.
            ThreatKind.Aerostat => new ThreatEmitter(id, name, kind, north, east, 17000, 0, 0, 8000),
            _ => new ThreatEmitter(id, name, kind, north, east, 2000, 1500, 0, 1500),
        };
}
