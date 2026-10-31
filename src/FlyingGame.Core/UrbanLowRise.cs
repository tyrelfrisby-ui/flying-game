using System.Collections.Generic;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The cities' low-rise infill as a solid: the houses, terraces, shops and warehouses the visual builder
/// fills the blocks with (UrbanFacades.RoofDistrict), about five hundred per city and none of them taller
/// than fourteen metres. An aeroplane that flies into one hits it.
///
/// The placement is NOT repeated here. The builder registers each building through <see cref="Add"/> as it
/// draws it, from the same foot and size that make the mesh, so the collider cannot drift from the roof the
/// pilot can see — that file is rewritten often, and a second copy of its layout in this one would be wrong
/// within the week. What lives here is the part that is physics: where a building may not stand, how a box
/// with a gabled roof is penetrated, and an index that keeps five hundred of them off the step's critical
/// path.
///
/// Sim frame throughout: x, y along the ground, <c>up</c> in metres. The builder converts from Unity's.
/// </summary>
public static class UrbanLowRise
{
    /// <summary>A building: a box from <see cref="Base"/> to the eave, with a gabled roof on top of it. The
    /// ridge runs the length of the building and the roof falls away to the eave on both sides; a flat-roofed
    /// building is one with <see cref="RidgeM"/> zero.</summary>
    public readonly struct Block
    {
        public readonly double Cx, Cy, Hx, Hy, Base, EaveM, RidgeM;
        /// <summary>The ridge line runs along x, so the roof falls away in y (and the gable half-width is Hy).</summary>
        public readonly bool RidgeAlongX;
        public Block(double cx, double cy, double hx, double hy, double bse, double eaveM, double ridgeM, bool ridgeAlongX)
        { Cx = cx; Cy = cy; Hx = hx; Hy = hy; Base = bse; EaveM = eaveM; RidgeM = ridgeM; RidgeAlongX = ridgeAlongX; }
        public double TopM => Base + EaveM + RidgeM;
        /// <summary>The roof height at a point, whether or not the point is inside the walls.</summary>
        public double RoofAt(double x, double y)
        {
            double a = RidgeAlongX ? System.Math.Abs(y - Cy) : System.Math.Abs(x - Cx);
            double half = RidgeAlongX ? Hy : Hx;
            return Base + EaveM + (half > 1e-9 ? RidgeM * System.Math.Max(0, 1 - a / half) : RidgeM);
        }
    }

    /// <summary>Ground a pilot has to be able to use: a pad, the floor under a ring, a final-approach
    /// corridor. Nothing is built inside one. A circle has <see cref="R"/> above zero; otherwise it is the
    /// rectangle x0..x1, y0..y1.</summary>
    public readonly struct KeepClear
    {
        public readonly string Why; public readonly double Cx, Cy, R, X0, Y0, X1, Y1;
        private KeepClear(string why, double cx, double cy, double r, double x0, double y0, double x1, double y1)
        { Why = why; Cx = cx; Cy = cy; R = r; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public static KeepClear Circle(string why, double cx, double cy, double r) => new(why, cx, cy, r, cx - r, cy - r, cx + r, cy + r);
        public static KeepClear Rect(string why, double x0, double y0, double x1, double y1) => new(why, 0, 0, 0, x0, y0, x1, y1);
        /// <summary>Does the footprint (centre, half-sizes) touch this?</summary>
        public bool Touches(double cx, double cy, double hx, double hy)
        {
            if (cx + hx < X0 || cx - hx > X1 || cy + hy < Y0 || cy - hy > Y1) return false;
            if (R <= 0) return true;
            // Nearest point of the footprint to the centre of the circle.
            double nx = System.Math.Max(cx - hx, System.Math.Min(Cx, cx + hx));
            double ny = System.Math.Max(cy - hy, System.Math.Min(Cy, cy + hy));
            return (nx - Cx) * (nx - Cx) + (ny - Cy) * (ny - Cy) <= R * R;
        }
    }

    private static readonly List<Block> _blocks = new();
    private static readonly List<KeepClear> _clear = new();
    public static IReadOnlyList<Block> Blocks => _blocks;
    public static IReadOnlyList<KeepClear> KeepClears => _clear;
    /// <summary>How many buildings the keep-clear rules turned away — a count the builder can log.</summary>
    public static int Refused { get; private set; }
    private static readonly Dictionary<string, int> _refusedBy = new();
    /// <summary>Which rule turned them away, so a rule that is quietly eating a district shows up.</summary>
    public static IReadOnlyDictionary<string, int> RefusedBy => _refusedBy;

    // ---- the index ------------------------------------------------------------------------------------
    // A uniform grid over the registered buildings, in the compressed-row form: cell c owns the block ids
    // _ids[_start[c] .. _start[c+1]). Built once, on the first query after a change; a building is 10-50 m
    // across and the cells are 64 m, so a query reads the four cells its point could reach into.
    private const double CellM = 64.0;
    private static bool _dirty = true;
    private static int _nx, _ny;
    private static double _x0, _y0, _minBase, _maxTop;
    private static int[] _start = System.Array.Empty<int>(), _ids = System.Array.Empty<int>();

    private static bool _staked;
    public static void Clear() { _blocks.Clear(); _clear.Clear(); _refusedBy.Clear(); Refused = 0; _dirty = true; _staked = false; }

    /// <summary>Stake out the ground no building may stand on, the first time anything asks to build. It is
    /// done HERE, from Core's own data, rather than as a side effect of registering the world's solids: the
    /// visual builder lays the plateau cities down BEFORE Landmarks.RegisterSolids runs, so a rule that
    /// waited for that call would arrive after half the houses were already standing.</summary>
    public static void Stake()
    {
        if (_staked) return;
        _staked = true;
        KeepAirportsClear();
        WorldTerrain? t = WorldTerrain.Active;
        if (t is null) return;
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
            if (WorldTerrain.Has(p, WorldTerrain.Feature.Town)) ValleyCity.At(p).KeepClear(t);
        FlyCity.Grid.KeepClear(t);
    }

    /// <summary>Register a building. Returns false (and builds nothing) where it would stand on ground a
    /// pilot needs: the builder may still draw it, but then the mesh and the sim disagree, so it should
    /// not.</summary>
    public static bool Add(double cx, double cy, double hx, double hy, double groundM, double eaveM, double ridgeM, bool ridgeAlongX)
    {
        if (hx <= 0 || hy <= 0 || eaveM + ridgeM <= 0) return false;
        Stake();
        foreach (KeepClear k in _clear)
            if (k.Touches(cx, cy, hx, hy))
            {
                Refused++;
                _refusedBy[k.Why] = _refusedBy.TryGetValue(k.Why, out int n) ? n + 1 : 1;
                return false;
            }
        // The walls run a little way into the ground, so a wheel at or just below the surface is inside the
        // house rather than under it.
        _blocks.Add(new Block(cx, cy, hx, hy, groundM - FootingM, eaveM + FootingM, ridgeM, ridgeAlongX));
        _dirty = true;
        return true;
    }

    public static void AddKeepClear(KeepClear k) { _clear.Add(k); _dirty = true; }

    /// <summary>The ground every pilot needs kept clear, whatever the visual builder would like to do with it.
    ///
    /// This is each airport's LANDING SURFACES and the finals off them, not its pad: a pad is 2,950 x 1,700 m
    /// of flattened field around 30 m of runway, and refusing the whole rectangle threw out a hundred and
    /// thirty-eight houses per Valley city that stood a kilometre from any strip. Which houses belong on an
    /// airfield is the visual builder's call; which ground an aeroplane lands on is not.
    ///
    /// Every strip is axis-aligned (headings 0 and 90), so each is a rectangle in world coordinates.</summary>
    public static void KeepAirportsClear()
    {
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            foreach (WorldTerrain.Strip st in WorldTerrain.AirportStrips)
            {
                bool alongX = System.Math.Abs(st.HeadingDeg % 180) < 45;
                double cx = a.X + st.Dx, cy = a.Y + st.Dy;
                double hAlong = st.Length / 2 + StripClearM, hAcross = st.Width / 2 + StripClearM;
                double hx = alongX ? hAlong : hAcross, hy = alongX ? hAcross : hAlong;
                AddKeepClear(KeepClear.Rect($"{a.Name} {st.Kind}", cx - hx, cy - hy, cx + hx, cy + hy));
                if (!WorldTerrain.IsPaved(st.Kind)) continue;   // a short field has no instrument-style final
                // A short final off each end, long enough to come down and roll out on.
                foreach (int end in new[] { -1, 1 })
                {
                    double a0 = end > 0 ? hAlong : -hAlong - ApproachLengthM, a1 = end > 0 ? hAlong + ApproachLengthM : -hAlong;
                    AddKeepClear(alongX
                        ? KeepClear.Rect($"{a.Name} {st.Kind} final {(end > 0 ? "north" : "south")}",
                            cx + a0, cy - ApproachHalfWidthM, cx + a1, cy + ApproachHalfWidthM)
                        : KeepClear.Rect($"{a.Name} {st.Kind} final {(end > 0 ? "east" : "west")}",
                            cx - ApproachHalfWidthM, cy + a0, cx + ApproachHalfWidthM, cy + a1));
                }
            }
    }

    /// <summary>The final an aeroplane actually needs clear off a threshold. Not an instrument approach: the
    /// whole world is 8.6 x 14.9 NM, and a four-kilometre corridor reaches across the combat city.</summary>
    public const double ApproachLengthM = 1500.0, ApproachHalfWidthM = 150.0;
    /// <summary>How far the walls are sunk below the ground they were placed on.</summary>
    public const double FootingM = 2.0;
    /// <summary>Clearance round the city's own landing pad, and under each ring: enough that the aeroplane
    /// that comes off one has somewhere to go.</summary>
    public const double PadClearM = 90.0, RingClearM = 40.0, PadApproachM = 600.0;
    /// <summary>How far off the edge of a runway, taxiway-width strip or threshold nothing may stand.</summary>
    public const double StripClearM = 120.0;

    private static UrbanSolids? _shape;
    /// <summary>Hang the low-rise on <see cref="WorldSolids"/> (idempotent per shape list).</summary>
    public static void RegisterSolids()
    {
        _shape ??= new UrbanSolids();
        if (!WorldSolids.Shapes.Contains(_shape)) WorldSolids.Shapes.Add(_shape);
    }

    // ---- collision ------------------------------------------------------------------------------------

    private static void Index()
    {
        if (!_dirty) return;
        _dirty = false;
        if (_blocks.Count == 0) { _nx = _ny = 0; _minBase = 1; _maxTop = 0; return; }
        double x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
        _x0 = double.PositiveInfinity; _y0 = double.PositiveInfinity;
        _minBase = double.PositiveInfinity; _maxTop = double.NegativeInfinity;
        foreach (Block b in _blocks)
        {
            if (b.Cx - b.Hx < _x0) _x0 = b.Cx - b.Hx;
            if (b.Cy - b.Hy < _y0) _y0 = b.Cy - b.Hy;
            if (b.Cx + b.Hx > x1) x1 = b.Cx + b.Hx;
            if (b.Cy + b.Hy > y1) y1 = b.Cy + b.Hy;
            if (b.Base < _minBase) _minBase = b.Base;
            if (b.TopM > _maxTop) _maxTop = b.TopM;
        }
        _nx = (int)((x1 - _x0) / CellM) + 1;
        _ny = (int)((y1 - _y0) / CellM) + 1;
        var count = new int[_nx * _ny + 1];
        void Span(Block b, out int i0, out int i1, out int j0, out int j1)
        {
            i0 = Clamp((int)((b.Cx - b.Hx - _x0) / CellM), _nx);
            i1 = Clamp((int)((b.Cx + b.Hx - _x0) / CellM), _nx);
            j0 = Clamp((int)((b.Cy - b.Hy - _y0) / CellM), _ny);
            j1 = Clamp((int)((b.Cy + b.Hy - _y0) / CellM), _ny);
        }
        foreach (Block b in _blocks)
        {
            Span(b, out int i0, out int i1, out int j0, out int j1);
            for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) count[i * _ny + j + 1]++;
        }
        for (int c = 1; c < count.Length; c++) count[c] += count[c - 1];
        _start = count;
        _ids = new int[_start[_nx * _ny]];
        var fill = new int[_nx * _ny];
        for (int k = 0; k < _blocks.Count; k++)
        {
            Span(_blocks[k], out int i0, out int i1, out int j0, out int j1);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                { int c = i * _ny + j; _ids[_start[c] + fill[c]++] = k; }
        }
    }

    private static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

    /// <summary>The push out of whatever building contains the point, along the least-penetration face, and
    /// how deep it is; null outside every one of them.</summary>
    public static (Vec3 normalNed, double depth)? Penetration(double x, double y, double up)
    {
        Index();
        if (_nx == 0 || up > _maxTop || up < _minBase) return null;
        int i = (int)((x - _x0) / CellM), j = (int)((y - _y0) / CellM);
        if (i < 0 || j < 0 || i >= _nx || j >= _ny) return null;
        int c = i * _ny + j;
        for (int k = _start[c]; k < _start[c + 1]; k++)
        {
            Block b = _blocks[_ids[k]];
            double dx = x - b.Cx, dy = y - b.Cy;
            if (System.Math.Abs(dx) > b.Hx || System.Math.Abs(dy) > b.Hy || up < b.Base) continue;
            double roof = b.RoofAt(x, y);
            if (up > roof) continue;
            // Out through a wall or off the roof, whichever is nearer. Never down through the floor: a house
            // stands ON the ground, so its underside is not a face anything can be pushed out of -- taking
            // the least-penetration face blindly (as a free-standing box does) drives an aeroplane that
            // clipped a wall a metre up into the earth.
            double px = b.Hx - System.Math.Abs(dx), py = b.Hy - System.Math.Abs(dy);
            double pTop = roof - up;
            double m = System.Math.Min(System.Math.Min(px, py), pTop);
            if (m == pTop) return (RoofNormal(b, dx, dy), pTop);
            if (m == px) return (new Vec3(System.Math.Sign(dx), 0, 0), px);
            return (new Vec3(0, System.Math.Sign(dy), 0), py);
        }
        return null;
    }

    /// <summary>The outward normal of the roof plane the point is under (NED, z down): straight up on a flat
    /// roof, tilted off the ridge on a gable, so a wing that clips one is thrown off the slope.</summary>
    private static Vec3 RoofNormal(Block b, double dx, double dy)
    {
        double half = b.RidgeAlongX ? b.Hy : b.Hx;
        if (b.RidgeM <= 0 || half <= 1e-9) return new Vec3(0, 0, -1);
        double lateral = b.RidgeAlongX ? dy : dx;
        double s = lateral >= 0 ? 1 : -1;
        double slope = b.RidgeM / half;
        double n = System.Math.Sqrt(1 + slope * slope);
        return b.RidgeAlongX ? new Vec3(0, s * slope / n, -1 / n) : new Vec3(s * slope / n, 0, -1 / n);
    }

    private sealed class UrbanSolids : WorldSolids.IShape
    {
        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            var hit = Penetration(x, y, up);
            if (hit is null) { normalNed = Vec3.Zero; depth = 0; return false; }
            normalNed = hit.Value.normalNed; depth = hit.Value.depth;
            return true;
        }
    }
}
