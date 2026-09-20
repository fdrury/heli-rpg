using Rotorwash.Sim;

namespace Rotorwash.SimLab;

public static class DialogueTests
{
    static TalkContext Ctx(double fuel = 0.8, double condition = 1.0, int meetings = 0,
                           double hoursSince = double.PositiveInfinity, double standing = 0.2,
                           double now = 3600, int parts = 0, double carried = 0,
                           bool medical = false, string[]? knows = null)
    {
        var c = new TalkContext
        {
            CarriedMedical = medical,
            Now = now,
            SiteName = "Iron Furrow",
            RegionName = "The Pan",
            FuelFraction = fuel,
            WorstComponentHealth = condition,
            WorstComponentName = "tail rotor",
            PreviousMeetings = meetings,
            HoursSinceLastMeeting = hoursSince,
            Standing = standing,
            CarriedParts = parts,
            CarriedMass = carried,
        };
        if (knows is not null) foreach (string k in knows) c.KnownIds.Add(k);
        return c;
    }

    /// <summary>Every named character, in the order story.md introduces them.</summary>
    static IEnumerable<(string Id, NpcMind Npc, DialogueBank Bank)> Cast()
    {
        foreach (string id in DialogueCorpus.NamedIds)
        {
            var pair = DialogueCorpus.Named(id);
            if (pair is null) throw new Exception($"{id} is in NamedIds and has no bank");
            yield return (id, pair.Value.Npc, pair.Value.Bank);
        }
    }

    static int Count(DialogueBank b, string tag) => b.Lines.Count(l => l.Tags.Contains(tag));

    public static string? Selection()
    {
        var bank = DialogueCorpus.MattieLines();
        Console.WriteLine("  the same character, different states - the line should change");
        Console.WriteLine();

        var cases = new (string label, TalkContext ctx)[]
        {
            ("first visit, healthy",        Ctx()),
            ("first visit, wrecked",        Ctx(condition: 0.3)),
            ("returning, nothing wrong",    Ctx(meetings: 4, hoursSince: 40)),
            ("returning after an hour",     Ctx(meetings: 4, hoursSince: 3)),
            ("returning after weeks",       Ctx(meetings: 4, hoursSince: 400)),
            ("arrived on fumes",            Ctx(fuel: 0.05, meetings: 2, hoursSince: 40)),
            ("arrived beaten up",           Ctx(condition: 0.25, meetings: 2, hoursSince: 40)),
        };

        var seen = new HashSet<string>();
        double clock = 0;
        foreach (var (label, ctx) in cases)
        {
            clock += 86400 * 3;            // days apart, so recency never interferes
            var c = ctx; c.Now = clock;
            DialogueLine? line = bank.Select(c, "greeting", clock);
            if (line is null) { Console.WriteLine($"  {label,-28} (nothing matched)"); continue; }
            seen.Add(line.Id);
            Console.WriteLine($"  {label,-28} \"{line.Text}\"");
            Console.WriteLine($"  {"",-28} [{line.Id}, {line.Requires.Count} conditions, " +
                              $"{line.DeliverySeconds:F1} s to say]");
        }

        Console.WriteLine();
        Console.WriteLine($"  {seen.Count} distinct lines across {cases.Length} states");

        if (seen.Count < 6) return $"only {seen.Count} distinct lines for {cases.Length} different states";

        // The specific must beat the generic: a damaged first visit picks the damaged line.
        // Fresh bank, so the recency penalty from the table above cannot interfere.
        var fresh = DialogueCorpus.MattieLines();
        var dmg = fresh.Select(Ctx(condition: 0.3, now: 99999), "greeting", 99999);
        if (dmg?.Id != "mattie.first.damaged")
            return $"a damaged first arrival chose {dmg?.Id} instead of the line written for it";
        return null;
    }

    public static string? Repetition()
    {
        var bank = DialogueCorpus.MattieLines();
        // Same state, over and over. It must not say the same thing twice running.
        var c = Ctx(meetings: 5, hoursSince: 40);
        var picked = new List<string>();
        double clock = 100000;

        for (int i = 0; i < 6; i++)
        {
            clock += 1800;             // half an hour apart
            c.Now = clock;
            DialogueLine? l = bank.Select(c, "greeting", clock);
            picked.Add(l?.Id ?? "-");
        }
        Console.WriteLine("  six arrivals in the same state, half an hour apart:");
        foreach (string id in picked) Console.WriteLine($"    {id}");

        int immediateRepeats = 0;
        for (int i = 1; i < picked.Count; i++) if (picked[i] == picked[i - 1]) immediateRepeats++;

        int distinct = picked.Distinct().Count();
        Console.WriteLine($"  immediate repeats: {immediateRepeats}, distinct lines: {distinct} of {picked.Count}");
        if (immediateRepeats > 0) return $"repeated itself {immediateRepeats} times in a row";
        if (distinct < 5) return $"only {distinct} distinct openings across six identical arrivals";
        return null;
    }

    /// <summary>
    /// The long-session repetition guard, and the reason it is separate from Repetition().
    ///
    /// The six-arrival test above spaces visits half an hour apart, which is exactly the
    /// case the selector's world-time recency penalty was built for - and it therefore
    /// cannot see the failure that actually matters. Real visits are DAYS apart. Once the
    /// penalty has decayed, a purely score-based selector picks the first legal line in
    /// list order every single time, so a bank of thirty greetings says one of them and
    /// buries twenty-nine. Adding depth to the corpus would then do nothing at all, which
    /// is the opposite of what this pass is for.
    ///
    /// So: sixty visits, days apart, states varying the way a real play session varies,
    /// measured per tag. The bar is that DEPTH CONVERTS INTO VARIETY - distinct lines
    /// heard has to scale with the size of the pool, not with the size of the test.
    /// </summary>
    public static string? LongSession()
    {
        const int Visits = 60;
        Console.WriteLine($"  {Visits} visits per character, 30+ hours apart, mixed states.");
        Console.WriteLine("  \"within 5\" = heard a line you had heard in the previous five of that tag.");
        Console.WriteLine();
        Console.WriteLine($"  {"character",-10} {"tag",-9} {"pool",5} {"heard",6} {"immed",6} " +
                          $"{"within5",8} {"top line",9} {"score only",10}");

        string? failure = null;
        var totals = new Dictionary<string, (int heard, int imm, int w5, int n)>();

        foreach (var (id, _, _) in Cast())
        {
            foreach (string tag in new[] { "greeting", "talk", "parting" })
            {
                // Fresh bank per tag so each column is measured from a clean corpus.
                DialogueBank bank = DialogueCorpus.Named(id)!.Value.Bank;
                int pool = Count(bank, tag);

                var picked = new List<string>();
                double clock = 500000;

                for (int v = 0; v < Visits; v++)
                {
                    // Days apart: the world-clock recency penalty is fully decayed and
                    // contributes nothing. Rotation has to come from somewhere else.
                    clock += 30 * 3600 + (v % 7) * 3600;

                    // A plausible spread of arrivals rather than one frozen state.
                    double fuel      = (v % 5) switch { 0 => 0.07, 1 => 0.22, _ => 0.6 + (v % 3) * 0.12 };
                    double condition = (v % 7) switch { 0 => 0.3, 3 => 0.55, _ => 0.9 };
                    double standing  = 0.1 + (v / 20) * 0.25;
                    // The player learns things as they play, which is the whole point of
                    // the gated layer: what is legal to say changes under the selector as
                    // the thread advances, and that is a second source of variety.
                    string[] known = DialogueCorpus.Knows.All.Take(v / 5).ToArray();

                    var c = Ctx(fuel: fuel, condition: condition, meetings: 1 + v,
                                hoursSince: 30 + (v % 7), standing: standing, now: clock,
                                medical: v % 4 == 0, knows: known);
                    DialogueLine? l = bank.Select(c, tag, clock);
                    picked.Add(l?.Id ?? "(nothing matched)");
                }

                int imm = 0;
                for (int i = 1; i < picked.Count; i++) if (picked[i] == picked[i - 1]) imm++;

                int within5 = 0;
                for (int i = 1; i < picked.Count; i++)
                {
                    for (int j = Math.Max(0, i - 5); j < i; j++)
                        if (picked[j] == picked[i]) { within5++; break; }
                }

                var uses = picked.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                int top = uses.Values.Max();
                int heard = uses.Count;
                int baseline = BaselineDistinct(id, tag, Visits);

                Console.WriteLine($"  {id,-10} {tag,-9} {pool,5} {heard,6} {imm,6} {within5,8} {top,9} {baseline,9}");

                (int heard, int imm, int w5, int n) t =
                    totals.TryGetValue(tag, out var x) ? x : (0, 0, 0, 0);
                totals[tag] = (t.heard + heard, t.imm + imm, t.w5 + within5, t.n + 1);

                if (picked.Contains("(nothing matched)"))
                    failure ??= $"{id} had no legal {tag} line in a plausible arrival state";
                if (imm > 0)
                    failure ??= $"{id} repeated a {tag} line back to back {imm} times";

                // Depth has to pay. A pool of N must yield most of N over sixty visits.
                int wantHeard = Math.Min(pool, tag == "parting" ? 8 : tag == "talk" ? 9 : 15);
                if (heard < wantHeard)
                    failure ??= $"{id} used only {heard} of {pool} {tag} lines over {Visits} visits";

                // And no single line may dominate the character's voice.
                if (top > Visits * 0.30)
                    failure ??= $"{id} said one {tag} line {top} times in {Visits} visits";

                // The point of the whole exercise: writing more lines has to be worth
                // doing. If the selector cannot reach them, the corpus is decoration.
                if (heard < baseline * 2)
                    failure ??= $"{id}'s {tag} pool yielded {heard} lines where the score-only " +
                                $"rule yields {baseline} - depth is not converting into variety";
            }
        }

        Console.WriteLine();
        var ceilings = new Dictionary<string, double> { ["greeting"] = 0.50, ["talk"] = 0.55, ["parting"] = 0.30 };
        foreach (string tag in new[] { "greeting", "talk", "parting" })
        {
            var (heard, imm, w5, n) = totals[tag];
            double rate = w5 / (double)(n * (Visits - 1));
            Console.WriteLine($"  {tag,-9} mean distinct heard {heard / (double)n,5:F1}   " +
                              $"immediate repeats {imm}   " +
                              $"within-5 rate {rate,5:P1}  (ceiling {ceilings[tag],4:P0})");
            if (rate > ceilings[tag])
                failure ??= $"{tag} lines repeat within five {rate:P1} of the time, over the {ceilings[tag]:P0} ceiling";
        }

        return failure;
    }

    /// <summary>
    /// What the selector's scoring rule ALONE would reach over the same session: most
    /// specific match wins, world-clock recency penalty applied, first line in list order
    /// breaks ties. That is the rule as it stood before this pass, and it is the number
    /// the corpus has to beat - a bank that the selector cannot reach is a bank that was
    /// not worth writing.
    /// </summary>
    static int BaselineDistinct(string npcId, string tag, int visits)
    {
        DialogueBank bank = DialogueCorpus.Named(npcId)!.Value.Bank;
        var lastUsed = new Dictionary<string, double>();
        var seen = new HashSet<string>();
        double clock = 500000;

        for (int v = 0; v < visits; v++)
        {
            clock += 30 * 3600 + (v % 7) * 3600;
            double fuel      = (v % 5) switch { 0 => 0.07, 1 => 0.22, _ => 0.6 + (v % 3) * 0.12 };
            double condition = (v % 7) switch { 0 => 0.3, 3 => 0.55, _ => 0.9 };
            double standing  = 0.1 + (v / 20) * 0.25;
            var c = Ctx(fuel: fuel, condition: condition, meetings: 1 + v,
                        hoursSince: 30 + (v % 7), standing: standing, now: clock,
                        medical: v % 4 == 0, knows: DialogueCorpus.Knows.All.Take(v / 5).ToArray());

            DialogueLine? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (DialogueLine line in bank.Lines)
            {
                if (!line.Tags.Contains(tag) || !line.Matches(c)) continue;
                double score = line.Requires.Count * 10.0 + line.Weight;
                if (lastUsed.TryGetValue(line.Id, out double when))
                    score -= Math.Max(0, 26.0 - Math.Max(0, (clock - when) / 3600.0) * 2.0);
                if (score > bestScore) { bestScore = score; best = line; }
            }
            if (best is null) continue;
            lastUsed[best.Id] = clock;
            seen.Add(best.Id);
        }
        return seen.Count;
    }

    /// <summary>
    /// The generic register, which most of the world is made of.
    ///
    /// Everywhere without a named person still has to sound like somewhere. An Ashfield
    /// fuel cache and a Basin farmstead share the common bank and nothing else, so the
    /// test is simply that two such places diverge quickly and that neither runs out of
    /// things to say.
    /// </summary>
    public static string? GenericRegister()
    {
        var places = new (string label, DialogueCorpus.RegionTag region, SiteKindTag kind)[]
        {
            ("Basin farmstead",      DialogueCorpus.RegionTag.Basin,      SiteKindTag.Farmstead),
            ("Ashfield fuel cache",  DialogueCorpus.RegionTag.Ashfield,   SiteKindTag.FuelCache),
            ("Wetland settlement",   DialogueCorpus.RegionTag.Wetland,    SiteKindTag.Settlement),
            ("Industrial workshop",  DialogueCorpus.RegionTag.Industrial, SiteKindTag.Workshop),
            ("Upland relay",         DialogueCorpus.RegionTag.Upland,     SiteKindTag.Relay),
            ("City settlement",      DialogueCorpus.RegionTag.City,       SiteKindTag.Settlement),
            ("Exurb airfield",       DialogueCorpus.RegionTag.Exurb,      SiteKindTag.Airfield),
            ("Farmland farmstead",   DialogueCorpus.RegionTag.Farmland,   SiteKindTag.Farmstead),
        };

        Console.WriteLine("  eight unnamed places, six exchanges each, same pilot and same state");
        Console.WriteLine();

        var heardAt = new Dictionary<string, HashSet<string>>();
        string? failure = null;

        foreach (var (label, region, kind) in places)
        {
            DialogueBank bank = DialogueCorpus.SettlerLines(region, kind);
            var said = new HashSet<string>();
            double clock = 900000;
            Console.WriteLine($"  {label}  ({bank.Lines.Count} lines)");
            for (int i = 0; i < 6; i++)
            {
                clock += 26 * 3600;
                var c = Ctx(meetings: 3 + i, hoursSince: 26, now: clock);
                DialogueLine? g = bank.Select(c, i % 2 == 0 ? "greeting" : "talk", clock);
                if (g is null) { failure ??= $"{label} had nothing to say"; continue; }
                said.Add(g.Id);
                Console.WriteLine($"      \"{g.Text}\"");
            }
            heardAt[label] = said;
            Console.WriteLine();
        }

        // Two places in different regions must not be reciting the same six lines.
        var basin = heardAt["Basin farmstead"];
        var ash = heardAt["Ashfield fuel cache"];
        int shared = basin.Intersect(ash).Count();
        Console.WriteLine($"  a Basin farmstead and an Ashfield fuel cache shared {shared} of " +
                          $"{basin.Count} lines across six exchanges");

        var regionalOnly = new HashSet<string>();
        foreach (var (label, region, kind) in places)
        {
            DialogueBank b = DialogueCorpus.SettlerLines(region, kind);
            foreach (DialogueLine l in b.Lines)
                if (l.Id.StartsWith("reg.") || l.Id.StartsWith("kind.")) regionalOnly.Add($"{label}|{l.Id}");
        }
        Console.WriteLine($"  {regionalOnly.Count} region- and kind-specific lines across the eight");

        if (failure is not null) return failure;
        if (shared > 3) return $"two very different places shared {shared} of six lines";

        foreach (var (label, region, kind) in places)
        {
            DialogueBank b = DialogueCorpus.SettlerLines(region, kind);
            if (Count(b, "greeting") < 14) return $"{label} has only {Count(b, "greeting")} greetings";
            if (Count(b, "talk") < 9) return $"{label} has only {Count(b, "talk")} things to talk about";
            if (Count(b, "parting") < 5) return $"{label} has only {Count(b, "parting")} ways to say goodbye";
        }
        return null;
    }

    public static string? CodaGating()
    {
        var policy = new CodaPolicy { ModelAvailable = true, MeasuredP95 = 0.55 };
        Console.WriteLine("  a coda is only requested when the baked line covers the latency");
        Console.WriteLine($"  measured p95 {policy.MeasuredP95:F2} s, cover factor {policy.CoverFactor:F1}x " +
                          $"-> need {policy.MeasuredP95 * policy.CoverFactor:F2} s of line");
        Console.WriteLine();

        var bank = DialogueCorpus.MattieLines();
        var ctx = Ctx(fuel: 0.08, condition: 0.4, meetings: 3, hoursSince: 30);

        Console.WriteLine($"   {"line",-46} {"says in",8}  decision");
        foreach (DialogueLine line in bank.Lines)
        {
            if (!line.Tags.Contains("greeting")) continue;
            CodaDecision d = policy.ShouldRequest(line, ctx);
            string text = line.Text.Length > 44 ? line.Text[..43] + "." : line.Text;
            Console.WriteLine($"  \"{text,-44}\" {line.DeliverySeconds,7:F1}s  {d}");
        }

        // The short one must be refused; a long one must be accepted.
        // "Back again." is 1.1 s, which clears the 0.83 s bar. "You." does not.
        var shortLine = new DialogueLine { Id = "x", Text = "You." };
        var longLine = bank.Lines.First(l => l.Id == "mattie.dry");

        if (policy.ShouldRequest(shortLine, ctx) != CodaDecision.LineTooShort)
            return $"a {shortLine.DeliverySeconds:F2} s line was given a coda it cannot cover";
        if (policy.ShouldRequest(longLine, ctx) != CodaDecision.Requested)
            return "a long line with plenty to notice was refused a coda";

        policy.ModelAvailable = false;
        if (policy.ShouldRequest(longLine, ctx) != CodaDecision.NoModel)
            return "a coda was requested with no model loaded";

        policy.ModelAvailable = true;
        var boring = Ctx(fuel: 0.95, condition: 1.0, meetings: 3, hoursSince: 20);
        if (policy.ShouldRequest(longLine, boring) != CodaDecision.NothingWorthNoticing)
            return "a coda was requested when there was nothing to remark on";

        Console.WriteLine();
        Console.WriteLine("  observations available when arriving on fumes in a beaten aircraft:");
        foreach (string o in CodaPolicy.BuildObservations(ctx)) Console.WriteLine($"    - {o}");
        return null;
    }

    public static string? Validation()
    {
        var policy = new CodaPolicy { ModelAvailable = true };
        NpcMind npc = DialogueCorpus.Mattie();
        var ctx = Ctx(fuel: 0.08, condition: 0.4, meetings: 3, hoursSince: 30);
        const string spoken = "You came in on fumes. One day the wind will be wrong and that will be the whole story.";

        Console.WriteLine("  every candidate sentence, and what the validator does with it");
        Console.WriteLine();

        var cases = new (string text, bool shouldPass)[]
        {
            ("That tail rotor has seen better days.", true),
            ("You are carrying more than usual.", true),
            ("\"Still flying it into the ground, then.\"", true),
            ("Where are you headed next?", false),                       // asked a question
            ("I'll pay you well if you bring me a fuel pump.", false),   // invented a quest
            ("Head to the depot at Slack Pit and ask for Ren.", false),  // invented a place to go
            ("I could offer you work if you want it.", false),           // offered something
            ("You came in on fumes and the wind will be wrong.", false), // restated the line
            ("Check the database for a replacement.", false),            // anachronism
            ("You there helicopter pilot like you should know better", false), // forbidden phrase
            ("This is a very long sentence indeed which goes on and on well past any " +
             "reasonable limit for a short remark about something visible", false),
            ("", false),
        };

        int wrong = 0;
        foreach (var (text, shouldPass) in cases)
        {
            CodaVerdict v = policy.Validate(text, ctx, npc, spoken);
            bool ok = v.Accepted == shouldPass;
            if (!ok) wrong++;
            string shown = text.Length > 52 ? text[..51] + "." : text;
            Console.WriteLine($"  {(ok ? "  " : "!!")} {(v.Accepted ? "PASS" : "drop")}  " +
                              $"{shown,-54} {(v.Accepted ? "" : v.Reason)}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {cases.Length - wrong} of {cases.Length} judged correctly");
        return wrong == 0 ? null : $"{wrong} sentences were judged wrongly";
    }

    public static string? Streaming()
    {
        // Sentence-granular commit: nothing is shown until a whole sentence has arrived.
        Console.WriteLine("  tokens arriving one at a time; only whole sentences are released");
        const string stream = "That tail rotor has seen better days. I would not trust it past the ridge. ";
        var buffer = new System.Text.StringBuilder();
        int consumed = 0;
        var released = new List<string>();

        foreach (string token in stream.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            buffer.Append(token).Append(' ');
            foreach (string s in CodaPolicy.CompleteSentences(buffer.ToString(), ref consumed))
            {
                released.Add(s.Trim());
                Console.WriteLine($"    released: \"{s.Trim()}\"");
            }
        }

        Console.WriteLine($"  {released.Count} sentences released from {stream.Split(' ').Length} tokens");

        // A decimal must not be mistaken for a sentence end.
        int c2 = 0;
        var numeric = CodaPolicy.CompleteSentences("Fuel is at 3.5 tons and falling. ", ref c2).ToList();

        if (released.Count != 2) return $"released {released.Count} sentences, expected 2";
        if (!released[0].EndsWith("days.")) return "the first sentence was not cut at its full stop";
        if (numeric.Count != 1) return $"a decimal point was treated as a sentence end ({numeric.Count} found)";
        return null;
    }

    public static string? Memory()
    {
        // The finding that mattered most in the research: the perceived magic is memory.
        NpcMind npc = DialogueCorpus.Mattie();
        var policy = new CodaPolicy { ModelAvailable = true };

        Console.WriteLine("  three visits, and what she has to work with each time");
        Console.WriteLine();

        var visits = new (string label, TalkContext ctx)[]
        {
            ("day 1, first arrival",  Ctx(fuel: 0.5, condition: 1.0, now: 86400)),
            ("day 2, tail rotor shot", Ctx(fuel: 0.2, condition: 0.35, now: 86400 * 2, parts: 2)),
            ("day 6, patched up",     Ctx(fuel: 0.6, condition: 0.85, now: 86400 * 6, parts: 5)),
        };

        foreach (var (label, ctxIn) in visits)
        {
            var c = ctxIn;
            c.PreviousMeetings = npc.Meetings;
            c.HoursSinceLastMeeting = double.IsNegativeInfinity(npc.LastSeenAt)
                ? double.PositiveInfinity : (c.Now - npc.LastSeenAt) / 3600.0;
            if (npc.Meetings == 1) c.VisibleFittings.Add("a radar warning receiver");

            Console.WriteLine($"  {label}");
            var mem = DialogueCorpus.MemoryPhrases(npc);
            Console.WriteLine($"    remembers:    {(mem.Count == 0 ? "nothing yet" : string.Join("; ", mem))}");
            var obs = CodaPolicy.BuildObservations(c);
            Console.WriteLine($"    can see:      {(obs.Count == 0 ? "nothing remarkable" : string.Join("; ", obs))}");

            DialogueCorpus.RememberVisit(npc, c);
            Console.WriteLine();
        }

        var final = DialogueCorpus.MemoryPhrases(npc);
        Console.WriteLine($"  after three visits she remembers: {string.Join("; ", final)}");

        // Show the prompt that would actually go to the model.
        var last = Ctx(fuel: 0.6, condition: 0.85, meetings: 3, hoursSince: 96, now: 86400 * 6, parts: 5);
        var req = new CodaRequest
        {
            NpcName = npc.Name, Persona = npc.Persona, Wants = npc.Wants, Standing = npc.Standing,
            SpokenLine = "Back again.",
            BudgetMs = 3000,
        };
        req.Observations.AddRange(CodaPolicy.BuildObservations(last));
        req.Memories.AddRange(final);
        req.Forbidden.AddRange(npc.Forbidden);

        Console.WriteLine();
        Console.WriteLine("  the prompt that would go to the 1.7B model:");
        foreach (string line in req.BuildPrompt().Split('\n')) Console.WriteLine($"    | {line}");

        int tokens = req.BuildPrompt().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Console.WriteLine($"  roughly {tokens} words of prompt");

        if (npc.Meetings != 3) return $"recorded {npc.Meetings} meetings instead of 3";
        if (final.Count == 0) return "she remembered nothing at all after three visits";
        if (!final.Any(p => p.Contains("radar warning"))) return "she did not notice a new box on the aircraft";
        if (tokens > 160) return $"the prompt is {tokens} words - too long for a 1.7B model on a latency budget";
        return null;
    }

    // =====================================================================================
    //  Voice, knowledge and premise
    // =====================================================================================

    /// <summary>
    /// The nine named people, and whether any of them could be told apart from a line.
    ///
    /// The mechanical part of that is testable and is tested here: depth per character,
    /// coverage of every tag, and - the one that actually catches copy-paste - no two
    /// characters sharing a sentence. A voice that borrows another character's line is
    /// not a voice.
    /// </summary>
    public static string? Voices()
    {
        Console.WriteLine($"  {"id",-9} {"name",-16} {"lines",5} {"greet",6} {"talk",5} {"part",5} {"gated",6}");

        var byText = new Dictionary<string, string>();
        var byId = new HashSet<string>();
        string? failure = null;
        int total = 0;

        foreach (var (id, npc, bank) in Cast())
        {
            int gated = bank.Lines.Count(l => l.Requires.Any(
                r => r.Key.StartsWith(Requirement.KnowsPrefix) || r.Key.StartsWith(Requirement.UnknownPrefix)));
            total += bank.Lines.Count;

            Console.WriteLine($"  {id,-9} {npc.Name,-16} {bank.Lines.Count,5} {Count(bank, "greeting"),6} " +
                              $"{Count(bank, "talk"),5} {Count(bank, "parting"),5} {gated,6}");

            if (bank.Lines.Count < 40) failure ??= $"{npc.Name} has only {bank.Lines.Count} lines";
            if (Count(bank, "greeting") < 18) failure ??= $"{npc.Name} has only {Count(bank, "greeting")} greetings";
            if (Count(bank, "talk") < 10) failure ??= $"{npc.Name} has only {Count(bank, "talk")} things to say";
            if (Count(bank, "parting") < 6) failure ??= $"{npc.Name} has only {Count(bank, "parting")} partings";
            if (gated < 6) failure ??= $"{npc.Name} has only {gated} knowledge-gated lines";

            foreach (DialogueLine l in bank.Lines)
            {
                if (!byId.Add(l.Id)) failure ??= $"duplicate line id {l.Id}";
                if (byText.TryGetValue(l.Text, out string? other) && other != id)
                    failure ??= $"{npc.Name} and {other} say exactly the same thing: \"{l.Text}\"";
                byText[l.Text] = id;

                // A character's own forbidden list is what the coda validator will refuse.
                // If the baked layer says it, the two layers disagree about who this is.
                foreach (string bad in npc.Forbidden)
                    if (l.Text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                        failure ??= $"{npc.Name} says \"{bad}\", which is on her own forbidden list ({l.Id})";
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  {total} authored lines across {DialogueCorpus.NamedIds.Length} named people, " +
                          $"{byText.Count} of them unique sentences");
        Console.WriteLine();
        Console.WriteLine("  one line each, same pilot, same ordinary arrival:");
        Console.WriteLine();

        foreach (var (id, npc, bank) in Cast())
        {
            var c = Ctx(meetings: 4, hoursSince: 40, standing: 0.3, now: 777000);
            DialogueLine? l = bank.Select(c, "talk", 777000);
            Console.WriteLine($"  {npc.Name,-16} \"{l?.Text ?? "(nothing)"}\"");
        }

        return failure;
    }

    /// <summary>
    /// Knowledge gating: the same person, the same state, before and after the player
    /// learns something. If the line does not change, the story layer is decorative.
    /// </summary>
    public static string? KnowledgeGating()
    {
        // The id each character's arc actually turns on, per story.md 3.
        var turns = new (string id, string knowledge, string tag)[]
        {
            ("mattie",  DialogueCorpus.Knows.Callsign, "talk"),
            ("doss",    DialogueCorpus.Knows.Rota,     "talk"),
            ("nell",    DialogueCorpus.Knows.Manifest, "talk"),
            ("bel",     DialogueCorpus.Knows.Wreck,    "talk"),
            ("osie",    DialogueCorpus.Knows.Cairn,    "talk"),
            ("ferren",  DialogueCorpus.Knows.Roster,   "talk"),
            ("wray",    DialogueCorpus.Knows.Magazine, "talk"),
            ("juno",    DialogueCorpus.Knows.Window,   "talk"),
            ("sparrow", DialogueCorpus.Knows.Magazine, "talk"),
        };

        Console.WriteLine("  the same question, before and after the player learns one thing");
        Console.WriteLine();

        string? failure = null;

        foreach (var (id, knowledge, tag) in turns)
        {
            var before = DialogueCorpus.Named(id)!.Value.Bank;
            var after = DialogueCorpus.Named(id)!.Value.Bank;
            string name = DialogueCorpus.Named(id)!.Value.Npc.Name;

            var cb = Ctx(meetings: 4, hoursSince: 40, standing: 0.5, medical: true, now: 500000);
            var ca = Ctx(meetings: 4, hoursSince: 40, standing: 0.5, medical: true, now: 500000,
                         knows: new[] { knowledge });

            DialogueLine? lb = before.Select(cb, tag, 500000);
            DialogueLine? la = after.Select(ca, tag, 500000);

            Console.WriteLine($"  {name}  (learns {knowledge})");
            Console.WriteLine($"    before  \"{lb?.Text ?? "(nothing)"}\"");
            Console.WriteLine($"    after   \"{la?.Text ?? "(nothing)"}\"");
            Console.WriteLine();

            if (la is null) failure ??= $"{name} had nothing to say once the player knew {knowledge}";
            else if (lb?.Id == la.Id)
                failure ??= $"{name} said the same thing before and after {knowledge}";
            else if (!la.Requires.Any(r => r.Key == Requirement.KnowsPrefix + knowledge))
                failure ??= $"{name}'s line after {knowledge} is not actually gated on it";
        }

        // And the arc has to sequence: walking the thread in order must keep changing what
        // the character says, without the selector ever falling back to the ungated line.
        Console.WriteLine("  Mattie, walked forward through the thread:");
        var bank = DialogueCorpus.MattieLines();
        var known = new List<string>();
        var seen = new List<string>();
        double clock = 1_000_000;
        foreach (string k in new[]
        {
            DialogueCorpus.Knows.Callsign, DialogueCorpus.Knows.Band, DialogueCorpus.Knows.Wreck,
            DialogueCorpus.Knows.Roster, DialogueCorpus.Knows.Wray,
        })
        {
            known.Add(k);
            clock += 40 * 3600;
            // Standing 0.5: she does not produce the fuel chit, which needs 0.6 and is
            // deliberately legal at every later beat. This walk is about the beats.
            var c = Ctx(meetings: 6, hoursSince: 40, standing: 0.5, now: clock, knows: known.ToArray());
            DialogueLine? l = bank.Select(c, "thread", clock);
            Console.WriteLine($"    +{k,-18} \"{l?.Text ?? "(nothing)"}\"");
            if (l is null) { failure ??= $"Mattie had no thread line at {k}"; continue; }
            seen.Add(l.Id);
            if (!l.Requires.Any(r => r.Key.StartsWith(Requirement.KnowsPrefix)
                                  || r.Key.StartsWith(Requirement.UnknownPrefix)))
                failure ??= $"Mattie's thread line at {k} is not gated on knowledge at all";
        }
        if (seen.Distinct().Count() < seen.Count)
            failure ??= "Mattie repeated a thread beat while the thread was still moving";

        // The negative gate must work too: a pre-knowledge line must stop being legal.
        var late = DialogueCorpus.BelLines();
        var lateCtx = Ctx(meetings: 4, hoursSince: 40, standing: 0.6, now: 2_000_000,
                          knows: new[] { DialogueCorpus.Knows.Wreck });
        bool refusalStillLegal = late.Lines
            .Where(l => l.Id == "bel.thread.wreck.unknown")
            .Any(l => l.Matches(lateCtx));
        Console.WriteLine();
        Console.WriteLine($"  Bel's refusal to say where the wreck is, once you have been inside it: " +
                          $"{(refusalStillLegal ? "STILL LEGAL" : "no longer legal")}");
        if (refusalStillLegal)
            failure ??= "Bel still refuses to say where the wreck is after the player has searched it";

        return failure;
    }

    /// <summary>
    /// D-008, which is LOCKED and is the one constraint the design says cannot be traded:
    /// no other helicopter pilots exist. D-050 records what it cost the last time a
    /// workstream wrote one in without reading the decision.
    ///
    /// Per D-006's blocklist discipline, these are PHRASINGS, not sentences. The earlier
    /// coda blocklist listed "i can offer" and let "I could offer you work if you want it"
    /// through; the same mistake here would be blocking one exact rival-pilot sentence and
    /// letting every paraphrase past. Past-tense aviation is deliberately NOT blocked -
    /// Mattie watched four a day go over that ridge, and the collapse ending that is the
    /// setting. What is blocked is a second flyer, or a second aircraft, now.
    /// </summary>
    public static string? PremiseGuard()
    {
        string[] rivalFlyer =
        {
            "another pilot", "other pilot", "second pilot", "two pilots", "pilot like you",
            "fellow pilot", "rival pilot", "pilots left", "another flyer", "other flyers",
            "another helicopter", "other helicopter", "second helicopter", "two helicopters",
            "another rotor", "other rotors", "two aircraft", "both aircraft",
            "the other aircraft", "another aircraft like", "machine like yours",
            "one like yours", "another one like", "someone else flying", "somebody else flying",
            "anyone else flying", "flies one too", "fly one too", "still flying one",
            "he flies", "she flies", "they fly one", "another bird", "other birds in the air",
            "work together in the air", "two of you up there",
        };

        var banks = new List<(string who, DialogueBank bank)>();
        foreach (var (id, _, bank) in Cast()) banks.Add((id, bank));
        foreach (DialogueCorpus.RegionTag r in Enum.GetValues<DialogueCorpus.RegionTag>())
            foreach (SiteKindTag k in Enum.GetValues<SiteKindTag>())
                banks.Add(($"generic {r}/{k}", DialogueCorpus.SettlerLines(r, k)));

        int scanned = 0;
        var hits = new List<string>();
        var uniqueText = new HashSet<string>();

        foreach (var (who, bank) in banks)
            foreach (DialogueLine l in bank.Lines)
            {
                scanned++;
                uniqueText.Add(l.Text);
                foreach (string bad in rivalFlyer)
                    if (l.Text.Contains(bad, StringComparison.OrdinalIgnoreCase))
                        hits.Add($"{who}/{l.Id}: \"{bad}\" in \"{l.Text}\"");
            }

        Console.WriteLine($"  {scanned} line instances scanned ({uniqueText.Count} distinct sentences) " +
                          $"against {rivalFlyer.Length} rival-flyer phrasings");

        // The blocklist has to be able to catch something, or it is decoration. These are
        // the shapes D-050 actually found in SearchThread before it was corrected.
        var shouldTrip = new[]
        {
            "Two aircraft can carry what one cannot.",
            "There is another pilot out east who flies one like yours.",
            "You are not the only one still flying one of those.",
            "Kara and you could work together in the air.",
            "I saw a second helicopter over the ridge last week.",
        };
        int caught = 0;
        foreach (string probe in shouldTrip)
            if (rivalFlyer.Any(bad => probe.Contains(bad, StringComparison.OrdinalIgnoreCase))) caught++;
        Console.WriteLine($"  blocklist self-check: caught {caught} of {shouldTrip.Length} known-bad phrasings");

        // And it must NOT catch the setting's legitimate past-tense aviation.
        var shouldPass = new[]
        {
            "Before, there were four of these a day over that ridge. I never once looked up.",
            "I worked the field. Chocks, fuel, loadsheets, eleven years.",
            "Nothing has moved off this field in six years.",
            "There is nothing else in the sky, so there is nothing else it could be.",
        };
        var falsePositives = shouldPass
            .Where(t => rivalFlyer.Any(bad => t.Contains(bad, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Console.WriteLine($"  false-positive check on legitimate past-tense aviation: " +
                          $"{shouldPass.Length - falsePositives.Count} of {shouldPass.Length} pass");

        foreach (string h in hits) Console.WriteLine($"  !! {h}");

        if (hits.Count > 0) return $"{hits.Count} line(s) violate D-008";
        if (caught < shouldTrip.Length) return $"the blocklist missed {shouldTrip.Length - caught} known-bad phrasings";
        if (falsePositives.Count > 0) return $"the blocklist rejects legitimate setting text: {falsePositives[0]}";
        return null;
    }

    /// <summary>
    /// D-089: dialogue rewards. When an NPC says "the crate is yours", the module or
    /// knowledge must actually be granted. This test verifies that the reward lines exist,
    /// reference valid targets, fire under the right conditions, and are idempotent.
    /// </summary>
    public static string? Rewards()
    {
        Console.WriteLine("  reward lines tagged in the corpus");
        Console.WriteLine();

        // The three Act II capability trades from story.md 3.
        var expected = new (string npcId, string lineId, DialogueRewardKind kind, string targetId)[]
        {
            ("nell",    "nell.thread.rwr",      DialogueRewardKind.Module,    "rwr"),
            ("osie",    "osie.thread.chart",     DialogueRewardKind.Knowledge, "chart.upland.masking"),
            ("ferren",  "ferren.thread.chart",   DialogueRewardKind.Knowledge, "chart.emitter.locations"),
        };

        foreach (var (npcId, lineId, kind, targetId) in expected)
        {
            var pair = DialogueCorpus.Named(npcId);
            if (pair is null) return $"{npcId} has no bank";

            var (npc, bank) = pair.Value;
            DialogueLine? line = bank.Lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null) return $"{lineId} not found in {npc.Name}'s bank";
            if (line.Reward is null) return $"{lineId} has no reward attached";
            if (line.Reward.Kind != kind) return $"{lineId} reward is {line.Reward.Kind}, expected {kind}";
            if (line.Reward.Id != targetId) return $"{lineId} reward targets '{line.Reward.Id}', expected '{targetId}'";

            Console.WriteLine($"  {npc.Name,-16} {lineId,-24} {kind,-10} -> {targetId}");

            // Module rewards must reference a real module in the catalog.
            if (kind == DialogueRewardKind.Module)
            {
                if (!Loadout.Catalog.ContainsKey(targetId))
                    return $"{lineId} grants module '{targetId}' which is not in the catalog";
            }

            // Knowledge rewards must have a label and detail.
            if (kind == DialogueRewardKind.Knowledge)
            {
                if (string.IsNullOrEmpty(line.Reward.Label))
                    return $"{lineId} knowledge reward has no label";
                if (string.IsNullOrEmpty(line.Reward.Detail))
                    return $"{lineId} knowledge reward has no detail";
            }
        }

        // --- Functional test: the reward line fires under the right conditions.
        Console.WriteLine();
        Console.WriteLine("  functional: does the reward line fire when conditions are met?");

        var nellBank = DialogueCorpus.Named("nell")!.Value.Bank;
        // Conditions for nell.thread.rwr: Knows(Manifest), Standing(0.3, 1)
        var rwrCtx = Ctx(meetings: 4, hoursSince: 40, standing: 0.4, now: 900000,
                         knows: new[] { DialogueCorpus.Knows.Manifest });
        DialogueLine? rwrLine = nellBank.Select(rwrCtx, "talk", 900000);
        Console.WriteLine($"  nell with manifest + standing 0.4: {rwrLine?.Id ?? "(nothing)"}");
        if (rwrLine?.Id != "nell.thread.rwr")
            return $"expected nell.thread.rwr but got {rwrLine?.Id}";
        if (rwrLine.Reward is null)
            return "the selected line has no reward";

        // Without the requirement, the reward line should not fire.
        var noManifest = Ctx(meetings: 4, hoursSince: 40, standing: 0.4, now: 900000);
        DialogueLine? noRwr = nellBank.Select(noManifest, "talk", 900000);
        Console.WriteLine($"  nell without manifest: {noRwr?.Id ?? "(nothing)"}");
        if (noRwr?.Id == "nell.thread.rwr")
            return "nell.thread.rwr fired without search.manifest — gate is broken";

        // --- Module reward is idempotent through Loadout.Find.
        var loadout = new Loadout();
        bool first = loadout.Find("rwr");
        bool second = loadout.Find("rwr");
        Console.WriteLine($"  loadout.Find idempotency: first={first}, second={second}");
        if (!first) return "first Find for rwr should succeed";
        if (second) return "second Find for rwr should be a no-op";

        // --- Knowledge reward is idempotent through Progress.Learn.
        var progress = new Progress();
        bool k1 = progress.Learn(new Knowledge(KnowledgeKind.Chart, "chart.upland.masking",
                                               "Upland masking routes", "test"));
        bool k2 = progress.Learn(new Knowledge(KnowledgeKind.Chart, "chart.upland.masking",
                                               "Upland masking routes", "test"));
        Console.WriteLine($"  progress.Learn idempotency: first={k1}, second={k2}");
        if (!k1) return "first Learn should succeed";
        if (k2) return "second Learn should be a no-op";

        Console.WriteLine();
        Console.WriteLine("  all three trades verified: module refs catalog, knowledge has labels, " +
                          "conditions gate correctly, both paths are idempotent");
        return null;
    }
}

