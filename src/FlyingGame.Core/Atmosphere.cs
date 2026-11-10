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

    /// <summary>Depth of the surface layer (m) for <see cref="NoThroughFlow"/>. Published from the regional config
    /// when one is loaded (AtmosphereField's constructor) so there is one number whichever path is flying.</summary>
    public static double SurfaceLayerM { get; set; } = 50.0;

    /// <summary>
    /// Air cannot come out of the earth. Fades the VERTICAL part of a wind contribution to nothing at the surface,
    /// over <see cref="SurfaceLayerM"/>; the horizontal part is untouched, because no-through-flow binds only the
    /// component normal to the ground.
    ///
    /// This belongs HERE, where every contribution is finally added together, and not in the thing that produces any
    /// one of them. Ty 2026-11-08, after the first attempt put it inside AtmosphereField alone: "I flew 3u2 again and
    /// the bubbles were still coming out of the ground and rising straight up at the surface my cub was climbing at
    /// over 300 fpm at idle." The field's own slope and convective lift were faded, and then WindFromSample added the
    /// AUTHORED thermal columns straight on top of them — 10 to 14 m/s cores, one per plateau, 3U2's among them —
    /// whose profile is a fraction of ABSOLUTE altitude, so on a plateau nearly 5000 ft up a column is already half
    /// developed where the wheels are. Fixing the producer I happened to be looking at left the one that mattered.
    /// </summary>
    public static MathTypes.Vec3 NoThroughFlow(MathTypes.Vec3 worldPosition, MathTypes.Vec3 wind)
    {
        if (wind.Z > -1e-9 && wind.Z < 1e-9) return wind;   // nothing vertical: do not pay for a ground query
        double ground = WorldTerrain.GroundHeightAt(worldPosition.X, worldPosition.Y);
        double? water = FloatHydro.WaterSurfaceAt(worldPosition.X, worldPosition.Y, -worldPosition.Z);
        if (water.HasValue && water.Value > ground) ground = water.Value;
        double taper = AtmosphereField.VerticalGroundTaper(-worldPosition.Z - ground, SurfaceLayerM);
        return taper >= 1.0 ? wind : new MathTypes.Vec3(wind.X, wind.Y, wind.Z * taper);
    }

    /// <summary>The wind at a point from a regional sample: its mean (and gust), the authored thermals at the field's
    /// thermal factor, and the glacier wind outside the calm zone. Everything added to the field's own mean is held to
    /// <see cref="NoThroughFlow"/>; the mean itself was already faded by the field.</summary>
    public static MathTypes.Vec3 WindFromSample(MathTypes.Vec3 worldPosition, in AtmosphereSample s, bool withGusts)
    {
        MathTypes.Vec3 extra = withGusts ? s.Gust : MathTypes.Vec3.Zero;
        double thermal = s.ThermalFactor * ThermalStrengthScale;
        if (thermal > 0)
            for (int i = 0; i < Thermals.Count; i++) extra += Thermals[i].WindAt(worldPosition) * thermal;
        double keep = 1.0 - s.CalmWeight;
        if (keep > 0) extra += BraidedRiver.WindAt(worldPosition.X, worldPosition.Y, -worldPosition.Z) * keep;
        return s.MeanWind + NoThroughFlow(worldPosition, extra);
    }

    /// <summary>
    /// THE WIND AN AEROPLANE OF THIS SPAN ACTUALLY FEELS. <see cref="WindFromSample"/> hands every airframe the gust at
    /// a single point, but a wing integrates a gust across its span and averages away everything smaller than itself
    /// (<see cref="Turbulence.UnitGustAt(Vec3,double,double,double,Vec3,double,Vec3,out Vec3)"/>). This rebuilds the
    /// spectral part of the gust with that averaging, from the sample's own sigma and length scales, and also returns
    /// the gust's FIRST MOMENT across the span — the rolling cue a soaring pilot reads, in closed form instead of by
    /// differencing two point samples of a field with metre-scale structure in it.
    ///
    /// Everything else in the wind is left alone: the mean wind, the discrete gust increment, the thermal columns and
    /// the braided-river flow are all far larger than a wingspan, so a point sample of them is the right answer.
    /// </summary>
    public static MathTypes.Vec3 WindForSpan(MathTypes.Vec3 worldPosition, in AtmosphereSample s,
                                             double spanM, MathTypes.Vec3 spanAxisWorld, out MathTypes.Vec3 gustGradPerMetre)
    {
        gustGradPerMetre = MathTypes.Vec3.Zero;
        var field = Field;
        if (field is null || !(spanM > 0.1) || (s.SigmaHorizMs <= 0 && s.SigmaVertMs <= 0))
            return WindFromSample(worldPosition, s, true);

        MathTypes.Vec3 unit = field.SpectralTurbulence.UnitGustAt(worldPosition, SimTimeSec, s.LengthHorizM, s.LengthVertM,
                                                                  field.AdvectionWind, spanM, spanAxisWorld, out MathTypes.Vec3 unitGrad);
        // The discrete gust increment is whatever the sample's spectral part was not: keep it as sampled.
        MathTypes.Vec3 spectral = new(unit.X * s.SigmaHorizMs, unit.Y * s.SigmaHorizMs, unit.Z * s.SigmaVertMs);
        MathTypes.Vec3 unfiltered = field.SpectralTurbulence.UnitGustAt(worldPosition, SimTimeSec, s.LengthHorizM, s.LengthVertM, field.AdvectionWind);
        MathTypes.Vec3 spectralRaw = new(unfiltered.X * s.SigmaHorizMs, unfiltered.Y * s.SigmaHorizMs, unfiltered.Z * s.SigmaVertMs);

        MathTypes.Vec3 extra = s.Gust - spectralRaw + spectral;
        double thermal = s.ThermalFactor * ThermalStrengthScale;
        if (thermal > 0)
            for (int i = 0; i < Thermals.Count; i++) extra += Thermals[i].WindAt(worldPosition) * thermal;
        double keep = 1.0 - s.CalmWeight;
        if (keep > 0) extra += BraidedRiver.WindAt(worldPosition.X, worldPosition.Y, -worldPosition.Z) * keep;
        gustGradPerMetre = new MathTypes.Vec3(unitGrad.X * s.SigmaHorizMs, unitGrad.Y * s.SigmaHorizMs, unitGrad.Z * s.SigmaVertMs);
        return s.MeanWind + NoThroughFlow(worldPosition, extra);
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

    /// <summary>
    /// THE ONE CLOCK (local 2-player, 2026-11-05). Every <c>Aircraft.Step</c> used to add its own dt to
    /// <see cref="SimTimeSec"/>, so the world clock ran once per aircraft: the player at 200 Hz and every combat drone,
    /// formation ship, dogfight opponent and tow tug at their own rate each added a full second per second. Fifteen
    /// drones made the turbulence field, the tide and the thermals run sixteen times too fast. A second local player
    /// would have doubled it again.
    ///
    /// When an owner is set, only the owner's steps advance the clock (<see cref="AdvanceTime(object, double)"/>); every
    /// other aircraft reads it. The game sets the first player's aeroplane (FlightSimDriver, on every new Aircraft).
    /// Null (headless tools, tests, the Aero Widget) keeps the old rule: every step advances it.
    /// </summary>
    public static object? ClockOwner { get; set; }

    /// <summary>Advance the clock by <paramref name="dt"/> if <paramref name="stepper"/> owns it (or nobody does).</summary>
    public static void AdvanceTime(object stepper, double dt)
    {
        if (ClockOwner is null || ReferenceEquals(ClockOwner, stepper)) SimTimeSec += dt;
    }

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
        // Everything but the air mass's own horizontal drift goes through NoThroughFlow together.
        MathTypes.Vec3 up = withGusts ? ActiveTurbulence?.WindAt(worldPosition, SimTimeSec) ?? MathTypes.Vec3.Zero : MathTypes.Vec3.Zero;
        for (int i = 0; i < Thermals.Count; i++)
        {
            up += Thermals[i].WindAt(worldPosition) * ThermalStrengthScale;
        }

        if (ActiveRidge is not null)
        {
            up += ActiveRidge.WindAt(worldPosition, SteadyWind);
        }
        if (SlopeLiftEnabled && WorldTerrain.Active is not null)
        {
            up += SlopeLift.WindAt(WorldTerrain.Active, worldPosition, steady);
        }
        // The glacier wind (owner 2026-10-16): cold air slides off the ice all day and runs down the outwash
        // plain, so a landing upstream on a gravel bar is always into a headwind. It is a shallow layer and
        // it is always there, whatever the session's weather says.
        up += BraidedRiver.WindAt(worldPosition.X, worldPosition.Y, -worldPosition.Z);

        return steady + NoThroughFlow(worldPosition, up);
    }
}
