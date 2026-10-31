using static FlyingGame.Core.IslandHeightIndex;
namespace FlyingGame.Core;

/// <summary>Ordered local candidates for fixed water planes and shallow streams. Index cells store
/// feature references, never sampled water/ground. No query scans or allocates world geometry.</summary>
public static class IslandWater
{
    private sealed class Lake
    {
        public readonly IslandPoint Centre, U;
        public readonly double Rx, Ry, Elevation;
        public Lake(IslandValue q)
        {
            Centre=q["centre"].Point; U=IslandPoint.Heading(q.N("long_axis_true_deg",90));
            Rx=q["radii_km"][0].Number(); Ry=q["radii_km"][1].Number(); Elevation=q.N("elevation_ft")*IslandTerrain.Feet;
        }
        public bool Contains(IslandPoint p)
        {
            var d=p-Centre; double a=d.Dot(U)/Rx,b=(d.E*U.N-d.N*U.E)/Ry;
            return Math.Sqrt(a*a+b*b)<=1;
        }
    }
    private sealed class Stream
    {
        public readonly Line Path;
        public readonly double Width;
        public Stream(IslandValue q) { Path=new Line(q["path"]); Width=q.S("id")=="C3"?.018:.010; }
    }
    private static readonly Grid<Lake> Lakes = new(IslandTerrain.World["water"].Items.Select(q=>new Lake(q))
        .Select(q=>(Rect.Around(q.Centre,Math.Max(q.Rx,q.Ry)),q)).ToList());
    private static Rect FieldBounds(IslandField f,bool shoulder)
    {
        double a=f.Length/2000+(shoulder?f.EndShoulder/1000:0),b=f.Width/2000+(shoulder?f.Shoulder/1000:0);
        double e=Math.Abs(f.U.E)*a+Math.Abs(f.V.E)*b,n=Math.Abs(f.U.N)*a+Math.Abs(f.V.N)*b;
        return new Rect(f.Centre.E-e,f.Centre.E+e,f.Centre.N-n,f.Centre.N+n);
    }
    private static readonly Grid<IslandField> Lanes = new(IslandTerrain.Fields.Where(f=>f.Surface=="water").Select(f=>(FieldBounds(f,false),f)).ToList());
    private static readonly Grid<IslandField> DryPads = new(IslandTerrain.Fields.Select(f=>(FieldBounds(f,true),f)).ToList());
    private static readonly Grid<Stream> Streams = new(IslandTerrain.World["canyons"].Items.Select(q=>new Stream(q)).Select(q=>(q.Path.Bounds(q.Width),q)).ToList());
    private static readonly Grid<IslandPoint> Outwash = new(new[]{"F13","F14","F15"}.Select(id=>IslandTerrain.Feature(id)["centre"].Point)
        .Select(c=>(new Rect(c.E-.35,c.E+.35,c.N-.12,c.N+.12),c)).ToList());
    internal static string WorkBounds => $"0.5 km water cells: lakes {Lakes.MaxCandidates}, water lanes {Lanes.MaxCandidates}, streams {Streams.MaxCandidates}, outwash {Outwash.MaxCandidates}, dry pads {DryPads.MaxCandidates}; stream paths use bounded 0.125 km nearest-segment cells";

    internal static double? WaterAt(IslandPoint p)
    {
        foreach(var q in Lakes.At(p)) if(q.Contains(p)) return q.Elevation;
        foreach(var f in Lanes.At(p)) if(f.Contains(p)) return f.Elevation;
        double ground=IslandTerrain.HeightKm(p);
        if(ground<0) return 0;
        return StreamAt(p)?ground+.15:(double?)null;
    }
    public static bool StreamAt(IslandPoint p)
    {
        bool wet=false;
        foreach(var c in Streams.At(p)) if(c.Path.At(p).d<c.Width) { wet=true; break; }
        if(!wet) foreach(var c in Outwash.At(p))
        {
            var d=p-c;
            if(Math.Abs(d.E)>.35||Math.Abs(d.N)>.11||Math.Abs(d.N)<.030) continue;
            for(int i=0;i<8;i++) if(Math.Abs(d.N-((i-3.5)*.025+.004*Math.Sin(d.E*27+i)))<.004) { wet=true; break; }
        }
        if(!wet) return false;
        foreach(var f in DryPads.At(p))
        {
            var d=p-f.Centre;
            if(Math.Abs(d.Dot(f.U))<f.Length/2000+f.EndShoulder/1000 && Math.Abs(d.Dot(f.V))<f.Width/2000+f.Shoulder/1000) return false;
        }
        return !IslandCavities.DrakeInside(p)&&!IslandCavities.PirateInside(p);
    }
    public static bool GravelBar(IslandPoint p)
    {
        foreach(var c in Outwash.At(p))
        {
            var d=p-c;
            if(Math.Abs(d.E)<=.35&&Math.Abs(d.N)<=.12) return true;
        }
        return false;
    }
}
