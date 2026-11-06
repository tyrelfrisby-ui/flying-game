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
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, v, altitudeM, idleProp: true);   // power-off: the idling prop's drag counts
            if (!t.Converged || t.GlideRatio <= 0 || double.IsNaN(t.GlideRatio)) continue;
            if (t.AlphaRad > 14 * System.Math.PI / 180) continue;   // past the usable range: near/over the stall
            if (!any || t.GlideRatio > best.GlideRatio) { best = new BestGlide(v, t.GlideRatio, t.AlphaRad, t.ThetaRad, t.ElevatorRad); any = true; }
        }
        if (!any)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(config, vRef, altitudeM, idleProp: true);
            best = new BestGlide(vRef, System.Math.Max(4.0, t.GlideRatio), t.AlphaRad, t.ThetaRad, t.ElevatorRad);
        }
        lock (Cache) { Cache[key] = best; }
        return best;
    }

    /// <summary>State on final for the airport's main runway (runway along +x, threshold at X − length/2), tracking
    /// north up the centreline, crabbed into any crosswind (see <see cref="CrabFor"/>).</summary>
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
        (double aimX, double aimY) = rw.At(AimPastThresholdM);
        return ComputeAt(config, aimX, aimY, airport.ElevationM, rw.HeadingRad);
    }

    /// <summary>On final to the field's LAKE for a floatplane (owner 2026-09-14): 300 ft above the water on the lake's long
    /// (north–south) axis, heading into whatever wind blows along it, aimed 200 m short of the centre.</summary>
    public static (RigidBodyState State, BestGlide Glide, double AimX) ComputeToLake(AircraftConfig config, WorldTerrain.Lake lake)
    {
        Vec3 wind = Atmosphere.WindAtPosition(new Vec3(lake.Cx, lake.Cy, -lake.SurfaceM));
        double heading = wind.X > 0 ? System.Math.PI : 0.0;   // wind blowing north → land heading south, into it
        double ax = System.Math.Cos(heading), ay = System.Math.Sin(heading);
        return ComputeAt(config, lake.Cx - ax * AimPastThresholdM, lake.Cy - ay * AimPastThresholdM, lake.SurfaceM, heading);
    }

    /// <summary>Largest crab the final is started with (Ty 2026-11-05 Q129). Past it the crosswind is more than the
    /// pilot would accept on final; the aeroplane is started at this crab and the rest of the crosswind is left to drift
    /// it — nothing else (bank, extra speed, a side-slip) is invented to make up the difference.</summary>
    public const double MaxCrabRad = 45.0 * System.Math.PI / 180.0;

    /// <summary>The crab that puts the ground track on the centreline. CrabRad: heading minus runway heading, positive =
    /// nose turned right of the centreline (into a wind from the right). ResidualCrossMs: ground drift across the
    /// centreline left over (0 when attainable; positive = to the right). AlongGroundMs: ground speed along the
    /// centreline toward the runway. Attainable: false when the crosswind needed more than <see cref="MaxCrabRad"/>.</summary>
    public readonly struct Crab
    {
        public readonly double CrabRad, ResidualCrossMs, AlongGroundMs;
        public readonly bool Attainable;
        public Crab(double crabRad, double residualCrossMs, double alongGroundMs, bool attainable) { CrabRad = crabRad; ResidualCrossMs = residualCrossMs; AlongGroundMs = alongGroundMs; Attainable = attainable; }
    }

    /// <summary>Crab for a runway heading, a wind vector (velocity of the air, world NED) and the horizontal part of the
    /// aeroplane's airspeed: the air vector's crosswind share cancels the wind's, sin(crab) = −w_cross / V_h, so ground
    /// velocity = air + wind runs down the centreline. When |w_cross| / V_h exceeds sin(<see cref="MaxCrabRad"/>) — which
    /// includes every wind faster across than the aeroplane flies, where no heading at all holds the line — the crab
    /// is held at the cap and the remainder is reported as drift. Never calls asin outside [−1, 1].</summary>
    public static Crab CrabFor(double headingRad, Vec3 wind, double airHorizMs)
    {
        double ux = System.Math.Cos(headingRad), uy = System.Math.Sin(headingRad);
        double wAlong = wind.X * ux + wind.Y * uy, wCross = -wind.X * uy + wind.Y * ux;   // cross: + = toward the right (east of north)
        if (!(airHorizMs > 1e-6)) return new Crab(0, wCross, wAlong, wCross == 0);   // no airspeed to crab with
        double s = -wCross / airHorizMs, cap = System.Math.Sin(MaxCrabRad);
        bool ok = System.Math.Abs(s) <= cap;
        double crab = System.Math.Asin(System.Math.Clamp(s, -cap, cap));
        return new Crab(crab, wCross + airHorizMs * System.Math.Sin(crab), wAlong + airHorizMs * System.Math.Cos(crab), ok);
    }

    /// <summary>State on final: 300 ft above the surface, on the approach line through the aim point, its ground track
    /// down the given heading (Ty 2026-11-05: "the flight path should be already pointed to the runway so a crosswind
    /// doesn't immediately cause it to be off course"). The wind is sampled ONCE at the start point with its gusts
    /// (<see cref="Atmosphere.WindAtPosition"/>, the same sample the initial velocity is built from), the nose is
    /// yawed into it by <see cref="CrabFor"/>, and the body velocity is the trimmed glide's airspeed at α plus that
    /// wind — so the aeroplane starts in balance (no sideslip) with its ground velocity on the centreline. Pitch,
    /// α, airspeed and the start point are those of the calm-air final.</summary>
    public static (RigidBodyState State, BestGlide Glide, double AimX) ComputeAt(AircraftConfig config, double aimX, double aimY, double surfaceM, double headingRad)
    {
        var r = ComputeWithCrab(config, aimX, aimY, surfaceM, headingRad);
        return (r.State, r.Glide, r.AimX);
    }

    /// <summary><see cref="ComputeAt"/>, also returning the crab it chose.</summary>
    public static (RigidBodyState State, BestGlide Glide, double AimX, Crab Crab) ComputeWithCrab(AircraftConfig config, double aimX, double aimY, double surfaceM, double headingRad)
    {
        double alt = surfaceM + AglM;
        BestGlide g = ApproachGlide(config, alt);
        double alongX = System.Math.Cos(headingRad), alongY = System.Math.Sin(headingRad);
        double back = AglM / System.Math.Tan(g.GammaRad);
        var pos = new Vec3(aimX - alongX * back, aimY - alongY * back, -alt);
        Vec3 wind = Atmosphere.WindAtPosition(pos);
        Crab crab = CrabFor(headingRad, wind, g.SpeedMs * System.Math.Cos(g.ThetaRad - g.AlphaRad));
        double half = g.ThetaRad / 2.0, hh = (headingRad + crab.CrabRad) / 2.0;
        var att = Quat.Multiply(new Quat(0, 0, System.Math.Sin(hh), System.Math.Cos(hh)), new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half)));   // yaw then pitch
        var vBody = new Vec3(g.SpeedMs * System.Math.Cos(g.AlphaRad), 0, g.SpeedMs * System.Math.Sin(g.AlphaRad))
                    + att.Conjugate().Rotate(wind);
        return (new RigidBodyState(pos, att, vBody, Vec3.Zero), g, aimX, crab);
    }
}
