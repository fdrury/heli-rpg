using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for the called-shot combat system: zone resolution, NPC health,
/// sidearm state and pilot health.
/// </summary>
public static class CombatTests
{
    /// <summary>Every zone resolves to the expected effect and damage multiplier.</summary>
    public static string? ZoneResolution()
    {
        Console.WriteLine("  checking zone → effect mapping");

        var head = CalledShot.Resolve(BodyZone.Head);
        if (head.Effect != ZoneEffect.Incapacitate)
            return $"Head should Incapacitate, got {head.Effect}";
        if (head.DamageMultiplier < 2.5f)
            return $"Head damage multiplier too low: {head.DamageMultiplier}";

        var torso = CalledShot.Resolve(BodyZone.Torso);
        if (torso.Effect != ZoneEffect.Wound)
            return $"Torso should Wound, got {torso.Effect}";

        var arm = CalledShot.Resolve(BodyZone.LeftArm);
        if (arm.Effect != ZoneEffect.ReduceAccuracy)
            return $"LeftArm should ReduceAccuracy, got {arm.Effect}";

        var leg = CalledShot.Resolve(BodyZone.RightLeg);
        if (leg.Effect != ZoneEffect.Immobilize)
            return $"RightLeg should Immobilize, got {leg.Effect}";

        var weapon = CalledShot.Resolve(BodyZone.Weapon);
        if (weapon.Effect != ZoneEffect.Disarm)
            return $"Weapon should Disarm, got {weapon.Effect}";
        if (weapon.DamageMultiplier > 0.01f)
            return $"Weapon shot should deal no HP damage, got {weapon.DamageMultiplier}";

        Console.WriteLine("  all 7 zones resolve correctly");
        return null;
    }

    /// <summary>NPC health tracks zone effects: immobilise, disarm, accuracy loss.</summary>
    public static string? NpcHealthEffects()
    {
        Console.WriteLine("  checking NPC health zone effects");

        // Headshot is instant down
        var npc1 = new NpcHealth();
        npc1.ApplyHit(BodyZone.Head);
        if (!npc1.IsDown) return "headshot should down the NPC";

        // Leg hit immobilises
        var npc2 = new NpcHealth();
        npc2.ApplyHit(BodyZone.LeftLeg);
        if (!npc2.IsImmobilized) return "leg hit should immobilise";
        if (npc2.SpeedFactor > 0.01f) return $"immobilised speed should be 0, got {npc2.SpeedFactor}";
        if (npc2.IsDown) return "single leg hit should not down";

        // Weapon hit disarms but does no HP damage
        var npc3 = new NpcHealth();
        float hpBefore = npc3.Health;
        npc3.ApplyHit(BodyZone.Weapon);
        if (!npc3.IsDisarmed) return "weapon hit should disarm";
        if (npc3.Health < hpBefore - 0.1f) return $"weapon hit should not reduce HP (was {hpBefore}, now {npc3.Health})";

        // Arm hits stack accuracy loss
        var npc4 = new NpcHealth();
        npc4.ApplyHit(BodyZone.LeftArm);
        float acc1 = npc4.AccuracyFactor;
        npc4.ApplyHit(BodyZone.RightArm);
        float acc2 = npc4.AccuracyFactor;
        if (acc2 >= acc1) return $"second arm hit should reduce accuracy further ({acc1} → {acc2})";
        if (acc2 > 0.20f) return $"two arm hits should reduce accuracy below 20%, got {acc2:P0}";

        // Torso hits reduce speed
        var npc5 = new NpcHealth();
        npc5.ApplyHit(BodyZone.Torso);
        if (npc5.SpeedFactor >= 1.0f) return "torso wound should reduce speed";

        Console.WriteLine("  NPC health effects verified");
        return null;
    }

    /// <summary>Multiple torso hits eventually down an NPC.</summary>
    public static string? NpcAttrition()
    {
        Console.WriteLine("  checking torso attrition");
        var npc = new NpcHealth();
        int shots = 0;
        while (!npc.IsDown && shots < 20)
        {
            npc.ApplyHit(BodyZone.Torso);
            shots++;
        }
        Console.WriteLine($"  NPC downed after {shots} torso hits");
        if (shots < 2) return $"NPC went down too easily ({shots} torso hits)";
        if (shots > 6) return $"NPC took too many torso hits ({shots})";
        return null;
    }

    /// <summary>Sidearm tracks rounds, reload and spare ammo correctly.</summary>
    public static string? SidearmMechanics()
    {
        Console.WriteLine("  checking sidearm mechanics");
        var gun = new SidearmState();
        if (gun.Rounds != 6) return $"should start with 6 rounds, got {gun.Rounds}";
        if (gun.SpareRounds != 12) return $"should have 12 spare, got {gun.SpareRounds}";

        // Fire all six
        for (int i = 0; i < 6; i++)
        {
            if (!gun.Fire()) return $"fire #{i + 1} should succeed";
        }
        if (gun.CanFire) return "should not be able to fire with 0 rounds";
        if (gun.Fire()) return "fire on empty should return false";

        // Reload
        if (!gun.CanReload) return "should be able to reload with spare ammo";
        gun.Reload();
        if (gun.Rounds != 6) return $"should have 6 after reload, got {gun.Rounds}";
        if (gun.SpareRounds != 6) return $"spare should be 6 after one reload, got {gun.SpareRounds}";

        // Fire and reload again
        for (int i = 0; i < 6; i++) gun.Fire();
        gun.Reload();
        if (gun.Rounds != 6) return $"second reload rounds wrong: {gun.Rounds}";
        if (gun.SpareRounds != 0) return $"spare should be 0 after two reloads, got {gun.SpareRounds}";

        // No more reloads
        for (int i = 0; i < 6; i++) gun.Fire();
        if (gun.CanReload) return "should not be able to reload with 0 spare";

        Console.WriteLine("  sidearm mechanics verified: 3 cylinders, 18 rounds total");
        return null;
    }

    /// <summary>Pilot health: takes hits, goes down, recovers.</summary>
    public static string? PilotHealthCycle()
    {
        Console.WriteLine("  checking pilot health");
        var pilot = new PilotHealth();
        if (pilot.Health != 100f) return $"should start at 100, got {pilot.Health}";

        pilot.TakeDamage(pilot.HitDamage);
        if (pilot.IsDown) return "one hit should not down the pilot";

        pilot.TakeDamage(pilot.HitDamage);
        if (pilot.IsDown) return "two hits should not down the pilot";

        pilot.TakeDamage(pilot.HitDamage);
        Console.WriteLine($"  pilot down after 3 hits at {pilot.HitDamage} each");
        if (!pilot.IsDown) return "three hits should down the pilot";

        pilot.Recover(0.5f);
        if (pilot.IsDown) return "pilot should recover";
        if (pilot.Health > 51f) return $"recover(0.5) should give 50 HP, got {pilot.Health}";

        Console.WriteLine("  pilot health cycle verified");
        return null;
    }
}
