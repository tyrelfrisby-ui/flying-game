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

        // Strength tapers with altitude (builds low, dies near the top).
        double altFactor = System.Math.Sin(System.Math.PI * System.Math.Clamp(altitude / TopAltitudeM, 0, 1));

        // Gaussian core updraft; a gentle sink ring just outside the core (mass continuity).
        double rNorm = r / CoreRadiusM;
        double up = CoreUpdraftMs * System.Math.Exp(-rNorm * rNorm) * altFactor;
        double sink = -0.25 * CoreUpdraftMs * System.Math.Exp(-((rNorm - 2.0) * (rNorm - 2.0))) * altFactor;
        double w = up + sink; // + = upward

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
