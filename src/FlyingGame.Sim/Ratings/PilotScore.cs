namespace FlyingGame.Sim.Ratings;

/// <summary>Where a pilot stands on one leaderboard: a challenge flown in a particular type.</summary>
public readonly struct BoardPlacing
{
    public readonly string ChallengeId;
    /// <summary>The aircraft type. Every challenge has a board PER TYPE — a Pitts list and an Extra list.</summary>
    public readonly string AircraftId;
    /// <summary>1 is the top. Outside the top <see cref="PilotScore.BoardSize"/> scores nothing.</summary>
    public readonly int Rank;

    public BoardPlacing(string challengeId, string aircraftId, int rank)
    { ChallengeId = challengeId; AircraftId = aircraftId; Rank = rank; }
}

/// <summary>
/// The public number beside somebody's handle. Owner, 2026-10-18:
///
/// > *"every challenge has a running leaderboard of the top 100 scores for that aircraft type in that
/// > challenge ie a list for Pitts and a list for extra and a list for supercub etc this translates to a
/// > combined score that is shown with the handle and badge above the airplane. The score is the summation
/// > of the inverse of the number you hold on every leader board. So if I am number one on 10 leader boards
/// > my score would be 1000 for example and if I was number 100 on 10 leader boards my score would be 10."*
///
/// So a placing is worth 100 / rank: the top of a board is 100 points, hundredth is 1, and off the board is
/// nothing. Ten firsts make 1,000 and ten hundredths make 10, which are his own two numbers.
///
/// The shape of it is worth noticing, because it decides how people will play. It is very top-heavy — second
/// place is worth half of first, and tenth is worth a tenth — so one board you own outright beats fifty you
/// are merely respectable on. That rewards specialising in an aeroplane and a course, which is exactly how
/// competition flying works, and it means the Super Cub list is as valuable as the Extra list: being the best
/// in the world at a slow aeroplane is worth the same 100 points as being the best at a fast one.
/// </summary>
public static class PilotScore
{
    /// <summary>How many places each board keeps. Outside it, nothing counts.</summary>
    public const int BoardSize = 100;

    /// <summary>Points for a rank: 100 for first, 1 for hundredth, 0 for off the board.</summary>
    public static double PointsForRank(int rank)
        => rank >= 1 && rank <= BoardSize ? (double)BoardSize / rank : 0.0;

    /// <summary>The combined score: every placing added up.</summary>
    public static double Combined(IEnumerable<BoardPlacing> placings)
    {
        double total = 0;
        foreach (BoardPlacing p in placings) total += PointsForRank(p.Rank);
        return total;
    }

    /// <summary>How it is written on a tag: whole points, because nobody reads decimals at two hundred yards.</summary>
    public static string Format(double score)
        => score <= 0 ? "" : score >= 1000 ? $"{score / 1000.0:0.0}k" : $"{score:0}";
}
