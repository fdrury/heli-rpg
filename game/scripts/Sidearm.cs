using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The result of firing the sidearm.
/// </summary>
public readonly record struct ShotResult(
    bool DidHit,
    HostileNpc? Npc,
    CalledShotResult? Effect,
    Vector3 HitPosition);

/// <summary>
/// The pilot's sidearm — a revolver. Fires a hitscan ray from a given origin
/// and direction, checks for NPC body zone collisions, and applies damage.
///
/// The weapon does not compute spread or random accuracy. The player aims;
/// Rotor Time gives them the time to aim precisely. Without RT, the world
/// moves at full speed and hitting small zones (head, weapon) is the player's
/// skill challenge. During RT, the world slows to 0.3x and called shots
/// become practical.
///
/// Fire() takes an explicit ray rather than reading from the camera so that
/// both the game (camera projection) and tests (known geometry) can use it.
/// </summary>
public sealed partial class Sidearm : Node
{
    public SidearmState State { get; } = new();
    public PilotHealth PilotHp { get; } = new();

    /// <summary>Cooldown remaining before the next shot, in real seconds.</summary>
    public float Cooldown { get; private set; }

    /// <summary>Reload timer remaining, in real seconds.</summary>
    public float ReloadTimer { get; private set; }

    /// <summary>True while a reload is in progress.</summary>
    public bool IsReloading => ReloadTimer > 0;

    /// <summary>Last shot result, for HUD display.</summary>
    public ShotResult? LastShot { get; private set; }

    /// <summary>Seconds since the last shot, for HUD fade timing.</summary>
    public float TimeSinceLastShot { get; private set; } = 999f;

    /// <summary>Seconds since the pilot last took damage, for HUD flash.</summary>
    public float TimeSinceDamage { get; private set; } = 999f;

    /// <summary>Raised when a shot is fired.</summary>
    public event System.Action<ShotResult>? ShotFired;

    /// <summary>Raised when the pilot takes damage.</summary>
    public event System.Action<float>? PilotDamaged;

    public override void _PhysicsProcess(double delta)
    {
        // Timers run on real time, not engine time, so combat pacing is consistent
        // regardless of Rotor Time dilation.
        float timeScale = (float)Engine.TimeScale;
        float realDt = timeScale > 0.01f ? (float)delta / timeScale : (float)delta;

        if (Cooldown > 0) Cooldown -= realDt;
        TimeSinceLastShot += realDt;
        TimeSinceDamage += realDt;

        if (ReloadTimer > 0)
        {
            ReloadTimer -= realDt;
            if (ReloadTimer <= 0)
            {
                State.Reload();
                GD.Print($"[sidearm] reloaded: {State.Rounds}/{State.Capacity} (+{State.SpareRounds} spare)");
            }
        }
    }

    /// <summary>
    /// Fire the sidearm along a ray. Returns the result.
    ///
    /// The ray origin and direction come from the caller: the game computes
    /// them from the camera, the test from known geometry.
    /// </summary>
    public ShotResult? Fire(Vector3 origin, Vector3 direction, PhysicsDirectSpaceState3D space)
    {
        if (IsReloading) return null;
        if (Cooldown > 0) return null;
        if (!State.CanFire) return null;

        State.Fire();
        Cooldown = State.FireInterval;
        TimeSinceLastShot = 0;

        // Hitscan ray against NPC body zones (layer 4).
        var query = PhysicsRayQueryParameters3D.Create(
            origin, origin + direction * State.Range);
        query.CollisionMask = HostileNpc.NpcZoneLayer;

        var hit = space.IntersectRay(query);
        ShotResult result;

        if (hit.Count == 0)
        {
            result = new ShotResult(false, null, null, origin + direction * State.Range);
        }
        else
        {
            var collider = hit["collider"].As<Node3D>();
            var position = (Vector3)hit["position"];

            var (npc, zone) = FindNpcZone(collider);
            if (npc is not null)
            {
                var effect = npc.TakeHit(zone);
                result = new ShotResult(true, npc, effect, position);
                GD.Print($"[sidearm] HIT {zone}: {effect.Description} ({npc.Health.Health:F0} HP)");
            }
            else
            {
                result = new ShotResult(false, null, null, position);
                GD.Print("[sidearm] hit non-NPC collider");
            }
        }

        LastShot = result;
        ShotFired?.Invoke(result);

        if (State.Rounds == 0)
            GD.Print($"[sidearm] empty — R to reload ({State.SpareRounds} spare)");

        return result;
    }

    /// <summary>Start a reload if possible.</summary>
    public bool TryReload()
    {
        if (IsReloading) return false;
        if (!State.CanReload) return false;
        ReloadTimer = State.ReloadTime;
        GD.Print("[sidearm] reloading...");
        return true;
    }

    /// <summary>Apply damage from a hostile NPC firing at the player.</summary>
    public void TakeNpcHit(float damage)
    {
        PilotHp.TakeDamage(damage);
        TimeSinceDamage = 0;
        PilotDamaged?.Invoke(damage);
        GD.Print($"[sidearm] pilot hit! HP {PilotHp.Health:F0}/{PilotHp.MaxHealth:F0}");
    }

    /// <summary>
    /// Walk up from the hit collider to find the NPC and body zone.
    /// Zone colliders are StaticBody3D children of HostileNpc with "body_zone" metadata.
    /// </summary>
    private static (HostileNpc?, BodyZone) FindNpcZone(Node3D collider)
    {
        if (collider.HasMeta("body_zone"))
        {
            var zone = (BodyZone)(int)collider.GetMeta("body_zone");
            // The zone StaticBody3D is a direct child of the HostileNpc.
            if (collider.GetParent() is HostileNpc npc)
                return (npc, zone);
        }

        return (null, BodyZone.Torso);
    }
}
