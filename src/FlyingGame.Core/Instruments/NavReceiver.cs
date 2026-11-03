using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Nav;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// A VHF navigation receiver and its indicator: tuned by frequency, a course selector (OBS), a course
/// deviation needle, a TO/FROM/OFF flag — and, on NAV1 only, a glideslope needle with its own flag. NAV2 is the
/// same receiver with a course-only head, which is what a 1970s 172 with "dual nav, one glideslope" had.
///
/// Built on this world's stations and on <see cref="NavReception"/>: line of sight with Fresnel fade, the cone
/// over a VOR, the deviation signs. What this adds is the instrument:
///
///  - **VOR**: ±10° full scale (five dots at 2°). TO/FROM from the OBS against the radial; within ±3° of the
///    90° ambiguity (abeam the station) the flag shows OFF, because the TO and FROM signals are equal there.
///    Overhead, the station's antenna has a null and the flag shows as you pass through the cone.
///  - **Localizer**: course width TAILORED as the real ones are — ±350 ft at the threshold, so the half-width is
///    atan(106.7 m / antenna-to-threshold), and no wider than ±3° (FAA Order 6750.16). Front-course coverage
///    ±10° to 18 NM and ±35° to 10 NM; behind the antenna it is the back course, and its needle senses the
///    same side of the centreline the front course does (so it reverses when you fly it inbound).
///  - **Glideslope** (UHF, 329–335 MHz): the antenna stands 305 m (1,000 ft) past the threshold and 120 m to
///    the side, so on the centreline a 3° path crosses the threshold about 17 m (56 ft) up — the threshold
///    crossing height is the geometry, not a number. The needle is the elevation angle from the antenna minus the path angle,
///    ±0.7° full scale. Usable within ±8° of the localizer course and 10 NM, between 0.45 and 1.75 times the
///    path angle; outside that the flag shows.
///  - **The needle** is a d'Arsonval meter behind a receiver filter: a 0.5 s lag (E). Flagged, the meter is
///    unpowered and centres.
///
/// Signals are re-evaluated at 5 Hz (each one marches the terrain); the needles move every step.
/// </summary>
public sealed class NavReceiver
{
    public const double VorFullScaleDeg = 10.0, GsFullScaleDeg = 0.7, LocMaxHalfWidthDeg = 3.0;
    public const double AmbiguityHalfWidthDeg = 3.0, NeedleLagS = 0.5, MinVolts = 10.0;
    public const double GsAlongM = 305.0, GsOffsetM = 120.0, GsFreqMhz = 332.0;
    public const double LocBeyondStopEndM = 300.0, SignalPeriodS = 0.2;

    public readonly bool HasGlideslope;
    public double FreqMhz;
    public double ObsDeg;
    /// <summary>Failure hook: the receiver is dead (flag OFF, needles centred).</summary>
    public bool Failed;

    // ---- outputs ----
    public string Ident { get; private set; } = "";
    public bool IsLocalizer { get; private set; }
    /// <summary>The tuned VOR has a DME paired with it.</summary>
    public bool PairedDme { get; private set; }
    /// <summary>CDI, −1…+1 of full scale, + = the course is to the RIGHT.</summary>
    public double Cdi { get; private set; }
    /// <summary>Glideslope needle, −1…+1, + = the path is ABOVE you (fly up).</summary>
    public double Gs { get; private set; }
    /// <summary>+1 TO, −1 FROM, 0 OFF (the flag).</summary>
    public int ToFrom { get; private set; }
    public bool Flag => ToFrom == 0;
    public bool GsFlag { get; private set; } = true;
    /// <summary>The deviation the receiver is decoding before the meter lag, degrees (+ = course right).</summary>
    public double RawDeviationDeg { get; private set; }
    public double RawGsDeg { get; private set; }

    private double _sigClock = double.MaxValue;
    private double _lastFreq = double.NaN;
    private double _locSignal, _gsSignal, _vorSignal;

    public NavReceiver(bool hasGlideslope, double freqMhz, double obsDeg)
    {
        HasGlideslope = hasGlideslope; FreqMhz = freqMhz; ObsDeg = obsDeg;
    }

    /// <summary>Where a localizer's antenna and its glideslope stand, and how wide its course is.</summary>
    public readonly struct LocalizerSite
    {
        public readonly double Ax, Ay, AntennaM, HalfWidthDeg, GsX, GsY;
        public LocalizerSite(double ax, double ay, double antennaM, double halfWidthDeg, double gsX, double gsY)
        { Ax = ax; Ay = ay; AntennaM = antennaM; HalfWidthDeg = halfWidthDeg; GsX = gsX; GsY = gsY; }
    }

    private static readonly Dictionary<string, LocalizerSite> _sites = new();

    /// <summary>The site of <paramref name="ils"/>, from its runway's surveyed length.</summary>
    public static LocalizerSite SiteOf(Ils ils)
    {
        lock (_sites)
        {
            if (_sites.TryGetValue(ils.Id, out var s)) return s;
            double len = 1125.0;   // the island's paved strips, if the runway is not found below
            foreach (var r in ApproachGeometry.RunwayEnds())
                if (r.Airport == ils.Airport && r.Name == ils.Runway) { len = r.LengthM; break; }
            double c = ils.CourseDeg * V.Deg, ux = Math.Cos(c), uy = Math.Sin(c);
            double toAnt = len + LocBeyondStopEndM;
            double ax = ils.ThresholdX + ux * toAnt, ay = ils.ThresholdY + uy * toAnt;
            double half = Math.Min(LocMaxHalfWidthDeg, Math.Atan(350.0 * 0.3048 / toAnt) / V.Deg);
            // Glideslope: down the runway from the threshold, offset to the left of the landing direction.
            double gx = ils.ThresholdX + ux * GsAlongM + uy * GsOffsetM;
            double gy = ils.ThresholdY + uy * GsAlongM - ux * GsOffsetM;
            s = new LocalizerSite(ax, ay, ils.TdzeM + 3.0, half, gx, gy);
            _sites[ils.Id] = s;
            return s;
        }
    }

    public void Step(double dt, Vec3 posWorld, double volts, WorldTerrain? terrain)
    {
        double x = posWorld.X, y = posWorld.Y, alt = -posWorld.Z;
        bool powered = !Failed && volts >= MinVolts;
        if (FreqMhz != _lastFreq) { _sigClock = double.MaxValue; _lastFreq = FreqMhz; }
        _sigClock += dt;
        bool sample = _sigClock >= SignalPeriodS;
        if (sample) _sigClock = 0;

        double dev = 0, gsDev = 0;
        int tf = 0;
        bool gsOk = false;
        Ident = ""; IsLocalizer = false; PairedDme = false;

        if (powered && TryVor(FreqMhz, out Vor v))
        {
            Ident = v.Id; PairedDme = v.HasDme;
            if (sample) _vorSignal = terrain == null ? 1.0 : NavReception.PathSignal(terrain, v.X, v.Y, v.AntennaM, x, y, alt, v.FreqMhz);
            bool cone = NavReception.InConeOfConfusion(v.X, v.Y, v.AntennaM, x, y, alt);
            double radial = NavReception.RadialDeg(v.X, v.Y, x, y);
            double d = NavReception.CourseDeviationDeg(ObsDeg, radial, out bool to);
            double fromAbeam = Math.Abs(Math.Abs(NavReception.AngleDiff(ObsDeg, radial)) - 90.0);
            if (_vorSignal >= Airways.UsableSignal && !cone && fromAbeam > AmbiguityHalfWidthDeg)
            {
                tf = to ? 1 : -1;
                dev = d / VorFullScaleDeg;
            }
            RawDeviationDeg = d;
        }
        else if (powered && TryLocalizer(FreqMhz, out Ils ils))
        {
            Ident = ils.Id; IsLocalizer = true;
            LocalizerSite site = SiteOf(ils);
            if (sample)
            {
                _locSignal = terrain == null ? 1.0 : NavReception.PathSignal(terrain, site.Ax, site.Ay, site.AntennaM, x, y, alt, ils.FreqMhz);
                _gsSignal = terrain == null ? 1.0 : NavReception.PathSignal(terrain, site.GsX, site.GsY, ils.TdzeM + 1.0, x, y, alt, GsFreqMhz);
            }
            double brg = NavReception.RadialDeg(site.Ax, site.Ay, x, y);
            double nm = Math.Sqrt((x - site.Ax) * (x - site.Ax) + (y - site.Ay) * (y - site.Ay)) / NavReception.MetresPerNm;
            // Front course: bearing from the antenna near the reciprocal of the course. Back course: near the course.
            double offFront = NavReception.AngleDiff(ils.CourseDeg + 180.0, brg);
            double offBack = NavReception.AngleDiff(ils.CourseDeg, brg);
            bool front = Math.Abs(offFront) <= 90.0;
            double off = front ? offFront : offBack;
            bool covered = (nm <= 18.0 && Math.Abs(off) <= 10.0) || (nm <= 10.0 && Math.Abs(off) <= 35.0);
            double d = front ? offFront : -offBack;   // the back course senses the same side of the line
            RawDeviationDeg = d;
            if (_locSignal >= Airways.UsableSignal && covered)
            {
                tf = 1;   // a localizer head shows no FROM; the flag is the only thing that moves
                dev = d / site.HalfWidthDeg;
            }
            if (HasGlideslope && ils.HasGlideslope && front)
            {
                double gdx = x - site.GsX, gdy = y - site.GsY;
                double horiz = Math.Sqrt(gdx * gdx + gdy * gdy);
                double elev = Math.Atan2(alt - ils.TdzeM, horiz) / V.Deg;
                double gsNm = horiz / NavReception.MetresPerNm;
                bool inVolume = gsNm <= 10.0 && Math.Abs(offFront) <= 8.0
                                && elev >= 0.45 * ils.GlideslopeDeg && elev <= 1.75 * ils.GlideslopeDeg;
                RawGsDeg = ils.GlideslopeDeg - elev;
                if (inVolume && _gsSignal >= Airways.UsableSignal)
                {
                    gsOk = true;
                    gsDev = RawGsDeg / GsFullScaleDeg;
                }
            }
        }

        ToFrom = tf;
        GsFlag = !gsOk;
        double k = 1.0 - Math.Exp(-dt / NeedleLagS);
        Cdi += (Math.Clamp(tf == 0 ? 0.0 : dev, -1.0, 1.0) - Cdi) * k;
        Gs += (Math.Clamp(gsOk ? gsDev : 0.0, -1.0, 1.0) - Gs) * k;
    }

    /// <summary>Set the needles straight to where they would settle (a fresh flight, already tuned).</summary>
    public void Settle(Vec3 posWorld, double volts, WorldTerrain? terrain)
    {
        _sigClock = double.MaxValue;
        Step(1e-3, posWorld, volts, terrain);
        Step(60.0, posWorld, volts, terrain);   // one long step lets the meter catch up
    }

    public static bool TryVor(double freq, out Vor v)
    {
        foreach (Vor s in Navaids.Vors) if (Math.Abs(s.FreqMhz - freq) < 0.005) { v = s; return true; }
        v = default; return false;
    }

    public static bool TryLocalizer(double freq, out Ils ils)
    {
        foreach (Approach a in Approaches.All)
            if (a.Localizer.HasValue && Math.Abs(a.Localizer.Value.FreqMhz - freq) < 0.005) { ils = a.Localizer.Value; return true; }
        ils = default; return false;
    }
}
