using System;

namespace Rotorwash.Sim;

/// <summary>
/// Forward gun pod state: an M60 in a fixed forward mount.
///
/// Fixed forward means every hit is earned with flying — the pilot aims by pointing
/// the helicopter's nose, not a cursor. During Rotor Time the world slows to 0.3x,
/// giving the time to set up a diving attack. This is the vision doc's "flamboyant
/// manoeuvre that earns the shot": you charge RT through committed flying, then spend
/// it on a strafing run that requires committed flying to aim.
///
/// Pure .NET, no Godot dependency. The game layer handles the raycast and the HUD.
/// </summary>
public sealed class GunPodState
{
    /// <summary>Rounds remaining in the belt.</summary>
    public int Rounds { get; set; } = 200;

    /// <summary>Maximum belt capacity.</summary>
    public int MaxRounds { get; } = 200;

    /// <summary>Damage per round against threat emitters (0–1 health scale).</summary>
    public double EmitterDamage { get; } = 0.12;

    /// <summary>Damage per round against hostile NPCs (HP).</summary>
    public float NpcDamage { get; } = 25f;

    /// <summary>Effective range, metres.</summary>
    public float Range { get; } = 800f;

    /// <summary>Minimum time between shots, seconds (real time). ~550 rpm.</summary>
    public float FireInterval { get; } = 0.109f;

    /// <summary>Proximity radius for emitter hit detection, metres.</summary>
    public float EmitterHitRadius { get; } = 15f;

    public bool CanFire => Rounds > 0;

    /// <summary>Expend one round. Returns false if empty.</summary>
    public bool Fire()
    {
        if (Rounds <= 0) return false;
        Rounds--;
        return true;
    }
}

/// <summary>
/// Result of an air-to-ground gun hit.
/// </summary>
public readonly record struct GunHitResult(
    GunHitKind Kind,
    int TargetId,
    string Description,
    double EmitterHealthAfter);

public enum GunHitKind
{
    Miss,
    EmitterHit,
    EmitterDestroyed,
    NpcHit,
}

/// <summary>
/// Resolve a gun round against a threat emitter.
///
/// Each emitter has a health pool (1.0 to 0.0). At zero the emitter is destroyed
/// and stops tracking. This turns the gun pod from a combat tool into a map-opening
/// tool: destroy a SAM site and the route through its envelope is clear. Consistent
/// with D-010's "jammer / emitter locator turns a wall into a target."
///
/// The damage per round (0.12) means roughly 8–9 hits to destroy an emitter, so a
/// single strafing pass must land most of its rounds to matter — and at 550 rpm with
/// nose-direction aiming from a moving helicopter, that is a genuine skill challenge.
/// </summary>
public static class EmitterGunnery
{
    /// <summary>
    /// Check whether a ray from <paramref name="origin"/> along <paramref name="direction"/>
    /// passes within <paramref name="hitRadius"/> of the point <paramref name="target"/>,
    /// within <paramref name="maxRange"/>. Returns the closest approach distance, or
    /// <see cref="double.MaxValue"/> if the closest point is behind the origin or beyond range.
    /// </summary>
    public static double RayPointDistance(
        Vec3 origin, Vec3 direction, Vec3 target, double maxRange)
    {
        var toTarget = target - origin;
        double along = Vec3.Dot(toTarget, direction);

        // Behind the ray origin or beyond range.
        if (along < 0 || along > maxRange) return double.MaxValue;

        var closest = origin + direction * along;
        return (target - closest).Length;
    }

    /// <summary>
    /// Apply gun damage to an emitter's health. Returns the result.
    /// </summary>
    public static GunHitResult HitEmitter(
        ThreatTrack track, double damagePerRound)
    {
        track.EmitterHealth = Math.Max(0, track.EmitterHealth - damagePerRound);
        bool destroyed = track.EmitterHealth <= 0;

        return new GunHitResult(
            destroyed ? GunHitKind.EmitterDestroyed : GunHitKind.EmitterHit,
            track.Emitter.Id,
            destroyed
                ? $"{track.Emitter.Name} destroyed"
                : $"{track.Emitter.Name} hit ({track.EmitterHealth:P0})",
            track.EmitterHealth);
    }
}
