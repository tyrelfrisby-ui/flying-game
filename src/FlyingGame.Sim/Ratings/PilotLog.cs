namespace FlyingGame.Sim.Ratings;

/// <summary>
/// What a pilot has actually flown: the BEST score for each lesson, in each class it was flown in.
///
/// Per class, and that is the whole subtlety of the owner's brief — *"change power to asel or ases as
/// applicable if they are taking lessons in both"*. A landing flown in a Cub counts toward ASEL; the same
/// landing on floats counts toward ASES; neither counts toward the other. So the key is the pair, and
/// somebody who flies the whole syllabus twice in two different aeroplanes earns two badges, which is
/// exactly what happens to a real pilot who adds a seaplane rating.
///
/// Best score and not last: a rating once earned is not lost by flying a bad circuit afterwards.
/// </summary>
public sealed class PilotLog
{
    private readonly Dictionary<string, double> _best = new();

    private static string Key(RatingClass cls, string lessonId) => $"{cls}|{lessonId}";

    /// <summary>Record a lesson flown. Keeps the better of what was there and what just happened.</summary>
    public void Record(RatingClass cls, string lessonId, double score)
    {
        if (string.IsNullOrEmpty(lessonId)) return;
        string k = Key(cls, lessonId);
        if (!_best.TryGetValue(k, out double had) || score > had) _best[k] = score;
    }

    /// <summary>The best score ever flown for this lesson in this class, or −1 if never flown.</summary>
    public double Best(RatingClass cls, string lessonId)
        => _best.TryGetValue(Key(cls, lessonId), out double v) ? v : -1;

    /// <summary>Has anything at all been flown in this class? That is what makes somebody a student in it.</summary>
    public bool AnyIn(RatingClass cls)
    {
        string prefix = cls + "|";
        foreach (string k in _best.Keys) if (k.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

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

    public static PilotLog Deserialize(string? s)
    {
        var log = new PilotLog();
        if (string.IsNullOrEmpty(s)) return log;
        foreach (string part in s!.Split(';'))
        {
            int eq = part.LastIndexOf('=');
            if (eq <= 0) continue;
            if (double.TryParse(part.Substring(eq + 1), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v))
                log._best[part.Substring(0, eq)] = v;
        }
        return log;
    }
}
