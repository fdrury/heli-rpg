namespace Rotorwash.Sim;

/// <summary>
/// What the world tells the flight model. In the game this is backed by the terrain and
/// weather systems; in tests it is flat ground and still air.
///
/// World frame is NED to match the body frame: X north, Y east, Z DOWN. Altitude is
/// therefore -Position.Z. The Godot bridge is the only place that flips this.
/// </summary>
public interface IEnvironment
{
    /// <summary>Terrain height above datum at a world position, m (positive up).</summary>
    double GroundHeight(double north, double east);

    /// <summary>Wind velocity in world NED axes, m/s.</summary>
    Vec3 Wind(Vec3 positionNed);

    Atmosphere Atmosphere { get; }
}

/// <summary>Flat ground, still air. The baseline every physics test is written against.</summary>
public sealed class FlatEnvironment : IEnvironment
{
    public double GroundElevation { get; set; }
    public Vec3 SteadyWind { get; set; }

    /// <summary>Gust intensity, m/s RMS. Zero by default so tests are deterministic.</summary>
    public double Turbulence { get; set; }

    public Atmosphere Atmosphere { get; } = new();

    private readonly Random _rng = new(9001);
    private Vec3 _gust;

    public double GroundHeight(double north, double east) => GroundElevation;

    public Vec3 Wind(Vec3 positionNed) => SteadyWind + _gust;

    /// <summary>Advance the turbulence filter. Call once per physics step if Turbulence &gt; 0.</summary>
    public void StepTurbulence(double dt)
    {
        if (Turbulence <= 0) { _gust = Vec3.Zero; return; }
        double a = 1.0 - Math.Exp(-dt / 1.5);
        Vec3 target = new(
            (_rng.NextDouble() * 2 - 1) * Turbulence,
            (_rng.NextDouble() * 2 - 1) * Turbulence,
            (_rng.NextDouble() * 2 - 1) * Turbulence * 0.6);
        _gust = Vec3.Lerp(_gust, target, a);
    }
}
