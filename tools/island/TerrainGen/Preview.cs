namespace IslandTerrainGen;
public static class Preview
{
    public static void Hillshade(string path, Grid g, double[] f, double[]? overlay = null, int step = 1)
    {
        int w = g.W / step, hgt = g.H / step;
        Png.Write(path, w, hgt, (x, y) =>
        {
            int i = x * step, j = (g.H - 1) - y * step; int k = j * g.W + i;
            int il = Math.Max(0, i - 1), ir = Math.Min(g.W - 1, i + 1), jd = Math.Max(0, j - 1), ju = Math.Min(g.H - 1, j + 1);
            double dx = (f[j * g.W + ir] - f[j * g.W + il]) / ((ir - il) * g.CellM), dy = (f[ju * g.W + i] - f[jd * g.W + i]) / ((ju - jd) * g.CellM);
            var c = Png.Shade(f[k], dx, dy, g.Cell);
            if (overlay != null && overlay[k] > 0) { double a = Math.Clamp(overlay[k], 0, 1); c = ((byte)(c.Item1 * (1 - a) + 90 * a), (byte)(c.Item2 * (1 - a) + 160 * a), (byte)(c.Item3 * (1 - a) + 255 * a)); }
            return c;
        });
    }
    public static void Scalar(string path, Grid g, double[] f, double lo, double hi)
    {
        Png.Write(path, g.W, g.H, (x, y) =>
        {
            double v = Math.Clamp((f[(g.H - 1 - y) * g.W + x] - lo) / (hi - lo), 0, 1);
            return ((byte)(255 * v), (byte)(255 * (1 - Math.Abs(v - .5) * 2)), (byte)(255 * (1 - v)));
        });
    }
}
