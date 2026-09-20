using System.Collections.Generic;
using System.Linq;

namespace Rotorwash.Sim;

/// <summary>
/// One-time radio messages that fire when the player enters a region whose relay
/// has been tuned (story.md §4.3, carrier type 2).
///
/// "Somebody raises you, because you tuned their mast and they have been listening
/// since. Fires on entering a region, once."
///
/// The game layer detects region transitions and checks whether any relay in the
/// new region has been tuned. If so, it calls <see cref="TryFire"/> with the region
/// ordinal. Each region fires at most once; the fired set persists through save/load.
///
/// Pure .NET, no Godot dependency.
/// </summary>
public sealed class DirectedCalls
{
    private readonly HashSet<int> _fired = new();

    /// <summary>Number of regions that have fired their directed call.</summary>
    public int FiredCount => _fired.Count;

    /// <summary>True if this region's directed call has already been delivered.</summary>
    public bool HasFired(int regionOrdinal) => _fired.Contains(regionOrdinal);

    /// <summary>
    /// Try to deliver the directed call for a region. Returns the message if this
    /// is the first time, null otherwise. The caller is responsible for checking
    /// that a relay in the region has actually been tuned before calling this.
    /// </summary>
    public RadioMessage? TryFire(int regionOrdinal)
    {
        if (_fired.Contains(regionOrdinal)) return null;
        if (!Calls.TryGetValue(regionOrdinal, out var call)) return null;
        _fired.Add(regionOrdinal);
        return new RadioMessage(call.Speaker, call.Text, RadioMessageKind.Directed);
    }

    // ------------------------------------------------------------------ save/load

    public int[] Save() => _fired.OrderBy(x => x).ToArray();

    public void Restore(int[]? data)
    {
        _fired.Clear();
        if (data is null) return;
        foreach (int r in data) _fired.Add(r);
    }

    // ------------------------------------------------------------------ content
    //
    // Keyed by region ordinal matching game layer's RegionKind enum:
    //   0=Basin, 1=Farmland, 2=Exurb, 3=City, 4=Industrial,
    //   5=Upland, 6=Wetland, 7=Ashfield
    //
    // Each message follows story.md §9: no chosen one, no instructions, facts only.
    // The speaker is the region name as a radio call sign. The text is a local
    // observation — what somebody at the relay would say to the only aircraft in
    // the world after it tuned their mast and came back.

    public static readonly Dictionary<int, (string Speaker, string Text)> Calls = new()
    {
        // Basin — The Pan. Home turf. Familiar, dry, nothing happens here.
        [0] = ("PAN RELAY",
            "You are on the Pan frequency. We saw you go over. " +
            "Pumps are running, road is clear, nothing has changed. Nothing ever does."),

        // Farmland — Long Acre. Talkative, agricultural.
        [1] = ("LONG ACRE",
            "Someone logged the Long Acre mast. If you can hear this, the set works " +
            "better than we thought. Rain last night. The creek is up."),

        // Exurb — Fenmoor. Practical, airfield territory.
        [2] = ("FENMOOR",
            "Fenmoor relay. First traffic on this frequency since we lit the transmitter. " +
            "Wind is westerly, the strip is dry."),

        // City — Ashmount. Tier 3, tense, under the aerostat.
        [3] = ("ASHMOUNT",
            "Ashmount relay. You logged this frequency, which was either brave or careless. " +
            "The bag is up. It is always up."),

        // Industrial — Sawtooth Works. Matter-of-fact, industrial.
        [4] = ("SAWTOOTH",
            "Sawtooth relay. You tuned us, so you have been here. " +
            "The mill is running. Smoke goes east today."),

        // Upland — Cold Shoulder. Sparse, high ground, clear air.
        [5] = ("COLD SHOULDER",
            "Cold Shoulder mast. Whoever you are, you are loud and clear. " +
            "Wind is gusting thirty from the north. Visibility is ten miles."),

        // Wetland — The Drowning. Careful, water people.
        [6] = ("DROWNING",
            "Drowning relay. Somebody has been at our mast. " +
            "The water is high this month and the crossing south of the village is under."),

        // Ashfield — The Scald. Carrier-only feeling; the ash, the emptiness.
        [7] = ("SCALD RELAY",
            "If you are receiving this, you have been close enough to touch our mast. " +
            "The ash is worse this season. Nothing else is flying in here."),
    };
}
