using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// A thermal: a column of rising air. Real thermals have a strong core updraft that falls off toward
/// the edge (roughly Gaussian) surrounded by a ring of compensating SINK, and they lean/drift with the
/// wind. Modeled as a vertical-velocity field the glider must center in to climb — circle in the core,
/// stray out and you hit the sink. Position is the core at the surface; the column leans downwind with
/// height by the mean wind.
/// </summary>
public sealed class Thermal
{
    public Vec3 SurfaceCenter { get; }   // core position at ground (world, z≈0)
    public double CoreRadiusM { get; }
    public double CoreUpdraftMs { get; } // peak updraft at the core (m/s, upward)
    public double TopAltitudeM { get; }  // thermal weakens to nothing by here
    public Vec3 LeanPerM { get; set; }   // horizontal drift of the core per metre of altitude (wind lean)

    public Thermal(Vec3 surfaceCenter, double coreRadiusM, double coreUpdraftMs, double topAltitudeM)
    {
        SurfaceCenter = surfaceCenter;
        CoreRadiusM = coreRadiusM;
        CoreUpdraftMs = coreUpdraftMs;
        TopAltitudeM = topAltitudeM;
    }

    /// <summary>How deep inside the warm core a point is, 0 (outside / above top) .. 1 (core centre at peak
    /// altitude): the same Gaussian-radial × altitude profile that shapes the updraft.</summary>
    public double CoreFractionAt(Vec3 pos)
    {
        double altitude = -pos.Z;
        if (altitude < 0 || altitude > TopAltitudeM)
        {
            return 0.0;
        }

        Vec3 core = SurfaceCenter + LeanPerM * altitude;
        double dx = pos.X - core.X, dy = pos.Y - core.Y;
        double zf = System.Math.Clamp(altitude / TopAltitudeM, 0, 1);
        double rNorm = System.Math.Sqrt(dx * dx + dy * dy) / (CoreRadiusM * (1.0 + 0.8 * zf));
        double altFactor = System.Math.Sin(System.Math.PI * zf);
        return System.Math.Exp(-rNorm * rNorm) * altFactor;
    }

    /// <summary>Vertical wind (world, +z down so a rising gust is NEGATIVE z) at a position.</summary>
    public Vec3 WindAt(Vec3 pos)
    {
        double altitude = -pos.Z;
        if (altitude < 0 || altitude > TopAltitudeM)
        {
            return Vec3.Zero;
        }

        // Core leans downwind with height.
        Vec3 core = SurfaceCenter + LeanPerM * altitude;
        double dx = pos.X - core.X, dy = pos.Y - core.Y;
        double r = System.Math.Sqrt(dx * dx + dy * dy);

        // Strength tapers with altitude (builds low, dies near the top); the column widens as it rises
        // (entrainment: convective plumes grow roughly linearly with height).
        double zf = System.Math.Clamp(altitude / TopAltitudeM, 0, 1);
        double altFactor = System.Math.Sin(System.Math.PI * zf);
        double radius = CoreRadiusM * (1.0 + 0.8 * zf);

        // Radial profile after the soaring-literature convective-plume models (Lenschow & Stephens 1980;
        // Allen 2006 as used in NASA's thermal-soaring work): a bell-shaped updraft core, surrounded by
        // an ANNULUS OF SINK — the air the plume lifts must come back down, and it does so mostly close
        // around the column. Glider pilots measure that sink at roughly a third of the core strength
        // just outside the edge, fading out by three radii. Profile: w/W = e^{-ρ²} − 0.38·e^{-(ρ−1.7)²}
        // (ρ = r/R): peak sink ≈ 0.36 W at ρ ≈ 1.7, zero crossing at ρ ≈ 1.15, gone by ρ ≈ 3.5.
        double rho = r / radius;
        double up = System.Math.Exp(-rho * rho);
        double sink = -0.38 * System.Math.Exp(-(rho - 1.7) * (rho - 1.7));
        if (rho > 5.0) return Vec3.Zero;
        double w = CoreUpdraftMs * (up + sink) * altFactor; // + = upward

        return new Vec3(0, 0, -w); // world +z is down, so upward air is -z
    }
}

/// <summary>
/// Ridge / slope lift: wind hitting a hill is deflected UP the windward face, giving a band of lift
/// along and above the slope. Modeled as an upward wind proportional to the component of the mean wind
/// blowing INTO the ridge, strongest just above the crest on the windward side and decaying with height
/// and downwind distance. The ridge is a line (crest) with an axis direction; the windward side is the
/// side the wind comes from.
/// </summary>
public sealed class Ridge
{
    public Vec3 CrestPoint { get; }     // a point on the ridge crest (world)
    public Vec3 AxisDir { get; }        // unit vector along the ridge line (horizontal)
    public double HeightM { get; }      // crest height above the surrounding ground
    public double LiftBandM { get; }    // how far above the crest the lift extends

    public Ridge(Vec3 crestPoint, Vec3 axisDir, double heightM, double liftBandM = 300)
    {
        CrestPoint = crestPoint;
        double len = System.Math.Sqrt(axisDir.X * axisDir.X + axisDir.Y * axisDir.Y);
        AxisDir = len > 1e-6 ? new Vec3(axisDir.X / len, axisDir.Y / len, 0) : new Vec3(1, 0, 0);
        HeightM = heightM;
        LiftBandM = liftBandM;
    }

    /// <summary>Upward wind from slope deflection given the current mean wind (world).</summary>
    public Vec3 WindAt(Vec3 pos, Vec3 meanWind)
    {
        // Cross-ridge horizontal wind component (the part that must climb the slope).
        Vec3 across = new(-AxisDir.Y, AxisDir.X, 0); // horizontal normal to the ridge
        double windAcross = meanWind.X * across.X + meanWind.Y * across.Y;
        if (System.Math.Abs(windAcross) < 0.5)
        {
            return Vec3.Zero; // calm or wind parallel to the ridge → no slope lift
        }

        // Distance from the crest along the across-axis (signed) and height above crest.
        double dAcross = (pos.X - CrestPoint.X) * across.X + (pos.Y - CrestPoint.Y) * across.Y;
        double heightAboveCrest = (-pos.Z) - (-CrestPoint.Z);

        // Lift lives on the WINDWARD side (upwind of the crest) and just above it. Windward is the
        // side the wind blows FROM: sign of dAcross opposite to windAcross direction.
        double windwardSide = -System.Math.Sign(windAcross) * dAcross; // >0 = on the windward face
        if (windwardSide < -50 || heightAboveCrest < -20 || heightAboveCrest > LiftBandM)
        {
            return Vec3.Zero;
        }

        // Band strongest near the crest, decaying up and with downwind/upwind distance.
        double heightFactor = System.Math.Exp(-System.Math.Max(0, heightAboveCrest) / (LiftBandM * 0.4));
        double acrossFactor = System.Math.Exp(-(windwardSide * windwardSide) / (2 * 150.0 * 150.0));
        double up = System.Math.Abs(windAcross) * 0.7 * heightFactor * acrossFactor; // deflected-up fraction

        return new Vec3(0, 0, -up);
    }
}


/// <summary>
/// Slope (ridge) lift from the terrain itself: the wind cannot pass through the ground, so at the surface
/// its vertical component equals the horizontal wind times the terrain slope along the wind
/// (w = U·∂h/∂s, the kinematic boundary condition) — UP on a windward face, DOWN on a lee slope. Aloft the
/// deflection is felt along tilted streamlines that carry it up and downwind of the face that made it, and
/// it decays with height above the surface over roughly a wall height. The strongest lift therefore sits
/// just in front of and above the crest, exactly where ridge pilots soar; the lee side is sink. Slopes
/// steeper than ~35° are capped (the flow separates rather than following a cliff face).
/// </summary>
public static class SlopeLift
{
    public const double MaxSlope = 0.7;            // tan 35°
    public const double DecayHeightM = 320.0;      // e-fold of the deflection with height above ground
    public const double StreamlineTilt = 0.8;      // upwind sample distance per metre of height (≈ 39° tilt)
    public const double SampleStepM = 25.0;

    public static Vec3 WindAt(WorldTerrain t, Vec3 pos, Vec3 meanWind)
    {
        double ux = meanWind.X, uy = meanWind.Y;
        double u = System.Math.Sqrt(ux * ux + uy * uy);
        if (u < 0.5) return Vec3.Zero;
        double alt = -pos.Z;
        double hHere = t.HeightAt(pos.X, pos.Y);
        double agl = alt - hHere;
        if (agl < -5 || agl > DecayHeightM * 4) return Vec3.Zero;
        agl = System.Math.Max(0, agl);
        // The air at height AGL was deflected by the ground it crossed UPWIND (tilted streamline).
        double sx = pos.X - ux / u * agl * StreamlineTilt, sy = pos.Y - uy / u * agl * StreamlineTilt;
        double dhds = (t.HeightAt(sx + ux / u * SampleStepM, sy + uy / u * SampleStepM) - t.HeightAt(sx - ux / u * SampleStepM, sy - uy / u * SampleStepM)) / (2 * SampleStepM);
        dhds = System.Math.Clamp(dhds, -MaxSlope, MaxSlope);
        double w = u * dhds * System.Math.Exp(-agl / DecayHeightM);   // + = upward
        return new Vec3(0, 0, -w);
    }
}
