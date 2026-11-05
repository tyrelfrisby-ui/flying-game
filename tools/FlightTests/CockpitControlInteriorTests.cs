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
        "Pitts_Black_Crinkle",   // the Pitts' panels since pitts_reference_visuals.py (Ty's photos, 2026-11-04)
    };

    // model, file, rear panel face x, front panel face x (builder metres: x forward, y left, z up)
    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "great-lakes-like", "great-lakes-like-own", -1.638, -0.968 },
        new object[] { "pitts-s2b-like", "pitts-s2b-like-own", -0.808, -0.151 },
        new object[] { "pitts-s2b-like", "pitts-s2b-like-own-checker-unmarked", -0.808, -0.151 },
        new object[] { "pitts-s2b-like", "pitts-s2b-like-own-factory-unmarked", -0.808, -0.151 },
        new object[] { "pitts-s2b-like", "pitts-s2b-like-own-n1191", -0.808, -0.151 },
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
            if (nodes[i].TryGetProperty("extras", out JsonElement ex) && ex.TryGetProperty("retiredPittsPanelV4", out _)) continue;
            controls++;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            double x = -m[12], y = m[14], z = m[13];                       // glTF -> builder
            double face = name.EndsWith("_Front") ? frontFace : rearFace;
            if (model == "great-lakes-like" && (name.Contains("_Prop") || name.Contains("_Mixture")))
            {
                if (!InGreatLakesQuadrant(x, y, z, face)) failures.Add($"{name}: ({x:F3},{y:F3},{z:F3}) not in the left-sidewall quadrant");
                continue;
            }
            // Ty 2026-11-04 19:06-19:13 (extra_front_seat.py): the Extra's verniers are on the LEFT wall beside the rear
            // throttle, outboard of it, and its fuel valve is the Great Lakes' lever on the RIGHT wall, one per cockpit.
            if (model == "extra-300-like" && (name.Contains("_Prop") || name.Contains("_Mixture")))
            {
                if (!(x > -1.33 && x < -1.25 && y > 0.30 && y < 0.35 && z > 0.12 && z < 0.24)) failures.Add($"{name}: ({x:F3},{y:F3},{z:F3}) not beside the rear throttle");
                continue;
            }
            if (model == "extra-300-like" && name.Contains("FuelSelector"))
            {
                double vx = name.EndsWith("_Front") ? -0.50 : -1.40;
                if (!(Math.Abs(x - vx) < 0.03 && y > -0.30 && y < -0.26 && z > 0.0 && z < 0.08)) failures.Add($"{name}: ({x:F3},{y:F3},{z:F3}) not on the right wall");
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
        // GL v7: + an alternator switch per seat (no Mount_ twin). GL panel v2 (Ty 2026-11-05, gl_panel_v2.py): the ignition
        // rotary and its Mount_ twin gone from each seat = 86. Pitts v4 (Ty 2026-11-04): six rear controls and their
        // mounts, plus the ALTERNATOR toggle (pitts_alternator_live.py, the master's copy: no Mount_ twin) = 13. Extra
        // (Ty 2026-11-04 19:05-19:14): the rear seat's six labelled switches, the COM/XPDR heads' 12 knobs, two verniers and
        // the fuel valve (19 controls); the front seat only its fuel-valve lever. Each with its Mount_ = 40.
        Assert.Equal(model == "great-lakes-like" ? 86 : model == "pitts-s2b-like" ? 13 : model == "extra-300-like" ? 40 : 88, controls);
        Assert.Empty(failures);
    }

    /// <summary>The Great Lakes quadrant box for a seat, builder metres: on the LEFT sidewall (y .24 to .35, inboard
    /// of the longerons at ~.36-.40), from 0.20 m aft of the panel face to the face, at panel-bottom height.</summary>
    private static bool InGreatLakesQuadrant(double x, double y, double z, double face) =>
        x > face - 0.20 && x < face - 0.02 && y > 0.24 && y < 0.35 && z > -0.20 && z < 0.08;

    /// <summary>
    /// Ty 2026-11-02: "the Great Lakes throttle is a QUADRANT LEVER (not push-pull)", "hand control did not work on
    /// the Great Lakes stick or throttle". Each cockpit has a throttle lever Throttle_R / Throttle_F on the left
    /// sidewall, on the 'throttle' input as a lever (rot about the lateral axis, 50 deg), carrying the
    /// Grip_Throttle_Rear / _Front empty VrCockpitHands needs for the grabbed LeverArc; full throttle is FORWARD of
    /// idle; prop and mixture are the blue and red levers beside it, also rot levers.
    /// </summary>
    [Fact]
    public void Great_Lakes_throttle_is_a_grabbable_quadrant_lever_on_the_left_sidewall()
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(Path.Combine(OwnPittsModelTests.Models, "great-lakes-like", "great-lakes-like-own.glb"));
        JsonElement nodes = j.GetProperty("nodes");
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        var index = new Dictionary<string, int>();
        for (int i = 0; i < count; i++) { parent[i] = -1; index[nodes[i].GetProperty("name").GetString()!] = i; }
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        (double x, double y, double z) B(string n) { double[] m = OwnPittsModelTests.World(nodes, parent, index[n]); return (-m[12], m[14], m[13]); }

        using var rigDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "great-lakes-own-rig.json")));
        var rig = new Dictionary<string, JsonElement>();
        foreach (JsonElement r in rigDoc.RootElement.GetProperty("rig").EnumerateArray()) rig.TryAdd(r.GetProperty("node").GetString()!, r);

        foreach (var (brief, full, ctl, face) in new[] { ("R", "Rear", "", -1.638), ("F", "Front", "_Front", -0.968) })
        {
            string thr = "Throttle_" + brief, grip = "Grip_Throttle_" + full;
            Assert.True(index.ContainsKey(thr) && index.ContainsKey(grip), $"{thr} / {grip} missing");
            Assert.Equal(index[thr], parent[index[grip]]);
            var g = B(grip);
            Assert.True(InGreatLakesQuadrant(g.x, g.y, g.z, face), $"{grip} at ({g.x:F3},{g.y:F3},{g.z:F3}) is not on the left sidewall quadrant");
            var pv = B(thr);
            JsonElement row = rig[thr];
            Assert.Equal("throttle", row.GetProperty("param").GetString());
            Assert.Equal("rot", row.GetProperty("kind").GetString());
            double[] axis = row.GetProperty("axis_gltf").EnumerateArray().Select(a => a.GetDouble()).ToArray();
            Assert.Equal(new[] { 0.0, 0.0, 1.0 }, axis);                    // glTF +z = lateral: the lever swings fore-aft
            var map = row.GetProperty("map").EnumerateArray().Select(e => (v: e[0].GetDouble(), deg: e[1].GetDouble())).ToList();
            double sweep = map.Max(e => e.deg) - map.Min(e => e.deg);
            Assert.InRange(sweep, 40, 60);
            // Rotating the knob by +sweep about glTF +z (builder: X forward, Z up) must carry it FORWARD.
            double rx = g.x - pv.x, rz = g.z - pv.z, a = sweep * Math.PI / 180;
            double fx = pv.x + rx * Math.Cos(a) + rz * Math.Sin(a);
            Assert.True(fx > g.x + 0.05, $"{thr}: full throttle knob x {fx:F3} is not forward of idle {g.x:F3}");
            foreach (string lever in new[] { "Prop", "Mixture" })
            {
                string n = "Ctl_PushPull_" + lever + ctl;
                Assert.Equal("rot", rig[n].GetProperty("kind").GetString());
                Assert.Equal(lever.ToLowerInvariant(), rig[n].GetProperty("param").GetString());
                var q = B("Grip_" + lever + ctl);
                Assert.True(InGreatLakesQuadrant(q.x, q.y, q.z, face), $"Grip_{lever}{ctl} not in the quadrant");
                Assert.True(q.y < g.y, $"{lever} is not to the right of the throttle (Ty's order: throttle, prop, mixture)");
            }
        }
    }

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
