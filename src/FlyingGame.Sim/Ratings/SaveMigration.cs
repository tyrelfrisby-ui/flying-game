namespace FlyingGame.Sim.Ratings;

/// <summary>
/// Carries a lesson log saved by an older build over to the 2026-11-01 ladder, so nobody loses what they
/// earned when the ladder changed shape.
///
/// WHAT AN OLD SAVE HOLDS. PlayerPrefs "ratings.log": the best score per lesson per CLASS ("Asel|Flare=81"),
/// and nothing else — no levels, and no record of which aeroplane anything was flown in. The old build worked
/// its levels out from that each time, per class, on the ladder Student pre-solo → Student solo → Private →
/// Commercial → ATP. The new ladder is a path through particular aeroplanes, which an old log cannot show, so
/// the scores alone would drop everybody back to a student.
///
/// SO: the old log is read under the OLD rules (frozen below, exactly as they were), the highest level held in
/// any class is mapped to the new ladder with <see cref="LegacyLevels.FromInt"/>, and the stages that lead to
/// that level are written into the log as CARRIED OVER ("!pilot=1"). The scores are kept as they were and keep
/// counting toward the class ratings.
///
///   old level (any class)     new level          stages carried over
///   none flown                None               —
///   Student pre-solo          Student pre-solo   —   (it comes from having flown anything)
///   Student solo, glider      Student solo       glider-solo
///   Student solo, powered     Student solo       glider-solo, cub-solo
///   Private                   Pilot              glider-solo, cub-solo, pilot
///   Commercial                Commercial         … pilot, aerobatic-glider, instrument, commercial
///   ATP                       Commercial         the same — the airline ladder is gone, and an ATP was never
///                                                asked for anything a Flight Instructor is
///
/// It runs once: the migrated log is stamped with the current version and saved that way.
/// </summary>
public static class SaveMigration
{
    /// <summary>What the migration found, for logging and for the tests.</summary>
    public readonly struct Result
    {
        public readonly int OldLevel;          // 0..5, the old enum's number
        public readonly CertLevel NewLevel;
        public readonly string[] Granted;
        public Result(int oldLevel, CertLevel newLevel, string[] granted) { OldLevel = oldLevel; NewLevel = newLevel; Granted = granted; }
    }

    /// <summary>The old enum's numbers, frozen. Do not reuse <see cref="CertLevel"/> here: its numbers moved.</summary>
    public const int OldNone = 0, OldStudentPreSolo = 1, OldStudentSolo = 2, OldPrivate = 3, OldCommercial = 4, OldAtp = 5;

    /// <summary>Migrate in place. Safe to call on a current log: it does nothing to one.</summary>
    public static Result Migrate(PilotLog log)
    {
        if (log.Version >= PilotLog.CurrentVersion) return new Result(OldNone, CertLevel.None, Array.Empty<string>());

        int oldBest = OldNone;
        bool poweredSolo = false;
        foreach (RatingClass cls in Certificate.BadgeOrder)
        {
            int lvl = OldLevelIn(log, cls);
            if (lvl > oldBest) oldBest = lvl;
            if (cls != RatingClass.Glider && lvl >= OldStudentSolo) poweredSolo = true;
        }

        CertLevel now = LegacyLevels.FromInt(oldBest);
        var grants = new List<string>();
        if (now >= CertLevel.StudentSolo) grants.Add(RatingBook.GliderSolo);
        if (now >= CertLevel.Pilot || (now == CertLevel.StudentSolo && poweredSolo)) grants.Add(RatingBook.CubSolo);
        if (now >= CertLevel.Pilot) grants.Add(RatingBook.PilotStage);
        if (now >= CertLevel.Commercial)
        {
            grants.Add(RatingBook.AerobaticGlider);
            grants.Add(RatingBook.InstrumentStage);
            grants.Add(RatingBook.CommercialStage);
        }
        foreach (string g in grants) log.Grant(g);
        log.MarkCurrent();
        return new Result(oldBest, now, grants.ToArray());
    }

    /// <summary>
    /// The level an old build would have shown for one class, by the old build's own rules: anything flown is a
    /// pre-solo student; the pre-solo list to 70 % is solo; the whole list to 78 / 88 / 93 % is Private /
    /// Commercial / ATP; and the walk stops at the first rung not met.
    /// </summary>
    public static int OldLevelIn(PilotLog log, RatingClass cls)
    {
        if (!log.AnyIn(cls)) return OldNone;
        bool glider = cls == RatingClass.Glider;
        string[] solo = glider ? OldGliderPreSolo : OldPreSolo;
        string[] full = glider ? OldGliderFull : OldFull;
        int have = OldStudentPreSolo;
        if (!All(log, cls, solo, 70)) return have;
        have = OldStudentSolo;
        if (!All(log, cls, full, 78)) return have;
        have = OldPrivate;
        if (!All(log, cls, full, 88)) return have;
        have = OldCommercial;
        if (!All(log, cls, full, 93)) return have;
        return OldAtp;
    }

    // The old build's lesson lists, copied here rather than shared with RatingBook so that a later change to
    // the current syllabus cannot change what an old save is judged to have earned.
    private static readonly string[] OldPreSolo = { "Straight", "ClimbLevelDescend", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
    private static readonly string[] OldFull =
    {
        "Straight", "ClimbLevelDescend", "GlideRear", "GlideSide", "ClimbVyRear", "ClimbVxSide",
        "StallElevator", "StallRudder", "StallSideView", "STurnsTest",
        "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
        "CrosswindAileron", "CrosswindRudder",
    };
    private static readonly string[] OldGliderPreSolo = { "Straight", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
    private static readonly string[] OldGliderFull =
    {
        "Straight", "GlideRear", "GlideSide", "StallElevator", "StallRudder", "StallSideView",
        "STurnsTest", "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
        "CrosswindAileron", "CrosswindRudder",
    };

    private static bool All(PilotLog log, RatingClass cls, string[] lessons, double score)
    {
        foreach (string l in lessons) if (log.Best(cls, l) < score) return false;
        return true;
    }
}
