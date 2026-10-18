using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Ratings;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The badge system: the rating ladder, the awards that are not licences, the leaderboard score, the air a
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

    /// <summary>Fly every lesson a step asks for, at a given score.</summary>
    private static void FlyAll(PilotLog log, RatingClass cls, CertLevel level, double score)
    {
        RatingStep step = Book.For(cls, level)!;
        foreach (string lesson in step.Lessons) log.Record(cls, lesson, score);
    }

    // ---------------------------------------------------------------- the ladder

    [Fact]
    public void FlyingSomethingMakesYouAPreSoloStudentAndNothingMore()
    {
        var log = new PilotLog();
        log.Record(RatingClass.Asel, "Straight", 95);
        var held = RatingResolver.Resolve(log, Book);
        _o.WriteLine(Certificate.Describe(held));
        Assert.Equal(CertLevel.StudentPreSolo, held[RatingClass.Asel]);
        Assert.Equal("ASEL STUDENT", Certificate.ShortBadge(held));
        // And only in that class: nothing flown in a glider, no glider badge.
        Assert.False(held.ContainsKey(RatingClass.Glider));
    }

    [Fact]
    public void YouCannotSkipARung()
    {
        // Fly the FULL syllabus to an ATP standard but leave ONE pre-solo lesson unflown: the ladder has a
        // missing rung, so there is no certificate above it however well everything else went.
        var log = new PilotLog();
        RatingStep atp = Book.For(RatingClass.Asel, CertLevel.Atp)!;
        foreach (string lesson in atp.Lessons)
            if (lesson != "LandingRudder") log.Record(RatingClass.Asel, lesson, 99);
        var held = RatingResolver.Resolve(log, Book);
        _o.WriteLine($"everything but one landing lesson, at 99 %: {Certificate.Describe(held)}");
        Assert.Equal(CertLevel.StudentPreSolo, held[RatingClass.Asel]);

        // Fly that one and the whole ladder arrives at once, up to the standard actually flown. (A score can
        // only ever go UP — the log keeps the best — so the way to test a missing rung is to leave it out,
        // not to fly it badly afterwards.)
        log.Record(RatingClass.Asel, "LandingRudder", 99);
        held = RatingResolver.Resolve(log, Book);
        _o.WriteLine($"with it flown to 99 %: {Certificate.Describe(held)}");
        Assert.Equal(CertLevel.Atp, held[RatingClass.Asel]);
    }

    [Fact]
    public void TheSameLessonsToATighterStandardIsWhatTheLadderIs()
    {
        // 78 % is a private pilot, 88 % is a commercial, 93 % is an ATP — the same manoeuvres each time.
        foreach (var (score, expect) in new[] { (72.0, CertLevel.StudentSolo), (80.0, CertLevel.Private),
                                               (90.0, CertLevel.Commercial), (95.0, CertLevel.Atp) })
        {
            var log = new PilotLog();
            FlyAll(log, RatingClass.Asel, CertLevel.Atp, score);
            var held = RatingResolver.Resolve(log, Book);
            _o.WriteLine($"  everything at {score:F0} % -> {Certificate.Describe(held)}");
            Assert.Equal(expect, held[RatingClass.Asel]);
        }
    }

    [Fact]
    public void AGliderHasItsOwnShorterListBecauseItCannotClimb()
    {
        RatingStep glider = Book.For(RatingClass.Glider, CertLevel.Private)!;
        RatingStep power = Book.For(RatingClass.Asel, CertLevel.Private)!;
        _o.WriteLine($"glider private asks for {glider.Lessons.Length} lessons, ASEL for {power.Lessons.Length}");
        Assert.True(glider.Lessons.Length < power.Lessons.Length);
        Assert.DoesNotContain("ClimbVyRear", glider.Lessons);
        Assert.DoesNotContain("ClimbLevelDescend", glider.Lessons);
        // And flying the glider list in a glider earns the glider badge.
        var log = new PilotLog();
        FlyAll(log, RatingClass.Glider, CertLevel.Private, 80);
        Assert.Equal("GLIDER PVT", Certificate.ShortBadge(RatingResolver.Resolve(log, Book)));
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
    public void FlyingTheSyllabusTwiceInTwoAeroplanesEarnsTwoBadges()
    {
        // Which is exactly what happens to a real pilot who adds a seaplane rating.
        var log = new PilotLog();
        FlyAll(log, RatingClass.Asel, CertLevel.Commercial, 90);
        FlyAll(log, RatingClass.Ases, CertLevel.Private, 80);
        FlyAll(log, RatingClass.Glider, CertLevel.StudentSolo, 75);
        var held = RatingResolver.Resolve(log, Book);
        _o.WriteLine(Certificate.Describe(held));
        Assert.Equal(CertLevel.Commercial, held[RatingClass.Asel]);
        Assert.Equal(CertLevel.Private, held[RatingClass.Ases]);
        Assert.Equal(CertLevel.StudentSolo, held[RatingClass.Glider]);
        // The badge leads with the highest and fits what it can into a tag.
        string shortBadge = Certificate.ShortBadge(held);
        _o.WriteLine($"short badge: '{shortBadge}' ({shortBadge.Length} chars)");
        Assert.StartsWith("ASEL COMM", shortBadge);
        Assert.True(shortBadge.Length <= 26);
        // A landing flown on floats does nothing for the land rating, and vice versa.
        Assert.True(log.Best(RatingClass.Asel, "Flare") > 0);
        Assert.Equal(-1, log.Best(RatingClass.Amel, "Flare"));
    }

    [Fact]
    public void AnAtpIsWrittenTheWayItIsSpoken()
    {
        var log = new PilotLog();
        FlyAll(log, RatingClass.Asel, CertLevel.Atp, 95);
        FlyAll(log, RatingClass.Amel, CertLevel.Atp, 95);
        string badge = Certificate.ShortBadge(RatingResolver.Resolve(log, Book));
        _o.WriteLine(badge);
        Assert.Equal("ATP ASEL AMEL", badge);
    }

    [Fact]
    public void ABadDayDoesNotCostYouARating()
    {
        var log = new PilotLog();
        FlyAll(log, RatingClass.Asel, CertLevel.Private, 80);
        Assert.Equal(CertLevel.Private, RatingResolver.Resolve(log, Book)[RatingClass.Asel]);
        // Fly one of them badly afterwards. The log keeps the BEST, so the certificate stands.
        log.Record(RatingClass.Asel, "Flare", 3);
        Assert.Equal(CertLevel.Private, RatingResolver.Resolve(log, Book)[RatingClass.Asel]);
    }

    [Fact]
    public void TheOwnersOwnScreenSaysWhatIsStillOutstanding()
    {
        var log = new PilotLog();
        FlyAll(log, RatingClass.Asel, CertLevel.StudentSolo, 75);
        var (next, missing) = RatingResolver.NextStep(log, Book, RatingClass.Asel);
        _o.WriteLine($"next: {next}; {missing.Count} lessons short");
        foreach (var m in missing.Take(4)) _o.WriteLine($"   {m.lesson,-20} need {m.need:F0}, best {m.have:F0}");
        Assert.Equal(CertLevel.Private, next);
        Assert.NotEmpty(missing);
        // The ones already flown to the private standard are not listed; the ones below it are.
        Assert.Contains(missing, m => m.lesson == "ClimbVyRear" && m.have < 0);
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
        var id = new PilotIdentity { Handle = "Ty", Badge = "ASEL COMM", Score = 420, Awards = new[] { "SPORTSMAN" } };
        string[] shown = id.TagLines(true);
        string[] hidden = id.TagLines(false);
        _o.WriteLine("shown:  " + string.Join(" / ", shown));
        _o.WriteLine("hidden: " + string.Join(" / ", hidden));
        Assert.Contains("Ty", shown[0]);
        Assert.Single(hidden);
        Assert.Equal("Ty", hidden[0]);
        Assert.Contains("ASEL COMM", string.Join(" ", shown));
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
        var log = new PilotLog();
        FlyAll(log, RatingClass.Asel, CertLevel.Commercial, 90);
        var placings = new[] { new BoardPlacing("event:stol", "pa18-bush-like", 1),
                               new BoardPlacing("event:race", "pitts-s2b-like", 4) };
        PilotIdentity id = PilotIdentity.From("Ty", log, Book, new[] { "SPORTSMAN", "STOL GOLD" }, placings);
        _o.WriteLine($"{id.Handle} | {id.Badge} | {string.Join(" ", id.Awards)} | {id.Score:F0}");
        Assert.Equal("Ty", id.Handle);
        Assert.Equal("ASEL COMM", id.Badge);
        Assert.Equal(125, id.Score, 6);                 // 100 + 25
        Assert.Equal(2, id.Awards.Length);
        Assert.Equal("125", PilotScore.Format(id.Score));
        Assert.Equal("1.2k", PilotScore.Format(1234));
        Assert.Equal("", PilotScore.Format(0));
    }

    [Fact]
    public void AnEmptyLogIsAStudentAndNotAnError()
    {
        var held = RatingResolver.Resolve(new PilotLog(), Book);
        Assert.Empty(held);
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
}
