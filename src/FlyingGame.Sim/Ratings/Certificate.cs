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
/// How far up the ladder a pilot has got. The order matters: every level is earned on top of the one below
/// it, never instead of it. Owner, 2026-11-01: the levels are exactly Student pre-solo, Student solo, Pilot,
/// Instrument (shown to the player as "Blind Flying"), Commercial, Flight Instructor.
///
/// THESE NUMBERS ARE NOT THE OLD ONES. Before 2026-11-01 the enum was None 0, StudentPreSolo 1,
/// StudentSolo 2, Private 3, Commercial 4, Atp 5. Nothing ever persisted a level — the save is the lesson
/// log, and the level is worked out from it — but a value written by an older build anywhere (a log, the
/// wire, a test fixture) goes through <see cref="LegacyLevels"/>, never a cast.
/// </summary>
public enum CertLevel
{
    None = 0,
    StudentPreSolo = 1,
    StudentSolo = 2,
    Pilot = 3,
    /// <summary>Instrument. The PLAYER sees "Blind Flying" — see <see cref="Certificate.LevelName"/>.</summary>
    Instrument = 4,
    Commercial = 5,
    /// <summary>The top of the ladder, and only on top of a commercial.</summary>
    FlightInstructor = 6,
}

/// <summary>
/// What goes on the badge above somebody's aeroplane, beside their handle.
///
/// This is modelled on real flying but it is **skill and test based rather than hour based**, which is the
/// owner's decision and the right one for a game: you get the level by flying the lessons to the standard
/// in the aeroplanes the ladder asks for, not by sitting in the air for long enough. See
/// <see cref="RatingBook"/> for what each step costs.
///
/// WORDS. Owner, 2026-11-01: the player never reads "private", "certified" or "license"/"licence".
/// `PlayerTextTests` scans every string this class can produce, so a badge cannot quietly grow one again.
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

    /// <summary>The level as the player reads it in a sentence: "You are now a Commercial pilot".</summary>
    public static string LevelName(CertLevel l) => l switch
    {
        CertLevel.StudentPreSolo => "Student pilot, pre-solo",
        CertLevel.StudentSolo => "Student pilot, solo",
        CertLevel.Pilot => "Pilot",
        CertLevel.Instrument => "Blind Flying",
        CertLevel.Commercial => "Commercial pilot",
        CertLevel.FlightInstructor => "Flight Instructor",
        _ => "Not yet flown",
    };

    /// <summary>The level on a name tag, where there is room for about two dozen characters.</summary>
    public static string LevelAbbrev(CertLevel l) => l switch
    {
        CertLevel.StudentPreSolo => "STUDENT",
        CertLevel.StudentSolo => "SOLO",
        CertLevel.Pilot => "PILOT",
        CertLevel.Instrument => "BLIND FLYING",
        CertLevel.Commercial => "COMMERCIAL",
        CertLevel.FlightInstructor => "INSTRUCTOR",
        _ => "",
    };

    /// <summary>The level below one, which is the level you must already hold to earn it.</summary>
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
    /// The badge, long form, for the pilot's own profile: the level, then every class they are rated in.
    /// "Commercial pilot · GLIDER ASEL".
    /// </summary>
    public static string Describe(PilotStanding s)
    {
        if (s.Level == CertLevel.None) return "NO RATING";
        string classes = ClassList(s.Classes);
        return classes.Length == 0 ? LevelName(s.Level) : $"{LevelName(s.Level)} · {classes}";
    }

    /// <summary>
    /// The badge, short form, for a name tag floating over an aeroplane. The level leads, and the classes
    /// follow only if they fit whole, because a tag nobody can read at two hundred yards is not a badge.
    /// </summary>
    public static string ShortBadge(PilotStanding s, int maxChars = 26)
    {
        if (s.Level == CertLevel.None) return "STUDENT";
        string level = LevelAbbrev(s.Level);
        var sb = new System.Text.StringBuilder(level);
        int shown = 0;
        foreach (RatingClass c in BadgeOrder)
        {
            if (!s.Classes.Contains(c)) continue;
            string piece = ClassAbbrev(c);
            string sep = shown == 0 ? " · " : " ";
            if (sb.Length + sep.Length + piece.Length > maxChars) break;
            sb.Append(sep).Append(piece);
            shown++;
        }
        return Trim(sb.ToString(), maxChars);
    }

    private static string ClassList(IReadOnlyCollection<RatingClass> classes)
    {
        var parts = new List<string>();
        foreach (RatingClass c in BadgeOrder) if (classes.Contains(c)) parts.Add(ClassAbbrev(c));
        return string.Join(" ", parts);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}

/// <summary>
/// The names and numbers an OLDER BUILD used, and what each one means now. Two places can still carry them:
///
///  * **A saved lesson log** from before the restructure. It has no levels in it — only the best score per
///    lesson per class — so it is re-read under the rules it was earned under and the result carried over
///    (<see cref="SaveMigration"/>).
///  * **The wire.** A pilot on an older build publishes a badge string such as "ASEL PVT · GLIDER SOLO",
///    and this build draws it over their aeroplane. <see cref="UpgradeBadge"/> rewrites the old words so
///    the tag never shows the player a word the owner has banned.
///
/// The mapping, which is the owner's ("old Private → Pilot, old Student → Student solo or pre-solo depending
/// on what they had earned"):
///
///   old None 0          → None
///   old StudentPreSolo 1→ Student pre-solo
///   old StudentSolo 2   → Student solo
///   old Private 3       → Pilot
///   old Commercial 4    → Commercial
///   old Atp 5           → Commercial  (the airline ladder is gone; an old ATP flew the commercial standard
///                                       and more, but never anything a Flight Instructor is asked for)
/// </summary>
public static class LegacyLevels
{
    /// <summary>An old enum NUMBER. Anything unknown is None: a level is never invented.</summary>
    public static CertLevel FromInt(int old) => old switch
    {
        1 => CertLevel.StudentPreSolo,
        2 => CertLevel.StudentSolo,
        3 => CertLevel.Pilot,
        4 => CertLevel.Commercial,
        5 => CertLevel.Commercial,
        _ => CertLevel.None,
    };

    /// <summary>An old enum NAME, or a current one. Case-insensitive; anything unknown is None.</summary>
    public static CertLevel FromName(string? old)
    {
        switch ((old ?? "").Trim().ToLowerInvariant())
        {
            case "studentpresolo": return CertLevel.StudentPreSolo;
            case "studentsolo": return CertLevel.StudentSolo;
            case "private": case "pilot": return CertLevel.Pilot;
            case "instrument": return CertLevel.Instrument;
            case "commercial": case "atp": return CertLevel.Commercial;
            case "flightinstructor": return CertLevel.FlightInstructor;
            default: return CertLevel.None;
        }
    }

    /// <summary>
    /// A badge string from an older build, rewritten in the current words: "ASEL PVT" → "ASEL PILOT",
    /// "ASEL COMM" → "ASEL COMMERCIAL", "ATP ASEL AMEL" → "COMMERCIAL ASEL AMEL". Words it does not know are
    /// left alone, so a current badge passes through unchanged.
    /// </summary>
    public static string UpgradeBadge(string? badge)
    {
        if (string.IsNullOrEmpty(badge)) return "";
        string[] words = badge!.Split(' ');
        for (int i = 0; i < words.Length; i++)
            words[i] = words[i] switch
            {
                "PVT" => "PILOT",
                "COMM" => "COMMERCIAL",
                "ATP" => "COMMERCIAL",
                _ => words[i],
            };
        return string.Join(" ", words);
    }
}
