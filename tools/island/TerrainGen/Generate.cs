using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>One land mass: its lattice, its constraints, and everything the generator derives on it.</summary>
public sealed class Mass
{
    public string Name = "";
    public Grid G = null!;
    public Constraints C = null!;
    public double[] Upper = null!;      // min(ceiling, slope envelope)
    public double[] Macro = null!, H = null!, Rain = null!, Ice = null!, Debris = null!, Wet = null!;
    public bool[] Fixed = null!;
    public List<string> Log = new();
}

public static class Generate
{
    const double Ft = IslandTerrain.Feet;
    /// <summary>Sustained mountainside gradient bounding how high ground can stand beside a ceiling: 45 deg.
    /// Steeper than this over hundreds of metres is a cliff band, which the erosion makes locally, not a slope.</summary>
    public const double EnvelopeSlope = 1.0;
    /// <summary>Sea cliffs may be steeper than a mountainside: 55 deg from the shoreline.</summary>
    public const double CoastSlope = 1.43;

    public static Mass Build(string name, Grid g, IslandPoint[] polygon, int seed, Action<string> say)
    {
        var m = new Mass { Name = name, G = g };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        m.C = new Constraints(g, polygon);
        var c = m.C;
        say($"{name}: constraints {sw.Elapsed.TotalSeconds:F1}s");

        // ---- how high anything can stand -----------------------------------------------------------
        var src = new double[g.N];
        for (int k = 0; k < g.N; k++) src[k] = double.IsFinite(c.Hi[k]) ? c.Hi[k] : double.PositiveInfinity;
        var envCap = Envelope.Compute(g, src, EnvelopeSlope);
        for (int k = 0; k < g.N; k++) src[k] = c.Ocean[k] ? 0 : double.PositiveInfinity;
        var envCoast = Envelope.Compute(g, src, CoastSlope);
        m.Upper = new double[g.N];
        for (int k = 0; k < g.N; k++) m.Upper[k] = Math.Min(c.Hi[k], Math.Min(envCap[k], envCoast[k]));

        // ---- summits: authored where physically possible ------------------------------------------
        foreach (var (p, top, id) in c.Summits)
        {
            int k = g.Nearest(p);
            double anchor = double.NegativeInfinity;   // the highest hard anchor within 600 m holds a summit up
            for (int q = 0; q < g.N; q++)
            {
                if (q == k) continue; var d = (g.P(q) - p).Length; if (d > .6) continue;
                if (!double.IsNaN(c.Fix[q])) anchor = Math.Max(anchor, c.Fix[q]);
                if (double.IsFinite(c.Lo[q])) anchor = Math.Max(anchor, c.Lo[q]);
            }
            double z = Math.Min(top, Math.Max(m.Upper[k], anchor + 60));
            c.Fix[k] = z;
            m.Log.Add($"summit {id}: authored {top:F0} m ({top / Ft:F0} ft), physical bound {m.Upper[k]:F0} m, anchors {anchor:F0} m -> {z:F0} m ({z / Ft:F0} ft)");
            // Let the summit be the summit: nothing nearby may stand above it.
            for (int q = 0; q < g.N; q++) if ((g.P(q) - p).Length < 1.2) m.Upper[q] = Math.Min(m.Upper[q], z - 1);
        }
        for (int k = 0; k < g.N; k++) m.Upper[k] = Math.Max(m.Upper[k], c.Lo[k]);
        say($"{name}: envelope + summits {sw.Elapsed.TotalSeconds:F1}s");

        // ---- macro shape -----------------------------------------------------------------------------
        var fixM = (double[])c.Fix.Clone();
        for (int k = 0; k < g.N; k++) if (c.Ocean[k]) fixM[k] = 0;
        // A thin plate between a shoreline at 0 and inland anchors can sag below the sea; the land floor rises
        // from 3 m at the shore (a beach) to 60 m 1.5 km inland, the height of an old raised shoreline.
        var loM = new double[g.N];
        for (int k = 0; k < g.N; k++) loM[k] = c.Ocean[k] ? double.NegativeInfinity : Math.Max(c.Lo[k], Math.Min(3 + 57 * IslandTerrain.Smooth(c.Shore[k] / 1.5), m.Upper[k] - 1));
        // The macro target keeps clear of the hard bound, so ridges may reach a ceiling but slopes do not lie on it.
        var upperM = new double[g.N];
        // Margin only below the slope envelopes (a hillside should not lie ON a 45-degree bound); a flat ceiling
        // such as an approach surface may be approached closely.
        for (int k = 0; k < g.N; k++) upperM[k] = Math.Max(loM[k], Math.Min(c.Hi[k] - 6, Math.Min(envCap[k], envCoast[k]) - 160));
        for (int k = 0; k < g.N; k++) if (!double.IsNaN(fixM[k]) && !c.Ocean[k] && double.IsNaN(c.Fix[k])) fixM[k] = Math.Min(fixM[k], upperM[k]);
        // Summit nodes are not membrane anchors (a membrane through a point is a spike); they become massifs.
        foreach (var (p, _, _) in c.Summits) fixM[g.Nearest(p)] = double.NaN;
        var basin = Macro.Solve(g, fixM, loM, upperM);
        m.Macro = Massifs(m, basin, loM, upperM);
        say($"{name}: macro {sw.Elapsed.TotalSeconds:F1}s");

        m.Fixed = new bool[g.N];
        for (int k = 0; k < g.N; k++) m.Fixed[k] = c.Ocean[k] || !double.IsNaN(c.Fix[k]);
        m.Rain = Climate.Precipitation(g, m.Macro);
        say($"{name}: climate {sw.Elapsed.TotalSeconds:F1}s");

        Erode(m, (uint)seed, say);
        say($"{name}: erosion {sw.Elapsed.TotalSeconds:F1}s");
        Glaciers.Apply(m, say);
        return m;
    }

    /// <summary>Erodibility (yr^-1 m^(1-2m)): crystalline core and volcanic flanks share one value; the red-rock
    /// country (sandstone under resistant caprock benches) is soft except in its cap layers.</summary>
    static double Erodibility(IslandPoint p, double z)
    {
        // Lithology varies: +/-60% erodibility over 1-3 km (joint density, alteration), which is what lets
        // some valleys capture others instead of every slope cutting the same parallel gullies.
        double k0 = 2.2e-5 * Math.Exp(.9 * Noise.Fbm(p, 2.2, 3, 77));
        bool redRock = p.E > 2 && p.N < -1 && p.N > -7;
        if (!redRock) return k0;
        // Caprock benches at the two mesa-pad levels and every 120 m between, 18 m thick.
        double[] caps = { 4475 * Ft, 4075 * Ft };
        double hard = 0;
        foreach (var cz in caps) for (int i = -6; i <= 6; i++) hard = Math.Max(hard, 1 - Math.Clamp(Math.Abs(z - (cz + i * 120)) / 9, 0, 1));
        return k0 * (2.4 - 2.2 * hard);
    }

    /// <summary>Smooth aprons round every fixed surface: within 250 m of a fixed node the free ground relaxes
    /// (harmonically, fixed nodes as boundary) towards it, strongest next to it. The runtime pad blend then
    /// has a gentle surface to blend into instead of a step.</summary>
    static void Feather(Mass m, double[] h)
    {
        var g = m.G; const int band = 10; var dist = new int[g.N]; Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        for (int k = 0; k < g.N; k++) if (m.Fixed[k] && !m.C.Ocean[k]) { dist[k] = 0; queue.Enqueue(k); }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue(); if (dist[k] >= band) continue; int i = k % g.W, j = k / g.W;
            for (int d = 0; d < 4; d++) { int ni = i + Grid.DI[d], nj = j + Grid.DJ[d]; if (!g.Inside(ni, nj)) continue; int nk = nj * g.W + ni; if (dist[nk] > dist[k] + 1) { dist[nk] = dist[k] + 1; queue.Enqueue(nk); } }
        }
        var orig = (double[])h.Clone(); var tmp = (double[])h.Clone();
        for (int it = 0; it < 60; it++)
        {
            for (int j = 1; j < g.H - 1; j++) for (int i = 1; i < g.W - 1; i++)
            {
                int k = j * g.W + i; if (m.Fixed[k] || dist[k] > band) continue;
                double w = 1 - IslandTerrain.Smooth((dist[k] - 1) / (double)band);
                double avg = (h[k + 1] + h[k - 1] + h[k + g.W] + h[k - g.W]) * .25;
                tmp[k] = orig[k] + (avg - orig[k]) * w + (h[k] - orig[k]) * w * 0;
                tmp[k] = w * avg + (1 - w) * orig[k];
            }
            Array.Copy(tmp, h, g.N);
        }
    }

    /// <summary>
    /// Mountains on the membrane: each summit lifts a massif whose flank falls off over its own radius
    /// (smoothstep, so it has a rounded shoulder rather than a cone's point), and each authored crest lifts a
    /// ridge 1.2 km either side of its line. Heights are what the summit table allows; nothing exceeds the
    /// macro ceiling, and the hard anchors are put back exactly afterwards.
    /// </summary>
    static double[] Massifs(Mass m, double[] basin, double[] lo, double[] up)
    {
        var g = m.G; var c = m.C; var z = (double[])basin.Clone();
        var crestTop = new Dictionary<string, double>();
        foreach (var (p, top, id) in c.Summits)
        {
            int k0 = g.Nearest(p); double lift = c.Fix[k0] - basin[k0]; if (lift <= 0) continue;
            double r = id == "Frank Church" || id == "Sierra" ? 3.2 : id == "Drake Hill" ? 1.0 : 2.4;
            for (int k = 0; k < g.N; k++)
            {
                double d = (g.P(k) - p).Length; if (d >= r) continue;
                double s = 1 - IslandTerrain.Smooth(d / r); s = Math.Pow(s, 1.3);
                z[k] = Math.Max(z[k], basin[k] + lift * s);
            }
        }
        foreach (var q in IslandTerrain.World["height_field"]["crests"].Items)
        {
            double top = q.N("top_ft") * Ft; const double r = 1.2;
            for (int k = 0; k < g.N; k++)
            {
                var (d, _) = IslandTerrain.Path(g.P(k), q["path"]); if (d >= r) continue;
                double s = 1 - IslandTerrain.Smooth(d / r);
                z[k] = Math.Max(z[k], basin[k] + Math.Max(0, Math.Min(top, up[k]) - basin[k]) * s * .9);
            }
        }
        for (int k = 0; k < g.N; k++) z[k] = Math.Clamp(z[k], lo[k], Math.Max(lo[k], up[k]));
        foreach (var (p, _, _) in c.Summits) { int k = g.Nearest(p); z[k] = c.Fix[k]; }
        for (int k = 0; k < g.N; k++) if (!double.IsNaN(c.Fix[k])) z[k] = c.Fix[k];
        return z;
    }

    /// <summary>Below a ceiling, ground that would stand higher is compressed smoothly into the last 20 m
    /// under it instead of being sheared flat, so no plane of the clearance surface shows in the terrain.</summary>
    static double SoftClamp(double h, double lo, double up)
    {
        const double s = 20;
        if (up <= lo) return lo;
        double knee = up - s; if (knee < lo) return Math.Clamp(h, lo, up);
        if (h > knee) h = knee + s * (1 - Math.Exp(-(h - knee) / s));
        return Math.Max(lo, h);
    }

    /// <summary>Low ground is not a table: glacial drift and old marine terraces give it 10-60 m of rolling
    /// relief (hummocks ~0.4-1.5 km across), fading out round fixed surfaces and at the shoreline.</summary>
    static void Lowland(Mass m, double[] h, uint seed)
    {
        var g = m.G; var mb = g.Blur(h, 40);
        for (int k = 0; k < g.N; k++)
        {
            if (m.Fixed[k] || m.C.Ocean[k]) continue;
            var p = g.P(k); double low = 1 - IslandTerrain.Smooth((mb[k] - 150) / 500);
            double shore = IslandTerrain.Smooth(m.C.Shore[k] / .6);
            double n = Noise.Fbm(p, 1.4, 5, seed + 101, .5);
            h[k] = SoftClamp(h[k] + low * shore * 55 * (n + .25), Math.Max(.5, m.C.Lo[k]), m.Upper[k]);
        }
    }

    static void Erode(Mass m, uint seed, Action<string> say)
    {
        var g = m.G; var c = m.C; var h = new double[g.N];
        for (int k = 0; k < g.N; k++)
        {
            var p = g.P(k); double relief = Math.Clamp(m.Macro[k] / 1500, .15, 1);
            h[k] = m.Fixed[k] ? (c.Ocean[k] ? -40 : c.Fix[k]) : m.Macro[k] + 200 * relief * Noise.Fbm(p + new IslandPoint(.8 * Noise.Fbm(p, 4, 2, seed + 7), .8 * Noise.Fbm(p, 4, 2, seed + 9)), 2.6, 8, seed, .55);
        }
        var ev = new Evolve(g); var kf = new double[g.N];
        var mb = g.Blur(m.Macro, 24);
        const double dt = 2000, mExp = .5; const int steps = 300;
        var outlet = c.Ocean;
        for (int t = 0; t < steps; t++)
        {
            ev.Route(h, outlet);
            ev.Accumulate(m.Rain);
            for (int k = 0; k < g.N; k++) kf[k] = Erodibility(g.P(k), h[k]);
            ev.Incise(h, m.Fixed, kf, mExp, dt);
            if (t % 4 == 0)
            {
                var hb = g.Blur(h, 24);
                for (int k = 0; k < g.N; k++) if (!m.Fixed[k]) h[k] += (mb[k] - hb[k]) * 4 / 30.0;
            }
            ev.Creep(h, m.Fixed, .04, dt);
            if (t % 2 == 0) ev.Talus(h, m.Fixed, 1.2, 1);
            for (int k = 0; k < g.N; k++) if (!m.Fixed[k]) h[k] = SoftClamp(h[k], Math.Max(.5, c.Lo[k]), m.Upper[k]);
            if (t % 40 == 0) say($"  step {t}");
        }
        // The erosion supplies the relief below ~1 km; the anchored macro surface supplies everything above.
        // Restore the macro exactly at low frequency: h + blur(M - h). Drainage survives because the
        // correction is smooth (sigma ~0.5 km) and the eroded valleys already follow M's own slopes.
        var corr = new double[g.N]; for (int k = 0; k < g.N; k++) corr[k] = m.Macro[k] - h[k];
        corr = g.Blur(corr, 20);
        for (int k = 0; k < g.N; k++) if (!m.Fixed[k]) h[k] = SoftClamp(h[k] + corr[k], Math.Max(.5, c.Lo[k]), m.Upper[k]);
        Lowland(m, h, seed);
        // A short second run integrates the lowland relief into the drainage (no closed hollows left).
        for (int t = 0; t < 30; t++)
        {
            ev.Route(h, outlet); ev.Accumulate(m.Rain);
            for (int k = 0; k < g.N; k++) kf[k] = Erodibility(g.P(k), h[k]);
            ev.Incise(h, m.Fixed, kf, mExp, 600);
            for (int k = 0; k < g.N; k++) if (!m.Fixed[k]) h[k] = SoftClamp(h[k], Math.Max(.5, c.Lo[k]), m.Upper[k]);
        }
        Feather(m, h);
        ev.Route(h, outlet); ev.Accumulate(m.Rain);
        m.H = h;
        m.Wet = new double[g.N];
        for (int k = 0; k < g.N; k++) m.Wet[k] = Math.Clamp((Math.Log10(Math.Max(1, ev.Discharge[k])) - 5.0) / 2.5, 0, 1);
    }
}
