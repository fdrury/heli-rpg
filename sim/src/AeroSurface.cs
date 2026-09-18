namespace Rotorwash.Sim;

/// <summary>
/// A flat lifting surface - horizontal stabiliser, vertical fin, stub wing, or anything
/// the player bolts on in the builder. Defined by where it is, which way it faces, how
/// big it is and how it stalls; everything else is computed.
/// </summary>
public sealed class AeroSurface
{
    public string Name { get; init; } = "surface";

    /// <summary>Position relative to the CG, body FRD, m.</summary>
    public Vec3 Position { get; init; }

    /// <summary>Surface normal in body axes: the direction positive lift pushes.</summary>
    public Vec3 Normal { get; init; } = Vec3.Up;

    /// <summary>Reference area, m^2.</summary>
    public double Area { get; init; } = 1.0;

    /// <summary>Built-in incidence, radians, positive about the surface normal.</summary>
    public double Incidence { get; init; }

    /// <summary>Aspect ratio - drives induced drag and the effective lift slope.</summary>
    public double AspectRatio { get; init; } = 4.0;

    public Airfoil Section { get; init; } = new() { AlphaStall = 12.0 * Math.PI / 180.0, Cd0 = 0.012 };

    /// <summary>
    /// Fraction of the main rotor downwash that washes over this surface in the hover,
    /// falling off with forward speed. A horizontal stabiliser sitting in the wake is
    /// the reason many helicopters pitch about as they accelerate through 30 kt.
    /// </summary>
    public double DownwashFactor { get; init; }

    /// <summary>Compute force and moment about the CG.</summary>
    public void Compute(Vec3 vAirBody, Vec3 omegaBody, double rho, double soundSpeed,
                        double rotorDownwash, double advanceRatio,
                        out Vec3 force, out Vec3 moment, Vec3 cg = default)
    {
        Vec3 pos = Position - cg;
        Vec3 v = vAirBody + Vec3.Cross(omegaBody, pos);

        if (DownwashFactor > 0 && rotorDownwash != 0)
        {
            double washout = 1.0 / (1.0 + Math.Pow(advanceRatio / 0.06, 2.0));
            v += Vec3.Down * (rotorDownwash * DownwashFactor * washout);
        }

        double speed = v.Length;
        if (speed < 0.5) { force = Vec3.Zero; moment = Vec3.Zero; return; }

        Vec3 n = Normal.Normalized;
        double vn = Vec3.Dot(v, n);

        // Chordwise direction: the part of the flow lying in the surface plane.
        Vec3 chordFlow = v - n * vn;
        double chordSpeed = chordFlow.Length;
        if (chordSpeed < 0.3) { force = Vec3.Zero; moment = Vec3.Zero; return; }
        Vec3 chordDir = chordFlow / chordSpeed;

        // Positive vn means the flow is coming at the surface from the +normal side,
        // which produces lift toward -normal.
        double alpha = Math.Atan2(-vn, chordSpeed) + Incidence;

        Section.Coefficients(alpha, speed / soundSpeed, out double cl, out double cd);

        // Finite-span correction: thin surfaces lift less per degree and drag more.
        double ar = Math.Max(AspectRatio, 0.5);
        cl *= ar / (ar + 2.0);
        cd += cl * cl / (Math.PI * ar * 0.85);

        double q = 0.5 * rho * speed * speed * Area;

        Vec3 dragDir = -v.Normalized;
        Vec3 liftDir = Vec3.Cross(Vec3.Cross(dragDir, n), dragDir).Normalized;
        if (Vec3.Dot(liftDir, n) < 0) liftDir = -liftDir;

        force = liftDir * (q * cl) + dragDir * (q * cd);
        moment = Vec3.Cross(pos, force);
    }
}
