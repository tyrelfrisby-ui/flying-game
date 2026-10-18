namespace FlyingGame.Sim.Ratings;

/// <summary>
/// The air a scored contest is flown in. Every field is locked; none of it is the pilot's to choose.
/// </summary>
public readonly struct ContestConditions
{
    public readonly double WindFromDeg, WindSpeedMs;
    public readonly int TurbulenceLevel;
    public readonly double IsaDeviationC, ThermalScale;
    /// <summary>Tyres and skis change take-off and landing distance, so a STOL contest has to pin them too.</summary>
    public readonly bool Bushwheels, Skis;

    public ContestConditions(double windFromDeg, double windSpeedMs, int turbulenceLevel,
                             double isaDeviationC, double thermalScale, bool bushwheels = false, bool skis = false)
    {
        WindFromDeg = windFromDeg; WindSpeedMs = windSpeedMs; TurbulenceLevel = turbulenceLevel;
        IsaDeviationC = isaDeviationC; ThermalScale = thermalScale; Bushwheels = bushwheels; Skis = skis;
    }

    /// <summary>
    /// A short, stable string naming these exact conditions, to be carried with a submitted score so a
    /// board can refuse anything flown in different air.
    ///
    /// Worth being honest about what this does and does not buy. It makes an ACCIDENT impossible: a score
    /// flown with the wind left on from the last flight will not match and will be rejected, which is most
    /// of the problem. It does NOT stop a determined cheat, because the client computes it and a client can
    /// be made to lie. The real answer is that a leaderboard entry should be a REPLAY the server re-flies —
    /// and this project already records replays, so that door is open. Until then this is a seatbelt and
    /// not a lock, and the leaderboard should say so.
    /// </summary>
    public string Fingerprint()
        => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                         "w{0:F0}/{1:F1}t{2}i{3:F1}h{4:F2}{5}{6}",
                         WindFromDeg, WindSpeedMs, TurbulenceLevel, IsaDeviationC, ThermalScale,
                         Bushwheels ? "B" : "", Skis ? "S" : "");
}

/// <summary>
/// WHICH FLIGHTS COUNT FOR A SCORE, AND IN WHAT AIR. Owner, 2026-10-18:
///
/// > *"every challenge like a specific aerobatic competition category, glider race course, stol contest must
/// > have the exact same conditions locked ie users cannot tweak the wind to get a better score or crank up
/// > the thermal intensity etc."*
///
/// This is the load-bearing piece of the whole leaderboard. A board of times flown in air each pilot chose
/// for themselves is not a board, it is a list of who picked the best tailwind — so a contest's conditions
/// live here, in code, and the menu's wind, turbulence, temperature, thermal and tyre controls are overridden
/// and shown as locked the moment a scored contest is selected.
///
/// THE STANDARD IS CALM, with three deliberate exceptions:
///
///  * **The STOL contest gets bushwheels**, locked ON. They are a legitimate STOL modification and every
///    real contest aeroplane has them, so locking them off would be the odd choice; locking them on makes
///    the field equal, which is the only thing that matters.
///  * **A glider course gets thermals**, locked at the standard strength, because a soaring task with the
///    lift switched off is not a soaring task. The wind is light and from a fixed direction so the ridge
///    works the same way for everybody.
///  * **The crosswind and headwind challenges get their wind**, obviously — a crosswind landing challenge in
///    calm air is a landing challenge. Their whole point is the wind, so it is pinned rather than removed.
/// </summary>
public static class Contests
{
    /// <summary>Dead calm, standard day, no thermals, standard tyres. What most contests are flown in.</summary>
    public static readonly ContestConditions Calm = new(0, 0, 0, 0, 0);

    /// <summary>A soaring day: light easterly onto the ridge, standard thermals, a little turbulence.</summary>
    public static readonly ContestConditions SoaringDay = new(90, 4, 1, 0, 1.0);

    /// <summary>Eight knots straight down the runway.</summary>
    public static readonly ContestConditions Headwind = new(0, 4.1, 0, 0, 0);

    /// <summary>Twelve knots across it, from the east onto the north-south runways.</summary>
    public static readonly ContestConditions Crosswind = new(90, 6.2, 1, 0, 0);

    private static readonly Dictionary<string, ContestConditions> Locked = new()
    {
        // The events that exist today.
        ["event:race"] = Calm,
        ["event:stol"] = new ContestConditions(0, 0, 0, 0, 0, bushwheels: true),
        ["event:dust"] = Calm,
        ["event:aerobox"] = Calm,

        // The arena-1 challenges, which until now inherited whatever the pilot had left in the menu.
        ["a1c1-wings-level"] = Calm,
        ["a1c2-best-glide"] = Calm,
        ["a1c3-cardinal-turn"] = Calm,
        ["a1c4-stall-recover"] = Calm,
        ["a1c10-headwind-landing"] = Headwind,
        ["a1c11-crosswind-landing"] = Crosswind,
    };

    /// <summary>
    /// Contests that are DESIGNED but NOT BUILT, listed here rather than left implicit so that the gap is
    /// visible: judging an aerobatic sequence figure by figure, and a cross-country soaring task, are each a
    /// feature in their own right. When they arrive they come with a line in <see cref="Locked"/> and
    /// nothing else here changes.
    /// </summary>
    public static readonly string[] Planned =
    {
        "contest:iac-primary", "contest:iac-sportsman", "contest:iac-intermediate",
        "contest:iac-advanced", "contest:iac-unlimited",
        "contest:soaring-triangle", "contest:soaring-outandreturn",
    };

    /// <summary>Does a score from this flight go on a board? Only if its air is pinned.</summary>
    public static bool IsScored(string? challengeId)
        => !string.IsNullOrEmpty(challengeId) && Locked.ContainsKey(challengeId!);

    /// <summary>The conditions a contest must be flown in.</summary>
    public static ContestConditions For(string challengeId) => Locked[challengeId];

    public static bool TryFor(string? challengeId, out ContestConditions c)
    {
        if (!string.IsNullOrEmpty(challengeId) && Locked.TryGetValue(challengeId!, out c)) return true;
        c = default;
        return false;
    }

    /// <summary>Every scored contest, for the leaderboard screen and for a test to walk.</summary>
    public static IEnumerable<string> All => Locked.Keys;

    /// <summary>
    /// A board is per contest AND per aircraft type — *"a list for Pitts and a list for extra and a list for
    /// supercub"*. This is its key.
    /// </summary>
    public static string BoardKey(string challengeId, string aircraftId) => $"{challengeId}|{aircraftId}";
}
