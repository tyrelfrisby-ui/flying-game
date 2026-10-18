namespace FlyingGame.Sim.Ratings;

/// <summary>One rung: what a given class has to fly, and to what standard, to earn a level.</summary>
public sealed class RatingStep
{
    /// <summary>The class this applies to, or null for "every class that does not override it".</summary>
    public RatingClass? Class;
    public CertLevel Level;
    /// <summary>The score every listed lesson must reach. THE SAME LESSONS TO A TIGHTER STANDARD is how the
    /// ladder works, which is also how real certification works.</summary>
    public double PassScore;
    /// <summary>Lesson ids that must all be passed at <see cref="PassScore"/>.</summary>
    public string[] Lessons = Array.Empty<string>();
    /// <summary>What the step is called on the badge screen.</summary>
    public string Title = "";
    /// <summary>One line on what it is for.</summary>
    public string Note = "";
}

/// <summary>
/// The rules the badges are earned under, read from <c>configs/ratings.json</c> so the owner can argue with
/// the numbers without a build.
///
/// THE SHAPE OF IT, and the reason it is this shape: a level does not ask for new manoeuvres so much as the
/// SAME manoeuvres to a tighter standard. A private pilot flies the pattern to 78 % of the ideal; a
/// commercial pilot flies the same pattern to 88 %; an ATP to 93 %. That is how the real tolerances work —
/// the commercial ACS asks for half the altitude and heading slop the private one allows — and it means the
/// ladder is skill based without needing a single hour counted anywhere, which is what the owner asked for
/// (2026-10-18: *"this badge system is modeled on the real pilots licenses but is more skill/test based
/// than hour based"*).
///
/// Gliders override the list, because a glider has no climb lessons to fly.
/// </summary>
public sealed class RatingBook
{
    public List<RatingStep> Steps { get; } = new();

    /// <summary>The step for a class and level: the class's own override if it has one, else the general one.</summary>
    public RatingStep? For(RatingClass cls, CertLevel level)
    {
        RatingStep? general = null;
        foreach (RatingStep s in Steps)
        {
            if (s.Level != level) continue;
            if (s.Class == cls) return s;
            if (s.Class == null) general = s;
        }
        return general;
    }

    /// <summary>The levels in the order they must be earned.</summary>
    public static readonly CertLevel[] Ladder =
    {
        CertLevel.StudentPreSolo, CertLevel.StudentSolo, CertLevel.Private, CertLevel.Commercial, CertLevel.Atp,
    };

    /// <summary>
    /// THE LADDER, in code rather than in a config file, because these are design decisions rather than
    /// tuning knobs — and because Sim deliberately has no JSON library (Core's loader is split between
    /// System.Text.Json for the headless build and Newtonsoft for Unity, and a second pair of those for
    /// this would be two more places to go wrong). If the owner wants to tune the standards without a
    /// build, a loader belongs in the Bridge over the top of these defaults.
    ///
    /// The lesson ids are <c>PracticeKind</c> names.
    /// </summary>
    public static RatingBook Default()
    {
        var book = new RatingBook();

        // Everything a student has to show before going round on their own.
        string[] preSolo = { "Straight", "ClimbLevelDescend", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
        // And everything a certificate asks for, which is the same list plus the awkward ones.
        string[] full =
        {
            "Straight", "ClimbLevelDescend", "GlideRear", "GlideSide", "ClimbVyRear", "ClimbVxSide",
            "StallElevator", "StallRudder", "StallSideView", "STurnsTest",
            "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
            "CrosswindAileron", "CrosswindRudder",
        };
        // A glider has no climbing to do under its own power, and no go-around either.
        string[] gliderPreSolo = { "Straight", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
        string[] gliderFull =
        {
            "Straight", "GlideRear", "GlideSide", "StallElevator", "StallRudder", "StallSideView",
            "STurnsTest", "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
            "CrosswindAileron", "CrosswindRudder",
        };

        void Add(RatingClass? cls, CertLevel level, double score, string[] lessons, string title, string note)
            => book.Steps.Add(new RatingStep { Class = cls, Level = level, PassScore = score, Lessons = lessons, Title = title, Note = note });

        // Being a pre-solo student costs nothing but turning up: fly anything in the class and you are one.
        Add(null, CertLevel.StudentPreSolo, 0, Array.Empty<string>(), "Student, pre-solo",
            "You have flown something in this class. Nothing else is asked.");
        Add(null, CertLevel.StudentSolo, 70, preSolo, "Student, solo",
            "The six pre-solo lessons to 70 %: the aeroplane goes where you point it and comes back down.");
        Add(null, CertLevel.Private, 78, full, "Private pilot",
            "The whole syllabus to 78 %, crosswinds and stalls included.");
        Add(null, CertLevel.Commercial, 88, full, "Commercial pilot",
            "The SAME syllabus to 88 %. The manoeuvres do not change; the tolerances halve, which is what "
            + "the real commercial standard does to you.");
        Add(null, CertLevel.Atp, 93, full, "Airline transport pilot",
            "The same again to 93 %, on top of a commercial. The top of the ladder.");

        Add(RatingClass.Glider, CertLevel.StudentSolo, 70, gliderPreSolo, "Glider student, solo",
            "No powered climb and no go-around: a glider's pre-solo list is shorter and less forgiving.");
        Add(RatingClass.Glider, CertLevel.Private, 78, gliderFull, "Glider pilot, private", "");
        Add(RatingClass.Glider, CertLevel.Commercial, 88, gliderFull, "Glider pilot, commercial", "");
        Add(RatingClass.Glider, CertLevel.Atp, 93, gliderFull, "Glider, ATP", "");

        return book;
    }

}
