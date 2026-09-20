using System;
using System.Collections.Generic;
using System.Linq;

namespace Rotorwash.Sim;

// =========================================================================================
//  THE UPLAND SERVICE
// =========================================================================================
//
//  Hollis Kerr is the voice on the station, and this file is everything he is: where he
//  transmits from, what he says, when he says it, and what he is allowed to know.
//
//  WHO HE IS, IN THREE LINES
//  -------------------------
//  Hollis Kerr, fifties, lives in the transformer hut at the foot of the upland relay mast
//  and has run "the Upland Service" off it, alone, for eleven years. He believes he is
//  operating a network; he is operating a transmitter, a card index of lost property, and a
//  running dispute with the man over the fence about where the fence is. He is one of the
//  four readers on the 06:40 weather sequence and he is the one who gets the dates wrong.
//
//  WHERE HE BROADCASTS FROM, AND BY WHAT RULE
//  ------------------------------------------
//  A real place on the map, found by rule, never by coordinate - the discipline
//  game/scripts/StoryPlaces.cs already sets, for the reason it already gives: the world is
//  seed-generated and a hardcoded coordinate is a lie the moment anything upstream moves.
//
//      INTENT:   the Relay in Cold Shoulder, by highest ground
//      FALLBACK: the Overlook in Cold Shoulder, by highest ground
//      FALLBACK: the Relay in Fenmoor, by highest ground
//      FALLBACK: the Relay in The Pan, by highest ground
//
//  The uplands, because three separate things agree on it. Physically, a VHF transmitter
//  belongs on the highest ground there is, and Radio.Signal is pure line-of-sight geometry,
//  so the highest mast genuinely is the one that reaches the whole world. Mechanically,
//  Cold Shoulder is tier 2 - far enough that he is a destination for most of the game and
//  close enough that reaching him is never a late-game privilege. And thematically, because
//  Cold Shoulder is already where Osie Crane buried four strangers and has held their names
//  ever since (story.md 3). Two men in the same country keeping a record nobody asked them
//  to keep: one reads out four names he cannot put down, the other reads out a found
//  wristwatch, a tin of screws and a child's glove, to no one, at four in the afternoon.
//  Hollis does not know that is what he is doing, which is the whole character.
//
//  Long Acre's relay and the Sawtooth relay are deliberately not candidates: StoryPlaces
//  has already claimed both (LongAcreMast, SawtoothRelay) and two roles on one mast is a
//  collision waiting for whoever builds them.
//
//  THE REGISTER (D-058)
//  --------------------
//  Not GTA. GTA's DJs are broad, loud and gag-driven, and every one of their lines works as
//  a one-liner out of context, which D-058 names as exactly the wrong kind of funny. This
//  is the other thing:
//
//    * DEADPAN. He is never trying to be funny and there are no jokes in this file. There
//      are facts, delivered at length, by a man with airtime to fill and nothing to fill it
//      with.
//    * CHARACTER, NOT GAG. Everything funny here comes from one gap: he thinks he is
//      running a broadcasting service, and he is a lonely man with a microphone. He says
//      "the Service", "your host" and "for the record" without a trace of irony.
//    * MUNDANE DETAIL PLAYED STRAIGHT. Lost property. The fence. The generator's oil. A
//      market report for four stalls. Over-explained weather, by somebody who opens by
//      saying he is not a meteorologist.
//    * IT NEVER UNDERCUTS THE WEIGHT. The lost-property bank is the funniest thing here and
//      it is also a memorial, and it is allowed to land as one without changing register.
//    * NEVER WINKS. He does not know anyone is listening. He never addresses the pilot, he
//      never acknowledges the player, and he has no idea the helicopter he keeps mentioning
//      is receiving him. DjAudit enforces all three.
//
//  WHY HE KNOWS ABOUT THE HELICOPTER (D-008 + D-010)
//  -------------------------------------------------
//  Because everyone does. There is exactly one aircraft in the world (D-008) and the entire
//  threat layout exists because everyone is defending against it (D-010). So a man on the
//  radio mentioning that somebody at Broke Line reckons they heard it go over on Tuesday is
//  simultaneously the funniest line available, the most atmospheric, and MECHANICALLY TRUE:
//  AlertState knows exactly which regions have seen the aircraft and how recently, and this
//  is the first thing in the game that ever says so out loud. A system that has been
//  invisible since it was written becomes local gossip.
//
//  The gating is strict, and RadioDjTests.OnlySpeaksOfWhatWasSeen asserts it: a named-region
//  aircraft line cannot be composed for a region AlertState has never raised. He is also
//  allowed to be a few hours behind, because a man relaying what a neighbour told him is
//  always a few hours behind, and AlertState's six-hour half-life is exactly that window.
//
//  FACTS, NOT INSTRUCTIONS (D-005a)
//  --------------------------------
//  He editorialises, gossips, repeats things he has not checked and is sometimes flatly
//  wrong about the date. That is characterisation. What he never does is tell the player
//  what to do. There is no line in this file that functions as a quest marker, and
//  DjAudit.Instructional is the blocklist that keeps it that way.
//
//  DELIVERY
//  --------
//  This file is CONTENT plus SCHEDULING and deliberately contains no audio. There is no
//  voice actor and nothing may cost anything at runtime, so a segment carries a spoken
//  duration (DjSegment.Seconds, from RadioDj.ReadSeconds at a measured reading pace) and
//  the game layer decides what to do with it - radio-styled subtitles over the carrier
//  hiss today, exactly as story.md 4.3 specifies for the rest of the radio, and a local TTS
//  pass later if one is ever wanted, which is the shape D-006 already uses for local models.
//  Nothing here builds a TTS and nothing here assumes one.
//
// =========================================================================================

/// <summary>What a line is about. The unit the scheduler weights and the corpus is filed by.</summary>
public enum DjTopic
{
    /// <summary>Station identification. He does this far more than is necessary.</summary>
    Ident,
    /// <summary>The time, given with unwarranted precision.</summary>
    TimeCheck,
    /// <summary>The sky, as it actually is this minute.</summary>
    Sky,
    /// <summary>Wind and gusts, when there are any worth the airtime.</summary>
    WindNote,
    /// <summary>Visibility, when it has collapsed.</summary>
    VisibilityNote,
    /// <summary>Temperature, when it is far enough from standard to complain about.</summary>
    TemperatureNote,
    /// <summary>Cloud base, when it is on the deck.</summary>
    CeilingNote,
    /// <summary>The time of year.</summary>
    Season,
    /// <summary>The time of day, as a mood rather than a number.</summary>
    Daypart,
    /// <summary>The card index. The best thing on the station, and he does not know it.</summary>
    LostProperty,
    /// <summary>The fence. There is a running dispute about the fence.</summary>
    Fence,
    /// <summary>Requests. Some of them are years old.</summary>
    Request,
    /// <summary>Notices, read as reported speech, because a notice is a fact about a notice.</summary>
    Notice,
    /// <summary>The market report. Four stalls, delivered like a commodities desk.</summary>
    Market,
    /// <summary>Corrections to previous broadcasts, issued with enormous gravity.</summary>
    Correction,
    /// <summary>Birthdays and anniversaries. Almost nobody writes in.</summary>
    Birthday,
    /// <summary>Engineering bulletins about a transmitter nobody else can see.</summary>
    Transmitter,
    /// <summary>There is a dog at the mast. It is not his dog. He is firm about this.</summary>
    Dog,
    /// <summary>Somebody somewhere heard the helicopter. Gated on AlertState, per region.</summary>
    Aircraft,
    /// <summary>Nobody has heard it. Gated on AlertState being flat everywhere.</summary>
    AircraftQuiet,
    /// <summary>Several regions have it at once, which he finds administratively difficult.</summary>
    AircraftBusy,
    /// <summary>Into the next track.</summary>
    IntoTrack,
    /// <summary>Out of the last one.</summary>
    OutOfTrack,
    /// <summary>Opening the station for the day.</summary>
    SignOn,
    /// <summary>Closing it. He does not like this part.</summary>
    SignOff,
    /// <summary>The 06:40 weather rota, and his views on the other three readers.</summary>
    Rota,
    /// <summary>After the net comes up (story.md 6.2): other people are on the air now.</summary>
    NetUp,
    /// <summary>The murmur between things. Short, and mostly dead air with a man in it.</summary>
    Filler,
}

/// <summary>
/// How awake a region is, in the five bands <see cref="AlertState.Describe"/> already uses.
///
/// The corpus is filed against these rather than against a number, so a line written for a
/// place that half-noticed something cannot be selected for a place that has been standing
/// to for two days. The thresholds are lifted from AlertState and must track it.
/// </summary>
public enum DjHeatBand
{
    /// <summary>Below AlertState's own noise floor. Not eligible for a named-region line.</summary>
    Quiet,
    /// <summary>Somebody saw you once.</summary>
    Seen,
    /// <summary>Watching.</summary>
    Watching,
    /// <summary>Expecting you.</summary>
    Expecting,
    /// <summary>Waiting for you.</summary>
    Waiting,
}

/// <summary>Morning, afternoon, and the parts either side. Coarse on purpose.</summary>
public enum DjDaypart { Night, Dawn, Morning, Midday, Afternoon, Evening }

/// <summary>The four seasons, off the same day-of-year the solar model uses.</summary>
public enum DjSeason { Spring, Summer, Autumn, Winter }

/// <summary>One authored line.</summary>
/// <param name="Id">Stable, derived from topic and index. The recency ring keys on it.</param>
/// <param name="Topic">What it is about.</param>
/// <param name="Text">
/// The line, with optional substitution tokens: <c>{REGION}</c>, <c>{CALLER}</c>,
/// <c>{NEIGHBOUR}</c>, <c>{TRACK}</c>. Anything not resolvable is filled with a neutral
/// fallback rather than left in the text, because a visible token on the HUD strip is the
/// single most immersion-breaking thing this system could ever ship.
/// </param>
public readonly record struct DjLine(string Id, DjTopic Topic, string Text);

/// <summary>One thing he says, resolved and ready for the strip.</summary>
/// <param name="Seconds">How long it takes to read aloud, from <see cref="RadioDj.ReadSeconds"/>.</param>
public readonly record struct DjSegment(string Id, DjTopic Topic, string Text, double Seconds);

/// <summary>
/// One link: everything he says in one go, between two tracks.
///
/// Segments rather than a single string, because the game layer wants them one at a time on
/// a two-line strip, and because the duration of each is what the carrier hiss is gated to.
/// </summary>
public sealed class DjBreak
{
    public IReadOnlyList<DjSegment> Segments { get; }
    public double Seconds { get; }
    public double StartedAt { get; }

    public DjBreak(IReadOnlyList<DjSegment> segments, double startedAt)
    {
        Segments = segments;
        StartedAt = startedAt;
        double t = 0;
        foreach (DjSegment s in segments) t += s.Seconds;
        Seconds = t;
    }

    /// <summary>The whole link as one paragraph, for transcripts and tests.</summary>
    public string Text => string.Join(" ", Segments.Select(s => s.Text));

    public override string ToString() => Text;
}

/// <summary>How a rule picks one site out of the candidates that match region and kind.</summary>
public enum DjPick { BuildOrder, HighestGround, LowestGround, NearestToCentre, FurthestFromCentre }

/// <summary>
/// One way of finding the transmitter: a region, a site kind, an ordering and an ordinal.
///
/// Data rather than a lambda, for the reason StoryPlaces gives: a rule that is data can be
/// printed, diffed and argued with without being run, and it cannot smuggle a coordinate in.
/// The region and kind tags are the sim-side mirrors (<see cref="DialogueCorpus.RegionTag"/>,
/// <see cref="SiteKindTag"/>) because sim/ must not reference Godot.
/// </summary>
public readonly record struct DjSiteRule(
    DialogueCorpus.RegionTag Region, SiteKindTag Kind, DjPick Pick, int Ordinal = 1)
{
    public string Text
    {
        get
        {
            string order = Pick switch
            {
                DjPick.BuildOrder => "in build order",
                DjPick.HighestGround => "by highest ground",
                DjPick.LowestGround => "by lowest ground",
                DjPick.NearestToCentre => "nearest the centre",
                DjPick.FurthestFromCentre => "furthest from the centre",
                _ => "?",
            };
            string nth = Ordinal switch { 1 => "first", 2 => "second", 3 => "third", _ => $"#{Ordinal}" };
            return $"{nth} {Kind} in {Region} {order}";
        }
    }
}

/// <summary>One region that has seen the aircraft, and how badly it took it.</summary>
/// <param name="Name">The region's name as the world calls it - "Cold Shoulder", "Fenmoor".</param>
/// <param name="Level">Straight from <see cref="AlertState.Level"/>, 0..1.</param>
public readonly record struct DjRegionHeat(string Name, double Level)
{
    public DjHeatBand Band => RadioDj.BandOf(Level);
}

/// <summary>
/// Everything the announcer is allowed to know this minute.
///
/// A snapshot rather than live references, for three reasons: the tests can build a world
/// that never existed, the game layer can throttle how often it assembles one, and nothing
/// in this file can reach into a system it does not own and read something it should not.
/// </summary>
public readonly record struct DjWorld(
    double ClockSeconds,
    SkyCondition Sky,
    double WindSpeedMs,
    double GustMs,
    double VisibilityM,
    double CloudBaseM,
    double IsaDeviation,
    double Precipitation,
    double StormIntensity,
    IReadOnlyList<DjRegionHeat> Heat,
    bool SearchComplete,
    string? TrackTitle,
    string? TrackArtist)
{
    /// <summary>Hour of the day, 0..24, fractional.</summary>
    public double Hour => (ClockSeconds / 3600.0) % 24.0;

    /// <summary>Whole days since the clock started. D-039 puts the start in summer.</summary>
    public int Day => (int)(ClockSeconds / 86400.0);

    public DjDaypart Daypart => RadioDj.DaypartOf(Hour);
    public DjSeason Season => RadioDj.SeasonOf(ClockSeconds);

    /// <summary>Anybody at all has seen the aircraft recently enough for it to be talk.</summary>
    public bool AnyoneHeardIt => Heat is not null && Heat.Count > 0;

    /// <summary>The region that has it worst. Default when nowhere does.</summary>
    public DjRegionHeat Hottest
    {
        get
        {
            var best = default(DjRegionHeat);
            if (Heat is null) return best;
            foreach (DjRegionHeat h in Heat) if (h.Level > best.Level) best = h;
            return best;
        }
    }

    /// <summary>An empty world at a given time, for tests and for a first frame.</summary>
    public static DjWorld Quiet(double clockSeconds = 12 * 3600) => new(
        clockSeconds, SkyCondition.Fair, 4.0, 1.5, 18000, 2000, 0, 0, 0,
        Array.Empty<DjRegionHeat>(), false, null, null);
}

/// <summary>
/// The announcer: who he is, where he is, and every word he has.
///
/// Static and immutable. All the state - what he has said lately, when he last spoke - lives
/// in <see cref="DjHost"/>, so two stations, a test and a save-scrubbing tool can all read
/// the same corpus without sharing a recency ring.
/// </summary>
public static class RadioDj
{
    // ================================================================= who he is

    /// <summary>What he is called. Used in the corpus by token, never hardcoded in a line.</summary>
    public const string HostName = "Hollis Kerr";

    /// <summary>What he calls it. He has never once called it anything shorter.</summary>
    public const string StationName = "the Upland Service";

    /// <summary>The man over the fence. Never heard, constantly referred to.</summary>
    public const string Neighbour = "Ennis Marr";

    /// <summary>
    /// Which of the four 06:40 readers he is.
    ///
    /// story.md 3 gives Doss Emery four readers he can tell apart by habit: one clears his
    /// throat, one reads the pressure first, one gets the dates wrong, and one says the wind
    /// speeds in knots. The last of those is the beat the whole search turns on and is not
    /// this man. Hollis is slot 1: the one who gets the dates wrong. He is aware of the
    /// reputation and considers it unfair.
    /// </summary>
    public const int RotaSlot = 1;

    /// <summary>How many readers are on the weather rota. Fixed by story.md 3.</summary>
    public const int RotaSize = 4;

    /// <summary>The daily weather sequence, on the world clock. story.md Act I, beat 5.</summary>
    public const double WeatherSequenceHour = 6.0 + 40.0 / 60.0;

    /// <summary>He opens the station at this hour, and has never once been late.</summary>
    public const double SignOnHour = 5.5;

    /// <summary>And closes it here, which he does not enjoy.</summary>
    public const double SignOffHour = 23.0 + 10.0 / 60.0;

    // ============================================================ where he is

    /// <summary>
    /// How the transmitter is found. Ordered: index 0 is the intent, the rest are declared,
    /// deliberate degradations in descending order of how well they keep the meaning.
    ///
    /// Site placement is rejection sampling and fails silently - a region can ask for a
    /// relay and get none - so a fallback chain is the difference between a shrugging
    /// stand-in and a dead station. Long Acre and Sawtooth Works are excluded on purpose:
    /// StoryPlaces has already claimed both of those relays.
    /// </summary>
    public static readonly DjSiteRule[] SiteRules =
    {
        new(DialogueCorpus.RegionTag.Upland,   SiteKindTag.Relay,    DjPick.HighestGround),
        new(DialogueCorpus.RegionTag.Upland,   SiteKindTag.Overlook, DjPick.HighestGround),
        new(DialogueCorpus.RegionTag.Exurb,    SiteKindTag.Relay,    DjPick.HighestGround),
        new(DialogueCorpus.RegionTag.Basin,    SiteKindTag.Relay,    DjPick.HighestGround),
    };

    /// <summary>The rule chain as prose, for a report or a world dump.</summary>
    public static string SiteRuleText =>
        string.Join("  ->  ", SiteRules.Select(r => r.Text));

    /// <summary>
    /// How tall the mast is, metres above its own ground.
    ///
    /// Taller than anything <see cref="Radio.StationAt"/> rolls (18-70 m), and at the top of
    /// that range rather than beyond it, because this is a real mast on the highest ground
    /// in the uplands rather than a special case.
    /// </summary>
    public const double MastHeightM = 64.0;

    /// <summary>
    /// Nominal reach, km, at the 300 m receiver height <see cref="Radio.FullRangeAglM"/> uses.
    ///
    /// Chosen against the map rather than picked. Radio.Signal is field = reach / distance,
    /// clean at 1.30 and unusable below 0.55, with reach scaled by 0.45 on the deck and 1.00
    /// at 300 m. The uplands sit about 10.3 km from the far corner of the content envelope,
    /// so at 15.0:
    ///
    ///   at 300 m AGL   clean to 11.5 km, usable to 27 km   - he covers the whole world
    ///   on the deck    clean to  5.2 km, usable to 12 km   - he does not
    ///
    /// That is the trade Radio.cs was built around and it is worth restating: D-010 spends
    /// the entire game teaching the player to fly low to stay out of threat envelopes, and
    /// flying low is exactly what loses the station. Neither answer is wrong. Ordinary
    /// stations roll 4.5-14 km, so his is the strongest transmitter on the band and the only
    /// one that reaches everywhere - which is why he is the one everybody's gossip reaches.
    /// </summary>
    public const double PowerKm = 15.0;

    /// <summary>
    /// The station as the radio hardware wants it, once the game layer has resolved the site.
    ///
    /// The id matches <see cref="Radio.FrequencyKnowledgeId"/>, so tuning his mast puts him on
    /// the band through the machinery that already exists and the save already carries.
    /// </summary>
    public static RadioStation Station(int siteId, double x, double y) =>
        new(Radio.FrequencyKnowledgeId(siteId), StationName, x, y, MastHeightM, PowerKm);

    // ============================================================== world hooks

    /// <summary>Alert level to band. Thresholds mirror <see cref="AlertState.Describe"/>.</summary>
    public static DjHeatBand BandOf(double level) => level switch
    {
        < 0.05 => DjHeatBand.Quiet,
        < 0.25 => DjHeatBand.Seen,
        < 0.55 => DjHeatBand.Watching,
        < 0.85 => DjHeatBand.Expecting,
        _ => DjHeatBand.Waiting,
    };

    /// <summary>Below this a region is not talked about at all. AlertState's own floor.</summary>
    public const double TalkAboutAbove = 0.05;

    public static DjDaypart DaypartOf(double hour) => hour switch
    {
        < 5.0 => DjDaypart.Night,
        < 7.5 => DjDaypart.Dawn,
        < 11.5 => DjDaypart.Morning,
        < 14.0 => DjDaypart.Midday,
        < 18.0 => DjDaypart.Afternoon,
        < 22.0 => DjDaypart.Evening,
        _ => DjDaypart.Night,
    };

    /// <summary>
    /// Season from the clock, off the same day-of-year the solar model in Weather uses, so
    /// the man on the radio and the sun in the sky never disagree about the time of year.
    /// </summary>
    public static DjSeason SeasonOf(double clockSeconds)
    {
        double doy = (clockSeconds / 86400.0) % 365.25;
        if (doy < 0) doy += 365.25;
        if (doy < 60) return DjSeason.Winter;
        if (doy < 152) return DjSeason.Spring;
        if (doy < 244) return DjSeason.Summer;
        if (doy < 335) return DjSeason.Autumn;
        return DjSeason.Winter;
    }

    /// <summary>
    /// Assemble what he knows from the systems that own it.
    ///
    /// Nothing in this file writes to any of them. AlertState is read through its own
    /// <see cref="AlertState.Raised"/>, which is the accessor it already exposes for exactly
    /// this - a map overlay or a report - and the names come from the game layer because
    /// region names live in WorldMap and sim/ does not reference Godot.
    /// </summary>
    public static DjWorld Observe(double clockSeconds,
                                  in Weather.Conditions wx,
                                  AlertState? alert,
                                  Func<int, string>? regionName,
                                  bool searchComplete = false,
                                  string? trackTitle = null,
                                  string? trackArtist = null)
    {
        var heat = new List<DjRegionHeat>();
        if (alert is not null && regionName is not null)
        {
            foreach (KeyValuePair<int, double> kv in alert.Raised(TalkAboutAbove))
            {
                string name = regionName(kv.Key);
                if (!string.IsNullOrWhiteSpace(name)) heat.Add(new DjRegionHeat(name, kv.Value));
            }
            // Hottest first, so the composer's default pick is the one the world is actually
            // talking about rather than whichever region id happened to hash first.
            heat.Sort((a, b) => b.Level.CompareTo(a.Level));
        }

        return new DjWorld(
            clockSeconds, wx.Sky, wx.WindSpeed, wx.Gust, wx.Visibility, wx.CloudBase,
            wx.IsaDeviation, wx.Precipitation, wx.StormIntensity,
            heat, searchComplete, trackTitle, trackArtist);
    }

    // =============================================================== reading pace

    /// <summary>
    /// Words per second of read-aloud speech. 2.55 is about 153 words a minute, which is
    /// unhurried news-reading rather than advertising - the right pace for a man with more
    /// airtime than material, and slow enough that the strip is readable at 100 kt.
    /// </summary>
    public const double WordsPerSecond = 2.55;

    /// <summary>A beat either side of a line, so segments do not run into each other.</summary>
    public const double SegmentPadSeconds = 0.70;

    /// <summary>How long a line takes to say. The strip and the hiss are both gated to this.</summary>
    public static double ReadSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int words = 1;
        for (int i = 0; i < text.Length; i++) if (text[i] == ' ') words++;
        return Math.Max(1.6, words / WordsPerSecond + SegmentPadSeconds);
    }
}

/// <summary>
/// Everything he says.
///
/// Volume is the point. A station with thirty lines is worse than no station at all, because
/// thirty lines is exactly enough to teach a player that the character is a loop. The banks
/// below are sized so that a player who listens for a long evening hears him repeat nothing,
/// and <c>RadioDjTests.EnoughToNotRepeat</c> flies a long session and proves it rather than
/// asserting a count and hoping.
///
/// Tokens: <c>{REGION}</c> a region that has actually seen the aircraft, <c>{CALLER}</c> a
/// listener, <c>{NEIGHBOUR}</c> the man over the fence, <c>{TRACK}</c> what is playing. Every
/// one has a fallback in <see cref="DjHost.Resolve"/>; none may ever reach the strip unfilled.
/// </summary>
public static class DjCorpus
{
    private static DjLine[] Keyed(DjTopic topic, string key, params string[] texts)
    {
        var lines = new DjLine[texts.Length];
        for (int i = 0; i < texts.Length; i++)
            lines[i] = new DjLine($"dj.{topic}.{key}.{i:D3}", topic, texts[i]);
        return lines;
    }

    /// <summary>
    /// A bank with no sub-key.
    ///
    /// This overload was UNREACHABLE and the compiler said nothing. `Bank(topic, "line one",
    /// "line two")` binds to the keyed overload - a string argument matches `string key`
    /// before it matches `params string[]` - so every bank declared without a key silently
    /// lost its first line, which then became the bank's key. It showed up as bank names
    /// like `dj.CeilingNote.The cloud is right down. On the mast, near enough.` in the
    /// volume report, and as twenty-one lines the announcer could never say.
    ///
    /// Renaming the keyed one to `Keyed` is what makes this reachable. Overloads that differ
    /// only by an optional-looking leading string are a trap.
    /// </summary>
    private static DjLine[] Bank(DjTopic topic, params string[] texts) => Keyed(topic, "x", texts);

    // =====================================================================================
    //  The station, and the clock
    // =====================================================================================

    /// <summary>
    /// Station identification. He does it far more often than any station needs to, because
    /// the identification is the part that makes it a Service rather than a man talking.
    /// </summary>
    public static readonly DjLine[] Ident = Bank(DjTopic.Ident,
        "This is the Upland Service, transmitting from the mast, as it has done, without interruption, for eleven years.",
        "You are listening to the Upland Service. There is nothing else on this frequency. I have checked.",
        "The Upland Service. Coverage area: the uplands, the low ground, and as far as the weather allows.",
        "This is the Upland Service, and I am your host, Hollis Kerr, which I say for the benefit of anyone joining us.",
        "The Upland Service continues. That is not a promise. That is an observation.",
        "You have the Upland Service. If you have found us by accident, that is how most people find us.",
        "This is the Upland Service, broadcasting from the highest ground in the district, which is a statement of fact and not a boast.",
        "The Upland Service. Established, by me, eleven years ago, on a Tuesday, in the rain.",
        "This is the Upland Service. We do not carry advertising. There is nobody to advertise.",
        "You are listening to the Upland Service. Our signal is strongest above three hundred metres. That is in my notes and I read my notes.",
        "The Upland Service. Hollis Kerr, at the mast, as usual, since there is nobody to relieve me.",
        "This is the Upland Service. The Service has no motto. I have considered several.",
        "You are listening to the Upland Service, which operates on one frequency and has never operated on two.",
        "The Upland Service. Eleven years, four months. I keep the log, so I would know.",
        "This is the Upland Service, and the Service is not affiliated with anybody, because there is nobody to be affiliated with.",
        "The Upland Service continues to broadcast. The generator is running. Those are two separate facts and only the second one is ever in doubt.",
        "You have the Upland Service. Hollis Kerr. Mast, uplands. That is the whole address and it has always been enough.",
        "This is the Upland Service. I am obliged to identify the station at intervals. I am not obliged by anyone in particular. It is simply correct.",
        "The Upland Service, on air. The kettle is on, for the record.",
        "This is the Upland Service. My predecessor ran this transmitter for two years and never once said what it was called, and I have been putting that right ever since.",
        "You are listening to the Upland Service. Somebody once described the Service as a public utility. That was me, and I stand by it.",
        "The Upland Service. Hollis Kerr with you until eleven tonight, unless something happens, which it does not.",
        "This is the Upland Service, coming to you from a building the map calls a transformer hut, which it has not been since before I was born.",
        "The Upland Service. If the signal has gone grainy, that is the weather and not the Service.");

    /// <summary>The time, given with a precision the occasion does not warrant.</summary>
    public static readonly DjLine[] TimeCheck = Bank(DjTopic.TimeCheck,
        "The time is coming up to four. Four in the afternoon. I will not say which four; you can work that out from the light.",
        "It is twenty past. Twenty past the hour. I have a clock here that is correct and a clock in the other room that is eleven minutes fast, and I have stopped correcting it.",
        "Time check. It is later than it was when I last said so.",
        "That is the half hour gone. Half past. I mark them because nobody else does.",
        "The time is a quarter to. I have been up since five, which I mention as information and not as a complaint.",
        "Time check: near enough the top of the hour. The clock is a wind-up and I wind it Sundays.",
        "It is coming up to the hour. There was a pip system here once. Three pips, then a long one. I have the recording of it somewhere and I have never found the somewhere.",
        "Twenty to. That is twenty minutes to the hour, for anyone counting the other way.",
        "It is gone eight. Gone eight is as accurate as I am prepared to be before the clock is wound.",
        "Time check, and I will do this properly: the hour, the minute, and no seconds, because the seconds hand came off in February.",
        "The time, for those who care, and I know of two of you: it is ten past.",
        "It is five to. Five minutes. I have filled longer.",
        "Time check. The Service keeps time off the clock in the hut, and the clock in the hut keeps time off me.",
        "Just after. Just after is what I say when the clock disagrees with the light and I have decided to believe the light.");

    // =====================================================================================
    //  The weather, over-explained, by a man who opens by saying he is not a meteorologist
    // =====================================================================================

    /// <summary>What the sky is doing, filed by <see cref="SkyCondition"/>.</summary>
    public static readonly IReadOnlyDictionary<SkyCondition, DjLine[]> Sky =
        new Dictionary<SkyCondition, DjLine[]>
        {
            [SkyCondition.Clear] = Keyed(DjTopic.Sky, "clear",
                "Clear sky. Nothing in it at all. I am not a meteorologist and I want that understood before I say anything else about it.",
                "It is clear. Properly clear, all the way round, which happens perhaps nine days in a good month and six in a bad one. I count them.",
                "Clear overhead. My father called this a washing day. He was not often right about the weather but he was right about washing.",
                "A clear one. I have been out and looked, which is more than the old forecast ever did.",
                "Clear. I will say for the record that clear is not the same as warm, and people confuse the two every single year.",
                "The sky is clear and the air is dry and there is not a great deal more to report, so I am going to say it slowly.",
                "Clear conditions. From the mast you can see the whole of the low ground on a day like this, and I have looked at it for eleven years and I still go out and look at it.",
                "Clear. The kind of clear where you can see the works chimney, and the works chimney is not close.",
                "Clear sky throughout. Somebody will tell me later that it clouded over in the afternoon at their end. It always does at their end.",
                "It is clear. I have said so. If it is not clear where you are then one of us is wrong, and it is not the mast."),

            [SkyCondition.Fair] = Keyed(DjTopic.Sky, "fair",
                "Fair. Some cloud, not much of it, and none of it with any intention behind it.",
                "Fair conditions. That is my own word for it. The old sheets said fair and I have kept the word on.",
                "Fair, with broken cloud. Broken is the technical term and I use it correctly, unlike some.",
                "It is a fair morning, or afternoon, depending on when you joined us. Fair either way.",
                "Fair. Cloud in the north that is going somewhere else.",
                "Fair conditions holding. I said yesterday they would hold. I do not often say that and I am going to enjoy it for about four seconds.",
                "Fair. Nothing to report, and I am going to report it anyway, because that is the difference between a service and a man sat in a hut.",
                "Fair, drying, a bit of high cloud. High cloud is not weather. High cloud is scenery.",
                "Fair. The sort of day where nothing goes wrong and nobody notices, which is most days and is the best kind.",
                "Fair with occasional cloud. I have been asked what occasional means. It means occasional."),

            [SkyCondition.Overcast] = Keyed(DjTopic.Sky, "overcast",
                "Overcast. Full cover, no breaks, and the light is that grey that makes everything look further away than it is.",
                "Overcast throughout. It has been the same colour since half past six and I have watched every minute of it.",
                "Complete cover. I want to stress that overcast is not rain. People hear overcast and they bring the washing in, and then it does not rain, and then they blame me.",
                "Overcast. Flat light. The hills have gone.",
                "It is grey. Overcast is the correct word and grey is the honest one.",
                "Overcast, with the cloud sitting on the top of the mast, which is a thing I can confirm, because I can normally see the top of the mast, and today I cannot.",
                "Full overcast. No wind to move it. It will sit there and do that all day, and there is nothing behind it.",
                "Overcast. This is the weather I get most letters about, and by most I mean two, over eleven years, from the same person.",
                "Cover is complete. I am obliged to say what the sky is doing, and what the sky is doing is nothing, thoroughly.",
                "Overcast. If you have come outside and looked up and wondered whether it is going to do anything, I can tell you that it is not, and I can tell you that I have been wrong about that before."),

            [SkyCondition.Rain] = Keyed(DjTopic.Sky, "rain",
                "Rain. It is raining, it has been raining, and I have nothing more constructive to add.",
                "Rain across the district. Steady, not heavy. Steady is worse. Heavy stops.",
                "It is raining. The gutter at the back of the hut is blocked again and I know exactly whose leaves they are.",
                "Rain. Visibility is down, the roads will be soft, and the water butt is full, which is the one good thing and I am putting it on the record.",
                "Raining, and set in. When it comes from that quarter it stays for the day. I have eleven years of notes that say so and one year that does not, and I do not talk about that year.",
                "Rain. I have been asked to describe the rain. It is wet and it is coming down and I am not going to do better than that.",
                "Precipitation, continuing. I say precipitation once per broadcast, because I like the word and because it is correct.",
                "It is raining. The generator does not like this and neither do I, but only one of us is going to be taken indoors and dried.",
                "Rain, moderate. There is a puddle at the gate that appears in exactly the same place every time and I have measured it twice.",
                "Rain. Anyone with stock out will already know. Anyone without stock out will not care. That is the whole of the audience accounted for."),

            [SkyCondition.Storm] = Keyed(DjTopic.Sky, "storm",
                "There is a storm on us. I will keep broadcasting for as long as the mast and I are both in agreement about it.",
                "Storm conditions. The set is going to crackle and that is not a fault in the set.",
                "It is a bad one. I have the hut shut up and the spare cells on charge and I am not going out to look at it, which is a first.",
                "Storm. Lightning to the south of the mast, which is the correct side, and I am going to leave it at that.",
                "Storm across the whole district. If the signal goes, it has gone at my end and it will come back, and there is no need for anybody to do anything about it.",
                "We are in it now. Thunder about four seconds behind, which puts it close but not on us. I count. I have always counted.",
                "Storm conditions, and worsening. I have unplugged the second set. The first set is the one you are hearing and it is staying plugged in.",
                "A storm, and a proper one. My predecessor took the station off the air in weather like this. I have never once done it and I am aware that is pride.",
                "Storm. The wind has got under the shed roof again. The shed roof has been a question for three years.",
                "It is a rough night, and I am going to be honest and say the mast moves in this. It is designed to. Knowing that has never once helped."),
        };

    /// <summary>Wind, when there is enough of it to be worth the airtime.</summary>
    public static readonly DjLine[] WindNote = Bank(DjTopic.WindNote,
        "Wind is up. From the west, and gusting. I will not give you a number, because the gauge has been lying to me since the spring.",
        "It is blowing. Blowing properly, not the usual. The gate has come open twice and I have shut it twice.",
        "Strong wind through the district. The mast guys hum in this, a note about a fifth below where you would expect, and I have never got used to it.",
        "Gusting. In between the gusts it is nothing at all, which is the tiring kind.",
        "The wind has veered round since this morning and taken the temperature with it, and I said it would, and nobody was listening, which is normal.",
        "Wind from the south-west, freshening. I do have a gauge. I have described the gauge before. It is on the pole and it is not to be touched.",
        "It is windy. I have been asked to stop saying breezy when it is windy, and that request was fair, and I have complied.",
        "Considerable wind. The washing line came down and took a fence panel with it, which we will come back to.",
        "Wind, steady, out of the north. Cold with it. Nothing dramatic, just constant, which over a day is worse.",
        "Gusts. I heard one take the bin lid off and go over the hill with it, and I have not gone after it.",
        "Blowing hard enough that the hut door needs the brick again. The brick lives by the door for this reason and gets moved by nobody, there being nobody else here.",
        "Wind is the thing today. Everything else is ordinary.");

    /// <summary>Visibility, when it has collapsed.</summary>
    public static readonly DjLine[] VisibilityNote = Bank(DjTopic.VisibilityNote,
        "Visibility is poor. From here I can see the gate and about half of the track and that is the extent of it.",
        "It has closed right in. There is a hill three hundred metres from this microphone and I cannot tell you where it is.",
        "Visibility down. Murk, the old sheets called it, and murk is a good word and I would like it back.",
        "You cannot see anything out there. I went to the end of the track to check, and then I came back, because there was nothing to check.",
        "Poor visibility across the low ground, better up here, which is the one advantage of the situation I am in.",
        "It is thick. I have seen it like this three times this year and on every occasion somebody has walked into the water trough.",
        "Visibility has gone. The mast light is on, for whatever that is worth, which on a day like this is nothing at all.",
        "Murk. Complete murk. The dog will not go out in it and the dog goes out in most things.",
        "Visibility poor and not lifting. It will lift when the wind gets up, and the wind is not getting up.",
        "You cannot see the works from here, and on a clear day you can count the sheds.");

    /// <summary>Colder than it should be. Selected on the sign of the ISA deviation.</summary>
    public static readonly DjLine[] TemperatureCold = Keyed(DjTopic.TemperatureNote, "cold",
        "It is cold. Below what it should be for the time of year, and I have the figures, and I am not going to read them all out.",
        "Cold. Properly cold. There was ice in the butt this morning and there should not be ice in the butt.",
        "It is a cold one. I have the stove in and the hut is warm and the microphone is cold, which is the wrong way round.",
        "Colder than yesterday by a good margin. I keep a card for each day and the cards do not lie, whatever anyone says about my dates.",
        "Cold, and the wind is making it colder, and those are two different numbers which people insist on adding together.",
        "It is cold enough that the generator took four pulls. Four. I counted, out loud, on my own.",
        "A cold day. Anyone keeping stock will have been up early. I was up early and I do not keep stock.",
        "Cold. That is the whole bulletin. I could pad it out and I am choosing not to, which I would like noted.");

    /// <summary>Warmer than it should be, which he says every year and is told so every year.</summary>
    public static readonly DjLine[] TemperatureWarm = Keyed(DjTopic.TemperatureNote, "warm",
        "It is warm. Warmer than it has any business being, and I say that every year, and every year somebody tells me I said it last year.",
        "Warm. The hut is unbearable by two in the afternoon and I broadcast the afternoon from the doorway, which you may be able to hear.",
        "Well above what it should be. The tar on the track has gone soft, which is the measure I actually use.",
        "Warm and still. The flies are up. That is the entire agricultural report.",
        "It is hot. I have taken my jumper off, which for the benefit of long-standing listeners is a significant event.",
        "Warm, and close with it. There will be something in it later. There usually is when it goes close like this.",
        "Above average temperature. Considerably. I have a card from eleven years ago with the same figure on it and I have it out on the desk, looking at me.",
        "Warm. The dog has gone under the hut and will not be coming out, and I have decided to interpret that as the forecast.");

    /// <summary>Cloud base, when it is on the deck. He will not read out a number he has guessed.</summary>
    public static readonly DjLine[] CeilingNote = Bank(DjTopic.CeilingNote,
                "The cloud is on the deck. I can hear the drip off the guy-wires, which is how I know without going out.",
                "Low ceiling. From the door the mast disappears about two-thirds of the way up, and it is oddly companionable.",
        "The cloud is right down. On the mast, near enough. When it is like this I cannot see the aerial from the door and I have to trust the meters.",
        "Cloud base very low across the district. The top of the hill is in it, and the hill is not high.",
        "It is down on the deck. The base has been below the mast head since first light and the meters say it is not moving.",
        "Low cloud, the whole way across. It came in overnight, sat down, and has not shifted.",
        "The base is low. I do not give a figure for the base because I do not have an instrument for it, and I will not read out a number I have guessed.",
        "Cloud right down on the tops. From the low ground it will look like an ordinary grey day. It is not an ordinary grey day up here.",
        "Very low base. There is a ridge behind this hut that has not existed since about four o'clock this morning.",
        "The cloud is sitting on us. It does this three or four times a month and every time I forget how quiet it makes everything.");

    // =====================================================================================
    //  The turning year, and the turning day
    // =====================================================================================

    public static readonly IReadOnlyDictionary<DjSeason, DjLine[]> Season =
        new Dictionary<DjSeason, DjLine[]>
        {
            [DjSeason.Spring] = Keyed(DjTopic.Season, "spring",
                "Spring. Somebody has put a lamb in the yard at the works and nobody will say whose it is.",
                "It is spring. The road crew have been past, because the verge is cut and so is one of my guy-wires, and I have not raised it with them yet.",
                "Spring, officially. I decide when it is officially. I have decided.",
                "It is spring, whatever the last week has been doing.",
                "Spring. The track is soft and the drain at the bend is doing what it does every spring, which is nothing.",
                "Spring, and the light is back in the evenings, which I feel more than I would like to admit on the air.",
                "It is the time of year when everyone is busy and the Service is quiet, and I do not take it personally.",
                "Spring. The birds are back at the mast. They nest in the junction box and every year I let them, and every year it costs me a fuse.",
                "First proper spring week. I have the card from last year and we are eleven days behind it, and eleven days is nothing, and I have still written it down."),

            [DjSeason.Summer] = Keyed(DjTopic.Season, "summer",
                "Summer. The hut gets to thirty-one in the afternoon. I have measured that repeatedly and said so out loud to people who did not ask.",
                "It is summer and the water is low at the cut. That is a fact and not a complaint.",
                "High summer. I have taken the door off the hut. It has changed the acoustic and I am not sorry.",
                "Midsummer, near enough. The evenings go on and on and there is nobody to spend them with, which I say as a scheduling matter.",
                "Summer. Dust on everything. The dust gets in the set and the set gets noisy and there is nothing to be done about either.",
                "It is the height of summer and the grass is off, and the low ground looks like it did before, from up here, if you do not look closely.",
                "Summer, and the generator is running hot. I have it in the shade now, which took some doing.",
                "Long days. I have been broadcasting since half five and it was already light, and it will be light for hours yet, and that is a lot of hours to fill.",
                "Summer. The water is down. It always goes down in July and people always tell me about it as though it were news."),

            [DjSeason.Autumn] = Keyed(DjTopic.Season, "autumn",
                "Autumn. The light goes at four and takes the afternoon with it.",
                "It is autumn and everything smells like the burn. It always does at this time of year. That is the season and not the ash, and I would like that on the record.",
                "Autumn. I have put the second jumper on. There is no third jumper.",
                "It is autumn. The light has changed. It goes in a week and you cannot say which week until it has gone.",
                "Autumn. Leaves in the gutter, which we have discussed, and will discuss again.",
                "The nights are drawing in. That is not a figure of speech at this latitude, that is four minutes a day, and I have the table.",
                "Autumn, and it is the busy season for lost property, for reasons I have never understood and have stopped trying to.",
                "It is the back end of the year. Everything gets put away in these weeks and then nothing happens for four months.",
                "Autumn. I like this part. I am aware that saying so is not a weather report."),

            [DjSeason.Winter] = Keyed(DjTopic.Season, "winter",
                "Winter. The set runs better cold. I run worse. On balance the Service improves.",
                "It is winter and the mast ices. It sheds about eleven in the morning, and I have learned not to be underneath it at eleven.",
                "Deep winter. I have not seen anybody in nine days and the Service has gone out on every one of them.",
                "Winter. It is dark when I sign on and dark when I sign off, and in between there is a grey bit.",
                "Deep winter. The fuel goes twice as fast, the daylight is six hours, and I would not live anywhere else, which I know says something about me.",
                "It is winter, and the road over the top will be what it always is, and people will try it anyway.",
                "Winter. Nobody comes up here between November and March. That is not a complaint, it is a timetable.",
                "Midwinter. From tomorrow the light comes back, by about a minute, and I will take it.",
                "Winter, and hard with it. The mast ices up. When it ices up the signal goes strange before it goes altogether, and now you know why."),
        };

    public static readonly IReadOnlyDictionary<DjDaypart, DjLine[]> Daypart =
        new Dictionary<DjDaypart, DjLine[]>
        {
            [DjDaypart.Night] = Keyed(DjTopic.Daypart, "night",
                "The overnight. Recorded Tuesday, if that matters, and it does not.",
                "Night service. Somewhere out there a generator is doing the same job I am and doing it better.",
                "This is the overnight service. I recorded this earlier and I am asleep.",
                "Overnight, the Service runs unattended. If something has gone wrong with it, it has been wrong for some hours.",
                "The night hours. Nobody is at the desk. The transmitter does not need anybody at the desk, which took me years to accept.",
                "Recorded for the overnight. It is strange to speak to a night that has not happened yet.",
                "Overnight broadcast. I leave the light on in the hut. Not for any reason.",
                "The Service continues through the night. I do not. I am fifty-four.",
                "This is the overnight. If you are up at this hour you have your reasons and they are not my business."),

            [DjDaypart.Dawn] = Keyed(DjTopic.Daypart, "dawn",
                "Dawn. The mast is the first thing the sun touches here and I have never once gone out to see it.",
                "Early. The meters settled overnight and I am choosing to believe that means something.",
                "Morning. It is early, and I am here, and the kettle has not gone on yet, which I consider a scandal.",
                "First light. From up here you get it about four minutes before the low ground does, and I have never stopped feeling smug about four minutes.",
                "Early. The Service is on. I have been up since five and I will say that most mornings, because most mornings it is true.",
                "It is first thing. The mast has ice on it or it does not, and today it does not, and that is the news.",
                "Dawn. The dog has been out and come back and gone to sleep, so the day is already going better for one of us.",
                "Early morning on the Service. I have the cards out, the log open and the kettle on, in that order, which is the correct order.",
                "Morning. There is a light on at the far farm, which there is every morning, and every morning I note it, and that is what I have instead of a colleague."),

            [DjDaypart.Morning] = Keyed(DjTopic.Daypart, "morning",
                "Morning proper. The kettle has been on twice, which tells you how the morning is going.",
                "Mid-morning. This is the hour when people remember that they want things. I write them down.",
                "Mid-morning. This is the busiest part of the day for the Service, which means I have three things to read instead of one.",
                "Through the morning now. People are about. I can see three of them from the window and I am not going to say who, because that would be gossip.",
                "Morning continues. I have done the rounds, checked the aerial, and had a disagreement with the gate.",
                "It is a working morning. Whatever you are doing, the Service is on in the background, which is where it belongs.",
                "Morning. The post, such as it is, has been. One card. We will get to it.",
                "Mid-morning bulletin, which is what I call it when there is no bulletin.",
                "Coming up through the morning. The light is good, the set is behaving, and the generator has not done the thing it does."),

            [DjDaypart.Midday] = Keyed(DjTopic.Daypart, "midday",
                "Midday. I eat at the desk. There is no rule against it. I have checked.",
                "Noon. The Service does not stop for lunch. I do, at the desk, quietly, during a long track.",
                "Middle of the day. I break for twenty minutes at one and the Service plays on without me, which nobody has ever noticed.",
                "It is the middle of the day, which is the flattest part of it and the hardest to fill.",
                "Midday. I have eaten. I mention it because the alternative is silence, and silence sounds like a fault.",
                "The middle hours. Historically this is when somebody walks up the track, and historically, nobody does.",
                "Midday on the Service. The sun is at its highest, which at this time of year means more than it does at others.",
                "Middle of the day. I have a rule about not doing lost property before two, and I am going to break it, because I have nothing else.",
                "Around noon. The hut gets warm about now and the set drifts a fraction when it does, and I nudge it, and that is my afternoon."),

            [DjDaypart.Afternoon] = Keyed(DjTopic.Daypart, "afternoon",
                "Afternoon. The dead part of the day, which I say with affection.",
                "Afternoon service. The light comes round to the window and I move the chair. Every day. I have never once moved the desk.",
                "Afternoon. This is the part of the day the Service does best. I am not going to explain why.",
                "Into the afternoon. The cards come out at two. They have come out at two for eleven years.",
                "Afternoon on the Service. The light goes long and the shadows come off the ridge and it is, frankly, the best hour up here.",
                "Mid-afternoon. Somebody will be along to the fence about now. Somebody is always along to the fence about now.",
                "Afternoon. I have done the log, done the meters, and I have four hours and very little to put in them.",
                "The afternoon hours. If you have the Service on in a shed somewhere, that is what it is for.",
                "Afternoon. I will be here until eleven. I am always here until eleven."),

            [DjDaypart.Evening] = Keyed(DjTopic.Daypart, "evening",
                "Evening. Whatever I did not do today has moved to tomorrow. It always moves. It never arrives.",
                "Evening service. The hut cools down and ticks as it goes, and I have mostly got used to it.",
                "Evening. The Service carries on. The light goes and the signal actually improves, which is a thing about this band I have never fully understood.",
                "Into the evening. This is when I get the requests, which is to say this is when I read the requests, which is not the same thing.",
                "Evening on the Service. The generator goes on to the night setting at nine and you may hear it change note.",
                "Evening. The low ground goes dark before we do and you can watch it happen from the door.",
                "Good evening. I say good evening at this hour whether or not it is a good one, because that is the form.",
                "Evening. Quiet on the band. Quiet everywhere, really.",
                "It is evening. I have the stove in and the door shut and there are three hours to run."),
        };

    // =====================================================================================
    //  The card index
    //
    //  The funniest bank in this file and the one that has to be written most carefully,
    //  because it is also the saddest. The rule it follows: a precise description, a precise
    //  place, a precise interval, and then either nothing at all or the flattest possible
    //  remark. He never draws the conclusion. He does not know there is one to draw. D-058
    //  is explicit that the humour must never undercut the weight, and the way this bank
    //  obeys that is by never changing register when a card stops being funny.
    // =====================================================================================

    public static readonly DjLine[] LostProperty = Bank(DjTopic.LostProperty,
        "Lost property. A wristwatch, leather strap, perished. Found on the wall by the ford. It has stopped at ten past four. It is here.",
        "Found: one glove, child's, left hand, red. On the road below the works. It has been here two years.",
        "Lost property. A tin of assorted screws, about a pound and a half of them, mostly one and a quarter inch. Found at the crossroads. I have not sorted them.",
        "Found on the low road: a single boot, size nine, right foot, laced. I would like it understood that I did not go looking for the other one.",
        "Lost property. A pair of spectacles in a hard case. The case has a name in it. I am not going to read the name out. The owner will know.",
        "Found: a key. One key, no ring, no label, brass. It is not to anything here.",
        "Lost property. A pocket knife with a broken back spring. Found in the hedge at the bend. It can be mended by somebody who can mend that.",
        "Found at the mast gate: a dog collar, no dog. This is not about our dog. Our dog is accounted for.",
        "Lost property. A photograph. I am not going to describe it. If you have lost a photograph you will know which one it is, and it is here.",
        "Found: a bicycle pump, working. On the track above the ford. Somebody put it down and walked off, which is how everything here arrives.",
        "Lost property. A wedding ring, plain, small. Found in the drain at the crossing when it was cleared in March. It has been here since March.",
        "Found: a hat. Felt, brown, band missing. It has been on the hook by the door for so long that I have started to think of it as mine, and it is not mine.",
        "Lost property. Half a set of dominoes. Twelve of them. Found in a tin by the wall. Somebody somewhere has the other sixteen.",
        "Found: a bundle of letters, tied with string, unopened. They are not going to be opened here. They are in the drawer.",
        "Lost property. A tool roll, canvas, containing four spanners and a place where a fifth one was.",
        "Found on the ridge path: a walking stick, ash, well used, with a name burnt into it. Six years.",
        "Lost property. A tea caddy with about two spoonfuls left in it. I have not had any. I want that on the record.",
        "Found: one earring. Gold, or gold-coloured. It came in with a man who said he did not want it found on him.",
        "Lost property. A ledger. Blank after page nine. Whoever was keeping it stopped keeping it.",
        "Found at the ford: a small brass plate off something, with an engraving of a ship on it. It is off nothing that is around here.",
        "Lost property. A pair of pliers, insulated handles, good ones. Somebody has missed these and has not said so, which is pride.",
        "Found: a rosary. Wooden beads, one missing. It is in the drawer with the letters.",
        "Lost property. A lighter that works, which I am aware is a temptation, and which has been on this desk for four months untouched.",
        "Found on the low road: a sack of onions, forty pounds of them, sound. I have not eaten those either, and that one was harder.",
        "Lost property. A hearing aid. It does not work. It may never have worked. It is here.",
        "Found: a chair. One chair, kitchen, three legs sound. It was at the side of the road with nothing near it.",
        "Lost property. A child's exercise book with the multiplication tables in it up to seven. Whoever it was was getting on well.",
        "Found: a cap badge. I know what it is off and I am not going to say, because somebody would come up here about it.",
        "Lost property. A thermos, dented, the inside gone. It is no good to anybody and it is still here, because that is the rule.",
        "Found at the gate: a note, folded, addressed to nobody. It says nothing that would identify anyone. It is in the drawer.",
        "Lost property. Two hundred feet of good rope, coiled properly by somebody who knew how. I have not used it.",
        "Found: a whistle on a lanyard. I blew it once, to check that it worked, and then felt strange about it.",
        "Lost property. A tin whistle, key of D, and a card with three tunes written out on it.",
        "Found on the track: a pair of reading glasses, one lens cracked across. That is the fourth pair of reading glasses this year.",
        "Lost property. A pen that does not write and a bottle of ink that does. They came in together and I have kept them together.",
        "Found: a fuel chit, unredeemed, signed. I will hold on to it. I always hold on to those.",
        "Lost property. A sewing box, complete, with a needle still threaded in it.",
        "Found at the crossroads: a mirror off a vehicle, the glass intact, which given everything is remarkable.",
        "Lost property. A biscuit tin with nothing in it but a smell of biscuits, which after all this time is an achievement.",
        "Found: a horse brass. Nobody here has a horse. Nobody here has had a horse for years.",
        "Lost property. A set of house keys, five of them, on a ring with a piece of blue tape on the largest. That is very specific and nobody has come.",
        "Found on the ridge: a folded map with a route on it in pencil, and I will say only that the route does not finish anywhere.");

    // =====================================================================================
    //  The fence
    //
    //  D-058 asks for "a grudge about a fence" by name. This is it, run as a serial: it has
    //  a shape, it moves, it has a thaw in it and a relapse, and the last card in the bank
    //  is the one that admits what it is actually about. Nobody but him is ever heard.
    // =====================================================================================

    public static readonly DjLine[] Fence = Bank(DjTopic.Fence,
        "The fence. I am going to be brief about the fence. The fence is where it was put, and where it was put is not where {NEIGHBOUR} says it was put.",
        "I have been asked not to talk about the fence on the air. I have not been asked by {NEIGHBOUR}. I have been asked by a third party, and I think we all know who sent them.",
        "Regarding the boundary: I have the original plan. I have had it out. It is on the desk. It says what I have always said it says.",
        "{NEIGHBOUR} has moved a post. One post. Eighteen inches. I have measured it and I have written it down and I am, as you can hear, entirely calm about it.",
        "The fence has come up again. It comes up every March. I have started to think of it as a season.",
        "I want to correct something that has been said about me in the valley, which is that I care about the fence. I do not care about the fence. I care about where the fence is.",
        "There is a gate in the fence. The gate was not in the original plan. I am not going to say more than that at this stage.",
        "{NEIGHBOUR} came up the track yesterday. We had what I would describe as a conversation, and what he would describe as a conversation, and we would not be describing the same one.",
        "On the boundary matter: I have offered arbitration. There is nobody left to arbitrate. That has not stopped me offering.",
        "The fence is down again in the wind. I will mend my half. I will mend my half in a way that makes it obvious which half is mine.",
        "I have been told I should let the fence go. I have considered letting the fence go. I have decided against letting the fence go.",
        "The post is back where it was. I am not going to say how. I am going to say that it is back where it was and that the matter is closed, and the matter is not closed.",
        "There is a sheep in my field that is not my sheep, and there is a gap in a fence that is not my gap, and I am going to leave those two facts next to each other.",
        "I would like to put on the record that in eleven years I have never once moved a post.",
        "{NEIGHBOUR} has put a notice on the fence. I have read the notice. The notice is not accurate, and the spelling is not good either, and only one of those matters.",
        "The boundary runs from the thorn to the corner of the barn. That is what it has always run from and to. This is not a difficult document.",
        "We are speaking again. Briefly. At the gate. About the weather, deliberately.",
        "The fence stood up to the wind last night, which I mention because I mended that section and he did not mend his.",
        "I am told {NEIGHBOUR} says I am obsessive about this. I keep a record. The record shows I have raised it eleven times in five years, which is twice a year, which is not obsessive, and I know that because I keep a record.",
        "There has been a development on the boundary. I am not going to broadcast a development I cannot yet substantiate. I am simply noting that there has been one.",
        "It is a good fence. That is the frustrating part. It is a genuinely good fence and it is in the wrong place.",
        "{NEIGHBOUR} left a bag of apples at the gate. I have not decided what that means.",
        "I ate the apples. I want to be clear that eating the apples is not agreement.",
        "The fence: no change. I will report when there is a change.",
        "I have taken the plan off the desk and put it back in the tin, which for those following this is a significant step.",
        "Somebody asked me why it matters. I did not have an answer at the time. I have had one since, and it is not a good one, so I am going to keep it.");

    // =====================================================================================
    //  Requests, notices, the market, corrections, birthdays
    // =====================================================================================

    public static readonly DjLine[] Request = Bank(DjTopic.Request,
        "A request. This came in four years ago and I read it when it comes round, because it came in.",
        "Somebody asked for something with a piano in it. There is nothing with a piano in it. I am still looking.",
        "A request from {CALLER}, for their mother, who liked this one. I am reading the card as it was written.",
        "This next one is a request. The card does not say who from. It has been in the box a long time and the writing has gone.",
        "Request. {CALLER} asked for anything at all, so long as it was loud. That is the whole of the card.",
        "For {CALLER}, who walked up here to ask in person, in the rain, and would not come in.",
        "A request that came in on a scrap of a feed sack, which I have kept, because you keep those.",
        "Somebody wrote in and asked whether I take requests. I do. I have taken eleven.",
        "This is for {CALLER}. They did not say why. Nobody ever says why and I have stopped asking.",
        "A request from the low ground, passed up by two people, so if it has changed on the way, that is not my doing.",
        "This one was asked for by somebody who is not around any more. I am still going to play it. That is the arrangement I have with the box.",
        "Request. The card says: the one about the road. I have made a decision about which one that is and I may be wrong.",
        "For {CALLER}, on the occasion of nothing in particular, which is the best occasion there is.",
        "A request, and an unusual one, in that it is recent.",
        "Somebody asked for the fast one. I have written back, in my head, asking which fast one, and received no reply.",
        "This is for the household at the far end, who have the Service on all day, and who I know have it on all day because I can see the aerial.",
        "A request from {CALLER} for their brother. The card is dated and I am not going to read the date out.",
        "I have a request here that I have played four times and will play again, because the box does not have a rule about that and I have decided not to make one.",
        "For {CALLER}, who asked at the market and then said not to make a fuss, which I am aware is what I am now doing.",
        "A request card in a child's hand. It asks for a song about a dog. I have done my best.",
        "This came in with the fuel and has been in the box since. It is nobody's fault that it took this long.",
        "Somebody left a request under a stone at the gate, which is a system we have arrived at without ever discussing it.",
        "For {CALLER}. The card says, and I quote, whatever you think. I have thought.",
        "A request. It is the same request. It is the only request some weeks and I have never once minded.");

    public static readonly DjLine[] Notice = Bank(DjTopic.Notice,
        "A notice. The bridge at the low crossing has a plank out. The notice says the plank has been out since Thursday. That is all the notice says.",
        "I have a notice here from the market. They have changed the day. It is now the day it used to be before they changed it the first time.",
        "A notice, passed up: the well at the middle farm is back in use. It has been tested. It was not tested by me.",
        "This notice is about dogs and I am going to read it exactly as written, which is not how I would have written it.",
        "A notice from the works: there is a shift on Sunday. The notice does not say why there is a shift on Sunday.",
        "I have been asked to read a notice about the drain. I have read the notice about the drain twice this month, and the drain is the same.",
        "Notice: a meeting was held. The notice does not say what was decided. I did ask.",
        "A notice from the low ground. Somebody's stock is out. The description is: brown, four of them, one with a bad foot.",
        "I have a notice about the road over the top. It says the road over the top is the road over the top. I think there was more to it originally.",
        "A notice, and this one is good news, so I am going to enjoy reading it: the pump at the crossing is mended.",
        "Notice from the workshop. They will take work in on the first of the month and not before, and the notice underlines not before.",
        "A notice has been handed in concerning a debt. I do not read notices concerning debts. I am reading this one only to say that I am not reading it.",
        "Notice: the school is running two days a week now instead of one. That is the best thing I have read out this month.",
        "A notice about the water. It is the same notice as last year, on the same paper, and I suspect it is literally last year's.",
        "I have a notice here signed by four people, which is the most signatures I have had on anything.",
        "Notice: there is a burial on Friday. The family have asked for it to be read, and I have read it, and that is as far as it goes.",
        "A notice from the fuel store. They are open. That is the notice. They felt it was worth saying and I agree with them.",
        "Notice concerning the track past the mast. It is soft. It has always been soft. Somebody has now written it down.",
        "I have a notice about a lost animal which I am placing under notices and not under lost property, because I have a system.",
        "A notice, handed in at the gate, about a wall. I am going to read the notice about the wall and I am going to say nothing about walls afterwards.",
        "Notice: the market has four stalls this week, which is up one.",
        "A notice asking whether anybody has a tap and die set. Not asking to borrow one. Asking whether anybody has one, which is a different and sadder question.",
        "Notice from the far end: the aerial they put up has come down. I offered to look at it. The offer stands, on the air, where everybody can hear it.",
        "A notice about the ford. It is high. When it is high it is high, and the notice does not add anything to that.",
        "I have a notice that simply says thank you. It does not say to whom, or for what. It has been on the board for a fortnight.",
        "Notice: there will be no notices next week, as the person who writes them is away.");

    public static readonly DjLine[] Market = Bank(DjTopic.Market,
        "The market report. Four stalls. Trade described as steady, by me, from a distance.",
        "Market. Eggs are up. Eggs are always up. I have eleven years of cards and eggs have never once gone down.",
        "Market report: potatoes plentiful, onions scarce, and one stall selling something I could not identify from where I was standing.",
        "Trade at the market was slow. There were more people than goods, which is the way round it has been since the spring.",
        "The market report, in full: three stalls, one of them selling rope.",
        "Market. Fuel changed hands at a rate I am not going to broadcast, because broadcasting it is how a rate becomes a price.",
        "The market report. Salt is the story this week. Salt is the story most weeks and nobody wants to hear it.",
        "Market: brisk. I use brisk when there were more than nine people and I witnessed two transactions.",
        "Market report. A man was selling glass. Actual window glass. I have not seen that for four years and I went over and looked at it.",
        "Trade steady. Barter mostly. One thing changed hands for another thing and neither party looked happy, which is how you know it was fair.",
        "The market. Wool is moving. Wool moves in September and then does not move again.",
        "Market report: no fish. There has been no fish for three weeks and people have started asking me about it as though I would know.",
        "Market. Somebody had books. Eleven of them. Four went.",
        "The report from the market is that it rained and everybody went home, and that is the report.",
        "Market: medical stock available at one stall, in small amounts, which I mention because it does not happen often.",
        "Trade was good. I am going to say good rather than give you numbers, because the numbers were four.",
        "Market report. Parts. There were parts, a box of them, mixed, and they were gone inside the hour.",
        "The market ran an extra hour. Nobody decided that. It simply did.",
        "Market: quiet. It was cold and it was quiet and the two are connected.",
        "The market report is that the market happened, which some weeks is the whole of the news, and is not nothing.");

    /// <summary>
    /// Corrections, issued with enormous gravity. story.md 3 says one of the four weather
    /// readers gets the dates wrong; this bank is that reputation, from the inside.
    /// </summary>
    public static readonly DjLine[] Correction = Bank(DjTopic.Correction,
        "A correction. On Tuesday I gave the date as the fourteenth. It was the fifteenth. The Service regrets the error.",
        "Correction. I said the ford was passable. It was passable when I said it and it was not passable an hour later, and I accept that the distinction is of limited use.",
        "I must issue a correction. The notice about the pump referred to the lower pump and I read it as the upper. Both pumps are mended, so the practical effect is nil, but the record should be right.",
        "A correction to yesterday. I gave the day of the week incorrectly. I am told this is becoming a pattern. I dispute that it is a pattern.",
        "Correction. The name I read out on the birthday card was misread by me. It is not a name I had seen before, and I have since been told how it is said.",
        "I said last week that it had not rained since the eleventh. It had rained on the ninth. I have the card. The card was filed wrongly. By me.",
        "A correction, and this one is mine entirely. The market is on Thursday. I said Wednesday. Two people went on Wednesday.",
        "Correction: I referred to the works whistle as going at six. It goes at half five. It has gone at half five for as long as there has been a whistle, and I have been saying six for some time.",
        "I need to correct the date I gave earlier. I will not say what I gave or what it is. I will simply say that it was wrong and that I have amended the log.",
        "A correction concerning lost property. The boot is a size nine. I said size eight. Somebody came a long way about that.",
        "Correction. The temperature I gave this morning was the reading off yesterday's card. I had them out in a stack and I read the wrong one.",
        "I am obliged to correct myself. The notice was signed by four people. I said three. The fourth signature is there and is perfectly legible and I apologise to them specifically.",
        "A correction. I gave the year wrongly on air yesterday. I would like to say that was a slip. It was not entirely a slip. I had to sit and work it out.",
        "Correction to an earlier item: the glove is a left hand. I said right. I have had that glove here for two years and I still said right.",
        "I said the wind was from the south. It was from the south-west. The gauge says south-west, and I have said before that the gauge lies, so I would like it noted that on this occasion I was the one lying.",
        "A correction, and it is the same correction. The date. I am aware.",
        "Correction. The burial is Friday, not Thursday. I will read that again later and I will read it correctly.",
        "I gave a bearing on air this morning. I should not give bearings. I do not have the instrument for it, and I gave it from memory, and it was out by a good margin.",
        "Correction: I described the found knife as having a broken blade. It has a broken spring. The blade is perfect. That matters to the sort of person who would come for a knife.",
        "A correction to the correction. The fifteenth was right after all. I have been through the log twice and the original entry was correct and my correction was wrong, and I am going to leave both on the record, because that is what a record is.");

    public static readonly DjLine[] Birthday = Bank(DjTopic.Birthday,
        "Birthdays. There is one. That is not a complaint about the population, it is a fact about the post.",
        "A birthday today for {CALLER}, who is, and I am reading this from the card, considerably older than they were.",
        "Birthdays and anniversaries. Nothing has come in this week. I will read last week's again, because they are still true.",
        "It is a birthday for {CALLER}. The card was handed in by somebody else, which is how it should be done.",
        "An anniversary today. The card says twenty-two years. It does not say twenty-two years of what, and I am not going to speculate.",
        "Birthday greetings to {CALLER} from the whole household, which the card lists by name, all five of them, and I am going to read all five.",
        "A birthday. The card came in three weeks early, which tells you something about how much the sender was looking forward to it.",
        "Anniversaries. One. It is a wedding. It is somebody's forty-first, and I have read out the last four.",
        "Birthday today for a child at the far end whose name I have been asked to say properly, and I have practised, and here we go.",
        "There are no birthdays today. There were two yesterday and I missed both, and that is on me and not on the post.",
        "A birthday for {CALLER}, who has asked every year that I do not make a fuss, and who has sent the card in every year.",
        "An anniversary of a less happy kind has been handed in. The family asked for the name to be read and nothing else. That is what I am going to do.",
        "Birthdays: {CALLER} today. The card has a drawing on it. I am going to describe the drawing, because the artist is eight.",
        "One birthday. One. I have had weeks with none and weeks with four and there is no pattern, and I have looked for one.",
        "A card has come in for somebody's birthday with no name on it at all. Somebody knows. It is read out.",
        "Birthday greetings today, and I would like to say that this is my favourite five minutes of the week, and then move quickly on.");

    // =====================================================================================
    //  The equipment, and the dog
    // =====================================================================================

    public static readonly DjLine[] Transmitter = Bank(DjTopic.Transmitter,
        "An engineering note. The transmitter is running warm but within itself. I check it twice a day and I have checked it twice today.",
        "The generator took eleven pulls this morning. Eleven. I am going to look at the plug when I have finished talking.",
        "Technical bulletin. There is a hum on the carrier. I can hear it, and I suspect I am the only person in the world who can hear it, and I am going to fix it.",
        "The aerial is sound. I climbed the first two sections and looked at the rest through the glass, which is as far as a man of my age goes up a mast.",
        "The Service is running on the second set today while the first one dries out. You may notice the difference. You will not notice the difference.",
        "An engineering note, and a good one: I have found the fault. It was the earth strap. It has been the earth strap twice before and I still did not think of it first.",
        "Oil in the generator changed. Forty hours since the last change, which is ten hours over, and I have written down why.",
        "There is a valve in this transmitter that should have failed nine years ago. It has not failed. I have a spare. I look at the spare sometimes.",
        "Technical note: I have retensioned the guy on the north side. It had gone slack over the summer, which they do.",
        "The meters are reading correctly. I have no way of confirming that the meters are reading correctly. They agree with each other, which is the best I can do.",
        "Fuel state at the mast is adequate. I keep two weeks in hand. I have kept two weeks in hand since the year I did not.",
        "An engineering bulletin. The modulation is a little flat this morning. That is the transformer, and that is the cold, and it will come right by ten.",
        "I have cleaned the contacts. I do this every Sunday and it makes no measurable difference and I am going to keep doing it.",
        "The mast light is out. I have a bulb. Getting the bulb to the light is the difficulty and it is going to be Thursday.",
        "Note on the equipment: the desk microphone has a rattle. If you hear a rattle, that is the microphone and not the band.",
        "The Service went off the air for nine minutes last night. It was the generator. It was not the transmitter. I want that distinction made, because the transmitter has never let me down.",
        "Engineering. I have rebuilt the power supply using parts from a thing that was not a power supply, and it works, and I am going to say no more about it.",
        "There is a spare transmitter under a sheet in the back room. It has never been used. I keep it because the day I do not keep it is the day I need it.",
        "The frequency has drifted a fraction in the heat. I have nudged it back. If you had to retune, that was me.",
        "A technical note for anyone who cares, and I am aware of the size of that audience: the aerial is a folded dipole, and it is the third one, and it is the best one.",
        "I have been up on the roof of the hut. Everything up there is fine. I am going to be stiff tomorrow.",
        "Engineering bulletin: nothing is wrong. I am going to say that clearly, because I mostly speak about this equipment when something is.");

    public static readonly DjLine[] Dog = Bank(DjTopic.Dog,
        "The dog is here. It is not my dog. It arrived four years ago and has never left and I have never fed it, except daily.",
        "The dog has been in the drawer with the lost property again. Nothing is missing. I have checked.",
        "A note about the dog: it does not like the generator and it does not like the wind, and it likes the microphone, which is a problem.",
        "The dog came in wet and has gone under the desk, so the next hour is going to smell of dog, and you will be spared that, and I will not.",
        "For anyone who has lost a dog: I have a dog. It is brown, medium, and answers to nothing at all. It has been here four years.",
        "The dog got out last night and came back this morning with a bone from somewhere. I have not investigated.",
        "The dog is on the chair. The chair is the only chair. I am standing up to broadcast, and this is a choice I have made.",
        "The dog barked at nothing at half past two. I went out. There was nothing.",
        "I am asked from time to time what the dog is called. The dog is not called anything. I have not named it, because naming it would be a decision.",
        "The dog has decided it comes into the hut now. That was not agreed and I was not consulted.",
        "The dog will not go out in this. I have described the weather at length and the dog has summarised it.",
        "I gave the dog the last of the meat, which I want on the record as a moment of considerable weakness.",
        "The dog goes down to the gate and sits there about an hour before anybody comes up the track. I have no explanation. It has never been wrong.",
        "There is a hole under the fence that the dog uses. I am aware of the irony. I am not going to fill it in.",
        "The dog is asleep. That is the extent of the report, and it took four years to become a report.",
        "I have been told the dog belongs to somebody in the valley. If it does, they have not come for it, and it has been four years, and I think we all know where we are.");

    // =====================================================================================
    //  The helicopter
    //
    //  The reason this character exists. There is exactly one aircraft in the world (D-008)
    //  and every threat system in the game is there because people are defending against it
    //  (D-010), so the aircraft is the single largest subject in the world and the only one
    //  everybody has an opinion about. AlertState has always known which regions have seen
    //  it and how recently; this is the first thing in the game that says so out loud.
    //
    //  Four bands, matching AlertState's own five minus Quiet, which is not eligible. A line
    //  may only ever be selected for a region whose level actually sits in its band, and
    //  DjHost will not compose one of these at all when nothing is raised. He is a relay for
    //  gossip, so he reports what he is told, attributes it, hedges it, and is sometimes
    //  plainly wrong about it - and he never once suggests that anybody do anything, because
    //  he has no idea what anybody could do and would not presume (D-005a).
    // =====================================================================================

    public static readonly IReadOnlyDictionary<DjHeatBand, DjLine[]> Aircraft =
        new Dictionary<DjHeatBand, DjLine[]>
        {
            // 0.05 - 0.25. One person half-noticed something, once.
            [DjHeatBand.Seen] = Keyed(DjTopic.Aircraft, "seen",
                "Somebody at {REGION} says they heard the helicopter go over. They say that most weeks, so I am passing it on and I am not standing behind it.",
                "A report from {REGION}. Heard, not seen. That is the whole of the report and I have written it on a card.",
                "{CALLER} at {REGION} reckons the helicopter went over on Tuesday. {CALLER} also reckons a great many things.",
                "There was the sound of it over {REGION}, apparently. Apparently is doing some work in that sentence.",
                "A single report, from {REGION}, of the helicopter. One person, at night, on their own. Make of that what you like. I have.",
                "{REGION} had it over, briefly. Nobody up there got out of their chair, which tells you how much of an event it was.",
                "It has been heard at {REGION}. That is the first time in a while and I have noted the date, and before anybody says anything, I have checked the date.",
                "The helicopter was over {REGION}. I have this from one person, and the one person was some distance from it.",
                "A report from {REGION}: engine noise, low, going north. It could have been the plant at the works. It was probably not the plant at the works.",
                "{REGION} report hearing it. I put these on a card and the card goes in the box, and nobody has ever asked to see the box."),

            // 0.25 - 0.55. Several reports, and they agree.
            [DjHeatBand.Watching] = Keyed(DjTopic.Aircraft, "watching",
                "{REGION} has had it over more than once now. People up there are looking up, which is not something people do any more.",
                "The helicopter has been over {REGION} again. That is three reports and they agree with each other, which is unusual.",
                "They are talking about it at {REGION}. Not worried. Talking. There is a difference and I am going to preserve it.",
                "A second and a third report from {REGION}. Same direction, same time of day. I have put them side by side on the desk.",
                "{REGION} is watching for it now. That is what I am told, and I believe it, because {CALLER} described the sound accurately, and most people do not.",
                "The reports from {REGION} have gone from one to several. I read them out in the order they arrived, which is the only order I trust.",
                "It has been over {REGION} enough times this week that people have started mentioning it before I ask.",
                "{REGION} again. I have started a second card for {REGION}, which I have not had to do for anywhere in two years.",
                "Word from {REGION} is that it comes low along the valley. I am reporting what I am told. I have not been to look.",
                "There is a sort of expectation at {REGION} now. Nobody has said anything official. You can hear it in how they say it."),

            // 0.55 - 0.85. Ready for it, and it has cost somebody something.
            [DjHeatBand.Expecting] = Keyed(DjTopic.Aircraft, "expecting",
                "{REGION} is on the hop. That is the word that was used to me and I am using it back, because I do not have a better one.",
                "They are ready for it at {REGION}. Whatever ready means up there, and I would rather not know.",
                "The people at {REGION} have things pointed at the sky. That is not gossip. That is what {CALLER} told me, and {CALLER} works there.",
                "{REGION} has had a bad few days with it. I am not going to editorialise about {REGION}, and I am aware that saying so is editorialising.",
                "At {REGION} they have stopped reporting each time, which I think means it has become ordinary, and I do not like ordinary.",
                "I have four cards from {REGION} this week and the handwriting on three of them is hurried.",
                "The talk from {REGION} is all one subject. It has been all one subject for days, and it was not this subject a fortnight ago.",
                "{REGION} is expecting it back. They have said so, out loud, to me, which people generally do not.",
                "Reports from {REGION} continue and have changed in character. Earlier they described a sound. Now they describe a direction.",
                "There has been shooting at {REGION}. I say that plainly, because saying it any other way would be worse."),

            // 0.85+. Standing to, and it is the only subject there is.
            [DjHeatBand.Waiting] = Keyed(DjTopic.Aircraft, "waiting",
                "{REGION} is standing to. That is the phrase that came up the line and I have not softened it.",
                "There is nothing coming out of {REGION} but this one subject. Nothing. No market, no notices, no birthdays.",
                "{REGION} has been like this for days and I have run out of neutral ways to say it, so here is the unneutral one: they are waiting.",
                "I have had nine cards from {REGION}. Nine. The most I ever had from anywhere before this was three.",
                "{CALLER} came up from {REGION} on foot to tell me about it, which is a long way to come to tell a man in a hut something.",
                "At {REGION} it is all they do now. Somebody is always on the roof.",
                "{REGION} is awake, and has been awake since Sunday. I am going to leave that there and play something.",
                "The reports from {REGION} have stopped. That is not the same as it being over, and I want to be careful about the difference.",
                "Everything I have from {REGION} this week is about the helicopter. I have looked for something else to read out from {REGION} and there is nothing else.",
                "They are not talking about anything else at {REGION}. I have my own view about that, and my own view is not a broadcast."),
        };

    /// <summary>Nothing anywhere. Composed only when no region is above the floor at all.</summary>
    public static readonly DjLine[] AircraftQuiet = Bank(DjTopic.AircraftQuiet,
        "Nothing on the helicopter this week. Not a card, not a word. It goes quiet like this, and then it does not.",
        "No reports of it anywhere. That happens. It happened for five months once, and then it stopped happening.",
        "Nobody has heard the helicopter. I am saying so because the weeks it is not mentioned are as much a fact as the weeks it is.",
        "The box is empty on that subject. I have looked twice.",
        "Quiet on the helicopter. Whoever keeps that thing in the air is presumably doing something else with their week.",
        "No sightings anywhere, which is the sort of thing I would not have bothered reporting eleven years ago and now report every time.",
        "Nothing to pass on about the aircraft. There is a card in the box from last month and that is the most recent one.",
        "No reports. It is remarkable how much of a subject a thing can be when it is not happening.",
        "Nothing on it. Quiet everywhere. Quiet is not the same as gone, and I have made that mistake before.",
        "No word on the helicopter from anywhere in the district. For once, the notices are the news.",
        "Nobody has heard a thing. Somebody at the market asked me and I had nothing for them, and they looked disappointed, and so did I.",
        "There have been no reports. I keep the cards in date order and the top of the pile has been the top of the pile for a while.");

    /// <summary>Three or more regions at once, which he finds administratively difficult.</summary>
    public static readonly DjLine[] AircraftBusy = Bank(DjTopic.AircraftBusy,
        "I have cards from three different places this week about the same thing, which has not happened before.",
        "It has been over half the district. I have put the cards out on the desk in a line and the line goes from one side of it to the other.",
        "Reports from all over. I am not going to read them individually. I am going to say that there are a lot of them.",
        "Everywhere is talking about it. That is not an exaggeration, and I try very hard not to exaggerate.",
        "The whole low ground has had it over in the last few days, by the cards. I have never had a week like this one.",
        "Four places. Four separate places, in four handwritings, about one thing.",
        "I have had to start a second box. That is the state of things.",
        "It is being reported from places that do not report anything. That is the part I would draw attention to, if I were in the business of drawing attention to things.",
        "The cards are coming in faster than I read them out, which has never been the problem here.",
        "Whatever is going on, it is going on over a wide area, and I only know that because I am the one who gets all the cards.");

    // =====================================================================================
    //  Into and out of the music
    // =====================================================================================

    public static readonly DjLine[] IntoTrack = Bank(DjTopic.IntoTrack,
        "Here is some music.",
        "That is the news. Here is the other thing I have.",
        "I am going to play something now, because I have said everything I had.",
        "Music. This is {TRACK}, according to the label, and the label has been wrong before.",
        "Something to be going on with.",
        "Here is one off the shelf. I have them in an order, and the order is mine.",
        "This next one was here when I got here.",
        "Music now, and I will be back, which is not in doubt.",
        "Here is {TRACK}. I have played it before and I will play it again and nobody has complained.",
        "Something loud, because it is that sort of afternoon.",
        "Something quiet. I am not going to explain.",
        "I am going to put this on and go and look at the generator.",
        "Here is one I like. That is not a recommendation, it is a disclosure.",
        "This is off the tape that came up from the low ground last year, which I have been through twice and am still finding things on.",
        "Music. The Service does play a great deal of music, and that is because there is a great deal of time.",
        "Here is {TRACK}, and then I have the cards.",
        "I will let this one run and say nothing over the start of it, which is the correct way to do it, and which the old stations had stopped doing.",
        "Something now, then the weather, then something else. That is the shape of the hour.");

    public static readonly DjLine[] OutOfTrack = Bank(DjTopic.OutOfTrack,
        "That was that.",
        "That is one I have had on the shelf since the beginning.",
        "That was {TRACK}. The label says so.",
        "That one always sounds better up here in the evening. I have no explanation for it and I have thought about it.",
        "That was music. This is me.",
        "That was longer than I remembered. They often are.",
        "That was one of the ones the tape came with. I do not know what the rest of the tape is, because the rest of the tape is not there.",
        "That one has a fault about two minutes in. It is on the tape and not on the transmitter, and I would rather play it with the fault than not play it.",
        "That was {TRACK}, which somebody asked for, once, a long time ago.",
        "There we are.",
        "That one gets played more than the others. I have noticed that about myself.",
        "That was the last of that side.",
        "That was a request. I will come to whose.",
        "I have nothing to add to that.",
        "That is a good one and I am not going to spoil it by talking about it, having just talked about it.",
        "That was one of about forty I have. Forty is not many, and it is enough.",
        "That was the one with the noise at the end, which is not the generator.",
        "And that is that finished.");

    // =====================================================================================
    //  Opening, closing, and the one fixture that is not his
    // =====================================================================================

    public static readonly DjLine[] SignOn = Bank(DjTopic.SignOn,
        "This is the Upland Service, opening for the day. It is half past five and I am here.",
        "Good morning. The Service is on the air. The generator is running, the aerial is intact and the kettle is going.",
        "The Upland Service begins its day. I have not missed an opening in eleven years, which I mention once a year, and today is not that day, and I have mentioned it anyway.",
        "Opening the Service. First thing: nothing is wrong.",
        "Good morning. The Upland Service is on. There will be weather at twenty to seven, as there is every day.",
        "The Service is open. I am aware it makes no difference to the transmitter whether I say so. It makes a difference to me.",
        "Morning. Upland Service. I have been up an hour and the first hour is always the best one.",
        "The Upland Service, opening. It is dark and it is cold and the set is warm, and that is the correct order of those things.",
        "Good morning. I have the log open, the cards out and one letter, which we will come to at a decent hour.",
        "Opening the Service for the day. There is frost on the mast, and there is nothing to be done about frost on the mast.",
        "Good morning, and the Service is on the air for another day, which I would not have said out loud in the first year.",
        "The Upland Service. Day one of week five hundred and something. I have stopped counting weeks and started counting years, which is a sort of surrender.");

    public static readonly DjLine[] SignOff = Bank(DjTopic.SignOff,
        "That is the Upland Service for today. The transmitter will run the overnight. I will not.",
        "I am going to close the Service. Eleven tonight, as always. Goodnight.",
        "The Upland Service closes now. It opens at half past five. It has opened at half past five for eleven years and it will open at half past five tomorrow.",
        "That is us. Goodnight to the low ground, goodnight to the far end, and goodnight to whoever has this on in a shed.",
        "Closing down. The overnight is recorded, and I made it this afternoon, so if it sounds cheerful, that is because the afternoon was.",
        "Goodnight. I always feel I should say something here, and I never have anything, and after eleven years I have accepted that.",
        "That is the Service closed. The mast light is on, the door is shut and the dog is in.",
        "Goodnight from the Upland Service. It has been an ordinary day, and ordinary is the good outcome.",
        "I am closing down. Anyone with anything for the Service can leave it at the gate, under the stone, where everything else goes.",
        "That is the day finished. Thank you for your company, which I say to a microphone, and which I mean.",
        "Closing the Service. Tomorrow there is weather at twenty to seven and cards at two, and the rest of it I will find as I go.",
        "Goodnight. The Service will be here. I would like that on the record, because there was a year when I was not sure.",
        "That is the Upland Service, off for the night. The overnight carries on without me and I have made my peace with that.",
        "Goodnight. There is nothing else. There never is, and that is exactly how I like it, and I am going to bed.");

    /// <summary>
    /// The 06:40 weather sequence, and the other three readers.
    ///
    /// story.md Act I beat 4 gives Doss Emery four readers he can tell apart by habit, and
    /// beat 5 turns one of them - the one who says the wind speeds in knots - into the first
    /// time the search touches the player in flight. This bank is the same fixture seen from
    /// the other end: a man who has shared a rota with three people for eleven years and has
    /// never met any of them. He notices the knots. He does not know what it means. He never
    /// will, and nothing in this bank moves the search one inch.
    /// </summary>
    public static readonly DjLine[] Rota = Bank(DjTopic.Rota,
        "The weather sequence is at twenty to seven, as it always is. It is not my turn to read it today, and I am going to listen to it like everybody else.",
        "It is my morning on the sequence. I have the sheet, I have the figures, and I have checked the date three times.",
        "The weather sequence shortly. There are four of us who read it. I am one of the four and I will say no more than that.",
        "One of the others is reading this morning. He clears his throat before every station. Every single one. I have counted them for years.",
        "The sequence at twenty to seven. There is a reader on it who gives the pressure first, before anything, which is not the order on the sheet, and I have raised it.",
        "My turn on the sequence tomorrow. I will prepare tonight, as I always do, and I will still be told I got the date wrong.",
        "The weather sequence comes off a paired channel and reaches me the same way it reaches everybody, which surprises people.",
        "There is a reader on the sequence who gives the wind speeds in knots. Nobody says knots. It has been years and I still notice it every time.",
        "I have never met the other three readers. In eleven years. We have a rota and no arrangement beyond the rota.",
        "The sequence this morning was read at pace. I do not read it at pace. A sequence is not a race, and I have said so, into a microphone, to nobody.",
        "It is the sequence next. I will be quiet for it. I am always quiet for it.",
        "One of the readers has a cold. You can hear it. I hope somebody is looking after them.",
        "The weather sequence at twenty to seven, every day, for longer than the Service has existed. It was going when I got here. I never found out who started it.",
        "I read the sequence yesterday and gave the date as the nineteenth. It was the twentieth. I am aware. I have had a card about it.",
        "The sequence is the only thing on this band that has never once missed a day. Not one, in all that time. Whatever else has gone wrong, that has gone out.",
        "There will be the sequence, and then I will come back. If you only ever listen for the sequence, that is what it is for, and I do not mind.");

    /// <summary>
    /// After the net comes up (story.md 6.2). Gated on the search being complete.
    ///
    /// The ending's reward, heard from the one place in the world that has been listening to
    /// an empty carrier for six years. He does not know why it changed and nobody tells him.
    /// </summary>
    public static readonly DjLine[] NetUp = Bank(DjTopic.NetUp,
        "Something has changed on the band. Ashmount is transmitting. I have been listening to an empty carrier off that relay for six years, and this morning there was a voice on it.",
        "There are other people on the air now. I want to be careful about how pleased I sound.",
        "The Service is no longer the only thing on this band, which I have wanted for eleven years and am finding strange.",
        "I have had contact from a relay I did not know was live. We exchanged station identifications. It took four minutes and I have written all of it down.",
        "Somebody is reading notices from the city. Notices. From the city. I have not had a notice from the city ever.",
        "The net is up. That is the phrase that came across, and I am adopting it.",
        "I spoke to another operator today. An actual operator, at an actual desk. We talked about aerials for some time.",
        "There is traffic on the band now at all hours. I have had to put the second set back into service, which is the best job I have had in years.",
        "I am told there is a rota forming for the notices. A rota. There have been four of us on the weather for eleven years and now there is a rota for notices.",
        "The Service has been relaying for two of the smaller sets this week. That is what a service is supposed to do, and it is the first time I have got to do it.");

    /// <summary>
    /// The murmur between things.
    ///
    /// Short, and mostly dead air with a man in it. Every one of these exists so that a link
    /// does not have to be a performance - the most characterful thing a small station does
    /// is lose its place, and a corpus of nothing but finished sentences sounds like a
    /// corpus.
    /// </summary>
    public static readonly DjLine[] Filler = Bank(DjTopic.Filler,
        "Right.",
        "Anyway.",
        "There we are, then.",
        "Yes.",
        "Where was I.",
        "I have lost my place. One moment.",
        "That is the card gone behind the desk. It will keep.",
        "Sorry. The dog.",
        "Hold on.",
        "Yes. That is all of it.",
        "I will come back to that.",
        "That is everything I had written down.",
        "Right, then.",
        "Give me a second.",
        "Well.",
        "One moment.");

    // =====================================================================================
    //  Substitutions
    // =====================================================================================

    /// <summary>
    /// The listeners, for <c>{CALLER}</c>.
    ///
    /// Deliberately not the nine named NPCs. Those people belong to the dialogue system and
    /// putting them in his mouth would make the station a second, unowned channel for them.
    /// These are the people at the edges: the ones who walk a card up a hill.
    /// </summary>
    public static readonly string[] Callers =
    {
        "Tolly Vance", "Mrs Abbott", "the Dunmore boy", "Peg Sadler", "Ren Motley",
        "old Corliss", "the Wickes family", "Sy Tarrant", "Nance Holloway", "Bram Uttley",
        "Del Fincher", "Ivy Marchbank", "Cass Rowe", "the Pell brothers", "Lott Speight",
        "Maida Crowe", "young Hedley", "the woman from the ford", "Silas Orme", "Bett Kinnaird",
    };

    /// <summary>What {TRACK} becomes when the library has no title for what is playing.</summary>
    public const string UnknownTrack = "that one";

    // =====================================================================================
    //  The whole corpus, for the audit and the counts
    // =====================================================================================

    /// <summary>Every authored line in the file, once each. Used by DjAudit and the tests.</summary>
    public static IEnumerable<DjLine> All
    {
        get
        {
            foreach (DjLine l in Ident) yield return l;
            foreach (DjLine l in TimeCheck) yield return l;
            foreach (DjLine[] bank in Sky.Values) foreach (DjLine l in bank) yield return l;
            foreach (DjLine l in WindNote) yield return l;
            foreach (DjLine l in VisibilityNote) yield return l;
            foreach (DjLine l in TemperatureCold) yield return l;
            foreach (DjLine l in TemperatureWarm) yield return l;
            foreach (DjLine l in CeilingNote) yield return l;
            foreach (DjLine[] bank in Season.Values) foreach (DjLine l in bank) yield return l;
            foreach (DjLine[] bank in Daypart.Values) foreach (DjLine l in bank) yield return l;
            foreach (DjLine l in LostProperty) yield return l;
            foreach (DjLine l in Fence) yield return l;
            foreach (DjLine l in Request) yield return l;
            foreach (DjLine l in Notice) yield return l;
            foreach (DjLine l in Market) yield return l;
            foreach (DjLine l in Correction) yield return l;
            foreach (DjLine l in Birthday) yield return l;
            foreach (DjLine l in Transmitter) yield return l;
            foreach (DjLine l in Dog) yield return l;
            foreach (DjLine[] bank in Aircraft.Values) foreach (DjLine l in bank) yield return l;
            foreach (DjLine l in AircraftQuiet) yield return l;
            foreach (DjLine l in AircraftBusy) yield return l;
            foreach (DjLine l in IntoTrack) yield return l;
            foreach (DjLine l in OutOfTrack) yield return l;
            foreach (DjLine l in SignOn) yield return l;
            foreach (DjLine l in SignOff) yield return l;
            foreach (DjLine l in Rota) yield return l;
            foreach (DjLine l in NetUp) yield return l;
            foreach (DjLine l in Filler) yield return l;
        }
    }

    /// <summary>How many authored lines there are. The headline number for the report.</summary>
    public static int Count => All.Count();

    /// <summary>Line counts by topic, for a report or a test that wants to print them.</summary>
    public static IReadOnlyList<(DjTopic Topic, int Lines)> CountsByTopic() =>
        All.GroupBy(l => l.Topic)
           .OrderByDescending(g => g.Count())
           .Select(g => (g.Key, g.Count()))
           .ToList();
}

/// <summary>
/// The three rules this character can break, expressed as things a machine can check.
///
/// Every one of them is a rule a human writer will break by accident and never notice,
/// which is exactly what a blocklist is for. The discipline is D-006's and is worth
/// restating because the project has already paid for getting it wrong once: these are
/// PHRASINGS, not sentences. The earlier coda blocklist listed "i can offer" and let "I
/// could offer you work if you want it" straight through.
///
/// Each list ships with its own self-check in <see cref="Probes"/>: a set of lines that
/// MUST trip it, so the blocklist cannot quietly become decoration, and a set of legitimate
/// lines that must NOT trip it, so it cannot quietly become a straitjacket.
/// </summary>
public static class DjAudit
{
    /// <summary>
    /// D-008. No second flyer, no second machine in the air, ever.
    ///
    /// Copied in shape from DialogueTests.PremiseGuard, and extended for the phrasings a
    /// radio announcer specifically would reach for - reported sightings, plurals, and the
    /// "one of them" construction that a man relaying gossip falls into naturally. Past-tense
    /// aviation is deliberately not blocked: it is the setting, and the collapse ending it is
    /// half of why the premise works at all.
    /// </summary>
    public static readonly string[] RivalFlyer =
    {
        "another pilot", "other pilot", "second pilot", "two pilots", "pilot like",
        "fellow pilot", "rival pilot", "pilots left", "another flyer", "other flyers",
        "another helicopter", "other helicopter", "second helicopter", "two helicopters",
        "another rotor", "other rotors", "two aircraft", "both aircraft",
        "the other aircraft", "another aircraft", "machine like yours", "one like yours",
        "another one like", "someone else flying", "somebody else flying", "anyone else flying",
        "flies one too", "fly one too", "still flying one", "another bird",
        "other birds in the air", "work together in the air", "two of you up there",
        "one of the helicopters", "a few of them left", "one of them again",
        "the helicopters", "helicopters have", "helicopters are", "more than one of them",
        "the second machine", "the other machine",
    };

    /// <summary>
    /// D-005a. He reports, gossips and is wrong. He never tells anybody what to do.
    ///
    /// The distinction that matters, and the one the corpus is written to: a notice is a
    /// FACT ABOUT A NOTICE. He is allowed to say that the notice asks people not to use the
    /// ford. He is not allowed to ask people not to use the ford. Everything in this list is
    /// a phrasing that turns a station into a quest marker.
    /// </summary>
    public static readonly string[] Instructional =
    {
        "you should", "you ought", "you need to", "you must", "you will want to",
        "you had better", "i would advise", "i advise", "my advice", "i recommend",
        "i would recommend", "your best bet", "if i were you", "make sure you",
        "be sure to", "do not forget to", "don't forget to", "remember to",
        "head for", "head to", "head over", "make your way", "get yourself",
        "you want to go", "go and find", "go and look for", "look for the",
        "fly to", "fly over to", "steer clear", "stay away from", "keep away from",
        "avoid the", "take the road", "follow the", "try the",
    };

    /// <summary>
    /// D-058. Never winking at the player.
    ///
    /// He does not know anybody is listening, he has no idea the aircraft he keeps mentioning
    /// is receiving him, and he never addresses the pilot. Addressing LISTENERS is fine and
    /// is what a station does - "you are listening to the Upland Service" is correct. What is
    /// blocked is the moment a line turns and speaks to the one person holding the controls.
    /// </summary>
    public static readonly string[] PlayerWink =
    {
        "if you are flying", "if you're flying", "whoever is listening up there",
        "you up there", "you know who you are", "our friend in the sky",
        "you in the helicopter", "if you can hear me up there", "i know you are listening",
        "i know you're listening", "this one is for the pilot", "to the pilot",
        "whoever you are up there", "wave as you go", "mind how you fly",
    };

    /// <summary>The only substitution tokens that may appear in an authored line.</summary>
    public static readonly string[] KnownTokens = { "{REGION}", "{CALLER}", "{NEIGHBOUR}", "{TRACK}" };

    /// <summary>One thing wrong with one line.</summary>
    public readonly record struct Finding(string LineId, string Rule, string Phrase, string Text)
    {
        public override string ToString() => $"{LineId}: {Rule} \"{Phrase}\" in \"{Text}\"";
    }

    /// <summary>Run all three blocklists and the token check over a set of lines.</summary>
    public static List<Finding> Scan(IEnumerable<DjLine> lines)
    {
        var findings = new List<Finding>();
        foreach (DjLine l in lines)
        {
            foreach (string bad in RivalFlyer)
                if (l.Text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    findings.Add(new Finding(l.Id, "D-008", bad, l.Text));

            foreach (string bad in Instructional)
                if (l.Text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    findings.Add(new Finding(l.Id, "D-005a", bad, l.Text));

            foreach (string bad in PlayerWink)
                if (l.Text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    findings.Add(new Finding(l.Id, "D-058", bad, l.Text));

            foreach (string token in UnknownTokensIn(l.Text))
                findings.Add(new Finding(l.Id, "token", token, l.Text));
        }
        return findings;
    }

    /// <summary>Any <c>{...}</c> in the text that is not one the host knows how to fill.</summary>
    public static IEnumerable<string> UnknownTokensIn(string text)
    {
        int i = 0;
        while (true)
        {
            int open = text.IndexOf('{', i);
            if (open < 0) yield break;
            int close = text.IndexOf('}', open);
            if (close < 0) { yield return text[open..]; yield break; }
            string token = text[open..(close + 1)];
            if (Array.IndexOf(KnownTokens, token) < 0) yield return token;
            i = close + 1;
        }
    }

    /// <summary>
    /// The self-check corpus: lines that must trip a rule, and lines that must not.
    ///
    /// A blocklist that has never been shown to catch anything is a comment. These probes are
    /// the shapes that would actually be written by accident - the D-050 rival-pilot beats
    /// that shipped once already, the quest-marker phrasings a writer reaches for when they
    /// want the station to be useful, and the wink that arrives the first time somebody
    /// thinks it would be funny if he noticed.
    /// </summary>
    public static (string[] MustTrip, string[] MustPass) Probes()
    {
        var mustTrip = new[]
        {
            "There is another pilot out east who flies one like yours.",
            "Two aircraft can carry what one cannot, is what they are saying.",
            "I saw a second helicopter over the ridge last week.",
            "Word is the helicopters are up again.",
            "You should head for the field at Fenmoor before dark.",
            "My advice is to stay away from the uplands this week.",
            "Don't forget to look for the crate at the depot.",
            "If you are flying tonight, mind how you fly over the city.",
            "I know you are listening up there, and this one is for the pilot.",
        };

        var mustPass = new[]
        {
            "Before, there were four of these a day over that ridge. I never once looked up.",
            "I worked the field. Chocks, fuel, loadsheets, eleven years.",
            "There is nothing else in the sky, so there is nothing else it could be.",
            "The notice says the plank has been out since Thursday. That is all the notice says.",
            "You are listening to the Upland Service. There is nothing else on this frequency.",
            "Somebody at the ford says they heard the helicopter go over.",
            "I am going to put this on and go and look at the generator.",
        };

        return (mustTrip, mustPass);
    }

    /// <summary>True when any of the three blocklists matches. For the probe check.</summary>
    public static bool Trips(string text) =>
        RivalFlyer.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)) ||
        Instructional.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)) ||
        PlayerWink.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The scheduler: how often he talks, what he does between tracks, and what he never repeats.
///
/// <b>He speaks between tracks and never over them.</b> That is one decision and it settles
/// most of the design. It means the hook the radio hardware needs is a single event that
/// already exists - <c>RadioSet.TrackChanged</c> - and it means a player who does not want
/// to hear him can simply not tune that station, which is the same rule Radio.cs already
/// holds itself to: the set never fights the player.
///
/// <b>He does not talk at every gap.</b> A station that links every track is a station that
/// is performing. <see cref="LinkChance"/> is the base odds at a boundary, floored by
/// <see cref="MaxSilentSeconds"/> so a player who has been listening a while always hears
/// somebody, and ceilinged by <see cref="MinGapSeconds"/> so he can never talk twice in a
/// row over a short track.
///
/// <b>The overnight is recorded.</b> Between sign-off and sign-on he is asleep and the
/// station runs unattended, so the links go rare and shrink to idents and the recorded
/// overnight lines. This costs nothing and it is the single cheapest way to make a station
/// feel like a place with a person in it rather than a playlist with a voice on top.
///
/// <b>Nothing repeats until everything has aired.</b> Every pool is a shuffle bag: a line is
/// drawn, removed, and cannot come back until the bag is empty and refilled. That is a
/// stronger guarantee than a recency window and it is trivially testable, which is why it is
/// this and not a window.
/// </summary>
public sealed class DjHost
{
    // ------------------------------------------------------------------ knobs

    /// <summary>Odds he links at a given track boundary, in his waking hours.</summary>
    public double LinkChance { get; set; } = 0.55;

    /// <summary>Odds he links at a boundary overnight, when the station is unattended.</summary>
    public double NightLinkChance { get; set; } = 0.16;

    /// <summary>He will not link twice inside this, whatever the track lengths are doing.</summary>
    public double MinGapSeconds { get; set; } = 150.0;

    /// <summary>And he will always link once this much station time has passed without one.</summary>
    public double MaxSilentSeconds { get; set; } = 900.0;

    /// <summary>Odds a link that could be one segment is two or three instead.</summary>
    public double SecondSegmentChance { get; set; } = 0.55;
    public double ThirdSegmentChance { get; set; } = 0.22;

    /// <summary>Odds a waking link opens by coming out of the track it followed.</summary>
    public double OutroChance { get; set; } = 0.60;

    /// <summary>Odds a link closes by going into the next one.</summary>
    public double IntroChance { get; set; } = 0.70;

    /// <summary>Odds he loses his place somewhere in a multi-segment link.</summary>
    public double FillerChance { get; set; } = 0.14;

    // ------------------------------------------------------------------ state

    private readonly Random _rng;
    private readonly Dictionary<string, List<DjLine>> _bags = new();
    private readonly HashSet<string> _aired = new();
    private double _lastBreakAt = double.NegativeInfinity;

    /// <summary>Every line id that has ever gone out of this host. For tests and transcripts.</summary>
    public IReadOnlyCollection<string> Aired => _aired;

    /// <summary>How many links this host has composed.</summary>
    public int Breaks { get; private set; }

    /// <summary>How many segments, across all of them.</summary>
    public int SegmentsSpoken { get; private set; }

    public DjHost(int seed = 90210) => _rng = new Random(seed);

    /// <summary>Forget everything. New game, not new sortie.</summary>
    public void Reset()
    {
        _bags.Clear();
        _aired.Clear();
        _lastBreakAt = double.NegativeInfinity;
        Breaks = 0;
        SegmentsSpoken = 0;
    }

    // -------------------------------------------------------------- the hook

    /// <summary>
    /// A track just ended on his station. Returns what he says, or null for straight into
    /// the next one.
    ///
    /// This is the whole integration surface. The game layer calls it when
    /// <c>RadioSet.TrackChanged</c> comes up true on a Station source tuned to his frequency,
    /// and puts the returned segments on the strip in order, each for its own
    /// <see cref="DjSegment.Seconds"/>.
    /// </summary>
    public DjBreak? OnTrackBoundary(in DjWorld w)
    {
        double since = w.ClockSeconds - _lastBreakAt;
        if (since < MinGapSeconds) return null;

        bool overnight = IsOvernight(w.Hour);
        double chance = overnight ? NightLinkChance : LinkChance;

        // The floor. A player who has had the station on for a quarter of an hour has heard
        // a human being, whatever the dice did.
        bool forced = since >= MaxSilentSeconds;
        if (!forced && _rng.NextDouble() > chance) return null;

        return Compose(w, overnight);
    }

    /// <summary>
    /// Make him talk now, about a named topic. For the fixtures, for a test, and for a
    /// transcript tool. Returns null when that topic has nothing legal to say in this world.
    /// </summary>
    public DjBreak? Say(in DjWorld w, DjTopic topic)
    {
        DjSegment? seg = Segment(w, topic);
        if (seg is null) return null;
        _lastBreakAt = w.ClockSeconds;
        Breaks++;
        SegmentsSpoken++;
        return new DjBreak(new[] { seg.Value }, w.ClockSeconds);
    }

    // --------------------------------------------------------------- fixtures

    /// <summary>Outside his waking hours the station runs on its own.</summary>
    public static bool IsOvernight(double hour)
        => hour < RadioDj.SignOnHour || hour >= RadioDj.SignOffHour;

    /// <summary>
    /// Whether the 06:40 weather sequence falls inside a window around this moment.
    ///
    /// Exposed rather than used: the sequence belongs to the story radio (story.md 4.3, beat
    /// 5) and this file does not schedule it, does not read it and must never pre-empt it.
    /// What is here is the announcer's awareness that it is coming, which is the
    /// <see cref="DjTopic.Rota"/> bank.
    /// </summary>
    public static bool WeatherSequenceDue(double clockSeconds, double windowMinutes = 20)
    {
        double hour = (clockSeconds / 3600.0) % 24.0;
        return Math.Abs(hour - RadioDj.WeatherSequenceHour) * 60.0 <= windowMinutes;
    }

    /// <summary>Whether today is his turn on the four-reader rota. Purely for flavour.</summary>
    public static bool HisTurnOnRota(double clockSeconds)
        => (int)(clockSeconds / 86400.0) % RadioDj.RotaSize == RadioDj.RotaSlot;

    // -------------------------------------------------------------- composing

    private DjBreak Compose(in DjWorld w, bool overnight)
    {
        var segments = new List<DjSegment>();

        if (overnight)
        {
            // Recorded, so no outro, no intro, no reacting to anything that happened today.
            AddIfAny(segments, Segment(w, _rng.NextDouble() < 0.5 ? DjTopic.Daypart : DjTopic.Ident));
            if (segments.Count > 0 && _rng.NextDouble() < 0.25)
                AddIfAny(segments, Segment(w, DjTopic.Ident));
            return Finish(segments, w);
        }

        // Sign-on and sign-off are fixtures and take the whole link.
        if (InWindow(w.Hour, RadioDj.SignOnHour, 0.5))
        {
            AddIfAny(segments, Segment(w, DjTopic.SignOn));
            if (_rng.NextDouble() < 0.6) AddIfAny(segments, Segment(w, DjTopic.Sky));
            return Finish(segments, w);
        }
        if (InWindow(w.Hour, RadioDj.SignOffHour, 0.5))
        {
            if (_rng.NextDouble() < 0.4) AddIfAny(segments, Segment(w, DjTopic.OutOfTrack));
            AddIfAny(segments, Segment(w, DjTopic.SignOff));
            return Finish(segments, w);
        }

        if (_rng.NextDouble() < OutroChance) AddIfAny(segments, Segment(w, DjTopic.OutOfTrack));

        int body = 1;
        if (_rng.NextDouble() < SecondSegmentChance) body++;
        if (body == 2 && _rng.NextDouble() < ThirdSegmentChance) body++;

        var used = new List<DjTopic>();
        for (int i = 0; i < body; i++)
        {
            DjTopic? t = PickTopic(w, used);
            if (t is null) break;
            used.Add(t.Value);
            if (i > 0 && _rng.NextDouble() < FillerChance) AddIfAny(segments, Segment(w, DjTopic.Filler));
            AddIfAny(segments, Segment(w, t.Value));
        }

        if (_rng.NextDouble() < IntroChance) AddIfAny(segments, Segment(w, DjTopic.IntoTrack));

        // A link that somehow emptied - every eligible bag refused - is a link that does not
        // happen. Silence is always a legal thing for a radio station to do.
        return Finish(segments, w);
    }

    private DjBreak Finish(List<DjSegment> segments, in DjWorld w)
    {
        _lastBreakAt = w.ClockSeconds;
        Breaks++;
        SegmentsSpoken += segments.Count;
        return new DjBreak(segments, w.ClockSeconds);
    }

    private static void AddIfAny(List<DjSegment> into, DjSegment? seg)
    {
        if (seg is not null) into.Add(seg.Value);
    }

    private static bool InWindow(double hour, double centreHour, double halfWidthHours)
        => Math.Abs(hour - centreHour) <= halfWidthHours;

    // ---------------------------------------------------------- topic weights

    /// <summary>
    /// What he could talk about now, and how much he wants to.
    ///
    /// Every entry is gated on the world actually being in that state. That is the rule the
    /// whole design rests on and the one the tests attack hardest: a wind line needs wind, a
    /// visibility line needs the visibility to have gone, a rota line needs it to be morning,
    /// and an aircraft line needs somewhere to have genuinely seen the aircraft.
    /// </summary>
    private DjTopic? PickTopic(in DjWorld w, List<DjTopic> already)
    {
        var options = new List<(DjTopic Topic, double Weight)>();

        void Offer(DjTopic t, double weight, bool when = true)
        {
            if (when && weight > 0 && !already.Contains(t)) options.Add((t, weight));
        }

        DjDaypart part = w.Daypart;

        Offer(DjTopic.Ident, 0.45);
        Offer(DjTopic.TimeCheck, 0.80);
        Offer(DjTopic.Sky, 1.60);
        // The conditional weather topics sit at ordinary conversational weight, which is
        // right: they are only offered at all when the weather is doing something, so the
        // gate does the selecting and the weight only decides how he paces it among the
        // other things on his mind.
        //
        // These were briefly inflated to 2.40 to fix an announcer who "would not mention a
        // gale". He mentioned it fine; the test driving him had frozen his clock at noon,
        // so he had spoken twice in a simulated twenty-nine hours. Measured against a
        // session that actually runs, a full storm gets 18 wind remarks, 19 on the
        // visibility, 13 on the cloudbase and 11 on the cold out of 609 segments - about
        // one line in forty each at these weights, which is a man remarking on the weather
        // rather than a man obsessed with it. A calm clear day still gets none of any.
        Offer(DjTopic.WindNote, 1.05, w.GustMs > 6.0 || w.WindSpeedMs > 9.0);
        Offer(DjTopic.VisibilityNote, 1.10, w.VisibilityM < 6000);
        Offer(DjTopic.TemperatureNote, 0.85, Math.Abs(w.IsaDeviation) > 5.0);
        Offer(DjTopic.CeilingNote, 0.85, w.CloudBaseM < 500);
        Offer(DjTopic.Season, 0.50);
        Offer(DjTopic.Daypart, 0.90);

        // The cards come out at two. He has said so on the air for eleven years.
        Offer(DjTopic.LostProperty, part == DjDaypart.Afternoon ? 2.40 : 1.15);
        Offer(DjTopic.Fence, 1.00);
        Offer(DjTopic.Request, part == DjDaypart.Evening ? 1.60 : 0.95);
        Offer(DjTopic.Notice, 1.10);
        Offer(DjTopic.Market, 0.70);
        Offer(DjTopic.Correction, 0.70);
        Offer(DjTopic.Birthday, 0.60);
        Offer(DjTopic.Transmitter, 0.90);
        Offer(DjTopic.Dog, 0.80);

        // The headline subject, and the only one gated on another system's state.
        Offer(DjTopic.Aircraft, 1.80, HasNamedRegion(w));
        Offer(DjTopic.AircraftQuiet, 0.50, !HasNamedRegion(w));
        Offer(DjTopic.AircraftBusy, 1.00, CountNamed(w) >= 3);

        Offer(DjTopic.Rota, 2.00, w.Hour >= 6.0 && w.Hour < 7.5);
        Offer(DjTopic.NetUp, 1.20, w.SearchComplete);

        if (options.Count == 0) return null;

        double total = options.Sum(o => o.Weight);
        double roll = _rng.NextDouble() * total;
        foreach ((DjTopic topic, double weight) in options)
        {
            roll -= weight;
            if (roll <= 0) return topic;
        }
        return options[^1].Topic;
    }

    private static bool HasNamedRegion(in DjWorld w) => CountNamed(w) > 0;

    private static int CountNamed(in DjWorld w)
    {
        if (w.Heat is null) return 0;
        int n = 0;
        foreach (DjRegionHeat h in w.Heat)
            if (h.Band != DjHeatBand.Quiet && !string.IsNullOrWhiteSpace(h.Name)) n++;
        return n;
    }

    // ------------------------------------------------------------- one segment

    /// <summary>Draw one line for a topic and resolve it, or null when there is nothing legal.</summary>
    private DjSegment? Segment(in DjWorld w, DjTopic topic)
    {
        string key = topic.ToString();
        IReadOnlyList<DjLine> pool;
        DjRegionHeat region = default;

        switch (topic)
        {
            case DjTopic.Ident: pool = DjCorpus.Ident; break;
            case DjTopic.TimeCheck: pool = DjCorpus.TimeCheck; break;
            case DjTopic.Sky:
                pool = DjCorpus.Sky[w.Sky];
                key = $"Sky.{w.Sky}";
                break;
            case DjTopic.WindNote: pool = DjCorpus.WindNote; break;
            case DjTopic.VisibilityNote: pool = DjCorpus.VisibilityNote; break;
            case DjTopic.TemperatureNote:
                bool cold = w.IsaDeviation < 0;
                pool = cold ? DjCorpus.TemperatureCold : DjCorpus.TemperatureWarm;
                key = cold ? "Temp.cold" : "Temp.warm";
                break;
            case DjTopic.CeilingNote: pool = DjCorpus.CeilingNote; break;
            case DjTopic.Season:
                pool = DjCorpus.Season[w.Season];
                key = $"Season.{w.Season}";
                break;
            case DjTopic.Daypart:
                pool = DjCorpus.Daypart[w.Daypart];
                key = $"Daypart.{w.Daypart}";
                break;
            case DjTopic.LostProperty: pool = DjCorpus.LostProperty; break;
            case DjTopic.Fence: pool = DjCorpus.Fence; break;
            case DjTopic.Request: pool = DjCorpus.Request; break;
            case DjTopic.Notice: pool = DjCorpus.Notice; break;
            case DjTopic.Market: pool = DjCorpus.Market; break;
            case DjTopic.Correction: pool = DjCorpus.Correction; break;
            case DjTopic.Birthday: pool = DjCorpus.Birthday; break;
            case DjTopic.Transmitter: pool = DjCorpus.Transmitter; break;
            case DjTopic.Dog: pool = DjCorpus.Dog; break;

            case DjTopic.Aircraft:
            {
                // The gate. A named-region line exists only for a region AlertState has
                // actually raised, and only in the band that region is actually in.
                DjRegionHeat? pick = PickRegion(w);
                if (pick is null) return null;
                region = pick.Value;
                pool = DjCorpus.Aircraft[region.Band];
                key = $"Aircraft.{region.Band}";
                break;
            }
            case DjTopic.AircraftQuiet:
                if (HasNamedRegion(w)) return null;
                pool = DjCorpus.AircraftQuiet;
                break;
            case DjTopic.AircraftBusy:
                if (CountNamed(w) < 3) return null;
                pool = DjCorpus.AircraftBusy;
                break;

            case DjTopic.IntoTrack: pool = DjCorpus.IntoTrack; break;
            case DjTopic.OutOfTrack: pool = DjCorpus.OutOfTrack; break;
            case DjTopic.SignOn: pool = DjCorpus.SignOn; break;
            case DjTopic.SignOff: pool = DjCorpus.SignOff; break;
            case DjTopic.Rota: pool = DjCorpus.Rota; break;
            case DjTopic.NetUp:
                if (!w.SearchComplete) return null;
                pool = DjCorpus.NetUp;
                break;
            case DjTopic.Filler: pool = DjCorpus.Filler; break;
            default: return null;
        }

        DjLine? drawn = Draw(key, pool);
        if (drawn is null) return null;

        DjLine line = drawn.Value;
        string text = Resolve(line.Text, w, region);
        _aired.Add(line.Id);
        return new DjSegment(line.Id, topic, text, RadioDj.ReadSeconds(text));
    }

    /// <summary>
    /// Which region he is repeating gossip about.
    ///
    /// Weighted by how awake the place is, because the loudest place is the one people are
    /// writing to him about - but not simply the hottest, because a man reading cards reads
    /// the cards he has, and a quieter place that sent one in still gets read out.
    /// </summary>
    private DjRegionHeat? PickRegion(in DjWorld w)
    {
        if (w.Heat is null || w.Heat.Count == 0) return null;

        double total = 0;
        foreach (DjRegionHeat h in w.Heat)
            if (h.Band != DjHeatBand.Quiet && !string.IsNullOrWhiteSpace(h.Name)) total += h.Level;
        if (total <= 0) return null;

        double roll = _rng.NextDouble() * total;
        foreach (DjRegionHeat h in w.Heat)
        {
            if (h.Band == DjHeatBand.Quiet || string.IsNullOrWhiteSpace(h.Name)) continue;
            roll -= h.Level;
            if (roll <= 0) return h;
        }

        foreach (DjRegionHeat h in w.Heat)
            if (h.Band != DjHeatBand.Quiet && !string.IsNullOrWhiteSpace(h.Name)) return h;
        return null;
    }

    /// <summary>
    /// The shuffle bag: every line in a pool airs once before any of them airs twice.
    ///
    /// A recency window would have been the obvious choice and is worse. A window has to be
    /// tuned against pool size, it makes no promise a test can assert without statistics, and
    /// it still lets the same line come back twice in an evening if the dice feel like it.
    /// This makes the guarantee absolute and the test a one-liner.
    /// </summary>
    private DjLine? Draw(string key, IReadOnlyList<DjLine> pool)
    {
        if (pool.Count == 0) return null;

        if (!_bags.TryGetValue(key, out List<DjLine>? bag) || bag.Count == 0)
        {
            bag = new List<DjLine>(pool);
            _bags[key] = bag;
        }

        // A shuffle bag alone is not enough, and the seam is at the refill.
        //
        // Emptying a bag guarantees every line is heard once per cycle, but says nothing
        // across the boundary: the last line of one cycle and the first of the next can be
        // the same line, and a listener does not perceive cycles, only "he just said that".
        // Every bank in the corpus failed on exactly that, always at the first refill.
        //
        // So the bag is filtered by a rolling memory of the last (n-1) lines from this key.
        // With a full memory that leaves precisely the one line not recently heard, which
        // makes each cycle a permutation that never collides with its neighbour.
        if (!_recent.TryGetValue(key, out Queue<string>? recent))
            _recent[key] = recent = new Queue<string>();

        List<DjLine> candidates = bag.FindAll(l => !recent.Contains(l.Id));
        if (candidates.Count == 0) candidates = bag;          // corpus too thin; say something

        DjLine line = candidates[_rng.Next(candidates.Count)];
        bag.Remove(line);

        recent.Enqueue(line.Id);
        while (recent.Count > Math.Max(pool.Count - 1, 0)) recent.Dequeue();
        return line;
    }

    private readonly Dictionary<string, Queue<string>> _recent = new();

    /// <summary>
    /// Fill the tokens.
    ///
    /// Belt and braces at the end: anything still in braces after the known tokens have been
    /// substituted is stripped rather than shown, because a visible <c>{REGION}</c> on the
    /// HUD strip is worse than any line this file could possibly contain.
    /// </summary>
    private string Resolve(string text, in DjWorld w, in DjRegionHeat region)
    {
        if (text.Contains("{REGION}", StringComparison.Ordinal))
        {
            string name = string.IsNullOrWhiteSpace(region.Name) ? w.Hottest.Name : region.Name;
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;   // never fabricate a place
            text = text.Replace("{REGION}", name, StringComparison.Ordinal);
        }

        if (text.Contains("{CALLER}", StringComparison.Ordinal))
        {
            // One caller per line, however many times the line names them: he is reading one
            // card, not two.
            string caller = DjCorpus.Callers[_rng.Next(DjCorpus.Callers.Length)];
            text = text.Replace("{CALLER}", caller, StringComparison.Ordinal);
        }

        if (text.Contains("{NEIGHBOUR}", StringComparison.Ordinal))
            text = text.Replace("{NEIGHBOUR}", RadioDj.Neighbour, StringComparison.Ordinal);

        if (text.Contains("{TRACK}", StringComparison.Ordinal))
        {
            string track = string.IsNullOrWhiteSpace(w.TrackTitle) ? DjCorpus.UnknownTrack : w.TrackTitle!;
            text = text.Replace("{TRACK}", track, StringComparison.Ordinal);
        }

        return StripAnyRemainingTokens(text);
    }

    private static string StripAnyRemainingTokens(string text)
    {
        if (!text.Contains('{')) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        bool inToken = false;
        foreach (char c in text)
        {
            if (c == '{') { inToken = true; continue; }
            if (c == '}') { inToken = false; continue; }
            if (!inToken) sb.Append(c);
        }
        return sb.ToString().Replace("  ", " ").Trim();
    }
}
