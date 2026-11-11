namespace FlyingGame.Core;

/// <summary>
/// Which aircraft a platform OFFERS (Ty, 2026-11-03: "the phone (iOS) build should only offer our proven airplanes in the
/// aircraft selector: SGS 2-33 glider, J-3 Cub, Great Lakes, Pitts S-2B, Extra 300"). Every other type stays in the
/// repo, its config in StreamingAssets and in every other build (Mac, Quest, the editor); this is only what the
/// selector, the in-flight Aircraft button, a saved choice and a dogfight opponent may be on a platform that has a list.
/// Engine-free so the list and its fallbacks are tested (FleetAllowListTests); the Unity side picks the list for its
/// platform (<c>SessionSettings.Fleet</c>).
/// </summary>
public static class FleetAllowList
{
    /// <summary>iOS (iPhone and iPad: one build): the five proven aircraft. The order here is not the menu's; the menu keeps
    /// the master fleet's order.</summary>
    public static readonly string[] Ios =
    {
        "glider-2-33-like",   // SGS 2-33
        "j3-cub-like",        // J-3 Cub
        "great-lakes-like",   // Great Lakes
        "pitts-s2b-like",     // Pitts S-2B
        "extra-300-like",     // Extra 300
    };

    /// <summary>The master fleet with everything the allow-list leaves out removed, in the master's order. A null list
    /// offers the whole fleet (every platform without one).</summary>
    public static (string id, string name)[] Offered((string id, string name)[] fleet, string[]? allow) =>
        allow == null ? fleet : System.Array.FindAll(fleet, f => System.Array.IndexOf(allow, f.id) >= 0);

    /// <summary>Is <paramref name="id"/> offered by this allow-list (null = everything is)?</summary>
    public static bool Allows(string[]? allow, string? id) => id != null && (allow == null || System.Array.IndexOf(allow, id) >= 0);

    /// <summary>A saved or default choice made safe: kept if it is offered, else <paramref name="preferred"/> if THAT is
    /// offered, else the first offered aircraft. (A phone that last flew the 172 opens on the 2-33.)</summary>
    public static string Resolve(string? id, (string id, string name)[] offered, string preferred)
    {
        if (System.Array.Exists(offered, f => f.id == id)) return id!;
        if (System.Array.Exists(offered, f => f.id == preferred)) return preferred;
        return offered[0].id;
    }
}
