namespace Rotorwash.Sim;

/// <summary>
/// One thing that can be bolted to the aircraft.
///
/// Every module is a MassItem plus drag and hardpoint occupancy (D-011). The existing
/// physics makes the trade real: hang something heavy aft and the CG shifts, bolt a
/// suppressor on the exhaust and the drag rises. Nothing needs faking.
/// </summary>
public sealed record ModuleDef(
    string Id,
    string Name,
    string Description,
    double Mass,
    Vec3 Position,              // body FRD, metres from airframe datum
    Vec3 DragDelta,             // additional flat-plate drag area, m²
    double FuelCapacityDelta,   // additional fuel capacity when installed, kg
    int PartsCost               // parts consumed on installation
);

/// <summary>
/// What is bolted to Hugh, and what is in the bag waiting to be.
///
/// This is the refit system promised by D-011. One bay per module, each holding one
/// specific module. The kneeboard shows every bay whether or not it is filled, because
/// you cannot want a thing you do not know exists (D-005a).
///
/// Every module has mass and a position, so the flight model feels it. The exhaust
/// suppressor adds drag. The long-range tank adds fuel capacity and weight. None of this
/// is faked — it flows through the same MassProperties and DragArea the structural items
/// use.
/// </summary>
public sealed class Loadout
{
    private readonly HashSet<string> _installed = new();
    private readonly HashSet<string> _bag = new();

    public IReadOnlyCollection<string> Installed => _installed;
    public IReadOnlyCollection<string> Bag => _bag;

    public bool IsInstalled(string id) => _installed.Contains(id);
    public bool InBag(string id) => _bag.Contains(id);
    public bool Has(string id) => _installed.Contains(id) || _bag.Contains(id);

    /// <summary>The player found a module. It goes in the bag until installed.</summary>
    public bool Find(string id)
    {
        if (Has(id)) return false;
        if (!Catalog.ContainsKey(id)) return false;
        _bag.Add(id);
        return true;
    }

    /// <summary>
    /// Install a module from the bag onto the aircraft. Returns the ModuleDef so the
    /// caller can apply its physics and gameplay effects.
    /// </summary>
    public ModuleDef? Install(string id)
    {
        if (!_bag.Remove(id)) return null;
        if (!Catalog.TryGetValue(id, out var def)) return null;
        _installed.Add(id);
        return def;
    }

    /// <summary>
    /// Remove a module from the aircraft back to the bag. Returns the ModuleDef so the
    /// caller can reverse its physics effects.
    /// </summary>
    public ModuleDef? Remove(string id)
    {
        if (!_installed.Remove(id)) return null;
        if (!Catalog.TryGetValue(id, out var def)) return null;
        _bag.Add(id);
        return def;
    }

    // ----------------------------------------------------------- save / load

    /// <summary>Set installed and bag contents directly. Save/load only.</summary>
    public void Restore(IEnumerable<string> installed, IEnumerable<string> bag)
    {
        _installed.Clear();
        foreach (var id in installed) _installed.Add(id);
        _bag.Clear();
        foreach (var id in bag) _bag.Add(id);
    }

    // ---------------------------------------------------------------- catalog

    /// <summary>All modules that exist, in kneeboard display order.</summary>
    public static readonly IReadOnlyList<ModuleDef> All = new ModuleDef[]
    {
        //                   id              name                  description
        //                   mass   position (body FRD)           drag delta           fuel+  parts
        new("sas",          "Attitude hold",
            "Holds the aircraft where you put it.",
            12, new Vec3(2.3, 0, -0.55),      Vec3.Zero,           0,     2),

        new("rwr",          "Radar warning",
            "Tells you when you are painted.",
            8,  new Vec3(2.1, 0.5, -0.65),    Vec3.Zero,           0,     2),

        new("chaff",        "Chaff",
            "Against radar-guided threats.",
            18, new Vec3(-5.8, 0.3, -0.85),   Vec3.Zero,           0,     1),

        new("flares",       "Flares",
            "Against heat-seeking threats.",
            22, new Vec3(-5.8, -0.3, -0.85),  Vec3.Zero,           0,     1),

        new("suppressor",   "Exhaust suppressor",
            "Reduces your heat signature. Adds drag.",
            35, new Vec3(-1.5, 0, -2.0),      new Vec3(0.15, 0, 0), 0,    2),

        new("tank",         "Long range tank",
            "More fuel, more weight, less cabin.",
            45, new Vec3(0.4, 0, -0.15),      Vec3.Zero,           350,   3),

        new("hook",         "Cargo hook",
            "Carry loads underneath.",
            28, new Vec3(0.1, 0, 0.4),        Vec3.Zero,           0,     2),

        new("hoist",        "Rescue hoist",
            "Reach people without landing.",
            40, new Vec3(0.8, -1.3, -0.5),    Vec3.Zero,           0,     3),

        // A receiver and a cassette deck in the console, and nothing about it is a special
        // case (D-058). It is a module like any other: mass on the airframe, a bay on the
        // kneeboard, parts to fit it, and it runs off the avionics stack - so a hit that
        // takes the navigation takes the music with it, because it is the same stack.
        // Nine kilos is a period set and its bracket; the point of D-011 is that even this
        // is weight the aircraft has to carry.
        new("radio",        "Cockpit radio",
            "A receiver and a tape deck. Runs off the avionics bus.",
            9,  new Vec3(1.9, -0.35, -0.30),  Vec3.Zero,           0,     1),
    };

    public static readonly IReadOnlyDictionary<string, ModuleDef> Catalog;

    static Loadout()
    {
        var dict = new Dictionary<string, ModuleDef>();
        foreach (var m in All) dict[m.Id] = m;
        Catalog = dict;
    }

    /// <summary>
    /// What module, if any, can be found at this site. Deterministic by site id so the
    /// same world always has the same distribution. About one in seven eligible sites
    /// (wrecks, airfields, depots) yields a module.
    /// </summary>
    public static string? ModuleAtSite(int siteId)
    {
        var rng = new Random(siteId * 48271 + 9973);
        if (rng.NextDouble() > 0.15) return null;

        // Weighted toward early-game modules so the player finds something useful first.
        string[] pool =
        {
            "sas", "sas", "rwr", "rwr",
            "chaff", "chaff", "flares", "flares",
            "suppressor", "tank", "hook", "hoist",
        };
        return pool[rng.Next(pool.Length)];
    }
}
