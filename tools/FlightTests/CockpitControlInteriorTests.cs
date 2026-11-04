using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-02 (build after 2026.1102): "the switches were outside of the great lakes on the sides". Every
/// interactive cockpit control (each root Ctl_* pivot and its Mount_* twin) of our own Great Lakes, Pitts and
/// Extra must sit ON a real instrument-panel face inside the cockpit: its pivot just behind that seat's panel
/// face plane, and a 24 x 24 mm footprint around it wholly on panel-face triangles (the panel sheet, radio /
/// transponder faceplates, the magneto dial, the Extra's lower switch subpanel), never off the panel's edge,
/// in a gauge hole or out through the fuselage side. Placement is tools/own_models/cockpit_controls_v2.py.
///
/// One exception (Ty 2026-11-02, "the Great Lakes throttle is a QUADRANT LEVER (not push-pull)"): the Great
/// Lakes prop and mixture are levers in the throttle quadrant on the left sidewall, not panel push-pulls, so
/// their Ctl_/Mount_ nodes must instead lie in that seat's quadrant box, inside the fuselage (see the quadrant
/// test below and tools/own_models/gl_quadrant_v3.py). And Ty's approved Great Lakes v7 (2026-11-03, from his own
/// photographs): only the ignition and the radio are on the GL panels; the battery / alternator / pump / light
/// toggles sit on the lid of a small console box on the left wall behind the quadrant, smoke and carb heat on its
/// face, the trim on the left wall and the fuel valve on the right wall. Those must lie ON that hardware (within
/// 30 mm of a GL_ console, quadrant, trim-run or fuel-rod mesh) and inside the cockpit walls.
///
/// The two legacy Great Lakes GLBs (-red-sunburst, -white-stripes) are excluded: no code loads them
/// (great-lakes-like-own.glb is the only Great Lakes the game uses) and their cockpit openings and panels sit
/// at different stations, while they carry the shared rig.
/// </summary>
public class CockpitControlInteriorTests
{
    private readonly ITestOutputHelper _o;
    public CockpitControlInteriorTests(ITestOutputHelper o) { _o = o; }

    /// <summary>Materials that are panel FACE: something a switch, knob or push-pull may be mounted through.</summary>
    private static readonly HashSet<string> PanelFace = new()
    {
        "Cockpit_panel", "Avionics_Face_COM", "Avionics_Face_XPDR", "Cockpit_blank_plug", "Dial_MAG",
        "P3e_Subpanel", "P3b_Panel", "P2c_Panel_White",
    };

    // model, file, rear panel face x, front panel face x (builder metres: x forward, y left, z up)
    public static IEnumerable<object[]> Cases() => new[]
    {
        // lane/claude-extra-cockpit-shared (2026-11-04) carries only the Extra's VR-cockpit GLBs onto island-nav; the
        // Great Lakes and Pitts rows (great-lakes-like-own -1.638/-0.968, the four Pitts GLBs -0.808/-0.151) and the
        // Great Lakes quadrant test live on the Quest lane with those models. Merging there: take the Quest file.
        // P3f (Ty 2026-11-03): the Extra's switches are on the P3b panel itself now (one row along its bottom edge),
        // its radio knobs on the centre radio subpanel in the same plane; the P3e subpanel 3 mm ahead is retired.
        new object[] { "extra-300-like", "extra-300-like-own", -1.175, -0.150 },
        new object[] { "extra-300-like", "extra-300-like-own-n300uy", -1.175, -0.150 },
        new object[] { "extra-300-like", "extra-300-like-own-unmarked", -1.175, -0.150 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_control_sits_on_a_panel_face_inside_the_cockpit(string model, string file, double rearFace, double frontFace)
    {
        string path = Path.Combine(OwnPittsModelTests.Models, model, file + ".glb");
        var (j, bin) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes");
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;

        var faces = PanelTriangles(j, bin, nodes, parent);
        Assert.NotEmpty(faces);
        var hardware = model == "great-lakes-like" ? HardwareBoxes(j, bin, nodes, parent, "GL_") : new List<(P lo, P hi)>();
        var failures = new List<string>();
        int controls = 0;
        for (int i = 0; i < count; i++)
        {
            string name = nodes[i].GetProperty("name").GetString()!;
            if (!name.StartsWith("Ctl_") && !name.StartsWith("Mount_")) continue;
            controls++;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            double x = -m[12], y = m[14], z = m[13];                       // glTF -> builder
            double face = name.EndsWith("_Front") ? frontFace : rearFace;
            if (model == "great-lakes-like" && (name.Contains("_Prop") || name.Contains("_Mixture")))
            {
                if (!InGreatLakesQuadrant(x, y, z, face)) failures.Add($"{name}: ({x:F3},{y:F3},{z:F3}) not in the left-sidewall quadrant");
                continue;
            }
            if (model == "great-lakes-like" && x < face - 0.008)
            {
                bool on = hardware.Any(b => x > b.lo.x - 0.03 && x < b.hi.x + 0.03 && y > b.lo.y - 0.03 && y < b.hi.y + 0.03 && z > b.lo.z - 0.03 && z < b.hi.z + 0.03);
                if (!on || Math.Abs(y) > 0.345) failures.Add($"{name}: ({x:F3},{y:F3},{z:F3}) is neither on the panel nor on the v7 console / wall hardware");
                continue;
            }
            if (x < face - 0.008 || x > face + 0.003) { failures.Add($"{name}: x {x:F3} not at panel face {face:F3}"); continue; }
            var local = faces.Where(t => Math.Min(t.a.x, Math.Min(t.b.x, t.c.x)) < face + 0.012
                                      && Math.Max(t.a.x, Math.Max(t.b.x, t.c.x)) > face - 0.03).ToList();
            foreach (double dy in new[] { -0.012, 0, 0.012 })
                foreach (double dz in new[] { -0.012, 0, 0.012 })
                    if (!local.Any(t => Inside(t, y + dy, z + dz)))
                    {
                        failures.Add($"{name}: ({y:F3},{z:F3}) footprint point ({y + dy:F3},{z + dz:F3}) is off the panel face");
                        goto next;
                    }
            next:;
        }
        _o.WriteLine($"{file}: {controls} controls, {failures.Count} off-panel");
        foreach (string f in failures.Take(20)) _o.WriteLine("  " + f);
        Assert.Equal(model == "great-lakes-like" ? 90 : 88, controls);   // GL v7: + an alternator switch per seat (no Mount_ twin)
        Assert.Empty(failures);
    }

    /// <summary>The Great Lakes quadrant box for a seat, builder metres: on the LEFT sidewall (y .24 to .35, inboard
    /// of the longerons at ~.36-.40), from 0.20 m aft of the panel face to the face, at panel-bottom height.</summary>
    private static bool InGreatLakesQuadrant(double x, double y, double z, double face) =>
        x > face - 0.20 && x < face - 0.02 && y > 0.24 && y < 0.35 && z > -0.20 && z < 0.08;

    private readonly record struct P(double x, double y, double z);
    private readonly record struct Tri(P a, P b, P c);

    private static bool Inside(Tri t, double y, double z)
    {
        double d1 = (t.b.y - t.a.y) * (z - t.a.z) - (t.b.z - t.a.z) * (y - t.a.y);
        double d2 = (t.c.y - t.b.y) * (z - t.b.z) - (t.c.z - t.b.z) * (y - t.b.y);
        double d3 = (t.a.y - t.c.y) * (z - t.c.z) - (t.a.z - t.c.z) * (y - t.c.y);
        if (Math.Abs(d1) + Math.Abs(d2) + Math.Abs(d3) == 0) return false;
        return (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
    }

    /// <summary>Every triangle, in builder metres, of every primitive whose material is a panel face.</summary>
    private static List<Tri> PanelTriangles(JsonElement j, byte[] bin, JsonElement nodes, int[] parent)
    {
        JsonElement acc = j.GetProperty("accessors"), views = j.GetProperty("bufferViews"), meshes = j.GetProperty("meshes"), mats = j.GetProperty("materials");
        var res = new List<Tri>();
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (!nodes[i].TryGetProperty("mesh", out JsonElement mi)) continue;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            foreach (JsonElement prim in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                if (!prim.TryGetProperty("material", out JsonElement mat) || !PanelFace.Contains(mats[mat.GetInt32()].GetProperty("name").GetString()!)) continue;
                var pos = Read(acc, views, bin, prim.GetProperty("attributes").GetProperty("POSITION").GetInt32())
                    .Select(v => { double x = v[0], y = v[1], z = v[2];
                        return new P(-(m[0] * x + m[4] * y + m[8] * z + m[12]), m[2] * x + m[6] * y + m[10] * z + m[14], m[1] * x + m[5] * y + m[9] * z + m[13]); }).ToArray();
                int[] ids = prim.TryGetProperty("indices", out JsonElement ix)
                    ? Read(acc, views, bin, ix.GetInt32()).Select(v => (int)v[0]).ToArray()
                    : Enumerable.Range(0, pos.Length).ToArray();
                for (int k = 0; k + 2 < ids.Length; k += 3) res.Add(new Tri(pos[ids[k]], pos[ids[k + 1]], pos[ids[k + 2]]));
            }
        }
        return res;
    }

    /// <summary>World axis-aligned boxes, builder metres, of every mesh node whose name starts with the prefix.</summary>
    private static List<(P lo, P hi)> HardwareBoxes(JsonElement j, byte[] bin, JsonElement nodes, int[] parent, string prefix)
    {
        JsonElement acc = j.GetProperty("accessors"), views = j.GetProperty("bufferViews"), meshes = j.GetProperty("meshes");
        var res = new List<(P, P)>();
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (!nodes[i].GetProperty("name").GetString()!.StartsWith(prefix) || !nodes[i].TryGetProperty("mesh", out JsonElement mi)) continue;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            var pts = meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray()
                .SelectMany(prim => Read(acc, views, bin, prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()))
                .Select(v => new P(-(m[0] * v[0] + m[4] * v[1] + m[8] * v[2] + m[12]), m[2] * v[0] + m[6] * v[1] + m[10] * v[2] + m[14], m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13])).ToList();
            res.Add((new P(pts.Min(q => q.x), pts.Min(q => q.y), pts.Min(q => q.z)), new P(pts.Max(q => q.x), pts.Max(q => q.y), pts.Max(q => q.z))));
        }
        return res;
    }

    private static List<double[]> Read(JsonElement acc, JsonElement views, byte[] bin, int index)
    {
        JsonElement a = acc[index], bv = views[a.GetProperty("bufferView").GetInt32()];
        int comp = a.GetProperty("componentType").GetInt32();
        int dim = a.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, _ => 4 };
        int size = comp switch { 5126 or 5125 => 4, 5123 => 2, _ => 1 };
        int o = (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        int stride = bv.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : dim * size;
        var res = new List<double[]>();
        for (int k = 0; k < a.GetProperty("count").GetInt32(); k++)
        {
            var v = new double[dim];
            for (int d = 0; d < dim; d++)
            {
                int at = o + k * stride + d * size;
                v[d] = comp switch { 5126 => BitConverter.ToSingle(bin, at), 5125 => BitConverter.ToUInt32(bin, at), 5123 => BitConverter.ToUInt16(bin, at), _ => bin[at] };
            }
            res.Add(v);
        }
        return res;
    }
}
