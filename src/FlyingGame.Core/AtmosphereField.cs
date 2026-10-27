using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>Everything the air is doing at one point and time.</summary>
public readonly struct AtmosphereSample
{
    /// <summary>The mean flow (world, m/s): regional wind with its profile and speed-up, slope lift, lee sink, rotor,
    /// lee waves, background convection. No gusts.</summary>
    public readonly Vec3 MeanWind;
    /// <summary>The fluctuation (world, m/s): spectral turbulence plus discrete gusts.</summary>
    public readonly Vec3 Gust;
    public readonly double TemperatureK, PressurePa, DensityKgM3, DensityAltitudeM, QnhPa, IsaDeviationK;
    /// <summary>Turbulence RMS (horizontal, vertical) and its length scales, after every multiplier.</summary>
    public readonly double SigmaHorizMs, SigmaVertMs, LengthHorizM, LengthVertM;
    /// <summary>Multiplier the authored thermal columns get here (0 in the calm zone).</summary>
    public readonly double ThermalFactor;
    /// <summary>1 inside the calm zone, 0 clear of it, smooth in between.</summary>
    public readonly double CalmWeight;
    /// <summary>Diagnostics for the overlay and the tests.</summary>
    public readonly double SpeedUp, SlopeLiftMs, LeeMs, WaveMs, ConvectiveMs, HeatingIndex, GustIncrementMs;
    public readonly string RegionId;

    public AtmosphereSample(Vec3 mean, Vec3 gust, double tK, double pPa, double rho, double da, double qnh, double isaDev,
        double sh, double sv, double lh, double lv, double thermalFactor, double calm,
        double speedUp, double slope, double lee, double wave, double conv, double heat, double gustInc, string regionId)
    {
        MeanWind = mean; Gust = gust; TemperatureK = tK; PressurePa = pPa; DensityKgM3 = rho; DensityAltitudeM = da; QnhPa = qnh;
        IsaDeviationK = isaDev; SigmaHorizMs = sh; SigmaVertMs = sv; LengthHorizM = lh; LengthVertM = lv; ThermalFactor = thermalFactor;
        CalmWeight = calm; SpeedUp = speedUp; SlopeLiftMs = slope; LeeMs = lee; WaveMs = wave; ConvectiveMs = conv; HeatingIndex = heat;
        GustIncrementMs = gustInc; RegionId = regionId;
    }

    public Vec3 Wind => MeanWind + Gust;
}

/// <summary>
/// THE REGIONAL ATMOSPHERE (owner 2026-10-27: "atmospheric conditions should vary by region across the landscape
/// instead of one uniform atmosphere"). A spatial field sampled by world position and sim time.
///
/// Regions are authored shapes (circle, box, runway-style capsule, or global) with a blend band; they are layered in
/// list order, each painting its air over what is below it with weight 1 inside the shape, falling to 0 across its
/// blend band on a C2 smootherstep (no step anywhere). Each region gives: ISA deviation and QNH (so temperature,
/// pressure, density and density altitude), a lapse rate (stability), a 10 m wind with a power-law profile to a
/// gradient height, gust/turbulence intensity, roughness, a thermal multiplier and a convective depth.
///
/// On top of the regional air the terrain acts (<see cref="Orography"/>) and the ground heats (the land-cover map:
/// sun-facing rock and fields give lift, water, snow and forest give weak lift or sink). Turbulence is the spectral
/// (von Kármán / Dryden) field of <see cref="Turbulence"/>, scaled by MIL-HDBK-1797 intensities and length scales that
/// follow height, roughness, terrain and region.
///
/// Last, the CALM ZONE (<see cref="CalmZone"/>): a runway-aligned stadium round the DEFAULT START airport inside which
/// the sample is EXACTLY standard ISA with zero wind, zero gust, zero turbulence and no thermal or sink — the code
/// returns the calm sample itself there, not a blend that happens to be small. Outside it the regional sample is
/// faded in across the blend width.
///
/// Deterministic and frame-rate independent: a sample is a pure function of (position, time) and the config. The
/// random wind processes are hashed draws keyed by time index, and their caches only memoise that function.
/// </summary>
public sealed class AtmosphereField
{
    public const double KtToMs = 0.514444;
    public AtmosphereFieldConfig Config { get; }
    /// <summary>Ground height (m MSL) at (x, y). Defaults to the active world terrain (flat 0 without one). The field
    /// reads it through <see cref="GroundAt"/>, a cached grid, never directly in the hot path.</summary>
    public System.Func<double, double, double> Terrain
    {
        get => _terrain;
        set { _terrain = value; _terrainIsWorld = false; _tiles.Clear(); _heatCache.Clear(); }
    }
    private System.Func<double, double, double> _terrain = WorldTerrain.GroundHeightAt;
    private bool _terrainIsWorld = true;
    private WorldTerrain? _cachedWorld;
    private readonly System.Func<double, double, double> _ground;

    // ---- terrain cache -------------------------------------------------------------------------------------------
    // A sample looks at the ground ~19 times (the upwind line, the slope stencil, the height above ground) and the world's
    // HeightAt costs microseconds, so heights are cached on a 20 m grid in 64×64 float tiles, filled lazily, and read
    // bilinearly. The whole built world is ~3 MB of tiles; a flight touches a fraction. 20 m is well inside every
    // length the atmosphere cares about (the slope stencil is ±25 m, the walls run 180 m).
    public const double TerrainCacheM = 20.0;
    private const int Tile = 64;
    private readonly Dictionary<long, float[]> _tiles = new();

    /// <summary>Ground height (m MSL), from the cache.</summary>
    public double GroundAt(double x, double y)
    {
        if (_terrainIsWorld && !ReferenceEquals(_cachedWorld, WorldTerrain.Active)) { _tiles.Clear(); _heatCache.Clear(); _cachedWorld = WorldTerrain.Active; }
        double fx = x / TerrainCacheM, fy = y / TerrainCacheM;
        long ix = (long)System.Math.Floor(fx), iy = (long)System.Math.Floor(fy);
        double tx = fx - ix, ty = fy - iy;
        double a = Node(ix, iy), b = Node(ix + 1, iy), c = Node(ix, iy + 1), d = Node(ix + 1, iy + 1);
        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;

        double Node(long i, long j)
        {
            long ti = i >= 0 ? i / Tile : (i - Tile + 1) / Tile, tj = j >= 0 ? j / Tile : (j - Tile + 1) / Tile;
            long key = (ti << 32) ^ (tj & 0xFFFFFFFFL);
            if (!_tiles.TryGetValue(key, out float[]? tile))
            {
                tile = new float[Tile * Tile];
                for (int k = 0; k < tile.Length; k++) tile[k] = float.NaN;
                _tiles[key] = tile;
            }
            int li = (int)(i - ti * Tile), lj = (int)(j - tj * Tile), idx = li * Tile + lj;
            float v = tile[idx];
            if (float.IsNaN(v)) { v = (float)_terrain(i * TerrainCacheM, j * TerrainCacheM); tile[idx] = v; }
            return v;
        }
    }
    /// <summary>Land-cover heating index in [−1, 1] at (x, y); null = the built-in map from the world's land cover.</summary>
    public System.Func<double, double, double>? Heating { get; set; }

    // The session's air mass (the menu overwrites the config's synoptic block through these).
    public double SynopticFromRad { get; set; }
    public double SynopticSpeedMs { get; set; }
    public double SynopticIsaDeviationK { get; set; }
    public double SynopticQnhPa { get; set; }
    /// <summary>Global turbulence multiplier (config × session).</summary>
    public double TurbulenceScale { get; set; }

    public CalmZone? Calm { get; }
    public IReadOnlyList<Region> Regions => _regions;
    private readonly List<Region> _regions = new();
    private readonly Region _background;
    public Turbulence SpectralTurbulence { get; }
    public TurbulenceSpectrum Spectrum { get; }

    public AtmosphereField(AtmosphereFieldConfig config)
    {
        Config = config;
        _ground = GroundAt;
        SynopticFromRad = config.Synoptic.WindFromDeg * System.Math.PI / 180;
        SynopticSpeedMs = config.Synoptic.WindSpeedKt * KtToMs;
        SynopticIsaDeviationK = config.Synoptic.IsaDeviationC;
        SynopticQnhPa = config.Synoptic.QnhHpa * 100.0;
        TurbulenceScale = config.Turbulence.Scale;
        Spectrum = string.Equals(config.Turbulence.Spectrum, "dryden", System.StringComparison.OrdinalIgnoreCase) ? TurbulenceSpectrum.Dryden : TurbulenceSpectrum.VonKarman;
        SpectralTurbulence = new Turbulence(Spectrum, config.Turbulence.Modes, config.Turbulence.Seed, config.Turbulence.MinLengthM, config.Turbulence.MaxLengthM);

        RegionConfig? bg = config.Regions.FirstOrDefault(r => r.Shape.Equals("global", System.StringComparison.OrdinalIgnoreCase));
        _background = new Region(bg ?? new RegionConfig { Id = "background", Shape = "global" });
        foreach (RegionConfig rc in config.Regions)
            if (!ReferenceEquals(rc, bg)) _regions.Add(new Region(rc));
        if (config.CalmZone.Enabled) Calm = new CalmZone(config.CalmZone);
    }

    // =============================================================================================================
    // Airports
    // =============================================================================================================

    /// <summary>"defaultStartAirport", "airport:&lt;name&gt;" or "airport:&lt;index&gt;" → the airport (null if none).</summary>
    public static WorldTerrain.Airport? ResolveAirport(string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor)) return null;
        if (anchor.Equals("defaultStartAirport", System.StringComparison.OrdinalIgnoreCase)) return WorldTerrain.DefaultAirport;
        string key = anchor.StartsWith("airport:", System.StringComparison.OrdinalIgnoreCase) ? anchor.Substring(8) : anchor;
        if (int.TryParse(key, out int idx) && idx >= 0 && idx < WorldTerrain.Airports.Length) return WorldTerrain.Airports[idx];
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            if (a.Name.Equals(key, System.StringComparison.OrdinalIgnoreCase)) return a;
        throw new System.ArgumentException($"Atmosphere config: unknown airport anchor '{anchor}'.");
    }

    // =============================================================================================================
    // Shapes
    // =============================================================================================================

    /// <summary>C2 smootherstep on [0, 1], clamped: 0 → 0, 1 → 1, monotonic, zero slope and curvature at both ends.</summary>
    public static double Smootherstep(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    /// <summary>An authored region, resolved to world coordinates.</summary>
    public sealed class Region
    {
        public RegionConfig Cfg { get; }
        public string Id => Cfg.Id;
        public string Shape { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double HeadingRad { get; }
        public double? CeilingMslM { get; }
        public VariableWind? Variable { get; }
        public GustyWind? Gusty { get; }

        public Region(RegionConfig c)
        {
            Cfg = c;
            Shape = c.Shape.ToLowerInvariant();
            WorldTerrain.Airport? a = ResolveAirport(c.Anchor);
            CenterX = (a?.X ?? 0) + c.CenterX;
            CenterY = (a?.Y ?? 0) + c.CenterY;
            HeadingRad = c.HeadingDeg * System.Math.PI / 180;
            if (c.CeilingM.HasValue) CeilingMslM = c.CeilingM.Value + (a?.ElevationM ?? 0);
            RegionWindConfig w = c.Wind;
            if (w.Mode.Equals("variable", System.StringComparison.OrdinalIgnoreCase))
                Variable = new VariableWind(w.SpeedKt * KtToMs, w.SpeedJitterKt * KtToMs, w.TurnRateSigmaDegS, w.TurnRateMaxDegS, w.CorrelationS, w.FromDeg, w.Seed);
            else if (w.Mode.Equals("gusty", System.StringComparison.OrdinalIgnoreCase))
                Gusty = new GustyWind(w.SpeedKt * KtToMs, w.GustPeakKt * KtToMs, w.FromDeg, w.MeanGustIntervalS, w.MinGustDurationS, w.MaxGustDurationS, w.Seed);
        }

        /// <summary>Distance (m) outside the shape's core (≤ 0 inside).</summary>
        public double DistanceOutside(double x, double y)
        {
            double dx = x - CenterX, dy = y - CenterY;
            switch (Shape)
            {
                case "global": return double.NegativeInfinity;
                case "box":
                {
                    double ox = System.Math.Abs(dx) - Cfg.HalfX, oy = System.Math.Abs(dy) - Cfg.HalfY;
                    double outside = System.Math.Sqrt(System.Math.Pow(System.Math.Max(ox, 0), 2) + System.Math.Pow(System.Math.Max(oy, 0), 2));
                    return outside > 0 ? outside : System.Math.Max(ox, oy);
                }
                case "capsule":
                {
                    double c = System.Math.Cos(HeadingRad), s = System.Math.Sin(HeadingRad);
                    double along = dx * c + dy * s, across = -dx * s + dy * c;
                    double a = System.Math.Max(0, System.Math.Abs(along) - Cfg.HalfLengthM);
                    return System.Math.Sqrt(a * a + across * across) - Cfg.RadiusM;
                }
                default: return System.Math.Sqrt(dx * dx + dy * dy) - Cfg.RadiusM;
            }
        }

        public double Weight(double x, double y, double altMsl)
        {
            double d = DistanceOutside(x, y);
            double wh = d <= 0 ? 1.0 : d >= Cfg.BlendM ? 0.0 : 1.0 - Smootherstep(d / System.Math.Max(1e-6, Cfg.BlendM));
            if (wh <= 0 || !CeilingMslM.HasValue) return wh;
            double above = altMsl - CeilingMslM.Value;
            if (above <= 0) return wh;
            return wh * (1.0 - Smootherstep(above / System.Math.Max(1e-6, Cfg.CeilingBlendM)));
        }
    }

    /// <summary>
    /// The calm zone, resolved: a stadium round the anchor airport's MAIN runway (WorldTerrain.AirportStrips[0]), its
    /// centreline extended past both thresholds, with a flat ceiling above field elevation. Weight 1 on and inside the
    /// stadium below the ceiling; smootherstep down to 0 across BlendWidthM outward and CeilingBlendM upward.
    /// </summary>
    public sealed class CalmZone
    {
        public string Id { get; }
        public string AirportName { get; }
        public double FieldElevationM { get; }
        public double CentreX { get; }
        public double CentreY { get; }
        public double HeadingRad { get; }
        public double HalfLengthM { get; }
        public double RadiusM { get; }
        public double CeilingAfeM { get; }
        public double CeilingMslM => FieldElevationM + CeilingAfeM;
        public double BlendWidthM { get; }
        public double CeilingBlendM { get; }

        public CalmZone(CalmZoneConfig c)
        {
            Id = c.Id;
            WorldTerrain.Airport a = ResolveAirport(c.Anchor) ?? WorldTerrain.DefaultAirport;
            WorldTerrain.Strip main = WorldTerrain.AirportStrips[0];
            AirportName = a.Name;
            FieldElevationM = a.ElevationM;
            CentreX = a.X + main.Dx;
            CentreY = a.Y + main.Dy;
            HeadingRad = main.HeadingDeg * System.Math.PI / 180;
            HalfLengthM = main.Length / 2 + c.ExtendBeyondThresholdsM;
            RadiusM = c.RadiusM;
            CeilingAfeM = c.CeilingAfeM;
            BlendWidthM = c.BlendWidthM;
            CeilingBlendM = c.CeilingBlendM;
        }

        public double DistanceOutside(double x, double y)
        {
            double dx = x - CentreX, dy = y - CentreY;
            double c = System.Math.Cos(HeadingRad), s = System.Math.Sin(HeadingRad);
            double along = dx * c + dy * s, across = -dx * s + dy * c;
            double a = System.Math.Max(0, System.Math.Abs(along) - HalfLengthM);
            return System.Math.Sqrt(a * a + across * across) - RadiusM;
        }

        public double HorizontalWeight(double x, double y)
        {
            double d = DistanceOutside(x, y);
            return d <= 0 ? 1.0 : d >= BlendWidthM ? 0.0 : 1.0 - Smootherstep(d / System.Math.Max(1e-6, BlendWidthM));
        }

        public double Weight(Vec3 pos)
        {
            double wh = HorizontalWeight(pos.X, pos.Y);
            if (wh <= 0) return 0;
            double above = -pos.Z - CeilingMslM;
            if (above <= 0) return wh;
            return wh * (1.0 - Smootherstep(above / System.Math.Max(1e-6, CeilingBlendM)));
        }
    }

    public double CalmWeightAt(Vec3 pos) => Calm?.Weight(pos) ?? 0.0;

    /// <summary>May a thermal stand here? Not inside the calm zone or its blend band (at any height).</summary>
    public bool AllowsThermalAt(double x, double y) => Calm is null || Calm.HorizontalWeight(x, y) <= 0.0;

    // =============================================================================================================
    // Regional parameters, blended
    // =============================================================================================================

    private struct Air
    {
        public double IsaDevK, QnhOffPa, LapseKPerM, ProfileExp, GradientH, TurbScale, ClearAir, LnZ0, OroScale, ThermalScale, BlDepth, BackgroundW;
        public Vec3 Wind10, GustInc10;
        public string Id; public double TopWeight;

        public static Air Lerp(Air a, Air b, double t)
        {
            double L(double x, double y) => x + (y - x) * t;
            return new Air
            {
                IsaDevK = L(a.IsaDevK, b.IsaDevK), QnhOffPa = L(a.QnhOffPa, b.QnhOffPa), LapseKPerM = L(a.LapseKPerM, b.LapseKPerM),
                ProfileExp = L(a.ProfileExp, b.ProfileExp), GradientH = L(a.GradientH, b.GradientH), TurbScale = L(a.TurbScale, b.TurbScale),
                ClearAir = L(a.ClearAir, b.ClearAir), LnZ0 = L(a.LnZ0, b.LnZ0), OroScale = L(a.OroScale, b.OroScale),
                ThermalScale = L(a.ThermalScale, b.ThermalScale), BlDepth = L(a.BlDepth, b.BlDepth), BackgroundW = L(a.BackgroundW, b.BackgroundW),
                Wind10 = a.Wind10 + (b.Wind10 - a.Wind10) * t, GustInc10 = a.GustInc10 + (b.GustInc10 - a.GustInc10) * t,
                Id = t >= a.TopWeight ? b.Id : a.Id, TopWeight = System.Math.Max(a.TopWeight, t),
            };
        }
    }

    private Air RegionAir(Region r, Vec3 pos, double t)
    {
        RegionConfig c = r.Cfg;
        RegionWindConfig w = c.Wind;
        Vec3 wind, gust = Vec3.Zero;
        if (r.Variable is not null) wind = r.Variable.VectorAt(t);
        else if (r.Gusty is not null)
        {
            Vec3 toward = r.Gusty.Toward;
            // A gust is a lump of air carried by the wind: it reaches a point downwind later (Taylor), so it sweeps
            // over the airport — and a wing — rather than switching on everywhere at once.
            double along = (pos.X - r.CenterX) * toward.X + (pos.Y - r.CenterY) * toward.Y;
            double tc = t - along / System.Math.Max(2.0, r.Gusty.MeanSpeedMs);
            wind = toward * r.Gusty.LullSpeedMs;
            gust = toward * r.Gusty.GustAt(tc);
        }
        else if (w.Mode.Equals("steady", System.StringComparison.OrdinalIgnoreCase))
            wind = FromVector(w.FromDeg * System.Math.PI / 180, w.SpeedKt * KtToMs);
        else
            wind = FromVector(SynopticFromRad + w.VeerDeg * System.Math.PI / 180, SynopticSpeedMs * w.SpeedScale);
        return new Air
        {
            IsaDevK = SynopticIsaDeviationK + c.IsaDeviationC, QnhOffPa = SynopticQnhPa - Atmosphere.SeaLevelPressurePa + c.QnhOffsetHpa * 100.0,
            LapseKPerM = c.LapseRateCPerKm / 1000.0, ProfileExp = w.ProfileExponent, GradientH = w.GradientHeightM, TurbScale = c.TurbulenceScale,
            ClearAir = c.ClearAirSigmaMs, LnZ0 = System.Math.Log(System.Math.Max(1e-5, c.RoughnessM)), OroScale = c.OrographicScale,
            ThermalScale = c.ThermalScale, BlDepth = c.BoundaryLayerM, BackgroundW = c.BackgroundVerticalMs,
            Wind10 = wind, GustInc10 = gust, Id = c.Id,
        };
    }

    private Air BlendedAir(Vec3 pos, double t)
    {
        Air air = RegionAir(_background, pos, t);
        air.TopWeight = 0;
        double alt = -pos.Z;
        for (int i = 0; i < _regions.Count; i++)
        {
            double wgt = _regions[i].Weight(pos.X, pos.Y, alt);
            if (wgt <= 0) continue;
            Air ra = RegionAir(_regions[i], pos, t);
            air = Air.Lerp(air, ra, wgt);
            if (wgt >= 0.5) air.Id = ra.Id;
        }
        return air;
    }

    private static Vec3 FromVector(double fromRad, double speed) =>
        new(-speed * System.Math.Cos(fromRad), -speed * System.Math.Sin(fromRad), 0);

    /// <summary>Power-law profile factor U(z)/U(10 m), z above the ground, held above the gradient height.</summary>
    public static double ProfileFactor(double aglM, double exponent, double gradientHeightM)
    {
        double z = System.Math.Clamp(aglM, 2.0, System.Math.Max(10.0, gradientHeightM));
        return System.Math.Pow(z / 10.0, exponent);
    }

    // =============================================================================================================
    // Thermodynamics
    // =============================================================================================================

    /// <summary>ISA temperature, pressure and density for an altitude, with a deviation, a lapse rate and a QNH.
    /// Pressure follows the altimeter's own relation p = QNH/1013.25 · p_ISA(h), so a standard day (QNH 1013.25,
    /// deviation 0, ISA lapse) reproduces <see cref="Atmosphere.PressureAtAltitude"/> and the ISA density exactly.</summary>
    public static (double tK, double pPa, double rho, double densityAltM) Thermo(double altM, double isaDevK, double lapseKPerM, double qnhPa)
    {
        double h = System.Math.Clamp(altM, 0.0, 11000.0);
        double tK = Atmosphere.SeaLevelTempK + isaDevK - lapseKPerM * h;
        double p = Atmosphere.PressureAtAltitude(altM);
        if (qnhPa != Atmosphere.SeaLevelPressurePa) p *= qnhPa / Atmosphere.SeaLevelPressurePa;
        double rho = p / (Atmosphere.GasConstantAir * tK);
        return (tK, p, rho, DensityAltitude(rho));
    }

    /// <summary>The ISA altitude whose density is ρ: (T0/L)·(1 − (ρ/ρ0)^(1/(g/LR − 1))).</summary>
    public static double DensityAltitude(double rho)
    {
        double rho0 = Atmosphere.SeaLevelPressurePa / (Atmosphere.GasConstantAir * Atmosphere.SeaLevelTempK);
        double n = Atmosphere.GravityMs2 / (Atmosphere.LapseRateKPerM * Atmosphere.GasConstantAir) - 1.0;
        return Atmosphere.SeaLevelTempK / Atmosphere.LapseRateKPerM * (1.0 - System.Math.Pow(rho / rho0, 1.0 / n));
    }

    /// <summary>The exact calm sample: standard ISA, no motion of any kind.</summary>
    public static AtmosphereSample CalmSample(double altM, string id)
    {
        var (tK, p, rho, da) = Thermo(altM, 0.0, Atmosphere.LapseRateKPerM, Atmosphere.SeaLevelPressurePa);
        return new AtmosphereSample(Vec3.Zero, Vec3.Zero, tK, p, rho, da, Atmosphere.SeaLevelPressurePa, 0.0,
            0, 0, 0, 0, 0.0, 1.0, 0, 0, 0, 0, 0, 0, 0, id);
    }

    // =============================================================================================================
    // Land cover → heating
    // =============================================================================================================

    private readonly Dictionary<long, double> _heatCache = new();

    /// <summary>
    /// How strongly the sun heats the ground here, −1 … 1 (cached on a grid, bilinear between nodes). Bare rock,
    /// scree and gravel heat best, then dry rough ground and fields, then grass; forest is a weak source that turns to a
    /// little sink; water and snow are cold — the air over them sinks. A slope facing the sun (south: the ground rising
    /// to the north, +x) gains, one facing away loses.
    /// </summary>
    public double HeatingAt(double x, double y)
    {
        if (Heating is not null) return Heating(x, y);
        if (_terrainIsWorld && !ReferenceEquals(_cachedWorld, WorldTerrain.Active)) GroundAt(x, y);   // (drops stale caches)
        double g = Config.Convection.GridM;
        double fx = x / g, fy = y / g;
        long ix = (long)System.Math.Floor(fx), iy = (long)System.Math.Floor(fy);
        double tx = fx - ix, ty = fy - iy;
        double a = HeatNode(ix, iy), b = HeatNode(ix + 1, iy), c = HeatNode(ix, iy + 1), d = HeatNode(ix + 1, iy + 1);
        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
    }

    private double HeatNode(long ix, long iy)
    {
        long key = (ix << 32) ^ (iy & 0xFFFFFFFFL);
        if (_heatCache.TryGetValue(key, out double v)) return v;
        v = HeatingRaw(ix * Config.Convection.GridM, iy * Config.Convection.GridM);
        _heatCache[key] = v;
        return v;
    }

    private double HeatingRaw(double x, double y)
    {
        if (WorldTerrain.Active is null) return 0.4;   // no world: generic open country
        double h = GroundAt(x, y);
        double? water = FloatHydro.WaterSurfaceAt(x, y);
        if (water.HasValue && water.Value >= h - 0.5) return -0.7;
        double v = WorldTerrain.SurfaceAt(x, y) switch
        {
            WorldTerrain.Surface.Paved => 0.7,
            WorldTerrain.Surface.Gravel => 0.85,
            WorldTerrain.Surface.Rough => 0.55,
            WorldTerrain.Surface.Grass => 0.35,
            WorldTerrain.Surface.Snow => -0.5,
            _ => 0.4,
        };
        double cover = Forest.CoverAt(x, y);
        v = v * (1 - cover) - 0.15 * cover;
        double dhdx = (GroundAt(x + 50, y) - GroundAt(x - 50, y)) / 100.0;
        v += 0.3 * System.Math.Clamp(dhdx / 0.4, -1, 1);
        return System.Math.Clamp(v, -1, 1);
    }

    // =============================================================================================================
    // Sampling
    // =============================================================================================================

    /// <summary>The regional base wind (world, horizontal) with its height profile — no terrain effects, no gusts. What
    /// a thermal leans with and what carries the bubbles; zero in the calm zone.</summary>
    public Vec3 SteadyWindAt(Vec3 pos, double t)
    {
        double calm = CalmWeightAt(pos);
        if (calm >= 1.0) return Vec3.Zero;
        Air air = BlendedAir(pos, t);
        double agl = -pos.Z - GroundAt(pos.X, pos.Y);
        return air.Wind10 * (ProfileFactor(agl, air.ProfileExp, air.GradientH) * (1 - calm));
    }

    /// <summary>The convective boundary-layer profile of the mean updraught (Lenschow &amp; Stephens' shape), peak ≈ 1.</summary>
    public static double ConvectiveProfile(double zOverZi)
    {
        if (zOverZi <= 0 || zOverZi >= 0.95) return 0;
        return 2.6 * System.Math.Pow(zOverZi, 1.0 / 3.0) * (1 - 1.05 * zOverZi);
    }

    public AtmosphereSample Sample(Vec3 pos, double t)
    {
        double alt = -pos.Z;
        double calm = CalmWeightAt(pos);
        if (calm >= 1.0) return CalmSample(alt, Calm!.Id);
        double keep = 1.0 - calm;

        Air air = BlendedAir(pos, t);
        double ground = GroundAt(pos.X, pos.Y);
        double agl = System.Math.Max(0.0, alt - ground);
        double profile = ProfileFactor(agl, air.ProfileExp, air.GradientH);
        Vec3 windH = air.Wind10 * profile;

        // Terrain.
        var (tK0, _, _, _) = Thermo(alt, air.IsaDevK, air.LapseKPerM, Atmosphere.SeaLevelPressurePa);
        Orography.Result oro = Orography.Evaluate(_ground, pos, windH, Config.Orography, air.OroScale, air.LapseKPerM, tK0);
        Vec3 mean = windH * (1 + oro.SpeedUp) + oro.Rotor;
        double w = oro.SlopeLiftMs + oro.LeeMs + oro.WaveMs + air.BackgroundW;

        // Sun on the ground: background convection, cellular over hot ground, gentle sink over cold.
        double heat = HeatingAt(pos.X, pos.Y);
        double zi = System.Math.Max(100.0, air.BlDepth);
        double prof = ConvectiveProfile(agl / zi);
        double cellSize = Config.Convection.CellSizeM;
        double conv = 0;
        if (prof > 0 && air.ThermalScale > 0)
        {
            if (heat > 0)
            {
                double p = SeededNoise.Value2D(pos.X / cellSize, pos.Y / cellSize, 4242);
                double cell = 2.0 * System.Math.Max(0, p) * System.Math.Max(0, p) - 0.25 * System.Math.Max(0, -p);
                conv = Config.Convection.BackgroundUpdraftMs * air.ThermalScale * heat * cell * prof;
            }
            else conv = 0.5 * Config.Convection.BackgroundUpdraftMs * air.ThermalScale * heat * prof;
        }
        w += conv;
        mean += new Vec3(0, 0, -w);

        // Turbulence: MIL-HDBK-1797 boundary layer from the 20 ft wind, roughness, the terrain's own, convection.
        double w20 = air.Wind10.Length * ProfileFactor(6.1, air.ProfileExp, air.GradientH);
        TurbulenceScales.Scales mil = TurbulenceScales.LowAltitude(agl, w20, Spectrum);
        double rough = System.Math.Clamp(System.Math.Log(10.0 / 0.03) / (System.Math.Log(10.0) - air.LnZ0), 0.4, 2.0);
        double blDecay = agl <= 305 ? 1.0 : System.Math.Exp(-(agl - 305) / 600.0);
        double su = mil.SigmaU * rough * blDecay * air.TurbScale, sw = mil.SigmaW * rough * blDecay * air.TurbScale;
        double wStar = 2.0 * air.ThermalScale * System.Math.Max(0, heat);
        double zn = agl / zi;
        double sConv = zn < 1 ? 0.6 * wStar * System.Math.Pow(System.Math.Max(zn, 0.02), 1.0 / 3.0) * (1 - 0.8 * zn) : 0;
        double sOro = oro.SigmaMs;
        double clear = air.ClearAir * air.TurbScale;
        double sigH = System.Math.Sqrt(su * su + 0.36 * sConv * sConv + sOro * sOro + clear * clear) * TurbulenceScale;
        double sigV = System.Math.Sqrt(sw * sw + sConv * sConv + 0.6 * sOro * sOro + clear * clear) * TurbulenceScale;
        double lh = System.Math.Clamp(mil.LengthU, Config.Turbulence.MinLengthM, Config.Turbulence.MaxLengthM);
        double lv = System.Math.Clamp(mil.LengthW, Config.Turbulence.MinLengthM, Config.Turbulence.MaxLengthM);

        Vec3 gust = Vec3.Zero;
        if (sigH > 0 || sigV > 0)
        {
            Vec3 unit = SpectralTurbulence.UnitGustAt(pos, t, lh, lv, AdvectionWind);
            gust = new Vec3(unit.X * sigH, unit.Y * sigH, unit.Z * sigV);
        }
        gust += air.GustInc10 * profile;

        var (tK, pPa, rho, da) = Thermo(alt, air.IsaDevK * keep, Atmosphere.LapseRateKPerM + (air.LapseKPerM - Atmosphere.LapseRateKPerM) * keep,
            Atmosphere.SeaLevelPressurePa + air.QnhOffPa * keep);
        return new AtmosphereSample(mean * keep, gust * keep, tK, pPa, rho, da, Atmosphere.SeaLevelPressurePa + air.QnhOffPa * keep, air.IsaDevK * keep,
            sigH * keep, sigV * keep, lh, lv, air.ThermalScale * keep, calm,
            oro.SpeedUp * keep, oro.SlopeLiftMs * keep, oro.LeeMs * keep, oro.WaveMs * keep, conv * keep, heat, air.GustInc10.Length * profile * keep, air.Id);
    }

    /// <summary>The wind the frozen turbulence drifts with: the synoptic wind near gradient height (one vector for the
    /// whole map, so the field stays a pure function of position and time).</summary>
    public Vec3 AdvectionWind => FromVector(SynopticFromRad, SynopticSpeedMs * 1.5);
}
