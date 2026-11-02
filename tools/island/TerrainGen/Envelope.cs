namespace IslandTerrainGen;

/// <summary>
/// The highest ground that can physically stand next to a ceiling: E(p) = min over capped nodes q of
/// cap(q) + S * |p - q|, and likewise from the shoreline. A multi-source Dijkstra on a 16-neighbour
/// stencil (chamfer error about 2%). S is a sustained mountainside gradient, not a cliff.
/// </summary>
public static class Envelope
{
    static readonly int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1, 2, 2, -2, -2, 1, 1, -1, -1 };
    static readonly int[] DJ = { 0, 0, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 2, -2, 2, -2 };
    /// <summary>As Compute, but a value travels at most maxM from its own source (the path length is carried
    /// with it), so a far source cannot reach through a near one.</summary>
    public static double[] ComputeLimited(Grid g, double[] source, double slope, double maxM)
    {
        var e = (double[])source.Clone(); var dist = new double[g.N];
        var pq = new PriorityQueue<int, (double, int)>();
        for (int k = 0; k < g.N; k++) if (double.IsFinite(e[k])) pq.Enqueue(k, (e[k], k));
        var done = new bool[g.N];
        while (pq.TryDequeue(out int c, out var pri))
        {
            if (done[c] || pri.Item1 > e[c]) continue; done[c] = true;
            int ci = c % g.W, cj = c / g.W;
            for (int d = 0; d < 16; d++)
            {
                int ni = ci + DI[d], nj = cj + DJ[d]; if (!g.Inside(ni, nj)) continue;
                int nk = nj * g.W + ni; double step = g.CellM * Math.Sqrt(DI[d] * DI[d] + DJ[d] * DJ[d]);
                if (dist[c] + step > maxM) continue;
                double v = e[c] + slope * step;
                if (v < e[nk]) { e[nk] = v; dist[nk] = dist[c] + step; pq.Enqueue(nk, (v, nk)); }
            }
        }
        return e;
    }
    public static double[] Compute(Grid g, double[] source, double slope)
    {
        var e = (double[])source.Clone();
        var pq = new PriorityQueue<int, (double, int)>();
        for (int k = 0; k < g.N; k++) if (double.IsFinite(e[k])) pq.Enqueue(k, (e[k], k));
        var done = new bool[g.N];
        while (pq.TryDequeue(out int c, out var pri))
        {
            if (done[c] || pri.Item1 > e[c]) continue; done[c] = true;
            int ci = c % g.W, cj = c / g.W;
            for (int d = 0; d < 16; d++)
            {
                int ni = ci + DI[d], nj = cj + DJ[d]; if (!g.Inside(ni, nj)) continue;
                int nk = nj * g.W + ni; double v = e[c] + slope * g.CellM * Math.Sqrt(DI[d] * DI[d] + DJ[d] * DJ[d]);
                if (v < e[nk]) { e[nk] = v; pq.Enqueue(nk, (v, nk)); }
            }
        }
        return e;
    }
}
