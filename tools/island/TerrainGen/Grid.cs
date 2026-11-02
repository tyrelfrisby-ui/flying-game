using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>A regular east/north lattice. Index k = j*W + i; node (i,j) sits at (E0 + i*Cell, N0 + j*Cell) km.</summary>
public sealed class Grid
{
    public readonly int W, H;
    public readonly double E0, N0, Cell; // km
    public double CellM => Cell * 1000;
    public Grid(double e0, double n0, double cell, int w, int h) { E0 = e0; N0 = n0; Cell = cell; W = w; H = h; }
    public int N => W * H;
    public IslandPoint P(int k) => new(E0 + (k % W) * Cell, N0 + (k / W) * Cell);
    public IslandPoint P(int i, int j) => new(E0 + i * Cell, N0 + j * Cell);
    public int K(int i, int j) => j * W + i;
    public bool Inside(int i, int j) => i >= 0 && j >= 0 && i < W && j < H;
    /// <summary>Nearest node to a point, or -1 off the grid.</summary>
    public int Nearest(IslandPoint p)
    {
        int i = (int)Math.Round((p.E - E0) / Cell), j = (int)Math.Round((p.N - N0) / Cell);
        return Inside(i, j) ? K(i, j) : -1;
    }
    public static readonly int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1 }, DJ = { 0, 0, 1, -1, 1, -1, 1, -1 };
    public static readonly double[] DL = { 1, 1, 1, 1, Math.Sqrt(2), Math.Sqrt(2), Math.Sqrt(2), Math.Sqrt(2) };

    /// <summary>Separable box blur repeated three times (close to Gaussian, sigma ~ radius*0.58*sqrt(3)).</summary>
    public double[] Blur(double[] src, int radius, int passes = 3)
    {
        var a = (double[])src.Clone(); var b = new double[N];
        for (int pass = 0; pass < passes; pass++)
        {
            Parallel.For(0, H, j =>
            {
                double s = 0; int o = j * W;
                for (int i = -radius; i <= radius; i++) s += a[o + Math.Clamp(i, 0, W - 1)];
                for (int i = 0; i < W; i++)
                {
                    b[o + i] = s / (2 * radius + 1);
                    s += a[o + Math.Min(W - 1, i + radius + 1)] - a[o + Math.Max(0, i - radius)];
                }
            });
            Parallel.For(0, W, i =>
            {
                double s = 0;
                for (int j = -radius; j <= radius; j++) s += b[Math.Clamp(j, 0, H - 1) * W + i];
                for (int j = 0; j < H; j++)
                {
                    a[j * W + i] = s / (2 * radius + 1);
                    s += b[Math.Min(H - 1, j + radius + 1) * W + i] - b[Math.Max(0, j - radius) * W + i];
                }
            });
        }
        return a;
    }

    /// <summary>Catmull-Rom sample of a field at an arbitrary point (clamped at the edges).</summary>
    public double Sample(double[] f, IslandPoint p)
    {
        double x = (p.E - E0) / Cell, y = (p.N - N0) / Cell;
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y); double tx = x - i, ty = y - j;
        double r = 0;
        for (int b = -1; b <= 2; b++)
        {
            double row = 0;
            for (int a = -1; a <= 2; a++) row += f[Math.Clamp(j + b, 0, H - 1) * W + Math.Clamp(i + a, 0, W - 1)] * CR(a, tx);
            r += row * CR(b, ty);
        }
        return r;
    }
    static double CR(int a, double t) => a switch
    {
        -1 => ((-t + 2) * t - 1) * t * .5,
        0 => ((3 * t - 5) * t * t + 2) * .5,
        1 => ((-3 * t + 4) * t + 1) * t * .5,
        _ => (t - 1) * t * t * .5
    };
}

/// <summary>Deterministic integer-hash noise. No System.Random, no process state.</summary>
public static class Noise
{
    static uint Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ seed * 0xcb1ab31fu;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15; return h;
        }
    }
    static double Grad(int x, int y, uint seed, double dx, double dy)
    {
        uint h = Hash(x, y, seed); double a = (h & 0xffff) / 65536.0 * 2 * Math.PI;
        return Math.Cos(a) * dx + Math.Sin(a) * dy;
    }
    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    /// <summary>Gradient noise, roughly -0.7..0.7, unit lattice.</summary>
    public static double Perlin(double x, double y, uint seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); double fx = x - xi, fy = y - yi, u = Fade(fx), v = Fade(fy);
        double a = Grad(xi, yi, seed, fx, fy), b = Grad(xi + 1, yi, seed, fx - 1, fy), c = Grad(xi, yi + 1, seed, fx, fy - 1), d = Grad(xi + 1, yi + 1, seed, fx - 1, fy - 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }
    /// <summary>Fractal sum, wavelength in km of the first octave.</summary>
    public static double Fbm(IslandPoint p, double wavelengthKm, int octaves, uint seed, double gain = .5)
    {
        double s = 0, amp = 1, f = 1 / wavelengthKm, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            // Rotate each octave so lattice axes never line up.
            double c = Math.Cos(o * 1.1), sn = Math.Sin(o * 1.1);
            s += amp * Perlin((p.E * c - p.N * sn) * f, (p.E * sn + p.N * c) * f, seed + (uint)o * 7919u);
            norm += amp; amp *= gain; f *= 2;
        }
        return s / norm;
    }
}
