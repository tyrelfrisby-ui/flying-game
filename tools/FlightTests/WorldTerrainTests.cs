using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>The stepped-plateau world: four airports at rising elevations behind steep irregular walls,
/// flat runways, lakes at each field, a river that steps down the walls. Flat when no terrain is active.</summary>
[Collection("WorldTerrainActive")]
public class WorldTerrainTests
{
    [Fact]
    public void FlatWhenInactive()
    {
        WorldTerrain.Active = null;
        Assert.Equal(0.0, WorldTerrain.GroundHeightAt(0, -5000));
    }

    [Fact]
    public void AirportsSitOnFlatPadsAtTheirElevations()
    {
        var t = new WorldTerrain();
        foreach (WorldTerrain.Airport a in WorldTerrain.Airports)
        {
            // The pad is flat over the runways and the strips (2026-10-05: the gorge rim now runs ~130–300 m past the north end).
            for (double dx = -1600; dx <= 760; dx += 100)
            for (double dy = -700; dy <= 700; dy += 100)
            {
                Assert.Equal(a.ElevationM, t.HeightAt(a.X + dx, a.Y + dy), 3);
            }
            // ... and the gorge's rim stays clear of the runway's north end.
            double end = a.X + WorldTerrain.MainRunwayDx + WorldTerrain.RunwayLengthM / 2;
            for (double dy = -40; dy <= 40; dy += 20) Assert.True(WorldTerrain.RiverCentreX(a.Y + dy) - WorldTerrain.GorgeHalfWidthAt(a.Y + dy) > end + 80, "gorge rim within 80 m of the runway end");
        }
        Assert.Equal(WorldTerrain.DatumM, WorldTerrain.Airports[0].ElevationM);   // the Valley: 1,000 ft coastal tableland
        Assert.InRange(WorldTerrain.DatumM * 3.28084, 999, 1001);
        Assert.Equal(WorldTerrain.DatumM + WorldTerrain.StepHeightM * 3, WorldTerrain.Airports[3].ElevationM, 6);   // 5,500 ft
        Assert.InRange(WorldTerrain.StepHeightM * 3.28084, 1499, 1501);                     // 1,500 ft a step
    }

    [Fact]
    public void WallsAreSteepAndIrregular()
    {
        var t = new WorldTerrain();
        // Crossing the first wall at x=0: one full step within the escarpment width; the cliff band exceeds 60°.
        double edge = WorldTerrain.EdgeMeanY(0) + WorldTerrain.EdgeWander(0, 0);
        double low = t.BaseHeightAt(0, edge + 50), high = t.BaseHeightAt(0, edge - WorldTerrain.EscarpmentWidthM - 50);
        Assert.InRange(high - low, WorldTerrain.StepHeightM - 50, WorldTerrain.StepHeightM + 50);
        double maxSlope = 0;
        for (double y = edge; y > edge - WorldTerrain.EscarpmentWidthM; y -= 5)
        {
            maxSlope = Math.Max(maxSlope, Math.Abs(t.BaseHeightAt(0, y - 2.5) - t.BaseHeightAt(0, y + 2.5)) / 5.0);
        }
        Assert.True(maxSlope > Math.Tan(60 * Math.PI / 180), $"cliff max grade {maxSlope:F2}");
        // Irregular: the edge position differs by hundreds of metres along x.
        double e1 = WorldTerrain.EdgeWander(0, 0), e2 = WorldTerrain.EdgeWander(0, 800), e3 = WorldTerrain.EdgeWander(0, 1900);
        Assert.True(Math.Abs(e1 - e2) > 100 || Math.Abs(e2 - e3) > 100);
    }

    [Fact]
    public void LakesAndRiverHaveWater()
    {
        var t = new WorldTerrain();
        foreach (WorldTerrain.Lake l in WorldTerrain.Lakes)
        {
            double? w = t.WaterSurfaceAt(l.Cx, l.Cy);
            Assert.True(w.HasValue);
            Assert.True(t.HeightAt(l.Cx, l.Cy) < w!.Value, "lake bed must be under the water");
        }
        double y = WorldTerrain.Airports[1].Y;
        double? river = t.WaterSurfaceAt(WorldTerrain.RiverCentreX(y), y);
        Assert.True(river.HasValue && river!.Value < WorldTerrain.DatumM + WorldTerrain.StepHeightM - 5 && river.Value > WorldTerrain.DatumM + WorldTerrain.StepHeightM - 250, $"river on the Bench plateau should sit in its gorge (got {river})");
        Assert.Null(t.WaterSurfaceAt(0, 0)); // runway is dry
    }

    [Fact]
    public void GorgeDeepensDownstreamWithFallsAndGentleBends()
    {
        var t = new WorldTerrain();
        double up = WorldTerrain.GorgeUpstreamY + 300, down = WorldTerrain.GorgeDownstreamY - 300;
        double DepthAt(double y) => t.BaseHeightAt(WorldTerrain.RiverCentreX(y) + WorldTerrain.GorgeHalfWidthAt(y) + 30, y) - t.RiverSurfaceAt(y);
        Assert.InRange(DepthAt(up), 5, 40);        // ~50 ft
        Assert.InRange(DepthAt(down), 250, 320);   // ~1,000 ft: the Grand Canyon down to the sea (2026-10-05)
        // Surface is monotone non-increasing downstream (falls, never uphill) sampled every 50 m.
        double prev = double.MaxValue; int drops = 0;
        for (double y = up; y <= down; y += 50)
        {
            double s = t.RiverSurfaceAt(y);
            Assert.True(s <= prev + 1e-6, $"river flows uphill at y={y}");
            if (prev - s > 5) drops++;
            prev = s;
        }
        Assert.True(drops >= 6, $"expected a staircase of waterfalls, got {drops} drops > 5 m");
        // Gentle meander upstream: minimum radius of curvature > 1.2 km (past TwistStartY the gorge snakes on purpose —
        // SeasideTests.GorgeTwistsButIsFlyable covers that stretch).
        double minR = double.MaxValue;
        for (double y = up; y <= System.Math.Min(down, WorldTerrain.TwistStartY - 40); y += 20)
        {
            double h = 20;
            double x0 = WorldTerrain.RiverCentreX(y - h), x1 = WorldTerrain.RiverCentreX(y), x2 = WorldTerrain.RiverCentreX(y + h);
            double d1 = (x2 - x0) / (2 * h), d2 = (x2 - 2 * x1 + x0) / (h * h);
            if (Math.Abs(d2) > 1e-9) minR = Math.Min(minR, Math.Pow(1 + d1 * d1, 1.5) / Math.Abs(d2));
        }
        Assert.True(minR > 1200, $"meander too sharp: min radius {minR:F0} m");
    }

    [Fact]
    public void GearUsesTerrainHeight()
    {
        // A wheel resting on the Summit runway must see the ground at the Summit elevation: place the glider there.
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            var cfg = TestAircraftConfig.Load();
            WorldTerrain.Airport a = WorldTerrain.Airports[3];
            var state = new FlyingGame.Core.RigidBodyState(new FlyingGame.Core.MathTypes.Vec3(a.X, a.Y, -(a.ElevationM + 0.9)),
                new FlyingGame.Core.MathTypes.Quat(0, 0, 0, 1), FlyingGame.Core.MathTypes.Vec3.Zero, FlyingGame.Core.MathTypes.Vec3.Zero);
            (FlyingGame.Core.MathTypes.Vec3 f, _) = LandingGear.Compute(cfg, state, 0, 0);
            Assert.True(f.Z < -1000, $"main wheel should push up on the plateau (Fz={f.Z:F0})");
        }
        finally { WorldTerrain.Active = null; }
    }
}
