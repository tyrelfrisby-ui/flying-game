namespace FlyingGame.Core;

/// <summary>
/// ISA atmosphere stub (troposphere only — plenty for arena 1's low-altitude glider slice;
/// thin-air arenas can extend this later per ARCHITECTURE.md).
/// </summary>
public static class Atmosphere
{
    public const double SeaLevelDensityKgM3 = 1.225;
    public const double SeaLevelPressurePa = 101325.0;
    public const double SeaLevelTempK = 288.15;
    public const double LapseRateKPerM = 0.0065;
    public const double GasConstantAir = 287.05287;
    public const double GravityMs2 = 9.80665;

    /// <summary>Air density (kg/m^3) at a given altitude above sea level (m), ISA troposphere model.</summary>
    public static double DensityAtAltitude(double altitudeM)
    {
        double clampedAlt = System.Math.Clamp(altitudeM, 0.0, 11000.0);
        double temperature = SeaLevelTempK - LapseRateKPerM * clampedAlt;
        double pressure = SeaLevelPressurePa * System.Math.Pow(temperature / SeaLevelTempK, GravityMs2 / (LapseRateKPerM * GasConstantAir));
        return pressure / (GasConstantAir * temperature);
    }

    /// <summary>Steady wind + gust field, still air (zero) for the v0 vertical slice.</summary>
    public static MathTypes.Vec3 WindAtPosition(MathTypes.Vec3 worldPosition) => MathTypes.Vec3.Zero;
}
