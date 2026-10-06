using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

// "A skate park for airplanes" (owner 2026-10-03): the canyon lake behind a dam (Lake Powell — sandstone spires to slalom,
// natural arches to fly under), and a city built for flying (wide avenues, sky-bridges, crossing fountain arcs, the Mall
// with its 3,000 ft reflecting pond and grass between the Capitol and the Washington Monument, a numbered rooftop ring
// course, a giant spinning ring to time, and a 500 ft round landing pad cantilevered off a tower).
// Frame: sim x = north, y = east, heights up (m above sea level).

/// <summary>World time for moving scenery (the spinning ring, the carrier): set once a frame by the game, by tests directly.</summary>
public static class WorldClock { public static double TimeS; }

/// <summary>Raised landable surfaces (the cantilevered pad; later the carrier): wheels and hard points stand on them when
/// they are at or above the deck — a point UNDER the deck is not lifted onto it.</summary>
public static class WorldDecks
{
    public interface IDeck { bool TopAt(double x, double y, out double topM); }
    public static readonly List<IDeck> All = new();

    /// <summary>The highest deck top under (x, y) that a point at height <paramref name="up"/> is standing on or above.</summary>
    public static bool DeckUnder(double x, double y, double up, out double topM)
    {
        topM = double.NegativeInfinity; bool any = false;
        foreach (IDeck d in All)
            if (d.TopAt(x, y, out double t) && up >= t - 2.5 && t > topM) { topM = t; any = true; }
        return any;
    }

    public sealed class Circle : IDeck
    {
        public readonly double Cx, Cy, R, Top;
        public Circle(double cx, double cy, double r, double top) { Cx = cx; Cy = cy; R = r; Top = top; }
        public bool TopAt(double x, double y, out double topM) { topM = Top; return (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) <= R * R; }
    }
}

/// <summary>A sandstone spire / butte standing in the lake: a tapering column (flat top) — solid.</summary>
public readonly struct Spire
{
    public readonly double X, Y, BaseR, TopR, BaseM, TopM;
    public Spire(double x, double y, double baseR, double topR, double baseM, double topM) { X = x; Y = y; BaseR = baseR; TopR = topR; BaseM = baseM; TopM = topM; }
    public double RadiusAt(double up) => BaseR + (TopR - BaseR) * System.Math.Clamp((up - BaseM) / (TopM - BaseM), 0, 1);
}

/// <summary>A natural arch: a rock band springing from A and B (x, y) at <c>FootM</c>, its underside rising to
/// <c>FootM + OpeningM</c> at mid-span, <c>ThickM</c> thick, <c>WidthM</c> wide.</summary>
public readonly struct NaturalArch
{
    public readonly double Ax, Ay, Bx, By, FootM, OpeningM, ThickM, WidthM;
    public NaturalArch(double ax, double ay, double bx, double by, double foot, double opening, double thick, double width)
    { Ax = ax; Ay = ay; Bx = bx; By = by; FootM = foot; OpeningM = opening; ThickM = thick; WidthM = width; }
    public double HalfSpan => 0.5 * System.Math.Sqrt((Bx - Ax) * (Bx - Ax) + (By - Ay) * (By - Ay));
    /// <summary>Along-span coordinate s (−half..half, 0 mid-span) and across c of a point.</summary>
    public (double s, double c) Local(double x, double y)
    {
        double mx = 0.5 * (Ax + Bx), my = 0.5 * (Ay + By), L = 2 * HalfSpan;
        double ux = (Bx - Ax) / L, uy = (By - Ay) / L;
        double dx = x - mx, dy = y - my;
        return (dx * ux + dy * uy, -dx * uy + dy * ux);
    }
    public double InnerAt(double s) { double u = s / HalfSpan; return FootM + OpeningM * (1 - u * u); }
    public double OuterAt(double s) { double u = s / HalfSpan; return InnerAt(s) + ThickM * (1 + 1.2 * u * u); }
}

/// <summary>The canyon lake (Lake Powell): the gorge below the Valley's big waterfall flooded behind a concrete arch dam,
/// the canyon widened into a reservoir with sandstone walls ~55 m above the water, slalom lines of spires and natural
/// arches. Landable water ~2.4 km long.</summary>
public static class CanyonLake
{
    public const double Y0 = -1250, DamY = 1350, SurfaceM = 95.0, WallM = 12.0;
    public const double TwinArchY = 520;
    public const double DamBowM = 45, DamCrestM = SurfaceM + 3, DamCrestThickM = 8, DamBaseThickM = 45;

    private static double S(double t) { t = System.Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>Half-width of the flooded canyon at y (narrowing to the gorge at both ends).</summary>
    public static double HalfWidthAt(double y)
    {
        if (y < Y0 || y > DamY + 60) return 0;
        double w = 300 + 60 * System.Math.Sin(y / 380.0) + 40 * System.Math.Sin(y / 170.0 + 1.3);
        double g = WorldTerrain.GorgeHalfWidthAt(y) - 20;
        double t = System.Math.Min((y - Y0) / 350, (DamY - y) / 320);
        return g + (System.Math.Min(w, 380) - g) * S(t);
    }

    /// <summary>The dam's centreline y at x (bowed UPSTREAM — an arch dam leans on the canyon walls).</summary>
    public static double DamCentreY(double x)
    {
        double hw = DamHalfLength, dx = (x - WorldTerrain.RiverCentreX(DamY)) / hw;
        return DamY - DamBowM * (1 - System.Math.Min(1, dx * dx));
    }
    public static double DamHalfLength => WorldTerrain.GorgeHalfWidthAt(DamY) + 10;

    /// <summary>Lake floor carve (min with the ground): vertical sandstone walls into the water, a deep floor.</summary>
    public static double CarveAt(double x, double y, double h)
    {
        if (y < Y0 || y > DamCentreY(x)) return h;
        double W = HalfWidthAt(y);
        double d = System.Math.Abs(x - WorldTerrain.RiverCentreX(y));
        if (d >= W) return h;
        double wallFoot = SurfaceM - 10;
        if (d > W - WallM) return System.Math.Min(h, wallFoot + (h - wallFoot) * System.Math.Pow((d - (W - WallM)) / WallM, 0.35));
        double floor = wallFoot - 48 * S((W - WallM - d) / 140);
        return System.Math.Min(h, floor);
    }

    public static bool OnWater(double x, double y) =>
        y > Y0 && y < DamCentreY(x) && System.Math.Abs(x - WorldTerrain.RiverCentreX(y)) < HalfWidthAt(y) - 2;

    public static double CentreX(double y) => WorldTerrain.RiverCentreX(y);

    // ---- spires: slalom lines down the lake + flat-topped buttes near the walls ----
    public static readonly Spire[] Spires = BuildSpires();
    private static Spire[] BuildSpires()
    {
        var l = new List<Spire>();
        double floor = SurfaceM - 50;
        // The slalom: alternate sides of the centreline every 190 m.
        int k = 0;
        for (double y = -900; y <= 950; y += 190, k++)
        {
            if (System.Math.Abs(y - TwinArchY) < 160) continue;   // nothing pokes up through the twin arches
            double off = (k % 2 == 0 ? -1 : 1) * 0.36 * HalfWidthAt(y);
            double top = WorldTerrain.DatumM + 25 + 35 * ((k * 37) % 5) / 4.0;
            l.Add(new Spire(CentreX(y) + off, y, 22, 9 + (k % 3) * 2, floor, top));
        }
        // Buttes: wide, flat-topped, near the walls, some below the rim (fly over), some above.
        (double y, double side, double r, double above)[] buttes = { (-700, 0.72, 45, -15), (-250, -0.7, 55, 35), (300, 0.68, 40, 50), (700, -0.66, 50, -10), (1050, 0.6, 35, 20) };
        foreach (var b in buttes)
            l.Add(new Spire(CentreX(b.y) + b.side * HalfWidthAt(b.y), b.y, b.r * 1.15, b.r, floor, WorldTerrain.DatumM + b.above));
        return l.ToArray();
    }

    // ---- natural arches (fly under them) ----
    public static readonly NaturalArch[] Arches = BuildArches();
    private static NaturalArch[] BuildArches()
    {
        var a = new List<NaturalArch>();
        double y1 = -450, W1 = HalfWidthAt(y1);
        // "Rainbow Bridge": from the west wall out to a free-standing pillar — 120 m span, 80 m clear over the water.
        a.Add(new NaturalArch(CentreX(y1) - W1 + 5, y1, CentreX(y1) - W1 + 125, y1 + 30, SurfaceM - 5, 85, 16, 20));
        // A low, wide arch right across the narrows below the waterfall — 65 m clear.
        double y2 = Y0 + 130, W2 = HalfWidthAt(y2);
        a.Add(new NaturalArch(CentreX(y2) - W2 - 5, y2, CentreX(y2) + W2 + 5, y2, SurfaceM - 5, 65, 14, 26));
        // Twin arches sharing a pillar, on the east side (the centreline stays clear for landing).
        double y3 = TwinArchY, c3 = CentreX(y3);
        a.Add(new NaturalArch(c3 + 90, y3 - 40, c3 + 190, y3, SurfaceM - 5, 70, 12, 18));
        a.Add(new NaturalArch(c3 + 190, y3, c3 + 290, y3 + 40, SurfaceM - 5, 70, 12, 18));
        return a.ToArray();
    }

    /// <summary>Spires (the twin arches' shared pillar + Rainbow Bridge's pillar too), arches, the dam — as solids.</summary>
    public static void RegisterSolids()
    {
        foreach (Spire s in Spires) WorldSolids.Shapes.Add(new SpireSolid(s));
        foreach (Spire s in ArchPillars()) WorldSolids.Shapes.Add(new SpireSolid(s));
        foreach (NaturalArch n in Arches) WorldSolids.Shapes.Add(new ArchSolid(n));
        WorldSolids.Shapes.Add(new DamSolid());
    }

    /// <summary>Pillars under arch ends that stand in the water.</summary>
    public static IEnumerable<Spire> ArchPillars()
    {
        foreach (NaturalArch n in Arches)
            foreach ((double x, double y) in new[] { (n.Ax, n.Ay), (n.Bx, n.By) })
                if (OnWater(x, y) && System.Math.Abs(x - CentreX(y)) < HalfWidthAt(y) - 30)
                    yield return new Spire(x, y, 18, 14, SurfaceM - 50, n.FootM + n.OpeningM + 6);
    }

    public sealed class SpireSolid : WorldSolids.IShape
    {
        private readonly Spire _s; public SpireSolid(Spire s) { _s = s; }
        public bool Penetrate(double x, double y, double up, out Vec3 n, out double depth)
        {
            n = Vec3.Zero; depth = 0;
            if (up > _s.TopM || up < _s.BaseM) return false;
            double dx = x - _s.X, dy = y - _s.Y, r = System.Math.Sqrt(dx * dx + dy * dy), R = _s.RadiusAt(up);
            if (r >= R) return false;
            double side = R - r, top = _s.TopM - up;
            if (top < side) { n = new Vec3(0, 0, -1); depth = top; }
            else { n = r > 1e-6 ? new Vec3(dx / r, dy / r, 0) : new Vec3(1, 0, 0); depth = side; }
            return true;
        }
    }

    public sealed class ArchSolid : WorldSolids.IShape
    {
        private readonly NaturalArch _a; public ArchSolid(NaturalArch a) { _a = a; }
        public bool Penetrate(double x, double y, double up, out Vec3 n, out double depth)
        {
            n = Vec3.Zero; depth = 0;
            var (s, c) = _a.Local(x, y);
            if (System.Math.Abs(s) > _a.HalfSpan + 8 || System.Math.Abs(c) > _a.WidthM / 2) return false;
            double inner = _a.InnerAt(System.Math.Clamp(s, -_a.HalfSpan, _a.HalfSpan)), outer = _a.OuterAt(System.Math.Clamp(s, -_a.HalfSpan, _a.HalfSpan));
            if (up < inner || up > outer) return false;
            double pDown = up - inner, pUp = outer - up, pSide = _a.WidthM / 2 - System.Math.Abs(c);
            double m = System.Math.Min(pSide, System.Math.Min(pDown, pUp));
            if (m == pDown) { n = new Vec3(0, 0, 1); depth = pDown; }
            else if (m == pUp) { n = new Vec3(0, 0, -1); depth = pUp; }
            else
            {
                double L = 2 * _a.HalfSpan, ux = (_a.Bx - _a.Ax) / L, uy = (_a.By - _a.Ay) / L, sg = System.Math.Sign(c);
                n = new Vec3(-uy * sg, ux * sg, 0); depth = pSide;
            }
            return true;
        }
    }

    public sealed class DamSolid : WorldSolids.IShape
    {
        public bool Penetrate(double x, double y, double up, out Vec3 n, out double depth)
        {
            n = Vec3.Zero; depth = 0;
            double cx = WorldTerrain.RiverCentreX(DamY);
            if (System.Math.Abs(x - cx) > DamHalfLength + 15 || up > DamCrestM || up < -10) return false;
            double frac = System.Math.Clamp((DamCrestM - up) / 90, 0, 1);
            double half = 0.5 * (DamCrestThickM + (DamBaseThickM - DamCrestThickM) * frac);
            double d = y - DamCentreY(x);
            if (System.Math.Abs(d) > half) return false;
            double pTop = DamCrestM - up, pFace = half - System.Math.Abs(d);
            if (pTop < pFace) { n = new Vec3(0, 0, -1); depth = pTop; }
            else { n = new Vec3(0, System.Math.Sign(d), 0); depth = pFace; }
            return true;
        }
    }
}

/// <summary>
/// The city built for flying, as a GRID that can be placed anywhere (owner 2026-10-05: "make the city near the initial
/// airport identical to the one you built in the combat zone"): 7 × 16 towers on a 170 m pitch, the same heights, sizes and
/// sky-bridges, the fountain plaza with its two crossing water cannons. Laid out in grid space (u along the 7 columns, v
/// along the 16 rows) and mapped onto the world — straight (the combat city) or turned 90° (the Valley's).
/// </summary>
public sealed class CityGrid
{
    public const double Pitch = 170;
    public const int Cols = 7, Rows = 16;
    public const double PlazaU0 = 170, PlazaU1 = 510, PlazaV0 = 1750, PlazaV1 = 2090, FountainCrossM = 110;
    public readonly double X0, Y0;
    public readonly bool Rotated;
    public readonly (int i, int j)? PadLot;
    public CityGrid(double x0, double y0, bool rotated, (int i, int j)? padLot) { X0 = x0; Y0 = y0; Rotated = rotated; PadLot = padLot; }

    public (double x, double y) Map(double u, double v) => Rotated ? (X0 + v, Y0 + u) : (X0 + u, Y0 + v);
    public (double u, double v) Unmap(double x, double y) => Rotated ? (y - Y0, x - X0) : (x - X0, y - Y0);
    public static double CentreU => Pitch * (Cols - 1) / 2.0;
    public static double CentreV => Pitch * (Rows - 1) / 2.0;
    public (double x, double y) Centre => Map(CentreU, CentreV);
    /// <summary>World rectangle the grid covers (tower centres ± margin).</summary>
    public (double x0, double y0, double x1, double y1) Extent(double margin)
    {
        var (ax, ay) = Map(-margin, -margin); var (bx, by) = Map(Pitch * (Cols - 1) + margin, Pitch * (Rows - 1) + margin);
        return (Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
    }
    public bool InPlaza(double x, double y) { var (u, v) = Unmap(x, y); return u > PlazaU0 - 60 && u < PlazaU1 + 60 && v > PlazaV0 - 60 && v < PlazaV1 + 60; }
    public (double x0, double y0, double x1, double y1) PlazaRect
    {
        get { var (ax, ay) = Map(PlazaU0, PlazaV0); var (bx, by) = Map(PlazaU1, PlazaV1); return (Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by)); }
    }
    /// <summary>The two jets: from one corner nozzle to the diagonally opposite basin.</summary>
    public ((double x, double y) from, (double x, double y) to)[] Jets => new[]
    {
        (Map(PlazaU0 + 20, PlazaV0 + 20), Map(PlazaU1 - 20, PlazaV1 - 20)),
        (Map(PlazaU0 + 20, PlazaV1 - 20), Map(PlazaU1 - 20, PlazaV0 + 20)),
    };

    private List<FlyCity.Tower>? _towers;
    public List<FlyCity.Tower> Towers() => _towers ??= BuildTowers();
    private List<FlyCity.Tower> BuildTowers()
    {
        var list = new List<FlyCity.Tower>();
        for (int i = 0; i < Cols; i++)
            for (int j = 0; j < Rows; j++)
            {
                double u = i * Pitch, v = j * Pitch;
                var (cx, cy) = Map(u, v);
                uint h = (uint)(i * 2654435761u) ^ (uint)(j * 40503u) ^ 0x9e3779b9u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
                double R(int k) => ((h >> (k * 5)) & 31) / 31.0;
                bool pad = PadLot.HasValue && (i, j) == PadLot.Value;
                if (!pad && (InPlaza(cx, cy) || R(4) < 0.16)) continue;                                                 // the plaza + open lots
                if (!pad && PadLot.HasValue && j == Rows - 1 && Math.Abs(i - PadLot.Value.i) <= 1) continue;          // the pad's approach
                double r = Math.Sqrt((u - CentreU) * (u - CentreU) + 0.45 * (v - CentreV) * (v - CentreV));
                double height = r < 300 ? 190 + 110 * R(0) : r < 600 ? 110 + 90 * R(0) : 60 + 60 * R(0);
                double hu = 18 + 12 * R(1), hv = 18 + 12 * R(2);
                if (pad) { height = FlyCity.PadTowerHeightM; hu = 28; hv = 28; }
                double hx = Rotated ? hv : hu, hy = Rotated ? hu : hv;
                list.Add(new FlyCity.Tower(i, j, cx, cy, hx, hy, height, (int)(R(3) * 4)));
            }
        // The tallest stands out (in the combat city it carries the spinning ring).
        int best = 0;
        for (int k = 1; k < list.Count; k++) if (list[k].HeightM > list[best].HeightM && !(PadLot.HasValue && (list[k].I, list[k].J) == PadLot.Value)) best = k;
        var t = list[best];
        list[best] = new FlyCity.Tower(t.I, t.J, t.Cx, t.Cy, 30, 30, 340, t.Style);
        return list;
    }

    /// <summary>Sky-bridges: between column neighbours both ≥ 130 m (every other pair), at 55 % of the lower one.</summary>
    public List<(FlyCity.Tower a, FlyCity.Tower b, double heightM)> SkyBridges()
    {
        var res = new List<(FlyCity.Tower, FlyCity.Tower, double)>();
        var map = new Dictionary<(int, int), FlyCity.Tower>();
        foreach (FlyCity.Tower t in Towers()) map[(t.I, t.J)] = t;
        foreach (FlyCity.Tower t in Towers())
            if (map.TryGetValue((t.I + 1, t.J), out FlyCity.Tower n) && t.HeightM >= 130 && n.HeightM >= 130 && ((t.I * 7 + t.J * 3) % 2 == 0))
                res.Add((t, n, 0.55 * Math.Min(t.HeightM, n.HeightM)));
        return res;
    }

    /// <summary>A sky-bridge's box: centre, half extents in x and y (8 m deep).</summary>
    public static (double cx, double cy, double hx, double hy) BridgeBox(FlyCity.Tower a, FlyCity.Tower b)
    {
        double cx = 0.5 * (a.Cx + b.Cx), cy = 0.5 * (a.Cy + b.Cy);
        bool alongX = Math.Abs(b.Cx - a.Cx) > Math.Abs(b.Cy - a.Cy);
        return alongX ? (cx, cy, 0.5 * Math.Abs(b.Cx - a.Cx), 6) : (cx, cy, 6, 0.5 * Math.Abs(b.Cy - a.Cy));
    }

    /// <summary>The towers and sky-bridges as solids (airframe contact).</summary>
    public void RegisterSolids(WorldTerrain t)
    {
        foreach (FlyCity.Tower tw in Towers())
        {
            double g = t.HeightAt(tw.Cx, tw.Cy);
            WorldSolids.Boxes.Add(new WorldSolids.Box(tw.Cx, tw.Cy, tw.Hx, tw.Hy, g - 2, g + tw.HeightM));
        }
        foreach (var (a, b, h) in SkyBridges())
        {
            double g = t.HeightAt(a.Cx, a.Cy);
            var (cx, cy, hx, hy) = BridgeBox(a, b);
            WorldSolids.Boxes.Add(new WorldSolids.Box(cx, cy, hx, hy, g + h, g + h + 8));
        }
    }
}

/// <summary>The Valley's city (owner 2026-10-05): the combat city's grid turned 90° on the town's old site south of the field,
/// east of the final-approach line — one on every plateau, like the other Valley landmarks.</summary>
public static class ValleyCity
{
    public const double X0 = -3330, Y0 = 600;
    private static readonly Dictionary<int, CityGrid> _at = new();
    public static CityGrid At(int p)
    {
        if (!_at.TryGetValue(p, out CityGrid? g)) _at[p] = g = new CityGrid(X0, Y0 + WorldTerrain.PlateauDy(p), true, null);
        return g;
    }
}

/// <summary>The city, rebuilt for flying (owner): towers on a generous 170 m grid (100+ m avenues), sky-bridges between
/// neighbours, the fountain plaza, the Mall, the rooftop ring course, the spinning ring, the cantilevered pad.</summary>
public static class FlyCity
{
    public const double X0 = 3330, Y0 = 2350, Pitch = 170;
    public const int Cols = 7, Rows = 16;
    public static double CentreX => X0 + Pitch * (Cols - 1) / 2.0;
    public static double CentreY => Y0 + Pitch * (Rows - 1) / 2.0;

    // ---- the fountain plaza (two water cannons whose arcs cross ~110 m up) ----
    public const double PlazaX0 = 3500, PlazaX1 = 3840, PlazaY0 = 4100, PlazaY1 = 4440, FountainCrossM = 110;
    public static bool InPlaza(double x, double y) => x > PlazaX0 - 60 && x < PlazaX1 + 60 && y > PlazaY0 - 60 && y < PlazaY1 + 60;
    /// <summary>The two jets: from one corner nozzle to the diagonally opposite basin.</summary>
    public static ((double x, double y) from, (double x, double y) to)[] Jets =>
        new[] { ((PlazaX0 + 20, PlazaY0 + 20), (PlazaX1 - 20, PlazaY1 - 20)), ((PlazaX0 + 20, PlazaY1 - 20), (PlazaX1 - 20, PlazaY0 + 20)) };

    // ---- the cantilevered pad (500 ft circle) off the east-most tower, out over the sea cliff ----
    public const double PadRadiusM = 76.2, PadTowerHeightM = 300, PadDeckM = 230;
    public static (int i, int j) PadLot => (3, Rows - 1);

    public readonly struct Tower
    {
        public readonly int I, J; public readonly double Cx, Cy, Hx, Hy, HeightM; public readonly int Style;
        public Tower(int i, int j, double cx, double cy, double hx, double hy, double h, int style) { I = i; J = j; Cx = cx; Cy = cy; Hx = hx; Hy = hy; HeightM = h; Style = style; }
    }

    /// <summary>The grid itself (shared with the Valley's copy): straight, with the pad lot.</summary>
    public static readonly CityGrid Grid = new(X0, Y0, false, PadLot);
    public static List<Tower> Towers() => Grid.Towers();

    public static Tower Tallest { get { Tower b = Towers()[0]; foreach (Tower t in Towers()) if (t.HeightM > b.HeightM) b = t; return b; } }
    public static Tower PadTower { get { foreach (Tower t in Towers()) if ((t.I, t.J) == PadLot) return t; return Towers()[0]; } }
    public static (double x, double y) PadCentre { get { Tower t = PadTower; return (t.Cx, t.Cy + t.Hy + PadRadiusM - 8); } }   // the disc's inner edge keyed 8 m into the tower

    public static List<(Tower a, Tower b, double heightM)> SkyBridges() => Grid.SkyBridges();

    // ---- the rooftop ring course: five numbered rings on five towers, a loop ----
    public const double RingRadiusM = 22, RingTubeM = 1.6, RingAboveRoofM = 32;
    public static List<(Tower t, double headingDeg)> RooftopRings()
    {
        // Five towers 190–300 m tall in a ring round downtown, flown in order 1..5; each ring faces the next.
        var picks = new List<Tower>();
        double[] angles = { 0, 72, 144, 216, 288 };
        foreach (double a in angles)
        {
            double tx = CentreX + 420 * System.Math.Cos(a * System.Math.PI / 180), ty = CentreY + 700 * System.Math.Sin(a * System.Math.PI / 180);
            Tower best = default; double bd = double.MaxValue;
            foreach (Tower t in Towers())
            {
                if (t.HeightM < 150 || (t.I, t.J) == PadLot || t.HeightM >= 330) continue;
                double d = (t.Cx - tx) * (t.Cx - tx) + (t.Cy - ty) * (t.Cy - ty);
                if (d < bd) { bd = d; best = t; }
            }
            picks.Add(best);
        }
        var res = new List<(Tower, double)>();
        for (int k = 0; k < picks.Count; k++)
        {
            Tower a = picks[k], b = picks[(k + 1) % picks.Count];
            res.Add((a, System.Math.Atan2(b.Cy - a.Cy, b.Cx - a.Cx) * 180 / System.Math.PI));
        }
        return res;
    }

    // ---- the giant spinning ring on the tallest tower: spins about its vertical diameter ----
    public const double SpinRingRadiusM = 45, SpinRingTubeM = 3.5, SpinPeriodS = 10, SpinPostM = 14;
    public static (double x, double y, double centreUp) SpinRingCentre { get { Tower t = Tallest; return (t.Cx, t.Cy, t.HeightM + SpinPostM + SpinRingRadiusM); } }
    /// <summary>The ring's yaw (its normal's heading) at time t.</summary>
    public static double SpinYawRad(double t) => 2 * System.Math.PI * (t / SpinPeriodS);

    /// <summary>Torus around (cx, cy, cz) in the vertical plane whose NORMAL has heading yaw; R major, r tube.</summary>
    public sealed class TorusSolid : WorldSolids.IShape
    {
        private readonly double _cx, _cy, _cz, _R, _r; private readonly double? _fixedYaw;
        public TorusSolid(double cx, double cy, double cz, double R, double r, double? fixedYawRad) { _cx = cx; _cy = cy; _cz = cz; _R = R; _r = r; _fixedYaw = fixedYawRad; }
        public bool Penetrate(double x, double y, double up, out Vec3 n, out double depth)
        {
            n = Vec3.Zero; depth = 0;
            double dx = x - _cx, dy = y - _cy, dz = up - _cz;
            if (dx * dx + dy * dy + dz * dz > (_R + _r) * (_R + _r)) return false;
            double yaw = _fixedYaw ?? SpinYawRad(WorldClock.TimeS);
            double nx = System.Math.Cos(yaw), ny = System.Math.Sin(yaw);      // ring normal (horizontal)
            double along = dx * nx + dy * ny;                                  // distance out of the ring's plane
            double px = dx - along * nx, py = dy - along * ny;                 // in-plane offset (horizontal part)
            double inPlane = System.Math.Sqrt(px * px + py * py + dz * dz);
            if (inPlane < 1e-6) return false;
            double radial = inPlane - _R;                                      // from the tube's centre circle, in-plane
            double dist = System.Math.Sqrt(radial * radial + along * along);
            if (dist >= _r) return false;
            depth = _r - dist;
            // Outward normal (world NED: z down): from the nearest point on the centre circle to the point.
            double cxp = px / inPlane * _R, cyp = py / inPlane * _R, czp = dz / inPlane * _R;
            double ox = dx - cxp, oy = dy - cyp, oz = dz - czp, ol = System.Math.Sqrt(ox * ox + oy * oy + oz * oz);
            n = ol > 1e-6 ? new Vec3(ox / ol, oy / ol, -oz / ol) : new Vec3(0, 0, -1);
            return true;
        }
    }

    public static void RegisterSolids(WorldTerrain t)
    {
        Grid.RegisterSolids(t);
        double gp = t.HeightAt(PadTower.Cx, PadTower.Cy);
        var (px, py) = PadCentre;
        double top = gp + PadDeckM;
        // The pad: a disc slab (squares approximating the circle) + its deck for wheels.
        for (double ox = -PadRadiusM; ox < PadRadiusM; ox += 12)
            for (double oy = -PadRadiusM; oy < PadRadiusM; oy += 12)
                if ((ox + 6) * (ox + 6) + (oy + 6) * (oy + 6) < PadRadiusM * PadRadiusM)
                    WorldSolids.Boxes.Add(new WorldSolids.Box(px + ox + 6, py + oy + 6, 6, 6, top - 4, top));
        WorldDecks.All.Add(new WorldDecks.Circle(px, py, PadRadiusM, top));
        // Rooftop rings (static) and the spinning ring.
        foreach (var (tw, hdg) in RooftopRings())
        {
            double g = t.HeightAt(tw.Cx, tw.Cy);
            WorldSolids.Shapes.Add(new TorusSolid(tw.Cx, tw.Cy, g + tw.HeightM + RingAboveRoofM, RingRadiusM, RingTubeM, hdg * System.Math.PI / 180));
        }
        var (sx, sy, sz) = SpinRingCentre;
        double sg = t.HeightAt(sx, sy);
        WorldSolids.Shapes.Add(new TorusSolid(sx, sy, sg + sz, SpinRingRadiusM, SpinRingTubeM, null));
        WorldSolids.Boxes.Add(new WorldSolids.Box(sx, sy, 2, 2, sg + Tallest.HeightM, sg + Tallest.HeightM + SpinPostM - 1));   // the bearing post
        Mall.RegisterSolids(t);
    }
}

/// <summary>The Mall (owner): a long rectangular reflecting pond with groomed grass either side — 3,000 ft (914 m) of
/// landable water and grass — the Capitol at the east end, the Washington Monument at the west.</summary>
public static class Mall
{
    public const double CentreX = 3050, PondY0 = 2500, PondLengthM = 914.4, PondHalfWidthM = 32, GrassWidthM = 100, PondDepthM = 2.6;
    public static double PondY1 => PondY0 + PondLengthM;
    public static double PondSurfaceM => WorldTerrain.DatumM - 0.3;
    public const double MonumentY = 2290, MonumentHeightM = 169.3, MonumentBaseHalfM = 8.4, MonumentShaftTopHalfM = 5.25, MonumentShaftM = 152.4;
    public const double CapitolY = 3560, CapitolHalfLengthM = 115, CapitolHalfDepthM = 35, CapitolWingM = 30, CapitolDomeR = 30, CapitolDomeTopM = 88;

    public static bool InPond(double x, double y) => System.Math.Abs(x - CentreX) < PondHalfWidthM && y > PondY0 && y < PondY1;
    public static bool OnGrass(double x, double y)
    {
        double d = System.Math.Abs(x - CentreX);
        return d >= PondHalfWidthM && d < PondHalfWidthM + GrassWidthM && y > PondY0 - 60 && y < PondY1 + 20;
    }
    public static bool InMall(double x, double y) => System.Math.Abs(x - CentreX) < PondHalfWidthM + GrassWidthM + 20 && y > MonumentY - 120 && y < CapitolY + 80;

    /// <summary>Ground: dead flat at the datum along the Mall; the pond's bed 2.6 m down.</summary>
    public static double? HeightAt(double x, double y)
    {
        if (!InMall(x, y)) return null;
        return InPond(x, y) ? PondSurfaceM - PondDepthM : WorldTerrain.DatumM;
    }

    public static void RegisterSolids(WorldTerrain t)
    {
        double g = WorldTerrain.DatumM;
        // Monument: stacked tapering blocks.
        for (int k = 0; k < 6; k++)
        {
            double h0 = MonumentShaftM * k / 6, h1 = MonumentShaftM * (k + 1) / 6;
            double half = MonumentBaseHalfM + (MonumentShaftTopHalfM - MonumentBaseHalfM) * (k + 0.5) / 6;
            WorldSolids.Boxes.Add(new WorldSolids.Box(CentreX, MonumentY, half, half, g + h0, g + h1));
        }
        WorldSolids.Boxes.Add(new WorldSolids.Box(CentreX, MonumentY, 3, 3, g + MonumentShaftM, g + MonumentHeightM));
        // Capitol: the long building, the drum and the dome.
        WorldSolids.Boxes.Add(new WorldSolids.Box(CentreX, CapitolY, CapitolHalfLengthM, CapitolHalfDepthM, g - 1, g + CapitolWingM));
        WorldSolids.Boxes.Add(new WorldSolids.Box(CentreX, CapitolY, CapitolDomeR, CapitolDomeR, g + CapitolWingM, g + 60));
        WorldSolids.Boxes.Add(new WorldSolids.Box(CentreX, CapitolY, CapitolDomeR * 0.6, CapitolDomeR * 0.6, g + 60, g + CapitolDomeTopM));
    }
}

/// <summary>The rooftop ring race: fly rings 1→5 in order (through the hole, the right way); the clock starts at ring 1.
/// A pass = crossing a ring's plane, in its facing direction, within its radius.</summary>
public sealed class RooftopRace
{
    public readonly List<(double x, double y, double z, double headingRad)> Rings = new();
    public int Next { get; private set; }
    public double StartS { get; private set; } = -1;
    public double LastLapS { get; private set; } = -1;
    public double BestLapS { get; private set; } = -1;
    public string LastEvent { get; private set; } = "";
    private Vec3 _prev; private bool _hasPrev;

    public RooftopRace(WorldTerrain t)
    {
        foreach (var (tw, hdg) in FlyCity.RooftopRings())
            Rings.Add((tw.Cx, tw.Cy, t.HeightAt(tw.Cx, tw.Cy) + tw.HeightM + FlyCity.RingAboveRoofM, hdg * System.Math.PI / 180));
    }

    /// <summary>Feed the aircraft position (sim frame) each step / frame with the world time.</summary>
    public void Update(Vec3 pos, double timeS)
    {
        if (!_hasPrev) { _prev = pos; _hasPrev = true; return; }
        var r = Rings[Next];
        double nx = System.Math.Cos(r.headingRad), ny = System.Math.Sin(r.headingRad);
        double a = (_prev.X - r.x) * nx + (_prev.Y - r.y) * ny, b = (pos.X - r.x) * nx + (pos.Y - r.y) * ny;
        if (a < 0 && b >= 0)
        {
            double f = a / (a - b);
            Vec3 c = _prev + (pos - _prev) * f;
            double ox = c.X - r.x, oy = c.Y - r.y, oz = -c.Z - r.z;
            if (ox * ox + oy * oy + oz * oz < FlyCity.RingRadiusM * FlyCity.RingRadiusM)
            {
                if (Next == 0) { StartS = timeS; LastEvent = "RING 1 — clock running"; }
                else LastEvent = $"RING {Next + 1}  {timeS - StartS:F1} s";
                Next++;
                if (Next == Rings.Count)
                {
                    LastLapS = timeS - StartS;
                    if (BestLapS < 0 || LastLapS < BestLapS) BestLapS = LastLapS;
                    LastEvent = $"FINISHED  {LastLapS:F1} s" + (BestLapS == LastLapS ? "  — best!" : $"  (best {BestLapS:F1} s)");
                    Next = 0; StartS = -1;
                }
            }
        }
        _prev = pos;
    }
}
