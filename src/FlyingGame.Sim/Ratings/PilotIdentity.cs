namespace FlyingGame.Sim.Ratings;

/// <summary>
/// EVERYTHING ABOUT A PILOT THAT OTHER PILOTS GET TO SEE, and nothing else.
///
/// This type is the privacy boundary, and it is a type rather than a rule so that it can be checked. The
/// owner drew the line exactly: *"Lesson records and scores are private nobody needs to know you took 100
/// hours to get a Pvt license in a glider or failed the checkride 20 times but challenge scores are
/// public."* So:
///
///  * **Public** — the handle, the badge (which ratings and which achievements), and the combined leaderboard
///    score. All three are results. They say what you can do, not what it cost you.
///  * **Private, and never leaves the device** — <see cref="PilotLog"/>: every lesson, every attempt, every
///    score. How many goes you had at a stall recovery is nobody's business, and a game that showed it would
///    punish exactly the people who most need to practise.
///
/// `PilotIdentityTests` asserts by reflection that nothing log-shaped has crept into this class, the same way
/// the chart is checked for an own-ship position. If somebody one day wants to put "47 attempts" on a name
/// tag, that test is where they will find out it was deliberate.
///
/// The HANDLE is always shown. A viewer may hide badges and scores — their own and other people's — but not
/// the handle, because you have to be able to tell who is in the circuit with you.
/// </summary>
public sealed class PilotIdentity
{
    /// <summary>The name over the aeroplane. Always shown, never hidden, never optional.</summary>
    public string Handle = "";

    /// <summary>The certificate badge, short form: "ASEL PVT", "ATP ASEL AMEL".</summary>
    public string Badge = "";

    /// <summary>The non-licence badges, short: "SPORTSMAN", "SILVER C", "STOL GOLD".</summary>
    public string[] Awards = Array.Empty<string>();

    /// <summary>The combined leaderboard score. A result, so it is public.</summary>
    public double Score;

    /// <summary>
    /// Build it from what the device knows. The log goes IN and does not come out: only the resolved badge
    /// does, which is the whole point of doing it here rather than at the tag.
    /// </summary>
    public static PilotIdentity From(string handle, PilotLog log, RatingBook book,
                                     IEnumerable<string> awards, IEnumerable<BoardPlacing> placings)
    {
        var held = RatingResolver.Resolve(log, book);
        var list = new List<string>();
        foreach (string a in awards) if (!string.IsNullOrEmpty(a)) list.Add(a);
        return new PilotIdentity
        {
            Handle = string.IsNullOrEmpty(handle) ? "Pilot" : handle,
            Badge = Certificate.ShortBadge(held),
            Awards = list.ToArray(),
            Score = PilotScore.Combined(placings),
        };
    }

    /// <summary>
    /// The tag over an aeroplane, as lines. <paramref name="showBadges"/> false leaves just the handle,
    /// which is the viewer's choice and the one thing they cannot switch off.
    /// </summary>
    public string[] TagLines(bool showBadges)
    {
        if (!showBadges) return new[] { Handle };
        var lines = new List<string> { Handle };
        string second = Badge;
        string scoreText = PilotScore.Format(Score);
        if (!string.IsNullOrEmpty(scoreText)) second = string.IsNullOrEmpty(second) ? scoreText : second + "   " + scoreText;
        if (!string.IsNullOrEmpty(second)) lines.Add(second);
        if (Awards.Length > 0) lines.Add(string.Join(" · ", Awards));
        return lines.ToArray();
    }
}
