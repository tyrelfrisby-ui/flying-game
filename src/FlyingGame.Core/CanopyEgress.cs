namespace FlyingGame.Core;

/// <summary>
/// WHAT COMES OFF THE AEROPLANE WHEN THE PILOT BAILS OUT (Ty 2026-11-05 06:37: "bailing out use the real canopy or door
/// unless it is a cub or Great Lakes etc where they don't need to be removed to get out. Not the phony clear canopy and
/// make sure the canopy/door on the airplane is gone"). Until this a type without a rigged canopy got a translucent
/// stand-in dome thrown off it while its own drawn canopy stayed in place.
///
/// The answer is the model's own nodes, by name, the way the draughtsmen named them:
///  • a canopy drawn as one piece to come off — <c>Canopy_Detachable</c> (Pitts, Extra), else the <c>Canopy</c> node
///    (Swift, EB-29R);
///  • a canopy drawn as its parts — <c>Canopy_Frame</c>, <c>Canopy_Glass</c>, <c>Canopy_Hardware</c> (2-33, Grob 103):
///    all of them, together;
///  • a cabin with a hinged door and no canopy — the door on the side he goes out of, with its glass and its latch
///    (the 172: <c>C172_Door_R</c>, <c>Glass.Door.R</c>, <c>C172_Door_latch_R</c>);
///  • nothing for an open cockpit (Great Lakes: windscreens only) or a door that folds out of the way (the Cub's
///    <c>DoorUpper_R</c> / <c>DoorLower_R</c>, open in flight by design), and nothing where the model has no separate
///    canopy or door. There is never a stand-in.
/// Pure: names in, names out. The Bridge (AirframeVisual.JettisonCanopy) detaches what it is given.
/// </summary>
public static class CanopyEgress
{
    /// <summary>A model's node name without glTFast's "Model:" prefix.</summary>
    public static string Bare(string name) => name.StartsWith("Model:", System.StringComparison.Ordinal) ? name.Substring(6) : name;

    /// <summary>The nodes that leave, from a model's node names (any order, prefixes allowed). Empty: nothing leaves.</summary>
    public static IReadOnlyList<string> PartsToJettison(IEnumerable<string> nodeNames, bool exitRight = true)
    {
        var names = new HashSet<string>();
        foreach (string n in nodeNames) if (!string.IsNullOrEmpty(n)) names.Add(Bare(n));
        if (names.Contains("Canopy_Detachable")) return new[] { "Canopy_Detachable" };
        if (names.Contains("Canopy")) return new[] { "Canopy" };
        var parts = new List<string>();
        foreach (string p in new[] { "Canopy_Frame", "Canopy_Glass", "Canopy_Hardware" }) if (names.Contains(p)) parts.Add(p);
        if (parts.Count > 0) return parts;
        // A door that folds open (an upper and a lower half) is how the pilot gets out: it stays.
        foreach (string n in names) if (n.StartsWith("DoorUpper", System.StringComparison.Ordinal) || n.StartsWith("DoorLower", System.StringComparison.Ordinal)) return System.Array.Empty<string>();
        string side = exitRight ? "R" : "L";
        foreach (string n in names)
        {
            if (n.IndexOf("Door", System.StringComparison.Ordinal) < 0) continue;
            if (n.EndsWith("_" + side, System.StringComparison.Ordinal) || n.EndsWith("." + side, System.StringComparison.Ordinal)) parts.Add(n);
        }
        parts.Sort(System.StringComparer.Ordinal);
        return parts;
    }
}
