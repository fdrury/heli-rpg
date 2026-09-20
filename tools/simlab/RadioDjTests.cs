using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The announcer, on the bench.
///
/// Four things have to be true of this character or he is worse than no character at all,
/// and each of them is a thing a listen cannot establish and a measurement can:
///
///   1. There is enough material that a long session does not loop. A DJ with thirty lines
///      teaches the player, within an hour, that he is a loop - and that lesson cannot be
///      untaught by adding lines later.
///   2. World-state lines only fire when that state is true. The whole reason the aircraft
///      gossip is worth building is that AlertState really does know which regions have seen
///      the player; a line that names a region which has never seen anything is a lie told
///      by a system that was supposed to be surfacing the truth.
///   3. No line implies a second flyer (D-008), which is premise, not preference.
///   4. No line instructs the player (D-005a). He may be wrong, he may gossip, he may
///      editorialise. He may not be a quest marker.
///
/// Everything below is a measurement, not an assertion of intent. Per the project rule that
/// every real bug here has been invisible in code and obvious in a number: the corpus is
/// scanned rather than trusted, the gating is exercised over hundreds of composed links
/// rather than reasoned about, and the no-repeat guarantee is checked against the actual
/// draw order rather than against a line count.
/// </summary>
public static class RadioDjTests
{
    // ------------------------------------------------------------------ helpers

    /// <summary>The eight regions, by the ids AlertState keys on. Mirrors WorldMap order.</summary>
    static readonly string[] RegionNames =
    {
        "The Pan", "Long Acre", "Fenmoor", "The Drowning",
        "Cold Shoulder", "Sawtooth Works", "Ashmount", "The Scald",
    };

    static string NameOf(int id) => id >= 0 && id < RegionNames.Length ? RegionNames[id] : "";

    static DjWorld W(double clock = 12 * 3600,
                     SkyCondition sky = SkyCondition.Fair,
                     double wind = 4.0, double gust = 1.5, double vis = 18000,
                     double cloudBase = 2000, double isa = 0,
                     DjRegionHeat[]? heat = null, bool searchComplete = false,
                     string? track = null)
        => new(clock, sky, wind, gust, vis, cloudBase, isa, 0, 0,
               heat ?? Array.Empty<DjRegionHeat>(), searchComplete, track, null);

    /// <summary>
    /// Run a station for a while and collect everything he said.
    ///
    /// One boundary per track, which is exactly how the game layer will drive it: the hook is
    /// <c>RadioSet.TrackChanged</c>, and <see cref="Radio.AssumedTrackSeconds"/> is the length
    /// the radio itself assumes when a file does not declare one.
    /// </summary>
    static List<DjSegment> Session(DjHost host, Func<double, DjWorld> world,
                                   double startClock, int boundaries,
                                   double trackSeconds = Radio.AssumedTrackSeconds)
    {
        var said = new List<DjSegment>();
        double clock = startClock;
        for (int i = 0; i < boundaries; i++)
        {
            DjBreak? link = host.OnTrackBoundary(world(clock));
            if (link is not null) said.AddRange(link.Segments);
            clock += trackSeconds;
        }
        return said;
    }

    /// <summary>The bank an id came from: everything up to the final index.</summary>
    static string BankOf(string lineId)
    {
        int last = lineId.LastIndexOf('.');
        return last < 0 ? lineId : lineId[..last];
    }

    // =====================================================================================
    //  1. There is enough of him
    // =====================================================================================

    /// <summary>
    /// How much material there is, per topic, and that none of it is duplicated.
    ///
    /// The duplicate check is the one that earns its place: writing five hundred lines in one
    /// sitting is exactly the circumstance under which the same sentence gets written twice
    /// in two banks, and a duplicate is invisible in the source and audible in the game.
    /// </summary>
    public static string? Volume()
    {
        var all = DjCorpus.All.ToList();
        Console.WriteLine($"  {all.Count} authored lines across {DjCorpus.CountsByTopic().Count} topics");
        Console.WriteLine();
        foreach ((DjTopic topic, int lines) in DjCorpus.CountsByTopic())
            Console.WriteLine($"    {topic,-18} {lines,4}");
        Console.WriteLine();

        // Ids must be unique or the shuffle bags and the recency reporting both lie.
        var byId = new HashSet<string>();
        var dupIds = all.Where(l => !byId.Add(l.Id)).ToList();
        foreach (DjLine d in dupIds) Console.WriteLine($"  !! duplicate id {d.Id}");

        var byText = new Dictionary<string, string>();
        var dupText = new List<string>();
        foreach (DjLine l in all)
        {
            if (byText.TryGetValue(l.Text, out string? first))
                dupText.Add($"{l.Id} repeats {first}: \"{l.Text}\"");
            else byText[l.Text] = l.Id;
        }
        foreach (string d in dupText) Console.WriteLine($"  !! {d}");

        // Every bank has to be deep enough that its own rotation is not a loop. Eight is the
        // floor for a bank that only fires in one weather state; the everyday banks are far
        // deeper and the counts above say by how much.
        var thin = DjCorpus.All
            .GroupBy(l => BankOf(l.Id))
            .Where(g => g.Count() < 8)
            .Select(g => $"{g.Key} has only {g.Count()}")
            .ToList();
        foreach (string t in thin) Console.WriteLine($"  !! {t}");

        double totalSpeech = all.Sum(l => RadioDj.ReadSeconds(l.Text));
        Console.WriteLine($"  read end to end: {totalSpeech / 60:F0} minutes of speech at {RadioDj.WordsPerSecond * 60:F0} words a minute");

        if (dupIds.Count > 0) return $"{dupIds.Count} duplicate line id(s)";
        if (dupText.Count > 0) return $"{dupText.Count} line(s) written twice";
        if (thin.Count > 0) return $"{thin.Count} bank(s) below the eight-line floor";
        if (all.Count < 400) return $"only {all.Count} lines; a station needs enough not to loop";
        return null;
    }

    /// <summary>
    /// A long session, and the no-repeat guarantee measured against the actual draw order.
    ///
    /// The guarantee is a shuffle bag rather than a recency window: within any run of N draws
    /// from a bank of N lines, every line must be different. That is stronger than "he does
    /// not repeat often", it is exact, and this is what checks it - by bank, from the ids, in
    /// the order they actually went out.
    /// </summary>
    public static string? EnoughToNotRepeat()
    {
        var host = new DjHost(4711);

        // A full day of station time, with the weather and the hour both moving, so the
        // banks that are gated on state get exercised rather than starved.
        var weather = new Weather(20260919);
        double start = 172 * 86400;
        List<DjSegment> said = Session(host, clock =>
        {
            Weather.Conditions wx = weather.At(clock);
            return RadioDj.Observe(clock, wx, null, null);
        }, start, 400);

        int distinct = said.Select(s => s.Id).Distinct().Count();
        Console.WriteLine($"  {host.Breaks} links over {400 * Radio.AssumedTrackSeconds / 3600:F1} hours of station time");
        Console.WriteLine($"  {said.Count} segments, {distinct} distinct lines");

        // Where the first repeat of anything falls. Not a pass condition - a number worth
        // seeing, because it is the answer to "how long before he sounds like a loop".
        var seen = new HashSet<string>();
        int firstRepeat = -1;
        for (int i = 0; i < said.Count; i++)
            if (!seen.Add(said[i].Id)) { firstRepeat = i; break; }
        Console.WriteLine(firstRepeat < 0
            ? "  nothing repeated at all in this session"
            : $"  first repeat of any line at segment {firstRepeat}");

        // The actual guarantee, per bank.
        var order = new Dictionary<string, List<string>>();
        foreach (DjSegment s in said)
        {
            string bank = BankOf(s.Id);
            if (!order.TryGetValue(bank, out List<string>? list)) order[bank] = list = new List<string>();
            list.Add(s.Id);
        }

        var sizes = DjCorpus.All.GroupBy(l => BankOf(l.Id)).ToDictionary(g => g.Key, g => g.Count());
        var broken = new List<string>();
        foreach ((string bank, List<string> draws) in order)
        {
            int size = sizes.GetValueOrDefault(bank, 1);
            for (int i = 0; i + size <= draws.Count; i++)
            {
                var window = new HashSet<string>(draws.Skip(i).Take(size));
                if (window.Count < size)
                {
                    broken.Add($"{bank}: a line came back inside a window of {size} draws (at draw {i})");
                    break;
                }
            }
        }
        foreach (string b in broken) Console.WriteLine($"  !! {b}");

        Console.WriteLine($"  {order.Count} banks drawn from; deepest rotation {order.Values.Max(v => v.Count)} draws");

        if (broken.Count > 0) return $"{broken.Count} bank(s) repeated a line before exhausting";
        if (distinct < 200) return $"only {distinct} distinct lines in a full day of station time";
        return null;
    }

    // =====================================================================================
    //  2. He only speaks of what was actually seen
    // =====================================================================================

    /// <summary>
    /// The gate on the aircraft gossip, driven by a real <see cref="AlertState"/>.
    ///
    /// One region is flown through and nowhere else is touched. Every named-region line he
    /// composes for the rest of the session must name that region and no other - which is the
    /// assertion that makes the whole conceit honest, because the moment he can name a place
    /// that never saw anything, he is not surfacing a system, he is decorating one.
    /// </summary>
    public static string? OnlySpeaksOfWhatWasSeen()
    {
        var alert = new AlertState();

        // Two minutes of being watched over Fenmoor (region 2) and nothing anywhere else.
        const int watched = 2;
        for (int i = 0; i < 120; i++) alert.Detected(watched, 1.0);
        Console.WriteLine($"  {NameOf(watched)} at {alert.Level(watched):F2} ({AlertState.Describe(alert.Level(watched))}); " +
                          $"everywhere else at 0.00");

        var host = new DjHost(1234);
        List<DjSegment> said = Session(host, clock =>
            RadioDj.Observe(clock, new Weather(7).At(clock), alert, NameOf),
            172 * 86400, 600);

        var aircraft = said.Where(s => s.Topic == DjTopic.Aircraft).ToList();
        Console.WriteLine($"  {aircraft.Count} aircraft lines out of {said.Count} segments");

        var wrong = new List<string>();
        foreach (DjSegment s in aircraft)
        {
            if (!s.Text.Contains(NameOf(watched), StringComparison.Ordinal))
                wrong.Add($"named no region that saw anything: \"{s.Text}\"");
            foreach (string other in RegionNames)
            {
                if (other == NameOf(watched)) continue;
                if (s.Text.Contains(other, StringComparison.Ordinal))
                    wrong.Add($"named {other}, which has never seen the aircraft: \"{s.Text}\"");
            }
        }
        foreach (string x in wrong.Take(6)) Console.WriteLine($"  !! {x}");

        if (aircraft.Count > 0)
            Console.WriteLine($"  e.g. \"{aircraft[0].Text}\"");

        // And the other half of the gate: a world nobody has seen anything in must never
        // produce a named-region line at all, and should produce the quiet bank instead.
        var quietHost = new DjHost(999);
        List<DjSegment> quiet = Session(quietHost, clock =>
            RadioDj.Observe(clock, new Weather(7).At(clock), new AlertState(), NameOf),
            172 * 86400, 600);

        int namedInQuietWorld = quiet.Count(s => s.Topic == DjTopic.Aircraft);
        int quietLines = quiet.Count(s => s.Topic == DjTopic.AircraftQuiet);
        Console.WriteLine($"  nothing raised anywhere: {namedInQuietWorld} named-region lines, {quietLines} \"nobody has heard it\" lines");

        // A region name must never appear anywhere in a quiet world, whatever the topic.
        var leaked = quiet
            .Where(s => RegionNames.Any(r => s.Text.Contains(r, StringComparison.Ordinal)))
            .Select(s => s.Text).Take(4).ToList();
        foreach (string l in leaked) Console.WriteLine($"  !! region named with nothing raised: \"{l}\"");

        if (wrong.Count > 0) return $"{wrong.Count} aircraft line(s) named a region that never saw the aircraft";
        if (aircraft.Count == 0) return "a region was raised all session and he never once mentioned it";
        if (namedInQuietWorld > 0) return $"{namedInQuietWorld} named-region line(s) fired with nothing raised anywhere";
        if (leaked.Count > 0) return $"{leaked.Count} line(s) named a region in a world where nothing was seen";
        if (quietLines == 0) return "nothing was raised all session and he never said so";
        return null;
    }

    /// <summary>
    /// The band a line is written for has to match the band the region is actually in.
    ///
    /// AlertState.Describe already grades readiness five ways and the corpus is filed against
    /// the same grades, so a place that half-noticed something once cannot be described as
    /// standing to. This walks all four speakable bands and checks the ids that come out.
    /// </summary>
    public static string? MatchesTheAlertBand()
    {
        var cases = new (double Level, DjHeatBand Band)[]
        {
            (0.12, DjHeatBand.Seen),
            (0.40, DjHeatBand.Watching),
            (0.70, DjHeatBand.Expecting),
            (0.95, DjHeatBand.Waiting),
        };

        var wrong = new List<string>();
        foreach ((double level, DjHeatBand band) in cases)
        {
            if (RadioDj.BandOf(level) != band)
                wrong.Add($"BandOf({level}) is {RadioDj.BandOf(level)}, not {band}");

            var host = new DjHost(31337);
            var heat = new[] { new DjRegionHeat("Cold Shoulder", level) };
            var drawn = new List<DjSegment>();
            for (int i = 0; i < 40; i++)
            {
                DjBreak? link = host.Say(W(clock: 172 * 86400 + i * 3600, heat: heat), DjTopic.Aircraft);
                if (link is not null) drawn.AddRange(link.Segments);
            }

            string want = $"dj.Aircraft.{band.ToString().ToLowerInvariant()}.";
            int offBand = drawn.Count(s => !s.Id.StartsWith(want, StringComparison.Ordinal));
            Console.WriteLine($"  level {level:F2} -> {AlertState.Describe(level),-18} {drawn.Count} lines, {offBand} from the wrong band");
            if (drawn.Count > 0) Console.WriteLine($"      \"{drawn[0].Text}\"");
            if (offBand > 0) wrong.Add($"{offBand} line(s) at level {level:F2} came from the wrong band");
            if (drawn.Count == 0) wrong.Add($"no line at all at level {level:F2}");
        }

        // Below the floor is not speakable at all. AlertState itself treats 0.05 as noise.
        var belowHost = new DjHost(4);
        var below = new[] { new DjRegionHeat("Ashmount", 0.02) };
        int spoke = 0;
        for (int i = 0; i < 40; i++)
            if (belowHost.Say(W(heat: below), DjTopic.Aircraft) is not null) spoke++;
        Console.WriteLine($"  level 0.02 (below AlertState's own floor): {spoke} lines");
        if (spoke > 0) wrong.Add($"{spoke} line(s) about a region below the talk-about floor");

        // Three or more places at once is its own bank, and must not fire below three.
        var busyHost = new DjHost(5);
        var two = new[] { new DjRegionHeat("Fenmoor", 0.5), new DjRegionHeat("The Drowning", 0.4) };
        var four = two.Concat(new[]
        {
            new DjRegionHeat("Cold Shoulder", 0.3), new DjRegionHeat("Long Acre", 0.2),
        }).ToArray();
        int busyOnTwo = 0, busyOnFour = 0;
        for (int i = 0; i < 20; i++)
        {
            if (busyHost.Say(W(heat: two), DjTopic.AircraftBusy) is not null) busyOnTwo++;
            if (busyHost.Say(W(heat: four), DjTopic.AircraftBusy) is not null) busyOnFour++;
        }
        Console.WriteLine($"  \"it is everywhere\" bank: {busyOnTwo} lines on two regions, {busyOnFour} on four");
        if (busyOnTwo > 0) wrong.Add("the everywhere bank fired with only two regions raised");
        if (busyOnFour == 0) wrong.Add("the everywhere bank never fired with four regions raised");

        foreach (string x in wrong) Console.WriteLine($"  !! {x}");
        return wrong.Count > 0 ? $"{wrong.Count} band problem(s)" : null;
    }

    /// <summary>
    /// The rest of the world-state gating: weather, time of day, season, and the ending.
    ///
    /// Same principle as the alert gate and the same failure mode. A station that mentions
    /// the storm you are actually flying through is a character; one that mentions a storm
    /// on a clear afternoon is a bug that reads as a character flaw.
    /// </summary>
    public static string? ReactsToTheWorld()
    {
        var wrong = new List<string>();

        // --- the sky he describes is the sky that is there --------------------
        foreach (SkyCondition sky in Enum.GetValues<SkyCondition>())
        {
            var host = new DjHost(808);
            var drawn = new List<DjSegment>();
            for (int i = 0; i < 30; i++)
            {
                DjBreak? link = host.Say(W(sky: sky), DjTopic.Sky);
                if (link is not null) drawn.AddRange(link.Segments);
            }
            string want = $"dj.Sky.{sky.ToString().ToLowerInvariant()}.";
            int off = drawn.Count(s => !s.Id.StartsWith(want, StringComparison.Ordinal));
            Console.WriteLine($"  {sky,-9} {drawn.Count,3} lines, {off} from another sky");
            if (off > 0) wrong.Add($"{off} {sky} line(s) came from another sky's bank");
        }
        Console.WriteLine();

        // --- weather notes only when the weather is doing it ------------------
        (string Label, DjWorld World, DjTopic Topic, bool Expect)[] gates =
        {
            ("calm air, wind note",        W(wind: 3, gust: 1),       DjTopic.WindNote,        false),
            ("blowing, wind note",         W(wind: 12, gust: 9),      DjTopic.WindNote,        true),
            ("clear air, visibility note", W(vis: 20000),             DjTopic.VisibilityNote,  true),
            ("standard temp, temp note",   W(isa: 0),                 DjTopic.TemperatureNote, true),
            ("high base, ceiling note",    W(cloudBase: 2200),        DjTopic.CeilingNote,     true),
            ("net up, ending bank",        W(searchComplete: true),   DjTopic.NetUp,           true),
            ("net down, ending bank",      W(searchComplete: false),  DjTopic.NetUp,           false),
        };

        // Say() bypasses the weighting and asks directly, so it tests the hard gates. The
        // soft gates - the ones that only change how often he raises a subject - are checked
        // through the composer below.
        foreach ((string label, DjWorld world, DjTopic topic, bool expect) in gates)
        {
            var host = new DjHost(99);
            bool got = host.Say(world, topic) is not null;
            Console.WriteLine($"  {label,-28} {(got ? "speaks" : "silent")}");
            if (topic == DjTopic.NetUp && got != expect)
                wrong.Add($"{label}: expected {(expect ? "a line" : "silence")}");
        }
        Console.WriteLine();

        // The composer's soft gates: a calm, clear, temperate world must never RAISE wind,
        // visibility or ceiling as a subject.
        var calmHost = new DjHost(2024);
        List<DjSegment> calm = Session(calmHost, _ => W(wind: 3, gust: 1, vis: 20000, cloudBase: 2200, isa: 0),
                                       172 * 86400, 500);
        int wind = calm.Count(s => s.Topic == DjTopic.WindNote);
        int vis = calm.Count(s => s.Topic == DjTopic.VisibilityNote);
        int ceil = calm.Count(s => s.Topic == DjTopic.CeilingNote);
        int temp = calm.Count(s => s.Topic == DjTopic.TemperatureNote);
        Console.WriteLine($"  calm clear temperate day: wind {wind}, visibility {vis}, ceiling {ceil}, temperature {temp} (all should be 0)");
        if (wind + vis + ceil + temp > 0)
            wrong.Add("he raised a weather subject the weather was not doing");

        var foulHost = new DjHost(2025);
        List<DjSegment> foul = Session(foulHost, _ => W(sky: SkyCondition.Storm, wind: 16, gust: 12,
                                                        vis: 1200, cloudBase: 180, isa: -9),
                                       172 * 86400, 500);
        Console.WriteLine($"  storm, gale, murk, low base, cold: wind {foul.Count(s => s.Topic == DjTopic.WindNote)}, " +
                          $"visibility {foul.Count(s => s.Topic == DjTopic.VisibilityNote)}, " +
                          $"ceiling {foul.Count(s => s.Topic == DjTopic.CeilingNote)}, " +
                          $"temperature {foul.Count(s => s.Topic == DjTopic.TemperatureNote)}");
        if (foul.Count(s => s.Topic == DjTopic.WindNote) == 0) wrong.Add("a gale all session and he never mentioned the wind");
        if (foul.Count(s => s.Topic == DjTopic.VisibilityNote) == 0) wrong.Add("murk all session and he never mentioned it");

        // Cold and warm come out of different banks.
        var coldHost = new DjHost(11);
        var warmHost = new DjHost(11);
        string? coldId = coldHost.Say(W(isa: -9), DjTopic.TemperatureNote)?.Segments[0].Id;
        string? warmId = warmHost.Say(W(isa: 9), DjTopic.TemperatureNote)?.Segments[0].Id;
        Console.WriteLine($"  ISA-9 -> {coldId}   ISA+9 -> {warmId}");
        if (coldId is null || !coldId.StartsWith("dj.TemperatureNote.cold.", StringComparison.Ordinal))
            wrong.Add("a cold day did not draw from the cold bank");
        if (warmId is null || !warmId.StartsWith("dj.TemperatureNote.warm.", StringComparison.Ordinal))
            wrong.Add("a warm day did not draw from the warm bank");

        // --- the season he names is the season the sun is in ------------------
        (int Day, DjSeason Season)[] seasons = { (20, DjSeason.Winter), (100, DjSeason.Spring), (200, DjSeason.Summer), (300, DjSeason.Autumn) };
        foreach ((int day, DjSeason season) in seasons)
        {
            if (RadioDj.SeasonOf(day * 86400.0) != season)
                wrong.Add($"day {day} reads as {RadioDj.SeasonOf(day * 86400.0)}, not {season}");
            var host = new DjHost(77);
            string? id = host.Say(W(clock: day * 86400.0 + 12 * 3600), DjTopic.Season)?.Segments[0].Id;
            string want = $"dj.Season.{season.ToString().ToLowerInvariant()}.";
            Console.WriteLine($"  day {day,3} -> {season,-6} {id}");
            if (id is null || !id.StartsWith(want, StringComparison.Ordinal))
                wrong.Add($"day {day} drew a line from the wrong season");
        }

        // --- the time of day -------------------------------------------------
        (double Hour, DjDaypart Part)[] hours = { (2, DjDaypart.Night), (6, DjDaypart.Dawn), (9, DjDaypart.Morning), (13, DjDaypart.Midday), (16, DjDaypart.Afternoon), (20, DjDaypart.Evening) };
        foreach ((double hour, DjDaypart part) in hours)
        {
            if (RadioDj.DaypartOf(hour) != part)
                wrong.Add($"{hour:F0}:00 reads as {RadioDj.DaypartOf(hour)}, not {part}");
        }
        Console.WriteLine($"  dayparts map correctly across {hours.Length} sample hours");

        foreach (string x in wrong) Console.WriteLine($"  !! {x}");
        return wrong.Count > 0 ? $"{wrong.Count} world-state gating problem(s)" : null;
    }

    // =====================================================================================
    //  3 and 4. The two rules that are premise rather than preference
    // =====================================================================================

    /// <summary>
    /// D-008 and D-005a, over the corpus and over a session's worth of composed output.
    ///
    /// Both are scanned twice on purpose. The corpus scan catches what was written; the
    /// composed scan catches what substitution could produce, because a region name dropped
    /// into a sentence is the one way a clean line can become a dirty one after review.
    ///
    /// The blocklists are checked against their own probes first. A blocklist nobody has ever
    /// seen catch anything is a comment, and D-006 already paid for that lesson once.
    /// </summary>
    public static string? PremiseAndAdviceGuard()
    {
        var problems = new List<string>();

        // --- the blocklists have to work -------------------------------------
        (string[] mustTrip, string[] mustPass) = DjAudit.Probes();
        int caught = mustTrip.Count(DjAudit.Trips);
        var falsePositives = mustPass.Where(DjAudit.Trips).ToList();
        Console.WriteLine($"  blocklist self-check: caught {caught} of {mustTrip.Length} known-bad phrasings");
        Console.WriteLine($"  false-positive check: {mustPass.Length - falsePositives.Count} of {mustPass.Length} legitimate lines pass");
        foreach (string f in falsePositives) Console.WriteLine($"  !! rejects legitimate text: \"{f}\"");
        if (caught < mustTrip.Length) problems.Add($"the blocklists missed {mustTrip.Length - caught} known-bad phrasings");
        if (falsePositives.Count > 0) problems.Add($"the blocklists reject legitimate text: {falsePositives[0]}");

        // --- the corpus --------------------------------------------------------
        var all = DjCorpus.All.ToList();
        List<DjAudit.Finding> findings = DjAudit.Scan(all);
        Console.WriteLine($"  {all.Count} authored lines scanned against " +
                          $"{DjAudit.RivalFlyer.Length} D-008 phrasings, " +
                          $"{DjAudit.Instructional.Length} D-005a phrasings and " +
                          $"{DjAudit.PlayerWink.Length} player-wink phrasings");
        foreach (DjAudit.Finding f in findings.Take(12)) Console.WriteLine($"  !! {f}");
        if (findings.Count > 0) problems.Add($"{findings.Count} authored line(s) break a rule");

        // --- what actually goes out, tokens filled -----------------------------
        var alert = new AlertState();
        for (int i = 0; i < 200; i++) { alert.Detected(2, 1.0); alert.Detected(6, 1.0); }
        alert.Engaged(4);

        var host = new DjHost(606);
        List<DjSegment> said = Session(host, clock =>
            RadioDj.Observe(clock, new Weather(3).At(clock), alert, NameOf,
                            searchComplete: true, trackTitle: "Bad Ground"),
            172 * 86400, 900);

        var composed = said.Select(s => new DjLine(s.Id, s.Topic, s.Text)).ToList();
        List<DjAudit.Finding> live = DjAudit.Scan(composed);
        Console.WriteLine($"  {said.Count} composed segments scanned; {live.Count} findings");
        foreach (DjAudit.Finding f in live.Take(12)) Console.WriteLine($"  !! {f}");
        if (live.Count > 0) problems.Add($"{live.Count} composed segment(s) break a rule");

        // --- nothing reaches the strip half-finished ---------------------------
        var unresolved = said.Where(s => s.Text.Contains('{') || s.Text.Contains('}')).ToList();
        var empty = said.Where(s => string.IsNullOrWhiteSpace(s.Text)).ToList();
        var untimed = said.Where(s => s.Seconds <= 0).ToList();
        foreach (DjSegment s in unresolved.Take(4)) Console.WriteLine($"  !! unresolved token: \"{s.Text}\"");
        Console.WriteLine($"  tokens: {unresolved.Count} unresolved, {empty.Count} empty segments, {untimed.Count} untimed");
        if (unresolved.Count > 0) problems.Add($"{unresolved.Count} segment(s) reached the strip with a token in them");
        if (empty.Count > 0) problems.Add($"{empty.Count} empty segment(s)");
        if (untimed.Count > 0) problems.Add($"{untimed.Count} segment(s) with no reading time");

        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return problems.Count > 0 ? problems[0] : null;
    }

    // =====================================================================================
    //  The scheduling, and the place he is transmitting from
    // =====================================================================================

    /// <summary>
    /// How often he talks: the floor, the ceiling, and the fact that he goes to bed.
    ///
    /// The overnight is the cheapest characterisation in the whole system - a station that
    /// runs unattended between sign-off and sign-on, with recorded idents and nothing that
    /// reacts to today - and it is worth a measurement because it is invisible in the corpus
    /// and obvious in a link rate.
    /// </summary>
    public static string? Scheduling()
    {
        var problems = new List<string>();
        var host = new DjHost(5150);
        var weather = new Weather(11);

        double start = 172 * 86400 + 5 * 3600;      // from just before sign-on
        double clock = start;
        var times = new List<double>();
        var dayLinks = 0; var dayBoundaries = 0;
        var nightLinks = 0; var nightBoundaries = 0;
        var topics = new List<(double Hour, DjTopic Topic)>();

        for (int i = 0; i < 700; i++)
        {
            double hour = (clock / 3600.0) % 24.0;
            bool night = DjHost.IsOvernight(hour);
            if (night) nightBoundaries++; else dayBoundaries++;

            DjBreak? link = host.OnTrackBoundary(
                RadioDj.Observe(clock, weather.At(clock), null, null));

            if (link is not null)
            {
                times.Add(clock);
                if (night) nightLinks++; else dayLinks++;
                foreach (DjSegment s in link.Segments) topics.Add((hour, s.Topic));
            }
            clock += Radio.AssumedTrackSeconds;
        }

        double dayRate = dayBoundaries > 0 ? dayLinks / (double)dayBoundaries : 0;
        double nightRate = nightBoundaries > 0 ? nightLinks / (double)nightBoundaries : 0;
        Console.WriteLine($"  {times.Count} links over {(clock - start) / 3600:F1} hours");
        Console.WriteLine($"  daytime  {dayLinks}/{dayBoundaries} boundaries = {dayRate:P0}");
        Console.WriteLine($"  overnight {nightLinks}/{nightBoundaries} boundaries = {nightRate:P0}");

        if (nightRate >= dayRate) problems.Add("he is no quieter overnight than he is at two in the afternoon");

        // The ceiling: never twice inside the minimum gap.
        double worstGap = double.PositiveInfinity;
        double longestSilence = 0;
        for (int i = 1; i < times.Count; i++)
        {
            double gap = times[i] - times[i - 1];
            worstGap = Math.Min(worstGap, gap);
            longestSilence = Math.Max(longestSilence, gap);
        }
        Console.WriteLine($"  shortest gap between links {worstGap:F0} s (floor {host.MinGapSeconds:F0} s)");
        Console.WriteLine($"  longest silence {longestSilence / 60:F1} min");
        if (worstGap < host.MinGapSeconds - 1e-6) problems.Add($"two links only {worstGap:F0} s apart");

        // The floor, in his waking hours: a listener never goes long without hearing anybody.
        // Overnight is allowed to be as quiet as it likes, which is the point of overnight.
        double wakingSilence = 0;
        for (int i = 1; i < times.Count; i++)
        {
            double h = (times[i] / 3600.0) % 24.0;
            if (DjHost.IsOvernight(h)) continue;
            wakingSilence = Math.Max(wakingSilence, times[i] - times[i - 1]);
        }
        double allowed = host.MaxSilentSeconds + Radio.AssumedTrackSeconds + 1;
        Console.WriteLine($"  longest waking silence {wakingSilence / 60:F1} min (floor allows {allowed / 60:F1})");
        if (wakingSilence > allowed) problems.Add($"a waking listener went {wakingSilence / 60:F1} min without hearing him");

        // Overnight is recorded: nothing that reacts to today may go out while he is asleep.
        var awakeOnly = new[]
        {
            DjTopic.Aircraft, DjTopic.AircraftBusy, DjTopic.Correction, DjTopic.Market,
            DjTopic.LostProperty, DjTopic.Fence, DjTopic.Rota, DjTopic.SignOn, DjTopic.SignOff,
        };
        var leaked = topics.Where(t => DjHost.IsOvernight(t.Hour) && awakeOnly.Contains(t.Topic)).ToList();
        foreach ((double h, DjTopic t) in leaked.Take(4))
            Console.WriteLine($"  !! {t} went out at {h:F1} with nobody at the desk");
        Console.WriteLine($"  overnight topics: {string.Join(", ", topics.Where(t => DjHost.IsOvernight(t.Hour)).Select(t => t.Topic).Distinct())}");
        if (leaked.Count > 0) problems.Add($"{leaked.Count} live topic(s) went out on the unattended overnight");

        // The fixtures.
        var signOn = new DjHost(1);
        var signOff = new DjHost(2);
        bool openedOk = signOn.Say(W(clock: 172 * 86400 + (int)(RadioDj.SignOnHour * 3600)), DjTopic.SignOn) is not null;
        bool closedOk = signOff.Say(W(clock: 172 * 86400 + (int)(RadioDj.SignOffHour * 3600)), DjTopic.SignOff) is not null;
        Console.WriteLine($"  sign-on at {RadioDj.SignOnHour:F2} {(openedOk ? "speaks" : "silent")}; " +
                          $"sign-off at {RadioDj.SignOffHour:F2} {(closedOk ? "speaks" : "silent")}");
        if (!openedOk || !closedOk) problems.Add("a fixture had nothing to say");

        // The 06:40 weather sequence is story.md's, not his. He knows it is coming; this file
        // must never schedule it.
        bool dueAt0640 = DjHost.WeatherSequenceDue(172 * 86400 + (int)(6.65 * 3600));
        bool dueAtNoon = DjHost.WeatherSequenceDue(172 * 86400 + 12 * 3600);
        Console.WriteLine($"  weather sequence window: 06:39 {dueAt0640}, 12:00 {dueAtNoon}");
        if (!dueAt0640 || dueAtNoon) problems.Add("the 06:40 window is wrong");

        // Reading pace has to be sane, or the strip either flashes past or blocks the hour.
        double slowest = DjCorpus.All.Max(l => RadioDj.ReadSeconds(l.Text));
        double fastest = DjCorpus.All.Min(l => RadioDj.ReadSeconds(l.Text));
        Console.WriteLine($"  segment length {fastest:F1} s to {slowest:F1} s at {RadioDj.WordsPerSecond * 60:F0} words a minute");
        if (slowest > 35) problems.Add($"a single line takes {slowest:F0} s to read");

        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return problems.Count > 0 ? problems[0] : null;
    }

    /// <summary>
    /// Where he transmits from, and that it is a rule rather than a coordinate.
    ///
    /// The rule has to survive worldgen moving, it must not collide with a relay
    /// StoryPlaces has already claimed, and the transmitter has to be strong enough to be
    /// heard across the world from three hundred metres and too weak to be heard across it
    /// from the deck - because that trade against D-010's fly-low habit is the whole reason
    /// the station is interesting from the cockpit rather than just from the fiction.
    /// </summary>
    public static string? BroadcastSite()
    {
        var problems = new List<string>();

        Console.WriteLine($"  {RadioDj.HostName}, {RadioDj.StationName}");
        Console.WriteLine($"  site rule: {RadioDj.SiteRuleText}");

        DjSiteRule intent = RadioDj.SiteRules[0];
        if (intent.Region != DialogueCorpus.RegionTag.Upland || intent.Kind != SiteKindTag.Relay)
            problems.Add($"the intent rule is {intent.Text}, not the upland relay");
        if (RadioDj.SiteRules.Length < 3)
            problems.Add("no fallback chain: site placement is rejection sampling and fails silently");

        // Long Acre (Farmland) and Sawtooth Works (Industrial) relays are StoryPlaces' -
        // LongAcreMast and SawtoothRelay. Two roles on one mast is a collision waiting for
        // whoever builds them.
        var claimed = new[] { DialogueCorpus.RegionTag.Farmland, DialogueCorpus.RegionTag.Industrial };
        var collisions = RadioDj.SiteRules
            .Where(r => r.Kind == SiteKindTag.Relay && claimed.Contains(r.Region))
            .Select(r => r.Text).ToList();
        foreach (string c in collisions) Console.WriteLine($"  !! collides with a StoryPlaces relay: {c}");
        if (collisions.Count > 0) problems.Add($"{collisions.Count} rule(s) target a relay the story already owns");

        // No rule may carry a coordinate. The type makes that structurally impossible, which
        // is the point of it being a record of tags rather than a lambda - assert the shape.
        Console.WriteLine($"  {RadioDj.SiteRules.Length} rules, all expressed as region + kind + ordering:");
        foreach (DjSiteRule r in RadioDj.SiteRules) Console.WriteLine($"      {r.Text}");

        // The reception geometry, measured against the map rather than asserted.
        // Cold Shoulder sits at (1500, -4400); the far corner of the content envelope is
        // The Scald's side of the world.
        RadioStation station = RadioDj.Station(siteId: 412, x: 1500, y: -4400);
        double farCorner = Math.Sqrt(Math.Pow(1500 - -4000, 2) + Math.Pow(-4400 - 4300, 2));
        double atAltitude = Radio.Quality(farCorner, 300, station, 0, 0);
        double onTheDeck = Radio.Quality(farCorner, 30, station, 0, 0);
        double inAStorm = Radio.Quality(farCorner, 300, station, 1.0, 1.0);

        Console.WriteLine();
        Console.WriteLine($"  mast {station.MastHeightM:F0} m, nominal {station.PowerKm:F1} km, id {station.Id}");
        Console.WriteLine($"  at the far side of the world ({farCorner / 1000:F1} km):");
        Console.WriteLine($"      at 300 m   quality {atAltitude:F2}");
        Console.WriteLine($"      on the deck quality {onTheDeck:F2}");
        Console.WriteLine($"      at 300 m in a storm  quality {inAStorm:F2}");

        if (station.Id != Radio.FrequencyKnowledgeId(412))
            problems.Add("the station id does not match the frequency knowledge id, so tuning his mast will not find him");
        if (atAltitude < 0.99)
            problems.Add($"he does not reach the far side of the world at 300 m (quality {atAltitude:F2})");
        if (onTheDeck >= atAltitude - 0.2)
            problems.Add("flying low costs nothing, so the station never trades against D-010's fly-low habit");
        if (inAStorm >= atAltitude)
            problems.Add("weather does not touch the signal");

        // He is on the four-reader rota and is not the one who says knots (story.md beat 5).
        Console.WriteLine();
        Console.WriteLine($"  rota slot {RadioDj.RotaSlot} of {RadioDj.RotaSize}; " +
                          $"his turn on days: {string.Join(", ", Enumerable.Range(0, 8).Where(d => DjHost.HisTurnOnRota(d * 86400.0)))}");
        int turns = Enumerable.Range(0, 40).Count(d => DjHost.HisTurnOnRota(d * 86400.0));
        if (turns != 10) problems.Add($"he reads {turns} of 40 mornings, not a clean quarter");

        foreach (string p in problems) Console.WriteLine($"  !! {p}");
        return problems.Count > 0 ? problems[0] : null;
    }

    /// <summary>
    /// A transcript. No pass condition beyond composing at all - its job is to be read.
    ///
    /// Every voice-carrying system in this project has needed somebody to look at its actual
    /// output rather than at its counters, and a register is the one property no assertion
    /// can check. This prints an evening of the station with a region genuinely raised, so
    /// the thing a reviewer has to judge - does this sound like a man, or like a corpus - is
    /// in front of them.
    /// </summary>
    public static string? Transcript()
    {
        var alert = new AlertState();
        for (int i = 0; i < 150; i++) alert.Detected(4, 1.0);   // Cold Shoulder
        for (int i = 0; i < 40; i++) alert.Detected(3, 1.0);    // The Drowning

        var weather = new Weather(20260919);
        var host = new DjHost(20260919);
        double clock = 172 * 86400 + 13 * 3600;

        int printed = 0;
        for (int i = 0; i < 240 && printed < 14; i++)
        {
            Weather.Conditions wx = weather.At(clock);
            DjBreak? link = host.OnTrackBoundary(
                RadioDj.Observe(clock, wx, alert, NameOf, trackTitle: "Cold Water"));

            if (link is not null && link.Segments.Count > 0)
            {
                double hour = (clock / 3600.0) % 24.0;
                Console.WriteLine();
                Console.WriteLine($"  [{(int)hour:00}:{(int)((hour % 1) * 60):00}] {wx.Sky}, {link.Seconds:F0} s");
                foreach (DjSegment s in link.Segments)
                    Console.WriteLine($"      {s.Text}");
                printed++;
            }
            clock += Radio.AssumedTrackSeconds;
        }

        Console.WriteLine();
        return printed == 0 ? "he never said anything in four hours of station time" : null;
    }
}
