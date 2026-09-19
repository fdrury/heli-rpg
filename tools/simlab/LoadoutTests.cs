using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Refit system tests. Verifies that installing and removing modules has the correct
/// effect on mass, CG, drag and fuel capacity — because those are the numbers the flight
/// model reads, and a module that adds weight but does not shift the CG is a module that
/// has no physics consequence.
/// </summary>
public static class LoadoutTests
{
    public static string? CatalogConsistency()
    {
        Console.WriteLine("  checking module catalog...");
        var ids = new HashSet<string>();
        foreach (var m in Loadout.All)
        {
            if (!ids.Add(m.Id))
                return $"duplicate module id '{m.Id}'";
            if (m.Mass <= 0)
                return $"module '{m.Id}' has non-positive mass {m.Mass}";
            if (m.PartsCost < 0)
                return $"module '{m.Id}' has negative parts cost";
        }

        if (Loadout.All.Count != Loadout.Catalog.Count)
            return $"catalog count {Loadout.Catalog.Count} != All count {Loadout.All.Count}";

        Console.WriteLine($"  {Loadout.All.Count} modules, all consistent");
        return null;
    }

    public static string? InstallMass()
    {
        Console.WriteLine("  installing each module and checking mass change...");

        var af = Airframe.Workhorse();
        double baseMass = af.Mass.TotalMass;
        Console.WriteLine($"  base structural mass: {baseMass:F1} kg");

        var loadout = new Loadout();

        foreach (var mod in Loadout.All)
        {
            loadout.Find(mod.Id);
            var def = loadout.Install(mod.Id);
            if (def is null) return $"Install returned null for '{mod.Id}'";

            af.Mass.Add($"mod_{mod.Id}", mod.Position, mod.Mass);
            double newMass = af.Mass.TotalMass;
            double delta = newMass - baseMass;

            Console.WriteLine($"    {mod.Id,-14} +{mod.Mass,5:F0} kg  total {newMass:F0} kg  " +
                              $"CG {af.Mass.CentreOfGravity}");

            if (Math.Abs(delta - mod.Mass) > 0.01)
                return $"module '{mod.Id}': expected +{mod.Mass} kg, got +{delta:F2}";

            // Remove for next iteration's clean baseline
            af.Mass.Remove($"mod_{mod.Id}");
        }

        return null;
    }

    public static string? CgShift()
    {
        Console.WriteLine("  checking CG shifts from aft modules...");

        var af = Airframe.Workhorse();
        Vec3 baseCg = af.Mass.CentreOfGravity;
        Console.WriteLine($"  base CG: {baseCg}");

        // Tail-boom mounted modules should shift CG aft (negative X in FRD).
        foreach (string id in new[] { "chaff", "flares" })
        {
            var mod = Loadout.Catalog[id];
            af.Mass.Add($"mod_{id}", mod.Position, mod.Mass);
            Vec3 newCg = af.Mass.CentreOfGravity;
            double shift = newCg.X - baseCg.X;
            Console.WriteLine($"    {id,-14} CG shift {shift:+0.000;-0.000} m fwd");

            if (shift >= 0)
                return $"aft module '{id}' at x={mod.Position.X:F1} did not shift CG aft (shift={shift:F4})";

            af.Mass.Remove($"mod_{id}");
        }

        // Forward modules should shift CG forward (positive X).
        foreach (string id in new[] { "sas", "rwr" })
        {
            var mod = Loadout.Catalog[id];
            af.Mass.Add($"mod_{id}", mod.Position, mod.Mass);
            Vec3 newCg = af.Mass.CentreOfGravity;
            double shift = newCg.X - baseCg.X;
            Console.WriteLine($"    {id,-14} CG shift {shift:+0.000;-0.000} m fwd");

            if (shift <= 0)
                return $"forward module '{id}' at x={mod.Position.X:F1} did not shift CG forward (shift={shift:F4})";

            af.Mass.Remove($"mod_{id}");
        }

        return null;
    }

    public static string? DragEffect()
    {
        Console.WriteLine("  checking exhaust suppressor drag delta...");

        var af = Airframe.Workhorse();
        Vec3 baseDrag = af.DragArea;
        Console.WriteLine($"  base drag area: {baseDrag}");

        var mod = Loadout.Catalog["suppressor"];
        if (mod.DragDelta.X < 0.01)
            return "suppressor has no forward drag delta";

        Vec3 newDrag = baseDrag + mod.DragDelta;
        Console.WriteLine($"  with suppressor: {newDrag}  (delta {mod.DragDelta})");

        if (newDrag.X <= baseDrag.X)
            return "suppressor drag delta did not increase forward drag";

        // Other modules should have zero drag delta
        foreach (var m in Loadout.All)
        {
            if (m.Id == "suppressor") continue;
            double mag = Math.Abs(m.DragDelta.X) + Math.Abs(m.DragDelta.Y) + Math.Abs(m.DragDelta.Z);
            if (mag > 0.001)
                return $"module '{m.Id}' has unexpected non-zero drag delta {m.DragDelta}";
        }

        return null;
    }

    public static string? FuelCapacity()
    {
        Console.WriteLine("  checking long-range tank fuel capacity increase...");

        var af = Airframe.Workhorse();
        double baseCap = af.FuelCapacity;
        Console.WriteLine($"  base fuel capacity: {baseCap:F0} kg");

        var mod = Loadout.Catalog["tank"];
        if (mod.FuelCapacityDelta < 1)
            return "long-range tank has no fuel capacity delta";

        double newCap = baseCap + mod.FuelCapacityDelta;
        Console.WriteLine($"  with tank: {newCap:F0} kg  (+{mod.FuelCapacityDelta:F0})");

        // Other modules should have zero fuel delta
        foreach (var m in Loadout.All)
        {
            if (m.Id == "tank") continue;
            if (Math.Abs(m.FuelCapacityDelta) > 0.01)
                return $"module '{m.Id}' has unexpected fuel capacity delta {m.FuelCapacityDelta}";
        }

        return null;
    }

    public static string? FullLoadout()
    {
        Console.WriteLine("  installing every module and checking total mass...");

        var af = Airframe.Workhorse();
        double baseMass = af.Mass.TotalMass;
        var loadout = new Loadout();
        double expectedDelta = 0;

        foreach (var mod in Loadout.All)
        {
            loadout.Find(mod.Id);
            loadout.Install(mod.Id);
            af.Mass.Add($"mod_{mod.Id}", mod.Position, mod.Mass);
            expectedDelta += mod.Mass;
        }

        double totalMass = af.Mass.TotalMass;
        double actualDelta = totalMass - baseMass;
        Vec3 cg = af.Mass.CentreOfGravity;
        Mat3 inertia = af.Mass.InertiaAboutCg();

        Console.WriteLine($"  all modules installed: {totalMass:F0} kg  (+{actualDelta:F0})");
        Console.WriteLine($"  CG: {cg}");
        Console.WriteLine($"  inertia diagonal: ({inertia.M00:F0}, {inertia.M11:F0}, {inertia.M22:F0})");

        if (Math.Abs(actualDelta - expectedDelta) > 0.1)
            return $"total mass delta {actualDelta:F1} != expected {expectedDelta:F1}";

        // The inertia tensor must still be positive definite (diagonal > 0)
        if (inertia.M00 <= 0 || inertia.M11 <= 0 || inertia.M22 <= 0)
            return $"non-positive inertia diagonal: ({inertia.M00}, {inertia.M11}, {inertia.M22})";

        // All modules installed = all in Installed set, none in Bag
        if (loadout.Installed.Count != Loadout.All.Count)
            return $"expected {Loadout.All.Count} installed, got {loadout.Installed.Count}";
        if (loadout.Bag.Count != 0)
            return $"expected empty bag, got {loadout.Bag.Count}";

        return null;
    }

    public static string? BagAndInstall()
    {
        Console.WriteLine("  testing find/install/remove lifecycle...");

        var loadout = new Loadout();

        // Cannot install what is not in the bag
        if (loadout.Install("sas") is not null)
            return "installed from empty bag";

        // Find puts it in the bag
        if (!loadout.Find("sas")) return "Find returned false";
        if (!loadout.InBag("sas")) return "not in bag after Find";
        if (loadout.IsInstalled("sas")) return "installed before Install";

        // Cannot find twice
        if (loadout.Find("sas")) return "Find allowed duplicate";

        // Install moves from bag to installed
        var def = loadout.Install("sas");
        if (def is null) return "Install returned null";
        if (def.Id != "sas") return $"wrong module returned: {def.Id}";
        if (loadout.InBag("sas")) return "still in bag after Install";
        if (!loadout.IsInstalled("sas")) return "not installed after Install";

        // Cannot install again
        if (loadout.Install("sas") is not null) return "installed twice";

        // Remove moves back to bag
        var removed = loadout.Remove("sas");
        if (removed is null) return "Remove returned null";
        if (loadout.IsInstalled("sas")) return "still installed after Remove";
        if (!loadout.InBag("sas")) return "not in bag after Remove";

        // Can reinstall
        def = loadout.Install("sas");
        if (def is null) return "reinstall failed";
        if (!loadout.IsInstalled("sas")) return "not installed after reinstall";

        Console.WriteLine("  lifecycle correct: find -> bag -> install -> remove -> bag -> reinstall");
        return null;
    }

    public static string? ModuleDistribution()
    {
        Console.WriteLine("  checking module distribution across site ids...");

        var counts = new Dictionary<string, int>();
        int total = 0;
        int withModule = 0;

        for (int siteId = 0; siteId < 200; siteId++)
        {
            total++;
            string? mod = Loadout.ModuleAtSite(siteId);
            if (mod is null) continue;
            withModule++;
            counts[mod] = counts.GetValueOrDefault(mod) + 0 + 1;
        }

        double rate = (double)withModule / total;
        Console.WriteLine($"  {withModule}/{total} sites have a module ({rate:P0})");
        foreach (var (id, count) in counts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    {id,-14} {count}");

        if (rate < 0.05 || rate > 0.35)
            return $"module site rate {rate:P0} is outside expected range (5-35%)";

        if (counts.Count < 6)
            return $"only {counts.Count} distinct module types across 200 sites — distribution too narrow";

        return null;
    }
}
