using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

[Collection("WorldTerrainActive")]
public class IslandWaterAndNormalTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method does
    /// not run when the test throws, so a failing test left its world installed for the whole rest of the run.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper output;
    public IslandWaterAndNormalTests(ITestOutputHelper output) { this.output=output; }
    private static IEnumerable<IslandPoint> Points()
    {
        var rng=new Random(20261031);
        for(int i=0;i<100000;i++)
        {
            double r=20*Math.Sqrt(rng.NextDouble()),a=2*Math.PI*rng.NextDouble();
            yield return new IslandPoint(r*Math.Cos(a),r*Math.Sin(a));
        }
        foreach(var q in IslandTerrain.World["water"].Items)
        {
            var c=q["centre"].Point; yield return c;
            var u=IslandPoint.Heading(q.N("long_axis_true_deg",90)); var v=new IslandPoint(u.N,-u.E);
            for(int angle=0;angle<360;angle++) foreach(double epsilon in new[]{-1e-10,0,1e-10})
            {
                double a=angle*Math.PI/180;
                yield return c+u*(q["radii_km"][0].Number()*(1+epsilon)*Math.Cos(a))+v*(q["radii_km"][1].Number()*(1+epsilon)*Math.Sin(a));
            }
        }
        foreach(var f in IslandTerrain.Fields)
        {
            yield return f.Centre;
            for(int i=-10;i<=10;i++) foreach(double sign in new[]{-1.0,1.0}) foreach(double epsilon in new[]{-1e-10,0,1e-10})
            {
                yield return f.Centre+f.U*(f.Length/2000*i/10)+f.V*(sign*(f.Width/2000+epsilon));
                yield return f.Centre+f.V*(f.Width/2000*i/10)+f.U*(sign*(f.Length/2000+epsilon));
                yield return f.Centre+f.U*((f.Length/2000+f.EndShoulder/1000)*i/10)+f.V*(sign*(f.Width/2000+f.Shoulder/1000+epsilon));
            }
        }
        foreach(var c in IslandTerrain.World["canyons"].Items)
        {
            var path=c["path"].Points; double width=c.S("id")=="C3"?.018:.010;
            for(int j=1;j<path.Length;j++)
            {
                var v=path[j]-path[j-1]; var side=new IslandPoint(v.N,-v.E)*(1/v.Length);
                for(int i=0;i<=100;i++) foreach(double radius in new[]{0,width-1e-10,width,width+1e-10}) foreach(int sign in new[]{-1,1})
                    yield return path[j-1]+v*(i/100.0)+side*(radius*sign);
            }
        }
        foreach(string id in new[]{"F13","F14","F15"})
        {
            var c=IslandTerrain.Feature(id)["centre"].Point;
            for(int e=-35;e<=35;e++) for(int n=-12;n<=12;n++) yield return c+new IslandPoint(e*.01,n*.01);
        }
        for(double e=-20;e<=20;e+=.5) for(double n=-20;n<=20;n+=.5)
            foreach(double epsilon in new[]{-1e-10,0,1e-10}) yield return new IslandPoint(e+epsilon,n-epsilon);
        foreach(double y in Enumerable.Range(870,40).Select(i=>(double)i)) yield return new IslandPoint(y/1000,3.245);
        foreach(var p in new[]{new IslandPoint(1e100,0),new IslandPoint(0,-1e100),new IslandPoint(18,0)}) yield return p;
    }
    [Fact]
    public void WaterIsBitwiseIdenticalAcrossRingRimsStreamsPadsAndCavityFloors()
    {
        int count=0,sea=0,dry=0,wet=0,floors=0;
        void Compare(IslandPoint p,double up=double.NaN)
        {
            double? a=IslandTerrain.WaterAtReference(p.X,p.Y,up),b=IslandTerrain.WaterAt(p.X,p.Y,up);
            Assert.True(a.HasValue==b.HasValue&&(!a.HasValue||BitConverter.DoubleToInt64Bits(a.Value)==BitConverter.DoubleToInt64Bits(b!.Value)),$"Water E{p.E:R} N{p.N:R} up{up}: {a} != {b}");
            count++; if(a==null) dry++; else if(a==0) sea++; else wet++;
        }
        foreach(var p in Points()) { Compare(p); Compare(p,2000); }
        foreach(var cavity in new[]{IslandCavities.Drake,IslandCavities.Pirate})
        {
            var c=cavity["chamber_centre"].Point;
            for(int e=-210;e<=210;e+=10) for(int n=-410;n<=410;n+=10)
            {
                var p=c+new IslandPoint(e/1000.0,n/1000.0); double up=cavity.N("floor_msl_m")+1;
                if(IslandCavities.TryFloor(p.X,p.Y,up,out _)) floors++;
                Compare(p,up);
            }
        }
        Assert.True(sea>0&&dry>0&&wet>0&&floors>0);
        output.WriteLine($"{count} bitwise water comparisons; sea {sea}, dry {dry}, wet {wet}, cavity floors {floors}");
    }
    [Fact]
    public void AnalyticGradientEvaluatesTheExactHeightAndMatchesSmoothSurfaceDerivatives()
    {
        int count=0,slopes=0,steep=0;
        foreach(var p in Points())
        {
            double h=IslandTerrain.HeightAndGradientAt(p.X,p.Y,out double dx,out double dy);
            Assert.True(BitConverter.DoubleToInt64Bits(h)==BitConverter.DoubleToInt64Bits(IslandTerrain.HeightAt(p.X,p.Y)),$"Gradient value E{p.E:R} N{p.N:R}: {h:R}");
            Assert.True(double.IsFinite(dx)&&double.IsFinite(dy),$"Derivative E{p.E} N{p.N}: {dx}, {dy}");
            count++; if(Math.Abs(dx)+Math.Abs(dy)>10) steep++;
            if(count%11!=0||Math.Abs(p.E)>25||Math.Abs(p.N)>25) continue;
            // Test oracle only: two scales reject kinks/jumps before checking a smooth branch.
            double Derivative(bool north,double step) => (IslandTerrain.HeightAt(p.X+(north?step:0),p.Y+(north?0:step))-IslandTerrain.HeightAt(p.X-(north?step:0),p.Y-(north?0:step)))/(2*step);
            foreach(bool north in new[]{true,false})
            {
                double step=.0001;
                double plus=(IslandTerrain.HeightAt(p.X+(north?step:0),p.Y+(north?0:step))-h)/step;
                double minus=(h-IslandTerrain.HeightAt(p.X-(north?step:0),p.Y-(north?0:step)))/step;
                if(Math.Abs(plus-minus)>1e-4*(1+Math.Abs(plus)+Math.Abs(minus))) continue;
                double a=Derivative(north,.0001),b=Derivative(north,.00005),analytic=north?dx:dy;
                if(Math.Abs(a-b)>1e-5*(1+Math.Abs(a))) continue;
                Assert.True(Math.Abs(analytic-b)<2e-4*(1+Math.Abs(b)),$"Derivative E{p.E:R} N{p.N:R} north={north}: analytic {analytic:R}, numeric {a:R}, {b:R}");
                slopes++;
            }
        }
        Assert.True(steep>0&&slopes>10000);
        output.WriteLine($"{count} bitwise gradient-height comparisons; {slopes} smooth derivatives; {steep} steep branch samples");
    }
    [Fact]
    public void PublicNormalsHaveCorrectFrameAndSlopePredicate()
    {
        var previous=WorldTerrain.Active;
        try
        {
            WorldTerrain.Active=null;
            Assert.Equal(new Vec3(0,0,-1),WorldTerrain.GroundNormalAt(3245,882));
            Assert.False(WorldTerrain.IsWallAt(3245,882,1));
            WorldTerrain.Active=new WorldTerrain();
            foreach(var p in Points().Where((_,i)=>i%97==0))
            {
                var n=WorldTerrain.GroundNormalAt(p.X,p.Y);
                IslandTerrain.HeightAndGradientAt(p.X,p.Y,out double dx,out double dy);
                Assert.InRange(Math.Abs(n.Length-1),0,1e-12); Assert.True(n.Z<0);
                Assert.InRange(Math.Abs(n.X/n.Z-dx),0,1e-10); Assert.InRange(Math.Abs(n.Y/n.Z-dy),0,1e-10);
                Assert.Equal(dx*dx+dy*dy>1,WorldTerrain.IsWallAt(p.X,p.Y,1));
            }
            Assert.Equal(new Vec3(0,0,-1),WorldTerrain.GroundNormalAt(0,18000));
            var strip=IslandTerrain.Fields.Single(f=>f.Field=="3U2");
            var stripNormal=WorldTerrain.GroundNormalAt(strip.Centre.X,strip.Centre.Y);
            Assert.InRange(Math.Abs(stripNormal.X/stripNormal.Z-strip.Slope/100*strip.U.N),0,1e-12);
            Assert.InRange(Math.Abs(stripNormal.Y/stripNormal.Z-strip.Slope/100*strip.U.E),0,1e-12);
            Assert.True(WorldTerrain.IsWallAt(3245,882,30));
            for(int east=870;east<=900;east++)
            {
                double reference=IslandTerrain.EvaluateReference(new IslandPoint(east/1000.0,3.245));
                Assert.Equal(BitConverter.DoubleToInt64Bits(reference),BitConverter.DoubleToInt64Bits(WorldTerrain.GroundHeightAt(3245,east)));
            }
        }
        finally { WorldTerrain.Active=previous; }
    }
    [Fact]
    public void WaterAndGradientAllocateZeroBytesAfterInitialization()
    {
        for(int i=0;i<1000;i++) { IslandTerrain.WaterAt(i*3,i*7,2000); IslandTerrain.GroundNormalAt(i*3,i*7); }
        var drake=IslandCavities.Drake["chamber_centre"].Point; var pirate=IslandCavities.Pirate["chamber_centre"].Point;
        IslandTerrain.WaterAt(drake.X,drake.Y,0); IslandTerrain.WaterAt(pirate.X,pirate.Y,0);
        long before=GC.GetAllocatedBytesForCurrentThread(); double sum=0;
        for(int i=0;i<20000;i++)
        {
            sum+=IslandTerrain.WaterAt(i*.73-10000,i*.57-8000,2000)??0;
            sum+=IslandTerrain.GroundNormalAt(i*.73-10000,i*.57-8000).Z;
            sum+=IslandTerrain.IsWallAt(i*.73-10000,i*.57-8000,1)?1:0;
            sum+=IslandTerrain.WaterAt(drake.X+i*.000001,drake.Y,0)??0;
            sum+=IslandTerrain.WaterAt(pirate.X+i*.000001,pirate.Y,0)??0;
        }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.True(double.IsFinite(sum)); Assert.Equal(0,bytes);
    }
    [Fact]
    public void GeneratedGradientSourceTracksTheScalarExpressions()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"src/FlyingGame.Core/IslandHeightIndex.cs"))) directory=directory.Parent;
        Assert.NotNull(directory);
        var path=Path.Combine(directory!.FullName,"src/FlyingGame.Core");
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(path,"IslandHeightIndex.cs")))).ToLowerInvariant();
        Assert.Contains("Source SHA256: "+hash,File.ReadAllText(Path.Combine(path,"IslandGradientIndex.Generated.cs")));
    }
}
