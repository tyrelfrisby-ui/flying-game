using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>All fuel aboard at a water break-up, spread to a rainbow film. Growth is linear in RADIUS,
/// reaching the volume-derived final radius after 300 simulation seconds. Times are elapsed sim seconds
/// since break-up, never wall time. Drift: the surface current under the slick's centre (<see cref="WaterCurrent"/>:
/// a river carries it downstream and round its bends, the tide sets it to and fro) plus 3 % of the mean 10 m wind
/// sampled at creation (no gusts), integrated in 1 s steps. The water shader's flow is cosmetic.</summary>
public sealed class FuelSlick
{
    // Bonn Agreement Oil Appearance Code, code 2: rainbow 0.30–5.0 micrometres.
    // https://www.bonnagreement.org/site/assets/files/17600/ba-oah_part_iii.pdf
    public const double RainbowThicknessM = 1.0e-6;
    public const double SpreadTimeS = 300.0;
    // Usual 3% rule in spill trajectory models; e.g. Wagner et al. (2022),
    // "How winds and ocean currents influence the drift of floating objects", arXiv:2201.12688.
    public const double WindDriftFraction = 0.03;
    // No density field in AircraftConfig; share the existing typical avgas/mogas density (720 kg/m³).
    public const double FuelDensityKgM3 = WaterPhysics.RhoAvgas;
    public double FuelKg { get; }
    public double VolumeM3 => FuelKg / FuelDensityKgM3;
    public double FinalAreaM2 => VolumeM3 / RainbowThicknessM;
    public double FinalRadiusM => System.Math.Sqrt(FinalAreaM2 / System.Math.PI);
    public Vec3 Origin { get; }
    /// <summary>The wind's share of the drift (3 % of the 10 m wind), m/s.</summary>
    public Vec3 WindDrift { get; }
    /// <summary>The drift at the origin when the slick formed: the wind's share plus the surface current there.</summary>
    public Vec3 DriftVelocity { get; }
    private double _tAt;
    private Vec3 _centreAt;

    private FuelSlick(double fuelKg, Vec3 surfacePoint, Vec3 wind10m)
    {
        FuelKg = fuelKg; Origin = surfacePoint;
        WindDrift = new Vec3(wind10m.X * WindDriftFraction, wind10m.Y * WindDriftFraction, 0);
        DriftVelocity = WindDrift + WaterCurrent.SurfaceAt(surfacePoint.X, surfacePoint.Y);
        _centreAt = Origin;
    }

    public static FuelSlick? TryCreate(AircraftConfig config, AircraftLoad load, bool water, bool brokenUp,
        Vec3 surfacePoint, Vec3 meanWind10m)
    {
        if (!water || !brokenUp || config.Propulsion is null || load.FuelCapacityKg <= 0 || load.FuelKg <= 0)
            return null;
        return new FuelSlick(load.FuelKg, surfacePoint, meanWind10m);
    }

    /// <summary>The atmosphere's 10 m reference wind, at this horizontal position. Its height profile
    /// uses terrain AGL (regional field) or the sea-level datum (uniform atmosphere), not water height.
    /// Sample at that reference height so an elevated lake does not multiply U10 by an altitude factor.</summary>
    public static Vec3 MeanWind10At(Vec3 surfacePoint)
    {
        double datum = Atmosphere.Field?.GroundAt(surfacePoint.X, surfacePoint.Y) ?? 0;
        return Atmosphere.SteadyWindAt(new Vec3(surfacePoint.X, surfacePoint.Y, -(datum + 10)));
    }

    public double RadiusAt(double elapsedSimS) => FinalRadiusM * System.Math.Clamp(elapsedSimS / SpreadTimeS, 0, 1);
    /// <summary>Where the slick's centre is after <paramref name="elapsedSimS"/>: carried by the current under it and the
    /// wind's share, stepped forward from the last answer (asking for an earlier time starts again from the origin).</summary>
    public Vec3 CentreAt(double elapsedSimS)
    {
        double t = System.Math.Max(0, elapsedSimS);
        if (t < _tAt) { _tAt = 0; _centreAt = Origin; }
        while (_tAt < t)
        {
            double h = System.Math.Min(1.0, t - _tAt);
            Vec3 v = WindDrift + WaterCurrent.SurfaceAt(_centreAt.X, _centreAt.Y);
            _centreAt += new Vec3(v.X, v.Y, 0) * h;
            _tAt += h;
        }
        return _centreAt;
    }
}
