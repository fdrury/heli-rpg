namespace Rotorwash.Sim;

/// <summary>
/// Where a thing was found. A mirror of the game layer's <c>Rotorwash.SiteKind</c>.
///
/// The sim is engine-free (D-002) and <c>SiteKind</c> lives in the Godot layer next to
/// <c>WorldMap</c>, so it cannot be referenced from here. The values are listed in the
/// SAME ORDER with the SAME NAMES, which makes <c>(SalvageSiteKind)(int)site.Kind</c> a
/// legal and readable conversion on the game side.
///
/// It is a mirror rather than a move because moving it would mean editing WorldMap, and
/// because the sim does not care where a place is - only what comes out of it.
///
/// If a value is ever added to the game enum and not to this one, <see cref="Yield"/>
/// will throw rather than silently hand back a Fuel Cache table. That is deliberate: a
/// wrong-but-plausible yield table is exactly the kind of bug this project keeps finding
/// three days late.
/// </summary>
public enum SalvageSiteKind
{
    FuelCache,
    Settlement,
    Workshop,
    Wreck,
    Relay,
    Depot,
    Airfield,
    Farmstead,
    Overlook,
}

/// <summary>Readable band for a condition number. Display only - nothing keys off it.</summary>
public enum PartGrade { Junk, Worn, Serviceable, Overhauled }

/// <summary>
/// A kind of component that can be pulled out of a ruin and bolted onto Hugh.
///
/// Every part services exactly one <see cref="Component"/>, which is the join between
/// this file and <c>Damage.cs</c>: salvage is not an abstract currency, it is the thing
/// that puts a number in the health array back up.
///
/// <para><b>Tier</b> is the scarcity gate and the reason the player flies outward. A part
/// with Tier 2 does not exist at a Tier 0 site at all - not rare, absent. Wanting a hot
/// section is what makes you plan a route into defended country (D-010, D-003b).</para>
///
/// <para><b>Mass</b> is real kilograms and is not negotiable. A main rotor blade is about
/// a hundred kilos, and the aircraft has roughly two hundred kilos of payload left once
/// it is fuelled (see <see cref="MaxGrossMass"/>). That single fact is the whole
/// weight-versus-value question, and it needed no invention.</para>
/// </summary>
public readonly record struct PartDef(
    string Id,
    string Name,
    Component Fits,
    double Mass,        // kg
    int Tier,           // minimum region tier at which this exists at all
    double Value,       // scrap-equivalent worth when overhauled
    double Rarity,      // relative weight within its component group, 1.0 = ordinary
    string Note);

/// <summary>One physical part, with the wear it happens to have.</summary>
public readonly record struct SalvagePart(PartDef Def, double Condition)
{
    public double Mass => Def.Mass;

    /// <summary>
    /// Trade worth. Convex in condition on purpose: a half-worn part is worth a great
    /// deal less than half a good one, because what a buyer is paying for is the hours
    /// left in it, and those fall away faster than the number does
    /// (see <see cref="Salvage.LifeHours"/>).
    /// </summary>
    public double Value => Def.Value * Math.Pow(Math.Clamp(Condition, 0, 1), 1.8);

    public double ValuePerKg => Value / Def.Mass;

    public PartGrade Grade =>
        Condition >= 0.85 ? PartGrade.Overhauled :
        Condition >= 0.60 ? PartGrade.Serviceable :
        Condition >= 0.35 ? PartGrade.Worn : PartGrade.Junk;

    public override string ToString() => $"{Def.Name} ({Condition * 100:F0}%, {Def.Mass:F0} kg)";
}

/// <summary>What one search of one site turned up.</summary>
public readonly record struct SalvageFind(
    double Scrap,
    int FuelCans,
    int Medical,
    int Food,
    int Ammunition,
    SalvagePart? Part)
{
    public bool Empty => Scrap <= 0 && FuelCans == 0 && Medical == 0
                      && Food == 0 && Ammunition == 0 && Part is null;
}

/// <summary>
/// What a kind of place gives you. One row per <see cref="SalvageSiteKind"/>.
///
/// The rows differ in four independent ways, and it is having all four that makes a
/// wreck feel unlike a depot rather than merely paying less:
///   - WHAT (which components are present at all),
///   - HOW MANY TIMES you can search before it is stripped,
///   - IN WHAT CONDITION the parts come out,
///   - and what BULK stock comes with them.
/// A workshop that gave fewer, better parts but the same condition band as a wreck would
/// just be a worse wreck.
/// </summary>
public readonly record struct SiteYield(
    int SearchesMin, int SearchesMax,
    double ScrapMin, double ScrapMax,
    double PartChance,
    double ConditionMin, double ConditionMax,
    double FuelChance, double MedicalChance, double FoodChance, double AmmoChance,
    Component[] Components,
    string Character);

/// <summary>
/// What the player is carrying that is not plumbed into the aircraft.
///
/// Deliberately a plain list rather than slots. The constraint on what you can carry is
/// not an inventory grid, it is <see cref="Salvage.MaxGrossMass"/> and the hover, and the
/// flight model already enforces that better than any UI could (D-003a: fuel is not a
/// range constraint, it is a load constraint, and load is felt).
/// </summary>
public sealed class Cargo
{
    private readonly List<SalvagePart> _parts = new();

    public IReadOnlyList<SalvagePart> Parts => _parts;
    public int Count => _parts.Count;

    public double Mass { get { double m = 0; foreach (var p in _parts) m += p.Mass; return m; } }
    public double Value { get { double v = 0; foreach (var p in _parts) v += p.Value; return v; } }

    public void Take(SalvagePart p) => _parts.Add(p);

    /// <summary>Drop the first part with this id. Returns it, or null if not carried.</summary>
    public SalvagePart? Drop(string partId)
    {
        for (int i = 0; i < _parts.Count; i++)
            if (_parts[i].Def.Id == partId)
            {
                SalvagePart p = _parts[i];
                _parts.RemoveAt(i);
                return p;
            }
        return null;
    }

    /// <summary>Best part carried for a given component, by condition. Null if none.</summary>
    public SalvagePart? BestFor(Component c)
    {
        SalvagePart? best = null;
        foreach (var p in _parts)
            if (p.Def.Fits == c && (best is null || p.Condition > best.Value.Condition))
                best = p;
        return best;
    }

    public void Clear() => _parts.Clear();

    /// <summary>Restore from a save. Save/load only.</summary>
    public void Restore(IEnumerable<(string Id, double Condition)> parts)
    {
        _parts.Clear();
        foreach (var (id, cond) in parts)
            if (Salvage.Catalog.TryGetValue(id, out PartDef def))
                _parts.Add(new SalvagePart(def, Math.Clamp(cond, 0, 1)));
    }
}

/// <summary>Why a fit is or is not a good idea. The numbers are facts; the verdict is not.</summary>
public enum FitVerdict
{
    Worthwhile,     // clearly better than what is in there
    Marginal,       // an improvement, but it buys less than a sortie
    NotAnUpgrade,   // the part is no better than the component already fitted
    WrongComponent, // does not fit
    NotEnoughScrap, // no shims, no safety wire, no fit
}

/// <summary>
/// Everything a player needs to decide whether to swap a component, and nothing they do
/// not. Per D-005a the kneeboard shows FACTS, never inferences - so the hours, the scrap
/// and the health are the product here, and <see cref="Verdict"/> is a convenience the
/// caller is free to ignore. <see cref="Salvage.Fit"/> will do a Marginal fit if asked.
/// </summary>
public readonly record struct FitPreview(
    Component Component,
    double OldHealth,
    double NewHealth,
    double OldLifeHours,
    double NewLifeHours,
    double ScrapCost,
    double ScrapRecovered,
    double LabourHours,
    FitVerdict Verdict,
    string Reason)
{
    public double HoursGained => NewLifeHours - OldLifeHours;
    public bool Possible => Verdict is FitVerdict.Worthwhile or FitVerdict.Marginal;
}

/// <summary>
/// The salvage and parts economy: what scarcity actually costs.
///
/// D-005 says capability comes from physical parts rather than from a level. That only
/// has teeth if parts are scarce, heavy and imperfect, and until this file existed the
/// game could hand the player a thing but could not make them want it. Four mechanisms,
/// each of which is a decision rather than a number going up:
///
/// 1. CONDITION AND CANNIBALISATION. A salvaged part arrives worn, and fitting it costs
///    scrap, hours and the part itself. Whether it is worth doing is not "is the number
///    bigger" - it is "how many flight hours does this actually buy", and because wear
///    accelerates as a component degrades (<see cref="LifeHours"/>), a barely-better part
///    buys almost nothing. Flying on the tired one you already have is frequently right.
///
/// 2. SITE CHARACTER. A wreck has aircraft parts and all of them have been through a
///    crash. A depot has crates in good order but nothing that was ever flown. A workshop
///    has three things and they are overhauled. A farmstead has food and farm steel and
///    nobody out there owned a helicopter.
///
/// 3. WEIGHT AGAINST VALUE. Parts weigh what they really weigh and the aircraft has about
///    two hundred kilos of payload once it is fuelled. The knapsack is not a puzzle bolted
///    on - it is what falls out of a 4309 kg gross limit and a 105 kg rotor blade.
///
/// 4. TIER GATING. Hot sections, main transmissions and Doppler sets do not exist in the
///    starting regions. Wanting one is the pull outward that D-010 and D-003b are for.
///
/// <para><b>Things tried that did not work.</b></para>
///
/// <para>A flat wear rate. With <c>dh/dt</c> constant, a component at 0.40 still has 40%
/// of a new one's life, so any part better than the fitted one is worth fitting and there
/// is no decision at all - just a greedy sort. The accelerating rate plus a per-component
/// UNSERVICEABLE FLOOR is what produces the cliff: a main rotor at 0.40 has 18 flight
/// hours left, not 60.</para>
///
/// <para>Making the floors up. The first pass invented "dead at 0.0" for every component,
/// which made hours-remaining a fiction - the aircraft is not flyable at 0.10 of main
/// rotor. The floors below are the thresholds <c>Damage.cs</c> ALREADY uses in
/// <c>Airworthy</c>, <c>SkidsServiceable</c> and <c>AvionicsWorking</c>, so "hours left"
/// means "hours until this stops doing its job", which is a fact rather than a flavour
/// number. <see cref="UnserviceableAt"/> forwards to <c>DamageState.UnserviceableAt</c>
/// so there is exactly one table.</para>
///
/// <para>Linear part value. Pricing a part at <c>Value * Condition</c> made stripping a
/// wreck for its heaviest components always correct, because a 0.30 transmission at 30%
/// of 200 was still the best thing in the pile per kilo. The exponent of 1.8 is chosen so
/// that value per kilo tracks hours-per-kilo rather than mass, which is what a buyer is
/// actually paying for.</para>
///
/// <para><c>System.Random</c> for the rolls. It is what <c>Loadout.ModuleAtSite</c> uses,
/// and it works, but the framework does not guarantee the sequence across .NET versions -
/// and a world that quietly reshuffles on a runtime upgrade would invalidate every save.
/// <see cref="Hash"/> is a fixed integer mix that will give the same world in ten years.</para>
/// </summary>
public static class Salvage
{
    // ------------------------------------------------------------ the aircraft

    /// <summary>
    /// Maximum gross weight, kg. A UH-1H is 9500 lb, and the Workhorse is that aircraft
    /// in all but name.
    ///
    /// This is the number that makes salvage a decision. Structure is about 3290 kg and a
    /// full internal tank is 800 kg, which leaves roughly 220 kg - two rotor blades, or
    /// one transmission, or eleven jerrycans, and not two of those things.
    /// </summary>
    public const double MaxGrossMass = 4309.0;

    /// <summary>Payload still available at a given all-up mass. Negative means overweight.</summary>
    public static double PayloadRemaining(double grossMass) => MaxGrossMass - grossMass;

    // ------------------------------------------------------------------- wear

    /// <summary>
    /// The health at which a component stops doing its job.
    ///
    /// <para>ONE source of truth, and it is <c>Damage.cs</c>. This used to be a second
    /// copy of the table, mirrored by hand with comments pointing back at
    /// <c>Airworthy</c>, <c>SkidsServiceable</c> and <c>AvionicsWorking</c> - which is
    /// exactly the arrangement that lets "hours until this stops doing its job" quietly
    /// stop meaning that. The thresholds are now named constants on
    /// <see cref="DamageState"/> and this forwards to them, so the two files cannot
    /// drift.</para>
    ///
    /// <para>Kept as a method here rather than deleted because every caller in this file
    /// - <see cref="LifeHours"/>, <see cref="Preview"/>, the kneeboard - is asking a
    /// salvage question, and routing them all through one call is what makes the
    /// forwarding provable in a test.</para>
    /// </summary>
    public static double UnserviceableAt(Component c) => DamageState.UnserviceableAt(c);

    /// <summary>
    /// Health lost per flight hour by a brand new component, before the wear multiplier.
    ///
    /// Rotor-turning hours, not wall clock. Calibrated so that a new main rotor is good
    /// for about 150 hours of flying, which at the measured 100 kt cruise and a 13 km
    /// content envelope (D-003b) is a great many sorties - D-007 asks for a loop that is
    /// core but forgiving, and a blade that needs replacing every third flight is neither.
    /// </summary>
    public static double BaseWearPerHour(Component c) => c switch
    {
        Component.MainRotor    => 0.0035,
        Component.TailRotor    => 0.0045,
        Component.Engine       => 0.0050,
        Component.Transmission => 0.0030,
        Component.Skids        => 0.0012,
        Component.FuelSystem   => 0.0020,
        Component.Hydraulics   => 0.0040,
        Component.Avionics     => 0.0015,
        Component.Fuselage     => 0.0010,
        _ => 0.0020,
    };

    /// <summary>
    /// How much faster a worn component wears than a new one.
    ///
    /// This is the shape that turns condition from a number into a decision. A tired
    /// bearing runs hot, a nicked blade is out of track and shakes everything around it,
    /// a leaking servo works harder - degradation is self-accelerating on real machinery,
    /// and modelling it means a half-worn part is worth much less than half a good one.
    /// </summary>
    public static double WearMultiplier(double health)
        => 1.0 + 3.0 * (1.0 - Math.Clamp(health, 0, 1)) * (1.0 - Math.Clamp(health, 0, 1));

    /// <summary>
    /// Flight hours from this health until the component is unserviceable.
    ///
    /// Closed form of <c>dh/dt = -b (1 + 3(1-h)^2)</c>, integrated between the floor and
    /// the current health. Analytic rather than stepped so it is exact, allocation-free
    /// and safe to call from a UI every frame.
    /// </summary>
    public static double LifeHours(Component c, double health)
    {
        double floor = UnserviceableAt(c);
        double h = Math.Clamp(health, 0, 1);
        if (h <= floor) return 0;
        double b = BaseWearPerHour(c);
        // Integral of du / (1 + 3u^2) is atan(u*sqrt3)/sqrt3, with u = 1 - h.
        const double S3 = 1.7320508075688772;
        double F(double hh) => Math.Atan((1.0 - hh) * S3) / S3;
        return (F(floor) - F(h)) / b;
    }

    /// <summary>
    /// Wear a component by flying it for some hours. Returns the health lost.
    ///
    /// <para>Stepped rather than inverted analytically because the caller applies the
    /// result through the damage log, so the wear looks exactly like every other source
    /// of damage. That caller is <c>DamageState.AccrueFlightHours</c>, and it is the ONLY
    /// one: the aircraft's Hobbs meter runs inside <c>DamageState.UpdateSystems</c> while
    /// the rotor turns and the hours are charged the moment it stops. There is one wear
    /// model, and this is it.</para>
    ///
    /// <para>Do not call this per frame. A per-frame wear tick perturbed main rotor
    /// health by 1.5e-4 after a minute of flying and moved the measured autorotation
    /// descent from 3193 to 3859 fpm; the autorotation equilibrium is knife-edged enough
    /// that a rounding error in blade condition relocates it. See
    /// <c>DamageState.AccrueFlightHours</c> for the full account.</para>
    /// </summary>
    public static double WearOver(Component c, double health, double hours)
    {
        double h = Math.Clamp(health, 0, 1);
        double b = BaseWearPerHour(c);
        const double step = 0.05;   // 3 minutes; the curve is smooth, this is plenty
        double t = 0;
        while (t < hours && h > 0)
        {
            double dt = Math.Min(step, hours - t);
            h = Math.Max(0, h - b * WearMultiplier(h) * dt);
            t += dt;
        }
        return Math.Clamp(health, 0, 1) - h;
    }

    // -------------------------------------------------------------- the parts

    /// <summary>
    /// Everything that can be pulled out of the world and bolted on.
    ///
    /// Twenty parts over nine components. Deliberately concrete and deliberately of
    /// wildly different mass: the interesting cargo decision only exists because an
    /// attitude reference is seven kilos and a main transmission is a hundred and eighty.
    /// </summary>
    public static readonly IReadOnlyList<PartDef> All = new PartDef[]
    {
        //     id              name                       fits                    kg  tier value rarity
        new("blade_main",     "Main rotor blade",        Component.MainRotor,    105, 1, 190, 1.00,
            "One of two. Fitting one means tracking both."),
        new("head_teeter",    "Teetering head assembly", Component.MainRotor,    140, 2, 260, 0.45,
            "The whole hub. Nobody builds these any more."),
        new("blade_comp",     "Composite main blade",    Component.MainRotor,     88, 3, 340, 0.25,
            "Lighter than the metal blade and it does not corrode. Late-war stock."),

        new("blade_tail",     "Tail rotor blade set",    Component.TailRotor,     28, 0,  70, 1.00,
            "Small, light, and the thing that most often comes back bent."),
        new("gearbox_tail",   "90 degree gearbox",       Component.TailRotor,     34, 1, 120, 0.70,
            "At the top of the fin. Everything that hits the fin hits this."),

        new("engine_hot",     "Turbine hot section",     Component.Engine,        95, 2, 300, 0.50,
            "The expensive half of the engine and the half that wears out."),
        new("engine_acc",     "Accessory gearbox",       Component.Engine,        42, 1, 140, 0.80,
            "Drives the pumps. Without it the engine runs and nothing else does."),

        new("xmsn_main",      "Main transmission",       Component.Transmission, 180, 2, 330, 0.40,
            "Two hundred kilos of gears. You will need the hoist or a crowd."),
        new("xmsn_input",     "Freewheeling unit",       Component.Transmission,  38, 1, 160, 0.75,
            "The part that lets you autorotate. Worth having a spare."),

        new("skid_set",       "Skid tube pair",          Component.Skids,         55, 0,  55, 1.00,
            "Straight ones are rarer than you would think."),
        new("skid_shoe",      "Skid shoes and crosstube",Component.Skids,         22, 0,  35, 0.90,
            "What you actually need after a hard one."),

        new("cell_fuel",      "Fuel cell bladder",       Component.FuelSystem,    26, 0,  80, 0.90,
            "Rubber. Perishes whether it is flown or not."),
        new("pump_fuel",      "Boost pump",              Component.FuelSystem,     9, 0,  60, 1.00,
            "Small and light and the aircraft will not start without one."),

        new("hyd_pack",       "Hydraulic power pack",    Component.Hydraulics,    31, 1, 150, 0.85,
            "Pump, reservoir and filter in one lump."),
        new("hyd_servo",      "Cyclic servo actuator",   Component.Hydraulics,    14, 1, 130, 0.90,
            "One of three. They fail one at a time and you feel it."),

        new("avi_radio",      "VHF transceiver",         Component.Avionics,      11, 0,  95, 1.00,
            "A frequency is worth nothing without one of these."),
        new("avi_dg",         "Directional gyro",        Component.Avionics,       6, 0,  80, 0.95,
            "Six kilos. Tells you where the nose is pointing in cloud."),
        new("avi_ah",         "Attitude reference",      Component.Avionics,       7, 1, 180, 0.60,
            "Seven kilos and it is the best value per kilo in the country."),
        new("avi_dopp",       "Doppler navigation set",  Component.Avionics,      18, 3, 420, 0.30,
            "Knows where you are with no chart and no stars. Only ever fitted to the good ones."),

        new("panel_skin",     "Airframe skin panels",    Component.Fuselage,      40, 0,  45, 1.00,
            "Forty kilos of flat aluminium. Cheap, heavy, and it is what stops the drag."),
    };

    public static readonly IReadOnlyDictionary<string, PartDef> Catalog;

    static Salvage()
    {
        var dict = new Dictionary<string, PartDef>();
        foreach (PartDef p in All) dict[p.Id] = p;
        Catalog = dict;
    }

    /// <summary>The highest tier at which anything exists. Used by tests and by the map.</summary>
    public static int MaxTier { get { int t = 0; foreach (var p in All) if (p.Tier > t) t = p.Tier; return t; } }

    // -------------------------------------------------------- the yield tables

    private static readonly Component[] Aircraft =
    {
        Component.MainRotor, Component.TailRotor, Component.Engine, Component.Transmission,
        Component.Hydraulics, Component.Avionics, Component.FuelSystem,
        Component.Skids, Component.Fuselage,
    };

    /// <summary>
    /// What this kind of place gives up. The whole of the site-character design is here,
    /// and it is data, so it is cheap to argue with.
    /// </summary>
    public static SiteYield Yield(SalvageSiteKind kind) => kind switch
    {
        // Aircraft parts and nothing else, and every one of them has been through a crash.
        // This is the only place a rotor system comes from - which is why wrecks come in
        // fields rather than a sprinkle (D-017) and why one of them is worth a detour.
        SalvageSiteKind.Wreck => new SiteYield(
            SearchesMin: 1, SearchesMax: 4, ScrapMin: 6, ScrapMax: 16,
            PartChance: 0.55, ConditionMin: 0.12, ConditionMax: 0.58,
            FuelChance: 0.10, MedicalChance: 0.10, FoodChance: 0.02, AmmoChance: 0.05,
            Components: new[] { Component.MainRotor, Component.TailRotor, Component.Transmission,
                                Component.Hydraulics, Component.Skids, Component.Fuselage,
                                Component.Engine },
            Character: "aircraft parts, all of them through something"),

        // Crates, in order, in quantity. Nothing here ever flew, so no rotor system and no
        // avionics - but the engine and fuel-system stock is good and there is a lot of it.
        // Guarded, per the enum's own comment, which is what the tier gate is for.
        SalvageSiteKind.Depot => new SiteYield(
            SearchesMin: 4, SearchesMax: 9, ScrapMin: 10, ScrapMax: 24,
            PartChance: 0.40, ConditionMin: 0.55, ConditionMax: 0.92,
            FuelChance: 0.35, MedicalChance: 0.20, FoodChance: 0.15, AmmoChance: 0.30,
            Components: new[] { Component.Engine, Component.FuelSystem, Component.Hydraulics,
                                Component.Fuselage, Component.Skids },
            Character: "crated, indexed, and someone is still guarding it"),

        // Three things, and they are overhauled. A workshop is not a richer wreck: it is
        // the only place a part comes out at a condition worth the fitting, and it is the
        // only place the fit itself is clean (see FieldFitPenalty).
        SalvageSiteKind.Workshop => new SiteYield(
            SearchesMin: 1, SearchesMax: 3, ScrapMin: 3, ScrapMax: 8,
            PartChance: 0.35, ConditionMin: 0.78, ConditionMax: 1.00,
            FuelChance: 0.10, MedicalChance: 0.15, FoodChance: 0.10, AmmoChance: 0.05,
            Components: new[] { Component.Hydraulics, Component.Avionics, Component.TailRotor,
                                Component.FuelSystem },
            Character: "few, but overhauled, and there is a bench to fit them on"),

        // Food, diesel and farm steel. Nobody out here owned a helicopter, and a farmstead
        // that occasionally coughed up a rotor blade would make the whole map interchangeable.
        SalvageSiteKind.Farmstead => new SiteYield(
            SearchesMin: 0, SearchesMax: 3, ScrapMin: 2, ScrapMax: 6,
            PartChance: 0.08, ConditionMin: 0.30, ConditionMax: 0.70,
            FuelChance: 0.20, MedicalChance: 0.10, FoodChance: 0.55, AmmoChance: 0.10,
            Components: new[] { Component.Fuselage, Component.Skids },
            Character: "food, diesel and farm steel"),

        // The big prizes, and the only place that has everything at once.
        SalvageSiteKind.Airfield => new SiteYield(
            SearchesMin: 3, SearchesMax: 8, ScrapMin: 8, ScrapMax: 20,
            PartChance: 0.60, ConditionMin: 0.42, ConditionMax: 0.88,
            FuelChance: 0.45, MedicalChance: 0.15, FoodChance: 0.10, AmmoChance: 0.20,
            Components: Aircraft,
            Character: "everything, if you can get in and out"),

        SalvageSiteKind.FuelCache => new SiteYield(
            SearchesMin: 1, SearchesMax: 3, ScrapMin: 1, ScrapMax: 4,
            PartChance: 0.06, ConditionMin: 0.40, ConditionMax: 0.75,
            FuelChance: 0.85, MedicalChance: 0.02, FoodChance: 0.05, AmmoChance: 0.02,
            Components: new[] { Component.FuelSystem },
            Character: "cans, and the reason you flew here"),

        // People live here. What was loose has been taken, and taking the rest is a
        // different kind of decision from prising it out of a ruin.
        SalvageSiteKind.Settlement => new SiteYield(
            SearchesMin: 0, SearchesMax: 2, ScrapMin: 1, ScrapMax: 4,
            PartChance: 0.05, ConditionMin: 0.35, ConditionMax: 0.70,
            FuelChance: 0.08, MedicalChance: 0.30, FoodChance: 0.40, AmmoChance: 0.10,
            Components: new[] { Component.Fuselage },
            Character: "picked over by people who are still here"),

        // Racks of radio and most of it still works. A relay is where avionics come from
        // if you cannot get to a workshop, and it is a reason to climb (D-010).
        SalvageSiteKind.Relay => new SiteYield(
            SearchesMin: 1, SearchesMax: 3, ScrapMin: 2, ScrapMax: 7,
            PartChance: 0.30, ConditionMin: 0.50, ConditionMax: 0.88,
            FuelChance: 0.05, MedicalChance: 0.05, FoodChance: 0.05, AmmoChance: 0.05,
            Components: new[] { Component.Avionics },
            Character: "racks of radio, most of it still live"),

        // No loot, a view, and a survey point that fills in the chart. The enum says so.
        SalvageSiteKind.Overlook => new SiteYield(
            SearchesMin: 0, SearchesMax: 0, ScrapMin: 0, ScrapMax: 0,
            PartChance: 0, ConditionMin: 0, ConditionMax: 0,
            FuelChance: 0, MedicalChance: 0, FoodChance: 0, AmmoChance: 0,
            Components: Array.Empty<Component>(),
            Character: "nothing but the view"),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "SalvageSiteKind has drifted from the game layer's SiteKind - add the row."),
    };

    // --------------------------------------------------------------- the rolls

    /// <summary>
    /// A fixed integer mix. Not System.Random: the framework does not promise that
    /// sequence across versions, and a world that reshuffles on a runtime upgrade would
    /// invalidate every save in existence. This will give the same world in ten years.
    /// </summary>
    private static uint Hash(int a, int b, int c)
    {
        unchecked
        {
            uint x = (uint)a * 2654435761u ^ (uint)b * 2246822519u ^ (uint)c * 3266489917u;
            x ^= x >> 16; x *= 2246822519u;
            x ^= x >> 13; x *= 3266489917u;
            x ^= x >> 16;
            return x;
        }
    }

    /// <summary>Uniform in [0,1) from three integers. Deterministic and stateless.</summary>
    private static double Unit(int a, int b, int c) => Hash(a, b, c) / 4294967296.0;

    /// <summary>
    /// How many times this site can be searched before it is stripped. Deterministic by
    /// id, so a place the player remembers as picked clean stays picked clean without
    /// anything being written down.
    /// </summary>
    public static int SearchesAt(SalvageSiteKind kind, int siteId)
    {
        SiteYield y = Yield(kind);
        if (y.SearchesMax <= 0) return 0;
        int span = y.SearchesMax - y.SearchesMin + 1;
        return y.SearchesMin + (int)(Unit(siteId, 0x5A1, 7) * span);
    }

    /// <summary>
    /// What the nth search of this site turns up.
    ///
    /// Pure: same arguments, same find, forever. Nothing here reads or writes state, so
    /// the caller owns the "how many searches are left" bookkeeping (which already exists
    /// as <c>SiteRecord.SalvageRemaining</c>) and a headless test can ask what the fourth
    /// search of site 91 gives without simulating its way there.
    /// </summary>
    public static SalvageFind Search(SalvageSiteKind kind, int siteTier, int siteId, int searchIndex)
    {
        SiteYield y = Yield(kind);
        if (y.SearchesMax <= 0) return default;

        int s = searchIndex;
        double scrap = Math.Round(y.ScrapMin + Unit(siteId, s, 11) * (y.ScrapMax - y.ScrapMin));

        int fuel = Unit(siteId, s, 23) < y.FuelChance ? 1 : 0;
        int med = Unit(siteId, s, 29) < y.MedicalChance ? 1 : 0;
        int food = Unit(siteId, s, 31) < y.FoodChance ? 1 + (int)(Unit(siteId, s, 37) * 3) : 0;
        int ammo = Unit(siteId, s, 41) < y.AmmoChance ? 4 + (int)(Unit(siteId, s, 43) * 20) : 0;

        SalvagePart? part = null;
        if (Unit(siteId, s, 47) < y.PartChance)
            part = RollPart(y, siteTier, siteId, s);

        return new SalvageFind(scrap, fuel, med, food, ammo, part);
    }

    /// <summary>
    /// Pick one part from what this place could have, at a tier it is allowed to have.
    ///
    /// The tier gate is applied AFTER the component is chosen, not before. That is
    /// deliberate: it means a tier-0 wreck still offers main rotor parts, just never the
    /// composite blade or the teetering head. Filtering the component list first would
    /// have made low-tier wrecks stop offering rotor systems at all, which reads as the
    /// site kind changing rather than as the good stuff being further out.
    /// </summary>
    private static SalvagePart? RollPart(SiteYield y, int siteTier, int siteId, int s)
    {
        if (y.Components.Length == 0) return null;
        Component comp = y.Components[(int)(Unit(siteId, s, 53) * y.Components.Length)];

        double total = 0;
        foreach (PartDef p in All)
            if (p.Fits == comp && p.Tier <= siteTier) total += p.Rarity;
        if (total <= 0) return null;

        double pick = Unit(siteId, s, 59) * total;
        PartDef chosen = default;
        bool got = false;
        foreach (PartDef p in All)
        {
            if (p.Fits != comp || p.Tier > siteTier) continue;
            pick -= p.Rarity;
            if (pick <= 0) { chosen = p; got = true; break; }
        }
        if (!got) return null;

        double cond = y.ConditionMin + Unit(siteId, s, 61) * (y.ConditionMax - y.ConditionMin);

        // Deeper country holds up better. Not because the weather is kinder, but because
        // the places worth defending were the places worth maintaining - and it gives the
        // tier gate a second, softer edge so that even a common part is better out there.
        cond = Math.Clamp(cond + 0.03 * siteTier, 0, 1);

        return new SalvagePart(chosen, cond);
    }

    // ----------------------------------------------------------- fitting parts

    /// <summary>
    /// Condition lost fitting a part anywhere but on a workshop bench.
    ///
    /// A field fit is done with the tools you carry, on a part you cannot properly test,
    /// by one person. Ten points of condition is the price of not flying to a workshop
    /// first - and on a marginal part it is the whole difference, which is exactly what
    /// makes a workshop somewhere you go rather than somewhere you pass.
    /// </summary>
    public const double FieldFitPenalty = 0.10;

    /// <summary>Scrap consumed fitting a part: shims, fasteners, safety wire, seals.</summary>
    public static double FitScrapCost(PartDef def) => Math.Ceiling(2 + def.Mass * 0.06);

    /// <summary>Scrap you get back for the component you pulled out. A dead one is still metal.</summary>
    public static double FitScrapRecovered(PartDef def, double oldHealth)
        => Math.Floor(def.Mass * (0.05 + 0.10 * Math.Clamp(oldHealth, 0, 1)));

    /// <summary>Flight-crew hours to do the job. Roughly linear in how heavy the thing is.</summary>
    public static double FitLabourHours(PartDef def) => 0.5 + def.Mass * 0.02;

    /// <summary>
    /// Hours gained below which a swap is judged Marginal rather than Worthwhile.
    ///
    /// Three flight hours. A sortie out to a tier-1 site and back is well under an hour,
    /// so three hours is "this buys you a few trips" - below that you have spent the part,
    /// the scrap and the afternoon to move a number.
    /// </summary>
    public const double MarginalHours = 3.0;

    /// <summary>
    /// What fitting this part would do, computed and not applied.
    ///
    /// The interesting output is <see cref="FitPreview.HoursGained"/>, not the health
    /// delta. Health is a number on a screen; hours are the thing the player is actually
    /// buying, and because wear accelerates they are not proportional to each other. A
    /// main rotor going 0.40 -> 0.50 is ten points and eight hours. The same ten points
    /// from 0.85 -> 0.95 is twenty-nine.
    /// </summary>
    public static FitPreview Preview(DamageState damage, SalvagePart part,
                                     bool atWorkshop, double scrapAvailable)
    {
        Component c = part.Def.Fits;
        double oldHealth = damage.Health(c);
        double newHealth = Math.Clamp(part.Condition - (atWorkshop ? 0 : FieldFitPenalty), 0, 1);

        double oldLife = LifeHours(c, oldHealth);
        double newLife = LifeHours(c, newHealth);
        double cost = FitScrapCost(part.Def);
        double back = FitScrapRecovered(part.Def, oldHealth);
        double labour = FitLabourHours(part.Def);

        FitVerdict verdict;
        string reason;
        if (newHealth <= oldHealth + 1e-9)
        {
            verdict = FitVerdict.NotAnUpgrade;
            reason = atWorkshop
                ? $"the one fitted is already at {oldHealth * 100:F0}%"
                : $"a field fit lands it at {newHealth * 100:F0}%, no better than what is in there";
        }
        else if (scrapAvailable < cost)
        {
            verdict = FitVerdict.NotEnoughScrap;
            reason = $"needs {cost:F0} scrap, you have {scrapAvailable:F0}";
        }
        else if (newLife - oldLife < MarginalHours)
        {
            verdict = FitVerdict.Marginal;
            reason = $"buys {newLife - oldLife:F1} flight hours for {cost:F0} scrap and a whole part";
        }
        else
        {
            verdict = FitVerdict.Worthwhile;
            reason = $"buys {newLife - oldLife:F0} flight hours";
        }

        return new FitPreview(c, oldHealth, newHealth, oldLife, newLife,
                              cost, back, labour, verdict, reason);
    }

    /// <summary>
    /// Actually fit it. The part is consumed and the old component is gone for scrap.
    ///
    /// Will do a Marginal fit, because <see cref="Preview"/> reports facts and the player
    /// does the thinking (D-005a). It will not do a NotAnUpgrade or an unaffordable one,
    /// because those are not decisions, they are mistakes with no upside.
    ///
    /// Scrap is NOT moved here. The caller holds the Progress object and spends
    /// <see cref="FitPreview.ScrapCost"/> and credits <see cref="FitPreview.ScrapRecovered"/>
    /// itself, which keeps this file free of any opinion about inventory.
    /// </summary>
    public static FitPreview Fit(DamageState damage, Cargo cargo, SalvagePart part,
                                 bool atWorkshop, double scrapAvailable)
    {
        FitPreview p = Preview(damage, part, atWorkshop, scrapAvailable);
        if (!p.Possible) return p;

        cargo.Drop(part.Def.Id);
        damage.Repair(p.Component, p.NewHealth - p.OldHealth);
        return p;
    }

    // ------------------------------------------------------- weight and value

    /// <summary>
    /// The most valuable set of parts that fits inside a mass budget.
    ///
    /// An exact 0/1 knapsack on one-kilogram buckets. Included not because the player
    /// should be handed the answer, but because the interesting claim of this whole file
    /// is that "what do I take" has a NON-OBVIOUS answer - and the only way to assert
    /// that is to have ground truth to compare the obvious answers against.
    ///
    /// Ties break toward the earlier part in the offered order, which makes it stable.
    /// </summary>
    public static List<SalvagePart> BestWithin(IReadOnlyList<SalvagePart> offered, double massBudget)
    {
        int cap = (int)Math.Floor(massBudget);
        var result = new List<SalvagePart>();
        if (cap <= 0 || offered.Count == 0) return result;

        int n = offered.Count;
        var best = new double[n + 1, cap + 1];
        for (int i = 1; i <= n; i++)
        {
            int w = (int)Math.Ceiling(offered[i - 1].Mass);
            double v = offered[i - 1].Value;
            for (int c = 0; c <= cap; c++)
            {
                double skip = best[i - 1, c];
                double take = w <= c ? best[i - 1, c - w] + v : double.NegativeInfinity;
                best[i, c] = take > skip ? take : skip;
            }
        }

        int cc = cap;
        for (int i = n; i >= 1; i--)
        {
            if (best[i, cc] > best[i - 1, cc])
            {
                result.Add(offered[i - 1]);
                cc -= (int)Math.Ceiling(offered[i - 1].Mass);
            }
        }
        result.Reverse();
        return result;
    }

    /// <summary>
    /// The obvious wrong answer: take the most valuable thing you can still lift.
    /// Kept in the model rather than in the test because it is what a player does on
    /// their first trip, and the gap between this and <see cref="BestWithin"/> is the
    /// size of the lesson.
    /// </summary>
    public static List<SalvagePart> GreedyByValue(IReadOnlyList<SalvagePart> offered, double massBudget)
        => GreedyBy(offered, massBudget, p => p.Value);

    /// <summary>What a player does on their tenth trip: sort by value per kilo.</summary>
    public static List<SalvagePart> GreedyByDensity(IReadOnlyList<SalvagePart> offered, double massBudget)
        => GreedyBy(offered, massBudget, p => p.ValuePerKg);

    private static List<SalvagePart> GreedyBy(IReadOnlyList<SalvagePart> offered,
                                              double massBudget, Func<SalvagePart, double> key)
    {
        var order = new List<int>();
        for (int i = 0; i < offered.Count; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            int cmp = key(offered[b]).CompareTo(key(offered[a]));
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        var taken = new List<SalvagePart>();
        double left = massBudget;
        foreach (int i in order)
            if (offered[i].Mass <= left) { taken.Add(offered[i]); left -= offered[i].Mass; }
        return taken;
    }

    public static double TotalMass(IEnumerable<SalvagePart> parts)
    {
        double m = 0; foreach (var p in parts) m += p.Mass; return m;
    }

    public static double TotalValue(IEnumerable<SalvagePart> parts)
    {
        double v = 0; foreach (var p in parts) v += p.Value; return v;
    }
}
