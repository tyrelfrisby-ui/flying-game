using FlyingGame.Core;
using IslandTerrainGen;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
string cmd = args.Length > 0 ? args[0] : "help";
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
            var pp = cellp.Split(','); int k = main.G.Nearest(new IslandPoint(double.Parse(pp[0]), double.Parse(pp[1])));
            Console.WriteLine($"cell H {main.H[k]:F1} Hi {main.C.Hi[k]:F1} Lo {main.C.Lo[k]:F1} Fix {main.C.Fix[k]:F1} Upper {main.Upper[k]:F1} Lower {main.Lower[k]:F1} Macro {main.Macro[k]:F1} ice {main.Ice[k]:F2} fixed {main.Fixed[k]}");
        }
        if (Environment.GetEnvironmentVariable("PROBE") is string probe)
        {
            var pp = probe.Split(','); var p0 = new IslandPoint(double.Parse(pp[0]), double.Parse(pp[1]));
            for (int k = 0; k < main.G.N; k++)
                if (main.Conflict[k] > 1e5 && (main.G.P(k) - p0).Length < .4)
                { var q = main.G.P(k); Console.WriteLine($"hard at ({q.E:F3},{q.N:F3}) Lower {main.Lower[k]:F1} Hi {main.C.Hi[k]:F1} Lo {main.C.Lo[k]:F1}"); Probe.Run(q.E, q.N); break; }
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
