using FlyingGame.Core.MathTypes;
namespace FlyingGame.Core;

/// <summary>One north doorway at Drake and one leeward cave mouth. Height-aware contact never mistakes
/// upper terrain for the interior floor. No reverse runway final or second Drake portal exists.</summary>
public static class IslandCavities
{
    public static IslandValue Drake { get; } = IslandTerrain.Feature("F1")["shell_geometry"];
    private static readonly IslandValue PirateFeature = IslandTerrain.Feature("F2");
    public static IslandValue Pirate { get; } = PirateFeature["interior"];
    public static bool DrakeInside(IslandPoint p) { var d = p - Drake["chamber_centre"].Point; return Math.Abs(d.E) <= .210 && Math.Abs(d.N) <= .410; }
    public static bool PirateInside(IslandPoint p)
    {
        var f = PirateFeature; var ci = f["interior"]; var c = ci["chamber_centre"].Point;
        if ((p - c).Length <= .090) return true;
        var mouth = f["centre"].Point; var u = IslandPoint.Heading(f.N("mouth_true_deg") + 180); var d = p - mouth;
        return d.Dot(u) >= 0 && d.Dot(u) <= ci.N("throat_length_m") / 1000 && Math.Abs(d.E * u.N - d.N * u.E) <= ci.N("throat_width_m") / 2000;
    }
    public static bool Skylight(IslandPoint p) => (p - Pirate["skylight_centre"].Point).Length < .005;
    public static bool TryFloor(double x, double y, double up, out double floor)
    {
        var p = new IslandPoint(y / 1000, x / 1000); floor = 0;
        if (DrakeInside(p) && up < Drake.N("minimum_roof_msl_m") - 1) { floor = Drake.N("floor_msl_m"); return true; }
        if (PirateInside(p) && (up < Math.Max(IslandTerrain.HeightAt(x,y),
            (p-Pirate["chamber_centre"].Point).Length<=.090?Pirate.N("roof_top_msl_m"):Pirate.N("throat_roof_top_msl_m_min"))-1 || Skylight(p))) { floor = Pirate.N("floor_msl_m"); return true; }
        return false;
    }
    public static double Ceiling(IslandPoint p, bool drake)
    {
        if (drake) return Drake.N("ceiling_msl_m");
        return (p - Pirate["chamber_centre"].Point).Length <= .090 ? Pirate.N("ceiling_msl_m") : Pirate.N("throat_ceiling_msl_m");
    }
    public sealed class Roof : WorldSolids.IShape
    {
        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            var p = new IslandPoint(y / 1000, x / 1000); normalNed = Vec3.Zero; depth = 0;
            bool drake = DrakeInside(p), pirate = PirateInside(p); if (!drake && !pirate || pirate && Skylight(p)) return false;
            double ceiling = Ceiling(p, drake), outer = Math.Max(ceiling + (drake ? 60 : 6), IslandTerrain.HeightAt(x, y));
            if (up < ceiling || up > outer) return false;
            double down = up - ceiling, above = outer - up;
            depth = Math.Min(down, above); normalNed = new Vec3(0, 0, down <= above ? 1 : -1); return true;
        }
    }
}
