namespace IslandTerrainGen;

/// <summary>
/// The kilometre-scale shape: a harmonic (membrane) surface through every hard anchor, bounded by the
/// ceilings and floors. A membrane obeys the maximum principle — it never rises above or sags below the
/// anchors around it — which a thin plate does not: pressed against a ceiling a plate bounces down beside
/// it, and between 1,450 m strips it sagged to the coastal floor. Summits and crests are added afterwards as
/// explicit massifs. Solved coarse-to-fine (factor 2 per level) with projected SOR.
/// </summary>
public static class Macro
{
    public static double[] Solve(Grid g, double[] fix, double[] lo, double[] hi, int finestSweeps = 60)
    {
        int levels = 0; while ((g.W >> levels) > 24 && (g.H >> levels) > 24) levels++;
        double[]? coarse = null; int cw = 0, ch = 0;
        for (int L = levels; L >= 0; L--)
        {
            int s = 1 << L, w = (g.W - 1) / s + 1, h = (g.H - 1) / s + 1;
            var f = new double[w * h]; var l = new double[w * h]; var u = new double[w * h]; var z = new double[w * h];
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++)
            {
                // A coarse node takes the anchors of its whole footprint, so small anchors (a summit node)
                // still shape the coarse levels.
                double fs = 0; int fn = 0; double lmax = double.NegativeInfinity, umin = double.PositiveInfinity;
                int r = s / 2;
                for (int b = -r; b <= r; b++) for (int a = -r; a <= r; a++)
                {
                    int fi = i * s + a, fj = j * s + b; if (fi < 0 || fj < 0 || fi >= g.W || fj >= g.H) continue;
                    int k = fj * g.W + fi;
                    if (!double.IsNaN(fix[k])) { fs += fix[k]; fn++; }
                    lmax = Math.Max(lmax, lo[k]); umin = Math.Min(umin, hi[k]);
                }
                int c = j * w + i; f[c] = fn > 0 ? fs / fn : double.NaN; l[c] = lmax; u[c] = umin;
                z[c] = coarse != null ? coarse[Math.Min(ch - 1, j / 2) * cw + Math.Min(cw - 1, i / 2)] : 600;
                if (!double.IsNaN(f[c])) z[c] = f[c];
            }
            if (coarse != null) // bilinear prolongation
                for (int j = 0; j < h; j++) for (int i = 0; i < w; i++)
                {
                    int c = j * w + i; if (!double.IsNaN(f[c])) continue;
                    double x = i / 2.0, y = j / 2.0; int x0 = Math.Min(cw - 1, (int)x), y0 = Math.Min(ch - 1, (int)y), x1 = Math.Min(cw - 1, x0 + 1), y1 = Math.Min(ch - 1, y0 + 1);
                    double tx = x - x0, ty = y - y0;
                    z[c] = (coarse[y0 * cw + x0] * (1 - tx) + coarse[y0 * cw + x1] * tx) * (1 - ty) + (coarse[y1 * cw + x0] * (1 - tx) + coarse[y1 * cw + x1] * tx) * ty;
                }
            int sweeps = L == levels ? 3000 : L > 2 ? 500 : L > 0 ? 200 : finestSweeps;
            for (int it = 0; it < sweeps; it++)
                for (int j = 0; j < h; j++) for (int i = 0; i < w; i++)
                {
                    int c = j * w + i; if (!double.IsNaN(f[c])) continue;
                    double Z(int a, int b) => z[Math.Clamp(j + b, 0, h - 1) * w + Math.Clamp(i + a, 0, w - 1)];
                    double v = (Z(1, 0) + Z(-1, 0) + Z(0, 1) + Z(0, -1)) * .25;
                    v = z[c] + 1.85 * (v - z[c]);
                    z[c] = Math.Clamp(v, l[c], Math.Max(l[c], u[c]));
                }
            coarse = z; cw = w; ch = h;
        }
        return coarse!;
    }
}
