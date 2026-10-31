namespace FlyingGame.Core;

public static partial class IslandTerrain
{
    // Frozen pre-index continuous evaluator (1fe347d). Validation/benchmark oracle only.
    // Keep this path independent of the optimized feature data and spatial rejection.
    public static double EvaluateReference(IslandPoint p)
    {
        double x = p.E, y = p.N, dm = Shore(p, Land[0]), dc = InBounds(p, 1) ? Shore(p, Land[1]) : -100, d = Math.Max(dm, dc);
        int owner = -1; bool volcanoProtected = false;
        for (int i = 0; i < Outer.Length; i++)
        {
            var q = Outer[i]; if (!InBounds(p, i + 2)) continue; double dd = Shore(p, Land[i + 2]);
            if (dd > d) { d = dd; owner = i; }
            if (q.Has("volcano") && (p - q["volcano"]["centre"].Point).Length <= q["volcano"].N("radius_km")) volcanoProtected = true;
        }
        if (d < -.35) return -30;
        double h = (dc > dm ? 30 : 30 + 4570 * Smooth(dm / 2)) * Feet;
        foreach (var q in H["regional_overrides"].Items)
        {
            var regionalDelta = p - q["centre"].Point; double rx = q["radii_km"][0].Number(), ry = q["radii_km"][1].Number();
            h = Mix(h, q.N("elevation_ft") * Feet, 1 - Smooth(Math.Sqrt(regionalDelta.E * regionalDelta.E / (rx * rx) + regionalDelta.N * regionalDelta.N / (ry * ry))));
        }
        foreach (var q in H["crests"].Items) h = Math.Max(h, Mix(h, q.N("top_ft") * Feet, 1 - Smooth(Path(p, q["path"]).d / q.N("width_km"))));
        foreach (var q in Peaks) h = Math.Max(h, Mix(h, q.N("top_ft") * Feet, 1 - Smooth((p - q["centre"].Point).Length / q.N("radius_km"))));
        foreach (var q in World["water"].Items) h = Mix(h, Math.Min(h, q.N("elevation_ft") * Feet - q.N("depth_m")), 1 - Smooth((EllipseRadius(p, q) - .85) / .45));
        foreach (var q in World["glaciers"].Items)
        {
            var (dd, t) = Path(p, q["path"]); double a = (1 - Smooth((dd - q.N("half_width_m") / 1000) / .3)) * Smooth(t / .12);
            var ski = q["ski_zone"]; h = Mix(h, (q.N("head_ft") * (1 - t) + ski.N("elevation_ft") * t) * Feet, a);
            h = Mix(h, ski.N("elevation_ft") * Feet, 1 - Smooth(((p - ski["centre"].Point).Length - .15) / .25));
        }
        foreach (var q in World["canyons"].Items)
        {
            var pts = q["path"].Items; double total = 0, run = 0;
            for (int i = 1; i < pts.Length; i++) total += (pts[i].Point - pts[i - 1].Point).Length;
            for (int i = 1; i < pts.Length; i++)
            {
                double l = (pts[i].Point - pts[i - 1].Point).Length; var (dd, t0) = Segment(p, pts[i - 1].Point, pts[i].Point); double t = (run + t0 * l) / total; run += l;
                var rw = q["rim_width_m"].Items; var fw = q["floor_width_m"].Items; var zf = q["floor_ft"].Items;
                double rim = Mix(rw[0].Number(), rw[rw.Length - 1].Number(), t), floor = Mix(fw[0].Number(), fw[fw.Length - 1].Number(), t);
                h = Mix(h, Math.Min(h, Mix(zf[0].Number(), zf[zf.Length - 1].Number(), t) * Feet), 1 - Smooth((dd * 1000 - floor / 2) / Math.Max(1, (rim - floor) / 2)));
            }
        }
        foreach (var q in World["lesson_terrain"].Items)
        {
            h = Mix(h, Math.Min(h, q.N("saddle_ft") * Feet), 1 - Smooth((Path(p, q["safe_path"]).d - .12) / .25));
            var (dd, t) = Path(p, q["box_path"]); h = Mix(h, q.N("saddle_ft") * Feet + q.N("box_floor_rise_m") * t, 1 - Smooth((dd - .08) / .12));
        }
        var box = World["aerobatic_boxes"][2]; var bd = p - box["centre"].Point;
        h = Mix(h, 5000 * Feet, 1 - Smooth((Math.Max(Math.Abs(bd.E), Math.Abs(bd.N)) - .5) / .2));
        if (owner >= 0)
        {
            h = -30;
            for (int i = 0; i < Outer.Length; i++)
            {
                if (!InBounds(p, i + 2)) continue; double sd = Shore(p, Land[i + 2]); if (sd <= 0) continue; var q = Outer[i];
                double zh = q.N("base_ft", q.N("elevation_ft", 35)) * Feet;
                if (q.Has("relief")) zh += (q["relief"].N("top_ft") * Feet - zh) * (1 - Smooth((p - q["centre"].Point).Length / q["relief"].N("radius_km")));
                if (q.Has("volcano"))
                {
                    var v = q["volcano"]; double r = (p - v["centre"].Point).Length, rr = v.N("rim_radius_km"), rim = v.N("rim_ft") * Feet, b = q.N("base_ft") * Feet;
                    if (r <= rr) zh = Mix(v.N("crater_floor_ft") * Feet, rim, Smooth(r / rr));
                    else if (r < v.N("radius_km")) zh = b + (rim - b) * (v.N("radius_km") - r) / (v.N("radius_km") - rr);
                }
                h = Math.Max(h, -30 + (zh + 30) * Smooth(sd / World["edge"].N("land_shore_fade_km")));
            }
        }
        var terrace = H["land_moa_terrace"];
        h = Mix(h, terrace.N("elevation_ft") * Feet, 1 - Smooth(Math.Max(0, -Shore(p, terrace["boundary"].Points)) / (terrace.N("blend_m") / 1000)));
        foreach (var z in World["combat_zones"].Items) foreach (var f in z["combat_features"].Items)
        {
            var c = f["centre"].Point;
            if (f.S("type") == "paired ridges and dry canyon")
            {
                double a = 1 - Smooth(Math.Max(0, Math.Abs(y - c.N) - f.N("length_m") / 2000) / .025);
                double cut = (1 - Smooth((Math.Abs(x - c.E) - f.N("canyon_floor_width_m") / 2000) / .02)) * (1 - Smooth((Math.Abs(y - c.N) - .12) / .03));
                h = Mix(h, Math.Min(h, f.N("canyon_floor_ft") * Feet), cut);
                foreach (int sign in new[] { -1, 1 })
                {
                    double rr = Math.Abs(x - c.E - sign * (f.N("canyon_width_m") / 2000 + f.N("ridge_half_width_m") / 1000)) / (f.N("ridge_half_width_m") / 1000);
                    h = Mix(h, f.N("rim_ft") * Feet, (1 - Smooth(rr)) * a);
                }
            }
        }
        foreach (var q in H["hazard_benches"].Items) h = Mix(h, q.N("elevation_ft") * Feet, 1 - Smooth(((p - q["centre"].Point).Length - q.N("core_radius_m") / 1000) / (q.N("blend_m") / 1000)));
        foreach (var z in World["combat_zones"].Items) foreach (var f in z["combat_features"].Items)
            if (f.S("type") == "rock spire pair") foreach (var c in f["stack_centres"].Items) h = Math.Max(h, Mix(h, f.N("top_msl_ft") * Feet, 1 - Smooth((p - c.Point).Length / (f.N("radius_m") / 1000))));
        foreach (var e in Budgets) if (!volcanoProtected) h = ApplyBudgetReference(p, e, h, 1, true);
        foreach (var q in World["lesson_terrain"].Items)
        {
            var (dd, t) = Path(p, q["box_path"]); h = Mix(h, q.N("saddle_ft") * Feet + q.N("box_floor_rise_m") * t, 1 - Smooth((dd - q.N("floor_half_width_m") / 1000) / (q.N("outer_blend_m") / 1000)));
            var wall = q["headwall_geometry"]; h = Math.Max(h, Mix(h, wall.N("top_ft") * Feet, 1 - Smooth(((p - wall["centre"].Point).Length - wall.N("core_radius_m") / 1000) / (wall.N("blend_m") / 1000))));
        }
        foreach (var q in Peaks) h = Mix(h, q.N("top_ft") * Feet, 1 - Smooth(((p - q["centre"].Point).Length - .12) / .28));
        h = Mix(h, box.N("centre_ground_ft") * Feet, 1 - Smooth((Math.Max(Math.Abs(bd.E), Math.Abs(bd.N)) - .5) / .15));
        var dr = Feature("F1"); var ds = dr["shell_geometry"]; var dp = p - ds["chamber_centre"].Point;
        double dd0 = Math.Sqrt(Math.Pow(Math.Max(0, Math.Abs(dp.N) - .410), 2) + Math.Pow(Math.Max(0, Math.Abs(dp.E) - .210), 2));
        h = Math.Max(h, Mix(h, ds.N("minimum_roof_msl_m"), 1 - Smooth(dd0 / (ds.N("roof_blend_m") / 1000))));
        h = Math.Max(h, Mix(h, ds.N("crown_msl_ft") * Feet, 1 - Smooth(((p - ds["crown_centre"].Point).Length - .25) / .4)));
        var cave = Feature("F2"); var ci = cave["interior"];
        h = Math.Max(h, Mix(h, ci.N("roof_top_msl_m"), 1 - Smooth(((p - ci["chamber_centre"].Point).Length - cave.N("chamber_radius_m") / 1000) / .08)));
        foreach (var q in World["water"].Items)
        {
            if (!q.Has("basin_detail")) continue; double rr = EllipseRadius(p, q), water = q.N("elevation_ft") * Feet;
            if (rr <= 1) h = water - q.N("depth_m") * (1 - Smooth(rr));
            else
            {
                double dd = (rr - 1) * Math.Min(q["radii_km"][0].Number(), q["radii_km"][1].Number()) * 1000; var cfg = q["basin_detail"]; double core = cfg.N("ridge_offset_m"), width = cfg.N("bank_width_m");
                if (dd < core) h = water + cfg.N("ridge_height_m") * Smooth(dd / core);
                else if (dd < width) h = Mix(h, water + cfg.N("ridge_height_m"), 1 - Smooth((dd - core) / (width - core)));
            }
        }
        foreach(var q in World["mesa_pads"].Items)
        {
            var u=IslandPoint.Heading(q.N("heading_true_deg"));var d0=p-q["centre"].Point;
            double da=Math.Max(0,Math.Abs(d0.Dot(u))-q.N("length_m")/2000),db=Math.Max(0,Math.Abs(d0.E*u.N-d0.N*u.E)-q.N("width_m")/2000);
            h=Mix(h,q.N("elevation_ft")*Feet,1-Smooth(Math.Sqrt(da*da+db*db)/(q.N("blend_m")/1000)));
        }
        double nearest = double.PositiveInfinity, bestA = 0, bestZ = 0, sumW = 0, sumZ = 0, maxA = 0;
        foreach (var f in Fields)
        {
            var c = f.Centre; double l = f.Length / 1000;
            if (f.Field == "DRAKE")
            {
                if (y < ds["mouth"].Point.N) continue;
                double exterior = dr.N("exterior_strip_m") / 1000; c += f.U * (-l / 2 + exterior / 2); l = exterior;
            }
            var delta = p - c; double ad = delta.Dot(f.U), cr = delta.Dot(f.V);
            double da = Math.Max(0, Math.Abs(ad) - l / 2 - f.EndShoulder / 1000), db = Math.Max(0, Math.Abs(cr) - f.Width / 2000 - f.Shoulder / 1000), dd = Math.Sqrt(da * da + db * db);
            if (dd >= f.Blend / 1000) continue;
            double a = 1 - Smooth(dd / (f.Blend / 1000)), zz = f.Elevation + Math.Clamp(ad, -l / 2, l / 2) * 1000 * f.Slope / 100 - (f.Surface == "water" ? H.N("water_lane_bed_offset_m") : 0);
            if (dd < nearest || (dd == nearest && a < bestA)) { nearest = dd; bestA = a; bestZ = zz; }
            maxA = Math.Max(maxA, a); if (dd > 0) { double wt = a / (dd * dd); sumW += wt; sumZ += wt * zz; }
        }
        if (nearest < double.PositiveInfinity)
        {
            h = Mix(h, nearest > 1e-10 ? sumZ / sumW : bestZ, nearest > 1e-10 ? maxA : bestA);
            if (nearest > 1e-10)
            {
                double release=Peaks.Min(q=>Smooth(((p-q["centre"].Point).Length-.12)/.28));
                foreach (var e in Budgets) if (!volcanoProtected) h = ApplyBudgetReference(p, e, h, maxA*release*Smooth(nearest / .03), false);
            }
        }
        double peakRelease=Peaks.Min(q=>Smooth(((p-q["centre"].Point).Length-.12)/.28));
        double padRelease=nearest<double.PositiveInfinity?Smooth(nearest/.03):1;
        foreach(var e in Budgets)
        {
            if(e.S("kind")!="airway")continue;
            double alpha=(1-Smooth((Segment(p,e["a"].Point,e["b"].Point).d-3.704)/e.N("blend")))*padRelease*peakRelease;
            h=Mix(h,Math.Min(h,e.N("cap_m")),alpha);
        }
        return -30 + (h + 30) * Smooth(d / World["edge"].N("land_shore_fade_km"));
    }
    private static double ApplyBudgetReference(IslandPoint p, IslandValue e, double h, double factor, bool broad)
    {
        double alpha, cap;
        if (e.S("kind") == "airway") { alpha = 1 - Smooth((Segment(p, e["a"].Point, e["b"].Point).d - e.N("width")) / e.N("blend")); cap = e.N("cap_m"); }
        else
        {
            var u = e["u"].Point; var dp = p - e["a"].Point; double ad = -dp.Dot(u), cross = Math.Abs(dp.E * u.N - dp.N * u.E), width = .15 + .05 * Math.Max(0, ad);
            double dd = Math.Max(0, Math.Max(Math.Max(-ad - .25, ad - e.N("length") - .25), cross - width - .25));
            alpha = 1 - Smooth(dd / e.N("blend")); cap = e.N("elevation_m") + (broad ? Math.Max(0, ad) : ad) * 1000 / e.N("ratio") - (broad ? 30 : 0);
        }
        return Mix(h, Math.Min(h, cap), alpha * factor);
    }
}
