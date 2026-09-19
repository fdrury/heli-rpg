using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for hostile site encounters: which sites are hostile, how many NPCs
/// guard them, determinism, and clearing.
/// </summary>
public static class EncounterTests
{
    /// <summary>Tier 0 sites are never hostile. The Basin is the tutorial.</summary>
    public static string? Tier0Safe()
    {
        Console.WriteLine("  checking tier 0 is always safe");
        for (int id = 0; id < 200; id++)
        {
            for (int kind = 0; kind <= 8; kind++)
            {
                if (Encounter.IsHostile(id, kind, tier: 0))
                    return $"site {id} kind {kind} at tier 0 should not be hostile";
            }
        }
        Console.WriteLine("  all tier 0 sites are safe");
        return null;
    }

    /// <summary>Settlements and workshops are never hostile at any tier.</summary>
    public static string? SafeKinds()
    {
        Console.WriteLine("  checking that safe site kinds are never hostile");
        // SiteKind: 0=FuelCache, 1=Settlement, 2=Workshop, 4=Relay, 8=Overlook
        int[] safeKinds = { 0, 1, 2, 4, 8 };
        foreach (int kind in safeKinds)
        {
            for (int tier = 0; tier <= 3; tier++)
            {
                for (int id = 0; id < 200; id++)
                {
                    if (Encounter.IsHostile(id, kind, tier))
                        return $"kind {kind} should never be hostile (id={id}, tier={tier})";
                }
            }
        }
        Console.WriteLine("  settlements, workshops, fuel caches, relays and overlooks are all safe");
        return null;
    }

    /// <summary>Some tier 1+ wrecks, depots and airfields are hostile.</summary>
    public static string? HostilesExist()
    {
        Console.WriteLine("  checking that hostile sites exist at tier 1+");
        int hostileCount = 0;
        int totalEligible = 0;
        int[] hostileKinds = { 3, 5, 6 }; // Wreck, Depot, Airfield
        foreach (int kind in hostileKinds)
        {
            for (int id = 0; id < 200; id++)
            {
                totalEligible++;
                if (Encounter.IsHostile(id, kind, tier: 1))
                    hostileCount++;
            }
        }
        double rate = (double)hostileCount / totalEligible;
        Console.WriteLine($"  {hostileCount}/{totalEligible} eligible sites are hostile ({rate:P0})");
        if (hostileCount == 0) return "no hostile sites at tier 1 — encounter system is dead";
        if (rate < 0.15) return $"hostile rate {rate:P0} is too low (< 15%)";
        if (rate > 0.75) return $"hostile rate {rate:P0} is too high (> 75%)";
        return null;
    }

    /// <summary>NPC counts are in the expected range for each site kind.</summary>
    public static string? NpcCounts()
    {
        Console.WriteLine("  checking NPC counts by site kind");
        int[] kinds = { 3, 5, 6, 7 }; // Wreck, Depot, Airfield, Farmstead
        foreach (int kind in kinds)
        {
            for (int id = 0; id < 100; id++)
            {
                int count = Encounter.NpcCount(id, kind);
                if (count < 1) return $"NPC count < 1 for kind {kind} id {id}";
                if (count > 4) return $"NPC count > 4 for kind {kind} id {id}";
            }
        }
        Console.WriteLine("  NPC counts are all 1-4");
        return null;
    }

    /// <summary>IsHostile is deterministic — same inputs, same answer.</summary>
    public static string? Determinism()
    {
        Console.WriteLine("  checking determinism");
        for (int id = 0; id < 100; id++)
        {
            for (int kind = 0; kind <= 8; kind++)
            {
                for (int tier = 0; tier <= 3; tier++)
                {
                    bool a = Encounter.IsHostile(id, kind, tier);
                    bool b = Encounter.IsHostile(id, kind, tier);
                    if (a != b) return $"IsHostile not deterministic (id={id}, kind={kind}, tier={tier})";
                }
            }
        }
        Console.WriteLine("  encounter rolls are deterministic");
        return null;
    }

    /// <summary>SiteRecord.Cleared persists through save/load round-trip.</summary>
    public static string? ClearedRoundTrip()
    {
        Console.WriteLine("  checking Cleared persists in save/load");
        var p = Progress.NewGame();
        var rec = p.Record(42);
        rec.Visited = true;
        rec.Cleared = true;

        var data = new SaveData();
        data.CaptureProgress(p);
        var json = data.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "save round-trip failed to deserialise";
        var restored = loaded.ApplyProgress();
        var restoredRec = restored.Record(42);
        if (!restoredRec.Cleared) return "Cleared did not survive save round-trip";
        if (!restoredRec.Visited) return "Visited did not survive save round-trip";

        Console.WriteLine("  Cleared round-trips through save/load");
        return null;
    }
}
