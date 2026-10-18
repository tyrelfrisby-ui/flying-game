namespace FlyingGame.Core.Nav;

/// <summary>One leg of an airway, with the altitudes the terrain and the radio force on it.</summary>
public readonly struct AirwayLeg
{
    public readonly string FromId, ToId;
    public readonly double CourseDeg, DistanceNm;
    /// <summary>Minimum en-route altitude: obstacle clearance AND a usable signal the whole way. Feet MSL.</summary>
    public readonly double MeaFt;
    /// <summary>Minimum obstruction clearance altitude: the same clearance, signal only near the station.</summary>
    public readonly double MocaFt;
    /// <summary>What set the MEA — "terrain" or "signal". Worth charting, because they fail differently.</summary>
    public readonly string MeaDrivenBy;

    public AirwayLeg(string fromId, string toId, double courseDeg, double distanceNm,
                     double meaFt, double mocaFt, string meaDrivenBy)
    { FromId = fromId; ToId = toId; CourseDeg = courseDeg; DistanceNm = distanceNm; MeaFt = meaFt; MocaFt = mocaFt; MeaDrivenBy = meaDrivenBy; }
}

/// <summary>
/// What kind of radio an airway is flown on, which decides how its minimum altitude is worked out.
/// </summary>
public enum AirwayKind
{
    /// <summary>A Victor airway: VOR, VHF, line of sight — so reception can set the MEA.</summary>
    Victor,
    /// <summary>A coloured airway: low frequency, ground wave — reception never sets the MEA.</summary>
    Coloured,
}

/// <summary>An airway: a designator and the navaids and fixes it runs through, in order.</summary>
public sealed class Airway
{
    public string Designator = "";
    public string[] Points = System.Array.Empty<string>();
    public AirwayKind Kind = AirwayKind.Victor;
    public string Notes = "";
}

/// <summary>
/// The airway structure, and the arithmetic that gives each leg its minimum altitudes.
///
/// A minimum en-route altitude is the lowest altitude at which you have BOTH:
///
///  * **obstacle clearance** — 1,000 ft above the highest thing in the leg's protected area, or 2,000 ft
///    where the country is mountainous; and
///  * **a navigation signal** for the whole leg.
///
/// Both halves are computed here from this world's own terrain. The second half is the interesting one and
/// the reason the owner asked for line-of-sight VORs: <see cref="MinSignalAltitudeFt"/> walks the leg and
/// asks, at each point, how high you have to be before the station defining the leg comes out of the
/// terrain's shadow. On a leg that runs behind an escarpment that number is higher than the obstacle figure,
/// and then the MEA is set by the radio rather than by the rocks — which is exactly what happens in real
/// mountain country, and is why a chart prints both an MEA and an MOCA.
///
/// An MOCA has the same obstacle clearance but only promises a signal within 22 NM of the station. This
/// world is 15 NM across, so every point in it is inside 22 NM of every station and the MOCA here is the
/// obstacle figure alone. That is written down because it is a property of a small world, not a rule.
/// </summary>
public static class Airways
{
    /// <summary>Signal quality a needle can be flown on. Below this the receiver flags.</summary>
    public const double UsableSignal = 0.35;

    /// <summary>How high you may be asked to climb before a leg is declared unflyable.</summary>
    public const double CeilingFt = 24000.0;

    public static readonly Airway[] All =
    {
        new()
        {
            Designator = "V1",
            Points = new[] { "SEALS", "VLY", "CRS", "SUM" },
            Notes = "The spine: in off the sea at SEALS, over the Valley field, up the escarpment "
                  + "staircase to the top field. Every airport in the world is on it or within two miles of "
                  + "it, because they are all in the same corridor. RIM is an INTERSECTION on the "
                  + "VLY-CRS leg rather than a bend in it: it lies 200 m (0.11 NM) off that line, and a "
                  + "0.65 NM airway leg from VLY to RIM would be a bend nobody could fly.",
        },
        new()
        {
            Designator = "GREEN 1", Kind = AirwayKind.Coloured,
            Points = new[] { "VR", "SUM" },
            Notes = "THE RANGE COURSE, and the oldest airway here. A coloured airway was not a line drawn "
                  + "near a station: it WAS the station's course, flown by ear — green and red for the "
                  + "east-west ones, amber and blue for north-south. This is the Valley range's 270 course, "
                  + "and because all four airports share x = 400 it runs straight down the staircase "
                  + "through every one of them. Its minimum altitude is lower than the Victor airway over "
                  + "the same ground, because a low-frequency signal follows the earth and reception is "
                  + "never what holds you up.",
        },
        new()
        {
            Designator = "AMBER 1", Kind = AirwayKind.Coloured,
            Points = new[] { "RIDGE", "VR", "SAGES" },
            Notes = "The Valley range's 360 and 180 courses, north and south from the field. Its end fixes "
                  + "are NOT the Victor airway's — TALUS and SCREE sit on y = -1180 and the range's courses "
                  + "run through y = 0, so they needed their own. Crosses "
                  + "GREEN 1 overhead the range, where the cone of silence gives you the only positive fix "
                  + "the whole system offers.",
        },
        new()
        {
            Designator = "RED 1", Kind = AirwayKind.Coloured,
            Points = new[] { "BN", "SM" },
            Notes = "The beacon airway: Bench beacon to Summit beacon, 450 m east of GREEN 1 and parallel "
                  + "to it, over the crosswind runways rather than the main ones. Two low-frequency routes "
                  + "up the same valley is what this country looked like in 1935, and it is the rung between "
                  + "the range and the VOR — you track it on an ADF needle rather than by ear.",
        },
        new()
        {
            Designator = "V2",
            Points = new[] { "TALUS", "RIM", "SCREE" },
            Notes = "Across V1 at Rimrock, north into the Alaska range's foothills and south over the high "
                  + "desert. The crossing at RIM is the one place in this world where two airways meet, so it "
                  + "is the fix everything else is described from.",
        },
    };

    /// <summary>The fixes that are not navaids: a position on a radial at a DME distance, which is how you
    /// find one without GPS. Each says how it is identified.</summary>
    public static readonly ProcedureFix[] EnRouteFixes =
    {
        // SEALS is where V1 meets the sea and is also the Valley ILS's final approach fix and the fix its
        // procedure turn is based on, which is why it sits exactly 4.0 NM out on that localizer.
        new("SEALS", 850, 7970, FixRole.AirwayFix, 0, "VLY R-087 at 4.3 DME"),
        new("TALUS", 7600, -1180, FixRole.AirwayFix, 0, "RIM R-360 at 3.8 DME"),
        new("SCREE", -6200, -1180, FixRole.AirwayFix, 0, "RIM R-180 at 3.7 DME"),
        // The three approach fixes, each on its own localizer or final approach course.
        new("BUOYS", 850, 4520, FixRole.Faf, 0, "RIM R-088 at 3.1 DME"),
        new("SHELF", 850, 1220, FixRole.Faf, 0, "RIM R-084 at 1.3 DME"),
        new("CEDAR", -5880, -10050, FixRole.Faf, 0, "SUM R-180 at 3.4 DME"),
        // On the range's own courses, which run through y = 0 rather than through the Victor fixes.
        new("RIDGE", 7600, 0, FixRole.AirwayFix, 0, "the VR 360 course, 3.9 NM out"),
        new("SAGES", -6200, 0, FixRole.AirwayFix, 0, "the VR 180 course, 3.6 NM out"),
        // The range approach's let-down, out over the water on the 090 course.
        new("EMBER", 400, 4630, FixRole.Iaf, 0, "the VR 090 course, 2.5 NM out over the water"),
    };

    /// <summary>Where a point on an airway is — a navaid, or one of the fixes above.</summary>
    public static (double x, double y) PointAt(string id)
    {
        if (Navaids.TryById(id, out Vor v)) return (v.X, v.Y);
        if (Navaids.TryNdbById(id, out Ndb n)) return (n.X, n.Y);
        if (Navaids.TryRangeById(id, out AnRange r)) return (r.X, r.Y);
        foreach (ProcedureFix f in EnRouteFixes) if (f.Name == id) return (f.X, f.Y);
        throw new System.ArgumentException($"no airway point {id}");
    }

    /// <summary>The nearest station to a point — the one a leg's signal is judged against.</summary>
    public static Vor NearestStation(double x, double y)
    {
        Vor best = Navaids.Vors[0];
        double bd = double.MaxValue;
        foreach (Vor v in Navaids.Vors)
        {
            double d = (v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y);
            if (d < bd) { bd = d; best = v; }
        }
        return best;
    }

    /// <summary>
    /// The lowest altitude, feet MSL, at which <paramref name="station"/> is usable at (x, y). Found by
    /// climbing in 100 ft steps from the ground until the signal comes good, which is crude and exactly
    /// right: an MEA is a charted hundred-foot number, so resolving it finer would be false precision.
    /// Returns <see cref="CeilingFt"/> where no altitude works.
    /// </summary>
    public static double MinSignalAltitudeFt(WorldTerrain t, Vor station, double x, double y)
    {
        double groundFt = t.HeightAt(x, y, false, false) * Terps.FtPerM;
        for (double ft = Terps.RoundUpFt(groundFt + 100, 100); ft <= CeilingFt; ft += 100)
        {
            double altM = ft * Terps.MPerFt;
            // The cone of confusion is NOT a gap in coverage: it is the station passage, which is the fix
            // itself. Counting it as a loss of signal would have put a 24,000 ft MEA on every leg that ends
            // at a VOR, which is every leg — it did, until this line.
            if (NavReception.InConeOfConfusion(station.X, station.Y, station.AntennaM, x, y, altM)) return ft;
            if (NavReception.PathSignal(t, station.X, station.Y, station.AntennaM, x, y, altM, station.FreqMhz) >= UsableSignal)
                return ft;
        }
        return CeilingFt;
    }

    /// <summary>
    /// Both minimum altitudes for a leg, and which half of the rule set the MEA. The signal is checked at
    /// 1 NM intervals along the centreline against whichever station is nearest to each point, because that
    /// is the station a pilot would actually be tracking there.
    /// </summary>
    public static AirwayLeg Leg(WorldTerrain t, string fromId, string toId)
        => Leg(t, fromId, toId, AirwayKind.Victor);

    /// <summary>
    /// Both minimum altitudes for a leg. A COLOURED airway skips the reception half entirely, and that is
    /// not a shortcut: a low-frequency signal follows the ground, so there is no altitude at which it
    /// "comes out of the shadow" because it was never in one. The consequence is worth charting — an LF
    /// airway's MEA over this terrain is the obstacle figure and nothing else, which can put it BELOW the
    /// Victor airway that replaced it.
    /// </summary>
    public static AirwayLeg Leg(WorldTerrain t, string fromId, string toId, AirwayKind kind)
    {
        var (x0, y0) = PointAt(fromId);
        var (x1, y1) = PointAt(toId);
        double dx = x1 - x0, dy = y1 - y0;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        double nm = len / Terps.MetresPerNm;
        double course = NavReception.RadialDeg(x0, y0, x1, y1);

        double obstacleFt = Terps.MinimumAltitudeFt(t, x0, y0, x1, y1, NavSegment.EnRoute);

        // The signal is judged against the stations that DEFINE the leg — the one you are tracking away from
        // and the one you are tracking toward — and the better of the two counts, because that is the one a
        // pilot would be tuned to there. Not simply the nearest station: a leg is flown on its own navaids.
        var defining = new List<Vor>();
        if (Navaids.TryById(fromId, out Vor vf)) defining.Add(vf);
        if (Navaids.TryById(toId, out Vor vt)) defining.Add(vt);
        if (defining.Count == 0) defining.Add(NearestStation((x0 + x1) * 0.5, (y0 + y1) * 0.5));

        if (kind == AirwayKind.Coloured)
            return new AirwayLeg(fromId, toId, course, nm, obstacleFt, obstacleFt, "terrain");

        double signalFt = 0;
        int n = System.Math.Max(4, (int)System.Math.Ceiling(nm * 2));
        for (int i = 0; i <= n; i++)
        {
            double f = (double)i / n;
            double px = x0 + dx * f, py = y0 + dy * f;
            double best = CeilingFt;
            foreach (Vor v in defining) best = System.Math.Min(best, MinSignalAltitudeFt(t, v, px, py));
            if (best > signalFt) signalFt = best;
        }

        double mea = System.Math.Max(obstacleFt, signalFt);
        return new AirwayLeg(fromId, toId, course, nm, mea, obstacleFt,
                             signalFt > obstacleFt ? "signal" : "terrain");
    }

    /// <summary>Every leg of every airway, computed from the terrain.</summary>
    public static List<AirwayLeg> Legs(WorldTerrain t)
    {
        var l = new List<AirwayLeg>();
        foreach (Airway a in All)
            for (int i = 0; i + 1 < a.Points.Length; i++)
                l.Add(Leg(t, a.Points[i], a.Points[i + 1], a.Kind));
        return l;
    }
}
