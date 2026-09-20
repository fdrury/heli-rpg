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

    /// <summary>Search thread advances when gates are met, including place checks.</summary>
    public static string? SearchAdvance()
    {
        var progress = Progress.NewGame();

        // A context with all roles "visited" so place gates are satisfied.
        // This isolates the test to counter and knowledge gates.
        var allRoles = new HashSet<string>
        {
            "place.mattie", "place.doss", "place.long_mast", "place.nell",
            "place.field", "place.bel", "place.wreck", "place.osie",
            "place.cairn", "place.ferren", "place.saw_relay", "place.wray",
            "place.juno", "place.tether", "place.sparrow", "place.magazine",
        };
        var ctx = new ThreadContext(0, false, -1, false, allRoles);

        // Stage 0 gate: VisitedCount >= 2 AND visited Mattie
        if (progress.Search.TryAdvance(progress, ctx) is not null)
            return "search advanced too early";

        progress.MarkVisited(1);
        progress.MarkVisited(2);

        var beat = progress.Search.TryAdvance(progress, ctx);
        if (beat is null) return "search did not advance at stage 0";
        if (beat.Name != "The correction") return $"wrong beat: {beat.Name}";
        if (progress.Search.Stage != 1) return $"stage: {progress.Search.Stage}";

        // Stage 1 gate: VisitedCount >= 4 AND has a frequency AND visited Long Acre mast
        progress.MarkVisited(3);
        progress.MarkVisited(4);
        if (progress.Search.TryAdvance(progress, ctx) is not null)
            return "advanced without frequency";

        progress.Learn(new Knowledge(KnowledgeKind.Frequency, "freq.test", "Test Freq", "test"));
        beat = progress.Search.TryAdvance(progress, ctx);
        if (beat is null) return "search did not advance at stage 1";
        if (progress.Search.Stage != 2) return $"stage after 1: {progress.Search.Stage}";

        // Verify place gates matter: without the role, the beat should not fire.
        var progress2 = Progress.NewGame();
        var emptyCtx = new ThreadContext(0, false, -1, false, new HashSet<string>());
        progress2.MarkVisited(1);
        progress2.MarkVisited(2);
        if (progress2.Search.TryAdvance(progress2, emptyCtx) is not null)
            return "search advanced without visited role — place gate broken";

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

        // Different cycle should produce different contracts. With limited test sites
        // the IDs can match (same eligible targets), so also compare titles and briefs
        // which include RNG-dependent text (cargo amounts, brief selection).
        bool anyDifferent = false;
        for (int cycle = 1; cycle <= 10 && !anyDifferent; cycle++)
        {
            var other = ContractBoard.Generate(0, "Test", 0, 0, sites, progress, 7200, cycle);
            if (other.Count != board1.Count) { anyDifferent = true; break; }
            for (int i = 0; i < board1.Count; i++)
            {
                if (board1[i].Id != other[i].Id
                    || board1[i].Title != other[i].Title
                    || board1[i].Brief != other[i].Brief)
                { anyDifferent = true; break; }
            }
        }

        if (!anyDifferent) return "10 different cycles all produced identical contracts";

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

    /// <summary>Clear contract generates for hostile sites and completes on clear.</summary>
    public static string? ClearContract()
    {
        var progress = Progress.NewGame();
        var sites = MakeSites();

        // Find a hostile site among our test sites.
        int hostileId = -1;
        foreach (var s in sites)
        {
            if (Encounter.IsHostile(s.Id, (int)s.Kind, s.Tier) && !progress.Record(s.Id).Cleared)
            {
                hostileId = s.Id;
                break;
            }
        }

        if (hostileId == -1)
            return null; // no hostile site in test data — skip, not a failure

        // Generate board — look for a Clear contract across several cycles.
        Contract? clearContract = null;
        for (int cycle = 0; cycle < 20 && clearContract is null; cycle++)
        {
            var board = ContractBoard.Generate(0, "Test Settlement", 0, 0,
                sites, progress, 7200, cycle);
            foreach (var c in board)
                if (c.Kind == ContractKind.Clear) { clearContract = c; break; }
        }

        if (clearContract is null)
            return "no Clear contract generated in 20 cycles";

        Console.WriteLine($"  Clear contract: {clearContract.Title} → {clearContract.TargetName}");
        Console.WriteLine($"  Reward: +{clearContract.RewardAmount} {clearContract.RewardKind}");

        // Should not complete before clearing
        clearContract.Accepted = true;
        if (clearContract.CheckCompletion(progress))
            return "completed before clearing";

        // Clear the site
        progress.Record(clearContract.TargetSiteId).Cleared = true;

        // Now it should complete
        if (!clearContract.CheckCompletion(progress))
            return "did not complete after clearing";

        return null;
    }

    /// <summary>Danger pay scales rewards when alert is raised.</summary>
    public static string? DangerPay()
    {
        var progress = Progress.NewGame();
        var sites = MakeSites();

        // Generate with no alert — baseline rewards.
        var baseline = ContractBoard.Generate(0, "Test Settlement", 0, 0,
            sites, progress, 7200, 0, alert: null);
        if (baseline.Count == 0) return "no baseline contracts generated";

        // Generate with a hot region — rewards should be higher.
        var alert = new AlertState();
        // Set region 1 (where most test sites live) to near-full readiness.
        alert.Set(1, 0.9);

        var hot = ContractBoard.Generate(0, "Test Settlement", 0, 0,
            sites, progress, 7200, 0, alert: alert);
        if (hot.Count == 0) return "no alert contracts generated";

        double baseTotal = 0, hotTotal = 0;
        foreach (var c in baseline) baseTotal += c.RewardAmount;
        foreach (var c in hot) hotTotal += c.RewardAmount;

        Console.WriteLine($"  Baseline total reward: {baseTotal:F0}");
        Console.WriteLine($"  Alert total reward: {hotTotal:F0}");

        // At least some contracts should pay more when alert is raised.
        if (hotTotal <= baseTotal)
            return $"alert rewards ({hotTotal:F0}) not higher than baseline ({baseTotal:F0})";

        return null;
    }

    /// <summary>Recovery contracts note hostile guards and pay extra.</summary>
    public static string? RecoverHostile()
    {
        var progress = Progress.NewGame();
        var sites = MakeSites();

        // Find a recovery contract targeting a hostile site.
        Contract? guardedRecovery = null;
        for (int cycle = 0; cycle < 20 && guardedRecovery is null; cycle++)
        {
            var board = ContractBoard.Generate(0, "Test Settlement", 0, 0,
                sites, progress, 7200, cycle);
            foreach (var c in board)
            {
                if (c.Kind != ContractKind.Recover) continue;
                var target = FindSite(sites, c.TargetSiteId);
                if (target is null) continue;
                if (Encounter.IsHostile(target.Id, (int)target.Kind, target.Tier))
                {
                    guardedRecovery = c;
                    break;
                }
            }
        }

        if (guardedRecovery is null)
            return null; // no hostile recovery in test data — skip

        Console.WriteLine($"  Guarded recovery: {guardedRecovery.Title}");
        Console.WriteLine($"  Brief: {guardedRecovery.Brief}");

        // The brief should mention the guards.
        bool mentionsGuards = guardedRecovery.Brief.Contains("guarding")
                           || guardedRecovery.Brief.Contains("sitting on")
                           || guardedRecovery.Brief.Contains("keeping everyone");
        if (!mentionsGuards)
            return "guarded recovery brief does not mention hostiles";

        return null;
    }

    /// <summary>Clear contract round-trips through save/load.</summary>
    public static string? ClearRoundTrip()
    {
        var progress = Progress.NewGame();
        progress.Clock = 50000;

        var c = new Contract
        {
            Id = "test.clr.1",
            Kind = ContractKind.Clear,
            Title = "Clear East Wreck",
            Brief = "Scavengers at East Wreck. Deal with them.",
            SourceSiteId = 0,
            TargetSiteId = 2,
            TargetName = "East Wreck",
            RewardKind = Stock.Scrap,
            RewardAmount = 15,
            StandingReward = 0.2,
            RewardKnowledgeId = "site.cleared.2",
            RewardKnowledgeLabel = "East Wreck",
            RewardKnowledgeDetail = "Cleared.",
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
        if (c2.Kind != ContractKind.Clear) return $"kind: {c2.Kind}";
        if (c2.Title != "Clear East Wreck") return $"title: {c2.Title}";
        if (c2.TargetSiteId != 2) return $"target: {c2.TargetSiteId}";
        if (c2.RewardKnowledgeId != "site.cleared.2") return $"knowledge: {c2.RewardKnowledgeId}";

        return null;
    }

    // ---- helpers ----

    private static List<SiteStub> MakeSites()
    {
        // Tier and RegionId now populated for encounter and alert awareness.
        return new List<SiteStub>
        {
            new(0, "Test Settlement", SiteKindTag.Settlement, 0, 0, Tier: 0, RegionId: 0),
            new(1, "North Cache", SiteKindTag.FuelCache, 1500, 0, Tier: 1, RegionId: 1),
            new(2, "East Wreck", SiteKindTag.Wreck, 2000, 1000, Tier: 2, RegionId: 1),
            new(3, "South Workshop", SiteKindTag.Workshop, 0, 2500, Tier: 1, RegionId: 1),
            new(4, "Far Settlement", SiteKindTag.Settlement, -3000, 1000, Tier: 1, RegionId: 2),
            new(5, "Relay Hill", SiteKindTag.Relay, 1000, -2000, Tier: 1, RegionId: 1),
            new(6, "Depot", SiteKindTag.Depot, -1500, -1500, Tier: 2, RegionId: 2),
            new(7, "Airfield", SiteKindTag.Airfield, 4000, 3000, Tier: 2, RegionId: 3),
            new(8, "Farm", SiteKindTag.Farmstead, 800, 500, Tier: 2, RegionId: 1),
            new(9, "Overlook", SiteKindTag.Overlook, -500, -3000, Tier: 1, RegionId: 1),
        };
    }

    private static SiteStub? FindSite(IReadOnlyList<SiteStub> list, int id)
    {
        foreach (var s in list) if (s.Id == id) return s;
        return null;
    }
}
