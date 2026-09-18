using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// The single place where the simulation's aerospace axes meet Godot's.
///
/// The sim thinks in FRD (forward / right / down) body axes and NED world axes, because
/// that is what every piece of rotorcraft literature the flight model is built from uses.
/// Godot thinks in X-right / Y-up / Z-back. Converting in one file - and never anywhere
/// else - is the only way to keep sign errors from becoming a permanent tax.
///
///     sim forward (1,0,0) -> godot (0, 0,-1)
///     sim right   (0,1,0) -> godot (1, 0, 0)
///     sim down    (0,0,1) -> godot (0,-1, 0)
///
/// The mapping is a proper rotation (determinant +1), so angular velocities and torques
/// convert with exactly the same function as positions and forces.
/// </summary>
public static class SimBridge
{
    /// <summary>Godot vector -> sim vector (works for both world and body frames).</summary>
    public static Vec3 ToSim(Vector3 g) => new(-g.Z, g.X, -g.Y);

    /// <summary>Sim vector -> Godot vector.</summary>
    public static Vector3 ToGodot(Vec3 v) => new((float)v.Y, (float)-v.Z, (float)-v.X);

    /// <summary>Sim world position (NED) from a Godot world position.</summary>
    public static Vec3 PositionToSim(Vector3 g) => new(-g.Z, g.X, -g.Y);

    /// <summary>Godot world position from a sim NED position.</summary>
    public static Vector3 PositionToGodot(Vec3 v) => new((float)v.Y, (float)-v.Z, (float)-v.X);

    /// <summary>
    /// Sim orientation quaternion from a Godot basis. The sim body axes map onto Godot
    /// body axes as forward = -Z, right = +X, down = -Y, so each sim axis is read off the
    /// corresponding Godot basis column and converted into sim world coordinates.
    /// </summary>
    public static Quat OrientationToSim(Basis basis)
    {
        Vec3 xAxis = ToSim(-basis.Z);   // sim forward
        Vec3 yAxis = ToSim(basis.X);    // sim right
        Vec3 zAxis = ToSim(-basis.Y);   // sim down
        return Quat.FromColumns(xAxis, yAxis, zAxis);
    }

    /// <summary>Godot basis from a sim orientation quaternion.</summary>
    public static Basis OrientationToGodot(Quat q)
    {
        Vector3 forward = ToGodot(q.Rotate(Vec3.Forward));
        Vector3 right = ToGodot(q.Rotate(Vec3.Right));
        Vector3 up = ToGodot(q.Rotate(Vec3.Up));
        return new Basis(right, up, -forward);
    }

    public const float MetresPerSecondToKnots = 1.94384f;
    public const float MetresPerSecondToFpm = 196.85f;
    public const float MetresToFeet = 3.28084f;
}
