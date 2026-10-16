namespace FlyingGame.Core;

/// <summary>
/// The world's ground and water, as ONE analytic height field shared by the sim (landing gear, floats)
/// and the renderer (terrain mesh) so what you see is what the wheels touch. Sim frame: x north,
/// y east, altitude UP (metres); callers convert to NED z as needed.
///
/// Layout (owner request): the base airport sits on a coastal tableland. To the WEST the land rises
/// in three steep, irregular escarpments — canyon-wall steps, not straight lines — each to a plateau
/// with its own airport, progressively higher for density-altitude work. Owner 2026-10-16: "keep the climb
/// from the coast to ~7,000 ft … airports at 3,000 / 5,000 / 7,000 ft for density-altitude practice" — so a
/// step is 2,000 ft (it was 1,500, which topped out at 5,500):
///   A0 Valley 1,000 ft  ·  A1 Bench 3,000 ft  ·  A2 Mesa 5,000 ft  ·  A3 Summit 7,000 ft.
/// A 7,000 ft field is the one that teaches the lesson: a Cub on a warm afternoon up there has a density
/// altitude near 10,000 ft and climbs at a couple of hundred feet a minute, which no amount of being told
/// conveys. Every plateau carries SOME of the Valley landscape — airport, lake, bridge, crop field, aerobatic
/// box, race course, arch, tower and town — shifted by <see cref="PlateauDy"/> in y and up to the plateau's
/// elevation. The river winds down the steps past all four airports (waterfalls at each wall).
/// </summary>
public sealed class WorldTerrain
{
    /// <summary>The active terrain (null = flat sea-level world, which is what the unit tests assume).</summary>
    public static WorldTerrain? Active { get; set; }

    /// <summary>Ground height (m, up) at a sim x/y — 0 everywhere when no terrain is active.</summary>
    public static double GroundHeightAt(double x, double y) => Active?.HeightAt(x, y) ?? 0.0;

    // ---- layout ---------------------------------------------------------------------------------
    public const double StepHeightM = 609.6;       // 2,000 ft a step (owner 2026-10-16: the top field at 7,000 ft)
    /// <summary>The Valley plain's height above SEA LEVEL (owner 2026-10-03: a coast and an ocean). The gorge is ~150 m
    /// deep in the Valley, so with the plain at sea level its river ran 150 m BELOW the sea; the whole landscape sits on
    /// a 500 ft (152.4 m) coastal tableland instead, and the river reaches the ocean at sea level through a cleft in
    /// the sea cliffs — the Golden Gate's own geography.</summary>
    /// <summary>Owner 2026-10-05: "the elevation of the first airport will have to be about 1,000 ft in order to build the
    /// gorge" — the tableland at 1,000 ft (304.8 m) over the sea: a Grand-Canyon-deep gorge down to the sea, 1,000 ft sea cliffs.</summary>
    public const double DatumM = 304.8;
    // Owner 2026-10-05: "tighten it up even more, bring everything closer together … it takes a long time to get from
    // feature to feature". The bluff 1.05 km west of the field (was 1.4), plateaus 3.3 km apart (were 4.2).
    public const double StepSpacingM = 3300.0;     // wall to wall: room for the whole Valley layout on every plateau
    public const double FirstEdgeY = -1050.0;      // mean y of the first escarpment (walls run along x)
    public const double EscarpmentWidthM = 183.0;  // horizontal run of one wall (talus + cliff) — same profile as the old 900 m wall
    public const int StepCount = 3;
    /// <summary>Each upper airport sits this far west of its plateau's east (lower) edge, so the landscape laid out
    /// around the Valley airport (tower/arch 1 km west of it, race course 2.1 km east) fits between the walls.</summary>
    public const double PlateauAirportOffsetM = 2400.0;

    public readonly struct Airport
    {
        public readonly string Name; public readonly double X, Y, ElevationM;
        public Airport(string name, double x, double y, double elev) { Name = name; X = x; Y = y; ElevationM = elev; }
    }

    /// <summary>Runway centre (x,y) and field elevation for the four airports; runways run along +x.</summary>
    public static readonly Airport[] Airports =
    {
        new("Valley", 400, 0, DatumM),                                                                      // 1,000 ft
        new("Bench", 400, FirstEdgeY - PlateauAirportOffsetM, DatumM + StepHeightM),                        // 3,000 ft
        new("Mesa", 400, FirstEdgeY - StepSpacingM - PlateauAirportOffsetM, DatumM + StepHeightM * 2),      // 5,000 ft
        new("Summit", 400, FirstEdgeY - StepSpacingM * 2 - PlateauAirportOffsetM, DatumM + StepHeightM * 3), // 7,000 ft
    };

    /// <summary>Number of plateaus (one per airport); the Valley is plateau 0.</summary>
    public static int PlateauCount => Airports.Length;

    /// <summary>y shift that carries a Valley-plateau feature onto plateau <paramref name="p"/> (airports share x).</summary>
    public static double PlateauDy(int p) => Airports[System.Math.Clamp(p, 0, Airports.Length - 1)].Y - Airports[0].Y;

    /// <summary>Main paved runway along x, centred on Airport.X: 2,400 m / 7,874 ft (owner 2026-10-07: "make the landing runway
    /// longer — I have gone off the end in light airplanes and a 737 would certainly go off the end"; was 1,500 m). Fits the
    /// pad (±1,250 m).</summary>
    public const double RunwayLengthM = 2400.0;
    public const double XwindRunwayLengthM = 1125.0;
    /// <summary>Main runway centre along x from the airport: the north end stays where the 1,500 m runway's was (a.X + 750 m,
    /// 130–300 m short of the gorge rim); the extra length runs south.</summary>
    public const double MainRunwayDx = 750.0 - RunwayLengthM / 2;   // the crosswind runway keeps its length (75 % of the old main; it spans the pad's y)
    public const double RunwayWidthM = 30.0;
    // Every airport has the same layout inside a flat pad: two crossing paved runways, an open hangar,
    // a dirt strip (STOL contest) and a grass strip. Local offsets are in AirportLayout.
    public const double PadHalfX = 1250.0, PadHalfY = 850.0;
    /// <summary>The pad reaches further SOUTH than north (2026-10-07): the 2,400 m main runway grew southward — its north end
    /// stays clear of the gorge rim.</summary>
    public const double PadSouthX = 1700.0;
    /// <summary>Distance outside the pad along x (0 inside).</summary>
    public static double PadOutsideX(Airport a, double x) { double rx = x - a.X; return rx >= 0 ? System.Math.Max(0, rx - PadHalfX) : System.Math.Max(0, -rx - PadSouthX); }

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
        var l = new Lake[Airports.Length + 1];
        for (int i = 0; i < Airports.Length; i++)
        {
            Airport a = Airports[i];
            l[i] = new Lake(a.X - 3000, a.Y - 450, 450, 300, a.ElevationM - 2.0);   // south-west of the field, past the tower (2026-10-05)
        }
        // The HARBOR (owner 2026-10-02: Hughes H-4) — a 300,000 lb flying boat needs ~2.2 km of water. Always the last lake.
        // Moved OUT TO SEA (owner 2026-10-03, the coast): the sheltered channel between the coast and the island, a
        // 3.2 km x 0.9 km stretch of open water running north–south.
        l[Airports.Length] = new Lake(600, Coast.MeanShoreY + 1850, 1600, 450, Coast.SeaLevelM);
        return l;
    }

    /// <summary>The big bay flying boats operate from (the last entry of <see cref="Lakes"/>).</summary>
    public static Lake Harbor => Lakes[Lakes.Length - 1];

    // ---- Seaplane water lanes (owner 2026-10-07: "make the seaplanes land on water but put two rows of buoys on the water
    // like runway edges so the user can better judge height"): one on the Valley lake, one in the harbor (the H-4 needs
    // its 2.4 km). Each is a "water" strip along x through the lake centre; the buoy rows mark its edges.
    public const double SeaplaneLaneWidthM = 60.0, SeaplaneBuoySpacingM = 50.0;
    public readonly struct SeaplaneLane
    {
        public readonly Lake Lake; public readonly double CentreX, CentreY, LengthM;
        public SeaplaneLane(Lake lake, double lengthM) { Lake = lake; CentreX = lake.Cx; CentreY = lake.Cy; LengthM = lengthM; }
        public Strip Strip => new("water", 0, 0, LengthM, SeaplaneLaneWidthM, 0);
        public double SurfaceM => Lake.SurfaceM;
    }
    /// <summary>[0] the Valley lake (~765 m), [1] the harbor (2.4 km).</summary>
    public static readonly SeaplaneLane[] SeaplaneLanes =
    {
        new(Lakes[0], 0.85 * 2 * Lakes[0].Rx),
        new(Lakes[Lakes.Length - 1], 2400.0),
    };
    /// <summary>The lane end into the wind (or the other end for a downwind practice): heading 0 = north (+x).</summary>
    public static RunwayEnd ChooseSeaplaneLane(SeaplaneLane lane, double windFromRad, bool headwind, double windSpeedMs = 1.0)
    {
        if (windSpeedMs < 0.3) windFromRad = 0;
        var n = new RunwayEnd(lane.Strip, 0, lane.CentreX, lane.CentreY);
        var sth = new RunwayEnd(lane.Strip, System.Math.PI, lane.CentreX, lane.CentreY);
        bool northBetter = n.HeadwindFactor(windFromRad) >= sth.HeadwindFactor(windFromRad);
        return northBetter == headwind ? n : sth;
    }

    // ---- Seaplane base on the Valley lake's east shore (owner 2026-10-07: "a seaplane base with docks that do not have
    // pilings and a ramp for the seaplanes to go up/down with a large parking area and a hangar"). A concrete ramp runs
    // from 1.5 m under the water up to a flat parking pad; floating docks lie beside it; the hangar sits at the back.
    public static Lake SeaplaneBaseLake => Lakes[0];
    public const double BaseRampHalfWidthM = 12.0, BaseRampWaterEndM = 55.0, BasePadHalfXM = 130.0, BasePadDepthM = 170.0, BasePadAboveWaterM = 1.0;
    /// <summary>The base is on the lake's WEST shore (inland = −y): the east shore lies under the extended centreline of the
    /// main runway (the hangar sat in the final approach corridor).</summary>
    public const double BaseInland = -1.0;
    /// <summary>Where the shoreline meets the ramp (lake centre x, the west shore y).</summary>
    public static (double x, double y) BaseShore => (SeaplaneBaseLake.Cx, SeaplaneBaseLake.Cy + BaseInland * SeaplaneBaseLake.Ry);
    public static double BasePadHeightM => SeaplaneBaseLake.SurfaceM + BasePadAboveWaterM;
    public const double BaseHangarDy = 120.0, BaseHangarHalfX = 22.0, BaseHangarHalfY = 18.0, BaseHangarHeightM = 11.0;

    /// <summary>The base's ground override (ramp + pad), or null outside it.</summary>
    public static double? SeaplaneBaseHeightAt(double x, double y, double terrainH)
    {
        var (sx, sy) = BaseShore;
        double dx = x - sx, dy = (y - sy) * BaseInland;   // dy > 0: ashore
        // Ramp: a straight slope from 1.5 m under the water (BaseRampWaterEndM out) to the pad edge (8 m ashore).
        if (System.Math.Abs(dx) <= BaseRampHalfWidthM && dy >= -BaseRampWaterEndM && dy <= 8.0)
        {
            double f = (dy + BaseRampWaterEndM) / (BaseRampWaterEndM + 8.0);
            return SeaplaneBaseLake.SurfaceM - 1.5 + f * (BasePadAboveWaterM + 1.5);
        }
        // Pad: flat, from 8 m ashore back BasePadDepthM, blended to the terrain over 60 m (never into the lake).
        if (dy < 0) return null;
        double ex = System.Math.Max(0, System.Math.Abs(dx) - BasePadHalfXM);
        double ey = System.Math.Max(0, System.Math.Max(8.0 - dy, dy - (8.0 + BasePadDepthM)));
        double d = System.Math.Sqrt(ex * ex + ey * ey);
        if (d >= 60) return null;
        double w = 1 - d / 60; w = w * w * (3 - 2 * w);
        return terrainH + (BasePadHeightM - terrainH) * w;
    }

    // River: runs downhill west→east across the steps in a GORGE, meandering gently in x (minimum turn
    // radius ≈ 2 km so the gorge can be flown at speed), passing the lakes' west shores.
    public const double RiverHalfWidthM = 35.0;
    public static double RiverCentreX(double y) => 1600.0 + 120.0 * System.Math.Sin(y / 900.0) + 60.0 * System.Math.Sin(y / 520.0 + 1.1)   // owner 2026-10-05: the runway closer to the river (rim ~300 m past its end)
                                                  + TwistAt(y);
    /// <summary>Owner 2026-10-03: "make the canyon have more twists and turns" — past the race course the gorge snakes
    /// through the combat zone to the sea: S-bends every ~850 m, centreline turn radius ≥ ~190 m (a 45° bank turn at
    /// 100 kt is 265 m, and the gorge is ~300 m wide — fly the inside line).</summary>
    /// Owner 2026-10-05: "make the river gorge more like the Grand Canyon, deep with sharp turns" — horseshoe bends every
    /// ~1.35 km swinging NORTH only (away from the field, the race course and the crop field), centreline radius ~185 m.
    public static double TwistAt(double y)
    {
        const double y0 = TwistStartY;
        if (y <= y0) return 0;
        double ramp = System.Math.Clamp((y - y0) / 500.0, 0, 1); ramp = ramp * ramp * (3 - 2 * ramp);
        double k = 2 * System.Math.PI / 1350.0;
        return ramp * (450.0 * (0.5 - 0.5 * System.Math.Cos(k * (y - y0))) + 15.0 * System.Math.Sin((y - y0) / 300.0 + 0.8));
    }
    public const double TwistStartY = 600.0;

    // Gorge: the river cuts a canyon whose depth grows downstream (west→east) from ~15 m (50 ft) on
    // the Summit plateau to ~150 m (500 ft) in the Valley, with a staircase of waterfalls along the way
    // (on top of the big drops at the canyon walls).
    public const double GorgeDepthUpstreamM = 15.0, GorgeDepthDownstreamM = 306.0;   // down to (just under) the sea from the 1,000 ft tableland
    public const double GorgeUpstreamY = -11500.0, GorgeDownstreamY = 3400.0;
    public const double BridgeY = -150.0;   // road bridge abeam the Valley airport

    /// <summary>Gorge depth (m) below the surrounding ground at along-flow position y.</summary>
    public static double GorgeDepthAt(double y)
    {
        double t = System.Math.Clamp((y - GorgeUpstreamY) / (GorgeDownstreamY - GorgeUpstreamY), 0, 1);
        return GorgeDepthUpstreamM + (GorgeDepthDownstreamM - GorgeDepthUpstreamM) * t;
    }

    /// <summary>Half-width of the gorge at the rim (m): walls near-vertical, floor a little wider than the river.</summary>
    public static double GorgeHalfWidthAt(double y) => RiverHalfWidthM + 25 + 0.5 * GorgeDepthAt(y) + EstuaryWidening(y);   // steep: Grand Canyon, not a valley

    /// <summary>The gorge opens out over its last kilometre into the strait the bridge spans (up to +230 m a side).</summary>
    public static double EstuaryWidening(double y)
    {
        double mouth = MouthY;
        double t = System.Math.Clamp((y - (mouth - 1100)) / 1100, 0, 1);
        return 230 * t * t * (3 - 2 * t);
    }

    /// <summary>The river (and its gorge) ends at the coast — beyond is the sea floor.</summary>
    public static bool PastRiverMouth(double y) => y > MouthY + 60;

    /// <summary>Where the river meets the coast: the FIRST y downstream at which its centreline reaches the shore (a scan —
    /// with sharp bends near the coast the old fixed-point iteration didn't converge).</summary>
    public static double MouthY => _mouthY ??= FindMouth();
    private static double? _mouthY;
    private static double FindMouth()
    {
        for (double y = 0; y < Coast.MeanShoreY + 3000; y += 5)
            if (y >= Coast.ShoreY(RiverCentreX(y))) return y;
        return Coast.MeanShoreY;
    }
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

    /// <summary>Same at every airport: paved main (along x), gravel STOL strip east, grass strip west — and (owner
    /// 2026-09-10) a shorter paved CROSSWIND runway across the north half of the main, along y = 09/27, straight into
    /// the default east wind, 75 % of the main's length.</summary>
    public static readonly Strip[] AirportStrips =
    {
        new("paved", MainRunwayDx, 0, RunwayLengthM, RunwayWidthM, 0),
        new("paved-xwind", XwindRunwayDx, 0, XwindRunwayLengthM, 25, 90),
        new("gravel", 100, 420, 600, 15, 0),
        new("grass", -100, -420, 750, 20, 0),
    };
    /// <summary>Crossing point of the into-wind runway along the main (north of the gravel/grass strips' ends).</summary>
    public const double XwindRunwayDx = 450;
    public static bool IsPaved(string kind) => kind.StartsWith("paved");
    public const double ThresholdSetbackM = 80.0;   // where a runway start / tow hookup sits in from the threshold

    /// <summary>One end of a paved runway to use: heading flown, centre, length, and the start point just in from
    /// the threshold. Owner 2026-09-10: the user picks HEADWIND (the runway end best aligned into the wind) or
    /// CROSSWIND (the other runway) at launch, for the runway start, the on-final start and the tow.</summary>
    public readonly struct RunwayEnd
    {
        public readonly Strip Strip; public readonly double HeadingRad, CentreX, CentreY;
        public RunwayEnd(Strip strip, double headingRad, double cx, double cy) { Strip = strip; HeadingRad = headingRad; CentreX = cx; CentreY = cy; }
        public double LengthM => Strip.Length;
        public double AlongX => System.Math.Cos(HeadingRad);
        public double AlongY => System.Math.Sin(HeadingRad);
        /// <summary>The threshold being flown toward from the start end (x, y).</summary>
        public (double x, double y) Threshold => (CentreX - AlongX * LengthM / 2, CentreY - AlongY * LengthM / 2);
        /// <summary>Runway start point: just in from the threshold, on the centreline.</summary>
        public (double x, double y) Start => (CentreX - AlongX * (LengthM / 2 - ThresholdSetbackM), CentreY - AlongY * (LengthM / 2 - ThresholdSetbackM));
        /// <summary>Point along the centreline: d metres past the threshold, s metres right of it.</summary>
        public (double x, double y) At(double d, double side = 0) => (Threshold.x + AlongX * d - AlongY * side, Threshold.y + AlongY * d + AlongX * side);
        /// <summary>Headwind component for a wind FROM <paramref name="windFromRad"/> (unit wind).</summary>
        public double HeadwindFactor(double windFromRad) => System.Math.Cos(HeadingRad - windFromRad);
    }

    /// <summary>The paved runway end to use: headwind = the end of any paved runway best aligned into the wind;
    /// crosswind = the other paved runway, its end with the lesser tailwind. Calm wind: main runway, north.</summary>
    public static RunwayEnd ChooseRunway(Airport a, double windFromRad, bool headwind, double windSpeedMs = 1.0)
    {
        var ends = new List<RunwayEnd>();
        foreach (Strip st in AirportStrips)
        {
            if (!IsPaved(st.Kind)) continue;
            double h = st.HeadingDeg * System.Math.PI / 180;
            ends.Add(new RunwayEnd(st, h, a.X + st.Dx, a.Y + st.Dy));
            ends.Add(new RunwayEnd(st, h + System.Math.PI, a.X + st.Dx, a.Y + st.Dy));
        }
        if (windSpeedMs < 0.3) { windFromRad = 0; }   // calm: "into the wind" means north on the main
        RunwayEnd best = ends[0]; double bestF = double.MinValue;
        foreach (RunwayEnd e in ends) { double f = e.HeadwindFactor(windFromRad); if (f > bestF + 1e-9) { bestF = f; best = e; } }
        if (headwind || ends.Count <= 2) return headwind ? best : Other(ends, best, windFromRad);
        return Other(ends, best, windFromRad);
    }

    private static RunwayEnd Other(List<RunwayEnd> ends, RunwayEnd best, double windFromRad)
    {
        RunwayEnd pick = best; double bestF = double.MinValue; bool found = false;
        foreach (RunwayEnd e in ends)
        {
            if (e.Strip.Kind == best.Strip.Kind) continue;
            double f = e.HeadwindFactor(windFromRad);
            if (!found || f > bestF + 1e-9) { bestF = f; pick = e; found = true; }
        }
        if (!found)   // a single paved runway: the crosswind choice is its downwind end
            foreach (RunwayEnd e in ends) if (e.Strip.Kind == best.Strip.Kind && System.Math.Abs(e.HeadingRad - best.HeadingRad) > 0.1) pick = e;
        return pick;
    }

    /// <summary>Hangar centre offset from the airport centre (long axis along x, doors open both ends).</summary>
    public const double HangarDx = -450, HangarDy = 200;
    /// <summary>Paved apron around the hangar (centre offset and size); the rest of the pad is rough ground.</summary>
    public const double ApronDx = HangarDx + 60, ApronDy = HangarDy - 30, ApronLengthM = 320, ApronWidthM = 260;

    // ---- ground surfaces (what the wheels feel) -------------------------------------------------

    public enum Surface { Paved, Gravel, Grass, Rough, Snow }

    /// <summary>Rolling-resistance coefficient (μ_r = rolling force / wheel load) per surface. Values from
    /// published aircraft ground-roll data (ESDU 71026, Raymer Table 17.1, FAA): dry concrete/asphalt
    /// 0.02–0.03; firm gravel 0.04–0.06; short firm grass 0.05–0.08; rough pasture / ploughed / soft
    /// ground 0.10–0.30.</summary>
    public static double RollingCoefficient(Surface s) => s switch
    {
        Surface.Paved => 0.025,
        Surface.Gravel => 0.05,
        Surface.Grass => 0.065,
        // Settled snow on a glacier: a wheel sinks into it and drags (0.10–0.20 in the literature for wheels
        // on snow); a SKI is a different matter entirely and gets its own treatment.
        Surface.Snow => 0.14,
        _ => 0.15,
    };

    /// <summary>KINETIC friction of a float keel (aluminium float bottom with a steel/aluminium keel skid strip) SLIDING on
    /// the surface (owner 2026-10-03). Published sliding coefficients: metal on dry asphalt/concrete 0.5-0.6 (Engineering
    /// ToolBox: metal-concrete 0.3-0.6; aluminium-asphalt ~0.5-0.6) -> 0.55; gravel ~0.5; grass 0.3-0.4 dry, ~0.2 wet
    /// (why floatplanes use wet grass) -> 0.35; ploughed/rough ground ~0.5. A Beaver sliding on at 50 kt stops in ~60 m on
    /// pavement, ~90 m on grass.</summary>
    public static double KeelSlidingCoefficient(Surface s) => s switch
    {
        Surface.Paved => 0.55,
        Surface.Gravel => 0.50,
        Surface.Grass => 0.35,
        Surface.Snow => 0.10,          // a keel on snow slides like a ski, because that is what it is
        _ => 0.50,
    };

    /// <summary>Test hook: force every surface query to one answer, for a test about a SURFACE rather than
    /// about a place (the wheel-ski tests want snow, not a particular glacier). Null in the game, always,
    /// and every test that sets it puts it back.</summary>
    public static Surface? SurfaceOverride { get; set; }

    /// <summary>Which surface lies under (x, y): a strip or the apron inside an airport pad, otherwise ROUGH
    /// (rolling pasture / ploughed field — off-airport landings are a bumpy, high-drag affair).</summary>
    public static Surface SurfaceAt(double x, double y)
    {
        if (SurfaceOverride.HasValue) return SurfaceOverride.Value;
        foreach (Airport a in Airports)
        {
            if (PadOutsideX(a, x) > 50 || System.Math.Abs(y - a.Y) > PadHalfY + 50) continue;
            foreach (Strip st in AirportStrips)
            {
                if (InStrip(a, st, x, y, 1.0)) return IsPaved(st.Kind) ? Surface.Paved : st.Kind == "gravel" ? Surface.Gravel : Surface.Grass;
            }
            if (System.Math.Abs(x - (a.X + ApronDx)) <= ApronLengthM / 2 && System.Math.Abs(y - (a.Y + ApronDy)) <= ApronWidthM / 2) return Surface.Paved;
        }
        if (y > Coast.MeanShoreY + 1000 && Island.IsPaved(x, y)) return Surface.Paved;   // Airport in the Sky
        // SOMETHING SOMEBODY MADE comes first — a strip, a bar, a bore — because the broad regions below are
        // catch-alls over whole blocks of country and will happily claim a runway that runs through them.
        // (The Alaska block's padding reached over the tunnel maze and turned one of its strips to gravel.)
        { Surface? fc = FrankChurch.SurfaceAt(x, y); if (fc.HasValue) return fc.Value; }   // the backcountry strips
        if (Active != null)
        {
            Waterfall tf = Active.Waterfalls[WaterfallTunnel.FallStep];
            if (WaterfallTunnel.OnRunway(tf, x, y)) return Surface.Paved;                   // the strips in the maze
            // The rest of the maze's floor is cut rock, not a ploughed field: a wheel rolls on it. (With it
            // reported as Rough, a tug could not even accelerate along a bore.)
            if (WaterfallTunnel.InFootprint(tf, x, y)) return Surface.Gravel;
        }
        { Surface? br = BraidedRiver.SurfaceAt(x, y); if (br.HasValue) return br.Value; }  // the outwash gravel
        { Surface? hd = HighDesert.SurfaceAt(x, y); if (hd.HasValue) return hd.Value; }    // the Reno high desert
        if (AlaskaRange.OnIce(x, y)) return Surface.Snow;                                  // glacier ice
        // ---- the catch-alls: whole regions, and only where nothing above has spoken for the ground.
        if (AlaskaRange.InRange(x, y)) return Surface.Gravel;                              // rock and moraine
        if (AlpineRange.InRange(x, y)) return Surface.Gravel;                              // granite and scree, not pasture
        if (Mall.OnGrass(x, y)) return Surface.Grass;                                      // the Mall's groomed grass
        if (Hills.IsGrass(x, y)) return Surface.Grass;                                     // the rolling hills: smooth turf
        return Surface.Rough;
    }

    /// <summary>Surface under a wheel at height <paramref name="up"/>: a raised deck (the pad) is paved.</summary>
    public static Surface SurfaceAt(double x, double y, double up) =>
        WorldDecks.DeckUnder(x, y, up, out double top) && top > GroundHeightAt(x, y) + 1 ? Surface.Paved : SurfaceAt(x, y);

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
    /// <summary>Ground under a wheel / hard point at height <paramref name="up"/>: the terrain, or a raised deck it is on.</summary>
    public static double WheelGroundHeightAt(double x, double y, double up)
    {
        double g = WheelGroundHeightAt(x, y);
        return WorldDecks.All.Count > 0 && WorldDecks.DeckUnder(x, y, up, out double top) && top > g ? top : g;
    }

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
                y += 0.5 * (CrestY(i, x) - y);     // CrestY, not the bare wander: one source of truth for the top
            }
            f[i] = new Waterfall(i, x, y, DatumM + StepHeightM * (i + 1), DatumM + StepHeightM * i);
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
        WaterfallTunnel.RegisterSolids(this);   // the bored runway behind the first fall: its arch and end wall
    }

    // ---- height field ---------------------------------------------------------------------------

    /// <summary>Mean escarpment edge y (before irregularity) for step i (0..2).</summary>
    public static double EdgeMeanY(int i) => FirstEdgeY - StepSpacingM * i;

    /// <summary>Canyon-wall irregularity: the edge wanders ±~380 m along x, differently per step.</summary>
    public static double EdgeWander(int i, double x)
    {
        double p = i * 1.7;
        // Half the old wander (±~210 m): the bluff is now 1 km from the field and must not reach its pad.
        return 0.5 * (210.0 * System.Math.Sin(x / 530.0 + 1.3 + p) + 120.0 * System.Math.Sin(x / 205.0 + 0.4 + 2 * p)
             + 60.0 * System.Math.Sin(x / 91.0 + 2.1 + p) + 30.0 * System.Math.Sin(x / 37.0 + 3 * p));
    }

    // ---- the mountain fronts (owner 2026-10-16) -------------------------------------------------
    //
    // "Reshape the risers as mountain fronts rather than flat shelves." A riser was a 183 m ramp from one
    // plateau to the next: 2,000 ft of rise in 600 ft of run, a 73° wall of rock with a flat top, and from
    // the air unmistakably a step in a staircase.
    //
    // A real mountain front is not a wall of constant height and width. It is EMBAYED: great spurs reach out
    // onto the plain and bays cut back between them, and along the crest there are summits with saddles
    // between — and the saddles are how you get over it without climbing to the summits. So the front here
    // has two things the wall did not:
    //
    //   WIDTH that varies along x. The crest stays exactly where the wall's top was (which is why the three
    //   waterfalls, their amphitheaters and the tunnel maze behind the first one do not move at all), and the
    //   TOE reaches forward onto the lower plateau by up to a further 980 m. Where it reaches far the front is
    //   a 1,160 m slope — a talus fan, then rock at about 44° — and where it reaches not at all it is still
    //   the old cliff.
    //
    //   RELIEF above the crest. Summits every 1.6 km or so, up to 520 m over the plateau behind, with their
    //   back slopes running down onto it. Between them the crest falls to plateau level: those gaps are the
    //   passes, and a test checks there is never more than 3 km of front without one.
    //
    // WHERE IT MAY GROW is the whole problem, because the plateaus are 3.3 km apart and most of that is
    // already spoken for. Both ends of the front's growth are therefore gated by <see cref="Keepouts"/>: the
    // airport pads, and the five big landscape blocks that fill their plateaus edge to edge. The airports all
    // share x, so every front keeps the old cliff across the middle 3.5 km — the aerodrome sits in a bay in
    // the range, which is the usual reason an aerodrome is where it is — and becomes mountain out to either
    // side, where there are 5 km to the west and 7 km to the east with nothing on them.

    /// <summary>How much further than <see cref="EscarpmentWidthM"/> a spur's toe reaches onto the lower plateau.</summary>
    public const double FrontExtraWidthM = 980.0;
    /// <summary>How far short of a keepout the toe (or a back slope) stops.</summary>
    public const double FrontClearM = 140.0;
    /// <summary>And over what distance in x it tapers away as it comes up on one, so a spur dies into a bay
    /// instead of ending in a wall.</summary>
    public const double FrontRoomFadeM = 650.0;
    /// <summary>How far a summit stands above the plateau behind it, at most.</summary>
    public const double CrestReliefM = 520.0;
    /// <summary>And how far back onto that plateau its back slope runs. 520 m in 620 is 40°: a steep back
    /// side, which is what a fault block looks like, and short enough to fit on a 3.3 km plateau.</summary>
    public const double CrestBackSlopeM = 620.0;
    /// <summary>Summits along the crest, roughly this far apart.</summary>
    public const double CrestSummitSpacingM = 1600.0;

    /// <summary>Uniform hash, 0..1, for the summits' places and sizes: the same scatter the high desert uses,
    /// and for the same reason — |sin(n)| on its own is an arcsine distribution, not a flat one, and a front
    /// built on it puts every summit the same size in the same place along every crest.</summary>
    public static double Hash(double n, double salt)
    {
        double v = System.Math.Sin(n * 12.9898 + salt * 78.233) * 43758.5453;
        return v - System.Math.Floor(v);
    }

    private static double Smooth01(double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Ground a front must not grow into. <see cref="Flat"/> marks the ones that FLATTEN what they stand on —
    /// a block of ground that replaces the height field with a floor of its own. Those also hold the crest
    /// back, because a floor needs a finished plateau under it; the rest (a race course, a town) only keep a
    /// summit's back slope off, since they take the ground they are given.
    /// </summary>
    public readonly struct Keepout
    {
        public readonly string Name; public readonly double X0, Y0, X1, Y1; public readonly bool Flat;
        public Keepout(string name, double x0, double y0, double x1, double y1, bool flat = false)
        { Name = name; X0 = System.Math.Min(x0, x1); Y0 = System.Math.Min(y0, y1); X1 = System.Math.Max(x0, x1); Y1 = System.Math.Max(y0, y1); Flat = flat; }
    }

    private static Keepout[]? _keepouts;
    /// <summary>
    /// The airport pads and the landscape blocks the fronts have to leave alone. The pads are listed with
    /// their 300 m blend margin, because the blend is what would otherwise ramp the ground from a runway up
    /// into a mountainside.
    /// </summary>
    public static Keepout[] Keepouts => _keepouts ??= BuildKeepouts();

    private static Keepout[] BuildKeepouts()
    {
        var k = new System.Collections.Generic.List<Keepout>();
        foreach (Airport a in Airports)
            k.Add(new Keepout($"{a.Name} pad", a.X - PadSouthX - 300, a.Y - PadHalfY - 300, a.X + PadHalfX + 300, a.Y + PadHalfY + 300, flat: true));
        for (int p = 0; p < PlateauCount; p++)
        {
            var (cx0, cy0, cx1, cy1) = ValleyCity.At(p).Extent(90);
            k.Add(new Keepout($"plateau {p} city", cx0, cy0, cx1, cy1));
            // The race course is the FURTHEST east of the repeated features — 2.1 km out, where the next
            // plateau's crest is 1.2 km out — so it is the one a summit's back slope reaches. The first front
            // put pylon 8 on plateau 1 sixty-nine metres up a mountainside.
            double rx0 = double.MaxValue, ry0 = double.MaxValue, rx1 = double.MinValue, ry1 = double.MinValue;
            foreach (RaceElement e in RaceCourse.ElementsFor(p))
            {
                rx0 = System.Math.Min(rx0, e.X); rx1 = System.Math.Max(rx1, e.X);
                ry0 = System.Math.Min(ry0, e.Y); ry1 = System.Math.Max(ry1, e.Y);
            }
            k.Add(new Keepout($"plateau {p} race course", rx0 - 60, ry0 - 60, rx1 + 60, ry1 + 60));
            CropField cf = CropField.For(p);
            k.Add(new Keepout($"plateau {p} crop field", cf.X0 - 60, cf.Y0 - 60, cf.X1 + 60, cf.Y1 + 60));
            k.Add(new Keepout($"plateau {p} aerobatic box", AeroBox.CenterX - AeroBox.SizeM / 2 - 60, AeroBox.CenterYAt(p) - AeroBox.SizeM / 2 - 60,
                                                            AeroBox.CenterX + AeroBox.SizeM / 2 + 60, AeroBox.CenterYAt(p) + AeroBox.SizeM / 2 + 60));
        }
        k.Add(new Keepout("braided plain", BraidedRiver.X0, BraidedRiver.Y0, BraidedRiver.X1, BraidedRiver.Y1, flat: true));
        k.Add(new Keepout("high desert", HighDesert.X0, HighDesert.Y0, HighDesert.X1, HighDesert.Y1, flat: true));
        k.Add(new Keepout("Frank Church", FrankChurch.X0, FrankChurch.Y0, FrankChurch.X1, FrankChurch.Y1, flat: true));
        // The Alaska range and the alpine range are deliberately NOT here. They take the ground as they find
        // it — their peaks are a max over whatever is underneath and stand two to four thousand metres over
        // it — so a front's summits simply become their northern foothills, which is what foothills are. The
        // five above all FLATTEN their ground, and a front that grows into one leaves a cliff at its boundary.
        return k.ToArray();
    }

    /// <summary>
    /// How far the front at this x may reach from its crest, in the given direction (+1 forward onto the
    /// lower plateau, −1 back onto the upper one), before it touches something already standing there.
    /// A keepout's influence fades over <see cref="FrontRoomFadeM"/> in x, so what it does to the front is a
    /// taper and not a step.
    /// </summary>
    private static double FrontRoomAt(double crestY, double x, int dir, double want)
    {
        double room = want;
        foreach (Keepout k in Keepouts)
        {
            if (x < k.X0 - FrontRoomFadeM || x > k.X1 + FrontRoomFadeM) continue;
            // Only what lies in the direction we are growing. Leaving this out was the first version's
            // mistake: every block ANYWHERE in y came out with a huge negative gap and shut the front down,
            // so the high desert three plateaus away stopped a spur on the Valley's own front.
            if (dir > 0 ? k.Y1 <= crestY : k.Y0 >= crestY) continue;
            // Gap from the crest to the near side of the block, in the direction we are growing.
            double gap = dir > 0 ? k.Y0 - crestY - FrontClearM : crestY - k.Y1 - FrontClearM;
            if (gap >= want) continue;                     // far enough away to be no constraint
            double inX = Smooth01(System.Math.Min(x - (k.X0 - FrontRoomFadeM), (k.X1 + FrontRoomFadeM) - x) / FrontRoomFadeM);
            room = System.Math.Min(room, want + (System.Math.Max(0, gap) - want) * inX);
        }
        return System.Math.Clamp(room, 0, want);
    }

    /// <summary>The spur profile along a front: 0 in a bay, 1 where a spur reaches out as far as it can.</summary>
    public static double FrontSpurAt(int i, double x)
    {
        double p = 2.3 + i * 5.1;
        return System.Math.Clamp(0.55 + 0.45 * System.Math.Sin(x / 1150.0 + p)
                                      + 0.22 * System.Math.Sin(x / 430.0 + 2 * p)
                                      + 0.10 * System.Math.Sin(x / 197.0 + 3 * p), 0, 1);
    }

    /// <summary>
    /// y of the front's crest at x — the top of the rise, which is where the old wall's top was, so the
    /// waterfalls and the tunnel maze behind the first one do not move.
    ///
    /// Except that the crest may not wander BEHIND a block that flattens the plateau: the plateau has to be
    /// finished before the block starts. The first wall's wander takes it up to 138 m past the high desert's
    /// northern boundary, and there the desert's floor stood 560 m above the half-built escarpment under it —
    /// a cliff along the boundary for 3 km. No height probe inside the desert could see it, because inside
    /// the desert the floor is exactly what it should be.
    /// </summary>
    public static double CrestY(int i, double x)
    {
        double c = EdgeMeanY(i) + EdgeWander(i, x) - EscarpmentWidthM, lim = c;
        foreach (Keepout k in Keepouts)
        {
            if (!k.Flat) continue;                                            // only a floor needs a finished plateau
            if (x < k.X0 - FrontRoomFadeM || x > k.X1 + FrontRoomFadeM) continue;
            if (k.Y1 >= EdgeMeanY(i) || k.Y1 + FrontClearM <= c) continue;    // not behind this crest, or clear of it
            double inX = Smooth01(System.Math.Min(x - (k.X0 - FrontRoomFadeM), (k.X1 + FrontRoomFadeM) - x) / FrontRoomFadeM);
            lim = System.Math.Max(lim, c + (k.Y1 + FrontClearM - c) * inX);
        }
        return lim;
    }

    /// <summary>The front's whole horizontal run at x: the cliff band plus however far its toe may reach.</summary>
    public double FrontWidthAt(int i, double x)
    {
        double want = EscarpmentWidthM + FrontExtraWidthM * FrontSpurAt(i, x) * (1 - NotchWeight(Waterfalls[i], x));
        // Never less than the cliff band: the old wall already stood where it stood, and a keepout is a
        // licence to not grow, not a licence to take the escarpment away.
        return System.Math.Max(EscarpmentWidthM, FrontRoomAt(CrestY(i, x), x, +1, want));
    }

    /// <summary>Summit shape along the crest, 0..1: domes about <see cref="CrestSummitSpacingM"/> apart with
    /// saddles between them. The gaps ARE the passes — do not fill them in.</summary>
    public static double CrestShapeAt(int i, double x)
    {
        double best = 0;
        int n = (int)System.Math.Floor(x / CrestSummitSpacingM);
        for (int j = n - 1; j <= n + 1; j++)
        {
            double seed = j * 1.0 + i * 37.0;
            double cx = (j + 0.5 + (Hash(seed, 0.0) - 0.5) * 0.8) * CrestSummitSpacingM;
            double halfW = 470.0 + 350.0 * Hash(seed, 1.7);
            double tall = 0.30 + 0.70 * Hash(seed, 3.1);
            double u = System.Math.Abs(x - cx) / halfW;
            if (u >= 1) continue;
            best = System.Math.Max(best, tall * Smooth01(1 - u));
        }
        // A crest that is dead flat between its summits reads as a terrace, which is the thing this was meant
        // to stop being — a render showed the straight green edge of the plateau running away between two
        // perfectly good mountains. Under the summits there is a low continuous swell of 10 to 47 m: enough
        // to lose the straight line, little enough that a saddle is still a saddle you can fly through.
        double q = 1.1 + i * 3.3;
        double swell = 0.055 + 0.035 * System.Math.Sin(x / 740.0 + q) + 0.025 * System.Math.Sin(x / 263.0 + 2 * q);
        return System.Math.Max(best, System.Math.Max(0, swell));
    }

    /// <summary>How far the summit at this x stands above the plateau behind the crest, and over what distance
    /// its back slope dies away on it. Zero at a water gap: the river's way through the front is a gap.</summary>
    public (double reliefM, double backSlopeM) CrestAt(int i, double x)
    {
        double shape = CrestShapeAt(i, x) * (1 - NotchWeight(Waterfalls[i], x));
        if (shape <= 0) return (0, CrestBackSlopeM);
        double back = FrontRoomAt(CrestY(i, x), x, -1, CrestBackSlopeM);
        if (back <= 0) return (0, CrestBackSlopeM);
        // A summit with half the room gets half the height as well as half the back slope: shortening it
        // alone would stand the same mountain up on a 60° back side, which is not what a fault block does.
        return (CrestReliefM * shape * (back / CrestBackSlopeM), back);
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
        double h = DatumM;
        Waterfall[] falls = Waterfalls;
        for (int i = 0; i < StepCount; i++)
        {
            double crest = CrestY(i, x);
            double run = FrontWidthAt(i, x);
            double t = (crest + run - y) / run;        // 0 at the toe, 1 at the crest, > 1 behind it
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
            // The summits along the crest, and their back slopes down onto the plateau behind.
            var (relief, back) = CrestAt(i, x);
            if (relief > 0)
            {
                double cw;
                if (t <= 0.5) cw = 0;
                else if (t <= 1) cw = Smooth01((t - 0.5) / 0.5);
                else cw = Smooth01(1 - (t - 1) * run / back);
                h += relief * cw;
            }
            // Rock texture on the wall face: small ledges (not inside the notch — the face there is sheer).
            if (t > 0 && t < 1 && w < 0.5) h += 6.0 * System.Math.Sin(x / 23.0) * System.Math.Sin(y / 17.0);
        }
        return h;
    }

    /// <summary>Ground height (m, up) including flat airport pads and the lake / river beds.</summary>
    public double HeightAt(double x, double y) => HeightAt(x, y, false);

    /// <summary>Ground height; <paramref name="shelfTop"/> = true gives the un-recessed surface the waterfall shelf carries.</summary>
    public double HeightAt(double x, double y, bool shelfTop) => HeightAt(x, y, shelfTop, true);

    /// <summary><paramref name="withTunnel"/> = false gives the mountain as if the runway tunnel behind the
    /// first fall had never been bored — the surface its rock roof carries, which is what the roof solid and
    /// the roof mesh measure against.</summary>
    public double HeightAt(double x, double y, bool shelfTop, bool withTunnel)
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
            double dx = PadOutsideX(a, x);
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

        // Rolling grass hills (owner 2026-10-05): smooth, landable.
        h += Hills.HeightAt(x, y);

        // The alpine range on the Summit plateau (owner 2026-10-16): peaks, cirques and the lakes in them.
        h = AlpineRange.HeightAt(x, y, h);
        // The backcountry west of it: a canyon a kilometre deep with the famous strips on its benches.
        h = FrankChurch.HeightAt(x, y, h);
        // The Alaska range on the Mesa plateau: a 19,000 ft mountain and the glaciers off it.
        h = AlaskaRange.HeightAt(x, y, h);
        // And the outwash plain below the ice: braided channels, gravel bars, driftwood and boulders.
        h = BraidedRiver.HeightAt(x, y, h);
        // The Reno high desert, west of the Alaska range on the same plateau: mesas you can land on top of.
        h = HighDesert.HeightAt(x, y, h);

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

        // The seaplane base: ramp into the Valley lake and its parking pad.
        { double? b = SeaplaneBaseHeightAt(x, y, h); if (b.HasValue) h = b.Value; }

        // The coast: the tableland ends in sea cliffs (cap taken off the uncut ground, applied after the gorge).
        double coastCap = Coast.ProfileAt(x, y, h);

        // Gorge: steep-walled canyon around the river; floor = river surface - 4 m.
        double ddx = System.Math.Abs(x - RiverCentreX(y));
        double gw = GorgeHalfWidthAt(y);
        if (ddx < gw && !PastRiverMouth(y))
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
        // The canyon lake behind the dam: the gorge widened and flooded (walls into the water, a deep floor).
        if (y > CanyonLake.Y0 && y < CanyonLake.DamY + 60) h = CanyonLake.CarveAt(x, y, h);
        // The Mall: dead flat, the reflecting pond's bed.
        { double? m = Mall.HeightAt(x, y); if (m.HasValue) h = m.Value; }
        h = System.Math.Min(h, coastCap);

        // The island rises out of the sea.
        if (y > Coast.MeanShoreY + 1000)
        {
            double? isl = Island.HeightAt(x, y);
            if (isl.HasValue) h = SeaCave.InFootprint(x, y) ? isl.Value : System.Math.Max(h, isl.Value);
        }

        // The runway bored in behind the first fall: inside the bore the ground is its floor, flush with the
        // amphitheater shelf outside the mouth. Last, so nothing above re-carves it.
        if (withTunnel && !shelfTop)
        {
            Waterfall tf = Waterfalls[WaterfallTunnel.FallStep];
            if (WaterfallTunnel.InFootprint(tf, x, y)) h = WaterfallTunnel.FloorAt(tf, x, y);
        }
        return h;
    }

    /// <summary>Water surface at x/y (m, up), or null if no water there. Lakes are flat at their level; the
    /// river surface is the local (uncarved) ground minus 2 m, so it steps down at every wall.</summary>
    public double? WaterSurfaceAt(double x, double y) => WaterSurfaceAt(x, y, double.NaN);

    /// <summary>
    /// Water surface at a POINT. Pass the point's height whenever you have it. At a plunge fall the river runs
    /// out over an OVERHANG, so one (x, y) has two surfaces: the river on the shelf, and 200 m below it the dry
    /// amphitheater floor — and now the dry runway in the tunnel bored behind it. Without a height there is no
    /// way to tell a float on the river from a wheel on the rock beneath it, and the height-less answer has to
    /// stay the river, because that is what the water mesh is drawn from.
    /// </summary>
    public double? WaterSurfaceAt(double x, double y, double up)
    {
        if (!double.IsNaN(up))
        {
            // Under the shelf behind a curtain: the water is overhead, in free fall, and the floor is dry.
            foreach (Waterfall wf in Waterfalls)
            {
                if (NotchWeight(wf, x) <= 0) continue;
                double back = wf.LipY - FallRecessAt(wf, x);
                if (y < wf.LipY && y > back && up < FallShelfBottomM(wf)) return null;
            }
            // Inside the tunnel: rock between the point and the river above it.
            Waterfall tf = Waterfalls[WaterfallTunnel.FallStep];
            if (WaterfallTunnel.InFootprint(tf, x, y) && up < WaterfallTunnel.CeilingAt(tf, x, y) + 1.0) return null;
        }
        return WaterSurfaceCore(x, y);
    }

    private double? WaterSurfaceCore(double x, double y)
    {
        { double? alp = AlpineRange.WaterSurfaceAt(x, y); if (alp.HasValue) return alp; }
        { double? fc = FrankChurch.WaterSurfaceAt(x, y); if (fc.HasValue) return fc; }
        { double? ak = AlaskaRange.WaterSurfaceAt(x, y); if (ak.HasValue) return ak; }
        { double? br = BraidedRiver.WaterSurfaceAt(x, y); if (br.HasValue) return br; }
        foreach (Lake l in Lakes)
        {
            if (l.Inside(x, y) < 1.0) return l.SurfaceM;
        }
        // The canyon lake (over the drowned river), the Mall's reflecting pond.
        if (CanyonLake.OnWater(x, y) && HeightAt(x, y) < CanyonLake.SurfaceM) return CanyonLake.SurfaceM;
        if (Mall.InPond(x, y)) return Mall.PondSurfaceM;
        double ddx = System.Math.Abs(x - RiverCentreX(y));
        if (ddx < RiverHalfWidthM + 12 && !PastRiverMouth(y))
        {
            return System.Math.Max(RiverSurfaceAt(y), Coast.SeaLevelM);
        }
        // The sea (and the sea cave): wherever the ground east of the cliff foot is below sea level.
        if (Coast.IsSea(x, y) && HeightAt(x, y) < Coast.SeaLevelM + 0.5) return Coast.SeaLevelM;
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

/// <summary>
/// Rolling hills of smooth grass (owner 2026-10-05: "put some rolling hills in that are smooth grass that can be landed
/// on"): broad mounds 20–45 m high, 180–300 m across, between the Valley city and the coast. Gentle — the steepest slope
/// is about 6°, so the wheels can land up or down them — and turf, not rough ground.
/// </summary>
public static class Hills
{
    public const double X0 = -3100, X1 = -400, Y0 = 1650, Y1 = 2480, EdgeM = 150;
    private static readonly (double x, double y, double a, double s)[] Mounds = Build();
    private static (double, double, double, double)[] Build()
    {
        var l = new List<(double, double, double, double)>();
        int k = 0;
        for (double x = X0 + 250; x < X1 - 150; x += 430)
            for (double y = Y0 + 200; y < Y1 - 100; y += 380)
            {
                double jx = 90 * System.Math.Sin(k * 2.3), jy = 70 * System.Math.Cos(k * 1.7);
                double a = 22 + 23 * (0.5 + 0.5 * System.Math.Sin(k * 1.31 + 0.4));
                double sg = 180 + 120 * (0.5 + 0.5 * System.Math.Cos(k * 0.93));
                l.Add((x + jx, y + jy, a, sg));
                k++;
            }
        return l.ToArray();
    }

    private static double Fade(double x, double y)
    {
        double d = System.Math.Min(System.Math.Min(x - X0, X1 - x), System.Math.Min(y - Y0, Y1 - y));
        if (d <= 0) return 0;
        double t = System.Math.Clamp(d / EdgeM, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double HeightAt(double x, double y)
    {
        if (x < X0 || x > X1 || y < Y0 || y > Y1) return 0;
        double h = 0;
        foreach (var (mx, my, a, sg) in Mounds)
        {
            double dx = x - mx, dy = y - my;
            double d2 = (dx * dx + dy * dy) / (2 * sg * sg);
            if (d2 < 9) h += a * System.Math.Exp(-d2);
        }
        return h * Fade(x, y);
    }

    public static bool IsGrass(double x, double y) => x > X0 && x < X1 && y > Y0 && y < Y1;
}
