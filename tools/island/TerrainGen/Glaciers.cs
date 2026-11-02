using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>
/// Where ice can exist on this terrain in this climate, and what its surface looks like.
///  * Balance b = 0.7 m w.e. per 100 m above the local ELA (a typical maritime balance gradient), capped at
///    +2.0 m w.e./yr above and unbounded below. The local ELA is the climatic one (Climate.Ela of the local
///    precipitation) moved 160 m down on a shaded north face and 160 m up on a sunlit south face, scaled by
///    how steep the face is.
///  * Ground steeper than 40 deg holds no snow: its accumulation avalanches onto the ground below it, which is
///    how small cirque glaciers survive under big headwalls.
///  * Ice moves downhill (multiple-flow routing on the smoothed bed, weights ~ slope^1.1). A cell is ice while
///    the flux reaching it, plus its own balance, is positive: the terminus is where the cumulative balance
///    of everything upstream runs out. No authored glacier outlines.
///  * Thickness from the shallow-ice approximation with sliding for temperate ice:
///    q = f 2A/(n+2) (rho g sin a)^n H^(n+2), A = 2.4e-24 Pa^-3 s^-1, n = 3, sliding factor f = 2.
/// </summary>
public static class Glaciers
{
    public const double BalanceGradient = .007, MaxAccumulation = 2.0, AspectShiftM = 160, AvalancheSlope = .84;
    public static void Apply(Mass m, Action<string> say)
    {
        var g = m.G; var bed = m.H; var sm = g.Blur(bed, 2);
        int n = g.N; double area = g.CellM * g.CellM;
        var balance = new double[n]; var slope = new double[n];
        for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
        {
            int k = j * g.W + i; if (m.C.Ocean[k]) continue;
            double dx = (sm[k + 1] - sm[k - 1]) / (2 * g.CellM), dy = (sm[k + g.W] - sm[k - g.W]) / (2 * g.CellM);
            double s = Math.Sqrt(dx * dx + dy * dy); slope[k] = s;
            // Facing north = ground falling to the north = dh/dN > 0? No: a north face falls TO the north, dh/dN < 0.
            double north = s > 1e-6 ? -dy / s : 0, steep = Math.Min(1, s / .35);
            double ela = Climate.Ela(m.Rain[k]) - AspectShiftM * north * steep;
            balance[k] = Math.Min(MaxAccumulation, BalanceGradient * (bed[k] - ela));
        }
        // Process from the top down so each cell's upstream flux is complete when it is reached.
        var order = Enumerable.Range(0, n).ToArray(); var keys = (double[])sm.Clone(); for (int k = 0; k < n; k++) keys[k] = -keys[k];
        Array.Sort(keys, order);
        var flux = new double[n]; var ice = new double[n];
        Span<double> w = stackalloc double[8];
        foreach (int k in order)
        {
            int i = k % g.W, j = k / g.W; if (i == 0 || j == 0 || i == g.W - 1 || j == g.H - 1 || m.C.Ocean[k]) continue;
            double b = balance[k] * area / .9; // m3 ice / yr
            bool steep = slope[k] > AvalancheSlope;
            double total = flux[k] + b; // steep ground keeps nothing: its snow goes straight on downhill
            if (!steep && total > 0 && !m.Fixed[k]) ice[k] = total;
            if (total <= 0) continue;
            double sum = 0;
            for (int d = 0; d < 8; d++)
            {
                int nk = (j + Grid.DJ[d]) * g.W + i + Grid.DI[d]; double drop = (sm[k] - sm[nk]) / Grid.DL[d];
                w[d] = drop > 0 ? Math.Pow(drop, 1.1) : 0; sum += w[d];
            }
            if (sum <= 0) continue;
            for (int d = 0; d < 8; d++) if (w[d] > 0) flux[(j + Grid.DJ[d]) * g.W + i + Grid.DI[d]] += total * w[d] / sum;
        }
        // Ice thickness from flux per unit width (flux spread over the glacier's width by a light blur).
        var q = g.Blur(ice, 3); var thick = new double[n];
        const double a = 2.4e-24 * 3.156e7, rhoG = 917 * 9.81, sliding = 2;
        int cells = 0; double vol = 0, maxH = 0;
        for (int k = 0; k < n; k++)
        {
            if (ice[k] <= 0) continue;
            double perWidth = q[k] / g.CellM, sinA = Math.Max(.06, Math.Sin(Math.Atan(slope[k])));
            double h5 = perWidth * 5 / (2 * a * sliding * Math.Pow(rhoG * sinA, 3));
            thick[k] = Math.Min(180, Math.Pow(Math.Max(0, h5), .2)); cells++; vol += thick[k] * area; maxH = Math.Max(maxH, thick[k]);
        }
        // Ice fraction: smooth the binary extent so the margin is a feathered line, not cell steps.
        var frac = new double[n]; for (int k = 0; k < n; k++) frac[k] = ice[k] > 0 ? 1 : 0;
        frac = g.Blur(frac, 1, 2);
        var surf = new double[n]; for (int k = 0; k < n; k++) surf[k] = bed[k] + thick[k];
        surf = g.Blur(surf, 2);
        var debris = new double[n];
        for (int k = 0; k < n; k++)
        {
            if (frac[k] <= 0 || m.Fixed[k]) continue;
            double s = Math.Max(bed[k], surf[k] * frac[k] + bed[k] * (1 - frac[k]));
            bed[k] = Math.Min(s, m.Upper[k]);
            // Debris cover on the ablation tongue: thicker the further below the ELA.
            if (balance[k] < 0) debris[k] = Math.Clamp(-balance[k] / 4, 0, 1) * frac[k];
        }
        // Moraines: lateral and end moraines outside ablation-zone ice, 8-20 m high, one to three cells out.
        var dist = new int[n]; Array.Fill(dist, 99); var queue = new Queue<int>();
        for (int k = 0; k < n; k++) if (ice[k] > 0 && balance[k] < 0) { dist[k] = 0; queue.Enqueue(k); }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue(); if (dist[k] >= 4) continue; int i = k % g.W, j = k / g.W;
            for (int d = 0; d < 8; d++) { int ni = i + Grid.DI[d], nj = j + Grid.DJ[d]; if (!g.Inside(ni, nj)) continue; int nk = nj * g.W + ni; if (ice[nk] > 0 || dist[nk] <= dist[k] + 1) continue; dist[nk] = dist[k] + 1; queue.Enqueue(nk); }
        }
        var ridge = new double[n];
        for (int k = 0; k < n; k++) if (dist[k] > 0 && dist[k] <= 4) ridge[k] = Math.Exp(-Math.Pow((dist[k] - 2) / 1.2, 2));
        ridge = g.Blur(ridge, 1, 2);
        for (int k = 0; k < n; k++)
        {
            if (ridge[k] <= .02 || m.Fixed[k] || frac[k] > .5) continue;
            bed[k] = Math.Min(bed[k] + 14 * ridge[k], m.Upper[k]);
            debris[k] = Math.Max(debris[k], ridge[k]);
        }
        m.Ice = frac; m.Debris = debris;
        say($"glaciers: {cells} cells ({cells * area / 1e6:F2} km2), volume {vol / 1e9:F3} km3, max thickness {maxH:F0} m");
        m.Log.Add($"glaciers: {cells * area / 1e6:F2} km2 of ice, {vol / 1e9:F3} km3, thickest {maxH:F0} m");
    }
}
