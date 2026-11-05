namespace FlyingGame.Sim.Ratings;

/// <summary>An aeroplane the progression or a job names that is NOT in <c>configs/aircraft</c>.</summary>
public sealed class PlaceholderAircraft
{
    public string Id = "";
    public string Name = "";
    /// <summary>Why it is wanted: which stage or job asks for it.</summary>
    public string WantedFor = "";
}

/// <summary>
/// Which aeroplanes the progression may and may not use, beyond what is in <c>configs/aircraft</c>.
///
/// PLACEHOLDERS are aeroplanes the ladder or a job asks for that have no config and no model yet. They are
/// data only: an id, a name and the reason. Nothing loads them, no picker lists them, and a stage that asks
/// for one says "coming soon" rather than offering a flight that cannot start. When one is built, its config
/// goes in <c>configs/aircraft</c> under the same id and it comes off this list.
///
/// RETIRED aeroplanes still have a config, a model and a livery — nothing is deleted — but no picker offers
/// them and no stage, job or lesson counts a flight in them. Owner, 2026-11-01: the 737-800 glass cockpit is
/// retired; the airline track is the DC-3 and a steam-gauge 737-200.
/// </summary>
public static class Hangar
{
    public static readonly PlaceholderAircraft[] Placeholders =
    {
        new() { Id = "glider-dg-acro-like", Name = "DG-class aerobatic glider (coming soon)", WantedFor = "the aerobatic glider stage" },
        new() { Id = "beech18-like", Name = "Beech 18-like (coming soon)", WantedFor = "the air-mail job" },
        new() { Id = "boeing-737-200-like", Name = "737-200-like, steam gauges (coming soon)", WantedFor = "the airline track" },
    };

    /// <summary>The aeroplanes the aerobatic glider stage accepts. The Grob G 103 C Twin III Acro is built (2026-11-01, re-based from the Twin II 2026-11-04;
    /// configs/aircraft/glider-grob103-acro-like.json), so the stage can be flown; the DG is still a placeholder. The
    /// Swift S-1 counts as a STAND-IN aerobatic glider (owner, 2026-11-04 16:05 PT: "the Swift S-1 counts as a stand-in
    /// aerobatic glider so Blind Flying is reachable"), alongside the Grob.</summary>
    public static readonly string[] AerobaticGliders = { "glider-grob103-acro-like", "glider-dg-acro-like", "glider-swift-s1-like" };

    /// <summary>Kept on disk, hidden from every picker and from the progression.</summary>
    public static readonly string[] Retired = { "boeing-737-like" };

    public static bool IsPlaceholder(string? id)
    {
        foreach (PlaceholderAircraft p in Placeholders) if (p.Id == id) return true;
        return false;
    }

    public static bool IsRetired(string? id) => id != null && Array.IndexOf(Retired, id) >= 0;

    /// <summary>Can a player pick this and fly it? Not if it is retired, and not if there is nothing to fly.</summary>
    public static bool IsPickable(string? id) => !string.IsNullOrEmpty(id) && !IsRetired(id) && !IsPlaceholder(id);
}
