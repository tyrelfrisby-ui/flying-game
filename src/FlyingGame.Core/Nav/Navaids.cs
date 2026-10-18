namespace FlyingGame.Core.Nav;

/// <summary>
/// This world's navigation aids: four VORs, two on airports and **two on hills**, which is what the owner
/// asked for on 2026-10-18 — *"only add two more vors on the map on hills so they get reasonable
/// reception"* — and which the measurement below says was the right instinct.
///
/// WHY THESE FOUR SITES. Every one was measured, not chosen. A probe swept the world for local maxima with
/// prominence, filtered for ground flat enough to stand a counterpoise on, and then scored each candidate by
/// what fraction of the world can SEE it (<see cref="NavReception"/>, terrain line of sight) at 1,000 and
/// 3,000 ft above the local ground:
///
///   | Site | Antenna | Sees, at 1,000 ft AGL | at 3,000 ft |
///   |---|---|---|---|
///   | Valley airport (400, 0) | 1,000 ft | 24.0 % | 31.7 % |
///   | Bench airport (400, −3450) | 3,000 ft | 22.6 % | 42.3 % |
///   | Mesa airport (400, −6750) | 5,000 ft | 14.3 % | 33.5 % |
///   | Summit airport (400, −10050) | 7,000 ft | 26.7 % | 43.0 % |
///   | **the first escarpment's crest (600, −1180)** | **3,472 ft** | **48.1 %** | **68.5 %** |
///   | **the third escarpment's crest (400, −7920)** | **8,135 ft** | **53.5 %** | **76.4 %** |
///
/// An airport in this world sits in the middle of its own plateau with a 2,000 ft wall on one side of it, so
/// a station on the field is in a hole: a VOR on the Mesa field is heard over a seventh of the world. A
/// station on the CREST of a wall is heard over half of it, because the wall it stands on is the thing that
/// was in the way. That is the whole argument for the two hill sites, and it is also why they are on the
/// crests rather than the flat tops behind them — the crest at (400, −1180) is 3,039 ft and sees 23.4 %; the
/// one 200 m north at (600, −1180) is 3,472 ft and sees 48.1 %. Four hundred feet of hill doubles the
/// coverage.
///
/// Together the four give **78.9 % of the world at least one station at 1,000 ft above the ground, 86.2 % at
/// 2,000 ft, and two or more stations — a cross fix, which is how you find out where you are with no GPS —
/// over 58.6 % at 1,000 ft and 72.9 % at 2,000 ft.** Along the corridor the four airports sit in, at 2,000 ft
/// above the ground, there are **always at least two** in view from the coast to the top field. That is what
/// makes this world navigable by radio alone.
///
/// MAGNETIC VARIATION IS ZERO, and that is a decision rather than a measurement: nothing in this world models
/// a magnetic field, so declaring the variation zero makes magnetic and true the same number and keeps the
/// compass, the charts and the radials honest with each other. Every course on every chart here is therefore
/// both.
///
/// FREQUENCIES follow the real band plan so they look right to a pilot: VORs above 112.00 or on an even tenth
/// below it, localizers on an odd tenth between 108.10 and 111.95.
/// </summary>
public static class Navaids
{
    /// <summary>Zero, by decision — see the class comment. Magnetic equals true everywhere here.</summary>
    public const double MagneticVariationDeg = 0.0;

    public static readonly Vor[] Vors =
    {
        new("VLY", "Valley", 400, 0, 304.8, 115.70, VorClass.Terminal, true,
            "on the Valley field, for its own approaches. Sees a quarter of the world: it is on a coastal "
            + "tableland with a 2,000 ft wall a kilometre west of it, and that wall is the shadow."),

        new("RIM", "Rimrock", 600, -1180, 1058.2, 116.20, VorClass.Low, true,
            "ON THE CREST of the first escarpment, 3,472 ft — 472 ft above the Bench plateau behind it. "
            + "Measured best of the low sites: 48.1 % of the world at 1,000 ft AGL against 24 % from the "
            + "Valley field, because it stands on top of the wall that was in the way."),

        new("CRS", "Crestline", 400, -7920, 2479.5, 114.80, VorClass.Low, true,
            "ON THE CREST of the third escarpment, 8,135 ft — 1,135 ft above the Summit plateau behind it, "
            + "and directly over the corridor the four airports sit in. The best site found anywhere: "
            + "53.5 % at 1,000 ft AGL, 76.4 % at 3,000 ft."),

        new("SUM", "Summit", 400, -10050, 2133.6, 113.40, VorClass.Terminal, true,
            "on the Summit field at 7,000 ft, for its own approaches and as the western end of the airway."),
    };

    public static Vor ById(string id)
    {
        foreach (Vor v in Vors) if (v.Id == id) return v;
        throw new System.ArgumentException($"no navaid {id}");
    }

    public static bool TryById(string id, out Vor v)
    {
        foreach (Vor x in Vors) if (x.Id == id) { v = x; return true; }
        v = default;
        return false;
    }

    /// <summary>Every frequency a receiver can be tuned to here, VORs and localizers, in order.</summary>
    public static double[] Frequencies()
    {
        var l = new List<double>();
        foreach (Vor v in Vors) l.Add(v.FreqMhz);
        foreach (Approach a in Approaches.All) if (a.Localizer.HasValue) l.Add(a.Localizer.Value.FreqMhz);
        l.Sort();
        return l.ToArray();
    }
}
