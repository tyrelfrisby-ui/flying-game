using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Tuning;

/// <summary>Where a pilot's tuning is kept between launches. The Bridge implements it over PlayerPrefs; tests use a
/// dictionary. Same shape as ILandingStore and IPedalStore, for the same reason: Core must not know about Unity.</summary>
public interface ITuningStore
{
    string? Get(string key);
    void Set(string key, string value);
}

/// <summary>One aeroplane a pilot has tuned: the key its values are filed under, and what to call it on screen.</summary>
public struct TunedEntry
{
    public string Key;
    public string Label;
    public int Count;
}

/// <summary>
/// THE TUNING A PILOT HAS DIALLED IN, per aeroplane, across launches.
///
/// Four jobs, and the awkward ones are the last two.
///
/// 1. REMEMBER THE SHIPPED DEFAULT. A knob's default is not the C# field initialiser — it is whatever this type's
///    config file carries, which differs per type and is the thing "reset" has to go back to. So the first time a
///    config is seen, every registered knob is read off it and kept. That capture must happen BEFORE any override
///    is applied, which is why <see cref="Apply"/> does both in that order and nothing else may.
///
/// 2. APPLY WHAT HE SAVED. <see cref="Apply"/> is called on every freshly loaded config, in the same place
///    SessionSettings.ApplyFeel is called. That is not a nicety: FlightSimDriver.ResetFlight RE-READS the JSON from
///    StreamingAssets, so an edit made in place is thrown away by the next FLY unless it is re-applied here. A page
///    without this hook appears to work and silently forgets every value the moment he restarts the flight.
///
/// 3. KEEP CONFIGURATIONS APART. Ty, 2026-11-11: "each type and config gets a unique developer page". The Cub on
///    wheels, on bushwheels and on skis are three separate sets of numbers, so a tuning session on one cannot
///    contaminate another. That is <see cref="ConfigKeyOf"/>, and it is what everything here is filed under —
///    never the bare aircraft id.
///
/// 4. SAY WHAT IS NOT STOCK. A tunable page makes every flight report suspect: "the Pitts will not recover from a
///    flat spin" means nothing if nobody can tell whether the aeroplane was the shipped one.
///    <see cref="ModifiedCount"/> drives an always-visible indicator and <see cref="DeltaReport"/> dumps the lot as
///    plain text a tester can paste into a report — and the same function writes the header of a flight-test
///    recording. One function, two callers: a recording whose knob state is unknown is worth nothing.
/// </summary>
public static class TuningSession
{
    private const string IndexKey = "tune.index";

    /// <summary>
    /// WHICH AEROPLANE THIS TUNING BELONGS TO: the type AND the configuration it is flying in, because they handle
    /// differently enough that one set of numbers cannot serve both. Floats are already their own type id; the
    /// undercarriage options are not, so they are spelled out here.
    /// </summary>
    public static string ConfigKeyOf(AircraftConfig? config)
    {
        if (config == null) return "";
        string id = config.Id ?? "";
        if (config.Skis) return id + "+skis";
        if (config.Bushwheels) return id + "+bushwheels";
        return id;
    }

    /// <summary>What to call that aeroplane on screen, configuration included.</summary>
    public static string LabelOf(AircraftConfig? config)
    {
        if (config == null) return "";
        string name = string.IsNullOrEmpty(config.DisplayName) ? (config.Id ?? "") : config.DisplayName;
        if (config.Skis) return name + " on skis";
        if (config.Bushwheels) return name + " on bushwheels";
        return name;
    }

    public static string KeyFor(string configKey, string tunableId) => "tune." + configKey + "." + tunableId;
    private static string DefaultKeyFor(string configKey, string tunableId) => "tune." + configKey + "." + tunableId + ".d";
    private static string NameKeyFor(string configKey) => "tune." + configKey + ".name";

    private static ITuningStore? _store;
    private static readonly Dictionary<string, Dictionary<string, double>> _defaults = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<string, double>> _overrides = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _loaded = new(StringComparer.Ordinal);

    /// <summary>Called once at start-up by the Bridge. Without a store the page still works for the session; it
    /// simply forgets at the next launch, which is better than refusing to run.</summary>
    public static void UseStore(ITuningStore store) => _store = store;

    /// <summary>
    /// CAPTURE THIS AEROPLANE'S DEFAULTS, THEN PUT THE PILOT'S VALUES BACK. Call on every freshly loaded config,
    /// before the Aircraft is built from it — some knobs (the g limits, the tyre) are read into the airframe at
    /// construction, and applying after that would leave them a spawn behind.
    /// </summary>
    public static void Apply(AircraftConfig? config)
    {
        if (config == null) return;
        string key = ConfigKeyOf(config);
        CaptureDefaults(config, key);
        // A SHIPPED BUILD FLIES STOCK. Once the Developer route is closed there is no page to see a knob on, no way
        // to reset one and no modified indicator — so a value saved during the beta would otherwise follow a pilot
        // into the public release and fly an aeroplane nobody could tell was not the shipped one. The defaults are
        // still captured above, which costs nothing and leaves the page honest if the route is ever reopened.
        if (!DeveloperAccess.Open) return;
        Dictionary<string, double> saved = OverridesFor(key);
        if (saved.Count == 0) return;
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!saved.TryGetValue(t.Id, out double v) || !Applies(t, config)) continue;
            TrySet(t, config, t.Clamp(v));
        }
    }

    private static void CaptureDefaults(AircraftConfig config, string key)
    {
        _labels[key] = LabelOf(config);
        if (_defaults.ContainsKey(key)) return;
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!Applies(t, config)) continue;
            try { map[t.Id] = t.Get(config); }
            catch (Exception) { /* a block this type has not got: not a default, not a row */ }
        }
        _defaults[key] = map;
    }

    private static bool Applies(Tunable t, AircraftConfig config)
    {
        try { return t.Applies(config); }
        catch (Exception) { return false; }
    }

    private static void TrySet(Tunable t, AircraftConfig config, double v)
    {
        try { t.Set(config, v); t.AfterSet?.Invoke(config); }
        catch (Exception) { /* never let one bad knob take the flight down */ }
    }

    /// <summary>The value this aeroplane's config file shipped with, or NaN if it has never been seen. Falls back to
    /// the value saved beside the override, so a COPY FROM can work out a delta for an aeroplane that has not been
    /// loaded this session.</summary>
    public static double DefaultOf(string configKey, string tunableId)
    {
        string k = configKey ?? "";
        if (_defaults.TryGetValue(k, out Dictionary<string, double>? m) && m.TryGetValue(tunableId, out double v)) return v;
        string? raw = _store?.Get(DefaultKeyFor(k, tunableId));
        return !string.IsNullOrEmpty(raw)
               && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;
    }

    /// <summary>Set a knob on the aeroplane being flown, and remember it. Live knobs are felt on the next physics
    /// step; the rest take effect at the next FLY, because the airframe was built from them.</summary>
    public static void Set(AircraftConfig config, Tunable t, double value)
    {
        if (config == null || t == null) return;
        double v = t.Clamp(value);
        TrySet(t, config, v);
        string key = ConfigKeyOf(config);
        OverridesFor(key)[t.Id] = v;
        if (_store != null)
        {
            _store.Set(KeyFor(key, t.Id), v.ToString("R", CultureInfo.InvariantCulture));
            double d = DefaultOf(key, t.Id);
            if (!double.IsNaN(d)) _store.Set(DefaultKeyFor(key, t.Id), d.ToString("R", CultureInfo.InvariantCulture));
            _store.Set(NameKeyFor(key), LabelOf(config));
            AddToIndex(key);
        }
    }

    /// <summary>Put one knob back to what this aeroplane shipped with, and forget the override.</summary>
    public static void Reset(AircraftConfig config, Tunable t)
    {
        if (config == null || t == null) return;
        string key = ConfigKeyOf(config);
        double d = DefaultOf(key, t.Id);
        if (!double.IsNaN(d)) TrySet(t, config, d);
        OverridesFor(key).Remove(t.Id);
        _store?.Set(KeyFor(key, t.Id), "");      // "" is the erased marker: a store need not support deletion
    }

    /// <summary>Put every knob on this aeroplane back to what it shipped with.</summary>
    public static void ResetAll(AircraftConfig config)
    {
        if (config == null) return;
        foreach (Tunable t in TunableRegistry.All) if (Applies(t, config)) Reset(config, t);
    }

    /// <summary>Is this knob off the value its aeroplane shipped with? The comparison is on the DEFAULT, not on
    /// whether an override happens to be stored, so a knob nudged back to its default by hand reads as stock.</summary>
    public static bool IsModified(AircraftConfig config, Tunable t)
    {
        if (config == null || t == null) return false;
        double d = DefaultOf(ConfigKeyOf(config), t.Id);
        if (double.IsNaN(d)) return false;
        double now;
        try { now = t.Get(config); }
        catch (Exception) { return false; }
        // A tenth of the fine step: below that the pilot cannot have meant it, and a float round-trip through the
        // store must not make a stock aeroplane report as modified.
        return Math.Abs(now - d) > Math.Max(Math.Abs(t.FineStep) * 0.1, 1e-9);
    }

    /// <summary>How many knobs on this aeroplane are off their shipped value. 0 means the stock aeroplane, and that
    /// is what the indicator says.</summary>
    public static int ModifiedCount(AircraftConfig? config)
    {
        if (config == null) return 0;
        int n = 0;
        foreach (Tunable t in TunableRegistry.All) if (Applies(t, config) && IsModified(config, t)) n++;
        return n;
    }

    /// <summary>
    /// EVERY NON-DEFAULT KNOB AS PLAIN TEXT — what a beta tester pastes into a report, and what goes in the header
    /// of a flight-test recording. It names the aeroplane and its configuration and, for each knob, the shipped
    /// value and the current one, so a reader who was not there can tell exactly what was flown.
    /// </summary>
    public static string DeltaReport(AircraftConfig? config)
    {
        if (config == null) return "no aeroplane";
        string key = ConfigKeyOf(config);
        var sb = new StringBuilder();
        sb.Append(LabelOf(config)).Append(" (").Append(key).Append(')');
        int n = 0;
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!Applies(t, config) || !IsModified(config, t)) continue;
            double d = DefaultOf(key, t.Id);
            double now;
            try { now = t.Get(config); } catch (Exception) { continue; }
            sb.Append('\n').Append(t.Id).Append(": ")
              .Append(now.ToString("F" + t.Decimals, CultureInfo.InvariantCulture)).Append(t.Units)
              .Append("  (shipped ").Append(d.ToString("F" + t.Decimals, CultureInfo.InvariantCulture)).Append(t.Units).Append(')');
            if (t.Derived) sb.Append("  [DERIVED — ").Append(t.Rule).Append(']');
            n++;
        }
        // THE GLOBAL KNOBS GO IN THE SAME REPORT. A flight flown on a moved flutter law is not a flight on the
        // shipped model, and the reader of a bug report cannot be expected to ask; nor can the header of a
        // flight-test recording, which is the other caller of this function.
        string globals = GlobalTuningSession.DeltaReport();
        if (n == 0 && globals.Length == 0) sb.Append("\nstock — every knob at its shipped value");
        else if (n == 0) sb.Append("\nthis aeroplane is stock; the GLOBAL knobs below are not");
        sb.Append(globals);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ COPY FROM

    /// <summary>
    /// THE AEROPLANES WORTH COPYING FROM: the ones that actually carry tuning. Ty, 2026-11-11: "More like copy x
    /// airplane" — he picks the source by name, so the list has to be real tuning sessions rather than the whole
    /// fleet. The aeroplane he is on is left out; copying onto yourself is not a thing he can have meant.
    /// </summary>
    public static List<TunedEntry> TunedAircraft(AircraftConfig? exclude)
    {
        var list = new List<TunedEntry>();
        string? skip = exclude == null ? null : ConfigKeyOf(exclude);
        foreach (string key in IndexKeys())
        {
            if (skip != null && string.Equals(key, skip, StringComparison.Ordinal)) continue;
            int n = OverridesFor(key).Count;
            if (n == 0) continue;
            string label = _labels.TryGetValue(key, out string? l) ? l : (_store?.Get(NameKeyFor(key)) ?? key);
            if (string.IsNullOrEmpty(label)) label = key;
            _labels[key] = label;
            list.Add(new TunedEntry { Key = key, Label = label, Count = n });
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        return list;
    }

    /// <summary>
    /// COPY ANOTHER AEROPLANE'S TUNING ONTO THIS ONE, AS DELTAS FROM DEFAULT.
    ///
    /// The delta is the point, and it is why this is not a straight value copy. Ty's example is the J-3 on wheels
    /// and the J-3 on floats, and what carries between them is the JUDGEMENT — "two degrees more toe-out suits it" —
    /// not the absolute number, which means something different measured against a different starting point. So each
    /// knob moves the target the same DISTANCE from its own shipped value that the source sits from its own. Where
    /// the two shipped the same number the two readings are identical anyway, so nothing is lost by it.
    ///
    /// A knob the target has not got is skipped, and <paramref name="skipped"/> counts them so the page can say so
    /// rather than quietly dropping a float knob onto a wheel aeroplane. Values are clamped to the target's range.
    ///
    /// Returns how many knobs it moved. <see cref="Snapshot"/> first if you want it undoable — the page does.
    /// </summary>
    public static int CopyFrom(AircraftConfig target, string sourceKey, out int skipped)
    {
        skipped = 0;
        if (target == null || string.IsNullOrEmpty(sourceKey)) return 0;
        string dstKey = ConfigKeyOf(target);
        Dictionary<string, double> src = OverridesFor(sourceKey);
        int moved = 0;
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!src.TryGetValue(t.Id, out double srcValue)) continue;
            if (!Applies(t, target)) { skipped++; continue; }
            double srcDefault = DefaultOf(sourceKey, t.Id);
            double dstDefault = DefaultOf(dstKey, t.Id);
            // No default on one side or the other means no delta can be formed; carry the value itself, which is the
            // only honest thing left, rather than inventing a distance from a number we do not have.
            double want = double.IsNaN(srcDefault) || double.IsNaN(dstDefault)
                        ? srcValue
                        : dstDefault + (srcValue - srcDefault);
            Set(target, t, t.Clamp(want));
            moved++;
        }
        return moved;
    }

    /// <summary>Every knob on this aeroplane as it stands, for undo. Taken before a COPY FROM, because he will copy
    /// onto a tuned aeroplane by accident and the tuning he lost was an evening's flying.</summary>
    public static Dictionary<string, double> Snapshot(AircraftConfig? config)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        if (config == null) return map;
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!Applies(t, config)) continue;
            try { map[t.Id] = t.Get(config); } catch (Exception) { }
        }
        return map;
    }

    /// <summary>Put a <see cref="Snapshot"/> back. Anything that is now at its default is forgotten rather than
    /// stored, so an undo of a copy onto a stock aeroplane leaves it stock rather than pinned.</summary>
    public static void Restore(AircraftConfig? config, Dictionary<string, double>? snapshot)
    {
        if (config == null || snapshot == null) return;
        string key = ConfigKeyOf(config);
        foreach (Tunable t in TunableRegistry.All)
        {
            if (!snapshot.TryGetValue(t.Id, out double v) || !Applies(t, config)) continue;
            double d = DefaultOf(key, t.Id);
            if (!double.IsNaN(d) && Math.Abs(v - d) <= Math.Max(Math.Abs(t.FineStep) * 0.1, 1e-9)) Reset(config, t);
            else Set(config, t, v);
        }
    }

    // ------------------------------------------------------------------ store plumbing

    private static Dictionary<string, double> OverridesFor(string configKey)
    {
        string key = configKey ?? "";
        if (!_overrides.TryGetValue(key, out Dictionary<string, double>? map))
            _overrides[key] = map = new Dictionary<string, double>(StringComparer.Ordinal);
        if (_loaded.Add(key) && _store != null)
        {
            foreach (Tunable t in TunableRegistry.All)
            {
                string? raw = _store.Get(KeyFor(key, t.Id));
                if (string.IsNullOrEmpty(raw)) continue;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    && !double.IsNaN(v) && !double.IsInfinity(v)) map[t.Id] = v;
            }
        }
        return map;
    }

    /// <summary>The keys that have ever been tuned. A key/value store cannot be enumerated, so the list is kept as
    /// one newline-separated value beside the rest.</summary>
    private static List<string> IndexKeys()
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string k in _overrides.Keys) if (_overrides[k].Count > 0 && seen.Add(k)) keys.Add(k);
        string? raw = _store?.Get(IndexKey);
        if (!string.IsNullOrEmpty(raw))
            foreach (string k in raw!.Split('\n'))
                if (!string.IsNullOrEmpty(k) && seen.Add(k)) keys.Add(k);
        return keys;
    }

    private static void AddToIndex(string key)
    {
        if (_store == null) return;
        string raw = _store.Get(IndexKey) ?? "";
        foreach (string k in raw.Split('\n')) if (string.Equals(k, key, StringComparison.Ordinal)) return;
        _store.Set(IndexKey, raw.Length == 0 ? key : raw + "\n" + key);
    }

    /// <summary>Tests only: forget every captured default, saved override and label.</summary>
    public static void ResetForTest()
    {
        _store = null;
        _defaults.Clear();
        _overrides.Clear();
        _labels.Clear();
        _loaded.Clear();
    }
}

/// <summary>The one cache the tuning page has to invalidate. See AeroModel.ForgetFuselageMunk for why it is one.</summary>
internal static class TuningCaches
{
    internal static void Invalidate(AircraftConfig config) => AeroModel.ForgetFuselageMunk(config);
}
