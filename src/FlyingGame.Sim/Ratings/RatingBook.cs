namespace FlyingGame.Sim.Ratings;

/// <summary>
/// One stage of the progression: what has to be flown, in which aeroplanes, to what standard, and on top of
/// which earlier stages. Some stages raise the pilot's level; some (the Cub solo, the aerobatic glider) are
/// gates on the way to the next one and raise nothing by themselves.
/// </summary>
public sealed class Stage
{
    /// <summary>Stable id. It is written into saves (a carried-over stage is stored by id), so never rename one.</summary>
    public string Id = "";
    /// <summary>What the stage is called on the badge screen.</summary>
    public string Title = "";
    /// <summary>One line on what it is for.</summary>
    public string Note = "";

    /// <summary>
    /// The aeroplanes the lessons must be flown in. Any ONE of them will do — that is how the player's
    /// choice between the Decathlon and the Great Lakes is expressed — but every lesson must be flown to
    /// standard in that same one, because a stage is passed in an aeroplane, not across a fleet. Empty means
    /// any aeroplane of <see cref="Classes"/> that is not retired or a placeholder.
    /// </summary>
    public string[] Aircraft = Array.Empty<string>();
    /// <summary>When <see cref="Aircraft"/> is empty: the classes that count.</summary>
    public RatingClass[] Classes = Array.Empty<RatingClass>();

    /// <summary>Lesson ids (<c>PracticeKind</c> names) that must all be passed at <see cref="PassScore"/>.</summary>
    public string[] Lessons = Array.Empty<string>();
    /// <summary>The score every listed lesson must reach. THE SAME LESSONS TO A TIGHTER STANDARD is how the
    /// ladder climbs, which is also how real check rides work.</summary>
    public double PassScore;

    /// <summary>Stage ids that must ALL be complete before this one counts.</summary>
    public string[] Requires = Array.Empty<string>();
    /// <summary>The level this stage gives the pilot, or None for a gate that only opens the next stage.</summary>
    public CertLevel Grants = CertLevel.None;
}

/// <summary>
/// The rules the badges are earned under.
///
/// THE SHAPE OF IT. Owner, 2026-11-01: glider first, with a glider solo; then a solo in the J-3 Cub; then the
/// player CHOOSES the Decathlon or the Great Lakes and earns Pilot in it; then an aerobatic glider; then
/// Instrument ("Blind Flying"), which is gated behind BOTH the glider and the aerobatic stages; then
/// Commercial; then Flight Instructor.
///
///     glider-solo ──► cub-solo ──► pilot (Decathlon | Great Lakes) ──► aerobatic-glider ──┐
///          │                                                                               ▼
///          └──────────────────────────────────────────────────────────────────────────► instrument
///                                                                                          │
///                                                          flight-instructor ◄── commercial ◄┘
///
/// Every step above the student ones asks for the SAME lessons to a tighter standard, which is what the
/// earlier ladder did (2026-10-18: *"this badge system is modeled on the real pilots licenses but is more
/// skill/test based than hour based"*): 70 % to solo, 78 % for Pilot, 83 % blind, 88 % commercial,
/// 93 % to teach it.
///
/// In code rather than in a config, because these are design decisions rather than tuning knobs and Sim
/// has no JSON library. <c>configs/progression.json</c> carries the same graph for reading, and
/// `ProgressionTests` fails if the two disagree.
/// </summary>
public sealed class RatingBook
{
    public List<Stage> Stages { get; } = new();

    /// <summary>What a class asks for before it goes on the badge, by class. See <see cref="ClassRatingScore"/>.</summary>
    public Dictionary<RatingClass, string[]> ClassLessons { get; } = new();

    /// <summary>A class goes on the badge once its whole list is flown in it to the Pilot standard.</summary>
    public double ClassRatingScore = 78;

    public Stage? Stage(string id)
    {
        foreach (Stage s in Stages) if (s.Id == id) return s;
        return null;
    }

    /// <summary>The levels in the order they are earned.</summary>
    public static readonly CertLevel[] Ladder =
    {
        CertLevel.StudentPreSolo, CertLevel.StudentSolo, CertLevel.Pilot, CertLevel.Instrument,
        CertLevel.Commercial, CertLevel.FlightInstructor,
    };

    // Stage ids. They are written into saves, so they are constants and never change.
    public const string GliderSolo = "glider-solo";
    public const string CubSolo = "cub-solo";
    public const string PilotStage = "pilot";
    public const string AerobaticGlider = "aerobatic-glider";
    public const string InstrumentStage = "instrument";
    public const string CommercialStage = "commercial";
    public const string InstructorStage = "flight-instructor";

    /// <summary>Everything a student has to show before going round on their own.</summary>
    public static readonly string[] PreSolo = { "Straight", "ClimbLevelDescend", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
    /// <summary>And the whole syllabus, which is the same list plus the awkward ones.</summary>
    public static readonly string[] Full =
    {
        "Straight", "ClimbLevelDescend", "GlideRear", "GlideSide", "ClimbVyRear", "ClimbVxSide",
        "StallElevator", "StallRudder", "StallSideView", "STurnsTest",
        "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
        "CrosswindAileron", "CrosswindRudder",
    };
    /// <summary>A glider has no climbing to do under its own power, and no go-around either.</summary>
    public static readonly string[] GliderPreSolo = { "Straight", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
    public static readonly string[] GliderFull =
    {
        "Straight", "GlideRear", "GlideSide", "StallElevator", "StallRudder", "StallSideView",
        "STurnsTest", "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
        "CrosswindAileron", "CrosswindRudder",
    };

    private static readonly RatingClass[] Powered = { RatingClass.Asel, RatingClass.Ases, RatingClass.Amel, RatingClass.Ames };

    public static RatingBook Default()
    {
        var book = new RatingBook();

        book.Stages.Add(new Stage
        {
            Id = GliderSolo, Title = "Glider solo",
            Note = "The five glider pre-solo lessons to 70 % in the 2-33. Glider first: no powered climb and no "
                 + "go-around, so a glider's list is shorter and less forgiving.",
            Aircraft = new[] { "glider-2-33-like" }, Lessons = GliderPreSolo, PassScore = 70,
            Grants = CertLevel.StudentSolo,
        });
        book.Stages.Add(new Stage
        {
            Id = CubSolo, Title = "Cub solo",
            Note = "The six pre-solo lessons to 70 % in the J-3 Cub: the aeroplane goes where you point it and comes back down.",
            Aircraft = new[] { "j3-cub-like" }, Lessons = PreSolo, PassScore = 70,
            Requires = new[] { GliderSolo },
        });
        book.Stages.Add(new Stage
        {
            Id = PilotStage, Title = "Pilot",
            Note = "The whole syllabus to 78 % in the Decathlon OR the Great Lakes — your choice, crosswinds and stalls included.",
            Aircraft = new[] { "decathlon-8kcab-like", "great-lakes-like" }, Lessons = Full, PassScore = 78,
            Requires = new[] { CubSolo }, Grants = CertLevel.Pilot,
        });
        book.Stages.Add(new Stage
        {
            Id = AerobaticGlider, Title = "Aerobatic glider",
            Note = "The glider syllabus to 78 % in an aerobatic glider: the Grob 103 Twin III Acro, or the Swift S-1 "
                 + "standing in for one. A DG-class aerobatic glider is coming soon.",
            Aircraft = Hangar.AerobaticGliders, Lessons = GliderFull, PassScore = 78,
            Requires = new[] { PilotStage },
        });
        book.Stages.Add(new Stage
        {
            Id = InstrumentStage, Title = "Blind Flying",
            Note = "The whole syllabus to 83 % in any powered aeroplane, after the glider AND the aerobatic glider "
                 + "stages: you fly by the needles once you have learnt what the aeroplane does without them.",
            Classes = Powered, Lessons = Full, PassScore = 83,
            Requires = new[] { GliderSolo, AerobaticGlider }, Grants = CertLevel.Instrument,
        });
        book.Stages.Add(new Stage
        {
            Id = CommercialStage, Title = "Commercial pilot",
            Note = "The SAME syllabus to 88 %. The manoeuvres do not change; the tolerances halve, which is what "
                 + "the real commercial standard does to you.",
            Classes = Powered, Lessons = Full, PassScore = 88,
            Requires = new[] { InstrumentStage }, Grants = CertLevel.Commercial,
        });
        book.Stages.Add(new Stage
        {
            Id = InstructorStage, Title = "Flight Instructor",
            Note = "The same again to 93 %, on top of a commercial: well enough to show somebody else how.",
            Classes = Powered, Lessons = Full, PassScore = 93,
            Requires = new[] { CommercialStage }, Grants = CertLevel.FlightInstructor,
        });

        book.ClassLessons[RatingClass.Glider] = GliderFull;
        foreach (RatingClass c in Powered) book.ClassLessons[c] = Full;
        return book;
    }
}
