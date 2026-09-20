using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Tests for the forward gun pod (D-081).
///
/// The gun pod is a fixed forward M60 that damages and destroys threat emitters.
/// These tests verify the core mechanics: ammo depletion, ray-to-point hit
/// detection, emitter damage and destruction, destroyed emitters ceasing to
/// track, and save/load round-trip for gun and emitter state.
/// </summary>
public static class GunneryTests
{
    /// <summary>Gun pod fires, depletes ammo, and reports empty correctly.</summary>
    public static string? AmmoDepletion()
    {
        Console.WriteLine("  checking gun pod ammo mechanics");

        var gun = new GunPodState();
        if (gun.Rounds != 200) return $"should start with 200, got {gun.Rounds}";
        if (gun.MaxRounds != 200) return $"max should be 200, got {gun.MaxRounds}";
        if (!gun.CanFire) return "should be able to fire when full";

        // Fire all rounds.
        int fired = 0;
        while (gun.Fire()) fired++;
        if (fired != 200) return $"should fire 200, fired {fired}";
        if (gun.Rounds != 0) return $"should be empty, got {gun.Rounds}";
        if (gun.CanFire) return "should not be able to fire when empty";
        if (gun.Fire()) return "Fire() should return false when empty";

        Console.WriteLine("  ammo mechanics verified");
        return null;
    }

    /// <summary>Ray-to-point distance calculation works correctly.</summary>
    public static string? RayPointDistance()
    {
        Console.WriteLine("  checking ray-point distance geometry");

        // Direct hit: target on the ray.
        Vec3 origin = Vec3.Zero;
        Vec3 dir = new(1, 0, 0);
        Vec3 target = new(400, 0, 0);
        double d = EmitterGunnery.RayPointDistance(origin, dir, target, 800);
        if (d > 0.01) return $"direct hit should be ~0, got {d:F3}";

        // Near miss: target 10 m to the side.
        target = new Vec3(400, 10, 0);
        d = EmitterGunnery.RayPointDistance(origin, dir, target, 800);
        if (Math.Abs(d - 10.0) > 0.01) return $"10m offset should be 10, got {d:F3}";

        // Beyond range: should return MaxValue.
        target = new Vec3(900, 0, 0);
        d = EmitterGunnery.RayPointDistance(origin, dir, target, 800);
        if (d < double.MaxValue / 2) return $"beyond range should be MaxValue, got {d}";

        // Behind origin: should return MaxValue.
        target = new Vec3(-100, 0, 0);
        d = EmitterGunnery.RayPointDistance(origin, dir, target, 800);
        if (d < double.MaxValue / 2) return $"behind origin should be MaxValue, got {d}";

        // Vertical offset.
        target = new Vec3(300, 0, 15);
        d = EmitterGunnery.RayPointDistance(origin, dir, target, 800);
        if (Math.Abs(d - 15.0) > 0.01) return $"15m vertical should be 15, got {d:F3}";

        Console.WriteLine("  ray-point geometry verified");
        return null;
    }

    /// <summary>Emitter takes damage and is eventually destroyed.</summary>
    public static string? EmitterDamage()
    {
        Console.WriteLine("  checking emitter hit and destruction");

        var emitter = ThreatField.Make(1, "Test SAM", ThreatKind.Sam, 1000, 500);
        var track = new ThreatTrack { Emitter = emitter, EmitterHealth = 1.0 };

        // First hit.
        var result = EmitterGunnery.HitEmitter(track, 0.12);
        if (result.Kind != GunHitKind.EmitterHit)
            return $"first hit should be Hit, got {result.Kind}";
        if (Math.Abs(track.EmitterHealth - 0.88) > 0.001)
            return $"health should be 0.88, got {track.EmitterHealth:F3}";
        if (track.Destroyed) return "should not be destroyed after one hit";

        // Fire enough to destroy (9 hits at 0.12 = 1.08 total, > 1.0).
        for (int i = 0; i < 8; i++)
            EmitterGunnery.HitEmitter(track, 0.12);

        if (!track.Destroyed) return $"should be destroyed after 9 hits, health = {track.EmitterHealth:F3}";

        // Check result for the kill shot.
        result = EmitterGunnery.HitEmitter(track, 0.12); // one more — already destroyed
        if (track.EmitterHealth < 0) return $"health should not go below 0, got {track.EmitterHealth}";

        Console.WriteLine("  emitter damage and destruction verified");
        return null;
    }

    /// <summary>A destroyed emitter stops tracking — confidence stays at 0, state stays Idle.</summary>
    public static string? DestroyedStopsTracking()
    {
        Console.WriteLine("  checking destroyed emitters stop tracking");

        var emitter = ThreatField.Make(1, "Test Gun", ThreatKind.Gun, 100, 100);

        var field = new ThreatField();
        field.Add(emitter);

        var track = field.Tracks[0];

        // Fly within range to build confidence.
        for (int i = 0; i < 40; i++)
            field.Update(100, 100, 200, 0, 30, 0.1, _ => true);

        if (track.State == TrackState.Idle)
            return "emitter should have detected us before destruction";

        // Destroy it.
        track.EmitterHealth = 0;

        // Update again — it should drop to idle.
        field.Update(100, 100, 200, 0, 30, 0.1, _ => true);

        if (track.State != TrackState.Idle)
            return $"destroyed emitter should be Idle, got {track.State}";
        if (track.Confidence > 0.001)
            return $"destroyed emitter confidence should be 0, got {track.Confidence:F3}";

        Console.WriteLine("  destroyed emitters stop tracking");
        return null;
    }

    /// <summary>Gun pod rounds and emitter health survive save/load round-trip.</summary>
    public static string? SaveRoundTrip()
    {
        Console.WriteLine("  checking gun pod and emitter health survive save/load");

        // Set up distinctive state.
        var save = new SaveData
        {
            GunRounds = 142,
            EmitterHealth =
            {
                [7] = 0.55,
                [13] = 0.0,
            },
        };

        string json = save.ToJson();
        var loaded = SaveData.FromJson(json);
        if (loaded is null) return "deserialisation returned null";

        if (loaded.GunRounds != 142)
            return $"gun rounds: expected 142, got {loaded.GunRounds}";

        if (!loaded.EmitterHealth.TryGetValue(7, out double h7) || Math.Abs(h7 - 0.55) > 0.001)
            return $"emitter 7 health: expected 0.55, got {h7}";

        if (!loaded.EmitterHealth.TryGetValue(13, out double h13) || h13 > 0.001)
            return $"emitter 13 health: expected 0, got {h13}";

        Console.WriteLine("  gun pod state and emitter health round-trip verified");
        return null;
    }

    /// <summary>The gun pod module exists in the loadout catalog with correct properties.</summary>
    public static string? GunPodModule()
    {
        Console.WriteLine("  checking gun pod module in loadout catalog");

        if (!Loadout.Catalog.TryGetValue("gunpod", out var def))
            return "gunpod not found in catalog";
        if (def.Mass < 40 || def.Mass > 50)
            return $"mass should be ~45 kg, got {def.Mass}";
        if (def.DragDelta.X < 0.05)
            return $"should add drag, got {def.DragDelta.X}";
        if (def.PartsCost < 1)
            return $"should cost parts, got {def.PartsCost}";

        // Gun pod should appear in site distribution.
        bool found = false;
        for (int id = 0; id < 2000 && !found; id++)
        {
            string? mod = Loadout.ModuleAtSite(id);
            if (mod == "gunpod") found = true;
        }
        if (!found) return "gunpod never appears in site distribution";

        Console.WriteLine("  gun pod module verified in catalog and distribution");
        return null;
    }
}
