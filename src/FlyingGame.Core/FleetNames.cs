namespace FlyingGame.Core;

/// <summary>
/// WHERE A TYPE'S MENU NAME COMES FROM: its config's <c>displayName</c>, and nowhere else.
///
/// The landing page used to show a name compiled into <c>SessionSettings.MasterFleet</c>, a second name for the same
/// aeroplane. On 2026-11-11 every config was renamed to a trademark-free name for the public release and the page went
/// on showing the old one — the 2-33 read "2-33-like Glider" on Ty's phone while
/// <c>configs/aircraft/glider-2-33-like.json</c> had said "Glider Trainer" all along, because nothing consumed it.
/// Two names for one aeroplane is how that happened, so the pair list keeps the ORDER and the ids, and this resolves
/// the NAME from the config at runtime. The compiled string survives only as the fallback for a type whose config is
/// missing or has no displayName — if that fallback is ever shown, the config is the thing to fix.
///
/// Engine-free so the rule is testable without Unity (FleetDisplayNameTests): the Unity side supplies the lookup.
/// </summary>
public static class FleetNames
{
    /// <summary>
    /// The same (id, name) pairs in the same order, each name replaced by the config's displayName where there is one.
    /// A lookup that returns null/blank, or throws, leaves that entry's compiled name alone: a broken config must never
    /// empty the aircraft picker.
    /// </summary>
    public static (string id, string name)[] Resolve((string id, string name)[] fleet, Func<string, string?> displayName)
    {
        if (fleet == null) return System.Array.Empty<(string, string)>();
        var resolved = new (string id, string name)[fleet.Length];
        for (int i = 0; i < fleet.Length; i++)
        {
            string? name = null;
            if (displayName != null)
            {
                try { name = displayName(fleet[i].id); }
                catch { name = null; }
            }
            resolved[i] = (fleet[i].id, string.IsNullOrWhiteSpace(name) ? fleet[i].name : name!.Trim());
        }
        return resolved;
    }
}
