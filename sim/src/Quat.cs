namespace Rotorwash.Sim;

/// <summary>Double-precision unit quaternion. Rotates body-frame vectors into world frame.</summary>
public readonly struct Quat
{
    public readonly double W, X, Y, Z;

    public Quat(double w, double x, double y, double z) { W = w; X = x; Y = y; Z = z; }

    public static readonly Quat Identity = new(1, 0, 0, 0);

    public static Quat FromAxisAngle(Vec3 axis, double angle)
    {
        Vec3 n = axis.Normalized;
        double h = angle * 0.5, s = Math.Sin(h);
        return new Quat(Math.Cos(h), n.X * s, n.Y * s, n.Z * s);
    }

    /// <summary>Roll (about X/forward), pitch (about Y/right), yaw (about Z/down), applied Z-Y-X.</summary>
    public static Quat FromEuler(double roll, double pitch, double yaw)
    {
        double cr = Math.Cos(roll * 0.5), sr = Math.Sin(roll * 0.5);
        double cp = Math.Cos(pitch * 0.5), sp = Math.Sin(pitch * 0.5);
        double cy = Math.Cos(yaw * 0.5), sy = Math.Sin(yaw * 0.5);
        return new Quat(
            cr * cp * cy + sr * sp * sy,
            sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy);
    }

    /// <summary>
    /// Build a quaternion from the three body axes expressed in world coordinates.
    /// Used by the engine bridge, which knows the aircraft orientation as a basis and
    /// would otherwise have to convert through Euler angles and lose precision at the poles.
    /// </summary>
    public static Quat FromColumns(Vec3 xAxis, Vec3 yAxis, Vec3 zAxis)
    {
        double m00 = xAxis.X, m10 = xAxis.Y, m20 = xAxis.Z;
        double m01 = yAxis.X, m11 = yAxis.Y, m21 = yAxis.Z;
        double m02 = zAxis.X, m12 = zAxis.Y, m22 = zAxis.Z;

        double trace = m00 + m11 + m22;
        if (trace > 0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;
            return new Quat(0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s).Normalized;
        }
        if (m00 > m11 && m00 > m22)
        {
            double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
            return new Quat((m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s).Normalized;
        }
        if (m11 > m22)
        {
            double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
            return new Quat((m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s).Normalized;
        }
        else
        {
            double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
            return new Quat((m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s).Normalized;
        }
    }

    public double Roll  => Math.Atan2(2 * (W * X + Y * Z), 1 - 2 * (X * X + Y * Y));
    public double Pitch { get { double s = 2 * (W * Y - Z * X); return Math.Asin(Math.Clamp(s, -1, 1)); } }
    public double Yaw   => Math.Atan2(2 * (W * Z + X * Y), 1 - 2 * (Y * Y + Z * Z));

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public Quat Conjugate => new(W, -X, -Y, -Z);

    public Quat Normalized
    {
        get
        {
            double n = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
            return n > 1e-12 ? new Quat(W / n, X / n, Y / n, Z / n) : Identity;
        }
    }

    /// <summary>Body -> world.</summary>
    public Vec3 Rotate(Vec3 v)
    {
        // v' = v + 2w(q x v) + 2(q x (q x v))
        Vec3 q = new(X, Y, Z);
        Vec3 t = Vec3.Cross(q, v) * 2.0;
        return v + t * W + Vec3.Cross(q, t);
    }

    /// <summary>World -> body.</summary>
    public Vec3 InverseRotate(Vec3 v) => Conjugate.Rotate(v);

    /// <summary>Derivative of the quaternion given body-frame angular velocity (rad/s).</summary>
    public Quat Derivative(Vec3 omegaBody)
    {
        Quat w = new(0, omegaBody.X, omegaBody.Y, omegaBody.Z);
        Quat d = this * w;
        return new Quat(d.W * 0.5, d.X * 0.5, d.Y * 0.5, d.Z * 0.5);
    }

    public Quat Integrated(Vec3 omegaBody, double dt)
    {
        Quat d = Derivative(omegaBody);
        return new Quat(W + d.W * dt, X + d.X * dt, Y + d.Y * dt, Z + d.Z * dt).Normalized;
    }

    public override string ToString() =>
        $"[roll {Roll * 180 / Math.PI:F1}, pitch {Pitch * 180 / Math.PI:F1}, yaw {Yaw * 180 / Math.PI:F1}]";
}
