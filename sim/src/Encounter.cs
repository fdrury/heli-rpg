namespace Rotorwash.Sim;

/// <summary>
/// Determines which sites are hostile and how many NPCs guard them.
///
/// The logic is deterministic (site seed) so the same world always has the same
/// hostile sites, and the result is a pure function of the site — no Godot dependency.
///
/// Design:
///   - Tier 0 is safe. The Basin is the tutorial; nobody shoots you there.
///   - Tier 1+: wrecks, depots, airfields have a ~40% chance of being guarded.
///   - Tier 2+: farmsteads join at ~30%.
///   - Settlements, workshops, relays and overlooks are never hostile.
///     Settlements are where you trade; workshops are where you repair;
///     relays and overlooks are too small and too exposed for anyone to camp.
///   - NPC count scales with site size: 1–2 at small sites, 2–4 at large ones.
///
/// Cleared state lives in SiteRecord, not here, so it persists through save/load.
/// </summary>
public static class Encounter
{
    /// <summary>
    /// Whether a site has hostile NPCs guarding it. Pure function of site data.
    /// </summary>
    public static bool IsHostile(int siteId, int siteKind, int tier)
    {
        if (tier < 1) return false;

        // Only certain site kinds can be hostile.
        // SiteKind values: 0=FuelCache, 1=Settlement, 2=Workshop, 3=Wreck,
        //   4=Relay, 5=Depot, 6=Airfield, 7=Farmstead, 8=Overlook
        bool eligible = siteKind switch
        {
            3 => true,  // Wreck
            5 => true,  // Depot
            6 => true,  // Airfield
            7 => tier >= 2, // Farmstead — only deeper in defended country
            _ => false,
        };
        if (!eligible) return false;

        // Deterministic roll from site seed.
        uint hash = Splitmix(unchecked((uint)(siteId * 104729 + 9181)));
        float roll = (hash & 0xFFFF) / 65535f;

        // Threshold varies by kind and tier: higher tier → more likely hostile.
        float threshold = siteKind switch
        {
            5 => 0.55f - tier * 0.10f,  // Depot: 45% at T1, 55% at T2, 65% at T3
            6 => 0.50f - tier * 0.10f,  // Airfield: 40% at T1, 50% at T2, 60% at T3
            3 => 0.45f - tier * 0.08f,  // Wreck: 37% at T1, 45% at T2, 53% at T3
            7 => 0.40f - tier * 0.05f,  // Farmstead: only T2+: 30% at T2, 35% at T3
            _ => 0.30f,
        };

        return roll < threshold;
    }

    /// <summary>How many NPCs guard a hostile site.</summary>
    public static int NpcCount(int siteId, int siteKind)
    {
        uint hash = Splitmix(unchecked((uint)(siteId * 31337 + 7)));
        int roll = (int)(hash % 100);

        return siteKind switch
        {
            6 => roll < 30 ? 4 : roll < 70 ? 3 : 2,  // Airfield: 2–4, skewed high
            5 => roll < 40 ? 3 : roll < 80 ? 2 : 1,   // Depot: 1–3
            3 => roll < 50 ? 2 : 1,                     // Wreck: 1–2
            7 => roll < 60 ? 2 : 1,                     // Farmstead: 1–2
            _ => 1,
        };
    }

    /// <summary>Splitmix32 — fast, deterministic, good enough for a roll.</summary>
    private static uint Splitmix(uint z)
    {
        z += 0x9E3779B9;
        z ^= z >> 16;
        z *= 0x85EBCA6B;
        z ^= z >> 13;
        z *= 0xC2B2AE35;
        z ^= z >> 16;
        return z;
    }
}
