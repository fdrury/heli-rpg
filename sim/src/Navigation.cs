namespace Rotorwash.Sim;

/// <summary>
/// Bearing and distance between two points in NED world coordinates.
///
/// The world is 13 km corner to corner and the curvature error at that scale is 1.3 m,
/// so flat-earth geometry is exact for everything this game needs.
/// </summary>
public static class Navigation
{
    /// <summary>
    /// True bearing from one point to another, radians, 0 = north, positive clockwise.
    /// NED convention: north and east in metres.
    /// </summary>
    public static double BearingRad(double fromN, double fromE, double toN, double toE)
    {
        double dn = toN - fromN;
        double de = toE - fromE;
        double b = Math.Atan2(de, dn);
        if (b < 0) b += 2.0 * Math.PI;
        return b;
    }

    /// <summary>Flat-earth distance, metres.</summary>
    public static double DistanceM(double fromN, double fromE, double toN, double toE)
    {
        double dn = toN - fromN;
        double de = toE - fromE;
        return Math.Sqrt(dn * dn + de * de);
    }

    /// <summary>
    /// How far the target bearing is from the aircraft heading, radians, -PI to +PI.
    /// Positive means the target is to the right.
    /// </summary>
    public static double RelativeBearing(double headingRad, double bearingRad)
    {
        double d = bearingRad - headingRad;
        while (d > Math.PI) d -= 2.0 * Math.PI;
        while (d < -Math.PI) d += 2.0 * Math.PI;
        return d;
    }
}

/// <summary>
/// A place the pilot wants to fly to. Computed each frame from active contracts and the
/// search thread, passed to the HUD so the compass can show where it is.
/// </summary>
public readonly record struct NavTarget(string Name, double NorthM, double EastM);
