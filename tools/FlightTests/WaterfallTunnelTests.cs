using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The hidden airport behind the first wall's plunge fall (owner 2026-10-16). These are the facts that decide
/// whether it can be flown rather than only looked at: a clear corridor at each mouth, arches that push a wide
/// airframe back in instead of letting it reach the mountain, walls at the dead ends, pavement long enough for
/// the aeroplane it was sized for, a height field that has ONE answer at every junction, and — the thing that
/// made the tunnel possible at all — no phantom water inside rock that sits under a river.
/// </summary>
public class WaterfallTunnelTests : IDisposable
{
    /// <summary>Put the world back as it was: these tests set the active terrain and fill the solids list, and
    /// everything else in the suite flies over flat ground at zero.</summary>
    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
    }

    private static WorldTerrain Fresh()
    {
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        t.RegisterWaterfallSolids();
        return t;
    }

    private static (WorldTerrain t, WorldTerrain.Waterfall f) World()
    {
        var t = Fresh();
        return (t, WaterfallTunnel.Fall(t));
    }

    private static WaterfallTunnel.Leg Leg(WorldTerrain.Waterfall f, string id)
        => System.Array.Find(WaterfallTunnel.Legs(f), l => l.Id == id);

    // ---- the low bore: the way out ----

    [Fact]
    public void TheBoreFloorIsFlatAndFlushWithTheShelfOutsideItsMouth()
    {
        var (t, f) = World();
        double floor = WaterfallTunnel.FloorM(f);
        Assert.Equal(f.LowerM, floor, 6);                       // the lower plateau: the shelf's own level
        double x = WaterfallTunnel.CentreX(f), mouth = WaterfallTunnel.MouthY(f);
        for (double y = mouth - 5; y > WaterfallTunnel.EndY(f) + 5; y -= 50)
            Assert.Equal(floor, t.HeightAt(x, y), 3);
        Assert.Equal(floor, t.HeightAt(x, mouth + 8), 3);       // no step at the sill
    }

    [Fact]
    public void ThereIsAClearCorridorThroughBothMouths()
    {
        var (t, f) = World();
        double mouth = WaterfallTunnel.MouthY(f);
        // Each mouth on its own centreline: the way out on the river's, the way in 110 m to the side of it.
        foreach ((double x, double floor) in new[]
                 {
                     (WaterfallTunnel.CentreX(f), WaterfallTunnel.FloorM(f)),
                     (WaterfallTunnel.CentreX(f) + WaterfallTunnel.EntryOffsetXM, WaterfallTunnel.EntryFloorM),
                 })
        for (double y = mouth - 2; y > mouth - 100; y -= 10)
        for (double up = floor + 2; up < floor + WaterfallTunnel.CrownM - 2; up += 6)
            Assert.Null(WorldSolids.Penetration(x, y, up));
    }

    [Fact]
    public void TheTwoMouthsAreOneAboveTheOtherBehindTheSameCurtain()
    {
        var (_, f) = World();
        Assert.True(WaterfallTunnel.EntryFloorM - WaterfallTunnel.FloorM(f) > 100,
                    "the way in should be well above the way out");
        // Both are in the same back wall. The way out is on the river's own centreline, squarely behind the
        // curtain; the way in is beside it, far enough that neither tunnel sits on top of the other.
        Assert.Equal(f.X, WaterfallTunnel.CentreX(f), 6);
        Assert.True(WaterfallTunnel.EntryOffsetXM > 2 * WaterfallTunnel.HalfWidthM, "the mouths would overlap");
        Assert.True(WaterfallTunnel.EntryOffsetXM < WorldTerrain.FallNotchHalfSpanM, "the way in must still be inside the amphitheater");
    }

    [Fact]
    public void TheMouthsHangBehindTheCurtainWithRoomToFlyThroughThem()
    {
        var (t, f) = World();
        double lip = t.RiverSurfaceAt(f.LipY - 5);
        foreach (double floor in new[] { WaterfallTunnel.FloorM(f), WaterfallTunnel.EntryFloorM })
        {
            double drop = lip - (floor + WaterfallTunnel.CrownM * 0.5);
            double curtain = WorldTerrain.FallLipSpeedMs * System.Math.Sqrt(2 * drop / 9.81);
            double pocket = curtain + (f.LipY - WaterfallTunnel.MouthY(f));
            Assert.True(pocket > 30, $"only {pocket:0} m between the water and the rock at {floor:0} m");
            Assert.True(pocket < 200, $"{pocket:0} m is not 'behind the waterfall' any more");
        }
    }

    // ---- the maze as a whole ----

    [Fact]
    public void EveryLegHasAMountainOverItsCrown()
    {
        // The layout is written as a path, not as coordinates, so this is the test that says the path stayed
        // inside the mountain. If a leg ever pokes out, it shows up here and not as a hole in the sky.
        var (t, f) = World();
        foreach (WaterfallTunnel.Leg l in WaterfallTunnel.Legs(f))
        {
            int n = (int)(l.Length / 20) + 2;
            for (int i = 0; i <= n; i++)
            {
                double s = (double)i / n;
                double x = l.X0 + (l.X1 - l.X0) * s, y = l.Y0 + (l.Y1 - l.Y0) * s;
                double outer = t.HeightAt(x, y, false, withTunnel: false);
                double rock = outer - (l.FloorAt(s) + l.Crown);
                Assert.True(rock > 50, $"{l.Id} at s={s:0.00}: only {rock:0} m of rock over the crown");
            }
        }
    }

    [Fact]
    public void WhereLegsOverlapTheyAgreeOnTheirFloor()
    {
        // The height field has to have ONE answer under a wheel. Two legs may share plan space — that is what
        // a junction is — but if their floors disagreed, the ground would jump as you rolled across it.
        var (_, f) = World();
        WaterfallTunnel.Leg[] legs = WaterfallTunnel.Legs(f);
        for (int a = 0; a < legs.Length; a++)
        for (int b = a + 1; b < legs.Length; b++)
        {
            WaterfallTunnel.Leg la = legs[a], lb = legs[b];
            int n = (int)(la.Length / 5) + 2;
            for (int i = 0; i <= n; i++)
            {
                double s = (double)i / n;
                double x = la.X0 + (la.X1 - la.X0) * s, y = la.Y0 + (la.Y1 - la.Y0) * s;
                if (!lb.Locate(x, y, out double s2, out _)) continue;
                double za = la.FloorAt(s), zb = lb.FloorAt(s2);
                Assert.True(System.Math.Abs(za - zb) < 2.5,
                            $"{la.Id} ({za:0.0} m) overlaps {lb.Id} ({zb:0.0} m) at ({x:0},{y:0}) — the floor has two answers");
            }
        }
    }

    [Fact]
    public void TheMazeIsOneConnectedRunFromTheWayInToTheWayOut()
    {
        // Every leg either starts inside another leg (a junction) or starts at a mouth. A leg that starts
        // nowhere is a leg nobody can reach.
        var (_, f) = World();
        WaterfallTunnel.Leg[] legs = WaterfallTunnel.Legs(f);
        double mouthY = WaterfallTunnel.MouthY(f), cx = WaterfallTunnel.CentreX(f);
        foreach (WaterfallTunnel.Leg l in legs)
        {
            bool atAMouth = System.Math.Abs(l.Y0 - mouthY) < 1
                            && (System.Math.Abs(l.X0 - cx) < 1
                                || System.Math.Abs(l.X0 - (cx + WaterfallTunnel.EntryOffsetXM)) < 1);
            if (atAMouth) continue;
            bool joined = false;
            foreach (WaterfallTunnel.Leg o in legs)
            {
                if (o.Id == l.Id) continue;
                // A hair inside the leg's own start, so a leg does not count itself through rounding.
                double dx = (l.X1 - l.X0) / l.Length, dy = (l.Y1 - l.Y0) / l.Length;
                if (o.Locate(l.X0 - dx * 2, l.Y0 - dy * 2, out _, out _)) { joined = true; break; }
            }
            Assert.True(joined, $"{l.Id} starts nowhere");
        }
    }

    [Fact]
    public void TheWayHomeJoinsTheBoreAtTheBoresOwnFloor()
    {
        var (_, f) = World();
        WaterfallTunnel.Leg[] legs = WaterfallTunnel.Legs(f);
        WaterfallTunnel.Leg join = legs[^1], bore = Leg(f, "exit.bore");
        double d = System.Math.Sqrt((join.X1 - bore.X1) * (join.X1 - bore.X1) + (join.Y1 - bore.Y1) * (join.Y1 - bore.Y1));
        Assert.True(d < WaterfallTunnel.HalfWidthM, $"the last leg ends {d:0} m from the end of the bore");
        Assert.Equal(WaterfallTunnel.FloorM(f), join.Z1, 1);
    }

    [Fact]
    public void BothBranchesOfTheHazardForkRejoin()
    {
        // The slit and the rockfall are symmetric doglegs, so they come back to the same point by
        // construction. If that ever stops being true the maze quietly grows a dead end nobody meant.
        var (_, f) = World();
        WaterfallTunnel.Leg[] legs = WaterfallTunnel.Legs(f);
        WaterfallTunnel.Leg d = System.Array.FindLast(legs, l => l.Id.StartsWith("D.in"));
        WaterfallTunnel.Leg e = System.Array.FindLast(legs, l => l.Id.StartsWith("E.in"));
        Assert.True(System.Math.Abs(d.X1 - e.X1) < 10 && System.Math.Abs(d.Y1 - e.Y1) < 10,
                    $"the branches end {System.Math.Sqrt((d.X1 - e.X1) * (d.X1 - e.X1) + (d.Y1 - e.Y1) * (d.Y1 - e.Y1)):0} m apart");
        Assert.True(System.Math.Abs(d.Z1 - e.Z1) < 2.5, "and at the same floor");
    }

    // ---- what each piece is for ----

    [Fact]
    public void EveryStripIsLongEnoughForTheAeroplaneItMightHaveToHold()
    {
        // Owner's rule: every dead end has to work for anything up to a T-6, which stops in about 1,500 ft and
        // clears fifty feet in about 1,800 ft. 760 m is 2,493 ft.
        var (_, f) = World();
        Assert.True(WaterfallTunnel.StripM >= 762, "a strip shorter than 2,500 ft makes a dead end a trap");
        Assert.True(WaterfallTunnel.StripM * 3.28084 >= 2500, "2,500 ft is the figure, in the units it came from");
        foreach (WaterfallTunnel.Leg l in WaterfallTunnel.Legs(f))
            if (l.Kind == WaterfallTunnel.Kind.Strip)
                Assert.True(l.Length >= WaterfallTunnel.StripM - 1, $"{l.Id} is only {l.Length:0} m");
    }

    [Fact]
    public void TheGliderStripComesBeforeTheFirstFork()
    {
        // The maze is powered-only, so a glider that follows somebody in has to meet its strip before it can
        // take a wrong turning.
        var (_, f) = World();
        WaterfallTunnel.Leg[] legs = WaterfallTunnel.Legs(f);
        int glider = System.Array.FindIndex(legs, l => l.Kind == WaterfallTunnel.Kind.GliderStrip);
        int firstStrip = System.Array.FindIndex(legs, l => l.Kind == WaterfallTunnel.Kind.Strip);
        Assert.True(glider >= 0, "there is no glider strip");
        Assert.True(glider < firstStrip, "the glider strip has to come first");
        Assert.True(legs[glider].Length >= 450, "a 2-33 lands in a quarter of it, but give it room");
    }

    [Fact]
    public void TheSlitStaysFortyFeetAndIsShortEnoughToBeBalletic()
    {
        // Owner 2026-10-16: it stays 40 ft and you must bank to get through. These are the numbers that make
        // that a challenge rather than a wall.
        var (_, f) = World();
        WaterfallTunnel.Leg slit = Leg(f, "D.slit");
        Assert.Equal(12.2, slit.HalfWidth * 2, 1);                          // 40 ft
        Assert.True(slit.Crown >= 13.0, "a T-6 on its side needs its 12.8 m of span in the vertical");
        Assert.True(slit.Length <= 50, "short enough that an aeroplane that cannot hold a knife edge can be thrown through it");
        // And the geometry of the ask: a 172 spans 11 m, so it fits 12.2 m only at a bank angle.
        double bank = System.Math.Acos(12.2 / 11.0 > 1 ? 1 : 12.2 / 11.0) * 180 / System.Math.PI;
        Assert.True(11.0 < 12.2, $"a 172 fits level with {(12.2 - 11.0) * 100 / 2:0} cm a side — {bank:0}° of bank to be comfortable");
    }

    [Fact]
    public void EveryDeadEndEndsInAWall()
    {
        var (_, f) = World();
        foreach (WaterfallTunnel.Leg l in WaterfallTunnel.Legs(f))
        {
            if (l.Kind != WaterfallTunnel.Kind.DeadEndPad) continue;
            double dx = (l.X1 - l.X0) / l.Length, dy = (l.Y1 - l.Y0) / l.Length;
            Assert.NotNull(WorldSolids.Penetration(l.X1 + dx * 10, l.Y1 + dy * 10, l.Z1 + 10));
        }
        // And so does the bore, which is the one dead end that was there before the maze.
        WaterfallTunnel.Leg bore = Leg(f, "exit.bore");
        Assert.NotNull(WorldSolids.Penetration(bore.X1 - 10, bore.Y1 - 10, bore.Z1 + 10));
    }

    [Fact]
    public void TheArchIsRockAboveTheCrownAndPushesDownAndIn()
    {
        var (_, f) = World();
        WaterfallTunnel.Leg bore = Leg(f, "exit.bore");
        double x = bore.X0, y = bore.Y0 - 300, floor = bore.Z0;
        Assert.Null(WorldSolids.Penetration(x, y, floor + WaterfallTunnel.CrownM - 1));
        var hit = WorldSolids.Penetration(x, y, floor + WaterfallTunnel.CrownM + 1);
        Assert.NotNull(hit);
        Assert.True(hit!.Value.normalNed.Z > 0.9);              // straight down, on the centreline

        // Out at the springing of the arch the push leans inward across the bore, which is what keeps a wing
        // off the mountain. This leg runs along y, so "inward" is −x on the +x side.
        var wide = WorldSolids.Penetration(x + 30, y, floor + 20);
        Assert.NotNull(wide);
        Assert.True(wide!.Value.normalNed.X < -0.3, $"expected an inward push, got {wide.Value.normalNed.X:0.00}");
        Assert.True(wide.Value.depth > 0);
    }

    [Fact]
    public void ThereIsPavementToLandOnAndItIsPavedOnlyWhereItShouldBe()
    {
        var (_, f) = World();
        WaterfallTunnel.Leg bore = Leg(f, "exit.bore");
        double x = bore.X0;
        double in0 = bore.Y0 - WaterfallTunnel.RunwayStartInM;
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x, in0 - 1));
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x, in0 - WaterfallTunnel.StripM + 1));
        Assert.NotEqual(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x, in0 - WaterfallTunnel.StripM - 20));
        Assert.NotEqual(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x + WaterfallTunnel.StripHalfWidthM + 4, in0 - 100));
        // The strips inside the maze are paved down their middles too.
        foreach (WaterfallTunnel.Leg l in WaterfallTunnel.Legs(f))
        {
            if (l.Kind != WaterfallTunnel.Kind.Strip && l.Kind != WaterfallTunnel.Kind.GliderStrip) continue;
            double mx = (l.X0 + l.X1) / 2, my = (l.Y0 + l.Y1) / 2;
            Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(mx, my));
        }
    }

    [Fact]
    public void TheRunwayIsWideEnoughForTheAirplanesThatCanGetIn()
    {
        var (_, f) = World();
        WaterfallTunnel.Leg bore = Leg(f, "exit.bore");
        double h = bore.ClearHeight(WaterfallTunnel.StripHalfWidthM);
        Assert.True(h > 20, $"only {h:0.0} m of headroom at the pavement edge");
        Assert.True(WaterfallTunnel.StripHalfWidthM * 2 > 20, "a Jenny spans 13 m");
    }

    // ---- the hazards ----

    [Fact]
    public void TheRockfallLeavesALaneWideEnoughForTheAeroplaneItWasSizedFor()
    {
        // The owner's rule: every bad fork is passable for anything up to a T-6, which spans 12.8 m. So the
        // rocks are not spread across the bore — they fall on one side of a lane that is always clear.
        var (_, f) = World();
        WaterfallTunnel.Leg leg = Leg(f, "E.rockfall");
        Assert.True(WaterfallTunnel.Rockfall.LaneHalfWidthM * 2 >= 24,
                    "a T-6 needs 12.8 m of span and somewhere to put it");
        for (double t = 0; t < WaterfallTunnel.Rockfall.CycleS * 3; t += 0.05)
        for (int i = 0; i < WaterfallTunnel.Rockfall.Count; i++)
        {
            if (!WaterfallTunnel.Rockfall.TryRock(i, t, leg, out double up, out double lateral)) continue;
            // Every rock, at every moment it is in the air, is clear of the lane and inside the bore.
            Assert.True(lateral < -WaterfallTunnel.Rockfall.LaneHalfWidthM + 0.001,
                        $"rock {i} at t={t:0.00} is {lateral:0.0} m off the centreline — inside the lane");
            Assert.True(System.Math.Abs(lateral) < leg.HalfWidth, $"rock {i} is outside the bore");
            Assert.True(up > leg.FloorAt(0.5) && up < leg.FloorAt(0.5) + leg.Crown, $"rock {i} is outside the arch");
        }
    }

    [Fact]
    public void TheRockfallIsTheClockAndNotADice()
    {
        var (_, f) = World();
        WaterfallTunnel.Leg leg = Leg(f, "E.rockfall");
        // The same moment of any cycle looks the same, for ever: a pilot who times it once has timed it.
        for (int i = 0; i < WaterfallTunnel.Rockfall.Count; i++)
        for (double t = 0.05; t < WaterfallTunnel.Rockfall.FallS; t += 0.05)
        {
            bool a = WaterfallTunnel.Rockfall.TryRock(i, t, leg, out double upA, out _);
            bool b = WaterfallTunnel.Rockfall.TryRock(i, t + WaterfallTunnel.Rockfall.CycleS * 7, leg, out double upB, out _);
            Assert.Equal(a, b);
            if (a) Assert.Equal(upA, upB, 3);        // millimetres, seven cycles apart
        }
        // And every fall is announced: the telegraph is on in the second before one, and off well before that.
        Assert.True(WaterfallTunnel.Rockfall.Telegraphing(WaterfallTunnel.Rockfall.CycleS - 0.4));
        Assert.False(WaterfallTunnel.Rockfall.Telegraphing(WaterfallTunnel.Rockfall.CycleS * 0.5));
    }

    [Fact]
    public void TheRocksFallDownwardsAtTheRateRocksDo()
    {
        var (_, f) = World();
        WaterfallTunnel.Leg leg = Leg(f, "E.rockfall");
        Assert.True(WaterfallTunnel.Rockfall.TryRock(0, 0.2, leg, out double early, out _));
        Assert.True(WaterfallTunnel.Rockfall.TryRock(0, 0.6, leg, out double later, out _));
        Assert.True(later < early, "a rock that is not falling is not a hazard");
        // 0.5 g t² from the roof, which is what a dropped rock does.
        double floor = leg.FloorAt(0.5), crown = floor + leg.Crown;
        Assert.Equal(crown - 1 - 0.5 * 9.81 * 0.36, later, 3);
    }

    // ---- water, which is what made all of this possible ----

    [Fact]
    public void NoWaterInsideTheTunnelThoughTheRiverRunsOverIt()
    {
        var (t, f) = World();
        WaterfallTunnel.Leg bore = Leg(f, "exit.bore");
        double x = bore.X0, y = bore.Y0 - 400, floor = bore.Z0;
        double? above = t.WaterSurfaceAt(x, y);
        Assert.NotNull(above);
        Assert.True(above!.Value > floor + 150);                       // the river, for the water mesh
        Assert.Null(t.WaterSurfaceAt(x, y, floor + 0.5));              // a wheel on the pavement is not in it
        Assert.Null(t.WaterSurfaceAt(x, y, floor + WaterfallTunnel.CrownM - 2));
        Assert.Null(FloatHydro.WaterSurfaceAt(x, y, floor + 0.5));
    }

    [Fact]
    public void TheAmphitheaterBehindEveryCurtainIsDryUnderTheShelf()
    {
        // This was wrong before the tunnel went in: the slot behind a curtain is meant to be flyable, and the
        // water query reported the river 240 m overhead as the surface the airframe was in.
        var t = Fresh();
        foreach (WorldTerrain.Waterfall wf in t.Waterfalls)
        {
            double x = wf.X, y = wf.LipY - 20;
            Assert.Null(t.WaterSurfaceAt(x, y, wf.LowerM + 10));          // on the shelf floor: dry
            double? onShelf = t.WaterSurfaceAt(x, y);                      // height-less: the river, as before
            Assert.NotNull(onShelf);
            Assert.True(onShelf!.Value > wf.LowerM + 100);
        }
    }

    [Fact]
    public void TheRiverItselfStillHasWaterAboveAndBelowTheFall()
    {
        var t = Fresh();
        var f = WaterfallTunnel.Fall(t);
        double above = t.RiverSurfaceAt(f.LipY - 80);
        Assert.NotNull(t.WaterSurfaceAt(WorldTerrain.RiverCentreX(f.LipY - 80), f.LipY - 80, above));
        double below = t.RiverSurfaceAt(f.LipY + 120);
        Assert.NotNull(t.WaterSurfaceAt(WorldTerrain.RiverCentreX(f.LipY + 120), f.LipY + 120, below));
    }
}
