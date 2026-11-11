using System;
using System.Collections.Generic;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Tuning;

/// <summary>
/// ONE TUNING NUMBER A PILOT CAN TURN, declared where the number lives rather than drawn by hand in the UI.
///
/// Ty, 2026-11-11: "Create a test mode page in options with all of the k factors so I can tune them and dial in each
/// airplane including things like toe in/out idle rpm adjustments vertical tail offsets etc". The point of the page is
/// the round trip it removes: a Unity iOS build is about a gigabyte and reaches his phone over TestFlight or the OTA
/// server, so a number that can only be changed by rebuilding costs hours to try. He and his beta test pilots turn
/// these in the aeroplane instead.
///
/// The shape is a REGISTRY, not a list in the page, so that the next knob is one <see cref="TunableRegistry.Register"/>
/// call beside the field it moves and no UI edit at all. That is also why it carries its own
/// <see cref="Description"/>: the page shows it when the title is tapped, and a description written next to the physics
/// is the one that stays true when the physics changes.
///
/// EVERYTHING HERE IS IN DISPLAY UNITS — degrees, rpm, psi, whatever a pilot would say — and <see cref="Get"/> and
/// <see cref="Set"/> do the conversion to and from whatever the config stores. So the slider's range, the number on
/// the screen, the saved override and the sentence in the description are all the same number, and nobody has to
/// remember that the fin offset is kept in radians.
/// </summary>
public sealed class Tunable
{
    /// <summary>Stable identity, and the key an override is saved under: the config path it moves, e.g.
    /// "spinAero.controlSpoilerK". It must never change once a pilot has saved a value against it.</summary>
    public string Id = "";

    /// <summary>Which section of the page it appears under (see <see cref="TunableRegistry.GroupOrder"/>).</summary>
    public string Group = "";

    /// <summary>The row's title, in a pilot's words. Tapping it shows <see cref="Description"/>.</summary>
    public string Label = "";

    /// <summary>What moving it does to the aeroplane, and which way — not the field name restated. One or two
    /// sentences; the page wraps them.</summary>
    public string Description = "";

    /// <summary>Unit suffix shown after the value ("°", " rpm", " psi"); empty for a bare coefficient.</summary>
    public string Units = "";

    /// <summary>Slider range, in display units. The ends are deliberately wider than any shipped value: this is a
    /// page for finding out what a number wants to be.</summary>
    public double Min, Max;

    /// <summary>What the coarse and fine step buttons move, in display units. Fine is what he lands on a value with;
    /// the slider alone cannot be read to a hundredth on a phone in sunlight.</summary>
    public double Step = 0.1, FineStep = 0.01;

    /// <summary>Digits after the point in the readout. Enough to tune with: a knob whose fine step is 0.01 and whose
    /// readout shows one decimal is a knob you cannot see yourself moving.</summary>
    public int Decimals = 2;

    /// <summary>
    /// TRUE if changing it is felt on the very next physics step, which is nearly all of them: the hot paths read
    /// their numbers straight off the live <see cref="AircraftConfig"/> every step, and the page mutates that object.
    /// FALSE if the value is baked into something built once per aircraft, in which case it takes effect the next
    /// time the aeroplane is spawned (FLY again) and <see cref="LiveNote"/> says why.
    /// </summary>
    public bool Live = true;

    /// <summary>For a knob that is not <see cref="Live"/>: why, in one line, shown on the row.</summary>
    public string LiveNote = "";

    /// <summary>
    /// TRUE for a number that is neither a free gain nor a measured fact, but the CACHED OUTPUT OF A DERIVATION —
    /// a prop inertia from the owner's blade rule, a pitch stop from the 26 inHg run-up, a fin offset the rig solver
    /// found, a limit load factor off the TCDS. Moving one of these does not tune the model; it FALSIFIES the rule
    /// the number came from, and a value that is settled on means re-running that derivation rather than editing the
    /// number in place. The page draws them apart from the K factors and says so on every row.
    ///
    /// They are on the page anyway because sweeping one in flight answers a question nothing else can: whether the
    /// Pitts' inverted-spin hold really needs its 1.9 degrees of fin, what the prop's inertia is worth in the pitch
    /// budget, where a damage threshold actually bites.
    /// </summary>
    public bool Derived;

    /// <summary>For a <see cref="Derived"/> knob: the rule it came from, in one line, shown on the row.</summary>
    public string Rule = "";

    /// <summary>Read the value, in display units.</summary>
    public Func<AircraftConfig, double> Get = _ => 0.0;

    /// <summary>Write the value, in display units.</summary>
    public Action<AircraftConfig, double> Set = (_, _) => { };

    /// <summary>Does this aeroplane have the thing at all? A constant-speed knob on a fixed-pitch type, or a tyre
    /// knob on a glider with a skid, is a row that can only mislead. Default: every type has it.</summary>
    public Func<AircraftConfig, bool> Applies = _ => true;

    /// <summary>Called after <see cref="Set"/>, for a value that something has cached off the config and must be
    /// told about. Null for the overwhelming majority, which are simply read again next step.</summary>
    public Action<AircraftConfig>? AfterSet;

    public double Clamp(double v) => v < Min ? Min : v > Max ? Max : v;
}

/// <summary>
/// EVERY TUNABLE IN THE GAME, in one place, so the Developer page is a renderer and not a list.
///
/// A new knob is one <see cref="Register"/> call — see <see cref="TunableCatalog"/>, which declares the ones that
/// exist today. Nothing in the UI needs touching to add one; it appears in its group, with its description, its
/// range and its reset, the moment it is registered.
/// </summary>
public static class TunableRegistry
{
    private static readonly List<Tunable> _all = new();
    private static readonly Dictionary<string, Tunable> _byId = new(StringComparer.Ordinal);
    private static bool _catalogLoaded;

    /// <summary>The order the groups are drawn in. A group not named here is drawn after these, alphabetically, so
    /// registering into a brand-new group still works without editing this.</summary>
    public static readonly IReadOnlyList<string> GroupOrder = new[]
    {
        "Rigging",
        "Spin and high alpha",
        "Engine and propeller",
        "Fuselage",
        "Control throws",
        "Ground and tyres",
        "Stall dynamics",
        DerivedGroup,
    };

    /// <summary>The group every <see cref="Tunable.Derived"/> knob goes in, drawn last and drawn differently.</summary>
    public const string DerivedGroup = "Derived — moving this falsifies its rule";

    /// <summary>Declare a tunable. Registering the same <see cref="Tunable.Id"/> twice REPLACES the earlier one,
    /// which is what makes the catalog safe to load more than once.</summary>
    public static void Register(Tunable t)
    {
        if (t == null) throw new ArgumentNullException(nameof(t));
        if (string.IsNullOrEmpty(t.Id)) throw new ArgumentException("A tunable needs an Id: it is the key its override is saved under.", nameof(t));
        if (string.IsNullOrEmpty(t.Description)) throw new ArgumentException($"Tunable '{t.Id}' needs a Description: the page shows it when the title is tapped, and a knob without one is a number nobody can use.", nameof(t));
        if (!(t.Max > t.Min)) throw new ArgumentException($"Tunable '{t.Id}' needs Max > Min.", nameof(t));
        // A derived number must say which rule it came from, and must be drawn apart from the free gains. Both are
        // the whole point of the category: a pilot has to be able to tell, on the row, that he is contradicting a
        // derivation rather than tuning a coefficient.
        if (t.Derived && string.IsNullOrEmpty(t.Rule)) throw new ArgumentException($"Tunable '{t.Id}' is Derived and must carry the Rule it came from.", nameof(t));
        if (t.Derived != string.Equals(t.Group, DerivedGroup, StringComparison.Ordinal))
            throw new ArgumentException($"Tunable '{t.Id}': a Derived tunable belongs in the '{DerivedGroup}' group and nothing else does.", nameof(t));
        if (_byId.TryGetValue(t.Id, out Tunable? old)) _all.Remove(old);
        _byId[t.Id] = t;
        _all.Add(t);
    }

    /// <summary>Every registered tunable, in registration order within a group.</summary>
    public static IReadOnlyList<Tunable> All { get { EnsureCatalog(); return _all; } }

    public static Tunable? ById(string id)
    {
        EnsureCatalog();
        return _byId.TryGetValue(id, out Tunable? t) ? t : null;
    }

    /// <summary>The tunables that apply to this aeroplane, grouped and in <see cref="GroupOrder"/>.</summary>
    public static List<KeyValuePair<string, List<Tunable>>> ForAircraft(AircraftConfig config)
    {
        EnsureCatalog();
        var byGroup = new Dictionary<string, List<Tunable>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (Tunable t in _all)
        {
            bool applies;
            try { applies = t.Applies(config); }
            catch (Exception) { applies = false; }   // a type missing a whole block is not a crash, it is a row that is not there
            if (!applies) continue;
            if (!byGroup.TryGetValue(t.Group, out List<Tunable>? list)) { byGroup[t.Group] = list = new List<Tunable>(); order.Add(t.Group); }
            list.Add(t);
        }
        order.Sort((a, b) =>
        {
            int ia = IndexOfGroup(a), ib = IndexOfGroup(b);
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a, b);
        });
        var result = new List<KeyValuePair<string, List<Tunable>>>(order.Count);
        foreach (string g in order) result.Add(new KeyValuePair<string, List<Tunable>>(g, byGroup[g]));
        return result;
    }

    private static int IndexOfGroup(string g)
    {
        for (int i = 0; i < GroupOrder.Count; i++) if (string.Equals(GroupOrder[i], g, StringComparison.Ordinal)) return i;
        return GroupOrder.Count;
    }

    /// <summary>Load the shipped catalog once. Tests that register their own call <see cref="Register"/> directly;
    /// the catalog is still loaded first so an id cannot silently collide with a real one.</summary>
    private static void EnsureCatalog()
    {
        if (_catalogLoaded) return;
        _catalogLoaded = true;      // set FIRST: TunableCatalog.Load calls Register, which calls EnsureCatalog
        TunableCatalog.Load();
    }

    /// <summary>Tests only: forget everything, including the catalog.</summary>
    public static void ResetForTest()
    {
        _all.Clear();
        _byId.Clear();
        _catalogLoaded = false;
    }
}
