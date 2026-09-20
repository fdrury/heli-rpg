using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// D-092: Bel's trade completion. The generator lift contract, the wreck position
/// knowledge reward, the fitted-module dialogue gate, and the full trade flow.
/// </summary>
public static class BelTradeTests
{
    /// <summary>
    /// Lift contract completes when the target site is visited.
    /// </summary>
    public static string? LiftCompletion()
    {
        var progress = Progress.NewGame();

        var c = new Contract
        {
            Id = "story.bel.lift.99",
            Kind = ContractKind.Lift,
            Title = "Lift generator from Island Cache",
            Brief = "Hoist the generator.",
            SourceSiteId = 99,
            TargetSiteId = 42,
            TargetName = "Island Cache",
            RewardKnowledgeId = ContractBoard.BelWreckPositionId,
            RewardKnowledgeLabel = "Wreck position — The Drowning",
            RewardKnowledgeDetail = "Bel told you where the wreck is.",
            StandingReward = 0.3,
            PostedAt = progress.Clock,
            Accepted = true,
        };

        // Not yet visited → should not complete
        if (c.CheckCompletion(progress)) return "completed before visiting";

        // Visit the target
        progress.MarkVisited(42);

        // Now it should complete
        if (!c.CheckCompletion(progress)) return "did not complete after visiting";
        if (!c.Completed) return "not marked completed";

        // Payout should grant the wreck position knowledge
        c.PayOut(progress);
        if (!progress.Knows(ContractBoard.BelWreckPositionId))
            return "wreck position knowledge not granted by payout";

        Console.WriteLine($"  Lift contract completed, knowledge granted: {ContractBoard.BelWreckPositionId}");
        return null;
    }

    /// <summary>
    /// StoryContract returns a generator lift when the player has the hoist installed
    /// at Bel's settlement, and null otherwise.
    /// </summary>
    public static string? StoryContractGating()
    {
        var progress = Progress.NewGame();
        var loadout = new Loadout();
        var sites = MakeSites();

        // No hoist → no story contract
        var c1 = ContractBoard.StoryContract("npc.bel", 10, "Stilt Village",
            0, 0, sites, progress, loadout, progress.Clock);
        if (c1 is not null) return "story contract offered without hoist";

        // Install hoist
        loadout.Find("hoist");
        loadout.Install("hoist");

        // Hoist installed → story contract offered
        var c2 = ContractBoard.StoryContract("npc.bel", 10, "Stilt Village",
            0, 0, sites, progress, loadout, progress.Clock);
        if (c2 is null) return "no story contract with hoist installed";
        if (c2.Kind != ContractKind.Lift) return $"expected Lift, got {c2.Kind}";
        if (c2.RewardKnowledgeId != ContractBoard.BelWreckPositionId)
            return $"reward knowledge: {c2.RewardKnowledgeId}";

        Console.WriteLine($"  contract: {c2.Title}");
        Console.WriteLine($"  brief: {c2.Brief}");
        Console.WriteLine($"  reward knowledge: {c2.RewardKnowledgeId}");

        // Already knows wreck position → no contract
        progress.Learn(new Knowledge(KnowledgeKind.Rumour,
            ContractBoard.BelWreckPositionId, "Wreck position", "test"));
        var c3 = ContractBoard.StoryContract("npc.bel", 10, "Stilt Village",
            0, 0, sites, progress, loadout, progress.Clock);
        if (c3 is not null) return "story contract still offered after trade complete";

        // Wrong NPC → no contract
        var c4 = ContractBoard.StoryContract("npc.nell", 10, "Fenmoor",
            0, 0, sites, progress, loadout, progress.Clock);
        if (c4 is not null) return "story contract offered for wrong NPC";

        Console.WriteLine("  gating: no hoist → ✗, hoist → ✓, already traded → ✗, wrong NPC → ✗");
        return null;
    }

    /// <summary>
    /// Bel's hoist dialogue line fires when the hoist is fitted and the wreck
    /// position is unknown. It stops firing once the wreck position is known.
    /// </summary>
    public static string? HoistDialogueGating()
    {
        var bank = DialogueCorpus.BelLines();

        // Context: high standing, many meetings, hoist fitted, wreck unknown
        var withHoist = BelCtx(meetings: 4, standing: 0.5,
            fitted: new[] { "hoist" });
        var line = bank.Select(withHoist, "greeting", withHoist.Now);
        Console.WriteLine($"  with hoist, no wreck position: {line?.Id ?? "(nothing)"}");
        if (line?.Id != "bel.thread.hoist")
            return $"expected bel.thread.hoist, got {line?.Id ?? "nothing"}";

        // Context: hoist fitted but wreck position known → hoist offer should not fire
        var afterTrade = BelCtx(meetings: 4, standing: 0.5,
            fitted: new[] { "hoist" },
            knows: new[] { ContractBoard.BelWreckPositionId });
        var bank2 = DialogueCorpus.BelLines();
        var line2 = bank2.Select(afterTrade, "greeting", afterTrade.Now);
        Console.WriteLine($"  with hoist, wreck position known: {line2?.Id ?? "(nothing)"}");
        if (line2?.Id == "bel.thread.hoist")
            return "bel.thread.hoist still fires after wreck position is known";

        // Context: no hoist fitted → hoist line should not fire
        var noHoist = BelCtx(meetings: 4, standing: 0.5);
        var bank3 = DialogueCorpus.BelLines();
        var line3 = bank3.Select(noHoist, "greeting", noHoist.Now);
        Console.WriteLine($"  without hoist: {line3?.Id ?? "(nothing)"}");
        if (line3?.Id == "bel.thread.hoist")
            return "bel.thread.hoist fires without hoist installed";

        Console.WriteLine("  gating: fitted hoist + unknown → ✓, known → ✗, no hoist → ✗");
        return null;
    }

    /// <summary>
    /// After the trade completes, Bel's dialogue reacts to the wreck position
    /// knowledge, and further reacts when the player has searched the wreck.
    /// </summary>
    public static string? PostTradeDialogue()
    {
        // After trade, before searching wreck
        var bank1 = DialogueCorpus.BelLines();
        var traded = BelCtx(meetings: 5, standing: 0.6,
            knows: new[] { ContractBoard.BelWreckPositionId });
        var line1 = bank1.Select(traded, "talk", traded.Now);
        Console.WriteLine($"  after trade, before wreck search: {line1?.Id ?? "(nothing)"}");
        if (line1?.Id != "bel.thread.traded")
            return $"expected bel.thread.traded, got {line1?.Id ?? "nothing"}";

        // After trade AND wreck searched
        var bank2 = DialogueCorpus.BelLines();
        var found = BelCtx(meetings: 6, standing: 0.6,
            knows: new[] { ContractBoard.BelWreckPositionId, DialogueCorpus.Knows.Wreck });
        var line2 = bank2.Select(found, "talk", found.Now);
        Console.WriteLine($"  after trade and wreck search: {line2?.Id ?? "(nothing)"}");
        if (line2?.Id != "bel.thread.traded.found")
            return $"expected bel.thread.traded.found, got {line2?.Id ?? "nothing"}";

        return null;
    }

    /// <summary>
    /// Lift contract survives save/load round trip.
    /// </summary>
    public static string? LiftRoundTrip()
    {
        var progress = Progress.NewGame();
        progress.Clock = 50000;

        var c = new Contract
        {
            Id = "story.bel.lift.10",
            Kind = ContractKind.Lift,
            Title = "Lift generator from Island Cache",
            Brief = "Hoist the generator.",
            SourceSiteId = 10,
            TargetSiteId = 42,
            TargetName = "Island Cache",
            RewardKnowledgeId = ContractBoard.BelWreckPositionId,
            RewardKnowledgeLabel = "Wreck position — The Drowning",
            RewardKnowledgeDetail = "Bel told you where.",
            StandingReward = 0.3,
            PostedAt = 48000,
            Accepted = true,
        };
        progress.AcceptContract(c);

        var save = new SaveData();
        save.CaptureProgress(progress);

        string json = save.ToJson();
        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();
        if (p2.Contracts.Count != 1) return $"contract count: {p2.Contracts.Count}";

        var c2 = p2.Contracts[0];
        if (c2.Kind != ContractKind.Lift) return $"kind: {c2.Kind}";
        if (c2.Title != "Lift generator from Island Cache") return $"title: {c2.Title}";
        if (c2.TargetSiteId != 42) return $"target: {c2.TargetSiteId}";
        if (c2.RewardKnowledgeId != ContractBoard.BelWreckPositionId)
            return $"knowledge: {c2.RewardKnowledgeId}";

        Console.WriteLine($"  Lift contract round-tripped: {c2.Kind} {c2.Title}");
        return null;
    }

    /// <summary>
    /// The fitted: requirement prefix works correctly.
    /// </summary>
    public static string? FittedRequirement()
    {
        var req = Requirement.Fitted("hoist");

        // Without hoist fitted
        var ctx1 = new TalkContext();
        if (req.Holds(ctx1)) return "fitted requirement passed on empty context";

        // With hoist fitted
        var ctx2 = new TalkContext();
        ctx2.FittedIds.Add("hoist");
        if (!req.Holds(ctx2)) return "fitted requirement failed when hoist is fitted";

        // With a different module fitted
        var ctx3 = new TalkContext();
        ctx3.FittedIds.Add("rwr");
        if (req.Holds(ctx3)) return "fitted hoist passed when only rwr is fitted";

        Console.WriteLine("  fitted: prefix works for hoist");
        return null;
    }

    // ---- helpers ----

    private static TalkContext BelCtx(int meetings = 0, double standing = 0.2,
        string[]? knows = null, string[]? fitted = null)
    {
        var c = new TalkContext
        {
            Now = 500000,
            SiteName = "Stilt Village",
            RegionName = "The Drowning",
            FuelFraction = 0.7,
            WorstComponentHealth = 0.9,
            WorstComponentName = "engine",
            PreviousMeetings = meetings,
            HoursSinceLastMeeting = 48,
            Standing = standing,
        };
        if (knows is not null) foreach (string k in knows) c.KnownIds.Add(k);
        if (fitted is not null) foreach (string f in fitted) c.FittedIds.Add(f);
        return c;
    }

    private static List<SiteStub> MakeSites()
    {
        return new List<SiteStub>
        {
            new(10, "Stilt Village", SiteKindTag.Settlement, 0, 0, Tier: 1, RegionId: 3),
            new(11, "Island Cache", SiteKindTag.FuelCache, 2000, 0, Tier: 1, RegionId: 3),
            new(12, "Reed Wreck", SiteKindTag.Wreck, -1500, 1000, Tier: 1, RegionId: 3),
            new(13, "Wet Farm", SiteKindTag.Farmstead, 800, -600, Tier: 1, RegionId: 3),
        };
    }
}
