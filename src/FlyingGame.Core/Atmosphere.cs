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

    /// <summary>Non-standard day: offset from ISA sea-level temperature (K). +15 = a hot day (thinner air),
    /// -30 = a cold day (denser air). Default 0 = standard day (59 °F at sea level).</summary>
    public static double IsaDeviationK { get; set; }

    /// <summary>Warmth (K above ambient) at the core of a thermal — the rising air is what heats the bubbles'
    /// tint. Visual/atmospheric only; the thermal's lift is what the wings feel.</summary>
    public const double ThermalCoreWarmthK = 4.0;

    /// <summary>ISA pressure (Pa) at altitude (m), standard troposphere.</summary>
    public static double PressureAtAltitude(double altitudeM)
    {
        double clampedAlt = System.Math.Clamp(altitudeM, 0.0, 11000.0);
        double isaTemp = SeaLevelTempK - LapseRateKPerM * clampedAlt;
        return SeaLevelPressurePa * System.Math.Pow(isaTemp / SeaLevelTempK, GravityMs2 / (LapseRateKPerM * GasConstantAir));
    }

    /// <summary>Ambient air temperature (K) at altitude (m): ISA lapse plus the day's ISA deviation.</summary>
    public static double TemperatureAtAltitudeK(double altitudeM)
    {
        double clampedAlt = System.Math.Clamp(altitudeM, 0.0, 11000.0);
        return SeaLevelTempK - LapseRateKPerM * clampedAlt + IsaDeviationK;
    }

    /// <summary>Air density (kg/m^3) at a given altitude above sea level (m), ISA troposphere model
    /// (with the day's ISA deviation applied to temperature — hot day = thinner air).</summary>
    public static double DensityAtAltitude(double altitudeM)
    {
        return PressureAtAltitude(altitudeM) / (GasConstantAir * TemperatureAtAltitudeK(altitudeM));
    }

    /// <summary>Local air temperature (K) at a world position: ambient at that altitude, plus the warmth of
    /// any thermal core the point sits in (same Gaussian profile as the thermal's updraft).</summary>
    public static double TemperatureAtPosition(MathTypes.Vec3 worldPosition)
    {
        double altitudeM = -worldPosition.Z;
        double t = TemperatureAtAltitudeK(altitudeM);
        for (int i = 0; i < Thermals.Count; i++)
        {
            t += ThermalCoreWarmthK * ThermalStrengthScale * Thermals[i].CoreFractionAt(worldPosition);
        }

        return t;
    }

    /// <summary>Local air density (kg/m^3) at a world position: ISA pressure at that altitude over the LOCAL
    /// temperature (so a thermal's warm core is slightly thinner than the air around it).</summary>
    public static double DensityAtPosition(MathTypes.Vec3 worldPosition)
    {
        double altitudeM = -worldPosition.Z;
        return PressureAtAltitude(altitudeM) / (GasConstantAir * TemperatureAtPosition(worldPosition));
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

    /// <summary>Thermals (rising columns) active in the world; glider soaring energy.</summary>
    public static System.Collections.Generic.List<Thermal> Thermals { get; } = new();

    /// <summary>Global multiplier on thermal strength (0 = off, 1 = as built, 2 = booming day).</summary>
    public static double ThermalStrengthScale { get; set; } = 1.0;

    /// <summary>Terrain-following slope lift over the whole height field (needs WorldTerrain.Active).</summary>
    public static bool SlopeLiftEnabled { get; set; }

    /// <summary>Ridge for slope soaring (null = none).</summary>
    public static Ridge? ActiveRidge { get; set; }

    /// <summary>Wind (world frame, m/s): steady wind + turbulence + thermals + ridge lift.</summary>
    public static MathTypes.Vec3 WindAtPosition(MathTypes.Vec3 worldPosition) => WindAt(worldPosition, true);

    /// <summary>The MEAN flow only (steady wind, thermals, slope lift) — no gusts. The fuselage crossflow terms
    /// use this: they are quadratic bluff-body terms for large-angle flow, not a gust-response model.</summary>
    public static MathTypes.Vec3 MeanWindAtPosition(MathTypes.Vec3 worldPosition) => WindAt(worldPosition, false);

    private static MathTypes.Vec3 WindAt(MathTypes.Vec3 worldPosition, bool withGusts)
    {
        MathTypes.Vec3 w = SteadyWind + (withGusts ? ActiveTurbulence?.WindAt(worldPosition, SimTimeSec) ?? MathTypes.Vec3.Zero : MathTypes.Vec3.Zero);
        for (int i = 0; i < Thermals.Count; i++)
        {
            w += Thermals[i].WindAt(worldPosition) * ThermalStrengthScale;
        }

        if (ActiveRidge is not null)
        {
            w += ActiveRidge.WindAt(worldPosition, SteadyWind);
        }
        if (SlopeLiftEnabled && WorldTerrain.Active is not null)
        {
            w += SlopeLift.WindAt(WorldTerrain.Active, worldPosition, SteadyWind);
        }

        return w;
    }
}
