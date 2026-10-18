namespace FlyingGame.Core.Nav;

/// <summary>The three things the owner asked to be able to look at.</summary>
public enum ChartLayer
{
    /// <summary>A sectional: hypsometric tints and contours, so you can see the shape of the ground.</summary>
    Vfr,
    /// <summary>A low-altitude en-route chart: no relief at all, because a real one does not draw any.</summary>
    IfrLow,
    /// <summary>What it looks like from above — surfaces and shaded relief.</summary>
    Satellite,
}

/// <summary>
/// The background of each chart, drawn from the terrain into a block of pixels. The symbology — airports,
/// navaids, airways, fixes, minimum altitudes — is drawn over the top by the panel, because lines and text
/// want to stay sharp when you zoom and a raster does not.
///
/// Owner, 2026-10-18: *"create an ifr low chart to go along with the vfr chart and a 'satalite image' so the
/// user can see all three but no gps type positions ever."* Nothing in this file knows where the aeroplane
/// is. It cannot: it is handed a rectangle of the world and returns pixels, and there is no aircraft in its
/// arguments. That is deliberate — the one feature this map must never grow is a little triangle showing
/// where you are, and the cheapest way to guarantee that is to give the drawing code no way to find out.
///
/// COST. One <see cref="WorldTerrain.HeightAt"/> per pixel, and that is the expensive call — about 600,000 of
/// them for a chart of this world at 26 m per pixel. So a chart is built ONCE, off the main thread, and the
/// height grid is sampled once and shared by all three layers. <see cref="Heights"/> is separated from
/// <see cref="Paint"/> for exactly that reason: sample once, paint three times.
/// </summary>
public static class ChartRaster
{
    /// <summary>The chart's extent. Wider than the drawn mesh so the coastline is not cut off.</summary>
    public const double XMin = -7200, XMax = 9200, YMin = -15600, YMax = 11000;

    /// <summary>The renderer's own snow line: white from 2,500 m, fully white by 3,100 m (WorldBuilder).</summary>
    public const double SnowFromM = 2500, SnowToM = 3100;

    /// <summary>Contour interval, feet. 500 ft is what a sectional uses in country like this.</summary>
    public const double ContourFt = 500;

    /// <summary>Sea level, so water reads as water.</summary>
    public const double SeaLevelM = 0.0;

    /// <summary>What was sampled at each pixel: enough for all three layers, sampled once.</summary>
    public sealed class Grid
    {
        public int W, H;
        public double X0, Y0, X1, Y1;
        public double[] GroundM = System.Array.Empty<double>();
        /// <summary>Water surface where there is water, else NaN.</summary>
        public double[] WaterM = System.Array.Empty<double>();
        public byte[] Surface = System.Array.Empty<byte>();     // (byte)WorldTerrain.Surface

        /// <summary>World position of a pixel. North is UP, so row 0 is the greatest x.</summary>
        public (double x, double y) World(int col, int row)
            => (X1 - (X1 - X0) * (row + 0.5) / H, Y0 + (Y1 - Y0) * (col + 0.5) / W);

        public double MetresPerPixelY => (Y1 - Y0) / W;
        public double MetresPerPixelX => (X1 - X0) / H;
    }

    /// <summary>
    /// Sample the world into a grid. <paramref name="rowDone"/> is called after each row so a caller can
    /// show progress — this takes seconds, and a frozen screen is worse than a bar.
    /// </summary>
    public static Grid Heights(WorldTerrain t, int w, int h,
                               double x0 = XMin, double y0 = YMin, double x1 = XMax, double y1 = YMax,
                               System.Action<int>? rowDone = null)
    {
        var g = new Grid
        {
            W = w, H = h, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1,
            GroundM = new double[w * h], WaterM = new double[w * h], Surface = new byte[w * h],
        };
        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                var (x, y) = g.World(col, row);
                int i = row * w + col;
                g.GroundM[i] = t.HeightAt(x, y, false, false);
                double? water = t.WaterSurfaceAt(x, y);
                g.WaterM[i] = water ?? double.NaN;
                g.Surface[i] = (byte)WorldTerrain.SurfaceAt(x, y);
            }
            rowDone?.Invoke(row);
        }
        return g;
    }

    /// <summary>Paint one layer from a sampled grid into RGBA32, four bytes a pixel, row 0 at the TOP.</summary>
    public static void Paint(Grid g, ChartLayer layer, byte[] rgba)
    {
        for (int row = 0; row < g.H; row++)
            for (int col = 0; col < g.W; col++)
            {
                int i = row * g.W + col;
                (byte r, byte gr, byte b) c = layer switch
                {
                    ChartLayer.Vfr => VfrPixel(g, col, row),
                    ChartLayer.IfrLow => IfrPixel(g, col, row),
                    _ => SatellitePixel(g, col, row),
                };
                int o = i * 4;
                rgba[o] = c.r; rgba[o + 1] = c.gr; rgba[o + 2] = c.b; rgba[o + 3] = 255;
            }
    }

    private static double At(Grid g, int col, int row)
        => g.GroundM[System.Math.Clamp(row, 0, g.H - 1) * g.W + System.Math.Clamp(col, 0, g.W - 1)];

    private static bool IsWater(Grid g, int col, int row)
    {
        int i = System.Math.Clamp(row, 0, g.H - 1) * g.W + System.Math.Clamp(col, 0, g.W - 1);
        return !double.IsNaN(g.WaterM[i]) && g.WaterM[i] >= g.GroundM[i] - 0.25;
    }

    // ---------------------------------------------------------------- VFR

    /// <summary>Sectional hypsometric tints: the paler the ground the lower it is, by 2,000 ft bands.</summary>
    private static readonly (double upToFt, byte r, byte g, byte b)[] Tints =
    {
        ( 1000, 222, 235, 214),
        ( 2000, 233, 235, 205),
        ( 3000, 240, 230, 195),
        ( 5000, 240, 219, 178),
        ( 7000, 236, 203, 155),
        ( 9000, 229, 184, 135),
        (11000, 219, 163, 116),
        (99000, 205, 142, 100),
    };

    private static (byte, byte, byte) VfrPixel(Grid g, int col, int row)
    {
        if (IsWater(g, col, row)) return (150, 186, 210);
        double ft = At(g, col, row) * Terps.FtPerM;
        // A contour line wherever the 500 ft band changes between this pixel and the one north or east of
        // it: a cheap and exact way to draw contours from a height field, and it never leaves a gap.
        int band = (int)System.Math.Floor(ft / ContourFt);
        if (band != (int)System.Math.Floor(At(g, col, row - 1) * Terps.FtPerM / ContourFt)
            || band != (int)System.Math.Floor(At(g, col + 1, row) * Terps.FtPerM / ContourFt))
        {
            // Every fifth contour is an index contour and darker, as on a real chart.
            bool index = band % 5 == 0;
            return index ? ((byte)140, (byte)92, (byte)56) : ((byte)178, (byte)132, (byte)94);
        }
        foreach (var t in Tints) if (ft < t.upToFt) return (t.r, t.g, t.b);
        return (205, 142, 100);
    }

    // ---------------------------------------------------------------- IFR low

    private static (byte, byte, byte) IfrPixel(Grid g, int col, int row)
    {
        // A low-altitude en-route chart draws NO relief: the altitudes are printed on the airways instead,
        // which is the whole point of them. Water and the coastline are all the ground there is.
        if (IsWater(g, col, row))
        {
            bool shore = !IsWater(g, col + 1, row) || !IsWater(g, col - 1, row)
                      || !IsWater(g, col, row + 1) || !IsWater(g, col, row - 1);
            return shore ? ((byte)120, (byte)150, (byte)170) : ((byte)214, (byte)228, (byte)238);
        }
        return (242, 243, 239);
    }

    // ---------------------------------------------------------------- satellite

    private static (byte, byte, byte) SatellitePixel(Grid g, int col, int row)
    {
        int i = row * g.W + col;
        if (IsWater(g, col, row))
        {
            // Deeper water reads darker: the sea against a lake is most of what a coast looks like.
            double depth = g.WaterM[i] - g.GroundM[i];
            double f = System.Math.Clamp(depth / 25.0, 0, 1);
            return (Lerp(58, 24, f), Lerp(104, 58, f), Lerp(132, 96, f));
        }

        double h = g.GroundM[i];
        // The ground gradient, from the neighbours, for the shading and for the snow's slope test.
        double dzdx = (At(g, col, row + 1) - At(g, col, row - 1)) / (2 * g.MetresPerPixelX);   // +x is north = up the image
        double dzdy = (At(g, col + 1, row) - At(g, col - 1, row)) / (2 * g.MetresPerPixelY);
        double slope = System.Math.Sqrt(dzdx * dzdx + dzdy * dzdy);

        (byte r, byte gr, byte b) c = (WorldTerrain.Surface)g.Surface[i] switch
        {
            WorldTerrain.Surface.Paved => ((byte)62, (byte)62, (byte)66),
            WorldTerrain.Surface.Gravel => ((byte)140, (byte)132, (byte)120),
            WorldTerrain.Surface.Grass => ((byte)88, (byte)122, (byte)62),
            WorldTerrain.Surface.Snow => ((byte)238, (byte)242, (byte)248),
            _ => ((byte)124, (byte)124, (byte)86),
        };

        // Snow above the renderer's own snow line, and not on a cliff — the same rule WorldBuilder uses, so
        // the picture agrees with what you see out of the window.
        double snowy = System.Math.Clamp((h - SnowFromM) / (SnowToM - SnowFromM), 0, 1)
                     * (1 - System.Math.Clamp((slope - 2.6) / 1.6, 0, 1));
        c = (Lerp(c.r, 238, snowy), Lerp(c.gr, 242, snowy), Lerp(c.b, 248, snowy));

        // Hillshade: the sun in the north-west at 45 degrees, which is the convention, plus ambient so a
        // shadowed face still shows its colour.
        double nx = -dzdx, ny = -dzdy, nz = 1.0;
        double n = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
        const double sx = 0.5, sy = -0.5, sz = 0.7071;      // north-west and up
        double lambert = System.Math.Max(0.0, (nx * sx + ny * sy + nz * sz) / n);
        double shade = 0.45 + 0.75 * lambert;
        return (Mul(c.r, shade), Mul(c.gr, shade), Mul(c.b, shade));
    }

    private static byte Lerp(double a, double b, double f) => (byte)System.Math.Clamp(a + (b - a) * f, 0, 255);
    private static byte Mul(byte a, double f) => (byte)System.Math.Clamp(a * f, 0, 255);
}
