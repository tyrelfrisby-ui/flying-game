using FlyingGame.Sim.Ratings;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Old saves have to load, and land somewhere sensible, after the 2026-11-01 level restructure.
///
/// The saves here are written out BY HAND in the format the old build wrote ("ratings.log" in PlayerPrefs:
/// "Asel|Flare=81.00;Glider|Straight=72.00"), and the lesson lists are copied in as literals rather than read
/// from RatingBook — so a later change to the current syllabus cannot quietly change what these tests expect
/// an OLD save to be worth.
///
/// The owner's rule: old Private → Pilot; old Student → Student solo or pre-solo depending on what they had
/// earned. The rest of the table is in SaveMigration's comment.
/// </summary>
public class SaveMigrationTests
{
    private readonly ITestOutputHelper _o;
    public SaveMigrationTests(ITestOutputHelper o) { _o = o; }

    private static readonly RatingBook Book = RatingBook.Default();

    // The old build's lists, verbatim.
    private static readonly string[] OldPreSolo = { "Straight", "ClimbLevelDescend", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
    private static readonly string[] OldFull =
    {
        "Straight", "ClimbLevelDescend", "GlideRear", "GlideSide", "ClimbVyRear", "ClimbVxSide",
        "StallElevator", "StallRudder", "StallSideView", "STurnsTest",
        "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
        "CrosswindAileron", "CrosswindRudder",
    };
    private static readonly string[] OldGliderPreSolo = { "Straight", "GlideRear", "StallElevator", "LandingAileron", "LandingRudder" };
    private static readonly string[] OldGliderFull =
    {
        "Straight", "GlideRear", "GlideSide", "StallElevator", "StallRudder", "StallSideView",
        "STurnsTest", "Flare", "ApproachSideView", "LandingAileron", "LandingRudder",
        "CrosswindAileron", "CrosswindRudder",
    };

    /// <summary>A saved log exactly as the old PilotLog.Serialize wrote it: no #v, class|lesson keys, F2 values.</summary>
    private static string OldSave(params (string cls, string[] lessons, double score)[] parts)
        => string.Join(";", parts.SelectMany(p => p.lessons.Select(l => $"{p.cls}|{l}={p.score.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}")));

    private PilotStanding Load(string saved)
    {
        PilotLog log = PilotLog.Deserialize(saved);
        PilotStanding s = RatingResolver.Resolve(log, Book);
        _o.WriteLine($"'{(saved.Length > 60 ? saved.Substring(0, 60) + "…" : saved)}' -> {Certificate.Describe(s)}"
                     + $"  [carried over: {string.Join(", ", s.CarriedOver)}]");
        Assert.Equal(PilotLog.CurrentVersion, log.Version);
        return s;
    }

    [Fact]
    public void NothingSavedIsNothingHeld()
    {
        Assert.Equal(CertLevel.None, Load("").Level);
        Assert.Equal(CertLevel.None, Load("not a log at all;;;=;x=y").Level);
        Assert.Equal(CertLevel.None, RatingResolver.Resolve(PilotLog.Deserialize(null), Book).Level);
    }

    [Fact]
    public void AnOldPreSoloStudentIsStillAPreSoloStudent()
    {
        PilotStanding s = Load("Asel|Straight=95.00");
        Assert.Equal(CertLevel.StudentPreSolo, s.Level);
        Assert.Empty(s.CarriedOver);
    }

    [Fact]
    public void AnOldGliderSoloIsAStudentSoloWithTheGliderSoloCarriedOver()
    {
        PilotStanding s = Load(OldSave(("Glider", OldGliderPreSolo, 72)));
        Assert.Equal(CertLevel.StudentSolo, s.Level);
        Assert.Equal(new[] { "glider-solo" }, s.CarriedOver);
        // What is next is the Cub solo — glider first was already behind them.
        Assert.Equal(new[] { "cub-solo" }, RatingResolver.Next(PilotLog.Deserialize(OldSave(("Glider", OldGliderPreSolo, 72))), Book)
                                                           .Select(n => n.stage.Id).ToArray());
    }

    [Fact]
    public void AnOldPoweredSoloIsAStudentSoloWithBothSolosCarriedOver()
    {
        PilotStanding s = Load(OldSave(("Asel", OldPreSolo, 72)));
        Assert.Equal(CertLevel.StudentSolo, s.Level);
        Assert.Equal(new[] { "glider-solo", "cub-solo" }, s.CarriedOver);
    }

    [Fact]
    public void AnOldSoloWithOneLessonMissingWasNeverSoloAndStillIsNot()
    {
        string[] allButOne = OldPreSolo.Where(l => l != "LandingRudder").ToArray();
        PilotStanding s = Load(OldSave(("Asel", allButOne, 99)));
        Assert.Equal(CertLevel.StudentPreSolo, s.Level);
        Assert.Empty(s.CarriedOver);
    }

    [Theory]
    [InlineData("Asel")]
    [InlineData("Ases")]
    [InlineData("Amel")]
    public void AnOldPrivateIsAPilot(string cls)
    {
        PilotStanding s = Load(OldSave((cls, OldFull, 80)));
        Assert.Equal(CertLevel.Pilot, s.Level);
        Assert.Equal(new[] { "glider-solo", "cub-solo", "pilot" }, s.CarriedOver);
        // The old save never said which aeroplane it was flown in, so there is no choice to show.
        Assert.Null(s.PilotAircraft);
        // And the class it was earned in stays on the badge.
        Assert.Contains(Enum.Parse<RatingClass>(cls), s.Classes);
    }

    [Fact]
    public void AnOldGliderPrivateIsAPilotToo()
    {
        PilotStanding s = Load(OldSave(("Glider", OldGliderFull, 80)));
        Assert.Equal(CertLevel.Pilot, s.Level);
        Assert.Equal(new HashSet<RatingClass> { RatingClass.Glider }, s.Classes);
        Assert.Equal("PILOT · GLIDER", Certificate.ShortBadge(s));
    }

    [Fact]
    public void AnOldCommercialIsACommercial()
    {
        PilotStanding s = Load(OldSave(("Asel", OldFull, 90), ("Glider", OldGliderPreSolo, 75)));
        Assert.Equal(CertLevel.Commercial, s.Level);
        Assert.Equal(new[] { "glider-solo", "cub-solo", "pilot", "aerobatic-glider", "instrument", "commercial" }, s.CarriedOver);
    }

    [Fact]
    public void AnOldAtpIsACommercialAndHasToFlyForInstructor()
    {
        // The airline ladder is gone. An ATP flew the commercial standard and more, but nothing a Flight
        // Instructor is asked for, and an old save cannot show the instructor stage flown in an aeroplane.
        string old = OldSave(("Asel", OldFull, 95), ("Amel", OldFull, 95));
        PilotStanding s = Load(old);
        Assert.Equal(CertLevel.Commercial, s.Level);
        var next = RatingResolver.Next(PilotLog.Deserialize(old), Book);
        Assert.Equal(new[] { "flight-instructor" }, next.Select(n => n.stage.Id).ToArray());
    }

    [Fact]
    public void TheMigrationRunsOnceAndTheMigratedSaveReadsBackTheSame()
    {
        string old = OldSave(("Asel", OldFull, 80));
        PilotLog first = PilotLog.Deserialize(old);
        string saved = first.Serialize();
        _o.WriteLine(saved.Length > 120 ? saved.Substring(0, 120) + "…" : saved);
        Assert.Contains("#v=2.00", saved);
        Assert.Contains("!pilot=1.00", saved);
        PilotLog second = PilotLog.Deserialize(saved);
        Assert.Equal(saved, second.Serialize());
        Assert.Equal(CertLevel.Pilot, RatingResolver.Resolve(second, Book).Level);
        // Asking again does nothing to a current log.
        SaveMigration.Result again = SaveMigration.Migrate(second);
        Assert.Empty(again.Granted);
        // A brand-new log is current from the start and is never "migrated".
        Assert.Equal(PilotLog.CurrentVersion, new PilotLog().Version);
        Assert.Contains("#v=2.00", new PilotLog().Serialize());
    }

    [Fact]
    public void TheScoresSurviveTheMigrationUntouched()
    {
        PilotLog log = PilotLog.Deserialize("Ases|Flare=87.50;Glider|Straight=61.25");
        Assert.Equal(87.5, log.Best(RatingClass.Ases, "Flare"), 2);
        Assert.Equal(61.25, log.Best(RatingClass.Glider, "Straight"), 2);
    }

    /// <summary>
    /// The other direction: a save written by THIS build, read by the old parser. The old parser only ever
    /// looked at "Class|Lesson" keys, so the class scores have to be there in that exact form.
    /// </summary>
    [Fact]
    public void AnOlderBuildStillReadsTheClassScoresOutOfANewSave()
    {
        var log = new PilotLog();
        log.Record("j3-cub-like", RatingClass.Asel, "Flare", 81);
        string saved = log.Serialize();
        _o.WriteLine(saved);
        Assert.Contains("Asel|Flare=81.00", saved.Split(';'));
        Assert.Contains("@j3-cub-like|Asel|Flare=81.00", saved.Split(';'));
        // And none of the new keys begins with a class name, so the old AnyIn() could not mistake one.
        foreach (string part in saved.Split(';'))
            if (!part.StartsWith("Asel|")) Assert.True(part[0] == '@' || part[0] == '!' || part[0] == '#', part);
    }

    [Fact]
    public void OldLevelNumbersAndNamesMapByTheTable()
    {
        // Old enum: None 0, StudentPreSolo 1, StudentSolo 2, Private 3, Commercial 4, Atp 5.
        Assert.Equal(CertLevel.None, LegacyLevels.FromInt(0));
        Assert.Equal(CertLevel.StudentPreSolo, LegacyLevels.FromInt(1));
        Assert.Equal(CertLevel.StudentSolo, LegacyLevels.FromInt(2));
        Assert.Equal(CertLevel.Pilot, LegacyLevels.FromInt(3));
        Assert.Equal(CertLevel.Commercial, LegacyLevels.FromInt(4));
        Assert.Equal(CertLevel.Commercial, LegacyLevels.FromInt(5));
        Assert.Equal(CertLevel.None, LegacyLevels.FromInt(99));
        Assert.Equal(CertLevel.Pilot, LegacyLevels.FromName("Private"));
        Assert.Equal(CertLevel.Commercial, LegacyLevels.FromName("Atp"));
        Assert.Equal(CertLevel.StudentSolo, LegacyLevels.FromName("StudentSolo"));
        Assert.Equal(CertLevel.Instrument, LegacyLevels.FromName("Instrument"));
        Assert.Equal(CertLevel.None, LegacyLevels.FromName(null));
        // The NEW numbers, pinned, because Commercial moved from 4 to 5 and nothing may cast an old one.
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 },
                     new[] { CertLevel.None, CertLevel.StudentPreSolo, CertLevel.StudentSolo, CertLevel.Pilot,
                             CertLevel.Instrument, CertLevel.Commercial, CertLevel.FlightInstructor }.Select(l => (int)l).ToArray());
    }

    [Theory]
    [InlineData("ASEL PVT", "ASEL PILOT")]
    [InlineData("GLIDER COMM · ASEL PVT", "GLIDER COMMERCIAL · ASEL PILOT")]
    [InlineData("ATP ASEL AMEL", "COMMERCIAL ASEL AMEL")]
    [InlineData("ASEL SOLO +2", "ASEL SOLO +2")]
    [InlineData("PILOT · ASEL ASES", "PILOT · ASEL ASES")]
    [InlineData("", "")]
    public void ABadgeFromAnOlderBuildIsDrawnInTheCurrentWords(string wire, string shown)
    {
        Assert.Equal(shown, LegacyLevels.UpgradeBadge(wire));
    }
}
