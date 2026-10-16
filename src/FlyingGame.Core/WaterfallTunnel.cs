using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The hidden airport behind the waterfall (owner 2026-10-16): fly through the curtain of the FIRST wall's
/// plunge fall and the mountain is bored out into a one-way loop — forks, landing strips, and three ways to
/// get it wrong.
///
/// Why this fall. There are three, one at each canyon wall; <see cref="FallStep"/> = 0 is the one between the
/// Valley and Bench plateaus, the nearest to the base airport and the only one a pilot flying from Valley
/// reaches without crossing a wall first.
///
/// TWO MOUTHS, both behind the same curtain. The low one is at the amphitheater's own floor and is the way
/// OUT; the high one, 125 m above it, is the way IN. At the high mouth's height the falling water hangs about
/// 19 m clear of the rock, so the pocket you fly into is some 40 m deep — tighter than the bottom, and
/// deliberately so.
///
/// THE RULE EVERY NUMBER COMES FROM is the T-6: 12.8 m of span and up to 180 kt, the largest and fastest
/// thing that has to fit. The small aeroplanes are the harder half — a 172 cannot hold a knife edge and has
/// to bank through the slit, and a glider cannot climb out of a mistake at all, which is why the maze is
/// powered-only with a marked strip for gliders before the first fork.
///
/// GEOMETRY. The maze is a list of <see cref="Leg"/>s: straight centreline segments (bends are arcs chopped
/// into segments) with an elliptical arch over each. A point is inside the maze if it is within a leg's
/// half-width of that leg's centreline; where two legs overlap in plan they must agree on their floor, which
/// is what keeps the height field single-valued at a junction and is checked by a test. The arch is a solid,
/// so a wing that drifts wide meets rock and is pushed back toward the centreline rather than reaching the
/// terrain outside, which is a few hundred metres of mountain and would throw the airframe.
/// </summary>
public static class WaterfallTunnel
{
    /// <summary>Which plunge fall hides it: the first wall's, between the Valley and Bench plateaus.</summary>
    public const int FallStep = 0;

    // ---- the cross-sections ---------------------------------------------------------------------
    public const double HalfWidthM = 35.0;        // the ordinary bore: 70 m across
    public const double CrownM = 32.0;            // clear height at the centreline
    public const double SlitHalfWidthM = 6.1;     // the knife-edge slit: 40 ft wide (owner: it stays 40 ft)
    public const double SlitCrownM = 20.0;        // and tall enough for a T-6 on its side
    public const double SlitLengthM = 40.0;
    public const double MinRoofM = 3.0;

    // ---- the strips -----------------------------------------------------------------------------
    /// <summary>Every strip a powered aeroplane may have to land on is this long: a T-6 stops in about
    /// 1,500 ft and clears fifty feet in about 1,800 ft, so anything shorter makes a dead end a trap
    /// (owner 2026-10-16: lengthen Strip 2 — every dead end has to work up to the T-6).</summary>
    public const double StripM = 762.0;           // 2,500 ft exactly
    public const double StripHalfWidthM = 15.0;
    /// <summary>The glider strip inside the entrance. Shorter, because a 2-33 lands in a quarter of it, and a
    /// terminus: a glider has no way out of a mountain, so this is where its flight ends — or where the tow
    /// plane picks it up.</summary>
    public const double GliderStripM = 500.0;

    public const double EntryFloorM = 430.0;      // the high mouth: the way in
    public const double BoreLengthM = 900.0;      // the low bore, the way out
    public const double RunwayStartInM = 40.0;    // the low bore's pavement starts this far in
    public const double BendRadiusM = 250.0;      // 180 kt at 4 g needs 226 m
    /// <summary>How far to the side of the low mouth the high one sits. Enough that the entry leg is not
    /// stacked on the bore below it, which the height field could not answer.</summary>
    public const double EntryOffsetXM = 110.0;

    public static WorldTerrain.Waterfall Fall(WorldTerrain t) => t.Waterfalls[FallStep];

    /// <summary>Bore centreline x at the mouths: the river's own centre at the lip, so both mouths are
    /// squarely behind the curtain.</summary>
    public static double CentreX(in WorldTerrain.Waterfall f) => f.X;

    /// <summary>y of the mouths — the amphitheater's back wall on the centreline. The maze runs to smaller y.</summary>
    public static double MouthY(in WorldTerrain.Waterfall f) => f.LipY - WorldTerrain.FallRecessAt(f, f.X);

    /// <summary>Floor of the low bore, flush with the amphitheater shelf outside its mouth.</summary>
    public static double FloorM(in WorldTerrain.Waterfall f) => f.LowerM;

    public static double EndY(in WorldTerrain.Waterfall f) => MouthY(f) - BoreLengthM;

    /// <summary>What a leg is for — the thing the renderer and the scoring need to know.</summary>
    public enum Kind { Bore, Strip, GliderStrip, Slit, Rockfall, WaterSheet, DeadEndPad }

    /// <summary>One straight run of tunnel: a centreline from (X0,Y0) to (X1,Y1) with the floor sloping from
    /// Z0 to Z1, and an elliptical arch <see cref="Crown"/> high and <see cref="HalfWidth"/> wide over it.</summary>
    public readonly struct Leg
    {
        public readonly string Id;
        public readonly Kind Kind;
        public readonly double X0, Y0, Z0, X1, Y1, Z1, HalfWidth, Crown, Length;

        public Leg(string id, Kind kind, double x0, double y0, double z0, double x1, double y1, double z1,
                   double halfWidth, double crown)
        {
            Id = id; Kind = kind;
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
            HalfWidth = halfWidth; Crown = crown;
            Length = System.Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        }

        /// <summary>Where (x, y) falls on this leg: <paramref name="s"/> is 0..1 along the centreline and
        /// <paramref name="lateral"/> the distance from it. False when the point is off the end or too wide.</summary>
        public bool Locate(double x, double y, out double s, out double lateral)
        {
            double dx = X1 - X0, dy = Y1 - Y0, len2 = dx * dx + dy * dy;
            s = len2 > 0 ? ((x - X0) * dx + (y - Y0) * dy) / len2 : 0;
            if (s < 0 || s > 1) { lateral = double.MaxValue; return false; }
            double px = X0 + s * dx, py = Y0 + s * dy;
            lateral = System.Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            return lateral < HalfWidth;
        }

        public double FloorAt(double s) => Z0 + (Z1 - Z0) * System.Math.Clamp(s, 0, 1);

        /// <summary>Clear height above the floor at a lateral offset: the arch, 0 at the bore's edge.</summary>
        public double ClearHeight(double lateral)
        {
            double u = lateral / HalfWidth;
            return u >= 1 ? 0 : Crown * System.Math.Sqrt(1 - u * u);
        }
    }

    // ---- the maze, laid out once ----------------------------------------------------------------
    private static Leg[]? _legs;
    private static double _builtForX = double.NaN, _builtForY;
    private static double _minX, _maxX, _minY, _maxY;

    /// <summary>Every leg of the maze, including the low bore. Built once; the layout is a pure function of
    /// the fall, so it follows the river if the river ever moves.</summary>
    public static Leg[] Legs(in WorldTerrain.Waterfall f)
    {
        if (_legs != null && _builtForX == f.X && _builtForY == f.LipY) return _legs;
        _legs = Build(f);
        _builtForX = f.X; _builtForY = f.LipY;
        _minX = _minY = double.MaxValue; _maxX = _maxY = double.MinValue;
        foreach (Leg l in _legs)
        {
            double pad = l.HalfWidth + 1;
            _minX = System.Math.Min(_minX, System.Math.Min(l.X0, l.X1) - pad);
            _maxX = System.Math.Max(_maxX, System.Math.Max(l.X0, l.X1) + pad);
            _minY = System.Math.Min(_minY, System.Math.Min(l.Y0, l.Y1) - pad);
            _maxY = System.Math.Max(_maxY, System.Math.Max(l.Y0, l.Y1) + pad);
        }
        return _legs;
    }

    /// <summary>The whole maze's footprint, for the terrain mesh and for a cheap early reject.</summary>
    public static (double minX, double maxX, double minY, double maxY) Bounds(in WorldTerrain.Waterfall f)
    {
        Legs(f);
        return (_minX, _maxX, _minY, _maxY);
    }

    /// <summary>Lays the tunnel out: a pen that goes straight, turns, and drops as it goes.</summary>
    private sealed class Pen
    {
        private readonly System.Collections.Generic.List<Leg> _legs = new();
        private double _x, _y, _z, _hdg;      // heading: 0 = +x (north), 90 = +y (east)

        public Pen(double x, double y, double z, double headingDeg) { _x = x; _y = y; _z = z; _hdg = headingDeg * System.Math.PI / 180; }

        public (double x, double y, double z, double hdg) Node() => (_x, _y, _z, _hdg * 180 / System.Math.PI);
        public void MoveTo((double x, double y, double z, double hdg) n) { _x = n.x; _y = n.y; _z = n.z; _hdg = n.hdg * System.Math.PI / 180; }

        public void Straight(string id, double length, double drop, Kind kind = Kind.Bore,
                             double halfWidth = HalfWidthM, double crown = CrownM)
        {
            double nx = _x + System.Math.Cos(_hdg) * length, ny = _y + System.Math.Sin(_hdg) * length, nz = _z - drop;
            _legs.Add(new Leg(id, kind, _x, _y, _z, nx, ny, nz, halfWidth, crown));
            _x = nx; _y = ny; _z = nz;
        }

        /// <summary>A bend of <paramref name="degrees"/> (positive = right) at <paramref name="radius"/>,
        /// chopped into segments short enough that the arch follows the curve.</summary>
        public void Turn(string id, double radius, double degrees, double drop)
        {
            int n = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(degrees) / 10.0));
            double stepDeg = degrees / n, dz = drop / n;
            double segLen = 2 * radius * System.Math.Sin(System.Math.Abs(stepDeg) * System.Math.PI / 360);
            for (int i = 0; i < n; i++)
            {
                _hdg += stepDeg * System.Math.PI / 360;            // half the turn, then the chord, then half
                Straight($"{id}.{i}", segLen, dz);
                _hdg += stepDeg * System.Math.PI / 360;
            }
        }

        public Leg[] Done() => _legs.ToArray();
    }

    private static Leg[] Build(in WorldTerrain.Waterfall f)
    {
        double mouthY = MouthY(f), cx = CentreX(f), low = FloorM(f);

        // ---- the way out: the low bore, straight from its mouth into the mountain ----------------
        var pen = new Pen(cx, mouthY, low, 270);                     // heading west (−y)
        pen.Straight("exit.bore", BoreLengthM, 0);
        var boreEnd = pen.Node();

        // ---- the way in: the high mouth, 125 m above the low one and 110 m to the side -----------
        // Not directly above the low mouth: nothing in the maze may sit on top of anything else, or the
        // ground under a wheel stops having one answer. The offset buys something else too — at 430 m the
        // curtain hangs only ~19 m out, so a pocket 40 m deep entered THROUGH the water would be
        // unflyable. The way in is beside the falling water; the way out still comes through it.
        pen.MoveTo((cx + EntryOffsetXM, mouthY, EntryFloorM, 270));
        pen.Straight("A.entry", 120, 6);
        pen.Turn("A.bend", BendRadiusM, 90, 12);                     // now heading north (+x)
        pen.Straight("S0.glider", GliderStripM, 0, Kind.GliderStrip);
        var fork1 = pen.Node();

        // ---- FORK 1: straight on, or right into a dead end --------------------------------------
        pen.Straight("B1.north", 700, 10);
        pen.Turn("B1.bend", BendRadiusM, -90, 6);                    // left: heading west
        pen.Straight("B2.west", 500, 26);
        var fork2 = pen.Node();

        pen.MoveTo(fork1);
        pen.Turn("C.bend", BendRadiusM, -90, 4);                     // left: heading west
        pen.Straight("C.run", 120, 2);
        pen.Straight("C.strip1", StripM, 0, Kind.Strip);
        pen.Straight("C.pad", 60, 0, Kind.DeadEndPad);

        // ---- FORK 2: the slit, or the falling rock ----------------------------------------------
        // Both branches are symmetric doglegs — out, back twice as far, and in again — so they advance the
        // same distance along the same line and END AT THE SAME POINT. That is the rejoin, by construction
        // rather than by hand-fitting two sets of coordinates.
        // The two runs either side of the middle turn have to be the SAME length or the dogleg is not
        // offset-free and the branches do not meet again: 120 + the hazard + 120 out, the same back.
        const double legOut = 120, hazard = SlitLengthM, legBack = legOut * 2 + hazard;
        pen.MoveTo(fork2);
        pen.Turn("D.out", 400, 20, 2);
        pen.Straight("D.run1", legOut, 3);
        pen.Straight("D.slit", hazard, 1, Kind.Slit, SlitHalfWidthM, SlitCrownM);
        pen.Straight("D.run2", legOut, 3);
        pen.Turn("D.back", 400, -40, 4);
        pen.Straight("D.run3", legBack, 7);
        pen.Turn("D.in", 400, 20, 2);
        var fork3 = pen.Node();

        pen.MoveTo(fork2);
        pen.Turn("E.out", 400, -20, 2);
        pen.Straight("E.run1", legOut, 3);
        pen.Straight("E.rockfall", hazard, 1, Kind.Rockfall);
        pen.Straight("E.run2", legOut, 3);
        pen.Turn("E.back", 400, 40, 4);
        pen.Straight("E.run3", legBack, 7);
        pen.Turn("E.in", 400, -20, 2);

        // ---- the Hall: the long chamber, turned south so the ring closes ------------------------
        pen.MoveTo(fork3);
        pen.Turn("F.bend", BendRadiusM, -90, 4);                     // left: heading south (−x)
        pen.Straight("F.hall", StripM, 24, Kind.Strip);
        pen.Straight("F.out", 60, 2);
        var fork4 = pen.Node();

        // ---- FORK 3: the sheet of water on the way home, or the last dead end -------------------
        // The dead end turns AWAY from the way home — a blind leg that crossed the exit would put two floors
        // in the same place.
        pen.MoveTo(fork4);
        pen.Turn("H.bend", 300, 45, 3);
        pen.Straight("H.run", 160, 4);
        pen.Straight("H.strip3", StripM, 0, Kind.Strip);
        pen.Straight("H.pad", 60, 0, Kind.DeadEndPad);

        pen.MoveTo(fork4);
        pen.Straight("G.run1", 70, 2);
        pen.Straight("G.sheet", SlitLengthM, 1, Kind.WaterSheet);
        pen.Straight("G.run2", 70, 2);
        // Home: south until the bore's own line, then east onto the end of it. A left turn from south to
        // east ends 250 m further south, so stop that far short of the line and let the turn land on it.
        double southTo = boreEnd.x + BendRadiusM;
        var here = pen.Node();
        pen.Straight("K.south", System.Math.Max(80, here.x - southTo), 10);
        pen.Turn("K.bend", BendRadiusM, -90, 6);                     // left: heading east (+y)
        var beforeJoin = pen.Node();
        // The last leg lands on the end of the bore, at the bore's own floor: a junction, not a step.
        double run = boreEnd.y - beforeJoin.y;
        pen.Straight("K.join", System.Math.Max(60, run), beforeJoin.z - low);

        return pen.Done();
    }

    // ---- queries ----------------------------------------------------------------------------------

    private static bool Outside(in WorldTerrain.Waterfall f, double x, double y)
    {
        Legs(f);
        return x < _minX || x > _maxX || y < _minY || y > _maxY;
    }

    /// <summary>Is (x, y) inside the maze's plan? Cheap: a box reject, then the legs.</summary>
    public static bool InFootprint(in WorldTerrain.Waterfall f, double x, double y)
    {
        if (Outside(f, x, y)) return false;
        foreach (Leg l in _legs!) if (l.Locate(x, y, out _, out _)) return true;
        return false;
    }

    /// <summary>The floor under (x, y). Where legs overlap — at a junction — the lowest floor wins, so a
    /// junction is a continuous surface and never a lip to trip a wheel on.</summary>
    public static double FloorAt(in WorldTerrain.Waterfall f, double x, double y)
    {
        double floor = double.MaxValue;
        if (Outside(f, x, y)) return floor;
        foreach (Leg l in _legs!)
            if (l.Locate(x, y, out double s, out _)) floor = System.Math.Min(floor, l.FloorAt(s));
        return floor;
    }

    /// <summary>Ceiling height at (x, y): the most open arch over the point, so a junction is open overhead
    /// rather than pinched by whichever leg happens to be listed first.</summary>
    public static double CeilingAt(in WorldTerrain.Waterfall f, double x, double y)
    {
        double ceiling = double.MinValue;
        if (Outside(f, x, y)) return ceiling;
        foreach (Leg l in _legs!)
            if (l.Locate(x, y, out double s, out double lat))
                ceiling = System.Math.Max(ceiling, l.FloorAt(s) + l.ClearHeight(lat));
        return ceiling;
    }

    /// <summary>Pavement: the strips inside the maze, and the low bore's own runway.</summary>
    public static bool OnRunway(in WorldTerrain.Waterfall f, double x, double y)
    {
        if (Outside(f, x, y)) return false;
        foreach (Leg l in _legs!)
        {
            bool strip = l.Kind == Kind.Strip || l.Kind == Kind.GliderStrip || l.Kind == Kind.DeadEndPad;
            if (!strip && l.Id != "exit.bore") continue;
            if (!l.Locate(x, y, out double s, out double lat)) continue;
            if (lat > StripHalfWidthM) continue;
            if (l.Id == "exit.bore")
            {
                double along = s * l.Length;
                if (along < RunwayStartInM || along > RunwayStartInM + StripM) continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>Rock between the arch and the mountain above it. <paramref name="outer"/> is the terrain as
    /// it would be if the maze had never been bored.</summary>
    public static bool RoofAt(WorldTerrain t, in WorldTerrain.Waterfall f, double x, double y, out double ceiling, out double outer)
    {
        ceiling = CeilingAt(f, x, y);
        outer = t.HeightAt(x, y, false, withTunnel: false);
        return InFootprint(f, x, y) && outer > ceiling + MinRoofM;
    }

    /// <summary>Register the arch, and a wall across the end of every dead end.</summary>
    public static void RegisterSolids(WorldTerrain t)
    {
        var f = Fall(t);
        WorldSolids.Shapes.Add(new ArchSolid(t));
        foreach (Leg l in Legs(f))
        {
            // A box across the far end of each blind leg. Near its face the least-penetration axis is the
            // way back in, which is the push a pilot who ran out of tunnel needs.
            if (l.Kind != Kind.DeadEndPad && l.Id != "exit.bore") continue;
            double dx = l.X1 - l.X0, dy = l.Y1 - l.Y0, len = System.Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) continue;
            dx /= len; dy /= len;
            const double thick = 20;
            double cxw = l.X1 + dx * thick, cyw = l.Y1 + dy * thick;
            // Sized along the leg and across it, so the wall sits beyond the end and does not eat the pavement.
            double hx = System.Math.Abs(dx) * thick + System.Math.Abs(dy) * l.HalfWidth;
            double hy = System.Math.Abs(dy) * thick + System.Math.Abs(dx) * l.HalfWidth;
            WorldSolids.Boxes.Add(new WorldSolids.Box(cxw, cyw, hx, hy, l.Z1 - 5, l.Z1 + l.Crown + 10));
        }
    }

    /// <summary>
    /// The arches as one solid. Inside the maze's plan, rock is everything above the arch and below the
    /// mountain's own surface; the push is back toward the nearest bore's axis along the ellipse's normal, so
    /// a wing that catches the springing of an arch is pushed down AND inward rather than straight down.
    /// </summary>
    public sealed class ArchSolid : WorldSolids.IShape
    {
        private readonly WorldTerrain _t;
        public ArchSolid(WorldTerrain t) { _t = t; }

        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            normalNed = Vec3.Zero; depth = 0;
            var f = Fall(_t);
            if (Outside(f, x, y)) return false;

            // The arch that is most open over this point decides: inside any of them is open air.
            double bestDepth = double.MaxValue;
            double nx = 0, ny = 0, nz = 1;
            bool inside = false, hit = false;
            foreach (Leg l in _legs!)
            {
                if (!l.Locate(x, y, out double s, out double lat)) continue;
                inside = true;
                double floor = l.FloorAt(s), z = up - floor;
                if (z < 0) return false;                       // under the floor is the floor's business
                double s2 = lat * lat / (l.HalfWidth * l.HalfWidth) + z * z / (l.Crown * l.Crown);
                if (s2 < 1.0) return false;                    // in the clear under this arch
                double sc = System.Math.Sqrt(s2);
                double d = System.Math.Sqrt(lat * lat + z * z) * (1 - 1 / sc);
                if (d < bestDepth)
                {
                    bestDepth = d;
                    // Inward ACROSS the bore (in the leg's own lateral direction, whichever way it runs) and
                    // down: the lateral sign comes from the side of the centreline the point is on.
                    double dxl = l.X1 - l.X0, dyl = l.Y1 - l.Y0, len = System.Math.Sqrt(dxl * dxl + dyl * dyl);
                    double rx = len > 0 ? -dyl / len : 0, ry = len > 0 ? dxl / len : 0;   // unit right of travel
                    double px = l.X0 + s * dxl, py = l.Y0 + s * dyl;
                    double side = (x - px) * rx + (y - py) * ry;
                    double mag = lat / (l.HalfWidth * l.HalfWidth);
                    double sign = side >= 0 ? -1 : 1;                 // push back toward the centreline
                    nx = sign * rx * mag; ny = sign * ry * mag;
                    nz = z / (l.Crown * l.Crown);
                    hit = true;
                }
            }
            if (!inside || !hit) return false;
            if (!RoofAt(_t, f, x, y, out _, out double outer) || up > outer) return false;   // out the top of the mountain

            var push = new Vec3(nx, ny, nz);     // NED: +z is down, and nz is the downward push
            double plen = push.Length;
            if (plen < 1e-12) { normalNed = new Vec3(0, 0, 1); depth = 0.1; return true; }
            normalNed = push / plen;
            depth = bestDepth;
            return true;
        }
    }
}
