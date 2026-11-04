using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Ratings;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The badge system: the level ladder, the awards that are not levels, the leaderboard score, the air a
/// scored contest is locked into, and the privacy line that runs through all of it.
///
/// The owner's brief, across four messages on 2026-10-18, comes down to five rules, and each one gets a
/// test: you cannot skip a rung; the class comes from the aeroplane; lesson records are private and results
/// are public; a scored contest is flown in the same air by everybody; and the score is the sum of 100/rank.
/// </summary>
public class BadgeTests
{
    private readonly ITestOutputHelper _o;
    public BadgeTests(ITestOutputHelper o) { _o = o; }

    private static readonly RatingBook Book = RatingBook.Default();

    private const string Glider = "glider-2-33-like", Cub = "j3-cub-like", Decathlon = "decathlon-8kcab-like",
                         GreatLakes = "great-lakes-like", AcroGlider = "glider-grob103-acro-like", C172 = "c172-like";

    /// <summary>Fly a list of lessons in one aeroplane, at a given score.</summary>
    private static void Fly(PilotLog log, string aircraft, RatingClass cls, string[] lessons, double score)
    {
        foreach (string lesson in lessons) log.Record(aircraft, cls, lesson, score);
    }

    /// <summary>Fly the ladder from the bottom up to and including a stage, every lesson at the same score.</summary>
    internal static PilotLog Through(string stage, double score = 99, string pilotIn = Decathlon)
    {
        var log = new PilotLog();
        string[] order = { RatingBook.GliderSolo, RatingBook.CubSolo, RatingBook.PilotStage, RatingBook.AerobaticGlider,
                           RatingBook.InstrumentStage, RatingBook.CommercialStage, RatingBook.InstructorStage };
        foreach (string id in order)
        {
            Stage st = Book.Stage(id)!;
            if (id == RatingBook.GliderSolo) Fly(log, Glider, RatingClass.Glider, st.Lessons, score);
            else if (id == RatingBook.CubSolo) Fly(log, Cub, RatingClass.Asel, st.Lessons, score);
            else if (id == RatingBook.PilotStage) Fly(log, pilotIn, RatingClass.Asel, st.Lessons, score);
            // The aerobatic glider stage is flown in the Grob G 103 C Twin III Acro (built 2026-11-01, re-based 2026-11-04).
            else if (id == RatingBook.AerobaticGlider) Fly(log, AcroGlider, RatingClass.Glider, st.Lessons, score);
            else Fly(log, pilotIn, RatingClass.Asel, st.Lessons, score);
            if (id == stage) break;
        }
        return log;
    }

    // ---------------------------------------------------------------- the ladder

    [Fact]
    public void FlyingSomethingMakesYouAPreSoloStudentAndNothingMore()
    {
        var log = new PilotLog();
        log.Record(C172, RatingClass.Asel, "Straight", 95);
        PilotStanding s = RatingResolver.Resolve(log, Book);
        _o.WriteLine(Certificate.Describe(s));
        Assert.Equal(CertLevel.StudentPreSolo, s.Level);
        Assert.Equal("STUDENT", Certificate.ShortBadge(s));
        Assert.Equal("Student pilot, pre-solo", Certificate.Describe(s));
        Assert.Empty(s.Stages);
        Assert.Empty(s.Classes);
    }

    [Fact]
    public void YouCannotSkipARung()
    {
        // The whole syllabus at 99 % in the Decathlon, with no glider solo and no Cub solo behind it: the
        // ladder has missing rungs, so it is worth nothing above a pre-solo student however well it went.
        var log = new PilotLog();
        Fly(log, Decathlon, RatingClass.Asel, RatingBook.Full, 99);
        PilotStanding s = RatingResolver.Resolve(log, Book);
        _o.WriteLine($"Decathlon only, at 99 %: {Certificate.Describe(s)}");
        Assert.Equal(CertLevel.StudentPreSolo, s.Level);
        Assert.DoesNotContain(RatingBook.PilotStage, s.Stages);

        // Glider solo: Student solo. Still no Pilot — the Cub solo is in between.
        Fly(log, Glider, RatingClass.Glider, RatingBook.GliderPreSolo, 99);
        s = RatingResolver.Resolve(log, Book);
        _o.WriteLine($"+ glider solo: {Certificate.Describe(s)}");
        Assert.Equal(CertLevel.StudentSolo, s.Level);
        Assert.Equal(new[] { RatingBook.GliderSolo }, s.Stages);

        // And the Cub solo, which makes the Decathlon flying already in the log count all at once.
        Fly(log, Cub, RatingClass.Asel, RatingBook.PreSolo, 99);
        s = RatingResolver.Resolve(log, Book);
        _o.WriteLine($"+ Cub solo: {Certificate.Describe(s)}");
        Assert.Equal(CertLevel.Pilot, s.Level);
        Assert.Equal(new[] { RatingBook.GliderSolo, RatingBook.CubSolo, RatingBook.PilotStage }, s.Stages);
    }

    [Fact]
    public void TheCubSoloHasToBeFlownInTheCubAndTheGliderSoloInTheTwoThirtyThree()
    {
        // The same lessons in the wrong aeroplanes: a 172 is not a Cub and the Swift is not a 2-33.
        var log = new PilotLog();
        Fly(log, "glider-swift-s1-like", RatingClass.Glider, RatingBook.GliderPreSolo, 99);
        Fly(log, C172, RatingClass.Asel, RatingBook.PreSolo, 99);
        Assert.Equal(CertLevel.StudentPreSolo, RatingResolver.Resolve(log, Book).Level);
        Fly(log, Glider, RatingClass.Glider, RatingBook.GliderPreSolo, 99);
        Assert.Equal(new[] { RatingBook.GliderSolo }, RatingResolver.Resolve(log, Book).Stages);
    }

    [Theory]
    [InlineData("decathlon-8kcab-like")]
    [InlineData("great-lakes-like")]
    public void ThePilotStageIsTheDecathlonOrTheGreatLakesAndThePlayersChoiceIsKept(string choice)
    {
        PilotStanding s = RatingResolver.Resolve(Through(RatingBook.PilotStage, 80, choice), Book);
        _o.WriteLine($"{choice}: {Certificate.Describe(s)}, flown in {s.PilotAircraft}");
        Assert.Equal(CertLevel.Pilot, s.Level);
        Assert.Equal(choice, s.PilotAircraft);
        // Anything else is not the choice on offer.
        PilotStanding other = RatingResolver.Resolve(Through(RatingBook.PilotStage, 80, "extra-300-like"), Book);
        Assert.Equal(CertLevel.StudentSolo, other.Level);
        Assert.Null(other.PilotAircraft);
    }

    [Fact]
    public void EveryLessonOfAStageHasToBeFlownInTheSameAeroplane()
    {
        // Half the syllabus in the Decathlon and the other half in the Great Lakes is not a Pilot stage in either.
        PilotLog log = Through(RatingBook.CubSolo, 99);
        string[] full = RatingBook.Full;
        Fly(log, Decathlon, RatingClass.Asel, full.Take(full.Length / 2).ToArray(), 99);
        Fly(log, GreatLakes, RatingClass.Asel, full.Skip(full.Length / 2).ToArray(), 99);
        Assert.Equal(CertLevel.StudentSolo, RatingResolver.Resolve(log, Book).Level);
    }

    [Fact]
    public void TheSameLessonsToATighterStandardIsWhatTheLadderIs()
    {
        // 70 % solos, 78 % is Pilot, 83 % is Blind Flying, 88 % Commercial, 93 % Flight Instructor — the same
        // manoeuvres each time. (Via the aerobatic glider stage, flown in the Grob 103 Twin III Acro.)
        foreach (var (score, expect) in new[] { (72.0, CertLevel.StudentSolo), (80.0, CertLevel.Pilot),
                                               (85.0, CertLevel.Instrument), (90.0, CertLevel.Commercial),
                                               (95.0, CertLevel.FlightInstructor) })
        {
            PilotStanding s = RatingResolver.Resolve(Through(RatingBook.InstructorStage, score), Book);
            _o.WriteLine($"  everything at {score:F0} % -> {Certificate.Describe(s)}");
            Assert.Equal(expect, s.Level);
        }
    }

    [Fact]
    public void OnThisBuildTheLadderContinuesPastPilotViaTheGrob()
    {
        // Until 2026-11-01 the ladder stopped at Pilot: Blind Flying is gated behind the aerobatic glider stage and
        // both of its aeroplanes were placeholders. The Grob 103 Twin III Acro is built now, so the stage is flyable,
        // and passing it in the Grob is what opens Blind Flying.
        Stage acro = Book.Stage(RatingBook.AerobaticGlider)!;
        Assert.Equal(new[] { "glider-grob103-acro-like", "glider-dg-acro-like" }, acro.Aircraft);
        Assert.True(RatingResolver.Flyable(acro));
        Assert.True(Hangar.IsPickable("glider-grob103-acro-like"));
        Assert.False(Hangar.IsPickable("glider-dg-acro-like"));    // the DG is still a placeholder

        // Through Pilot at 80 %. The Swift is an aerobatic glider but not a Grob or a DG: the glider syllabus at
        // 99 % in it still leaves the aerobatic glider stage next.
        PilotLog log = Through(RatingBook.PilotStage, 80);
        Fly(log, "glider-swift-s1-like", RatingClass.Glider, RatingBook.GliderFull, 99);
        Assert.Equal(CertLevel.Pilot, RatingResolver.Resolve(log, Book).Level);
        Assert.Equal(new[] { RatingBook.AerobaticGlider }, RatingResolver.Next(log, Book).Select(n => n.stage.Id).ToArray());

        // The same syllabus flown in the Grob to the stage's 78 % passes it, and the next rung is Blind Flying (83 %).
        Fly(log, AcroGlider, RatingClass.Glider, RatingBook.GliderFull, 80);
        PilotStanding s = RatingResolver.Resolve(log, Book);
        _o.WriteLine(Certificate.Describe(s));
        Assert.Equal(CertLevel.Pilot, s.Level);    // the aerobatic glider stage is a gate, it grants no level itself
        Assert.Equal(new[] { RatingBook.InstrumentStage }, RatingResolver.Next(log, Book).Select(n => n.stage.Id).ToArray());
    }

    [Fact]
    public void BlindFlyingNeedsBothTheGliderAndTheAerobaticGliderStages()
    {
        Stage inst = Book.Stage(RatingBook.InstrumentStage)!;
        Assert.Equal(new[] { RatingBook.GliderSolo, RatingBook.AerobaticGlider }, inst.Requires);
        Assert.Equal(CertLevel.Instrument, inst.Grants);
        Assert.Equal("Blind Flying", Certificate.LevelName(CertLevel.Instrument));
        Assert.Equal("Blind Flying", inst.Title);
        // Flown to standard, with the aerobatic glider: Blind Flying.
        Assert.Equal(CertLevel.Instrument, RatingResolver.Resolve(Through(RatingBook.InstrumentStage, 85), Book).Level);
    }

    [Fact]
    public void AFlightInTheRetired737CountsForNothingOnTheLadder()
    {
        // Blind Flying and up take "any powered aeroplane" — but not a retired one.
        PilotLog log = Through(RatingBook.AerobaticGlider, 80);   // under the 83 % Blind Flying asks for
        Fly(log, "boeing-737-like", RatingClass.Amel, RatingBook.Full, 99);
        Assert.Equal(CertLevel.Pilot, RatingResolver.Resolve(log, Book).Level);
        Fly(log, "dc3-like", RatingClass.Amel, RatingBook.Full, 85);
        Assert.Equal(CertLevel.Instrument, RatingResolver.Resolve(log, Book).Level);
    }

    [Fact]
    public void AGliderHasItsOwnShorterListBecauseItCannotClimb()
    {
        Stage glider = Book.Stage(RatingBook.GliderSolo)!;
        Stage cub = Book.Stage(RatingBook.CubSolo)!;
        _o.WriteLine($"glider solo asks for {glider.Lessons.Length} lessons, Cub solo for {cub.Lessons.Length}");
        Assert.True(glider.Lessons.Length < cub.Lessons.Length);
        Assert.DoesNotContain("ClimbLevelDescend", glider.Lessons);
        Assert.DoesNotContain("ClimbVyRear", RatingBook.GliderFull);
        // And flying the whole glider list in a glider puts GLIDER on the badge.
        var log = new PilotLog();
        Fly(log, Glider, RatingClass.Glider, RatingBook.GliderFull, 80);
        PilotStanding s = RatingResolver.Resolve(log, Book);
        Assert.Equal(CertLevel.StudentSolo, s.Level);
        Assert.Equal("SOLO · GLIDER", Certificate.ShortBadge(s));
    }

    // ---------------------------------------------------------------- the class comes from the aeroplane

    [Theory]
    [InlineData("glider-2-33-like", 0, false, RatingClass.Glider)]
    [InlineData("c172-like", 0, false, RatingClass.Asel)]
    [InlineData("pa18-floats-like", 0, true, RatingClass.Ases)]
    [InlineData("seminole-like", 2, false, RatingClass.Amel)]
    [InlineData("aircam-like", 2, false, RatingClass.Amel)]
    [InlineData("aircam-amphib-like", 2, true, RatingClass.Ames)]
    [InlineData("hughes-h4-like", 8, true, RatingClass.Ames)]
    public void TheClassIsWorkedOutFromTheAeroplaneRatherThanAList(string id, int engines, bool floats, RatingClass expect)
    {
        var cfg = new AircraftConfig();
        for (int i = 0; i < engines; i++) cfg.Engines.Add(new EngineMount());
        if (floats) cfg.Floats = new FloatsConfig();
        RatingClass got = Certificate.ClassOf(id, cfg);
        _o.WriteLine($"{id,-22} {engines} engines, floats {floats} -> {got}");
        Assert.Equal(expect, got);
    }

    [Fact]
    public void FlyingTheSyllabusInTwoClassesPutsBothOnTheBadge()
    {
        // Which is exactly what happens to a real pilot who adds a seaplane rating.
        PilotLog log = Through(RatingBook.PilotStage, 90);
        Fly(log, "pa18-floats-like", RatingClass.Ases, RatingBook.Full, 80);
        PilotStanding s = RatingResolver.Resolve(log, Book);
        _o.WriteLine(Certificate.Describe(s));
        Assert.Equal(CertLevel.Pilot, s.Level);
        Assert.Equal(new HashSet<RatingClass> { RatingClass.Asel, RatingClass.Ases }, s.Classes);
        Assert.Equal("PILOT · ASEL ASES", Certificate.ShortBadge(s));
        Assert.Equal("Pilot · ASEL ASES", Certificate.Describe(s));
        // A landing flown on floats does nothing for the land rating, and vice versa.
        Assert.True(log.Best(RatingClass.Asel, "Flare") > 0);
        Assert.Equal(-1, log.Best(RatingClass.Amel, "Flare"));
    }

    [Fact]
    public void TheLevelLeadsTheTagAndTheClassesFollowOnlyIfTheyFitWhole()
    {
        var s = new PilotStanding { Level = CertLevel.Instrument };
        foreach (RatingClass c in Certificate.BadgeOrder) s.Classes.Add(c);
        string tag = Certificate.ShortBadge(s);
        _o.WriteLine($"'{tag}' ({tag.Length})");
        Assert.Equal("BLIND FLYING · GLIDER ASEL", tag);
        Assert.True(tag.Length <= 26);
        Assert.Equal("INSTRUCTOR", Certificate.ShortBadge(new PilotStanding { Level = CertLevel.FlightInstructor }));
    }

    [Fact]
    public void ABadDayDoesNotCostYouARating()
    {
        PilotLog log = Through(RatingBook.PilotStage, 80);
        Assert.Equal(CertLevel.Pilot, RatingResolver.Resolve(log, Book).Level);
        // Fly one of them badly afterwards. The log keeps the BEST, so the level stands.
        log.Record(Decathlon, RatingClass.Asel, "Flare", 3);
        Assert.Equal(CertLevel.Pilot, RatingResolver.Resolve(log, Book).Level);
    }

    [Fact]
    public void TheOwnersOwnScreenSaysWhatIsStillOutstanding()
    {
        PilotLog log = Through(RatingBook.GliderSolo, 75);
        var next = RatingResolver.Next(log, Book);
        foreach (var (st, missing) in next) _o.WriteLine($"next: {st.Id}; {missing.Count} lessons short");
        var (stage, miss) = Assert.Single(next);
        Assert.Equal(RatingBook.CubSolo, stage.Id);
        Assert.Equal(RatingBook.PreSolo.Length, miss.Count);
        Assert.Contains(miss, m => m.lesson == "ClimbLevelDescend" && m.have < 0 && m.need == 70);
        // After the Cub solo, the open stage is the Pilot stage, in the aeroplane of the player's choice.
        log = Through(RatingBook.CubSolo, 75);
        Fly(log, GreatLakes, RatingClass.Asel, new[] { "Straight" }, 80);
        var (pilot, short2) = Assert.Single(RatingResolver.Next(log, Book));
        Assert.Equal(RatingBook.PilotStage, pilot.Id);
        Assert.Equal(RatingBook.Full.Length - 1, short2.Count);   // the Great Lakes is closest: one lesson done
    }

    // ---------------------------------------------------------------- privacy

    /// <summary>
    /// THE PRIVACY LINE, checked against the type rather than trusted to a convention. The owner:
    /// *"nobody needs to know you took 100 hours to get a Pvt license in a glider or failed the checkride 20
    /// times."* So the public type carries a handle, a badge, awards and a score — all results — and nothing
    /// that could count attempts, hours or failures.
    /// </summary>
    [Fact]
    public void NothingAboutHowHardItWasEverLeavesTheDevice()
    {
        var bad = new List<string>();
        foreach (var f in typeof(PilotIdentity).GetFields(System.Reflection.BindingFlags.Public
                                                          | System.Reflection.BindingFlags.Instance))
        {
            string n = f.Name.ToLowerInvariant();
            string ty = f.FieldType.Name.ToLowerInvariant();
            _o.WriteLine($"  PilotIdentity.{f.Name} : {f.FieldType.Name}");
            if (n.Contains("log") || n.Contains("attempt") || n.Contains("hour") || n.Contains("fail")
                || n.Contains("lesson") || ty.Contains("pilotlog"))
                bad.Add($"{f.Name} ({f.FieldType.Name})");
        }
        Assert.True(bad.Count == 0,
                    "the public identity has grown something private: " + string.Join(", ", bad));
        // And the log cannot be reached from it at all.
        Assert.DoesNotContain(typeof(PilotLog), typeof(PilotIdentity)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => f.FieldType));
    }

    [Fact]
    public void TheHandleIsTheOneThingAViewerCannotTurnOff()
    {
        var id = new PilotIdentity { Handle = "Ty", Badge = "COMMERCIAL · ASEL", Score = 420, Awards = new[] { "SPORTSMAN" } };
        string[] shown = id.TagLines(true);
        string[] hidden = id.TagLines(false);
        _o.WriteLine("shown:  " + string.Join(" / ", shown));
        _o.WriteLine("hidden: " + string.Join(" / ", hidden));
        Assert.Contains("Ty", shown[0]);
        Assert.Single(hidden);
        Assert.Equal("Ty", hidden[0]);
        Assert.Contains("COMMERCIAL · ASEL", string.Join(" ", shown));
        Assert.Contains("SPORTSMAN", string.Join(" ", shown));
        Assert.DoesNotContain("ASEL", string.Join(" ", hidden));
    }

    [Fact]
    public void TheLogSurvivesARoundTripAndACorruptOneLosesNothingButRatings()
    {
        var log = new PilotLog();
        log.Record(RatingClass.Ases, "Flare", 87.5);
        log.Record(RatingClass.Glider, "Straight", 61.25);
        PilotLog back = PilotLog.Deserialize(log.Serialize());
        Assert.Equal(87.5, back.Best(RatingClass.Ases, "Flare"), 2);
        Assert.Equal(61.25, back.Best(RatingClass.Glider, "Straight"), 2);
        // Rubbish in, empty log out, no exception.
        Assert.Equal(-1, PilotLog.Deserialize("not a log at all;;;=;x=y").Best(RatingClass.Asel, "Flare"));
        Assert.Equal(-1, PilotLog.Deserialize(null).Best(RatingClass.Asel, "Flare"));
    }

    // ---------------------------------------------------------------- the score

    [Fact]
    public void TheScoreIsTheSumOfOneHundredOverYourRank()
    {
        // The owner's own two examples, verbatim: ten firsts make 1,000 and ten hundredths make 10.
        var tens = new List<BoardPlacing>();
        for (int i = 0; i < 10; i++) tens.Add(new BoardPlacing($"c{i}", "pitts-s2b-like", 1));
        Assert.Equal(1000, PilotScore.Combined(tens), 6);

        var lasts = new List<BoardPlacing>();
        for (int i = 0; i < 10; i++) lasts.Add(new BoardPlacing($"c{i}", "pitts-s2b-like", 100));
        Assert.Equal(10, PilotScore.Combined(lasts), 6);

        Assert.Equal(100, PilotScore.PointsForRank(1), 6);
        Assert.Equal(50, PilotScore.PointsForRank(2), 6);
        Assert.Equal(1, PilotScore.PointsForRank(100), 6);
        Assert.Equal(0, PilotScore.PointsForRank(101), 6);
        Assert.Equal(0, PilotScore.PointsForRank(0), 6);
        // Top-heavy on purpose: one board you own beats fifty you are merely respectable on.
        _o.WriteLine($"first {PilotScore.PointsForRank(1):F0}, tenth {PilotScore.PointsForRank(10):F0}, fiftieth {PilotScore.PointsForRank(50):F0}");
        // One board you own outright is worth fifty you are merely fiftieth on — exactly, as it happens.
        Assert.Equal(PilotScore.PointsForRank(1), 50 * PilotScore.PointsForRank(50), 6);
        Assert.True(PilotScore.PointsForRank(1) > 10 * PilotScore.PointsForRank(10) - 0.001);
    }

    [Fact]
    public void EveryBoardIsPerContestAndPerAircraftType()
    {
        // "a list for Pitts and a list for extra and a list for supercub"
        Assert.NotEqual(Contests.BoardKey("event:stol", "pa18-bush-like"),
                        Contests.BoardKey("event:stol", "c172-like"));
        Assert.NotEqual(Contests.BoardKey("event:race", "pitts-s2b-like"),
                        Contests.BoardKey("event:stol", "pitts-s2b-like"));
        // So being first in a Super Cub is worth exactly what being first in an Extra is.
        Assert.Equal(PilotScore.PointsForRank(1), PilotScore.PointsForRank(1), 6);
    }

    // ---------------------------------------------------------------- locked conditions

    /// <summary>
    /// A leaderboard of times flown in air each pilot chose for themselves is a list of who picked the best
    /// tailwind. So every scored contest pins the wind, the turbulence, the temperature, the thermals AND
    /// the tyres, and this walks the catalogue to make sure none of them was left open.
    /// </summary>
    [Fact]
    public void EveryScoredContestPinsEveryThingAPilotCouldTweak()
    {
        Assert.NotEmpty(Contests.All);
        foreach (string id in Contests.All)
        {
            ContestConditions c = Contests.For(id);
            _o.WriteLine($"  {id,-26} wind {c.WindFromDeg:000}/{c.WindSpeedMs:F1} turb {c.TurbulenceLevel} "
                         + $"isa {c.IsaDeviationC:+0;-0;0} thermals {c.ThermalScale:F2} "
                         + $"{(c.Bushwheels ? "bushwheels " : "")}{(c.Skis ? "skis" : "")}  [{c.Fingerprint()}]");
            Assert.InRange(c.WindFromDeg, 0, 360);
            Assert.InRange(c.WindSpeedMs, 0, 20);
            Assert.InRange(c.TurbulenceLevel, 0, 3);
            Assert.InRange(c.ThermalScale, 0, 2);
            Assert.False(c.Bushwheels && c.Skis, $"{id} asks for bushwheels AND skis");
        }
        // The STOL contest gets bushwheels, because every real contest aeroplane has them and a locked-off
        // field would be the odd choice. Everybody gets them, which is the only thing that matters.
        Assert.True(Contests.For("event:stol").Bushwheels);
        // The crosswind challenge keeps its crosswind: its whole point is the wind.
        Assert.True(Contests.For("a1c11-crosswind-landing").WindSpeedMs > 3);
        // And the aerobatic box is dead calm.
        Assert.Equal(0, Contests.For("event:aerobox").WindSpeedMs, 6);
        Assert.Equal(0, Contests.For("event:aerobox").TurbulenceLevel);
    }

    [Fact]
    public void AFingerprintChangesWhenAnythingAboutTheAirChanges()
    {
        ContestConditions calm = Contests.Calm;
        string baseline = calm.Fingerprint();
        _o.WriteLine("calm: " + baseline);
        Assert.NotEqual(baseline, new ContestConditions(90, 0, 0, 0, 0).Fingerprint());
        Assert.NotEqual(baseline, new ContestConditions(0, 5, 0, 0, 0).Fingerprint());
        Assert.NotEqual(baseline, new ContestConditions(0, 0, 2, 0, 0).Fingerprint());
        Assert.NotEqual(baseline, new ContestConditions(0, 0, 0, 15, 0).Fingerprint());
        Assert.NotEqual(baseline, new ContestConditions(0, 0, 0, 0, 1.5).Fingerprint());
        Assert.NotEqual(baseline, new ContestConditions(0, 0, 0, 0, 0, bushwheels: true).Fingerprint());
        Assert.NotEqual(baseline, new ContestConditions(0, 0, 0, 0, 0, skis: true).Fingerprint());
        // And it is stable: the same air gives the same string every time, or a board could never match it.
        Assert.Equal(baseline, Contests.Calm.Fingerprint());
    }

    [Fact]
    public void FreeFlightAndLessonsAreNotScored()
    {
        // A lesson is private and a free flight is nobody's business, so neither goes on a board — which is
        // also why neither has its conditions taken away from the pilot.
        Assert.False(Contests.IsScored(null));
        Assert.False(Contests.IsScored(""));
        Assert.False(Contests.IsScored("practice:flare"));
        Assert.False(Contests.IsScored("lesson:climb-vy"));
        Assert.True(Contests.IsScored("event:stol"));
    }

    [Fact]
    public void TheContestsThatAreDesignedButNotBuiltAreNamedRatherThanImplied()
    {
        // The IAC categories and the soaring tasks are each a feature in their own right; listing them keeps
        // the gap visible instead of letting it look like an oversight.
        Assert.NotEmpty(Contests.Planned);
        foreach (string id in Contests.Planned)
        {
            _o.WriteLine("  planned: " + id);
            Assert.False(Contests.IsScored(id), $"{id} is listed as planned but is already scored");
        }
        Assert.Contains("contest:iac-sportsman", Contests.Planned);
    }

    // ---------------------------------------------------------------- the awards

    [Fact]
    public void TheAerobaticBadgeIsTheIacLadderAtFivePointsAFigure()
    {
        var scores = new Dictionary<string, double>();
        Assert.Equal(-1, Achievements.AerobaticTier(scores));
        scores["Primary"] = 4.9;
        Assert.Equal(-1, Achievements.AerobaticTier(scores));      // 4.9 is not a pass
        scores["Primary"] = 5.0;
        Assert.Equal(0, Achievements.AerobaticTier(scores));
        scores["Sportsman"] = 6.2;
        Assert.Equal(1, Achievements.AerobaticTier(scores));
        Assert.Equal("SPORTSMAN", Achievements.Label(AchievementKind.Aerobatic, 1));
        // The ladder is the IAC's own.
        Assert.Equal(new[] { "Primary", "Sportsman", "Intermediate", "Advanced", "Unlimited" }, Achievements.IacCategories);
    }

    [Fact]
    public void ASoaringBadgeNeedsAllThreeLegsLikeAnFaiBadgeDoes()
    {
        // Height alone does not buy you a Silver, which is the whole character of the FAI scheme.
        Assert.Equal(-1, Achievements.SoaringTier(bestGainM: 5000, racesCompleted: 0, bestMinutesAloft: 0));
        Assert.Equal(0, Achievements.SoaringTier(400, 0, 12));
        Assert.Equal(1, Achievements.SoaringTier(1200, 1, 35));
        Assert.Equal(2, Achievements.SoaringTier(2600, 3, 65));
        Assert.Equal(3, Achievements.SoaringTier(4100, 6, 130));
        // One leg short of Diamond leaves you on Gold.
        Assert.Equal(2, Achievements.SoaringTier(4100, 3, 130));
        Assert.Equal("SILVER C", Achievements.Label(AchievementKind.Soaring, 1));
    }

    [Fact]
    public void TheStolBadgeIsTheSumOfTakeoffAndLandingAndLowerIsBetter()
    {
        Assert.Equal(-1, Achievements.StolTier(0));
        Assert.Equal(-1, Achievements.StolTier(1200));
        Assert.Equal(0, Achievements.StolTier(880));
        Assert.Equal(1, Achievements.StolTier(590));
        Assert.Equal(2, Achievements.StolTier(410));
        Assert.Equal(3, Achievements.StolTier(280));
        Assert.Equal("STOL MASTER", Achievements.Label(AchievementKind.Stol, 3));
    }

    [Fact]
    public void TheIdentityIsBuiltFromTheLogAndCarriesOnlyTheResult()
    {
        PilotLog log = Through(RatingBook.CommercialStage, 90);
        var placings = new[] { new BoardPlacing("event:stol", "pa18-bush-like", 1),
                               new BoardPlacing("event:race", "pitts-s2b-like", 4) };
        PilotIdentity id = PilotIdentity.From("Ty", log, Book, new[] { "SPORTSMAN", "STOL GOLD" }, placings);
        _o.WriteLine($"{id.Handle} | {id.Badge} | {string.Join(" ", id.Awards)} | {id.Score:F0}");
        Assert.Equal("Ty", id.Handle);
        Assert.Equal("COMMERCIAL · GLIDER ASEL", id.Badge);
        Assert.Equal(125, id.Score, 6);                 // 100 + 25
        Assert.Equal(2, id.Awards.Length);
        Assert.Equal("125", PilotScore.Format(id.Score));
        Assert.Equal("1.2k", PilotScore.Format(1234));
        Assert.Equal("", PilotScore.Format(0));
    }

    [Fact]
    public void AnEmptyLogIsAStudentAndNotAnError()
    {
        PilotStanding held = RatingResolver.Resolve(new PilotLog(), Book);
        Assert.Equal(CertLevel.None, held.Level);
        Assert.Empty(held.Stages);
        Assert.Equal("NO RATING", Certificate.Describe(held));
        Assert.Equal("STUDENT", Certificate.ShortBadge(held));
        PilotIdentity id = PilotIdentity.From("", new PilotLog(), Book,
                                              System.Array.Empty<string>(), System.Array.Empty<BoardPlacing>());
        Assert.Equal("Pilot", id.Handle);
        Assert.Equal(0, id.Score, 6);
        // Everybody starts as a student, so the tag reads handle then STUDENT — and with no score there is
        // no number beside it, because a nought would say the wrong thing about somebody who has not
        // entered a contest yet.
        string[] lines = id.TagLines(true);
        _o.WriteLine(string.Join(" / ", lines));
        Assert.Equal(2, lines.Length);
        Assert.Equal("Pilot", lines[0]);
        Assert.Equal("STUDENT", lines[1]);
        Assert.Single(id.TagLines(false));
    }

    /// <summary>
    /// THE THIRD SWITCH, and it is a different kind from the other two. Owner, 2026-10-18: *"show hide is
    /// both a declutter and a personal preference not to show your own badges and accomplishments. they
    /// would still be visible on the user's profile just not shown above their handle to others as they
    /// fly."*
    ///
    /// Two of the three switches are about the viewer's own screen. This one is about what LEAVES THE
    /// DEVICE, and the distinction is the whole value of it: "nobody can see it" has to mean nobody HAS it,
    /// or the setting is a request to other people's clients rather than a decision. So an unpublished
    /// badge is not sent — and because the net layer drops nulls, there is no field in the message at all.
    /// </summary>
    [Fact]
    public void AnUnpublishedBadgeIsNotSentRatherThanNotDrawn()
    {
        PilotLog log = Through(RatingBook.CommercialStage, 90);
        PilotIdentity full = PilotIdentity.From("Ty", log, Book, new[] { "SPORTSMAN" },
                                                new[] { new BoardPlacing("event:stol", "pa18-bush-like", 1) });
        Assert.Equal("COMMERCIAL · GLIDER ASEL", full.Badge);
        Assert.Equal(100, full.Score, 6);

        PilotIdentity published = full.Published(true);
        Assert.Equal("COMMERCIAL · GLIDER ASEL", published.Badge);
        Assert.Equal(100, published.Score, 6);
        Assert.Single(published.Awards);

        // Switched off: the handle survives and NOTHING else does.
        PilotIdentity withheld = full.Published(false);
        _o.WriteLine($"withheld -> handle '{withheld.Handle}', badge '{withheld.Badge}', "
                     + $"{withheld.Awards.Length} awards, score {withheld.Score:F0}");
        Assert.Equal("Ty", withheld.Handle);
        Assert.Equal("", withheld.Badge);
        Assert.Empty(withheld.Awards);
        Assert.Equal(0, withheld.Score, 6);
        // And the tag it makes is the handle alone, whatever the VIEWER has chosen.
        Assert.Equal(new[] { "Ty" }, withheld.TagLines(true));
        Assert.Equal(new[] { "Ty" }, withheld.TagLines(false));

        // The pilot's own copy is untouched: it is still on their profile.
        Assert.Equal("COMMERCIAL · GLIDER ASEL", full.Badge);
        Assert.Equal(100, full.Score, 6);
    }

    [Fact]
    public void TheHandleIsNotOnAnyOfTheThreeSwitches()
    {
        // Whichever way all three go, you always get to know who is in the circuit with you.
        var id = new PilotIdentity { Handle = "Ty", Badge = "INSTRUCTOR · ASEL", Score = 900 };
        foreach (bool publish in new[] { true, false })
            foreach (bool view in new[] { true, false })
            {
                string[] lines = id.Published(publish).TagLines(view);
                _o.WriteLine($"  publish {publish}, view {view} -> {string.Join(" / ", lines)}");
                Assert.Equal("Ty", lines[0]);
                Assert.True(lines.Length >= 1);
            }
    }
}
