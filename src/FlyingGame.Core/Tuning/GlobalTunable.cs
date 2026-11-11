using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlyingGame.Core.Tuning;

/// <summary>
/// A TUNING NUMBER THAT IS NOT AN AEROPLANE'S. The flutter law's exponent, the speeds it arms and fails at: one
/// value for the whole fleet, because it is a statement about how the physics behaves rather than about how a
/// particular airframe is built.
///
/// IT IS A SEPARATE TYPE FROM <see cref="Tunable"/> ON PURPOSE, and the reason is the one thing a reader must not
/// get wrong here: a per-aircraft knob is a field on an <see cref="FlyingGame.Core.DataContracts.AircraftConfig"/>
/// that is RE-READ FROM StreamingAssets ON EVERY SPAWN, so its value has to be written back onto each freshly
/// loaded config or it is silently lost. A global knob is a static field. Nothing reloads it, so it must be applied
/// exactly ONCE — and if it were put on the per-aircraft path instead, the defaults capture that runs at the same
/// moment would read the pilot's own modified value as the "shipped" one, and RESET would then put it back to
/// wherever he happened to have left it. Giving the two the same shape would have made that bug available; giving
/// them different shapes makes it unavailable.
///
/// Everything else matches <see cref="Tunable"/> exactly — display units, slider range, coarse and fine steps,
/// decimals, a description the page shows when the title is tapped — so the page can draw them with the same row.
/// </summary>
public sealed class GlobalTunable
{
    /// <summary>Stable identity, and the key the override is saved under: the static it moves, e.g.
    /// "flutter.rateExponent". It must never change once a pilot has saved a value against it.</summary>
    public string Id = "";

    /// <summary>Which global section it appears under. Shown as a heading, apart from the per-aircraft groups.</summary>
    public string Group = "";

    /// <summary>The row's title, in a pilot's words. Tapping it shows <see cref="Description"/>.</summary>
    public string Label = "";

    /// <summary>What moving it does, and which way. The page wraps it.</summary>
    public string Description = "";

    public string Units = "";
    public double Min, Max;
    public double Step = 0.1, FineStep = 0.01;
    public int Decimals = 2;

    /// <summary>Read the value, in display units.</summary>
    public Func<double> Get = () => 0.0;

    /// <summary>Write the value, in display units.</summary>
    public Action<double> Set = _ => { };

    /// <summary>What the game shipped with — captured at registration, from the C# field's own initialiser, before
    /// any saved value can have been applied. NaN only if registration itself threw.</summary>
    public double Shipped { get; internal set; } = double.NaN;

    public double Clamp(double v) => v < Min ? Min : v > Max ? Max : v;
}

/// <summary>
/// EVERY GLOBAL KNOB, in one place, so the Developer page's global section is a renderer like the rest of it.
///
/// The next global constant is ONE <see cref="Register"/> call beside the field it moves. Nothing in the UI, the
/// persistence or the report needs touching.
/// </summary>
public static class GlobalTunableRegistry
{
    private static readonly List<GlobalTunable> _all = new();
    private static readonly Dictionary<string, GlobalTunable> _byId = new(StringComparer.Ordinal);

    /// <summary>The order the global groups are drawn in; a group not named here is drawn after these.</summary>
    public static readonly IReadOnlyList<string> GroupOrder = new[] { FlutterGroup };

    public const string FlutterGroup = "Flutter";

    /// <summary>
    /// Declare a global tunable. THE SHIPPED VALUE IS CAPTURED HERE, which is the whole of the defaults problem for
    /// a static: the field's initialiser has run and nothing has had a chance to overwrite it, so this is the only
    /// moment at which "what the game shipped with" is knowable without keeping a second copy of the number.
    /// Registering the same Id twice replaces the earlier one and keeps the first capture.
    /// </summary>
    public static void Register(GlobalTunable t)
    {
        if (t == null) throw new ArgumentNullException(nameof(t));
        if (string.IsNullOrEmpty(t.Id)) throw new ArgumentException("A global tunable needs an Id: it is the key its override is saved under.", nameof(t));
        if (string.IsNullOrEmpty(t.Description)) throw new ArgumentException($"Global tunable '{t.Id}' needs a Description.", nameof(t));
        if (!(t.Max > t.Min)) throw new ArgumentException($"Global tunable '{t.Id}' needs Max > Min.", nameof(t));
        if (_byId.TryGetValue(t.Id, out GlobalTunable? old)) { t.Shipped = old.Shipped; _all.Remove(old); }
        if (double.IsNaN(t.Shipped)) { try { t.Shipped = t.Get(); } catch (Exception) { t.Shipped = double.NaN; } }
        _byId[t.Id] = t;
        _all.Add(t);
        GlobalTuningSession.ApplySavedTo(t);   // a store attached before this knob existed still reaches it
    }

    public static IReadOnlyList<GlobalTunable> All { get { EnsureCatalog(); return _all; } }

    public static GlobalTunable? ById(string id)
    {
        EnsureCatalog();
        return _byId.TryGetValue(id, out GlobalTunable? t) ? t : null;
    }

    /// <summary>The globals, grouped and in <see cref="GroupOrder"/>.</summary>
    public static List<KeyValuePair<string, List<GlobalTunable>>> Groups()
    {
        EnsureCatalog();
        var byGroup = new Dictionary<string, List<GlobalTunable>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (GlobalTunable t in _all)
        {
            if (!byGroup.TryGetValue(t.Group, out List<GlobalTunable>? list)) { byGroup[t.Group] = list = new List<GlobalTunable>(); order.Add(t.Group); }
            list.Add(t);
        }
        order.Sort((a, b) =>
        {
            int ia = IndexOfGroup(a), ib = IndexOfGroup(b);
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a, b);
        });
        var result = new List<KeyValuePair<string, List<GlobalTunable>>>(order.Count);
        foreach (string g in order) result.Add(new KeyValuePair<string, List<GlobalTunable>>(g, byGroup[g]));
        return result;
    }

    private static int IndexOfGroup(string g)
    {
        for (int i = 0; i < GroupOrder.Count; i++) if (string.Equals(GroupOrder[i], g, StringComparison.Ordinal)) return i;
        return GroupOrder.Count;
    }

    private static bool _catalogLoaded;
    private static void EnsureCatalog()
    {
        if (_catalogLoaded) return;
        _catalogLoaded = true;             // first: Load calls Register, which comes back through here
        GlobalTunableCatalog.Load();
    }

    /// <summary>Tests only: forget everything, including the catalog.</summary>
    public static void ResetForTest()
    {
        _all.Clear();
        _byId.Clear();
        _catalogLoaded = false;
    }
}

/// <summary>
/// WHERE A GLOBAL KNOB'S VALUE IS KEPT BETWEEN LAUNCHES, and the deliberate difference from
/// <see cref="TuningSession"/>.
///
/// The per-aircraft session has to re-apply on every spawn because the config object is thrown away and re-read
/// from StreamingAssets each time. A global is a static field: it survives the spawn, so re-applying would be
/// pointless at best. At worst it is the bug — see <see cref="GlobalTunable"/> — so the saved values are applied
/// ONCE, from <see cref="UseStore"/>, and never again. <see cref="UseStore"/> is idempotent so that it can sit on
/// the same start-up path as the per-aircraft one without that mattering.
///
/// A SHIPPED BUILD FLIES STOCK, exactly as the per-aircraft session does: with the Developer route closed there is
/// no page to see a knob on, so a value saved during the beta must not follow a pilot into the public release.
/// </summary>
public static class GlobalTuningSession
{
    private const string Prefix = "tuneg.";
    private static ITuningStore? _store;
    private static readonly Dictionary<string, double> _overrides = new(StringComparer.Ordinal);

    public static string KeyFor(string id) => Prefix + id;

    /// <summary>Attach the store and put the pilot's saved globals back. Idempotent: the second call does nothing,
    /// because applying a global twice is at best a waste and the page is the only thing that should move one after
    /// start-up.</summary>
    public static void UseStore(ITuningStore store)
    {
        if (store == null || ReferenceEquals(_store, store)) return;
        _store = store;
        if (!DeveloperAccess.Open) return;
        foreach (GlobalTunable t in GlobalTunableRegistry.All) ApplySavedTo(t);
    }

    /// <summary>Put this one knob's saved value back, if there is one. Called from the registry so that a knob
    /// registered after the store was attached is not left stock while its neighbours are not.</summary>
    internal static void ApplySavedTo(GlobalTunable t)
    {
        if (_store == null || t == null || !DeveloperAccess.Open) return;
        string? raw = _store.Get(KeyFor(t.Id));
        if (string.IsNullOrEmpty(raw)) return;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
        if (double.IsNaN(v) || double.IsInfinity(v)) return;
        double c = t.Clamp(v);
        _overrides[t.Id] = c;
        try { t.Set(c); } catch (Exception) { /* never let one bad knob take the flight down */ }
    }

    /// <summary>Set a global knob and remember it.</summary>
    public static void Set(GlobalTunable t, double value)
    {
        if (t == null) return;
        double v = t.Clamp(value);
        try { t.Set(v); } catch (Exception) { return; }
        _overrides[t.Id] = v;
        _store?.Set(KeyFor(t.Id), v.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>Put one knob back to what the game shipped with and forget the override.</summary>
    public static void Reset(GlobalTunable t)
    {
        if (t == null || double.IsNaN(t.Shipped)) return;
        try { t.Set(t.Shipped); } catch (Exception) { }
        _overrides.Remove(t.Id);
        _store?.Set(KeyFor(t.Id), "");      // "" is the erased marker: a store need not support deletion
    }

    public static void ResetAll() { foreach (GlobalTunable t in GlobalTunableRegistry.All) Reset(t); }

    /// <summary>Is this knob off what the game shipped with? Measured against the VALUE, not against whether an
    /// override happens to be stored, so a knob nudged back by hand reads as stock.</summary>
    public static bool IsModified(GlobalTunable t)
    {
        if (t == null || double.IsNaN(t.Shipped)) return false;
        double now;
        try { now = t.Get(); } catch (Exception) { return false; }
        return Math.Abs(now - t.Shipped) > Math.Max(Math.Abs(t.FineStep) * 0.1, 1e-9);
    }

    public static int ModifiedCount()
    {
        int n = 0;
        foreach (GlobalTunable t in GlobalTunableRegistry.All) if (IsModified(t)) n++;
        return n;
    }

    /// <summary>Every non-default global as plain text — it goes in the SAME report and the same recording header
    /// as the per-aircraft deltas, because a flight flown on a moved flutter law is not a flight on the shipped
    /// model and the reader cannot be expected to ask.</summary>
    public static string DeltaReport()
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (GlobalTunable t in GlobalTunableRegistry.All)
        {
            if (!IsModified(t)) continue;
            double now;
            try { now = t.Get(); } catch (Exception) { continue; }
            sb.Append('\n').Append("GLOBAL ").Append(t.Id).Append(": ")
              .Append(now.ToString("F" + t.Decimals, CultureInfo.InvariantCulture)).Append(t.Units)
              .Append("  (shipped ").Append(t.Shipped.ToString("F" + t.Decimals, CultureInfo.InvariantCulture)).Append(t.Units).Append(')');
            n++;
        }
        return n == 0 ? "" : sb.ToString();
    }

    /// <summary>Tests only: forget the store and every override. Does NOT put the statics back — a test that moved
    /// one calls <see cref="ResetAll"/> first, which is the honest order.</summary>
    public static void ResetForTest()
    {
        _store = null;
        _overrides.Clear();
    }
}
