using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// A hostile NPC on foot. Built in code like everything else during bring-up.
///
/// Body zones are StaticBody3D children on collision layer 4 (NpcZoneLayer),
/// each tagged with metadata identifying the zone. The sidearm raycast
/// checks layer 4 to determine what was hit.
///
/// AI is simple and deliberate: detect the player when close, face them,
/// fire back after a reaction delay. An NPC that can't aim (arm hit) fires
/// wild; one that can't move (leg hit) stays put; one that's disarmed does
/// nothing. The point is that combat has consequences, not that it has
/// complexity.
/// </summary>
public sealed partial class HostileNpc : CharacterBody3D
{
    /// <summary>Collision layer for NPC body zones. Matched by the sidearm raycast.</summary>
    public const uint NpcZoneLayer = 1u << 3; // layer 4 (0-indexed bit 3)

    public NpcHealth Health { get; } = new();

    /// <summary>True when the NPC has detected the player and is hostile.</summary>
    public bool Alert { get; private set; }

    /// <summary>Seconds since the NPC went alert. Reaction time before first shot.</summary>
    public float AlertTime { get; private set; }

    /// <summary>Detection range in metres.</summary>
    public float DetectionRange { get; set; } = 30f;

    /// <summary>Seconds before the NPC fires after detecting the player.</summary>
    public float ReactionTime { get; set; } = 1.5f;

    /// <summary>Seconds between NPC shots.</summary>
    public float FireInterval { get; set; } = 2.0f;

    /// <summary>Base hit probability per shot (modified by accuracy factor).</summary>
    public float BaseHitChance { get; set; } = 0.25f;

    /// <summary>Damage dealt to the pilot per hit.</summary>
    public float Damage { get; set; } = 35f;

    /// <summary>Raised when the NPC fires at the player. Bool = did it hit.</summary>
    public event System.Action<bool>? Fired;

    /// <summary>Raised when a zone is hit on this NPC.</summary>
    public event System.Action<CalledShotResult>? Hit;

    private float _fireCooldown;
    private Node3D? _playerTarget;
    private MeshInstance3D? _weaponMesh;

    public override void _Ready()
    {
        // Don't interact with terrain physics — use analytical ground like the pilot.
        CollisionLayer = 0;
        CollisionMask = 0;

        BuildBody();
        BuildZones();
    }

    /// <summary>Set the player for AI targeting.</summary>
    public void SetTarget(Node3D player) => _playerTarget = player;

    /// <summary>Apply a hit to a body zone. Returns the effect.</summary>
    public CalledShotResult TakeHit(BodyZone zone)
    {
        var result = Health.ApplyHit(zone);
        Hit?.Invoke(result);

        if (zone == BodyZone.Weapon && _weaponMesh is not null)
            _weaponMesh.Visible = false;

        if (Health.IsDown)
            OnDowned();

        return result;
    }

    /// <summary>Place the NPC at a world position facing a direction.</summary>
    public void SpawnAt(Vector3 position, float yaw)
    {
        GlobalPosition = position;
        Rotation = new Vector3(0, yaw, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Health.IsDown) return;

        float dt = (float)delta;
        SnapToTerrain();

        if (_playerTarget is null) return;

        float dist = GlobalPosition.DistanceTo(_playerTarget.GlobalPosition);

        // Detection
        if (!Alert && dist < DetectionRange)
        {
            Alert = true;
            AlertTime = 0;
        }

        if (!Alert) return;
        AlertTime += dt;

        // Face the player (if can move, turn; if immobilised, still turn upper body → just face)
        Vector3 toPlayer = (_playerTarget.GlobalPosition - GlobalPosition);
        toPlayer.Y = 0;
        if (toPlayer.LengthSquared() > 0.01f)
        {
            float targetYaw = Mathf.Atan2(toPlayer.X, toPlayer.Z);
            float currentYaw = Rotation.Y;
            float diff = Mathf.Wrap(targetYaw - currentYaw, -Mathf.Pi, Mathf.Pi);
            float turnRate = 3.0f * dt;
            Rotation = new Vector3(0, currentYaw + Mathf.Clamp(diff, -turnRate, turnRate), 0);
        }

        // Move toward player if not immobilised and too far
        if (!Health.IsImmobilized && dist > 12f)
        {
            Vector3 moveDir = toPlayer.Normalized();
            float speed = 2.0f * Health.SpeedFactor;
            var pos = GlobalPosition;
            pos += moveDir * speed * dt;
            GlobalPosition = pos;
        }

        // Fire at player
        if (Health.IsDisarmed) return;
        if (AlertTime < ReactionTime) return;

        _fireCooldown -= dt;
        if (_fireCooldown > 0) return;
        _fireCooldown = FireInterval;

        // Hit chance modified by NPC accuracy (arm hits reduce it) and range
        float rangeFactor = Mathf.Clamp(1f - dist / DetectionRange, 0.1f, 1f);
        float hitChance = BaseHitChance * Health.AccuracyFactor * rangeFactor;
        bool didHit = GD.Randf() < hitChance;

        Fired?.Invoke(didHit);
    }

    private void SnapToTerrain()
    {
        float ground = WorldHeight.At(GlobalPosition.X, GlobalPosition.Z);
        if (GlobalPosition.Y < ground || GlobalPosition.Y > ground + 2f)
        {
            var p = GlobalPosition;
            p.Y = ground;
            GlobalPosition = p;
        }
    }

    private void OnDowned()
    {
        // Tilt sideways to indicate downed.
        Rotation = new Vector3(Mathf.DegToRad(90f), Rotation.Y, 0);
        var p = GlobalPosition;
        p.Y -= 0.6f;
        GlobalPosition = p;
    }

    // ----------------------------------------------------------------- visuals

    private void BuildBody()
    {
        // Hostile NPC: ragged brown-grey, visually distinct from the olive drab pilot.
        var bodyColor = new Color(0.45f, 0.38f, 0.32f);

        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.2f },
            Position = new Vector3(0, 0.85f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.85f },
        };
        AddChild(body);

        var headColor = new Color(0.60f, 0.50f, 0.42f);
        var head = new MeshInstance3D
        {
            Name = "Head",
            Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.26f, 0.26f) },
            Position = new Vector3(0, 1.63f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = headColor, Roughness = 0.9f },
        };
        AddChild(head);

        // Weapon: a small dark cylinder held in front.
        var weaponColor = new Color(0.25f, 0.22f, 0.20f);
        _weaponMesh = new MeshInstance3D
        {
            Name = "WeaponMesh",
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.6f },
            Position = new Vector3(0.35f, 1.1f, -0.4f),
            Rotation = new Vector3(Mathf.DegToRad(90f), 0, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = weaponColor, Roughness = 0.7f },
        };
        AddChild(_weaponMesh);
    }

    private void BuildZones()
    {
        // Each zone is a StaticBody3D with a CollisionShape3D, on NpcZoneLayer.
        // Metadata identifies the zone for the sidearm raycast.
        AddZone(BodyZone.Head,     new Vector3(0, 1.63f, 0),  new SphereShape3D { Radius = 0.18f });
        AddZone(BodyZone.Torso,    new Vector3(0, 0.95f, 0),  new BoxShape3D { Size = new Vector3(0.50f, 0.60f, 0.30f) });
        AddZone(BodyZone.LeftArm,  new Vector3(-0.38f, 1.0f, 0), new BoxShape3D { Size = new Vector3(0.14f, 0.55f, 0.14f) });
        AddZone(BodyZone.RightArm, new Vector3(0.38f, 1.0f, 0),  new BoxShape3D { Size = new Vector3(0.14f, 0.55f, 0.14f) });
        AddZone(BodyZone.LeftLeg,  new Vector3(-0.14f, 0.35f, 0), new BoxShape3D { Size = new Vector3(0.16f, 0.60f, 0.16f) });
        AddZone(BodyZone.RightLeg, new Vector3(0.14f, 0.35f, 0),  new BoxShape3D { Size = new Vector3(0.16f, 0.60f, 0.16f) });
        AddZone(BodyZone.Weapon,   new Vector3(0.35f, 1.1f, -0.4f), new BoxShape3D { Size = new Vector3(0.10f, 0.10f, 0.65f) });
    }

    private void AddZone(BodyZone zone, Vector3 position, Shape3D shape)
    {
        var zoneBody = new StaticBody3D
        {
            Name = $"Zone_{zone}",
            Position = position,
            CollisionLayer = NpcZoneLayer,
            CollisionMask = 0,
        };
        zoneBody.SetMeta("body_zone", (int)zone);

        var col = new CollisionShape3D
        {
            Name = "Shape",
            Shape = shape,
        };
        zoneBody.AddChild(col);
        AddChild(zoneBody);
    }
}
