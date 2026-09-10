namespace FlyingGame.Core;

/// <summary>
/// The world's ground and water, as ONE analytic height field shared by the sim (landing gear, floats)
/// and the renderer (terrain mesh) so what you see is what the wheels touch. Sim frame: x north,
/// y east, altitude UP (metres); callers convert to NED z as needed.
///
/// Layout (owner request): the base airport sits on a plain at sea level. To the WEST the land rises
/// in three steep, irregular escarpments — canyon-wall steps, not straight lines — each to a plateau
/// with its own airport, progressively higher for density-altitude work (owner 2026-09-09: 1,500 ft a step):
///   A0  0 ft (origin)  ·  A1 1,500 ft  ·  A2 3,000 ft  ·  A3 4,500 ft.
/// Every plateau carries the SAME landscape as the Valley — airport, lake, bridge, crop field, aerobatic box,
/// race course, arch, tower and town — shifted by <see cref="PlateauDy"/> in y and up to the plateau's
/// elevation. The river winds down the steps past all four airports (waterfalls at each wall).
/// </summary>
public sealed class WorldTerrain
{
    /// <summary>The active terrain (null = flat sea-level world, which is what the unit tests assume).</summary>
    public static WorldTerrain? Active { get; set; }

    /// <summary>Ground height (m, up) at a sim x/y — 0 everywhere when no terrain is active.</summary>
    public static double GroundHeightAt(double x, double y) => Active?.HeightAt(x, y) ?? 0.0;

    // ---- layout ---------------------------------------------------------------------------------
    public const double StepHeightM = 457.2;       // 1,500 ft a step
    public const double StepSpacingM = 4200.0;     // wall to wall: room for the whole Valley layout on every plateau
    public const double FirstEdgeY = -1400.0;      // mean y of the first escarpment (walls run along x)
    public const double EscarpmentWidthM = 183.0;  // horizontal run of one wall (talus + cliff) — same profile as the old 900 m wall
    public const int StepCount = 3;
    /// <summary>Each upper airport sits this far west of its plateau's east (lower) edge, so the landscape laid out
    /// around the Valley airport (tower/arch 1 km west of it, race course 2.1 km east) fits between the walls.</summary>
    public const double PlateauAirportOffsetM = 2600.0;

    public readonly struct Airport
    {
        public readonly string Name; public readonly double X, Y, ElevationM;
        public Airport(string name, double x, double y, double elev) { Name = name; X = x; Y = y; ElevationM = elev; }
    }

    /// <summary>Runway centre (x,y) and field elevation for the four airports; runways run along +x.</summary>
    public static readonly Airport[] Airports =
    {
        new("Valley", 400, 0, 0),
        new("Bench", 400, FirstEdgeY - PlateauAirportOffsetM, StepHeightM),
        new("Mesa", 400, FirstEdgeY - StepSpacingM - PlateauAirportOffsetM, StepHeightM * 2),
        new("Summit", 400, FirstEdgeY - StepSpacingM * 2 - PlateauAirportOffsetM, StepHeightM * 3),
    };

    /// <summary>Number of plateaus (one per airport); the Valley is plateau 0.</summary>
    public static int PlateauCount => Airports.Length;

    /// <summary>y shift that carries a Valley-plateau feature onto plateau <paramref name="p"/> (airports share x).</summary>
    public static double PlateauDy(int p) => Airports[System.Math.Clamp(p, 0, Airports.Length - 1)].Y - Airports[0].Y;

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
            l[i] = new Lake(a.X + 2600, a.Y + 600, 500, 380, a.ElevationM - 2.0);   // beyond the gorge, closer in
        }
        return l;
    }

    // River: runs downhill west→east across the steps in a GORGE, meandering gently in x (minimum turn
    // radius ≈ 2 km so the gorge can be flown at speed), passing the lakes' west shores.
    public const double RiverHalfWidthM = 35.0;
    public static double RiverCentreX(double y) => 1900.0 + 200.0 * System.Math.Sin(y / 900.0) + 60.0 * System.Math.Sin(y / 520.0 + 1.1);   // ~500 m past the runway end (owner: closer in)

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
        // At a plunge fall the drop is exactly AT the lip, not at the nearest table cell: sample the table
        // one full cell clear of the lip on whichever side y is.
        foreach (Waterfall wf in Waterfalls)
        {
            if (System.Math.Abs(y - wf.LipY) < RiverTableStep)
            {
                y = y < wf.LipY ? wf.LipY - RiverTableStep - 1 : wf.LipY + RiverTableStep + 1;
                break;
            }
        }
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
    public const double RiverTableStepPublic = RiverTableStep;   // renderer: sample clear of a lip
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
            double rim = BaseHeightAt(RiverCentreX(y), y, shelfTop: true);
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

    /// <summary>Same at every airport, all PARALLEL (along x): paved main, gravel STOL strip east, grass strip west.
    /// (The crossing runway is gone — with the wind always onto the ridge every landing is a crosswind.)</summary>
    public static readonly Strip[] AirportStrips =
    {
        new("paved", 0, 0, RunwayLengthM, RunwayWidthM, 0),
        new("gravel", 100, 420, 600, 15, 0),
        new("grass", -100, -420, 750, 20, 0),
    };

    /// <summary>Hangar centre offset from the airport centre (long axis along x, doors open both ends).</summary>
    public const double HangarDx = -450, HangarDy = 200;
    /// <summary>Paved apron around the hangar (centre offset and size); the rest of the pad is rough ground.</summary>
    public const double ApronDx = HangarDx + 60, ApronDy = HangarDy - 30, ApronLengthM = 320, ApronWidthM = 260;

    // ---- ground surfaces (what the wheels feel) -------------------------------------------------

    public enum Surface { Paved, Gravel, Grass, Rough }

    /// <summary>Rolling-resistance coefficient (μ_r = rolling force / wheel load) per surface. Values from
    /// published aircraft ground-roll data (ESDU 71026, Raymer Table 17.1, FAA): dry concrete/asphalt
    /// 0.02–0.03; firm gravel 0.04–0.06; short firm grass 0.05–0.08; rough pasture / ploughed / soft
    /// ground 0.10–0.30.</summary>
    public static double RollingCoefficient(Surface s) => s switch
    {
        Surface.Paved => 0.025,
        Surface.Gravel => 0.05,
        Surface.Grass => 0.065,
        _ => 0.15,
    };

    /// <summary>Which surface lies under (x, y): a strip or the apron inside an airport pad, otherwise ROUGH
    /// (rolling pasture / ploughed field — off-airport landings are a bumpy, high-drag affair).</summary>
    public static Surface SurfaceAt(double x, double y)
    {
        foreach (Airport a in Airports)
        {
            if (System.Math.Abs(x - a.X) > PadHalfX + 50 || System.Math.Abs(y - a.Y) > PadHalfY + 50) continue;
            foreach (Strip st in AirportStrips)
            {
                if (InStrip(a, st, x, y, 1.0)) return st.Kind switch { "paved" => Surface.Paved, "gravel" => Surface.Gravel, _ => Surface.Grass };
            }
            if (System.Math.Abs(x - (a.X + ApronDx)) <= ApronLengthM / 2 && System.Math.Abs(y - (a.Y + ApronDy)) <= ApronWidthM / 2) return Surface.Paved;
        }
        return Surface.Rough;
    }

    /// <summary>True inside the strip's rectangle (grown by `margin` m on every side).</summary>
    public static bool InStrip(Airport a, Strip st, double x, double y, double margin)
    {
        double h = st.HeadingDeg * System.Math.PI / 180, c = System.Math.Cos(h), sn = System.Math.Sin(h);
        double dx = x - (a.X + st.Dx), dy = y - (a.Y + st.Dy);
        double along = dx * c + dy * sn, across = -dx * sn + dy * c;
        return System.Math.Abs(along) <= st.Length / 2 + margin && System.Math.Abs(across) <= st.Width / 2 + margin;
    }

    /// <summary>The grass strip's "Snoopy swoop": smooth flowing undulations along the strip (two long sine
    /// waves, 0..0.5 m, never below the pad), fading out across the strip's edges. Zero elsewhere.</summary>
    public static double GrassSwoopAt(double x, double y)
    {
        foreach (Airport a in Airports)
        {
            foreach (Strip st in AirportStrips)
            {
                if (st.Kind != "grass" || !InStrip(a, st, x, y, 6.0)) continue;
                double along = x - (a.X + st.Dx), across = System.Math.Abs(y - (a.Y + st.Dy));
                double edge = System.Math.Clamp((st.Width / 2 + 6.0 - across) / 6.0, 0.0, 1.0);   // 1 on the strip, 0 six metres outside
                edge = edge * edge * (3 - 2 * edge);
                double w = 0.5 * (1 + System.Math.Sin(2 * System.Math.PI * along / 70.0)) * 0.30
                         + 0.5 * (1 + System.Math.Sin(2 * System.Math.PI * along / 165.0 + 1.0)) * 0.20;
                return w * edge;
            }
        }
        return 0.0;
    }

    /// <summary>Small-scale surface roughness the WHEELS feel (not in the terrain mesh): ~0.15 m lumps every
    /// few metres on rough ground (bounce, and at speed a possible nose-over), a 1–2 cm rattle on gravel.</summary>
    public static double MicroBumpAt(double x, double y)
    {
        Surface s = SurfaceAt(x, y);
        if (s == Surface.Rough)
        {
            return 0.10 * System.Math.Sin(x / 1.3) * System.Math.Sin(y / 1.7) + 0.06 * System.Math.Sin(x / 0.7 + y / 0.9) + 0.06;
        }
        if (s == Surface.Gravel)
        {
            return 0.012 * System.Math.Sin(x / 0.23) * System.Math.Sin(y / 0.31);
        }
        return 0.0;
    }

    /// <summary>Ground height under a WHEEL: the height field plus the surface micro-roughness.</summary>
    public static double WheelGroundHeightAt(double x, double y) => GroundHeightAt(x, y) + (Active != null ? MicroBumpAt(x, y) : 0.0);

    /// <summary>STOL contest on the dirt strip: landing line this far from the strip's south (−x) end; markers beyond it.</summary>
    public const double StolLineFromThresholdM = 150.0, StolMarkedLengthM = 250.0;

    // ---- plunge waterfalls at the canyon walls (owner 2026-09-08) --------------------------------
    // Where the river crosses each escarpment it used to slide down the 78° face. Now each crossing is a
    // PLUNGE fall: a straight lip, the water leaving it in free fall, and the cliff behind cut back into a
    // concave amphitheater (FallRecessM at the river centre, ≥ 150 ft across the river's width) so an
    // aircraft can fly along the wall BETWEEN the curtain and the rock. The river reaches the lip on an
    // overhanging rock SHELF (a solid the airframe collides with); the ground under the shelf and the
    // whole notch downstream of it are at the lower plateau.

    public const double FallRecessM = 60.0;          // back-wall set-back behind the lip at the river centre (≈ 197 ft)
    public const double FallNotchHalfSpanM = 300.0;  // half-width (x) of the amphitheater notch
    public const double FallShelfThickM = 12.0;      // rock under the upper river bed
    public const double FallLipSpeedMs = 4.0;        // water speed leaving the lip (sets the curtain's arc)

    public readonly struct Waterfall
    {
        public readonly int Step;          // escarpment index 0..2
        public readonly double X;          // river centre x at the lip
        public readonly double LipY;       // y of the (straight) lip; water falls toward +y
        public readonly double UpperM;     // plateau level above the wall
        public readonly double LowerM;     // plateau level below the wall
        public Waterfall(int step, double x, double lipY, double upper, double lower) { Step = step; X = x; LipY = lipY; UpperM = upper; LowerM = lower; }
    }

    private Waterfall[]? _falls;
    public Waterfall[] Waterfalls => _falls ??= BuildWaterfalls();

    private static Waterfall[] BuildWaterfalls()
    {
        var f = new Waterfall[StepCount];
        for (int i = 0; i < StepCount; i++)
        {
            // The lip is the TOP of the wall (edge - run); the edge wanders with x and the river with y —
            // iterate to the crossing, then freeze the lip straight across the notch.
            double y = EdgeMeanY(i) - EscarpmentWidthM, x = RiverCentreX(y);
            for (int k = 0; k < 40; k++)
            {
                x = RiverCentreX(y);
                double target = EdgeMeanY(i) + EdgeWander(i, x) - EscarpmentWidthM;
                y += 0.5 * (target - y);
            }
            f[i] = new Waterfall(i, x, y, StepHeightM * (i + 1), StepHeightM * i);
        }
        return f;
    }

    /// <summary>Notch weight 0..1 at x for fall f: 1 inside the notch, fading to 0 over a band beyond it.</summary>
    private static double NotchWeight(in Waterfall f, double x)
    {
        double u = System.Math.Abs(x - f.X) / FallNotchHalfSpanM;
        if (u <= 1) return 1;
        if (u >= 1.5) return 0;
        double v = (u - 1) / 0.5;
        return 1 - v * v * (3 - 2 * v);
    }

    /// <summary>Back-wall set-back behind the lip at x (m): full at the centre, rounding off at the notch ends —
    /// a gently concave plan so the flyable slot is ≥ 46 m (150 ft) deep across the whole river and most of the notch.</summary>
    public static double FallRecessAt(in Waterfall f, double x)
    {
        double u = System.Math.Abs(x - f.X) / FallNotchHalfSpanM;
        if (u >= 1) return 0;
        return FallRecessM * System.Math.Pow(1 - u * u, 0.25);
    }

    /// <summary>Under-side of the shelf at fall f (m, up): the upper river bed minus the rock thickness.</summary>
    public double FallShelfBottomM(in Waterfall f) => RiverSurfaceAt(f.LipY - 1.0) - 4.0 - FallShelfThickM;

    /// <summary>Register the shelf overhangs as collision solids (call after the landmarks reset the list).</summary>
    public void RegisterWaterfallSolids()
    {
        foreach (Waterfall f in Waterfalls)
        {
            double backY = f.LipY - FallRecessM;
            WorldSolids.Boxes.Add(new WorldSolids.Box(f.X, (backY + f.LipY) * 0.5, FallNotchHalfSpanM, (f.LipY - backY) * 0.5, FallShelfBottomM(f), f.UpperM + 1.0));
        }
    }

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
    public double BaseHeightAt(double x, double y) => BaseHeightAt(x, y, false);

    /// <summary>
    /// Plateau/escarpment height. <paramref name="shelfTop"/> = false: the real terrain, with each waterfall's
    /// amphitheater notch cut in (vertical concave back wall FallRecessAt behind the lip, lower plateau in front
    /// of it). true: the surface the rock SHELF carries — the same wall but dropping vertically AT the lip, with
    /// no recess (used for the river surface table and the shelf mesh).
    /// </summary>
    public double BaseHeightAt(double x, double y, bool shelfTop)
    {
        double h = 0;
        Waterfall[] falls = Waterfalls;
        for (int i = 0; i < StepCount; i++)
        {
            double edge = EdgeMeanY(i) + EdgeWander(i, x);
            double t = (edge - y) / EscarpmentWidthM;   // grows as you go west past the edge
            double p = WallProfile(t);
            double w = NotchWeight(falls[i], x);
            if (w > 0)
            {
                // Straight lip, vertical wall: 1 above (west of) the back wall, 0 in the notch.
                double backY = falls[i].LipY - (shelfTop ? 0.0 : FallRecessAt(falls[i], x));
                double step = y <= backY ? 1.0 : 0.0;
                p += (step - p) * w;
            }
            h += StepHeightM * p;
            // Rock texture on the wall face: small ledges (not inside the notch — the face there is sheer).
            if (t > 0 && t < 1 && w < 0.5) h += 6.0 * System.Math.Sin(x / 23.0) * System.Math.Sin(y / 17.0);
        }
        return h;
    }

    /// <summary>Ground height (m, up) including flat airport pads and the lake / river beds.</summary>
    public double HeightAt(double x, double y) => HeightAt(x, y, false);

    /// <summary>Ground height; <paramref name="shelfTop"/> = true gives the un-recessed surface the waterfall shelf carries.</summary>
    public double HeightAt(double x, double y, bool shelfTop)
    {
        double h = BaseHeightAt(x, y, shelfTop);

        // Airport pads: exactly flat at field elevation inside the pad, blended over 300 m outside.
        // Not inside a waterfall notch (real terrain): the Bench pad's corner overhangs the first fall's
        // amphitheater — the rock SHELF carries it (shelfTop surface); the ground below is the lower plateau.
        bool inNotch = false;
        if (!shelfTop)
        {
            foreach (Waterfall wf in Waterfalls)
            {
                if (NotchWeight(wf, x) > 0 && y > wf.LipY - FallRecessAt(wf, x)) { inNotch = true; break; }
            }
        }
        foreach (Airport a in Airports)
        {
            if (inNotch) break;
            double dx = System.Math.Max(0, System.Math.Abs(x - a.X) - PadHalfX);
            double dy = System.Math.Max(0, System.Math.Abs(y - a.Y) - PadHalfY);
            double d = System.Math.Sqrt(dx * dx + dy * dy);
            if (d < 300)
            {
                double w = 1 - d / 300; w = w * w * (3 - 2 * w);
                h = h + (a.ElevationM - h) * w;
            }
        }

        // Grass strip undulations (smooth swoops, on top of the flat pad).
        h += GrassSwoopAt(x, y);

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
