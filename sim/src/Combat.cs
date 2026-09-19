using System;

namespace Rotorwash.Sim;

/// <summary>Body zone for called shots — on-foot targeting against NPCs.</summary>
public enum BodyZone
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
    Weapon,
}

/// <summary>What happens when a zone is hit.</summary>
public enum ZoneEffect
{
    /// <summary>Target is immediately incapacitated.</summary>
    Incapacitate,
    /// <summary>Heavy wound: slowed, reduced capability.</summary>
    Wound,
    /// <summary>Arm hit: cannot aim well.</summary>
    ReduceAccuracy,
    /// <summary>Leg hit: cannot move.</summary>
    Immobilize,
    /// <summary>Weapon destroyed: target is disarmed.</summary>
    Disarm,
}

/// <summary>The result of a called shot hitting a specific zone.</summary>
public readonly record struct CalledShotResult(
    BodyZone Zone,
    ZoneEffect Effect,
    float DamageMultiplier,
    string Description);

/// <summary>
/// Called shot resolution. Pure data, no Godot dependency.
///
/// During Rotor Time the world slows to 0.3x, giving the player time to aim at
/// a specific body zone. Without RT the player fires at whatever the crosshair
/// covers — which at normal speed is usually the torso. The mechanic IS the
/// slow-motion: you earn it through committed flying, you spend it on a shot
/// that matters.
/// </summary>
public static class CalledShot
{
    public static CalledShotResult Resolve(BodyZone zone) => zone switch
    {
        BodyZone.Head       => new(zone, ZoneEffect.Incapacitate,  3.0f, "headshot — down"),
        BodyZone.Torso      => new(zone, ZoneEffect.Wound,         1.0f, "center mass — wounded"),
        BodyZone.LeftArm    => new(zone, ZoneEffect.ReduceAccuracy, 0.6f, "left arm — can't aim"),
        BodyZone.RightArm   => new(zone, ZoneEffect.ReduceAccuracy, 0.6f, "right arm — can't aim"),
        BodyZone.LeftLeg    => new(zone, ZoneEffect.Immobilize,    0.6f, "left leg — can't move"),
        BodyZone.RightLeg   => new(zone, ZoneEffect.Immobilize,    0.6f, "right leg — can't move"),
        BodyZone.Weapon     => new(zone, ZoneEffect.Disarm,        0.0f, "weapon shot — disarmed"),
        _                   => new(zone, ZoneEffect.Wound,         0.5f, "graze"),
    };

    /// <summary>All zones, for UI enumeration.</summary>
    public static readonly BodyZone[] AllZones = Enum.GetValues<BodyZone>();
}

/// <summary>
/// Health tracking for an NPC, per body zone. Effects accumulate: two arm hits
/// stack into a worse accuracy penalty.
///
/// This mirrors the aircraft DamageState pattern: health is a multiplier on
/// capability, and the game mechanics work out the consequences.
/// </summary>
public sealed class NpcHealth
{
    /// <summary>Overall health, 0–100. At 0 the NPC is down.</summary>
    public float Health { get; private set; } = 100f;

    /// <summary>True when health has reached zero.</summary>
    public bool IsDown => Health <= 0;

    /// <summary>True when a leg hit has immobilised the NPC.</summary>
    public bool IsImmobilized { get; private set; }

    /// <summary>True when the NPC's weapon has been destroyed.</summary>
    public bool IsDisarmed { get; private set; }

    /// <summary>Accuracy multiplier, 0–1. Arm hits reduce it.</summary>
    public float AccuracyFactor { get; private set; } = 1.0f;

    /// <summary>Speed multiplier, 0–1. Wounds and leg hits reduce it.</summary>
    public float SpeedFactor { get; private set; } = 1.0f;

    /// <summary>Apply a hit to a zone. Returns the effect that was applied.</summary>
    public CalledShotResult ApplyHit(BodyZone zone, float baseDamage = 30f)
    {
        var result = CalledShot.Resolve(zone);
        float damage = baseDamage * result.DamageMultiplier;
        Health = Math.Max(0, Health - damage);

        switch (result.Effect)
        {
            case ZoneEffect.Incapacitate:
                Health = 0;
                break;
            case ZoneEffect.Wound:
                SpeedFactor *= 0.5f;
                break;
            case ZoneEffect.Immobilize:
                IsImmobilized = true;
                SpeedFactor = 0f;
                break;
            case ZoneEffect.Disarm:
                IsDisarmed = true;
                break;
            case ZoneEffect.ReduceAccuracy:
                AccuracyFactor *= 0.4f;
                break;
        }

        return result;
    }

    public void Reset()
    {
        Health = 100f;
        IsImmobilized = false;
        IsDisarmed = false;
        AccuracyFactor = 1.0f;
        SpeedFactor = 1.0f;
    }
}

/// <summary>
/// Sidearm state: a revolver. Six rounds, deliberate, every shot matters.
/// The pilot is a mechanic who happens to carry a gun, not a soldier.
///
/// Ammo is scarce and scavenged. Starting with 18 total (6 loaded + 12 spare)
/// means three reloads before you are dry, which is enough for exactly one
/// encounter if you are careful and not enough if you are not.
/// </summary>
public sealed class SidearmState
{
    public int Rounds { get; set; } = 6;
    public int SpareRounds { get; set; } = 12;
    public int Capacity { get; } = 6;

    /// <summary>Base damage per hit.</summary>
    public float Damage { get; } = 30f;

    /// <summary>Effective range in metres.</summary>
    public float Range { get; } = 50f;

    /// <summary>Minimum time between shots, seconds (real time).</summary>
    public float FireInterval { get; } = 0.5f;

    /// <summary>Reload time, seconds (real time).</summary>
    public float ReloadTime { get; } = 2.0f;

    public bool CanFire => Rounds > 0;
    public bool CanReload => Rounds < Capacity && SpareRounds > 0;

    /// <summary>Expend one round. Returns false if empty.</summary>
    public bool Fire()
    {
        if (Rounds <= 0) return false;
        Rounds--;
        return true;
    }

    /// <summary>Reload from spare ammo.</summary>
    public void Reload()
    {
        int need = Capacity - Rounds;
        int load = Math.Min(need, SpareRounds);
        Rounds += load;
        SpareRounds -= load;
    }
}

/// <summary>
/// Pilot health when on foot. Three hits and you are down.
/// The pilot is not a soldier; being on foot is already risky.
/// </summary>
public sealed class PilotHealth
{
    public float Health { get; private set; } = 100f;
    public float MaxHealth { get; } = 100f;
    public bool IsDown => Health <= 0;

    /// <summary>Damage per hostile NPC hit.</summary>
    public float HitDamage { get; } = 35f;

    public void TakeDamage(float amount)
    {
        Health = Math.Max(0, Health - amount);
    }

    /// <summary>Heal back to a fraction of max. Used after going down.</summary>
    public void Recover(float fraction = 0.5f)
    {
        Health = MaxHealth * Math.Clamp(fraction, 0, 1);
    }

    public void Reset() => Health = MaxHealth;

    /// <summary>Set health directly. Save/load only.</summary>
    public void RestoreHealth(float h) => Health = Math.Clamp(h, 0, MaxHealth);
}
