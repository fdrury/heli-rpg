using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rotorwash.Sim;

/// <summary>
/// Everything that goes into and comes out of a save file.
///
/// Pure data — no Godot dependency. The game layer captures aircraft position and NPC
/// state; everything else is handled here. Save only happens when the aircraft is parked
/// and shut down, so rotor/engine internal state is not needed — on load the aircraft is
/// placed with the engine off and the rotor stopped.
/// </summary>
public sealed class SaveData
{
    public int Version { get; set; } = 1;

    // ------------------------------------------------------------- slot header
    //
    // Four fields that exist only so a save can be DESCRIBED without being loaded. A slot
    // list that says "slot 3" and nothing else asks the player to remember what they were
    // doing three sessions ago, which nobody can; a slot list that says where they were and
    // when tells them instantly which one they want. Written at capture, read by the menu,
    // and ignored by everything else.

    /// <summary>Real-world time the save was written, ISO 8601. For "which is the newest".</summary>
    public string SavedAtUtc { get; set; } = "";

    /// <summary>Where the aircraft was parked. The single most useful word in the list.</summary>
    public string PlaceName { get; set; } = "";

    /// <summary>Hours on the airframe. The clock the whole campaign runs against (D-086).</summary>
    public double FlightHours { get; set; }

    // ---- aircraft ----
    public double North { get; set; }
    public double East { get; set; }
    public double Altitude { get; set; }
    public double HeadingRad { get; set; }
    public double Fuel { get; set; }

    // ---- damage ----
    public double[] ComponentHealth { get; set; } = Array.Empty<double>();
    public List<DamageEventSave> DamageLog { get; set; } = new();

    // ---- loadout ----
    public List<string> Installed { get; set; } = new();
    public List<string> Bag { get; set; } = new();

    // ---- progress ----
    public double Clock { get; set; }
    public Dictionary<string, double> Inventory { get; set; } = new();
    public List<KnowledgeSave> Knowledge { get; set; } = new();
    public Dictionary<string, SiteRecordSave> Sites { get; set; } = new();
    public List<string> Journal { get; set; } = new();

    /// <summary>
    /// Salvaged components aboard but not fitted.
    ///
    /// Only the id and the condition are stored. Everything else about a part - its mass,
    /// what it fits, what it is worth - comes from <c>Salvage.Catalog</c>, which is code,
    /// so retuning a part's mass retunes every save rather than only new ones. A part id
    /// that has since left the catalog is dropped on load by <c>Cargo.Restore</c>: a save
    /// that silently sheds a part is bad, and a save that will not load at all is worse.
    /// </summary>
    public List<CargoPartSave> Cargo { get; set; } = new();

    // ---- NPCs ----
    public List<NpcSave> Npcs { get; set; } = new();

    // ---- combat ----
    public float PilotHealth { get; set; } = 100;
    public int SidearmRounds { get; set; } = 6;
    public int SidearmSpare { get; set; } = 12;
    public float RotorTimeCharge { get; set; }

    // ---- gun pod (D-081) ----
    public int GunRounds { get; set; } = 200;

    // ---- threats ----
    public int ChaffRemaining { get; set; }
    public int FlaresRemaining { get; set; }
    public List<int> DetectedEmitters { get; set; } = new();

    /// <summary>
    /// Emitter health for emitters damaged or destroyed by gunfire (D-081).
    /// Keyed by emitter id. Only emitters below 1.0 are stored; a missing entry
    /// means full health. A save written before this field existed loads with all
    /// emitters intact, which is correct.
    /// </summary>
    public Dictionary<int, double> EmitterHealth { get; set; } = new();

    /// <summary>
    /// Regional readiness levels that outlive the sortie. Keyed by region ordinal
    /// (RegionKind cast to int); values are 0 to 1. Regions that have calmed below 1e-4
    /// are omitted. A save written before this field existed will load with all regions
    /// quiet, which is correct — alert is the kind of state where absence is the default.
    /// </summary>
    public Dictionary<string, double> AlertLevels { get; set; } = new();

    // ---- fog of war ----
    public byte[]? FogGrid { get; set; }

    // ---- contracts ----
    public List<ContractSave> Contracts { get; set; } = new();

    /// <summary>
    /// What the district is still talking about (D-074).
    ///
    /// Saved because the announcer bringing up a delivery from before you last quit is most
    /// of what separates him from a session-local effect. `Progress` prunes past the
    /// callback window, so this list stays short whatever the length of the campaign.
    /// </summary>
    public List<DeedSave> Deeds { get; set; } = new();

    /// <summary>What the player has knocked down, and how far it has got back up.</summary>
    public List<SiteDamageSave> Damage { get; set; } = new();
    public int ContractsCompleted { get; set; }
    public int SearchStage { get; set; }

    /// <summary>
    /// Game clock (seconds) when each of the four ferry-route legs closed.
    /// A zero means that leg is still open. A save written before this field
    /// existed loads with all legs open, which is correct.
    /// </summary>
    public List<double> LegClosedAt { get; set; } = new();

    /// <summary>
    /// Total flight hours since the rotor was last tracked. Persisted so the
    /// kneeboard THREAD page can show "N h since track" after a save/load cycle.
    /// A save written before this field existed loads as 0, which is acceptable.
    /// </summary>
    public double TotalFlightHours { get; set; }

    /// <summary>
    /// The passenger aboard, if any. A save written before this field existed
    /// loads with no passenger, which is correct.
    /// </summary>
    public string? PassengerAboard { get; set; }

    /// <summary>
    /// The sling load on the cargo hook, if any. A save written before this field
    /// existed loads with no load, which is correct — the blade pair is the finale
    /// and earlier saves cannot have it.
    /// </summary>
    public string? SlingLoadId { get; set; }

    /// <summary>
    /// Region ordinals whose directed radio call has already fired (story.md §4.3).
    /// A save written before this field existed loads with none fired, which is
    /// correct — undelivered calls will fire on the next region entry.
    /// </summary>
    public List<int> DirectedCallsFired { get; set; } = new();

    // ================================================================ JSON

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static SaveData? FromJson(string json)
        => JsonSerializer.Deserialize<SaveData>(json, Opts);

    // ============================================ capture / apply (sim layer)

    public void CaptureProgress(Progress p)
    {
        Clock = p.Clock;

        Inventory.Clear();
        foreach (Stock s in Enum.GetValues<Stock>())
        {
            double amt = p.Amount(s);
            if (amt > 0) Inventory[s.ToString()] = amt;
        }

        Knowledge.Clear();
        foreach (var k in p.Known)
            Knowledge.Add(new KnowledgeSave
            {
                Kind = k.Kind, Id = k.Id, Label = k.Label, Detail = k.Detail,
            });

        Sites.Clear();
        foreach (var (id, rec) in p.AllSites)
            Sites[id.ToString()] = new SiteRecordSave
            {
                Visited = rec.Visited, Surveyed = rec.Surveyed, Cleared = rec.Cleared,
                FuelRemaining = rec.FuelRemaining, SalvageRemaining = rec.SalvageRemaining,
                LastVisitedAt = rec.LastVisitedAt, VisitCount = rec.VisitCount,
            };

        Journal.Clear();
        Journal.AddRange(p.Journal_);

        Cargo.Clear();
        foreach (SalvagePart part in p.Cargo.Parts)
            Cargo.Add(new CargoPartSave { Id = part.Def.Id, Condition = part.Condition });

        Damage.Clear();
        foreach ((int id, SiteDamage dmg) in p.DamagedSites)
        {
            if (dmg.Untouched) continue;
            var save = new SiteDamageSave { SiteId = id, PopulationLost = dmg.PopulationLost };
            foreach ((int index, double remaining) in dmg.Snapshot())
                save.Ruins.Add(new RuinSave { Index = index, Remaining = remaining });
            Damage.Add(save);
        }

        Deeds.Clear();
        foreach (DjDeed d in p.Deeds)
            Deeds.Add(new DeedSave
            {
                Kind = d.Kind.ToString(), At = d.AtClockSeconds,
                Place = d.Place, Amount = d.Amount, Witnesses = d.Witnesses,
            });

        ContractsCompleted = p.ContractsCompleted;
        SearchStage = p.Search.Stage;
        PassengerAboard = p.PassengerAboard;
        SlingLoadId = p.SlingLoadId;

        DirectedCallsFired.Clear();
        DirectedCallsFired.AddRange(p.DirectedCalls.Save());

        LegClosedAt.Clear();
        LegClosedAt.AddRange(p.Search.LegClosedAt);

        Contracts.Clear();
        foreach (var c in p.Contracts)
            Contracts.Add(new ContractSave
            {
                Id = c.Id, Kind = c.Kind,
                Title = c.Title, Brief = c.Brief,
                SourceSiteId = c.SourceSiteId,
                TargetSiteId = c.TargetSiteId,
                TargetName = c.TargetName,
                CargoKind = c.CargoKind,
                CargoAmount = c.CargoAmount,
                RewardKind = c.RewardKind,
                RewardAmount = c.RewardAmount,
                StandingReward = c.StandingReward,
                RewardKnowledgeId = c.RewardKnowledgeId,
                RewardKnowledgeLabel = c.RewardKnowledgeLabel,
                RewardKnowledgeDetail = c.RewardKnowledgeDetail,
                PostedAt = c.PostedAt,
                Accepted = c.Accepted,
                Completed = c.Completed,
                CompletedAt = c.CompletedAt,
            });
    }

    public Progress ApplyProgress()
    {
        var p = new Progress { Clock = Clock };

        foreach (var (name, amt) in Inventory)
            if (Enum.TryParse<Stock>(name, out var kind))
                p.RestoreStock(kind, amt);

        foreach (var k in Knowledge)
            p.RestoreKnowledge(new Sim.Knowledge(k.Kind, k.Id, k.Label, k.Detail));

        foreach (SiteDamageSave sd in Damage)
        {
            var d = new SiteDamage();
            d.Restore(sd.PopulationLost, sd.Ruins.Select(r => (r.Index, r.Remaining)));
            p.RestoreDamage(sd.SiteId, d);
        }

        // A deed whose kind has left the enum is dropped rather than throwing, the same rule
        // the cargo list follows: a save written by an older build must still open.
        foreach (DeedSave d in Deeds)
            if (Enum.TryParse(d.Kind, out DjDeedKind kind))
                p.RestoreDeed(new DjDeed(kind, d.At, d.Place, d.Amount, d.Witnesses));

        foreach (var (idStr, rec) in Sites)
        {
            if (!int.TryParse(idStr, out int id)) continue;
            p.RestoreSite(id, new SiteRecord
            {
                Visited = rec.Visited, Surveyed = rec.Surveyed, Cleared = rec.Cleared,
                FuelRemaining = rec.FuelRemaining, SalvageRemaining = rec.SalvageRemaining,
                LastVisitedAt = rec.LastVisitedAt, VisitCount = rec.VisitCount,
            });
        }

        p.RestoreJournal(Journal);

        p.Cargo.Restore(Cargo.Select(c => (c.Id, c.Condition)));

        p.ContractsCompleted = ContractsCompleted;
        p.Search.Stage = SearchStage;
        p.PassengerAboard = PassengerAboard;
        p.SlingLoadId = SlingLoadId;
        p.DirectedCalls.Restore(DirectedCallsFired.Count > 0 ? DirectedCallsFired.ToArray() : null);

        for (int i = 0; i < Math.Min(LegClosedAt.Count, 4); i++)
            p.Search.LegClosedAt[i] = LegClosedAt[i];

        var contracts = new List<Contract>();
        foreach (var cs in Contracts)
            contracts.Add(new Contract
            {
                Id = cs.Id, Kind = cs.Kind,
                Title = cs.Title, Brief = cs.Brief,
                SourceSiteId = cs.SourceSiteId,
                TargetSiteId = cs.TargetSiteId,
                TargetName = cs.TargetName,
                CargoKind = cs.CargoKind,
                CargoAmount = cs.CargoAmount,
                RewardKind = cs.RewardKind,
                RewardAmount = cs.RewardAmount,
                StandingReward = cs.StandingReward,
                RewardKnowledgeId = cs.RewardKnowledgeId,
                RewardKnowledgeLabel = cs.RewardKnowledgeLabel,
                RewardKnowledgeDetail = cs.RewardKnowledgeDetail,
                PostedAt = cs.PostedAt,
                Accepted = cs.Accepted,
                Completed = cs.Completed,
                CompletedAt = cs.CompletedAt,
            });
        p.RestoreContracts(contracts);

        return p;
    }

    public void CaptureDamage(DamageState d)
    {
        int count = Enum.GetValues<Component>().Length;
        ComponentHealth = new double[count];
        for (int i = 0; i < count; i++)
            ComponentHealth[i] = d.Health((Component)i);

        TotalFlightHours = d.TotalFlightHours;

        DamageLog.Clear();
        foreach (var e in d.Log)
            DamageLog.Add(new DamageEventSave
            {
                Component = e.Component, Amount = e.Amount,
                Cause = e.Cause, Note = e.Note,
            });
    }

    public void ApplyDamage(DamageState d)
    {
        for (int i = 0; i < ComponentHealth.Length; i++)
            d.SetHealth((Component)i, ComponentHealth[i]);

        d.TotalFlightHours = TotalFlightHours;

        d.RestoreLog(DamageLog.Select(e =>
            new DamageEvent(e.Component, e.Amount, e.Cause, e.Note)));
    }

    public void CaptureLoadout(Loadout l)
    {
        Installed = new List<string>(l.Installed);
        Bag = new List<string>(l.Bag);
    }

    public void ApplyLoadout(Loadout l) => l.Restore(Installed, Bag);

    public void CaptureAlert(AlertState alert)
    {
        AlertLevels.Clear();
        foreach (var kv in alert.Raised(0.001))
            AlertLevels[kv.Key.ToString()] = kv.Value;
    }

    public void ApplyAlert(AlertState alert)
    {
        alert.Clear();
        foreach (var (key, level) in AlertLevels)
            if (int.TryParse(key, out int rid))
                alert.Set(rid, level);
    }

    public static NpcSave CaptureNpc(int siteId, NpcMind npc, DialogueBank bank)
    {
        var save = new NpcSave
        {
            SiteId = siteId,
            Id = npc.Id, Name = npc.Name, Persona = npc.Persona,
            Standing = npc.Standing, Wants = npc.Wants,
            LastSeenAt = npc.LastSeenAt, Meetings = npc.Meetings,
        };
        foreach (var m in npc.Memories)
            save.Memories.Add(new MemoryFactSave
            {
                Key = m.Key, Value = m.Value, When = m.WhenSeconds,
            });
        foreach (var (k, v) in bank.LineUsage)
            save.LineUsage[k] = v;
        return save;
    }

    public static NpcMind RestoreNpcMind(NpcSave save)
    {
        var npc = new NpcMind
        {
            Id = save.Id, Name = save.Name, Persona = save.Persona,
            Standing = save.Standing, Wants = save.Wants,
            LastSeenAt = save.LastSeenAt, Meetings = save.Meetings,
        };
        foreach (var m in save.Memories)
            npc.Notice(m.Key, m.Value, m.When);
        return npc;
    }
}

// ================================================================ nested DTOs

public sealed class DamageEventSave
{
    public Component Component { get; set; }
    public double Amount { get; set; }
    public DamageCause Cause { get; set; }
    public string Note { get; set; } = "";
}

public sealed class KnowledgeSave
{
    public KnowledgeKind Kind { get; set; }
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>
/// One salvaged part aboard. Id plus wear; the catalog supplies the rest.
/// Matches the <c>(string Id, double Condition)</c> tuple <c>Cargo.Restore</c> takes.
/// </summary>
/// <summary>
/// One site's structural damage. The rebuild queue is stored in order, because the order is
/// the model: work goes into the oldest ruin first, and a save that reshuffled it would have
/// the village start a different house after a reload.
/// </summary>
public sealed class SiteDamageSave
{
    public int SiteId { get; set; }
    public int PopulationLost { get; set; }
    public List<RuinSave> Ruins { get; set; } = new();
}

/// <summary>One structure that is down, and the person-days left to put it back.</summary>
public sealed class RuinSave
{
    public int Index { get; set; }
    public double Remaining { get; set; }
}

/// <summary>One deed, flattened. The kind is stored by name so the enum can be reordered.</summary>
public sealed class DeedSave
{
    public string Kind { get; set; } = "";
    public double At { get; set; }
    public string? Place { get; set; }
    public double Amount { get; set; }
    public int Witnesses { get; set; }
}

public sealed class CargoPartSave
{
    public string Id { get; set; } = "";
    public double Condition { get; set; }
}

public sealed class SiteRecordSave
{
    public bool Visited { get; set; }
    public bool Surveyed { get; set; }
    public bool Cleared { get; set; }
    public double FuelRemaining { get; set; }
    public int SalvageRemaining { get; set; }
    public double LastVisitedAt { get; set; }
    public int VisitCount { get; set; }
}

public sealed class NpcSave
{
    public int SiteId { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Persona { get; set; } = "";
    public double Standing { get; set; }
    public string Wants { get; set; } = "";
    public double LastSeenAt { get; set; }
    public int Meetings { get; set; }
    public List<MemoryFactSave> Memories { get; set; } = new();
    public Dictionary<string, double> LineUsage { get; set; } = new();
}

public sealed class MemoryFactSave
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public double When { get; set; }
}

public sealed class ContractSave
{
    public string Id { get; set; } = "";
    public ContractKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Brief { get; set; } = "";
    public int SourceSiteId { get; set; }
    public int TargetSiteId { get; set; }
    public string TargetName { get; set; } = "";
    public Stock CargoKind { get; set; }
    public double CargoAmount { get; set; }
    public Stock RewardKind { get; set; }
    public double RewardAmount { get; set; }
    public double StandingReward { get; set; }
    public string? RewardKnowledgeId { get; set; }
    public string? RewardKnowledgeLabel { get; set; }
    public string? RewardKnowledgeDetail { get; set; }
    public double PostedAt { get; set; }
    public bool Accepted { get; set; }
    public bool Completed { get; set; }
    public double CompletedAt { get; set; }
}
