namespace Rotorwash.Sim;

/// <summary>
/// Geometry and structural properties of a lifting rotor.
///
/// Everything a player can change in the block builder eventually lands in here:
/// bolting on longer blades raises <see cref="Radius"/>, which raises thrust but also
/// tip Mach, blade inertia and the rotor drag you have to feed with an engine. The
/// point of a physical rotor model is that those trades are computed, not authored.
/// </summary>
public sealed class RotorConfig
{
    public int NumBlades { get; init; } = 4;

    /// <summary>Rotor radius, m.</summary>
    public double Radius { get; init; } = 8.18;

    /// <summary>Blade chord, m (rectangular planform).</summary>
    public double Chord { get; init; } = 0.53;

    /// <summary>Inboard fraction of the radius that carries no aerofoil.</summary>
    public double RootCutout { get; init; } = 0.15;

    /// <summary>Total linear washout from root to tip, radians (negative = nose-down at tip).</summary>
    public double Twist { get; init; } = -0.18;

    /// <summary>Flapping hinge offset as a fraction of radius. 0 = teetering rotor.</summary>
    public double HingeOffset { get; init; } = 0.045;

    /// <summary>Mass of one blade, kg.</summary>
    public double BladeMass { get; init; } = 110.0;

    /// <summary>Rotational inertia of the whole rotor system about the shaft, kg.m^2.</summary>
    public double RotorInertia { get; init; } = 5800.0;

    /// <summary>+1 = anticlockwise seen from above (US convention). -1 = clockwise (French/Russian).</summary>
    public int SpinSign { get; init; } = 1;

    /// <summary>Nominal (100% Nr) rotor speed, rad/s.</summary>
    public double NominalOmega { get; init; } = 27.0;

    /// <summary>Forward tilt of the mast, radians. Lets the fuselage sit level in cruise.</summary>
    public double ShaftTiltForward { get; init; } = 0.052;

    /// <summary>Lateral tilt of the mast, radians (positive = tilted right).</summary>
    public double ShaftTiltRight { get; init; } = 0.0;

    /// <summary>Hub position relative to the airframe centre of gravity, body FRD, m.</summary>
    public Vec3 HubPosition { get; init; } = new(0.0, 0.0, -2.2);

    /// <summary>Collective pitch at the 75% station, radians, at zero and full lever.</summary>
    public double CollectiveMin { get; init; } = -3.0 * Math.PI / 180.0;
    public double CollectiveMax { get; init; } = 16.0 * Math.PI / 180.0;

    /// <summary>Maximum cyclic-commanded disc tilt authority, radians.</summary>
    public double CyclicRange { get; init; } = 9.0 * Math.PI / 180.0;

    /// <summary>Pitch-flap coupling (delta-3): blade pitch decreases as it flaps up.</summary>
    public double Delta3 { get; init; } = 0.0;

    public Airfoil Blade { get; init; } = new();

    /// <summary>Radial integration stations per blade.</summary>
    public int RadialStations { get; init; } = 10;

    /// <summary>Rotor substeps per physics step. More = better flapping fidelity.</summary>
    public int Substeps { get; init; } = 4;

    // ---- Derived -----------------------------------------------------------
    public double DiscArea => Math.PI * Radius * Radius;

    /// <summary>Solidity: blade area / disc area.</summary>
    public double Solidity => NumBlades * Chord * Radius * (1.0 - RootCutout) / DiscArea;

    /// <summary>Flapping inertia of one blade about its hinge, kg.m^2 (uniform bar approximation).</summary>
    public double BladeFlapInertia => BladeMass * Radius * Radius / 3.0;

    /// <summary>Spanwise centre of mass of one blade, m from the hinge.</summary>
    public double BladeCgRadius => Radius * 0.5;

    /// <summary>Non-dimensional flapping frequency. 1.0 for a teetering rotor.</summary>
    public double FlapFrequencyRatio
    {
        get
        {
            double e = Math.Clamp(HingeOffset, 0.0, 0.3);
            return Math.Sqrt(1.0 + 1.5 * e / Math.Max(1.0 - e, 1e-3));
        }
    }

    /// <summary>Lock number - the ratio of aerodynamic to inertial flapping forces.</summary>
    public double LockNumber(double rho) =>
        rho * Blade.LiftSlope * Chord * Math.Pow(Radius, 4) / Math.Max(BladeFlapInertia, 1e-6);
}
