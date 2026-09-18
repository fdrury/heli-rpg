namespace Rotorwash.Sim;

/// <summary>Something an NPC noticed about the player, and is prepared to mention later.</summary>
public readonly record struct MemoryFact(string Key, string Value, double WhenSeconds)
{
    public override string ToString() => $"{Key}={Value}";
}

/// <summary>
/// One person, as far as the dialogue system is concerned.
///
/// The research pass on local NPC dialogue found the same thing twice, independently, in
/// the baked-corpus prior art and in the shipped local-model prior art: **the perceived
/// magic is memory, not prose.** Playtesters singled out an NPC remembering a name and a
/// weapon preference, not the quality of its sentences. So memory is the part built first
/// and the part the coda is allowed to use.
/// </summary>
public sealed class NpcMind
{
    public string Id = "";
    public string Name = "";

    /// <summary>A short, fixed description. Goes into the coda prompt verbatim.</summary>
    public string Persona = "";

    /// <summary>Faction standing, -1 (hostile) to +1 (will hold something back for you).</summary>
    public double Standing;

    /// <summary>What they want. Colours everything they say.</summary>
    public string Wants = "";

    /// <summary>Things this character must never say. Enforced by the validator, not by hope.</summary>
    public readonly List<string> Forbidden = new();

    private readonly List<MemoryFact> _memories = new();

    public IReadOnlyList<MemoryFact> Memories => _memories;

    public double LastSeenAt = double.NegativeInfinity;
    public int Meetings;

    /// <summary>Record or update something noticed. Newer observations replace older ones.</summary>
    public void Notice(string key, string value, double now)
    {
        for (int i = 0; i < _memories.Count; i++)
        {
            if (_memories[i].Key != key) continue;
            _memories[i] = new MemoryFact(key, value, now);
            return;
        }
        _memories.Add(new MemoryFact(key, value, now));
        // A person remembers a handful of things about someone, not a database.
        if (_memories.Count > 12) _memories.RemoveAt(0);
    }

    public string? Recall(string key)
    {
        foreach (MemoryFact m in _memories) if (m.Key == key) return m.Value;
        return null;
    }

    public bool Remembers(string key) => Recall(key) is not null;
}

/// <summary>
/// Everything true about the player and the world at the moment of speaking.
///
/// Facts only. The dialogue system never receives an interpretation - it receives numbers
/// and names and decides for itself what is worth remarking on, which is what stops the
/// writing from being about the game telling you how you feel.
/// </summary>
public struct TalkContext
{
    public double Now;                  // world clock, seconds
    public string SiteId;
    public string SiteName;
    public string RegionName;

    public double FuelFraction;         // 0..1
    public double WorstComponentHealth; // 0..1
    public string WorstComponentName;
    public double CarriedMass;          // kg
    public int CarriedParts;
    public bool CarriedMedical;

    public double HoursSinceLastMeeting;   // +inf if never
    public int PreviousMeetings;
    public bool ArrivedDamaged;
    public bool ArrivedLowOnFuel;
    public bool ArrivedAtNight;
    public double Standing;

    /// <summary>What is bolted to the aircraft. Visible to anyone standing next to it.</summary>
    public readonly List<string> VisibleFittings = new();

    public TalkContext() { }
}

/// <summary>A condition a baked line requires. Deliberately tiny and inspectable.</summary>
public readonly record struct Requirement(string Key, double Min, double Max)
{
    public bool Holds(in TalkContext c) => Key switch
    {
        "fuel" => c.FuelFraction >= Min && c.FuelFraction <= Max,
        "condition" => c.WorstComponentHealth >= Min && c.WorstComponentHealth <= Max,
        "meetings" => c.PreviousMeetings >= Min && c.PreviousMeetings <= Max,
        "standing" => c.Standing >= Min && c.Standing <= Max,
        "hours_since" => c.HoursSinceLastMeeting >= Min && c.HoursSinceLastMeeting <= Max,
        "carried" => c.CarriedMass >= Min && c.CarriedMass <= Max,
        _ => true,
    };

    public static Requirement Fuel(double min, double max) => new("fuel", min, max);
    public static Requirement Condition(double min, double max) => new("condition", min, max);
    public static Requirement Meetings(double min, double max) => new("meetings", min, max);
    public static Requirement Standing(double min, double max) => new("standing", min, max);
    public static Requirement HoursSince(double min, double max) => new("hours_since", min, max);
}

/// <summary>One hand-vetted line. Thousands of these are generated offline and shipped.</summary>
public sealed class DialogueLine
{
    public string Id = "";
    public string Text = "";
    public readonly List<Requirement> Requires = new();

    /// <summary>Higher wins ties. Use it to make the specific beat the generic.</summary>
    public double Weight = 1.0;

    /// <summary>Tags for selection and for telling the coda what tone to match.</summary>
    public readonly List<string> Tags = new();

    /// <summary>
    /// Estimated delivery time. This is not decoration: it is the latency budget. A coda
    /// is only requested when the line takes long enough to say that the local model can
    /// finish behind it (D-006a).
    /// </summary>
    public double DeliverySeconds => EstimateDelivery(Text);

    /// <summary>Roughly three words a second, plus a beat at each sentence end.</summary>
    public static double EstimateDelivery(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int stops = 0;
        foreach (char ch in text) if (ch is '.' or '?' or '!') stops++;
        return words / 3.0 + stops * 0.45;
    }

    public bool Matches(in TalkContext c)
    {
        foreach (Requirement r in Requires) if (!r.Holds(c)) return false;
        return true;
    }
}

/// <summary>
/// Chooses what gets said.
///
/// The structure that makes a finite corpus feel responsive rather than canned, borrowed
/// from how Fallout: New Vegas and Citizen Sleeper condition their lines: score every
/// candidate by how SPECIFIC it is to the current state, not merely whether it is legal,
/// and prefer the most specific thing that has not been said recently.
/// </summary>
public sealed class DialogueBank
{
    private readonly List<DialogueLine> _lines = new();
    private readonly Dictionary<string, double> _lastUsed = new();

    public IReadOnlyList<DialogueLine> Lines => _lines;

    public void Add(DialogueLine line) => _lines.Add(line);

    public void AddRange(IEnumerable<DialogueLine> lines) { foreach (var l in lines) _lines.Add(l); }

    /// <summary>
    /// Pick the best line for this moment.
    /// </summary>
    /// <param name="tag">Restrict to lines carrying this tag, e.g. "greeting".</param>
    public DialogueLine? Select(in TalkContext context, string tag, double now)
    {
        DialogueLine? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (DialogueLine line in _lines)
        {
            if (tag.Length > 0 && !line.Tags.Contains(tag)) continue;
            if (!line.Matches(context)) continue;

            // Specificity: a line that asked for four conditions and got them is a better
            // fit than a line that asked for none. This is the whole trick.
            double score = line.Requires.Count * 10.0 + line.Weight;

            // Recency penalty, decaying over a day of world time. Saying the same thing
            // twice running is what makes a corpus feel small.
            if (_lastUsed.TryGetValue(line.Id, out double when))
            {
                // Clamped at zero: a clock that appears to run backwards (a reload, a
                // fast-travel, a test) must not turn the recency penalty into a bonus or
                // an enormous punishment.
                double hours = Math.Max(0, (now - when) / 3600.0);
                score -= Math.Max(0, 26.0 - hours * 2.0);
            }

            if (score > bestScore) { bestScore = score; best = line; }
        }

        if (best is not null) _lastUsed[best.Id] = now;
        return best;
    }

    public void ForgetUsage() => _lastUsed.Clear();
}

// ---------------------------------------------------------------------------
//  The coda
// ---------------------------------------------------------------------------

/// <summary>Why a coda was or was not requested. Logged, so the decision is inspectable.</summary>
public enum CodaDecision { Requested, NoModel, LineTooShort, NothingWorthNoticing, Disabled }

/// <summary>What the local model is asked to notice. Never what to say.</summary>
public sealed class CodaRequest
{
    public string NpcName = "";
    public string Persona = "";
    public string Wants = "";
    public double Standing;

    /// <summary>The baked line that is being spoken right now. The coda follows it.</summary>
    public string SpokenLine = "";

    /// <summary>Facts the NPC can see or remember. The coda may reference these and nothing else.</summary>
    public readonly List<string> Observations = new();
    public readonly List<string> Memories = new();
    public readonly List<string> Forbidden = new();

    /// <summary>Milliseconds available before the baked line finishes.</summary>
    public double BudgetMs;

    /// <summary>The prompt, assembled. Kept short: every token is latency on a 1.7B model.</summary>
    public string BuildPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("You are ").Append(NpcName).Append(", ").Append(Persona).Append('\n');
        if (Wants.Length > 0) sb.Append("You want: ").Append(Wants).Append('\n');
        sb.Append("You just said: \"").Append(SpokenLine).Append("\"\n");

        if (Memories.Count > 0)
        {
            sb.Append("You remember about this pilot: ");
            sb.AppendJoin("; ", Memories).Append('\n');
        }
        if (Observations.Count > 0)
        {
            sb.Append("You can see right now: ");
            sb.AppendJoin("; ", Observations).Append('\n');
        }

        // The single most important instruction, and the reason this works at 1.7B:
        // noticing is a much smaller task than answering.
        sb.Append("Add ONE short sentence remarking on something you can see or remember. ");
        sb.Append("Do not ask a question. Do not offer work, payment, directions or items. ");
        sb.Append("Do not repeat what you just said. Under 20 words.\n");
        return sb.ToString();
    }
}

public readonly record struct CodaVerdict(bool Accepted, string Text, string Reason);

/// <summary>
/// Decides whether to ask for a coda, and whether to let it through.
///
/// D-006a rewrote this part of the design and the corrections are load-bearing:
///
///   THE CODA NOTICES; IT DOES NOT ANSWER. If the player's input were a menu choice, a
///   frontier model could bake every possible reply offline for free and the local model
///   would earn nothing. It justifies itself only against unbounded state - fuel, damage,
///   cargo, how long since you were here. This reframe also kills tonal mismatch, state
///   contradiction and quest-invention by construction, because a remark about a fact
///   cannot promise anything.
///
///   COMMIT BY SENTENCE, NOT BY TOKEN. You cannot un-display streamed text, so nothing is
///   shown until a whole sentence has arrived and passed validation.
///
///   ONLY WHEN THERE IS COVER. A coda is requested only if the spoken line takes at least
///   1.5x the measured p95 latency to deliver. "Yeah?" buys nothing and gets nothing.
/// </summary>
public sealed class CodaPolicy
{
    /// <summary>Measured p95 round trip of the local model, seconds. Updated at runtime.</summary>
    public double MeasuredP95 { get; set; } = 0.55;

    /// <summary>Give up and let the baked line end naturally past this.</summary>
    public double HardAbandon { get; set; } = 4.0;

    /// <summary>Required ratio of delivery time to measured latency.</summary>
    public double CoverFactor { get; set; } = 1.5;

    public int MaxWords { get; set; } = 22;

    public bool ModelAvailable { get; set; }
    public bool Enabled { get; set; } = true;

    public CodaDecision ShouldRequest(DialogueLine line, in TalkContext context)
    {
        if (!Enabled) return CodaDecision.Disabled;
        if (!ModelAvailable) return CodaDecision.NoModel;
        if (line.DeliverySeconds < MeasuredP95 * CoverFactor) return CodaDecision.LineTooShort;
        if (BuildObservations(context).Count == 0) return CodaDecision.NothingWorthNoticing;
        return CodaDecision.Requested;
    }

    /// <summary>
    /// Turn the world state into things a person standing next to the aircraft could
    /// actually notice. Only remarkable facts: a full tank is not worth a sentence.
    /// </summary>
    public static List<string> BuildObservations(in TalkContext c)
    {
        var o = new List<string>();

        if (c.FuelFraction < 0.12) o.Add("their fuel gauge is almost on the stop");
        else if (c.FuelFraction < 0.28) o.Add("they are low on fuel");

        if (c.WorstComponentHealth < 0.35) o.Add($"the {c.WorstComponentName} is visibly wrecked");
        else if (c.WorstComponentHealth < 0.7) o.Add($"the {c.WorstComponentName} has been patched up");

        if (c.CarriedMass > 240) o.Add("the aircraft is loaded heavy");
        if (c.CarriedParts > 3) o.Add("they are carrying salvaged parts");
        if (c.CarriedMedical) o.Add("they are carrying medical supplies");

        if (c.ArrivedAtNight) o.Add("it is dark and they flew in anyway");
        if (c.PreviousMeetings == 0) o.Add("this is the first time they have been here");
        else if (c.HoursSinceLastMeeting > 72) o.Add($"it has been about {c.HoursSinceLastMeeting / 24:F0} days");

        foreach (string f in c.VisibleFittings) o.Add($"the aircraft now has {f} fitted");

        return o;
    }

    /// <summary>
    /// Validate one complete sentence from the model. Nothing reaches the screen until it
    /// has been through here.
    /// </summary>
    public CodaVerdict Validate(string sentence, in TalkContext context, NpcMind npc, string spokenLine)
    {
        string s = sentence.Trim();
        if (s.Length == 0) return new CodaVerdict(false, "", "empty");

        // Strip anything the model wrapped it in.
        s = s.Trim('"', '\'', '*', '-', ' ');
        if (s.Length < 3) return new CodaVerdict(false, "", "too short to be a sentence");

        int words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > MaxWords) return new CodaVerdict(false, "", $"{words} words, over the cap");

        // A coda notices. It does not ask, and it does not promise.
        if (s.Contains('?')) return new CodaVerdict(false, "", "asked a question");

        foreach (string bad in QuestSmells)
            if (s.Contains(bad, StringComparison.OrdinalIgnoreCase))
                return new CodaVerdict(false, "", $"tried to offer something ({bad})");

        foreach (string bad in npc.Forbidden)
            if (s.Contains(bad, StringComparison.OrdinalIgnoreCase))
                return new CodaVerdict(false, "", $"said a forbidden thing ({bad})");

        // Do not let it simply repeat the line it is supposed to be following.
        if (Overlap(s, spokenLine) > 0.6) return new CodaVerdict(false, "", "restated the baked line");

        // Anachronism guard: nothing in this world has any of these.
        foreach (string bad in Anachronisms)
            if (s.Contains(bad, StringComparison.OrdinalIgnoreCase))
                return new CodaVerdict(false, "", $"anachronism ({bad})");

        if (!char.IsUpper(s[0])) s = char.ToUpper(s[0]) + s[1..];
        if (s[^1] is not ('.' or '!' or '.')) s += ".";

        return new CodaVerdict(true, s, "ok");
    }

    /// <summary>
    /// Phrases that mean the model has started inventing content the game cannot honour.
    /// This is the failure mode every shipped local-NPC product was criticised for.
    /// </summary>
    private static readonly string[] QuestSmells =
    {
        "i'll pay", "i will pay", "reward", "bring me", "fetch", "go to", "head to",
        "meet me", "i need you to", "if you can get", "in exchange", "deal?",
        "quest", "mission", "task for you", "job for you", "i'll give you", "i can offer",
        // Phrasings rather than exact sentences: the first version listed "i can offer"
        // and let "I could offer you work if you want it" straight through.
        "offer you", "offer work", "i'll trade", "i will trade", "i want you to",
        "come back with", "find me a", "get me a", "see me about",
    };

    private static readonly string[] Anachronisms =
    {
        "internet", "phone", "email", "wifi", "satellite tv", "app ", "online",
        "database", "server", "download",
    };

    private static double Overlap(string a, string b)
    {
        var wa = new HashSet<string>(a.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var wb = new HashSet<string>(b.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (wa.Count == 0) return 0;
        int shared = 0;
        foreach (string w in wa) if (wb.Contains(w)) shared++;
        return shared / (double)wa.Count;
    }

    /// <summary>
    /// Split a growing stream into complete sentences. The caller feeds in whatever has
    /// arrived so far and gets back whole sentences only - which is what makes
    /// sentence-granular commit possible at all.
    /// </summary>
    public static IEnumerable<string> CompleteSentences(string buffer, ref int consumed)
    {
        var done = new List<string>();
        for (int i = consumed; i < buffer.Length; i++)
        {
            if (buffer[i] is not ('.' or '!' or '?')) continue;
            // Require whitespace or end after the stop, so "3.5" is not a sentence.
            if (i + 1 < buffer.Length && !char.IsWhiteSpace(buffer[i + 1])) continue;
            done.Add(buffer[consumed..(i + 1)]);
            consumed = i + 1;
        }
        return done;
    }
}
