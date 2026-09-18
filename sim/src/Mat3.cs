namespace Rotorwash.Sim;

/// <summary>Row-major 3x3 matrix, used for inertia tensors.</summary>
public readonly struct Mat3
{
    public readonly double M00, M01, M02;
    public readonly double M10, M11, M12;
    public readonly double M20, M21, M22;

    public Mat3(double m00, double m01, double m02,
                double m10, double m11, double m12,
                double m20, double m21, double m22)
    {
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
    }

    public static readonly Mat3 Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Mat3 Diagonal(double xx, double yy, double zz) => new(xx, 0, 0, 0, yy, 0, 0, 0, zz);
    public static Mat3 Diagonal(Vec3 d) => Diagonal(d.X, d.Y, d.Z);

    public static Vec3 operator *(Mat3 m, Vec3 v) => new(
        m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
        m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
        m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

    public static Mat3 operator +(Mat3 a, Mat3 b) => new(
        a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02,
        a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12,
        a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22);

    public static Mat3 operator *(Mat3 a, double s) => new(
        a.M00 * s, a.M01 * s, a.M02 * s,
        a.M10 * s, a.M11 * s, a.M12 * s,
        a.M20 * s, a.M21 * s, a.M22 * s);

    public double Determinant =>
        M00 * (M11 * M22 - M12 * M21)
      - M01 * (M10 * M22 - M12 * M20)
      + M02 * (M10 * M21 - M11 * M20);

    public Mat3 Inverse()
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-12) return Identity;
        double i = 1.0 / det;
        return new Mat3(
            (M11 * M22 - M12 * M21) * i, (M02 * M21 - M01 * M22) * i, (M01 * M12 - M02 * M11) * i,
            (M12 * M20 - M10 * M22) * i, (M00 * M22 - M02 * M20) * i, (M02 * M10 - M00 * M12) * i,
            (M10 * M21 - M11 * M20) * i, (M01 * M20 - M00 * M21) * i, (M00 * M11 - M01 * M10) * i);
    }

    public Vec3 DiagonalVector => new(M00, M11, M22);
}
