namespace FlyingGame.Sim.Ratings;

/// <summary>
/// What a pilot has actually flown: the BEST score for each lesson, in each class it was flown in, and — from
/// the 2026-11-01 restructure on — in each aeroplane it was flown in.
///
/// Per class, and that is the whole subtlety of the owner's brief — *"change power to asel or ases as
/// applicable if they are taking lessons in both"*. A landing flown in a Cub counts toward ASEL; the same
/// landing on floats counts toward ASES; neither counts toward the other.
///
/// Per aeroplane, because the progression is now a path through particular aeroplanes (the 2-33, the J-3,
/// the Decathlon or the Great Lakes…), and a stage passed in one of them has to have been flown in it.
///
/// Best score and not last: a rating once earned is not lost by flying a bad circuit afterwards.
///
/// THE SAVED FORM is one PlayerPrefs string, "key=value;key=value", and it has four kinds of key:
///
///   Asel|Flare=81.00                     best score for a lesson in a class (every version)
///   @j3-cub-like|Asel|Flare=81.00        the same, in one aeroplane (2026-11-01 on)
///   !pilot=1                             a stage carried over from an older save by <see cref="SaveMigration"/>
///   #v=2                                 the format version; a log with no #v is from before 2026-11-01
///
/// An older build reading a newer log skips nothing it understood before: it only ever looked at the class
/// keys, and the new keys do not begin with a class name.
/// </summary>
public sealed class PilotLog
{
    /// <summary>The format this build writes. 1 (no #v key) is everything before 2026-11-01.</summary>
    public const int CurrentVersion = 2;

    private const string VersionKey = "#v";
    private readonly Dictionary<string, double> _best = new();

    public PilotLog() { _best[VersionKey] = CurrentVersion; }

    private static string Key(RatingClass cls, string lessonId) => $"{cls}|{lessonId}";
    private static string AircraftKey(string aircraftId, RatingClass cls, string lessonId) => $"@{aircraftId}|{cls}|{lessonId}";
    private static string GrantKey(string stageId) => "!" + stageId;

    /// <summary>The version the log was READ as. 1 means it came from an older build.</summary>
    public int Version => _best.TryGetValue(VersionKey, out double v) ? (int)v : 1;

    /// <summary>Record a lesson flown in a class, without saying in which aeroplane (older callers, tests).</summary>
    public void Record(RatingClass cls, string lessonId, double score)
    {
        if (string.IsNullOrEmpty(lessonId)) return;
        Keep(Key(cls, lessonId), score);
    }

    /// <summary>Record a lesson flown in a particular aeroplane. Counts toward its class as well.</summary>
    public void Record(string aircraftId, RatingClass cls, string lessonId, double score)
    {
        if (string.IsNullOrEmpty(lessonId)) return;
        Keep(Key(cls, lessonId), score);
        if (!string.IsNullOrEmpty(aircraftId)) Keep(AircraftKey(aircraftId, cls, lessonId), score);
    }

    private void Keep(string k, double score)
    {
        if (!_best.TryGetValue(k, out double had) || score > had) _best[k] = score;
    }

    /// <summary>The best score ever flown for this lesson in this class, or −1 if never flown.</summary>
    public double Best(RatingClass cls, string lessonId)
        => _best.TryGetValue(Key(cls, lessonId), out double v) ? v : -1;

    /// <summary>The best score ever flown for this lesson in this aeroplane, in any class, or −1.</summary>
    public double Best(string aircraftId, string lessonId)
    {
        double best = -1;
        foreach (RatingClass c in Certificate.BadgeOrder)
            if (_best.TryGetValue(AircraftKey(aircraftId, c, lessonId), out double v) && v > best) best = v;
        return best;
    }

    /// <summary>Every aeroplane anything has been recorded in, with the classes it was flown as.</summary>
    public Dictionary<string, HashSet<RatingClass>> AircraftFlown()
    {
        var map = new Dictionary<string, HashSet<RatingClass>>();
        foreach (string k in _best.Keys)
        {
            if (k.Length < 2 || k[0] != '@') continue;
            string[] parts = k.Substring(1).Split('|');
            if (parts.Length != 3 || !Enum.TryParse(parts[1], out RatingClass cls)) continue;
            if (!map.TryGetValue(parts[0], out var set)) map[parts[0]] = set = new HashSet<RatingClass>();
            set.Add(cls);
        }
        return map;
    }

    /// <summary>Has anything at all been flown in this class? That is what makes somebody a student in it.</summary>
    public bool AnyIn(RatingClass cls)
    {
        string prefix = cls + "|";
        foreach (string k in _best.Keys) if (k.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Has anything been flown at all, in anything?</summary>
    public bool AnyFlown()
    {
        foreach (RatingClass c in Certificate.BadgeOrder) if (AnyIn(c)) return true;
        return false;
    }

    /// <summary>Mark a stage as carried over from an older save. See <see cref="SaveMigration"/>.</summary>
    public void Grant(string stageId) { if (!string.IsNullOrEmpty(stageId)) _best[GrantKey(stageId)] = 1; }

    /// <summary>Was this stage carried over from an older save rather than flown on this build?</summary>
    public bool Granted(string stageId) => _best.ContainsKey(GrantKey(stageId));

    internal void MarkCurrent() => _best[VersionKey] = CurrentVersion;

    public IReadOnlyDictionary<string, double> Raw => _best;

    /// <summary>
    /// The log, as one short string for PlayerPrefs. Not JSON: Sim has no JSON library, and the shape here
    /// is flat key/value pairs, so a semicolon-separated list is both smaller and impossible to get wrong.
    /// A record that cannot be parsed loses the ratings, never the flight.
    /// </summary>
    public string Serialize()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in _best)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Read a saved log. A log written by a build from before 2026-11-01 (no #v key) is carried over to the
    /// current ladder on the way in — see <see cref="SaveMigration"/> — so every caller gets a current log.
    /// </summary>
    public static PilotLog Deserialize(string? s)
    {
        var log = new PilotLog();
        if (string.IsNullOrEmpty(s)) return log;
        log._best.Remove(VersionKey);
        foreach (string part in s!.Split(';'))
        {
            int eq = part.LastIndexOf('=');
            if (eq <= 0) continue;
            if (double.TryParse(part.Substring(eq + 1), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v))
                log._best[part.Substring(0, eq)] = v;
        }
        if (log.Version < CurrentVersion) SaveMigration.Migrate(log);
        return log;
    }
}
