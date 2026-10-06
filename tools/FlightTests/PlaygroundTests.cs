using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>"A skate park for airplanes" (owner 2026-10-03): the canyon lake, the city built for flying, the Mall.</summary>
public class PlaygroundTests
{
    private readonly ITestOutputHelper _o;
    public PlaygroundTests(ITestOutputHelper o) { _o = o; }
    private static readonly WorldTerrain T = new();
    private static void Solids() { Landmarks.RegisterSolids(T); }

    [Fact]
    public void CanyonLakeIsLongDeepAndWalled()
    {
        double wet = 0;
        for (double y = CanyonLake.Y0; y < CanyonLake.DamY; y += 10)
        {
            double cx = CanyonLake.CentreX(y);
            if (T.WaterSurfaceAt(cx, y) == CanyonLake.SurfaceM) wet += 10;
        }
        double y0 = 0, c0 = CanyonLake.CentreX(y0), W = CanyonLake.HalfWidthAt(y0);
        _o.WriteLine($"lake {wet:F0} m long; at y=0 {2 * W:F0} m wide, floor {T.HeightAt(c0, y0):F0} m, rim {T.HeightAt(c0 + W + 15, y0):F0} m, surface {CanyonLake.SurfaceM} m");
        Assert.True(wet > 1400, $"lake only {wet:F0} m long");   // 2026-10-05: the compact world — still 3,000 ft of clear water (LakeCentreline…)
        Assert.True(T.HeightAt(c0, y0) < CanyonLake.SurfaceM - 30);
        Assert.InRange(T.HeightAt(c0 + W + 15, y0), WorldTerrain.DatumM - 3, WorldTerrain.DatumM + 3);   // sandstone rim ~57 m above the water
        // Below the dam the river is far lower than the lake.
        double below = CanyonLake.DamY + 150;
        Assert.True(T.WaterSurfaceAt(WorldTerrain.RiverCentreX(below), below) < CanyonLake.SurfaceM - 40);
        // The Valley airport and the Valley lake are untouched.
        Assert.Equal(WorldTerrain.Airports[0].ElevationM, T.HeightAt(WorldTerrain.Airports[0].X + 700, 0), 2);
        Assert.Equal(WorldTerrain.Lakes[0].SurfaceM, T.WaterSurfaceAt(WorldTerrain.Lakes[0].Cx, WorldTerrain.Lakes[0].Cy) ?? double.NaN, 3);
    }

    [Fact]
    public void LakeCentrelineIsAClearLandingRunAndTheFeaturesAreSolid()
    {
        Solids();
        double clear = 0, best = 0;
        for (double y = CanyonLake.Y0 + 150; y < CanyonLake.DamY - 120; y += 5)
        {
            double cx = CanyonLake.CentreX(y);
            bool blocked = false;
            for (double dx = -12; dx <= 12 && !blocked; dx += 6) for (double up = CanyonLake.SurfaceM + 1; up < CanyonLake.SurfaceM + 6 && !blocked; up += 2) blocked |= WorldSolids.Penetration(cx + dx, y, up) != null;
            clear = blocked ? 0 : clear + 5; best = System.Math.Max(best, clear);
        }
        _o.WriteLine($"longest clear water run on the centreline {best:F0} m; {CanyonLake.Spires.Length} spires, {CanyonLake.Arches.Length} arches");
        Assert.True(best > 914, "3,000 ft of clear water to land a floatplane");
        foreach (Spire s in CanyonLake.Spires) Assert.NotNull(WorldSolids.Penetration(s.X, s.Y, CanyonLake.SurfaceM + 20));
        foreach (NaturalArch a in CanyonLake.Arches)
        {
            double mx = 0.5 * (a.Ax + a.Bx), my = 0.5 * (a.Ay + a.By);
            Assert.Null(WorldSolids.Penetration(mx, my, a.InnerAt(0) - 8));           // fly under it
            Assert.NotNull(WorldSolids.Penetration(mx, my, a.InnerAt(0) + a.ThickM / 2));
        }
        double dcx = WorldTerrain.RiverCentreX(CanyonLake.DamY);
        Assert.NotNull(WorldSolids.Penetration(dcx, CanyonLake.DamCentreY(dcx), CanyonLake.DamCrestM - 5));
    }

    [Fact]
    public void CityHasWideAvenuesSkyBridgesRingsAndAPad()
    {
        Solids();
        var t = FlyCity.Towers();
        double minGap = double.MaxValue;
        for (int a = 0; a < t.Count; a++) for (int b = a + 1; b < t.Count; b++)
        {
            double gx = System.Math.Abs(t[a].Cx - t[b].Cx) - t[a].Hx - t[b].Hx, gy = System.Math.Abs(t[a].Cy - t[b].Cy) - t[a].Hy - t[b].Hy;
            minGap = System.Math.Min(minGap, System.Math.Max(gx, gy));
        }
        var rings = FlyCity.RooftopRings();
        _o.WriteLine($"{t.Count} towers, narrowest avenue {minGap:F0} m, {FlyCity.SkyBridges().Count} sky-bridges, tallest {FlyCity.Tallest.HeightM:F0} m, rings on {string.Join(",", rings.ConvertAll(r => $"{r.t.HeightM:F0}"))}");
        Assert.True(minGap > 95, "generous spacing");
        Assert.True(FlyCity.SkyBridges().Count >= 4);
        Assert.Equal(5, rings.Count);
        Assert.Equal(5, new HashSet<(int, int)>(rings.ConvertAll(r => (r.t.I, r.t.J))).Count);
        // The pad: 500 ft across, wheels stand on it from above, but a point under it is not lifted onto it.
        var (px, py) = FlyCity.PadCentre;
        double top = T.HeightAt(FlyCity.PadTower.Cx, FlyCity.PadTower.Cy) + FlyCity.PadDeckM;
        // (Checked through WorldDecks directly: setting the global WorldTerrain.Active here raced other test classes.)
        Assert.True(WorldDecks.DeckUnder(px + 50, py, top + 1, out double dt) && System.Math.Abs(dt - top) < 1e-6);
        Assert.False(WorldDecks.DeckUnder(px + 50, py, top - 40, out _));
        Assert.False(WorldDecks.DeckUnder(px + FlyCity.PadRadiusM + 5, py, top + 1, out _));
        Assert.Equal(152.4, 2 * FlyCity.PadRadiusM, 1);
        Assert.NotNull(WorldSolids.Penetration(px, py, top - 1));
    }

    [Fact]
    public void SpinningRingMustBeTimed()
    {
        Solids();
        var (sx, sy, cz) = FlyCity.SpinRingCentre;
        double g = T.HeightAt(sx, sy), z = g + cz;
        // Flying north (along x) through the centre: clear when the ring faces north (normal along x)...
        WorldClock.TimeS = 0;
        bool hitFaceOn = false;
        for (double dx = -20; dx <= 20; dx += 1) hitFaceOn |= WorldSolids.Penetration(sx + dx, sy, z) != null;
        // ...and when it is edge-on, a path through the centre runs into the rim at ±R.
        WorldClock.TimeS = FlyCity.SpinPeriodS / 4;
        bool hitEdgeOn = false;
        for (double dx = -60; dx <= 60; dx += 1) hitEdgeOn |= WorldSolids.Penetration(sx + dx, sy, z - FlyCity.SpinRingRadiusM) != null || WorldSolids.Penetration(sx + dx, sy + FlyCity.SpinRingRadiusM, z) != null;
        WorldClock.TimeS = 0;
        Assert.False(hitFaceOn, "face-on: the centre is clear");
        Assert.True(hitEdgeOn, "edge-on: the rim is in the way");
    }

    [Fact]
    public void MallIs3000FeetOfLandableWaterAndGrass()
    {
        Solids();
        Assert.Equal(914.4, Mall.PondLengthM, 1);
        for (double y = Mall.PondY0 + 5; y < Mall.PondY1 - 5; y += 50)
        {
            Assert.Equal(Mall.PondSurfaceM, T.WaterSurfaceAt(Mall.CentreX, y) ?? double.NaN, 3);
            Assert.True(T.HeightAt(Mall.CentreX, y) < Mall.PondSurfaceM - 1.5, "deep enough for floats");
            double gx = Mall.CentreX + Mall.PondHalfWidthM + Mall.GrassWidthM / 2;
            Assert.Equal(WorldTerrain.Surface.Grass, WorldTerrain.SurfaceAt(gx, y));
            Assert.Equal(WorldTerrain.DatumM, T.HeightAt(gx, y), 3);
            Assert.Null(WorldSolids.Penetration(Mall.CentreX, y, WorldTerrain.DatumM + 3));
            Assert.Null(WorldSolids.Penetration(gx, y, WorldTerrain.DatumM + 3));
        }
        Assert.NotNull(WorldSolids.Penetration(Mall.CentreX, Mall.MonumentY, WorldTerrain.DatumM + 150));
        Assert.Null(WorldSolids.Penetration(Mall.CentreX, Mall.MonumentY, WorldTerrain.DatumM + 175));
        Assert.NotNull(WorldSolids.Penetration(Mall.CentreX, Mall.CapitolY, WorldTerrain.DatumM + 80));
    }
}
