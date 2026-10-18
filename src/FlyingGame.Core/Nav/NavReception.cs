namespace FlyingGame.Core.Nav;

/// <summary>
/// Whether a VHF navigation station can actually be heard from where the aeroplane is, and how well.
///
/// Owner, 2026-10-18: *"make all of the vor's line of sight in function and analize the shaddows."* So a VOR
/// here is not a magic circle on a map. It is an antenna on the ground radiating at about 113 MHz, and the
/// mountains in this world are between it and you. Fly into a valley behind a ridge and the needle goes to
/// flag, exactly as it does in the real aeroplane, which is the whole reason a VOR airway has a minimum
/// *reception* altitude printed on it as well as a minimum obstacle-clearance altitude.
///
/// WHAT IS MODELLED
///
///  1. **Terrain between the two ends.** The straight line from the antenna to the aeroplane is marched and
///     the ground height sampled along it. This is the part that makes the shadows.
///  2. **Fresnel clearance, not a bare ray.** A radio path does not switch off the instant the ray grazes a
///     ridge; it starts to fade before the ridge touches the line and is not fully gone until well after.
///     What matters is the first Fresnel zone, radius √(λ·d₁·d₂/d). At 113 MHz, λ = 2.65 m, so a path 15 km
///     long whose obstacle is in the middle needs about 100 m of clearance to be unobstructed, and the
///     standard planning criterion is 60 % of that. Below 60 % the signal fades; at the ray itself it is
///     unusable. That gives a soft edge with a physical basis, and it is also simply more pleasant to fly:
///     a hard binary makes the needle chatter as you cross a ridge line.
///  3. **The cone of confusion.** Overhead the station the radials converge and there is nothing to track.
///     Real VORs are unusable within roughly 40–50° of total cone width above the antenna, so
///     <see cref="ConeHalfAngleDeg"/> is 25° from the vertical. At 3,000 ft above the station that is about
///     0.7 NM across, which is the station passage a pilot actually sees.
///  4. **The service volume.** A terminal VOR is guaranteed to 25 NM, a low-altitude one to 40 NM. Both are
///     far larger than this world, which is about 9 × 15 NM — so range never refuses anybody here, and the
///     check is written down anyway because leaving it out would be a lie about what a VOR is.
///
/// WHAT IS NOT, AND WHY
///
///  * **The Earth's curvature.** This world's ground is a flat height field, by the project's own design.
///    Over 15 km the real curve would drop an aeroplane 18 m below the chord, which is not nothing — but
///    inventing it here would put the radio on a different planet from the terrain and the altimeter.
///  * **Knife-edge diffraction, ducting, ground reflection lobes.** The Fresnel fade above is a
///    first-order stand-in for the first of those and the rest do not change where a pilot loses a needle.
/// </summary>
public static class NavReception
{
    /// <summary>Mid-band VOR frequency, MHz. The band is 108.00–117.95.</summary>
    public const double BandMidMhz = 113.0;

    /// <summary>Metres per MHz·m: c = 299.792458 m·MHz.</summary>
    public const double LightMMhz = 299.792458;

    /// <summary>Half-angle from the vertical inside which the station is overhead and useless.</summary>
    public const double ConeHalfAngleDeg = 25.0;

    /// <summary>The fraction of the first Fresnel zone that must be clear for a full-strength signal.</summary>
    public const double FresnelClearFraction = 0.6;

    /// <summary>How far apart the ground samples are along the path. 60 m resolves every ridge in this world
    /// — the escarpments are 183 m of horizontal run and the narrowest spur crest is wider than that.</summary>
    public const double MarchM = 60.0;

    public const double MetresPerNm = 1852.0;

    /// <summary>Wavelength in metres at a frequency in MHz.</summary>
    public static double WaveLengthM(double freqMhz) => LightMMhz / freqMhz;

    /// <summary>
    /// Radius of the first Fresnel zone, metres, at the point <paramref name="d1"/> along a path of total
    /// length <paramref name="d"/>: √(λ·d₁·d₂/d).
    /// </summary>
    public static double FresnelRadiusM(double freqMhz, double d1, double d)
    {
        double d2 = d - d1;
        if (d1 <= 0 || d2 <= 0 || d <= 0) return 0;
        return System.Math.Sqrt(WaveLengthM(freqMhz) * d1 * d2 / d);
    }

    /// <summary>
    /// Signal quality 0…1 from the antenna at (sx, sy, sAltM) to an aeroplane at (x, y, altM), terrain only
    /// — no cone and no range. 1 = the path is clear by 60 % of the first Fresnel zone everywhere along it;
    /// 0 = something stands in the straight line itself.
    /// </summary>
    public static double PathSignal(WorldTerrain t, double sx, double sy, double sAltM,
                                    double x, double y, double altM, double freqMhz)
    {
        double dx = x - sx, dy = y - sy;
        double d = System.Math.Sqrt(dx * dx + dy * dy);
        if (d < MarchM) return 1.0;
        int n = (int)System.Math.Ceiling(d / MarchM);
        double worst = 1.0;
        for (int i = 1; i < n; i++)
        {
            double f = (double)i / n;
            double ground = t.HeightAt(sx + dx * f, sy + dy * f, false, false);
            double ray = sAltM + (altM - sAltM) * f;
            double clearance = ray - ground;
            if (clearance <= 0) return 0.0;                       // the ground is in the line: nothing gets through
            double need = FresnelRadiusM(freqMhz, f * d, d) * FresnelClearFraction;
            double q = need <= 0 ? 1.0 : clearance / need;
            if (q < worst) worst = q;
            if (worst <= 0) return 0.0;
        }
        return worst >= 1.0 ? 1.0 : worst;
    }

    /// <summary>Is the aeroplane in the cone above the station, where there is no radial to track?</summary>
    public static bool InConeOfConfusion(double sx, double sy, double sAltM, double x, double y, double altM)
    {
        double above = altM - sAltM;
        if (above <= 0) return false;
        double dx = x - sx, dy = y - sy;
        double horiz = System.Math.Sqrt(dx * dx + dy * dy);
        // Inside the cone when the horizontal distance is less than altitude × tan(half-angle).
        return horiz < above * System.Math.Tan(ConeHalfAngleDeg * System.Math.PI / 180.0);
    }

    /// <summary>The radial the aeroplane is on: the bearing FROM the station, degrees, 0 = north (= magnetic
    /// here, because this world's variation is zero — see <see cref="Navaids.MagneticVariationDeg"/>).</summary>
    public static double RadialDeg(double sx, double sy, double x, double y)
    {
        double deg = System.Math.Atan2(y - sy, x - sx) * 180.0 / System.Math.PI;
        return deg < 0 ? deg + 360 : deg >= 360 ? deg - 360 : deg;
    }

    /// <summary>DME: slant range in nautical miles, which is what a DME reads and why it over-reads overhead.</summary>
    public static double SlantRangeNm(double sx, double sy, double sAltM, double x, double y, double altM)
    {
        double dx = x - sx, dy = y - sy, dz = altM - sAltM;
        return System.Math.Sqrt(dx * dx + dy * dy + dz * dz) / MetresPerNm;
    }

    /// <summary>
    /// Where the needle sits on a course deviation indicator: **positive means the course is to the right**,
    /// which is the whole convention — a CDI tells you which way to steer, not which way you have drifted.
    ///
    /// The two halves are NOT symmetrical and getting that wrong makes the instrument unflyable. Set the
    /// course selector within 90° of the radial you are on and you are tracking away from the station, FROM:
    /// the course line runs outward on that radial, and being clockwise of it means the course is to your
    /// left. Set it beyond 90° and you are tracking inbound, TO: the course line runs from the station back
    /// through you, and the sign flips.
    ///
    /// Worked, because the first version of this had the FROM case backwards. Station at the origin, you on
    /// the 010 radial, course selector on 360 — you are east of a line that runs due north, flying north, so
    /// the course is to your WEST and the needle must go LEFT. The radial minus the course is +10, so the
    /// needle is the NEGATIVE of it. Inbound: you on the 175 radial with 360 selected, which is 005 of east
    /// drift on a line running north to the station; the needle is again left, and there the arithmetic
    /// (course + 180) − radial = +5 comes out positive, so that branch is NOT negated.
    /// </summary>
    public static double CourseDeviationDeg(double obsDeg, double radialFromDeg, out bool toStation)
    {
        double dev = AngleDiff(obsDeg, radialFromDeg);
        toStation = System.Math.Abs(dev) > 90;
        return toStation ? AngleDiff(obsDeg + 180, radialFromDeg) : -dev;
    }

    /// <summary>
    /// A localizer's needle. The transmitter stands beyond the stop end, so the course line IS the runway
    /// centreline and the deviation is the angle between the inbound course and the bearing from the
    /// antenna — which is why a localizer gets more sensitive as you close in rather than staying a fixed
    /// width over the ground. Same sign convention: + = the course is to the right.
    ///
    /// This is the INBOUND case of <see cref="CourseDeviationDeg"/> with the antenna as the station, so it
    /// uses the same arithmetic and is NOT negated. Both of these were written with a minus sign in front
    /// the first time, which would have had a pilot chasing the needle off the centreline.
    /// </summary>
    public static double LocalizerDeviationDeg(double courseDeg, double bearingFromAntennaDeg)
        => AngleDiff(courseDeg + 180, bearingFromAntennaDeg);

    // ---------------------------------------------------------------- the beacons

    /// <summary>The most the terrain may bend an ADF needle, degrees.</summary>
    public const double NdbBearingErrorMaxDeg = 8.0;

    /// <summary>Metres of ground standing above the direct path that costs one degree of bearing error.</summary>
    public const double NdbErrorMetresPerDegree = 100.0;

    /// <summary>
    /// How strongly a beacon is heard, 0…1. **No terrain test at all**, and that is the whole difference
    /// between this and <see cref="PathSignal"/>: a low-frequency signal follows the ground rather than
    /// travelling in a straight line, so a ridge does not put a beacon in shadow the way it shadows a VOR.
    /// What limits a beacon is its power, so this fades from its rated range out to half again as far.
    /// </summary>
    public static double NdbSignal(Ndb n, double x, double y)
    {
        double dx = x - n.X, dy = y - n.Y;
        double nm = System.Math.Sqrt(dx * dx + dy * dy) / MetresPerNm;
        if (nm <= n.RangeNm) return 1.0;
        double over = (nm - n.RangeNm) / (n.RangeNm * 0.5);
        return over >= 1 ? 0.0 : 1.0 - over;
    }

    /// <summary>
    /// How far the terrain bends the needle, degrees, signed the way the needle moves — positive meaning it
    /// reads clockwise of the truth.
    ///
    /// This is "mountain effect", and it is the price of a beacon's reach. The signal is re-radiated by high
    /// ground beside the path, so the needle leans toward the hill. Modelled from the thing that causes it:
    /// the ground that stands ABOVE the direct line is what re-radiates, so the error grows with how far
    /// above the line it stands, and it leans toward whichever side of the track is higher. Deterministic,
    /// so a pilot can learn where this world's needles lie to them — which is the honest version of a
    /// beacon, and more useful for a lesson than a random wobble.
    /// </summary>
    public static double NdbBearingErrorDeg(WorldTerrain t, Ndb n, double x, double y, double altM)
    {
        double dx = x - n.X, dy = y - n.Y;
        double d = System.Math.Sqrt(dx * dx + dy * dy);
        if (d < MarchM) return 0;
        double ux = dx / d, uy = dy / d;          // along the path
        double px = -uy, py = ux;                  // across it
        double above = 0, left = 0, right = 0;
        int steps = 0;
        int nSteps = (int)System.Math.Ceiling(d / MarchM);
        for (int i = 1; i < nSteps; i++)
        {
            double f = (double)i / nSteps;
            double cx = n.X + dx * f, cy = n.Y + dy * f;
            double line = n.AntennaM + (altM - n.AntennaM) * f;
            above = System.Math.Max(above, t.HeightAt(cx, cy, false, false) - line);
            // Which side the high ground is on, sampled a third of a mile either side.
            double off = MetresPerNm / 3.0;
            left += t.HeightAt(cx - px * off, cy - py * off, false, false);
            right += t.HeightAt(cx + px * off, cy + py * off, false, false);
            steps++;
        }
        if (above <= 0 || steps == 0) return 0;
        double mag = System.Math.Min(NdbBearingErrorMaxDeg, above / NdbErrorMetresPerDegree);
        // "Right" is the +across side, which is clockwise of the track from the station outward.
        return right >= left ? mag : -mag;
    }

    /// <summary>
    /// What an ADF needle actually shows: the RELATIVE bearing, degrees clockwise from the nose. Not a
    /// bearing to anywhere — the aeroplane's heading has to be added to get that, which is the whole skill.
    /// </summary>
    public static double RelativeBearingDeg(double bearingToStationDeg, double headingDeg)
    {
        double rb = (bearingToStationDeg - headingDeg) % 360.0;
        return rb < 0 ? rb + 360 : rb;
    }

    /// <summary>The bearing FROM the aeroplane TO a point — the reciprocal of a radial.</summary>
    public static double BearingToDeg(double x, double y, double tx, double ty) => RadialDeg(x, y, tx, ty);

    /// <summary>Signed difference b − a in degrees, folded to −180…180.</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = (b - a) % 360.0;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }
}
