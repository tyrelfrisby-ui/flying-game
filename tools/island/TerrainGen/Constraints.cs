using FlyingGame.Core;
namespace IslandTerrainGen;

/// <summary>
/// Everything the authored layout says about the ground, mirrored from the runtime layers in
/// IslandHeightIndex so that the natural relief already agrees with them and the runtime layers
/// (which still run, unchanged, and remain the authority) have almost nothing left to correct.
///   Fix  : equality (pads, lakes, mesas, ski zones, summits, box floors, knolls, crowns).
///   Lo/Hi: inequalities (cave roofs and lake rims from below; clearance caps, saddles and canyon floors from above).
/// </summary>
public sealed class Constraints
{
    public readonly Grid G;
    public readonly double[] Fix, Lo, Hi;
    public readonly bool[] Ocean;
    public readonly double[] Shore; // signed km, + inland, of the owning land polygon
    public readonly List<(IslandPoint p, double z, string id)> Summits = new();
    static IslandValue W => IslandTerrain.World;
    const double Ft = IslandTerrain.Feet;
    static double Smooth(double t) => IslandTerrain.Smooth(t);

    /// <summary>Experiment hook: shorten visual-only runway-end surfaces (budget list order: airways, IFR finals, runway ends).</summary>
    public static double? VfrEndLengthKm;
    public static readonly Dictionary<int, double> AirwayCapOverride = new();
    public static double VfrGuardKm = .25, VfrBlendKm = .2;
    static readonly IslandValue[] BudgetList = W["clearance_budgets"].Items;
    static bool IfrFinalCount(IslandValue e) => Array.IndexOf(BudgetList, e) >= 35;
    public Constraints(Grid g, IslandPoint[] landPolygon)
    {
        G = g; Fix = new double[g.N]; Lo = new double[g.N]; Hi = new double[g.N]; Ocean = new bool[g.N]; Shore = new double[g.N];
        Array.Fill(Fix, double.NaN); Array.Fill(Lo, double.NegativeInfinity); Array.Fill(Hi, double.PositiveInfinity);
        Parallel.For(0, g.N, k => { Shore[k] = IslandTerrain.Shore(g.P(k), landPolygon); Ocean[k] = Shore[k] <= 0; });
        Parallel.For(0, g.N, k => Point(k, g.P(k)));
        foreach (var q in W["height_field"]["peaks"].Items)
        {
            var c = q["centre"].Point; int k = g.Nearest(c); if (k < 0 || Ocean[k]) continue;
            Fix[k] = q.N("top_ft") * Ft; Summits.Add((c, Fix[k], q.S("id")));
        }
    }
    void SetFix(int k, double z) { if (double.IsNaN(Fix[k])) Fix[k] = z; }
    void SetLo(int k, double z) { Lo[k] = Math.Max(Lo[k], z); }
    void SetHi(int k, double z) { Hi[k] = Math.Min(Hi[k], z); }

    void Point(int k, IslandPoint p)
    {
        if (Ocean[k]) return;
        // Field pads (and their shoulders): the exact plane the runtime pad blend tends to.
        foreach (var f in IslandTerrain.Fields)
        {
            var c = f.Centre; double l = f.Length / 1000;
            if (f.Field == "DRAKE")
            {
                if (p.N < W["features"].Items.First(x => x.S("id") == "F1")["shell_geometry"]["mouth"].Point.N) continue;
                c += f.U * (-l / 2 + f.Definition.N("exterior_strip_m", 152.4) / 2000); l = .1524;
            }
            var d = p - c; double ad = d.Dot(f.U), cr = d.Dot(f.V);
            if (Math.Abs(ad) <= l / 2 + f.EndShoulder / 1000 && Math.Abs(cr) <= f.Width / 2000 + f.Shoulder / 1000)
                SetFix(k, f.Elevation + Math.Clamp(ad, -l / 2, l / 2) * 1000 * f.Slope / 100 - (f.Surface == "water" ? W["height_field"].N("water_lane_bed_offset_m") : 0));
        }
        foreach (var q in W["mesa_pads"].Items)
        {
            var u = IslandPoint.Heading(q.N("heading_true_deg")); var d = p - q["centre"].Point;
            if (Math.Abs(d.Dot(u)) <= q.N("length_m") / 2000 && Math.Abs(d.E * u.N - d.N * u.E) <= q.N("width_m") / 2000) SetFix(k, q.N("elevation_ft") * Ft);
        }
        // Lakes: the water surface inside, a dry rim around (the runtime cuts the bed below the surface).
        foreach (var q in W["water"].Items)
        {
            double rr = IslandTerrain.EllipseRadius(p, q), z = q.N("elevation_ft") * Ft;
            if (rr <= 1) SetFix(k, z);
            else if (rr <= 1.45) SetLo(k, z + (q.Has("basin_detail") ? q["basin_detail"].N("ridge_height_m") : 2));
        }
        foreach (var q in W["glaciers"].Items)
        {
            var s = q["ski_zone"]; if ((p - s["centre"].Point).Length <= .15) SetFix(k, s.N("elevation_ft") * Ft);
        }
        // Lesson terrain: the escape saddle is a ceiling, the box floor and headwall are exact.
        foreach (var q in W["lesson_terrain"].Items)
        {
            if (IslandTerrain.Path(p, q["safe_path"]).d <= .2) SetHi(k, q.N("saddle_ft") * Ft);
            var (d, t) = IslandTerrain.Path(p, q["box_path"]);
            if (d <= q.N("floor_half_width_m") / 1000) SetFix(k, q.N("saddle_ft") * Ft + q.N("box_floor_rise_m") * t);
            var wall = q["headwall_geometry"];
            if ((p - wall["centre"].Point).Length <= wall.N("core_radius_m") / 1000) SetFix(k, wall.N("top_ft") * Ft);
        }
        var box = W["aerobatic_boxes"][2]; var bd = p - box["centre"].Point;
        if (Math.Max(Math.Abs(bd.E), Math.Abs(bd.N)) <= .5) SetFix(k, box.N("centre_ground_ft") * Ft);
        foreach (var q in W["height_field"]["hazard_benches"].Items)
            if ((p - q["centre"].Point).Length <= q.N("core_radius_m") / 1000) SetFix(k, q.N("elevation_ft") * Ft);
        var shell = IslandTerrain.Feature("F1")["shell_geometry"];
        if ((p - shell["crown_centre"].Point).Length <= .25) SetFix(k, shell.N("crown_msl_ft") * Ft);
        var ch = p - shell["chamber_centre"].Point;
        if (Math.Abs(ch.N) <= .43 && Math.Abs(ch.E) <= .23) SetLo(k, shell.N("minimum_roof_msl_m") + 5);
        var cave = IslandTerrain.Feature("F2"); var ci = cave["interior"];
        if ((p - ci["chamber_centre"].Point).Length <= cave.N("chamber_radius_m") / 1000 + .03) SetLo(k, ci.N("roof_top_msl_m") + 2);
        // Authored canyon floors are ceilings along the thalweg; the walls are left to the erosion.
        foreach (var q in W["canyons"].Items)
        {
            var path = q["path"].Items; double total = 0; for (int i = 1; i < path.Length; i++) total += (path[i].Point - path[i - 1].Point).Length;
            var fw = q["floor_width_m"].Items; var zf = q["floor_ft"].Items; double run = 0;
            for (int i = 1; i < path.Length; i++)
            {
                double len = (path[i].Point - path[i - 1].Point).Length; var (d, t0) = IslandTerrain.Segment(p, path[i - 1].Point, path[i].Point);
                double t = (run + t0 * len) / total; run += len;
                double floorW = fw[0].Number() + (fw[^1].Number() - fw[0].Number()) * t;
                if (d * 1000 <= floorW / 2) SetHi(k, (zf[0].Number() + (zf[^1].Number() - zf[0].Number()) * t) * Ft);
            }
        }
        // Clearance budgets, exactly where the runtime would cap (including its outer blend), with a
        // margin so the runtime min() never binds and leaves a flat. Summit cores are released, as at runtime.
        bool nearSummit = W["height_field"]["peaks"].Items.Any(q => (p - q["centre"].Point).Length < .4);
        foreach (var e in W["clearance_budgets"].Items)
        {
            double length = e.Has("length") ? e.N("length") : 0;
            if (VfrEndLengthKm.HasValue && e.S("kind") == "final" && !e.Has("ifr") && IfrFinalCount(e) ) length = VfrEndLengthKm.Value;
            if (e.S("kind") == "airway")
            {
                if (nearSummit) continue;
                double cap = e.N("cap_m"); if (AirwayCapOverride.TryGetValue(Array.IndexOf(BudgetList, e), out double o)) cap = o;
                if (IslandTerrain.Segment(p, e["a"].Point, e["b"].Point).d <= e.N("width") + e.N("blend")) SetHi(k, cap - 12);
                continue;
            }
            var u = e["u"].Point; var dp = p - e["a"].Point; double along = -dp.Dot(u), cross = Math.Abs(dp.E * u.N - dp.N * u.E), blend = e.N("blend");
            bool vfr = VfrEndLengthKm.HasValue && IfrFinalCount(e); double guard = vfr ? VfrGuardKm : .25; if (vfr) blend = VfrBlendKm;
            double dd = Math.Max(0, Math.Max(Math.Max(-along - guard, along - length - guard), cross - (.15 + .05 * Math.Max(0, along)) - guard));
            // A Part 77 visual approach surface starts AT the runway end's elevation; the 30 m TERPS-style margin
            // belongs to the instrument finals only.
            if (dd < blend) SetHi(k, e.N("elevation_m") + Math.Max(0, along) * 1000 / e.N("ratio") - (vfr ? 0 : 30) - 8);
        }
    }
}
