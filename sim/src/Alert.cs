using System;
using System.Collections.Generic;

namespace Rotorwash.Sim;

/// <summary>
/// How ready a piece of country is for you, and how long it stays that way.
///
/// The threat system had no memory. Every emitter woke up the instant you came into range
/// and forgot you completely the moment you left, so the tenth flight through a valley cost
/// exactly what the first did. That is a strange thing for a world whose entire premise
/// (D-008, D-010) is that **there is only one aircraft** and everyone who matters is
/// defending against it. If you are the only helicopter in the world, being seen is
/// information, and information should travel and persist.
///
/// So: being detected raises a region's readiness; readiness makes the next visit harder;
/// and it bleeds away over hours if you stay out. That turns a route into a decision with a
/// cost that outlives the sortie — the direct way home is the way you came, which is the
/// way they are now watching.
///
/// Three properties this is built to have:
///
///   * **It decays in hours, not minutes.** Fast decay makes it a nuisance you wait out in
///     a hover; slow decay makes it a reason to plan a different way home tomorrow. The
///     whole point is to reach across sorties.
///   * **It is local.** Stirring up one region must not arm the world, or the player is
///     punished globally for a single mistake and stops flying anywhere.
///   * **It saturates.** A region that has seen you once behaves differently from one that
///     has never seen you; a region that has seen you twenty times should not be
///     twenty times worse than one that has seen you five. Past a point they are simply
///     awake, and more evidence changes nothing.
/// </summary>
public sealed class AlertState
{
    /// <summary>
    /// How long readiness takes to fall by half, in seconds of game time.
    ///
    /// Six hours. Short enough that a region you avoided for a day is genuinely calmer;
    /// long enough that you cannot launder a bad crossing by orbiting out of range for ten
    /// minutes. This is the number that decides whether the system is strategy or tedium,
    /// and it is the first thing to revisit if it turns out to be either.
    /// </summary>
    public double HalfLifeSeconds { get; set; } = 6 * 3600.0;

    /// <summary>
    /// Raised per second while an emitter has eyes on you.
    ///
    /// Measured, not guessed: at 0.020 a single two-minute overflight took a region to
    /// 0.91 - effectively fully awake off one pass, after which the scale had nowhere left
    /// to go and every subsequent flight was identical. At 0.003 the same pass lands near
    /// 0.30, so one crossing is noticed, several make a place genuinely hostile, and the
    /// difference between them is legible.
    /// </summary>
    public double RisePerSecondDetected { get; set; } = 0.003;

    /// <summary>Raised once when a shot is fired at you. Being engaged is loud.</summary>
    public double RiseOnEngagement { get; set; } = 0.12;

    private readonly Dictionary<int, double> _level = new();

    /// <summary>Readiness of one region, 0 (nobody is expecting you) to 1 (fully awake).</summary>
    public double Level(int regionId) => _level.TryGetValue(regionId, out double v) ? v : 0.0;

    /// <summary>Every region that is above a threshold, for a map overlay or a report.</summary>
    public IEnumerable<KeyValuePair<int, double>> Raised(double above = 0.05)
    {
        foreach (KeyValuePair<int, double> kv in _level)
            if (kv.Value > above) yield return kv;
    }

    /// <summary>Somebody has eyes on you, in this region, this tick.</summary>
    public void Detected(int regionId, double dt)
        => Add(regionId, RisePerSecondDetected * dt);

    /// <summary>Somebody shot at you.</summary>
    public void Engaged(int regionId) => Add(regionId, RiseOnEngagement);

    private void Add(int regionId, double amount)
    {
        double now = Level(regionId);

        // Saturating rather than linear: the same evidence is worth progressively less as a
        // region wakes up. Adding a fixed amount instead lets a long transit pin every
        // region to 1.0, after which the system has no dynamic range left and stops saying
        // anything at all.
        _level[regionId] = Math.Clamp(now + amount * (1.0 - now), 0.0, 1.0);
    }

    /// <summary>Let the world calm down. Call once per step with game seconds.</summary>
    public void Decay(double dt)
    {
        if (dt <= 0 || _level.Count == 0) return;
        double k = Math.Pow(0.5, dt / Math.Max(HalfLifeSeconds, 1e-6));

        // Iterate a copy of the keys: the dictionary is written inside the loop.
        var keys = new List<int>(_level.Keys);
        foreach (int id in keys)
        {
            double v = _level[id] * k;
            if (v < 1e-4) _level.Remove(id); else _level[id] = v;
        }
    }

    /// <summary>Forget everything. For a new game, not for a new sortie.</summary>
    public void Clear() => _level.Clear();

    // ----------------------------------------------------------------- effects

    /// <summary>
    /// How much further out an emitter looks when the region is expecting you.
    ///
    /// Modest on purpose. Detection range is level design - the envelopes in
    /// <see cref="ThreatField.Make"/> are tuned so that particular country is crossable at
    /// particular altitudes - and letting alert stretch them far would quietly redraw the
    /// map the player has learned. A quarter again at full readiness is enough to feel and
    /// too little to invalidate a route that was sound.
    /// </summary>
    public double DetectionScale(int regionId) => 1.0 + 0.25 * Level(regionId);

    /// <summary>
    /// How much faster they get from "something is there" to "locked".
    ///
    /// This is the part that should actually hurt, because it attacks the thing the player
    /// relies on: the few seconds of masking behind a ridge that used to be enough. At full
    /// readiness they are half as slow to make up their minds.
    /// </summary>
    public double ReactionScale(int regionId) => 1.0 - 0.50 * Level(regionId);

    /// <summary>A phrase for the kneeboard. Facts, not advice (D-005a).</summary>
    public static string Describe(double level) => level switch
    {
        < 0.05 => "quiet",
        < 0.25 => "someone saw you",
        < 0.55 => "watching",
        < 0.85 => "expecting you",
        _ => "waiting for you",
    };
}
