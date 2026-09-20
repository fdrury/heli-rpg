using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for intercepted radio calls (story.md §4.3, carrier type 3): overheard
/// hostile traffic when the player is inside a threat envelope and being tracked.
/// </summary>
public static class InterceptedCallTests
{
    /// <summary>All 5 threat kinds have authored content.</summary>
    public static string? AllKinds()
    {
        var expected = new[] { ThreatKind.SearchRadar, ThreatKind.Gun, ThreatKind.Sam,
                               ThreatKind.Manpads, ThreatKind.Aerostat };

        foreach (var kind in expected)
        {
            if (!InterceptedCalls.Corpus.ContainsKey(kind))
                return $"no content for {kind}";
            var lines = InterceptedCalls.Corpus[kind];
            if (lines.Length == 0)
                return $"empty corpus for {kind}";
            foreach (var (speaker, text) in lines)
            {
                if (string.IsNullOrWhiteSpace(speaker))
                    return $"empty speaker in {kind}";
                if (string.IsNullOrWhiteSpace(text))
                    return $"empty text in {kind}";
            }
        }

        Console.WriteLine($"  5 threat kinds, {InterceptedCalls.TotalLines} total lines");
        return null;
    }

    /// <summary>TryFire returns messages until the kind is exhausted, then null.</summary>
    public static string? Exhaustion()
    {
        var ic = new InterceptedCalls();

        int radarCount = InterceptedCalls.Corpus[ThreatKind.SearchRadar].Length;
        int got = 0;

        for (int i = 0; i < radarCount + 5; i++)
        {
            // Clear cooldown between attempts
            ic.Update(InterceptedCalls.CooldownSeconds + 1);
            var msg = ic.TryFire(ThreatKind.SearchRadar);
            if (msg is not null)
            {
                got++;
                if (msg.Kind != RadioMessageKind.Intercepted)
                    return $"wrong kind: {msg.Kind}";
            }
        }

        if (got != radarCount)
            return $"expected {radarCount} SearchRadar lines, got {got}";

        // Other kind should still work
        ic.Update(InterceptedCalls.CooldownSeconds + 1);
        var sam = ic.TryFire(ThreatKind.Sam);
        if (sam is null)
            return "Sam returned null after exhausting SearchRadar";

        Console.WriteLine($"  SearchRadar exhausted after {radarCount} lines");
        Console.WriteLine($"  Sam still available independently");
        return null;
    }

    /// <summary>Cooldown blocks rapid fire.</summary>
    public static string? Cooldown()
    {
        var ic = new InterceptedCalls();

        var first = ic.TryFire(ThreatKind.Gun);
        if (first is null) return "first fire returned null";

        // Without clearing cooldown, second attempt should be blocked
        var blocked = ic.TryFire(ThreatKind.Gun);
        if (blocked is not null) return "second fire should be blocked by cooldown";

        // Advance half the cooldown — still blocked
        ic.Update(InterceptedCalls.CooldownSeconds / 2);
        blocked = ic.TryFire(ThreatKind.Gun);
        if (blocked is not null) return "half-cooldown fire should be blocked";

        // Advance past cooldown — should work
        ic.Update(InterceptedCalls.CooldownSeconds);
        var unblocked = ic.TryFire(ThreatKind.Gun);
        if (unblocked is null) return "post-cooldown fire returned null";

        Console.WriteLine($"  Cooldown blocks at {InterceptedCalls.CooldownSeconds}s");
        return null;
    }

    /// <summary>Save/load round-trips the used set.</summary>
    public static string? SaveRoundTrip()
    {
        var p = Progress.NewGame();

        // Fire two lines from different kinds
        p.InterceptedCalls.TryFire(ThreatKind.SearchRadar);
        p.InterceptedCalls.Update(InterceptedCalls.CooldownSeconds + 1);
        p.InterceptedCalls.TryFire(ThreatKind.Sam);

        if (p.InterceptedCalls.UsedCount != 2)
            return $"expected 2 used, got {p.InterceptedCalls.UsedCount}";

        // Capture → JSON → restore
        var save = new SaveData();
        save.CaptureProgress(p);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();

        if (p2.InterceptedCalls.UsedCount != 2)
            return $"used count after load: {p2.InterceptedCalls.UsedCount}";

        // The used lines must not fire again — exhaust SearchRadar starting from index 1
        p2.InterceptedCalls.Update(InterceptedCalls.CooldownSeconds + 1);
        int radarTotal = InterceptedCalls.Corpus[ThreatKind.SearchRadar].Length;
        int remaining = 0;
        for (int i = 0; i < radarTotal + 2; i++)
        {
            p2.InterceptedCalls.Update(InterceptedCalls.CooldownSeconds + 1);
            if (p2.InterceptedCalls.TryFire(ThreatKind.SearchRadar) is not null)
                remaining++;
        }

        if (remaining != radarTotal - 1)
            return $"expected {radarTotal - 1} remaining SearchRadar, got {remaining}";

        Console.WriteLine($"  Used set survived save/load");
        Console.WriteLine($"  {remaining} SearchRadar lines remaining after 1 used");
        return null;
    }

    /// <summary>No line tells the player what to do (story.md §9).</summary>
    public static string? NoInstructions()
    {
        string[] forbidden = { "you should", "go to", "head for", "fly to",
                               "come to", "you must", "land at", "head to" };

        foreach (var (kind, lines) in InterceptedCalls.Corpus)
        {
            foreach (var (speaker, text) in lines)
            {
                string lower = text.ToLowerInvariant();
                foreach (string f in forbidden)
                    if (lower.Contains(f))
                        return $"{kind} ({speaker}): contains instruction '{f}'";
            }
        }

        Console.WriteLine($"  All lines are overheard traffic, not instructions");
        return null;
    }

    /// <summary>No line addresses the player directly — these are overheard.</summary>
    public static string? NotAddressed()
    {
        // story.md §4.3: "traffic not meant for you"
        // Lines should never say "you" because the operators don't know the player is listening.
        string[] forbidden = { " you ", "your " };

        foreach (var (kind, lines) in InterceptedCalls.Corpus)
        {
            foreach (var (speaker, text) in lines)
            {
                string lower = (" " + text.ToLowerInvariant() + " ");
                foreach (string f in forbidden)
                    if (lower.Contains(f))
                        return $"{kind} ({speaker}): addresses player with '{f.Trim()}' — " +
                               $"intercepted calls are overheard, not directed";
            }
        }

        Console.WriteLine($"  No line addresses the player directly");
        return null;
    }
}
