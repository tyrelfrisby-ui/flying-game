using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;

// Same three probes as Claude's IslandStartupCostTests, read from lane/claude-island-nav.
// Timing is evidence, not an assertion. No cache reset, quantization or baked terrain.
if(args.Contains("--queries"))
{
    QueryCost.Run(); return;
}
var probe=new CostProbe();
var initialization=System.Diagnostics.Stopwatch.StartNew();
long memoryBefore=GC.GetTotalMemory(true);
Console.WriteLine(IslandTerrain.HeightIndexWorkBounds);
Console.WriteLine($"world + index initialization: {initialization.Elapsed.TotalMilliseconds:F1} ms; retained managed bytes {GC.GetTotalMemory(true)-memoryBefore}");
probe.FreshSamples();
probe.HowExpensiveIsAGroundSample();
probe.WhatDoesOneAircraftSecondCost();
probe.WhereDoesTheStepTimeActuallyGo();

sealed class CostProbe
{
    public void FreshSamples()
    {
        var rng=new Random(7);
        var points=Enumerable.Range(0,20000).Select(_=>new IslandPoint(rng.NextDouble()*20-10,rng.NextDouble()*20-10)).ToArray();
        foreach(var eval in new Func<IslandPoint,double>[] { IslandTerrain.EvaluateReference,IslandTerrain.EvaluateCompiled,IslandTerrain.Evaluate })
        {
            double sum=0; for(int i=0;i<1000;i++) sum+=eval(points[i]);
            var sw=System.Diagnostics.Stopwatch.StartNew(); long before=GC.GetAllocatedBytesForCurrentThread();
            foreach(var p in points) sum+=eval(p);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-before; sw.Stop();
            Console.WriteLine($"{eval.Method.Name}: {sw.Elapsed.TotalMilliseconds*1000/points.Length:F3} us per fresh sample; {bytes/(double)points.Length:F1} B/sample; sum {sum:R}");
        }
        // Unique moving coordinates at the three aircraft probe locations; no memo hits.
        foreach(var c in new[]{new IslandPoint(.96,-8.06),new IslandPoint(0,0),new IslandPoint(3.5,-14),new IslandPoint(18,0)})
        {
            double sum=0; var sw=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<20000;i++) sum+=IslandTerrain.Evaluate(c+new IslandPoint(i*.000001,i*.0000007));
            sw.Stop(); Console.WriteLine($"fresh near E{c.E},N{c.N}: {sw.Elapsed.TotalMilliseconds*1000/20000:F3} us/sample; sum {sum:R}");
        }
    }
    public void HowExpensiveIsAGroundSample()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        // Warm up the generated tables and any lazy caches.
        double warm = 0;
        for (int i = 0; i < 1000; i++) warm += WorldTerrain.GroundHeightAt(i * 3.0, i * 7.0);
        Console.WriteLine($"warm {warm:F0}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int n = 20000;
        double s = 0;
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            double x = rng.NextDouble() * 20000 - 10000, y = rng.NextDouble() * 20000 - 10000;
            s += WorldTerrain.GroundHeightAt(x, y);
        }
        sw.Stop();
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
        Console.WriteLine($"GroundHeightAt: {us:F1} us per call over {n} random samples (sum {s:F0})");

        // And a realistic case: samples clustered near one aircraft, as a flight actually makes them.
        sw.Restart();
        s = 0;
        for (int i = 0; i < n; i++)
        {
            double x = 1000 + (i % 37) * 2.0, y = -8000 + (i % 53) * 2.0;
            s += WorldTerrain.GroundHeightAt(x, y);
        }
        sw.Stop();
        Console.WriteLine($"clustered: {sw.Elapsed.TotalMilliseconds * 1000.0 / n:F1} us per call (sum {s:F0})");
    }

    public void WhatDoesOneAircraftSecondCost()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));

        foreach ((string where, double x, double y, double aglM) in new[]
                 {
                     ("on the runway at KSNA", -8060.0, 960.0, 2.0),
                     ("500 ft over KSNA", -8060.0, 960.0, 152.0),
                     ("5000 ft over mid-island", 0.0, 0.0, 1524.0),
                 })
        {
            double ground = WorldTerrain.GroundHeightAt(x, y);
            var st = new RigidBodyState(new Vec3(x, y, -(ground + aglM)), new Quat(0, 0, 0, 1), new Vec3(40, 0, 0), Vec3.Zero);
            var air = new Aircraft(cfg, st, ControlDeflections.Neutral);
            var loop = new SimLoop(air);
            var ci = new ControlInputs(0, 0, 0, 0.6);
            loop.RunFor(0.1, ci);                                  // settle and warm every lazy path
            var sw = System.Diagnostics.Stopwatch.StartNew();
            loop.RunFor(1.0, ci);
            sw.Stop();
            Console.WriteLine($"{where,-26} 1.0 s of sim took {sw.Elapsed.TotalMilliseconds,7:F1} ms of CPU "
                         + $"(ground {ground:F0} m, {(sw.Elapsed.TotalMilliseconds > 1000 ? "SLOWER THAN REAL TIME" : $"{1000 / sw.Elapsed.TotalMilliseconds:F1}x real time")})");
        }
    }

    public void WhereDoesTheStepTimeActuallyGo()
    {
        WorldTerrain.Active ??= new WorldTerrain();
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));
        var tables = Aircraft.BuildAirfoilTables(cfg);
        var pts = AirframeContact.BuildPoints(cfg);
        double ground = WorldTerrain.GroundHeightAt(0, 0);
        var vb = new Vec3(40, 0, 2);
        var ctl = ControlDeflections.Neutral;
        const int n = 2000;

        // aero only
        AeroModel.Compute(cfg, tables, vb, Vec3.Zero, Vec3.Zero, 1.0, ctl);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++) AeroModel.Compute(cfg, tables, vb, Vec3.Zero, Vec3.Zero, 1.0, ctl);
        sw.Stop();
        double aeroUs = sw.Elapsed.TotalMilliseconds * 1000 / n;

        // contact only, at a fresh position each time so the terrain queries are real misses
        var cg = cfg.Mass.CgVec();
        AirframeContact.Compute(pts, cg, new RigidBodyState(new Vec3(0, 0, -(ground + 1524)), new Quat(0, 0, 0, 1), vb, Vec3.Zero), null);
        sw.Restart();
        for (int i = 0; i < n; i++)
        {
            var st = new RigidBodyState(new Vec3(i * 0.21, i * 0.17, -(ground + 1524)), new Quat(0, 0, 0, 1), vb, Vec3.Zero);
            AirframeContact.Compute(pts, cg, st, null);
        }
        sw.Stop();
        double contactUs = sw.Elapsed.TotalMilliseconds * 1000 / n;

        Console.WriteLine($"AeroModel.Compute      {aeroUs,8:F1} us per call");
        Console.WriteLine($"AirframeContact.Compute{contactUs,8:F1} us per call ({pts.Count} points, terrain queried per point)");
        // RK4 re-evaluates forces four times per step, and the default step is 200 Hz.
        double perSec = 4 * 200.0;
        Console.WriteLine($"at {perSec:F0} evaluations per second of flight: aero {aeroUs * perSec / 1000,7:F0} ms/s, contact {contactUs * perSec / 1000,7:F0} ms/s");
    }

}

static class QueryCost
{
    public static void Run()
    {
        Console.WriteLine(IslandTerrain.WaterIndexWorkBounds);
        Console.WriteLine(IslandTerrain.HeightIndexWorkBounds);
        var initialization=System.Diagnostics.Stopwatch.StartNew();
        IslandTerrain.GroundNormalAt(0,0); initialization.Stop();
        Console.WriteLine($"analytic index initialization: {initialization.Elapsed.TotalMilliseconds:F1} ms");
        var rng=new Random(771); const int count=100000;
        var ring=Enumerable.Range(0,count).Select(_=> { double r=20*Math.Sqrt(rng.NextDouble()),a=2*Math.PI*rng.NextDouble(); return new IslandPoint(r*Math.Cos(a),r*Math.Sin(a)); }).ToArray();
        var sets=new[]{("ring",ring),("sea",Moving(new IslandPoint(18,0))),("KSNA",Moving(new IslandPoint(.96,-8.06))),("Drake floor",Moving(IslandCavities.Drake["chamber_centre"].Point)),("Pirate floor",Moving(IslandCavities.Pirate["chamber_centre"].Point))};
        foreach(var (name,points) in sets)
        {
            int queryIndex=0;
            foreach(var (label,query) in new (string,Func<IslandPoint,double>)[]{
                ("water reference",p=>IslandTerrain.WaterAtReference(p.X,p.Y,0)??-1),
                ("water indexed",p=>IslandTerrain.WaterAt(p.X,p.Y,0)??-1),
                ("height indexed",p=>IslandTerrain.Evaluate(p)),
                ("analytic normal",p=>IslandTerrain.GroundNormalAt(p.X,p.Y).X),
                ("wall predicate",p=>IslandTerrain.IsWallAt(p.X,p.Y,1)?1:0)})
            {
                double sum=0; for(int i=0;i<10000;i++) sum+=query(points[i%points.Length]);
                var elapsed=new double[5]; long bytes=0;
                queryIndex++;
                for(int trial=0;trial<5;trial++)
                {
                    // Distinct exact coordinates for every trial/evaluator: never time memo hits.
                    double offset=(queryIndex*5+trial+1)*1e-8;
                    var fresh=points.Select(p=>p+new IslandPoint(offset,offset*.713)).ToArray();
                    var sw=System.Diagnostics.Stopwatch.StartNew(); long before=GC.GetAllocatedBytesForCurrentThread();
                    foreach(var p in fresh) sum+=query(p);
                    bytes=GC.GetAllocatedBytesForCurrentThread()-before;sw.Stop();elapsed[trial]=sw.Elapsed.TotalMilliseconds*1000/points.Length;
                }
                Array.Sort(elapsed);
                Console.WriteLine($"{name,-13} {label,-17}: median {elapsed[2]:F3} us/sample; {bytes/(double)points.Length:F1} B/sample; sum {sum:R}");
            }
        }
        Console.WriteLine("Cliff profile (north 3245 m; bitwise equality with the pre-index evaluator):");
        for(int east=870;east<=900;east++)
        {
            double h=IslandTerrain.HeightAndGradientAt(3245,east,out double dx,out double dy),reference=IslandTerrain.EvaluateReference(new IslandPoint(east/1000.0,3.245));
            Console.WriteLine($"E{east}: h={h:R} reference={reference:R} identical={BitConverter.DoubleToInt64Bits(h)==BitConverter.DoubleToInt64Bits(reference)} dh/dN={dx:F4} dh/dE={dy:F4}");
        }
    }
    private static IslandPoint[] Moving(IslandPoint c) => Enumerable.Range(0,20000).Select(i=>c+new IslandPoint(i*.0000001,i*.00000007)).ToArray();
}
