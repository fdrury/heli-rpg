using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The salvage and parts economy, measured.
///
/// The claim <c>Salvage.cs</c> makes is that scarcity is real: that a place has a
/// character, that a worn part is a decision rather than a discount, that weight costs
/// performance, and that some things only exist out where it is dangerous. Each of those
/// is checked against something defensible here - and the weight one is checked against
/// the actual flight model rather than against a constant, because a mass budget that
/// nothing measures is just a number in a spreadsheet.
/// </summary>
public static class SalvageTests
{
    private static readonly SalvageSiteKind[] AllKinds = Enum.GetValues<SalvageSiteKind>();

    /// <summary>Sweep many searches of many sites of one kind and summarise the haul.</summary>
    private sealed class Sweep
    {
        public int Searches, Parts, Fuel, Medical, Food, Ammo;
        public double Scrap, Condition, PartMass;
        public readonly Dictionary<Component, int> Components = new();
        public readonly Dictionary<string, int> Ids = new();

        public double PartRate => Searches == 0 ? 0 : (double)Parts / Searches;
        public double ScrapPerSearch => Searches == 0 ? 0 : Scrap / Searches;
        public double MeanCondition => Parts == 0 ? 0 : Condition / Parts;
        public double MeanPartMass => Parts == 0 ? 0 : PartMass / Parts;
        public double FuelRate => Searches == 0 ? 0 : (double)Fuel / Searches;
        public double FoodRate => Searches == 0 ? 0 : (double)Food / Searches;
    }

    private static Sweep Run(SalvageSiteKind kind, int tier, int sites = 400)
    {
        var s = new Sweep();
        for (int id = 0; id < sites; id++)
        {
            int n = Salvage.SearchesAt(kind, id);
            for (int i = 0; i < n; i++)
            {
                SalvageFind f = Salvage.Search(kind, tier, id, i);
                s.Searches++;
                s.Scrap += f.Scrap;
                if (f.FuelCans > 0) s.Fuel++;
                if (f.Medical > 0) s.Medical++;
                if (f.Food > 0) s.Food++;
                if (f.Ammunition > 0) s.Ammo++;
                if (f.Part is SalvagePart p)
                {
                    s.Parts++;
                    s.Condition += p.Condition;
                    s.PartMass += p.Mass;
                    s.Components[p.Def.Fits] = s.Components.GetValueOrDefault(p.Def.Fits) + 1;
                    s.Ids[p.Def.Id] = s.Ids.GetValueOrDefault(p.Def.Id) + 1;
                }
            }
        }
        return s;
    }

    // ------------------------------------------------------------------ catalog

    public static string? Catalog()
    {
        Console.WriteLine("  part catalog:");
        Console.WriteLine("    id            component       kg   tier   value  val/kg  life@100%");

        var ids = new HashSet<string>();
        var covered = new HashSet<Component>();
        foreach (PartDef d in Salvage.All)
        {
            if (!ids.Add(d.Id)) return $"duplicate part id '{d.Id}'";
            if (d.Mass <= 0) return $"part '{d.Id}' has non-positive mass";
            if (d.Value <= 0) return $"part '{d.Id}' has non-positive value";
            if (d.Rarity <= 0) return $"part '{d.Id}' has non-positive rarity";
            if (d.Tier < 0 || d.Tier > 3) return $"part '{d.Id}' tier {d.Tier} outside 0..3";
            covered.Add(d.Fits);

            var fresh = new SalvagePart(d, 1.0);
            Console.WriteLine($"    {d.Id,-13} {d.Fits,-13} {d.Mass,5:F0} {d.Tier,5}  {d.Value,6:F0} " +
                              $"{fresh.ValuePerKg,7:F2} {Salvage.LifeHours(d.Fits, 1.0),9:F0} h");
        }

        // Every component that Damage.cs tracks must be salvageable, or a hit to it is a
        // dead end with no route back to serviceable - which is a punishment, not a gate.
        foreach (Component c in Enum.GetValues<Component>())
            if (!covered.Contains(c))
                return $"no salvageable part fits {c} — damage to it would be unrepairable";

        // Every site kind must have a yield row, including the ones that give nothing.
        foreach (SalvageSiteKind k in AllKinds)
        {
            SiteYield y = Salvage.Yield(k);
            if (y.SearchesMax < y.SearchesMin) return $"{k} has SearchesMax < SearchesMin";
            if (y.ConditionMax < y.ConditionMin) return $"{k} has ConditionMax < ConditionMin";
            if (string.IsNullOrWhiteSpace(y.Character)) return $"{k} has no character line";
        }

        Console.WriteLine($"  {Salvage.All.Count} parts covering all " +
                          $"{Enum.GetValues<Component>().Length} components, " +
                          $"{AllKinds.Length} site kinds all have yield rows");

        if (Salvage.MaxTier < 3)
            return $"nothing exists above tier {Salvage.MaxTier} — there is nothing to fly out for";

        return null;
    }

    // ----------------------------------------------------------- site character

    public static string? SiteCharacter()
    {
        Console.WriteLine("  400 sites of each kind, tier 1, every search taken:");
        Console.WriteLine("    kind          searches  scrap/s  part%  cond   kg/part  fuel%  food%");

        var sweeps = new Dictionary<SalvageSiteKind, Sweep>();
        foreach (SalvageSiteKind k in AllKinds)
        {
            Sweep s = Run(k, 1);
            sweeps[k] = s;
            Console.WriteLine($"    {k,-13} {s.Searches,8} {s.ScrapPerSearch,8:F1} " +
                              $"{s.PartRate,5:P0} {s.MeanCondition,6:F2} {s.MeanPartMass,8:F0} " +
                              $"{s.FuelRate,6:P0} {s.FoodRate,6:P0}");
        }

        Console.WriteLine();
        Console.WriteLine("  what each kind actually hands over:");
        foreach (SalvageSiteKind k in AllKinds)
        {
            Sweep s = sweeps[k];
            if (s.Components.Count == 0) { Console.WriteLine($"    {k,-13} (no parts)"); continue; }
            var top = s.Components.OrderByDescending(kv => kv.Value)
                                  .Select(kv => $"{kv.Key} {(double)kv.Value / s.Parts:P0}");
            Console.WriteLine($"    {k,-13} {string.Join(", ", top)}");
        }

        Sweep wreck = sweeps[SalvageSiteKind.Wreck];
        Sweep depot = sweeps[SalvageSiteKind.Depot];
        Sweep shop = sweeps[SalvageSiteKind.Workshop];
        Sweep farm = sweeps[SalvageSiteKind.Farmstead];
        Sweep cache = sweeps[SalvageSiteKind.FuelCache];
        Sweep look = sweeps[SalvageSiteKind.Overlook];

        // A wreck is where a rotor system comes from, and the only place. If a depot
        // yielded blades there would be no reason to go looking for wrecks at all.
        if (!wreck.Components.ContainsKey(Component.MainRotor))
            return "a wreck yielded no main rotor parts";
        if (depot.Components.ContainsKey(Component.MainRotor))
            return "a depot yielded main rotor parts — nothing there ever flew";

        // A workshop's parts are in a different class, not merely a better roll.
        if (shop.MeanCondition - wreck.MeanCondition < 0.30)
            return $"workshop parts (mean {shop.MeanCondition:F2}) are not meaningfully better " +
                   $"than wreck parts (mean {wreck.MeanCondition:F2})";

        // ...but it does not hand them over in quantity. A workshop is a destination,
        // not a warehouse.
        if (shop.Searches >= depot.Searches * 0.6)
            return $"a workshop supports {shop.Searches} searches vs a depot's {depot.Searches} — " +
                   "it is behaving like a richer depot rather than a specialist";

        // Nobody out at a farmstead owned a helicopter.
        if (farm.PartRate > wreck.PartRate * 0.35)
            return $"farmstead part rate {farm.PartRate:P0} is close to a wreck's {wreck.PartRate:P0}";
        if (farm.FoodRate < 0.35)
            return $"farmstead food rate {farm.FoodRate:P0} is too low for the one place that farms";

        // A fuel cache is the reason to fly anywhere at all (the enum's own words).
        if (cache.FuelRate < 0.70)
            return $"fuel cache yields a can only {cache.FuelRate:P0} of the time";
        foreach (SalvageSiteKind k in AllKinds)
            if (k != SalvageSiteKind.FuelCache && sweeps[k].FuelRate >= cache.FuelRate)
                return $"{k} yields fuel at least as often as a fuel cache";

        // No loot, a view, and a survey point.
        if (look.Searches != 0 || look.Parts != 0 || look.Scrap != 0)
            return "an overlook yielded loot";

        return null;
    }

    // --------------------------------------------------- condition and wear-out

    public static string? ConditionCliff()
    {
        Console.WriteLine("  flight hours remaining vs condition (hours until unserviceable):");
        Console.WriteLine("    component       100%    85%    70%    55%    40%   floor");

        foreach (Component c in Enum.GetValues<Component>())
        {
            Console.Write($"    {c,-13}");
            foreach (double h in new[] { 1.00, 0.85, 0.70, 0.55, 0.40 })
                Console.Write($" {Salvage.LifeHours(c, h),6:F0}");
            Console.WriteLine($"  {Salvage.UnserviceableAt(c),6:F2}");
        }

        // The whole design rests on this being non-linear. A flat wear rate makes any
        // better part worth fitting and there is no decision left.
        foreach (Component c in Enum.GetValues<Component>())
        {
            double full = Salvage.LifeHours(c, 1.0);
            double floor = Salvage.UnserviceableAt(c);
            double mid = floor + (1.0 - floor) * 0.5;          // exactly half way to the floor
            double midLife = Salvage.LifeHours(c, mid);
            double share = midLife / full;
            if (share > 0.42)
                return $"{c} at half-way to the floor still has {share:P0} of its life — " +
                       "wear is too close to linear for condition to be a decision";
            if (share < 0.15)
                return $"{c} at half-way to the floor has only {share:P0} of its life — " +
                       "the cliff is so steep that anything but an overhauled part is worthless";
        }

        Console.WriteLine();
        Console.WriteLine("  half-way-to-floor life as a share of new:");
        foreach (Component c in Enum.GetValues<Component>())
        {
            double floor = Salvage.UnserviceableAt(c);
            double mid = floor + (1.0 - floor) * 0.5;
            Console.WriteLine($"    {c,-13} {Salvage.LifeHours(c, mid) / Salvage.LifeHours(c, 1.0),6:P0}");
        }

        // The number a player reads off the kneeboard is the CONDITION, not the distance
        // to the floor - so the cliff has to be visible at a round condition too. A part
        // at 40% must be worth well under half a good one, or "it says 40, that is nearly
        // half" is a reasonable reading and the economy has lied to them.
        Console.WriteLine();
        Console.WriteLine("  life at condition 0.40 as a share of new:");
        foreach (Component c in Enum.GetValues<Component>())
        {
            double share = Salvage.LifeHours(c, 0.40) / Salvage.LifeHours(c, 1.0);
            Console.WriteLine($"    {c,-13} {share,6:P0}");
            if (share > 0.25)
                return $"{c} at 40% condition still has {share:P0} of a new one's life";
        }

        // Nothing is left with hours once it is at or below the floor.
        foreach (Component c in Enum.GetValues<Component>())
            if (Salvage.LifeHours(c, Salvage.UnserviceableAt(c)) != 0)
                return $"{c} reports life remaining at its unserviceable threshold";

        // The stepped wear used by the game must land where the analytic life says it
        // will. These are two independent implementations of the same differential
        // equation and disagreement means one of them is wrong.
        Console.WriteLine();
        Console.WriteLine("    component      analytic h   stepped h   error");
        foreach (Component c in Enum.GetValues<Component>())
        {
            double predicted = Salvage.LifeHours(c, 1.0);
            double floor = Salvage.UnserviceableAt(c);
            double h = 1.0, t = 0;
            while (h > floor && t < predicted * 4)
            {
                h -= Salvage.WearOver(c, h, 0.25);
                t += 0.25;
            }
            double err = Math.Abs(t - predicted) / predicted;
            Console.WriteLine($"    {c,-13} {predicted,10:F1} {t,11:F1} {err,7:P1}");
            if (err > 0.05)
                return $"{c}: stepped wear reached the floor at {t:F1} h, analytic says {predicted:F1} h";
        }

        return null;
    }

    // ------------------------------------------------------- cannibalisation

    public static string? FitDecision()
    {
        Console.WriteLine("  fitting a salvaged main rotor blade over what is already on the aircraft:");
        Console.WriteLine("    fitted  part  where      ->health  old h   new h   gained  scrap  verdict");

        PartDef blade = Salvage.Catalog["blade_main"];
        var rows = new (double Fitted, double Cond, bool Shop)[]
        {
            (0.40, 0.45, false), (0.40, 0.45, true),
            (0.40, 0.60, false), (0.40, 0.60, true),
            (0.40, 0.90, false), (0.40, 0.90, true),
            (0.85, 0.90, false), (0.85, 0.90, true),
        };

        FitPreview? marginalField = null, worthwhileShop = null;
        foreach (var (fitted, cond, shop) in rows)
        {
            var dmg = new DamageState();
            dmg.Apply(Component.MainRotor, 1.0 - fitted, DamageCause.Wear);
            var part = new SalvagePart(blade, cond);
            FitPreview p = Salvage.Preview(dmg, part, shop, 100);

            Console.WriteLine($"    {fitted,6:F2} {cond,5:F2}  {(shop ? "workshop" : "field   "),-9} " +
                              $"{p.NewHealth,8:F2} {p.OldLifeHours,6:F1} {p.NewLifeHours,7:F1} " +
                              $"{p.HoursGained,8:F1} {p.ScrapCost,6:F0}  {p.Verdict} — {p.Reason}");

            if (Math.Abs(fitted - 0.40) < 1e-9 && Math.Abs(cond - 0.45) < 1e-9 && !shop) marginalField = p;
            if (Math.Abs(fitted - 0.40) < 1e-9 && Math.Abs(cond - 0.90) < 1e-9 && shop) worthwhileShop = p;
        }

        // A 0.45 blade over a 0.40 blade, fitted in the field, must not be an upgrade at
        // all: the field-fit penalty eats the whole difference. This is the case that
        // makes "leave it alone" a real answer.
        if (marginalField!.Value.Verdict != FitVerdict.NotAnUpgrade)
            return $"a 0.45 blade field-fitted over a 0.40 blade was judged " +
                   $"{marginalField.Value.Verdict}, not NotAnUpgrade";

        // An overhauled blade on a bench must be worth a great deal.
        if (worthwhileShop!.Value.Verdict != FitVerdict.Worthwhile)
            return $"an overhauled blade fitted at a workshop was judged {worthwhileShop.Value.Verdict}";
        if (worthwhileShop.Value.HoursGained < 100)
            return $"an overhauled blade over a 0.40 one bought only " +
                   $"{worthwhileShop.Value.HoursGained:F0} flight hours";

        // The workshop must be worth flying to. Measured as the difference the bench
        // makes on the SAME part over the SAME component.
        {
            var dmg = new DamageState();
            dmg.Apply(Component.MainRotor, 1.0 - 0.40, DamageCause.Wear);
            var part = new SalvagePart(blade, 0.70);
            FitPreview field = Salvage.Preview(dmg, part, false, 100);
            FitPreview shop = Salvage.Preview(dmg, part, true, 100);
            double gap = shop.HoursGained - field.HoursGained;
            Console.WriteLine();
            Console.WriteLine($"  same 0.70 blade: field buys {field.HoursGained:F1} h, " +
                              $"bench buys {shop.HoursGained:F1} h — the bench is worth {gap:F1} h");
            if (gap < 15)
                return $"a workshop is only worth {gap:F1} flight hours over a field fit — " +
                       "no reason to fly to one";
        }

        // Refusals.
        {
            var dmg = new DamageState();
            dmg.Apply(Component.MainRotor, 0.60, DamageCause.Wear);
            var part = new SalvagePart(blade, 0.90);
            FitPreview poor = Salvage.Preview(dmg, part, true, 3);
            Console.WriteLine($"  with 3 scrap in the bag: {poor.Verdict} — {poor.Reason}");
            if (poor.Verdict != FitVerdict.NotEnoughScrap)
                return $"fitting a {Salvage.FitScrapCost(blade):F0}-scrap part with 3 scrap was allowed";

            // Wrong component: a blade cannot mend an engine. Preview reports against the
            // component the part fits, so the engine must be untouched by fitting it.
            var eng = new DamageState();
            eng.Apply(Component.Engine, 0.50, DamageCause.Wear);
            FitPreview onEngine = Salvage.Preview(eng, part, true, 100);
            if (onEngine.Component != Component.MainRotor)
                return $"a main rotor blade previewed against {onEngine.Component}";
        }

        // Fit for real: the part is consumed, the health goes up, the scrap numbers are
        // reported for the caller to settle.
        {
            var dmg = new DamageState();
            dmg.Apply(Component.MainRotor, 1.0 - 0.30, DamageCause.Wear);
            var cargo = new Cargo();
            var part = new SalvagePart(blade, 0.88);
            cargo.Take(part);
            cargo.Take(new SalvagePart(Salvage.Catalog["avi_dg"], 0.9));
            double before = dmg.Health(Component.MainRotor);
            double massBefore = cargo.Mass;

            FitPreview done = Salvage.Fit(dmg, cargo, part, true, 100);
            Console.WriteLine();
            Console.WriteLine($"  fitted: rotor {before:F2} -> {dmg.Health(Component.MainRotor):F2}, " +
                              $"cargo {massBefore:F0} kg -> {cargo.Mass:F0} kg, " +
                              $"{done.ScrapCost:F0} scrap spent, {done.ScrapRecovered:F0} recovered, " +
                              $"{done.LabourHours:F1} h of work");

            if (Math.Abs(dmg.Health(Component.MainRotor) - 0.88) > 1e-6)
                return $"after a bench fit of a 0.88 blade the rotor is at " +
                       $"{dmg.Health(Component.MainRotor):F3}, not 0.88";
            if (cargo.Count != 1) return "the fitted part was not consumed from cargo";
            if (Math.Abs(massBefore - cargo.Mass - blade.Mass) > 0.01)
                return "cargo mass did not fall by the fitted part's mass";
            if (done.ScrapRecovered <= 0)
                return "the old component was thrown away for nothing";

            // Fitting the same part twice is impossible — it is gone.
            FitPreview again = Salvage.Fit(dmg, cargo, part, true, 100);
            if (again.Verdict != FitVerdict.NotAnUpgrade)
                return $"re-fitting a consumed part returned {again.Verdict}";
        }

        return null;
    }

    // ----------------------------------------------------- weight vs performance

    public static string? WeightCost()
    {
        var af = Airframe.Workhorse();
        double structure = af.Mass.TotalMass;
        double fullFuel = af.FuelCapacity;

        Console.WriteLine($"  structure {structure:F0} kg, internal fuel {fullFuel:F0} kg, " +
                          $"max gross {Salvage.MaxGrossMass:F0} kg");
        double payload = Salvage.PayloadRemaining(structure + fullFuel);
        Console.WriteLine($"  payload left with a full tank: {payload:F0} kg");

        if (payload <= 0)
            return $"the aircraft cannot carry anything at all with a full tank ({payload:F0} kg)";
        if (payload > 600)
            return $"{payload:F0} kg of spare payload — nothing the player picks up will ever matter";

        // What that budget actually buys, in parts.
        Console.WriteLine();
        Console.WriteLine("  what {0:F0} kg buys:", payload);
        foreach (PartDef d in Salvage.All.OrderBy(p => p.Mass))
            Console.WriteLine($"    {(int)(payload / d.Mass),3} x {d.Name,-26} ({d.Mass,3:F0} kg each)");

        if ((int)(payload / Salvage.Catalog["xmsn_main"].Mass) != 1)
            return "a main transmission is not a one-at-a-time decision at full fuel";

        // And what it costs, measured on the real flight model rather than asserted. This
        // is the point of the whole mass budget: D-003a says fuel is a LOAD system, and a
        // load system that nothing feels is a spreadsheet.
        //
        // MEASURED ON THE TRIM SOLUTION AND THE CEILING, NOT ON PowerRequired. The first
        // version of this test read PowerRequired off a pinned hover and got nonsense:
        // 100 kg raised it 0.9% at 300 m, 4.6% at 600 m, and at 400 kg the number went
        // DOWN, because the free-turbine governor is still hunting a tenth of a per cent
        // of Nr and PowerRequired is an instantaneous sample of whatever phase that
        // oscillation is in. Longer pins moved the answer instead of settling it. Same
        // family as D-042: measure the pinned condition, and the trim solution IS the
        // pinned condition - it converges to a residual of 0.00000 g and is monotone in
        // load to four decimal places.
        Console.WriteLine();
        Console.WriteLine("  collective required to hover at 300 m:");
        Console.WriteLine("    load kg   all-up kg   collective");

        double baseCollective = 0, lastCollective = -1;
        foreach (double load in new[] { 0.0, 50.0, 100.0, 150.0, 200.0 })
        {
            var h = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
            if (load > 0) h.Airframe.Mass.Add("salvage", new Vec3(0.5, 0, -0.4), load);
            h.InvalidateMass();
            h.PlaceInFlight(300);
            h.UseInternalGroundModel = false;

            TrimResult t = Trim.Solve(h, 300, 0);
            if (!t.Converged) return $"hover trim did not converge with {load:F0} kg aboard";

            double coll = t.Controls.Collective;
            Console.WriteLine($"    {load,7:F0} {h.TotalMass,11:F0} {coll,12:F4}");
            if (load == 0) baseCollective = coll;
            if (coll <= lastCollective)
                return $"{load:F0} kg needed no more collective than the load below it " +
                       $"({coll:F4} vs {lastCollective:F4}) — the mass is not reaching the rotor";
            lastCollective = coll;
        }

        double collCost = lastCollective - baseCollective;
        Console.WriteLine($"  200 kg costs {collCost:F4} of collective travel " +
                          $"({collCost / 200 * 1000:F3} per 1000 kg... per 100 kg: {collCost / 2:F4})");
        if (collCost < 0.010)
            return $"200 kg of salvage cost only {collCost:F4} of collective — not felt";

        // The number that makes it a routing decision rather than a stat. D-003b bounds
        // the content envelope with terrain above the aircraft's measured 3000 m hover
        // ceiling: a load that takes the ceiling below 3000 m physically closes off the
        // high country while it is aboard.
        Console.WriteLine();
        Console.WriteLine("  hover ceiling OGE, 125 m steps:");
        double emptyCeiling = 0, loadedCeiling = 0, prevCeiling = double.MaxValue;
        foreach (double load in new[] { 0.0, 100.0, 200.0 })
        {
            double ceiling = 0;
            for (double alt = 0; alt <= 5000; alt += 125)
            {
                var h = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
                if (load > 0) h.Airframe.Mass.Add("salvage", new Vec3(0.5, 0, -0.4), load);
                h.InvalidateMass();
                h.PlaceInFlight(alt);
                var ap = new Autopilot { CollectiveTrim = 0.6 };
                var demand = new AutopilotDemand
                    { Altitude = alt, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
                for (int i = 0; i < 9600; i++)   // 40 s at 240 Hz
                {
                    h.Input = ap.Update(h, demand, 1.0 / 240.0);
                    h.Step(1.0 / 240.0);
                }
                if (Math.Abs(h.State.Altitude - alt) < 8 && h.Input.Collective < 0.985) ceiling = alt;
                else break;
            }
            Console.WriteLine($"    {load,7:F0} kg  {ceiling,6:F0} m");
            if (ceiling >= prevCeiling)
                return $"{load:F0} kg aboard did not lower the hover ceiling " +
                       $"({ceiling:F0} m vs {prevCeiling:F0} m)";
            prevCeiling = ceiling;
            if (load == 0) emptyCeiling = ceiling;
            if (load == 200) loadedCeiling = ceiling;
        }

        double lost = emptyCeiling - loadedCeiling;
        Console.WriteLine($"  200 kg of salvage costs {lost:F0} m of hover ceiling " +
                          $"({emptyCeiling:F0} -> {loadedCeiling:F0})");
        if (emptyCeiling < 3000)
            return $"the empty aircraft only hovers to {emptyCeiling:F0} m — " +
                   "D-003b bounds the content envelope at a 3000 m ceiling";
        if (loadedCeiling >= 3000)
            return $"with 200 kg aboard it still hovers to {loadedCeiling:F0} m — " +
                   "carrying salvage does not close any of the map off";
        if (lost < 300)
            return $"200 kg cost only {lost:F0} m of ceiling";

        return null;
    }

    // ------------------------------------------------------------ what to take

    public static string? WhatToTake()
    {
        // A good day at a tier-3 airfield, laid out on the apron. Chosen so the single
        // most valuable object in the pile is also the heaviest, because that is the case
        // the whole mass budget exists for - an overhauled main transmission is worth
        // more than anything else here AND it is worth less than what it displaces.
        var pile = new List<SalvagePart>
        {
            new(Salvage.Catalog["xmsn_main"],  0.95),
            new(Salvage.Catalog["blade_main"], 0.80),
            new(Salvage.Catalog["engine_hot"], 0.92),
            new(Salvage.Catalog["hyd_pack"],   0.85),
            new(Salvage.Catalog["avi_ah"],     0.90),
            new(Salvage.Catalog["avi_dopp"],   0.75),
            new(Salvage.Catalog["blade_tail"], 0.60),
            new(Salvage.Catalog["panel_skin"], 0.95),
            new(Salvage.Catalog["pump_fuel"],  0.88),
        };

        Console.WriteLine($"  on the apron: {pile.Count} parts, " +
                          $"{Salvage.TotalMass(pile):F0} kg, {Salvage.TotalValue(pile):F0} value");
        Console.WriteLine("    part                         kg   value  val/kg");
        foreach (var p in pile.OrderByDescending(p => p.ValuePerKg))
            Console.WriteLine($"    {p.Def.Name,-26} {p.Mass,4:F0} {p.Value,7:F0} {p.ValuePerKg,7:F2}");

        const double budget = 200;
        var best = Salvage.BestWithin(pile, budget);
        var byValue = Salvage.GreedyByValue(pile, budget);
        var byDensity = Salvage.GreedyByDensity(pile, budget);

        Console.WriteLine();
        Console.WriteLine($"  {budget:F0} kg of payload:");
        void Show(string label, List<SalvagePart> set)
            => Console.WriteLine($"    {label,-22} {Salvage.TotalMass(set),4:F0} kg  " +
                                 $"{Salvage.TotalValue(set),6:F0} value  " +
                                 $"[{string.Join(", ", set.Select(p => p.Def.Id))}]");
        Show("heaviest-hitter first", byValue);
        Show("value per kilo first", byDensity);
        Show("actual best", best);

        if (Salvage.TotalMass(best) > budget + 1e-9)
            return $"the optimum weighs {Salvage.TotalMass(best):F0} kg, over a {budget:F0} kg budget";
        if (Salvage.TotalMass(byValue) > budget + 1e-9 || Salvage.TotalMass(byDensity) > budget + 1e-9)
            return "a greedy pick broke the mass budget";

        double vBest = Salvage.TotalValue(best);
        if (Salvage.TotalValue(byValue) > vBest + 1e-6 || Salvage.TotalValue(byDensity) > vBest + 1e-6)
            return "a greedy pick beat the exact optimum — the knapsack is wrong";

        // The interesting claim: grabbing the most valuable thing you can lift is a real
        // mistake, not a rounding difference. If it were not, "what do I take" would have
        // an obvious answer and the mass budget would be decoration.
        double loss = (vBest - Salvage.TotalValue(byValue)) / vBest;
        double heuristic = Salvage.TotalValue(byDensity) / vBest;
        Console.WriteLine($"  taking the most valuable thing first costs {loss:P0} of the load's worth");
        Console.WriteLine($"  taking the best value per kilo first gets {heuristic:P0} of the optimum");
        if (loss < 0.10)
            return $"the naive pick is only {loss:P0} worse than optimal — " +
                   "weight and value are not in genuine tension";

        // ...and the lesson the player is meant to learn must actually work, or the
        // answer is "run a solver", which is not a thing anyone does in a cargo bay.
        if (heuristic < 0.95)
            return $"sorting by value per kilo only reaches {heuristic:P0} of the optimum — " +
                   "there is no learnable rule here";

        // And the heaviest single part must be a real sacrifice: taking the transmission
        // has to mean leaving most of the rest behind.
        var withXmsn = Salvage.BestWithin(
            pile.Where(p => p.Def.Id != "xmsn_main").ToList(), budget - 180);
        Console.WriteLine($"  take the transmission (180 kg) and the other 20 kg holds " +
                          $"{withXmsn.Count} of the remaining {pile.Count - 1} parts");
        if (withXmsn.Count > 3)
            return $"the 180 kg transmission still leaves room for {withXmsn.Count} other parts";

        // Value per kilo must genuinely invert the mass order somewhere, or "take the
        // small thing" is never the answer.
        var byMass = pile.OrderByDescending(p => p.Mass).First();
        var byDens = pile.OrderByDescending(p => p.ValuePerKg).First();
        Console.WriteLine($"  heaviest is {byMass.Def.Id}, best value per kilo is {byDens.Def.Id}");
        if (byMass.Def.Id == byDens.Def.Id)
            return "the heaviest part is also the best per kilo — there is no trade";

        return null;
    }

    // ------------------------------------------------------- scarcity by region

    public static string? TierScarcity()
    {
        Console.WriteLine("  every part, and the lowest tier it was actually found at:");

        var firstTier = new Dictionary<string, int>();
        var perTier = new Dictionary<int, HashSet<string>>();

        for (int tier = 0; tier <= 3; tier++)
        {
            var seen = new HashSet<string>();
            foreach (SalvageSiteKind k in AllKinds)
            {
                Sweep s = Run(k, tier, 900);
                foreach (string id in s.Ids.Keys)
                {
                    seen.Add(id);
                    if (!firstTier.ContainsKey(id)) firstTier[id] = tier;
                }
            }
            perTier[tier] = seen;
            Console.WriteLine($"    tier {tier}: {seen.Count} distinct parts in circulation");
        }

        Console.WriteLine();
        Console.WriteLine("    id            declared tier   first seen at");
        foreach (PartDef d in Salvage.All)
        {
            bool found = firstTier.TryGetValue(d.Id, out int at);
            Console.WriteLine($"    {d.Id,-13} {d.Tier,13} {(found ? at.ToString() : "never"),15}");

            // Declared scarcity must be real scarcity. A tier-3 part appearing in the
            // starting country would make the whole outward pull decorative.
            if (found && at < d.Tier)
                return $"'{d.Id}' is declared tier {d.Tier} but was found at tier {at}";
            if (!found)
                return $"'{d.Id}' never appears anywhere — it is in the catalog and not in the world";
        }

        // Each tier must actually add something, or there is no reason to press on.
        for (int t = 1; t <= 3; t++)
        {
            int added = perTier[t].Count - perTier[t - 1].Count;
            if (added <= 0)
                return $"tier {t} puts nothing new in circulation over tier {t - 1}";
            Console.WriteLine($"  tier {t} adds {added} part(s) that do not exist at tier {t - 1}");
        }

        // Specifically: the three things worth a dangerous sortie.
        foreach (string id in new[] { "blade_comp", "avi_dopp" })
        {
            if (perTier[2].Contains(id))
                return $"'{id}' is supposed to be tier 3 only but is in circulation at tier 2";
            if (!perTier[3].Contains(id))
                return $"'{id}' does not appear even at tier 3";
        }

        // Condition improves with tier too — the soft half of the gate.
        Console.WriteLine();
        Console.WriteLine("    tier   mean wreck condition");
        double prev = -1;
        for (int t = 0; t <= 3; t++)
        {
            double c = Run(SalvageSiteKind.Wreck, t, 900).MeanCondition;
            Console.WriteLine($"    {t,4} {c,22:F3}");
            if (c <= prev) return $"tier {t} wrecks are no better kept than tier {t - 1}";
            prev = c;
        }

        return null;
    }

    // --------------------------------------------------------------- stability

    public static string? Determinism()
    {
        Console.WriteLine("  re-rolling 20,000 searches and comparing byte for byte...");

        var first = new List<string>();
        for (int pass = 0; pass < 2; pass++)
        {
            var lines = new List<string>();
            foreach (SalvageSiteKind k in AllKinds)
                for (int id = 0; id < 250; id++)
                {
                    int n = Salvage.SearchesAt(k, id);
                    for (int i = 0; i < n; i++)
                    {
                        SalvageFind f = Salvage.Search(k, id % 4, id, i);
                        lines.Add($"{k}|{id}|{i}|{f.Scrap}|{f.FuelCans}|{f.Medical}|{f.Food}|" +
                                  $"{f.Ammunition}|{f.Part?.Def.Id ?? "-"}|{f.Part?.Condition ?? -1:F9}");
                    }
                }
            if (pass == 0) first = lines;
            else
            {
                if (lines.Count != first.Count)
                    return $"pass 2 produced {lines.Count} finds, pass 1 produced {first.Count}";
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i] != first[i])
                        return $"find {i} differs between passes: '{first[i]}' vs '{lines[i]}'";
            }
        }
        Console.WriteLine($"  {first.Count} finds identical across two passes");

        // The rolls must not be correlated with the site id in a way a player could read.
        // Neighbouring ids of the same kind should not hand over the same thing.
        int sameAsNeighbour = 0, compared = 0;
        for (int id = 0; id < 400; id++)
        {
            SalvagePart? a = Salvage.Search(SalvageSiteKind.Wreck, 2, id, 0).Part;
            SalvagePart? b = Salvage.Search(SalvageSiteKind.Wreck, 2, id + 1, 0).Part;
            if (a is null || b is null) continue;
            compared++;
            if (a.Value.Def.Id == b.Value.Def.Id) sameAsNeighbour++;
        }
        double rate = (double)sameAsNeighbour / compared;
        Console.WriteLine($"  neighbouring wrecks gave the same part {rate:P1} of the time " +
                          $"({sameAsNeighbour}/{compared})");
        if (rate > 0.35)
            return $"neighbouring site ids repeat a part {rate:P0} of the time — the hash is banding";

        // Searches remaining must be stable for the same id, because the game writes it
        // down once and the player remembers the place as picked clean.
        for (int id = 0; id < 500; id++)
            foreach (SalvageSiteKind k in AllKinds)
            {
                int n = Salvage.SearchesAt(k, id);
                if (n != Salvage.SearchesAt(k, id)) return "SearchesAt is not stable";
                SiteYield y = Salvage.Yield(k);
                if (n < y.SearchesMin || n > y.SearchesMax)
                    return $"{k} site {id} has {n} searches, outside {y.SearchesMin}..{y.SearchesMax}";
            }

        // Cargo round-trips through a save.
        var cargo = new Cargo();
        cargo.Take(new SalvagePart(Salvage.Catalog["avi_ah"], 0.73));
        cargo.Take(new SalvagePart(Salvage.Catalog["blade_main"], 0.41));
        var saved = cargo.Parts.Select(p => (p.Def.Id, p.Condition)).ToList();
        var restored = new Cargo();
        restored.Restore(saved);
        if (restored.Count != 2 || Math.Abs(restored.Mass - cargo.Mass) > 1e-9
            || Math.Abs(restored.Value - cargo.Value) > 1e-9)
            return "cargo did not survive a save round trip";
        if (restored.BestFor(Component.Avionics)?.Def.Id != "avi_ah")
            return "BestFor returned the wrong part after a round trip";
        Console.WriteLine($"  cargo round trip: {restored.Count} parts, {restored.Mass:F0} kg, " +
                          $"{restored.Value:F0} value");

        return null;
    }
}
