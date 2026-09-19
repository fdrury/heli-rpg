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
    /// <summary>
    /// How triangular the induced inflow is across the radius, 0 uniform to 1 fully linear.
    ///
    /// A uniform induced velocity is the classical simplification, and it is the one that
    /// under-predicts autorotation: the driving region of an autorotating disc lives
    /// INBOARD, where the local upflow tilts the lift vector forward, and uniform inflow
    /// gives that region far more downwash than it really sees. The measured consequence
    /// was a rotor that could only hold 100% Nr by descending at 3700 fpm, where the real
    /// aircraft holds it at 1700.
    ///
    /// Real rotors are closer to triangular - induced velocity small at the root, largest
    /// near the tip. The shape here is (4/3)·r/R, whose area-weighted mean is 8/9, so
    /// turning it on shifts inflow outboard without greatly changing the total.
    /// </summary>
    public double RadialInflow { get; set; } = 0.0;

    /// <summary>
    /// How much of the element velocity is resolved on the FLAPPED BLADE's normal rather
    /// than on the shaft axis. 1 is correct blade-element theory; 0 reproduces the older,
    /// shaft-normal behaviour, and it is a knob only so the difference stays measurable.
    ///
    /// Blade-element theory defines U_P along the coned blade's normal, which carries the
    /// classical mu*beta*cos(psi) term - the in-plane freestream blowing up through the
    /// front of a coned disc and down through the back. Dropping it costs almost nothing
    /// in powered flight, where beta is small and the term averages out over a revolution,
    /// but in a descent it is a real part of the upflow the driving region of the disc
    /// runs on: measured on the six-DOF autorotation trim, 2475 -> 2319 fpm at 70 kt
    /// (2.86:1 -> 3.06:1) with hover power unchanged (815 -> 814 kW).
    /// </summary>
    // DEFAULT 0 - the term is correct physics and currently breaks lateral control.
    //
    // At 1.0 it buys a real autorotation improvement (best glide 1.98:1 -> 2.54:1) and
    // costs right cyclic: the Godot bridge self-test measures -31.7 deg/s of roll for a
    // RIGHT cyclic input, with the sim and the rigid body disagreeing (-31.7 against
    // +11.2), where at 0.0 they agree exactly at +49.8. The spanwise term puts a 1/rev
    // variation into U_P, which through the usual 90-degree gyroscopic lag becomes lateral
    // flapping - so it biases roll by construction, and the bias is currently large enough
    // to reverse the control. Flipping its sign makes both numbers worse (-64.1 deg/s,
    // glide 1.94:1), so it is not a simple sign error.
    //
    // Kept, flagged and measured rather than deleted: the physics is real and the
    // autorotation gap (D-041) is still open. Flying the aircraft correctly wins until
    // somebody works out why the lateral bias is that big.
    public double ConingInflow { get; set; } = 0.0;

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
