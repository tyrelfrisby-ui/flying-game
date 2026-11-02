using FlyingGame.Core.MathTypes;
namespace FlyingGame.Core;

/// <summary>One north doorway at Drake and one leeward cave mouth. Height-aware contact never mistakes
/// upper terrain for the interior floor. No reverse runway final or second Drake portal exists.</summary>
public static class IslandCavities
{
    public static IslandValue Drake { get; } = IslandTerrain.Feature("F1")["shell_geometry"];
    private static readonly IslandValue PirateFeature = IslandTerrain.Feature("F2");
    public static IslandValue Pirate { get; } = PirateFeature["interior"];
    // Every number the per-point queries below need, read out of the world description once. These run for every
    // contact point and wheel at every RK4 stage near the ground, and each IslandValue["key"] is a string-keyed
    // dictionary lookup: on the Quest's IL2CPP that was a measurable part of the frame (docs/quest-performance.md).
    private static readonly IslandPoint DrakeCentre = Drake["chamber_centre"].Point;
    private static readonly double DrakeMinimumRoof = Drake.N("minimum_roof_msl_m"), DrakeFloor = Drake.N("floor_msl_m"), DrakeCeiling = Drake.N("ceiling_msl_m");
    private static readonly IslandPoint PirateCentre = Pirate["chamber_centre"].Point, PirateMouth = PirateFeature["centre"].Point,
        PirateSkylight = Pirate["skylight_centre"].Point;
    private static readonly IslandPoint PirateInward = IslandPoint.Heading(PirateFeature.N("mouth_true_deg") + 180);
    private static readonly double PirateThroatLength = Pirate.N("throat_length_m") / 1000, PirateThroatHalfWidth = Pirate.N("throat_width_m") / 2000,
        PirateRoofTop = Pirate.N("roof_top_msl_m"), PirateThroatRoofTop = Pirate.N("throat_roof_top_msl_m_min"), PirateFloor = Pirate.N("floor_msl_m"),
        PirateCeiling = Pirate.N("ceiling_msl_m"), PirateThroatCeiling = Pirate.N("throat_ceiling_msl_m");
    public static bool DrakeInside(IslandPoint p) { var d = p - DrakeCentre; return Math.Abs(d.E) <= .210 && Math.Abs(d.N) <= .410; }
    public static bool PirateInside(IslandPoint p)
    {
        if ((p - PirateCentre).Length <= .090) return true;
        var u = PirateInward; var d = p - PirateMouth;
        return d.Dot(u) >= 0 && d.Dot(u) <= PirateThroatLength && Math.Abs(d.E * u.N - d.N * u.E) <= PirateThroatHalfWidth;
    }
    public static bool Skylight(IslandPoint p) => (p - PirateSkylight).Length < .005;
    public static bool TryFloor(double x, double y, double up, out double floor)
    {
        var p = new IslandPoint(y / 1000, x / 1000); floor = 0;
        if (DrakeInside(p) && up < DrakeMinimumRoof - 1) { floor = DrakeFloor; return true; }
        if (PirateInside(p) && (up < Math.Max(IslandTerrain.HeightAt(x,y),
            (p-PirateCentre).Length<=.090?PirateRoofTop:PirateThroatRoofTop)-1 || Skylight(p))) { floor = PirateFloor; return true; }
        return false;
    }
    public static double Ceiling(IslandPoint p, bool drake)
    {
        if (drake) return DrakeCeiling;
        return (p - PirateCentre).Length <= .090 ? PirateCeiling : PirateThroatCeiling;
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
