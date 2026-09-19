using Godot;

namespace Rotorwash;

public enum CameraMode { Chase, Cockpit, Orbit, Flyby, ThirdPerson }

/// <summary>
/// Camera rig for the helicopter and the pilot on foot.
///
/// Chase is deliberately not rigidly attached: it lags in position and leads in
/// direction, so the aircraft visibly moves inside the frame. A helicopter manoeuvres by
/// changing attitude long before it changes direction, and a camera welded to the
/// airframe hides exactly the information the pilot needs.
///
/// ThirdPerson follows the pilot on foot with an over-shoulder offset. The camera's
/// delta is compensated for Rotor Time so the player's view stays responsive during
/// slow-motion.
///
/// Cockpit mode supports head-look: middle-mouse drag, hat switch / D-pad, or numpad
/// slew the view within the cockpit. Release springs back to forward. L padlocks onto
/// the nearest detected threat or known site.
/// </summary>
public sealed partial class ChaseCamera : Camera3D
{
    [Export] public NodePath TargetPath { get; set; } = "";
    [Export] public CameraMode Mode { get; set; } = CameraMode.Chase;

    // Low and well back. Sitting the camera high turns the game into a map: a pilot
    // reads attitude against the horizon, so the horizon has to be in shot.
    [Export] public Vector3 ChaseOffset { get; set; } = new(0, 2.1f, 16.0f);
    /// <summary>
    /// Where the pilot's eyes are, in the airframe's local frame.
    ///
    /// Was (-0.62, 0.55, +1.45). Forward is -Z and the cockpit spans Z -4.0 to -2.4, so
    /// +1.45 put the viewpoint behind the CG in the engine bay, looking forward through the
    /// length of the fuselage. That is why the "cockpit" view was a black slab with panes
    /// of glass floating in it: the camera was inside the hull with every body panel
    /// backface-culled away from it, and only the double-sided glass left to see.
    /// </summary>
    [Export] public Vector3 CockpitOffset { get; set; } = new(-0.62f, 0.22f, -2.15f);
    [Export] public Vector3 ThirdPersonOffset { get; set; } = new(0.6f, 1.8f, 3.5f);

    [Export] public float PositionLag { get; set; } = 6.5f;
    [Export] public float RotationLag { get; set; } = 7.5f;

    private Node3D? _target;
    private HelicopterController? _heli;
    private PilotController? _pilot;
    private float _orbitAngle;
    private Vector3 _flybyPoint;
    private float _shake;

    // --------------------------------------------------------- head-look state
    // A Huey has big side windows and a chin bubble, so the pilot can look almost
    // directly behind and well below the horizon.
    private const float HeadYawLimit = 150f * Mathf.Pi / 180f;
    private const float HeadPitchDown = -40f * Mathf.Pi / 180f;
    private const float HeadPitchUp = 60f * Mathf.Pi / 180f;
    private const float HeadReturnRate = 4f;
    private const float MouseLookSens = 0.003f;
    private const float HatLookRate = 2.5f;  // rad/s

    private float _headYaw;
    private float _headPitch;
    private bool _lookActiveThisFrame;

    // Hat / keyboard look input, set each frame by Main before physics runs.
    private float _hatYaw, _hatPitch;

    // Padlock
    private bool _padlocked;
    private Vector3 _padlockTarget;

    /// <summary>Head yaw in radians (0 = forward, positive = right).</summary>
    public float HeadYaw => _headYaw;
    /// <summary>Head pitch in radians (0 = level, positive = up).</summary>
    public float HeadPitch => _headPitch;
    /// <summary>True when padlocked to a target.</summary>
    public bool IsPadlocked => _padlocked;

    public override void _Ready()
    {
        _target = GetNodeOrNull<Node3D>(TargetPath);
        _heli = _target as HelicopterController;
        Current = true;
        Fov = 68;
        Far = 12000;
        Near = 0.15f;
    }

    /// <summary>Set the pilot for third-person mode. Called by Main after construction.</summary>
    public void SetPilot(PilotController pilot) => _pilot = pilot;

    public void CycleMode()
    {
        // Only cycle through flight camera modes; ThirdPerson is set by the game mode.
        Mode = Mode switch
        {
            CameraMode.Chase => CameraMode.Cockpit,
            CameraMode.Cockpit => CameraMode.Orbit,
            CameraMode.Orbit => CameraMode.Flyby,
            _ => CameraMode.Chase,
        };
        if (Mode == CameraMode.Flyby && _target is not null)
            _flybyPoint = _target.GlobalPosition + new Vector3(38, 12, 38);
        ResetHead();
    }

    /// <summary>Switch to third-person or back to chase for mode transitions.</summary>
    public void SetOnFoot(bool onFoot)
    {
        Mode = onFoot ? CameraMode.ThirdPerson : CameraMode.Chase;
        ResetHead();
    }

    // --------------------------------------------------------- head-look API

    /// <summary>Apply relative mouse motion to head-look (middle-mouse drag).</summary>
    public void ApplyHeadLook(Vector2 delta)
    {
        if (Mode != CameraMode.Cockpit) return;
        _headYaw = Mathf.Clamp(_headYaw + delta.X * MouseLookSens, -HeadYawLimit, HeadYawLimit);
        _headPitch = Mathf.Clamp(_headPitch - delta.Y * MouseLookSens, HeadPitchDown, HeadPitchUp);
        _lookActiveThisFrame = true;
        _padlocked = false;
    }

    /// <summary>Store hat / keyboard look demand. Called by Main each physics frame.</summary>
    public void SetLookInput(float yaw, float pitch)
    {
        _hatYaw = yaw;
        _hatPitch = pitch;
    }

    /// <summary>Padlock onto a world-space target, or release if null.</summary>
    public void Padlock(Vector3? target)
    {
        if (target.HasValue)
        {
            _padlockTarget = target.Value;
            _padlocked = true;
        }
        else
        {
            _padlocked = false;
        }
    }

    /// <summary>Snap head to forward and release padlock.</summary>
    public void CenterHead()
    {
        _headYaw = 0;
        _headPitch = 0;
        _padlocked = false;
    }

    /// <summary>Set head angles directly (for scripted shots). Prevents spring return.</summary>
    public void SetHeadAngles(float yaw, float pitch)
    {
        _headYaw = Mathf.Clamp(yaw, -HeadYawLimit, HeadYawLimit);
        _headPitch = Mathf.Clamp(pitch, HeadPitchDown, HeadPitchUp);
        _lookActiveThisFrame = true;
    }

    private void ResetHead()
    {
        _headYaw = 0;
        _headPitch = 0;
        _padlocked = false;
        _lookActiveThisFrame = false;
        _hatYaw = 0;
        _hatPitch = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        // During Rotor Time the engine delta is scaled. The camera should stay responsive,
        // so we compensate: camera moves at real-time rate, world slows around it.
        float timeScale = (float)Engine.TimeScale;
        float dt = timeScale > 0.01f ? (float)delta / timeScale : (float)delta;

        // Vibration: the 2/rev shake of a two-bladed rotor, scaled by how hard the rotor
        // is working, plus a hard kick if the aircraft is in the vortex ring state.
        if (_heli is not null && Mode != CameraMode.ThirdPerson)
        {
            float load = (float)Mathf.Clamp(_heli.Sim.Telemetry.TorquePercent / 100.0, 0, 1.4);
            float vrs = (float)_heli.Sim.Telemetry.VrsSeverity;
            _shake = Mathf.Lerp(_shake, load * 0.012f + vrs * 0.10f, dt * 6f);
        }
        else if (Mode == CameraMode.ThirdPerson)
        {
            _shake = Mathf.Lerp(_shake, 0, dt * 6f);
        }

        switch (Mode)
        {
            case CameraMode.ThirdPerson: UpdateThirdPerson(dt); break;
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

        // --- head-look ---

        // Hat / keyboard continuous input
        if (Mathf.Abs(_hatYaw) > 0.1f || Mathf.Abs(_hatPitch) > 0.1f)
        {
            _headYaw = Mathf.Clamp(_headYaw + _hatYaw * HatLookRate * dt, -HeadYawLimit, HeadYawLimit);
            _headPitch = Mathf.Clamp(_headPitch + _hatPitch * HatLookRate * dt, HeadPitchDown, HeadPitchUp);
            _lookActiveThisFrame = true;
            _padlocked = false;
        }

        // Padlock: slew to track the target
        if (_padlocked)
        {
            Vector3 toTarget = _padlockTarget - GlobalPosition;
            if (toTarget.LengthSquared() > 1f)
            {
                Vector3 localDir = t.Basis.Inverse() * toTarget.Normalized();
                float wantYaw = Mathf.Atan2(localDir.X, -localDir.Z);
                float wantPitch = Mathf.Asin(Mathf.Clamp(localDir.Y, -1, 1));
                _headYaw = Mathf.Lerp(_headYaw,
                    Mathf.Clamp(wantYaw, -HeadYawLimit, HeadYawLimit),
                    1f - Mathf.Exp(-8f * dt));
                _headPitch = Mathf.Lerp(_headPitch,
                    Mathf.Clamp(wantPitch, HeadPitchDown, HeadPitchUp),
                    1f - Mathf.Exp(-8f * dt));
            }
        }
        // Spring return when idle
        else if (!_lookActiveThisFrame)
        {
            float decay = 1f - Mathf.Exp(-HeadReturnRate * dt);
            _headYaw = Mathf.Lerp(_headYaw, 0, decay);
            _headPitch = Mathf.Lerp(_headPitch, 0, decay);
            if (Mathf.Abs(_headYaw) < 0.001f) _headYaw = 0;
            if (Mathf.Abs(_headPitch) < 0.001f) _headPitch = 0;
        }
        _lookActiveThisFrame = false;

        // Apply head rotation on top of the rigid airframe basis
        if (_headYaw != 0 || _headPitch != 0)
        {
            Basis yaw = new(Vector3.Up, _headYaw);
            Basis pitch = new(yaw.X, _headPitch);
            GlobalBasis = t.Basis * (pitch * yaw);
        }
        else
        {
            GlobalBasis = t.Basis;
        }
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

    private void UpdateThirdPerson(float dt)
    {
        if (_pilot is null) return;

        // The camera sits behind and above the pilot's right shoulder.
        // Direction comes from the pilot's camera pivot (mouse-driven pitch + yaw).
        Transform3D pivotXform = _pilot.CameraPivot.GlobalTransform;
        Vector3 pivotPos = pivotXform.Origin;

        // Offset is applied in the pilot's local space so the camera orbits with mouse look.
        Basis pilotBasis = _pilot.GlobalTransform.Basis;
        Vector3 back = -pivotXform.Basis.Z;
        Vector3 up = Vector3.Up;
        Vector3 right = pilotBasis.X;

        Vector3 desired = pivotPos
                          + back * ThirdPersonOffset.Z
                          + up * (ThirdPersonOffset.Y - 1.6f)  // offset relative to pivot
                          + right * ThirdPersonOffset.X;

        GlobalPosition = GlobalPosition.Lerp(desired, 1f - Mathf.Exp(-5f * dt));

        // Look at a point slightly ahead and above the pilot.
        Vector3 lookTarget = pivotPos - pivotXform.Basis.Z * 8f;
        var targetXform = GlobalTransform.LookingAt(lookTarget, Vector3.Up);
        GlobalTransform = new Transform3D(
            GlobalTransform.Basis.Slerp(targetXform.Basis, 1f - Mathf.Exp(-12f * dt)),
            GlobalPosition);
    }
}
