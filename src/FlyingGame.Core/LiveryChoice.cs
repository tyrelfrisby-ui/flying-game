namespace FlyingGame.Core;

/// <summary>
/// WHICH LIVERY A PLAYER FLIES EACH TYPE IN (Ty 2026-10-28: "Yes have Claude build the picker"). Some of our own models
/// come in more than one scheme, each baked into its own GLB: the Pitts as N31DG or N1191, the Extra in Talk Flying or
/// N300UY. The landing page offers a choice for exactly the types listed here, and the choice is per type and persists
/// (the Bridge keeps it in PlayerPrefs under <see cref="PrefKey"/>), so picking N1191 for the Pitts leaves the Extra alone.
///
/// Resolution, in order: the AERO_LIVERY override (captures and dev builds — if set it wins for every type), then the
/// type's saved choice if it is still one of its options, then <see cref="FleetDefault"/>, "type". "type" is what every
/// aeroplane wore before the picker: each type's own colours, which for the Pitts is N31DG and for the Extra its Talk
/// Flying GLB — so a player who never opens the picker sees exactly what they saw before.
///
/// Pure and headless so FlightTests can hold it; Livery.cs (the paint) and AirframeModels.cs (the GLB) both read it.
/// </summary>
public static class LiveryChoice
{
    /// <summary>The fleet-wide default scheme id (Livery.cs "type").</summary>
    public const string FleetDefault = "type";

    /// <summary>The schemes offered per type, the default first. A type with fewer than two shows no picker.
    /// Add a type here (and its GLB suffix in <see cref="OwnSuffix"/>) and the picker offers it.</summary>
    private static readonly Dictionary<string, (string Id, string Name)[]> Options = new()
    {
        ["extra-300-like"] = new[] { ("talk-flying", "Talk Flying"), ("n300uy", "N300UY") },
        ["pitts-s2b-like"] = new[] { ("n31dg", "N31DG"), ("n1191", "N1191") },
    };

    /// <summary>The schemes a player can choose between for <paramref name="aircraftId"/>; empty if it has no choice.</summary>
    public static IReadOnlyList<(string Id, string Name)> For(string? aircraftId) =>
        aircraftId != null && Options.TryGetValue(aircraftId, out var o) ? o : Array.Empty<(string, string)>();

    /// <summary>Does this type get a picker?</summary>
    public static bool HasChoice(string? aircraftId) => For(aircraftId).Count > 1;

    /// <summary>The PlayerPrefs key the type's choice is saved under.</summary>
    public static string PrefKey(string aircraftId) => "livery." + aircraftId;

    private static bool IsOption(string? aircraftId, string? liveryId)
    {
        if (string.IsNullOrEmpty(liveryId)) return false;
        foreach (var o in For(aircraftId)) if (o.Id == liveryId) return true;
        return false;
    }

    /// <summary>The scheme id to paint and load <paramref name="aircraftId"/> with: the override, else the type's saved
    /// choice (ignored if the type has no choice or the id is no longer one of its options), else "type".</summary>
    public static string Resolve(string? aircraftId, string? envOverride, string? saved)
    {
        if (!string.IsNullOrEmpty(envOverride)) return envOverride!;
        if (HasChoice(aircraftId) && IsOption(aircraftId, saved)) return saved!;
        return FleetDefault;
    }

    /// <summary>Which option the picker shows as chosen for a resolved id: the id itself if it is one of the type's
    /// options, otherwise the type's default (the first). null for a type with no choice.</summary>
    public static string? Selected(string? aircraftId, string? resolved)
    {
        var o = For(aircraftId);
        if (o.Count == 0) return null;
        return IsOption(aircraftId, resolved) ? resolved : o[0].Id;
    }

    /// <summary>The GLB suffix for our own model in a livery (Models/&lt;id&gt;/&lt;id&gt;&lt;suffix&gt;). Our 2-33, Extra and
    /// Pitts have their paint baked in, one GLB per livery. 2-33 (tools/own_models/build_sgs233.py, SGS_LIVERY=talk-flying):
    /// the plain factory yellow, and the same yellow with Talk Flying trim and lettering. Extra
    /// (tools/own_models/build_ea300.py, EA_LIVERY=tf|n300uy): Talk Flying for every livery but "n300uy", the plain N300UY
    /// scheme with no Talk Flying branding. Pitts (tools/own_models/build_pitts_s2b.py, PITTS_LIVERY=N31DG|N1191): N31DG
    /// (Hartzell 2-blade) for every livery but "n1191", the factory sunburst with diamonds (MT 3-blade).</summary>
    public static string OwnSuffix(string? modelId, string? liveryId) =>
        modelId == "glider-2-33-like" && liveryId == "talk-flying" ? "-own-tf"
        : modelId == "extra-300-like" && liveryId == "n300uy" ? "-own-n300uy"
        : modelId == "pitts-s2b-like" && liveryId == "n1191" ? "-own-n1191"
        : "-own";
}
