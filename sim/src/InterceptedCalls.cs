using System;
using System.Collections.Generic;
using System.Linq;

namespace Rotorwash.Sim;

/// <summary>
/// Overheard hostile radio traffic when the player is inside a threat envelope
/// and being tracked (story.md §4.3, carrier type 3).
///
/// "You overhear traffic not meant for you — Sparrow's crew, the Ashmount tether
/// net — when you are inside a threat envelope and being tracked. Overheard traffic
/// is a reward for being somewhere dangerous, which pairs with D-005a's rule that
/// the bad sortie must pay out."
///
/// The player never hears these unless something is actively tracking them. Each
/// line fires once per session (repeats feel scripted, not overheard). The used set
/// persists through save/load. A cooldown prevents message spam when multiple
/// emitters are tracking simultaneously.
///
/// Pure .NET, no Godot dependency. The game layer checks threat state each frame
/// and calls <see cref="TryFire"/> when any emitter is tracking.
/// </summary>
public sealed class InterceptedCalls
{
    /// <summary>Minimum seconds between intercepted messages.</summary>
    public const double CooldownSeconds = 90.0;

    private readonly HashSet<int> _used = new();
    private double _cooldown;

    /// <summary>Number of lines that have been delivered this session.</summary>
    public int UsedCount => _used.Count;

    /// <summary>
    /// Try to deliver an intercepted call. Returns a message if tracking is active,
    /// the cooldown has expired, and there are unheard lines remaining for the
    /// tracking threat kind. Returns null otherwise.
    ///
    /// <paramref name="trackingKind"/> is the <see cref="ThreatKind"/> of the emitter
    /// that is currently tracking. When multiple emitters track simultaneously, pass
    /// the highest-confidence one — the voice you overhear is the one closest to
    /// shooting.
    /// </summary>
    public RadioMessage? TryFire(ThreatKind trackingKind)
    {
        if (_cooldown > 0) return null;

        // Find an unused line for this threat kind.
        if (!Corpus.TryGetValue(trackingKind, out var lines)) return null;

        for (int i = 0; i < lines.Length; i++)
        {
            int key = CorpusKey(trackingKind, i);
            if (_used.Contains(key)) continue;

            _used.Add(key);
            _cooldown = CooldownSeconds;
            var (speaker, text) = lines[i];
            return new RadioMessage(speaker, text, RadioMessageKind.Intercepted);
        }

        return null; // all lines for this kind exhausted
    }

    /// <summary>Advance the cooldown timer. Call every frame with delta time.</summary>
    public void Update(double dt)
    {
        if (_cooldown > 0) _cooldown -= dt;
    }

    // ------------------------------------------------------------------ save/load

    public int[] Save() => _used.OrderBy(x => x).ToArray();

    public void Restore(int[]? data)
    {
        _used.Clear();
        _cooldown = 0;
        if (data is null) return;
        foreach (int k in data) _used.Add(k);
    }

    // ------------------------------------------------------------------ corpus key

    /// <summary>Encode (kind, index) into a single int for the used set.</summary>
    private static int CorpusKey(ThreatKind kind, int index) => (int)kind * 100 + index;

    // ------------------------------------------------------------------ content
    //
    // Hostile radio chatter, by threat kind. Each entry is a short coordination
    // call between operators — terse, procedural, never addressing the player.
    // These are overheard, not directed. The speaker is the operator's designation,
    // not a name. story.md §9: no instructions, no chosen-one language.
    //
    // Content tiers by threat kind:
    //   SearchRadar  — lookouts reporting contacts, bearing calls
    //   Gun          — gun pit coordination, fire commands
    //   Sam          — missile battery procedure, track/lock calls
    //   Manpads      — shoulder teams, short and tense
    //   Aerostat     — tether net operators, altitude reports

    public static readonly Dictionary<ThreatKind, (string Speaker, string Text)[]> Corpus = new()
    {
        [ThreatKind.SearchRadar] = new[]
        {
            ("SEARCH",
                "Contact bearing two-seven-zero, range six. Single rotary, low and slow."),
            ("SEARCH",
                "Same contact. Descending. Looks like he is using the valley."),
            ("SEARCH",
                "Track fading. Terrain shadow. Last position south of the ridge."),
            ("SEARCH",
                "New paint. Bearing zero-four-five, range four. Not squawking anything."),
            ("SEARCH",
                "Confirmed single aircraft. Same signature as last time."),
            ("SEARCH",
                "Lost him behind the hill. Either landed or dropped below the mast."),
        },

        [ThreatKind.Gun] = new[]
        {
            ("GUN PIT",
                "All stations, target inbound, low level. Hold fire until he commits."),
            ("GUN PIT",
                "Stand by. He is circling. Wait for the straight run."),
            ("GUN PIT",
                "Gunner three, track is left to right, five hundred metres. Lead him."),
            ("GUN PIT",
                "He is in the weeds. Short bursts, watch the traverse."),
            ("GUN PIT",
                "Same aircraft. Same approach. He has been here before."),
        },

        [ThreatKind.Sam] = new[]
        {
            ("BATTERY",
                "Track. Single rotary, bearing one-eight-zero, altitude two hundred."),
            ("BATTERY",
                "Tracking. Signal is good. Ready to engage on command."),
            ("BATTERY",
                "He popped chaff last time. Wait for him to run out."),
            ("BATTERY",
                "Contact is manoeuvring. Trying to get below the beam."),
            ("BATTERY",
                "Lock. Steady track. He does not know we have him."),
            ("BATTERY",
                "Contact broke lock. Terrain masking. Stand by to reacquire."),
        },

        [ThreatKind.Manpads] = new[]
        {
            ("TEAM",
                "I can hear him. West, below the ridge. Not yet."),
            ("TEAM",
                "Visual. Rotor wash in the trees. He is close."),
            ("TEAM",
                "Do not fire until he turns. I want a tail shot."),
            ("TEAM",
                "Moving to better ground. He keeps coming in low."),
            ("TEAM",
                "Flares last time. Two, maybe three. He has a dispenser."),
        },

        [ThreatKind.Aerostat] = new[]
        {
            ("TETHER NET",
                "The bag has him. Altitude one-fifty, heading north."),
            ("TETHER NET",
                "He dropped below the others but we still see him. That is the point."),
            ("TETHER NET",
                "Same aircraft. Fourth time this week. He is mapping us."),
            ("TETHER NET",
                "Contact is hugging the terrain. Altitude sixty metres. We have him anyway."),
            ("TETHER NET",
                "Cable tension is good. Wind is ten knots. The bag is stable."),
        },
    };

    /// <summary>Total number of authored lines across all threat kinds.</summary>
    public static int TotalLines
    {
        get
        {
            int n = 0;
            foreach (var lines in Corpus.Values) n += lines.Length;
            return n;
        }
    }
}
