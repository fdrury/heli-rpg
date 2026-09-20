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
    /// <summary>Clear hostile scavengers from a site so it can be used.</summary>
    Clear,
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

            ContractKind.Clear =>
                progress.Record(TargetSiteId).Cleared,

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
///
/// Since D-058, contracts are aware of the encounter and alert systems:
///   - Clear contracts ask the player to remove scavengers from hostile sites.
///   - Recovery contracts to hostile sites note the guards and pay more.
///   - All contracts to alert regions carry danger pay (up to 2x at full readiness).
///   - Brief text mentions whether an area is being watched.
/// </summary>
public static class ContractBoard
{
    /// <summary>
    /// Generate the board for a settlement. Deterministic given the same inputs.
    /// </summary>
    /// <param name="sourceSiteId">The settlement posting the contracts.</param>
    /// <param name="sourceName">Name of the settlement.</param>
    /// <param name="sourceX">X position of the settlement (world XZ).</param>
    /// <param name="sourceY">Y position of the settlement (world XZ).</param>
    /// <param name="sites">All sites in the world.</param>
    /// <param name="progress">Current player progress (to avoid offering completed work).</param>
    /// <param name="clock">Current world clock (for seeding and posting time).</param>
    /// <param name="boardCycle">How many times the board has refreshed (advances the seed).</param>
    /// <param name="alert">Regional readiness state, if available. Null is fine — contracts
    /// generate without danger pay, which is exactly what happens on a new game before
    /// anyone has been detected.</param>
    public static List<Contract> Generate(
        int sourceSiteId,
        string sourceName,
        double sourceX, double sourceY,
        IReadOnlyList<SiteStub> sites,
        Progress progress,
        double clock,
        int boardCycle,
        AlertState? alert = null)
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
        TryAddDelivery(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
        // Survey: visit somewhere unvisited.
        TryAddSurvey(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);

        // Clear a hostile site, or recovery/relay based on what is around.
        bool triedClear = false;
        if (rng.NextDouble() < 0.4)
        {
            TryAddClear(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
            triedClear = true;
        }

        if (contracts.Count < target)
        {
            if (rng.NextDouble() < 0.5)
                TryAddRecover(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
            else
                TryAddRelay(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
        }

        // If we're short, try the other type.
        if (contracts.Count < target)
            TryAddRelay(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
        if (contracts.Count < target)
            TryAddRecover(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
        if (contracts.Count < target && !triedClear)
            TryAddClear(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);
        if (contracts.Count < target)
            TryAddDelivery(contracts, rng, nearby, used, sourceSiteId, sourceName, clock, progress, alert);

        return contracts;
    }

    // ----------------------------------------------------------------- danger pay
    //
    // Flying into a region where they are expecting you is harder than flying into one
    // that is quiet. The reward should reflect that, or the player stops taking contracts
    // to hot regions and the board stagnates. The multiplier is 1.0 in a quiet region and
    // up to 2.0 at full readiness — enough to notice, not enough to farm.

    private static double DangerPay(AlertState? alert, int regionId)
    {
        if (alert is null) return 1.0;
        return 1.0 + alert.Level(regionId);
    }

    /// <summary>A terse suffix about the alert state. Appended to briefs when relevant.</summary>
    private static string AlertSuffix(AlertState? alert, int regionId)
    {
        if (alert is null) return "";
        double level = alert.Level(regionId);
        if (level < 0.15) return "";
        if (level < 0.45) return " Word is, somebody saw something out that way.";
        if (level < 0.75) return " That area is being watched. Fly careful.";
        return " They are expecting company out there. Pay reflects it.";
    }

    private static void TryAddDelivery(
        List<Contract> contracts, Random rng,
        List<(SiteStub site, double dist)> nearby, HashSet<int> used,
        int sourceId, string sourceName, double clock, Progress progress,
        AlertState? alert)
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

            // Reward scales with distance, multiplied by danger pay.
            double reward = Math.Round((4 + dist / 400.0) * DangerPay(alert, site.RegionId));

            var c = new Contract
            {
                Id = $"del.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Deliver,
                Title = $"Deliver {what} to {site.Name}",
                Brief = DeliveryBrief(rng, site.Name, what) + AlertSuffix(alert, site.RegionId),
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
        int sourceId, string sourceName, double clock, Progress progress,
        AlertState? alert)
    {
        // Prefer sites in quieter regions: if someone is watching, a scout is risky for
        // little gain. A quiet region is a window of opportunity.
        var candidates = new List<(SiteStub site, double dist)>();
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (progress.HasVisited(site.Id)) continue;
            candidates.Add((site, dist));
        }

        // Sort: quiet regions first, then by distance. The quiet preference is subtle —
        // it changes which unvisited site the board picks, not whether it offers a survey.
        if (alert is not null && candidates.Count > 1)
        {
            candidates.Sort((a, b) =>
            {
                double aScore = a.dist + alert.Level(a.site.RegionId) * 4000;
                double bScore = b.dist + alert.Level(b.site.RegionId) * 4000;
                return aScore.CompareTo(bScore);
            });
        }

        foreach (var (site, dist) in candidates)
        {
            double reward = Math.Round((6 + dist / 300.0) * DangerPay(alert, site.RegionId));

            var c = new Contract
            {
                Id = $"sur.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Survey,
                Title = $"Scout {site.Name}",
                Brief = SurveyBrief(rng, site.Name) + AlertSuffix(alert, site.RegionId),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                RewardKind = Stock.Scrap,
                RewardAmount = reward,
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
        int sourceId, string sourceName, double clock, Progress progress,
        AlertState? alert)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (site.Kind is not (SiteKindTag.Wreck or SiteKindTag.Depot or SiteKindTag.Airfield)) continue;

            bool hostile = Encounter.IsHostile(site.Id, (int)site.Kind, site.Tier);
            bool cleared = progress.Record(site.Id).Cleared;

            // Hostile site that hasn't been cleared: use the guarded brief and pay more.
            string brief = (hostile && !cleared)
                ? RecoverGuardedBrief(rng, site.Name)
                : RecoverBrief(rng, site.Name);
            brief += AlertSuffix(alert, site.RegionId);

            double baseReward = 1 + rng.Next(2);
            if (hostile && !cleared) baseReward += 1; // extra for the fight
            baseReward = Math.Round(baseReward * DangerPay(alert, site.RegionId));

            var c = new Contract
            {
                Id = $"rec.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Recover,
                Title = $"Search {site.Name}",
                Brief = brief,
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                CargoAmount = 1, // must search at least once
                RewardKind = Stock.Parts,
                RewardAmount = baseReward,
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
        int sourceId, string sourceName, double clock, Progress progress,
        AlertState? alert)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;
            if (site.Kind != SiteKindTag.Settlement) continue;
            if (site.Id == sourceId) continue;

            double reward = Math.Round((5 + dist / 350.0) * DangerPay(alert, site.RegionId));

            var c = new Contract
            {
                Id = $"msg.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Relay,
                Title = $"Message to {site.Name}",
                Brief = RelayBrief(rng, sourceName, site.Name) + AlertSuffix(alert, site.RegionId),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                RewardKind = Stock.Scrap,
                RewardAmount = reward,
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

    private static void TryAddClear(
        List<Contract> contracts, Random rng,
        List<(SiteStub site, double dist)> nearby, HashSet<int> used,
        int sourceId, string sourceName, double clock, Progress progress,
        AlertState? alert)
    {
        foreach (var (site, dist) in nearby)
        {
            if (used.Contains(site.Id)) continue;

            // Only sites that are actually hostile and not yet cleared.
            if (!Encounter.IsHostile(site.Id, (int)site.Kind, site.Tier)) continue;
            if (progress.Record(site.Id).Cleared) continue;

            int npcCount = Encounter.NpcCount(site.Id, (int)site.Kind);
            double reward = Math.Round((8 + dist / 300.0 + npcCount * 2)
                                       * DangerPay(alert, site.RegionId));

            var c = new Contract
            {
                Id = $"clr.{sourceId}.{site.Id}.{clock:F0}",
                Kind = ContractKind.Clear,
                Title = $"Clear {site.Name}",
                Brief = ClearBrief(rng, site.Name, npcCount) + AlertSuffix(alert, site.RegionId),
                SourceSiteId = sourceId,
                TargetSiteId = site.Id,
                TargetName = site.Name,
                RewardKind = Stock.Scrap,
                RewardAmount = reward,
                StandingReward = 0.2,
                PostedAt = clock,
                // Clearing a site reveals what was there all along.
                RewardKnowledgeId = $"site.cleared.{site.Id}",
                RewardKnowledgeLabel = site.Name,
                RewardKnowledgeDetail = $"Cleared. Whatever is there is accessible now.",
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

    private static readonly string[] RecoverGuardedBriefs =
    {
        "There is hardware at {0}, but people are sitting on it. Clear them out, then search.",
        "{0} has not been stripped because somebody is guarding it. Deal with them first.",
        "Scavengers at {0} are keeping everyone out. Remove them and take what is there.",
    };

    private static readonly string[] RelayBriefs =
    {
        "{0} needs to hear from {1}. You are the only thing that flies.",
        "Carry word from here to {0}. Nobody else can get through.",
        "A message for {0}. Hand delivery — there is no other kind.",
    };

    private static readonly string[] ClearBriefs =
    {
        "{0} has scavengers dug in — {1} at least. Clear them and the place is open.",
        "Armed people at {0}. Roughly {1}. Nobody gets in until they are gone.",
        "We need {0} accessible. That means dealing with the {1} who have claimed it.",
    };

    private static string DeliveryBrief(Random rng, string target, string cargo) =>
        string.Format(DeliveryBriefs[rng.Next(DeliveryBriefs.Length)], target, cargo);

    private static string SurveyBrief(Random rng, string target) =>
        string.Format(SurveyBriefs[rng.Next(SurveyBriefs.Length)], target);

    private static string RecoverBrief(Random rng, string target) =>
        string.Format(RecoverBriefs[rng.Next(RecoverBriefs.Length)], target);

    private static string RecoverGuardedBrief(Random rng, string target) =>
        string.Format(RecoverGuardedBriefs[rng.Next(RecoverGuardedBriefs.Length)], target);

    private static string RelayBrief(Random rng, string source, string target) =>
        string.Format(RelayBriefs[rng.Next(RelayBriefs.Length)], target, source);

    private static string ClearBrief(Random rng, string target, int npcCount) =>
        string.Format(ClearBriefs[rng.Next(ClearBriefs.Length)], target, npcCount);
}

/// <summary>
/// Lightweight site descriptor for contract generation. Lives in sim/ so it has no
/// Godot dependency. The game layer maps real Site records into these.
/// </summary>
public sealed record SiteStub(int Id, string Name, SiteKindTag Kind, double X, double Y, int Tier = 0, int RegionId = 0);

/// <summary>
/// Mirror of SiteKind for use in sim/. Avoids pulling Godot's Vector2 into the pure
/// .NET layer.
/// </summary>
public enum SiteKindTag
{
    FuelCache, Settlement, Workshop, Wreck, Relay, Depot, Airfield, Farmstead, Overlook,
}
