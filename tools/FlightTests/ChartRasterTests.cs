using FlyingGame.Core;
using FlyingGame.Core.Nav;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The three chart backgrounds. Small rasters, because the point is not the picture but the properties: that
/// north is up, that the IFR layer draws no relief, that water reads as water, and — the one that matters —
/// that nothing in the drawing path can know where the aeroplane is.
/// </summary>
[Collection("WorldTerrainActive")]
public class ChartRasterTests
{
    private readonly ITestOutputHelper _o;
    private readonly WorldTerrain _t = new();
    public ChartRasterTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = _t; }

    private const int W = 128, H = 78;

    private ChartRaster.Grid Grid() => ChartRaster.Heights(_t, W, H);

    private static (byte r, byte g, byte b) Px(byte[] rgba, int w, int col, int row)
    {
        int o = (row * w + col) * 4;
        return (rgba[o], rgba[o + 1], rgba[o + 2]);
    }

    [Fact]
    public void NorthIsUpAndEastIsRight()
    {
        var g = Grid();
        var (topX, _) = g.World(W / 2, 0);
        var (botX, _) = g.World(W / 2, H - 1);
        var (_, leftY) = g.World(0, H / 2);
        var (_, rightY) = g.World(W - 1, H / 2);
        _o.WriteLine($"top x {topX:F0}, bottom x {botX:F0}, left y {leftY:F0}, right y {rightY:F0}");
        Assert.True(topX > botX, "row 0 must be the NORTH edge");
        Assert.True(rightY > leftY, "column 0 must be the WEST edge");
    }

    [Fact]
    public void TheThreeLayersAreActuallyDifferentPictures()
    {
        var g = Grid();
        var vfr = new byte[W * H * 4];
        var ifr = new byte[W * H * 4];
        var sat = new byte[W * H * 4];
        ChartRaster.Paint(g, ChartLayer.Vfr, vfr);
        ChartRaster.Paint(g, ChartLayer.IfrLow, ifr);
        ChartRaster.Paint(g, ChartLayer.Satellite, sat);

        int vi = 0, vs = 0;
        for (int i = 0; i < W * H * 4; i += 4)
        {
            if (vfr[i] != ifr[i] || vfr[i + 1] != ifr[i + 1]) vi++;
            if (vfr[i] != sat[i] || vfr[i + 1] != sat[i + 1]) vs++;
        }
        _o.WriteLine($"VFR differs from IFR in {100.0 * vi / (W * H):F0} % of pixels, from satellite in {100.0 * vs / (W * H):F0} %");
        Assert.True(vi > W * H / 2);
        Assert.True(vs > W * H / 2);
        // Every pixel is opaque: a transparent chart would show the world through it.
        for (int i = 3; i < W * H * 4; i += 4) Assert.Equal(255, rgbaAlpha(vfr, i));
        static int rgbaAlpha(byte[] b, int i) => b[i];
    }

    [Fact]
    public void TheIfrChartDrawsNoRelief()
    {
        // A real low-altitude en-route chart has no terrain on it at all — the altitudes are printed on the
        // airways instead. So the IFR raster must use only a handful of colours: land, water, shoreline.
        var g = Grid();
        var ifr = new byte[W * H * 4];
        ChartRaster.Paint(g, ChartLayer.IfrLow, ifr);
        var seen = new HashSet<(byte, byte, byte)>();
        for (int row = 0; row < H; row++)
            for (int col = 0; col < W; col++) seen.Add(Px(ifr, W, col, row));
        _o.WriteLine($"the IFR layer uses {seen.Count} colours: {string.Join(" ", seen.Select(c => $"({c.Item1},{c.Item2},{c.Item3})"))}");
        Assert.True(seen.Count <= 3, $"the IFR chart is drawing relief: {seen.Count} colours");

        // And the VFR one must use many, because that IS its job.
        var vfr = new byte[W * H * 4];
        ChartRaster.Paint(g, ChartLayer.Vfr, vfr);
        var vseen = new HashSet<(byte, byte, byte)>();
        for (int row = 0; row < H; row++)
            for (int col = 0; col < W; col++) vseen.Add(Px(vfr, W, col, row));
        _o.WriteLine($"the VFR layer uses {vseen.Count} colours");
        Assert.True(vseen.Count >= 6, $"the VFR chart should show the shape of the ground: only {vseen.Count} colours");
    }

    [Fact]
    public void TheSeaIsBlueOnEveryLayerThatDrawsGround()
    {
        var g = Grid();
        // A pixel well out to sea: east of the coast, at the latitude of the Valley field.
        int col = -1, row = -1;
        for (int r = 0; r < H && col < 0; r++)
            for (int c = 0; c < W; c++)
            {
                var (x, y) = g.World(c, r);
                if (System.Math.Abs(x - 400) < 300 && y > 8000) { col = c; row = r; break; }
            }
        Assert.True(col >= 0, "the chart does not reach the open sea");

        foreach (ChartLayer l in new[] { ChartLayer.Vfr, ChartLayer.IfrLow, ChartLayer.Satellite })
        {
            var buf = new byte[W * H * 4];
            ChartRaster.Paint(g, l, buf);
            var p = Px(buf, W, col, row);
            _o.WriteLine($"{l}: the open sea is ({p.r},{p.g},{p.b})");
            Assert.True(p.b > p.r, $"{l} draws the sea with more red than blue");
        }
    }

    [Fact]
    public void TheHighPeaksReadHigherThanTheCoast()
    {
        var g = Grid();
        double coast = double.MaxValue, peak = double.MinValue;
        for (int row = 0; row < H; row++)
            for (int col = 0; col < W; col++)
            {
                double h = g.GroundM[row * W + col];
                if (h > peak) peak = h;
                if (h < coast) coast = h;
            }
        _o.WriteLine($"the sampled grid spans {coast:F0} m to {peak:F0} m ({peak * Terps.FtPerM:F0} ft)");
        // The world's own numbers: sea level to the 17,000 ft peak in the Alaska range.
        Assert.True(coast <= 0.5, $"the chart never reaches the sea: lowest {coast:F0} m");
        Assert.True(peak > 4000, $"the chart misses the high country: highest {peak:F0} m");
    }

    [Fact]
    public void NothingInTheDrawingPathCanKnowWhereTheAeroplaneIs()
    {
        // The owner's rule, asserted against the API rather than against a picture: every public entry point
        // into the chart drawing takes a rectangle of the world and the terrain, and NOTHING takes an
        // aircraft, a position, or anything that could be used as one. If somebody adds such a parameter to
        // put a little triangle on the map, this test is where they will find out it was deliberate.
        var bad = new List<string>();
        foreach (var m in typeof(ChartRaster).GetMethods(System.Reflection.BindingFlags.Public
                                                         | System.Reflection.BindingFlags.Static))
        {
            foreach (var p in m.GetParameters())
            {
                string n = p.Name?.ToLowerInvariant() ?? "";
                string ty = p.ParameterType.Name.ToLowerInvariant();
                if (n.Contains("aircraft") || n.Contains("ownship") || n.Contains("position") || n.Contains("driver")
                    || ty.Contains("aircraft") || ty.Contains("vec3"))
                    bad.Add($"{m.Name}({p.ParameterType.Name} {p.Name})");
            }
        }
        _o.WriteLine(bad.Count == 0
            ? "ChartRaster still cannot find out where you are"
            : "something has handed the chart the aeroplane: " + string.Join(", ", bad));
        Assert.True(bad.Count == 0,
                    "the chart must not be able to learn the aeroplane's position — that is the whole point of it: "
                    + string.Join(", ", bad));
    }
}
