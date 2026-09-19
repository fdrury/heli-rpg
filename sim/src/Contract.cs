namespace Rotorwash.Sim;

/// <summary>What kind of job this is. Each type has a different completion condition.</summary>
public enum ContractKind
{
    /// <summary>Take cargo (fuel/parts/medical) to a named site.</summary>
    Deliver,
    /// <summary>Visit and survey a site the player has not been to.</summary>
    Survey,
    /// <summary>Recover something from a wreck or depot.</summary>
    Recover,
    /// <summary>Carry a message between two settlements.</summary>
    Relay,
}

/// <summary>
/// One job from the contract board.
///
/// The contract board is the answer to "why should I fly there?" — the critical gap
/// identified by benchmark pass #2 (D-048). The model is Elite Dangerous / MSFS 2024 /
/// Far Cry 2: generated per-sortie purpose from the systems already built, not hundreds
/// of hand-authored quests.
///
/// Each contract references real sites by id, uses real stock types, and pays in things
/// the game already tracks (stock, standing, knowledge). Completion is checked against
/// Progress state — nothing new needs inventing.
/// </summary>
public sealed class Contract
{
    /// <summary>Unique id, generated from source site + clock. Stable across save/load.</summary>
    public string Id = "";

    public ContractKind Kind;

    /// <summary>Short imperative: "Deliver fuel to Cold Rise".</summary>
    public string Title = "";

    /// <summary>One or two sentences of context. Written like a person said it.</summary>
    public string Brief = "";

    /// <summary>Site id where the contract was posted.</summary>
    public int SourceSiteId;

    /// <summary>Site id the player must visit to complete the contract.</summary>
    public int TargetSiteId;

    /// <summary>Name of the target site, for display without a lookup.</summary>
    public string TargetName = "";

    // ---- completion conditions (kind-dependent) ----

    /// <summary>Deliver: what stock to bring. Survey/Recover: irrelevant.</summary>
    public Stock CargoKind;

    /// <summary>Deliver: how much to bring. Recover: how many searches.</summary>
    public double CargoAmount;

    // ---- rewards ----

    /// <summary>What stock the player receives on completion.</summary>
    public Stock RewardKind;
    public double RewardAmount;

    /// <summary>Standing change with the giver.</summary>
    public double StandingReward = 0.1;

    /// <summary>Knowledge granted on completion, if any.</summary>
    public string? RewardKnowledgeId;
    public string? RewardKnowledgeLabel;
    public string? RewardKnowledgeDetail;

    // ---- state ----

    /// <summary>World clock when this contract was posted.</summary>
    public double PostedAt;

    /// <summary>True once the player has accepted it.</summary>
    public bool Accepted;

    /// <summary>True once the completion condition has been met.</summary>
    public bool Completed;

    /// <summary>World clock when completed.</summary>
    public double CompletedAt;

    /// <summary>
    /// Check whether the contract is complete, given current progress.
    /// Returns true on the transition from incomplete to complete.
    /// </summary>
    public bool CheckCompletion(Progress progress)
    {
        if (Completed) return false;

        bool done = Kind switch
        {
            ContractKind.Deliver =>
                progress.HasVisited(TargetSiteId)
                && progress.Amount(CargoKind) >= CargoAmount,

            ContractKind.Survey =>
                progress.HasVisited(TargetSiteId),

            ContractKind.Recover =>
                progress.HasVisited(TargetSiteId)
                && progress.Record(TargetSiteId).SalvageRemaining >= 0
                && progress.Record(TargetSiteId).SalvageRemaining
                    < (int)CargoAmount, // they searched at least once

            ContractKind.Relay =>
                progress.HasVisited(TargetSiteId),

            _ => false,
        };

        if (!done) return false;

        Completed = true;
        CompletedAt = progress.Clock;
        return true;
    }

    /// <summary>Pay out the rewards.</summary>
    public void PayOut(Progress progress)
    {
        if (RewardAmount > 0)
            progress.Add(RewardKind, RewardAmount);

        if (RewardKnowledgeId is not null)
        {
            progress.Learn(new Knowledge(
                KnowledgeKind.Rumour,
                RewardKnowledgeId,
                RewardKnowledgeLabel ?? "",
                RewardKnowledgeDetail ?? ""));
        }
    }
}

/// <summary>
/// Generates contracts from site data and world state.
///
/// The design is deliberately simple: each settlement offers 2-3 contracts drawn from
/// its neighbours. The contracts reference real sites, use real distances, and pay in
/// real stock. Nothing is randomised at display time — the board is generated once when
/// the player arrives (or on a clock refresh) and stays stable.
///
/// Contract type distribution is weighted by what the world around the settlement
/// actually has: a settlement near wrecks offers recovery jobs; one near fuel caches
/// offers delivery jobs.
/// </summary>
public static class ContractBoard
{
    /// <summary>
    /// Generate the board for a settlement. Deterministic given the same inputs.
    /// </summary>
    /// <param name="sourceSiteId">The settlement posting the contracts.</param>
    /// <param name="sourceName">Name of the settlement.</param>
    /// <param name="sourcePos">Position of the settlement (world XZ, for distance calc).</param>
    /// <param name="sites">All sites in the world.</param>
    /// <param name="progress">Current player progress (to avoid offering completed work).</param>
    /// <param name="clock">Current world clock (for seeding and posting time).</param>
    /// <param name="boardCycle">How many times the board has refreshed (advances the seed).</param>
    public static List<Contract> Generate(
        int sourceSiteId,
        string sourceName,
        double sourceX, double sourceY,
        IReadOnlyList<SiteStub> sites,
        Progress progress,
        double clock,
        int boardCycle)
    {
        var contracts = new List<Contract>();
        var rng = new Random((sourceSiteId + 1) * 104729 + boardCycle * 999983 + 31);

        // Collect nearby sites by kind, sorted by distance.
        var nearby = new List<(SiteStub site, double dist)>();
        foreach (var s in sites)
        {
            if (s.Id == sourceSiteId) continue;
            double dx = s.X - sourceX;
            double dy = s.Y - sourceY;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 8000) continue; // beyond reasonable sortie range
            nearby.Add((s, dist));
        }
        nearby.Sort((a, b) => a.dist.CompareTo(b.dist));

        // Build candidate pool: try to offer one of each useful type, then fill.
        int target = Math.Min(3, Math.Max(2, nearby.Count / 8));
        var used = new HashSet<int>();

        // Delivery: send supplies to a fuel cache, farmstead, or settlement that needs them.
        TryAddDelivery(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);
        // Survey: visit somewhere unvisited.
        TryAddSurvey(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);
        // Recovery or relay based on what is around.
        if (rng.NextDouble() < 0.5)
            TryAddRecover(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);
        else
            TryAddRelay(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);

        // If we're short, try the other type.
        if (contracts.Count < target)
            TryAddRelay(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);
        if (contracts.Count < target)
            TryAddRecover(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);
        if (contracts.Count < target)
            TryAddDelivery(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress);

        return contracts;
    }

    private static void TryAddDelivery(
        List<Contract> contracts, Random rng,
        List<(SiteStub site, double dist)> nearby, HashSet<int> used,
        int sourceId, string sourceName, double clock, Progress progress)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (site.Kind is not (SiteKindTag.FuelCache or SiteKindTag.Settlement
                or SiteKindTag.Farmstead or SiteKindTag.Workshop)) continue;

            // Pick cargo and amount based on destination type.
            Stock cargo; double amount; string what;
            switch (site.Kind)
            {
                case SiteKindTag.FuelCache:
                    cargo = Stock.Fuel; amount = 1 + rng.Next(2); what = $"{amount} fuel can{(amount > 1 ? "s" : "")}";
                    break;
                case SiteKindTag.Workshop:
                    cargo = Stock.Parts; amount = 1 + rng.Next(2); what = $"{amount} part{(amount > 1 ? "s" : "")}";
                    break;
                default:
                    if (rng.NextDouble() < 0.5)
                    { cargo = Stock.Medical; amount = 1; what = "a medical kit"; }
                    else
                    { cargo = Stock.Food; amount = 2 + rng.Next(3); what = $"{amount} food"; }
                    break;
            }

            // Reward scales with distance.
            double reward = Math.Round(4 + dist / 400.0);

            var c = new Contract
            {
                Id = $"del.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Deliver,
                Title = $"Deliver {what} to {site.Name}",
                Brief = DeliveryBrief(rng, site.Name, what),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                CargoKind = cargo,
                CargoAmount = amount,
                RewardKind = Stock.Scrap,
                RewardAmount = reward,
                StandingReward = 0.1,
                PostedAt = clock,
            };
            contracts.Add(c);
            used.Add(site.Id);
            return;
        }
    }

    private static void TryAddSurvey(
        List<Contract> contracts, Random rng,
        List<(SiteStub site, double dist)> nearby, HashSet<int> used,
        int sourceId, string sourceName, double clock, Progress progress)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (progress.HasVisited(site.Id)) continue; // only unvisited sites

            var c = new Contract
            {
                Id = $"sur.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Survey,
                Title = $"Scout {site.Name}",
                Brief = SurveyBrief(rng, site.Name),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                RewardKind = Stock.Scrap,
                RewardAmount = Math.Round(6 + dist / 300.0),
                StandingReward = 0.15,
                PostedAt = clock,
            };

            // Survey contracts sometimes reward a rumour about another place.
            if (rng.NextDouble() < 0.4 && nearby.Count > 3)
            {
                var hint = nearby[rng.Next(Math.Min(nearby.Count, 8))];
                c.RewardKnowledgeId = $"site.{hint.site.Id}";
                c.RewardKnowledgeLabel = hint.site.Name;
                c.RewardKnowledgeDetail = $"Somebody at {site.Name} mentioned this place.";
            }

            contracts.Add(c);
            used.Add(site.Id);
            return;
        }
    }

    private static void TryAddRecover(
        List<Contract> contracts, Random rng,
        List<(SiteStub site, double dist)> nearby, HashSet<int> used,
        int sourceId, string sourceName, double clock, Progress progress)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (site.Kind is not (SiteKindTag.Wreck or SiteKindTag.Depot or SiteKindTag.Airfield)) continue;

            var c = new Contract
            {
                Id = $"rec.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Recover,
                Title = $"Search {site.Name}",
                Brief = RecoverBrief(rng, site.Name),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                CargoAmount = 1, // must search at least once
                RewardKind = Stock.Parts,
                RewardAmount = 1 + rng.Next(2),
                StandingReward = 0.1,
                PostedAt = clock,
            };
            contracts.Add(c);
            used.Add(site.Id);
            return;
        }
    }

    private static void TryAddRelay(
        List<Contract> contracts, Random rng,
        List<(SiteStub site, double dist)> nearby, HashSet<int> used,
        int sourceId, string sourceName, double clock, Progress progress)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (site.Kind != SiteKindTag.Settlement) continue;
            if (site.Id == sourceId) continue;

            var c = new Contract
            {
                Id = $"msg.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Relay,
                Title = $"Message to {site.Name}",
                Brief = RelayBrief(rng, sourceName, site.Name),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                RewardKind = Stock.Scrap,
                RewardAmount = Math.Round(5 + dist / 350.0),
                StandingReward = 0.15,
                PostedAt = clock,
                // Relay contracts often reward a contact.
                RewardKnowledgeId = $"contact.relay.{site.Id}",
                RewardKnowledgeLabel = $"Contact at {site.Name}",
                RewardKnowledgeDetail = $"Introduced by {sourceName}. They will remember you came.",
            };
            contracts.Add(c);
            used.Add(site.Id);
            return;
        }
    }

    // ---- brief text ----
    // Deliberately terse. These are overheard, not read off a quest-giver.

    private static readonly string[] DeliveryBriefs =
    {
        "{0} is short on {1}. They will pay if you can get it there.",
        "Nobody has been out to {0} in weeks. They need {1}.",
        "There are people at {0} who could use {1}. Scrap for your trouble.",
        "{1} to {0}. Not far, not safe. They pay in scrap.",
    };

    private static readonly string[] SurveyBriefs =
    {
        "Nobody has been to {0} since the road broke. Go look.",
        "We need to know what is at {0}. Fly over, come back, tell us.",
        "Someone swears {0} is still standing. Confirm it.",
        "{0} used to be worth the trip. Find out if it still is.",
    };

    private static readonly string[] RecoverBriefs =
    {
        "There is hardware at {0} that nobody has claimed. Get there first.",
        "{0} has not been stripped yet. Whatever is there is yours and ours.",
        "Something at {0} is worth having. Search the wreckage.",
    };

    private static readonly string[] RelayBriefs =
    {
        "{0} needs to hear from {1}. You are the only thing that flies.",
        "Carry word from here to {0}. Nobody else can get through.",
        "A message for {0}. Hand delivery — there is no other kind.",
    };

    private static string DeliveryBrief(Random rng, string target, string cargo) =>
        string.Format(DeliveryBriefs[rng.Next(DeliveryBriefs.Length)], target, cargo);

    private static string SurveyBrief(Random rng, string target) =>
        string.Format(SurveyBriefs[rng.Next(SurveyBriefs.Length)], target);

    private static string RecoverBrief(Random rng, string target) =>
        string.Format(RecoverBriefs[rng.Next(RecoverBriefs.Length)], target);

    private static string RelayBrief(Random rng, string source, string target) =>
        string.Format(RelayBriefs[rng.Next(RelayBriefs.Length)], target, source);
}

/// <summary>
/// Lightweight site descriptor for contract generation. Lives in sim/ so it has no
/// Godot dependency. The game layer maps real Site records into these.
/// </summary>
public sealed record SiteStub(int Id, string Name, SiteKindTag Kind, double X, double Y);

/// <summary>
/// Mirror of SiteKind for use in sim/. Avoids pulling Godot's Vector2 into the pure
/// .NET layer.
/// </summary>
public enum SiteKindTag
{
    FuelCache, Settlement, Workshop, Wreck, Relay, Depot, Airfield, Farmstead, Overlook,
}
