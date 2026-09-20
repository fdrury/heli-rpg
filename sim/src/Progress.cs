namespace Rotorwash.Sim;

/// <summary>Things the player can be carrying. Deliberately few and concrete.</summary>
public enum Stock
{
    Scrap,          // generic salvage, the currency of repair
    Parts,          // specific serviceable components
    Fuel,           // jerrycans, carried not plumbed - weight you choose to accept
    Medical,
    Food,
    Ammunition,
}

/// <summary>A thing that is KNOWN rather than owned. This is the progression currency.</summary>
public enum KnowledgeKind
{
    Chart,          // a region surveyed, terrain and hazards filled in
    Frequency,      // a radio channel: contacts, traffic, warnings
    Schematic,      // how to build or repair something
    Contact,        // a person who will deal with you
    ThreatSite,     // where something will shoot at you, and what kind
    Rumour,         // an unresolved thread
}

public readonly record struct Knowledge(KnowledgeKind Kind, string Id, string Label, string Detail);

/// <summary>What has happened at one place.</summary>
public sealed class SiteRecord
{
    public bool Visited;
    public bool Surveyed;
    public bool Cleared;              // hostile NPCs downed; site is now safe
    public double FuelRemaining = -1;   // -1 = not yet determined
    public int SalvageRemaining = -1;
    public double LastVisitedAt;
    public int VisitCount;
}

/// <summary>
/// Everything the player has, knows, and has done.
///
/// This is the save file, and under D-005 it is also the character sheet: there is no
/// experience and there are no levels, so a player's capability is exactly the contents of
/// this object plus whatever is bolted to the aircraft.
///
/// D-005a added the requirement that drove the shape of it: there must be one screen that
/// shows the player they are further along than they were, and it must hold **facts, not
/// inferences**. Nothing here computes a percentage or a score. It records what was found,
/// where, and when - and lets the player do the thinking.
/// </summary>
public sealed class Progress
{
    private readonly Dictionary<Stock, double> _stock = new();
    private readonly Dictionary<string, Knowledge> _known = new();
    private readonly Dictionary<int, SiteRecord> _sites = new();
    private readonly List<string> _journal = new();
    private readonly List<Contract> _contracts = new();

    /// <summary>Main quest search thread. Persists through save/load.</summary>
    public SearchThread Search { get; } = new();

    /// <summary>One-time radio calls from tuned relays, per region (story.md §4.3).</summary>
    public DirectedCalls DirectedCalls { get; } = new();

    /// <summary>
    /// Who is riding in the right seat, or null. Set by DialogueRewardKind.Passenger.
    /// This is not a module or cargo — it is a person who agreed to fly with you, and
    /// their mass is real mass at a real position (story.md §7.6).
    /// </summary>
    public string? PassengerAboard { get; set; }

    /// <summary>
    /// What is hanging on the cargo hook, or null. Identified by a string id
    /// that maps to <see cref="SlingLoads.ById"/>.  The hook module must be
    /// installed for the load to be attached; the load's mass, drag and cable
    /// dynamics are set by the factory, not stored here.  story.md §7.7.
    /// </summary>
    public string? SlingLoadId { get; set; }

    /// <summary>
    /// Salvaged components aboard but not fitted: the heavy, awkward, valuable things.
    ///
    /// Lives here rather than beside the aircraft because it is part of the character
    /// sheet (D-005: capability is what you have, not what level you are) and because
    /// this is the object that gets saved. The consequence that matters is one line
    /// down: <see cref="CarriedMass"/> counts it, so a rotor blade in the back is a
    /// hundred and five kilos the rotor has to lift.
    /// </summary>
    public Cargo Cargo { get; } = new();

    /// <summary>In-world seconds since the game began.</summary>
    public double Clock { get; set; }

    // ------------------------------------------------------------------ deeds

    private readonly List<DjDeed> _deeds = new();

    /// <summary>
    /// What the district has to talk about: things the player did that somebody saw.
    ///
    /// Lives on <see cref="Progress"/> rather than on the radio because it has to survive a
    /// save - the announcer bringing up a delivery from before you last quit is most of what
    /// makes him a person rather than a session-local effect (D-074) - and because the radio
    /// is not the only thing that will want this list. It is deliberately a record of what
    /// HAPPENED, not of what was said; who has heard about it is the announcer's problem and
    /// is decided by <see cref="RadioDj.Knowable"/> every time he opens his mouth.
    /// </summary>
    public IReadOnlyList<DjDeed> Deeds => _deeds;

    /// <summary>
    /// Record something the player did.
    ///
    /// <paramref name="witnesses"/> is the field that matters and the one that must not be
    /// defaulted by callers: it decides whether the deed is ever mentioned at all and how
    /// far the figure drifts on the way to the mast. It comes from the world - the
    /// population of the nearest inhabited place - so that flying the long way round over
    /// empty country is genuinely quieter than flying over the valley. A hook that passes 1
    /// everywhere turns the whole system back into the event log D-074 exists to avoid.
    /// </summary>
    public void RecordDeed(DjDeedKind kind, string? place, double amount, int witnesses)
    {
        _deeds.Add(new DjDeed(kind, Clock, place, amount, witnesses));
        Prune();
    }

    /// <summary>Restore one from a save, at its original time.</summary>
    public void RestoreDeed(in DjDeed deed) { _deeds.Add(deed); Prune(); }

    /// <summary>
    /// Drop anything older than the announcer could still bring up.
    ///
    /// Without this the list grows for the length of a campaign and is written to every save
    /// forever, to hold deeds no line can ever draw. The horizon is the announcer's own
    /// callback window, so the pruning rule and the speaking rule cannot drift apart.
    /// </summary>
    private void Prune()
    {
        double cutoff = Clock - RadioDj.DeedCallbackSeconds;
        _deeds.RemoveAll(d => d.AtClockSeconds < cutoff);
    }

    public event Action<Knowledge>? Learned;
    public event Action<Stock, double>? StockChanged;
    public event Action<string>? Journalled;

    // ------------------------------------------------------------------ stock

    public double Amount(Stock kind) => _stock.GetValueOrDefault(kind);

    public void Add(Stock kind, double amount)
    {
        if (amount == 0) return;
        _stock[kind] = Math.Max(0, Amount(kind) + amount);
        StockChanged?.Invoke(kind, _stock[kind]);
    }

    public bool Spend(Stock kind, double amount)
    {
        if (Amount(kind) < amount) return false;
        Add(kind, -amount);
        return true;
    }

    /// <summary>
    /// Mass of everything carried, kg. Fed straight into the flight model, because the
    /// point of a load system is that it is felt in the hover, not read off a screen.
    ///
    /// <para>Bulk stock is counted at a nominal mass per unit; <see cref="Cargo"/> is
    /// counted at the real mass of the real parts, because that is where the interesting
    /// numbers are. A main rotor blade is 105 kg and the aircraft has about 219 kg of
    /// payload left with a full tank, so two blades is the whole budget - and 200 kg
    /// aboard costs 625 m of measured hover ceiling (3250 m -> 2625 m). That is the
    /// mechanism D-003a is describing when it says fuel is a load constraint rather than
    /// a range constraint: the load is felt, and it closes the high country off.</para>
    ///
    /// <para>One place, on purpose. The alternative considered was a second
    /// <c>MassItem</c> pushed in from the Godot layer for cargo alone, which would have
    /// put the mass budget in two files and guaranteed they disagreed. The game layer
    /// reads this one number and keeps a single "cargo" mass item in sync with it.</para>
    /// </summary>
    public double CarriedMass =>
        Amount(Stock.Scrap) * 1.0 +
        Amount(Stock.Parts) * 6.5 +
        Amount(Stock.Fuel) * 20.0 +     // a full jerrycan
        Amount(Stock.Medical) * 1.5 +
        Amount(Stock.Food) * 0.8 +
        Amount(Stock.Ammunition) * 0.05 +
        Cargo.Mass;

    // -------------------------------------------------------------- knowledge

    public bool Knows(string id) => _known.ContainsKey(id);
    public IReadOnlyCollection<Knowledge> Known => _known.Values;

    public int CountKnown(KnowledgeKind kind)
    {
        int n = 0;
        foreach (Knowledge k in _known.Values) if (k.Kind == kind) n++;
        return n;
    }

    /// <summary>Record something learned. Returns false if it was already known.</summary>
    public bool Learn(Knowledge k)
    {
        if (_known.ContainsKey(k.Id)) return false;
        _known[k.Id] = k;
        Learned?.Invoke(k);
        Journal($"{k.Kind}: {k.Label}");
        return true;
    }

    // ------------------------------------------------------------------ sites

    // ------------------------------------------------------- what you knocked down

    private readonly Dictionary<int, SiteDamage> _damage = new();

    /// <summary>
    /// Structural damage and dead at every place the player has hit.
    ///
    /// Separate from <see cref="SiteRecord"/> on purpose: a record is what the player has
    /// learned about a site and is written on almost every visit, and this is what they did
    /// TO it, which most sites never have at all. Keeping them apart means the common case
    /// stays empty and a save does not carry a damage object for a hundred and nineteen
    /// untouched places.
    /// </summary>
    public SiteDamage Damage(int siteId)
    {
        if (!_damage.TryGetValue(siteId, out SiteDamage? d)) _damage[siteId] = d = new SiteDamage();
        return d;
    }

    /// <summary>Damage without creating a record for a site that has never been touched.</summary>
    public SiteDamage? DamageOrNull(int siteId)
        => _damage.TryGetValue(siteId, out SiteDamage? d) ? d : null;

    /// <summary>Every site that has been knocked about, for the save and for the rebuild tick.</summary>
    public IEnumerable<KeyValuePair<int, SiteDamage>> DamagedSites => _damage;

    /// <summary>Restore one from a save.</summary>
    public void RestoreDamage(int siteId, SiteDamage d) => _damage[siteId] = d;

    public SiteRecord Record(int siteId)
    {
        if (!_sites.TryGetValue(siteId, out SiteRecord? r)) _sites[siteId] = r = new SiteRecord();
        return r;
    }

    public bool HasVisited(int siteId) => _sites.TryGetValue(siteId, out SiteRecord? r) && r.Visited;
    public int VisitedCount { get { int n = 0; foreach (var r in _sites.Values) if (r.Visited) n++; return n; } }
    public int SurveyedCount { get { int n = 0; foreach (var r in _sites.Values) if (r.Surveyed) n++; return n; } }

    public void MarkVisited(int siteId)
    {
        SiteRecord r = Record(siteId);
        if (!r.Visited) { r.Visited = true; }
        r.VisitCount++;
        r.LastVisitedAt = Clock;
    }

    // --------------------------------------------------------------- contracts

    public IReadOnlyList<Contract> Contracts => _contracts;

    /// <summary>Number of contracts completed across the whole game.</summary>
    public int ContractsCompleted { get; set; }

    /// <summary>Accept a contract. It moves from the board into the active list.</summary>
    public void AcceptContract(Contract c)
    {
        c.Accepted = true;
        _contracts.Add(c);
        Journal($"Accepted: {c.Title}");
    }

    /// <summary>Check all active contracts for completion. Returns newly completed ones.</summary>
    public List<Contract> CheckContracts()
    {
        var completed = new List<Contract>();
        foreach (var c in _contracts)
        {
            if (c.Completed || !c.Accepted) continue;
            if (c.CheckCompletion(this))
            {
                c.PayOut(this);
                ContractsCompleted++;
                Journal($"Completed: {c.Title}");
                completed.Add(c);
            }
        }
        return completed;
    }

    /// <summary>Active (accepted, not completed) contracts.</summary>
    public IEnumerable<Contract> ActiveContracts
    {
        get { foreach (var c in _contracts) if (c.Accepted && !c.Completed) yield return c; }
    }

    /// <summary>Remove completed contracts older than a threshold.</summary>
    public void PruneContracts(double maxAge)
    {
        _contracts.RemoveAll(c => c.Completed && Clock - c.CompletedAt > maxAge);
    }

    // ----------------------------------------------------------- save / load

    public IReadOnlyDictionary<Stock, double> AllStock => _stock;
    public IReadOnlyDictionary<string, Knowledge> AllKnown => _known;
    public IReadOnlyDictionary<int, SiteRecord> AllSites => _sites;

    /// <summary>Set a stock amount directly, bypassing events. Save/load only.</summary>
    public void RestoreStock(Stock kind, double amount) => _stock[kind] = amount;

    /// <summary>Record knowledge without firing events or journalling. Save/load only.</summary>
    public void RestoreKnowledge(Knowledge k) => _known[k.Id] = k;

    /// <summary>Set a site record directly. Save/load only.</summary>
    public void RestoreSite(int id, SiteRecord rec) => _sites[id] = rec;

    /// <summary>Replace the journal wholesale. Save/load only.</summary>
    public void RestoreJournal(IEnumerable<string> entries)
    {
        _journal.Clear();
        _journal.AddRange(entries);
    }

    /// <summary>Replace contracts wholesale. Save/load only.</summary>
    public void RestoreContracts(IEnumerable<Contract> contracts)
    {
        _contracts.Clear();
        _contracts.AddRange(contracts);
    }

    // ---------------------------------------------------------------- journal

    /// <summary>
    /// A plain log of what happened, newest last. Facts only - no summaries, no progress
    /// bars, no "you are 34% through the story". The kneeboard shows this, and the player
    /// draws their own conclusions.
    /// </summary>
    public IReadOnlyList<string> Journal_ => _journal;

    public void Journal(string line)
    {
        string stamped = $"[{FormatClock(Clock)}] {line}";
        _journal.Add(stamped);
        if (_journal.Count > 400) _journal.RemoveAt(0);
        Journalled?.Invoke(stamped);
    }

    public static string FormatClock(double seconds)
    {
        int days = (int)(seconds / 86400);
        int hours = (int)(seconds % 86400 / 3600);
        int mins = (int)(seconds % 3600 / 60);
        return days > 0 ? $"D{days + 1} {hours:00}:{mins:00}" : $"{hours:00}:{mins:00}";
    }

    /// <summary>A brand new pilot: one aircraft, almost nothing else, and a name.</summary>
    public static Progress NewGame()
    {
        var p = new Progress { Clock = 6 * 3600 };   // dawn on the first day
        p.Add(Stock.Scrap, 12);
        p.Add(Stock.Parts, 1);
        p.Add(Stock.Food, 6);
        p.Learn(new Knowledge(KnowledgeKind.Rumour, "rumour.the_name",
            "Wray. A name, and four legs in her handwriting.",
            "The logbook, the nose, every inspection page. And a number that might be a frequency. Neither is a place."));
        p.Journal("Fuel state noted. Hugh is airworthy. Everything else is guesswork.");
        return p;
    }
}
