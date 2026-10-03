using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Combat;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The seaside world (owner 2026-10-03): coast, gorge to the sea, Golden Gate, city, Catalina-like island with
/// its runway, sea arch, Avalon and the sea cave. Geometry facts a pilot would rely on.</summary>
public class SeasideTests
{
    private readonly ITestOutputHelper _o;
    public SeasideTests(ITestOutputHelper o) { _o = o; }
    private static readonly WorldTerrain T = new();

    [Fact]
    public void RiverReachesTheSeaAtSeaLevelThroughCliffs()
    {
        double yb = GoldenGate.Y, xb = GoldenGate.CentreX;
        double surface = T.RiverSurfaceAt(yb);
        _o.WriteLine($"bridge line y {yb:F0}, river x {xb:F0}, river surface {surface:F1} m, gorge half-width {WorldTerrain.GorgeHalfWidthAt(yb):F0}");
        Assert.InRange(surface, -0.5, 6);
        // A kilometre inland of the cliffs the coast is the 500 ft tableland; past the beach it is sea.
        for (double x = -3000; x <= 6000; x += 1000)
        {
            double shore = Coast.ShoreY(x);
            if (System.Math.Abs(x - WorldTerrain.RiverCentreX(shore)) < 700) continue;   // the gorge mouth
            Assert.InRange(T.HeightAt(x, shore - 400), WorldTerrain.DatumM - 3, WorldTerrain.DatumM + 30);
            Assert.True(T.HeightAt(x, shore + 200) < -5, $"sea floor at x={x}");
            Assert.Equal(Coast.SeaLevelM, T.WaterSurfaceAt(x, shore + 200));
        }
    }

    [Fact]
    public void GorgeTwistsButIsFlyable()
    {
        double minR = double.MaxValue, at = 0;
        for (double y = WorldTerrain.TwistStartY; y < GoldenGate.Y; y += 10)
        {
            double x0 = WorldTerrain.RiverCentreX(y - 10), x1 = WorldTerrain.RiverCentreX(y), x2 = WorldTerrain.RiverCentreX(y + 10);
            double d1 = (x2 - x0) / 20, d2 = (x2 - 2 * x1 + x0) / 100;
            double r = System.Math.Pow(1 + d1 * d1, 1.5) / System.Math.Max(1e-9, System.Math.Abs(d2));
            if (r < minR) { minR = r; at = y; }
        }
        _o.WriteLine($"tightest centreline radius {minR:F0} m at y={at:F0}");
        Assert.InRange(minR, 150, 400);
    }

    [Fact]
    public void GoldenGateSpansTheMouth()
    {
        double y = GoldenGate.Y, cx = GoldenGate.CentreX, half = GoldenGate.HalfMainSpanM;
        double under = T.HeightAt(cx, y), endL = T.HeightAt(cx - half - 250, y), endR = T.HeightAt(cx + half + 250, y);
        _o.WriteLine($"main span {2 * half:F0} m, deck {GoldenGate.DeckTopM:F0} m, towers {GoldenGate.TowerTopM:F0} m; ground under midspan {under:F1}, beyond the towers {endL:F0} / {endR:F0}");
        Assert.True(under < 0, "water under the middle of the bridge");
        Assert.InRange(T.WaterSurfaceAt(cx, y) ?? double.NaN, Coast.SeaLevelM, Coast.SeaLevelM + 3);   // the river's last reach, at the sea
        Assert.InRange(2 * half, 450, 900);
        Assert.InRange(endL, GoldenGate.DeckTopM - 15, GoldenGate.DeckTopM + 20);   // the deck lands on the cliff tops
        Assert.InRange(endR, GoldenGate.DeckTopM - 15, GoldenGate.DeckTopM + 20);
        // Solid: the deck and a tower; clear: under the deck mid-span.
        Landmarks.RegisterSolids(T);
        Assert.NotNull(WorldSolids.Penetration(cx, y, GoldenGate.DeckTopM - 3));
        Assert.NotNull(WorldSolids.Penetration(cx + half, y + GoldenGate.DeckHalfWidthM + 5, 100));
        Assert.Null(WorldSolids.Penetration(cx, y, 60));
    }

    [Fact]
    public void CityStandsOnFlatDryGroundInsideTheCombatZone()
    {
        var b = SeaCity.Buildings();
        double maxH = 0;
        foreach (var bd in b)
        {
            double g = T.HeightAt(bd.Cx, bd.Cy);
            Assert.InRange(g, WorldTerrain.DatumM - 3, WorldTerrain.DatumM + 8);
            Assert.True(bd.Cx > CombatZone.X0 && bd.Cx < CombatZone.X1 && bd.Cy > CombatZone.Y0 && bd.Cy < CombatZone.Y1);
            maxH = System.Math.Max(maxH, bd.HeightM);
        }
        _o.WriteLine($"{b.Count} buildings, tallest {maxH:F0} m");
        Assert.True(b.Count > 80);
        Assert.True(CombatZone.Inside(new Vec3(GoldenGate.CentreX, GoldenGate.Y, -(WorldTerrain.DatumM + 200))), "bridge inside the zone");
        foreach (var gt in CombatZone.BuildGroundTargets())
            Assert.Null(WorldSolids.Penetration(gt.X, gt.Y, T.HeightAt(gt.X, gt.Y) + 1));
    }

    [Fact]
    public void IslandHasItsRunwayArchAvalonAndHarborOffshore()
    {
        double rwy = T.HeightAt(Island.RunwayX, Island.RunwayY);
        double peak = 0;
        for (double x = Island.MinX; x < Island.MaxX; x += 50) for (double y = Island.MinY; y < Island.MaxY; y += 50) peak = System.Math.Max(peak, T.HeightAt(x, y));
        _o.WriteLine($"runway {rwy * 3.28084:F0} ft, peak {peak * 3.28084:F0} ft, arch {SeaArch.X0:F0}..{SeaArch.X1:F0} at y {SeaArch.Cy:F0}");
        for (double dx = -Island.RunwayLengthM / 2; dx <= Island.RunwayLengthM / 2; dx += 100)
            Assert.InRange(T.HeightAt(Island.RunwayX + dx, Island.RunwayY), Island.RunwayElevM - 0.5, Island.RunwayElevM + 0.5);
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(Island.RunwayX, Island.RunwayY));
        Assert.InRange(peak, 450, 700);
        // The arch: water under it all the way across the channel centre, a big opening, rock abutments either side.
        Assert.True(T.HeightAt(SeaArch.Cx, SeaArch.Cy) < -3, "channel under the arch");
        Assert.InRange(SeaArch.InnerAt(SeaArch.Cx), 120, 200);
        Assert.True(SeaArch.X1 - SeaArch.X0 > 200);
        Assert.True(T.HeightAt(SeaArch.X0 - 150, SeaArch.Cy) > 60 && T.HeightAt(SeaArch.X1 + 150, SeaArch.Cy) > 60, "abutments");
        // Avalon: water in the cove, the Casino on dry ground at the point.
        Assert.True(T.HeightAt(Island.AvalonX, Island.AvalonY) < -5);
        var (cx, cy) = Island.CasinoXY;
        double cg = T.HeightAt(cx, cy);
        _o.WriteLine($"Avalon cove centre ({Island.AvalonX:F0},{Island.AvalonY:F0}) depth {T.HeightAt(Island.AvalonX, Island.AvalonY):F1}; Casino ground {cg:F1} m");
        Assert.InRange(cg, 0.5, 60);
        // Harbor (H-4) is open sea, deep enough for a hull.
        var hb = WorldTerrain.Harbor;
        for (double u = -1; u <= 1; u += 0.25)
        {
            double x = hb.Cx + u * hb.Rx;
            Assert.Equal(0.0, T.WaterSurfaceAt(x, hb.Cy) ?? double.NaN, 3);
            Assert.True(T.HeightAt(x, hb.Cy) < -8, $"harbor depth at x={x:F0}: {T.HeightAt(x, hb.Cy):F1}");
        }
    }

    [Fact]
    public void SeaCaveCanBeTaxiedIntoAndHasABeachAndASkylight()
    {
        double cx = SeaCave.Cx, cy = SeaCave.Cy;
        _o.WriteLine($"cave centre ({cx:F0},{cy:F0}); shore y {Island.WestShoreY(cx):F0}");
        // Mouth: on the sea side of the chamber there is water and NO roof up to ~20 m.
        double my = cy - SeaCave.RadiusM * 0.5;   // inside the seaward wall
        Landmarks.RegisterSolids(T);
        double mouthTop = 60;
        for (double up = 1; up < 60; up += 1) { if (WorldSolids.Penetration(cx, my, up) != null) { mouthTop = up; break; } }
        double mouthW = 0;
        for (double dx = -60; dx <= 60; dx += 2) if (T.WaterSurfaceAt(cx + dx, my) == 0 && WorldSolids.Penetration(cx + dx, my, 6) == null) mouthW += 2;
        _o.WriteLine($"mouth: clear to {mouthTop:F0} m above the water, {mouthW:F0} m wide at 6 m");
        Assert.True(mouthTop > 12, "mouth tall enough for a floatplane's tail");
        Assert.True(mouthW > 20, "mouth wide enough for a Beaver (15 m span)");
        // Inside: water in the chamber, roof overhead.
        Assert.Equal(0.0, T.WaterSurfaceAt(cx, cy - 20) ?? double.NaN, 3);
        Assert.NotNull(WorldSolids.Penetration(cx, cy - 20, SeaCave.CeilingAt(cx, cy - 20) + 1.5));
        Assert.Null(WorldSolids.Penetration(cx, cy - 20, 20));
        // Beach at the back: dry sand above the water.
        double beach = T.HeightAt(cx, cy + SeaCave.RadiusM * 0.6);
        Assert.InRange(beach, 0.3, 3);
        Assert.True(SeaCave.IsSand(cx, cy + SeaCave.RadiusM * 0.6));
        // Skylight: open sky straight up through the roof.
        var (sx, sy) = SeaCave.Skylight;
        for (double up = 5; up < 200; up += 5) Assert.Null(WorldSolids.Penetration(sx, sy, up));
        Assert.True((Island.HeightAt(sx, sy, false) ?? 0) > SeaCave.CeilingAt(sx, sy), "the skylight is a hole through rock");
    }
}
