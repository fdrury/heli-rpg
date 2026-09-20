using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Game-layer controller for the forward gun pod (D-081).
///
/// The gun pod is a fixed forward M60 — aim by pointing the helicopter's nose.
/// During Rotor Time the world slows to 0.3x, giving time to line up a strafing
/// run. Firing checks each round against every threat emitter within range using
/// EmitterGunnery.RayPointDistance; a hit reduces EmitterHealth, and at zero the
/// emitter is destroyed and stops tracking. This turns a wall into a target,
/// completing D-010's "jammer / emitter locator" progression.
///
/// LMB fires while flying with the gun pod installed. The crosshair and ammo
/// counter are drawn by FlightHud.
/// </summary>
public sealed partial class GunPodController : Node
{
    public GunPodState State { get; } = new();

    /// <summary>True when the gun pod module is installed on the airframe.</summary>
    public bool Installed { get; set; }

    /// <summary>Seconds since last shot, for HUD feedback timing.</summary>
    public float TimeSinceLastShot { get; private set; } = 99f;

    /// <summary>Most recent hit result, for HUD feedback.</summary>
    public GunHitResult? LastHit { get; private set; }

    /// <summary>Cooldown remaining before the next round can fire.</summary>
    private float _cooldown;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _cooldown = Mathf.Max(0, _cooldown - dt);
        TimeSinceLastShot += dt;
    }

    /// <summary>
    /// Attempt to fire one round along the helicopter's nose direction, checking
    /// against all threat emitters within range. Returns the hit result, or null
    /// if the gun cannot fire.
    /// </summary>
    public GunHitResult? Fire(
        Vector3 heliPosition, Vector3 noseDirection,
        ThreatWorld threats)
    {
        if (!Installed || _cooldown > 0 || !State.CanFire)
            return null;

        State.Fire();
        _cooldown = State.FireInterval;
        TimeSinceLastShot = 0;

        // Convert helicopter position and nose to sim coordinates (NED).
        Vec3 origin = SimBridge.PositionToSim(heliPosition);
        Vec3 direction = SimBridge.ToSim(noseDirection).Normalized;

        // Check every emitter — find the closest one within the hit radius.
        ThreatTrack? bestTrack = null;
        double bestDist = double.MaxValue;

        foreach (ThreatTrack track in threats.Field.Tracks)
        {
            if (track.Destroyed) continue;

            // Emitter position in sim coordinates (north, east, down=0 approx).
            Vec3 emitterPos = new(track.Emitter.North, track.Emitter.East, 0);
            // Refine altitude: emitters are on the ground.
            float gx = (float)track.Emitter.East;
            float gz = (float)-track.Emitter.North;
            float groundY = WorldHeight.At(gx, gz);
            emitterPos = new Vec3(track.Emitter.North, track.Emitter.East,
                                  -groundY); // sim Z is down, Godot Y is up

            double dist = EmitterGunnery.RayPointDistance(
                origin, direction, emitterPos, State.Range);

            if (dist < State.EmitterHitRadius && dist < bestDist)
            {
                bestDist = dist;
                bestTrack = track;
            }
        }

        GunHitResult result;
        if (bestTrack is not null)
        {
            result = EmitterGunnery.HitEmitter(bestTrack, State.EmitterDamage);
            GD.Print($"[gunpod] {result.Description} (miss distance {bestDist:F1} m)");
        }
        else
        {
            result = new GunHitResult(GunHitKind.Miss, -1, "miss", 0);
        }

        LastHit = result;
        return result;
    }
}
