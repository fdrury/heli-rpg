using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for directed radio calls (story.md §4.3, carrier type 2): one-time
/// messages that fire when the player enters a region whose relay has been tuned.
/// </summary>
public static class DirectedCallTests
{
    /// <summary>All 8 regions have authored content.</summary>
    public static string? AllRegions()
    {
        // RegionKind ordinals: 0=Basin, 1=Farmland, 2=Exurb, 3=City,
        //                      4=Industrial, 5=Upland, 6=Wetland, 7=Ashfield
        for (int i = 0; i < 8; i++)
        {
            if (!DirectedCalls.Calls.ContainsKey(i))
                return $"no content for region ordinal {i}";
            var (speaker, text) = DirectedCalls.Calls[i];
            if (string.IsNullOrWhiteSpace(speaker))
                return $"empty speaker for region {i}";
            if (string.IsNullOrWhiteSpace(text))
                return $"empty text for region {i}";
        }
        Console.WriteLine($"  8 regions, all have speaker + text");
        return null;
    }

    /// <summary>TryFire returns a message the first time and null the second.</summary>
    public static string? OneShot()
    {
        var dc = new DirectedCalls();

        var msg = dc.TryFire(0); // Basin
        if (msg is null)
            return "first fire returned null";
        if (msg.Kind != RadioMessageKind.Directed)
            return $"wrong kind: {msg.Kind}";
        if (msg.Speaker != "PAN RELAY")
            return $"wrong speaker: {msg.Speaker}";

        // Second fire for the same region — must be null
        var again = dc.TryFire(0);
        if (again is not null)
            return "second fire was not null — one-shot guard failed";

        // Different region — must still work
        var other = dc.TryFire(5); // Upland
        if (other is null)
            return "different region returned null";
        if (other.Speaker != "COLD SHOULDER")
            return $"wrong speaker for upland: {other.Speaker}";

        Console.WriteLine($"  Basin fired once, blocked on repeat");
        Console.WriteLine($"  Upland fired independently");
        Console.WriteLine($"  FiredCount: {dc.FiredCount}");
        return null;
    }

    /// <summary>HasFired tracks which regions have been delivered.</summary>
    public static string? HasFired()
    {
        var dc = new DirectedCalls();

        if (dc.HasFired(2))
            return "unfired region reports as fired";

        dc.TryFire(2); // Exurb
        if (!dc.HasFired(2))
            return "fired region reports as unfired";
        if (dc.HasFired(3))
            return "other region reports as fired after firing exurb";

        Console.WriteLine($"  HasFired tracks correctly");
        return null;
    }

    /// <summary>Save/load round-trips the fired set.</summary>
    public static string? SaveRoundTrip()
    {
        var p = Progress.NewGame();

        // Fire two regions
        p.DirectedCalls.TryFire(0); // Basin
        p.DirectedCalls.TryFire(7); // Ashfield

        if (p.DirectedCalls.FiredCount != 2)
            return $"expected 2 fired, got {p.DirectedCalls.FiredCount}";

        // Capture → JSON → restore
        var save = new SaveData();
        save.CaptureProgress(p);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();

        if (!p2.DirectedCalls.HasFired(0))
            return "Basin not fired after load";
        if (!p2.DirectedCalls.HasFired(7))
            return "Ashfield not fired after load";
        if (p2.DirectedCalls.HasFired(3))
            return "City should not be fired after load";
        if (p2.DirectedCalls.FiredCount != 2)
            return $"fired count after load: {p2.DirectedCalls.FiredCount}";

        // The fired regions must not fire again
        if (p2.DirectedCalls.TryFire(0) is not null)
            return "Basin fired again after load";
        if (p2.DirectedCalls.TryFire(7) is not null)
            return "Ashfield fired again after load";

        // Unfired region must still work
        var msg = p2.DirectedCalls.TryFire(3);
        if (msg is null)
            return "City did not fire after load";

        Console.WriteLine($"  Fired: Basin + Ashfield survived save/load");
        Console.WriteLine($"  City still available after load");
        return null;
    }

    /// <summary>No region content tells the player what to do (story.md §9).</summary>
    public static string? NoInstructions()
    {
        // story.md §9: "The story never tells the player what to do next."
        // Directed calls report conditions — they never say "go to", "you should",
        // "head for", etc.
        string[] forbidden = { "you should", "go to", "head for", "fly to", "come to", "you must" };

        foreach (var (ordinal, (speaker, text)) in DirectedCalls.Calls)
        {
            string lower = text.ToLowerInvariant();
            foreach (string f in forbidden)
            {
                if (lower.Contains(f))
                    return $"region {ordinal} ({speaker}): contains instruction '{f}'";
            }
        }

        Console.WriteLine($"  All 8 messages are facts, not instructions");
        return null;
    }
}
