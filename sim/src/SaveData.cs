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

    // ---- NPCs ----
    public List<NpcSave> Npcs { get; set; } = new();

    // ---- combat ----
    public float PilotHealth { get; set; } = 100;
    public int SidearmRounds { get; set; } = 6;
    public int SidearmSpare { get; set; } = 12;
    public float RotorTimeCharge { get; set; }

    // ---- threats ----
    public int ChaffRemaining { get; set; }
    public int FlaresRemaining { get; set; }
    public List<int> DetectedEmitters { get; set; } = new();

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
                Visited = rec.Visited, Surveyed = rec.Surveyed,
                FuelRemaining = rec.FuelRemaining, SalvageRemaining = rec.SalvageRemaining,
                LastVisitedAt = rec.LastVisitedAt, VisitCount = rec.VisitCount,
            };

        Journal.Clear();
        Journal.AddRange(p.Journal_);
    }

    public Progress ApplyProgress()
    {
        var p = new Progress { Clock = Clock };

        foreach (var (name, amt) in Inventory)
            if (Enum.TryParse<Stock>(name, out var kind))
                p.RestoreStock(kind, amt);

        foreach (var k in Knowledge)
            p.RestoreKnowledge(new Sim.Knowledge(k.Kind, k.Id, k.Label, k.Detail));

        foreach (var (idStr, rec) in Sites)
        {
            if (!int.TryParse(idStr, out int id)) continue;
            p.RestoreSite(id, new SiteRecord
            {
                Visited = rec.Visited, Surveyed = rec.Surveyed,
                FuelRemaining = rec.FuelRemaining, SalvageRemaining = rec.SalvageRemaining,
                LastVisitedAt = rec.LastVisitedAt, VisitCount = rec.VisitCount,
            });
        }

        p.RestoreJournal(Journal);
        return p;
    }

    public void CaptureDamage(DamageState d)
    {
        int count = Enum.GetValues<Component>().Length;
        ComponentHealth = new double[count];
        for (int i = 0; i < count; i++)
            ComponentHealth[i] = d.Health((Component)i);

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

        d.RestoreLog(DamageLog.Select(e =>
            new DamageEvent(e.Component, e.Amount, e.Cause, e.Note)));
    }

    public void CaptureLoadout(Loadout l)
    {
        Installed = new List<string>(l.Installed);
        Bag = new List<string>(l.Bag);
    }

    public void ApplyLoadout(Loadout l) => l.Restore(Installed, Bag);

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

public sealed class SiteRecordSave
{
    public bool Visited { get; set; }
    public bool Surveyed { get; set; }
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
