using System.Runtime.CompilerServices;
namespace FlyingGame.Core;

/// <summary>
/// The island's natural relief (2026-11-02, lane/claude-island-terrain): eroded, glaciated ground generated
/// by tools/island/TerrainGen from the authored layout and stored in IslandRelief.Generated.cs. It is the
/// BASE of the height evaluator — every authored pad, lake, cut, cap and cave layer in IslandHeightIndex still
/// runs on top of it, unchanged, and remains the authority — so sim, contact, chart and mesh keep one source.
///
/// Between the 25 m lattice nodes the surface is Catmull-Rom bicubic: it passes through every node and is
/// C1 (continuous slope) everywhere, so the analytic normal never jumps at a cell edge and a mesh shaded with
/// it shows no facets. Each query reads a fixed 4 x 4 block: bounded, allocation-free, no search.
///
/// Byte channels ride on the same lattice (bilinear): glacier ice fraction, debris/moraine cover, wetness
/// (log of the discharge the erosion routed through the node) and the local equilibrium-line altitude.
/// </summary>
public static class IslandRelief
{
    internal sealed class Lattice
    {
        public double E0, N0, Cell, InvCell;
        public int W, H;
        public float[] Height = System.Array.Empty<float>();
        public byte[] Ice = System.Array.Empty<byte>(), Debris = System.Array.Empty<byte>(), Wet = System.Array.Empty<byte>(), Ela = System.Array.Empty<byte>();
        public double MaxE => E0 + (W - 1) * Cell;
        public double MaxN => N0 + (H - 1) * Cell;
    }
    /// <summary>Ocean bed outside every lattice; the shore fade in the evaluator meets the same depth.</summary>
    public const double SeaBedM = -30;
    internal static readonly Lattice[] Lattices = Decode();

    /// <summary>Base relief height (m MSL) at an east/north point in km.</summary>
    public static double Height(IslandPoint p)
    {
        foreach (var l in Lattices)
        {
            double x = (p.E - l.E0) * l.InvCell, y = (p.N - l.N0) * l.InvCell;
            if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) continue;
            return Bicubic(l, x, y, out _, out _);
        }
        return SeaBedM;
    }

    /// <summary>Height with its exact derivative (metres per km east/north), for the analytic normal.</summary>
    internal static TerrainJet Height(TerrainJetPoint p)
    {
        foreach (var l in Lattices)
        {
            double x = (p.E.Value - l.E0) * l.InvCell, y = (p.N.Value - l.N0) * l.InvCell;
            if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) continue;
            double v = Bicubic(l, x, y, out double dx, out double dy);
            dx *= l.InvCell; dy *= l.InvCell; // per lattice step -> per km
            return new TerrainJet(v, dx * p.E.E + dy * p.N.E, dx * p.E.N + dy * p.N.N);
        }
        return new TerrainJet(SeaBedM);
    }

    /// <summary>Surface channels at a point: ice fraction, debris cover, wetness (0..1) and the local
    /// equilibrium-line altitude (m). Off the lattices (the outer islands): no ice, debris or wetness, and the
    /// ELA of the windward background precipitation, 2.2 m/yr: (18 C - 1.63 x 2.2) / 6.5 K/km = 2,220 m.</summary>
    public static (float ice, float debris, float wet, float elaM) Channels(IslandPoint p)
    {
        foreach (var l in Lattices)
        {
            double x = (p.E - l.E0) * l.InvCell, y = (p.N - l.N0) * l.InvCell;
            if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) continue;
            int i = System.Math.Min(l.W - 2, (int)x), j = System.Math.Min(l.H - 2, (int)y); float tx = (float)(x - i), ty = (float)(y - j);
            int k = j * l.W + i;
            float B(byte[] c) => ((c[k] * (1 - tx) + c[k + 1] * tx) * (1 - ty) + (c[k + l.W] * (1 - tx) + c[k + l.W + 1] * tx) * ty) / 255f;
            return (B(l.Ice), B(l.Debris), B(l.Wet), 1200 + B(l.Ela) * 255 * 8);
        }
        return (0, 0, 0, OffLatticeElaM);
    }
    public const float OffLatticeElaM = 2220;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Bicubic(Lattice l, double x, double y, out double dx, out double dy)
    {
        int i = (int)x, j = (int)y;
        if (i > l.W - 2) i = l.W - 2;
        if (j > l.H - 2) j = l.H - 2;
        double tx = x - i, ty = y - j;
        Weights(tx, out double a0, out double a1, out double a2, out double a3, out double da0, out double da1, out double da2, out double da3);
        Weights(ty, out double b0, out double b1, out double b2, out double b3, out double db0, out double db1, out double db2, out double db3);
        int i0 = i > 0 ? i - 1 : 0, i3 = i + 2 < l.W ? i + 2 : l.W - 1;
        int j0 = j > 0 ? j - 1 : 0, j3 = j + 2 < l.H ? j + 2 : l.H - 1;
        var h = l.Height; int w = l.W;
        double R(int r, out double drx)
        {
            int o = r * w; double p0 = h[o + i0], p1 = h[o + i], p2 = h[o + i + 1], p3 = h[o + i3];
            drx = p0 * da0 + p1 * da1 + p2 * da2 + p3 * da3;
            return p0 * a0 + p1 * a1 + p2 * a2 + p3 * a3;
        }
        double r0 = R(j0, out double x0), r1 = R(j, out double x1), r2 = R(j + 1, out double x2), r3 = R(j3, out double x3);
        dx = x0 * b0 + x1 * b1 + x2 * b2 + x3 * b3;
        dy = r0 * db0 + r1 * db1 + r2 * db2 + r3 * db3;
        return r0 * b0 + r1 * b1 + r2 * b2 + r3 * b3;
    }

    /// <summary>Catmull-Rom weights for the four samples at offsets -1..2 and their derivatives.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Weights(double t, out double w0, out double w1, out double w2, out double w3,
                                out double d0, out double d1, out double d2, out double d3)
    {
        double t2 = t * t, t3 = t2 * t;
        w0 = .5 * (-t3 + 2 * t2 - t); w1 = .5 * (3 * t3 - 5 * t2 + 2); w2 = .5 * (-3 * t3 + 4 * t2 + t); w3 = .5 * (t3 - t2);
        d0 = .5 * (-3 * t2 + 4 * t - 1); d1 = .5 * (9 * t2 - 10 * t); d2 = .5 * (-9 * t2 + 8 * t + 1); d3 = .5 * (3 * t2 - 2 * t);
    }

    private static Lattice[] Decode()
    {
        var b64 = string.Concat(IslandReliefData.Base64);
        var data = System.Convert.FromBase64String(b64);
        if (data.Length != IslandReliefData.Bytes) throw new System.InvalidOperationException("Island relief data is truncated.");
        int o = 0;
        ulong V() { ulong v = 0; int s = 0; while (true) { byte b = data[o++]; v |= (ulong)(b & 0x7f) << s; if (b < 0x80) return v; s += 7; } }
        double D() { double d = System.BitConverter.ToDouble(data, o); o += 8; return d; }
        if (data[0] != 'I' || data[1] != 'R' || data[2] != 'L' || data[3] != '1') throw new System.InvalidOperationException("Island relief data has the wrong format.");
        o = 4; int count = (int)V(); var result = new Lattice[count];
        for (int n = 0; n < count; n++)
        {
            var l = new Lattice { E0 = D(), N0 = D(), Cell = D() };
            l.InvCell = 1 / l.Cell; l.W = (int)V(); l.H = (int)V();
            int size = l.W * l.H; var q = new int[size];
            for (int j = 0; j < l.H; j++) for (int i = 0; i < l.W; i++)
            {
                int k = j * l.W + i; int pred = i > 0 && j > 0 ? q[k - 1] + q[k - l.W] - q[k - l.W - 1] : i > 0 ? q[k - 1] : j > 0 ? q[k - l.W] : 0;
                ulong z = V(); long r = (long)(z >> 1) ^ -(long)(z & 1); q[k] = (int)(pred + r);
            }
            l.Height = new float[size];
            for (int k = 0; k < size; k++) l.Height[k] = (float)(q[k] * .05 - 30);
            byte[] Channel() { var c = new byte[size]; int k = 0; while (k < size) { byte v = data[o++]; int run = (int)V(); for (int r = 0; r < run; r++) c[k++] = v; } return c; }
            l.Ice = Channel(); l.Debris = Channel(); l.Wet = Channel(); l.Ela = Channel();
            result[n] = l;
        }
        return result;
    }
}
