using Rotorwash.Sim;

namespace Rotorwash.SimLab;

public static class DialogueTests
{
    static TalkContext Ctx(double fuel = 0.8, double condition = 1.0, int meetings = 0,
                           double hoursSince = double.PositiveInfinity, double standing = 0.2,
                           double now = 3600, int parts = 0, double carried = 0)
    {
        var c = new TalkContext
        {
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
        return c;
    }

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
}
