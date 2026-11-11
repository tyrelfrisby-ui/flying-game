namespace FlyingGame.Core.Landing;

/// <summary>
/// The thumbnail pipeline's naming (Ty 2026-11-05: "locations with icons showing them with that actual airplane ie a
/// screenshot of what they'll first see"). A key is a relative path without extension; the same key names the bench
/// capture shipped in Resources (Editor/LandingThumbnailCapture) and the device's own capture of its first frame
/// after LAUNCH (Bridge/Landing/LandingThumbnails, under persistentDataPath). Lookup tries the most specific key first
/// and falls back one step at a time, so a card always shows the closest picture there is:
///
///   location: loc/&lt;aircraft&gt;/&lt;gear&gt;/&lt;airport&gt;/&lt;viewpoint&gt;  →  loc/&lt;aircraft&gt;/standard/&lt;airport&gt;/&lt;viewpoint&gt;
///             →  loc/any/&lt;airport&gt;/&lt;viewpoint&gt;  →  loc/any/&lt;airport&gt;/InTheAir
///   aircraft: ac/&lt;aircraft&gt;/&lt;gear&gt;  →  ac/&lt;aircraft&gt;/standard
///   viewpoint, time, weather, reliability, seat: icon/&lt;group&gt;/&lt;id&gt; (drawn icons, the visual lane's)
///
/// Only [a-z0-9-] reach a file name.
/// </summary>
public static class ThumbnailKeys
{
    public const string Any = "any";

    public static string Aircraft(string aircraftId, string gearId) => $"ac/{Safe(aircraftId)}/{Safe(gearId)}";

    public static string Location(string aircraftId, string gearId, string airportId, Viewpoint v) =>
        $"loc/{Safe(aircraftId)}/{Safe(gearId)}/{Safe(airportId)}/{v}";

    public static string Icon(string group, string id) => $"icon/{Safe(group)}/{Safe(id)}";

    /// <summary>The keys to try for a location card, most specific first.</summary>
    public static IEnumerable<string> LocationFallbacks(string aircraftId, string gearId, string airportId, Viewpoint v)
    {
        yield return Location(aircraftId, gearId, airportId, v);
        if (gearId != "standard") yield return Location(aircraftId, "standard", airportId, v);
        yield return $"loc/{Any}/{Safe(airportId)}/{v}";
        if (v != Viewpoint.InTheAir) yield return $"loc/{Any}/{Safe(airportId)}/{Viewpoint.InTheAir}";
    }

    public static IEnumerable<string> AircraftFallbacks(string aircraftId, string gearId)
    {
        yield return Aircraft(aircraftId, gearId);
        if (gearId != "standard") yield return Aircraft(aircraftId, "standard");
    }

    /// <summary>Resolve against whatever exists (bundled or captured). Null when nothing does: the card draws its icon.</summary>
    public static string? Resolve(IEnumerable<string> fallbacks, Func<string, bool> exists)
    {
        foreach (string k in fallbacks) if (exists(k)) return k;
        return null;
    }

    /// <summary>A file-name-safe form: lower case, [a-z0-9-] only, anything else becomes '-'. "InTheAir" etc. are
    /// written by <see cref="Location"/> AFTER this, so viewpoint names keep their case.</summary>
    public static string Safe(string s)
    {
        if (string.IsNullOrEmpty(s)) return "none";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s.ToLowerInvariant()) sb.Append((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' ? ch : '-');
        return sb.ToString();
    }
}
