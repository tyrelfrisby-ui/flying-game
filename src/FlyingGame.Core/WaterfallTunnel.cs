using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The hidden runway behind the waterfall (owner 2026-10-16): fly through the curtain of the FIRST wall's
/// plunge fall and the back of the amphitheater is not rock but a bored tunnel with a paved runway in it,
/// long enough to land in and take off from again.
///
/// Why this fall. There are three, one at each canyon wall; <see cref="FallStep"/> = 0 is the one between the
/// Valley and Bench plateaus — the nearest to the base airport and the only one a pilot flying from Valley
/// reaches without crossing a wall first.
///
/// The approach is the whole point of it. The amphitheater floor is the lower plateau's own level, so a pilot
/// crosses the gorge at a few tens of feet, aims at the base of the falls, and flies through the water: at the
/// mouth's height the curtain hangs about 88 m clear of the back wall, which at 90 kt is two seconds between
/// the water and the rock. There is no way in that does not commit.
///
/// Geometry, all derived from the fall so it follows the river if the river ever moves:
///  - the bore runs upstream (−y) from the amphitheater's back wall, on the river's own centreline, under the
///    upper gorge — 204 m of rock between the crown and the river bed above it;
///  - the floor is flush with the amphitheater shelf, so the runway and the shelf are one surface;
///  - the cross-section is an elliptical arch, <see cref="HalfWidthM"/> by <see cref="CrownM"/>, which closes
///    to the floor at the edges. That matters for more than looks: the arch is a solid, so a pilot who drifts
///    wide meets rock and is pushed back in rather than reaching the terrain outside, which is 200 m of
///    mountain and would throw the airframe.
/// </summary>
public static class WaterfallTunnel
{
    /// <summary>Which plunge fall hides it: the first wall's, between the Valley and Bench plateaus.</summary>
    public const int FallStep = 0;

    public const double HalfWidthM = 35.0;        // bore half-width; the mouth is 70 m across
    public const double CrownM = 32.0;            // clear height at the centreline
    public const double BoreLengthM = 900.0;      // from the mouth to the end wall
    public const double RunwayLengthM = 800.0;    // 2,625 ft of pavement inside
    public const double RunwayHalfWidthM = 15.0;  // 30 m wide
    public const double RunwayStartInM = 40.0;    // pavement starts this far in, so the threshold is not the sill
    public const double MinRoofM = 3.0;

    /// <summary>The fall this hangs behind, from a terrain instance.</summary>
    public static WorldTerrain.Waterfall Fall(WorldTerrain t) => t.Waterfalls[FallStep];

    /// <summary>Bore centreline x: the river's own centre at the lip, so the mouth is squarely behind the
    /// curtain.</summary>
    public static double CentreX(in WorldTerrain.Waterfall f) => f.X;

    /// <summary>y of the mouth — the amphitheater's back wall on the centreline. The bore runs to smaller y.</summary>
    public static double MouthY(in WorldTerrain.Waterfall f) => f.LipY - WorldTerrain.FallRecessAt(f, f.X);

    public static double EndY(in WorldTerrain.Waterfall f) => MouthY(f) - BoreLengthM;

    /// <summary>Floor height: the lower plateau, flush with the shelf outside the mouth.</summary>
    public static double FloorM(in WorldTerrain.Waterfall f) => f.LowerM;

    public static bool InFootprint(in WorldTerrain.Waterfall f, double x, double y)
        => System.Math.Abs(x - CentreX(f)) < HalfWidthM && y <= MouthY(f) && y >= EndY(f);

    /// <summary>Clear height above the floor at x: the elliptical arch, 0 at the bore's edge.</summary>
    public static double ClearHeightAt(in WorldTerrain.Waterfall f, double x)
    {
        double u = (x - CentreX(f)) / HalfWidthM;
        if (System.Math.Abs(u) >= 1) return 0;
        return CrownM * System.Math.Sqrt(1 - u * u);
    }

    /// <summary>Ceiling height (m, up) at x — the underside of the arch.</summary>
    public static double CeilingAt(in WorldTerrain.Waterfall f, double x) => FloorM(f) + ClearHeightAt(f, x);

    /// <summary>The paved runway inside: it starts in from the sill and stops short of the end wall.</summary>
    public static bool OnRunway(in WorldTerrain.Waterfall f, double x, double y)
    {
        if (System.Math.Abs(x - CentreX(f)) > RunwayHalfWidthM) return false;
        double in0 = MouthY(f) - RunwayStartInM;
        return y <= in0 && y >= in0 - RunwayLengthM;
    }

    /// <summary>Rock between the arch and the mountain above it, at (x, y). <paramref name="outer"/> is the
    /// terrain as it would be with no tunnel bored through it.</summary>
    public static bool RoofAt(WorldTerrain t, in WorldTerrain.Waterfall f, double x, double y, out double ceiling, out double outer)
    {
        ceiling = CeilingAt(f, x);
        outer = t.HeightAt(x, y, false, withTunnel: false);
        return InFootprint(f, x, y) && outer > ceiling + MinRoofM;
    }

    /// <summary>Register the arch and the end wall. Called from
    /// <see cref="WorldTerrain.RegisterWaterfallSolids"/> with the rest of the falls' rock.</summary>
    public static void RegisterSolids(WorldTerrain t)
    {
        var f = Fall(t);
        WorldSolids.Shapes.Add(new ArchSolid(t));
        // The end of the bore: a plain wall across it. A box, because near its face the least-penetration
        // axis is +y, which is the push a pilot who ran out of tunnel needs.
        double endY = EndY(f);
        WorldSolids.Boxes.Add(new WorldSolids.Box(CentreX(f), endY - 30, HalfWidthM, 30, FloorM(f) - 5, FloorM(f) + CrownM + 10));
    }

    /// <summary>
    /// The arch as a solid. Inside the bore's footprint, the rock is everything above the arch and below the
    /// mountain's own surface; the push is back toward the bore's axis along the ellipse's normal, so a wing
    /// that catches the springing of the arch is pushed down and inward rather than straight down.
    /// </summary>
    public sealed class ArchSolid : WorldSolids.IShape
    {
        private readonly WorldTerrain _t;
        public ArchSolid(WorldTerrain t) { _t = t; }

        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            normalNed = Vec3.Zero; depth = 0;
            var f = Fall(_t);
            if (!InFootprint(f, x, y)) return false;
            double floor = FloorM(f);
            double z = up - floor;
            if (z < 0) return false;                       // under the floor is the floor's business
            double dx = x - CentreX(f);
            double s2 = dx * dx / (HalfWidthM * HalfWidthM) + z * z / (CrownM * CrownM);
            if (s2 < 1.0) return false;                    // inside the arch: open air
            if (!RoofAt(_t, f, x, y, out _, out double outer) || up > outer) return false;   // out the top of the mountain
            double s = System.Math.Sqrt(s2);
            // Push toward the axis along the ellipse normal, and take the depth as the distance back to the
            // surface along that ray — exact for a circle, close enough for this arch.
            var push = new Vec3(-dx / (HalfWidthM * HalfWidthM), 0, z / (CrownM * CrownM));   // NED: z down = +(up)
            double len = push.Length;
            if (len < 1e-12) { normalNed = new Vec3(0, 0, 1); depth = 0.1; return true; }
            normalNed = push / len;
            depth = System.Math.Sqrt(dx * dx + z * z) * (1 - 1 / s);
            return true;
        }
    }
}
