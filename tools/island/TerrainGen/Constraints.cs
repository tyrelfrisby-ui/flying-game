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
    /// <summary>The rising ground behind an alpine tarn: a landform preference, not an authored constraint,
    /// so it yields to every runtime ceiling (Generate merges it into the soft bench).</summary>
    public readonly double[] TarnRim;
    public readonly bool[] Ocean;
    public readonly double[] Shore; // signed km, + inland, of the owning land polygon
    public readonly List<(IslandPoint p, double z, string id)> Summits = new();
    static IslandValue W => IslandTerrain.World;
    const double Ft = IslandTerrain.Feet;
    static double Smooth(double t) => IslandTerrain.Smooth(t);

    public Constraints(Grid g, IslandPoint[] landPolygon)
    {
        G = g; Fix = new double[g.N]; Lo = new double[g.N]; Hi = new double[g.N]; TarnRim = new double[g.N]; Ocean = new bool[g.N]; Shore = new double[g.N];
        Array.Fill(Fix, double.NaN); Array.Fill(TarnRim, double.NaN); Array.Fill(Lo, double.NegativeInfinity); Array.Fill(Hi, double.PositiveInfinity);
        Parallel.For(0, g.N, k => { Shore[k] = IslandTerrain.Shore(g.P(k), landPolygon); Ocean[k] = Shore[k] <= 0; });
        Parallel.For(0, g.N, k => Point(k, g.P(k)));
        // Charted wires inside an airway swath: the airway check takes the highest ground under the span plus the
        // wire's height and sag, and needs it 2,000 ft under the MEA (MEA - 2,025 ft is the swath's cap_m).
        foreach (var wire in W["hazards"]["wires"].Items)
        {
            var a = wire["endpoints"][0].Point; var b = wire["endpoints"][1].Point; double span = (b - a).Length * 1000;
            double top = wire.N("lowest_agl_m") + span * wire.N("sag_fraction"), cap = double.PositiveInfinity;
            var mid = (a + b) * .5;
            foreach (var e in W["clearance_budgets"].Items)
                if (e.S("kind") == "airway" && IslandTerrain.Segment(mid, e["a"].Point, e["b"].Point).d <= e.N("width") + e.N("blend") + span / 1000)
                    cap = Math.Min(cap, e.N("cap_m") + 25 * Ft - top - 5);
            if (double.IsInfinity(cap)) continue;
            for (int k = 0; k < g.N; k++)
            {
                var p = g.P(k);
                if (p.E >= Math.Min(a.E, b.E) - .03 && p.E <= Math.Max(a.E, b.E) + .03 && p.N >= Math.Min(a.N, b.N) - .03 && p.N <= Math.Max(a.N, b.N) + .03 && !Ocean[k]) SetHi(k, cap);
            }
        }
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
                SetFix(k, f.Elevation + Math.Clamp(ad, -l / 2 - f.SlopeShoulder / 1000, l / 2 + f.SlopeShoulder / 1000) * 1000 * f.Slope / 100 - (f.Surface == "water" ? W["height_field"].N("water_lane_bed_offset_m") : 0));
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
            // An alpine tarn (above 1,900 m) lies in a cirque: the ground RISES behind it, 40% over the first
            // 300 m of rim, so the lake is in a basin and not on a mound.
            if (z > 1900 && rr > 1)
            {
                double rim = (rr - 1) * Math.Min(q["radii_km"][0].Number(), q["radii_km"][1].Number()) * 1000;
                if (rim < 450) TarnRim[k] = Math.Max(double.IsNaN(TarnRim[k]) ? double.NegativeInfinity : TarnRim[k], z + Math.Min(120, .4 * rim));
            }
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
                // A canyon whose floor is "exact" (the Frank Church strip reaches) holds its floor AT the authored grade,
                // so the erosion cannot dig the river below its strips' benches or leave a hump in it; the others (and the
                // gorges where a river leaves for lower ground) keep the floor as a ceiling only.
                double fz = (zf[0].Number() + (zf[^1].Number() - zf[0].Number()) * t) * Ft;
                if (d * 1000 <= floorW / 2) { if (q.S("floor") == "exact") SetFix(k, fz); else SetHi(k, fz); }
            }
        }
        // Clearance budgets, exactly where the runtime would cap (including its outer blend), with a
        // margin so the runtime min() never binds and leaves a flat. Summit cores are released, as at runtime.
        bool nearSummit = W["height_field"]["peaks"].Items.Any(q => (p - q["centre"].Point).Length < .4);
        foreach (var e in W["clearance_budgets"].Items)
        {
            if (e.S("kind") == "airway")
            {
                if (nearSummit) continue;
                if (IslandTerrain.Segment(p, e["a"].Point, e["b"].Point).d <= e.N("width") + e.N("blend")) SetHi(k, e.N("cap_m") - 12);
                continue;
            }
            // Same footprint and ceiling as IslandHeightIndex.Budget (its broad pass), from the budget's own data:
            // instrument finals 7.408 km with a 0.25 km guard and 30 m margin; visual runway ends 1.524 km, none.
            var u = e["u"].Point; var dp = p - e["a"].Point; double along = -dp.Dot(u), cross = Math.Abs(dp.E * u.N - dp.N * u.E);
            double guard = e.N("guard_km", .25), margin = e.N("margin_m", 30), length = e.N("length");
            double dd = Math.Max(0, Math.Max(Math.Max(-along - guard, along - length - guard), cross - (.15 + .05 * Math.Max(0, along)) - guard));
            // Generation margin under the runtime ceiling: 8 m below an instrument surface, 2 m below a visual
            // one (whose surface starts level with the runway end, so a bigger margin would dig a moat round it).
            if (dd < e.N("blend")) SetHi(k, e.N("elevation_m") + Math.Max(0, along) * 1000 / e.N("ratio") - margin - (margin > 0 ? 8 : 2));
        }
        // Beside a canyon strip the wall leans back: within 400 m of its pad the ground stands no higher than the pad's
        // plane plus 0.7 m a metre (35 degrees), so the pad's blend meets a slope, not the foot of a cliff. (Without
        // it the blend's outer edge met walls rising 20 m in 5 m beside Sulphur Creek.) Not past a one-way strip's high
        // end, where the wall is the point.
        foreach (var f in IslandTerrain.Fields)
        {
            if (!f.Definition.Has("river")) continue;
            var d = p - f.Centre; double ad = d.Dot(f.U), cr = d.Dot(f.V);
            double da = Math.Max(0, Math.Abs(ad) - f.Length / 2000 - f.EndShoulder / 1000), db = Math.Max(0, Math.Abs(cr) - f.Width / 2000 - f.Shoulder / 1000);
            double dd = Math.Sqrt(da * da + db * db) * 1000; if (dd <= 0 || dd > 400) continue;
            if (f.Definition.Has("one_way_true_deg") && ad > f.Length / 2000) continue;
            SetHi(k, f.Plane(p) + 25 + .7 * dd);
        }
        // Canyon walls, and the headwall past a one-way strip: floors, never ceilings, and kept 25 m under any
        // runtime ceiling so the runtime cap never has a wall to shave.
        double sides = WallFloor(p);
        if (double.IsFinite(sides)) SetLo(k, Math.Min(sides, Hi[k] - 25));
    }

    // ---- The Frank Church valleys (2026-11-03, lane/claude-frank-church-valleys) ------------------------------
    // Ty, flying the island: the backcountry strips sat on mesa tops, "always deep in the valleys" in the real
    // place. A canyon used to hold only its THALWEG down (the floor is a ceiling above) and leave its walls to the
    // erosion, and between 1,400 m plateau and a membrane through strip anchors the erosion made a shallow dish.
    // A canyon that carries "walls" now also holds its sides UP: within run_m of the floor's edge the ground may not
    // stand lower than a smoothstep from the floor to top_ft, and it stays at least top_ft for crest_m more; past
    // that the relief is free again, so the walls end in ridges, not a table. It is a lower bound only — the
    // erosion still cuts gullies into anything above it — and it is measured from the NEAREST walled thalweg, so
    // a tributary's wall never stands on the main stem's floor at a junction.
    sealed record WallCanyon(IslandPoint A, IslandPoint B, double Z0, double Z1, double Fw0, double Fw1, double Top, double Run, double Crest);
    static readonly WallCanyon[] WallCanyons = W["canyons"].Items.Where(q => q.Has("walls")).SelectMany(q =>
    {
        var path = q["path"].Items; var fw = q["floor_width_m"].Items; var zf = q["floor_ft"].Items; var w = q["walls"];
        double total = 0; for (int i = 1; i < path.Length; i++) total += (path[i].Point - path[i - 1].Point).Length;
        var list = new List<WallCanyon>(); double run = 0;
        for (int i = 1; i < path.Length; i++)
        {
            double len = (path[i].Point - path[i - 1].Point).Length, t0 = run / total, t1 = (run + len) / total; run += len;
            double Z(double t) => (zf[0].Number() + (zf[^1].Number() - zf[0].Number()) * t) * Ft;
            double F(double t) => fw[0].Number() + (fw[^1].Number() - fw[0].Number()) * t;
            list.Add(new WallCanyon(path[i - 1].Point, path[i].Point, Z(t0), Z(t1), F(t0), F(t1), w.N("top_ft") * Ft, w.N("run_m"), w.N("crest_m")));
        }
        return list;
    }).ToArray();
    /// <summary>The one-way strips: landing heading (uphill), the high end, and its elevation.</summary>
    sealed record OneWay(IslandField F, IslandPoint High, double HighZ);
    static readonly OneWay[] OneWays = IslandTerrain.Fields.Where(f => f.Definition.Has("one_way_true_deg"))
        .Select(f => new OneWay(f, f.Centre + f.U * (f.Length / 2000), f.Elevation + f.Length / 2 * f.Slope / 100)).ToArray();

    static double WallFloor(IslandPoint p)
    {
        double lo = double.NegativeInfinity;
        if (WallCanyons.Length > 0)
        {
            double best = double.PositiveInfinity; WallCanyon? near = null; double nt = 0;
            foreach (var c in WallCanyons) { var (d, t) = IslandTerrain.Segment(p, c.A, c.B); if (d < best) { best = d; near = c; nt = t; } }
            double dm = best * 1000, z = near!.Z0 + (near.Z1 - near.Z0) * nt, half = (near.Fw0 + (near.Fw1 - near.Fw0) * nt) / 2;
            if (dm > half && dm < half + near.Run + near.Crest)
                lo = z - 30 + (Math.Max(z, near.Top) - z + 30) * Smooth((dm - half) / near.Run);
        }
        // Never bury a landing site: within a kilometre of any pad the floor gives way to the pad's own plane plus
        // half the distance outside its shoulders, so the wall rises behind a strip instead of standing on it.
        if (double.IsFinite(lo))
            foreach (var f in IslandTerrain.Fields)
            {
                var d = p - f.Centre; double ad = d.Dot(f.U), cr = d.Dot(f.V);
                double da = Math.Max(0, Math.Abs(ad) - f.Length / 2000 - f.EndShoulder / 1000), db = Math.Max(0, Math.Abs(cr) - f.Width / 2000 - f.Shoulder / 1000);
                double dd = Math.Sqrt(da * da + db * db) * 1000; if (dd > 1000) continue;
                lo = Math.Min(lo, f.Plane(p) + .5 * dd);
            }
        // A one-way strip lands uphill INTO the ground: past its high end, within a cone about its axis, the ground
        // climbs at least 0.6 m a metre for 600 m (31 degrees). That is the wall that makes a go-around impossible.
        foreach (var o in OneWays)
        {
            var d = p - o.High; double along = d.Dot(o.F.U) * 1000, cross = Math.Abs(d.Dot(o.F.V)) * 1000;
            // Continuous everywhere: it rises from the end shoulder, falls away to either side of the axis and fades out
            // past 650 m, so it never switches off into a step (a hard cone edge made a 30 m cliff beside Dewey Moore).
            double from = o.F.EndShoulder; if (along < from) continue;
            double head = o.HighZ + .6 * Math.Min(along - from, 600) - 1.2 * Math.Max(0, cross - 60 - .3 * along) - 1.2 * Math.Max(0, along - 650);
            lo = Math.Max(lo, head);
        }
        return lo;
    }
}
