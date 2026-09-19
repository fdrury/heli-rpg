using Godot;

namespace Rotorwash;

/// <summary>
/// Third-person player character for on-foot movement.
///
/// A CharacterBody3D capsule that walks around the world when the pilot has shut down
/// and dismounted. Movement is WASD + mouse look, sprint with Shift. No combat, no
/// animation — this is the movement layer that proves third-person works.
///
/// The pilot is not a separate entity from the helicopter: they are the same person,
/// and Rotor Time is what makes that feel true.
///
/// Terrain floor detection uses WorldHeight.At analytically rather than relying on
/// MoveAndSlide against the terrain collision shape. ConcavePolygonShape3D does not
/// work with CharacterBody3D.MoveAndSlide in Godot 4.7 — raycasts hit the trimesh
/// at exactly the right height while MoveAndSlide sails straight through it. Since
/// we have the exact height function, using it directly is both simpler and correct.
/// MoveAndSlide still handles horizontal collisions against structures and objects.
/// </summary>
public sealed partial class PilotController : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 2.5f;
    [Export] public float SprintSpeed { get; set; } = 5.5f;
    [Export] public float MouseSensitivity { get; set; } = 0.002f;
    [Export] public float Gravity { get; set; } = 9.81f;

    /// <summary>The node the camera pivots around vertically.</summary>
    public Node3D CameraPivot { get; private set; } = null!;

    /// <summary>True when the pilot is standing on terrain.</summary>
    public bool OnGround { get; private set; }

    private float _pitchAngle;
    private bool _movedThisFrame;
    private float _siteFloor = float.NegativeInfinity;

    public override void _Ready()
    {
        // Collision capsule: a pilot is about 1.8 m tall, 0.4 m radius.
        var shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
        var col = new CollisionShape3D { Name = "Shape", Shape = shape };
        col.Position = new Vector3(0, 0.9f, 0);
        AddChild(col);

        // Placeholder mesh: olive drab capsule body + box head. Same palette as Hugh
        // so it reads as part of the same world.
        var bodyColor = new Color(0.40f, 0.42f, 0.35f);
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.2f },
            Position = new Vector3(0, 0.85f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.85f },
        };
        AddChild(body);

        var head = new MeshInstance3D
        {
            Name = "Head",
            Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.28f, 0.28f) },
            Position = new Vector3(0, 1.65f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.60f, 0.50f), Roughness = 0.9f },
        };
        AddChild(head);

        // Camera pivot sits at head height so the third-person camera orbits naturally.
        CameraPivot = new Node3D { Name = "CameraPivot" };
        CameraPivot.Position = new Vector3(0, 1.6f, 0);
        AddChild(CameraPivot);

        // The character should not rotate from physics contacts; yaw is mouse-driven.
        FloorStopOnSlope = true;
        FloorMaxAngle = Mathf.DegToRad(50f);

        // Terrain floor detection is done analytically via WorldHeight.At because
        // CharacterBody3D.MoveAndSlide does not work with ConcavePolygonShape3D in
        // Godot 4.7. With no collision mask, MoveAndSlide just applies the velocity
        // without physics interaction, and SnapToTerrain keeps the pilot on the ground.
        // This also avoids embedding in site building collision bodies.
        CollisionMask = 0;
    }

    /// <summary>Reset the per-frame guard. Call at the top of _PhysicsProcess.</summary>
    public void BeginFrame() => _movedThisFrame = false;

    /// <summary>
    /// Call each physics frame when on foot. Returns velocity for the frame.
    /// Mouse input is handled in _UnhandledInput on Main so the camera stays responsive.
    /// </summary>
    public void Move(double delta)
    {
        if (_movedThisFrame) return;
        _movedThisFrame = true;

        float dt = (float)delta;

        // Input direction relative to where the character is facing.
        Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");

        // Also read raw WASD since the default ui_ actions may not be bound to WASD.
        if (inputDir.LengthSquared() < 0.01f)
        {
            float ix = 0, iy = 0;
            if (Input.IsKeyPressed(Key.A)) ix -= 1;
            if (Input.IsKeyPressed(Key.D)) ix += 1;
            if (Input.IsKeyPressed(Key.W)) iy -= 1;
            if (Input.IsKeyPressed(Key.S)) iy += 1;
            inputDir = new Vector2(ix, iy);
            if (inputDir.LengthSquared() > 1) inputDir = inputDir.Normalized();
        }

        bool sprinting = Input.IsKeyPressed(Key.Shift);
        float speed = sprinting ? SprintSpeed : WalkSpeed;

        // Transform input to world direction based on character yaw.
        Vector3 direction = (GlobalTransform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        var vel = Velocity;
        if (inputDir.LengthSquared() > 0.01f)
        {
            vel.X = direction.X * speed;
            vel.Z = direction.Z * speed;
        }
        else
        {
            // Decelerate smoothly.
            vel.X = Mathf.MoveToward(vel.X, 0, speed * dt * 8f);
            vel.Z = Mathf.MoveToward(vel.Z, 0, speed * dt * 8f);
        }

        ApplyTerrainGravity(ref vel, dt);
        Velocity = vel;
        ApplyVelocity(dt);
    }

    /// <summary>
    /// Walk in a world-space direction under script control, with the same gravity and
    /// floor handling the player gets. Used by the foottest since headless mode has no
    /// keyboard input.
    ///
    /// This OVERRIDES any Move() that already ran this frame. Main calls Move first (it
    /// runs higher in the tree), and in headless mode Move does nothing useful because
    /// there is no keyboard input. WalkToward replaces the result with scripted motion.
    /// </summary>
    public void WalkToward(Vector3 worldDirection, float speed, double delta)
    {
        var dt = (float)delta;
        Vector3 flat = new Vector3(worldDirection.X, 0, worldDirection.Z);
        if (flat.LengthSquared() > 1e-6f) flat = flat.Normalized();

        var vel = Velocity;
        vel.X = flat.X * speed;
        vel.Z = flat.Z * speed;

        ApplyTerrainGravity(ref vel, dt);
        Velocity = vel;
        ApplyVelocity(dt);
        _movedThisFrame = true;
    }

    /// <summary>
    /// Move the pilot by its current velocity and snap to terrain.
    ///
    /// This replaces MoveAndSlide because CharacterBody3D.MoveAndSlide does not work
    /// with ConcavePolygonShape3D terrain in Godot 4.7 (it passes straight through).
    /// Since floor detection is analytical (WorldHeight.At), we do not need the physics
    /// sweep test, and moving the position directly is simpler and reliable.
    /// </summary>
    private void ApplyVelocity(float dt)
    {
        var p = GlobalPosition;
        p += Velocity * dt;
        GlobalPosition = p;
        SnapToTerrain();
    }

    /// <summary>
    /// The floor the pilot should stand on: the higher of WorldHeight (terrain including
    /// site pads) and the site floor remembered from SpawnAtDoor. As the pilot walks away
    /// from the site the terrain takes over and the site floor fades to irrelevance.
    /// </summary>
    private float GroundAt(float x, float z)
    {
        return Mathf.Max(WorldHeight.At(x, z), _siteFloor);
    }

    /// <summary>
    /// Gravity that checks against the analytic ground rather than MoveAndSlide's floor
    /// detection. CharacterBody3D.MoveAndSlide does not reliably detect
    /// ConcavePolygonShape3D terrain in Godot 4.7, but GroundAt gives the exact answer.
    /// </summary>
    private void ApplyTerrainGravity(ref Vector3 vel, float dt)
    {
        float ground = GroundAt(GlobalPosition.X, GlobalPosition.Z);
        OnGround = GlobalPosition.Y <= ground + 0.15f;

        if (OnGround)
        {
            if (vel.Y < 0) vel.Y = 0;
        }
        else
        {
            vel.Y -= Gravity * dt;
        }
    }

    /// <summary>
    /// After MoveAndSlide, clamp Y so the pilot never sinks below the ground.
    /// </summary>
    private void SnapToTerrain()
    {
        float ground = GroundAt(GlobalPosition.X, GlobalPosition.Z);
        if (GlobalPosition.Y < ground)
        {
            var p = GlobalPosition;
            p.Y = ground;
            GlobalPosition = p;
            OnGround = true;
        }
    }

    /// <summary>Apply mouse motion: yaw rotates character, pitch rotates camera pivot.</summary>
    public void ApplyMouseMotion(Vector2 relative)
    {
        RotateY(-relative.X * MouseSensitivity);
        _pitchAngle = Mathf.Clamp(_pitchAngle - relative.Y * MouseSensitivity,
                                   Mathf.DegToRad(-70f), Mathf.DegToRad(60f));
        CameraPivot.Rotation = new Vector3(_pitchAngle, 0, 0);
    }

    /// <summary>Place the pilot at the helicopter's left door.</summary>
    public void SpawnAtDoor(Node3D helicopter)
    {
        // UH-1 left cargo door: roughly 0.8m left of centreline, at ground level.
        Vector3 doorOffset = new(-2.2f, 0, 0.3f);
        Vector3 worldPos = helicopter.GlobalTransform * doorOffset;

        // The ground the pilot should stand on is the HIGHER of:
        //   - the terrain height (WorldHeight, which includes site pads), and
        //   - the collision surface the helicopter is actually resting on.
        // At settlements, the site buildings create collision above the terrain. The
        // helicopter lands on those. WorldHeight does not know about buildings, so using
        // it alone puts the pilot 10m below the helicopter they just climbed out of.
        float terrainGround = WorldHeight.At(worldPos.X, worldPos.Z);
        float heliGround = helicopter.GlobalPosition.Y - 1.3f; // skids are ~1.3m below CG
        float ground = Mathf.Max(terrainGround, heliGround);
        worldPos.Y = ground + 0.1f;

        // Remember this floor so GroundAt keeps the pilot above it while walking
        // around the site. Once they return to raw terrain the terrain height dominates.
        _siteFloor = ground;

        GlobalPosition = worldPos;

        // Face away from the helicopter.
        Vector3 awayDir = (worldPos - helicopter.GlobalPosition).Normalized();
        float yaw = Mathf.Atan2(awayDir.X, awayDir.Z);
        Rotation = new Vector3(0, yaw, 0);

        _pitchAngle = 0;
        CameraPivot.Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    /// <summary>Distance to a point in world space, for boarding checks.</summary>
    public float DistanceTo(Vector3 worldPos)
    {
        return GlobalPosition.DistanceTo(worldPos);
    }
}
