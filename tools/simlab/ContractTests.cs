using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for the contract and search systems: generation, completion, round-trip,
/// and search thread gating.
/// </summary>
public static class ContractTests
{
    /// <summary>Contract board generates 2-3 contracts for a settlement near other sites.</summary>
    public static string? Generation()
    {
        var progress = Progress.NewGame();
        var sites = MakeSites();

        var board = ContractBoard.Generate(
            sourceSiteId: 0, sourceName: "Test Settlement",
            sourceX: 0, sourceY: 0,
            sites, progress, clock: 7200, boardCycle: 0);

        if (board.Count < 1) return $"expected at least 1 contract, got {board.Count}";
        if (board.Count > 4) return $"expected at most 4 contracts, got {board.Count}";

        // Each contract should have an id, title, target
        foreach (var c in board)
        {
            if (string.IsNullOrEmpty(c.Id)) return "contract has empty id";
            if (string.IsNullOrEmpty(c.Title)) return "contract has empty title";
            if (string.IsNullOrEmpty(c.TargetName)) return "contract has empty target name";
            if (c.TargetSiteId == 0) return "contract targets the source site";
            if (c.RewardAmount <= 0) return "contract has no reward";
        }

        Console.WriteLine($"  Generated {board.Count} contracts:");
        foreach (var c in board)
            Console.WriteLine($"    [{c.Kind}] {c.Title}  →{c.TargetName}  +{c.RewardAmount} {c.RewardKind}");

        return null;
    }

    /// <summary>Contract completion triggers when conditions are met.</summary>
    public static string? Completion()
    {
        var progress = Progress.NewGame();

        var c = new Contract
        {
            Id = "test.survey.1",
            Kind = ContractKind.Survey,
            Title = "Scout Far Place",
            Brief = "Go look.",
            SourceSiteId = 0,
            TargetSiteId = 5,
            TargetName = "Far Place",
            RewardKind = Stock.Scrap,
            RewardAmount = 10,
            StandingReward = 0.15,
            PostedAt = progress.Clock,
            Accepted = true,
        };

        // Not yet visited → should not complete
        if (c.CheckCompletion(progress)) return "completed before visiting";

        // Visit the target
        progress.MarkVisited(5);

        // Now it should complete
        if (!c.CheckCompletion(progress)) return "did not complete after visiting";
        if (!c.Completed) return "not marked completed";

        // Double-check it does not complete twice
        if (c.CheckCompletion(progress)) return "completed twice";

        return null;
    }

    /// <summary>Contract payout adds stock and knowledge.</summary>
    public static string? Payout()
    {
        var progress = Progress.NewGame();
        double scrapBefore = progress.Amount(Stock.Scrap);

        var c = new Contract
        {
            Id = "test.pay.1",
            Kind = ContractKind.Survey,
            Title = "Scout Far Place",
            Brief = "Go look.",
            TargetSiteId = 5,
            TargetName = "Far Place",
            RewardKind = Stock.Scrap,
            RewardAmount = 10,
            StandingReward = 0.15,
            RewardKnowledgeId = "site.99",
            RewardKnowledgeLabel = "Distant Place",
            RewardKnowledgeDetail = "Somebody mentioned it.",
            Accepted = true,
        };

        c.PayOut(progress);

        double scrapAfter = progress.Amount(Stock.Scrap);
        if (Math.Abs(scrapAfter - scrapBefore - 10) > 0.01)
            return $"scrap: expected +10, got {scrapAfter - scrapBefore}";

        if (!progress.Knows("site.99")) return "knowledge not granted";

        return null;
    }

    /// <summary>Delivery contract requires cargo at destination.</summary>
    public static string? DeliveryCompletion()
    {
        var progress = Progress.NewGame();

        var c = new Contract
        {
            Id = "test.del.1",
            Kind = ContractKind.Deliver,
            Title = "Deliver fuel",
            Brief = "Bring fuel.",
            TargetSiteId = 3,
            TargetName = "Fuel Spot",
            CargoKind = Stock.Fuel,
            CargoAmount = 2,
            RewardKind = Stock.Scrap,
            RewardAmount = 8,
            Accepted = true,
        };

        // Visit but no fuel
        progress.MarkVisited(3);
        if (c.CheckCompletion(progress)) return "completed without cargo";

        // Add fuel
        progress.Add(Stock.Fuel, 2);
        if (!c.CheckCompletion(progress)) return "did not complete with cargo";

        return null;
    }

    /// <summary>Contracts survive save/load round trip.</summary>
    public static string? RoundTrip()
    {
        var progress = Progress.NewGame();
        progress.Clock = 50000;

        var c = new Contract
        {
            Id = "test.rt.1",
            Kind = ContractKind.Relay,
            Title = "Message to Stone Yard",
            Brief = "Carry word.",
            SourceSiteId = 0,
            TargetSiteId = 7,
            TargetName = "Stone Yard",
            RewardKind = Stock.Scrap,
            RewardAmount = 12,
            StandingReward = 0.15,
            RewardKnowledgeId = "contact.relay.7",
            RewardKnowledgeLabel = "Contact at Stone Yard",
            RewardKnowledgeDetail = "They will remember you came.",
            PostedAt = 48000,
            Accepted = true,
        };
        progress.AcceptContract(c);

        // Also advance the search thread
        progress.Search.Stage = 3;
        progress.ContractsCompleted = 5;

        var save = new SaveData();
        save.CaptureProgress(progress);

        string json = save.ToJson();
        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var p2 = loaded.ApplyProgress();

        // Check contracts
        if (p2.Contracts.Count != 1) return $"contract count: {p2.Contracts.Count}";
        var c2 = p2.Contracts[0];
        if (c2.Id != "test.rt.1") return $"id: {c2.Id}";
        if (c2.Kind != ContractKind.Relay) return $"kind: {c2.Kind}";
        if (c2.Title != "Message to Stone Yard") return $"title: {c2.Title}";
        if (c2.TargetSiteId != 7) return $"target: {c2.TargetSiteId}";
        if (c2.TargetName != "Stone Yard") return $"targetName: {c2.TargetName}";
        if (Math.Abs(c2.RewardAmount - 12) > 0.01) return $"reward: {c2.RewardAmount}";
        if (c2.RewardKnowledgeId != "contact.relay.7") return $"knowledge: {c2.RewardKnowledgeId}";
        if (!c2.Accepted) return "not accepted after load";

        // Check search stage
        if (p2.Search.Stage != 3) return $"search stage: {p2.Search.Stage}";
        if (p2.ContractsCompleted != 5) return $"completed count: {p2.ContractsCompleted}";

        return null;
    }

    /// <summary>Search thread advances when gates are met.</summary>
    public static string? SearchAdvance()
    {
        var progress = Progress.NewGame();

        // Stage 0 gate: VisitedCount >= 2
        if (progress.Search.TryAdvance(progress) is not null)
            return "search advanced too early";

        progress.MarkVisited(1);
        progress.MarkVisited(2);

        var beat = progress.Search.TryAdvance(progress);
        if (beat is null) return "search did not advance at stage 0";
        if (beat.Name != "The correction") return $"wrong beat: {beat.Name}";
        if (progress.Search.Stage != 1) return $"stage: {progress.Search.Stage}";

        // Stage 1 gate: VisitedCount >= 4 AND has a frequency
        progress.MarkVisited(3);
        progress.MarkVisited(4);
        if (progress.Search.TryAdvance(progress) is not null)
            return "advanced without frequency";

        progress.Learn(new Knowledge(KnowledgeKind.Frequency, "freq.test", "Test Freq", "test"));
        beat = progress.Search.TryAdvance(progress);
        if (beat is null) return "search did not advance at stage 1";
        if (progress.Search.Stage != 2) return $"stage after 1: {progress.Search.Stage}";

        Console.WriteLine($"  Search stage: {progress.Search.Stage}");
        Console.WriteLine($"  Current hint: {progress.Search.CurrentHint}");

        return null;
    }

    /// <summary>Board is deterministic for the same inputs.</summary>
    public static string? Determinism()
    {
        var progress = Progress.NewGame();
        var sites = MakeSites();

        var board1 = ContractBoard.Generate(0, "Test", 0, 0, sites, progress, 7200, 0);
        var board2 = ContractBoard.Generate(0, "Test", 0, 0, sites, progress, 7200, 0);

        if (board1.Count != board2.Count) return $"count differs: {board1.Count} vs {board2.Count}";

        for (int i = 0; i < board1.Count; i++)
        {
            if (board1[i].Id != board2[i].Id)
                return $"id[{i}] differs: {board1[i].Id} vs {board2[i].Id}";
            if (board1[i].Title != board2[i].Title)
                return $"title[{i}] differs";
        }

        // Different cycle should produce different contracts
        var board3 = ContractBoard.Generate(0, "Test", 0, 0, sites, progress, 7200, 1);
        bool anyDifferent = false;
        if (board3.Count != board1.Count) anyDifferent = true;
        else
            for (int i = 0; i < board1.Count; i++)
                if (board1[i].Id != board3[i].Id) { anyDifferent = true; break; }

        if (!anyDifferent) return "different cycle produced identical contracts";

        return null;
    }

    /// <summary>AcceptContract and CheckContracts work through Progress.</summary>
    public static string? ProgressIntegration()
    {
        var progress = Progress.NewGame();

        var c = new Contract
        {
            Id = "test.int.1",
            Kind = ContractKind.Survey,
            Title = "Scout Test Site",
            Brief = "Go look.",
            TargetSiteId = 10,
            TargetName = "Test Site",
            RewardKind = Stock.Scrap,
            RewardAmount = 8,
        };

        progress.AcceptContract(c);
        if (progress.Contracts.Count != 1) return $"count after accept: {progress.Contracts.Count}";

        int activeCount = 0;
        foreach (var _ in progress.ActiveContracts) activeCount++;
        if (activeCount != 1) return $"active count: {activeCount}";

        // Not yet completed
        var completed = progress.CheckContracts();
        if (completed.Count != 0) return $"completed too early: {completed.Count}";

        // Visit the target
        progress.MarkVisited(10);
        completed = progress.CheckContracts();
        if (completed.Count != 1) return $"did not complete: {completed.Count}";
        if (progress.ContractsCompleted != 1) return $"completed count: {progress.ContractsCompleted}";

        // Scrap should have been paid
        // NewGame gives 12 scrap; +8 from contract
        if (Math.Abs(progress.Amount(Stock.Scrap) - 20) > 0.01)
            return $"scrap after payout: {progress.Amount(Stock.Scrap)}";

        return null;
    }

    // ---- helpers ----

    private static List<SiteStub> MakeSites()
    {
        return new List<SiteStub>
        {
            new(0, "Test Settlement", SiteKindTag.Settlement, 0, 0),
            new(1, "North Cache", SiteKindTag.FuelCache, 1500, 0),
            new(2, "East Wreck", SiteKindTag.Wreck, 2000, 1000),
            new(3, "South Workshop", SiteKindTag.Workshop, 0, 2500),
            new(4, "Far Settlement", SiteKindTag.Settlement, -3000, 1000),
            new(5, "Relay Hill", SiteKindTag.Relay, 1000, -2000),
            new(6, "Depot", SiteKindTag.Depot, -1500, -1500),
            new(7, "Airfield", SiteKindTag.Airfield, 4000, 3000),
            new(8, "Farm", SiteKindTag.Farmstead, 800, 500),
            new(9, "Overlook", SiteKindTag.Overlook, -500, -3000),
        };
    }
}
