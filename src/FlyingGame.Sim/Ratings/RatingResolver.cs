namespace FlyingGame.Sim.Ratings;

/// <summary>Where a pilot stands: the level, the stages behind them, and the classes on their badge.</summary>
public sealed class PilotStanding
{
    public CertLevel Level = CertLevel.None;
    /// <summary>Every stage complete, flown or carried over, in book order.</summary>
    public List<string> Stages = new();
    /// <summary>The stages that came from an older save rather than being flown on this build.</summary>
    public List<string> CarriedOver = new();
    /// <summary>The classes the pilot is rated in: the class's whole list flown in it to the Pilot standard.</summary>
    public HashSet<RatingClass> Classes = new();
    /// <summary>
    /// Which of the Decathlon and the Great Lakes the Pilot stage was passed in — the player's choice — or
    /// null if it has not been, or was carried over from an older save that never said.
    /// </summary>
    public string? PilotAircraft;

    public bool Has(string stageId) => Stages.Contains(stageId);
}

/// <summary>
/// Turns a <see cref="PilotLog"/> into where somebody actually stands.
///
/// The one rule the owner stated explicitly, and the only one that needs enforcing: *"you must have
/// appropriate rating prior to getting the next one"*. So a stage counts only once every stage it requires
/// is complete — you cannot fly your way to Commercial with the aerobatic glider still outstanding, however
/// well you happen to fly.
/// </summary>
public static class RatingResolver
{
    public static PilotStanding Resolve(PilotLog log, RatingBook book)
    {
        var s = new PilotStanding();
        var flown = log.AircraftFlown();

        // Book order is dependency order, so one pass sees every requirement before what needs it.
        foreach (Stage st in book.Stages)
        {
            if (log.Granted(st.Id))
            {
                s.Stages.Add(st.Id);
                s.CarriedOver.Add(st.Id);
            }
            else if (RequirementsMet(st, s))
            {
                string? ac = PassedIn(log, st, flown);
                if (ac == null) continue;
                s.Stages.Add(st.Id);
                if (st.Id == RatingBook.PilotStage) s.PilotAircraft = ac;
            }
            else continue;
            if (st.Grants > s.Level) s.Level = st.Grants;
        }
        if (s.Level == CertLevel.None && log.AnyFlown()) s.Level = CertLevel.StudentPreSolo;

        foreach (var kv in book.ClassLessons)
        {
            bool all = kv.Value.Length > 0;
            foreach (string lesson in kv.Value)
                if (log.Best(kv.Key, lesson) < book.ClassRatingScore) { all = false; break; }
            if (all) s.Classes.Add(kv.Key);
        }
        return s;
    }

    private static bool RequirementsMet(Stage st, PilotStanding s)
    {
        foreach (string r in st.Requires) if (!s.Has(r)) return false;
        return true;
    }

    /// <summary>The aeroplane this stage has been passed in, or null. Every lesson in ONE aeroplane.</summary>
    private static string? PassedIn(PilotLog log, Stage st, Dictionary<string, HashSet<RatingClass>> flown)
    {
        foreach (string ac in Candidates(st, flown))
        {
            bool all = true;
            foreach (string lesson in st.Lessons)
                if (log.Best(ac, lesson) < st.PassScore) { all = false; break; }
            if (all) return ac;
        }
        return null;
    }

    /// <summary>The aeroplanes a stage may be flown in, among those flown, in a stable order.</summary>
    private static IEnumerable<string> Candidates(Stage st, Dictionary<string, HashSet<RatingClass>> flown)
    {
        if (st.Aircraft.Length > 0)
        {
            foreach (string ac in st.Aircraft)
                if (!Hangar.IsRetired(ac)) yield return ac;
            yield break;
        }
        var ids = new List<string>(flown.Keys);
        ids.Sort(StringComparer.Ordinal);
        foreach (string ac in ids)
        {
            if (!Hangar.IsPickable(ac)) continue;
            foreach (RatingClass c in st.Classes)
                if (flown[ac].Contains(c)) { yield return ac; break; }
        }
    }

    /// <summary>
    /// The stages a pilot can work on now — every requirement met, not yet complete — and for each, the
    /// lessons still short of the standard in the aeroplane closest to passing it. This is the PRIVATE side
    /// of the system — see <see cref="PilotIdentity"/> — and it is what the badge screen shows its owner and
    /// nobody else.
    /// </summary>
    public static List<(Stage stage, List<(string lesson, double need, double have)> missing)> Next(PilotLog log, RatingBook book)
    {
        PilotStanding s = Resolve(log, book);
        var flown = log.AircraftFlown();
        var list = new List<(Stage, List<(string, double, double)>)>();
        foreach (Stage st in book.Stages)
        {
            if (s.Has(st.Id) || !RequirementsMet(st, s)) continue;
            List<(string, double, double)>? best = null;
            foreach (string ac in Candidates(st, flown).Concat(st.Aircraft).Distinct())
            {
                var missing = new List<(string, double, double)>();
                foreach (string lesson in st.Lessons)
                {
                    double have = log.Best(ac, lesson);
                    if (have < st.PassScore) missing.Add((lesson, st.PassScore, have));
                }
                if (best == null || missing.Count < best.Count) best = missing;
            }
            if (best == null)
            {
                best = new List<(string, double, double)>();
                foreach (string lesson in st.Lessons) best.Add((lesson, st.PassScore, -1));
            }
            list.Add((st, best));
        }
        return list;
    }

    /// <summary>Can this stage be flown on this build at all? Not if every aeroplane it asks for is a placeholder.</summary>
    public static bool Flyable(Stage st)
    {
        if (st.Aircraft.Length == 0) return true;
        foreach (string ac in st.Aircraft) if (Hangar.IsPickable(ac)) return true;
        return false;
    }
}
