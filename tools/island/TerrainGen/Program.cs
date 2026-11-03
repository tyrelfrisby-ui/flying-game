using FlyingGame.Core;
using IslandTerrainGen;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
string cmd = args.Length > 0 ? args[0] : "help";
Topo.Repo = repo;
switch (cmd)
{
    case "survey":
        Survey.Budgets();
        Survey.Run(args.Length > 1 ? args[1] : "/private/tmp/island-terrain-survey", IslandTerrain.HeightKm, "shipping");
        ConstraintMap.Run(args.Length > 1 ? args[1] : "/private/tmp/island-terrain-survey");
        EnvelopeMap.Run(args.Length > 1 ? args[1] : "/private/tmp/island-terrain-survey");
        break;
    case "probe":
        for (int a = 1; a + 1 < args.Length; a += 2) Probe.Run(double.Parse(args[a]), double.Parse(args[a + 1]));
        break;
    case "ceilings":
    {
        // Coarse map of the runtime ceilings (min of every cap) in hundreds of metres, '~' sea, '.' none.
        var g = new Grid(-12.0, -10.4, .025, 840, 752); var c = new Constraints(g, IslandTerrain.World["land_polygons"][0].Points);
        for (double n = 8.25; n > -10.4; n -= .5)
        {
            var line = new System.Text.StringBuilder($"{n,6:F2} ");
            for (double e = -11.75; e < 9; e += .5)
            {
                int k = g.Nearest(new IslandPoint(e, n)); double hi = c.Hi[k];
                line.Append(c.Ocean[k] ? " ~" : !double.IsNaN(c.Fix[k]) ? " F" : double.IsInfinity(hi) ? " ." : $"{Math.Min(99, (int)(hi / 100)),2}");
            }
            Console.WriteLine(line);
        }
        break;
    }
    case "allowed":
    {
        // Coarse map of the highest ground that can stand anywhere: min(42-degree envelope of every ceiling,
        // 55-degree envelope of the sea), hundreds of metres.
        var g = new Grid(-12.0, -10.4, .025, 840, 752); var c = new Constraints(g, IslandTerrain.World["land_polygons"][0].Points);
        var src = new double[g.N]; for (int k = 0; k < g.N; k++) src[k] = double.IsFinite(c.Hi[k]) ? c.Hi[k] : double.PositiveInfinity;
        var env = Envelope.Compute(g, src, Generate.EnvelopeSlope);
        for (int k = 0; k < g.N; k++) src[k] = c.Ocean[k] ? 0 : double.PositiveInfinity;
        var coast = Envelope.Compute(g, src, Generate.CoastSlope);
        Console.Write("       "); for (double e = -11.75; e < 9; e += .5) Console.Write(Math.Abs(e % 1) < .01 ? $"{e,2:F0}" : "  "); Console.WriteLine();
        for (double n = 8.25; n > -10.4; n -= .25)
        {
            var line = new System.Text.StringBuilder($"{n,6:F2} ");
            for (double e = -11.75; e < 9; e += .5)
            {
                int k = g.Nearest(new IslandPoint(e, n)); double a = Math.Min(env[k], coast[k]);
                line.Append(c.Ocean[k] ? " ~" : !double.IsNaN(c.Fix[k]) ? " F" : $"{Math.Min(99, (int)(a / 100)),2}");
            }
            Console.WriteLine(line);
        }
        break;
    }
    case "bound":
    {
        // Which ceiling (or the sea) holds the ground down at a point: argmin of Hi + 0.9 d and of 1.43 d to the sea.
        var g = new Grid(-12.0, -10.4, .025, 840, 752); var c = new Constraints(g, IslandTerrain.World["land_polygons"][0].Points);
        for (int a = 1; a + 1 < args.Length; a += 2)
        {
            var p = new IslandPoint(double.Parse(args[a]), double.Parse(args[a + 1])); double best = double.PositiveInfinity, sea = double.PositiveInfinity; int bk = -1;
            for (int k = 0; k < g.N; k++)
            {
                double d = (g.P(k) - p).Length * 1000;
                if (c.Ocean[k]) { sea = Math.Min(sea, 1.43 * d); continue; }
                if (double.IsFinite(c.Hi[k]) && c.Hi[k] + .9 * d < best) { best = c.Hi[k] + .9 * d; bk = k; }
            }
            Console.WriteLine($"({p.E},{p.N}): ceiling envelope {best:F0} m from Hi {(bk >= 0 ? c.Hi[bk] : 0):F0} at ({g.P(bk).E:F3},{g.P(bk).N:F3}); sea envelope {sea:F0} m");
            if (bk >= 0) Probe.Run(g.P(bk).E, g.P(bk).N);
        }
        break;
    }
    case "hi":
        HiDebug.Run(double.Parse(args[1]), double.Parse(args[2]), 3);
        break;
    case "generate":
    case "check":
    {
        bool check = cmd == "check";
        string outDir = args.Length > 1 ? args[1] : Path.Combine(repo, "build/island-relief"); Directory.CreateDirectory(outDir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Say(string s) => Console.WriteLine($"[{sw.Elapsed.TotalSeconds,6:F1}s] {s}");
        var main = Generate.Build("mainland", new Grid(-12.0, -10.4, .025, 840, 752), IslandTerrain.World["land_polygons"][0].Points, 20261102, Say);
        var cat = Generate.Build("catalina", new Grid(0.8, -15.95, .025, 217, 157), IslandTerrain.World["land_polygons"][1].Points, 20261103, Say);
        var masses = new[] { main, cat };
        if (Environment.GetEnvironmentVariable("CELL") is string cellp)
        {
            foreach (var cell in cellp.Split(';'))
            {
                var pp = cell.Split(','); int k = main.G.Nearest(new IslandPoint(double.Parse(pp[0]), double.Parse(pp[1])));
                Console.WriteLine($"cell ({cell}) H {main.H[k]:F1} Hi {main.C.Hi[k]:F1} Lo {main.C.Lo[k]:F1} Fix {main.C.Fix[k]:F1} Upper {main.Upper[k]:F1} Lower {main.Lower[k]:F1} Macro {main.Macro[k]:F1} topo {main.TopoZ[k]:F1} w {main.TopoW[k]:F2} ice {main.Ice[k]:F2} rain {main.Rain[k]:F2} fixed {main.Fixed[k]}");
            }
        }
        if (Environment.GetEnvironmentVariable("PROBE") is string probe)
        {
            var pp = probe.Split(','); var p0 = new IslandPoint(double.Parse(pp[0]), double.Parse(pp[1]));
            for (int k = 0; k < main.G.N; k++)
                if (main.Conflict[k] > 1e5 && (main.G.P(k) - p0).Length < .4)
                { var q = main.G.P(k); Console.WriteLine($"hard at ({q.E:F3},{q.N:F3}) Lower {main.Lower[k]:F1} Hi {main.C.Hi[k]:F1} Lo {main.C.Lo[k]:F1}"); Probe.Run(q.E, q.N); break; }
        }
        if (Environment.GetEnvironmentVariable("DUMP") is string dump)
        {
            // Debug dump of the main lattice (float32 LE): height, ice, rain, topo weight, fixed.
            using var f = new BinaryWriter(File.Create(dump));
            foreach (var arr in new[] { main.H, main.Ice, main.Rain, main.TopoW }) foreach (var v in arr) f.Write((float)v);
            foreach (var v in main.Fixed) f.Write(v ? 1f : 0f);
        }
        if (Environment.GetEnvironmentVariable("SKI_SEARCH") is string box)
        {
            // Glacier ski-zone candidates: 450 m of ice-covered ground within the locked 1,000-1,300 ft band,
            // along its best heading, rising no more than 6% end to end and 3% across, all of it ice.
            var b = box.Split(',').Select(double.Parse).ToArray(); var g = main.G; var found = new List<(double score, string line)>();
            for (double e = b[0]; e <= b[1]; e += .05) for (double n = b[2]; n <= b[3]; n += .05)
            {
                var p = new IslandPoint(e, n); int k = g.Nearest(p); if (k < 0 || main.Ice[k] < .7) continue;
                double z = main.H[k]; if (z < 1000 * IslandTerrain.Feet || z > 1300 * IslandTerrain.Feet) continue;
                for (int hd = 0; hd < 180; hd += 15)
                {
                    var u = IslandPoint.Heading(hd); var v = new IslandPoint(u.N, -u.E); bool ok = true; double lo = 1e9, hi = -1e9, worst = 0;
                    for (double a = -.225; a <= .2251 && ok; a += .075) for (double c = -.075; c <= .0751; c += .075)
                    {
                        int q = g.Nearest(p + u * a + v * c); if (q < 0 || main.Ice[q] < .5 || main.Fixed[q]) { ok = false; break; }
                        lo = Math.Min(lo, main.H[q]); hi = Math.Max(hi, main.H[q]);
                    }
                    if (!ok) continue;
                    double along = Math.Abs(g.Sample(main.H, p + u * .225) - g.Sample(main.H, p - u * .225)) / 450, across = Math.Abs(g.Sample(main.H, p + v * .1) - g.Sample(main.H, p - v * .1)) / 200;
                    if (along > double.Parse(Environment.GetEnvironmentVariable("SKI_ALONG") ?? ".06") || across > .03 * double.Parse(Environment.GetEnvironmentVariable("SKI_ALONG") ?? ".06") / .06) continue;
                    found.Add((along + across + (hi - lo) / 1000, $"ski candidate ({e:F2},{n:F2}) {z:F0} m ({z / IslandTerrain.Feet:F0} ft) heading {hd} along {along * 100:F1}% across {across * 100:F1}% relief {hi - lo:F0} m ice {main.Ice[k]:F2}"));
                }
            }
            foreach (var f in found.OrderBy(f => f.score).Take(25)) Console.WriteLine(f.line);
        }
        foreach (var m in masses) for (int k = 0; k < m.G.N; k++) m.H[k] = Export.Dq(Export.Q(m.H[k])); // what the runtime will hold
        var data = Export.Encode(masses);
        var summary = string.Join("\n", masses.SelectMany(m => m.Log.Select(l => m.Name + ": " + l)));
        string source = Export.Source(data, summary);
        string target = Path.Combine(repo, "src/FlyingGame.Core/IslandRelief.Generated.cs");
        if (check)
        {
            bool same = File.Exists(target) && File.ReadAllText(target) == source;
            Console.WriteLine(same ? "IslandRelief.Generated.cs is byte-identical to a fresh generation." : "STALE: IslandRelief.Generated.cs differs from a fresh generation.");
            if (!same) Environment.Exit(1);
            break;
        }
        File.WriteAllText(target, source);
        Console.WriteLine($"wrote {target}: {data.Length} bytes of data, {source.Length} characters");
        foreach (var m in masses)
        {
            foreach (var l in m.Log) Console.WriteLine(m.Name + ": " + l);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-relief.png"), m.G, m.H, m.Ice);
            Preview.Scalar(Path.Combine(outDir, m.Name + "-rain.png"), m.G, m.Rain, 0, 5);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-topo-areas.png"), m.G, m.H, m.TopoW);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-macro.png"), m.G, m.Macro);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-snow.png"), m.G, m.H, m.Snow);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-crevasse.png"), m.G, m.H, m.Crevasse);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-bare-ice.png"), m.G, m.H, m.Rough);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-debris.png"), m.G, m.H, m.Debris);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-protect.png"), m.G, m.H, m.Protect);
            Preview.Hillshade(Path.Combine(outDir, m.Name + "-conflicts.png"), m.G, m.H, m.Conflict.Select(v => v > 1e5 ? 1.0 : v > 0 ? .35 : 0).ToArray());
        }
        break;
    }
    case "agreement":
    {
        // The mesh's LOD0 surface (25 m nodes from IslandTerrain.HeightAt, split SW-NE like IslandTerrainMesh)
        // against the continuous height at each cell's centre and edge midpoints, over land.
        WorldTerrain.Active = new WorldTerrain();
        bool baseOnly = args.Contains("--relief");
        double HeightAt(double xx, double yy) => baseOnly ? IslandRelief.Height(new IslandPoint(yy / 1000, xx / 1000)) : IslandTerrain.HeightAt(xx, yy);
        var errs = new List<double>(); double worstPad = 0; int padSamples = 0; var worst = (0.0, 0.0, 0.0);
        for (double x = -10000; x < 8000; x += 25) for (double y = -12000; y < 9000; y += 25)
        {
            double a = HeightAt(x, y); if (a < 1) continue;
            if (!baseOnly && IslandRelief.NeedsFineMesh(y / 1000, x / 1000, (y + 25) / 1000, (x + 25) / 1000)) continue; // drawn with 5 m cells, below
            double b = HeightAt(x, y + 25), d = HeightAt(x + 25, y), e = HeightAt(x + 25, y + 25);
            foreach (var (tx, ty) in new[] { (.5, .5), (.25, .75), (.75, .25), (.5, 0.0), (0.0, .5) })
            {
                double mesh = tx >= ty ? a + (b - a) * tx + (e - b) * ty : a + (e - d) * tx + (d - a) * ty; // tx along east (y), ty along north (x)
                double truth = HeightAt(x + ty * 25, y + tx * 25), err = Math.Abs(mesh - truth);
                errs.Add(err); if (err > worst.Item1) worst = (err, y / 1000, x / 1000);
                var p = new IslandPoint((y + tx * 25) / 1000, (x + ty * 25) / 1000);
                foreach (var f in IslandTerrain.Fields) if (f.Field != "DRAKE" && f.Contains(p)) { worstPad = Math.Max(worstPad, err); padSamples++; break; }
            }
        }
        // The 5 m sub-cells the mesh uses where crevasses and boulders are (IslandRelief.NeedsFineMesh), measured the
        // same way: each 5 m cell split SW-NE, against HeightAt at its centre and edge midpoints.
        var fineErrs = new List<double>();
        if (!baseOnly)
            for (double x = -10000; x < 8000; x += 25) for (double y = -12000; y < 9000; y += 25)
            {
                if (!IslandRelief.NeedsFineMesh(y / 1000, x / 1000, (y + 25) / 1000, (x + 25) / 1000)) continue;
                for (double fx = 0; fx < 25; fx += 5) for (double fy = 0; fy < 25; fy += 5)
                {
                    double a = HeightAt(x + fx, y + fy), b = HeightAt(x + fx, y + fy + 5), d = HeightAt(x + fx + 5, y + fy), e = HeightAt(x + fx + 5, y + fy + 5);
                    foreach (var (tx, ty) in new[] { (.5, .5), (.25, .75), (.75, .25), (.5, 0.0), (0.0, .5) })
                    {
                        double mesh = tx >= ty ? a + (b - a) * tx + (e - b) * ty : a + (e - d) * tx + (d - a) * ty;
                        fineErrs.Add(Math.Abs(mesh - HeightAt(x + fx + ty * 5, y + fy + tx * 5)));
                    }
                }
            }
        fineErrs.Sort();
        if (fineErrs.Count > 0) Console.WriteLine($"fine (5 m) cells: {fineErrs.Count} samples; median {fineErrs[fineErrs.Count / 2]:F2} m, 95% {fineErrs[(int)(fineErrs.Count * .95)]:F2} m, 99% {fineErrs[(int)(fineErrs.Count * .99)]:F2} m, max {fineErrs[^1]:F1} m");
        errs.Sort();
        Console.WriteLine($"mesh vs HeightAt over land: {errs.Count} samples; median {errs[errs.Count / 2]:F2} m, 95% {errs[(int)(errs.Count * .95)]:F2} m, 99% {errs[(int)(errs.Count * .99)]:F2} m, max {errs[^1]:F1} m at ({worst.Item2:F3},{worst.Item3:F3}) km; on runway/strip surfaces ({padSamples} samples) max {worstPad:F3} m");
        break;
    }
    case "wall":
    {
        WorldTerrain.Active = new WorldTerrain();
        foreach (double x in new[] { -3880.0, -3878, -3877, -3876.5, -3876.4, -3876, -3875, -3874, -3872, -3870 })
        {
            double h = IslandTerrain.HeightAndGradientAt(x, -5000, out double dx, out double dy);
            Console.WriteLine($"x {x} y -5000: h {h:F2} dh/dx {dx:F2} wall30 {WorldTerrain.IsWallAt(x, -5000, 30)}");
        }
        break;
    }
    case "surfaces":
    {
        // Glacier-surface statistics on the runtime height function (Ty's surfaces spec).
        WorldTerrain.Active = new WorldTerrain();
        var boulders = IslandRelief.Boulders();
        Console.WriteLine($"boulders: {boulders.Count}, radius {boulders.Select(b => b.r).DefaultIfEmpty().Min():F1}-{boulders.Select(b => b.r).DefaultIfEmpty().Max():F1} m, height {boulders.Select(b => b.h).DefaultIfEmpty().Min():F1}-{boulders.Select(b => b.h).DefaultIfEmpty().Max():F1} m");
        foreach (var b in boulders.Take(8)) Console.WriteLine($"  boulder at ({b.e / 1000:F3},{b.n / 1000:F3}) km r {b.r:F1} h {b.h:F1}");
        int onset = 0, fineCells = 0, iceCells = 0, crevCells = 0, snowCells = 0, ledgeCells = 0; double deepest = 0, undMax = 0;
        var undul = new List<double>();
        for (double e = -12; e < 9; e += .025) for (double n = -10.4; n < 8.4; n += .025)
        {
            var p = new IslandPoint(e, n); var (ice, _, _, _) = IslandRelief.Channels(p); var (snow, rough, crev, ledge) = IslandRelief.Classes(p);
            if (ledge > .5) ledgeCells++;
            if (snow > .5) snowCells++;
            if (ice > .5 && snow <= .5) iceCells++;
            if (crev > .5) crevCells++;
            if (IslandRelief.NeedsFineMesh(e, n, e + .025, n + .025)) fineCells++;
            if (crev > .05) deepest = Math.Min(deepest, IslandRelief.FineDetail(p));
            if (crev > IslandRelief.CrevasseOnset) onset++;
        }
        double a = .025 * .025;
        // Analytic gradient against central differences, on detail ground only (skipping crevasse lips and
        // boulder rims, where the slope is legitimately discontinuous).
        var rng = new Random(7); int tested = 0; double worstRel = 0; var bad = (0.0, 0.0, 0.0, 0.0);
        while (tested < 4000)
        {
            double x = -10000 + rng.NextDouble() * 18000, y = -12000 + rng.NextDouble() * 21000; var p = new IslandPoint(y / 1000, x / 1000);
            var (ice, _, _, _) = IslandRelief.Channels(p); if (ice < .3) continue;
            double h = IslandTerrain.HeightAndGradientAt(x, y, out double gx, out double gy); const double d = .02;
            double fx = (IslandTerrain.HeightAt(x + d, y) - IslandTerrain.HeightAt(x - d, y)) / (2 * d), fy = (IslandTerrain.HeightAt(x, y + d) - IslandTerrain.HeightAt(x, y - d)) / (2 * d);
            double fx2 = (IslandTerrain.HeightAt(x + 2 * d, y) - IslandTerrain.HeightAt(x - 2 * d, y)) / (4 * d);
            double fy2 = (IslandTerrain.HeightAt(x, y + 2 * d) - IslandTerrain.HeightAt(x, y - 2 * d)) / (4 * d);
            if (Math.Abs(fx2 - fx) > 1e-3 * (1 + Math.Abs(fx)) || Math.Abs(fy2 - fy) > 1e-3 * (1 + Math.Abs(fy))) continue; // a kink inside the stencil
            double err = Math.Max(Math.Abs(gx - fx), Math.Abs(gy - fy)) / (1 + Math.Max(Math.Abs(fx), Math.Abs(fy)));
            if (err > worstRel) { worstRel = err; bad = (x, y, gx - fx, gy - fy); }
            tested++;
        }
        Console.WriteLine($"gradient check on ice: {tested} points, worst relative error {worstRel:E2} at x {bad.Item1:F1} y {bad.Item2:F1} (dx err {bad.Item3:E2}, dy err {bad.Item4:E2})");
        Console.WriteLine($"classes (km2): snow {snowCells * a:F2}, bare ice {iceCells * a:F2}, crevassed {crevCells * a:F2}, ledge {ledgeCells * a:F3}; fine-mesh cells {fineCells} ({fineCells * a:F2} km2), crevasse channel above onset {onset * a:F2} km2; deepest fine detail {deepest:F1} m");
        break;
    }
    case "surfacepoints":
    {
        // Candidate literal positions for the surface tests: a 300 m run of bare ice, of glacier snow, a full-
        // strength crevasse field, and the biggest boulders (sim x north, y east, metres).
        WorldTerrain.Active = new WorldTerrain();
        var boulders = IslandRelief.Boulders();
        bool NearBoulder(double e, double n, double r) => boulders.Any(b => (b.e - e) * (b.e - e) + (b.n - n) * (b.n - n) < r * r);
        var found = new Dictionary<string, List<string>> { ["ice"] = new(), ["snow"] = new(), ["crevasse"] = new() };
        for (double x = -10000; x < 8000; x += 50) for (double y = -12000; y < 9000; y += 50)
        {
            var p = new IslandPoint(y / 1000, x / 1000); var (ice, _, _, _) = IslandRelief.Channels(p); if (ice < .9) continue;
            var (snow, rough, crev, _) = IslandRelief.Classes(p);
            IslandTerrain.HeightAndGradientAt(x, y, out double gx, out double gy); double g = Math.Sqrt(gx * gx + gy * gy); if (g < 1e-4) continue;
            double ux = -gx / g, uy = -gy / g; // downhill
            bool All(Func<IslandPoint, bool> ok) { for (double t = -150; t <= 150; t += 10) { var q = new IslandPoint((y + uy * t) / 1000, (x + ux * t) / 1000); if (!ok(q)) return false; } return true; }
            string where = $"x {x:F0} y {y:F0} downhill {Math.Atan2(uy, ux) * 180 / Math.PI:F0} deg slope {Math.Atan(g) * 180 / Math.PI:F1} deg";
            if (rough > .6 && crev < .1 && All(q => IslandRelief.Classes(q).rough > .5 && IslandRelief.Classes(q).crevasse < IslandRelief.CrevasseOnset && IslandTerrain.SurfaceAt(q.X, q.Y) == WorldTerrain.Surface.GlacierIce) && !NearBoulder(y, x, 60) && g < .2) found["ice"].Add(where);
            if (snow > .97 && All(q => IslandRelief.Classes(q).snow > .95 && IslandRelief.Classes(q).crevasse < .01) && g < .12) found["snow"].Add(where);
            if (crev > .7 && IslandRelief.Classes(p).rough > .5) found["crevasse"].Add(where + $" c {crev:F2}");
        }
        foreach (var (k, v) in found) { Console.WriteLine($"{k}: {v.Count} candidates"); foreach (var l in v.Where((_, i) => i % Math.Max(1, v.Count / 6) == 0).Take(6)) Console.WriteLine("  " + l); }
        foreach (var b in boulders.OrderByDescending(b => b.h).Take(4)) Console.WriteLine($"boulder x {b.n:F1} y {b.e:F1} r {b.r:F1} h {b.h:F1} ground {IslandTerrain.HeightAt(b.n, b.e):F1} ring {IslandTerrain.HeightAt(b.n + b.r + 3, b.e):F1} surface {IslandTerrain.SurfaceAt(b.n, b.e)}");
        break;
    }
    case "icemap":
    {
        // ASCII map, 200 m cells, north up: '*' snow, 'i' bare ice, 'C' crevasse field, 'd' debris on ice, 'o' boulder,
        // 'P' snow patch, 'L' ledge, '~' sea.
        WorldTerrain.Active = new WorldTerrain();
        var bs = IslandRelief.Boulders();
        Console.Write("       "); for (double e = -11.9; e < 9; e += .2) Console.Write(Math.Abs(e - Math.Round(e)) < .05 && ((int)Math.Round(e)) % 2 == 0 ? (Math.Round(e) < 0 ? "-" : "+") : " "); Console.WriteLine();
        for (double n = 8.3; n > -10.4; n -= .2)
        {
            var line = new System.Text.StringBuilder($"{n,6:F1} ");
            for (double e = -11.9; e < 9; e += .2)
            {
                var p = new IslandPoint(e, n); double h = IslandTerrain.HeightKm(p);
                var (ice, debris, _, _) = IslandRelief.Channels(p); var (snow, rough, crev, ledge) = IslandRelief.Classes(p);
                bool boulder = bs.Any(b => Math.Abs(b.e / 1000 - e) < .1 && Math.Abs(b.n / 1000 - n) < .1);
                char c = h < 0 ? '~' : ledge > .5 ? 'L' : snow > .5 && ice < .5 ? 'P' : boulder ? 'o' : ice > .5 ? (crev > IslandRelief.CrevasseOnset ? 'C' : snow > .5 ? '*' : debris > .35 ? 'd' : 'i') : '.';
                line.Append(c);
            }
            Console.WriteLine(line);
        }
        break;
    }
    case "bench":
    {
        WorldTerrain.Active = new WorldTerrain(); IslandGeometry.Build();
        var pts = new List<(double x, double y)>(); for (int i = 0; i < 200000; i++) pts.Add((-9000 + (i * 7919 % 17000), -10000 + (i * 104729 % 18000) + (i % 7) * 3.1));
        void T(string n, Func<double, double, double> f)
        {
            double s0 = 0; var w = System.Diagnostics.Stopwatch.StartNew(); foreach (var (x, y) in pts) s0 += f(x, y);
            Console.WriteLine($"{n,-28} {w.Elapsed.TotalMilliseconds * 1000 / pts.Count:F2} us/query ({s0:E2})");
        }
        T("HeightAndGradientAt", (x, y) => IslandTerrain.HeightAndGradientAt(x, y, out _, out _));
        T("HeightAt (fresh x+0.5)", (x, y) => IslandTerrain.HeightAt(x + .5, y));
        T("IslandRelief.Channels", (x, y) => IslandRelief.Channels(new IslandPoint(y / 1000, x / 1000)).elaM);
        T("Forest.CoverAt(h,slope)", (x, y) => Forest.CoverAt(x, y, 500, .2, .1));
        T("SurfaceAt", (x, y) => (double)IslandTerrain.SurfaceAt(x, y));
        T("Forest.Timbered", (x, y) => Forest.Timbered(x, y) ? 1 : 0);
        break;
    }
    default:
        Console.WriteLine("TerrainGen survey [outdir] | generate | check");
        break;
}
