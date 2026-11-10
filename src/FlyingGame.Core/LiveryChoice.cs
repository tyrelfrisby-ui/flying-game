namespace FlyingGame.Core;

/// <summary>
/// WHICH LIVERY A PLAYER FLIES EACH TYPE IN (Ty 2026-10-28: "Yes have Claude build the picker"). Some of our own models
/// come in more than one scheme, each baked into its own GLB: the Pitts checkered or factory, each unmarked or as N31DG /
/// N1191, the Extra unmarked, in Talk Flying or as N300UY. The landing page offers a choice for exactly the types listed
/// here, and the choice is per type and persists (the Bridge keeps it in PlayerPrefs under <see cref="PrefKey"/>), so
/// picking N1191 for the Pitts leaves the Extra alone.
///
/// Resolution, in order: the AERO_LIVERY override (captures and dev builds — if set it wins for every type), then the
/// type's saved choice if it is still one of its options, then the type's default (its first option: the UNMARKED scheme,
/// Ty 2026-10-28 "unmarked liveries become the game defaults"), and for a type with no choice <see cref="FleetDefault"/>,
/// "type". A saved id from before (n31dg, n1191, talk-flying, n300uy) is still an option and keeps working; anything else
/// falls back to the default.
///
/// Pure and headless so FlightTests can hold it; Livery.cs (the paint) and AirframeModels.cs (the GLB) both read it.
/// </summary>
public static class LiveryChoice
{
    /// <summary>The fleet-wide default scheme id (Livery.cs "type"), for the types with no choice.</summary>
    public const string FleetDefault = "type";

    /// <summary>The schemes offered per type, the default first. A type with fewer than two shows no picker.
    /// Add a type here (and its GLB suffix in <see cref="OwnSuffix"/>) and the picker offers it.</summary>
    private static readonly Dictionary<string, (string Id, string Name)[]> Options = new()
    {
        ["extra-300-like"] = new[] { ("unmarked", "Unmarked"), ("talk-flying", "Talk Flying"), ("n300uy", "N300UY") },
        ["pitts-s2b-like"] = new[] { ("checker-unmarked", "Checkered"), ("factory-unmarked", "Factory"), ("n31dg", "N31DG"), ("n1191", "N1191") },
    };

    /// <summary>
    /// Types that come in exactly ONE scheme, whatever was saved or set. Ty 2026-11-02, after flying the red-and-white
    /// 2-33 on the Quest: that livery is removed everywhere, and the yellow 2-33 is the default and the only one. The
    /// red-and-white was the procedural trainer drawn for the 2-33's "type" livery (AirframeVisual.StyleFor), so the
    /// 2-33 never resolves to "type" any more: it is always our own yellow model in Talk Flying trim
    /// (glider-2-33-like-own-tf.glb, "the yellow 2-33" of da5c8f05 / ad33a2c). A saved choice from a build with a
    /// 2-33 picker ("factory", "type", anything) is ignored, so it falls back to the yellow one. The plain factory
    /// yellow (-own, "factory") is still yellow and stays reachable through AERO_LIVERY for captures and renders; no
    /// menu offers it.
    /// </summary>
    private static readonly Dictionary<string, (string Id, string[] OverrideAlso)> Only = new()
    {
        ["glider-2-33-like"] = ("talk-flying", new[] { "factory" }),
    };

    /// <summary>Every type the picker has more than one scheme for (the packager's list; see PhoneLiveries).</summary>
    public static IReadOnlyList<string> TypesWithOptions
    {
        get
        {
            var list = new List<string>();
            foreach (var kv in Options) if (kv.Value.Length > 1) list.Add(kv.Key);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    /// <summary>The schemes a player can choose between for <paramref name="aircraftId"/>; empty if it has no choice.
    ///
    /// ON THE PHONE THERE IS ONE (Ty 2026-11-10: "ship a single default livery for each aeroplane; the alternates stay
    /// in desktop and Quest builds"). An iOS player is compiled with PhoneLiveries.Define and its build does not
    /// CONTAIN the alternates' GLBs, so offering them would be offering a missing file: the list is cut to the default
    /// (the first), which makes HasChoice false, hides the picker and sends Resolve to <see cref="FleetDefault"/> —
    /// and every type's <see cref="OwnSuffix"/> maps its first option and "type" to the same GLB, so the aeroplane is
    /// unchanged. Mac and Quest builds see the whole list.</summary>
    public static IReadOnlyList<(string Id, string Name)> For(string? aircraftId)
    {
        if (aircraftId == null || !Options.TryGetValue(aircraftId, out var o)) return Array.Empty<(string, string)>();
        if (PhoneLiveries.OneLiveryPerType && o.Length > 1) return new[] { o[0] };
        return o;
    }

    /// <summary>Does this type get a picker?</summary>
    public static bool HasChoice(string? aircraftId) => For(aircraftId).Count > 1;

    /// <summary>The type's default scheme (its first option), or null for a type with no choice.</summary>
    public static string? Default(string? aircraftId) => HasChoice(aircraftId) ? For(aircraftId)[0].Id : null;

    /// <summary>The PlayerPrefs key the type's choice is saved under.</summary>
    public static string PrefKey(string aircraftId) => "livery." + aircraftId;

    private static bool IsOption(string? aircraftId, string? liveryId)
    {
        if (string.IsNullOrEmpty(liveryId)) return false;
        foreach (var o in For(aircraftId)) if (o.Id == liveryId) return true;
        return false;
    }

    /// <summary>The scheme id to paint and load <paramref name="aircraftId"/> with: for a type with only one scheme
    /// (<see cref="Only"/>, the 2-33) that scheme; otherwise the override, else the type's saved choice if it is still
    /// one of its options, else the type's default; "type" for a type with no choice.</summary>
    public static string Resolve(string? aircraftId, string? envOverride, string? saved)
    {
        // AERO_LIVERY asks for a GLB by name, and on the phone the alternates are not in the build at all
        // (PhoneLiveries). Drop the override there rather than place a model that is not there: a capture build that
        // really needs them is made with FLYINGGAME_PHONE_ALL_LIVERIES=1, which leaves the define off.
        if (PhoneLiveries.OneLiveryPerType) envOverride = null;
        if (aircraftId != null && Only.TryGetValue(aircraftId, out var only))
            return !string.IsNullOrEmpty(envOverride) && System.Array.IndexOf(only.OverrideAlso, envOverride) >= 0 ? envOverride! : only.Id;
        if (!string.IsNullOrEmpty(envOverride)) return envOverride!;
        if (!HasChoice(aircraftId)) return FleetDefault;
        return IsOption(aircraftId, saved) ? saved! : Default(aircraftId)!;
    }

    /// <summary>Which option the picker shows as chosen for a resolved id: the id itself if it is one of the type's
    /// options, otherwise the type's default (the first). null for a type with no choice.</summary>
    public static string? Selected(string? aircraftId, string? resolved)
    {
        var o = For(aircraftId);
        if (o.Count == 0) return null;
        return IsOption(aircraftId, resolved) ? resolved : o[0].Id;
    }

    /// <summary>
    /// The types whose OWN model (Models/&lt;id&gt;/&lt;id&gt;-own*.glb) the game draws whatever the "own models" option says
    /// (AirframeModels.PickOwn). Every other type draws its downloaded model unless SessionSettings.UseOwnModels is on,
    /// and that option is OFF by default.
    ///
    /// c172-like joined 2026-11-04. Ty, 5:25 PM PT, on the iPhone (set.ownModels=0): "The Cessna 172 cockpit is
    /// TRANSPARENT, with NO interior at all." With the option off the 172 drew the downloaded c172-like.obj, an exterior
    /// shell with no cabin, whose outward-facing faces are back-face culled from the eye, so the pilot looked straight
    /// through the aeroplane. The period panel, the four-place cabin, the rig and the raised yoke that Ty approved in
    /// renders (OwnRigCheck forces own models on) had only ever been in our GLB, which the game never placed by default.
    /// </summary>
    private static readonly HashSet<string> OwnAlways = new(StringComparer.Ordinal)
    {
        "extra-300-like",       // our EA300 for every livery (Ty 2026-10-27)
        "pitts-s2b-like",       // our S-2B for every livery (Ty 2026-10-27)
        "great-lakes-like",     // there is no downloaded Great Lakes
        "glider-2-33-like",     // yellow, the only 2-33 (Ty 2026-11-02)
        "j3-cub-like",          // wheels and floats (Ty 2026-11-02)
        "j3-cub-floats-like",
        "c172-like",            // its cabin and panel exist only in our GLB (Ty 2026-11-04, see above)
        "glider-grob103-acro-like",   // there is no downloaded Twin III (2026-11-04)
    };

    /// <summary>Does the game draw our own model of this type even with the "own models" option off?</summary>
    public static bool OwnModelAlways(string? modelId) => modelId != null && OwnAlways.Contains(modelId);

    /// <summary>The GLB suffix for our own model in a livery (Models/&lt;id&gt;/&lt;id&gt;&lt;suffix&gt;). Our 2-33, Extra and
    /// Pitts have their paint baked in, one GLB per livery. 2-33 (tools/own_models/build_sgs233.py, SGS_LIVERY=talk-flying):
    /// the plain factory yellow, and the same yellow with Talk Flying trim and lettering. Extra
    /// (tools/own_models/build_ea300.py, EA_LIVERY=unmarked|tf|n300uy, every one a 3-blade prop): Talk Flying for
    /// "talk-flying" (and the old "sunrise"), N300UY for "n300uy", and the unmarked default for anything else ("type"
    /// included). Pitts (tools/own_models/build_pitts_s2b.py, PITTS_LIVERY=CHECKER-UNMARKED|FACTORY-UNMARKED|N31DG|N1191):
    /// the checkered scheme with the Hartzell 2-blade, as N31DG ("n31dg", and "talk-flying", whose Pitts was always N31DG)
    /// or unmarked; the factory sunburst with diamonds and the MT 3-blade, as N1191 or unmarked ("factory-unmarked"); the
    /// unmarked checkered default for anything else.</summary>
    public static string OwnSuffix(string? modelId, string? liveryId) => (modelId, liveryId) switch
    {
        ("glider-2-33-like", "talk-flying") => "-own-tf",
        ("extra-300-like", "talk-flying" or "sunrise") => "-own",
        ("extra-300-like", "n300uy") => "-own-n300uy",
        ("extra-300-like", _) => "-own-unmarked",
        ("pitts-s2b-like", "n31dg" or "talk-flying") => "-own",
        ("pitts-s2b-like", "n1191") => "-own-n1191",
        ("pitts-s2b-like", "factory-unmarked") => "-own-factory-unmarked",
        ("pitts-s2b-like", _) => "-own-checker-unmarked",
        _ => "-own",
    };
}
