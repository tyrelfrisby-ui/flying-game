namespace FlyingGame.Core.DataContracts;

/// <summary>
/// The regional atmosphere (configs/atmosphere/regional-atmosphere.json, mirrored to StreamingAssets/atmosphere). The
/// landscape is split into authored REGIONS, each a shape with a blend band, layered in list order over a global
/// background; the CALM ZONE round the default start airport is applied last, over all of them. Every number a
/// designer might want to turn is here, and AGENT-LOG.md says what each one is for and why it has the value it has.
///
/// Units: metres, seconds, knots for wind speeds (pilots author winds in knots), degrees for directions (wind FROM,
/// compass, sim x = north / y = east), °C for temperature deviations, hPa for pressure. Sim coordinates for positions.
/// </summary>
public sealed class AtmosphereFieldConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "regional-atmosphere";
    /// <summary>The air mass the whole map sits in. The session menu's wind and hot/cold day overwrite these.</summary>
    public SynopticConfig Synoptic { get; set; } = new();
    public CalmZoneConfig CalmZone { get; set; } = new();
    /// <summary>Layered bottom to top: a later region paints over an earlier one across its blend band.</summary>
    public List<RegionConfig> Regions { get; set; } = new();
    public TurbulenceModelConfig Turbulence { get; set; } = new();
    public OrographyConfig Orography { get; set; } = new();
    public ConvectionConfig Convection { get; set; } = new();
}

public sealed class SynopticConfig
{
    public double WindFromDeg { get; set; } = 90;
    /// <summary>Wind at the 10 m reference height over open ground.</summary>
    public double WindSpeedKt { get; set; } = 10;
    public double IsaDeviationC { get; set; }
    public double QnhHpa { get; set; } = 1013.25;
}

/// <summary>
/// The still air round the main airport (owner: "calm zone around the main airport"). The shape is a runway-aligned
/// STADIUM: every point within <see cref="RadiusM"/> of the main runway's centreline, the centreline extended
/// <see cref="ExtendBeyondThresholdsM"/> past each threshold. Its centre is the anchor airport's main-runway centre, and
/// the anchor is the DEFAULT START airport (WorldTerrain.DefaultAirportIndex), so moving the default spawn moves the zone.
/// </summary>
public sealed class CalmZoneConfig
{
    public string Id { get; set; } = "home-airport-calm";
    public bool Enabled { get; set; } = true;
    /// <summary>"defaultStartAirport", or "airport:&lt;name&gt;" / "airport:&lt;index&gt;".</summary>
    public string Anchor { get; set; } = "defaultStartAirport";
    public double RadiusM { get; set; } = 1200;
    public double ExtendBeyondThresholdsM { get; set; } = 1800;
    /// <summary>Top of the calm air, metres ABOVE FIELD ELEVATION (AFE) of the anchor airport: a flat ceiling at
    /// field elevation + this, MSL, everywhere over the zone.</summary>
    public double CeilingAfeM { get; set; } = 457.2;
    /// <summary>Horizontal blend, outward from the stadium's edge.</summary>
    public double BlendWidthM { get; set; } = 600;
    /// <summary>Vertical blend, upward from the ceiling.</summary>
    public double CeilingBlendM { get; set; } = 150;
}

public sealed class RegionConfig
{
    public string Id { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>"global", "circle", "box" or "capsule".</summary>
    public string Shape { get; set; } = "circle";
    /// <summary>Optional: centre the shape on an airport ("airport:Bench", "airport:1", "defaultStartAirport").
    /// Then CenterX/Y are offsets from it and the ceiling is above THAT field.</summary>
    public string? Anchor { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RadiusM { get; set; } = 1000;
    /// <summary>Box: half extents along x and y. Capsule: HalfLengthM along HeadingDeg, RadiusM across.</summary>
    public double HalfX { get; set; } = 1000;
    public double HalfY { get; set; } = 1000;
    public double HalfLengthM { get; set; } = 1000;
    public double HeadingDeg { get; set; }
    public double BlendM { get; set; } = 800;
    /// <summary>Optional top of the region: above the anchor's field (anchored) or MSL (not), with a blend above it.</summary>
    public double? CeilingM { get; set; }
    public double CeilingBlendM { get; set; } = 300;

    // ---- the air in it ----
    public double IsaDeviationC { get; set; }          // added to the synoptic deviation
    public double QnhOffsetHpa { get; set; }           // added to the synoptic QNH
    public double LapseRateCPerKm { get; set; } = 6.5; // environmental lapse rate: < 9.8 is stable, which lets lee waves stand
    public RegionWindConfig Wind { get; set; } = new();
    /// <summary>Multiplier on the MIL-HDBK-1797 boundary-layer turbulence (0 = smooth).</summary>
    public double TurbulenceScale { get; set; } = 1.0;
    /// <summary>Clear-air turbulence σ above the boundary layer (m/s).</summary>
    public double ClearAirSigmaMs { get; set; } = 0.3;
    /// <summary>Aerodynamic roughness length z0 (m): 0.0002 water, 0.03 grass, 0.1 crops, 0.5–1 forest/broken rock.</summary>
    public double RoughnessM { get; set; } = 0.03;
    /// <summary>Multiplier on everything orographic (speed-up, slope lift, lee sink/rotor, mountain turbulence, waves).</summary>
    public double OrographicScale { get; set; } = 1.0;
    /// <summary>Multiplier on thermals: the land-cover convection AND the authored thermal columns.</summary>
    public double ThermalScale { get; set; } = 1.0;
    /// <summary>Convective boundary layer depth (m above ground): thermals top out here.</summary>
    public double BoundaryLayerM { get; set; } = 2000;
    /// <summary>A uniform up (+) or down (−) drift of the whole air mass (m/s), e.g. subsidence over cold water.</summary>
    public double BackgroundVerticalMs { get; set; }
}

public sealed class RegionWindConfig
{
    /// <summary>"synoptic" (the air mass's wind, scaled/veered), "steady", "variable" or "gusty".</summary>
    public string Mode { get; set; } = "synoptic";
    public double SpeedScale { get; set; } = 1.0;      // synoptic: multiplier
    public double VeerDeg { get; set; }                // synoptic: turn of direction (+ = clockwise)
    public double FromDeg { get; set; } = 90;          // steady/gusty: direction
    public double SpeedKt { get; set; } = 5;           // steady/variable: mean; gusty: mean (gust lulls below it)
    public double GustPeakKt { get; set; } = 15;       // gusty: the top of a gust
    public double MeanGustIntervalS { get; set; } = 18; // gusty: mean quiet time between gusts
    public double MinGustDurationS { get; set; } = 4;
    public double MaxGustDurationS { get; set; } = 10;
    public double SpeedJitterKt { get; set; } = 0.8;   // variable: slow speed wander (1σ)
    public double TurnRateSigmaDegS { get; set; } = 2.5; // variable: 1σ of the direction's rate of change
    public double TurnRateMaxDegS { get; set; } = 6;   // variable: hard cap on the rate of change
    public double CorrelationS { get; set; } = 40;     // variable: how long a turn persists
    public int Seed { get; set; } = 1;
    /// <summary>Power-law exponent of the wind profile (0.10 water, 0.14 open, 0.22 forest/rough, 0.3 town).</summary>
    public double ProfileExponent { get; set; } = 0.14;
    /// <summary>Gradient height: the profile stops growing here (m above ground).</summary>
    public double GradientHeightM { get; set; } = 600;
}

public sealed class TurbulenceModelConfig
{
    /// <summary>"vonKarman" or "dryden" (MIL-F-8785C / MIL-HDBK-1797 forms).</summary>
    public string Spectrum { get; set; } = "vonKarman";
    public int Modes { get; set; } = 48;
    public int Seed { get; set; } = 7;
    /// <summary>Global multiplier (the session's turbulence setting rides on this).</summary>
    public double Scale { get; set; } = 1.0;
    /// <summary>Length-scale caps (m): MIL gives L ≈ h near the ground and 762 m (von Kármán, 2,500 ft; Dryden 533 m) aloft.</summary>
    public double MinLengthM { get; set; } = 15;
    public double MaxLengthM { get; set; } = 762;
}

public sealed class OrographyConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Peak fractional speed-up over a crest (Jackson–Hunt: ΔS ≈ 1.6 H/L, capped where the flow separates).</summary>
    public double MaxSpeedUp { get; set; } = 0.6;
    /// <summary>Relief that earns the full speed-up (m).</summary>
    public double SpeedUpReliefM { get; set; } = 400;
    /// <summary>e-fold height of the speed-up above the local ground (m).</summary>
    public double SpeedUpDecayM { get; set; } = 300;
    /// <summary>Lee downdraught as a fraction of the cross-ridge wind.</summary>
    public double LeeSinkFraction { get; set; } = 0.35;
    /// <summary>Peak rotor circulation speed as a fraction of the wind.</summary>
    public double RotorStrength { get; set; } = 0.45;
    /// <summary>Rotor centre downwind of the crest, in lee drops.</summary>
    public double RotorDistanceDrops { get; set; } = 1.2;
    /// <summary>Least lee drop that makes a rotor (m).</summary>
    public double MinLeeDropM { get; set; } = 60;
    /// <summary>Mechanical turbulence from slope: σ adds SlopeTurbulence × |slope| × U.</summary>
    public double SlopeTurbulence { get; set; } = 0.15;
    /// <summary>Extra σ in the lee/rotor, as a fraction of U.</summary>
    public double LeeTurbulence { get; set; } = 0.2;
    /// <summary>Lee waves: wavelength 2πU/N × this. Real waves are 5–15 km; the map is ~16 km across.</summary>
    public double WaveLengthCompression { get; set; } = 0.35;
    public double WaveAmplitude { get; set; } = 0.25;
    public double MinWaveWindMs { get; set; } = 6;
}

public sealed class ConvectionConfig
{
    /// <summary>Peak background convective updraught over the hottest ground at ThermalScale 1 (m/s).</summary>
    public double BackgroundUpdraftMs { get; set; } = 1.0;
    /// <summary>Convective cell spacing (m). Real cells are ~1.5 boundary-layer depths (3 km); compressed.</summary>
    public double CellSizeM { get; set; } = 700;
    /// <summary>Land-cover grid used to cache the heating map (m).</summary>
    public double GridM { get; set; } = 100;
}
