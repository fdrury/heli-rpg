namespace Rotorwash.Sim;

/// <summary>
/// Sectional 2-D aerodynamics for a rotor blade element.
///
/// Deliberately analytic rather than table-driven: a rotor blade in forward flight
/// sees the full +/-180 degree range of angle of attack (the retreating blade near the
/// root flies backwards), so a model that degrades gracefully everywhere beats an
/// interpolated table with holes in it. Linear lift slope near zero alpha, blended
/// into flat-plate behaviour through stall, with a compressibility correction so the
/// advancing tip actually costs you something at speed.
/// </summary>
public sealed class Airfoil
{
    /// <summary>Lift-curve slope, per radian. ~5.73 for a NACA 0012-ish section.</summary>
    public double LiftSlope { get; init; } = 5.73;
    /// <summary>Angle of attack for zero lift (radians). 0 for a symmetric section.</summary>
    public double AlphaZeroLift { get; init; } = 0.0;
    /// <summary>Static stall angle (radians).</summary>
    public double AlphaStall { get; init; } = 14.0 * Math.PI / 180.0;
    /// <summary>Profile drag at zero lift.</summary>
    public double Cd0 { get; init; } = 0.0087;
    /// <summary>Induced/profile drag growth with lift, Cd += K * Cl^2.</summary>
    public double DragK { get; init; } = 0.0216;
    /// <summary>Drag-divergence Mach number.</summary>
    public double MachDrag { get; init; } = 0.74;
    /// <summary>Maximum Cl achieved at the stall break.</summary>
    public double ClMax => LiftSlope * (AlphaStall - AlphaZeroLift);

    /// <param name="alpha">Angle of attack, radians, any magnitude.</param>
    /// <param name="mach">Local Mach number (absolute).</param>
    public void Coefficients(double alpha, double mach, out double cl, out double cd)
    {
        // Wrap into [-pi, pi] so a blade in reverse flow is handled, not NaN'd.
        alpha = WrapPi(alpha);

        double a = alpha - AlphaZeroLift;
        double absA = Math.Abs(a);

        // --- Lift -------------------------------------------------------------
        // Below stall: linear. Above: blend to a flat plate (2 sin a cos a) over a
        // few degrees so the stall break is sharp but the derivative stays finite,
        // which keeps the flapping integrator stable during retreating-blade stall.
        double clLinear = LiftSlope * a;
        double clPlate = 2.0 * Math.Sin(a) * Math.Cos(a);

        const double blendWidth = 6.0 * Math.PI / 180.0;
        double t = Smoothstep(AlphaStall, AlphaStall + blendWidth, absA);
        cl = clLinear * (1 - t) + clPlate * t;

        // Cap the linear branch at ClMax so a very high pitch angle can't make lift
        // from nothing before the blend takes over.
        if (t < 1.0)
        {
            double cap = ClMax * 1.05;
            cl = Math.Clamp(cl, -cap, cap) * (1 - t) + cl * t;
        }

        // --- Drag -------------------------------------------------------------
        double cdAttached = Cd0 + DragK * cl * cl;
        double cdPlate = 0.05 + 2.0 * Math.Sin(a) * Math.Sin(a);
        cd = cdAttached * (1 - t) + cdPlate * t;

        // --- Compressibility --------------------------------------------------
        // Prandtl-Glauert lift growth below Mdd, then a drag rise that punishes the
        // advancing tip. This is why Vne exists and why a big fast rotor is expensive.
        if (mach > 0.3 && mach < 0.97)
        {
            double pg = 1.0 / Math.Sqrt(1.0 - mach * mach);
            cl *= Math.Min(pg, 2.5);
        }
        if (mach > MachDrag)
        {
            double dm = mach - MachDrag;
            cd += 12.0 * dm * dm * dm;   // classic cubic drag-divergence rise
        }
    }

    public static double WrapPi(double a)
    {
        a = Math.IEEERemainder(a, 2 * Math.PI);
        if (a > Math.PI) a -= 2 * Math.PI;
        if (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static double Smoothstep(double e0, double e1, double x)
    {
        if (e1 <= e0) return x < e0 ? 0 : 1;
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
