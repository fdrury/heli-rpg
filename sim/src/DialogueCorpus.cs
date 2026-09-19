namespace Rotorwash.Sim;

/// <summary>
/// The authored layer: hand-vetted lines, conditioned on state.
///
/// In the finished game this is generated offline at development time - thousands of lines
/// across dozens of characters - and loaded from data. What is here is the shape of it, and
/// a working character to test and tune against. Everything about the format is deliberate:
///
///   * Lines carry REQUIREMENTS, and the selector prefers the most specific match. That is
///     what makes a finite corpus feel like it is paying attention.
///   * Lines carry TAGS, so the coda can be told what tone it is following.
///   * Nothing here asks the player a question the game cannot answer, and nothing here
///     promises anything. The same rules the coda validator enforces apply to the humans
///     and the frontier model writing the baked layer.
///
/// Tone, per the vision: mature, melancholy, dryly funny. Never grim-dark, never quippy.
/// These people are tired and getting on with it.
/// </summary>
public static class DialogueCorpus
{
    private static DialogueLine L(string id, string text, string tag, double weight,
                                  params Requirement[] reqs)
    {
        var line = new DialogueLine { Id = id, Text = text, Weight = weight };
        line.Tags.Add(tag);
        line.Requires.AddRange(reqs);
        return line;
    }

    /// <summary>
    /// Mattie Sowerby keeps the pumps at a fuel cache and has opinions about airmanship.
    /// She is the tutorial character: the first person who is pleased to see the aircraft,
    /// and the first person to point out what is wrong with it.
    /// </summary>
    public static NpcMind Mattie() => new()
    {
        Id = "npc.mattie",
        Name = "Mattie Sowerby",
        Persona = "a fuel-cache keeper in her sixties, dry, unimpressed, secretly glad of the company. "
                + "You speak in short sentences. You do not gush.",
        Wants = "the pumps kept running, and for this pilot not to die stupidly",
        Standing = 0.2,
        Forbidden = { "helicopter pilot like you", "chosen one", "hero" },
    };

    public static DialogueBank MattieLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            // --- First meeting ----------------------------------------------
            L("mattie.first", "So you are real. I had it on the radio twice and put it down to weather.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("mattie.first.damaged",
              "So you are real. And you have brought it here in that state, which tells me most of what I need to know.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            // --- Returning --------------------------------------------------
            // Several interchangeable openings for the same ordinary state. The selector
            // can only rotate between lines it is actually given, so a corpus with one
            // legal line per state repeats no matter how clever the selection is - which
            // is precisely why the real corpus is generated offline in the thousands.
            L("mattie.return", "Back again.", "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.b", "You.", "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.c", "Heard you coming. Everyone did.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.d", "Still up, then.", "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.e", "I will not ask where you have been.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.f", "Shut it down properly this time. It does not like being left running.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.soon",
              "You were here this morning. Either you are very busy or you are very lost.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("mattie.return.long",
              "I had started telling people you were a story. Do not make a liar of me twice.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            // --- Fuel -------------------------------------------------------
            L("mattie.dry",
              "You came in on fumes. One day the wind will be wrong and that will be the whole story.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("mattie.low",
              "Gauge says you cut that fine. Pumps are yours, such as they are.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),

            // --- Condition --------------------------------------------------
            L("mattie.wrecked",
              "I can hear that from inside the shed. Whatever you did, do less of it.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("mattie.patched",
              "Somebody has been at that with wire and good intentions. I assume that somebody was you.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),

            // --- Standing ---------------------------------------------------
            L("mattie.warm",
              "Kettle is on. Do not read anything into it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            // --- Parting ----------------------------------------------------
            L("mattie.bye", "Go on, then.", "parting", 1),
            L("mattie.bye.b", "Right.", "parting", 1),
            L("mattie.bye.c", "Try to come back.", "parting", 1),
            L("mattie.bye.low",
              "Mind the ridge on the way out. It has had two before you and it was not fussy.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("mattie.bye.damaged",
              "If it starts making a different noise, put it down. Any ground. Do not get clever about it.",
              "parting", 5, Requirement.Condition(0, 0.6)),
            L("mattie.bye.night",
              "It will be dark before you are anywhere. But you know that.",
              "parting", 3),

            // --- Small talk -------------------------------------------------
            L("mattie.talk.pumps",
              "Windpump turns, pump works, tank fills. That is the whole of my life now and I do not mind it.",
              "talk", 2),
            L("mattie.talk.before",
              "Before, there were four of these a day over that ridge. I never once looked up.",
              "talk", 2),
            L("mattie.talk.sound",
              "You can hear it coming a long way out. Everybody can. Bear that in mind.",
              "talk", 3, Requirement.Meetings(2, 99)),
        });
        return b;
    }

    /// <summary>
    /// Everything the NPC should notice and remember about a visit. Called once per
    /// arrival; the coda then has something to work with next time.
    /// </summary>
    public static void RememberVisit(NpcMind npc, in TalkContext c)
    {
        npc.Notice("last_fuel", $"{c.FuelFraction:P0}", c.Now);
        if (c.WorstComponentHealth < 0.7)
            npc.Notice("last_damage", $"{c.WorstComponentName} at {c.WorstComponentHealth:P0}", c.Now);
        if (c.CarriedParts > 0) npc.Notice("carries_parts", $"{c.CarriedParts}", c.Now);
        foreach (string f in c.VisibleFittings) npc.Notice($"fitting.{f}", "fitted", c.Now);
        npc.LastSeenAt = c.Now;
        npc.Meetings++;
    }

    /// <summary>Turn remembered facts into the short phrases the coda prompt takes.</summary>
    public static List<string> MemoryPhrases(NpcMind npc)
    {
        var phrases = new List<string>();
        if (npc.Recall("last_damage") is string dmg) phrases.Add($"last time the {dmg}");
        if (npc.Recall("carries_parts") is not null) phrases.Add("they scavenge parts");
        foreach (MemoryFact m in npc.Memories)
            if (m.Key.StartsWith("fitting.")) phrases.Add($"they have {m.Key[8..]} on it");
        if (npc.Meetings > 4) phrases.Add($"they have been here {npc.Meetings} times");
        return phrases;
    }

    // ------------------------------------------------------------ generic settlers

    private static readonly string[] SettlerNames =
    {
        "Sal", "Mick", "Jen", "Drew", "Kit", "Nora", "Ruth", "Corin",
        "Ash", "Bren", "Dale", "Moss", "Wren", "Fen", "Tess", "Clay",
        "Rae", "Joss", "Hale", "Senna",
    };

    private static readonly string[] SettlerPersonas =
    {
        "keeps things running at {0}, practical, does not waste words",
        "watches the perimeter at {0}, says little, sees everything",
        "trades salvage at {0}, weighs every word before speaking it",
        "tends what grows at {0}, tired, steady, not unkind",
        "repairs things at {0}, patient, matter-of-fact",
    };

    /// <summary>
    /// A generic settler for sites without a named character. Each gets a deterministic
    /// name and persona. The persona mentions the site so the coda can contextualise.
    /// </summary>
    public static NpcMind Settler(int seed, string siteName)
    {
        var rng = new Random(seed * 104729 + 3);
        return new NpcMind
        {
            Id = $"npc.settler.{seed}",
            Name = SettlerNames[rng.Next(SettlerNames.Length)],
            Persona = string.Format(SettlerPersonas[rng.Next(SettlerPersonas.Length)], siteName),
            Wants = "to keep this place going and for people to stop dying out there",
            Standing = 0.0,
            Forbidden = { "chosen one", "hero", "save the world" },
        };
    }

    /// <summary>
    /// Lines shared by all generic settlers. Each NPC gets its own bank instance so
    /// recency tracking is per-character.
    /// </summary>
    public static DialogueBank SettlerLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            // --- First meeting ---
            L("settler.first", "Not many come out this way. Not on purpose.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("settler.first.damaged",
              "Whatever did that to your aircraft, it is still out there.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            // --- Returning ---
            L("settler.return.a", "You again.", "greeting", 1, Requirement.Meetings(1, 99)),
            L("settler.return.b", "Back.", "greeting", 1, Requirement.Meetings(1, 99)),
            L("settler.return.c", "Heard you before I saw you.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("settler.return.d", "Still flying.", "greeting", 1, Requirement.Meetings(1, 99)),
            L("settler.return.e", "I know that sound now.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("settler.return.f", "Thought you would be back.",
              "greeting", 1, Requirement.Meetings(1, 99)),

            L("settler.return.soon",
              "Twice in one day. You are either busy or in trouble.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("settler.return.long",
              "I had stopped expecting you.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            // --- Fuel ---
            L("settler.dry",
              "You came in on nothing. I could hear it from the stutter.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("settler.low",
              "That does not look like enough fuel to get back.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),

            // --- Condition ---
            L("settler.wrecked",
              "That does not sound right. None of it sounds right.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("settler.patched",
              "Somebody worked on that. You, I assume.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),

            // --- Standing ---
            L("settler.warm",
              "Sit down. You have earned it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            // --- Parting ---
            L("settler.bye.a", "Watch yourself.", "parting", 1),
            L("settler.bye.b", "Go.", "parting", 1),
            L("settler.bye.c", "Safe skies.", "parting", 1),
            L("settler.bye.low",
              "Do not push the fuel. Land before it is a decision somebody else makes.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("settler.bye.damaged",
              "Get that looked at before you go anywhere that matters.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            // --- Talk ---
            L("settler.talk.quiet",
              "Quiet here, mostly. The way I like it.",
              "talk", 2),
            L("settler.talk.before",
              "Before all this, I never looked up. Nobody did.",
              "talk", 2),
            L("settler.talk.sound",
              "You can hear it a long way off. Everyone can.",
              "talk", 3, Requirement.Meetings(2, 99)),
            L("settler.talk.trade",
              "If you find parts, I know people who need them. Everybody needs something.",
              "talk", 2),
            L("settler.talk.relay",
              "The relay was on last week. Could not make out the words, but it was on.",
              "talk", 2),
        });
        return b;
    }
}
