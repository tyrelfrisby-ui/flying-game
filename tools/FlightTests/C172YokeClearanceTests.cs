using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 14:58 (737 captain, CFI): "Move the yoke up ... It should go up where it does not touch the thighs
/// with full deflection." Before yoke-v1 the pilot's right horn, at 90 deg of wheel, reached 17 mm into the right
/// thigh, 29 mm with the column forward at full left rudder (check_c172_pedal_fit.py, seat-v1).
///
/// This sweeps BOTH yokes of the shipped own 172 (c172-like-own.glb) through the rig's whole envelope, the column
/// over its +/-70 mm in 10 mm steps and the wheel over +/-90 deg in 2 deg steps (the deepest point of a horn is not
/// at 90 deg: its lower grip end swings lowest near 73 deg), and measures every yoke triangle, sampled at <= 2 mm,
/// against both seated pilots' thighs.
///
/// The thighs are literals, copied by hand from check_c172_pedal_fit.py's solution of Codex's 1.778 m mannequin at
/// the approved eyes (CockpitEyeTests.ApprovedEyes: pilot body y -0.337, CFI +0.332; both x -0.346, z -0.548):
/// H-point fwd -0.2993, up -0.1561; hips 0.1005 m either side of the eye's lateral; hip to knee 0.4125 m; thigh
/// radius 0.065 m. The checker lifts a thigh at most 2 deg off the cushion (full aft pedal), so each thigh is tested
/// flat and lifted 2 deg. glTF frame: +x forward, +y up, +z right.
/// </summary>
public class C172YokeClearanceTests
{
    private readonly ITestOutputHelper _o;
    public C172YokeClearanceTests(ITestOutputHelper o) { _o = o; }

    private const double RequiredClearanceM = 0.015;
    private const double HipFwd = -0.2993, HipUp = -0.1561, HipHalfWidth = 0.1005, HipToKnee = 0.4125, ThighRadius = 0.065;
    private const double ColumnTravelM = 0.07, WheelDeg = 90.0, MaxThighLiftDeg = 2.0;

    private static string Model => Path.Combine(OwnPittsModelTests.Models, "c172-like", "c172-like-own.glb");

    [Fact]
    public void Both_yokes_clear_both_thighs_through_full_wheel_and_column_travel()
    {
        var (column, columnPivot) = Triangles("C172_Yoke_L_pitch");
        var (wheel, wheelPivot) = Triangles("C172_Yoke_L_roll");
        var (columnR, _) = Triangles("C172_Yoke_R_pitch");
        var (wheelR, wheelPivotR) = Triangles("C172_Yoke_R_roll");
        var seats = new[]
        {
            (name: "pilot", eyeRight: -0.337, column: Sample(column), wheel: Sample(wheel), pivot: wheelPivot),
            (name: "CFI", eyeRight: 0.332, column: Sample(columnR), wheel: Sample(wheelR), pivot: wheelPivotR),
        };
        double worst = double.PositiveInfinity; string where = "";
        foreach (var seat in seats)
        {
            double seatWorst = double.PositiveInfinity; string seatWhere = "";
            foreach (int sideSign in new[] { -1, 1 })
                foreach (double lift in new[] { 0.0, MaxThighLiftDeg })
                {
                    double right = seat.eyeRight + sideSign * HipHalfWidth;
                    var hip = (x: HipFwd, y: HipUp, z: right);
                    double l = lift * Math.PI / 180;
                    var knee = (x: HipFwd + HipToKnee * Math.Cos(l), y: HipUp + HipToKnee * Math.Sin(l), z: right);
                    for (int ci = -7; ci <= 7; ci++)
                    {
                        double c = ci * ColumnTravelM / 7;
                        foreach (var p in seat.column)
                            Keep(Distance((p.x + c, p.y, p.z), hip, knee) - ThighRadius, $"{seat.name} {(sideSign < 0 ? "left" : "right")} thigh lift {lift} deg, column {c:+0.000;-0.000} m, shaft", ref seatWorst, ref seatWhere);
                        // A wheel point keeps its distance from the hub pivot as it turns, so it cannot come nearer the thigh
                        // than the pivot's own distance less that: points that cannot beat the worst so far are skipped.
                        var pivotNow = (x: seat.pivot.x + c, y: seat.pivot.y, z: seat.pivot.z);
                        double reach = Distance(pivotNow, hip, knee) - ThighRadius;
                        foreach (double w in Angles)
                        {
                            double a = w * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
                            foreach (var p in seat.wheel)
                            {
                                if (reach - L(p, seat.pivot) > seatWorst) continue;   // |q - pivot| does not change with the turn
                                // About the hub's fore-aft axis through its pivot, then the column's travel.
                                double dy = p.y - seat.pivot.y, dz = p.z - seat.pivot.z;
                                var q = (x: p.x + c, y: seat.pivot.y + cs * dy - sn * dz, z: seat.pivot.z + sn * dy + cs * dz);
                                double d = Distance(q, hip, knee) - ThighRadius;
                                if (d < seatWorst) Keep(d, $"{seat.name} {(sideSign < 0 ? "left" : "right")} thigh lift {lift} deg, column {c:+0.000;-0.000} m, wheel {w:+0;-0} deg", ref seatWorst, ref seatWhere);
                            }
                        }
                    }
                }
            _o.WriteLine($"{seat.name}: least yoke-to-thigh clearance {seatWorst * 1000:F1} mm ({seatWhere})");
            if (seatWorst < worst) { worst = seatWorst; where = seatWhere; }
        }
        _o.WriteLine($"column shaft pivot (fwd, up, right) {columnPivot}");
        Assert.True(worst >= RequiredClearanceM, $"yoke {worst * 1000:F1} mm from a thigh at {where}; want >= {RequiredClearanceM * 1000:F0} mm");
    }

    /// <summary>Ty: "Usually the suction gage is a tiny 1 inch instrument", green range, a red line at each end. The
    /// needle sweeps 0-10 inHg over 270 deg, 27 deg/inHg (c172_cockpit.py SUCTION_*); its case is a 1 in face.</summary>
    [Fact]
    public void Suction_gauge_is_a_one_inch_instrument_on_a_270_degree_scale()
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(Model);
        JsonElement nodes = j.GetProperty("nodes");
        var node = nodes.EnumerateArray().Single(n => n.GetProperty("name").GetString() == "C172_Suction_case");
        var layout = JsonDocument.Parse(node.GetProperty("extras").GetProperty("layout").GetString()!).RootElement;
        Assert.Equal(0.0127 + 0.005, layout.GetProperty("half_extent").GetDouble(), 6);
        var dg = nodes.EnumerateArray().Single(n => n.GetProperty("name").GetString() == "C172_DG_case");
        var dgLayout = JsonDocument.Parse(dg.GetProperty("extras").GetProperty("layout").GetString()!).RootElement;
        Assert.Equal(dgLayout.GetProperty("x").GetDouble(), layout.GetProperty("x").GetDouble(), 6);   // still under the DG
        Assert.True(layout.GetProperty("z").GetDouble() < dgLayout.GetProperty("z").GetDouble());

        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "c172-like-own-rig.json");
        var row = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("rig").EnumerateArray()
            .Single(r => r.GetProperty("node").GetString() == "C172_Suction_needle");
        var map = row.GetProperty("map");
        Assert.Equal(0.0, map[0][0].GetDouble()); Assert.Equal(0.0, map[0][1].GetDouble());
        Assert.Equal(10.0, map[1][0].GetDouble()); Assert.Equal(270.0, map[1][1].GetDouble());
        Assert.Equal(0.0, row.GetProperty("model").GetDouble());
        // Ty 2026-11-04 16:42: upper red line 5.4 inHg (1980 172N manual), not 5.5; green arc 4.5-5.4.
        Assert.Contains("green 4.5-5.4", row.GetProperty("desc").GetString());
    }

    /// <summary>The wheel's full deflections first, so the bound in the sweep bites early; then every 2 deg.</summary>
    private static readonly double[] Angles = new[] { -WheelDeg, WheelDeg }
        .Concat(Enumerable.Range(0, (int)(2 * WheelDeg / 2) + 1).Select(i => -WheelDeg + 2.0 * i)).ToArray();

    private static void Keep(double d, string label, ref double worst, ref string where)
    {
        if (d < worst) { worst = d; where = label; }
    }

    private static double Distance((double x, double y, double z) p, (double x, double y, double z) a, (double x, double y, double z) b)
    {
        double bx = b.x - a.x, by = b.y - a.y, bz = b.z - a.z, px = p.x - a.x, py = p.y - a.y, pz = p.z - a.z;
        double t = Math.Clamp((px * bx + py * by + pz * bz) / (bx * bx + by * by + bz * bz), 0, 1);
        double dx = px - t * bx, dy = py - t * by, dz = pz - t * bz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double L((double x, double y, double z) u, (double x, double y, double z) v) =>
        Math.Sqrt((u.x - v.x) * (u.x - v.x) + (u.y - v.y) * (u.y - v.y) + (u.z - v.z) * (u.z - v.z));

    /// <summary>Points on every triangle, no two neighbours more than 2 mm apart.</summary>
    private static List<(double x, double y, double z)> Sample(List<((double x, double y, double z) a, (double x, double y, double z) b, (double x, double y, double z) c)> tris)
    {
        var pts = new List<(double, double, double)>();
        foreach (var (a, b, c) in tris)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Math.Max(L(a, b), Math.Max(L(b, c), L(c, a))) / 0.002));
            for (int i = 0; i <= n; i++)
                for (int k = 0; k <= n - i; k++)
                {
                    double u = (double)i / n, v = (double)k / n, w = 1 - u - v;
                    pts.Add((u * a.x + v * b.x + w * c.x, u * a.y + v * b.y + w * c.y, u * a.z + v * b.z + w * c.z));
                }
        }
        return pts.GroupBy(p => (Math.Round(p.Item1 / 0.0005), Math.Round(p.Item2 / 0.0005), Math.Round(p.Item3 / 0.0005))).Select(g => g.First()).ToList();
    }

    /// <summary>A node's triangles in model space (all its primitives, its own and its parents' transforms), and its pivot.</summary>
    private static (List<((double x, double y, double z), (double x, double y, double z), (double x, double y, double z))> tris, (double x, double y, double z) pivot) Triangles(string name)
    {
        var (j, bin) = OwnPittsModelTests.ReadGlb(Model);
        JsonElement nodes = j.GetProperty("nodes"), acc = j.GetProperty("accessors"), views = j.GetProperty("bufferViews");
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        int idx = Enumerable.Range(0, count).Single(i => nodes[i].GetProperty("name").GetString() == name);
        double[] m = OwnPittsModelTests.World(nodes, parent, idx);
        int Offset(JsonElement a) { JsonElement bv = views[a.GetProperty("bufferView").GetInt32()]; return (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0); }
        var tris = new List<((double, double, double), (double, double, double), (double, double, double))>();
        foreach (JsonElement prim in j.GetProperty("meshes")[nodes[idx].GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
        {
            JsonElement pa = acc[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
            int po = Offset(pa);
            var v = new (double, double, double)[pa.GetProperty("count").GetInt32()];
            for (int k = 0; k < v.Length; k++)
            {
                double x = BitConverter.ToSingle(bin, po + 12 * k), y = BitConverter.ToSingle(bin, po + 12 * k + 4), z = BitConverter.ToSingle(bin, po + 12 * k + 8);
                v[k] = (m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]);
            }
            JsonElement ia = acc[prim.GetProperty("indices").GetInt32()];
            int io = Offset(ia), ct = ia.GetProperty("componentType").GetInt32(), ic = ia.GetProperty("count").GetInt32();
            int Index(int k) => ct == 5125 ? (int)BitConverter.ToUInt32(bin, io + 4 * k) : ct == 5123 ? BitConverter.ToUInt16(bin, io + 2 * k) : bin[io + k];
            for (int k = 0; k + 2 < ic; k += 3) tris.Add((v[Index(k)], v[Index(k + 1)], v[Index(k + 2)]));
        }
        Assert.NotEmpty(tris);
        return (tris, (m[12], m[13], m[14]));
    }
}
