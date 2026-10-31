using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

public class IslandHeightIndexTests
{
    private readonly ITestOutputHelper output;
    public IslandHeightIndexTests(ITestOutputHelper output) { this.output=output; }

    [Fact]
    public void IndexedContinuousHeightsEqualTheFrozenEvaluatorExactly()
    {
        var random=new Random(20261031);
        var points=new List<IslandPoint>();
        // Uniform area sampling across the full 20 km ring; every coordinate is fresh.
        for(int i=0;i<20000;i++)
        {
            double r=20*Math.Sqrt(random.NextDouble()),a=random.NextDouble()*2*Math.PI;
            points.Add(new IslandPoint(r*Math.Cos(a),r*Math.Sin(a)));
        }
        // Both thresholds and centre of every authored runway/strip/lane/pad, plus all starts.
        foreach(var f in IslandTerrain.Fields)
        {
            points.Add(f.Centre); points.Add(f.Centre-f.U*(f.Length/2000)); points.Add(f.Centre+f.U*(f.Length/2000));
            // Shoulders and blend limits, both sides and ends (including overlapping pads).
            foreach(double sign in new[]{-1.0,1.0}) foreach(double epsilon in new[]{-1e-10,0,1e-10})
            {
                points.Add(f.Centre+f.U*(sign*(f.Length/2000+f.EndShoulder/1000+f.Blend/1000+epsilon)));
                points.Add(f.Centre+f.V*(sign*(f.Width/2000+f.Shoulder/1000+f.Blend/1000+epsilon)));
            }
        }
        foreach(var q in IslandTerrain.World["mesa_pads"].Items.Concat(IslandTerrain.World["starts"].Items)) points.Add(q["centre"].Point);
        // Grid seams, shore boundaries, feature centres, and authored polyline vertices.
        for(double e=-20;e<=20;e+=.5) for(double n=-20;n<=20;n+=.5)
            foreach(double epsilon in new[]{-1e-10,0,1e-10}) points.Add(new IslandPoint(e+epsilon,n-epsilon));
        foreach(string key in new[]{"land_polygons","glaciers","canyons","lesson_terrain","water","outer_islands","islets","clearance_budgets"})
            foreach(var q in IslandTerrain.World[key].Items)
            {
                var vertices=key=="land_polygons"?q.Points:q["path"].Points;
                foreach(var p in vertices.Concat(new[]{q["centre"].Point,q["a"].Point,q["b"].Point}))
                    foreach(double epsilon in new[]{-1e-9,0,1e-9}) points.Add(new IslandPoint(p.E+epsilon,p.N-epsilon));
            }
        foreach(var p in points)
        {
            double expected=IslandTerrain.EvaluateReference(p),actual=IslandTerrain.Evaluate(p);
            Assert.Equal(BitConverter.DoubleToInt64Bits(actual),BitConverter.DoubleToInt64Bits(IslandTerrain.HeightKm(p)));
            Assert.True(BitConverter.DoubleToInt64Bits(expected)==BitConverter.DoubleToInt64Bits(actual),
                $"E={p.E:R}, N={p.N:R}: reference={expected:R}, indexed={actual:R}");
        }
        output.WriteLine($"{points.Count} exact bitwise comparisons passed (20,000 random ring points, all thresholds, pad centres, starts, seams and authored vertices).");
    }

    [Fact]
    public void FreshIndexedSamplesAllocateNothingAfterInitialization()
    {
        for(int i=0;i<1000;i++) IslandTerrain.Evaluate(new IslandPoint(i*.003,i*.007));
        long before=GC.GetAllocatedBytesForCurrentThread(); double sum=0;
        for(int i=0;i<20000;i++) sum+=IslandTerrain.Evaluate(new IslandPoint(i*.001-10,i*.00073-8));
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.True(double.IsFinite(sum)); Assert.Equal(0,allocated);
    }

    [Fact]
    public void FreshSampleCostIsReportedWithoutMemoHits()
    {
        output.WriteLine(IslandTerrain.HeightIndexWorkBounds);
        var rng=new Random(7);
        var points=Enumerable.Range(0,20000).Select(_=>new IslandPoint(rng.NextDouble()*20-10,rng.NextDouble()*20-10)).ToArray();
        foreach(var evaluator in new Func<IslandPoint,double>[] {IslandTerrain.EvaluateReference,IslandTerrain.EvaluateCompiled,IslandTerrain.Evaluate})
        {
            double sum=0; for(int i=0;i<1000;i++) sum+=evaluator(points[i]);
            var sw=System.Diagnostics.Stopwatch.StartNew();
            foreach(var p in points) sum+=evaluator(p);
            sw.Stop(); output.WriteLine($"{evaluator.Method.Name}: {sw.Elapsed.TotalMilliseconds*1000/points.Length:F3} us per fresh sample; sum {sum:R}");
        }
    }
}
