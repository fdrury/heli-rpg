namespace Rotorwash.Sim;

/// <summary>What the ground under the aircraft is like. Produced by the world, read by the game.</summary>
public enum SurfaceKind { Rock, Hardpack, Grass, Loose, Sand, Water, Metal, Concrete }

/// <summary>An assessment of somewhere to put the aircraft down.</summary>
public readonly record struct LandingSite(
    double SlopeDegrees,
    double RoughnessMetres,     // peak-to-trough under the skid footprint
    double ClearanceRadius,     // metres of rotor clearance before something is in the way
    SurfaceKind Surface,
    double HeightAgl)
{
    /// <summary>
    /// Verdict, 0 (do not) to 1 (put it anywhere). Deliberately not a pass/fail: the
    /// interesting landings are the marginal ones, and the pilot should be the one who
    /// decides whether 0.4 is good enough today.
    /// </summary>
    public double Quality(double maxSlopeDegrees, double rotorRadius)
    {
        if (Surface == SurfaceKind.Water) return 0;

        double slope = 1.0 - Math.Clamp(SlopeDegrees / Math.Max(maxSlopeDegrees, 1e-3), 0, 1);
        double rough = 1.0 - Math.Clamp(RoughnessMetres / 0.55, 0, 1);
        double clear = Math.Clamp((ClearanceRadius - rotorRadius * 1.15) / (rotorRadius * 0.6), 0, 1);

        // The worst factor dominates: a perfectly flat pad with a tree over it is not a
        // landing site, and neither is a wide-open slope you would slide off.
        return Math.Min(Math.Min(slope, rough), clear);
    }

    public string Verdict(double maxSlopeDegrees, double rotorRadius)
    {
        if (Surface == SurfaceKind.Water) return "water";
        double q = Quality(maxSlopeDegrees, rotorRadius);
        if (ClearanceRadius < rotorRadius * 1.05) return "no rotor clearance";
        if (SlopeDegrees > maxSlopeDegrees) return "too steep";
        if (RoughnessMetres > 0.55) return "too rough";
        return q > 0.75 ? "good" : q > 0.45 ? "marginal" : "poor";
    }
}

/// <summary>What happened when the skids touched.</summary>
public readonly record struct TouchdownReport(
    double VerticalSpeed,       // m/s, positive down
    double GroundSpeed,         // m/s
    double RollDegrees,
    double PitchDegrees,
    double SlopeDegrees,
    bool RotorStrike,
    bool Rollover,
    double StructuralDamage,    // 0..1 applied to the skids
    string Summary);

/// <summary>
/// Landing: assessment, touchdown quality, dynamic rollover and brownout.
///
/// The benchmark pass concluded that in a world a helicopter crosses in five minutes,
/// *flying* cannot be the expensive act - so landing is. This class is where that cost
/// lives. None of it is arbitrary: every threshold below comes from the geometry and mass
/// properties the flight model already has.
/// </summary>
public static class Landing
{
    /// <summary>
    /// Critical static rollover angle: the bank at which the centre of gravity passes
    /// outside the downhill skid and the aircraft goes over. Pure geometry - a tall,
    /// narrow aircraft rolls over sooner, and the block builder changes both numbers.
    /// </summary>
    public static double CriticalRollAngle(Airframe airframe, Vec3 centreOfGravity)
    {
        double halfTrack = 0, skidDrop = 0;
        foreach (Vec3 p in airframe.ContactPoints)
        {
            halfTrack = Math.Max(halfTrack, Math.Abs(p.Y - centreOfGravity.Y));
            skidDrop = Math.Max(skidDrop, p.Z - centreOfGravity.Z);
        }
        if (halfTrack < 1e-3 || skidDrop < 1e-3) return Math.PI * 0.5;
        return Math.Atan2(halfTrack, skidDrop);
    }

    /// <summary>
    /// How far the rotor tips hang below the hub, accounting for coning and disc tilt. This
    /// is the number that decides whether a slope landing ends with a rotor strike, and it
    /// is why you approach a rising slope nose-in and never tail-in.
    /// </summary>
    public static double TipDroopBelowHub(RotorConfig rotor, double coning, double discTilt)
    {
        double angle = Math.Max(0, discTilt - coning);
        return rotor.Radius * Math.Sin(angle);
    }

    /// <summary>
    /// Evaluate a touchdown. Thresholds are set by what skid gear is actually built for:
    /// a utility helicopter absorbs about 2 m/s without complaint, is designed to survive
    /// about 3 m/s, and folds somewhere past 6.
    /// </summary>
    public static TouchdownReport Evaluate(Airframe airframe, Vec3 centreOfGravity,
                                           double verticalSpeed, double groundSpeed,
                                           double rollRad, double pitchRad, double slopeRad,
                                           bool rotorStruck)
    {
        double rollDeg = rollRad * 180 / Math.PI;
        double pitchDeg = pitchRad * 180 / Math.PI;
        double slopeDeg = slopeRad * 180 / Math.PI;
        double critical = CriticalRollAngle(airframe, centreOfGravity) * 180 / Math.PI;

        // Rollover is about where the centre of gravity sits relative to the skid that is
        // carrying the weight. The primary term is bank against the HORIZON, because that
        // is what gravity sees. Slope adds only a modest amount - it decides which skid
        // becomes the pivot and offsets the CG toward it - and drifting sideways onto a
        // planted skid is what turns a survivable angle into a dynamic rollover.
        double drift = Math.Max(0, groundSpeed - 0.8) * 2.2;
        double effectiveRoll = Math.Abs(rollDeg) + slopeDeg * 0.35 + drift * (Math.Abs(rollDeg) > 5 ? 1.0 : 0.25);
        bool rollover = effectiveRoll > critical || Math.Abs(pitchDeg) > critical * 1.25;

        double damage = 0;
        string summary;

        if (rotorStruck)
        {
            damage = 1.0;
            summary = "rotor strike";
        }
        else if (rollover)
        {
            damage = 0.9;
            summary = $"dynamic rollover at {effectiveRoll:F0} deg (critical {critical:F0})";
        }
        else if (verticalSpeed > 6.0)
        {
            damage = 0.85;
            summary = $"gear collapse, {verticalSpeed:F1} m/s";
        }
        else if (verticalSpeed > 3.0)
        {
            damage = (verticalSpeed - 3.0) / 3.0 * 0.6;
            summary = $"hard landing, {verticalSpeed:F1} m/s";
        }
        else if (verticalSpeed > 1.8)
        {
            damage = (verticalSpeed - 1.8) / 1.2 * 0.12;
            summary = $"firm, {verticalSpeed:F1} m/s";
        }
        else
        {
            summary = groundSpeed > 3.0 ? $"run-on, {groundSpeed:F1} m/s" : "clean";
        }

        // Sliding sideways onto a skid is how a rollover starts, not how it ends.
        if (!rollover && groundSpeed > 2.0 && Math.Abs(rollDeg) > 6)
        {
            damage = Math.Max(damage, 0.25);
            summary += " with drift";
        }

        return new TouchdownReport(verticalSpeed, groundSpeed, rollDeg, pitchDeg, slopeDeg,
                                   rotorStruck, rollover, Math.Clamp(damage, 0, 1), summary);
    }

    /// <summary>
    /// Brownout intensity, 0 to 1. A helicopter landing on anything loose recirculates its
    /// own downwash and disappears inside a dust cloud somewhere in the last ten metres -
    /// exactly when the pilot most needs to see the ground. It is the single most dangerous
    /// routine thing a helicopter does, and it is free drama.
    /// </summary>
    public static double Brownout(SurfaceKind surface, double heightAgl, double rotorRadius,
                                  double downwashSpeed, double groundSpeed)
    {
        double susceptibility = surface switch
        {
            SurfaceKind.Sand => 1.0,
            SurfaceKind.Loose => 0.85,
            SurfaceKind.Hardpack => 0.40,
            SurfaceKind.Grass => 0.22,
            SurfaceKind.Rock => 0.12,
            SurfaceKind.Concrete => 0.10,
            SurfaceKind.Metal => 0.05,
            SurfaceKind.Water => 0.55,   // spray, not dust, but it blinds you the same way
            _ => 0.3,
        };
        if (susceptibility <= 0.01) return 0;

        // The cloud forms when the wake reaches the ground and recirculates, which happens
        // inside roughly two rotor radii and becomes total in the last few metres - which
        // is exactly when the pilot most needs to see the ground.
        double proximity = 1.0 - Math.Clamp(heightAgl / (rotorRadius * 2.2), 0, 1);
        proximity = Math.Pow(proximity, 1.2);

        double energy = Math.Clamp(downwashSpeed / 12.0, 0, 1.4);

        // Translational lift blows the cloud behind you. Above about 10 kt you fly out of
        // your own dust, which is why running landings exist.
        double stationary = 1.0 - Math.Clamp(groundSpeed / 6.0, 0, 1);

        return Math.Clamp(susceptibility * proximity * energy * stationary, 0, 1);
    }
}
