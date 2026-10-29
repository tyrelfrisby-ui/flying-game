using System.Collections.Generic;
using System.Diagnostics;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The cities' low-rise infill as something you crash into (owner 2026-10-29). The visual builder places the
/// houses, terraces, shops and warehouses and registers each one through <see cref="UrbanLowRise"/>; these
/// tests own the other half — the roof a box with a gable really has, the ground a building may not stand
/// on, and the cost of asking five hundred of them a question twenty times a step.
/// </summary>
public class UrbanLowRiseTests
{
    private readonly ITestOutputHelper _o;
    public UrbanLowRiseTests(ITestOutputHelper o) { _o = o; }
    private static readonly WorldTerrain T = new();
    /// <summary>Open ground, well outside every airport pad and city — the keep-clear rules are staked out of
    /// Core's own data the moment anything asks to build, so a fixture dropped on the Valley runway is
    /// refused and the test is testing nothing.</summary>
    private const double Ex = 20000, Ey = 20000;

    /// <summary>A terrace off any pad: 30 m along x, 16 m across y, eaves at 7 m, 36° gable ridged along x.</summary>
    private static (double cx, double cy) Terrace(double cx, double cy, double ground = 100)
    {
        double ridge = 8 * System.Math.Tan(36 * System.Math.PI / 180);
        Assert.True(UrbanLowRise.Add(cx, cy, 15, 8, ground, 7, ridge, ridgeAlongX: true), "the fixture was refused — it is not on open ground");
        return (cx, cy);
    }

    [Fact]
    public void AnAeroplaneThatFliesIntoAHouseHitsIt()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        Terrace(Ex, Ey);
        double ridge = 8 * System.Math.Tan(36 * System.Math.PI / 180);
        _o.WriteLine($"eaves 107.0 m, ridge {100 + 7 + ridge:F2} m");

        // Through a wall at eaves height: pushed out sideways, not up.
        var side = UrbanLowRise.Penetration(Ex + 14.0, Ey, 104);
        Assert.NotNull(side);
        Assert.Equal(1.0, side!.Value.normalNed.X, 6);
        Assert.Equal(1.0, side.Value.depth, 6);

        // Under the ridge and over the eaves is inside the roof; a metre outside the gable is not.
        Assert.NotNull(UrbanLowRise.Penetration(Ex, Ey, 107 + ridge - 0.2));
        Assert.Null(UrbanLowRise.Penetration(Ex, Ey, 107 + ridge + 0.2));
        Assert.Null(UrbanLowRise.Penetration(Ex, Ey + 7.9, 107 + ridge - 0.2));   // out at the eaves, the roof is low
        Assert.Null(UrbanLowRise.Penetration(Ex, Ey + 8.1, 104));                 // outside the wall altogether
        Assert.NotNull(UrbanLowRise.Penetration(Ex, Ey, 99.9));               // the walls are footed into the ground
        Assert.Null(UrbanLowRise.Penetration(Ex, Ey, 100 - UrbanLowRise.FootingM - 0.1));

        // A wheel a metre up inside a wall is thrown out SIDEWAYS, never down through the floor.
        var low = UrbanLowRise.Penetration(Ex + 14.9, Ey, 101);
        Assert.NotNull(low);
        Assert.Equal(0.0, low!.Value.normalNed.Z, 6);

        // Clipping the roof throws the wing off the slope, not straight up: the normal leans the way the
        // roof falls, and it leans the other way on the other side of the ridge.
        var roof = UrbanLowRise.Penetration(Ex, Ey + 4, 100 + 7 + ridge * 0.5 - 0.1);
        Assert.NotNull(roof);
        Assert.True(roof!.Value.normalNed.Z < 0, "a roof strike must push UP (z is down)");
        Assert.True(roof.Value.normalNed.Y > 0.3, $"and off the south slope, not straight up: {roof.Value.normalNed.Y:F3}");
        var other = UrbanLowRise.Penetration(Ex, Ey - 4, 100 + 7 + ridge * 0.5 - 0.1);
        Assert.NotNull(other);
        Assert.Equal(-roof.Value.normalNed.Y, other!.Value.normalNed.Y, 6);

        // A flat-roofed building is pushed straight up.
        UrbanLowRise.Clear();
        Assert.True(UrbanLowRise.Add(Ex, Ey, 15, 9, 100, 5, 0, ridgeAlongX: true));
        var flat = UrbanLowRise.Penetration(Ex, Ey, 104.9);
        Assert.NotNull(flat);
        Assert.Equal(-1.0, flat!.Value.normalNed.Z, 6);
        Assert.Equal(0.1, flat.Value.depth, 6);
        UrbanLowRise.Clear();
    }

    [Fact]
    public void TheGableRunsTheLengthOfTheBuildingWhicheverWayItLies()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        // Long along y: the ridge runs along y and the roof falls away in x.
        double ridge = 8 * System.Math.Tan(36 * System.Math.PI / 180);
        Assert.True(UrbanLowRise.Add(Ex, Ey, 8, 15, 100, 7, ridge, ridgeAlongX: false));
        Assert.NotNull(UrbanLowRise.Penetration(Ex, Ey + 12, 107 + ridge - 0.2));   // high under the ridge, way down the length
        Assert.Null(UrbanLowRise.Penetration(Ex + 7.9, Ey, 107 + ridge - 0.2));     // low at the eaves
        UrbanLowRise.Clear();
    }

    [Fact]
    public void NothingIsBuiltOnAPadUnderARingOrOnAFinal()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        UrbanLowRise.Stake();          // the airports, and every city's pad and rings
        int before = UrbanLowRise.Refused;
        var clears = new List<UrbanLowRise.KeepClear>(UrbanLowRise.KeepClears);
        Assert.True(clears.Count >= 3 * WorldTerrain.Airports.Length, $"only {clears.Count} keep-clear regions");

        // On the Valley's main runway, beside it, and on the final off each end: all refused.
        WorldTerrain.Airport v = WorldTerrain.Airports[0];
        WorldTerrain.Strip main = WorldTerrain.AirportStrips[0];
        double rx = v.X + main.Dx, half = main.Length / 2;
        Assert.False(UrbanLowRise.Add(rx, v.Y, 9, 8, v.ElevationM, 7, 5, true));
        Assert.False(UrbanLowRise.Add(rx, v.Y + main.Width / 2 + 60, 9, 8, v.ElevationM, 7, 5, true));
        Assert.False(UrbanLowRise.Add(rx + half + 600, v.Y, 9, 8, v.ElevationM, 7, 5, true));
        Assert.False(UrbanLowRise.Add(rx - half - 600, v.Y, 9, 8, v.ElevationM, 7, 5, true));
        // The grass strip and the crosswind runway are kept clear too.
        Assert.False(UrbanLowRise.Add(v.X - 100, v.Y - 420, 9, 8, v.ElevationM, 7, 5, true));
        Assert.False(UrbanLowRise.Add(v.X + WorldTerrain.XwindRunwayDx, v.Y + 400, 9, 8, v.ElevationM, 7, 5, true));
        // The pad's far corner is a field, not a landing surface: the visual builder may use it.
        Assert.True(UrbanLowRise.Add(v.X - WorldTerrain.PadSouthX + 120, v.Y + WorldTerrain.PadHalfY - 120, 9, 8, v.ElevationM, 7, 5, true));
        // And so is open ground beside a final.
        Assert.True(UrbanLowRise.Add(rx + half + 600, v.Y + UrbanLowRise.ApproachHalfWidthM + 60, 9, 8, v.ElevationM, 7, 5, true));

        // The city's own landing pad and the ground under every ring.
        CityGrid city = ValleyCity.At(0);
        var (px, py) = city.PadCentre;
        Assert.False(UrbanLowRise.Add(px, py, 9, 8, T.HeightAt(px, py), 7, 5, true));
        foreach (var (tw, _) in city.RooftopRings())
            Assert.False(UrbanLowRise.Add(tw.Cx, tw.Cy, 9, 8, T.HeightAt(tw.Cx, tw.Cy), 7, 5, true));
        var (sx, sy, _) = city.SpinRingCentre;
        Assert.False(UrbanLowRise.Add(sx, sy, 9, 8, T.HeightAt(sx, sy), 7, 5, true));

        _o.WriteLine($"{clears.Count} keep-clear regions; {UrbanLowRise.Refused - before} buildings refused in this test");
        Assert.True(UrbanLowRise.Refused - before >= 6 + 1 + 5 + 1);
        foreach (var kv in UrbanLowRise.RefusedBy) _o.WriteLine($"  refused by '{kv.Key}': {kv.Value}");
        UrbanLowRise.Clear();
    }

    [Fact]
    public void ARefusedBuildingIsNotInTheWorldAtAll()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        Landmarks.RegisterSolids(T);
        WorldTerrain.Airport v = WorldTerrain.Airports[0];
        double rx = v.X + WorldTerrain.AirportStrips[0].Dx;
        Assert.False(UrbanLowRise.Add(rx, v.Y, 9, 8, v.ElevationM, 7, 5, true));
        // Nothing of it survives: not in the list, and not in the way of an aeroplane on the runway.
        foreach (UrbanLowRise.Block bl in UrbanLowRise.Blocks)
            Assert.False(System.Math.Abs(bl.Cx - rx) < 9 && System.Math.Abs(bl.Cy - v.Y) < 8);
        Assert.Null(WorldSolids.Penetration(rx, v.Y, v.ElevationM + 3));
        UrbanLowRise.Clear();
    }

    [Fact]
    public void TheLowRiseIsOnTheWorldsSolidsNotJustItsOwnList()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        Landmarks.RegisterSolids(T);
        // Somewhere empty, well away from anything the world already owns.
        double x = 9000, y = -9000, g = T.HeightAt(x, y);
        Assert.Null(WorldSolids.Penetration(x, y, g + 4));
        Assert.True(UrbanLowRise.Add(x, y, 15, 8, g, 7, 5, true));
        var hit = WorldSolids.Penetration(x, y, g + 4);
        Assert.NotNull(hit);
        Assert.True(hit!.Value.normalNed.Z <= 0, "a hit inside a house never pushes DOWN into the ground");
        _o.WriteLine($"through WorldSolids at {g + 4:F0} m: depth {hit.Value.depth:F2} m, normal {hit.Value.normalNed.X:F2},{hit.Value.normalNed.Y:F2},{hit.Value.normalNed.Z:F2}");
        UrbanLowRise.Clear();
    }

    [Fact]
    public void FiveHundredBuildingsCostTheStepNothing()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        // Three cities' worth, on a 170 m block lattice with six rows of seven houses in each block —
        // the shape the builder actually lays down.
        int n = 0;
        for (int city = 0; city < 3; city++)
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 16 && n < 1500; j++)
                    for (int row = 0; row < 6; row++)
                        for (int col = 0; col < 7 && n < 1500; col++)
                        {
                            double cx = Ex + city * 6000 + i * 170 - 52.5 + row * 21;
                            double cy = Ey + j * 170 - 54 + col * 18;
                            if (UrbanLowRise.Add(cx, cy, 7, 5, 100, 6, 3.6, true)) n++;
                        }
        Assert.True(n >= 1500, $"only {n} buildings registered");

        // Twenty hard points, five hundred steps: the same question the contact code asks every step.
        var pts = new List<(double x, double y, double up)>();
        var rng = new System.Random(7);
        for (int k = 0; k < 20; k++) pts.Add((Ex + rng.NextDouble() * 18000 - 500, Ey + rng.NextDouble() * 2800 - 100, 100 + rng.NextDouble() * 12));
        UrbanLowRise.Penetration(Ex, Ey, 103);   // build the index outside the timing
        var sw = Stopwatch.StartNew();
        int hits = 0;
        for (int step = 0; step < 500; step++)
            foreach (var (x, y, up) in pts)
                if (UrbanLowRise.Penetration(x, y, up) is not null) hits++;
        sw.Stop();
        double perStepUs = sw.Elapsed.TotalMilliseconds * 1000 / 500;
        _o.WriteLine($"{n} buildings, {pts.Count} hard points: {perStepUs:F1} us per step ({hits} hits)");
        // A 500 Hz step has 2,000 us. A linear scan of 1,500 buildings would be most of one.
        Assert.True(perStepUs < 60, $"{perStepUs:F1} us per step for {n} buildings");
        UrbanLowRise.Clear();
    }

    [Fact]
    public void TheIndexFindsEveryBuildingAPlainScanWould()
    {
        UrbanLowRise.Clear();
        WorldTerrain.Active ??= T;
        var rng = new System.Random(11);
        for (int k = 0; k < 400; k++)
            UrbanLowRise.Add(Ex + rng.NextDouble() * 800, Ey + rng.NextDouble() * 800 - 400, 4 + rng.NextDouble() * 12,
                4 + rng.NextDouble() * 9, 100, 3 + rng.NextDouble() * 6, rng.NextDouble() * 5, k % 2 == 0);
        int disagreements = 0, found = 0;
        for (int k = 0; k < 40000; k++)
        {
            double x = Ex + rng.NextDouble() * 800, y = Ey + rng.NextDouble() * 800 - 400, up = 99 + rng.NextDouble() * 18;
            bool indexed = UrbanLowRise.Penetration(x, y, up) is not null;
            bool scanned = false;
            foreach (UrbanLowRise.Block bl in UrbanLowRise.Blocks)
                if (System.Math.Abs(x - bl.Cx) <= bl.Hx && System.Math.Abs(y - bl.Cy) <= bl.Hy
                    && up >= bl.Base && up <= bl.RoofAt(x, y)) { scanned = true; break; }
            if (indexed != scanned) disagreements++;
            if (scanned) found++;
        }
        _o.WriteLine($"{found} of 40,000 samples inside a building; {disagreements} disagreements with a plain scan");
        Assert.True(found > 1500, $"only {found} samples landed in a building — the test is not testing anything");
        Assert.Equal(0, disagreements);
        UrbanLowRise.Clear();
    }
}
