namespace FlyingGame.Core;

/// <summary>Port of the approved continuous authoring evaluator. All distances inside are east/north km;
/// public terrain calls use sim north/east metres. The 100 m review bake is evidence, never a second ground.</summary>
public static partial class IslandTerrain
{
    public const double Feet = 0.3048;
    public static IslandValue World => IslandData.World;
    private static readonly IslandValue H = World["height_field"];
    public static readonly IslandField[] Fields = World["fields"].Items.Select(v => new IslandField(v)).ToArray();
    private static readonly IslandPoint[][] Land = World["land_polygons"].Items.Select(v => v.Points).ToArray();
    private static readonly IslandValue[] Outer = World["outer_islands"].Items.Concat(World["islets"].Items).ToArray();
    private static readonly (double minE, double maxE, double minN, double maxN)[] Bounds = Land.Select(poly =>
        (poly.Min(p => p.E) - .35, poly.Max(p => p.E) + .35, poly.Min(p => p.N) - .35, poly.Max(p => p.N) + .35)).ToArray();
    private static readonly IslandValue[] Peaks = H["peaks"].Items, Budgets = World["clearance_budgets"].Items;
    private static bool InBounds(IslandPoint p, int i) => p.E >= Bounds[i].minE && p.E <= Bounds[i].maxE && p.N >= Bounds[i].minN && p.N <= Bounds[i].maxN;
    public static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    public static double Mix(double h, double z, double a) => h + (z - h) * a;
    public static bool Inside(IslandPoint p, IslandPoint[] poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var a = poly[j]; var b = poly[i];
            if ((a.N > p.N) != (b.N > p.N) && p.E < (b.E - a.E) * (p.N - a.N) / (b.N - a.N) + a.E) c = !c;
        }
        return c;
    }
    public static (double d, double t) Segment(IslandPoint p, IslandPoint a, IslandPoint b)
    {
        var v = b - a; double t = v.Dot(v) > 0 ? Math.Clamp((p - a).Dot(v) / v.Dot(v), 0, 1) : 0;
        return ((p - (a + v * t)).Length, t);
    }
    public static double Shore(IslandPoint p, IslandPoint[] poly)
    {
        double d = double.PositiveInfinity;
        for (int i = 0; i < poly.Length; i++) d = Math.Min(d, Segment(p, poly[i], poly[(i + 1) % poly.Length]).d);
        return Inside(p, poly) ? d : -d;
    }
    public static (double d, double t) Path(IslandPoint p, IslandValue path)
    {
        var a = path.Items; double total = 0, run = 0, best = double.PositiveInfinity, bt = 0;
        for (int i = 1; i < a.Length; i++) total += (a[i].Point - a[i - 1].Point).Length;
        for (int i = 1; i < a.Length; i++)
        {
            double l = (a[i].Point - a[i - 1].Point).Length; var (d, t) = Segment(p, a[i - 1].Point, a[i].Point);
            if (d < best) { best = d; bt = (run + t * l) / Math.Max(.00001, total); } run += l;
        }
        return (best, bt);
    }
    public static double EllipseRadius(IslandPoint p, IslandValue q)
    {
        var u = IslandPoint.Heading(q.N("long_axis_true_deg", 90)); var d = p - q["centre"].Point;
        double a = d.Dot(u) / q["radii_km"][0].Number(), b = (d.E * u.N - d.N * u.E) / q["radii_km"][1].Number();
        return Math.Sqrt(a * a + b * b);
    }
    public static IslandValue Feature(string id) => World["features"].Items.First(f => f.S("id") == id);
    public static double HeightAt(double x, double y) => HeightKm(new IslandPoint(y / 1000, x / 1000));
    // Exact-coordinate, per-thread memoization: meshes/forest/contact repeat the same queries.
    // No interpolation or quantization, and no shared mutable cache between physics/render workers.
    private struct HeightCacheEntry { public long E,N; public double Height; public bool Used; }
    [ThreadStatic] private static HeightCacheEntry[]? heightCache;
    public static double HeightKm(IslandPoint p)
    {
        var cache=heightCache??=new HeightCacheEntry[16384];long e=BitConverter.DoubleToInt64Bits(p.E),n=BitConverter.DoubleToInt64Bits(p.N);
        ulong hash=unchecked((ulong)e*11400714819323198485UL^(ulong)n*14029467366897019727UL);hash^=hash>>33;hash=unchecked(hash*0xff51afd7ed558ccdUL);hash^=hash>>33;
        int index=(int)(hash&16383);var found=cache[index];if(found.Used&&found.E==e&&found.N==n)return found.Height;
        double h=Evaluate(p);cache[index]=new HeightCacheEntry{E=e,N=n,Height=h,Used=true};return h;
    }
    /// <summary>Height and analytic branch derivatives (metres per sim metre), evaluated together.
    /// Generated from IslandHeightIndex; no displaced samples. At creases/ties the evaluator's
    /// first selected branch wins. At a true jump only that surface's derivative is defined.</summary>
    public static double HeightAndGradientAt(double x,double y,out double dhdx,out double dhdy)
    {
        var h=IslandGradientIndex.Evaluate(TerrainJetPoint.Variable(new IslandPoint(y/1000,x/1000)));
        dhdx=h.N/1000; dhdy=h.E/1000; return h.Value;
    }
    /// <summary>Outward unit normal in NED for HeightAt's surface, including the roof above cavities.</summary>
    public static MathTypes.Vec3 GroundNormalAt(double x,double y)
    {
        HeightAndGradientAt(x,y,out double dx,out double dy);
        if(dx==0&&dy==0) return new MathTypes.Vec3(0,0,-1);
        return new MathTypes.Vec3(-dx,-dy,-1).Normalized();
    }
    /// <summary>Whether the local slope magnitude exceeds the supplied nonnegative tangent.</summary>
    public static bool IsWallAt(double x,double y,double slopeTangent)
    {
        HeightAndGradientAt(x,y,out double dx,out double dy);
        return dx*dx+dy*dy>slopeTangent*slopeTangent;
    }
    public static string HeightIndexWorkBounds => IslandHeightIndex.WorkBounds;
    /// <summary>Uncached continuous query; useful for fresh-sample performance and parity checks.</summary>
    public static double Evaluate(IslandPoint p) => IslandHeightIndex.Evaluate(p);
    /// <summary>Same typed arithmetic without spatial rejection, for optimization decomposition.</summary>
    public static double EvaluateCompiled(IslandPoint p) => IslandHeightIndex.Evaluate(p, false);
    public static double? WaterAt(double x, double y, double up = double.NaN)
    {
        if (!double.IsNaN(up) && IslandCavities.TryFloor(x, y, up, out _)) return null;
        var p = new IslandPoint(y / 1000, x / 1000);
        return IslandWater.WaterAt(p);
    }
    public static string WaterIndexWorkBounds => IslandWater.WorkBounds;
    public static WorldTerrain.Surface SurfaceAt(double x, double y)
    {
        var p = new IslandPoint(y / 1000, x / 1000);
        foreach (var f in Fields) if (f.Contains(p)) return f.Surface == "asphalt" || f.Surface == "paved" ? WorldTerrain.Surface.Paved : f.Surface.Contains("grass") || f.Surface == "turf" ? WorldTerrain.Surface.Grass : WorldTerrain.Surface.Gravel;
        // Glacier ice is where the relief generator's mass balance put it (IslandRelief ice channel); the authored
        // ski landing zones stay snow whatever the ice does.
        foreach (var q in World["glaciers"].Items) if ((p - q["ski_zone"]["centre"].Point).Length < .15) return WorldTerrain.Surface.Snow;
        if (IslandRelief.Channels(p).ice > .5f) return WorldTerrain.Surface.Snow;
        foreach(var q in World["mesa_pads"].Items)
        {
            var d=p-q["centre"].Point;var u=IslandPoint.Heading(q.N("heading_true_deg"));
            if(Math.Abs(d.Dot(u))<=q.N("length_m")/2000&&Math.Abs(d.E*u.N-d.N*u.E)<=q.N("width_m")/2000)return WorldTerrain.Surface.Gravel;
        }
        if (IslandWater.GravelBar(p)) return WorldTerrain.Surface.Gravel;
        return WorldTerrain.Surface.Rough;
    }
}

public sealed class IslandField
{
    public readonly IslandValue Definition;
    public readonly string Id, Field, Surface, Numbers;
    public readonly IslandPoint Centre, U, V;
    public readonly double Heading, Length, Width, Elevation, Slope, Shoulder, EndShoulder, Blend;
    public IslandField(IslandValue d)
    {
        Definition = d; Id = d.S("key"); Field = d.S("field"); Centre = d["centre"].Point; Heading = d.N("heading_true_deg"); U = IslandPoint.Heading(Heading); V = new(U.N, -U.E);
        Surface = d.S("surface"); Numbers = d.S("numbers_magnetic", d.S("id")); Length = d.N("length_m", d.N("length_ft") * IslandTerrain.Feet); Width = d.N("width_m", d.N("width_ft") * IslandTerrain.Feet);
        Elevation = d.N("elevation_ft") * IslandTerrain.Feet; Slope = d.N("slope_percent"); Shoulder = d["pad"].N("shoulder_m", 60); EndShoulder = d["pad"].N("end_shoulder_m", 60); Blend = d["pad"].N("blend_m", 250);
    }
    public bool Contains(IslandPoint p, double margin = 0) { var d = p - Centre; return Math.Abs(d.Dot(U)) <= Length / 2000 + margin / 1000 && Math.Abs(d.Dot(V)) <= Width / 2000 + margin / 1000; }
    public double Plane(IslandPoint p) => Elevation + Math.Clamp((p - Centre).Dot(U) * 1000, -Length / 2, Length / 2) * Slope / 100;
}
