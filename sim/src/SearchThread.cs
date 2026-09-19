namespace Rotorwash.Sim;

/// <summary>
/// The main quest: you are looking for someone.
///
/// Subnautica's radio messages are the model (D-048): an occasional signal, a bearing, a
/// name, a clue that points the player to the next place. 12 authored beats, spaced
/// across the game by gating on world state (visited count, clock, knowledge).
///
/// The search is the story. Even this minimal version — a name, a frequency, a bearing,
/// a clue at each stop — is enough to carry 20 hours (D-048). Without it, the helicopter
/// is a toy with nowhere important to go.
///
/// The target is Sera Wray, a flight engineer who signed Hugh's logbook every twenty-five
/// hours for nine years. She was riding out on a fixed-wing freighter — callsign
/// SIERRA-FOUR-THREE — when the collapse came. You have her name, a partial callsign you
/// have been mistaking for a frequency, and a four-leg ferry route she never finished.
/// She is alive, four hours' flight away, and has been for six years. She has what you
/// need: the location of the last serviceable rotor blades.
///
/// D-008: no other helicopter pilots exist. Wray is a flight engineer, not a pilot.
/// D-050: corrected from "Kara Morrow, a pilot" which violated D-008.
///
/// NOTE: The gates here are counter-based (VisitedCount, CountKnown) as a floor to
/// prevent beats from stacking. story.md specifies place-based triggers (talk to Mattie,
/// visit the Fenmoor airfield, search the Drowning wreck) which require site-role
/// resolution — mapping story roles to generated site IDs. That system does not exist
/// yet; when it does, these gates should be replaced with the predicates in story.md §2.
/// </summary>
public sealed class SearchThread
{
    /// <summary>How far the search has progressed. Persisted through save/load.</summary>
    public int Stage { get; set; }

    /// <summary>True when all beats have triggered.</summary>
    public bool Complete => Stage >= Beats.Length;

    /// <summary>The current beat's hint text, for the kneeboard.</summary>
    public string CurrentHint =>
        Stage < Beats.Length ? Beats[Stage].Hint : "New blades. Hugh flies true.";

    /// <summary>The most recent clue learned, for the journal.</summary>
    public string LastClue =>
        Stage > 0 && Stage <= Beats.Length ? Beats[Stage - 1].Journal : "";

    /// <summary>
    /// Check whether the next beat should trigger, given current progress.
    /// Returns the beat if it fires, null otherwise.
    /// </summary>
    public SearchBeat? TryAdvance(Progress progress)
    {
        if (Stage >= Beats.Length) return null;

        SearchBeat beat = Beats[Stage];
        if (!beat.Gate(progress)) return null;

        Stage++;
        return beat;
    }

    // ---------------------------------------------------------------- beats

    /// <summary>
    /// The 12 authored beats of the main search, following docs/wiki/story.md.
    ///
    /// Each beat has:
    /// - A gate: what the player must have done for this to trigger.
    /// - A journal entry: what the player learns.
    /// - A hint: what to do next, shown on the kneeboard.
    /// - Knowledge: what to add to the Known list.
    ///
    /// Three acts: Act I (The Pan, Long Acre) corrects a misconception and teaches the
    /// verb. Act II (Fenmoor, The Drowning, Cold Shoulder, Sawtooth Works) follows the
    /// ferry route leg by leg. Act III (Ashmount, The Scald) finds her and makes the
    /// last flight.
    /// </summary>
    public static readonly SearchBeat[] Beats =
    {
        // ACT I — "So you are real." (The Pan, Long Acre · tier 0)
        // Target: 90 minutes. No threats. Learn to land, talk, tune a mast.

        new(
            "The correction",
            gate: p => p.VisitedCount >= 2,
            journal: "Mattie looked at the logbook. \"That is not a frequency. That is the back half " +
                     "of a callsign. Sierra four three. You have been listening for a number for six " +
                     "years and it was a name all along.\"",
            hint: "SIERRA-FOUR-THREE. A callsign, not a frequency. Tune every mast.",
            knowledgeId: "search.callsign", knowledgeLabel: "SIERRA-FOUR-THREE",
            knowledgeDetail: "The partial frequency was a callsign. Fixed-wing freighter. Tune masts to narrow the band."),

        new(
            "The band",
            gate: p => p.VisitedCount >= 4 && p.CountKnown(KnowledgeKind.Frequency) >= 1,
            journal: "Every relay logged is one candidate eliminated. Airband, 118 to 152 MHz. " +
                     "Thirty-four paired channels. The mast at Long Acre is next.",
            hint: "Airband: 34 candidates. Every relay narrows it. Keep tuning.",
            knowledgeId: "search.band", knowledgeLabel: "The airband search",
            knowledgeDetail: "118-152 MHz, thirty-four paired channels. Every mast tuned eliminates one."),

        new(
            "The broadcast",
            gate: p => p.VisitedCount >= 6 && p.CountKnown(KnowledgeKind.Frequency) >= 2,
            journal: "A man at Long Acre has listened to the same carrier at 06:40 every morning " +
                     "for six years. A weather sequence, read by a rota of four people. He can tell " +
                     "them apart. One of them, he says, \"says the wind speeds in knots. Nobody says knots.\"",
            hint: "A daily broadcast at 06:40. Four readers. One says knots.",
            knowledgeId: "search.rota", knowledgeLabel: "The 06:40 broadcast",
            knowledgeDetail: "A weather sequence, four readers, one says knots. Nobody says knots."),

        // ACT II — "The route." (Fenmoor, The Drowning, Cold Shoulder, Sawtooth · tiers 1-2)
        // Target: 10-14 hours. Threats begin. Follow the ferry route leg by leg.

        new(
            "The manifest",
            gate: p => p.VisitedCount >= 10 && p.CountKnown(KnowledgeKind.Chart) >= 2,
            journal: "A woman at Fenmoor kept the load manifest because her brother's name is on it. " +
                     "Tail number, routing, and the fact that the aircraft was 420 kg over gross. " +
                     "That is why it did not make the uplands.",
            hint: "The ferry was overloaded. It never reached Cold Shoulder. Leg 1 closed.",
            knowledgeId: "search.manifest", knowledgeLabel: "The load manifest",
            knowledgeDetail: "Fixed-wing freighter, 420 kg over gross. Did not make the uplands. Leg 1 of 4."),

        new(
            "The wreck",
            gate: p => p.VisitedCount >= 14 && p.CountKnown(KnowledgeKind.ThreatSite) >= 1,
            journal: "A wreck in the wetlands, half in the water, tail boom up. The tail number " +
                     "matches. The cabin is empty. The liferaft cradle is empty and the strap was " +
                     "cut clean — the door was opened from the inside.",
            hint: "The aircraft is found. No bodies. Raft gone, door opened from inside.",
            knowledgeId: "search.wreck", knowledgeLabel: "SIERRA-FOUR-THREE found",
            knowledgeDetail: "In the wetlands. Cabin empty. Liferaft gone, door opened from inside. They walked out."),

        new(
            "The cairn",
            gate: p => p.VisitedCount >= 18 && p.CountKnown(KnowledgeKind.Chart) >= 3,
            journal: "A man in the uplands buried four of them and can still recite their names. " +
                     "Seven came up out of the water and walked into the hills in November. " +
                     "Four names on the cairn. Wray is not one of them.",
            hint: "Seven walked. Four buried. Wray's name is not on the cairn.",
            knowledgeId: "search.cairn", knowledgeLabel: "The cairn — four names",
            knowledgeDetail: "Seven survivors walked into the uplands. Four buried. Three kept walking. Her name absent."),

        new(
            "The roster",
            gate: p => p.VisitedCount >= 20 && p.CountKnown(KnowledgeKind.Frequency) >= 3,
            journal: "The works took three walkers in and put them to work. The shop roster has " +
                     "her name against a four-year span and a leaving date. She was alive four " +
                     "years after the crash and she left on her own legs, heading for Ashmount.",
            hint: "Alive four years after the crash. Left for Ashmount on foot.",
            knowledgeId: "search.roster", knowledgeLabel: "The roster — alive, left for Ashmount",
            knowledgeDetail: "Sera Wray. Four years at Sawtooth Works. Left heading for Ashmount. Alive."),

        // ACT III — "The price." (Ashmount, The Scald · tier 3)
        // Target: 2-3 hours. The defended city. Find her.

        new(
            "The water",
            gate: p => p.VisitedCount >= 24 && p.CountKnown(KnowledgeKind.Chart) >= 4,
            journal: "Ashmount. Someone at the edge of the city knows who pumps the water: a woman " +
                     "in her sixties with a bad hip, who rebuilt a ventilation fan into a turbine. " +
                     "She has run it for six years. Three hundred people drink because of her.",
            hint: "She pumps water for Ashmount. Find the turbine at the flooded cut.",
            knowledgeId: "search.water", knowledgeLabel: "The water — Ashmount",
            knowledgeDetail: "She runs a turbine at a flooded cut. Three hundred people. Six years. She did not leave."),

        new(
            "Sera Wray",
            gate: p => p.VisitedCount >= 26 && p.CountKnown(KnowledgeKind.ThreatSite) >= 3,
            journal: "\"Your track is out. I could hear it from the cut. How long has it been " +
                     "doing that?\" She is sixty-one. She has a bad hip. She did not come because " +
                     "for the first two years she believed Hugh had burned.",
            hint: "Found her. She knows where the blades are.",
            knowledgeId: "search.wray", knowledgeLabel: "Sera Wray — found",
            knowledgeDetail: "Flight engineer. Sixty-one, bad hip, pumps water. Did not come because she thought Hugh burned."),

        new(
            "The magazine",
            gate: p => p.VisitedCount >= 28 && p.Knows("search.wray"),
            journal: "The blade stock she inventoried is in a hardened magazine at a depot deep in " +
                     "the ash country, under an aerostat, behind a door that has been sealed since " +
                     "it burned. She has known where they are for six years. She had no way to reach them.",
            hint: "Blades in a sealed magazine in The Scald. Under an aerostat.",
            knowledgeId: "search.magazine", knowledgeLabel: "The blade magazine",
            knowledgeDetail: "Hardened depot in The Scald, under the second aerostat. Sealed since it burned. Serviceable blades inside."),

        new(
            "The window",
            gate: p => p.VisitedCount >= 30 && p.Knows("search.magazine"),
            journal: "Juno Kessel runs the Ashmount tether crew. The aerostat comes down every " +
                     "eighth night to swap its gas bag. Ninety minutes of nobody looking down. " +
                     "\"What you do with it is not my business and I would like it kept that way.\"",
            hint: "Ninety-minute window every eighth night. Fly in, get the blades, fly out.",
            knowledgeId: "search.window", knowledgeLabel: "The window — 90 minutes",
            knowledgeDetail: "Aerostat down every eighth night. Ninety minutes. The last gate is a time of day."),
    };
}

/// <summary>One authored beat in the main search.</summary>
public sealed class SearchBeat
{
    public string Name;
    public Func<Progress, bool> Gate;
    public string Journal;
    public string Hint;
    public string? KnowledgeId;
    public string? KnowledgeLabel;
    public string? KnowledgeDetail;

    public SearchBeat(string name, Func<Progress, bool> gate, string journal, string hint,
                      string? knowledgeId = null, string? knowledgeLabel = null,
                      string? knowledgeDetail = null)
    {
        Name = name;
        Gate = gate;
        Journal = journal;
        Hint = hint;
        KnowledgeId = knowledgeId;
        KnowledgeLabel = knowledgeLabel;
        KnowledgeDetail = knowledgeDetail;
    }
}
