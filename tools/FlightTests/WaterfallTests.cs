using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Plunge waterfalls at the canyon walls: a straight lip the river leaves in free fall, a concave
/// back wall set back behind it, an overhanging rock shelf (collision solid) carrying the river to the lip,
/// and a flyable slot of at least 150 ft between curtain and cliff across the river's width.</summary>
public class WaterfallTests
{
    private const double FeetPerMetre = 3.28084;

    [Fact]
    public void ThreeFallsOneAtEachWallOnTheRiver()
    {
        var t = new WorldTerrain();
        Assert.Equal(WorldTerrain.StepCount, t.Waterfalls.Length);
        foreach (WorldTerrain.Waterfall f in t.Waterfalls)
        {
            Assert.Equal(WorldTerrain.RiverCentreX(f.LipY), f.X, 0.5);
            // The lip is the top of the wall: the wandering edge at the river's x, less the wall run.
            double edge = WorldTerrain.EdgeMeanY(f.Step) + WorldTerrain.EdgeWander(f.Step, f.X);
            Assert.Equal(edge - WorldTerrain.EscarpmentWidthM, f.LipY, 1.0);
            Assert.Equal(WorldTerrain.StepHeightM, f.UpperM - f.LowerM, 6);
        }
    }

    [Fact]
    public void RiverDropsCleanlyAtTheLip()
    {
        var t = new WorldTerrain();
        foreach (WorldTerrain.Waterfall f in t.Waterfalls)
        {
            double above = t.WaterSurfaceAt(f.X, f.LipY - 2) ?? double.NaN;
            double below = t.WaterSurfaceAt(f.X, f.LipY + 2) ?? double.NaN;
            Assert.False(double.IsNaN(above)); Assert.False(double.IsNaN(below));
            // A full-step fall in 4 m of river: a free-fall lip, not a slide.
            Assert.True(above - below > WorldTerrain.StepHeightM - 200, $"fall {f.Step}: drop {above - below:F0} m");
            // On the shelf the river is still at the upper level all the way back to the wall.
            double onShelf = t.WaterSurfaceAt(f.X, f.LipY - WorldTerrain.FallRecessM + 2) ?? double.NaN;
            Assert.Equal(above, onShelf, 2.0);
        }
    }

    [Fact]
    public void GroundUnderTheShelfIsTheLowerPlateau()
    {
        var t = new WorldTerrain();
        foreach (WorldTerrain.Waterfall f in t.Waterfalls)
        {
            for (double dx = -WorldTerrain.RiverHalfWidthM; dx <= WorldTerrain.RiverHalfWidthM; dx += 10)
            {
                double under = t.HeightAt(f.X + dx, f.LipY - 20);
                Assert.True(under < f.LowerM + 5, $"fall {f.Step}: ground under the shelf at {under:F0} m, lower plateau {f.LowerM:F0}");
                // ... and the shelf-top surface above it still carries the upper river.
                double shelfTop = t.HeightAt(f.X + dx, f.LipY - 20, shelfTop: true);
                Assert.True(shelfTop > f.UpperM - 200, $"fall {f.Step}: shelf top {shelfTop:F0}");
            }
        }
    }

    [Fact]
    public void FlyableSlotBehindTheCurtainIsAtLeast150FtDeep()
    {
        var t = new WorldTerrain();
        foreach (WorldTerrain.Waterfall f in t.Waterfalls)
        {
            // Across the river's full width (and well beyond), the back wall is ≥ 150 ft behind the lip.
            for (double dx = -80; dx <= 80; dx += 10)
            {
                double recess = WorldTerrain.FallRecessAt(f, f.X + dx);
                Assert.True(recess * FeetPerMetre >= 150, $"fall {f.Step}: slot {recess * FeetPerMetre:F0} ft at dx {dx}");
                // The back wall really is there: just behind the recess the ground is the upper plateau.
                double rock = t.HeightAt(f.X + dx, f.LipY - recess - 3);
                double air = t.HeightAt(f.X + dx, f.LipY - recess + 3);
                Assert.True(rock > f.UpperM - 200, $"fall {f.Step}: back wall {rock:F0}");
                Assert.True(air < f.LowerM + 5, $"fall {f.Step}: slot floor {air:F0}");
            }
        }
    }

    [Fact]
    public void ShelfIsASolidTheAirframeHits()
    {
        var t = new WorldTerrain();
        WorldSolids.Boxes.Clear();
        t.RegisterWaterfallSolids();
        Assert.Equal(WorldTerrain.StepCount, WorldSolids.Boxes.Count);
        WorldTerrain.Waterfall f = t.Waterfalls[0];
        double bottom = t.FallShelfBottomM(f);
        Assert.NotNull(WorldSolids.Penetration(f.X, f.LipY - 20, bottom + 3));           // inside the rock shelf
        Assert.Null(WorldSolids.Penetration(f.X, f.LipY - 20, bottom - 30));             // in the slot under it
        Assert.Null(WorldSolids.Penetration(f.X, f.LipY + 20, bottom + 3));              // in front of the lip: air
        WorldSolids.Boxes.Clear();
    }
}
