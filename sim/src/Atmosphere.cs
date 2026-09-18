namespace Rotorwash.Sim;

/// <summary>
/// International Standard Atmosphere, troposphere only (0-11 km), with optional
/// temperature offset so "hot and high" is an actual gameplay problem: density
/// altitude is what strands you on a summer afternoon at a mountain settlement.
/// </summary>
public sealed class Atmosphere
{
    public const double SeaLevelPressure = 101325.0;   // Pa
    public const double SeaLevelTemp = 288.15;         // K
    public const double LapseRate = 0.0065;            // K/m
    public const double GasConstant = 287.05287;       // J/(kg K)
    public const double Gravity = 9.80665;             // m/s^2
    public const double SeaLevelDensity = 1.225;       // kg/m^3

    /// <summary>Degrees C above (or below) the ISA standard for this altitude.</summary>
    public double IsaDeviation { get; set; }

    public double TemperatureAt(double altitudeM) =>
        SeaLevelTemp - LapseRate * Math.Max(altitudeM, -500.0) + IsaDeviation;

    public double PressureAt(double altitudeM)
    {
        double tStd = SeaLevelTemp - LapseRate * Math.Max(altitudeM, -500.0);
        double exp = Gravity / (LapseRate * GasConstant);
        return SeaLevelPressure * Math.Pow(tStd / SeaLevelTemp, exp);
    }

    public double DensityAt(double altitudeM) => PressureAt(altitudeM) / (GasConstant * TemperatureAt(altitudeM));

    public double SpeedOfSoundAt(double altitudeM) => Math.Sqrt(1.4 * GasConstant * TemperatureAt(altitudeM));

    /// <summary>Pressure altitude corrected for temperature - the number that actually limits your payload.</summary>
    public double DensityAltitude(double altitudeM)
    {
        double rho = DensityAt(altitudeM);
        return 44330.0 * (1.0 - Math.Pow(rho / SeaLevelDensity, 1.0 / 4.256));
    }
}
