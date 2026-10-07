using System;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Core.Aero;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

public class EventTests
{
    [Fact]
    public void RaceCountsGatesInOrderAndPenalises()
    {
        var race = new AirRace();
        RaceElement g1 = RaceCourse.Elements[0], g2 = RaceCourse.Elements[1];
        // Approach gate 1 from the south at 10 m AGL, level.
        race.Update(new Vec3(g1.X - 50, g1.Y, -10), 0, 0.1);
        race.Update(new Vec3(g1.X + 50, g1.Y, -10), 0, 0.1);
        Assert.True(race.Running); Assert.Equal(1, race.Next);
        // Gate 2 crossed 3 m off-centre but banked 40° → +2 s.
        race.Update(new Vec3(g2.X - 50, g2.Y + 3, -10), 0.7, 5.0);
        race.Update(new Vec3(g2.X + 50, g2.Y + 3, -10), 0.7, 0.1);
        Assert.Equal(2, race.Next); Assert.Equal(2, race.PenaltySec);
        // Pylon 3 must be on the RIGHT: pass with lateral < 0 (to its left)... PylonOnRight means pylon on the pilot's right → aircraft to its LEFT.
        RaceElement p3 = RaceCourse.Elements[2];
        Vec3 left = p3.Right * -20;
        race.Update(p3.Centre + left - p3.Forward * 30 + new Vec3(0, 0, -10), 0.5, 5.0);
        race.Update(p3.Centre + left + p3.Forward * 30 + new Vec3(0, 0, -10), 0.5, 0.1);
        Assert.Equal(3, race.Next); Assert.Equal(2, race.PenaltySec);
        Assert.True(race.ElapsedSec > 10);
    }

    [Fact]
    public void RaceIgnoresCrossingsFarFromTheElement()
    {
        var race = new AirRace();
        RaceElement g1 = RaceCourse.Elements[0];
        race.Update(new Vec3(g1.X - 50, g1.Y + 500, -10), 0, 0.1);
        race.Update(new Vec3(g1.X + 50, g1.Y + 500, -10), 0, 0.1);
        Assert.False(race.Running);
    }

    [Fact]
    public void StolScoresStopDistanceAndDisqualifiesShortLandings()
    {
        WorldTerrain.Airport a = WorldTerrain.Airports[0];
        StolRun run = StolRun.ForAirport(a);
        WorldTerrain.Strip dirt = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "gravel");
        double line = a.X + dirt.Dx - dirt.Length / 2 + WorldTerrain.StolLineFromThresholdM, y = a.Y + dirt.Dy;
        run.Update(new Vec3(line - 40, y, -5), false, 25);
        run.Update(new Vec3(line + 8, y, 0), true, 22);           // touchdown 8 m past the line
        Assert.Equal(StolRun.Phases.Rolling, run.Phase);
        run.Update(new Vec3(line + 60, y, 0), true, 10);
        run.Update(new Vec3(line + 75, y, 0), true, 0.2);         // stopped
        Assert.Equal(StolRun.Phases.Stopped, run.Phase);
        Assert.InRange(run.StopPastLineM, 74, 76);

        var shortRun = StolRun.ForAirport(a);
        shortRun.Update(new Vec3(line - 40, y, -5), false, 25);
        shortRun.Update(new Vec3(line - 3, y, 0), true, 22);
        Assert.Equal(StolRun.Phases.Disqualified, shortRun.Phase);
    }

    [Fact]
    public void AeroBoxIsOnFlatValleyGround()
    {
        var t = new WorldTerrain();
        double h0 = t.HeightAt(AeroBox.CenterX, AeroBox.CenterY);
        for (double dx = -500; dx <= 500; dx += 250)
        for (double dy = -500; dy <= 500; dy += 250)
            Assert.InRange(t.HeightAt(AeroBox.CenterX + dx, AeroBox.CenterY + dy), h0 - 5, h0 + 5);
        // Race elements all on the valley floor, hoops above ground.
        foreach (RaceElement e in RaceCourse.Elements) Assert.InRange(t.HeightAt(e.X, e.Y) - WorldTerrain.DatumM, -1, 30);
    }

    [Fact]
    public void WingTipThroughAPylonIsAStrikeOnceAndCostsThreeSeconds()
    {
        var race = new AirRace();
        RaceElement g1 = RaceCourse.Elements[0];
        race.Update(new Vec3(g1.X - 50, g1.Y, -10), 0, 0.1);
        race.Update(new Vec3(g1.X + 50, g1.Y, -10), 0, 0.1);   // GO
        Assert.True(race.Running);
        // Right wing tip sweeps through the right pylon of gate 2 at 20 m AGL.
        RaceElement g2 = RaceCourse.Elements[1];
        double px = g2.X + g2.Right.X * RaceElement.GateHalfWidthM, py = g2.Y + g2.Right.Y * RaceElement.GateHalfWidthM;
        var hit = race.CheckPylonStrike(new Vec3(px, py - 12, -20), new Vec3(px, py + 0.5, -20), 0.02);
        Assert.NotNull(hit); Assert.Equal((1, 1), hit.Value);
        Assert.Equal(3, race.PenaltySec);
        Assert.Null(race.CheckPylonStrike(new Vec3(px, py - 12, -20), new Vec3(px, py + 0.5, -20), 0.02));   // same pylon: no double count
        Assert.Null(race.CheckPylonStrike(new Vec3(px, py - 12, -90), new Vec3(px, py + 0.5, -90), 0.02));   // above the pylon top: clear
        // The course finishes at its LAST element, not by looping back to the start.
        Assert.Equal(RaceElement.Kinds.Gate, RaceCourse.Elements[^1].Kind);
        Assert.InRange(RaceElement.GateHalfWidthM * 2, 45.6, 45.8);   // 150 ft between the pylons (owner 2026-10-06: farther apart; was 100 ft)
        Assert.Equal(75.0, RaceElement.GateHeightM);
    }
}

[Collection("WorldTerrainActive")]
public class StolContestTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public StolContestTests(Xunit.Abstractions.ITestOutputHelper o) { _out = o; }

    /// <summary>Owner 2026-10-07: three landings and three takeoffs from the white line, scored as average takeoff + average
    /// landing; a touchdown short of the line is a foul and is flown again.</summary>
    [Fact]
    public void ThreeLandingsAndThreeTakeoffsAveraged()
    {
        var k = new StolContest(1000, 0, 12, 200);
        double L(double past) => k.XAt(past);
        void Land(double td, double stop)
        {
            Assert.Equal(StolContest.Kinds.Landing, k.Kind);
            k.Update(new Vec3(L(td - 30), 0, -5), false, 25, 0.02);
            k.Update(new Vec3(L(td), 0, 0), true, 20, 0.02);
            for (int i = 0; i < 80; i++) k.Update(new Vec3(L(stop), 0, 0), true, 0, 0.02);
            k.Next();
        }
        void TakeOff(double lift)
        {
            Assert.Equal(StolContest.Kinds.Takeoff, k.Kind);
            k.Update(new Vec3(L(0), 0, 0), true, 0, 0.02);
            k.Update(new Vec3(L(lift), 0, 0), true, 15, 0.02);
            for (int i = 0; i < 80; i++) k.Update(new Vec3(L(lift + 10), 0, -2), false, 18, 0.02);
            k.Next();
        }
        // a foul: touched 20 m short — the same landing again
        k.Update(new Vec3(L(-20), 0, 0), true, 20, 0.02); Assert.Equal(StolContest.Phases.Foul, k.Phase); k.Next();
        Assert.Equal(0, k.Attempt);
        Land(5, 40); TakeOff(30); Land(2, 50); TakeOff(36); Land(8, 45); TakeOff(33);
        Assert.Equal(StolContest.Phases.Finished, k.Phase);
        Assert.Equal(45.0, k.AvgLandingM, 6); Assert.Equal(33.0, k.AvgTakeoffM, 6);
        Assert.Equal(78 * 3.28084, k.TotalFt, 3);
    }

    /// <summary>The starts: on final 600 m out on a 4° path at 1.1 Vs, full flaps; at rest with the mains on the line.</summary>
    [Theory]
    [InlineData("pa18-cub-like.json")]
    [InlineData("c172-like.json")]
    public void StartsOnFinalAndOnTheLine(string file)
    {
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TestData", file));
        WorldTerrain.Active = null;
        var k = new StolContest(1000, 0, 12, 200);
        var (st, elev, thr, v) = StolSpawn.Final(c, k, 0);
        var ac = new Aircraft(c, st, new ControlDeflections(0, elev, 0, 0, 1.0)) { FlapFraction = 1.0 };
        var sim = new SimLoop(ac);
        sim.RunFor(3.0, new ControlInputs(0, Aircraft.StickForDeflection(elev, c.Controls.Elevator), 0, 1 - 2 * thr));
        double sink = ac.State.Attitude.Rotate(ac.State.Velocity).Z;
        _out.WriteLine($"{file}: 1.1 Vs = {v * 1.944:F0} kt, throttle {thr:F2}; after 3 s sink {sink:F1} m/s (4° path = {v * Math.Sin(4 * Math.PI / 180):F1})");
        Assert.InRange(sink, 0.0, 2 * v * Math.Sin(4 * Math.PI / 180) + 1.0);
        var rest = StolSpawn.OnTheLine(c, k, 0);
        Assert.InRange(k.PastLine(StolSpawn.Mains(c, rest).X), -0.3, 0.3);
    }
}
