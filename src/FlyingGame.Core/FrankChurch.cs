namespace FlyingGame.Core;

/// <summary>
/// The backcountry: the famous Idaho strips, packed in tight (owner 2026-10-16: "a frank church wilderness
/// area with the most famous strips replicated like Johnson creek etc but make it super compact so all the
/// strips are within 10 minutes of each other and put it adjacent to the cascades").
///
/// COMPACT. The real ones are scattered over sixty miles of wilderness. These sit in a block about 7 km by
/// 5 km, west of the Cascades range, so the FARTHEST pair is some 8 km apart — under four minutes in a Cub.
/// A test measures that rather than trusting it.
///
/// WHAT IS REPLICATED is each strip's CHARACTER, which is what you fly: its length, its width, its slope, its
/// surface, whether it is one-way, and which end the ground falls away from. The absolute elevations are
/// adapted to this world's terrain — the real strips run from 2,000 ft on the Salmon to 5,800 ft at
/// Chamberlain, and here they run from the canyon floor to the plateau rim — but the relationships are kept:
/// Soldier Bar is still a short shelf above a river with a cliff at one end, Johnson Creek is still the long
/// friendly one in the trees, and Dewey Moore still slopes.
///
/// THE COUNTRY. A canyon system cut a kilometre into the plateau, the way the Middle Fork cuts the Salmon
/// River mountains, with a river in the bottom. Some strips are benches on the canyon walls, some are down on
/// the bars beside the water, and the high ones — Chamberlain, Big Creek, Thomas Creek — are up on the
/// plateau itself, which is what makes the altitude difference between them worth flying.
/// </summary>
public static class FrankChurch
{
    /// <summary>The block it occupies, west of the Cascades on the same plateau.</summary>
    public const double X0 = -5600, X1 = 1200, Y0 = -15000, Y1 = -10400;
    public static double RimM => WorldTerrain.DatumM + 3 * WorldTerrain.StepHeightM;   // the plateau it is cut into

    /// <summary>The canyon's floor at its north end and its south end: a kilometre below the rim, falling as
    /// it goes, the way a river canyon does. Given as DEPTH below the rim, so the canyon keeps its walls at
    /// 35° when the plateau moves — written as metres above the sea it would have become a 4,800 ft slot with
    /// 58° sides when the step went to 2,000 ft on 2026-10-16.
    ///
    /// A real river falls about 1–2 % through country like this. The first cut fell 4 %, which made a
    /// 4,000 ft strip on a bar need a 3.4 % slope just to follow the water.</summary>
    public const double FloorDepthNorthM = 636, FloorDepthSouthM = 756;
    public static double FloorNorthM => RimM - FloorDepthNorthM;
    public static double FloorSouthM => RimM - FloorDepthSouthM;
    public const double CanyonHalfWidthM = 900;      // rim to rim, halved
    public const double RiverHalfWidthM = 26;

    public enum Ground { Turf, Gravel, Dirt }

    /// <summary>
    /// One strip. <see cref="OneWayInHeadingDeg"/> is the heading you must land on when there is only one way
    /// in — the other end of the strip has a mountain, a river or nothing at all behind it.
    /// </summary>
    public readonly struct Strip
    {
        public readonly string Name;
        public readonly double X, Y, HeadingDeg, LengthM, WidthM, ElevationM, SlopePercent;
        public readonly Ground Surface;
        public readonly double? OneWayInHeadingDeg;
        public readonly string Character;

        /// <paramref name="depthBelowRimM"/>: how far below the plateau rim the strip sits, 0 for one up on the
        /// rim itself. Depth, not elevation, so that raising the plateau raises the whole canyon with it.
        public Strip(string name, double x, double y, double headingDeg, double lengthM, double widthM,
                     double depthBelowRimM, double slopePercent, Ground surface, double? oneWayInHeadingDeg,
                     string character)
        {
            Name = name; X = x; Y = y; HeadingDeg = headingDeg; LengthM = lengthM; WidthM = widthM;
            ElevationM = RimM - depthBelowRimM; SlopePercent = slopePercent; Surface = surface;
            OneWayInHeadingDeg = oneWayInHeadingDeg; Character = character;
        }

        public bool OneWay => OneWayInHeadingDeg.HasValue;
        public double LengthFt => LengthM * 3.28084;

        /// <summary>Along the strip (0 at the threshold you land on, 1 at the far end) and across it.</summary>
        public (double along, double across) Local(double x, double y)
        {
            double h = HeadingDeg * System.Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * System.Math.Cos(h) + dy * System.Math.Sin(h),
                    -dx * System.Math.Sin(h) + dy * System.Math.Cos(h));
        }

        public bool On(double x, double y, double widthPad = 0)
        {
            var (a, c) = Local(x, y);
            return System.Math.Abs(a) <= LengthM * 0.5 && System.Math.Abs(c) <= WidthM * 0.5 + widthPad;
        }

        /// <summary>The surface height on the strip: flat across, sloping along.</summary>
        public double HeightAt(double x, double y)
        {
            var (a, _) = Local(x, y);
            return ElevationM + a * SlopePercent / 100.0;
        }

        /// <summary>The height at the middle of the strip's ends.</summary>
        public double LowEndM => ElevationM - LengthM * 0.5 * SlopePercent / 100.0;
        public double HighEndM => ElevationM + LengthM * 0.5 * SlopePercent / 100.0;
    }

    /// <summary>How far out from the strip the bench stays flat before the ground falls or rises away.</summary>
    public const double ApronM = 70;
    /// <summary>How far the bench blends into the hillside.</summary>
    public const double BlendM = 240;

    /// <summary>
    /// Ten of the famous ones. Lengths and slopes are the real strips'; the elevations are this world's.
    /// </summary>
    /// <summary>
    /// Ten of the famous ones. Lengths and slopes are the real strips'; the positions were not chosen on a map
    /// but MEASURED against the canyon — every one sits where the ground can actually hold it. Four are up on
    /// the plateau rim at 7,000 ft, four are benches cut into the canyon walls, and two are bars down beside
    /// the water, which is roughly how the real ones are distributed.
    ///
    /// The one-way strips point up the FALL LINE of their wall, 20° off it: the far end climbs into the
    /// mountain and the approach end hangs over the canyon, so there is no going around. That is Soldier Bar
    /// and Dewey Moore exactly.
    /// </summary>
    public static readonly Strip[] Strips =
    {
        //         name            x       y    hdg  len  wid  depth slope  surface          one-way
        // ---- up on the rim: high, long, and the density-altitude lesson. Depth 0 = the rim, 7,000 ft.
        new Strip("Chamberlain", -3900, -11000,  85, 1100, 40,    0,  0.0, Ground.Gravel, null,
                  "On the plateau in an open basin at 7,000 ft. Long and flat — and 7,000 ft on a warm day is the whole lesson."),
        new Strip("Big Creek", -2600, -11400, 108, 1070, 34,    0,  0.8, Ground.Gravel, null,
                  "On the rim, slightly uphill to the west. Long enough to be comfortable, high enough not to be."),
        new Strip("Thomas Creek", -4700, -14100,  25, 1220, 30,    0,  1.2, Ground.Gravel, null,
                  "The longest of them, and uphill. Fine until you try to leave on a warm afternoon."),
        new Strip("Sulphur Creek", -3200, -14300,  70,  915, 30,    0,  0.0, Ground.Turf, null,
                  "A meadow strip on the rim, timber off the east end."),
        // ---- benches cut into the canyon walls.
        new Strip("Johnson Creek", -1400, -12600,  12, 1040, 46,  176,  0.0, Ground.Turf, null,
                  "3,400 ft of grass on a wide bench at 6,400 ft, timber at both ends. Two-way, forgiving, and the one to arrive at first."),
        new Strip("Soldier Bar",  -1900, -13900, 250,  400, 20,  346,  1.5, Ground.Dirt, 250,
                  "A 1,300 ft shelf above the river with nothing under the approach end. ONE WAY: land uphill, and there is no going around."),
        new Strip("Cabin Creek", -3500, -12126,  70,  460, 20,  226,  3.0, Ground.Dirt, 70,
                  "Short, 3 % uphill, and you come at it off the canyon. ONE WAY to the north-east."),
        new Strip("Dewey Moore", -2900, -13528, 290,  370, 18,  276,  4.0, Ground.Dirt, 290,
                  "The shortest and the steepest: 4 % up, 1,200 ft, and a 400 m drop off the low end. ONE WAY."),
        // ---- bars on the canyon floor, sloping with the river.
        new Strip("Indian Creek",  -300, -12830,  34, 1220, 30,  658,  1.8, Ground.Gravel, null,
                  "On a bar in the canyon bottom beside the river. Long, but the walls are close on both sides."),
        new Strip("Mackay Bar",    1000, -12486,   8,  460, 24,  635,  1.8, Ground.Gravel, null,
                  "Right down on the river bar where the canyon opens out — and the walls lean over it."),
    };

    private static bool InBlock(double x, double y)
        => x > X0 - 400 && x < X1 + 400 && y > Y0 - 400 && y < Y1 + 400;

    public static bool InArea(double x, double y) => InBlock(x, y);

    private static double Smooth(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The canyon's centreline: it runs north-south and meanders, like a river in rock.</summary>
    public static double CanyonCentreY(double x)
        => -12800 + 420 * System.Math.Sin(x / 1150.0) + 170 * System.Math.Sin(x / 480.0 + 1.1);

    /// <summary>The river's surface where the canyon has one.</summary>
    public static double RiverSurfaceAt(double x)
    {
        double t = System.Math.Clamp((x - X0) / (X1 - X0), 0, 1);
        return FloorSouthM + (FloorNorthM - FloorSouthM) * t;
    }

    /// <summary>The canyon cut into the plateau, before any strip bench is put on it.</summary>
    private static double CanyonAt(double x, double y, double rim)
    {
        double dy = System.Math.Abs(y - CanyonCentreY(x));
        if (dy > CanyonHalfWidthM) return rim;
        double floor = RiverSurfaceAt(x) - 3.0;
        double flat = RiverHalfWidthM + 90;                  // the canyon bottom: the river and its bars
        if (dy <= flat) return floor;
        double t = (dy - flat) / (CanyonHalfWidthM - flat);
        // Steep near the bottom, rounding off to the rim: a canyon, not a valley.
        double wall = System.Math.Pow(Smooth(t), 0.62);
        return floor + (rim - floor) * wall;
    }

    /// <summary>The ground: the plateau, cut by the canyon, with a flat bench under every strip.</summary>
    public static double HeightAt(double x, double y, double baseH)
    {
        if (!InBlock(x, y)) return baseH;
        double h = CanyonAt(x, y, baseH);

        // The NEAREST strip owns the ground, and only that one. Applying each strip in turn let one strip's
        // blend zone drag a neighbour's surface off level where the two overlapped — two strips a kilometre
        // apart left the rim strips 2 m low in the middle.
        double bestOutside = double.MaxValue, bestSurface = 0;
        foreach (Strip s in Strips)
        {
            var (along, across) = s.Local(x, y);
            double halfL = s.LengthM * 0.5, halfW = s.WidthM * 0.5;
            double outAlong = System.Math.Max(0, System.Math.Abs(along) - halfL);
            double outAcross = System.Math.Max(0, System.Math.Abs(across) - halfW);
            double outside = System.Math.Sqrt(outAlong * outAlong + outAcross * outAcross);
            if (outside > ApronM + BlendM || outside >= bestOutside) continue;
            bestOutside = outside;
            // The strip and its apron are dead flat across and at the strip's own slope along.
            bestSurface = s.ElevationM + System.Math.Clamp(along, -halfL, halfL) * s.SlopePercent / 100.0;
        }
        if (bestOutside <= ApronM) return bestSurface;
        if (bestOutside < double.MaxValue)
        {
            double t = Smooth((bestOutside - ApronM) / BlendM);
            return bestSurface + (h - bestSurface) * t;
        }
        return h;
    }

    /// <summary>Is (x, y) inside any strip's bench, apron or blend — the ground the strips have shaped?</summary>
    public static bool OnWorkedGround(double x, double y, double extra = 0)
    {
        foreach (Strip s in Strips)
        {
            var (along, across) = s.Local(x, y);
            double outAlong = System.Math.Max(0, System.Math.Abs(along) - s.LengthM * 0.5);
            double outAcross = System.Math.Max(0, System.Math.Abs(across) - s.WidthM * 0.5);
            if (outAlong * outAlong + outAcross * outAcross <= (ApronM + BlendM + extra) * (ApronM + BlendM + extra)) return true;
        }
        return false;
    }

    /// <summary>The strip under (x, y), if any — what the gear rolls on.</summary>
    public static bool TryStripAt(double x, double y, out Strip found)
    {
        if (InBlock(x, y))
            foreach (Strip s in Strips)
                if (s.On(x, y)) { found = s; return true; }
        found = default;
        return false;
    }

    /// <summary>What the wheels find: the strips are turf, gravel or dirt; the rest is mountain.</summary>
    public static WorldTerrain.Surface? SurfaceAt(double x, double y)
    {
        if (!TryStripAt(x, y, out Strip s)) return null;
        return s.Surface switch
        {
            Ground.Turf => WorldTerrain.Surface.Grass,
            Ground.Gravel => WorldTerrain.Surface.Gravel,
            _ => WorldTerrain.Surface.Gravel,     // dirt rolls like gravel and is rougher to look at
        };
    }

    /// <summary>The river in the canyon bottom, for floats and for the look of it.</summary>
    public static double? WaterSurfaceAt(double x, double y)
    {
        if (!InBlock(x, y)) return null;
        if (System.Math.Abs(y - CanyonCentreY(x)) > RiverHalfWidthM) return null;
        // Not where a strip's bench has been laid over the top of it.
        foreach (Strip s in Strips) if (s.On(x, y, ApronM)) return null;
        return RiverSurfaceAt(x);
    }

    /// <summary>How long a Cub takes between the two strips farthest apart, in minutes at 90 kt.</summary>
    public static double WorstCrossingMinutes()
    {
        double worst = 0;
        for (int i = 0; i < Strips.Length; i++)
        for (int j = i + 1; j < Strips.Length; j++)
        {
            double d = System.Math.Sqrt((Strips[i].X - Strips[j].X) * (Strips[i].X - Strips[j].X)
                                      + (Strips[i].Y - Strips[j].Y) * (Strips[i].Y - Strips[j].Y));
            worst = System.Math.Max(worst, d);
        }
        return worst / (90 * 0.514444) / 60.0;
    }
}
