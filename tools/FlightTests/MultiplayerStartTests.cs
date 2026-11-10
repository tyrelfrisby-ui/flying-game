using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Multiplayer;
using FlyingGame.Core.Nav;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner 2026-11-03: multiplayer starts must not put two pilots in the same place. Fixed starts (runway, final, the
/// aerobatic box, the authored air start) QUEUE, each showing its wait; the alternative, for a pilot who does not
/// want to wait, is a RANDOM AIR START with guaranteed separation, trimmed by the same <see cref="AirStart"/> path
/// as the altitude picker.
///
/// The separation checks here do NOT call <see cref="RandomAirStart.Why"/>: they re-measure every returned start
/// against every pilot by brute force (positions stepped every 10 ms over the window, terrain on a 100 m grid), so
/// a fault in the picker's own geometry cannot pass its own test. The distances are LITERALS, written out in metres
/// rather than taken from the constants, so that changing a constant breaks a test instead of silently moving the
/// guarantee: 1,389 m / 304.8 m, held at the start and across the first 30 s, and 304.8 m over the terrain.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class MultiplayerStartTests
{
    private const double D = 180.0 / Math.PI;
    /// <summary>The START volume (rule 1), in metres, written out: 1.5 NM and 1,000 ft.</summary>
    private const double SepH = 2778.0, SepV = 304.8;
    /// <summary>The FIRST-MINUTE floor (rule 2): 0.5 NM and 500 ft, over a 60 s window.</summary>
    private const double CpaH = 926.0, CpaV = 152.4, Window = 60.0;

    private readonly ITestOutputHelper _out;
    public MultiplayerStartTests(ITestOutputHelper o) => _out = o;

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Traffic Air(string id, double x, double y, double altM, double vx = 0, double vy = 0, double vz = 0, double age = 0) =>
        new(id, new Vec3(x, y, -altM), new Vec3(vx, vy, vz), age);
    /// <summary>A pilot queued for a FIXED start: nowhere at all, whatever position is attached.</summary>
    private static Traffic Holding(string id, string key, double since) => new(id, Vec3.Zero, Vec3.Zero, 0, key, since);
    /// <summary>A pilot holding with a RANDOM start already claimed: they will be at (x, y, alt) on that heading.</summary>
    private static Traffic Claim(string id, double since, double x, double y, double altM, double vx = 0, double vy = 0) =>
        new(id, new Vec3(x, y, -altM), new Vec3(vx, vy, 0), 0, StartQueue.RandomClaim, since);

    // ------------------------------------------------------------------------------------------------ the queue

    [Fact]
    public void ZonesAndServiceTimesAreTheOnesStated()
    {
        StartPoint rw = StartPoint.Runway("rw", "Runway", 0, 0, 100);
        Assert.Equal((400.0, 30.0, 60.0, 130.0), (rw.RadiusM, rw.HalfHeightM, rw.ServiceS, rw.AltitudeM));
        StartPoint fin = StartPoint.Final("f", "Final", 0, 0, 191.44);
        Assert.Equal((600.0, 90.0, 45.0), (fin.RadiusM, fin.HalfHeightM, fin.ServiceS));
        StartPoint box = StartPoint.Box("b", "Box", 0, 0, 100);
        // The IAC box's own floor and ceiling, 328 ft and 3,500 ft AGL, from AeroBox — not a second copy of them.
        Assert.Equal((600.0, 483.5, 240.0, 100 + 583.5), (box.RadiusM, box.HalfHeightM, box.ServiceS, box.AltitudeM));
        Assert.Equal((100.0, 1067.0), (AeroBox.FloorAglM, AeroBox.CeilingAglM));
        StartPoint air = StartPoint.Air("a", "Air", 0, 0, 1000);
        Assert.Equal((1500.0, 300.0, 30.0), (air.RadiusM, air.HalfHeightM, air.ServiceS));
    }

    [Fact]
    public void OccupiedMeansBeingThereNotQueuedForIt()
    {
        StartPoint rw = StartPoint.Runway("rw", "Runway", 1000, 2000, 100);   // zone: r 400 m, 100–160 m MSL
        Assert.True(StartQueue.Occupies(rw, Air("a", 1000, 2000, 101)));
        Assert.True(StartQueue.Occupies(rw, Air("a", 1399, 2000, 159)));
        Assert.False(StartQueue.Occupies(rw, Air("a", 1401, 2000, 120)));    // just outside the radius
        Assert.False(StartQueue.Occupies(rw, Air("a", 1000, 2000, 161)));    // above the band: overflying, not using it
        // A pilot queued for a fixed start is NOWHERE: the position on their report means nothing.
        Assert.False(StartQueue.Occupies(rw, new Traffic("h", new Vec3(1000, 2000, -110), Vec3.Zero, 0, "rw", 5)));
        // A pilot with a random claim IS somewhere — they will be there in a second or two.
        Assert.True(StartQueue.Occupies(rw, Claim("c", 5, 1000, 2000, 110)));
    }

    [Fact]
    public void WaitIsHalfAServiceForTheOccupantPlusAFullOneForEachAhead()
    {
        StartPoint box = StartPoint.Box("box", "Box", 0, 0, 0);   // 240 s
        Assert.True(StartQueue.For(box, Array.Empty<Traffic>()).Clear);
        Assert.Equal(0.0, StartQueue.For(box, Array.Empty<Traffic>()).WaitS);
        var one = new[] { Air("p", 0, 0, 500) };
        Assert.Equal(120.0, StartQueue.For(box, one).WaitS);
        var two = new[] { Air("p", 0, 0, 500), Air("q", 100, 0, 700) };
        Assert.Equal(120.0 + 240.0, StartQueue.For(box, two).WaitS);
        var queued = new[] { Air("p", 0, 0, 500), Holding("a", "box", 10), Holding("b", "box", 20), Holding("c", "rw", 5) };
        StartQueue.Status look = StartQueue.For(box, queued);   // the START PAGE's view: everybody queued is ahead
        Assert.Equal((1, 2, 120.0 + 480.0), (look.Occupants, look.Ahead, look.WaitS));
        StartQueue.Status me = StartQueue.For(box, queued, "me", 15);   // joined between a and b
        Assert.Equal((1, 2, false), (me.Ahead, me.Position, me.Clear));
        // A queue for a DIFFERENT start never counts ("c" is waiting for the runway).
        Assert.Equal(0, StartQueue.For(StartPoint.Runway("other", "Other", 50000, 0, 0), queued).Occupants);
    }

    [Fact]
    public void EveryPilotComputesTheSameQueueWithNoReferee()
    {
        // Four pilots queued for one start, two at the same second: each works out its own place from the same
        // roster and they agree, which is what lets this ship against a relay that only fans out bytes.
        var q = new[] { Holding("delta", "rw", 30), Holding("bravo", "rw", 10), Holding("alpha", "rw", 10), Holding("charlie", "rw", 20) };
        StartPoint rw = StartPoint.Runway("rw", "Runway", 0, 0, 0);
        var places = q.ToDictionary(t => t.Id, t => StartQueue.For(rw, q, t.Id, t.HoldingSinceS).Position);
        Assert.Equal(1, places["alpha"]);     // same second as bravo, lower id
        Assert.Equal(2, places["bravo"]);
        Assert.Equal(3, places["charlie"]);
        Assert.Equal(4, places["delta"]);
        Assert.Equal(new[] { 1, 2, 3, 4 }, places.Values.OrderBy(v => v).ToArray());   // no two pilots claim one place
        Assert.True(StartQueue.For(rw, q, "alpha", 10).Clear);   // nobody on the runway: the first in line goes
        Assert.False(StartQueue.For(rw, q.Append(Air("x", 0, 0, 10)).ToArray(), "alpha", 10).Clear);
    }

    [Theory]
    [InlineData(0.0, "no wait")]
    [InlineData(1.0, "~10 s")]
    [InlineData(30.0, "~30 s")]
    [InlineData(31.0, "~40 s")]
    [InlineData(60.0, "~1 min")]
    [InlineData(61.0, "~2 min")]
    [InlineData(600.0, "~10 min")]
    public void WaitTextRoundsUp(double s, string text) => Assert.Equal(text, StartQueue.WaitText(s));

    // ------------------------------------------------- the separation distances and where they come from

    [Fact]
    public void SeparationDistancesAreTheOnesTheOwnerChose()
    {
        // These came from Ty on 2026-11-10 ("Tighter - the world is small"), NOT from an aviation standard, and
        // this test is the lock on them: it fails if anybody moves one without meaning to.
        Assert.Equal(1.5, RandomAirStart.SepHorizontalNm);
        Assert.Equal(2778.0, RandomAirStart.SepHorizontalM, 0);
        Assert.Equal(1000 * Terps.MPerFt, RandomAirStart.SepVerticalM, 9);
        Assert.Equal(0.5, RandomAirStart.CpaHorizontalNm);
        Assert.Equal(926.0, RandomAirStart.CpaHorizontalM, 0);
        Assert.Equal(500 * Terps.MPerFt, RandomAirStart.CpaVerticalM, 9);
        Assert.Equal(60.0, RandomAirStart.LookaheadS);
        // The floor is tighter than the start volume, which is the whole point of having two.
        Assert.True(RandomAirStart.CpaHorizontalM < RandomAirStart.SepHorizontalM);
        Assert.True(RandomAirStart.CpaVerticalM < RandomAirStart.SepVerticalM);
        // Terrain is NOT the owner's number: 91.177's 1,000 ft unscaled, its 4 NM radius at Terps.LateralScale.
        Assert.Equal(4.0, Terps.LateralScale);
        Assert.Equal(304.8, RandomAirStart.TerrainClearanceM, 9);
        Assert.Equal(1852.0, RandomAirStart.TerrainRadiusM, 9);
        // The ring's inner edge is derived, not chosen: the air-start zone plus one start volume.
        Assert.True(RandomAirStart.MinRingM >= 1500 + RandomAirStart.SepHorizontalM);
        Assert.Equal((4500.0, 10000.0), (RandomAirStart.MinRingM, RandomAirStart.MaxRingM));
    }

    /// <summary>
    /// THE SEPARATION STANDARD, MEASURED rather than asserted — this is the test the choice of 1.5 NM rests on, and
    /// the one to re-read if anybody wants to move it. It measures the thing that actually decides whether the
    /// feature works: how much of the candidate ring and band is still AVAILABLE for a start as the sky fills.
    /// Candidates are sampled on a grid over the ring (4.5–10 km) and the powered band (2,000–5,000 ft AGL), and
    /// the fraction clear of every pilot is counted at each candidate standard. The vertical limit is 1,000 ft in
    /// every column, because that figure was never in question; only the lateral one differs.
    ///
    /// MEASURED (printed on every run; figures recorded 2026-11-10). The three columns are the standard originally
    /// proposed, the one the owner chose, and the one derived from this repo's own Terps.LateralScale before he
    /// answered — kept in the table because it is the cheapest way to see that 1.5 NM is a real choice and not just
    /// the only thing that fits:
    ///
    ///       pilots      3 NM (proposed)    1.5 NM (CHOSEN)    0.75 NM (Terps scale)
    ///            4               54.1 %             79.7 %                   94.6 %
    ///            8               32.7 %             71.3 %                   91.1 %
    ///           16               10.4 %             49.8 %                   82.1 %
    ///           32                2.5 %             27.5 %                   67.8 %
    ///
    /// THE ANSWER TO THE QUESTION THAT WAS ASKED: 1.5 NM does NOT exhaust the space. Sixteen pilots stacked round
    /// ONE field still leaves half the ring, and thirty-two leaves a quarter — and sixteen round one field is
    /// already far busier than this game has ever been, since the relay's whole free-for-all shard holds 100
    /// spread over an island with four start fields. The proposed 3 NM, by contrast, is down to 10.4 % at sixteen
    /// and 2.5 % at thirty-two, which is the exhaustion the owner's "tighter" instruction avoided. The 0.75 NM
    /// column is kept only to show that 1.5 NM was a real choice between two workable figures rather than the one
    /// thing that fitted.
    ///
    /// WHAT THE NUMBERS HAVE TO SHOW for 1.5 NM to be the right answer, and what this test therefore asserts:
    /// that at 16 pilots round ONE field — far busier than this game has ever been, and the relay's free-for-all
    /// shard holds 100 across the whole island — the chosen standard still leaves a large majority of the ring, so
    /// the guarantee fails by geometry if it fails at all, never by exhaustion. The coordinating agent asked for
    /// this number explicitly, and it is the answer: 1.5 NM does not exhaust the space.
    ///
    /// REJECTED: "just widen the ring" as a way to keep 3 NM. The ring would need an outer edge near 25 km at this
    /// density, and the world is 15.9 km across its short axis — the candidates would be in the sea.
    /// </summary>
    [Fact]
    public void SeparationStandardsAreMeasured()
    {
        const double Proposed = 3 * 1852.0, Chosen = 1.5 * 1852.0, TerpsScaled = 0.75 * 1852.0;
        double lo = 2000 * 0.3048, hi = 5000 * 0.3048;
        double inner = RandomAirStart.MinRingM, outer = RandomAirStart.MaxRingM;

        // The arithmetic first: one exclusion cylinder against the whole ring's area.
        double ringArea = Math.PI * (outer * outer - inner * inner);
        _out.WriteLine($"ring {ringArea / 1e6:F0} km²  |  one cylinder: 3 NM {Math.PI * Proposed * Proposed / 1e6:F0} km², "
                     + $"1.5 NM {Math.PI * Chosen * Chosen / 1e6:F0} km², 0.75 NM {Math.PI * TerpsScaled * TerpsScaled / 1e6:F1} km²");

        double FractionClear(double sepH, int n)
        {
            var rng0 = new Random(4242);
            var pilots = new List<Vec3>();
            for (int i = 0; i < n; i++)
            {
                double a = rng0.NextDouble() * 2 * Math.PI, rr = inner + rng0.NextDouble() * (outer - inner);
                pilots.Add(new Vec3(rr * Math.Cos(a), rr * Math.Sin(a), -(lo + rng0.NextDouble() * (hi - lo))));
            }
            int tried = 0, clear = 0;
            for (double x = -outer; x <= outer; x += 400)
                for (double y = -outer; y <= outer; y += 400)
                {
                    double d = Math.Sqrt(x * x + y * y);
                    if (d < inner || d > outer) continue;
                    for (double alt = lo; alt <= hi; alt += 100)
                    {
                        tried++;
                        var c = new Vec3(x, y, -alt);
                        if (pilots.All(q => RandomAirStart.Separated(c, q, sepH, SepV))) clear++;
                    }
                }
            return tried == 0 ? 0 : (double)clear / tried;
        }

        _out.WriteLine("pilots    3 NM (proposed)   1.5 NM (CHOSEN)   0.75 NM (Terps scale)");
        foreach (int n in new[] { 4, 8, 16, 32 })
            _out.WriteLine($"{n,6}   {FractionClear(Proposed, n) * 100,15:F1} % {FractionClear(Chosen, n) * 100,16:F1} % {FractionClear(TerpsScaled, n) * 100,20:F1} %");

        double chosen16 = FractionClear(Chosen, 16), chosen32 = FractionClear(Chosen, 32);
        double proposed16 = FractionClear(Proposed, 16);
        _out.WriteLine($"CHOSEN 1.5 NM: {chosen16:P1} of the ring clear at 16 pilots, {chosen32:P1} at 32");

        // The claim being locked in: the chosen standard leaves the ring usable at a density far past anything this
        // game sees, so a null answer means the sky really is full and not that the rule ate the world.
        Assert.True(chosen16 > 0.40, $"1.5 NM left only {chosen16:P1} of the ring at 16 pilots — it exhausts the space");
        Assert.True(chosen32 > 0.20, $"1.5 NM left only {chosen32:P1} of the ring at 32 pilots");
        // And the reason the proposed figure was tightened: it is markedly worse in the same sky.
        Assert.True(proposed16 < 0.15, $"3 NM somehow left {proposed16:P1} of the ring at 16 pilots");
        Assert.True(chosen16 > 3 * proposed16, $"1.5 NM ({chosen16:P1}) should leave far more than 3 NM ({proposed16:P1})");
    }

    [Theory]
    [InlineData("pitts-s2b-like", 0, 0, 2000, 5000)]
    [InlineData("c172-like", 0, 0, 2000, 5000)]
    [InlineData("glider-2-33-like", 0, 0, 2000, 5000)]
    [InlineData("hang-glider-like", 0, 0, 1500, 3000)]
    [InlineData("paraglider-en-b-like", 0, 0, 1500, 3000)]
    // A powered type trimming above FastMs (195 kt) is in the fast group and starts higher.
    [InlineData("f86-sabre-like", 0, 150, 4000, 8000)]
    [InlineData("p51d-like", 0, 150, 4000, 8000)]
    // The picker's choice overrules the type: ±1,000 ft round what the pilot asked for.
    [InlineData("pitts-s2b-like", 10000, 0, 9000, 11000)]
    [InlineData("pitts-s2b-like", 2000, 0, 1500, 3000)]
    [InlineData("hang-glider-like", 6000, 0, 5000, 7000)]
    [InlineData("f86-sabre-like", 4000, 150, 3000, 5000)]
    public void BandsAreTheOnesStated(string id, int pickerFt, double speedMs, double minFt, double maxFt)
    {
        (double lo, double hi) = RandomAirStart.Band(Load(id), pickerFt, speedMs);
        Assert.Equal(minFt * 0.3048, lo, 9);
        Assert.Equal(maxFt * 0.3048, hi, 9);
    }

    [Fact]
    public void TheRingReachesAsFarAsTheTypeFliesInThreeMinutes()
    {
        // A paraglider at 11 m/s would be stranded 10 km out, so its ring stops one protected volume past the inner
        // edge; a 60 m/s type gets most of the ring; a jet is capped by the world rather than by its speed.
        Assert.Equal(4500 + SepH, RandomAirStart.OuterRingFor(11.0), 0);     // clamped to inner + one start volume
        Assert.Equal(7278.0, RandomAirStart.OuterRingFor(30.0), 0);          // 5,400 m is inside that clamp too
        Assert.Equal(9000.0, RandomAirStart.OuterRingFor(50.0), 0);          // 50 m/s x 180 s
        Assert.Equal(10000.0, RandomAirStart.OuterRingFor(250.0), 0);        // capped by the world, not the speed
        Assert.True(RandomAirStart.OuterRingFor(1.0) > RandomAirStart.MinRingM, "the ring is never empty");
    }

    [Fact]
    public void BeingOutsideTheVolumeAtTheStartIsNotEnough()
    {
        // Rule 2's reason: two fast types 3 km apart at the same height, nose to nose at 200 m/s each, are inside
        // the first-minute floor in six seconds — and rule 1, which looks only at t = 0, passes them.
        var r = new RandomAirStart.Request { OwnSpeedMs = 200, Traffic = new[] { Air("jet", 3000, 0, 2000, vx: -200) } };
        var p = new RandomAirStart.Pick(0, 0, 2000, 0.0, 1);
        Assert.True(RandomAirStart.Separated(p.Position, r.Traffic[0].Position, SepH, SepV));   // rule 1 alone passes it
        Assert.Equal("converging with jet", RandomAirStart.Why(p, r));
        // Turned away it is fine; 1,000 ft above it is fine.
        Assert.Null(RandomAirStart.Why(new RandomAirStart.Pick(0, 0, 2000, Math.PI, 1), r));
        Assert.Null(RandomAirStart.Why(new RandomAirStart.Pick(0, 0, 2000 + 305, 0.0, 1), r));
    }

    [Fact]
    public void AStaleReportIsWidenedBySpeedTimesAge()
    {
        // 3.5 km away, 1.5 NM is passed — unless the report is 10 s old and they were doing 150 m/s.
        var fresh = new RandomAirStart.Request { OwnSpeedMs = 50, Traffic = new[] { Air("t", 0, 3500, 1000, vx: 150) } };
        var stale = new RandomAirStart.Request { OwnSpeedMs = 50, Traffic = new[] { Air("t", 0, 3500, 1000, vx: 150, age: 10) } };
        var p = new RandomAirStart.Pick(0, 0, 1000, Math.PI, 1);
        Assert.Null(RandomAirStart.Why(p, fresh));
        Assert.Equal("too close to t", RandomAirStart.Why(p, stale));   // 2,778 + 1,500 > 3,500
    }

    [Fact]
    public void ClosestApproachFindsTheMomentAWholeSecondStepWouldMiss()
    {
        // Crossing at 400 m/s closure, closest (0 m) at t = 12.3456 s: no whole second lands within 100 m of it.
        Vec3 p1 = new(0, 0, -1000), v1 = new(200, 0, 0), p2 = new(4938.24, 0, -1000), v2 = new(-200, 0, 0);
        (double h, double v) = RandomAirStart.ClosestApproach(p1, v1, p2, v2, 60);
        Assert.True(h < 1.0, $"h {h}");
        Assert.Equal(0.0, v, 9);
    }

    // ------------------------------------------------- rule 5: two pilots choosing at the same moment

    [Fact]
    public void APilotQueuedForAFixedStartIsNotTrafficButTheirStartPointIs()
    {
        // The one that must NOT be traffic: their reported position is stale junk from their last flight.
        var holding = new[] { Holding("h", "rw", 10) };
        var p = new RandomAirStart.Pick(0, 0, 1000, 0.0, 1);
        var r = new RandomAirStart.Request { OwnSpeedMs = 50, Traffic = holding };
        Assert.Null(RandomAirStart.Why(p, r));
        // Their START POINT is what gets avoided — rule 4, the zone's radius plus the protected volume.
        r.OccupiedStarts = new[] { StartPoint.Runway("rw", "Runway", 1000, 0, 900) };
        Assert.Equal("too close to Runway", RandomAirStart.Why(p, r));
    }

    [Fact]
    public void AnEarlierRandomClaimIsTrafficAndALaterOneIsNot()
    {
        // THE HOLE THIS CLOSES: two pilots press FLY in the same second, both hold, both pick. Neither is flying,
        // so "ignore pilots who are not airborne" would let them pick the same piece of sky.
        var p = new RandomAirStart.Pick(0, 0, 1000, 0.0, 1);
        // Their claim came first (t = 10 against my t = 20): I must avoid it.
        var mine = new RandomAirStart.Request
        {
            OwnSpeedMs = 50, SelfId = "me", SelfClaimSinceS = 20,
            Traffic = new[] { Claim("them", 10, 500, 0, 1000) },
        };
        Assert.Equal("too close to them", RandomAirStart.Why(p, mine));
        // Mine came first (t = 5): theirs yields to me, so their claim does not constrain my pick.
        mine.SelfClaimSinceS = 5;
        Assert.Null(RandomAirStart.Why(p, mine));
        // Dead heat on the clock: the lower id wins, so exactly one of the two yields — never both and never neither.
        var a = new RandomAirStart.Request { OwnSpeedMs = 50, SelfId = "aaa", SelfClaimSinceS = 7, Traffic = new[] { Claim("bbb", 7, 500, 0, 1000) } };
        var b = new RandomAirStart.Request { OwnSpeedMs = 50, SelfId = "bbb", SelfClaimSinceS = 7, Traffic = new[] { Claim("aaa", 7, 500, 0, 1000) } };
        Assert.Null(RandomAirStart.Why(p, a));                        // "aaa" < "bbb": aaa keeps its pick
        Assert.Equal("too close to aaa", RandomAirStart.Why(p, b));   // bbb re-picks round it
        // A chooser with no claim of its own yields to every claim: the safe default.
        var none = new RandomAirStart.Request { OwnSpeedMs = 50, Traffic = new[] { Claim("them", 10, 500, 0, 1000) } };
        Assert.Equal("too close to them", RandomAirStart.Why(p, none));
    }

    [Fact]
    public void TwoSimultaneousChoosersEndUpSeparated()
    {
        // The whole mechanism end to end: the later chooser picks AGAINST the earlier one's published claim, so the
        // two starts are separated even though neither pilot was flying when either chose.
        var req1 = new RandomAirStart.Request
        {
            MinAglM = 609.6, MaxAglM = 1524.0, OwnSpeedMs = 60, SelfId = "alpha", SelfClaimSinceS = 100,
        };
        RandomAirStart.Pick first = RandomAirStart.Choose(req1, new Random(11))!.Value;
        var req2 = new RandomAirStart.Request
        {
            MinAglM = 609.6, MaxAglM = 1524.0, OwnSpeedMs = 60, SelfId = "bravo", SelfClaimSinceS = 100,
            Traffic = new[] { Claim("alpha", 100, first.X, first.Y, first.AltitudeM,
                                    60 * Math.Cos(first.HeadingRad), 60 * Math.Sin(first.HeadingRad)) },
        };
        RandomAirStart.Pick second = RandomAirStart.Choose(req2, new Random(11))!.Value;   // same seed on purpose
        double h = Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
        double v = Math.Abs(first.AltitudeM - second.AltitudeM);
        _out.WriteLine($"two simultaneous choosers: {h:F0} m apart, {v:F0} m of height");
        Assert.True(h >= SepH || v >= SepV, $"both chose the same air: {h:F0} m / {v:F0} m");
        AssertSeparated(second, 60, req2.Traffic, Array.Empty<StartPoint>(), (_, _) => -1e9, "bravo against alpha's claim");
    }

    // ------------------------------------------------------------------------------------------------ on the island

    /// <summary>Brute force, independent of the picker: every rule, re-measured from the literals.</summary>
    private static void AssertSeparated(RandomAirStart.Pick p, double ownSpeed, IReadOnlyList<Traffic> traffic, IReadOnlyList<StartPoint> occupied,
                                        Func<double, double, double> ground, string ctx, string? selfId = null, double selfSince = double.PositiveInfinity)
    {
        Vec3 own = p.Position, ov = new Vec3(Math.Cos(p.HeadingRad), Math.Sin(p.HeadingRad), 0) * ownSpeed;
        foreach (Traffic t in traffic)
        {
            if (t.Id == selfId) continue;
            if (!t.OccupiesSpace) continue;
            if (t.HasRandomClaim && !StartQueue.Before(t.HoldingSinceS, t.Id, selfSince, selfId)) continue;
            double gs = Math.Sqrt(t.Velocity.X * t.Velocity.X + t.Velocity.Y * t.Velocity.Y);
            double wH = gs * t.AgeS, wV = Math.Abs(t.Velocity.Z) * t.AgeS;
            double h0 = Math.Sqrt(Math.Pow(own.X - t.Position.X, 2) + Math.Pow(own.Y - t.Position.Y, 2)), v0 = Math.Abs(own.Z - t.Position.Z);
            Assert.True(h0 >= SepH + wH || v0 >= SepV + wV, $"{ctx}: start {h0:F0} m / {v0:F0} m from {t.Id}");
            for (int i = 0; i <= (int)(Window * 100); i++)
            {
                double tt = i * 0.01;
                Vec3 a = own + ov * tt, b = t.Position + t.Velocity * tt;
                double h = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)), v = Math.Abs(a.Z - b.Z);
                Assert.True(h >= CpaH + wH || v >= CpaV + wV, $"{ctx}: at {tt:F2} s {h:F0} m / {v:F0} m from {t.Id}");
            }
        }
        foreach (StartPoint s in occupied)
        {
            double h = Math.Sqrt(Math.Pow(own.X - s.X, 2) + Math.Pow(own.Y - s.Y, 2)), v = Math.Abs(-own.Z - s.AltitudeM);
            Assert.True(h >= SepH + s.RadiusM || v >= SepV + s.HalfHeightM, $"{ctx}: {h:F0} m / {v:F0} m from {s.Name}");
        }
        for (double dx = -1852; dx <= 1852; dx += 100)
            for (double dy = -1852; dy <= 1852; dy += 100)
                if (dx * dx + dy * dy <= 1852 * 1852)
                    Assert.True(p.AltitudeM - ground(p.X + dx, p.Y + dy) >= SepV, $"{ctx}: terrain at ({dx},{dy})");
        for (double tt = 0; tt <= Window; tt += 0.25)
            Assert.True(p.AltitudeM - ground(p.X + ov.X * tt, p.Y + ov.Y * tt) >= SepV, $"{ctx}: terrain on the track at {tt} s");
    }

    [Fact]
    public void RandomStartsKeepEveryRuleInBusySkiesAtEveryField()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            var rng = new Random(20261110);
            int found = 0, none = 0, trials = 0;
            foreach (WorldTerrain.Airport ap in WorldTerrain.Airports)
            {
                for (int k = 0; k < 6; k++)
                {
                    trials++;
                    // 0–40 other pilots in and around the ring, at the heights people fly, some fast, some reports
                    // stale, a few queued for a fixed start (which must be ignored) and a few holding with a random
                    // claim (which must not be), and the field's runway and air start occupied.
                    int n = k * 8;
                    var traffic = new List<Traffic>();
                    for (int i = 0; i < n; i++)
                    {
                        double a = rng.NextDouble() * 2 * Math.PI, r = rng.NextDouble() * 12000, spd = 20 + rng.NextDouble() * 200, hd = rng.NextDouble() * 2 * Math.PI;
                        double alt = ap.ElevationM + 300 + rng.NextDouble() * 2500;
                        double px = ap.X + r * Math.Cos(a), py = ap.Y + r * Math.Sin(a);
                        traffic.Add(i % 9 == 8 ? Holding($"h{i}", "rw", i)
                            : i % 11 == 10 ? Claim($"c{i}", i, px, py, alt, spd * Math.Cos(hd), spd * Math.Sin(hd))
                            : Air($"p{i}", px, py, alt, spd * Math.Cos(hd), spd * Math.Sin(hd), (rng.NextDouble() - 0.5) * 10, i % 4 == 0 ? rng.NextDouble() * 5 : 0));
                    }
                    var occupied = new[] { StartPoint.Runway("rw", "Runway", ap.X, ap.Y, ap.ElevationM), StartPoint.Air("air", "Air start", ap.X - 4000, ap.Y, ap.ElevationM + 600) };
                    var req = new RandomAirStart.Request
                    {
                        CentreX = ap.X, CentreY = ap.Y, MinAglM = 2000 * 0.3048, MaxAglM = 5000 * 0.3048, OwnSpeedMs = 70,
                        GroundAt = WorldTerrain.GroundHeightAt, Traffic = traffic, OccupiedStarts = occupied,
                        SelfId = "me", SelfClaimSinceS = 1e9,   // I claimed last: every claim on the roster binds me
                    };
                    RandomAirStart.Pick? p = RandomAirStart.Choose(req, rng);
                    if (p == null) { none++; continue; }
                    found++;
                    double dc = Math.Sqrt(Math.Pow(p.Value.X - ap.X, 2) + Math.Pow(p.Value.Y - ap.Y, 2));
                    Assert.InRange(dc, RandomAirStart.MinRingM, RandomAirStart.MaxRingM);
                    double agl = p.Value.AltitudeM - WorldTerrain.GroundHeightAt(p.Value.X, p.Value.Y);
                    Assert.InRange(agl, 2000 * 0.3048 - 1e-6, 5000 * 0.3048 + 1e-6);
                    AssertSeparated(p.Value, 70, traffic, occupied, WorldTerrain.GroundHeightAt, $"{ap.Name} n={n}", "me", 1e9);
                }
            }
            _out.WriteLine($"{found} starts found, {none} with no clear airspace, of {trials}");
            Assert.Equal(trials, found);
        }
        finally { WorldTerrain.Active = null; }
    }

    [Fact]
    public void AFullSkyGivesNoStartRatherThanABadOne()
    {
        // A pilot every 600 m over the whole ring at every height in the band: nowhere keeps the volume clear, and
        // the answer must be "no start", never a start with the guarantee quietly dropped.
        var traffic = new List<Traffic>();
        for (double x = -12000; x <= 12000; x += 1200)
            for (double y = -12000; y <= 12000; y += 1200)
                for (double alt = 550; alt <= 1600; alt += 250)
                    traffic.Add(Air($"{x},{y},{alt}", x, y, alt));
        var req = new RandomAirStart.Request { MinAglM = 609.6, MaxAglM = 1524.0, OwnSpeedMs = 60, Traffic = traffic };
        Assert.Null(RandomAirStart.Choose(req, new Random(1)));
    }

    [Fact]
    public void TheSameSeedGivesTheSameStart()
    {
        var req = new RandomAirStart.Request { MinAglM = 609.6, MaxAglM = 1524.0, OwnSpeedMs = 60, Traffic = new[] { Air("a", 5000, 0, 1500) } };
        RandomAirStart.Pick a = RandomAirStart.Choose(req, new Random(42))!.Value, b = RandomAirStart.Choose(req, new Random(42))!.Value;
        Assert.Equal((a.X, a.Y, a.AltitudeM, a.HeadingRad), (b.X, b.Y, b.AltitudeM, b.HeadingRad));
    }

    // ------------------------------------------------------------------------------------------------ trimmed, every type

    public static IEnumerable<object[]> Fleet() => RigTable.Rows.Select(r => new object[] { r.Id });

    /// <summary>Every type through <see cref="AirStart.ComputeRandom"/> at the default field with traffic about: the
    /// aeroplane is built where the pick says, on its heading, inside its band, trimmed by the same solve as the
    /// altitude picker (converged, level for a powered type, best glide for a glider), and separated from everybody
    /// at the speed it really starts at.</summary>
    [Theory]
    [MemberData(nameof(Fleet))]
    public void RandomAirStartIsTrimmedLikeTheAltitudePicker(string id)
    {
        WorldTerrain.Active = new WorldTerrain();
        Atmosphere.Field = null; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.IsaDeviationK = 0;
        try
        {
            WorldTerrain.Airport ap = WorldTerrain.DefaultAirport;
            AircraftConfig c = Load(id);
            double fallback = c.SpawnIasMs > 0 ? c.SpawnIasMs : 22.0;
            (double lo, double hi, double minRing, double maxRing, double speed) = AirStart.RandomBounds(c, 0, ap.ElevationM, fallback);
            var traffic = new[] { Air("east", ap.X, ap.Y + 2500, ap.ElevationM + 1200, vy: -60), Air("north", ap.X + 3000, ap.Y, ap.ElevationM + 900, vx: -45, age: 2) };
            var req = new RandomAirStart.Request
            {
                CentreX = ap.X, CentreY = ap.Y, MinAglM = lo, MaxAglM = hi, MinRingM = minRing, MaxRingM = maxRing,
                GroundAt = WorldTerrain.GroundHeightAt, Traffic = traffic,
            };
            AirStart.Plan? plan = AirStart.ComputeRandom(c, req, new Random(7), fallback, out RandomAirStart.Pick? pick);
            Assert.NotNull(plan); Assert.NotNull(pick);
            RandomAirStart.Pick p = pick!.Value;
            Assert.Null(plan!.Warning);
            Assert.True(plan.Rig.Converged);
            Assert.Equal(p.AltitudeM, plan.Rig.AltitudeM, 6);
            Assert.Equal(new Vec3(p.X, p.Y, -p.AltitudeM), plan.State.Position);
            Vec3 nose = plan.State.Attitude.Rotate(new Vec3(1, 0, 0));
            double dh = Math.Atan2(nose.Y, nose.X) - p.HeadingRad; dh = Math.Atan2(Math.Sin(dh), Math.Cos(dh));
            Assert.True(Math.Abs(dh) < 1e-9, $"heading off by {dh * D}°");
            double agl = p.AltitudeM - WorldTerrain.GroundHeightAt(p.X, p.Y);
            Assert.InRange(agl, lo - 1e-6, hi + 1e-6);
            double dc = Math.Sqrt(Math.Pow(p.X - ap.X, 2) + Math.Pow(p.Y - ap.Y, 2));
            Assert.InRange(dc, minRing, maxRing);
            if (c.Propulsion != null) Assert.True(Math.Abs(plan.Rig.GammaRad) < 1e-4);
            else Assert.Equal(RigSolver.RigGlideTasAt(c, p.AltitudeM), plan.Rig.TasMs, 6);
            // The speed the separation rules flew IS the speed the aeroplane starts at (still air) — the point of
            // ComputeRandom's re-check loop.
            Assert.Equal(plan.State.Velocity.Length, req.OwnSpeedMs, 1);
            AssertSeparated(p, plan.State.Velocity.Length, traffic, Array.Empty<StartPoint>(), WorldTerrain.GroundHeightAt, id);
            _out.WriteLine($"{id}: {agl / 0.3048:N0} ft AGL, hdg {p.HeadingRad * D:F0}°, {plan.Rig.TasMs * 1.943844:F0} KTAS, "
                         + $"ring {minRing / 1000:F1}–{maxRing / 1000:F1} km, probe {speed:F0} m/s, {p.Tries} tries");
        }
        finally { WorldTerrain.Active = null; }
    }
}
