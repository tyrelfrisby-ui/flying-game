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
    /// What this pilot ALLOWS other pilots to see. Owner, 2026-10-18: *"show hide is both a declutter and a
    /// personal preference not to show your own badges and accomplishments. they would still be visible on
    /// the user's profile just not shown above their handle to others as they fly."*
    ///
    /// So there are three switches and they are three different things:
    ///
    ///  * **viewing, others** — do I draw other people's badges on MY screen? Declutter. Their choice is
    ///    unaffected; mine is about my own view.
    ///  * **viewing, mine** — do I draw my own badge on my own screen?
    ///  * **publishing, mine** — THIS ONE. Do other pilots get my badge at all? And when the answer is no,
    ///    the badge is not SENT rather than merely not drawn by whoever receives it. That distinction is the
    ///    whole value of the switch: "nobody can see it" has to mean nobody has it, or it is a request
    ///    rather than a setting. It still shows on your own profile, because it is your profile.
    ///
    /// The handle survives either way. You always get to know who is in the circuit with you.
    /// </summary>
    public PilotIdentity Published(bool publish)
        => publish ? this : new PilotIdentity { Handle = Handle };

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
