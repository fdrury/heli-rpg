using Rotorwash.Sim;

namespace Rotorwash.SimLab;

/// <summary>
/// Verify the flat-earth bearing and distance math that the HUD compass depends on.
/// </summary>
public static class NavigationTests
{
    /// <summary>
    /// Cardinal directions: a target due north, east, south and west must produce the
    /// expected bearing, and atan2's branch cut must not appear.
    /// </summary>
    public static string? Cardinals()
    {
        // Due north: bearing 0
        double b = Navigation.BearingRad(0, 0, 1000, 0);
        if (Math.Abs(b) > 0.001) return $"north bearing {b:F4}, expected 0";

        // Due east: bearing pi/2
        b = Navigation.BearingRad(0, 0, 0, 1000);
        if (Math.Abs(b - Math.PI / 2) > 0.001) return $"east bearing {b:F4}, expected {Math.PI / 2:F4}";

        // Due south: bearing pi
        b = Navigation.BearingRad(0, 0, -1000, 0);
        if (Math.Abs(b - Math.PI) > 0.001) return $"south bearing {b:F4}, expected {Math.PI:F4}";

        // Due west: bearing 3*pi/2
        b = Navigation.BearingRad(0, 0, 0, -1000);
        if (Math.Abs(b - 3 * Math.PI / 2) > 0.001) return $"west bearing {b:F4}, expected {3 * Math.PI / 2:F4}";

        Console.WriteLine("  cardinal bearings: N=0, E=pi/2, S=pi, W=3pi/2  — correct");
        return null;
    }

    /// <summary>
    /// Distance is Euclidean. A 3-4-5 triangle must produce exactly 5000 m.
    /// </summary>
    public static string? Distance()
    {
        double d = Navigation.DistanceM(0, 0, 3000, 4000);
        if (Math.Abs(d - 5000) > 0.01) return $"3-4-5 distance {d:F2}, expected 5000";

        // Zero distance
        d = Navigation.DistanceM(100, 200, 100, 200);
        if (d > 0.001) return $"same point distance {d}, expected 0";

        Console.WriteLine($"  3-4-5 triangle: {d:F1} m  — correct");
        return null;
    }

    /// <summary>
    /// Relative bearing: a target 90° right of heading must produce +pi/2,
    /// a target behind and left must produce a negative value near -pi.
    /// </summary>
    public static string? RelBearing()
    {
        // Heading north, target due east → +pi/2
        double r = Navigation.RelativeBearing(0, Math.PI / 2);
        if (Math.Abs(r - Math.PI / 2) > 0.001) return $"rel bearing right {r:F4}";

        // Heading north, target due west → -pi/2
        r = Navigation.RelativeBearing(0, 3 * Math.PI / 2);
        if (Math.Abs(r + Math.PI / 2) > 0.001) return $"rel bearing left {r:F4}, expected {-Math.PI / 2:F4}";

        // Heading east, target north → -pi/2 (target is to the left)
        r = Navigation.RelativeBearing(Math.PI / 2, 0);
        if (Math.Abs(r + Math.PI / 2) > 0.001) return $"heading east target north {r:F4}";

        // Heading south, target north → +pi (or -pi, both valid for directly behind)
        r = Navigation.RelativeBearing(Math.PI, 0);
        if (Math.Abs(Math.Abs(r) - Math.PI) > 0.001) return $"behind {r:F4}";

        Console.WriteLine($"  relative bearings: right +pi/2, left -pi/2, behind ±pi  — correct");
        return null;
    }

    /// <summary>
    /// Round-trip: compute bearing and distance from A to B, then walk along that bearing
    /// for that distance, and verify we arrive at B.
    /// </summary>
    public static string? RoundTrip()
    {
        double aN = 1200, aE = -800;
        double bN = 4500, bE = 3200;

        double bearing = Navigation.BearingRad(aN, aE, bN, bE);
        double dist = Navigation.DistanceM(aN, aE, bN, bE);

        double cN = aN + dist * Math.Cos(bearing);
        double cE = aE + dist * Math.Sin(bearing);

        double err = Navigation.DistanceM(bN, bE, cN, cE);
        if (err > 0.01) return $"round-trip error {err:F4} m, expected < 0.01";

        Console.WriteLine($"  bearing {bearing * 180 / Math.PI:F1}°, dist {dist:F0} m, round-trip error {err:F6} m");
        return null;
    }
}
