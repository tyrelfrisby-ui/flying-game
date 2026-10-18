namespace FlyingGame.Core.Nav;

/// <summary>Which part of a procedure a leg belongs to. Each carries its own obstacle clearance.</summary>
public enum NavSegment { EnRoute, Initial, Intermediate, FinalNonPrecision, FinalPrecision, ProcedureTurn, MissedApproach }

/// <summary>
/// The criteria an instrument procedure is built from, as code, so the altitudes on this world's charts are
/// DERIVED from its terrain rather than chosen to look plausible.
///
/// Owner, 2026-10-18: *"build instrument approaches based on the terps that allow the user to get from an
/// airway (also build airways based on the terps) to the approach."* So every altitude on the IFR chart comes
/// out of <see cref="Airways"/> and <see cref="Approaches"/> calling into this file, which measures the
/// highest ground inside a segment's protected area and adds that segment's required obstacle clearance. If
/// the terrain changes, the altitudes change with it, and the test suite notices.
///
/// WHERE THE NUMBERS COME FROM. FAA Order 8260.3 (TERPS) is the governing document. The required obstacle
/// clearances below are the standard values:
///
///   | Segment | ROC, primary area |
///   |---|---|
///   | En route | 1,000 ft; **2,000 ft over mountainous terrain** |
///   | Initial | 1,000 ft (secondary 500 ft) |
///   | Intermediate | 500 ft (secondary tapering 500 → 0) |
///   | Final, non-precision | 250 ft (300 / 350 ft in some cases) |
///   | Procedure turn | 1,000 ft |
///
/// A precision final is not an ROC at all but a sloping surface: for the standard 3° glidepath the obstacle
/// clearance surface is **34:1**, written run-over-rise as TERPS writes slopes (102 ÷ 3). Nothing may
/// penetrate it; the Category I decision height is 200 ft above the touchdown zone.
///
/// A procedure turn is flown **within 10 NM** of its fix, at **200 KIAS or less** — that speed limit exists
/// precisely so the aeroplane stays inside the area this file protects.
///
/// THE SIMPLIFICATION, STATED PLAINLY. TERPS protected areas are trapezoids that splay with distance from
/// the facility and carry secondary areas in which the clearance tapers to zero at the outer edge. This file
/// uses a **constant-width corridor of the primary area's width, with no secondary area**, and applies the
/// primary ROC across the whole of it. That is a simplification in two directions and it is not hidden:
/// where the real area would have splayed wider, this corridor misses obstacles further out; where the real
/// area would be narrower near the facility, this is conservative. The widths used are the primary-area
/// half-widths — 4 NM en route and initial, 2 NM intermediate, 1 NM final. For a world 9 × 15 NM across,
/// with every leg inside 25 NM of its station, that is the part of the criteria that bites; the splay is not.
/// It is written here rather than in a comment on one call site because somebody will one day want to know
/// whether these charts could be trusted in a real aeroplane, and the answer has to be easy to find: **no.**
/// </summary>
public static class Terps
{
    public const double FtPerM = 3.280839895013123;
    public const double MPerFt = 0.3048;
    public const double MetresPerNm = 1852.0;

    // ---- required obstacle clearance, feet ----
    public const double EnRouteRocFt = 1000.0;
    public const double EnRouteRocMountainousFt = 2000.0;
    public const double InitialRocFt = 1000.0;
    public const double IntermediateRocFt = 500.0;
    public const double FinalNonPrecisionRocFt = 250.0;
    public const double ProcedureTurnRocFt = 1000.0;

    // ---- protected areas: primary-area half widths, NM ----
    //
    // THE ONE PLACE THIS WORLD IS NOT FULL SCALE, and it had to be measured to be believed. A Victor airway's
    // primary area is 4 NM each side of the centreline: EIGHT nautical miles wide. This world is 8.6 NM
    // across. A full-size airway down the corridor the airports sit in therefore reaches 4 NM north, which is
    // far enough to include the 17,644 ft peak in the Alaska range, and the minimum en-route altitude for the
    // leg between the two hill stations comes out at 19,800 ft — correct arithmetic, useless airway, and no
    // routing fixes it because there is nowhere in this world that is 4 NM from a mountain.
    //
    // So the LATERAL areas are divided by <see cref="LateralScale"/> and the real figure is kept beside each
    // one. The vertical criteria are NOT scaled: the mountains here are full-size mountains and 1,000 ft of
    // obstacle clearance is 1,000 ft of obstacle clearance. Airway legs here run 0.6 to 5.3 NM against 20 to
    // 100 in real airway country, so a quarter of the width is, if anything, still generous for the scale.
    public const double LateralScale = 4.0;

    public const double EnRouteHalfWidthNmReal = 4.0;
    public const double InitialHalfWidthNmReal = 4.0;
    public const double IntermediateHalfWidthNmReal = 2.0;
    public const double FinalHalfWidthNmReal = 1.0;

    public const double EnRouteHalfWidthNm = EnRouteHalfWidthNmReal / LateralScale;
    public const double InitialHalfWidthNm = InitialHalfWidthNmReal / LateralScale;
    public const double IntermediateHalfWidthNm = IntermediateHalfWidthNmReal / LateralScale;
    public const double FinalHalfWidthNm = FinalHalfWidthNmReal / LateralScale;

    // ---- the procedure turn ----
    public const double ProcedureTurnRemainWithinNm = 10.0;
    public const double ProcedureTurnMaxIasKt = 200.0;

    // ---- precision final ----
    public const double StandardGlidepathDeg = 3.0;
    /// <summary>Run over rise, TERPS's own convention: 102 ÷ 3 for a 3° path.</summary>
    public const double PrecisionOcsSlope = 34.0;
    public const double CategoryOneHatFt = 200.0;

    // ---- circling ----
    /// <summary>Obstacle clearance in the circling area, feet.</summary>
    public const double CirclingRocFt = 300.0;
    /// <summary>Circling radius for the slowest approach categories, nautical miles.</summary>
    public const double CirclingRadiusNmCatA = 1.3;

    // ---- missed approach ----
    public const double MissedApproachClimbFtPerNm = 200.0;
    public const double MissedApproachOcsSlope = 40.0;

    /// <summary>
    /// ICAO's definition of mountainous country, which is a measurement and not an opinion: ground that
    /// changes by more than 900 m (3,000 ft) within 10 NM. Computed from the terrain, so it answers for this
    /// world rather than for a designated area on a real chart.
    /// </summary>
    public const double MountainousReliefM = 914.4;
    public const double MountainousRadiusNm = 10.0;

    /// <summary>Is the ground around (x, y) mountainous by the relief test above?</summary>
    public static bool IsMountainous(WorldTerrain t, double x, double y)
        => Relief10NmM(t, x, y) > MountainousReliefM;

    /// <summary>The relief — highest minus lowest ground — within 10 NM of (x, y), metres.</summary>
    public static double Relief10NmM(WorldTerrain t, double x, double y)
    {
        double r = MountainousRadiusNm * MetresPerNm;
        double hi = double.MinValue, lo = double.MaxValue;
        // A ring-and-spoke sample: 16 bearings × 8 radii plus the centre. 129 probes for a 10 NM disc is
        // coarse, and it is looking for a 3,000 ft difference, which no 2 km gap in this world hides.
        void P(double px, double py)
        {
            double h = t.HeightAt(px, py, false, false);
            if (h > hi) hi = h;
            if (h < lo) lo = h;
        }
        P(x, y);
        for (int b = 0; b < 16; b++)
        {
            double a = b * System.Math.PI / 8;
            double cx = System.Math.Cos(a), cy = System.Math.Sin(a);
            for (int k = 1; k <= 8; k++) P(x + cx * r * k / 8, y + cy * r * k / 8);
        }
        return hi - lo;
    }

    /// <summary>
    /// The highest ground inside a straight segment's protected area: a corridor
    /// <paramref name="halfWidthNm"/> either side of the line from (x0, y0) to (x1, y1), metres.
    /// </summary>
    public static double HighestInCorridorM(WorldTerrain t, double x0, double y0, double x1, double y1,
                                            double halfWidthNm, double stepM = 150.0)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return t.HeightAt(x0, y0, false, false);
        double ux = dx / len, uy = dy / len;            // along
        double px = -uy, py = ux;                       // across
        double half = halfWidthNm * MetresPerNm;
        double hi = double.MinValue;
        int na = (int)System.Math.Ceiling(len / stepM);
        int nc = (int)System.Math.Ceiling(half / stepM);
        for (int i = 0; i <= na; i++)
        {
            double a = len * i / na;
            for (int j = -nc; j <= nc; j++)
            {
                double c = half * j / nc;
                double h = t.HeightAt(x0 + ux * a + px * c, y0 + uy * a + py * c, false, false);
                if (h > hi) hi = h;
            }
        }
        return hi;
    }

    /// <summary>The highest ground within <paramref name="radiusNm"/> of a point — the procedure turn's area.</summary>
    public static double HighestInCircleM(WorldTerrain t, double x, double y, double radiusNm, double stepM = 150.0)
    {
        double r = radiusNm * MetresPerNm;
        double hi = double.MinValue;
        int n = (int)System.Math.Ceiling(r / stepM);
        for (int i = -n; i <= n; i++)
            for (int j = -n; j <= n; j++)
            {
                double ox = r * i / n, oy = r * j / n;
                if (ox * ox + oy * oy > r * r) continue;
                double h = t.HeightAt(x + ox, y + oy, false, false);
                if (h > hi) hi = h;
            }
        return hi;
    }

    /// <summary>The required obstacle clearance for a segment, feet.</summary>
    public static double RocFt(NavSegment s, bool mountainous) => s switch
    {
        NavSegment.EnRoute => mountainous ? EnRouteRocMountainousFt : EnRouteRocFt,
        NavSegment.Initial => InitialRocFt,
        NavSegment.Intermediate => IntermediateRocFt,
        NavSegment.FinalNonPrecision => FinalNonPrecisionRocFt,
        NavSegment.ProcedureTurn => ProcedureTurnRocFt,
        _ => InitialRocFt,
    };

    /// <summary>The primary-area half width for a segment, NM.</summary>
    public static double HalfWidthNm(NavSegment s) => s switch
    {
        NavSegment.EnRoute => EnRouteHalfWidthNm,
        NavSegment.Initial => InitialHalfWidthNm,
        NavSegment.Intermediate => IntermediateHalfWidthNm,
        NavSegment.FinalNonPrecision => FinalHalfWidthNm,
        NavSegment.FinalPrecision => FinalHalfWidthNm,
        NavSegment.ProcedureTurn => EnRouteHalfWidthNm,
        _ => EnRouteHalfWidthNm,
    };

    /// <summary>Round an altitude UP to the next multiple — 100 ft for a minimum en-route altitude.</summary>
    public static double RoundUpFt(double ft, double to) => System.Math.Ceiling(ft / to - 1e-9) * to;

    /// <summary>
    /// A straight leg's minimum altitude: the highest ground in its protected area, plus the segment's ROC,
    /// rounded up. Feet. Obstacle clearance only — a minimum EN-ROUTE altitude must also have a signal, which
    /// is <see cref="Airways"/>'s job because it needs the reception model as well as this one.
    /// </summary>
    public static double MinimumAltitudeFt(WorldTerrain t, double x0, double y0, double x1, double y1,
                                           NavSegment s, double roundToFt = 100.0)
    {
        bool mtn = IsMountainous(t, (x0 + x1) * 0.5, (y0 + y1) * 0.5);
        double groundFt = HighestInCorridorM(t, x0, y0, x1, y1, HalfWidthNm(s)) * FtPerM;
        return RoundUpFt(groundFt + RocFt(s, mtn), roundToFt);
    }

    /// <summary>
    /// Height of the 34:1 obstacle clearance surface above the touchdown zone at a distance back from the
    /// threshold, feet. Nothing may stick through it; where something does, the decision altitude rises.
    /// </summary>
    public static double PrecisionOcsHeightFt(double distanceFromThresholdNm)
        => distanceFromThresholdNm * MetresPerNm * FtPerM / PrecisionOcsSlope;

    /// <summary>Height of the glidepath itself above the touchdown zone at that distance, feet.</summary>
    public static double GlidepathHeightFt(double distanceFromThresholdNm, double angleDeg = StandardGlidepathDeg)
        => distanceFromThresholdNm * MetresPerNm * FtPerM * System.Math.Tan(angleDeg * System.Math.PI / 180.0);
}
