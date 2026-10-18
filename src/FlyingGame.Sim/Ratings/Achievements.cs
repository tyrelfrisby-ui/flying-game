namespace FlyingGame.Sim.Ratings;

/// <summary>The kinds of badge that are nothing to do with a certificate.</summary>
public enum AchievementKind { Aerobatic, Soaring, Stol }

/// <summary>
/// The badges that are not licences. Owner, 2026-10-18: *"There are also badges for non FAA
/// licenses/ratings like aerobatics (based on IAC categories getting 5 or higher on the known sequence)
/// soaring completing cross country races achieving altitude thresholds thermaling etc plus stol competition
/// levels of achievement etc things that anchor more advancements due to peer recognition."*
///
/// The difference from a certificate is who it is for. A rating says you are safe to be allowed to do
/// something; one of these says you are **good at it**, in front of other people. So they are awarded for
/// performance rather than for meeting a standard, and they sit on the badge beside the licence.
///
/// AEROBATICS follows the IAC's own ladder — Primary, Sportsman, Intermediate, Advanced, Unlimited — and the
/// bar is the owner's: fly that category's Known sequence and average **5 or better** per figure, which is
/// the score a competent competition flight actually earns.
///
/// SOARING follows the FAI badges, which are the oldest award scheme in the sport: height gained in one
/// flight, distance flown, and time aloft. The DISTANCES ARE SCALED and that is stated rather than hidden —
/// a Silver distance is 50 km and this world is 27 km across, so the thresholds below are the world's own
/// and a Diamond here is not a Diamond at Minden.
///
/// STOL is the contest the world already runs: take-off plus landing distance added together, which is how
/// a real STOL drag is scored.
/// </summary>
public static class Achievements
{
    // ---- aerobatics: the IAC ladder ----
    public static readonly string[] IacCategories = { "Primary", "Sportsman", "Intermediate", "Advanced", "Unlimited" };

    /// <summary>The average figure score a Known sequence must earn to count. The owner's number.</summary>
    public const double IacPassAverage = 5.0;

    // ---- soaring: the FAI badges, with this world's distances ----
    public static readonly string[] SoaringTiers = { "Bronze", "Silver", "Gold", "Diamond" };

    /// <summary>Height gained in one flight, metres, for Bronze…Diamond. The real badges want
    /// 1,000 / 3,000 / 5,000 m of gain; the top of this world is 5,378 m, so Diamond asks for 4,000.</summary>
    public static readonly double[] SoaringGainM = { 300, 1000, 2500, 4000 };

    /// <summary>Cross-country race courses completed, for Bronze…Diamond.</summary>
    public static readonly int[] SoaringRaces = { 0, 1, 3, 6 };

    /// <summary>Minutes aloft in one flight after release, for Bronze…Diamond.</summary>
    public static readonly double[] SoaringMinutes = { 10, 30, 60, 120 };

    // ---- STOL: total of take-off and landing ----
    public static readonly string[] StolTiers = { "Bronze", "Silver", "Gold", "Master" };
    /// <summary>Take-off plus landing, FEET added together, at or under which each tier is earned.</summary>
    public static readonly double[] StolTotalFt = { 900, 600, 420, 300 };

    /// <summary>The highest IAC category flown to standard, or −1 for none.</summary>
    public static int AerobaticTier(IReadOnlyDictionary<string, double> bestKnownAverage)
    {
        int best = -1;
        for (int i = 0; i < IacCategories.Length; i++)
            if (bestKnownAverage.TryGetValue(IacCategories[i], out double avg) && avg >= IacPassAverage) best = i;
        return best;
    }

    /// <summary>
    /// The soaring tier earned. Every one of the three has to be met for a tier, the way an FAI badge needs
    /// all of its legs — you do not get a Silver for the height alone.
    /// </summary>
    public static int SoaringTier(double bestGainM, int racesCompleted, double bestMinutesAloft)
    {
        int best = -1;
        for (int i = 0; i < SoaringTiers.Length; i++)
            if (bestGainM >= SoaringGainM[i] && racesCompleted >= SoaringRaces[i] && bestMinutesAloft >= SoaringMinutes[i])
                best = i;
        return best;
    }

    /// <summary>The STOL tier for a best combined distance, in feet. Lower is better.</summary>
    public static int StolTier(double bestTotalFt)
    {
        int best = -1;
        for (int i = 0; i < StolTiers.Length; i++)
            if (bestTotalFt > 0 && bestTotalFt <= StolTotalFt[i]) best = i;
        return best;
    }

    /// <summary>What goes on a badge: "SPORTSMAN", "SILVER C", "STOL GOLD" — short, because it shares a tag.</summary>
    public static string Label(AchievementKind kind, int tier) => kind switch
    {
        AchievementKind.Aerobatic => tier < 0 ? "" : IacCategories[tier].ToUpperInvariant(),
        AchievementKind.Soaring => tier < 0 ? "" : SoaringTiers[tier].ToUpperInvariant() + " C",
        _ => tier < 0 ? "" : "STOL " + StolTiers[tier].ToUpperInvariant(),
    };
}
