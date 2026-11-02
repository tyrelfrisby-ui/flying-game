using FlyingGame.Core;
using IslandTerrainGen;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
if (Environment.GetEnvironmentVariable("VFR") == "1") Constraints.VfrEndLengthKm = 1.524;
if (Environment.GetEnvironmentVariable("VFRGUARD") is string vg) { Constraints.VfrGuardKm = double.Parse(vg); Constraints.VfrBlendKm = double.Parse(Environment.GetEnvironmentVariable("VFRBLEND") ?? ".2"); }
if (Environment.GetEnvironmentVariable("A2MEA") is string a2) Constraints.AirwayCapOverride[11] = (double.Parse(a2) - 2025) * .3048;
string cmd = args.Length > 0 ? args[0] : "help";
switch (cmd)
{
    case "survey":
        if (args.Contains("--vfr")) Constraints.VfrEndLengthKm = 1.524;
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
    {
        string outDir = args.Length > 1 ? args[1] : "/private/tmp/island-terrain-gen"; Directory.CreateDirectory(outDir);
        Constraints.VfrEndLengthKm = 1.524; Constraints.VfrGuardKm = 0; Constraints.VfrBlendKm = .1;
        var g = new Grid(-12.0, -10.4, .025, 840, 752);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var m = Generate.Build("mainland", g, IslandTerrain.World["land_polygons"][0].Points, 20261102, s => Console.WriteLine($"[{sw.Elapsed.TotalSeconds,6:F1}s] {s}"));
        foreach (var l in m.Log) Console.WriteLine(l);
        Preview.Hillshade(Path.Combine(outDir, "macro.png"), g, m.Macro);
        Preview.Hillshade(Path.Combine(outDir, "relief.png"), g, m.H, m.Ice);
        Preview.Scalar(Path.Combine(outDir, "rain.png"), g, m.Rain, 0, 4.5);
        foreach (double nn in new[] { 1.0, -2.0, 4.5 })
        {
            Console.Write($"N={nn}: ");
            for (double ee = -10; ee <= 7; ee += 1) { int k = g.Nearest(new IslandPoint(ee, nn)); Console.Write($"{ee}:{m.H[k]:F0}/{m.Macro[k]:F0}/{Math.Min(9999, m.Upper[k]):F0} "); }
            Console.WriteLine();
        }
        Console.WriteLine($"rain min {m.Rain.Min():F2} max {m.Rain.Max():F2}; ELA at max {Climate.Ela(m.Rain.Max()):F0} m, at 1.8 m {Climate.Ela(1.8):F0} m");
        break;
    }
    default:
        Console.WriteLine("TerrainGen survey [outdir] | generate | check");
        break;
}
