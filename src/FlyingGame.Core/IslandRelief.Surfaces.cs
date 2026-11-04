using System.Runtime.CompilerServices;
namespace FlyingGame.Core;

/// <summary>
/// Glacier surface detail (Ty's spec 2026-11-02, docs/pending/ISLAND-TERRAIN-SURFACES-TY.md), in the ONE height
/// function: the sim's HeightAt, the analytic normal, the chart and the mesh all see the same undulating ice,
/// the same crevasses and the same boulders. The generator (tools/island/TerrainGen/Surfaces.cs) decides WHERE
/// — the SNOW, ICE, CREVASSE channels and the debris cover — and this file decides WHAT the ground does there.
/// Every term is bounded: one 2 x 2 channel block, at most four value-noise lookups and one boulder cell, no
/// search, no allocation. A cell with no ice detail at any of its corners returns after four byte reads.
///
///  * ICE undulates: bare ablation-zone ice is hummocky (differential melt, flow over the bed, refrozen
///    channels), with relief of the order of a metre over tens to a hundred metres. Here 1.5 m amplitude on a
///    value noise of 55 m cells (features ~110 m apart, which the 25 m mesh draws to within ~0.4 m), and a
///    300 m-scale modulation that leaves some patches smooth (0.3 m) but none level. At a Super Cub's 45 kt
///    touchdown a 1.5 m, 110 m hummock is a 0.26 g heave at 1.3 rad/s: the aeroplane bounces.
///  * CREVASSES are cut where the generator found the ice in tension past fracture. They run along the
///    surface contours (transverse to the flow, which is how tension opens them), one per 24 m of fall of the
///    ice surface — 29 m apart in a 40-degree icefall (where the fields are: 25-50 degrees), 42 m on 30 — wavering
///    with a 45 m warp. None below channel 0.15 (a field's feathered fringe), full strength from 0.5. V-shaped
///    (parabolic) troughs 22 m deep at full strength: Nye's (1957) zero-stress depth 2 sigma / (rho g) for the
///    100 kPa fracture stress. An icefall's crevasses gape: 40-70 % of the spacing, 20-30 m across at 30-40
///    degrees — which the mesh's 5 m cells can draw (a slit the mesh cannot draw is a hole the wheels find and
///    the eye does not). Between them the blocks are seracs: +/-3 m jagged tops on 15 m cells.
///  * BOULDERS sit on debris-covered ice: one chance in 100 x 100 m cell, 35 % on full debris, radius 5-12 m,
///    0.55-0.8 of the radius high — the giant supraglacial blocks a rockfall leaves riding a glacier.
/// </summary>
public static partial class IslandRelief
{
    public const double UndulationM = 1.5, UndulationCellM = 55, UndulationPatchCellM = 320, UndulationFloor = .2;
    public const double CrevasseFallM = 24, CrevasseWarpCellM = 45, CrevasseWarp = .5, CrevasseDepthM = 22, SeracM = 3, SeracCellM = 15;
    /// <summary>Half the crevasse's share of its period: 0.2 at the field's onset, 0.35 at full strength.</summary>
    public const double CrevasseHalf0 = .2, CrevasseHalfK = .15;
    public const double BoulderCellM = 100, BoulderChance = .35;
    /// <summary>Crevasse channel value below which there is no crevasse (the feathered fringe of a field);
    /// the field reaches full strength at 0.5.</summary>
    public const double CrevasseOnset = .15;
    const byte CrevasseOnsetByte = 38; // 0.15 x 255, rounded down
    const uint SeedUndulate = 0x51a7u, SeedPatch = 0x9e37u, SeedWarp = 0x2c1bu, SeedSerac = 0x77f1u, SeedBoulder = 0x4b0du;

    /// <summary>Surface classes at a point (bilinear, 0..1): glacier/patch SNOW, bare-ICE weight, CREVASSE
    /// intensity, rock LEDGE. Off the lattices: none.</summary>
    public static (float snow, float rough, float crevasse, float ledge) Classes(IslandPoint p)
    {
        foreach (var l in Lattices)
        {
            double x = (p.E - l.E0) * l.InvCell, y = (p.N - l.N0) * l.InvCell;
            if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) continue;
            int i = System.Math.Min(l.W - 2, (int)x), j = System.Math.Min(l.H - 2, (int)y); float tx = (float)(x - i), ty = (float)(y - j);
            int k = j * l.W + i;
            float B(byte[] c) => ((c[k] * (1 - tx) + c[k + 1] * tx) * (1 - ty) + (c[k + l.W] * (1 - tx) + c[k + l.W + 1] * tx) * ty) / 255f;
            return (B(l.Snow), B(l.Rough), B(l.Crevasse), B(l.Ledge));
        }
        return (0, 0, 0, 0);
    }

    /// <summary>The part of the height the 25 m lattice cannot carry — crevasse cuts, serac tops and boulders —
    /// so a coarse mesh level can leave it out. The undulation is NOT in it: the 25 m mesh draws that.</summary>
    public static double FineDetail(IslandPoint p)
    {
        foreach (var l in Lattices)
        {
            double x = (p.E - l.E0) * l.InvCell, y = (p.N - l.N0) * l.InvCell;
            if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) continue;
            double h = Bicubic(l, x, y, out double gx, out double gy);
            return Detail(l, x, y, h, gx, gy, true, out _, out _) - Detail(l, x, y, h, gx, gy, false, out _, out _);
        }
        return 0;
    }

    /// <summary>Every boulder on the island (east, north m; radius m; height m above the ice) — for tests, docs and
    /// the editor; the height function never enumerates them.</summary>
    public static List<(double e, double n, double r, double h)> Boulders()
    {
        var list = new List<(double, double, double, double)>();
        foreach (var l in Lattices)
            for (int bj = (int)System.Math.Floor(l.N0 * 1000 / BoulderCellM); bj <= (int)System.Math.Floor(l.MaxN * 1000 / BoulderCellM); bj++)
                for (int bi = (int)System.Math.Floor(l.E0 * 1000 / BoulderCellM); bi <= (int)System.Math.Floor(l.MaxE * 1000 / BoulderCellM); bi++)
                    if (BoulderIn(l, bi, bj, out double ce, out double cn, out double r, out _, out _, out _, out double hb)) list.Add((ce, cn, r, hb));
        return list;
    }

    /// <summary>FineDetail with its derivatives (metres per sim metre north, east): a coarse mesh level takes it
    /// off both the height and the analytic normal.</summary>
    public static double FineDetailAndGradientAt(double x, double y, out double dhdx, out double dhdy)
    {
        dhdx = 0; dhdy = 0; var p = new IslandPoint(y / 1000, x / 1000);
        foreach (var l in Lattices)
        {
            double lx = (p.E - l.E0) * l.InvCell, ly = (p.N - l.N0) * l.InvCell;
            if (lx < 0 || ly < 0 || lx > l.W - 1 || ly > l.H - 1) continue;
            double h = Bicubic(l, lx, ly, out double gx, out double gy);
            double all = Detail(l, lx, ly, h, gx, gy, true, out double ax, out double ay);
            double smooth = Detail(l, lx, ly, h, gx, gy, false, out double sx, out double sy);
            // per lattice step (east, north) -> per metre; sim x is north, y is east
            dhdx = (ay - sy) / (l.Cell * 1000); dhdy = (ax - sx) / (l.Cell * 1000);
            return all - smooth;
        }
        return 0;
    }

    /// <summary>True on top of a boulder (inside 90 % of its radius).</summary>
    public static bool OnBoulder(IslandPoint p)
    {
        foreach (var l in Lattices)
        {
            double x = (p.E - l.E0) * l.InvCell, y = (p.N - l.N0) * l.InvCell;
            if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) continue;
            return Boulder(l, (l.E0 + x * l.Cell) * 1000, (l.N0 + y * l.Cell) * 1000, out _, out _, out double rho2) > 0 && rho2 < .81;
        }
        return false;
    }

    /// <summary>Whether any crevasse or boulder reaches into the east/north box [e0, e1] x [n0, n1] (km): the
    /// mesh gives such cells fine sub-cells, because a 25 m triangle cannot draw a 10 m crevasse.</summary>
    public static bool NeedsFineMesh(double e0, double n0, double e1, double n1)
    {
        foreach (var l in Lattices)
        {
            if (e1 < l.E0 || n1 < l.N0 || e0 > l.MaxE || n0 > l.MaxN) continue;
            // The nodes whose channel values reach into the box (a box on lattice lines reads just its corners).
            const double eps = 1e-6;
            int i0 = System.Math.Max(0, (int)System.Math.Floor((e0 - l.E0) * l.InvCell + eps)), i1 = System.Math.Min(l.W - 1, (int)System.Math.Ceiling((e1 - l.E0) * l.InvCell - eps));
            int j0 = System.Math.Max(0, (int)System.Math.Floor((n0 - l.N0) * l.InvCell + eps)), j1 = System.Math.Min(l.H - 1, (int)System.Math.Ceiling((n1 - l.N0) * l.InvCell - eps));
            bool debris = false;
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
            {
                int k = j * l.W + i; if (l.Crevasse[k] > CrevasseOnsetByte) return true;
                if (l.Debris[k] > 0 && l.Ice[k] > 0) debris = true;
            }
            if (!debris) return false;
            // Boulder cells overlapping the box (grown by the largest radius plus a margin).
            double m0e = e0 * 1000 - 14, m1e = e1 * 1000 + 14, m0n = n0 * 1000 - 14, m1n = n1 * 1000 + 14;
            for (double bn = System.Math.Floor(m0n / BoulderCellM); bn <= System.Math.Floor(m1n / BoulderCellM); bn++)
                for (double be = System.Math.Floor(m0e / BoulderCellM); be <= System.Math.Floor(m1e / BoulderCellM); be++)
                {
                    if (!BoulderIn(l, (int)be, (int)bn, out double ce, out double cn, out double r, out _, out _, out _, out _)) continue;
                    double dx = System.Math.Max(0, System.Math.Max(e0 * 1000 - ce, ce - e1 * 1000)), dy = System.Math.Max(0, System.Math.Max(n0 * 1000 - cn, cn - n1 * 1000));
                    if (dx * dx + dy * dy < (r + 3) * (r + 3)) return true;
                }
            return false;
        }
        return false;
    }

    /// <summary>
    /// Surface detail at lattice coordinates (x, y) over base height h with base gradient (gx, gy) per lattice
    /// step. Returns metres; (dx, dy) per lattice step. <paramref name="fine"/> false leaves out crevasses,
    /// seracs and boulders (FineDetail).
    /// </summary>
    internal static double Detail(Lattice l, double x, double y, double h, double gx, double gy, bool fine, out double dx, out double dy)
    {
        dx = 0; dy = 0;
        int i = (int)x, j = (int)y;
        if (i > l.W - 2) i = l.W - 2;
        if (j > l.H - 2) j = l.H - 2;
        int k = j * l.W + i, w = l.W;
        var act = l.Active;
        if ((act[k] | act[k + 1] | act[k + w] | act[k + w + 1]) == 0) return 0;
        double tx = x - i, ty = y - j;
        double sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty), dsx = 6 * tx * (1 - tx), dsy = 6 * ty * (1 - ty);
        double cm = l.Cell * 1000;                       // metres per lattice step
        double em = (l.E0 + x * l.Cell) * 1000, nm = (l.N0 + y * l.Cell) * 1000;
        double v = 0;

        // ---- ICE: undulation ----------------------------------------------------------------------------
        double r = Ch(l.Rough, k, w, sx, sy, dsx, dsy, out double rx, out double ry);
        if (r > 0)
        {
            double n1 = Value(em, nm, UndulationCellM, SeedUndulate, out double n1e, out double n1n);
            double n2 = Value(em, nm, UndulationPatchCellM, SeedPatch, out double n2e, out double n2n);
            double t = (n2 + .25) / .5, s = 0, ds = 0;
            if (t >= 1) s = 1; else if (t > 0) { s = t * t * (3 - 2 * t); ds = 6 * t * (1 - t) / .5; }
            double mod = UndulationFloor + (1 - UndulationFloor) * s;
            double dme = (1 - UndulationFloor) * ds * n2e, dmn = (1 - UndulationFloor) * ds * n2n; // per metre
            v += UndulationM * r * mod * n1;
            dx += UndulationM * (rx * mod * n1 + r * dme * n1 * cm + r * mod * n1e * cm);
            dy += UndulationM * (ry * mod * n1 + r * dmn * n1 * cm + r * mod * n1n * cm);
        }
        if (!fine) return v;

        // ---- CREVASSES and seracs ---------------------------------------------------------------------
        double c = Ch(l.Crevasse, k, w, sx, sy, dsx, dsy, out double cx, out double cy);
        if (c > CrevasseOnset)
        {
            // Field strength: nothing below the onset (the channel's feathered fringe), full from 0.5.
            double ct = (c - CrevasseOnset) / (.5 - CrevasseOnset);
            if (ct >= 1) { c = 1; cx = 0; cy = 0; }
            else { double dct = 6 * ct * (1 - ct) / (.5 - CrevasseOnset); c = ct * ct * (3 - 2 * ct); cx *= dct; cy *= dct; }
            double wv = Value(em, nm, CrevasseWarpCellM, SeedWarp, out double we, out double wn);
            double phi = h / CrevasseFallM + CrevasseWarp * wv;
            double px = gx / CrevasseFallM + CrevasseWarp * we * cm, py = gy / CrevasseFallM + CrevasseWarp * wn * cm;
            double f = phi - System.Math.Floor(phi) - .5, half = CrevasseHalf0 + CrevasseHalfK * c, tt = f / half;
            if (tt > -1 && tt < 1)
            {
                double q = 1 - tt * tt;
                double tx_ = (px * half - f * CrevasseHalfK * cx) / (half * half), ty_ = (py * half - f * CrevasseHalfK * cy) / (half * half);
                v -= CrevasseDepthM * c * q;
                dx -= CrevasseDepthM * (cx * q - c * 2 * tt * tx_);
                dy -= CrevasseDepthM * (cy * q - c * 2 * tt * ty_);
            }
            double sv = Value(em, nm, SeracCellM, SeedSerac, out double se, out double sn);
            v += SeracM * c * sv;
            dx += SeracM * (cx * sv + c * se * cm);
            dy += SeracM * (cy * sv + c * sn * cm);
        }

        // ---- BOULDERS -------------------------------------------------------------------------------------
        double b = Boulder(l, em, nm, out double be, out double bn, out _);
        if (b > 0) { v += b; dx += be * cm; dy += bn * cm; }
        return v;
    }

    /// <summary>Smoothstep-weighted bilinear sample of a byte channel (C1, so a height built on it has a
    /// continuous slope) with its derivatives per lattice step.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Ch(byte[] ch, int k, int w, double sx, double sy, double dsx, double dsy, out double dx, out double dy)
    {
        double c00 = ch[k] / 255.0, c10 = ch[k + 1] / 255.0, c01 = ch[k + w] / 255.0, c11 = ch[k + w + 1] / 255.0;
        double a = c00 + (c10 - c00) * sx, b = c01 + (c11 - c01) * sx;
        dx = dsx * ((c10 - c00) * (1 - sy) + (c11 - c01) * sy);
        dy = dsy * (b - a);
        return a + (b - a) * sy;
    }

    /// <summary>Height of the boulder (if any) of the boulder cell containing (em, nm) metres, with its
    /// derivatives per metre and the normalised radius squared.</summary>
    static double Boulder(Lattice l, double em, double nm, out double de, out double dn, out double rho2)
    {
        de = 0; dn = 0; rho2 = 2;
        int bi = (int)System.Math.Floor(em / BoulderCellM), bj = (int)System.Math.Floor(nm / BoulderCellM);
        if (!BoulderIn(l, bi, bj, out double ce, out double cn, out double r, out double ca, out double sa, out double q, out double hb)) return 0;
        double ex = em - ce, ny = nm - cn; if (ex * ex + ny * ny >= r * r) return 0;
        double du = ex * ca + ny * sa, dv = -ex * sa + ny * ca, r2 = r * r, qr2 = q * q * r2;
        rho2 = du * du / r2 + dv * dv / qr2; if (rho2 >= 1) return 0;
        double dre = 2 * du * ca / r2 - 2 * dv * sa / qr2, drn = 2 * du * sa / r2 + 2 * dv * ca / qr2;
        de = -hb * 2 * rho2 * dre; dn = -hb * 2 * rho2 * drn;
        return hb * (1 - rho2 * rho2);
    }

    /// <summary>The boulder of cell (bi, bj), if the debris-covered ice at its centre carries one. Placed wholly
    /// inside its own cell, so a query reads one cell.</summary>
    static bool BoulderIn(Lattice l, int bi, int bj, out double ce, out double cn, out double r, out double ca, out double sa, out double q, out double hb)
    {
        r = 5 + 7 * U(bi, bj, SeedBoulder);
        double margin = r + 2, span = BoulderCellM - 2 * margin;
        ce = bi * BoulderCellM + margin + span * U(bi, bj, SeedBoulder + 1);
        cn = bj * BoulderCellM + margin + span * U(bi, bj, SeedBoulder + 2);
        double ax = U(bi, bj, SeedBoulder + 3) - .5, ay = U(bi, bj, SeedBoulder + 4) - .5, al = System.Math.Sqrt(ax * ax + ay * ay);
        ca = al > 1e-6 ? ax / al : 1; sa = al > 1e-6 ? ay / al : 0;
        q = .65 + .35 * U(bi, bj, SeedBoulder + 5);
        hb = (.55 + .25 * U(bi, bj, SeedBoulder + 6)) * r;
        double x = (ce / 1000 - l.E0) * l.InvCell, y = (cn / 1000 - l.N0) * l.InvCell;
        if (x < 0 || y < 0 || x > l.W - 1 || y > l.H - 1) return false;
        int i = System.Math.Min(l.W - 2, (int)x), j = System.Math.Min(l.H - 2, (int)y); double tx = x - i, ty = y - j; int k = j * l.W + i;
        double B(byte[] c) => ((c[k] * (1 - tx) + c[k + 1] * tx) * (1 - ty) + (c[k + l.W] * (1 - tx) + c[k + l.W + 1] * tx) * ty) / 255.0;
        double debris = B(l.Debris); if (debris <= .35) return false;
        double ice = B(l.Ice), rough = B(l.Rough), crev = B(l.Crevasse);
        if (rough <= .15 || crev >= .3) return false; // never on snow, in a protected area or in an icefall
        double chance = BoulderChance * Step((debris - .35) / .3) * Step((ice - .5) / .3);
        return U(bi, bj, SeedBoulder + 7) < chance;
    }

    static double Step(double t) { t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * (3 - 2 * t); }

    /// <summary>Uniform 0..1 from an integer cell and a seed (the generator's hash, reduced to 24 bits).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double U(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ seed * 0xcb1ab31fu;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xffffff) / 16777215.0;
        }
    }

    /// <summary>Value noise in -1..1 on square cells of <paramref name="cell"/> metres, quintic fade (C2), with
    /// its derivatives per metre.</summary>
    static double Value(double em, double nm, double cell, uint seed, out double de, out double dn)
    {
        double X = em / cell, Y = nm / cell, fx0 = System.Math.Floor(X), fy0 = System.Math.Floor(Y);
        int ix = (int)fx0, iy = (int)fy0; double fx = X - fx0, fy = Y - fy0;
        double u = fx * fx * fx * (fx * (fx * 6 - 15) + 10), v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
        double du = 30 * fx * fx * (fx - 1) * (fx - 1) / cell, dv = 30 * fy * fy * (fy - 1) * (fy - 1) / cell;
        double a = 2 * U(ix, iy, seed) - 1, b = 2 * U(ix + 1, iy, seed) - 1, c = 2 * U(ix, iy + 1, seed) - 1, d = 2 * U(ix + 1, iy + 1, seed) - 1;
        double k = a - b - c + d;
        de = du * ((b - a) + k * v);
        dn = dv * ((c - a) + k * u);
        return a + (b - a) * u + (c - a) * v + k * u * v;
    }
}
