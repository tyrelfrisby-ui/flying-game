using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// "On final" start: the aircraft at 300 ft AGL on the extended centreline, power at idle, trimmed at its BEST
/// GLIDE speed on its best glide angle, aimed at a point 200 m past the threshold. Best glide is found from the
/// trim solver by sweeping airspeed for the maximum glide ratio (the idle prop's few newtons are ignored).
/// </summary>
public static class ApproachSpawn
{
    public const double AglM = 91.44;             // 300 ft
    public const double AimPastThresholdM = 200.0;

    public readonly struct BestGlide
    {
        public readonly double SpeedMs, GlideRatio, AlphaRad, ThetaRad, ElevatorRad, SpoilerFraction;
        public BestGlide(double v, double ld, double a, double th, double e, double spoiler = 0.0) { SpeedMs = v; GlideRatio = ld; AlphaRad = a; ThetaRad = th; ElevatorRad = e; SpoilerFraction = spoiler; }
        public double GammaRad => System.Math.Atan(1.0 / GlideRatio);   // descent angle (positive = down)
    }

    /// <summary>Gliders come down final with HALF SPOILER (owner): approach at 1.15 × best-glide speed, trimmed with
    /// the brakes half out — a steeper, stable path. Powered types glide clean at idle.</summary>
    public const double GliderSpoilerFraction = 0.5, GliderApproachSpeedFactor = 1.15;

    public static BestGlide ApproachGlide(AircraftConfig config, double altitudeM)
    {
        BestGlide best = FindBestGlide(config, altitudeM);
        if (config.Propulsion is not null) return best;
        double v = best.SpeedMs * GliderApproachSpeedFactor;
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, altitudeM, spoilerFraction: GliderSpoilerFraction);
        if (!t.Converged || t.GlideRatio <= 0) return best;
        return new BestGlide(v, t.GlideRatio, t.AlphaRad, t.ThetaRad, t.ElevatorRad, GliderSpoilerFraction);
    }

    private static readonly Dictionary<string, BestGlide> Cache = new();

    /// <summary>Sweep airspeed (0.55–1.6 × the type's spawn speed, 0.5 m/s steps) for the trimmed glide with the
    /// highest L/D.</summary>
    public static BestGlide FindBestGlide(AircraftConfig config, double altitudeM)
    {
        string key = config.Id + ":" + System.Math.Round(altitudeM / 100);
        lock (Cache) { if (Cache.TryGetValue(key, out BestGlide cached)) return cached; }
        double vRef = config.SpawnIasMs > 1 ? config.SpawnIasMs : 25.0;
        BestGlide best = default; bool any = false;
        for (double v = vRef * 0.55; v <= vRef * 1.6; v += 0.5)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, altitudeM);
            if (!t.Converged || t.GlideRatio <= 0 || double.IsNaN(t.GlideRatio)) continue;
            if (t.AlphaRad > 14 * System.Math.PI / 180) continue;   // past the usable range: near/over the stall
            if (!any || t.GlideRatio > best.GlideRatio) { best = new BestGlide(v, t.GlideRatio, t.AlphaRad, t.ThetaRad, t.ElevatorRad); any = true; }
        }
        if (!any)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, vRef, altitudeM);
            best = new BestGlide(vRef, System.Math.Max(4.0, t.GlideRatio), t.AlphaRad, t.ThetaRad, t.ElevatorRad);
        }
        lock (Cache) { Cache[key] = best; }
        return best;
    }

    /// <summary>State on final for the airport's main runway (runway along +x, threshold at X − length/2), heading
    /// north up the centreline. Ground velocity = best-glide air velocity + local wind (coordinated crab).</summary>
    public static (RigidBodyState State, BestGlide Glide, double AimX) Compute(AircraftConfig config, WorldTerrain.Airport airport)
    {
        var main = System.Array.Find(WorldTerrain.AirportStrips, st => st.Kind == "paved");
        var rw = new WorldTerrain.RunwayEnd(main, 0, airport.X + main.Dx, airport.Y + main.Dy);
        return Compute(config, airport, rw);
    }

    /// <summary>State on final for a chosen runway end (owner 2026-09-10: headwind or crosswind runway): 300 ft AGL on
    /// the extended centreline, heading the runway's heading, aimed 200 m past its threshold.</summary>
    public static (RigidBodyState State, BestGlide Glide, double AimX) Compute(AircraftConfig config, WorldTerrain.Airport airport, WorldTerrain.RunwayEnd rw)
    {
        double alt = airport.ElevationM + AglM;
        BestGlide g = ApproachGlide(config, alt);
        (double aimX, double aimY) = rw.At(AimPastThresholdM);
        double back = AglM / System.Math.Tan(g.GammaRad);
        var pos = new Vec3(aimX - rw.AlongX * back, aimY - rw.AlongY * back, -alt);
        double half = g.ThetaRad / 2.0, hh = rw.HeadingRad / 2.0;
        var att = Quat.Multiply(new Quat(0, 0, System.Math.Sin(hh), System.Math.Cos(hh)), new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half)));   // yaw then pitch
        var vBody = new Vec3(g.SpeedMs * System.Math.Cos(g.AlphaRad), 0, g.SpeedMs * System.Math.Sin(g.AlphaRad))
                    + att.Conjugate().Rotate(Atmosphere.WindAtPosition(pos));
        return (new RigidBodyState(pos, att, vBody, Vec3.Zero), g, aimX);
    }
}
