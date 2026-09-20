using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// The main rotor repair ceiling (story.md §1.3, D-086).
///
/// The ceiling is the maximum health that Repair can restore the main rotor to.
/// It falls with total flight hours and floors at 0.55 — a flyable, unpleasant
/// aircraft that never kills you and never locks you out. New blades reset it to
/// 1.0, and that is the only event in the game that does.
/// </summary>
public static class CeilingTests
{
    /// <summary>
    /// The ceiling degrades linearly with flight hours and floors at 0.55.
    /// At 0 hours: 1.0. At 94 hours: ~0.82. At 240+ hours: 0.55.
    /// </summary>
    public static string? Degrades()
    {
        var d = new DamageState();

        // At zero hours the ceiling is 1.0.
        if (Math.Abs(d.MainRotorCeiling - 1.0) > 1e-9)
            return $"ceiling at 0 h: {d.MainRotorCeiling} (expected 1.0)";

        // At 94 hours the ceiling should be ~0.8214, matching story.md's "ceiling 82%".
        d.TotalFlightHours = 94;
        double at94 = 1.00 - DamageState.CeilingRate * 94;
        if (Math.Abs(d.MainRotorCeiling - at94) > 1e-9)
            return $"ceiling at 94 h: {d.MainRotorCeiling} (expected {at94})";
        Console.WriteLine($"  at  94 h: ceiling {d.MainRotorCeiling:P1}  (story.md says ~82%)");

        // At 120 hours.
        d.TotalFlightHours = 120;
        double at120 = 1.00 - DamageState.CeilingRate * 120;
        if (Math.Abs(d.MainRotorCeiling - at120) > 1e-9)
            return $"ceiling at 120 h: {d.MainRotorCeiling} (expected {at120})";
        Console.WriteLine($"  at 120 h: ceiling {d.MainRotorCeiling:P1}");

        // At 240 hours the ceiling should have hit the floor (0.55).
        d.TotalFlightHours = 240;
        double raw240 = 1.00 - DamageState.CeilingRate * 240;
        if (raw240 > DamageState.CeilingFloor)
            return $"ceiling formula gives {raw240} at 240 h, expected <= {DamageState.CeilingFloor}";
        if (Math.Abs(d.MainRotorCeiling - DamageState.CeilingFloor) > 1e-9)
            return $"ceiling at 240 h: {d.MainRotorCeiling} (expected floor {DamageState.CeilingFloor})";
        Console.WriteLine($"  at 240 h: ceiling {d.MainRotorCeiling:P1}  (floor)");

        // At extreme hours it still floors.
        d.TotalFlightHours = 1000;
        if (Math.Abs(d.MainRotorCeiling - DamageState.CeilingFloor) > 1e-9)
            return $"ceiling at 1000 h: {d.MainRotorCeiling} (expected floor {DamageState.CeilingFloor})";

        return null;
    }

    /// <summary>
    /// Repair cannot push the main rotor past the ceiling.
    /// Damage the rotor, set hours, repair fully — health should cap at ceiling.
    /// </summary>
    public static string? CapsRepair()
    {
        var d = new DamageState();
        d.TotalFlightHours = 120;
        double ceiling = d.MainRotorCeiling;

        // Damage the rotor down to 0.40.
        d.Apply(Component.MainRotor, 0.60, DamageCause.Wear, "test wear");
        if (Math.Abs(d.Health(Component.MainRotor) - 0.40) > 1e-9)
            return $"after damage: {d.Health(Component.MainRotor)} (expected 0.40)";

        // Repair by 1.0 — should cap at ceiling, not 1.0.
        d.Repair(Component.MainRotor, 1.0);
        Console.WriteLine($"  at 120 h: repaired rotor to {d.Health(Component.MainRotor):F4}, ceiling {ceiling:F4}");

        if (Math.Abs(d.Health(Component.MainRotor) - ceiling) > 1e-9)
            return $"repaired to {d.Health(Component.MainRotor)}, expected ceiling {ceiling}";

        // Other components should still repair to 1.0.
        d.Apply(Component.Engine, 0.50, DamageCause.Wear, "test");
        d.Repair(Component.Engine, 1.0);
        if (Math.Abs(d.Health(Component.Engine) - 1.0) > 1e-9)
            return $"engine repaired to {d.Health(Component.Engine)}, expected 1.0 (ceiling is rotor-only)";
        Console.WriteLine($"  engine repaired to {d.Health(Component.Engine):F4} (unaffected by ceiling)");

        return null;
    }

    /// <summary>
    /// RepairAll also caps the main rotor at the ceiling.
    /// </summary>
    public static string? CapsRepairAll()
    {
        var d = new DamageState();
        d.TotalFlightHours = 200;
        double ceiling = d.MainRotorCeiling;

        d.Apply(Component.MainRotor, 0.70, DamageCause.Wear, "test");
        d.Apply(Component.Engine, 0.30, DamageCause.Wear, "test");
        d.RepairAll();

        Console.WriteLine($"  at 200 h: RepairAll -> rotor {d.Health(Component.MainRotor):F4} (ceiling {ceiling:F4}), engine {d.Health(Component.Engine):F4}");

        if (Math.Abs(d.Health(Component.MainRotor) - ceiling) > 1e-9)
            return $"RepairAll set rotor to {d.Health(Component.MainRotor)}, expected ceiling {ceiling}";
        if (Math.Abs(d.Health(Component.Engine) - 1.0) > 1e-9)
            return $"RepairAll set engine to {d.Health(Component.Engine)}, expected 1.0";

        return null;
    }

    /// <summary>
    /// ResetRotorHours zeros the flight hours and restores the ceiling to 1.0.
    /// This is the payoff: new blades make the aircraft new again.
    /// </summary>
    public static string? Reset()
    {
        var d = new DamageState();
        d.TotalFlightHours = 200;
        double ceilingBefore = d.MainRotorCeiling;

        if (ceilingBefore >= 1.0 - 1e-9)
            return $"ceiling at 200 h should be well below 1.0, got {ceilingBefore}";

        d.ResetRotorHours();

        if (d.TotalFlightHours > 1e-9)
            return $"TotalFlightHours after reset: {d.TotalFlightHours} (expected 0)";
        if (Math.Abs(d.MainRotorCeiling - 1.0) > 1e-9)
            return $"ceiling after reset: {d.MainRotorCeiling} (expected 1.0)";

        // After reset, repair should reach 1.0.
        d.Apply(Component.MainRotor, 0.50, DamageCause.Wear, "test");
        d.Repair(Component.MainRotor, 1.0);
        if (Math.Abs(d.Health(Component.MainRotor) - 1.0) > 1e-9)
            return $"repaired to {d.Health(Component.MainRotor)} after reset, expected 1.0";

        Console.WriteLine($"  before reset: ceiling {ceilingBefore:P1}");
        Console.WriteLine($"  after  reset: ceiling {d.MainRotorCeiling:P1}, repair reaches 1.0");

        return null;
    }

    /// <summary>
    /// The ceiling survives a save/load round trip because TotalFlightHours is persisted.
    /// </summary>
    public static string? SaveRoundTrip()
    {
        var d = new DamageState();
        d.TotalFlightHours = 137.5;
        double ceilingBefore = d.MainRotorCeiling;

        // Cap the rotor at the ceiling.
        d.Apply(Component.MainRotor, 0.70, DamageCause.Wear, "test");
        d.Repair(Component.MainRotor, 1.0);
        double healthBefore = d.Health(Component.MainRotor);

        var save = new SaveData();
        save.CaptureDamage(d);
        string json = save.ToJson();

        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        var d2 = new DamageState();
        loaded.ApplyDamage(d2);

        if (Math.Abs(d2.TotalFlightHours - 137.5) > 1e-6)
            return $"flight hours: {d2.TotalFlightHours} vs 137.5";
        if (Math.Abs(d2.MainRotorCeiling - ceilingBefore) > 1e-6)
            return $"ceiling: {d2.MainRotorCeiling} vs {ceilingBefore}";
        if (Math.Abs(d2.Health(Component.MainRotor) - healthBefore) > 1e-6)
            return $"rotor health: {d2.Health(Component.MainRotor)} vs {healthBefore}";

        Console.WriteLine($"  saved at {d.TotalFlightHours:F1} h, ceiling {ceilingBefore:P1}");
        Console.WriteLine($"  loaded:  {d2.TotalFlightHours:F1} h, ceiling {d2.MainRotorCeiling:P1}, rotor {d2.Health(Component.MainRotor):F4}");

        return null;
    }
}
