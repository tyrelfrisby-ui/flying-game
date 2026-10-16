using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The hidden runway behind the first wall's plunge fall (owner 2026-10-16). These are the facts that decide
/// whether it can be flown rather than only looked at: a clear corridor at the mouth's height, an arch that
/// pushes a wide airframe back in instead of letting it reach the mountain, an end wall, pavement to land on,
/// and — the thing that made the tunnel possible at all — no phantom water inside rock that sits under a river.
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
        return (t, t.Waterfalls[WaterfallTunnel.FallStep]);
    }

    [Fact]
    public void TheFloorIsFlatAndFlushWithTheShelfOutsideTheMouth()
    {
        var (t, f) = World();
        double floor = WaterfallTunnel.FloorM(f);
        Assert.Equal(f.LowerM, floor, 6);                       // the lower plateau: the shelf's own level
        double x = WaterfallTunnel.CentreX(f), mouth = WaterfallTunnel.MouthY(f);
        // Flat all the way down the bore.
        for (double y = mouth - 5; y > WaterfallTunnel.EndY(f) + 5; y -= 50)
            Assert.Equal(floor, t.HeightAt(x, y), 3);
        // And no step at the sill: the amphitheater floor just outside matches.
        Assert.Equal(floor, t.HeightAt(x, mouth + 8), 3);
    }

    [Fact]
    public void ThereIsAClearCorridorToFlyInThrough()
    {
        var (t, f) = World();
        double x = WaterfallTunnel.CentreX(f), floor = WaterfallTunnel.FloorM(f);
        for (double y = WaterfallTunnel.MouthY(f) - 2; y > WaterfallTunnel.EndY(f) + 20; y -= 25)
        for (double up = floor + 2; up < floor + WaterfallTunnel.CrownM - 2; up += 6)
            Assert.Null(WorldSolids.Penetration(x, y, up));
    }

    [Fact]
    public void TheArchIsRockAboveTheCrownAndPushesDownAndIn()
    {
        var (t, f) = World();
        double x = WaterfallTunnel.CentreX(f), y = WaterfallTunnel.MouthY(f) - 300, floor = WaterfallTunnel.FloorM(f);
        Assert.Null(WorldSolids.Penetration(x, y, floor + WaterfallTunnel.CrownM - 1));
        var hit = WorldSolids.Penetration(x, y, floor + WaterfallTunnel.CrownM + 1);
        Assert.NotNull(hit);
        Assert.True(hit!.Value.normalNed.Z > 0.9);              // straight down, on the centreline

        // Out at the springing of the arch the push leans inward, which is what keeps a wing off the mountain.
        var wide = WorldSolids.Penetration(x + 30, y, floor + 20);
        Assert.NotNull(wide);
        Assert.True(wide!.Value.normalNed.X < -0.3, $"expected an inward push, got {wide.Value.normalNed.X:0.00}");
        Assert.True(wide.Value.depth > 0);
    }

    [Fact]
    public void TheMountainOutsideTheBoreIsUntouched()
    {
        var (t, f) = World();
        double x = WaterfallTunnel.CentreX(f), y = WaterfallTunnel.MouthY(f) - 300;
        // A metre outside the footprint the ground is the upper gorge floor again, hundreds of feet up.
        double outside = t.HeightAt(x + WaterfallTunnel.HalfWidthM + 1, y);
        Assert.True(outside > WaterfallTunnel.FloorM(f) + 150, $"ground outside the bore is {outside:0}");
        // And the bore has a mountain's worth of rock over its crown the whole way.
        for (double yy = WaterfallTunnel.MouthY(f) - 20; yy > WaterfallTunnel.EndY(f); yy -= 50)
        {
            Assert.True(WaterfallTunnel.RoofAt(t, f, x, yy, out double ceil, out double outer));
            Assert.True(outer - ceil > 150, $"only {outer - ceil:0} m of rock at y={yy:0}");
        }
    }

    [Fact]
    public void TheBoreEndsInAWall()
    {
        var (t, f) = World();
        double x = WaterfallTunnel.CentreX(f), floor = WaterfallTunnel.FloorM(f);
        var hit = WorldSolids.Penetration(x, WaterfallTunnel.EndY(f) - 10, floor + 10);
        Assert.NotNull(hit);
    }

    [Fact]
    public void ThereIsPavementToLandOnAndItIsLongEnough()
    {
        var (t, f) = World();
        double x = WaterfallTunnel.CentreX(f);
        double in0 = WaterfallTunnel.MouthY(f) - WaterfallTunnel.RunwayStartInM;
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x, in0 - 1));
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x, in0 - WaterfallTunnel.RunwayLengthM + 1));
        Assert.NotEqual(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x, in0 - WaterfallTunnel.RunwayLengthM - 20));
        Assert.NotEqual(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(x + WaterfallTunnel.RunwayHalfWidthM + 4, in0 - 100));
        // 2,500 ft is the figure that matters: a 172 or a Cub can land and take off again inside the hill.
        Assert.True(WaterfallTunnel.RunwayLengthM >= 762, "the runway inside has to be at least 2,500 ft");
        // It all fits in the bore, with the end wall clear of the pavement.
        Assert.True(WaterfallTunnel.RunwayStartInM + WaterfallTunnel.RunwayLengthM < WaterfallTunnel.BoreLengthM);
    }

    [Fact]
    public void TheRunwayIsWideEnoughForTheAirplanesThatCanGetIn()
    {
        var (t, f) = World();
        // Clear height at the pavement's edge, where a wing tip actually is.
        double h = WaterfallTunnel.ClearHeightAt(f, WaterfallTunnel.CentreX(f) + WaterfallTunnel.RunwayHalfWidthM);
        Assert.True(h > 20, $"only {h:0.0} m of headroom at the pavement edge");
        Assert.True(WaterfallTunnel.RunwayHalfWidthM * 2 > 20, "a Jenny spans 13 m");
    }

    [Fact]
    public void NoWaterInsideTheTunnelThoughTheRiverRunsOverIt()
    {
        var (t, f) = World();
        double x = WaterfallTunnel.CentreX(f), y = WaterfallTunnel.MouthY(f) - 400, floor = WaterfallTunnel.FloorM(f);
        // The river is up there, and the height-less answer still says so — the water mesh is drawn from it.
        double? above = t.WaterSurfaceAt(x, y);
        Assert.NotNull(above);
        Assert.True(above!.Value > floor + 150);
        // A wheel on the pavement is not in a river.
        Assert.Null(t.WaterSurfaceAt(x, y, floor + 0.5));
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
        var f = t.Waterfalls[WaterfallTunnel.FallStep];
        double above = t.RiverSurfaceAt(f.LipY - 80);
        Assert.NotNull(t.WaterSurfaceAt(WorldTerrain.RiverCentreX(f.LipY - 80), f.LipY - 80, above));
        double below = t.RiverSurfaceAt(f.LipY + 120);
        Assert.NotNull(t.WaterSurfaceAt(WorldTerrain.RiverCentreX(f.LipY + 120), f.LipY + 120, below));
    }

    [Fact]
    public void TheMouthHangsBehindTheCurtainWithRoomToFlyThroughIt()
    {
        var (t, f) = World();
        // Where the water is at the mouth's height: it leaves the lip at FallLipSpeedMs and falls free, so it
        // is this far downstream of the lip by the time it is level with the mouth.
        double lip = t.RiverSurfaceAt(f.LipY - 5);
        double drop = lip - (WaterfallTunnel.FloorM(f) + WaterfallTunnel.CrownM * 0.5);
        double curtain = WorldTerrain.FallLipSpeedMs * System.Math.Sqrt(2 * drop / 9.81);
        double pocket = curtain + (f.LipY - WaterfallTunnel.MouthY(f));
        Assert.True(pocket > 60, $"only {pocket:0} m between the water and the rock");
        Assert.True(pocket < 200, $"{pocket:0} m is not 'behind the waterfall' any more");
    }
}
