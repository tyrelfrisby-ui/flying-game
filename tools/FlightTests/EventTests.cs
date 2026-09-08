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
        WorldTerrain.Strip dirt = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "dirt");
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
        foreach (RaceElement e in RaceCourse.Elements) Assert.InRange(t.HeightAt(e.X, e.Y), -1, 30);
    }
}
