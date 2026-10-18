using FlyingGame.Core.DataContracts;

namespace FlyingGame.Sim.Ratings;

/// <summary>
/// The category and class a rating is held in, named the way a real certificate names them.
/// Owner, 2026-10-18: *"change power to asel or ases as applicable if they are taking lessons in both etc
/// so glider asel ases amel ames etc."*
/// </summary>
public enum RatingClass
{
    Glider,
    /// <summary>Airplane single-engine land.</summary>
    Asel,
    /// <summary>Airplane single-engine sea.</summary>
    Ases,
    /// <summary>Airplane multi-engine land.</summary>
    Amel,
    /// <summary>Airplane multi-engine sea.</summary>
    Ames,
}

/// <summary>
/// How far up the ladder a pilot has got in one class. The order matters: the rule is that you may only
/// ever hold the next one up, never skip — *"minimums are you must have appropriate rating prior to getting
/// the next one ie comm before atp Pvt before comm"*.
/// </summary>
public enum CertLevel
{
    None = 0,
    StudentPreSolo = 1,
    StudentSolo = 2,
    Private = 3,
    Commercial = 4,
    /// <summary>Airline transport pilot: the top of the ladder, and only on top of a commercial.</summary>
    Atp = 5,
}

/// <summary>
/// What goes on the badge above somebody's aeroplane, beside their handle.
///
/// This is modelled on real certificates but it is **skill and test based rather than hour based**, which is
/// the owner's decision and the right one for a game: you get the rating by flying the lessons to the
/// standard and passing the check, not by sitting in the air for long enough. See
/// <see cref="RatingBook"/> for what each step costs.
/// </summary>
public static class Certificate
{
    /// <summary>The classes in the order a badge lists them: glider first, then land before sea, single before multi.</summary>
    public static readonly RatingClass[] BadgeOrder =
        { RatingClass.Glider, RatingClass.Asel, RatingClass.Ases, RatingClass.Amel, RatingClass.Ames };

    public static string ClassAbbrev(RatingClass c) => c switch
    {
        RatingClass.Glider => "GLIDER",
        RatingClass.Asel => "ASEL",
        RatingClass.Ases => "ASES",
        RatingClass.Amel => "AMEL",
        _ => "AMES",
    };

    public static string LevelAbbrev(CertLevel l) => l switch
    {
        CertLevel.StudentPreSolo => "STUDENT",
        CertLevel.StudentSolo => "SOLO",
        CertLevel.Private => "PVT",
        CertLevel.Commercial => "COMM",
        CertLevel.Atp => "ATP",
        _ => "",
    };

    /// <summary>The level below one, which is the rating you must already hold to earn it.</summary>
    public static CertLevel Prerequisite(CertLevel l) => l <= CertLevel.StudentPreSolo ? CertLevel.None : (CertLevel)((int)l - 1);

    /// <summary>
    /// Which class a flight in this aeroplane counts toward, worked out from the aeroplane itself rather
    /// than from a list that would go stale: a glider by its id, sea by whether it has floats, multi by
    /// whether it has more than one engine mount. The AirCam is a twin, so it is AMEL; the amphibious one
    /// is AMES; the H-4 is eight engines on a hull, which is AMES as well.
    /// </summary>
    public static RatingClass ClassOf(string aircraftId, AircraftConfig? cfg)
    {
        if (!string.IsNullOrEmpty(aircraftId) && aircraftId.StartsWith("glider", StringComparison.OrdinalIgnoreCase))
            return RatingClass.Glider;
        bool sea = cfg?.Floats != null;
        bool multi = (cfg?.Engines?.Count ?? 0) >= 2;
        return multi ? (sea ? RatingClass.Ames : RatingClass.Amel)
                     : (sea ? RatingClass.Ases : RatingClass.Asel);
    }

    /// <summary>
    /// The badge, long form: every class held, with its level. "GLIDER COMM · ASEL PVT".
    /// </summary>
    public static string Describe(IReadOnlyDictionary<RatingClass, CertLevel> held)
    {
        var parts = new List<string>();
        foreach (RatingClass c in BadgeOrder)
            if (held.TryGetValue(c, out CertLevel l) && l != CertLevel.None)
                parts.Add($"{ClassAbbrev(c)} {LevelAbbrev(l)}");
        return parts.Count == 0 ? "NO RATING" : string.Join(" · ", parts);
    }

    /// <summary>
    /// The badge, short form, for a name tag floating over an aeroplane where there is room for about two
    /// dozen characters.
    ///
    /// An ATP is written the way it is spoken — "ATP ASEL AMEL" — because once you hold it, it IS the
    /// certificate and the classes are ratings on it. Below that, the badge leads with the highest thing you
    /// hold and adds the next one if it fits, because a tag nobody can read at two hundred yards is not a
    /// badge.
    /// </summary>
    public static string ShortBadge(IReadOnlyDictionary<RatingClass, CertLevel> held, int maxChars = 26)
    {
        var atp = new List<string>();
        foreach (RatingClass c in BadgeOrder)
            if (held.TryGetValue(c, out CertLevel l) && l == CertLevel.Atp) atp.Add(ClassAbbrev(c));
        if (atp.Count > 0) return Trim("ATP " + string.Join(" ", atp), maxChars);

        // Highest first, then as many more as fit.
        var ordered = new List<(RatingClass c, CertLevel l)>();
        foreach (RatingClass c in BadgeOrder)
            if (held.TryGetValue(c, out CertLevel l) && l != CertLevel.None) ordered.Add((c, l));
        if (ordered.Count == 0) return "STUDENT";
        ordered.Sort((a, b) => b.l.CompareTo(a.l));

        var sb = new System.Text.StringBuilder();
        int shown = 0;
        foreach (var (c, l) in ordered)
        {
            string piece = $"{ClassAbbrev(c)} {LevelAbbrev(l)}";
            if (sb.Length > 0 && sb.Length + 3 + piece.Length > maxChars) break;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(piece);
            shown++;
        }
        if (shown < ordered.Count) sb.Append($" +{ordered.Count - shown}");
        return Trim(sb.ToString(), maxChars);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
