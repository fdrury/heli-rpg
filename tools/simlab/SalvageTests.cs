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

    // ================================================ wear, weight and the save
    //
    // The three hooks that turn this file from a model into a mechanism. Until they
    // existed Salvage.cs was complete, covered, and dead code in the running game:
    // nothing wore components with flight time, salvaged parts weighed nothing, and a
    // part did not survive a save. Each test below measures the hook rather than the
    // model - through the real aircraft, the real damage path and the real save file -
    // because a model nothing calls passes its own tests forever.

    private const double Dt = 1.0 / 240.0;

    /// <summary>
    /// Flying the aircraft wears it out, and flying it does nothing else at all.
    ///
    /// <para><b>Two claims, and the second one is the trap.</b> The demand side of the
    /// whole parts economy is that hours cost condition. But the obvious implementation -
    /// tick <c>Salvage.WearOver</c> every frame - was tried and is documented in
    /// <c>02-damage.md</c>: after one minute of flying it had perturbed main rotor health
    /// by 1.5e-4, and that moved the measured autorotation rate of descent from 3193 to
    /// 3859 fpm and best glide from 50 kt to 70. The autorotation equilibrium is
    /// knife-edged. It also made wear depend on how many iterations <c>Trim.Solve</c>
    /// took, because trim solves by stepping the aircraft.</para>
    ///
    /// <para>So the Hobbs meter runs in <c>DamageState.UpdateSystems</c> and the hours are
    /// charged when the rotor stops. This test asserts BOTH halves: that ten minutes of
    /// real flight moved no health value by so much as a bit, and that stopping the rotor
    /// then charged exactly what <c>Salvage.WearOver</c> says it should. If the first
    /// assertion ever fails, look at the autorotation numbers before anything else.</para>
    /// </summary>
    public static string? FlightHours()
    {
        const double flightMinutes = 10.0;

        var h = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
        h.PlaceInFlight(300);
        h.UseInternalGroundModel = false;
        var ap = new Autopilot { CollectiveTrim = 0.6 };
        var demand = new AutopilotDemand
            { Altitude = 300, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };

        // Settle first, and throw the settling away.
        //
        // PlaceInFlight drops an UNTRIMMED aircraft into the air, and the autopilot
        // catching it puts a brief excursion through the gearbox: measured at 2.36e-5 of
        // transmission health in the first thirty seconds, from the continuous-torque
        // term in UpdateSystems, and then nothing ever again. That is the damage model
        // working - it really is a momentary overtorque - but it is an artefact of how
        // the aircraft got into the air, not of flying it, and counting it would make
        // this test assert something it does not mean.
        for (int i = 0; i < (int)(30 / Dt); i++)
        {
            h.Input = ap.Update(h, demand, Dt);
            h.Step(Dt);
        }
        h.Damage.AccrueFlightHours();   // zero the Hobbs meter; the settling is not the test

        var atLiftoff = new Dictionary<Component, double>();
        foreach (Component c in Enum.GetValues<Component>()) atLiftoff[c] = h.Damage.Health(c);

        int steps = (int)(flightMinutes * 60 / Dt);
        for (int i = 0; i < steps; i++)
        {
            h.Input = ap.Update(h, demand, Dt);
            h.Step(Dt);
        }

        // --- 1. The flight itself perturbed nothing --------------------------
        foreach (Component c in Enum.GetValues<Component>())
            if (h.Damage.Health(c) != atLiftoff[c])
                return $"{c} health moved during the flight itself " +
                       $"({atLiftoff[c]:R} -> {h.Damage.Health(c):R}). Wear is being ticked in " +
                       "flight again; check the autorotation numbers, they will have moved too";

        double hobbs = h.Damage.RotorTurningSeconds;
        Console.WriteLine($"  flew {flightMinutes:F0} min of hover at 300 m, {h.TotalMass:F0} kg all up");
        Console.WriteLine($"  Hobbs meter: {hobbs / 60:F1} min of rotor-turning time, " +
                          "health untouched to the last bit");
        if (hobbs < flightMinutes * 60 * 0.98)
            return $"the Hobbs meter only ran {hobbs:F0} s over a {flightMinutes * 60:F0} s flight";

        // --- 2. Stopping the rotor charges exactly the salvage curve ----------
        double booked = h.Damage.AccrueFlightHours();
        Console.WriteLine();
        Console.WriteLine($"  shut down; {booked:F4} flight hours booked");
        Console.WriteLine("    component      health after    lost   Salvage.WearOver   life left h");

        foreach (Component c in Enum.GetValues<Component>())
        {
            double lost = atLiftoff[c] - h.Damage.Health(c);
            double expect = Salvage.WearOver(c, atLiftoff[c], booked);
            Console.WriteLine($"    {c,-13} {h.Damage.Health(c),13:F6} {lost,8:F6} {expect,17:F6}" +
                              $" {Salvage.LifeHours(c, h.Damage.Health(c)),12:F0}");
            if (lost <= 0)
                return $"{c} lost no condition to {booked:F3} flight hours - nothing wears";
            if (Math.Abs(lost - expect) > 1e-12)
                return $"{c} lost {lost:R} but Salvage.WearOver says {expect:R} - " +
                       "there are two wear models again";
        }

        // The wear must also be in the damage log, with a cause, like every other kind of
        // damage. A silent health change is one the player can never find out about.
        int wearEntries = 0;
        foreach (DamageEvent e in h.Damage.Log) if (e.Cause == DamageCause.Wear) wearEntries++;
        Console.WriteLine($"  damage log carries {wearEntries} Wear entries " +
                          "(logged in hundredths, so a short flight books none yet)");

        // --- 3. How long the aircraft lasts on hours alone --------------------
        //
        // Through DamageState.UpdateSystems, not through Salvage.LifeHours, so the answer
        // includes the couplings: a worn rotor shakes, and the shaking chafes the lines
        // around it. That makes "hours until grounded" shorter than the component curves
        // predict on their own, which is the honest number and the one the economy has to
        // keep up with.
        double shaft = Math.Max(h.Telemetry.MainRotorPower, 0)
                     + Math.Max(h.Telemetry.TailRotorPower, 0)
                     + Math.Max(h.Telemetry.DrivetrainPower, 0);
        var hover = new SystemLoad(
            RotorFraction: h.Telemetry.RotorRpmPercent / 100.0,
            TorqueFraction: h.Telemetry.TorquePercent / 100.0,
            ShaftPowerW: shaft,
            PowerFraction: shaft / Math.Max(h.Telemetry.PowerAvailable, 1.0),
            AmbientTempC: 15.0,
            EngineRunning: true);
        var parked = new SystemLoad(0, 0, 0, 0, 15.0, false);

        Console.WriteLine();
        Console.WriteLine($"  measured flight load: {hover.ShaftPowerW / 1000:F0} kW through the gearbox, " +
                          $"{hover.TorqueFraction * 100:F0}% torque, Nr {hover.RotorFraction * 100:F0}%");

        const double maxHours = 800;

        // One sortie: an hour aloft, then the rotor stops and the hours book. With
        // <paramref name="serviced"/> the fluids are also put back, which is what
        // ResetSystems means and what the game does on every load: a parked aircraft is
        // cold, with whatever the mechanic poured into it.
        (Dictionary<Component, double> floors, double grounded) Ladder(bool serviced)
        {
            var d = new DamageState();
            var floorAt = new Dictionary<Component, double>();
            double groundedAt = 0, flown = 0;
            while (flown < maxHours)
            {
                for (int i = 0; i < 60; i++) d.UpdateSystems(hover, 60.0);
                d.UpdateSystems(parked, 1.0);
                if (serviced) d.ResetSystems();
                flown += 1.0;

                foreach (Component c in Enum.GetValues<Component>())
                    if (!floorAt.ContainsKey(c) && d.Health(c) <= DamageState.UnserviceableAt(c))
                        floorAt[c] = flown;
                if (groundedAt == 0 && !d.Airworthy) groundedAt = flown;
                if (floorAt.Count == Enum.GetValues<Component>().Length) break;
            }
            return (floorAt, groundedAt);
        }

        // Two ladders, because the difference between them is a real finding and not a
        // measurement artefact. Wear is quadratic into the gearbox oil leak: at 0.99 of
        // transmission health the leak is a millionth of the reservoir per second, which
        // is nothing on a sortie and is 0.4% of the oil per flight hour. An aircraft
        // NOBODY EVER TOPS UP therefore does not die of wear, it dies of oil starvation
        // at 21 hours, with the gearbox and the hydraulics off it going together. A
        // serviced one lives on the component curves, which is what the parts economy is
        // priced against.
        var (unserviced, unservicedGrounded) = Ladder(serviced: false);
        var (floors, grounded) = Ladder(serviced: true);

        Console.WriteLine("  flying it an hour at a time, nothing but hours:");
        Console.WriteLine("    component      serviced   never serviced   floor   pure-wear curve");
        foreach (Component c in Enum.GetValues<Component>())
        {
            string a = floors.TryGetValue(c, out double hrs) ? $"{hrs:F0} h" : "none";
            string b = unserviced.TryGetValue(c, out double uh) ? $"{uh:F0} h" : "none";
            Console.WriteLine($"    {c,-13} {a,10} {b,16}   {DamageState.UnserviceableAt(c),5:F2}" +
                              $" {Salvage.LifeHours(c, 1.0),15:F0} h");
            if (!floors.ContainsKey(c))
                return $"{c} never reached its unserviceable threshold in {maxHours:F0} flight hours - " +
                       "it is effectively immortal and will never be a reason to salvage anything";
        }

        Console.WriteLine($"  serviced between sorties, the aircraft stopped being airworthy after " +
                          $"{grounded:F0} flight hours with nothing going wrong at all");
        Console.WriteLine($"  never serviced, {unservicedGrounded:F0} h - the gearbox weeps its oil away " +
                          "on ordinary wear long before the gears are worn out");

        if (grounded <= 0) return "hours alone never made the aircraft unairworthy";
        if (grounded < 40)
            return $"{grounded:F0} flight hours to grounded - D-007 asks for a loop that is " +
                   "forgiving, and this is an aircraft that needs an engine every third sortie";
        if (grounded > 300)
            return $"{grounded:F0} flight hours to grounded - the player will finish the game " +
                   "before anything needs replacing, so the demand side does not exist";
        if (unservicedGrounded >= grounded)
            return "servicing the aircraft between sorties made no difference at all";

        return null;
    }

    // ------------------------------------------------------- cargo has mass

    /// <summary>
    /// A part in the back weighs what the part weighs, and the rotor has to lift it.
    ///
    /// <para><c>Progress.CarriedMass</c> used to count scrap, jerrycans and rations and
    /// nothing else, so a main transmission in the cabin weighed exactly as much as an
    /// empty cabin. That is not a missing feature, it is THE feature: D-003a says load is
    /// a constraint that is FELT, and the whole knapsack argument in this file rests on
    /// two hundred kilos costing something real.</para>
    ///
    /// <para>Measured on the flight model, and routed through <c>Progress.CarriedMass</c>
    /// exactly as the game layer routes it - one "cargo" mass item kept in sync with that
    /// one number - so this fails if the plumbing comes apart, not only if the arithmetic
    /// does.</para>
    /// </summary>
    public static string? CargoWeighs()
    {
        // --- 1. The arithmetic ------------------------------------------------
        var p = new Progress();
        p.Add(Stock.Scrap, 20);      // 20 kg
        p.Add(Stock.Fuel, 2);        // two jerrycans, 40 kg
        double bulk = p.CarriedMass;

        p.Cargo.Take(new SalvagePart(Salvage.Catalog["xmsn_main"], 0.62));   // 180 kg
        p.Cargo.Take(new SalvagePart(Salvage.Catalog["avi_ah"], 0.88));      //   7 kg

        Console.WriteLine($"  bulk stock {bulk:F0} kg, cargo {p.Cargo.Mass:F0} kg " +
                          $"({p.Cargo.Count} parts), carried {p.CarriedMass:F0} kg");
        if (Math.Abs(p.CarriedMass - (bulk + p.Cargo.Mass)) > 1e-9)
            return $"CarriedMass is {p.CarriedMass:F1} kg but bulk + cargo is {bulk + p.Cargo.Mass:F1} kg";
        if (Math.Abs(p.Cargo.Mass - 187.0) > 1e-9)
            return $"a transmission and an attitude reference came to {p.Cargo.Mass:F1} kg, not 187";

        p.Cargo.Drop("xmsn_main");
        if (Math.Abs(p.CarriedMass - (bulk + 7.0)) > 1e-9)
            return "dropping the transmission did not take 180 kg off the carried mass";
        Console.WriteLine($"  dropped the transmission: {p.CarriedMass:F0} kg");

        // A payload check against the aircraft's real gross limit, which is the number
        // that makes the knapsack a knapsack.
        var af = Airframe.Workhorse();
        double allUp = af.Mass.TotalMass + af.FuelCapacity + 187.0;
        Console.WriteLine($"  full fuel and both parts: {allUp:F0} kg all up, " +
                          $"{Salvage.PayloadRemaining(allUp):F0} kg of payload left");

        // --- 2. A real haul, on the real aircraft -----------------------------
        //
        // 200 kg exactly, in four parts a player would plausibly come home with, so the
        // ceiling numbers below sit alongside salvage_weight's and can be compared.
        var haul = new Progress();
        foreach (var (id, cond) in new[]
                 { ("blade_main", 0.44), ("skid_set", 0.61), ("gearbox_tail", 0.37), ("avi_dg", 0.80) })
            haul.Cargo.Take(new SalvagePart(Salvage.Catalog[id], cond));

        double load = haul.CarriedMass;
        Console.WriteLine();
        Console.WriteLine($"  the haul: {string.Join(", ", haul.Cargo.Parts)}");
        Console.WriteLine($"  {load:F0} kg, worth {haul.Cargo.Value:F0}");
        if (Math.Abs(load - 200.0) > 1e-9)
            return $"the haul came to {load:F1} kg, not the 200 the ceiling numbers are quoted at";

        Console.WriteLine();
        Console.WriteLine("  hover ceiling OGE, 125 m steps, mass routed through Progress.CarriedMass:");
        double emptyCeiling = 0, loadedCeiling = 0;
        foreach (double kg in new[] { 0.0, load })
        {
            double ceiling = 0;
            for (double alt = 0; alt <= 5000; alt += 125)
            {
                var h = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
                if (kg > 0.1) h.Airframe.Mass.Add("cargo", new Vec3(-0.6, 0, -0.1), kg);
                h.InvalidateMass();
                h.PlaceInFlight(alt);
                var ap = new Autopilot { CollectiveTrim = 0.6 };
                var demand = new AutopilotDemand
                    { Altitude = alt, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };
                for (int i = 0; i < 9600; i++)   // 40 s at 240 Hz
                {
                    h.Input = ap.Update(h, demand, Dt);
                    h.Step(Dt);
                }
                if (Math.Abs(h.State.Altitude - alt) < 8 && h.Input.Collective < 0.985) ceiling = alt;
                else break;
            }
            Console.WriteLine($"    {kg,7:F0} kg  {ceiling,6:F0} m");
            if (kg < 0.1) emptyCeiling = ceiling; else loadedCeiling = ceiling;
        }

        double lost = emptyCeiling - loadedCeiling;
        Console.WriteLine($"  the haul costs {lost:F0} m of hover ceiling " +
                          $"({emptyCeiling:F0} -> {loadedCeiling:F0})");
        if (lost <= 0)
            return "a 200 kg haul did not lower the hover ceiling at all - " +
                   "Progress.Cargo is not reaching the rotor";
        if (lost < 300)
            return $"a 200 kg haul cost only {lost:F0} m of ceiling";

        // --- 3. And it climbs worse everywhere below the ceiling --------------
        //
        // The ceiling is where climb reaches zero; this is the same fact where the player
        // actually lives. Same lever position in both aircraft, so the only difference is
        // the two hundred kilos: mean rate of climb over the second half of a 40 s pull.
        //
        // NOT at full collective, and that is worth writing down. Pinning the lever at
        // the stop droops Nr to 60% and the aircraft DESCENDS at 10 m/s, loaded or empty
        // - the powerplant runs into the torque limit, the rotor decelerates, and the
        // thrust goes with it. That is correct behaviour and it is what a real one does,
        // but it measures the droop rather than the load. 0.55 holds Nr at 99.8% and 82%
        // torque, inside the continuous rating, where a climb rate means something.
        const double climbCollective = 0.55;
        Console.WriteLine();
        Console.WriteLine($"  rate of climb from 1500 m at {climbCollective:F2} collective " +
                          "(the same lever in both):");
        Console.WriteLine("       load        climb          Nr    torque");
        double emptyRoc = 0, loadedRoc = 0;
        foreach (double kg in new[] { 0.0, load })
        {
            var h = new Helicopter(Airframe.Workhorse(), new FlatEnvironment()) { Fuel = 500 };
            if (kg > 0.1) h.Airframe.Mass.Add("cargo", new Vec3(-0.6, 0, -0.1), kg);
            h.InvalidateMass();
            h.PlaceInFlight(1500);
            h.UseInternalGroundModel = false;
            var ap = new Autopilot { CollectiveTrim = 0.6 };
            var demand = new AutopilotDemand
                { Collective = climbCollective, ForwardSpeed = 0, LateralSpeed = 0, Heading = 0 };

            double startAlt = 0;
            for (int i = 0; i < 9600; i++)   // 40 s at 240 Hz
            {
                h.Input = ap.Update(h, demand, Dt);
                h.Step(Dt);
                if (i == 4799) startAlt = h.State.Altitude;
            }
            double roc = (h.State.Altitude - startAlt) / 20.0;
            Console.WriteLine($"    {kg,7:F0} kg {roc * 196.85,7:F0} fpm ({roc,4:F2} m/s)" +
                              $" {h.Telemetry.RotorRpmPercent,6:F1}% {h.Telemetry.TorquePercent,7:F1}%");
            if (h.Telemetry.RotorRpmPercent < 99.0)
                return $"Nr drooped to {h.Telemetry.RotorRpmPercent:F1}% at {kg:F0} kg — " +
                       "this is measuring the rotor running down, not the load";
            if (kg < 0.1) emptyRoc = roc; else loadedRoc = roc;
        }

        double rocCost = emptyRoc - loadedRoc;
        Console.WriteLine($"  the haul costs {rocCost * 196.85:F0} fpm of climb at 1500 m, " +
                          $"{rocCost / Math.Max(emptyRoc, 1e-9):P0} of what the aircraft had");
        if (emptyRoc <= 0)
            return $"the empty aircraft did not climb at 1500 m ({emptyRoc:F2} m/s) — " +
                   "the measurement is broken before the load is even aboard";
        if (rocCost <= 0)
            return $"the loaded aircraft climbed as well as the empty one " +
                   $"({loadedRoc:F2} vs {emptyRoc:F2} m/s) - the load is not being felt";
        if (rocCost / emptyRoc < 0.20)
            return $"200 kg cost only {rocCost / emptyRoc:P0} of the climb rate";

        return null;
    }

    // -------------------------------------------------- cargo survives a save

    /// <summary>
    /// A part in the back is still in the back after a save and a load.
    ///
    /// <para>Exactly, not approximately. Condition is the whole value of a part
    /// (<c>Value</c> goes as condition^1.8) and it is the number the player weighed a
    /// hundred and five kilos against, so a save that rounds it is a save that quietly
    /// charges them for the round trip. The round trip here goes through the JSON, not
    /// through the DTOs, because the JSON is what is actually on disk.</para>
    ///
    /// <para>Only id and condition are stored. That is deliberate and is tested: mass and
    /// value come back from <c>Salvage.Catalog</c>, so retuning a part retunes every
    /// existing save rather than only new ones.</para>
    /// </summary>
    public static string? CargoSurvivesSave()
    {
        var p = Progress.NewGame();
        p.Add(Stock.Scrap, 31);

        var haul = new (string Id, double Condition)[]
        {
            ("blade_main",  0.41234567890123),   // awkward on purpose
            ("blade_main",  0.98765432109876),   // two of the same part, different wear
            ("avi_dopp",    1.0),                // the ends of the range
            ("skid_shoe",   0.0),
            ("xmsn_input",  0.5),
        };
        foreach (var (id, cond) in haul)
            p.Cargo.Take(new SalvagePart(Salvage.Catalog[id], cond));

        double massBefore = p.CarriedMass;
        Console.WriteLine($"  aboard: {p.Cargo.Count} parts, {p.Cargo.Mass:F0} kg, " +
                          $"worth {p.Cargo.Value:F1}; carried mass {massBefore:F1} kg");

        var save = new SaveData();
        save.CaptureProgress(p);
        string json = save.ToJson();
        SaveData? read = SaveData.FromJson(json);
        if (read is null) return "the save did not deserialise at all";
        Progress q = read.ApplyProgress();

        Console.WriteLine($"  save file carries {read.Cargo.Count} cargo entries, " +
                          $"{json.Length} bytes of JSON in total");

        if (q.Cargo.Count != p.Cargo.Count)
            return $"{p.Cargo.Count} parts went in and {q.Cargo.Count} came out";

        Console.WriteLine("    part                        before           after            kg");
        for (int i = 0; i < p.Cargo.Count; i++)
        {
            SalvagePart a = p.Cargo.Parts[i], b = q.Cargo.Parts[i];
            Console.WriteLine($"    {b.Def.Name,-24} {a.Condition,16:F14} {b.Condition,16:F14} {b.Mass,4:F0}");
            if (b.Def.Id != a.Def.Id)
                return $"part {i} came back as '{b.Def.Id}', not '{a.Def.Id}' - the order moved";
            if (b.Condition != a.Condition)
                return $"'{a.Def.Id}' went in at {a.Condition:R} and came back at {b.Condition:R}";
            if (b.Mass != a.Mass || b.Value != a.Value)
                return $"'{a.Def.Id}' came back with a different mass or value";
        }

        if (q.CarriedMass != massBefore)
            return $"carried mass was {massBefore:R} kg and came back {q.CarriedMass:R} kg";
        Console.WriteLine($"  carried mass round-tripped exactly: {q.CarriedMass:F1} kg");

        // An older save has no cargo field at all. It must load, with nothing aboard,
        // rather than throw - every save written before this field existed is one of these.
        string older = json.Replace("\"cargo\"", "\"cargoWasNotAThingYet\"");
        SaveData? old = SaveData.FromJson(older);
        if (old is null) return "a save without a cargo field did not deserialise";
        Progress r = old.ApplyProgress();
        if (r.Cargo.Count != 0)
            return $"a save with no cargo field produced {r.Cargo.Count} parts from nowhere";
        Console.WriteLine("  a save written before cargo existed still loads: " +
                          $"{r.Cargo.Count} parts, {r.CarriedMass:F1} kg of bulk stock intact");

        // A part id that has since left the catalog is dropped, not fatal. A save that
        // sheds a part is bad; a save that will not open is worse.
        read.Cargo.Add(new CargoPartSave { Id = "blade_unobtainium", Condition = 0.9 });
        Progress s = read.ApplyProgress();
        if (s.Cargo.Count != p.Cargo.Count)
            return $"an unknown part id changed the cargo count to {s.Cargo.Count}";
        Console.WriteLine("  an id that is no longer in the catalog is dropped, and the save opens");

        return null;
    }
}
