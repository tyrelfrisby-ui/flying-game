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
    /// any thermal core the point sits in (same Gaussian profile as the thermal's updraft). With a regional
    /// <see cref="Field"/> the ambient is the region's.</summary>
    public static double TemperatureAtPosition(MathTypes.Vec3 worldPosition)
    {
        double altitudeM = -worldPosition.Z;
        double t, scale = ThermalStrengthScale;
        if (Field is not null)
        {
            AtmosphereSample s = Field.Sample(worldPosition, SimTimeSec);
            t = s.TemperatureK;
            scale *= s.ThermalFactor;
        }
        else t = TemperatureAtAltitudeK(altitudeM);
        if (scale == 0) return t;
        for (int i = 0; i < Thermals.Count; i++)
        {
            t += ThermalCoreWarmthK * scale * Thermals[i].CoreFractionAt(worldPosition);
        }

        return t;
    }

    /// <summary>Local air density (kg/m^3) at a world position: ISA pressure at that altitude over the LOCAL
    /// temperature (so a thermal's warm core is slightly thinner than the air around it).</summary>
    public static double DensityAtPosition(MathTypes.Vec3 worldPosition)
    {
        double altitudeM = -worldPosition.Z;
        double p = Field is not null ? Field.Sample(worldPosition, SimTimeSec).PressurePa : PressureAtAltitude(altitudeM);
        return p / (GasConstantAir * TemperatureAtPosition(worldPosition));
    }

    /// <summary>Static pressure (Pa) at a position: the region's (its QNH) with a field, ISA without.</summary>
    public static double PressureAt(MathTypes.Vec3 worldPosition) =>
        Field is not null ? Field.Sample(worldPosition, SimTimeSec).PressurePa : PressureAtAltitude(-worldPosition.Z);

    /// <summary>Air density (kg/m^3) for the flight model at a position: the regional field's when there is one,
    /// otherwise exactly <see cref="DensityAtAltitude"/> (the uniform atmosphere, unchanged).</summary>
    public static double DensityAt(MathTypes.Vec3 worldPosition) =>
        Field is not null ? Field.Sample(worldPosition, SimTimeSec).DensityKgM3 : DensityAtAltitude(-worldPosition.Z);

    /// <summary>
    /// THE REGIONAL ATMOSPHERE (null = the one uniform atmosphere this class has always been). When set, the field owns
    /// the wind, its profile, the turbulence, the terrain lift and the temperature/pressure: <see cref="SteadyWind"/>,
    /// <see cref="ActiveTurbulence"/>, <see cref="ActiveRidge"/> and <see cref="SlopeLiftEnabled"/> are not read. The
    /// authored <see cref="Thermals"/> still fly, scaled by the field's thermal factor (zero in the calm zone). A
    /// uniform override (a challenge's headwind, a practice crosswind, a contest's locked weather) is simply
    /// Field = null.
    /// </summary>
    public static AtmosphereField? Field { get; set; }

    /// <summary>Add a thermal column unless the regional field forbids one there (the calm zone and its blend band):
    /// the one door thermals should come in by. Returns false, and adds nothing, if refused.</summary>
    public static bool TryAddThermal(Thermal thermal)
    {
        if (Field is not null && !Field.AllowsThermalAt(thermal.SurfaceCenter.X, thermal.SurfaceCenter.Y)) return false;
        Thermals.Add(thermal);
        return true;
    }

    /// <summary>The full regional sample at a position now (null without a field).</summary>
    public static AtmosphereSample? SampleAt(MathTypes.Vec3 worldPosition) => Field?.Sample(worldPosition, SimTimeSec);

    /// <summary>The horizontal wind the air mass drifts with here, its height profile applied — what a thermal leans
    /// in and what carries smoke and bubbles. Uniform: <see cref="SteadyWind"/> × <see cref="WindGradientFactor"/>.</summary>
    public static MathTypes.Vec3 SteadyWindAt(MathTypes.Vec3 worldPosition) =>
        Field is not null ? Field.SteadyWindAt(worldPosition, SimTimeSec) : SteadyWind * WindGradientFactor(worldPosition);

    /// <summary>The wind at a point from a regional sample: its mean (and gust), the authored thermals at the field's
    /// thermal factor, and the glacier wind outside the calm zone.</summary>
    public static MathTypes.Vec3 WindFromSample(MathTypes.Vec3 worldPosition, in AtmosphereSample s, bool withGusts)
    {
        MathTypes.Vec3 w = withGusts ? s.MeanWind + s.Gust : s.MeanWind;
        double thermal = s.ThermalFactor * ThermalStrengthScale;
        if (thermal > 0)
            for (int i = 0; i < Thermals.Count; i++) w += Thermals[i].WindAt(worldPosition) * thermal;
        double keep = 1.0 - s.CalmWeight;
        if (keep > 0) w += BraidedRiver.WindAt(worldPosition.X, worldPosition.Y, -worldPosition.Z) * keep;
        return w;
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

    /// <summary>Wind speed grows with height (surface friction slows the low layers): the power-law boundary
    /// layer U(z) = U10 · (z/10 m)^0.14 over open country, so the wind at 900 m — the first wall's crest — is
    /// about 1.9× the 10 m wind. Referenced to height above the valley floor (the sea-level datum here) and
    /// capped at 2.2×, so lift up the ridge is stronger the higher you go.</summary>
    public static double WindGradientFactor(MathTypes.Vec3 worldPosition)
    {
        double z = System.Math.Max(10.0, -worldPosition.Z);
        return System.Math.Min(2.2, System.Math.Pow(z / 10.0, 0.14));
    }

    private static MathTypes.Vec3 WindAt(MathTypes.Vec3 worldPosition, bool withGusts)
    {
        if (Field is not null) return WindFromSample(worldPosition, Field.Sample(worldPosition, SimTimeSec), withGusts);
        MathTypes.Vec3 steady = SteadyWind * WindGradientFactor(worldPosition);
        MathTypes.Vec3 w = steady + (withGusts ? ActiveTurbulence?.WindAt(worldPosition, SimTimeSec) ?? MathTypes.Vec3.Zero : MathTypes.Vec3.Zero);
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
            w += SlopeLift.WindAt(WorldTerrain.Active, worldPosition, steady);
        }
        // The glacier wind (owner 2026-10-16): cold air slides off the ice all day and runs down the outwash
        // plain, so a landing upstream on a gravel bar is always into a headwind. It is a shallow layer and
        // it is always there, whatever the session's weather says.
        w += BraidedRiver.WindAt(worldPosition.X, worldPosition.Y, -worldPosition.Z);

        return w;
    }
}
