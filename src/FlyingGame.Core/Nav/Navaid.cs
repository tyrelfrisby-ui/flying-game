namespace FlyingGame.Core.Nav;

/// <summary>Service volume. The whole of this world is inside the smallest of them; see <see cref="NavReception"/>.</summary>
public enum VorClass { Terminal, Low }

/// <summary>
/// A VOR, with or without DME. Position is the world's own frame — x north, y east, metres — and
/// <see cref="GroundM"/> is the ground it stands on, measured from the terrain rather than chosen.
/// </summary>
public readonly struct Vor
{
    /// <summary>Three letters, as a navaid identifier is.</summary>
    public readonly string Id;
    public readonly string Name;
    public readonly double X, Y;
    /// <summary>Ground elevation at the site, metres. Measured — see NavSiteTests.</summary>
    public readonly double GroundM;
    /// <summary>Antenna height above the ground, metres. A VOR counterpoise sits a few metres up.</summary>
    public readonly double MastM;
    public readonly double FreqMhz;
    public readonly VorClass Class;
    public readonly bool HasDme;
    /// <summary>Why this site and not another one. Every station in this world has to answer that.</summary>
    public readonly string Why;

    public Vor(string id, string name, double x, double y, double groundM, double freqMhz,
               VorClass cls, bool hasDme, string why, double mastM = 5.0)
    {
        Id = id; Name = name; X = x; Y = y; GroundM = groundM; FreqMhz = freqMhz;
        Class = cls; HasDme = hasDme; Why = why; MastM = mastM;
    }

    /// <summary>The antenna's altitude, metres.</summary>
    public double AntennaM => GroundM + MastM;

    /// <summary>Service volume radius, nautical miles.</summary>
    public double ServiceNm => Class == VorClass.Terminal ? 25.0 : 40.0;
}

/// <summary>
/// An ILS: a localizer along the runway centreline and, where there is one, a glideslope. The localizer's
/// course is the runway's own heading, which is what makes it a precision approach — a VOR final may be up to
/// 30° off the centreline, a localizer may not be off it at all.
/// </summary>
public readonly struct Ils
{
    public readonly string Id;                  // the localizer identifier, e.g. "I-VLY"
    public readonly string Airport;             // "Valley"
    public readonly string Runway;              // "27"
    public readonly double FreqMhz;             // 108.10–111.95, odd tenths
    /// <summary>Threshold of the runway served, world frame, and its elevation (the touchdown zone).</summary>
    public readonly double ThresholdX, ThresholdY, TdzeM;
    /// <summary>The course flown inbound, degrees — the runway heading.</summary>
    public readonly double CourseDeg;
    public readonly double GlideslopeDeg;
    public readonly bool HasGlideslope;
    /// <summary>Decision altitude, feet MSL. Derived, not chosen — see <see cref="Approaches"/>.</summary>
    public readonly double DecisionAltitudeFt;

    public Ils(string id, string airport, string runway, double freqMhz,
               double thresholdX, double thresholdY, double tdzeM, double courseDeg,
               double glideslopeDeg, bool hasGlideslope, double decisionAltitudeFt)
    {
        Id = id; Airport = airport; Runway = runway; FreqMhz = freqMhz;
        ThresholdX = thresholdX; ThresholdY = thresholdY; TdzeM = tdzeM; CourseDeg = courseDeg;
        GlideslopeDeg = glideslopeDeg; HasGlideslope = hasGlideslope; DecisionAltitudeFt = decisionAltitudeFt;
    }

    /// <summary>Height above touchdown of the decision altitude, feet.</summary>
    public double DecisionHeightFt => DecisionAltitudeFt - TdzeM * Terps.FtPerM;
}

/// <summary>
/// A non-directional beacon. Owner, 2026-10-18: *"Add two ndb's and build a few vor and adf approaches."*
///
/// An NDB is a different animal from a VOR and the difference is the point of having them here. It transmits
/// in the low/medium band (190–535 kHz), where the signal follows the ground instead of travelling in a
/// straight line — so **an NDB is heard behind a ridge that a VOR cannot see over**, which in a world made of
/// 2,000 ft walls is exactly where you want one. What you pay for that is accuracy: an LF signal is bent by
/// high ground beside the path, so the needle leans toward the terrain, and the aeroplane has to work out the
/// bearing itself from a needle that points at the station rather than from a radial the station tells you.
///
/// Both of this world's beacons sit on an extended runway centreline a few hundred metres past the stop end,
/// which is where a locator goes: track inbound to it and you are tracking down the centreline.
/// </summary>
public readonly struct Ndb
{
    /// <summary>Two letters, as a locator's identifier is.</summary>
    public readonly string Id;
    public readonly string Name;
    public readonly double X, Y, GroundM;
    public readonly double FreqKhz;
    /// <summary>Rated coverage, nautical miles. A locator of this power is good for about fifteen.</summary>
    public readonly double RangeNm;
    public readonly string Why;

    public Ndb(string id, string name, double x, double y, double groundM, double freqKhz, double rangeNm, string why)
    { Id = id; Name = name; X = x; Y = y; GroundM = groundM; FreqKhz = freqKhz; RangeNm = rangeNm; Why = why; }

    public double AntennaM => GroundM + 2.0;
}
