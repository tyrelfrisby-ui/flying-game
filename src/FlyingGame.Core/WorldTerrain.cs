namespace FlyingGame.Core;

/// <summary>
/// The world's ground and water, as ONE analytic height field shared by the sim (landing gear, floats)
/// and the renderer (terrain mesh) so what you see is what the wheels touch. Sim frame: x north,
/// y east, altitude UP (metres); callers convert to NED z as needed.
///
/// Layout (owner request): the base airport sits on a plain at sea level. To the WEST the land rises
/// in three steep, irregular escarpments — canyon-wall steps, not straight lines — each to a plateau
/// with its own airport, progressively higher for density-altitude work:
///   A0  0 m (origin)  ·  A1 900 m  ·  A2 1800 m  ·  A3 2700 m (≈ 8,900 ft).
/// Every airport has a lake and the river winds down the steps past all four (waterfalls at each wall).
/// </summary>
public sealed class WorldTerrain
{
    /// <summary>The active terrain (null = flat sea-level world, which is what the unit tests assume).</summary>
    public static WorldTerrain? Active { get; set; }

    /// <summary>Ground height (m, up) at a sim x/y — 0 everywhere when no terrain is active.</summary>
    public static double GroundHeightAt(double x, double y) => Active?.HeightAt(x, y) ?? 0.0;

    // ---- layout ---------------------------------------------------------------------------------
    public const double StepHeightM = 900.0;
    public const double StepSpacingM = 3000.0;
    public const double FirstEdgeY = -1400.0;      // mean y of the first escarpment (walls run along x)
    public const double EscarpmentWidthM = 360.0;  // horizontal run of one wall (talus + cliff)
    public const int StepCount = 3;

    public readonly struct Airport
    {
        public readonly string Name; public readonly double X, Y, ElevationM;
        public Airport(string name, double x, double y, double elev) { Name = name; X = x; Y = y; ElevationM = elev; }
    }

    /// <summary>Runway centre (x,y) and field elevation for the four airports; runways run along +x.</summary>
    public static readonly Airport[] Airports =
    {
        new("Valley", 400, 0, 0),
        new("Bench", 400, FirstEdgeY - StepSpacingM * 0.5, StepHeightM),
        new("Mesa", 400, FirstEdgeY - StepSpacingM * 1.5, StepHeightM * 2),
        new("Summit", 400, FirstEdgeY - StepSpacingM * 2.5, StepHeightM * 3),
    };

    public const double RunwayLengthM = 1500.0;   // main paved runway along x, centred on Airport.X
    public const double RunwayWidthM = 30.0;
    // Every airport has the same layout inside a flat pad: two crossing paved runways, an open hangar,
    // a dirt strip (STOL contest) and a grass strip. Local offsets are in AirportLayout.
    public const double PadHalfX = 1250.0, PadHalfY = 850.0;

    // Lakes: one per airport, off the runway's east side. Ellipse (cx, cy, rx, ry).
    public readonly struct Lake
    {
        public readonly double Cx, Cy, Rx, Ry, SurfaceM;
        public Lake(double cx, double cy, double rx, double ry, double surface) { Cx = cx; Cy = cy; Rx = rx; Ry = ry; SurfaceM = surface; }
        public double Inside(double x, double y) // <1 inside, 1 at the shore
        {
            double dx = (x - Cx) / Rx, dy = (y - Cy) / Ry;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public static readonly Lake[] Lakes = BuildLakes();

    private static Lake[] BuildLakes()
    {
        var l = new Lake[Airports.Length];
        for (int i = 0; i < Airports.Length; i++)
        {
            Airport a = Airports[i];
            l[i] = new Lake(a.X + 2700, a.Y + 600, 650, 420, a.ElevationM - 2.0);
        }
        return l;
    }

    // River: runs downhill west→east across the steps in a GORGE, meandering gently in x (minimum turn
    // radius ≈ 2 km so the gorge can be flown at speed), passing the lakes' west shores.
    public const double RiverHalfWidthM = 35.0;
    public static double RiverCentreX(double y) => 2150.0 + 350.0 * System.Math.Sin(y / 900.0) + 60.0 * System.Math.Sin(y / 520.0 + 1.1);

    // Gorge: the river cuts a canyon whose depth grows downstream (west→east) from ~15 m (50 ft) on
    // the Summit plateau to ~150 m (500 ft) in the Valley, with a staircase of waterfalls along the way
    // (on top of the big drops at the canyon walls).
    public const double GorgeDepthUpstreamM = 15.0, GorgeDepthDownstreamM = 150.0;
    public const double GorgeUpstreamY = -10500.0, GorgeDownstreamY = 2500.0;
    public const double BridgeY = -150.0;   // road bridge abeam the Valley airport

    /// <summary>Gorge depth (m) below the surrounding ground at along-flow position y.</summary>
    public static double GorgeDepthAt(double y)
    {
        double t = System.Math.Clamp((y - GorgeUpstreamY) / (GorgeDownstreamY - GorgeUpstreamY), 0, 1);
        return GorgeDepthUpstreamM + (GorgeDepthDownstreamM - GorgeDepthUpstreamM) * t;
    }

    /// <summary>Half-width of the gorge at the rim (m): walls near-vertical, floor a little wider than the river.</summary>
    public static double GorgeHalfWidthAt(double y) => RiverHalfWidthM + 25 + 0.9 * GorgeDepthAt(y);
    // NOTE: GorgeDepthAt is the smooth design depth (sets the rim width); the cut depth is the staircase.

    /// <summary>Waterfall staircase: between the giant drops at the canyon walls the river surface is a
    /// series of flat reaches separated by falls of 6–18 m every ~1.1 km. The accumulated drops make the
    /// gorge deepen from ~15 m upstream to ~150 m in the valley. Monotone non-increasing downstream.</summary>
    public double RiverSurfaceAt(double y)
    {
        // Rim along the meander is not monotone (the wall edge wanders back across the river's x), so the
        // surface uses a RUNNING MINIMUM of the rim downstream, sampled once into a table.
        _riverTable ??= BuildRiverTable();
        double f = (y - GorgeUpstreamY) / RiverTableStep;
        int i = (int)System.Math.Floor(f);
        if (i < 0) return _riverTable[0];
        if (i >= _riverTable.Length - 1) return _riverTable[^1];
        double t = f - i;
        // Falls are discontinuities; interpolate only along flat reaches (small differences).
        double a = _riverTable[i], b = _riverTable[i + 1];
        return System.Math.Abs(a - b) > 3.0 ? (t < 0.5 ? a : b) : a + (b - a) * t;
    }

    private const double RiverTableStep = 25.0;
    private double[]? _riverTable;

    private double[] BuildRiverTable()
    {
        int n = (int)((GorgeDownstreamY - GorgeUpstreamY) / RiverTableStep) + 1;
        var table = new double[n];
        const double fallSpacing = 1100.0;
        int nFalls = (int)System.Math.Floor((GorgeDownstreamY - GorgeUpstreamY) / fallSpacing);
        double total = GorgeDepthDownstreamM - GorgeDepthUpstreamM;
        double sumW = 0; for (int k = 0; k < nFalls; k++) sumW += 1.0 + 0.5 * System.Math.Sin(k * 2.7);
        double runningMin = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double y = GorgeUpstreamY + i * RiverTableStep;
            double rim = BaseHeightAt(RiverCentreX(y), y);
            runningMin = System.Math.Min(runningMin, rim);
            double depth = GorgeDepthUpstreamM;
            for (int k = 0; k < nFalls; k++)
            {
                double yk = GorgeUpstreamY + (k + 1) * fallSpacing;
                if (y >= yk) depth += total * (1.0 + 0.5 * System.Math.Sin(k * 2.7)) / sumW;
            }
            table[i] = runningMin - depth;
        }
        return table;
    }

    /// <summary>Gorge depth actually cut at y (the staircase value, for the gorge width).</summary>
    public double GorgeDepthCutAt(double y) => BaseHeightAt(RiverCentreX(y), y) - RiverSurfaceAt(y);

    // ---- airport layout (local sim offsets from the airport centre, metres) ---------------------
    public readonly struct Strip
    {
        public readonly string Kind; public readonly double Dx, Dy, Length, Width, HeadingDeg;
        public Strip(string kind, double dx, double dy, double length, double width, double headingDeg)
        { Kind = kind; Dx = dx; Dy = dy; Length = length; Width = width; HeadingDeg = headingDeg; }
    }

    /// <summary>Same at every airport: paved main (along x), paved crossing (60°), dirt STOL strip east, grass strip west.</summary>
    public static readonly Strip[] AirportStrips =
    {
        new("paved", 0, 0, RunwayLengthM, RunwayWidthM, 0),
        new("paved", 150, 0, 1200, 30, 60),
        new("dirt", 100, 420, 600, 15, 0),
        new("grass", -100, -420, 750, 20, 0),
    };

    /// <summary>Hangar centre offset from the airport centre (long axis along x, doors open both ends).</summary>
    public const double HangarDx = -450, HangarDy = 200;

    /// <summary>STOL contest on the dirt strip: landing line this far from the strip's south (−x) end; markers beyond it.</summary>
    public const double StolLineFromThresholdM = 150.0, StolMarkedLengthM = 250.0;

    // ---- height field ---------------------------------------------------------------------------

    /// <summary>Mean escarpment edge y (before irregularity) for step i (0..2).</summary>
    public static double EdgeMeanY(int i) => FirstEdgeY - StepSpacingM * i;

    /// <summary>Canyon-wall irregularity: the edge wanders ±~380 m along x, differently per step.</summary>
    public static double EdgeWander(int i, double x)
    {
        double p = i * 1.7;
        return 210.0 * System.Math.Sin(x / 530.0 + 1.3 + p) + 120.0 * System.Math.Sin(x / 205.0 + 0.4 + 2 * p)
             + 60.0 * System.Math.Sin(x / 91.0 + 2.1 + p) + 30.0 * System.Math.Sin(x / 37.0 + 3 * p);
    }

    /// <summary>Wall profile 0..1 over t = 0..1 across the escarpment: a talus apron then a near-vertical cliff.</summary>
    private static double WallProfile(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        // 0..0.55 of the run climbs 18 % (talus, ~25°); 0.55..1 climbs the remaining 82 % (cliff, ~78°).
        const double talusEnd = 0.55, talusRise = 0.18;
        if (t < talusEnd)
        {
            double u = t / talusEnd;
            return talusRise * (u * u * (3 - 2 * u));
        }
        double v = (t - talusEnd) / (1 - talusEnd);
        return talusRise + (1 - talusRise) * (v * v * (3 - 2 * v));
    }

    /// <summary>Plateau/escarpment height without airport pads or water carving.</summary>
    public double BaseHeightAt(double x, double y)
    {
        double h = 0;
        for (int i = 0; i < StepCount; i++)
        {
            double edge = EdgeMeanY(i) + EdgeWander(i, x);
            double t = (edge - y) / EscarpmentWidthM;   // grows as you go west past the edge
            h += StepHeightM * WallProfile(t);
            // Rock texture on the wall face: small ledges.
            if (t > 0 && t < 1) h += 6.0 * System.Math.Sin(x / 23.0) * System.Math.Sin(y / 17.0);
        }
        return h;
    }

    /// <summary>Ground height (m, up) including flat airport pads and the lake / river beds.</summary>
    public double HeightAt(double x, double y)
    {
        double h = BaseHeightAt(x, y);

        // Airport pads: exactly flat at field elevation inside the pad, blended over 300 m outside.
        foreach (Airport a in Airports)
        {
            double dx = System.Math.Max(0, System.Math.Abs(x - a.X) - PadHalfX);
            double dy = System.Math.Max(0, System.Math.Abs(y - a.Y) - PadHalfY);
            double d = System.Math.Sqrt(dx * dx + dy * dy);
            if (d < 300)
            {
                double w = 1 - d / 300; w = w * w * (3 - 2 * w);
                h = h + (a.ElevationM - h) * w;
            }
        }

        // Lake beds: a shallow bowl under the water.
        foreach (Lake l in Lakes)
        {
            double r = l.Inside(x, y);
            if (r < 1.0)
            {
                double depth = 9.0 * System.Math.Sqrt(1 - r * r);
                h = System.Math.Min(h, l.SurfaceM + 2.0 - depth);
            }
        }

        // Gorge: steep-walled canyon around the river; floor = river surface - 4 m.
        double ddx = System.Math.Abs(x - RiverCentreX(y));
        double gw = GorgeHalfWidthAt(y);
        if (ddx < gw)
        {
            double floorW = RiverHalfWidthM + 20;
            double floor = RiverSurfaceAt(y) - 4.0;
            if (ddx < floorW)
            {
                h = System.Math.Min(h, floor);
            }
            else
            {
                double f = (ddx - floorW) / (gw - floorW);         // 0 at floor edge .. 1 at rim
                double wall = f * f * (3 - 2 * f);                  // near-vertical lower, rounding to the rim
                wall = System.Math.Pow(wall, 0.6);
                h = System.Math.Min(h, floor + (h - floor) * wall);
            }
        }
        return h;
    }

    /// <summary>Water surface at x/y (m, up), or null if no water there. Lakes are flat at their level; the
    /// river surface is the local (uncarved) ground minus 2 m, so it steps down at every wall.</summary>
    public double? WaterSurfaceAt(double x, double y)
    {
        foreach (Lake l in Lakes)
        {
            if (l.Inside(x, y) < 1.0) return l.SurfaceM;
        }
        double ddx = System.Math.Abs(x - RiverCentreX(y));
        if (ddx < RiverHalfWidthM + 12)
        {
            return RiverSurfaceAt(y);
        }
        return null;
    }

    /// <summary>Unit ground normal (sim frame, z DOWN) from finite differences — for wheels/floats on slopes.</summary>
    public MathTypes.Vec3 NormalAt(double x, double y)
    {
        const double e = 2.0;
        double dhdx = (HeightAt(x + e, y) - HeightAt(x - e, y)) / (2 * e);
        double dhdy = (HeightAt(x, y + e) - HeightAt(x, y - e)) / (2 * e);
        // Up-vector (x, y, up) = (-dhdx, -dhdy, 1); NED: z = -up.
        var n = new MathTypes.Vec3(-dhdx, -dhdy, -1.0);
        return n / n.Length;
    }
}
