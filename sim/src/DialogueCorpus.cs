namespace Rotorwash.Sim;

/// <summary>
/// The authored layer: hand-vetted lines, conditioned on state.
///
/// In the finished game this is generated offline at development time - thousands of lines
/// across dozens of characters - and loaded from data. What is here is the shape of it, and
/// nine working characters to test and tune against. Everything about the format is
/// deliberate:
///
///   * Lines carry REQUIREMENTS, and the selector prefers the most specific match. That is
///     what makes a finite corpus feel like it is paying attention.
///   * Lines carry TAGS, so the coda can be told what tone it is following.
///   * Nothing here asks the player a question the game cannot answer, and nothing here
///     promises anything the game cannot honour. The same rules the coda validator
///     enforces apply to the humans and the frontier model writing the baked layer.
///
/// Tone, per the vision: mature, melancholy, dryly funny. Never grim-dark, never quippy.
/// These people are tired and getting on with it.
///
/// -------------------------------------------------------------------------------------
/// VOICE
/// -------------------------------------------------------------------------------------
/// story.md 3 names nine people and gives each of them a want, a thing they know and a
/// thing they will not discuss. The test of this file is the one story.md sets: a line of
/// dialogue, on its own, with no name attached, should be attributable. The levers used,
/// in order of how much work they do:
///
///   1. SENTENCE LENGTH. Doss runs on and interrupts himself; Mattie stops at five words;
///      Sparrow speaks only in instructions. This is doing most of the identification.
///   2. WHAT THEY COUNT. Nell counts kilograms, Ferren counts mouths fed, Doss counts
///      millibars, Wray counts hours since a track, Juno counts minutes she is not being
///      watched, Osie counts stones and names. Nobody counts the same thing twice.
///   3. WHAT THEY REFUSE. Bel refuses to say where the wreck is. Ferren refuses to be
///      judged. Mattie refuses to let anything mean anything. Juno refuses to have said
///      it. The refusals are gated on knowledge, so the refusal changes into the answer.
///
/// -------------------------------------------------------------------------------------
/// KNOWLEDGE GATING
/// -------------------------------------------------------------------------------------
/// Every `search.*` id below is granted by sim/src/SearchThread.cs or bound by
/// game/scripts/StoryPlaces.cs. This file only ever READS them - it gates lines on
/// `Requirement.Knows(id)` and `Requirement.Unknown(id)`, which the selector scores as
/// extra specificity, so the post-knowledge line beats the pre-knowledge one for free and
/// the arc sequences itself (story.md 4.5).
///
/// The rule the writing follows: a gated line states a FACT the character owns, and the
/// journal records the fact. Nobody tells the player where to fly next (story.md 9).
///
/// -------------------------------------------------------------------------------------
/// D-008
/// -------------------------------------------------------------------------------------
/// No other helicopter pilots exist. Nobody in this file refers to another flyer, another
/// machine in the air, or a second aircraft to work with. Past-tense aviation is allowed
/// and is part of the setting - Mattie watched four a day go over that ridge, Nell worked
/// a loading apron - because the collapse ended it. DialogueTests.PremiseGuard scans every
/// line in every bank for rival-flyer phrasings, not exact sentences (D-006's blocklist
/// discipline: the earlier failure was blocking "i can offer" and letting "I could offer
/// you work" straight through).
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

    /// <summary>Same, for a line that wants a second tag - "thread" on every story beat.</summary>
    private static DialogueLine LT(string id, string text, string[] tags, double weight,
                                   params Requirement[] reqs)
    {
        var line = new DialogueLine { Id = id, Text = text, Weight = weight };
        line.Tags.AddRange(tags);
        line.Requires.AddRange(reqs);
        return line;
    }

    /// <summary>Thread line that grants a reward when delivered (D-089).</summary>
    private static DialogueLine LR(string id, string text, string[] tags, double weight,
                                   DialogueReward reward, params Requirement[] reqs)
    {
        var line = new DialogueLine { Id = id, Text = text, Weight = weight, Reward = reward };
        line.Tags.AddRange(tags);
        line.Requires.AddRange(reqs);
        return line;
    }

    private static readonly string[] Thread = { "talk", "thread" };
    private static readonly string[] ThreadGreeting = { "greeting", "thread" };
    private static readonly string[] ThreadParting = { "parting", "thread" };

    // =====================================================================================
    //  Knowledge ids. Granted elsewhere; read here. Never written by this file.
    // =====================================================================================

    /// <summary>The story ids this corpus gates on. Kept as constants so a typo is a build error.</summary>
    public static class Knows
    {
        public const string Callsign = "search.callsign";   // SIERRA-FOUR-THREE, not a frequency
        public const string Band     = "search.band";       // 34 airband candidates
        public const string Rota     = "search.rota";       // the 06:40 broadcast, four readers
        public const string Manifest = "search.manifest";   // 420 kg over gross
        public const string Field    = "search.field";      // the departure field, leg 1 closed
        public const string Wreck    = "search.wreck";      // the aeroplane, in the water
        public const string Cairn    = "search.cairn";      // four names, hers absent
        public const string Roster   = "search.roster";     // alive +4 years, left for Ashmount
        public const string Water    = "search.water";      // who pumps the water at Ashmount
        public const string Wray     = "search.wray";       // her
        public const string Magazine = "search.magazine";   // where the blades are
        public const string Window   = "search.window";     // ninety minutes, every eighth night

        /// <summary>Wray's load specification: 420 kg, hook required, LRT off (D-093).</summary>
        public const string Load     = "search.load";       // what the finale costs

        /// <summary>Sparrow granted passage to the magazine for medical stock (D-093).</summary>
        public const string Passage  = "sparrow.passage";   // the ash-country door

        /// <summary>Juno's approach route past the tether field (D-093).</summary>
        public const string Approach = "juno.approach";      // under the terrace, below the bag

        /// <summary>Bel's trade: she told you where the wreck is.</summary>
        public const string WreckPosition = ContractBoard.BelWreckPositionId;

        /// <summary>Every id above, for tests and for building a "knows everything" context.</summary>
        public static readonly string[] All =
        {
            Callsign, Band, Rota, Manifest, Field, Wreck, Cairn, Roster, Water, Wray,
            Magazine, Window, Load, Passage, Approach, WreckPosition,
        };
    }

    /// <summary>The nine people story.md names, by the npc id StoryPlaces binds them under.</summary>
    public static readonly string[] NamedIds =
    {
        "mattie", "doss", "nell", "bel", "osie", "ferren", "wray", "juno", "sparrow",
    };

    /// <summary>
    /// The mind and the bank for a named character, or null if the id is not one of the
    /// nine. The game layer resolves the id from StoryPlaces.StorySite.NpcId and falls
    /// through to Settler() when this returns null.
    /// </summary>
    public static (NpcMind Npc, DialogueBank Bank)? Named(string npcId) => npcId switch
    {
        "mattie"  => (Mattie(),  MattieLines()),
        "doss"    => (Doss(),    DossLines()),
        "nell"    => (Nell(),    NellLines()),
        "bel"     => (Bel(),     BelLines()),
        "osie"    => (Osie(),    OsieLines()),
        "ferren"  => (Ferren(),  FerrenLines()),
        "wray"    => (Wray(),    WrayLines()),
        "juno"    => (Juno(),    JunoLines()),
        "sparrow" => (Sparrow(), SparrowLines()),
        _ => null,
    };

    /// <summary>Just the bank, for save restore, where the mind comes out of the save file.</summary>
    public static DialogueBank? LinesFor(string npcId) => Named(npcId)?.Bank;

    // =====================================================================================
    //  Mattie Sowerby - The Pan, the pumps. Tier 0. The tutorial character.
    // =====================================================================================

    /// <summary>
    /// Mattie Sowerby keeps the pumps at a fuel cache and has opinions about airmanship.
    /// She is the tutorial character: the first person who is pleased to see the aircraft,
    /// and the first person to point out what is wrong with it.
    ///
    /// Voice: five words where anyone else would use fifteen. She is the one character who
    /// has deliberately decided not to hope, and the register of every line is a person
    /// refusing to let a thing mean something (story.md 3).
    /// </summary>
    public static NpcMind Mattie() => new()
    {
        Id = "npc.mattie",
        Name = "Mattie Sowerby",
        Persona = "a fuel-cache keeper in her sixties, dry, unimpressed, secretly glad of the company. "
                + "You speak in short sentences. You do not gush.",
        Wants = "the pumps kept running, and for this pilot not to die stupidly",
        Standing = 0.2,
        Forbidden = { "helicopter pilot like you", "chosen one", "hero", "the blades" },
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
            L("mattie.return.g", "Pumps are on. That is my news in full.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.h", "Do not stand in the wind and talk. Come round the side.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.i", "I had the kettle on for something else.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.j", "Dust is up. It will be in your seals by Thursday.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("mattie.return.k", "You have the look of somebody who wants something and has not decided how to ask.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("mattie.return.l", "Nothing has happened here. It rarely does. I have got used to saying it.",
              "greeting", 1, Requirement.Meetings(2, 99)),
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
            L("mattie.warm.b",
              "There is a second chair now. I did not get it for anybody in particular.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("mattie.warm.c",
              "I have started keeping the good tea separate. That is all I am going to say about it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            // --- Parting ----------------------------------------------------
            L("mattie.bye", "Go on, then.", "parting", 1),
            L("mattie.bye.b", "Right.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.c", "Try to come back.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.d", "Tank is full. That is the extent of what I can do for anybody.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.e", "Wind will be on your nose till the ridge. After that it is your problem.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.low",
              "Mind the ridge on the way out. It has had two before you and it was not fussy.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("mattie.bye.damaged",
              "If it starts making a different noise, put it down. Any ground. Do not get clever about it.",
              "parting", 5, Requirement.Condition(0, 0.6)),
            L("mattie.bye.f", "That is the lot, then.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.g", "Do not lift over the water tower. It is held together with paint.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.h", "I will hear you go. Everybody will.", "parting", 1, Requirement.Meetings(1, 99)),
            L("mattie.bye.night",
              "It will be dark before you are anywhere. But you know that.",
              "parting", 3, Requirement.Meetings(1, 99)),

            // --- Small talk -------------------------------------------------
            L("mattie.talk.pumps",
              "Windpump turns, pump works, tank fills. That is the whole of my life now and I do not mind it.",
              "talk", 2),
            L("mattie.talk.before",
              "Before, there were four of these a day over that ridge. I never once looked up.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("mattie.talk.sound",
              "You can hear it coming a long way out. Everybody can. Bear that in mind.",
              "talk", 3, Requirement.Meetings(2, 99)),
            L("mattie.talk.hope",
              "People here waited three years for somebody to come and sort it out. Then they stopped. Stopping was better.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("mattie.talk.cause",
              "Every third traveller knows exactly why it happened, and none of them agree. I have stopped asking.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("mattie.talk.tin",
              "I keep things in tins. Chits, washers, a button. It is not sentiment, it is that tins keep the damp out.",
              "talk", 2, Requirement.Meetings(2, 99)),

            // --- The drums ---------------------------------------------------
            // Deadpan comes from a person not trying to be funny, saying a small thing
            // flatly while doing something mundane. Mattie's is procedure: she has a
            // correct way of doing an unimportant job and the world will not adopt it.
            L("mattie.wry.drums",
              "Drums go on their sides. Two high, bungs at three o'clock. I have written it on the wall. People still stand them up.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("mattie.wry.list",
              "There is a list on the door. It is not a long list. Nobody has ever read it.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("mattie.wry.chair",
              "That is the good chair. There is only the one. I am not going to make a thing of it, but you are sitting in it.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("mattie.wry.lonely",
              "Somebody asked me last year whether I get lonely. I told them the pump seal had gone. That was a true answer and it was also the end of it.",
              "talk", 2, Requirement.Meetings(3, 99)),
            L("mattie.wry.name",
              "People have started calling this place the pumps. It had a name before there were pumps. I have stopped correcting them.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("mattie.wry.bye",
              "Bungs at three o'clock.",
              "parting", 1, Requirement.Meetings(3, 99)),

            // --- The thread -------------------------------------------------
            // She is the correction (beat 2) and she is the flat note at the end.
            LT("mattie.thread.name.unknown",
              "Show me that page again. No, the last one, in the other hand. Hm.",
              Thread, 4, Requirement.Unknown(Knows.Callsign)),
            LT("mattie.thread.callsign",
              "That is not a frequency. That is the back half of a callsign. Sierra four three. "
              + "You have been listening for a number for six years and it was a name all along.",
              Thread, 6, Requirement.Knows(Knows.Callsign), Requirement.Unknown(Knows.Band)),
            LT("mattie.thread.band",
              "Thirty-four channels. Long Acre has the tall mast and the only working set for eighty kilometres. Start there.",
              Thread, 5, Requirement.Knows(Knows.Band), Requirement.Unknown(Knows.Rota)),
            LT("mattie.thread.chit",
              "I kept it because it was the only one anybody ever signed with a flourish. "
              + "That is the whole reason. Do not make it a sign.",
              Thread, 7, Requirement.Knows(Knows.Callsign), Requirement.Standing(0.6, 1)),
            LT("mattie.thread.chit.b",
              "Fuel chit. Half a litre short, and signed in a hand that goes up at the end. It has "
              + "been in a tin with a button and some washers since before you were flying past here.",
              Thread, 7, Requirement.Knows(Knows.Callsign), Requirement.Standing(0.6, 1)),
            LT("mattie.thread.chit.c",
              "The tin stays. You can have what is in it. That tin is the right size for the washers and there is not another one.",
              Thread, 7, Requirement.Knows(Knows.Callsign), Requirement.Standing(0.6, 1)),
            LT("mattie.thread.chit.d",
              "I did not keep it for you. I want that clear. I did not know there was a you.",
              Thread, 7, Requirement.Knows(Knows.Wreck), Requirement.Standing(0.6, 1)),
            LT("mattie.thread.wreck",
              "In the water, then. Six years of not asking, and it was in the water the whole time.",
              Thread, 5, Requirement.Knows(Knows.Wreck), Requirement.Unknown(Knows.Roster)),
            LT("mattie.thread.roster",
              "Four years at a works and then she walked to a city. That is not somebody who got lost. "
              + "That is somebody who kept choosing.",
              Thread, 5, Requirement.Knows(Knows.Roster)),
            // The same words as mattie.return, deliberately. story.md 3: when the net comes
            // up, Ashmount answers on her set and she does not say anything about it at
            // all, which is the line. A different greeting here would be the game telling
            // the player how to feel about it.
            LT("mattie.thread.wray.greet",
              "Back again.",
              ThreadGreeting, 8, Requirement.Knows(Knows.Wray), Requirement.Meetings(1, 99)),
            LT("mattie.thread.wray",
              "Right. Pumps need doing. Tank is where it always is.",
              Thread, 6, Requirement.Knows(Knows.Wray)),
            LT("mattie.thread.window",
              "An eighth night and ninety minutes. I have waited longer than that for a pump seal. "
              + "You will be fine or you will not, and either way I would like the aircraft back.",
              Thread, 5, Requirement.Knows(Knows.Window)),
            LT("mattie.thread.bye",
              "Go and get your blades.",
              ThreadParting, 5, Requirement.Knows(Knows.Magazine)),
        });
        return b;
    }

    // =====================================================================================
    //  Doss Emery - Long Acre, the aerial guyed to the chimney. Tier 0.
    // =====================================================================================

    /// <summary>
    /// Voice: the opposite of Mattie. He has not been listened to in six years and it all
    /// comes out at once, in subordinate clauses, with numbers attached to things that do
    /// not need numbers. He gives everything away for free and that should be slightly
    /// uncomfortable (story.md 3).
    /// </summary>
    public static NpcMind Doss() => new()
    {
        Id = "npc.doss",
        Name = "Doss Emery",
        Persona = "a farmer in his late sixties at Long Acre with an airband set he has never once "
                + "turned off and nobody to talk to. You talk too much and you know it. You say "
                + "numbers out loud.",
        Wants = "someone to confirm he has not been imagining the broadcast at 06:40",
        Standing = 0.1,
        Forbidden = { "chosen one", "hero", "the blades", "Wray", "Sierra four three" },
    };

    public static DialogueBank DossLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("doss.first",
              "You are real, then. I have had you on the set twice and both times I told myself it "
              + "was a tractor, and both times I knew perfectly well it was not a tractor.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("doss.first.damaged",
              "You are real, and you are dropping oil on my yard, and I find that I do not mind "
              + "about the yard at all, which has surprised me.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("doss.return.a", "You came back. People say they will and then the winter happens to them.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.b", "Sit down, sit down. I have not said a word out loud since Tuesday and it will show.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.c", "One thousand and eleven, and steady. That is the pressure. You did not ask.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.d", "I heard you at nine kilometres. I counted it off on the fence posts. Nine.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.e", "Mind the dog. There is no dog. Six years and I still say it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.f", "Wheat came up in one corner this year. One corner. I have told everybody who has come past, which is you.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.g", "I had something saved up to tell you and now I have gone and lost it. It will come back at about four in the morning.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.h", "Kettle, chair, and then I will stop talking. I will not stop talking, but I will mean to.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.i", "You have a way of turning up just after I have decided you were weather.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.j", "Still up. I say that every time and I hear myself doing it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.return.k", "Barometer has fallen four points since breakfast, which means nothing to you and a great deal to me.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("doss.return.soon",
              "Twice in a day. I shall have nothing left to say by the evening, which has never once happened in my life.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("doss.return.long",
              "Eleven weeks. I am not counting. I am, obviously, I have just said the number out loud.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("doss.dry",
              "That was not a healthy noise on the approach. That was a noise I have heard before, "
              + "out of a thing that did not fly again afterwards.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("doss.low",
              "You are short. There is a drum by the gate, it is four years old and it is dry, and you are welcome to all of it.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("doss.wrecked",
              "Something has had a proper go at that. I would like to be able to say I have seen worse, "
              + "and I have not, not since the year it all went.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("doss.patched",
              "Wire and tape. I do the same to the baler. It works right up until the morning it is the only thing you needed to work.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("doss.warm",
              "You listen. I would like that written down somewhere, because nobody believes me about the broadcast and you sat there and listened.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("doss.warm.b",
              "I have put a chair by the set. Your chair, I have been calling it, to myself, which I realise is a thing I have been doing.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("doss.warm.c",
              "I have started saving things up to tell you rather than saying them to the dog, and there is no dog, so it is an improvement.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("doss.bye.a", "Right. Yes. Go on.", "parting", 1),
            L("doss.bye.b", "I will be here. That is not a complaint, it is only true.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.c", "Safe out. And do not call on the set, I will hear it and then I will worry about it all night.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.d", "Hedge lines run the way they always ran. Follow them and you cannot get it wrong.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.e", "I will wave. You will not see it. I do it regardless.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.f", "Zero six four zero, if you are anywhere near a set.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.g", "Take the drum by the gate if you want it. Nobody else is coming for it.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.h", "Right. Well. Yes. Off you go, then. Right.", "parting", 1, Requirement.Meetings(1, 99)),
            L("doss.bye.low",
              "Wind is westerly and it will be on your nose the entire way. Put that into your sums before you lift.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("doss.bye.damaged",
              "If it changes note, put it in a field. Any field. Mine, if you can reach mine.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("doss.talk.set",
              "The set has been on six years, not once off. I clean the contacts with a pencil rubber, "
              + "which I am aware is not correct, and it works, so there we are.",
              "talk", 2),
            L("doss.talk.wheat",
              "Wheat wants nitrogen and rain in the right fortnight. It has had neither since. I plant it anyway and I could not tell you why.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("doss.talk.wife",
              "I can tell you the pressure at Ashmount on the morning my wife died. One thousand and two. "
              + "I do not know what a man is supposed to do with a thing like that.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("doss.talk.quiet",
              "You get used to talking to nobody. You do not get used to nobody talking back. Those are two different things and it took me a while.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("doss.talk.flat",
              "Flat country. Everybody hears you twenty minutes before you arrive and gets their face ready.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("doss.talk.cause",
              "People come through and tell me they know why it happened, and every one of them says a "
              + "different thing, and they cannot all be right, and I do not think any of them are.",
              "talk", 2, Requirement.Meetings(2, 99)),

            // --- The system nobody asked about --------------------------------
            L("doss.wry.names",
              "I have given them names. You did not ask for them. Throat, Pressure, Wrong Date, and Knots. I am aware of how that sounds out loud.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("doss.wry.ledger",
              "I keep it in a ledger. Date, time, pressure, who read it. Four ledgers now. The first one I did in pencil and I regret that daily.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("doss.wry.speech",
              "I had a speech ready for the first person who came. I have had six years to work on it. I have only just noticed I have not given it and we are already sitting down.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("doss.wry.cassette",
              "There is a cassette in that drawer with nothing written on it. I have not played it. If I play it and it is nothing, then it is nothing, and at the moment it is a cassette.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("doss.wry.noanswer",
              "You asked me how I am. Zero six four zero, clear, wind two four zero at eight. Sorry. That is not what you asked. It is what I have.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("doss.wry.bye",
              "I will put it in the ledger that you came. Under remarks.",
              "parting", 1, Requirement.Meetings(2, 99)),

            // --- The thread -------------------------------------------------
            LT("doss.thread.band.unknown",
              "There is a carrier at 06:40 every morning of the world. I am not asking you to believe me. "
              + "I am asking you to go up a mast with a proper set and look.",
              Thread, 4, Requirement.Unknown(Knows.Band)),
            LT("doss.thread.band",
              "Thirty-four channels. I have sat through all of them and I could not tell you which is which. "
              + "You have a mast and a proper set. I have a chimney and a wire.",
              Thread, 5, Requirement.Knows(Knows.Band), Requirement.Unknown(Knows.Rota)),
            LT("doss.thread.rota",
              "Four of them read it. One clears his throat. One does the pressure first, always, out of "
              + "order. One gets the date wrong twice a week. And one says the wind speeds in knots. Nobody says knots.",
              Thread, 6, Requirement.Knows(Knows.Rota)),
            LT("doss.thread.rota.greet",
              "The knots one read this morning. Six, at zero six four zero. I wrote it down, in case.",
              ThreadGreeting, 5, Requirement.Knows(Knows.Rota), Requirement.Meetings(1, 99)),
            LT("doss.thread.wreck",
              "A wreck in the water, you say. And the set still talks at 06:40. Both of those are true at "
              + "the same time and I have been sitting here all week trying to make them fit together.",
              Thread, 5, Requirement.Knows(Knows.Wreck)),
            LT("doss.thread.roster",
              "Four years at a works, and then a walk to a city. On her legs. At that age. I could not do it and I have the good hip.",
              Thread, 5, Requirement.Knows(Knows.Roster)),
            LT("doss.thread.wray.greet",
              "Which one was she. Of the four. You will say the knots one, and I will say I knew, and I did "
              + "not know, I only hoped, and there is a difference and I have had six years to learn it.",
              ThreadGreeting, 8, Requirement.Knows(Knows.Wray), Requirement.Meetings(1, 99)),
            LT("doss.thread.window",
              "Every eighth night. I could set a clock by less than that. Give me the date and I will be on the set for the whole of it.",
              Thread, 5, Requirement.Knows(Knows.Window)),
            LT("doss.thread.bye",
              "I will be listening. That is all I have ever been able to offer anybody and it is going to have to do.",
              ThreadParting, 5, Requirement.Knows(Knows.Rota)),
        });
        return b;
    }

    // =====================================================================================
    //  Nell Abergale - Fenmoor, the scrap yard. Tier 1.
    // =====================================================================================

    /// <summary>
    /// Voice: clipped, transactional on the surface, and underneath it a person who has
    /// been keeping a ledger against herself for six years. She counts mass. She does not
    /// want money, she wants a reason, and she will not say so (story.md 3).
    /// </summary>
    public static NpcMind Nell() => new()
    {
        Id = "npc.nell",
        Name = "Nell Abergale",
        Persona = "a woman in her forties at Fenmoor, ground crew at the field before, a scrap yard "
                + "now. You are clipped. You count things. You do not offer comfort and you do not "
                + "want any.",
        Wants = "to know what happened to her brother, whose name is on a loadsheet she signed",
        Standing = -0.1,
        Forbidden = { "chosen one", "hero", "the blades", "Wray" },
    };

    public static DialogueBank NellLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("nell.first", "New face. I will want something for whatever it is you are about to ask.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("nell.first.damaged",
              "You brought that here in that state. Either you are desperate or you are careless. Both have a price and it is the same price.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("nell.return.a", "Back. Say the number first and we will get on quicker.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.b", "Heap on the left is sorted. Do not unsort it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.c", "I am not busy. I want that on record, and I would still rather you were brief.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.d", "The yard is the same. Everything here is the same.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.e", "I hear the rotor. I always hear the rotor. It is not a compliment.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.f", "Say what you need. I will say a price or I will say no.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.g", "There is tea. It is not good tea and I am not going to apologise for it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.h", "Nothing has come in since you were last here. Nothing comes in.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.i", "You are the only thing round here that changes, and you are not much of a change.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("nell.return.j", "Weigh what you have brought before you tell me about it.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("nell.return.k", "I have been shifting axle stock all morning. Ask me anything, I will get it wrong.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("nell.return.soon",
              "Twice today. Either you forgot something or you have decided I go soft in the afternoon.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("nell.return.long",
              "Long gap. I had put your crate back on the shelf. I will get it down again, and I want you to know I put it back.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("nell.dry",
              "You landed on fumes at a scrap yard. There is no fuel here. There has never been fuel here.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("nell.low",
              "Short again. I will not lecture you. I will only say I have weighed what that costs and it is more than you think it is.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("nell.wrecked",
              "That is a write-off anywhere but here. Which makes it a repair. Sit down.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("nell.patched",
              "Somebody has bodged that, and bodged it well. I will not ask what with.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("nell.heavy",
              "You are loaded. Good. I would rather deal with somebody who has brought something than somebody who has brought a story.",
              "greeting", 5, Requirement.Carried(240, 99999)),
            L("nell.warm",
              "There is a crate in the back I have never sold to anybody. I am not selling it to you either. I am telling you it is there.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("nell.warm.b",
              "I have put your name on a shelf. Not a tribute. There is a shelf, and things on it are yours, and it needed a label.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("nell.warm.c",
              "You weigh things before you talk about them now. I noticed. I was not going to mention it and I have mentioned it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("nell.bye.a", "Go on.", "parting", 1),
            L("nell.bye.b", "West line out. The east line has a mast on it that is not on anybody's chart.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.c", "Do not thank me. Weigh it, and if the weight was right, thank me next time.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.d", "Shut the gate. It matters more than it looks like it matters.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.e", "That is the weight settled. Go.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.f", "If it comes back heavier, I will notice.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.g", "Do not lift over the crane. It is not a crane any more, it is a shape.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.h", "Shut the ledger on your way past. It lives shut.", "parting", 1, Requirement.Meetings(1, 99)),
            L("nell.bye.low",
              "Do not try to make anywhere from here on that. Sit down and wait for the wind to turn.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("nell.bye.damaged",
              "Straight and level, and do not ask it for anything clever. That is all a patched airframe owes you.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("nell.talk.yard",
              "Everything in this yard came off something somebody needed. I sort it because sorting is the only part of it I am in charge of.",
              "talk", 2),
            L("nell.talk.field",
              "I worked the field. Chocks, fuel, loadsheets. Eleven years, and I was good at it. That is not a boast, it is the problem.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("nell.talk.money",
              "I do not need money. There is nothing to buy. I need a reason, and reasons are harder to price than axles.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("nell.talk.exurb",
              "This was the bit between the city and the country. Now it is just the bit.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("nell.talk.before",
              "People ask what it was like working aircraft. It was cold and it was early, and I would have it back tomorrow.",
              "talk", 2, Requirement.Meetings(2, 99)),

            // --- The third heap ------------------------------------------------
            L("nell.wry.heaps",
              "Three heaps. Ferrous, non-ferrous, and things I have not decided about. People think the third heap is rubbish. The third heap is the hardest heap.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("nell.wry.fence",
              "The man on the east side moved his fence a metre into my yard in the spring. I have not said anything. I have measured it twice and I have not said anything.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("nell.wry.tea",
              "I said the tea is not good. I would like it understood that I make it correctly and the water here is the problem.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("nell.wry.label",
              "Everything is labelled. You will notice the labels are all in my handwriting, because there is nobody else.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("nell.wry.noanswer",
              "You have asked twice now whether I am all right. The heap is sorted to the third row. That is where we are up to.",
              "greeting", 1, Requirement.Meetings(3, 99)),
            L("nell.wry.bye",
              "Do not move anything on the way past. I will know.",
              "parting", 1, Requirement.Meetings(2, 99)),

            // --- The thread -------------------------------------------------
            LT("nell.thread.manifest.unknown",
              "I keep paper. That is all I am going to say about paper today.",
              Thread, 4, Requirement.Unknown(Knows.Manifest)),
            LT("nell.thread.manifest",
              "Four hundred and twenty kilograms over. I signed the loadsheet. Every day since, I have "
              + "known exactly how much I signed for.",
              Thread, 6, Requirement.Knows(Knows.Manifest)),
            LT("nell.thread.field",
              "You have been out to the field, then. The apron is empty. It was not empty that morning, and I am the one who knows what was on it.",
              Thread, 5, Requirement.Knows(Knows.Field)),
            LT("nell.thread.wreck.greet",
              "You have been out to the wet. I can tell by how you are standing. Say it plainly. I will not thank you for gentle.",
              ThreadGreeting, 6, Requirement.Knows(Knows.Wreck), Requirement.Meetings(1, 99)),
            LT("nell.thread.wreck",
              "In the water, and empty. That is one of three things it could have been, and it is the one I had stopped letting myself have.",
              Thread, 5, Requirement.Knows(Knows.Wreck), Requirement.Unknown(Knows.Cairn)),
            LT("nell.thread.cairn",
              "Four names on a stone, and one of them is my brother. I carried that question six years and "
              + "you closed it in a fortnight, and I have not worked out yet what I owe you for that.",
              Thread, 7, Requirement.Knows(Knows.Cairn)),
            LT("nell.thread.roster",
              "Alive four years after. So the loadsheet did not kill everybody on it. That is not absolution. I will take it anyway.",
              Thread, 5, Requirement.Knows(Knows.Roster)),
            LR("nell.thread.rwr",
              "The crate is yours. It is a warning receiver, it is older than you are, and it will tell "
              + "you when somebody is looking at you. That is all it does and it is enough.",
              Thread, 5,
              new DialogueReward(DialogueRewardKind.Module, "rwr"),
              Requirement.Knows(Knows.Manifest), Requirement.Standing(0.3, 1)),
            LT("nell.thread.bye",
              "Nothing. Go. I will be here weighing things.",
              ThreadParting, 5, Requirement.Knows(Knows.Cairn)),
        });
        return b;
    }

    // =====================================================================================
    //  Bel Tiernan - The Drowning, the stilt village. Tier 1.
    // =====================================================================================

    /// <summary>
    /// Voice: boundaries. Almost every line states what is permitted, where you may stand,
    /// which way you may go. She is not hostile - she is the person who decides who is
    /// taken where by boat, and that is a job about limits (story.md 3).
    /// </summary>
    public static NpcMind Bel() => new()
    {
        Id = "npc.bel",
        Name = "Bel Tiernan",
        Persona = "a woman in her thirties who runs a stilt village and an eel trade in the wetland "
                + "and decides who is taken where by boat. You speak in boundaries. You say what is "
                + "allowed and what is not.",
        Wants = "the drowned aircraft left alone, and a generator brought off an island",
        Standing = 0.0,
        Forbidden = { "chosen one", "hero", "the blades", "Wray" },
    };

    public static DialogueBank BelLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("bel.first",
              "Skids on the hard standing. Everything else here is somebody's roof and it will not hold you.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("bel.first.damaged",
              "You put that down here in that state. If it had gone through a roof we would be having a "
              + "different conversation and you would be having it from the water.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("bel.return.a", "Channel is marked. Follow the withies in, same as last time.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.b", "Tide is out. You have an hour of hard standing and then you have a boat.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.c", "You are expected. Not welcome exactly. Expected.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.d", "Eels are running. That is the news. That is all of the news.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.e", "You took the washing off the rail again.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.f", "Come in off the planks before you say anything. The wind takes half of it out here.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.g", "We know what week it is by the water. You have a clock. It is not better.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.h", "Nobody has drowned since you were last here. I like being able to say that.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.i", "Boats are out. It is me and the smoke house.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.return.j", "Stay on the boards. They are marked for a reason and the reason is under them.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("bel.return.soon",
              "You have been gone four hours. Whatever you forgot, it is not here.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("bel.return.long",
              "Long time. The water has moved two of the paths since. Do not use the memory you arrived with.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("bel.dry",
              "You crossed the wet on that. If it had stopped, you would be down there with everything else that stopped.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("bel.low",
              "Short. Nothing here burns the way you need. Peat and fish oil, and neither one will fly you home.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("bel.wrecked",
              "I can hear that from the smoke house. Out here damage is a sentence, not a repair bill.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("bel.patched",
              "Salt will find every one of those patches. Give it a month and come back and tell me I was wrong.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("bel.warm",
              "There is a place by the fire and a chair that is not wet. That is the best thing this village has.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("bel.warm.b",
              "You have stopped landing on the drying racks. People have noticed. Nobody is going to say so, so I am.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("bel.warm.c",
              "The boat crew take you out now without asking me first. That took two years and I did not do anything to cause it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("bel.bye.a", "Go while the light is still on the water.", "parting", 1),
            L("bel.bye.b", "North of the withies on the way out.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.c", "Mind the birds off the reed beds. They go up all at once and they do not look where.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.d", "Do not come back over the houses. Round, not over.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.e", "Tide turns at four. That is not a threat. It is a tide.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.f", "Outer channel. The inner one has a boat in it that nobody will admit to sinking.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.g", "The whole village will come out to watch you go. They will all deny it afterwards.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.h", "Off the boards, then up. In that order.", "parting", 1, Requirement.Meetings(1, 99)),
            L("bel.bye.low",
              "Do not chance the open water on that. Go round. It is longer and it is ground.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("bel.bye.damaged",
              "If it fails out there you will not be found. I am not being hard with you. That is only what the water is.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("bel.talk.village",
              "Twelve houses on legs and a smoke house. We eat, we trade eel, nobody takes anything from anybody. It took eleven years to get that.",
              "talk", 2),
            L("bel.talk.water",
              "The water does not give things back. It keeps them, and it keeps them tidy, and people find the tidy part harder than the wreckage.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("bel.talk.boats",
              "Everything that moves here moves because somebody rows it. You have made a lot of people think about that.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("bel.talk.graves",
              "You cannot dig a grave in this. We put people in the water and we say where. To us that is the same thing.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("bel.talk.island",
              "There is a generator on an island two kilometres out. Three years it has sat there. We can "
              + "get a boat to it and we cannot get it into a boat.",
              "talk", 3, Requirement.Meetings(2, 99)),

            // --- The rota ------------------------------------------------------
            L("bel.wry.rota",
              "There is a rota for mending the boards. Four names on it. Two of them are mine. I wrote the rota.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("bel.wry.meeting",
              "We have a meeting on the first of the month. The last one ran two hours. An hour and forty minutes of it was about a boat that nobody owns.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("bel.wry.eels",
              "People say all eel tastes the same. It does not. I will not be drawn any further on it in front of a guest.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("bel.wry.landing",
              "That is the visitors' landing. You are the only visitor. It has been the visitors' landing for eleven years.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("bel.wry.noanswer",
              "You asked what we would do if the water came up another foot. The smoke house is on the high staging. That is the answer to a different question and it is the one I have thought about.",
              "greeting", 1, Requirement.Meetings(3, 99)),
            L("bel.wry.bye",
              "I will put you on the rota one of these days. As a joke. Mostly as a joke.",
              "parting", 1, Requirement.Meetings(3, 99)),

            // --- The thread -------------------------------------------------
            LT("bel.thread.wreck.unknown",
              "You can hover over the wet all you like. You will not find it from the air, and I am not "
              + "going to be the one who tells you where it is so you can take photographs of my uncle.",
              Thread, 4, Requirement.Unknown(Knows.Wreck)),
            LT("bel.thread.wreck.unknown.b",
              "It is a grave. We decided that, all of us, in one evening. If you want it to stop being a "
              + "grave you will have to change eleven minds and mine is the last one.",
              Thread, 5, Requirement.Unknown(Knows.Wreck), Requirement.Standing(0.4, 1)),
            LT("bel.thread.wreck",
              "You have been inside it. Then you know it is tidy in there. That is the part that stays with people.",
              Thread, 5, Requirement.Knows(Knows.Wreck)),
            LT("bel.thread.raft",
              "A raft came ashore two days after, down by the cut, with people in it. They stood on my "
              + "mother's landing and then they walked north, and we let them, and nobody has said it out loud since.",
              Thread, 7, Requirement.Knows(Knows.Wreck), Requirement.Standing(0.4, 1)),
            LT("bel.thread.cairn",
              "So four of them stopped in the hills. I want those names for the post by the landing. We put "
              + "names on it. It is the only thing we use paint for.",
              Thread, 5, Requirement.Knows(Knows.Cairn)),
            LT("bel.thread.roster",
              "Walked out of my water, into a works, and out of that as well. Some people the water simply declines.",
              Thread, 5, Requirement.Knows(Knows.Roster)),
            LT("bel.thread.hoist",
              "You have a hoist on it now. Then we can talk about the island, and after the island we can "
              + "talk about what my people will and will not take you to.",
              ThreadGreeting, 5, Requirement.Fitted("hoist"),
              Requirement.Unknown(Knows.WreckPosition), Requirement.Meetings(2, 99)),
            LT("bel.thread.traded",
              "The lowest ground in the wet. You will see the tail boom. My uncle was the load master and "
              + "he would not have let that cabin door stand open. Somebody walked out of it.",
              Thread, 8, Requirement.Knows(Knows.WreckPosition), Requirement.Unknown(Knows.Wreck)),
            LT("bel.thread.traded.found",
              "So you have been inside it. Good. Now you know why we call it a grave and why it is not one.",
              Thread, 8, Requirement.Knows(Knows.WreckPosition), Requirement.Knows(Knows.Wreck)),
            LT("bel.thread.bye",
              "Straight out over the reeds. And when you are over the deep bit, do not look down. Everybody looks down.",
              ThreadParting, 5, Requirement.Knows(Knows.WreckPosition)),
        });
        return b;
    }

    // =====================================================================================
    //  Osie Crane - Cold Shoulder, the village under the shoulder. Tier 2.
    // =====================================================================================

    /// <summary>
    /// Voice: slow, plain, undecorated, and always about something that lasts - stone,
    /// weather, names. He is the only character who wants to give something away rather
    /// than trade it, and the transaction is deliberately not a transaction (story.md 3).
    /// </summary>
    public static NpcMind Osie() => new()
    {
        Id = "npc.osie",
        Name = "Osie Crane",
        Persona = "a man in his seventies in a village under the shoulder of the uplands. You buried "
                + "four strangers in November six years ago and you have held the names ever since. "
                + "You speak slowly and plainly and you do not decorate anything.",
        Wants = "to hand the names to somebody who will write them down",
        Standing = 0.3,
        Forbidden = { "chosen one", "hero", "the blades", "Sierra four three" },
    };

    public static DialogueBank OsieLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("osie.first", "You will be the aircraft. Sit where the wind is not. This side of the wall.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("osie.first.damaged",
              "You will be the aircraft, and something up here has already had a piece of it. That will be "
              + "the guns on the col. They are not people any more. They are just on.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("osie.return.a", "You found us again. Most do not, twice.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.b", "There is a fire. Sit at it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.c", "Cloud is on the tops. You will have come up the valley, which is the right way.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.d", "Still here. Still the both of us.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.e", "I put a stone back on the wall this morning. That is my report.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.f", "Nothing has happened. Six years of nothing happening is not the worst outcome.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.g", "You bring the sound with you. The dogs go quiet, and then they start up again.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.h", "I will not get up. Not for want of manners.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.i", "Tea. Then say what it is.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("osie.return.j", "Frost last night. Two more stones off the wall. It is a slow argument and I am losing it.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("osie.return.soon",
              "Back inside the day. Something is chasing you, or something is bothering you. Up here those look the same.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("osie.return.long",
              "It has been a season. I had begun to put you with the others I do not expect.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("osie.dry",
              "You had nothing left. If the cloud had come down, you would be a name I was learning.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("osie.low",
              "Thin. There is no fuel in this valley and there has not been since the lorries stopped.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("osie.wrecked",
              "Sit down first. Whatever that is, it will still be broken in an hour.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("osie.patched",
              "Mended, then. Mending is most of what is left to anybody.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("osie.warm",
              "You come back. That is the whole of it. People who come back are a short list now.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("osie.warm.b",
              "I have been leaving the fire banked. Not for any reason. It is easier than lighting it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("osie.warm.c",
              "The dogs have stopped going quiet when you come over. That took them four years and it took me longer.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("osie.bye.a", "Go on down. Keep the shoulder on your right.", "parting", 1),
            L("osie.bye.b", "Mind the col. The wind stands straight up in it.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.c", "Right you are.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.d", "The wall will be here. So will I, most likely.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.e", "Take the tea with you. The cup comes back.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.f", "There will be frost tonight. There is always frost tonight.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.g", "Go careful. That is not an instruction. It is what we say.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.h", "I will get another six metres of wall done while you are away. That is my week.", "parting", 1, Requirement.Meetings(1, 99)),
            L("osie.bye.low",
              "Down the valley, not over. Over is shorter, and over is how people end up on my list.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("osie.bye.damaged",
              "If it lets go, aim for the green. The green is bog, and bog is soft, and soft is the whole of my advice.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("osie.talk.wall",
              "I walk the wall. Stones come off it in the frost and I put them back. Forty years of that before, and near enough six since.",
              "talk", 2),
            L("osie.talk.november",
              "November here is not a month. It is a thing that happens to you.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("osie.talk.remember",
              "They made me the one who remembers. Nobody voted on it. It settled on me, the way jobs do.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("osie.talk.town",
              "We went down to the town once, after. We came back up. That is all I will say about the town.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("osie.talk.certain",
              "A man came through in the spring saying he knew what caused it all. Very certain, he was. "
              + "Certain is not the same as right, and up here we have the time to tell the difference.",
              "talk", 2, Requirement.Meetings(2, 99)),

            // --- The wall ------------------------------------------------------
            L("osie.wry.ivy",
              "Ivy Pell puts them back flat. You do not put them back flat. I take them off again in the evening and do them properly. She knows. We have never discussed it.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("osie.wry.gate",
              "There is a gate on the top field that has not opened since before all this. I oil it twice a year. There is nothing in the field.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("osie.wry.tea",
              "The tea is nettle. I am not going to pretend it is tea. I will say that the third cup goes down easier than the first.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("osie.wry.dogs",
              "Two dogs. One of them is useful. I will not say which in front of them.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("osie.wry.noanswer",
              "You have asked whether I mind being up here on my own. The wall is four hundred metres. I have done sixty of it this year.",
              "greeting", 1, Requirement.Meetings(3, 99)),
            L("osie.wry.bye",
              "Flat stones on the top. It is not the way it is done. Somebody has to hold the line on it.",
              "parting", 1, Requirement.Meetings(3, 99)),

            // --- The thread -------------------------------------------------
            LT("osie.thread.cairn.unknown",
              "There is a cairn on the ridge above. I built it. I will not walk up to it with you, and I will not stop you going.",
              Thread, 4, Requirement.Unknown(Knows.Cairn)),
            LT("osie.thread.cairn.unknown.b",
              "I will say them once. You write them down, and then they are yours, and I can stop.",
              Thread, 5, Requirement.Unknown(Knows.Cairn), Requirement.Standing(0.4, 1)),
            LT("osie.thread.cairn",
              "You have the four. Keep them somewhere that does not get wet. That is all I asked and you have done it.",
              Thread, 5, Requirement.Knows(Knows.Cairn)),
            LT("osie.thread.cairn.three",
              "Seven came up out of the water. I put four under stone. Three walked on over the col in "
              + "November, in what they stood up in, and I did not expect to hear of them again.",
              Thread, 6, Requirement.Knows(Knows.Cairn)),
            LT("osie.thread.engine",
              "One of the three could name every part of an engine. She argued with me about a pump for an "
              + "hour and she was right, and I have thought about that more than I have thought about the burials.",
              Thread, 6, Requirement.Knows(Knows.Cairn), Requirement.Standing(0.3, 1)),
            LT("osie.thread.roster",
              "Took work at a works, then. Four years of it. And then went on again. That is a person going somewhere. It is not a person lost.",
              Thread, 5, Requirement.Knows(Knows.Roster)),
            LT("osie.thread.wray.greet",
              "Then she is alive. Well. I have been the man who holds the dead in this valley for six "
              + "years and today somebody has walked in with the other kind of news.",
              ThreadGreeting, 8, Requirement.Knows(Knows.Wray), Requirement.Meetings(1, 99)),
            LR("osie.thread.chart",
              "There is a way through the tops that keeps you out of sight of the col the whole way. I "
              + "will draw it. It is not worth anything to me and it took a man's whole life to learn.",
              Thread, 5,
              new DialogueReward(DialogueRewardKind.Knowledge, "chart.upland.masking",
                  KnowledgeKind.Chart, "Upland masking routes",
                  "Dead ground through the Cold Shoulder ridges. A life's knowledge, given freely."),
              Requirement.Knows(Knows.Cairn), Requirement.Meetings(2, 99)),
            LT("osie.thread.bye",
              "Write them down properly when you are down. Not up here. You will get it wrong up here.",
              ThreadParting, 5, Requirement.Knows(Knows.Cairn)),
        });
        return b;
    }

    // =====================================================================================
    //  Halvard Ferren - Sawtooth Works, the shop floor. Tier 2.
    // =====================================================================================

    /// <summary>
    /// Voice: arithmetic. Tonnage, shifts, mouths. He is not a villain and he is not sorry,
    /// and the thing he refuses is to be made to feel something about it (story.md 3).
    /// </summary>
    public static NpcMind Ferren() => new()
    {
        Id = "npc.ferren",
        Name = "Halvard Ferren",
        Persona = "a man in his fifties who runs the shop floor at a works that keeps two hundred "
                + "people fed. You talk in tonnage, shifts and mouths. You do not apologise and you "
                + "are not sorry.",
        Wants = "the works to stay useful, because a works that is not useful is a village that starves",
        Standing = -0.2,
        Forbidden = { "chosen one", "hero", "the blades", "Sierra four three" },
    };

    public static DialogueBank FerrenLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("ferren.first",
              "You will want something. Everybody who comes through that gate wants something. State it and I will price it.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("ferren.first.damaged",
              "You have flown a damaged machine into a works. That is either good sense or you had nowhere else. I do not need to know which.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("ferren.return.a", "Mill is running. Talk over it or wait for the shift change.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.b", "Fifteen minutes. I have a furnace that does not care about either of us.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.c", "Back. What is the job.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.d", "Two hundred and eleven ate today. That is the only number I keep. Now yours.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.e", "Boots on the yellow. The crane does not stop for visitors.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.f", "If you have come to trade, trade. If you have come to look, look from there.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.g", "Four tonnes this week. It should have been six. That is my morning, in full.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.h", "You are the only thing that arrives here without a lorry behind it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.i", "Say it while I am walking. I am always walking.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("ferren.return.j", "Night shift is short two fitters and you are not a fitter, so be quick.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("ferren.return.soon",
              "Twice in a shift. Whatever you left here with, it did not do the job.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("ferren.return.long",
              "Months. A works does not miss people, it reorganises around the gap. I am telling you that as a fact, not a reproach.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("ferren.dry",
              "You came in dry. There is oil here. There is no aviation fuel here, and I will not pretend otherwise to keep you talking.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("ferren.low",
              "Short. I can put you up. I cannot put that up.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("ferren.wrecked",
              "That, I can help with. Bring it under the gantry and do not touch anything on the way in.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("ferren.patched",
              "Field repair. Neat enough. Whoever taught you was better than the job you have done.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("ferren.heavy",
              "You are carrying weight. Good. Weight I can use. Opinions I cannot.",
              "greeting", 5, Requirement.Carried(240, 99999)),
            L("ferren.warm",
              "You have been useful to this works. I will say it once and then we will both pretend I did not.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("ferren.warm.b",
              "You are on the gate list now. It means the crane stops. It does not mean anything else.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("ferren.warm.c",
              "Somebody chalked you on the tonnage board. As a line item. I have left it up, which I should not have.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("ferren.bye.a", "Gate is that way. Mind the slag.", "parting", 1),
            L("ferren.bye.b", "Out.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.c", "Bring the weight and we will talk about the rest.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.d", "Do not lift over the stacks. They are not as stacked as they look.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.e", "Shift ends at six. After six I am not the works, I am a man in a chair.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.f", "Yellow line to the gate. It is painted for a reason and the reason is the crane.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.g", "If you are here Friday you will hear the tonnage whether you want it or not.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.h", "Right. I have a furnace.", "parting", 1, Requirement.Meetings(1, 99)),
            L("ferren.bye.low",
              "You will not make the city on what is in that tank. Do not make me right about it.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("ferren.bye.damaged",
              "Do not take that over the ridge line at night. There is nothing on the other side that wants to help you.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("ferren.talk.works",
              "A works that is not useful is a village that starves. That is not a philosophy. It is arithmetic and I do it every Friday.",
              "talk", 3),
            L("ferren.talk.people",
              "I have taken people in. I feed them and I put them on a shift. Anybody who wants thanking for that is not running a works.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("ferren.talk.metal",
              "Steel does not care who owns it. Heat it, roll it, let it cool. It is the last honest thing in the country.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("ferren.talk.furnace",
              "The furnace has not been out in four years. If it goes out, that is the end of this place, and every one of them knows it.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("ferren.talk.debt",
              "Everything here runs on debts. Not money. Debts. Somebody in the city is owed by me, and one "
              + "day it will be collected, and I will pay it, because that is how the lights stay on.",
              "talk", 3, Requirement.Meetings(2, 99)),

            // --- Friday --------------------------------------------------------
            L("ferren.wry.friday",
              "I read the tonnage out every Friday at the gate. Attendance is not compulsory. Attendance is low.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("ferren.wry.board",
              "There is a board with the week's figures chalked on it. Somebody rubs out the six and makes it an eight. Every week. I have not found out who and I have stopped enjoying looking.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("ferren.wry.mug",
              "That is a works mug. It stays at the works. I am aware of how that sounds, and it stays at the works.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("ferren.wry.title",
              "People call me the foreman. There has been nobody to be foreman of since the union folded. I have not corrected it and I am not going to.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("ferren.wry.noanswer",
              "You asked me whether I sleep. Four tonnes. It should have been six.",
              "greeting", 1, Requirement.Meetings(3, 99)),
            L("ferren.wry.bye",
              "Friday. The gate. Ten minutes. You would be the first outsider ever to come to one.",
              "parting", 1, Requirement.Meetings(3, 99)),

            // --- The thread -------------------------------------------------
            LT("ferren.thread.roster.unknown",
              "The office keeps a roster and it goes back to the start. I am not in the habit of opening it "
              + "for people who have not done anything for this works.",
              Thread, 4, Requirement.Unknown(Knows.Roster)),
            LT("ferren.thread.roster",
              "You want me to feel something about it. I fed her for four years. You can decide what that was.",
              Thread, 6, Requirement.Knows(Knows.Roster)),
            LT("ferren.thread.roster.b",
              "Four years on my floor and she never once asked to be anywhere else, and then one morning she "
              + "asked for a coat and went. I told her not to. I was not being kind. I was short a fitter.",
              Thread, 6, Requirement.Knows(Knows.Roster)),
            LT("ferren.thread.wray.greet",
              "She got there, then. I had it at about one in four. I am not often glad to be wrong about a number.",
              ThreadGreeting, 8, Requirement.Knows(Knows.Wray), Requirement.Meetings(1, 99)),
            LT("ferren.thread.magazine",
              "A hardened magazine in the ash. If it was sealed before it burned, the contents will be as "
              + "they were put in. That is what hardened means, and it is the only good sentence in this conversation.",
              Thread, 5, Requirement.Knows(Knows.Magazine)),
            LT("ferren.thread.window",
              "Ninety minutes. I have run a shift change in less and lost a man doing it. Plan it like a shift change and do not improvise.",
              Thread, 5, Requirement.Knows(Knows.Window)),
            LR("ferren.thread.chart",
              "The chart shows where the emitters sit, near enough. It came off a lorry driver who is dead "
              + "now and it is accurate, and I want the delivery made before you take it.",
              Thread, 5,
              new DialogueReward(DialogueRewardKind.Knowledge, "chart.emitter.locations",
                  KnowledgeKind.ThreatSite, "Emitter locations",
                  "Where the emitters sit in the outer tiers. Off a lorry driver who knew the roads."),
              Requirement.Knows(Knows.Cairn), Requirement.Meetings(2, 99)),
            LT("ferren.thread.bye",
              "Do the delivery or do not. The roster will be in the office either way, and I will be at the furnace.",
              ThreadParting, 5, Requirement.Knows(Knows.Roster)),
        });
        return b;
    }

    // =====================================================================================
    //  Sera Wray - Ashmount, the cut. Tier 3. The object of the search.
    // =====================================================================================

    /// <summary>
    /// Voice: diagnosis. She hears the aircraft before she sees it and says what is wrong
    /// with it, and she does that in her first line, before any reunion, because the
    /// reunion is not what she is for (story.md 3). She is the only person besides the
    /// player who calls the aircraft Hugh, and that is how the player knows (story.md 9).
    ///
    /// D-008: flight engineer. She has never flown one and never says she has.
    /// </summary>
    public static NpcMind Wray() => new()
    {
        Id = "npc.wray",
        Name = "Sera Wray",
        Persona = "a flight engineer of sixty-one with a bad hip who pumps water for three hundred "
                + "people out of a flooded cut. You diagnose before you greet. You call the aircraft "
                + "Hugh.",
        Wants = "three hundred people to keep having water",
        Standing = 0.5,
        Forbidden = { "chosen one", "hero", "destiny" },
    };

    public static DialogueBank WrayLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("wray.first",
              "Your track is out. I could hear it from the cut. How long has it been doing that?",
              "greeting", 8, Requirement.Meetings(0, 0)),
            L("wray.first.damaged",
              "Before anything else. Shut it down and let me look. You have been flying Hugh with "
              + "something loose in him and I could hear which side from the water.",
              "greeting", 10, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("wray.return.a", "Torque split is better. You have been flying him kinder.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.b", "Leave it running a moment. There. That. Now shut down.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.c", "Hugh sounds tired. So does everybody.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.d", "Chip detector. Have you looked at it this week. Look at it in front of me.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.e", "You came in flat. Flat is fine. Flat is easier on the head than pretty.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.f", "Sit. I will talk and you will let that gearbox cool.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.g", "Blade tape has gone on the advancing side. I can see it from here and I am sixty-one.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.h", "Collective friction is off again. I can tell from the flare.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.i", "Pump is up, head is holding, and I have twenty minutes.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("wray.return.j", "Tail rotor gearbox. Smell it when you walk past. If it smells of anything at all, tell me.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("wray.return.soon",
              "You have been up twice today. Those are hours on a head that does not have many left in it.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("wray.return.long",
              "Long time turning. I want the hours before I want the news.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("wray.dry",
              "You landed on vapour with a rotor in that condition. I taught you better than that, and I taught you in person.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("wray.low",
              "Fuel first, talking after. Nothing you have to say improves on a full tank.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("wray.wrecked",
              "No. Do not tell me what happened. Tell me what it sounded like, in order, and I will tell you what happened.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("wray.patched",
              "Somebody has been kind to that with wire. Kindness is not tracking. I will do the tracking.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("wray.warm",
              "You kept him flying six years on your own. I have read the logbook. You did not do a bad job. "
              + "I will not say more than that with a spanner in my hand.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("wray.warm.b",
              "I have written the next four inspections out on a card for you. You will lose the card. I have made two.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("wray.warm.c",
              "The sluice notice has gone. Somebody took it down. I am choosing to believe it was the weather.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("wray.bye.a", "Go and be gentle with him.", "parting", 1),
            L("wray.bye.b", "Twenty-five hours. Then you come back and I sign it.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.c", "Down through the cut, not over the terrace. The terrace is people.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.d", "Note the start time. You never note the start time.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.e", "Hydraulics. Check them cold, not hot. Everybody checks them hot.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.f", "Log the hours. The actual hours. Not the ones you think are fair.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.g", "Bring the cup back. The one on the housing is not the first I have lost.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.h", "Gently on the pedals out of the cut. It is not a race and there is nobody to race.", "parting", 1, Requirement.Meetings(1, 99)),
            L("wray.bye.low",
              "Do not plan on the reserve. The reserve is for the day the plan was wrong.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("wray.bye.damaged",
              "Straight and level, no turns past forty degrees, and if the vibration changes at all you put "
              + "him on the ground. Any ground.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("wray.talk.turbine",
              "A ventilation fan, a shaft, and a great deal of arguing with water. Three hundred people "
              + "drink because of it. That is a better machine than anything I ever signed off.",
              "talk", 3),
            L("wray.talk.nose",
              "I painted the name because it was the widest brush in the hangar. Everybody has made it mean something since. It meant I was in a hurry.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("wray.talk.hip",
              "The hip is from the walk, not from the aircraft. Six years of pumping has not helped it. I am not asking for sympathy, I am explaining the stick.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("wray.talk.logbook",
              "I signed that logbook every twenty-five hours for nine years. My handwriting is in there more than yours is.",
              "talk", 3, Requirement.Meetings(2, 99)),
            L("wray.talk.believed",
              "For two years I thought he had burned. The last anybody had of that tail number was a field "
              + "that had been strafed. By the time I heard the sound go over, I was the reason three hundred people had water.",
              "talk", 3, Requirement.Meetings(2, 99)),

            // --- Answering a different question ---------------------------------
            // Her comic register and her characterisation are the same thing: asked about
            // herself, she reports on the aircraft instead, flatly, and does not notice.
            L("wray.wry.hip",
              "You have asked about the hip twice now. The gearbox is the thing here with a problem.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("wray.wry.tools",
              "The spanners are in order. Not tidy. In order. There is a difference and nobody in this city has ever wanted to hear it.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("wray.wry.tea",
              "I will have tea in a minute. I have been saying that for six years. There is a cup on the turbine housing from the spring.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("wray.wry.notice",
              "Somebody put a notice up asking people not to lean on the sluice. I put it up. They lean on it. I have since laminated the notice.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("wray.wry.thanks",
              "People here thank me for the water. I tell them it is a fan and a shaft. They keep thanking me. It is a fan and a shaft.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("wray.wry.bye",
              "Note the start time. You will not note the start time.",
              "parting", 1, Requirement.Meetings(2, 99)),

            // --- The thread -------------------------------------------------
            LT("wray.thread.arrive",
              "You did not come looking for a person. You came looking for a rotor. That is all right. They "
              + "are the same errand, and I am the one who knows where both of them are.",
              Thread, 6, Requirement.Knows(Knows.Wray), Requirement.Unknown(Knows.Magazine)),
            LT("wray.thread.magazine",
              "A hardened magazine, in the ash, under the second aerostat. I wrote the inventory. Two matched "
              + "pairs, in grease, in racks. I have known exactly where they are for six years and had no way on earth to reach them.",
              Thread, 6, Requirement.Knows(Knows.Magazine)),
            LR("wray.thread.load",
              "Four hundred and twenty kilograms with the grips and the tie bars. You will want the hook "
              + "fitted and the long-range tank off, and you will not like the second part.",
              Thread, 6,
              new DialogueReward(DialogueRewardKind.Knowledge, Knows.Load,
                  KnowledgeKind.Rumour, "Load specification — blade pair",
                  "420 kg with grips and tie bars. Cargo hook required. Long-range tank removed to make the weight."),
              Requirement.Knows(Knows.Magazine)),
            LT("wray.thread.hook",
              "The hook is on. Good. You did not ask me and that tells me you have already done the arithmetic.",
              Thread, 7, Requirement.Knows(Knows.Magazine), Requirement.Fitted("hook")),
            LT("wray.thread.ceiling",
              "Two hundred and forty hours on that head since anybody tracked it. I do not say it will not "
              + "hold a hover at that ceiling. I say you will not enjoy the hover it holds.",
              Thread, 6, Requirement.Knows(Knows.Window)),
            LT("wray.thread.stay",
              "Two days. I will give you two days and then I come back to the pump. Do not spend the two days "
              + "trying to change that. We will both get tired and the answer will be the same.",
              Thread, 6, Requirement.Knows(Knows.Magazine), Requirement.Standing(0.5, 1)),
            LT("wray.thread.window",
              "Every eighth night, ninety minutes. We go on the eighth and we come back heavy, which is the "
              + "wrong way round, and there is nothing whatever to be done about it.",
              Thread, 6, Requirement.Knows(Knows.Window)),
            LT("wray.thread.window.b",
              "I will call the torque and the temperatures. You fly. Do not look at me and do not answer. If I say down, it is down.",
              Thread, 6, Requirement.Knows(Knows.Window)),
            LT("wray.thread.window.greet",
              "Two hundred and forty-one hours on that head since anybody tracked it. Tonight is the eighth. Start it.",
              ThreadGreeting, 8, Requirement.Knows(Knows.Window), Requirement.Meetings(1, 99)),
            LT("wray.thread.bye",
              "Go and do the ordinary flying. I will still be here, and the pump will still be here, and the eighth night comes round whatever we do.",
              ThreadParting, 5, Requirement.Knows(Knows.Window)),

            // --- Boarding: the moment she gets in (D-090) ----------------------------
            // Gated on Window knowledge + sufficient standing + at least two meetings.
            // The reward sets Progress.PassengerAboard, which adds her mass to the
            // airframe and starts the copilot callout system.
            LR("wray.board",
              "I am in. Do not wait for me to be comfortable. Go.",
              Thread, 10,
              new DialogueReward(DialogueRewardKind.Passenger, "wray"),
              Requirement.Knows(Knows.Window), Requirement.Standing(0.5, 1),
              Requirement.Meetings(2, 99)),
        });
        return b;
    }

    // =====================================================================================
    //  Juno Kessel - Ashmount, the tether crew's billet. Tier 3.
    // =====================================================================================

    /// <summary>
    /// Voice: deniability. Every sentence is hedged, walked back, or framed as a position
    /// rather than a statement. She is selling a timetable, not a way in, and the
    /// distinction is the only self-respect she has left (story.md 3).
    /// </summary>
    public static NpcMind Juno() => new()
    {
        Id = "npc.juno",
        Name = "Juno Kessel",
        Persona = "a woman in her late twenties who runs the aerostat tether crew because the "
                + "alternative was worse. You hedge everything. You would rather this conversation "
                + "had not happened.",
        Wants = "to not be the person who winches up the thing that shoots people",
        Standing = -0.4,
        Forbidden = { "chosen one", "hero", "the blades", "Wray" },
    };

    public static DialogueBank JunoLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("juno.first", "I did not see you land. Nobody did. Let us keep that as the position.",
              "greeting", 3, Requirement.Meetings(0, 0)),
            L("juno.first.damaged",
              "Whatever put those holes in you, it was not ours, and if it was ours I would not know, and I "
              + "have already told you more than I meant to.",
              "greeting", 5, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("juno.return.a", "You are early. Or late. I do not know what you are on time for.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.b", "Walk with me. Standing still near the winch is how people get noticed.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.c", "Say it quietly. The crew are all right. It is the habit I do not trust.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.d", "Back. I had half hoped you would not be.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.e", "Nothing has changed here, which is the worst thing I can tell anybody.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.f", "Do not bring it in over the terrace again. People looked up.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.g", "Eleven minutes and then I am on the drum.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.h", "You keep coming back to the one person here who cannot help you. There is something almost restful about it.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.i", "Whatever this is, it is not a conversation we are having.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("juno.return.j", "Bag is up. Bag is always up. That is the job description in full.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("juno.return.soon",
              "Twice. Twice is a pattern, and patterns get written down by people who are paid to write things down.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("juno.return.long",
              "I assumed you were dead. I did not check. Checking is also a thing people notice.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("juno.dry",
              "You came into a missile belt on an empty tank. I want it on record that I find that insane, "
              + "and I am the one who winches up the radar.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("juno.low",
              "You will not get out on that, and you cannot sit here. Those are the two facts and they do not like each other.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("juno.wrecked",
              "If that came off the belt, then the belt worked, and I helped. I would rather you did not tell me which way you came in.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("juno.patched",
              "Patched. Everything here is patched. The bag is patched, the winch is patched, I am patched.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("juno.warm",
              "I am going to tell you something, and afterwards I am going to say I did not. You already know which of those is true.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("juno.warm.b",
              "The crew have stopped logging you. I did not ask them to. I have not asked them why either.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("juno.warm.c",
              "There is a second mug now. It does not have a name on it. Do not put a name on it.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("juno.bye.a", "Go. Not that way. The other way.", "parting", 1),
            L("juno.bye.b", "We did not speak.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.c", "Down the gully and stay under the terrace line. The bag does not see what the terrace hides.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.d", "If anybody asks, you were lost.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.e", "I am going to walk the other way now. Do not follow for the first minute.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.f", "The drum starts on the hour. Be somewhere else on the hour.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.g", "You were never here and I was on shift.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.h", "If the bag is up when you go, stay under the terrace. If it is down, go faster.", "parting", 1, Requirement.Meetings(1, 99)),
            L("juno.bye.low",
              "Do not climb out of here. Climbing is what the whole thing is for.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("juno.bye.damaged",
              "If it is trailing smoke, do not come back to this side of the city. I cannot make that not be seen.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("juno.talk.crew",
              "Nine of us on the drum. Four want out, three have stopped saying so, and two have decided it is a job. I will not tell you which I am.",
              "talk", 3),
            L("juno.talk.bag",
              "The bag has four swaps left in it, maybe five. After that it is fabric and wishes. Nobody upstairs has a plan for that and I have stopped asking.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("juno.talk.out",
              "I want out. Everybody here wants out. Wanting is cheap, and the walk is three hundred kilometres.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("juno.talk.city",
              "Three hundred people in a city built for ninety thousand. You can hear yourself walk.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("juno.talk.timetable",
              "I do not have a door and I could not open one. I have a timetable. A timetable is not a door.",
              "talk", 3, Requirement.Meetings(2, 99)),

            // --- The mug -------------------------------------------------------
            L("juno.wry.mug",
              "There is a mug in the winch house with my name on it in tape. Somebody has been using it. I have not raised it formally.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("juno.wry.forms",
              "We still fill in the log. Time up, time down, gas pressure. Nobody reads it. I have been initialling it for four years in case somebody starts.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("juno.wry.rehearsed",
              "I have a thing I say when people ask what I do. I say I am on the winch. I have practised the way I say it so that it closes the subject. It does not close the subject.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("juno.wry.rota",
              "I did the rota so that I am never on the same shift as Emlin. Emlin does not know that. Emlin thinks it is the rota.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("juno.wry.noanswer",
              "You asked whether I am all right. Bag pressure is nominal. I know that is not an answer. It is the one I am giving.",
              "greeting", 1, Requirement.Meetings(3, 99)),
            L("juno.wry.bye",
              "If anybody mentions the mug, I did not bring it up.",
              "parting", 1, Requirement.Meetings(3, 99)),

            // --- The thread -------------------------------------------------
            LT("juno.thread.magazine.unknown",
              "Whatever you are working up to asking, ask it somewhere that is not under the tether.",
              Thread, 4, Requirement.Unknown(Knows.Magazine)),
            LT("juno.thread.magazine",
              "The ash country. Right. So you want to know when it is not looking, which is a different "
              + "question to the one you started with and a considerably more expensive one.",
              Thread, 5, Requirement.Knows(Knows.Magazine), Requirement.Unknown(Knows.Window)),
            LT("juno.thread.window",
              "I am not selling you a way in. I am selling you an hour and a half of nobody looking down. "
              + "What you do with it is not my business and I would like it kept that way.",
              Thread, 6, Requirement.Knows(Knows.Window)),
            LT("juno.thread.window.b",
              "Every eighth night, from the moment the bag comes off the mast. Ninety minutes, and the last "
              + "ten of those are them putting it back up early because somebody is cold.",
              Thread, 6, Requirement.Knows(Knows.Window)),
            LR("juno.thread.approach",
              "The gully runs south-east to the spine. Stay below the terrace and you are below the bag. "
              + "When it is down, you have the full ninety and nobody on the ground is looking up because "
              + "everybody on the ground is on the drum.",
              Thread, 6,
              new DialogueReward(DialogueRewardKind.Knowledge, Knows.Approach,
                  KnowledgeKind.Rumour, "Approach — under the terrace",
                  "South-east gully to the spine. Stay below the terrace, below the aerostat. During the window the crew is on the drum."),
              Requirement.Knows(Knows.Window)),
            LT("juno.thread.lift",
              "When you go, you come back over the tether field. Low. I will be on the drum and I will be "
              + "the one not holding a torch. That is the whole of the arrangement and I will not say it twice.",
              Thread, 6, Requirement.Knows(Knows.Window), Requirement.Standing(-0.2, 1)),
            LT("juno.thread.passenger",
              "The woman from the cut is in the right seat. I can see her from here. I did not need to "
              + "know that and now I do.",
              ThreadGreeting, 7, Requirement.Knows(Knows.Window), Requirement.Passenger("wray")),
            LT("juno.thread.wray",
              "The woman at the cut. Everybody knows who she is and nobody bothers her, which in this city is "
              + "the highest honour going.",
              Thread, 5, Requirement.Knows(Knows.Wray)),
            LT("juno.thread.bye",
              "Eighth night. Do not confirm it, do not repeat it, and do not thank me on the way out.",
              ThreadParting, 5, Requirement.Knows(Knows.Window)),
        });
        return b;
    }

    // =====================================================================================
    //  "Sparrow" - The Scald, the camp in the ash. Tier 3.
    // =====================================================================================

    /// <summary>
    /// Voice: instructions. Where to stand, what to put down, which way to walk. No small
    /// talk, no names for the crew - the player never learns them because the player never
    /// earns them (story.md 9). Visibly dying of the ash and matter-of-fact about it.
    /// </summary>
    public static NpcMind Sparrow() => new()
    {
        Id = "npc.sparrow",
        Name = "Sparrow",
        Persona = "a scavenger crew leader in the ash country, visibly dying of it and aware of the "
                + "fact. You speak in instructions. You do not do small talk and you do not give "
                + "names.",
        Wants = "not to be found, and failing that, medical stock",
        Standing = -0.6,
        Forbidden = { "chosen one", "hero", "Wray" },
    };

    public static DialogueBank SparrowLines()
    {
        var b = new DialogueBank();
        b.AddRange(new[]
        {
            L("sparrow.first",
              "Stop there. Not because of me. There is a wire at your boot and it is not mine.",
              "greeting", 8, Requirement.Meetings(0, 0)),
            L("sparrow.first.damaged",
              "Stop there. You have come a long way loud and broken, and there is nobody out here who will be pleased about either.",
              "greeting", 10, Requirement.Meetings(0, 0), Requirement.Condition(0, 0.6)),

            L("sparrow.return.a", "Stand where you stood last time.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.b", "Mask on before you talk. The talking is what puts it into you.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.c", "Still breathing. Both of us, apparently.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.d", "We had you at twelve kilometres. There is nothing else in the sky, so there is nothing else it could be.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.e", "Do not sit on the drums.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.f", "Crew are out. It is me, the fire, and a great deal of grey.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.g", "Say your piece from there. The fire is not for guests.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.h", "I have had a bad week. That is not conversation. That is a warning about my manners.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.i", "Hands where they were. It is not personal, it is just the arrangement.",
              "greeting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.return.j", "Wind is off the depot today. Keep it short.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("sparrow.return.soon",
              "Twice in a day. Either you forgot something or somebody has told you I am easier to deal with than I am.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(0, 14)),
            L("sparrow.return.long",
              "Long gap. Two of us went in it. Do not ask which two.",
              "greeting", 4, Requirement.Meetings(1, 99), Requirement.HoursSince(200, 99999)),

            L("sparrow.dry",
              "Empty, in the ash, on your own. If you go down out here nobody comes. Not us. We would take the tank and go.",
              "greeting", 6, Requirement.Fuel(0, 0.1)),
            L("sparrow.low",
              "You are short, and there is nothing here that burns clean. Sit with it or leave now.",
              "greeting", 4, Requirement.Fuel(0, 0.25)),
            L("sparrow.wrecked",
              "That is depot guns. They are still on. They will always be on, and they do not need anybody to feed them.",
              "greeting", 6, Requirement.Condition(0, 0.35)),
            L("sparrow.patched",
              "Wire and grease. Out here that is a religion.",
              "greeting", 4, Requirement.Condition(0.35, 0.7)),
            L("sparrow.medical",
              "You are carrying medical. I can see the box from here and so can everybody behind me. Put the tin on the drum and walk where I tell you.",
              "greeting", 7, Requirement.Medical(true)),
            L("sparrow.warm",
              "You turn up with things we need and no speech attached to them. That is the closest thing to a friend this place has had.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("sparrow.warm.b",
              "You can stand nearer the fire. Not in the chair. Nearer the fire.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),
            L("sparrow.warm.c",
              "Nobody has raised a rifle at you in four visits. That was a decision. It was taken without a meeting and it can be untaken.",
              "greeting", 3, Requirement.Standing(0.5, 1), Requirement.Meetings(3, 99)),

            L("sparrow.bye.a", "Out the way you came. Exactly the way you came.", "parting", 1),
            L("sparrow.bye.b", "Go.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.c", "Do not come at night. At night we shoot first and apologise to the daylight.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.d", "Lift from where you landed. Not a metre to either side.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.e", "Wind is right for you. That happens about once a fortnight.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.f", "Anything you leave behind is ours. That is not a threat. It is the rule for everybody.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.g", "Walk the line of pegs. There are pegs for a reason.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.h", "Everyone in the ash will hear you go. Everyone in the ash always does.", "parting", 1, Requirement.Meetings(1, 99)),
            L("sparrow.bye.low",
              "You have fuel for one mistake. Make it early, where the ground is flat.",
              "parting", 4, Requirement.Fuel(0, 0.3)),
            L("sparrow.bye.damaged",
              "Whatever is wrong with it will be worse in the ash. It gets into everything. That is the entire lesson of this place.",
              "parting", 5, Requirement.Condition(0, 0.6)),

            L("sparrow.talk.ash",
              "The ash does not settle. It gets up with the wind and it goes in, and then one morning you "
              + "cough and it is grey, and after that you are on a clock.",
              "talk", 3),
            L("sparrow.talk.crew",
              "There were fourteen. There are nine. I do not give you their names because you will not be here long enough to need them.",
              "talk", 3, Requirement.Meetings(1, 99)),
            L("sparrow.talk.depot",
              "The depot still has power going to something. You can hear it if you stand still, which nobody does.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("sparrow.talk.ground",
              "Nothing comes up out of this ground and nothing will. We are not farming. We are waiting.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("sparrow.talk.found",
              "We did not come out here to be found. That is not a hard rule. It is only the one that has kept anybody alive.",
              "talk", 3, Requirement.Meetings(2, 99)),

            // --- The chair -----------------------------------------------------
            L("sparrow.wry.chair",
              "There is a chair. I carried that chair eleven kilometres. Nobody sits in it. Including me.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("sparrow.wry.drums",
              "I said do not sit on the drums. Two of mine sit on the drums. I have said it about four hundred times and I will say it again tomorrow.",
              "greeting", 1, Requirement.Meetings(2, 99)),
            L("sparrow.wry.tins",
              "Tins get stacked label out. It costs nothing, and it means you can find the one you want in the dark, and we are quite often in the dark.",
              "talk", 2, Requirement.Meetings(1, 99)),
            L("sparrow.wry.name",
              "Sparrow is not a name. It is what somebody called me once in front of the others. You do not get to choose.",
              "talk", 2, Requirement.Meetings(2, 99)),
            L("sparrow.wry.noanswer",
              "You asked how long I have got. The wind is off the depot today. Keep it short.",
              "greeting", 1, Requirement.Meetings(3, 99)),
            L("sparrow.wry.bye",
              "Label out. It costs nothing.",
              "parting", 1, Requirement.Meetings(3, 99)),

            // --- The thread -------------------------------------------------
            LT("sparrow.thread.magazine.unknown",
              "Whatever is in that depot, it has been in there since it burned, and everybody who has gone in after it is also still in there.",
              Thread, 4, Requirement.Unknown(Knows.Magazine)),
            LT("sparrow.thread.magazine",
              "The magazine. Sealed from the outside, which means somebody had time, which means it was not "
              + "an accident. The last crew who opened a door in that place did not come back out of it.",
              Thread, 5, Requirement.Knows(Knows.Magazine)),
            LT("sparrow.thread.window",
              "The eighth night. You know about the eighth night. Then you have talked to the tether crew, "
              + "and you should know that the tether crew talk as well.",
              Thread, 5, Requirement.Knows(Knows.Window)),
            LR("sparrow.thread.window.paid",
              "Tin on the drum. Then I walk you in as far as the revetment and no further, and what happens at the door is yours.",
              Thread, 7,
              new DialogueReward(DialogueRewardKind.Knowledge, Knows.Passage,
                  KnowledgeKind.Rumour, "Passage — The Scald magazine",
                  "Sparrow's crew will walk you to the revetment. What happens at the door is yours."),
              Requirement.Knows(Knows.Window), Requirement.Medical(true)),
            LT("sparrow.thread.window.unpaid",
              "You want the door in the ash and you have come with nothing. That is not a negotiation. That is a visit.",
              Thread, 6, Requirement.Knows(Knows.Magazine), Requirement.Medical(false)),
            LT("sparrow.thread.door",
              "The door is mechanical. Push, quarter turn, pull. It was not built to keep people out. "
              + "It was built to keep the weather off what is inside.",
              Thread, 6, Requirement.Knows(Knows.Passage)),
            LR("sparrow.thread.bye",
              "Revetment, racks, grease. Take the two you came for and leave the rest where they are, because somebody after you will want them.",
              ThreadParting, 5,
              new DialogueReward(DialogueRewardKind.SlingLoad, "blade_pair"),
              Requirement.Knows(Knows.Passage), Requirement.Fitted("hook")),
        });
        return b;
    }

    // =====================================================================================
    //  Memory
    // =====================================================================================

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

    // =====================================================================================
    //  Generic people
    // =====================================================================================

    /// <summary>
    /// Mirror of the game layer's RegionKind, in declaration order, so the game can cast
    /// straight across. Lives here because sim/ must not reference Godot.
    /// </summary>
    public enum RegionTag { Basin, Farmland, Exurb, City, Industrial, Upland, Wetland, Ashfield }

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

    /// <summary>What a person in this region has on their mind, for the coda prompt.</summary>
    private static string RegionWants(RegionTag r) => r switch
    {
        RegionTag.Basin      => "the water to hold out and the dust to stay down",
        RegionTag.Farmland   => "a crop that comes up, for once",
        RegionTag.Exurb      => "the empty houses to stay empty and the scrap to keep coming",
        RegionTag.City       => "the water shared fairly and nobody looking up",
        RegionTag.Industrial => "the furnace lit and everybody on a shift",
        RegionTag.Upland     => "the wall standing and the valley left alone",
        RegionTag.Wetland    => "the boards mended and nobody else drowned",
        RegionTag.Ashfield   => "a week where the wind does not get up",
        _ => "to keep this place going and for people to stop dying out there",
    };

    /// <summary>
    /// A generic settler for sites without a named character. Each gets a deterministic
    /// name and persona. The persona mentions the site so the coda can contextualise.
    /// </summary>
    public static NpcMind Settler(int seed, string siteName) =>
        Settler(seed, siteName, RegionTag.Basin, SiteKindTag.Settlement);

    /// <summary>
    /// The same, told where it is. Two people in two regions should not have the same
    /// things on their mind, and the coda prompt is where that starts.
    /// </summary>
    public static NpcMind Settler(int seed, string siteName, RegionTag region, SiteKindTag kind)
    {
        var rng = new Random(seed * 104729 + 3);
        return new NpcMind
        {
            Id = $"npc.settler.{seed}",
            Name = SettlerNames[rng.Next(SettlerNames.Length)],
            Persona = string.Format(SettlerPersonas[rng.Next(SettlerPersonas.Length)], siteName),
            Wants = RegionWants(region),
            Standing = 0.0,
            Forbidden = { "chosen one", "hero", "save the world", "Wray", "Sierra four three", "the blades" },
        };
    }

    /// <summary>
    /// Lines shared by all generic settlers. Each NPC gets its own bank instance so
    /// recency tracking is per-character.
    /// </summary>
    public static DialogueBank SettlerLines() =>
        SettlerLines(RegionTag.Basin, SiteKindTag.Settlement);

    /// <summary>
    /// Generic lines, plus what this region sounds like, plus what this kind of place
    /// sounds like.
    ///
    /// story.md is explicit that most sites have nobody named and that those places still
    /// have to carry the world. An Ashfield fuel cache and a Basin farmstead share the
    /// common bank and nothing else: the region layer supplies what is on people's minds
    /// and the kind layer supplies what they are standing next to. Because the region and
    /// kind lines are no more specific than the common ones, the selector's LRU rotation
    /// mixes all three freely, and two sites in different regions diverge inside a couple
    /// of exchanges.
    /// </summary>
    public static DialogueBank SettlerLines(RegionTag region, SiteKindTag kind)
    {
        var b = new DialogueBank();
        b.AddRange(CommonSettlerLines());
        b.AddRange(RegionLines(region));
        b.AddRange(KindLines(kind));
        return b;
    }

    private static IEnumerable<DialogueLine> CommonSettlerLines() => new[]
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
        L("settler.return.g", "Nothing to report. There never is.",
          "greeting", 1, Requirement.Meetings(1, 99)),
        L("settler.return.h", "Give us a minute. Everyone comes out when you land.",
          "greeting", 1, Requirement.Meetings(1, 99)),
        L("settler.return.i", "Same as it was. Same as it will be.",
          "greeting", 1, Requirement.Meetings(1, 99)),
        L("settler.return.j", "You want something or you want somewhere to sit. Either is fine.",
          "greeting", 1, Requirement.Meetings(2, 99)),

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
        L("settler.bye.b", "Go.", "parting", 1, Requirement.Meetings(1, 99)),
        L("settler.bye.c", "Safe skies.", "parting", 1, Requirement.Meetings(1, 99)),
        L("settler.bye.d", "Come back through if you are passing.", "parting", 1, Requirement.Meetings(1, 99)),
        L("settler.bye.e", "Right, then.", "parting", 1, Requirement.Meetings(1, 99)),
        L("settler.bye.f", "Do not lift over the sheds. They are not sheds, they are a lean-to and a hope.", "parting", 1, Requirement.Meetings(1, 99)),
        L("settler.bye.g", "Somebody will be up when you get back, whenever that is.", "parting", 1, Requirement.Meetings(1, 99)),
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
          "talk", 2, Requirement.Meetings(1, 99)),
        L("settler.talk.sound",
          "You can hear it a long way off. Everyone can.",
          "talk", 3, Requirement.Meetings(2, 99)),
        L("settler.talk.trade",
          "If you find parts, I know people who need them. Everybody needs something.",
          "talk", 2, Requirement.Meetings(1, 99)),
        L("settler.talk.relay",
          "The relay was on last week. Could not make out the words, but it was on.",
          "talk", 2, Requirement.Meetings(1, 99)),
        L("settler.talk.cause",
          "Somebody comes through every year with a reason for all of it. Never the same reason.",
          "talk", 2, Requirement.Meetings(1, 99)),

        // --- Mundane, played straight ---
        // The generic register needs the same comedy the named people have, or every
        // unnamed place reads as solemn. It is never a joke: it is a small grievance or a
        // small system, stated flatly by somebody who is not trying to be funny, and it
        // never undercuts the weight of where they are living.
        L("settler.wry.rota",
          "There is a rota. It works. Two people think it does not work and they are the two who wrote it.",
          "talk", 2, Requirement.Meetings(2, 99)),
        L("settler.wry.inventory",
          "We did a count in the spring. It came out eleven short. We did it again and it came out nine over. Nobody has suggested a third count.",
          "talk", 2, Requirement.Meetings(2, 99)),
        L("settler.wry.cassette",
          "There is a box of cassettes somebody brought in. Four of them play. Two of those four are the same one.",
          "talk", 2, Requirement.Meetings(1, 99)),
        L("settler.wry.cassette.b",
          "We had a vote on the tapes. It did not settle it. We play them in order now, which suits nobody and is at least a system.",
          "talk", 2, Requirement.Meetings(2, 99)),
        L("settler.wry.fence",
          "That fence has been half a metre wrong for four years. Everybody knows. Nobody is going to be the one who says it at a meeting.",
          "talk", 2, Requirement.Meetings(2, 99)),
        L("settler.wry.greet",
          "You have landed on the drying ground. It is fine. I am telling you rather than saying nothing about it for a year.",
          "greeting", 1, Requirement.Meetings(1, 99)),
        L("settler.wry.greet.b",
          "Everyone came out to look the first time. Now it is about four of us. That is not a comment on you.",
          "greeting", 1, Requirement.Meetings(2, 99)),
        L("settler.wry.bye",
          "Not the drying ground next time. Or the drying ground. It is not worth a conversation.",
          "parting", 1, Requirement.Meetings(2, 99)),

        // ---- Night arrival (D-094) -------------------------------------------------
        // ArrivedAtNight is populated from SceneMood.SunNow.IsNight. Flying at night is
        // unusual and dangerous and people should notice.
        L("settler.night.greet",
          "You flew that in the dark. Nobody does that. Nobody should.",
          "greeting", 6, Requirement.Night(), Requirement.Meetings(0, 0)),
        L("settler.night.greet.b",
          "I heard you before I saw the light. Nobody comes in at night.",
          "greeting", 6, Requirement.Night(), Requirement.Meetings(0, 0)),
        L("settler.night.return",
          "At this hour. Right. Come in.",
          "greeting", 5, Requirement.Night(), Requirement.Meetings(1, 99)),
        L("settler.night.return.b",
          "You are either very good or very lost. Come in out of the dark.",
          "greeting", 5, Requirement.Night(), Requirement.Meetings(1, 99)),
        L("settler.night.bye",
          "Wait for light. That is not a suggestion.",
          "parting", 5, Requirement.Night()),
        L("settler.night.bye.b",
          "If you must go, follow the river. It is the only thing you can see.",
          "parting", 5, Requirement.Night()),

        // ---- Story-progress-aware (D-094) ------------------------------------------
        // Settlers react to what the player has learned. These use Knows() so they fire
        // only after the relevant search beat. Weight 5-6 so they beat common talk but
        // not region-specific lines. Per story.md 9: nobody tells you where to fly next,
        // nobody explains the collapse, nobody calls the aircraft Hugh.

        // After Beat 2 — someone is asking after a callsign. Word travels.
        L("settler.heard.callsign",
          "Somebody was asking after a callsign. Sierra something. That you?",
          "talk", 5, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Callsign),
          Requirement.Unknown(Knows.Manifest)),

        // After Beat 4 — the broadcast is real and people know about it
        L("settler.heard.rota",
          "There is a voice on the radio some mornings. You know about that.",
          "talk", 5, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Rota),
          Requirement.Unknown(Knows.Wreck)),
        L("settler.heard.rota.b",
          "Six forty in the morning, if you want to hear it. I have heard it once. The pressure was wrong.",
          "talk", 5, Requirement.Meetings(2, 99), Requirement.Knows(Knows.Rota)),

        // After Beat 6 — the manifest, the ferry, the tail number
        L("settler.heard.manifest",
          "Word is you found paperwork from a flight that never closed. People remember that flight.",
          "talk", 5, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Manifest),
          Requirement.Unknown(Knows.Cairn)),

        // After Beat 8 — the wreck, somebody survived
        L("settler.heard.wreck",
          "They are saying you found the aircraft. In the water. And that nobody was in it.",
          "talk", 6, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Wreck),
          Requirement.Unknown(Knows.Roster)),

        // After Beat 10 — the cairn, four names, hers absent
        L("settler.heard.cairn",
          "Four names on a stone and one of them missing. That is what I heard.",
          "talk", 5, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Cairn),
          Requirement.Unknown(Knows.Wray)),

        // After Beat 11 — the roster, she was alive
        L("settler.heard.roster",
          "Alive. Four years after. And she walked away from the works on her own legs.",
          "talk", 6, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Roster),
          Requirement.Unknown(Knows.Wray)),

        // After Beat 12 — you found her
        L("settler.heard.wray",
          "You found her, then. Whatever happens now, you found her.",
          "greeting", 6, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Wray),
          Requirement.Unknown(Knows.Window)),
        L("settler.heard.wray.b",
          "The woman at the turbine. I have heard the name now. I did not need to.",
          "talk", 5, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Wray)),

        // After Beat 14 — the window is known, finale is coming
        L("settler.heard.window",
          "Whatever you are planning, the look on your face says it is soon.",
          "greeting", 5, Requirement.Meetings(1, 99), Requirement.Knows(Knows.Window)),

        // ---- Passenger-aware (D-094) -----------------------------------------------
        // Wray in the right seat is a big moment — the aircraft has carried one person
        // for six years. Anyone standing next to it can see there are two.
        L("settler.passenger.wray",
          "There is someone in the right seat. I have never seen that before.",
          "greeting", 7, Requirement.Meetings(1, 99), Requirement.Passenger("wray")),
        L("settler.passenger.wray.b",
          "Two of you. That changes the sound of it, coming in.",
          "greeting", 7, Requirement.Meetings(2, 99), Requirement.Passenger("wray")),
        L("settler.passenger.wray.talk",
          "She did not get out. You did. That says something about the hurry you are in.",
          "talk", 6, Requirement.Passenger("wray")),
        L("settler.passenger.wray.bye",
          "Safe out. Both of you.",
          "parting", 5, Requirement.Passenger("wray")),

        // ---- Sling-load-aware (D-094) ----------------------------------------------
        // The blade pair on the hook: 420 kg, visible, and the point of everything.
        L("settler.sling.blades",
          "Whatever is on the wire under you, it is heavy. I felt it in the ground.",
          "greeting", 7, Requirement.Sling("blade_pair")),
        L("settler.sling.blades.b",
          "You are carrying something important. I can tell by the way you came in.",
          "greeting", 6, Requirement.Sling("blade_pair")),
        L("settler.sling.blades.bye",
          "Careful lifting with that load. Straight up, no drift.",
          "parting", 6, Requirement.Sling("blade_pair")),
    };

    // ------------------------------------------------------------------ region registers

    // Region and kind lines carry a higher weight than the common bank on purpose. Both
    // have the same requirements, so the selector's specificity score cannot separate
    // them - but a line about the reed beds you are standing in front of IS more specific
    // than a line that would serve at any of two hundred sites, and Weight is the only
    // place to say so. The effect is that local colour leads and the common bank fills in
    // behind it, which is why two unnamed places diverge inside one exchange.

    private static IEnumerable<DialogueLine> RegionLines(RegionTag r) => r switch
    {
        RegionTag.Basin => new[]
        {
            L("reg.basin.greet", "You will be the one from over the ridge.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.basin.greet.b", "Dust is up. It will be in your seals by the end of the week.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.basin.a", "The basin holds what little rain there is and then the wind takes it back. That is the whole argument of this place.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.basin.b", "Nothing shoots at anybody down here. People come to the basin to be bored, and they mean it kindly.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.basin.c", "There is water under this if you go deep enough. Somebody deep enough is always somebody else.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.basin.bye", "Straight out over the flat. Nothing to hit for eleven kilometres, and this is the only place I can say that.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.Farmland => new[]
        {
            L("reg.farm.greet", "Mind the rows coming in. They are not much, but they are rows.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.farm.greet.b", "We heard you twenty minutes out. Everyone had their face ready.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.farm.a", "Soil is still soil. It does not know anything has happened. That is restful, some days.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.farm.b", "We got a crop in two of the last six years. Two is not nothing.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.farm.c", "There is an aerial on half the chimneys round here. Nobody admits to listening to anything.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.farm.bye", "Follow the hedge lines out. They still go where they always went.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.Exurb => new[]
        {
            L("reg.exurb.greet", "You will want the yard, then. Not us.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.exurb.greet.b", "You can put down on the hard. Everything here is hard.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.exurb.a", "This was the bit between the city and the country. Now it is just the bit.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.exurb.b", "Every house here still has somebody's things in it. We do not go in. Not because of ghosts. Because of the smell.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.exurb.c", "There is an airfield out there that nobody uses. You could use it, I suppose. You are the only one who could.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.exurb.bye", "Keep off the dual carriageway going out. It is all wire now.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.City => new[]
        {
            L("reg.city.greet", "You came in under the belt. People will want to know how. I would not tell them.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.city.greet.b", "Inside, quickly. Standing in the open here is a habit people lose.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.city.a", "Do not look up at it. Everybody does, once, and then they learn not to.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.city.b", "Water comes up the cut and gets shared out. That is the government here, in full.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.city.c", "There are streets we do not use. Not ruined. Just not used, and nobody remembers deciding.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.city.bye", "Stay under the terrace line going out. It is not a rule. It is what the ones who are still here do.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.Industrial => new[]
        {
            L("reg.ind.greet", "Boots on the yellow. The crane does not stop for anybody.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.ind.greet.b", "Talk over the mill or wait for the shift change. Your choice.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.ind.a", "Mill runs six days. On the seventh it cools and we all stand about listening to how quiet it is, and hating it.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.ind.b", "Everybody here has a shift. Having a shift is most of what stops people going strange.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.ind.c", "There is a scrap price for everything, and somebody here knows it to the kilogram.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.ind.bye", "Out through the gate, and mind the slag heap. It is still hot in the middle.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.Upland => new[]
        {
            L("reg.up.greet", "Cloud is down. You came up the valley, which means you have some sense.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.up.greet.b", "Get in out of it. The wind up here is not weather, it is a person.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.up.a", "Weather comes over the shoulder with nothing in front of it. You get four seasons and you get them by teatime.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.up.b", "Everything up here is stone, and everything up here is cold, and everybody up here chose it.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.up.c", "There are guns on the col. Not people. They have been on since it started and they will be on after.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.up.bye", "Down the valley, not over the top. Over the top is shorter.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.Wetland => new[]
        {
            L("reg.wet.greet", "Hard standing only. Everything else here floats or sinks and we are not always sure which.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.wet.greet.b", "Come in off the boards before you say anything. The wind takes half of it.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.wet.a", "Everything here is two feet above the water, and staying there is a full-time job.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.wet.b", "You cannot dig a grave in this. We put people in the water and we say where. To us that is the same thing.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.wet.c", "The paths move. That map you made last spring is a nice drawing now.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.wet.bye", "North of the withies, and not low over the reed beds. The birds go up all at once.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        RegionTag.Ashfield => new[]
        {
            L("reg.ash.greet", "You landed in the open. Do not do that twice.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.ash.greet.b", "Mask. Then talk.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("reg.ash.a", "Nothing comes up out of this ground and nothing will. We are not farming. We are waiting.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.ash.b", "Everyone out here is on a clock and everyone out here knows roughly what the number is. It makes for short conversations.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.ash.c", "The grey is not soot. Soot washes off.", "talk", 4, Requirement.Meetings(1, 99)),
            L("reg.ash.bye", "Go with the wind behind you if you can. It keeps the worst of it out of the intakes.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        _ => Array.Empty<DialogueLine>(),
    };

    // ------------------------------------------------------------------ kind registers

    private static IEnumerable<DialogueLine> KindLines(SiteKindTag k) => k switch
    {
        SiteKindTag.FuelCache => new[]
        {
            L("kind.fuel.greet", "You will be here for the fuel. Nobody is here for me.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.fuel.a", "Pump works, tank has something in it, and I do not ask where it came from. Neither should you.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.fuel.b", "Drums go off. Four years and it is varnish. Check whatever I give you before you put it in.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.fuel.bye", "Do not run it dry on the way out to prove a point.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        SiteKindTag.Workshop => new[]
        {
            L("kind.shop.greet", "Wheel it in or fly it in, I do not mind, but it goes under the gantry.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.shop.a", "If it is bent I can straighten it. If it is cracked I can weld it. If it is a casting I can sell you sympathy.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.shop.b", "There is no new stock anywhere. There is only other people's old stock, sorted.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.shop.bye", "Come back before it breaks, not after. Nobody ever does.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        SiteKindTag.Farmstead => new[]
        {
            L("kind.farm.greet", "We do not get anybody. We are not on the way to anything.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.farm.a", "Two of us, some goats, and a well that has not failed yet. That is the inventory.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.farm.b", "You are welcome to the barn. It is dry at the south end.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.farm.bye", "Shut the gate behind you. Habit, and the goats.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        SiteKindTag.Airfield => new[]
        {
            L("kind.field.greet", "There is a windsock. I keep it mended. You are the only reason it means anything.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.field.a", "Nothing has moved off this field in six years and I still walk the runway every morning.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.field.b", "The hangars are empty. Picked clean, and half of it by me.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.field.bye", "You have six hundred metres and a fence at the end of it. Use the first four hundred.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        SiteKindTag.Depot => new[]
        {
            L("kind.depot.greet", "Hands where I can see them until you are past the wire.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.depot.a", "It is all in crates and none of the crates say what is in them. That is the job.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.depot.b", "Whoever stocked this place stocked it for a war that did not happen in the shape they expected.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.depot.bye", "Do not come over the fence line low. We have not turned everything off.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        SiteKindTag.Relay => new[]
        {
            L("kind.relay.greet", "You have come for the mast. Everybody comes for the mast.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.relay.a", "Mast is up, the set is on, and I have never once heard a person on it.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.relay.b", "Climb it if you want. Sixty metres, and the top three are rotten.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.relay.bye", "If you hear anything on the way out, come back and tell me what it said.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        SiteKindTag.Settlement => new[]
        {
            L("kind.settle.greet", "Eighteen of us. Nineteen in the spring, if it goes well.",
              "greeting", 2, Requirement.Meetings(1, 99)),
            L("kind.settle.a", "We have a rule. Nobody arrives armed and nobody leaves hungry. It has held so far.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.settle.b", "People turn up, stay a winter, move on. You are the only one who has ever come twice.", "talk", 4, Requirement.Meetings(1, 99)),
            L("kind.settle.bye", "Door is open next time. That is not a promise. Doors are just open here.", "parting", 3, Requirement.Meetings(1, 99)),
        },

        _ => Array.Empty<DialogueLine>(),
    };
}
