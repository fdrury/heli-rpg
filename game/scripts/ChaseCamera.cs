using Godot;

namespace Rotorwash;

public enum CameraMode { Chase, Cockpit, Orbit, Flyby }

/// <summary>
/// Camera rig for the helicopter.
///
/// Chase is deliberately not rigidly attached: it lags in position and leads in
/// direction, so the aircraft visibly moves inside the frame. A helicopter manoeuvres by
/// changing attitude long before it changes direction, and a camera welded to the
/// airframe hides exactly the information the pilot needs.
/// </summary>
public sealed partial class ChaseCamera : Camera3D
{
    [Export] public NodePath TargetPath { get; set; } = "";
    [Export] public CameraMode Mode { get; set; } = CameraMode.Chase;

    // Low and well back. Sitting the camera high turns the game into a map: a pilot
    // reads attitude against the horizon, so the horizon has to be in shot.
    [Export] public Vector3 ChaseOffset { get; set; } = new(0, 2.1f, 16.0f);
    [Export] public Vector3 CockpitOffset { get; set; } = new(-0.62f, 0.55f, 1.45f);

    [Export] public float PositionLag { get; set; } = 6.5f;
    [Export] public float RotationLag { get; set; } = 7.5f;

    private Node3D? _target;
    private HelicopterController? _heli;
    private float _orbitAngle;
    private Vector3 _flybyPoint;
    private float _shake;

    public override void _Ready()
    {
        _target = GetNodeOrNull<Node3D>(TargetPath);
        _heli = _target as HelicopterController;
        Current = true;
        Fov = 68;
        Far = 12000;
        Near = 0.15f;
    }

    public void CycleMode()
    {
        Mode = Mode switch
        {
            CameraMode.Chase => CameraMode.Cockpit,
            CameraMode.Cockpit => CameraMode.Orbit,
            CameraMode.Orbit => CameraMode.Flyby,
            _ => CameraMode.Chase,
        };
        if (Mode == CameraMode.Flyby && _target is not null)
            _flybyPoint = _target.GlobalPosition + new Vector3(38, 12, 38);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target is null) return;
        float dt = (float)delta;

        // Vibration: the 2/rev shake of a two-bladed rotor, scaled by how hard the rotor
        // is working, plus a hard kick if the aircraft is in the vortex ring state.
        if (_heli is not null)
        {
            float load = (float)Mathf.Clamp(_heli.Sim.Telemetry.TorquePercent / 100.0, 0, 1.4);
            float vrs = (float)_heli.Sim.Telemetry.VrsSeverity;
            _shake = Mathf.Lerp(_shake, load * 0.012f + vrs * 0.10f, dt * 6f);
        }

        switch (Mode)
        {
            case CameraMode.Cockpit: UpdateCockpit(dt); break;
            case CameraMode.Orbit: UpdateOrbit(dt); break;
            case CameraMode.Flyby: UpdateFlyby(dt); break;
            default: UpdateChase(dt); break;
        }

        if (_shake > 0.0001f)
        {
            var jitter = new Vector3(
                (GD.Randf() - 0.5f), (GD.Randf() - 0.5f), (GD.Randf() - 0.5f)) * _shake;
            GlobalPosition += jitter;
        }
    }

    private void UpdateChase(float dt)
    {
        Basis b = _target!.GlobalTransform.Basis;

        // Follow the aircraft's heading, but only lean with its roll a little: matching
        // roll one-for-one makes the horizon spin and tells the player nothing.
        Vector3 flatForward = new Vector3(-b.Z.X, 0, -b.Z.Z).Normalized();
        if (flatForward.LengthSquared() < 0.01f) flatForward = Vector3.Forward;

        Vector3 desired = _target.GlobalPosition
                          - flatForward * ChaseOffset.Z
                          + Vector3.Up * ChaseOffset.Y
                          + b.X * ChaseOffset.X;

        // Pull back and up as speed builds, so fast flight reads as fast.
        if (_heli is not null)
        {
            float speed = (float)_heli.Sim.Telemetry.AirspeedTrue;
            desired -= flatForward * Mathf.Clamp(speed * 0.10f, 0, 6f);
            desired += Vector3.Up * Mathf.Clamp(speed * 0.02f, 0, 2f);
        }

        GlobalPosition = GlobalPosition.Lerp(desired, 1f - Mathf.Exp(-PositionLag * dt));

        Vector3 lookAt = _target.GlobalPosition + Vector3.Up * 0.4f + flatForward * 14f;
        var targetXform = GlobalTransform.LookingAt(lookAt, Vector3.Up);
        GlobalTransform = new Transform3D(
            GlobalTransform.Basis.Slerp(targetXform.Basis, 1f - Mathf.Exp(-RotationLag * dt)),
            GlobalPosition);
    }

    private void UpdateCockpit(float dt)
    {
        Transform3D t = _target!.GlobalTransform;
        GlobalPosition = t * CockpitOffset;
        // Rigid in the cockpit: the airframe is the frame of reference, which is the
        // whole point of the view.
        GlobalBasis = t.Basis;
    }

    private void UpdateOrbit(float dt)
    {
        _orbitAngle += dt * 0.25f;
        float r = 26f;
        Vector3 desired = _target!.GlobalPosition
                          + new Vector3(Mathf.Cos(_orbitAngle) * r, 7f, Mathf.Sin(_orbitAngle) * r);
        GlobalPosition = GlobalPosition.Lerp(desired, 1f - Mathf.Exp(-4f * dt));
        LookAt(_target.GlobalPosition, Vector3.Up);
    }

    private void UpdateFlyby(float dt)
    {
        // Stay put and watch it go past, then reposition once it is well away. Cheap,
        // and it sells scale better than any tracking shot.
        if (_target!.GlobalPosition.DistanceTo(_flybyPoint) > 260f)
        {
            Basis b = _target.GlobalTransform.Basis;
            Vector3 ahead = _target.GlobalPosition - b.Z * 120f;
            _flybyPoint = ahead + new Vector3(GD.Randf() * 60 - 30, 6 + GD.Randf() * 25, GD.Randf() * 60 - 30);
        }
        GlobalPosition = _flybyPoint;
        LookAt(_target.GlobalPosition, Vector3.Up);
    }
}
