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

    /// <summary>Active turbulence field (null = calm). Set by the host (challenge/scene/weather).</summary>
    public static Turbulence? ActiveTurbulence { get; set; }

    /// <summary>Steady mean wind (world frame, m/s) — the air mass drifting over the ground. This is
    /// what makes headwind/tailwind/crosswind: it adds to every wind sample, so an aircraft in a
    /// crosswind crabs (heading ≠ ground track) and drifts sideways on landing.</summary>
    public static MathTypes.Vec3 SteadyWind { get; set; } = MathTypes.Vec3.Zero;

    /// <summary>Shared sim clock (s) for the frozen turbulence field; advanced by the sim each step.</summary>
    public static double SimTimeSec { get; set; }

    public static void AdvanceTime(double dt) => SimTimeSec += dt;

    /// <summary>Wind (world frame, m/s) at a position: steady wind + turbulence gust.</summary>
    public static MathTypes.Vec3 WindAtPosition(MathTypes.Vec3 worldPosition) =>
        SteadyWind + (ActiveTurbulence?.WindAt(worldPosition, SimTimeSec) ?? MathTypes.Vec3.Zero);
}
