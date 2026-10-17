namespace FlyingGame.Core;

using FlyingGame.Core.MathTypes;

/// <summary>
/// The timber, everywhere — owner 2026-10-16: "conifers up to a treeline that falls with latitude and rises
/// on sun-facing slopes, solid where you can hit them. Changes how every low approach feels."
///
/// There is no list of trees. A forest the size of this world is 16 km by 27 km of country, and a list of
/// every stem in it would be tens of millions of entries that nothing could store, draw or search. So the
/// forest is a FUNCTION: the world is cut into 18 m cells, each cell is asked whether it has a tree and how
/// big it is, and the answer comes out of a hash of the cell's own index. The same cell always answers the
/// same way, so what the renderer draws and what the airframe hits are the same trees, and neither of them
/// had to agree with the other in advance.
///
/// What that buys: collision costs one height query in the common case (you are higher than the tallest tree
/// that could be there, so there is nothing to test), and the renderer can draw whatever subset of the world
/// it can afford — the trees it leaves out still exist, and still hurt.
///
/// WHERE IT GROWS is the interesting part, and it is all subtraction:
///   • below the treeline, which falls going north and rises on the slopes that face the sun;
///   • only on <see cref="WorldTerrain.Surface.Rough"/> ground — so never on a runway, an apron, a backcountry
///     strip, the braided outwash, a glacier, the groomed grass, or the alpine and Alaska rock;
///   • never over water, on a beach, on ground steeper than about 49°, in the high desert (too dry — that
///     block has its own juniper scatter), or where somebody has built: an airport pad, a town, a crop field;
///   • and in stands, not as a uniform speckle, so there are meadows to land in and edges to read.
/// </summary>
public static class Forest
{
    /// <summary>One candidate stem per cell of this size. Mean spacing in full timber is a little more than
    /// this, because about a third of the cells come up empty.</summary>
    public const double CellM = 18.0;

    /// <summary>Treeline at x = 0, metres AMSL — a little above the 7,000 ft plateau, so the top field has
    /// timber at its southern end and bare ground at its northern one.</summary>
    public const double MeanTreeLineM = 2350.0;
    /// <summary>How far the line falls per metre of northing (x). Over the world's 16 km of x that is 900 m
    /// of treeline, which is the whole point of flying from one end of it to the other.</summary>
    public const double TreeLineLapsePerM = 450.0 / 8000.0;
    /// <summary>How much a slope that faces the sun raises its own treeline, metres.</summary>
    public const double TreeLineAspectM = 130.0;
    /// <summary>The krummholz band: trees thin out over this many metres below the line rather than stopping
    /// at it, and the last of them are waist-high.</summary>
    public const double TreeLineFadeM = 160.0;

    /// <summary>
    /// Nothing roots on ground steeper than this (tangent) — 63°, with the thinning starting at 51°.
    ///
    /// This number has been wrong twice. 49° (the textbook figure) left every mountain front in the world
    /// bare brown rock. 58° left a hard horizontal line of timber across the foot of each one, because a
    /// front's upper band sits at 44° whatever its width — WorldTerrain's wall profile puts 82 %
    /// of the climb in the top 45 % of the run — so a limit that fades out at 45° cuts the forest off in a
    /// dead straight line at exactly the height the profile steepens. What makes a treeline look like a
    /// treeline is HEIGHT, not slope; slope is only here to keep trees off a cliff, and the escarpments that
    /// really are cliffs stand at 6:1, nowhere near this.
    /// </summary>
    public const double MaxSlopeTan = 2.00;
    /// <summary>No timber below this, so the beach stays a beach.</summary>
    public const double ShoreClearM = 6.0;

    /// <summary>The tallest a tree gets, in the deep valleys. Doug fir, near enough.</summary>
    public const double MaxHeightM = 42.0;
    /// <summary>
    /// How far above the ground AT THE QUERY POINT a tree's top can still reach. The cheap path in the
    /// collision asks one height query and gives up if you are above this — but the trees it would have
    /// tested stand up to 17 m away, and on a hillside that ground is higher than the ground under you. At
    /// the steepest slope timber holds (2 : 1) that is 35 m; 60 m is the margin, and it still means every
    /// query above 100 m AGL costs one height query and nothing else.
    /// </summary>
    public const double SkyClearM = MaxHeightM + 60;
    /// <summary>How far below the treeline a tree has to be to reach full height.</summary>
    public const double FullHeightBelowM = 700.0;
    /// <summary>Crown half-width as a multiple of the trunk radius — what you are in when you are in the
    /// branches, and the radius the collision cylinder uses.</summary>
    public const double CrownRatio = 2.4;

    // ---- the treeline ----------------------------------------------------------------------------------

    /// <summary>
    /// The treeline over (x, y). Falls going north; rises where the ground faces the sun, which in this
    /// hemisphere is the slopes that fall away to the south (−x) — that is, where dh/dx is POSITIVE, because
    /// ground that rises to the north is ground whose face looks south. A little hashed wander on top,
    /// because a treeline that is a contour line looks drawn rather than grown.
    /// </summary>
    public static double TreeLineAt(double x, double y) => TreeLineAt(x, y, NorthGradientAt(x, y));

    /// <summary>The treeline where the caller already has dh/dx (the renderer has it per vertex).</summary>
    public static double TreeLineAt(double x, double y, double northGrad)
    {
        double line = MeanTreeLineM - TreeLineLapsePerM * x;
        line += TreeLineAspectM * System.Math.Clamp(northGrad / 0.6, -1, 1);
        line += 70 * (Lattice(x / 2400.0, y / 2400.0, 19.3) - 0.5);
        return line;
    }

    /// <summary>dh/dx — the slope's north-south component. Positive means the ground rises to the north, so
    /// the slope faces south, into the sun.</summary>
    private static double NorthGradientAt(double x, double y)
    {
        WorldTerrain? t = WorldTerrain.Active;
        if (t is null) return 0;
        const double e = 24.0;
        return (t.HeightAt(x + e, y) - t.HeightAt(x - e, y)) / (2 * e);
    }

    // ---- where it grows --------------------------------------------------------------------------------

    /// <summary>
    /// How much of the ground at (x, y) is timber, 0 to 1 — the chance that a cell there carries a tree, and
    /// the weight the renderer tints the ground with. Needs <see cref="WorldTerrain.Active"/>.
    /// </summary>
    public static double CoverAt(double x, double y)
    {
        WorldTerrain? t = WorldTerrain.Active;
        if (t is null) return 0;
        return CoverAt(x, y, t.HeightAt(x, y));
    }

    /// <summary>Cover where the caller already knows the ground height.</summary>
    public static double CoverAt(double x, double y, double h)
    {
        if (h < ShoreClearM || !Timbered(x, y)) return 0;
        var (slope, grad) = SlopeAt(x, y);
        return CoverAt(x, y, h, slope, grad);
    }

    /// <summary>
    /// Cover where the caller already has the ground height AND the slope — which the renderer does, per
    /// vertex, because it needs them for the rock and the snow anyway. Six height queries a vertex is the
    /// difference between a world that builds in a minute and one that does not.
    /// </summary>
    public static double CoverAt(double x, double y, double h, double slopeTan, double northGrad)
    {
        if (h < ShoreClearM) return 0;                                     // the beach and the sea
        if (!Timbered(x, y)) return 0;
        WorldTerrain? t = WorldTerrain.Active;
        if (t is not null && t.WaterSurfaceAt(x, y).HasValue) return 0;    // the river, the lakes, the harbour

        // Slope: a hillside holds timber, a cliff does not.
        double slope = slopeTan;
        double steep = 1.0 - Smooth(System.Math.Clamp((slope - MaxSlopeTan * 0.62) / (MaxSlopeTan * 0.38), 0, 1));   // full to 51°, gone by 63°
        if (steep <= 0) return 0;

        // The line itself, with the krummholz band under it.
        double below = TreeLineAt(x, y, northGrad) - h;
        if (below <= 0) return 0;
        double alt = Smooth(System.Math.Clamp(below / TreeLineFadeM, 0, 1));

        return Stand(x, y) * alt * steep;
    }

    /// <summary>The stand pattern: where the forest is solid and where it opens into meadow. Two lattices —
    /// 760 m of whole hillsides and 185 m of edges and clearings — through a soft threshold.</summary>
    public static double Stand(double x, double y)
    {
        double s = 0.62 * Lattice(x / 760.0, y / 760.0, 3.1) + 0.38 * Lattice(x / 185.0, y / 185.0, 7.7);
        return Smooth(System.Math.Clamp((s - 0.33) / 0.30, 0, 1));
    }

    /// <summary>True where timber is allowed at all: the right surface, and nobody has built here.</summary>
    public static bool Timbered(double x, double y)
    {
        // Nothing stands outside the built world: the height field answers everywhere, but the mesh stops,
        // and a render from the north edge showed a line of conifers standing in mid-air over the sea.
        if (!WorldTerrain.OnBuiltGround(x, y)) return false;
        if (WorldTerrain.SurfaceAt(x, y) != WorldTerrain.Surface.Rough) return false;
        // The backcountry strips are grass and gravel, so the surface test above already keeps timber off the
        // strip itself — but not off the APRON that has been graded flat round it. Nobody clears a bench out
        // of a canyon side and leaves two hundred-foot firs standing on the overrun. The blend beyond the
        // apron keeps its timber, which is what makes those strips what they are: trees to the edge of the
        // worked ground and no further. (OnWorkedGround measures apron + blend; −BlendM asks for the apron.)
        // (Padded: Mackay Bar's east end runs 30 m outside the block's own X1.)
        if (x > FrankChurch.X0 - 400 && x < FrankChurch.X1 + 400 && y > FrankChurch.Y0 - 400 && y < FrankChurch.Y1 + 400 &&
            FrankChurch.OnWorkedGround(x, y, -FrankChurch.BlendM)) return false;
        // Too dry: the high desert has sagebrush and its own juniper, and no forest.
        if (x > HighDesert.X0 - 150 && x < HighDesert.X1 + 150 && y > HighDesert.Y0 - 150 && y < HighDesert.Y1 + 150) return false;
        for (int p = 0; p < WorldTerrain.PlateauCount; p++)
        {
            WorldTerrain.Airport a = WorldTerrain.Airports[p];
            if (WorldTerrain.PadOutsideX(a, x) < 70 && System.Math.Abs(y - a.Y) < WorldTerrain.PadHalfY + 70) return false;
            if (WorldTerrain.Has(p, WorldTerrain.Feature.Town))
            {
                var (cx0, cy0, cx1, cy1) = ValleyCity.At(p).Extent(70);
                if (x > cx0 && x < cx1 && y > cy0 && y < cy1) return false;
            }
            if (WorldTerrain.Has(p, WorldTerrain.Feature.CropField))
            {
                CropField f = CropField.For(p);
                if (x > f.X0 - 40 && x < f.X1 + 40 && y > f.Y0 - 40 && y < f.Y1 + 40) return false;
            }
        }
        // The pylon course is built ground too. A render of the Mesa plateau had gates 5 to 8 standing in
        // close timber, which is a different game from the one that course was laid out for.
        foreach (var r in RaceGround)
            if (x > r.x0 && x < r.x1 && y > r.y0 && y < r.y1) return false;
        return true;
    }

    private static (double x0, double y0, double x1, double y1)[]? _raceGround;
    /// <summary>The rectangle round each plateau's pylon course, with room to turn in.</summary>
    private static (double x0, double y0, double x1, double y1)[] RaceGround => _raceGround ??= BuildRaceGround();

    private static (double x0, double y0, double x1, double y1)[] BuildRaceGround()
    {
        var list = new System.Collections.Generic.List<(double, double, double, double)>();
        foreach (int p in WorldTerrain.PlateausWith(WorldTerrain.Feature.RaceCourse))
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (RaceElement e in RaceCourse.ElementsFor(p))
            {
                x0 = System.Math.Min(x0, e.X); x1 = System.Math.Max(x1, e.X);
                y0 = System.Math.Min(y0, e.Y); y1 = System.Math.Max(y1, e.Y);
            }
            const double room = 120;
            list.Add((x0 - room, y0 - room, x1 + room, y1 + room));
        }
        return list.ToArray();
    }

    /// <summary>(slope magnitude, dh/dx) at (x, y) — four height queries, done once.</summary>
    private static (double slopeTan, double northGrad) SlopeAt(double x, double y)
    {
        WorldTerrain? t = WorldTerrain.Active;
        if (t is null) return (0, 0);
        const double e = 22.0;
        double dx = (t.HeightAt(x + e, y) - t.HeightAt(x - e, y)) / (2 * e);
        double dy = (t.HeightAt(x, y + e) - t.HeightAt(x, y - e)) / (2 * e);
        return (System.Math.Sqrt(dx * dx + dy * dy), dx);
    }

    // ---- the trees themselves --------------------------------------------------------------------------

    /// <summary>One conifer. <see cref="GroundM"/> is the ground it stands on, so the renderer does not have
    /// to ask the terrain a second time.</summary>
    public readonly struct Tree
    {
        public readonly double X, Y, GroundM, HeightM, RadiusM, LeanDeg, TwistDeg;
        public Tree(double x, double y, double groundM, double heightM, double radiusM, double leanDeg, double twistDeg)
        { X = x; Y = y; GroundM = groundM; HeightM = heightM; RadiusM = radiusM; LeanDeg = leanDeg; TwistDeg = twistDeg; }
        public double TopM => GroundM + HeightM;
        public double CrownRadiusM => RadiusM * CrownRatio;
    }

    public static int CellOf(double v) => (int)System.Math.Floor(v / CellM);

    /// <summary>The tree in cell (i, j), if that cell has one. The whole forest comes out of here.</summary>
    public static bool TryTreeInCell(int i, int j, out Tree tree)
    {
        tree = default;
        double key = i * 2147.31 + j * 7919.17;
        double x = (i + 0.08 + 0.84 * WorldTerrain.Hash(key, 1.7)) * CellM;
        double y = (j + 0.08 + 0.84 * WorldTerrain.Hash(key, 2.3)) * CellM;
        WorldTerrain? t = WorldTerrain.Active;
        if (t is null) return false;
        // The cheapest question first: is timber allowed here at all? That costs no height queries, and it is
        // what rules out every runway, apron, strip, glacier and town in the world.
        if (!Timbered(x, y)) return false;
        double g = t.HeightAt(x, y);
        var (slope, grad) = SlopeAt(x, y);
        double cover = CoverAt(x, y, g, slope, grad);
        if (cover <= 0 || WorldTerrain.Hash(key, 2.9) > cover) return false;

        // Height: full in the valleys, waist-high in the last of the krummholz.
        double below = System.Math.Clamp((TreeLineAt(x, y, grad) - g) / FullHeightBelowM, 0, 1);
        double tall = (2.5 + (MaxHeightM - 2.5) * Smooth(below)) * (0.74 + 0.5 * WorldTerrain.Hash(key, 4.3));
        tall = System.Math.Clamp(tall, 2.0, MaxHeightM);
        tree = new Tree(x, y, g, tall, 0.22 + tall / 24.0,
                        (WorldTerrain.Hash(key, 5.1) * 2 - 1) * 5,
                        WorldTerrain.Hash(key, 6.7) * 360);
        return true;
    }

    /// <summary>Every tree whose stem stands in the rectangle. Deterministic and in cell order.</summary>
    public static System.Collections.Generic.IEnumerable<Tree> InRect(double x0, double y0, double x1, double y1)
    {
        for (int i = CellOf(x0); i <= CellOf(x1); i++)
            for (int j = CellOf(y0); j <= CellOf(y1); j++)
                if (TryTreeInCell(i, j, out Tree tr) && tr.X >= x0 && tr.X <= x1 && tr.Y >= y0 && tr.Y <= y1)
                    yield return tr;
    }

    // ---- hitting them ----------------------------------------------------------------------------------

    /// <summary>
    /// The forest as a solid, for <see cref="WorldSolids"/>. The cheap path is the one that matters: above the
    /// tallest tree that could stand here there is nothing to test, and that costs one height query — the same
    /// query the contact code has already made every step since the beginning.
    /// </summary>
    public sealed class Solids : WorldSolids.IShape
    {
        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            normalNed = Vec3.Zero; depth = 0;
            WorldTerrain? t = WorldTerrain.Active;
            if (t is null) return false;
            if (up > t.HeightAt(x, y) + SkyClearM) return false;         // the whole sky, in one query
            int i0 = CellOf(x - MaxHeightM * 0.4), i1 = CellOf(x + MaxHeightM * 0.4);
            int j0 = CellOf(y - MaxHeightM * 0.4), j1 = CellOf(y + MaxHeightM * 0.4);
            double best = 0; Vec3 bestN = Vec3.Zero;
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!TryTreeInCell(i, j, out Tree tr)) continue;
                    if (up < tr.GroundM || up > tr.TopM) continue;
                    double dx = x - tr.X, dy = y - tr.Y, r = tr.CrownRadiusM;
                    double d = System.Math.Sqrt(dx * dx + dy * dy);
                    if (d > r) continue;
                    // Out the side, or off the top if that is nearer — a tree you clip the top of should drop
                    // you onto it, not spit you sideways.
                    double side = r - d, top = tr.TopM - up;
                    if (top < side)
                    {
                        if (top > best) { best = top; bestN = new Vec3(0, 0, -1); }
                    }
                    else if (side > best)
                    {
                        best = side;
                        bestN = d > 1e-6 ? new Vec3(dx / d, dy / d, 0) : new Vec3(1, 0, 0);
                    }
                }
            if (best <= 0) return false;
            normalNed = bestN; depth = best;
            return true;
        }
    }

    private static Solids? _solids;
    /// <summary>Register the forest with <see cref="WorldSolids"/> (idempotent per shape list).</summary>
    public static void RegisterSolids()
    {
        _solids ??= new Solids();
        if (!WorldSolids.Shapes.Contains(_solids)) WorldSolids.Shapes.Add(_solids);
    }

    // ---- plumbing --------------------------------------------------------------------------------------

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    /// <summary>Value noise on a unit lattice, 0..1 — the same one the erosion uses.</summary>
    public static double Lattice(double u, double v, double salt) => WorldTerrain.Lattice(u, v, salt);
}
