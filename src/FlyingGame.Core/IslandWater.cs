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
        .Select(c=>(new Rect(c.E-.37,c.E+.37,c.N-.15,c.N+.15),c)).ToList());
    internal static string WorkBounds => $"0.5 km water cells: lakes {Lakes.MaxCandidates}, water lanes {Lanes.MaxCandidates}, streams {Streams.MaxCandidates}, outwash {Outwash.MaxCandidates}, dry pads {DryPads.MaxCandidates}; stream paths use bounded 0.125 km nearest-segment cells";

    internal static double? WaterAt(IslandPoint p)
    {
        foreach(var q in Lakes.At(p)) if(q.Contains(p)) return q.Elevation;
        foreach(var f in Lanes.At(p)) if(f.Contains(p)) return f.Elevation;
        double ground=IslandTerrain.HeightKm(p);
        if(ground<0) return 0;
        return StreamAt(p)?ground+.15:(double?)null;
    }
    /// <summary>
    /// The eight braids of a gravel-bar outwash (F13 Knik, F14 Middle Fork, F15 Talkeetna), across-offset (km) of
    /// braid <paramref name="i"/> at along-offset <paramref name="e"/> (km, east of the feature centre). They used to
    /// be eight ruled lines 25 m apart, all starting and stopping on the same two edges: from the air, the stripes of
    /// a marked runway (Ty, 2026-11-03, beside the Knik glacier zone). Now each braid wanders on its own (two sines,
    /// its own phase) and starts and ends at its own place. The amplitude is bounded so a braid never reaches the
    /// bar's clean landing lane (|N| &lt; 30 m, plus a braid's 4 m half-width): 3.5 m for the two braids that flank it,
    /// 12 m and 18 m for the outer ones (which may cross and rejoin, as braids do), never past the bar's ±110 m.
    /// Wavelengths ~370 m and ~150 m. Braids 3 and 4 lie inside the lane and never run.
    /// </summary>
    public static double BraidOffsetKm(int i, double e)
    {
        double amp = i == 2 || i == 5 ? .0035 : i == 1 || i == 6 ? .012 : .018;
        return (i - 3.5) * .025 + amp * (.6 * Math.Sin(e * 17 + 1.9 * i) + .4 * Math.Sin(e * 41 + 4.1 * i));
    }
    /// <summary>Where braid <paramref name="i"/> runs, along the bar (km from the centre): each its own ends.</summary>
    public static (double lo, double hi) BraidSpanKm(int i)
        => (-.35 + .08 * Frac(i * .618 + .13), .35 - .08 * Frac(i * .382 + .71));
    private static double Frac(double v) => v - Math.Floor(v);
    /// <summary>Is a point d (km from the bar's centre) in one of its braids.</summary>
    public static bool InBraid(IslandPoint d)
    {
        if (Math.Abs(d.E) > .35 || Math.Abs(d.N) > .11 || Math.Abs(d.N) < .030) return false;
        for (int i = 0; i < 8; i++)
        {
            var (lo, hi) = BraidSpanKm(i);
            if (d.E >= lo && d.E <= hi && Math.Abs(d.N - BraidOffsetKm(i, d.E)) < .004) return true;
        }
        return false;
    }

    public static bool StreamAt(IslandPoint p)
    {
        bool wet=false;
        foreach(var c in Streams.At(p)) if(c.Path.At(p).d<c.Width) { wet=true; break; }
        if(!wet) foreach(var c in Outwash.At(p))
        {
            if(InBraid(p-c)) { wet=true; break; }
        }
        if(!wet) return false;
        foreach(var f in DryPads.At(p))
        {
            var d=p-f.Centre;
            if(Math.Abs(d.Dot(f.U))<f.Length/2000+f.EndShoulder/1000 && Math.Abs(d.Dot(f.V))<f.Width/2000+f.Shoulder/1000) return false;
        }
        return !IslandCavities.DrakeInside(p)&&!IslandCavities.PirateInside(p);
    }
    /// <summary>
    /// The open gravel of a river bar. Not a 700 x 240 m rectangle any more — its straight tree lines were half of what
    /// made Knik's bar read as a runway beside the glacier (Ty, 2026-11-03) — but a lens (superellipse, exponent 4,
    /// 370 x 125 m half-axes) whose half-width wanders ±13 % along the bar. The 520 x 52 m clean landing lane is always
    /// inside it (at |E| &lt;= 260 m the half-width is never under 97 m); braid ends that run past it end in the trees.
    /// </summary>
    public static bool GravelBar(IslandPoint p)
    {
        foreach(var c in Outwash.At(p))
        {
            var d=p-c; double e=d.E/.37; if(Math.Abs(e)>=1) continue;
            double half=.125*Math.Pow(1-e*e*e*e,.25)*(1+.08*Math.Sin(d.E*31+1.3)+.05*Math.Sin(d.E*73+2.1));
            if(Math.Abs(d.N)<=half) return true;
        }
        return false;
    }
}
