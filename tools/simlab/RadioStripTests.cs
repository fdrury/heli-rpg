using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for the radio strip (D-085): word-by-word text reveal, queue management,
/// and suppression during threat engagement.
/// </summary>
public static class RadioStripTests
{
    /// <summary>Enqueueing a message and updating makes it active with the first word.</summary>
    public static string? Enqueue()
    {
        var strip = new RadioStrip();
        strip.Enqueue("RADIO", "Wind zero-three-zero at twelve knots", RadioMessageKind.Broadcast);
        strip.Update(0.01);

        if (!strip.Active)
            return "strip not active after enqueue + update";
        if (strip.CurrentSpeaker != "RADIO")
            return $"speaker: {strip.CurrentSpeaker}";
        if (strip.CurrentText != "Wind")
            return $"first word should be 'Wind', got '{strip.CurrentText}'";
        if (strip.CurrentKind != RadioMessageKind.Broadcast)
            return $"kind: {strip.CurrentKind}";

        Console.WriteLine($"  Active: {strip.Active}");
        Console.WriteLine($"  Speaker: {strip.CurrentSpeaker}");
        Console.WriteLine($"  Text: {strip.CurrentText}");
        return null;
    }

    /// <summary>Words appear one by one at the configured rate.</summary>
    public static string? WordReveal()
    {
        var strip = new RadioStrip();
        strip.Enqueue("TX", "one two three four five", RadioMessageKind.Directed);
        strip.Update(0.01); // starts with "one"

        if (strip.CurrentText != "one")
            return $"expected 'one', got '{strip.CurrentText}'";

        // Advance enough for one more word (1/2.55 ≈ 0.392 s)
        strip.Update(0.40);
        if (strip.CurrentText != "one two")
            return $"expected 'one two', got '{strip.CurrentText}'";

        // Advance through remaining words (3 more × 0.392 ≈ 1.18 s)
        strip.Update(1.20);
        if (strip.CurrentText != "one two three four five")
            return $"expected all five words, got '{strip.CurrentText}'";

        Console.WriteLine($"  Full text revealed: {strip.CurrentText}");
        Console.WriteLine($"  Words/sec: {RadioStrip.WordsPerSecond}");
        return null;
    }

    /// <summary>Fully revealed text holds for HoldSeconds then clears.</summary>
    public static string? Hold()
    {
        var strip = new RadioStrip();
        strip.Enqueue("TX", "short message", RadioMessageKind.Broadcast);

        // Reveal all words
        strip.Update(0.01); // "short"
        strip.Update(0.50); // "short message"

        if (strip.CurrentText != "short message")
            return $"expected 'short message', got '{strip.CurrentText}'";

        // Hold for less than HoldSeconds — should still be visible
        strip.Update(RadioStrip.HoldSeconds - 0.5);
        if (!strip.Active)
            return "cleared too early during hold";

        // Hold past HoldSeconds — should clear
        strip.Update(1.0);
        if (strip.Active)
            return $"not cleared after hold: '{strip.CurrentText}'";

        Console.WriteLine($"  Hold duration: {RadioStrip.HoldSeconds}s");
        Console.WriteLine($"  Cleared: {!strip.Active}");
        return null;
    }

    /// <summary>Suppression defers new messages but does not interrupt the current one.</summary>
    public static string? Suppressed()
    {
        var strip = new RadioStrip();
        strip.Enqueue("TX", "first message", RadioMessageKind.Broadcast);
        strip.Update(0.01); // starts "first"

        // Suppress — current message should continue
        strip.Suppressed = true;
        strip.Update(0.40); // should reveal "first message"

        if (!strip.Active)
            return "suppression killed current message";

        // Finish the current message and let it hold-expire
        strip.Update(10.0);

        // Enqueue a second message while suppressed
        strip.Enqueue("TX", "second message", RadioMessageKind.Directed);
        strip.Update(0.01);

        if (strip.Active)
            return "suppressed strip started a new message";
        if (strip.PendingCount != 1)
            return $"expected 1 pending, got {strip.PendingCount}";

        // Unsuppress — the pending message should start
        strip.Suppressed = false;
        strip.Update(0.01);

        if (!strip.Active)
            return "did not resume after unsuppression";
        if (strip.CurrentKind != RadioMessageKind.Directed)
            return $"wrong kind after resume: {strip.CurrentKind}";

        Console.WriteLine($"  Suppression deferred correctly");
        Console.WriteLine($"  Resumed with: {strip.CurrentText}");
        return null;
    }

    /// <summary>Multiple messages drain in FIFO order.</summary>
    public static string? QueueOrder()
    {
        var strip = new RadioStrip();
        strip.Enqueue("A", "alpha", RadioMessageKind.Broadcast);
        strip.Enqueue("B", "bravo", RadioMessageKind.Directed);
        strip.Enqueue("C", "charlie", RadioMessageKind.Intercepted);

        // First message
        strip.Update(0.01);
        if (strip.CurrentSpeaker != "A" || strip.CurrentText != "alpha")
            return $"first: {strip.CurrentSpeaker}/{strip.CurrentText}";

        // Let it fully reveal and hold-expire
        strip.Update(RadioStrip.HoldSeconds + 1.0);
        strip.Update(0.01);

        // Second message
        if (strip.CurrentSpeaker != "B" || strip.CurrentText != "bravo")
            return $"second: {strip.CurrentSpeaker}/{strip.CurrentText}";

        // Let it expire
        strip.Update(RadioStrip.HoldSeconds + 1.0);
        strip.Update(0.01);

        // Third message
        if (strip.CurrentSpeaker != "C" || strip.CurrentText != "charlie")
            return $"third: {strip.CurrentSpeaker}/{strip.CurrentText}";

        Console.WriteLine($"  Queue drained A → B → C in order");
        return null;
    }

    /// <summary>Beat 5 gate checks: weather window and airborne requirement.</summary>
    public static string? VoiceBeat()
    {
        // The weather window is 06:20–07:00 (22800–25200 seconds of day)
        if (!SearchThread.IsWeatherWindow(24000))
            return "06:40 (24000s) not in weather window";
        if (!SearchThread.IsWeatherWindow(22800))
            return "06:20 (22800s) not in weather window";
        if (!SearchThread.IsWeatherWindow(25200))
            return "07:00 (25200s) not in weather window";
        if (SearchThread.IsWeatherWindow(22799))
            return "06:19:59 should not be in weather window";
        if (SearchThread.IsWeatherWindow(25201))
            return "07:00:01 should not be in weather window";

        // The beat requires airborne + weather window + knows rota
        var p = Progress.NewGame();
        for (int i = 0; i < 10; i++) p.MarkVisited(i);
        for (int i = 0; i < 3; i++)
            p.Learn(new Knowledge(KnowledgeKind.Frequency, $"f.{i}", $"F{i}", ""));

        // Advance through beats 0-2 to learn search.rota
        var allRoles = new HashSet<string>
        {
            "place.mattie", "place.doss", "place.long_mast", "place.nell",
            "place.field", "place.bel", "place.wreck", "place.osie",
            "place.cairn", "place.ferren", "place.saw_relay", "place.wray",
            "place.juno", "place.tether", "place.sparrow", "place.magazine",
        };
        var ctx = new ThreadContext(24000, true, -1, false, allRoles);
        while (p.Search.Stage < 3)
        {
            var beat = p.Search.TryAdvance(p, ctx);
            if (beat is null) break;
            if (beat.KnowledgeId is not null)
                p.Learn(new Knowledge(KnowledgeKind.Rumour, beat.KnowledgeId,
                    beat.KnowledgeLabel ?? "", beat.KnowledgeDetail ?? ""));
        }

        if (p.Search.Stage != 3)
            return $"expected stage 3 before voice beat, got {p.Search.Stage}";

        // Not airborne — should not fire
        var groundCtx = new ThreadContext(24000, false, -1, false, allRoles);
        if (p.Search.TryAdvance(p, groundCtx) is not null)
            return "voice beat fired on the ground";

        // Wrong time — should not fire
        var wrongTime = new ThreadContext(12 * 3600, true, -1, false, allRoles);
        if (p.Search.TryAdvance(p, wrongTime) is not null)
            return "voice beat fired at noon";

        // Correct conditions — should fire
        var fly = new ThreadContext(24000, true, -1, false, allRoles);
        var voice = p.Search.TryAdvance(p, fly);
        if (voice is null)
            return "voice beat did not fire";
        if (voice.Name != "The voice")
            return $"wrong beat name: {voice.Name}";
        if (voice.RadioText is null)
            return "voice beat has no RadioText";
        if (!voice.RadioText.Contains("knots"))
            return "voice beat radio text does not mention knots";

        Console.WriteLine($"  Weather window: 06:20–07:00 ({22800}–{25200}s)");
        Console.WriteLine($"  Voice beat stage: {p.Search.Stage}");
        Console.WriteLine($"  RadioText: {voice.RadioText[..60]}...");
        return null;
    }
}
