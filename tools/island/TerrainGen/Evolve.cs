namespace IslandTerrainGen;

/// <summary>
/// Landscape evolution on a lattice: detachment-limited stream power (implicit, Braun &amp; Willett 2013),
/// hillslope creep and a talus (thermal) limit. Fixed nodes are base levels or authored surfaces;
/// they route water but never erode. Single-threaded where order matters, so results are deterministic.
/// </summary>
public sealed class Evolve
{
    readonly Grid g;
    public readonly int[] Receiver, Stack;
    public readonly double[] Discharge, Filled;
    readonly bool[] visited;
    uint step;
    static double Hash01(uint a, uint b) { unchecked { uint h = a * 0x9E3779B1u ^ b * 0x85EBCA77u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15; return (h & 0xFFFFFF) / 16777216.0; } }
    public Evolve(Grid grid)
    {
        g = grid; Receiver = new int[g.N]; Stack = new int[g.N]; Discharge = new double[g.N]; Filled = new double[g.N]; visited = new bool[g.N];
    }

    /// <summary>
    /// Priority-flood routing (Barnes et al. 2014): outlets are the open ocean nodes; every other node drains
    /// to the steepest neighbour on the epsilon-filled surface, so pits spill and flats drain. Stack lists
    /// nodes outlet-first, which is a valid order for the implicit incision solve (receiver before donor).
    /// </summary>
    public void Route(double[] h, bool[] outlet)
    {
        Array.Clear(visited);
        var pq = new PriorityQueue<int, (double, int)>(g.N / 8);
        int n = 0;
        for (int k = 0; k < g.N; k++)
        {
            int i = k % g.W, j = k / g.W;
            if (outlet[k] || i == 0 || j == 0 || i == g.W - 1 || j == g.H - 1)
            { visited[k] = true; Filled[k] = h[k]; Receiver[k] = k; pq.Enqueue(k, (h[k], k)); }
        }
        const double eps = 1e-4;
        while (pq.TryDequeue(out int c, out _))
        {
            Stack[n++] = c;
            int ci = c % g.W, cj = c / g.W;
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + Grid.DI[d], nj = cj + Grid.DJ[d]; if (!g.Inside(ni, nj)) continue;
                int nk = nj * g.W + ni; if (visited[nk]) continue;
                visited[nk] = true; Filled[nk] = Math.Max(h[nk], Filled[c] + eps); Receiver[nk] = c;
                pq.Enqueue(nk, (Filled[nk], nk));
            }
        }
        // A downhill neighbour on the filled surface, chosen with probability proportional to its slope
        // (Fairfield & Leymarie 1991's "Rho8"): on average the steepest-descent direction, but without the
        // eight-way grid bias that makes D8 channels run in dead-straight parallel lines. The choice is a hash
        // of (node, step), so a run is exactly repeatable.
        Span<double> s = stackalloc double[8];
        for (int k = 0; k < g.N; k++)
        {
            if (Receiver[k] == k) continue;
            int i = k % g.W, j = k / g.W; double total = 0; int bk = Receiver[k];
            for (int d = 0; d < 8; d++)
            {
                int ni = i + Grid.DI[d], nj = j + Grid.DJ[d]; s[d] = 0; if (!g.Inside(ni, nj)) continue;
                int nk = nj * g.W + ni; double v = (Filled[k] - Filled[nk]) / Grid.DL[d];
                if (v > 0) { s[d] = v * v; total += s[d]; }
            }
            if (total > 0)
            {
                double r = Hash01((uint)k, step) * total;
                for (int d = 0; d < 8; d++) { if (s[d] <= 0) continue; bk = (j + Grid.DJ[d]) * g.W + i + Grid.DI[d]; r -= s[d]; if (r <= 0) break; }
            }
            Receiver[k] = bk;
        }
        step++;
        // The flood pops in ascending (filled height, index) and every steepest receiver is strictly lower,
        // so the pop order is already receiver-before-donor.
    }

    /// <summary>Discharge = upstream sum of rain x cell area (m3/yr if rain in m/yr).</summary>
    public void Accumulate(double[] rain)
    {
        double area = g.CellM * g.CellM;
        for (int k = 0; k < g.N; k++) Discharge[k] = rain[k] * area;
        for (int s = g.N - 1; s >= 0; s--)
        {
            int k = Stack[s], r = Receiver[k];
            if (r != k) Discharge[r] += Discharge[k];
        }
    }

    /// <summary>Implicit stream-power incision, n = 1: dh/dt = -K Q^m S. Fixed nodes do not erode.
    /// Pits (below their filled level) are filled by sediment instead.</summary>
    public void Incise(double[] h, bool[] fixedNode, double[] k, double m, double dt)
    {
        for (int s = 0; s < g.N; s++)
        {
            int c = Stack[s], r = Receiver[c];
            if (r == c || fixedNode[c]) continue;
            if (h[c] < Filled[c]) { h[c] = Filled[c] - 1e-4; continue; }
            int di = Math.Abs(c % g.W - r % g.W), dj = Math.Abs(c / g.W - r / g.W);
            double len = (di + dj == 2 ? Math.Sqrt(2) : 1) * g.CellM;
            double f = k[c] * dt * Math.Pow(Discharge[c], m) / len;
            double nh = (h[c] + f * h[r]) / (1 + f);
            if (nh < h[c]) h[c] = Math.Max(nh, h[r]);
        }
    }

    /// <summary>Explicit linear creep (stable for D dt / dx^2 &lt; 0.125 with the 8-neighbour stencil used).</summary>
    public void Creep(double[] h, bool[] fixedNode, double d, double dt)
    {
        double c = d * dt / (g.CellM * g.CellM);
        int sub = Math.Max(1, (int)Math.Ceiling(c / .1)); c /= sub;
        var tmp = new double[g.N];
        for (int it = 0; it < sub; it++)
        {
            Parallel.For(1, g.H - 1, j =>
            {
                for (int i = 1; i < g.W - 1; i++)
                {
                    int k = j * g.W + i;
                    double lap = h[k + 1] + h[k - 1] + h[k + g.W] + h[k - g.W] - 4 * h[k];
                    tmp[k] = fixedNode[k] ? h[k] : h[k] + c * lap;
                }
            });
            for (int j = 1; j < g.H - 1; j++) Array.Copy(tmp, j * g.W + 1, h, j * g.W + 1, g.W - 2);
        }
    }

    /// <summary>Talus: where a neighbour is steeper than the repose angle, move half the excess downhill.
    /// Gauss-Seidel in index order, deterministic.</summary>
    public void Talus(double[] h, bool[] fixedNode, double maxTan, int passes)
    {
        for (int p = 0; p < passes; p++)
            for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
            {
                int k = j * g.W + i; if (fixedNode[k]) continue;
                for (int d = 0; d < 8; d++)
                {
                    int nk = (j + Grid.DJ[d]) * g.W + i + Grid.DI[d];
                    double lim = maxTan * Grid.DL[d] * g.CellM, ex = h[k] - h[nk] - lim;
                    if (ex <= 0) continue;
                    if (fixedNode[nk]) { h[k] -= ex * .5; continue; }
                    h[k] -= ex * .25; h[nk] += ex * .25;
                }
            }
    }
}
