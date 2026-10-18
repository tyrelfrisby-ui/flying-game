namespace FlyingGame.Sim.Ratings;

/// <summary>
/// Turns a <see cref="PilotLog"/> into the ratings somebody actually holds.
///
/// The one rule the owner stated explicitly, and the only one that needs enforcing: *"you must have
/// appropriate rating prior to getting the next one ie comm before atp Pvt before comm"*. So the walk stops
/// at the first level whose lessons are not all flown to standard — you cannot have a commercial with a
/// private still outstanding, however well you happen to fly.
/// </summary>
public static class RatingResolver
{
    /// <summary>Every class the pilot holds something in, and what.</summary>
    public static Dictionary<RatingClass, CertLevel> Resolve(PilotLog log, RatingBook book)
    {
        var held = new Dictionary<RatingClass, CertLevel>();
        foreach (RatingClass cls in Certificate.BadgeOrder)
        {
            if (!log.AnyIn(cls)) continue;                 // never flown in this class: no badge for it
            CertLevel have = CertLevel.None;
            foreach (CertLevel level in RatingBook.Ladder)
            {
                if (!Meets(log, book, cls, level)) break;  // the ladder has no missing rungs
                have = level;
            }
            if (have != CertLevel.None) held[cls] = have;
        }
        return held;
    }

    /// <summary>Is every lesson for this step flown to its standard?</summary>
    public static bool Meets(PilotLog log, RatingBook book, RatingClass cls, CertLevel level)
    {
        RatingStep? step = book.For(cls, level);
        if (step == null) return false;
        foreach (string lesson in step.Lessons)
            if (log.Best(cls, lesson) < step.PassScore) return false;
        return true;
    }

    /// <summary>
    /// What stands between a pilot and their next rating in a class: the level, and every lesson still short
    /// of the standard with the score it needs. This is the PRIVATE side of the system — see
    /// <see cref="PilotIdentity"/> — and it is what the badge screen shows its owner and nobody else.
    /// </summary>
    public static (CertLevel next, List<(string lesson, double need, double have)> missing) NextStep(
        PilotLog log, RatingBook book, RatingClass cls)
    {
        CertLevel have = Resolve(log, book).TryGetValue(cls, out CertLevel h) ? h : CertLevel.None;
        CertLevel next = CertLevel.None;
        foreach (CertLevel level in RatingBook.Ladder)
            if (level > have) { next = level; break; }

        var missing = new List<(string, double, double)>();
        if (next == CertLevel.None) return (next, missing);
        RatingStep? step = book.For(cls, next);
        if (step == null) return (next, missing);
        foreach (string lesson in step.Lessons)
        {
            double best = log.Best(cls, lesson);
            if (best < step.PassScore) missing.Add((lesson, step.PassScore, best));
        }
        return (next, missing);
    }
}
