namespace Rotorwash.Sim;

/// <summary>
/// Double-precision 3-vector. The simulation deliberately does not use
/// System.Numerics (float) or Godot.Vector3: the rotor inflow solver and the
/// 6-DOF integrator both benefit from double precision, and the sim assembly
/// must stay engine-free so it can be unit tested without Godot.
///
/// AXIS CONVENTION INSIDE THE SIM: aerospace body axes, "FRD".
///     X = forward (nose)
///     Y = right   (starboard)
///     Z = down
/// Conversion to/from Godot's Y-up, -Z-forward world happens in exactly one
/// place: the GodotBridge adapter. See docs/wiki/01-flight-model.md.
/// </summary>
public readonly struct Vec3 : IEquatable<Vec3>
{
    public readonly double X, Y, Z;

    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Forward = new(1, 0, 0);
    public static readonly Vec3 Right = new(0, 1, 0);
    public static readonly Vec3 Down = new(0, 0, 1);
    public static readonly Vec3 Up = new(0, 0, -1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public Vec3 Normalized
    {
        get { double l = Length; return l > 1e-12 ? this / l : Zero; }
    }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public bool Equals(Vec3 o) => X.Equals(o.X) && Y.Equals(o.Y) && Z.Equals(o.Z);
    public override bool Equals(object? o) => o is Vec3 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}
