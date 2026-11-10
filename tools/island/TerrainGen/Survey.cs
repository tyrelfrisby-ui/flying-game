using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>Read-only look at the shipping island: images of its height field and a list of the clearance caps.</summary>
public static class Survey
{
    public static void Run(string outDir, Func<IslandPoint, double> height, string tag)
    {
        Directory.CreateDirectory(outDir);
        const double e0 = -12.0, n0 = -10.4, cell = .025; int w = 840, hgt = 752;
        var hf = new double[w * hgt];
        Parallel.For(0, hgt, j => { for (int i = 0; i < w; i++) hf[j * w + i] = height(new IslandPoint(e0 + i * cell, n0 + j * cell)); });
        Png.Write(Path.Combine(outDir, tag + "-hillshade.png"), w, hgt, (x, y) =>
        {
            int j = hgt - 1 - y, i = x;
            int il = Math.Max(0, i - 1), ir = Math.Min(w - 1, i + 1), jd = Math.Max(0, j - 1), ju = Math.Min(hgt - 1, j + 1);
            double dx = (hf[j * w + ir] - hf[j * w + il]) / ((ir - il) * cell * 1000), dy = (hf[ju * w + i] - hf[jd * w + i]) / ((ju - jd) * cell * 1000);
            return Png.Shade(hf[j * w + i], dx, dy, cell);
        });
        // Slope histogram over land: fraction of land steeper than given angles, and the flat-cell fraction.
        int land = 0, flat = 0; var bins = new int[10];
        for (int j = 1; j < hgt - 1; j++) for (int i = 1; i < w - 1; i++)
        {
            double h = hf[j * w + i]; if (h <= 0) continue; land++;
            double dx = (hf[j * w + i + 1] - hf[j * w + i - 1]) / 50, dy = (hf[(j + 1) * w + i] - hf[(j - 1) * w + i]) / 50;
            double s = Math.Atan(Math.Sqrt(dx * dx + dy * dy)) * 180 / Math.PI; bins[Math.Min(9, (int)(s / 10))]++;
            if (s < .5) flat++;
        }
        Console.WriteLine($"{tag}: land cells {land} ({land * .000625:F1} km2), dead-flat (<0.5 deg) {100.0 * flat / land:F1}%, max {hf.Max():F0} m");
        Console.WriteLine("  slope % by 10-degree bin: " + string.Join(" ", bins.Select(b => (100.0 * b / land).ToString("F1"))));
    }
    public static void Budgets()
    {
        foreach (var e in IslandTerrain.World["clearance_budgets"].Items)
        {
            if (e.S("kind") == "airway") Console.WriteLine($"airway {e.S("id", "?")} a=({e["a"].Point.E:F2},{e["a"].Point.N:F2}) b=({e["b"].Point.E:F2},{e["b"].Point.N:F2}) cap {e.N("cap_m"):F0} m ({e.N("cap_m") / .3048:F0} ft) width {e.N("width"):F3}");
            else Console.WriteLine($"final {e.S("id", e.S("title", "?"))} a=({e["a"].Point.E:F2},{e["a"].Point.N:F2}) elev {e.N("elevation_m"):F0} ratio {e.N("ratio")} len {e.N("length"):F2}");
        }
    }
}
public static class ConstraintMap
{
    public static void Run(string outDir)
    {
        var g = new Grid(-12.0, -10.4, .025, 840, 752);
        var land = IslandTerrain.World["land_polygons"][0].Points;
        var c = new Constraints(g, land);
        Png.Write(Path.Combine(outDir, "constraints.png"), g.W, g.H, (x, y) =>
        {
            int k = (g.H - 1 - y) * g.W + x;
            if (c.Ocean[k]) return (20, 40, 80);
            if (!double.IsNaN(c.Fix[k])) return (255, (byte)Math.Clamp(c.Fix[k] / 2800 * 255, 0, 255), 0);
            if (double.IsFinite(c.Lo[k])) return (0, 255, 255);
            if (double.IsFinite(c.Hi[k])) { double v = Math.Clamp(c.Hi[k] / 3000, 0, 1); return ((byte)(40 + 200 * v), (byte)(40 + 200 * v), (byte)(120 + 135 * v)); }
            return (90, 140, 70);
        });
        // Grid lines every km for reading coordinates: printed legend instead.
        Console.WriteLine("constraints.png: E from -12 (left) to 9, N from -10.4 (bottom) to 8.4; 40 px per km");
    }
}
public static class Probe
{
    public static void Run(double e, double n)
    {
        var p = new IslandPoint(e, n);
        Console.WriteLine($"({e},{n}) shipping h={IslandTerrain.HeightKm(p):F0} m");
        int i = 0;
        foreach (var b in IslandTerrain.World["clearance_budgets"].Items)
        {
            i++;
            if (b.S("kind") == "airway") { double d = IslandTerrain.Segment(p, b["a"].Point, b["b"].Point).d; if (d < b.N("width") + b.N("blend")) Console.WriteLine($"  airway#{i} cap {b.N("cap_m"):F0} d={d:F2}"); continue; }
            var u = b["u"].Point; var dp = p - b["a"].Point; double along = -dp.Dot(u), cross = Math.Abs(dp.E * u.N - dp.N * u.E);
            double dd = Math.Max(0, Math.Max(Math.Max(-along - .25, along - b.N("length") - .25), cross - (b.N("half_width_km", .15) + b.N("widen", .05) * Math.Max(0, along)) - .25));
            if (dd < b.N("blend")) Console.WriteLine($"  final#{i} a=({b["a"].Point.E:F2},{b["a"].Point.N:F2}) elev {b.N("elevation_m"):F0} along {along:F2} cap {b.N("elevation_m") + Math.Max(0, along) * 1000 / b.N("ratio") - 30:F0}");
        }
    }
}
public static class EnvelopeMap
{
    public static void Run(string outDir)
    {
        var g = new Grid(-12.0, -10.4, .025, 840, 752);
        var c = new Constraints(g, IslandTerrain.World["land_polygons"][0].Points);
        var src = new double[g.N];
        for (int k = 0; k < g.N; k++) src[k] = c.Ocean[k] ? 0 : c.Hi[k];
        var env = Envelope.Compute(g, src, Math.Tan(double.Parse(Environment.GetEnvironmentVariable("ENVSLOPE") ?? "38") * Math.PI / 180));
        foreach (var s in c.Summits) Console.WriteLine($"summit {s.id,-14} authored {s.z,6:F0} m ({s.z / .3048,5:F0} ft)  envelope {env[g.Nearest(s.p)],6:F0} m ({env[g.Nearest(s.p)] / .3048,5:F0} ft)");
        foreach (var (id, e, n) in new[] { ("Knik River strip", -5.205, -.713), ("Fairweather ski", -3.9, 4.7), ("Chugach", -3.8, -1.8) }) Console.WriteLine($"{id}: envelope {env[g.Nearest(new IslandPoint(e, n))]:F0}");
        Png.Write(Path.Combine(outDir, "envelope.png"), g.W, g.H, (x, y) =>
        {
            int k = (g.H - 1 - y) * g.W + x; if (c.Ocean[k]) return (20, 40, 80);
            double v = env[k]; if (x % 40 == 0 || y % 40 == 0) return (0, 0, 0);
            if (v > 1750) return (255, 255, 255);
            return Png.Shade(v, 0, 0, .025);
        });
    }
}
public static class HiDebug
{
    public static void Run(double e, double n, double r)
    {
        var g = new Grid(-12.0, -10.4, .025, 840, 752);
        var c = new Constraints(g, IslandTerrain.World["land_polygons"][0].Points);
        var p0 = new IslandPoint(e, n); double best = double.PositiveInfinity; IslandPoint bp = p0; double bh = 0;
        for (int k = 0; k < g.N; k++)
        {
            var p = g.P(k); double d = (p - p0).Length; if (d > r) continue;
            double v = (c.Ocean[k] ? 0 : c.Hi[k]) + Math.Tan(38 * Math.PI / 180) * d * 1000;
            if (v < best) { best = v; bp = p; bh = c.Ocean[k] ? 0 : c.Hi[k]; }
        }
        Console.WriteLine($"binding source for ({e},{n}): ({bp.E:F3},{bp.N:F3}) Hi {bh:F0} -> {best:F0}");
        Probe.Run(bp.E, bp.N);
    }
}
