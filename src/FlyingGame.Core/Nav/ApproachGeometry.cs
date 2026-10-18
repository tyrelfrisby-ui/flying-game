namespace FlyingGame.Core.Nav;

/// <summary>
/// The geometry an approach is DERIVED from: where a runway's thresholds are, what the highest ground inside
/// a segment's protected area is, and what minimum that ground forces. Separate from
/// <see cref="Approaches"/>, which is the published result, in the same way a survey is separate from the
/// plate printed from it.
///
/// THE PROTECTED AREA SPLAYS. <see cref="Terps.HighestInCorridorM"/> uses a constant width, which is right
/// for an airway and badly wrong for a final: at the threshold the real area is a few hundred feet across,
/// not two miles, and a constant 1 NM half width at Valley reaches a mile west of the runway and picks up the
/// 2,000 ft escarpment that the aeroplane is nowhere near. So a final is measured in a corridor that grows
/// from <see cref="FinalHalfWidthAtThresholdM"/> at the threshold, splaying at
/// <see cref="FinalSplayDeg"/>, which is the shape of the real thing even if the constants are rounder than
/// the order's.
/// </summary>
public static class ApproachGeometry
{
    /// <summary>Half width of the final approach area at the threshold, metres (about 500 ft).</summary>
    public const double FinalHalfWidthAtThresholdM = 152.0;

    /// <summary>How the final approach area splays outward from the threshold, degrees each side.</summary>
    public const double FinalSplayDeg = 15.0;

    /// <summary>MDA is charted to the next higher 20 ft, as plates print it.</summary>
    public const double MdaRoundFt = 20.0;

    /// <summary>A runway end: where you land, which way, and what the touchdown zone is.</summary>
    public readonly struct RunwayEndGeom
    {
        public readonly string Airport, Name;        // "Valley", "27"
        public readonly double ThresholdX, ThresholdY;
        public readonly double CourseDeg;            // the direction of landing
        public readonly double LengthM, WidthM, TdzeM;
        public RunwayEndGeom(string airport, string name, double tx, double ty, double courseDeg,
                             double lengthM, double widthM, double tdzeM)
        {
            Airport = airport; Name = name; ThresholdX = tx; ThresholdY = ty; CourseDeg = courseDeg;
            LengthM = lengthM; WidthM = widthM; TdzeM = tdzeM;
        }
        public double TdzeFt => TdzeM * Terps.FtPerM;
        /// <summary>A point <paramref name="nm"/> out on the final approach course.</summary>
        public (double x, double y) OnFinal(double nm)
        {
            double back = CourseDeg + 180.0;
            double r = back * System.Math.PI / 180.0;
            double d = nm * Terps.MetresPerNm;
            return (ThresholdX + System.Math.Cos(r) * d, ThresholdY + System.Math.Sin(r) * d);
        }
    }

    /// <summary>Runway number from a heading: 360 is "36", 90 is "09". Variation here is zero.</summary>
    public static string RunwayNumber(double courseDeg)
    {
        int n = (int)System.Math.Round(((courseDeg % 360 + 360) % 360) / 10.0);
        if (n == 0) n = 36;
        return n.ToString("00");
    }

    /// <summary>Every paved runway end in the world, both directions of each strip.</summary>
    public static List<RunwayEndGeom> RunwayEnds()
    {
        var l = new List<RunwayEndGeom>();
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
            foreach (WorldTerrain.Strip st in WorldTerrain.AirportStrips)
            {
                if (!st.Kind.StartsWith("paved")) continue;
                double h = st.HeadingDeg * System.Math.PI / 180.0;
                double cx = a.X + st.Dx, cy = a.Y + st.Dy;
                double ex = System.Math.Cos(h) * st.Length / 2, ey = System.Math.Sin(h) * st.Length / 2;
                // Landing along the strip's heading: the threshold is the end you cross first.
                l.Add(new RunwayEndGeom(a.Name, RunwayNumber(st.HeadingDeg), cx - ex, cy - ey,
                                        st.HeadingDeg, st.Length, st.Width, a.ElevationM));
                // And the other way round.
                double back = (st.HeadingDeg + 180) % 360;
                l.Add(new RunwayEndGeom(a.Name, RunwayNumber(back), cx + ex, cy + ey,
                                        back, st.Length, st.Width, a.ElevationM));
            }
        return l;
    }

    /// <summary>
    /// The highest ground in a final approach area that splays from the threshold, metres, between
    /// <paramref name="fromNm"/> and <paramref name="toNm"/> out.
    /// </summary>
    public static double HighestInFinalAreaM(WorldTerrain t, RunwayEndGeom r, double fromNm, double toNm,
                                             double stepM = 60.0)
    {
        double tan = System.Math.Tan(FinalSplayDeg * System.Math.PI / 180.0);
        double back = (r.CourseDeg + 180.0) * System.Math.PI / 180.0;
        double ux = System.Math.Cos(back), uy = System.Math.Sin(back);     // out along the final
        double ax = -uy, ay = ux;                                          // across it
        double hi = double.MinValue;
        int n = (int)System.Math.Ceiling((toNm - fromNm) * Terps.MetresPerNm / stepM);
        for (int i = 0; i <= n; i++)
        {
            double d = (fromNm + (toNm - fromNm) * i / n) * Terps.MetresPerNm;
            double half = FinalHalfWidthAtThresholdM + d * tan;
            int nc = (int)System.Math.Ceiling(half / stepM);
            for (int j = -nc; j <= nc; j++)
            {
                double c = half * j / nc;
                double h = t.HeightAt(r.ThresholdX + ux * d + ax * c, r.ThresholdY + uy * d + ay * c, false, false);
                if (h > hi) hi = h;
            }
        }
        return hi;
    }

    /// <summary>
    /// How far, and by how much, the ground penetrates the 34:1 precision obstacle clearance surface on a
    /// runway's final. Returns the worst penetration in feet (0 = nothing penetrates) and where it is.
    /// </summary>
    public static (double penetrationFt, double atNm) PrecisionPenetration(WorldTerrain t, RunwayEndGeom r,
                                                                           double toNm = 10.0, double stepNm = 0.05)
    {
        double worst = 0, at = 0;
        double tan = System.Math.Tan(FinalSplayDeg * System.Math.PI / 180.0);
        double back = (r.CourseDeg + 180.0) * System.Math.PI / 180.0;
        double ux = System.Math.Cos(back), uy = System.Math.Sin(back);
        double ax = -uy, ay = ux;
        for (double nm = stepNm; nm <= toNm; nm += stepNm)
        {
            double d = nm * Terps.MetresPerNm;
            double ocsFt = Terps.PrecisionOcsHeightFt(nm);
            double half = FinalHalfWidthAtThresholdM + d * tan;
            int nc = (int)System.Math.Ceiling(half / 60.0);
            for (int j = -nc; j <= nc; j++)
            {
                double c = half * j / nc;
                double h = t.HeightAt(r.ThresholdX + ux * d + ax * c, r.ThresholdY + uy * d + ay * c, false, false);
                double aboveFt = (h - r.TdzeM) * Terps.FtPerM;
                double pen = aboveFt - ocsFt;
                if (pen > worst) { worst = pen; at = nm; }
            }
        }
        return (worst, at);
    }

    /// <summary>
    /// The decision altitude for an ILS, feet MSL. 200 ft above the touchdown zone where nothing penetrates
    /// the 34:1 surface; where something does, the penetration is added. **That addition is this project's
    /// own conservative rule, not TERPS's** — the real adjustment is a table and a set of surfaces, and
    /// adding the penetration outright is simpler and never optimistic.
    /// </summary>
    public static double IlsDecisionAltitudeFt(WorldTerrain t, RunwayEndGeom r)
    {
        var (pen, _) = PrecisionPenetration(t, r);
        return Terps.RoundUpFt(r.TdzeFt + Terps.CategoryOneHatFt + System.Math.Max(0, pen), MdaRoundFt);
    }

    /// <summary>
    /// The minimum descent altitude for a CIRCLING approach, feet MSL: 300 ft above the highest ground
    /// within 1.3 NM of the airport, to the next higher 20 ft.
    ///
    /// A circling approach is the answer when no runway can be lined up with — which in this world happens
    /// at Valley and Mesa, where the only usable final approach course comes from a station that is not on
    /// any centreline. You are brought to the airport and you look out of the window for the rest, and the
    /// minimum is a height above everything around the field rather than above a corridor.
    /// </summary>
    public static double CirclingMdaFt(WorldTerrain t, double airportX, double airportY,
                                       double radiusNm = Terps.CirclingRadiusNmCatA)
    {
        double groundFt = Terps.HighestInCircleM(t, airportX, airportY, radiusNm, 100) * Terps.FtPerM;
        return Terps.RoundUpFt(groundFt + Terps.CirclingRocFt, MdaRoundFt);
    }

    /// <summary>
    /// The minimum descent altitude for a non-precision final, feet MSL: the highest ground in the final
    /// approach area from the threshold out to the final approach fix, plus 250 ft, to the next higher 20 ft.
    /// </summary>
    public static double NonPrecisionMdaFt(WorldTerrain t, RunwayEndGeom r, double fafNm)
    {
        double groundFt = HighestInFinalAreaM(t, r, 0, fafNm) * Terps.FtPerM;
        return Terps.RoundUpFt(groundFt + Terps.FinalNonPrecisionRocFt, MdaRoundFt);
    }
}
