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
                Assert.True(shelfTop > f.LowerM + 50, $"fall {f.Step}: shelf top {shelfTop:F0}");   // still carries the upper river (in its gorge), well above the lower plateau
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
                // The back wall really is there: just behind the recess the ground is the upper plateau — except
                // at the first fall, where the 70 m of it dead behind the curtain is the tunnel's mouth, and the
                // ground carries on at the bore's floor instead. That hole is the feature (owner 2026-10-16).
                // Two holes in this wall, at the first fall: the way OUT at the shelf's own level on the river's
                // centreline, and the way IN 110 m to the side and 125 m higher. Everywhere else it is rock.
                bool atTheMaze = f.Step == WaterfallTunnel.FallStep;
                bool throughTheLowMouth = atTheMaze && System.Math.Abs(dx) < WaterfallTunnel.HalfWidthM;
                bool throughTheHighMouth = atTheMaze
                    && System.Math.Abs(dx - WaterfallTunnel.EntryOffsetXM) < WaterfallTunnel.HalfWidthM + WaterfallTunnel.JointPadM;
                double rock = t.HeightAt(f.X + dx, f.LipY - recess - 3);
                if (throughTheLowMouth) Assert.Equal(WaterfallTunnel.FloorM(f), rock, 3);
                else if (throughTheHighMouth) Assert.InRange(rock, WaterfallTunnel.EntryFloorM - 2, WaterfallTunnel.EntryFloorM);
                else Assert.True(rock > f.LowerM + 50, $"fall {f.Step}: back wall {rock:F0}");
                double air = t.HeightAt(f.X + dx, f.LipY - recess + 3);
                if (throughTheHighMouth) Assert.InRange(air, WaterfallTunnel.EntryFloorM - 2, WaterfallTunnel.EntryFloorM);   // the high mouth's sill, which slopes
                else Assert.True(air < f.LowerM + 5, $"fall {f.Step}: slot floor {air:F0}");
            }
        }
    }

    [Fact]
    public void ShelfIsASolidTheAirframeHits()
    {
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        t.RegisterWaterfallSolids();
        // One shelf per fall, plus a wall across the end of every blind leg of the tunnel maze — its two dead
        // ends and the bore. Counted from the layout rather than written down, so adding a dead end to the
        // maze does not fail a test that has nothing to do with it.
        WorldTerrain.Waterfall tf = WaterfallTunnel.Fall(t);
        int blind = 0;
        foreach (WaterfallTunnel.Leg l in WaterfallTunnel.Legs(tf))
            if (l.Kind == WaterfallTunnel.Kind.DeadEndPad) blind++;
        Assert.Equal(WorldTerrain.StepCount + blind, WorldSolids.Boxes.Count);
        Assert.Single(WorldSolids.Shapes);                                               // the maze's arches
        WorldTerrain.Waterfall f = t.Waterfalls[0];
        double bottom = t.FallShelfBottomM(f);
        Assert.NotNull(WorldSolids.Penetration(f.X, f.LipY - 20, bottom + 3));           // inside the rock shelf
        Assert.Null(WorldSolids.Penetration(f.X, f.LipY - 20, bottom - 30));             // in the slot under it
        Assert.Null(WorldSolids.Penetration(f.X, f.LipY + 20, bottom + 3));              // in front of the lip: air
        WorldSolids.Boxes.Clear();
        WorldSolids.Shapes.Clear();
        WorldTerrain.Active = null;
    }
}
