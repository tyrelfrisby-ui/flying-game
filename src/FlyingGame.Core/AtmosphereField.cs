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
    /// <summary>The terrain's own turbulence σ and the rotor's speed here (calm-faded, like the rest).</summary>
    public readonly double OrographicSigmaMs, RotorMs;
    /// <summary>The mechanical-roughness factor applied here (the mountain's own × a seaplane lake's smoothing), and how
    /// far into a lake's smooth zone this is (1 in its core, 0 clear of it).</summary>
    public readonly double RoughnessFactor, LakeSmoothing;

    public AtmosphereSample(Vec3 mean, Vec3 gust, double tK, double pPa, double rho, double da, double qnh, double isaDev,
        double sh, double sv, double lh, double lv, double thermalFactor, double calm,
        double speedUp, double slope, double lee, double wave, double conv, double heat, double gustInc, string regionId,
        double oroSigma = 0, double rotor = 0, double roughness = 1, double lakeSmoothing = 0)
    {
        OrographicSigmaMs = oroSigma; RotorMs = rotor; RoughnessFactor = roughness; LakeSmoothing = lakeSmoothing;
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
    /// <summary>Global multiplier on the land-cover convection (the session's thermal setting; the authored thermal
    /// columns follow <see cref="Atmosphere.ThermalStrengthScale"/> as they always have).</summary>
    public double ConvectionScale { get; set; } = 1.0;

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
        foreach (RoughnessZoneConfig z in config.Roughness) _roughness.Add(new RoughnessZone(z));
        if (config.LakeSmoothing.Enabled) _lakes.AddRange(SeaplaneLake.FromSources(config.LakeSmoothing));
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

    /// <summary>
    /// Where an authored thermal may stand: the point itself if allowed, otherwise pushed straight out of the calm zone
    /// (along the stadium's outward normal) to <paramref name="marginM"/> past its blend band. The Valley's hand-placed
    /// thermals keep their number and their side of the field; they just start where the calm air ends.
    /// </summary>
    public (double x, double y) NearestThermalSite(double x, double y, double marginM = 50)
    {
        if (AllowsThermalAt(x, y) || Calm is null) return (x, y);
        double c = System.Math.Cos(Calm.HeadingRad), s = System.Math.Sin(Calm.HeadingRad);
        double dx = x - Calm.CentreX, dy = y - Calm.CentreY;
        double along = dx * c + dy * s, across = -dx * s + dy * c;
        double clamped = System.Math.Clamp(along, -Calm.HalfLengthM, Calm.HalfLengthM);
        double nx = along - clamped, ny = across;                     // from the nearest centreline point, stadium frame
        double len = System.Math.Sqrt(nx * nx + ny * ny);
        if (len < 1e-6) { nx = 0; ny = 1; len = 1; }
        double r = Calm.RadiusM + Calm.BlendWidthM + marginM;
        double a2 = clamped + nx / len * r, c2 = ny / len * r;
        return (Calm.CentreX + a2 * c - c2 * s, Calm.CentreY + a2 * s + c2 * c);
    }

    // =============================================================================================================
    // Mountain roughness and seaplane lakes
    // =============================================================================================================

    public IReadOnlyList<RoughnessZone> RoughnessZones => _roughness;
    private readonly List<RoughnessZone> _roughness = new();
    public IReadOnlyList<SeaplaneLake> SeaplaneLakes => _lakes;
    private readonly List<SeaplaneLake> _lakes = new();

    /// <summary>A mountain's roughness: a world feature (or authored shape) resolved to a box or a circle, with a blend.</summary>
    public sealed class RoughnessZone
    {
        public RoughnessZoneConfig Cfg { get; }
        public string Id => Cfg.Id;
        public bool IsCircle { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double HalfX { get; }
        public double HalfY { get; }
        public double RadiusM { get; }

        public RoughnessZone(RoughnessZoneConfig c)
        {
            Cfg = c;
            double x0, x1, y0, y1;
            string f = (c.Feature ?? "").Trim();
            string fl = f.ToLowerInvariant();
            if (f.Length == 0)
            {
                IsCircle = c.Shape.Equals("circle", System.StringComparison.OrdinalIgnoreCase);
                CenterX = c.CenterX; CenterY = c.CenterY; HalfX = c.HalfX + c.PadM; HalfY = c.HalfY + c.PadM; RadiusM = c.RadiusM + c.PadM;
                return;
            }
            if (fl.StartsWith("peak:"))
            {
                string[] parts = f.Split(':');
                if (parts.Length != 3) throw new System.ArgumentException($"Atmosphere config: roughness feature '{f}' must be peak:<range>:<name>.");
                (double x, double y, double r)? pk = null;
                if (parts[1].Equals("alaska", System.StringComparison.OrdinalIgnoreCase))
                    foreach (AlaskaRange.Peak p in AlaskaRange.Peaks) { if (p.Name.Equals(parts[2], System.StringComparison.OrdinalIgnoreCase)) pk = (p.X, p.Y, p.RadiusM); }
                else if (parts[1].Equals("alpine", System.StringComparison.OrdinalIgnoreCase))
                    foreach (AlpineRange.Peak p in AlpineRange.Peaks) { if (p.Name.Equals(parts[2], System.StringComparison.OrdinalIgnoreCase)) pk = (p.X, p.Y, p.RadiusM); }
                if (pk is null) throw new System.ArgumentException($"Atmosphere config: unknown peak '{f}'.");
                IsCircle = true; CenterX = pk.Value.x; CenterY = pk.Value.y; RadiusM = pk.Value.r + c.PadM;
                return;
            }
            if (fl.StartsWith("front:") && int.TryParse(f.Substring(6), out int step))
            {
                if (step < 0 || step >= WorldTerrain.StepCount) throw new System.ArgumentException($"Atmosphere config: no mountain front {step}.");
                // From the back slope behind the crest (crest = edge − the wall's run; the summits' back slopes reach
                // ~400 m behind it) to the farthest spur toe on the lower plateau, both widened by the edge's wander.
                double e = WorldTerrain.EdgeMeanY(step), wander = 210;
                x0 = WorldTerrain.MeshMinX; x1 = WorldTerrain.MeshMaxX;
                y0 = e - WorldTerrain.EscarpmentWidthM - 400 - wander; y1 = e + WorldTerrain.FrontExtraWidthM + wander;
            }
            else switch (fl)
            {
                case "alaskarange": x0 = AlaskaRange.X0; x1 = AlaskaRange.X1; y0 = AlaskaRange.Y0; y1 = AlaskaRange.Y1; break;
                case "alpinerange": x0 = AlpineRange.X0; x1 = AlpineRange.X1; y0 = AlpineRange.Y0; y1 = AlpineRange.Y1; break;
                case "frankchurch": x0 = FrankChurch.X0; x1 = FrankChurch.X1; y0 = FrankChurch.Y0; y1 = FrankChurch.Y1; break;
                case "highdesert": x0 = HighDesert.X0; x1 = HighDesert.X1; y0 = HighDesert.Y0; y1 = HighDesert.Y1; break;
                case "hills": x0 = Hills.X0; x1 = Hills.X1; y0 = Hills.Y0; y1 = Hills.Y1; break;
                case "seacliffs":
                    // The cliff line wanders ±330 m about the mean shore (Coast.ShoreY); 90 m of cliff run seaward of it.
                    x0 = WorldTerrain.MeshMinX; x1 = WorldTerrain.MeshMaxX; y0 = Coast.MeanShoreY - 450; y1 = Coast.MeanShoreY + 450; break;
                default: throw new System.ArgumentException($"Atmosphere config: unknown roughness feature '{f}'.");
            }
            CenterX = 0.5 * (x0 + x1); CenterY = 0.5 * (y0 + y1);
            HalfX = 0.5 * (x1 - x0) + c.PadM; HalfY = 0.5 * (y1 - y0) + c.PadM;
        }

        public double DistanceOutside(double x, double y)
        {
            double dx = x - CenterX, dy = y - CenterY;
            if (IsCircle) return System.Math.Sqrt(dx * dx + dy * dy) - RadiusM;
            double ox = System.Math.Abs(dx) - HalfX, oy = System.Math.Abs(dy) - HalfY;
            double outside = System.Math.Sqrt(System.Math.Pow(System.Math.Max(ox, 0), 2) + System.Math.Pow(System.Math.Max(oy, 0), 2));
            return outside > 0 ? outside : System.Math.Max(ox, oy);
        }

        public double Weight(double x, double y)
        {
            double d = DistanceOutside(x, y);
            return d <= 0 ? 1.0 : d >= Cfg.BlendM ? 0.0 : 1.0 - Smootherstep(d / System.Math.Max(1e-6, Cfg.BlendM));
        }
    }

    /// <summary>
    /// A seaplane lake's smooth zone, built from the terrain's own lake data: the water (an ellipse, or for the canyon
    /// lake a run of capsules down its gorge) grown by the shore margin, and approach corridors off both ends of the
    /// landing axis (and down an alpine lake's outlet valley).
    /// </summary>
    public sealed class SeaplaneLake
    {
        public string Name { get; }
        public double CentreX { get; }
        public double CentreY { get; }
        private readonly double _a, _b, _heading;   // ellipse semi-axes along / across the heading (0 = none)
        private readonly double _margin;
        /// <summary>Segments (x0, y0, x1, y1, half-width): the corridors, and a gorge lake's water.</summary>
        public IReadOnlyList<(double x0, double y0, double x1, double y1, double halfW)> Segments => _segs;
        private readonly List<(double x0, double y0, double x1, double y1, double halfW)> _segs = new();
        public double SemiMajorM => _a;
        public double SemiMinorM => _b;
        public double HeadingRad => _heading;

        private SeaplaneLake(string name, double cx, double cy, double a, double b, double headingRad, double margin)
        { Name = name; CentreX = cx; CentreY = cy; _a = a; _b = b; _heading = headingRad; _margin = margin; }

        /// <summary>A corridor from the centre out along a bearing, to the shore plus the approach length.</summary>
        private void Corridor(double bearingRad, double lengthM, double halfW)
        {
            double c = System.Math.Cos(bearingRad), s = System.Math.Sin(bearingRad);
            _segs.Add((CentreX, CentreY, CentreX + c * lengthM, CentreY + s * lengthM, halfW));
        }

        /// <summary>An elliptical lake landed along its long axis (both ways), and out along its outlet if it has one.</summary>
        private static SeaplaneLake Ellipse(string name, double cx, double cy, double a, double b, double headingDeg, double? outletDeg, LakeSmoothingConfig c)
        {
            double h = headingDeg * System.Math.PI / 180;
            var l = new SeaplaneLake(name, cx, cy, a, b, h, c.ShoreMarginM);
            if (c.ApproachLengthM > 0)
            {
                l.Corridor(h, a + c.ApproachLengthM, c.ApproachHalfWidthM);
                l.Corridor(h + System.Math.PI, a + c.ApproachLengthM, c.ApproachHalfWidthM);
                if (outletDeg.HasValue)
                {
                    double o = outletDeg.Value * System.Math.PI / 180;
                    double co = System.Math.Cos(o - h), so = System.Math.Sin(o - h);
                    double shore = 1.0 / System.Math.Sqrt(co * co / (a * a) + so * so / (b * b));   // the shore on that bearing
                    if (System.Math.Abs(System.Math.Sin(o - h)) > 0.17) l.Corridor(o, shore + c.ApproachLengthM, c.ApproachHalfWidthM);
                }
            }
            return l;
        }

        public static IEnumerable<SeaplaneLake> FromSources(LakeSmoothingConfig c)
        {
            foreach (string src in c.Sources)
            {
                switch (src.Trim().ToLowerInvariant())
                {
                    case "alpinelandablelakes":
                        foreach (AlpineRange.Lake l in AlpineRange.Lakes)
                            if (l.Landable) yield return Ellipse("alpine:" + l.Name, l.X, l.Y, 0.5 * l.LengthM, 0.5 * l.WidthM, l.HeadingDeg, l.OutletHeadingDeg, c);
                        break;
                    case "alaskaglacierlakes":
                        foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers)
                        {
                            var (gx, gy) = g.LakeCentre;
                            // Axis-aligned ellipses (LakeRx along x); landed along whichever axis is longer.
                            bool alongX = g.LakeRx >= g.LakeRy;
                            yield return Ellipse("alaska:" + g.Name, gx, gy, System.Math.Max(g.LakeRx, g.LakeRy), System.Math.Min(g.LakeRx, g.LakeRy), alongX ? 0 : 90, null, c);
                        }
                        break;
                    case "plateaulakes":
                        // One beside each airport (the last entry of WorldTerrain.Lakes is the harbour, at sea: no mountain).
                        for (int i = 0; i < WorldTerrain.Lakes.Length - 1; i++)
                        {
                            WorldTerrain.Lake l = WorldTerrain.Lakes[i];
                            bool alongX = l.Rx >= l.Ry;
                            string name = i < WorldTerrain.Airports.Length ? WorldTerrain.Airports[i].Name : i.ToString();
                            yield return Ellipse("plateau:" + name, l.Cx, l.Cy, System.Math.Max(l.Rx, l.Ry), System.Math.Min(l.Rx, l.Ry), alongX ? 0 : 90, null, c);
                        }
                        break;
                    case "canyonlake":
                    {
                        // The flooded gorge: capsules down the river's centreline over the water, then corridors on up and
                        // down the gorge for the approach.
                        double y0 = CanyonLake.Y0, y1 = CanyonLake.DamY, ym = 0.5 * (y0 + y1);
                        var l = new SeaplaneLake("canyon", CanyonLake.CentreX(ym), ym, 0, 0, 0, c.ShoreMarginM);
                        double maxHalf = 0;
                        for (double y = y0; y <= y1; y += 25) maxHalf = System.Math.Max(maxHalf, CanyonLake.HalfWidthAt(y));
                        const int n = 8;
                        for (int k = 0; k < n; k++)
                        {
                            double ya = y0 + (y1 - y0) * k / n, yb = y0 + (y1 - y0) * (k + 1) / n;
                            l._segs.Add((CanyonLake.CentreX(ya), ya, CanyonLake.CentreX(yb), yb, maxHalf + c.ShoreMarginM));
                        }
                        if (c.ApproachLengthM > 0)
                        {
                            l._segs.Add((CanyonLake.CentreX(y0), y0, CanyonLake.CentreX(y0 - c.ApproachLengthM), y0 - c.ApproachLengthM, c.ApproachHalfWidthM));
                            l._segs.Add((CanyonLake.CentreX(y1), y1, CanyonLake.CentreX(y1 + c.ApproachLengthM), y1 + c.ApproachLengthM, c.ApproachHalfWidthM));
                        }
                        yield return l;
                        break;
                    }
                    default: throw new System.ArgumentException($"Atmosphere config: unknown seaplane lake source '{src}'.");
                }
            }
        }

        /// <summary>Distance (m) outside the smooth core (≤ 0 inside): the water plus its margin, or a corridor.</summary>
        public double DistanceOutside(double x, double y)
        {
            double d = double.PositiveInfinity;
            if (_a > 0 && _b > 0)
            {
                double dx = x - CentreX, dy = y - CentreY;
                double c = System.Math.Cos(_heading), s = System.Math.Sin(_heading);
                double u = dx * c + dy * s, v = -dx * s + dy * c;
                double r = System.Math.Sqrt(u * u + v * v);
                // Radial distance to the shore: |p| − (the shore's distance on this bearing).
                double shore = r < 1e-9 ? _b : r / System.Math.Sqrt(u * u / (_a * _a) + v * v / (_b * _b));
                d = r - shore - _margin;
            }
            foreach (var (x0, y0, x1, y1, hw) in _segs)
            {
                double ex = x1 - x0, ey = y1 - y0, len2 = ex * ex + ey * ey;
                double t = len2 > 0 ? System.Math.Clamp(((x - x0) * ex + (y - y0) * ey) / len2, 0, 1) : 0;
                double px = x0 + t * ex - x, py = y0 + t * ey - y;
                d = System.Math.Min(d, System.Math.Sqrt(px * px + py * py) - hw);
            }
            return d;
        }

        public double Weight(double x, double y, double blendM)
        {
            double d = DistanceOutside(x, y);
            return d <= 0 ? 1.0 : d >= blendM ? 0.0 : 1.0 - Smootherstep(d / System.Math.Max(1e-6, blendM));
        }
    }

    /// <summary>The mountain's own roughness here (before any lake): the zones layered in list order over 1, each
    /// painting its factor over what is below it across its blend. Returns (factor, length-scale multiplier).</summary>
    public (double factor, double lengthScale) MountainRoughnessAt(double x, double y)
    {
        double f = 1.0, l = 1.0;
        for (int i = 0; i < _roughness.Count; i++)
        {
            double w = _roughness[i].Weight(x, y);
            if (w <= 0) continue;
            f += (_roughness[i].Cfg.Factor - f) * w;
            l += (_roughness[i].Cfg.LengthScale - l) * w;
        }
        return (f, l);
    }

    /// <summary>How far into a seaplane lake's smooth zone (x, y) is: 1 in a core, 0 clear of every lake (the most of
    /// any lake, so overlapping zones never add).</summary>
    public double LakeSmoothingAt(double x, double y)
    {
        double w = 0;
        double blend = Config.LakeSmoothing.BlendWidthM;
        for (int i = 0; i < _lakes.Count && w < 1.0; i++) w = System.Math.Max(w, _lakes[i].Weight(x, y, blend));
        return w;
    }

    /// <summary>The roughness the terrain's mechanical effects get here: the mountain's × the lake's smoothing.</summary>
    public double OrographicRoughnessAt(double x, double y)
    {
        double lake = LakeSmoothingAt(x, y);
        return MountainRoughnessAt(x, y).factor * (1.0 - lake * (1.0 - Config.LakeSmoothing.Factor));
    }

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
        double exp = air.ProfileExp, gh = air.GradientH;
        // How rough this mountain is, and how much of a seaplane lake's smoothing reaches here.
        var (mRough, mLength) = MountainRoughnessAt(pos.X, pos.Y);
        double lakeW = LakeSmoothingAt(pos.X, pos.Y);
        LakeSmoothingConfig lc = Config.LakeSmoothing;
        double roughness = mRough * (1.0 - lakeW * (1.0 - lc.Factor));
        Orography.Result oro = Orography.Evaluate(_ground, pos, windH, Config.Orography, air.OroScale, air.LapseKPerM, tK0, z => ProfileFactor(z, exp, gh), roughness);
        Vec3 mean = windH * (1 + oro.SpeedUp) + oro.Rotor;
        double w = oro.SlopeLiftMs + oro.LeeMs + oro.WaveMs + air.BackgroundW;

        // Sun on the ground: background convection, cellular over hot ground, gentle sink over cold.
        double heat = HeatingAt(pos.X, pos.Y);
        double zi = System.Math.Max(100.0, air.BlDepth);
        double prof = ConvectiveProfile(agl / zi);
        double cellSize = Config.Convection.CellSizeM;
        double conv = 0;
        if (prof > 0 && air.ThermalScale > 0 && ConvectionScale > 0)
        {
            if (heat > 0)
            {
                double p = SeededNoise.Value2D(pos.X / cellSize, pos.Y / cellSize, 4242);
                double cell = 2.0 * System.Math.Max(0, p) * System.Math.Max(0, p) - 0.25 * System.Math.Max(0, -p);
                conv = Config.Convection.BackgroundUpdraftMs * air.ThermalScale * ConvectionScale * heat * cell * prof;
            }
            else conv = 0.5 * Config.Convection.BackgroundUpdraftMs * air.ThermalScale * ConvectionScale * heat * prof;
        }
        w += conv;
        mean += new Vec3(0, 0, -w);

        // Turbulence: MIL-HDBK-1797 boundary layer from the 20 ft wind, roughness, the terrain's own, convection.
        double w20 = air.Wind10.Length * ProfileFactor(6.1, air.ProfileExp, air.GradientH);
        TurbulenceScales.Scales mil = TurbulenceScales.LowAltitude(agl, w20, Spectrum);
        double rough = System.Math.Clamp(System.Math.Log(10.0 / 0.03) / (System.Math.Log(10.0) - air.LnZ0), 0.4, 2.0);
        double blDecay = agl <= 305 ? 1.0 : System.Math.Exp(-(agl - 305) / 600.0);
        double lakeBl = 1.0 - lakeW * (1.0 - lc.BoundaryLayerFactor);   // water is a smoother surface than the hills round it
        double su = mil.SigmaU * rough * blDecay * air.TurbScale * lakeBl, sw = mil.SigmaW * rough * blDecay * air.TurbScale * lakeBl;
        double wStar = 2.0 * air.ThermalScale * ConvectionScale * System.Math.Max(0, heat);
        double zn = agl / zi;
        double sConv = zn < 1 ? 0.6 * wStar * System.Math.Pow(System.Math.Max(zn, 0.02), 1.0 / 3.0) * (1 - 0.8 * zn) : 0;
        double sOro = oro.SigmaMs;
        double clear = air.ClearAir * air.TurbScale;
        double sigH = System.Math.Sqrt(su * su + 0.36 * sConv * sConv + sOro * sOro + clear * clear) * TurbulenceScale;
        double sigV = System.Math.Sqrt(sw * sw + sConv * sConv + 0.6 * sOro * sOro + clear * clear) * TurbulenceScale;
        // A rough mountain's eddies are its own size and sharper; the length follows the terrain's share of the variance.
        double total2 = su * su + 0.36 * sConv * sConv + sOro * sOro + clear * clear;
        double oroShare = total2 > 1e-12 ? sOro * sOro / total2 : 0.0;
        double lenMul = (1.0 + (mLength - 1.0) * oroShare) * (1.0 + lakeW * (lc.LengthScale - 1.0));
        double lh = System.Math.Clamp(mil.LengthU * lenMul, Config.Turbulence.MinLengthM, Config.Turbulence.MaxLengthM);
        double lv = System.Math.Clamp(mil.LengthW * lenMul, Config.Turbulence.MinLengthM, Config.Turbulence.MaxLengthM);

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
            oro.SpeedUp * keep, oro.SlopeLiftMs * keep, oro.LeeMs * keep, oro.WaveMs * keep, conv * keep, heat, air.GustInc10.Length * profile * keep, air.Id,
            sOro * keep, oro.RotorMs * keep, roughness, lakeW);
    }

    /// <summary>The wind the frozen turbulence drifts with: the synoptic wind near gradient height (one vector for the
    /// whole map, so the field stays a pure function of position and time).</summary>
    public Vec3 AdvectionWind => FromVector(SynopticFromRad, SynopticSpeedMs * 1.5);
}
