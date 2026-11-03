using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Our own J-3 on wheels and on floats (Ty 2026-11-02: "get the eye location fixed on the j3 and add it to quest too
/// both on wheels and floats"). The game now places j3-cub-like-own.glb / j3-cub-floats-like-own.glb and drives them
/// from docs/models/j3-cub-own-rig.json / j3-cub-floats-own-rig.json, which tools/own_models/j3_rig_json.py reads out
/// of the GLBs' node extras. These hold those tables to what the sim can drive and to the type certificate, with the
/// travels typed in by hand from TCDS A-691 (Item 4) and docs/models/j3-cub-own-RIG.md, not read back from the file.
/// </summary>
public class J3OwnModelTests
{
    private static ModelRig Rig(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", file);
        Assert.True(File.Exists(path), $"the J-3's rig table should be beside the test data at {path}");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var nodes = new List<RigNode>();
        foreach (JsonElement row in doc.RootElement.GetProperty("rig").EnumerateArray())
        {
            var mp = new List<double>(); var mv = new List<double>();
            foreach (JsonElement pt in row.GetProperty("map").EnumerateArray()) { mp.Add(pt[0].GetDouble()); mv.Add(pt[1].GetDouble()); }
            JsonElement axis = row.GetProperty("axis_gltf"), pivot = row.GetProperty("pivot_gltf"), extra = row.GetProperty("extra");
            double[]? detents = null;
            if (extra.TryGetProperty("rig_detents", out JsonElement det)) detents = det.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            nodes.Add(new RigNode(
                row.GetProperty("node").GetString()!,
                row.GetProperty("parent").ValueKind == JsonValueKind.Null ? "" : row.GetProperty("parent").GetString()!,
                row.GetProperty("param").GetString()!,
                row.GetProperty("kind").GetString() == "move",
                axis[0].GetDouble(), axis[1].GetDouble(), axis[2].GetDouble(),
                pivot[0].GetDouble(), pivot[1].GetDouble(), pivot[2].GetDouble(),
                mp.ToArray(), mv.ToArray(),
                extra.TryGetProperty("rig_map_wrap", out JsonElement w) ? w.GetDouble() : 0.0,
                extra.TryGetProperty("rig_map_extrapolate", out JsonElement ex) && ex.GetInt32() != 0,
                detents,
                row.GetProperty("model").GetDouble()));
        }
        return new ModelRig(doc.RootElement.GetProperty("model").GetString()!, nodes);
    }

    /// <summary>The table's "model" is the name AirframeModels.PlacedOwnName gives the placed GLB (&lt;id&gt;-own), which is
    /// what ConfigSync names the StreamingAssets copy and AirframeRig.Load asks for. A mismatch and the rig never binds.</summary>
    [Theory]
    [InlineData("j3-cub-own-rig.json", "j3-cub-like-own")]
    [InlineData("j3-cub-floats-own-rig.json", "j3-cub-floats-like-own")]
    public void EachTableIsNamedForTheModelTheGamePlaces(string file, string model)
    {
        Assert.Equal(model, Rig(file).Model);
    }

    /// <summary>Every rigged node asks for something the sim supplies, so nothing is bound and then stands still.</summary>
    [Theory]
    [InlineData("j3-cub-own-rig.json")]
    [InlineData("j3-cub-floats-own-rig.json")]
    public void EveryParamIsOneTheSimDrives(string file)
    {
        var inputs = new RigInputs();
        foreach (RigNode n in Rig(file).Nodes)
            Assert.True(inputs.TryGet(n.Param, out _), $"{file}: '{n.Node}' asks for '{n.Param}', which the sim does not supply");
    }

    [Theory]
    [InlineData("j3-cub-own-rig.json", false)]
    [InlineData("j3-cub-floats-own-rig.json", true)]
    public void TheSurfacesTravelAsTheTypeCertificateSays(string file, bool floats)
    {
        ModelRig rig = Rig(file);
        // TCDS A-691 Item 4: aileron 18 / 18, elevator 34 up / 29 down, rudder 30 / 30; stabilizer 2.5 LE up / 4 LE down.
        foreach (string a in new[] { "Aileron_L", "Aileron_R" })
        {
            Assert.Equal(18.0, rig.Find(a)!.Value(1), 3);
            Assert.Equal(-18.0, rig.Find(a)!.Value(-1), 3);
        }
        foreach (string e in new[] { "Elevator_L", "Elevator_R" })
        {
            Assert.Equal(34.0, rig.Find(e)!.Value(1), 3);     // stick aft = trailing edge up
            Assert.Equal(-29.0, rig.Find(e)!.Value(-1), 3);
        }
        Assert.Equal(30.0, rig.Find("Rudder")!.Value(1), 3);
        Assert.Equal(-30.0, rig.Find("Rudder")!.Value(-1), 3);
        Assert.Equal(-4.0, rig.Find("Stab")!.Value(1), 3);     // full nose-up trim = LE down 4
        Assert.Equal(2.5, rig.Find("Stab")!.Value(-1), 3);
        // The water rudder steers with the air rudder, +-25 (RIG.md), and exists only on the floats.
        if (floats) Assert.Equal(25.0, rig.Find("WaterRudder_L")!.Value(1), 3);
        else Assert.Null(rig.Find("WaterRudder_L"));
        // Both sticks, all four pedals and both throttles are there and linked by param.
        foreach (string n in new[] { "Stick_Front", "Stick_Rear", "Stick_Front_Roll", "Stick_Rear_Roll",
                                     "Pedal_Front_L", "Pedal_Front_R", "Pedal_Rear_L", "Pedal_Rear_R",
                                     "Throttle_Lever_Front", "Throttle_Lever_Rear", "Needle_Airspeed", "Needle_Tach" })
            Assert.NotNull(rig.Find(n));
    }

    /// <summary>The magneto plate reads OFF / L / R / BOTH and the sim counts 0 OFF, 1 R, 2 L, 3 BOTH: each sim position
    /// must land on its own label (detent angles from RIG.md: OFF 0, L -37, R -73, BOTH -115).</summary>
    [Fact]
    public void TheMagnetoSwitchPointsAtTheSimsPosition()
    {
        RigNode mag = Rig("j3-cub-own-rig.json").Find("MagSwitch_Knob")!;
        Assert.Equal(0.0, mag.Value(0), 3);
        Assert.Equal(-73.0, mag.Value(1), 3);   // R
        Assert.Equal(-37.0, mag.Value(2), 3);   // L
        Assert.Equal(-115.0, mag.Value(3), 3);  // BOTH
    }

    /// <summary>Ty 2026-11-02 (v16 cockpit), with the values typed in by hand from build_j3_cub.py's RIG log and
    /// docs/models/j3-cub-own-RIG.md: the mag switch sits on the LEFT root rib facing inboard (glTF -z), the trim crank
    /// moved 0.30 m aft (glTF x -0.252 -> 0.048), the rear-seat carb heat knob is gone and the fuel lever became a
    /// push-pull knob. The carb heat and fuel valve have no sim value yet, so they are listed static, by their new names,
    /// and nothing still names a node the GLB no longer has.</summary>
    [Theory]
    [InlineData("j3-cub-own-rig.json")]
    [InlineData("j3-cub-floats-own-rig.json")]
    public void TheV16CockpitIsWhereTySaid(string file)
    {
        ModelRig rig = Rig(file);
        // The mag switch moved again in v17 (Ty 2026-11-02 19:00): TheV17CockpitIsWhereTySaid holds where it is now.
        RigNode trim = rig.Find("Trim_Crank")!;
        Assert.Equal(0.048, trim.Px, 3);
        Assert.Equal(450.0, trim.Value(1), 3);
        Assert.Equal(26, rig.Nodes.Count);

        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", file);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var stat = doc.RootElement.GetProperty("static").EnumerateArray().ToDictionary(e => e.GetProperty("node").GetString()!, e => e.GetProperty("param").GetString()!);
        Assert.Equal("fuel_valve", stat["FuelValve_Knob"]);
        Assert.Equal("carb_heat", stat["CarbHeat_Lever"]);
        foreach (string gone in new[] { "CarbHeat_Knob_Rear", "FuelValve_Lever" })
        {
            Assert.False(stat.ContainsKey(gone), $"{file}: {gone} was removed in v16 and must not be listed");
            Assert.Null(rig.Find(gone));
        }
    }

    /// <summary>Ty 2026-11-02 19:00-19:07 (v17 cockpit). Values typed in by hand from build_j3_cub.py's log
    /// (MAGSWITCH v17, FUELTANK v17, RIG MagSwitch_Knob), in the GLB's glTF frame (x = drawing station - 1.817, nose at -x;
    /// y up = z + 0.135; z = to port):
    ///  * "the ignition switch is abeam the top of the front seat": the front seat back's top is at station 2.130
    ///    (glTF x 0.313; v16 had the switch at 1.743, glTF -0.074, 0.39 m further forward). Still on the LEFT root rib
    ///    (rib face at z 0.300) up in the wing well, now turned 20 deg toward the rear seat so BOTH is not hidden by the
    ///    knob from the rear eye.
    ///  * the 12 US gal nose tank (45.4 L) between the firewall (station 0.800, glTF x -1.017) and the panel's aft face
    ///    (1.233, glTF -0.584), under the dash deck and the red filler cap (cap top y 0.363), measured here from the
    ///    GLB's own triangles (rays along x through the shell), not from anything the build wrote down.
    ///    Band 41.6..49.2 L = 12 +- 1 US gal: no drawing of the tank was found (AOPA / Univair give only "12 gal"),
    ///    so the drum is sized from the room and the photos; 1 gal is about 1.5 cm on its height, a sizing slip this
    ///    would still catch, and the stiffening beads stand 3.5 mm proud of the drawn outline (about +0.5 L).
    ///  * the clear drain hose runs from the sump (just under the tank) into the floorboards (floor top at station
    ///    0.950: z -0.532, glTF y -0.397).</summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void TheV17CockpitIsWhereTySaid(string model)
    {
        RigNode mag = Rig(model == "j3-cub-like" ? "j3-cub-own-rig.json" : "j3-cub-floats-own-rig.json").Find("MagSwitch_Knob")!;
        Assert.Equal(0.313, mag.Px, 3);                         // abeam the front seat back's top (station 2.130)
        Assert.Equal(0.7122, mag.Py, 3);                        // up in the wing well, above the left window
        Assert.InRange(mag.Pz, 0.26, 0.30);                     // on the left root rib (face at 0.300), plate stands out 0..3.6 cm
        Assert.Equal((0.342, 0.0, -0.940), (Math.Round(mag.Ax, 3), Math.Round(mag.Ay, 3), Math.Round(mag.Az, 3)));   // inboard, 20 deg aft
        Assert.Equal(26, Rig(model == "j3-cub-like" ? "j3-cub-own-rig.json" : "j3-cub-floats-own-rig.json").Nodes.Count);

        string glb = Path.Combine(OwnPittsModelTests.Models, model, model + "-own.glb");
        var (tank, tankMat) = Triangles(glb, "Fuel_Tank", "Fuel_tank_black");
        var (hose, hoseMat) = Triangles(glb, "Fuel_Drain_Hose", null);
        double minX = tank.Min(t => t.Min(p => p.x)), maxX = tank.Max(t => t.Max(p => p.x));
        double minY = tank.Min(t => t.Min(p => p.y)), maxY = tank.Max(t => t.Max(p => p.y));
        Assert.InRange(minX, -1.017, -0.95);                    // aft of the firewall, close to it
        Assert.InRange(maxX, -0.75, -0.584 - 0.08);             // ahead of the panel's aft face, room for the gauge cans
        Assert.True(maxY < 0.363, $"{model}: tank top {maxY:F3} must sit under the filler cap");
        double litres = VolumeAlongX(tank) * 1000.0;
        Assert.InRange(litres, 41.6, 49.2);

        double hoseTop = hose.Max(t => t.Max(p => p.y)), hoseBottom = hose.Min(t => t.Min(p => p.y));
        Assert.InRange(hoseTop, minY - 0.03, minY);             // starts at the sump, just under the tank
        Assert.True(hoseBottom <= -0.397, $"{model}: the drain hose ends at {hoseBottom:F3}, above the floor (-0.397)");
        Assert.Equal("BLEND", hoseMat.GetProperty("alphaMode").GetString());   // clear
        Assert.True(hoseMat.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[3].GetDouble() < 0.9);
        Assert.False(tankMat.TryGetProperty("alphaMode", out _));
    }

    /// <summary>One node's triangles, from one primitive (by material name) or all of them. The J-3's static parts are
    /// exported with their transforms applied, so the node itself must be the identity.</summary>
    private static (List<(double x, double y, double z)[]> tris, JsonElement material) Triangles(string path, string node, string? material)
    {
        var (j, bin) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nd = j.GetProperty("nodes").EnumerateArray().Single(n => n.TryGetProperty("name", out JsonElement nm) && nm.GetString() == node);
        Assert.False(nd.TryGetProperty("translation", out _) || nd.TryGetProperty("rotation", out _) || nd.TryGetProperty("matrix", out _), node + " should carry no transform");
        var tris = new List<(double, double, double)[]>(); JsonElement mat = default;
        foreach (JsonElement prim in j.GetProperty("meshes")[nd.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
        {
            JsonElement m = j.GetProperty("materials")[prim.GetProperty("material").GetInt32()];
            if (material != null && m.GetProperty("name").GetString() != material) continue;
            mat = m;
            var pos = Accessor(j, bin, prim.GetProperty("attributes").GetProperty("POSITION").GetInt32());
            var idx = Accessor(j, bin, prim.GetProperty("indices").GetInt32());
            for (int k = 0; k + 2 < idx.Count; k += 3)
                tris.Add(new[] { pos[(int)idx[k][0]], pos[(int)idx[k + 1][0]], pos[(int)idx[k + 2][0]] }.Select(p => (p[0], p[1], p[2])).ToArray());
        }
        Assert.True(tris.Count > 0, $"{node}: no triangles");
        return (tris, mat);
    }

    private static List<double[]> Accessor(JsonElement j, byte[] bin, int i)
    {
        JsonElement a = j.GetProperty("accessors")[i], bv = j.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
        int o = (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        int n = a.GetProperty("type").GetString() == "VEC3" ? 3 : 1, ct = a.GetProperty("componentType").GetInt32();
        int size = ct == 5126 || ct == 5125 ? 4 : ct == 5123 ? 2 : 1;
        int stride = bv.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : n * size;
        var res = new List<double[]>();
        for (int k = 0; k < a.GetProperty("count").GetInt32(); k++)
        {
            var v = new double[n];
            for (int c = 0; c < n; c++)
            {
                int at = o + k * stride + c * size;
                v[c] = ct == 5126 ? BitConverter.ToSingle(bin, at) : ct == 5125 ? BitConverter.ToUInt32(bin, at) : ct == 5123 ? BitConverter.ToUInt16(bin, at) : bin[at];
            }
            res.Add(v);
        }
        return res;
    }

    /// <summary>Enclosed volume, by casting rays along x on a 4 mm grid in (y, z) and pairing the crossings in order.
    /// The grid is offset by an odd fraction so no ray runs exactly along a shared edge; rays with an odd number of
    /// crossings (grazing an edge) are left out and must be rare.</summary>
    private static double VolumeAlongX(List<(double x, double y, double z)[]> tris)
    {
        const double h = 0.004;
        double y0 = tris.Min(t => t.Min(p => p.y)), y1 = tris.Max(t => t.Max(p => p.y));
        double z0 = tris.Min(t => t.Min(p => p.z)), z1 = tris.Max(t => t.Max(p => p.z));
        double vol = 0; int odd = 0, rays = 0;
        for (double y = y0 + 0.000613; y < y1; y += h)
            for (double z = z0 + 0.001377; z < z1; z += h)
            {
                var xs = new List<double>();
                foreach (var t in tris)
                {
                    var (a, b, c) = (t[0], t[1], t[2]);
                    double d = (b.y - a.y) * (c.z - a.z) - (c.y - a.y) * (b.z - a.z);
                    if (Math.Abs(d) < 1e-14) continue;
                    double u = ((y - a.y) * (c.z - a.z) - (c.y - a.y) * (z - a.z)) / d, v = ((b.y - a.y) * (z - a.z) - (y - a.y) * (b.z - a.z)) / d;
                    if (u < 0 || v < 0 || u + v > 1) continue;
                    xs.Add(a.x + u * (b.x - a.x) + v * (c.x - a.x));
                }
                if (xs.Count == 0) continue;
                rays++;
                if (xs.Count % 2 == 1) { odd++; continue; }
                xs.Sort();
                for (int k = 0; k < xs.Count; k += 2) vol += (xs[k + 1] - xs[k]) * h * h;
            }
        Assert.True(odd < rays / 50, $"{odd} of {rays} rays crossed the shell an odd number of times");
        return vol;
    }

    /// <summary>The J-3's elevator goes further up than down (config maxDeflNegRad 0.5934 = 34 deg, maxDeflRad 0.5061 = 29),
    /// so the stick-aft side is normalised by the UP stop: full up draws 34, half up 17, full down 29.</summary>
    [Fact]
    public void AnAsymmetricElevatorIsDrawnAtItsOwnDeflection()
    {
        ControlAxisConfig ele = new() { MaxDeflRad = 0.5061, MaxDeflNegRad = 0.5934 };
        RigNode e = Rig("j3-cub-own-rig.json").Find("Elevator_L")!;
        double Drawn(double deflRad) => e.Value(RigInputs.FromDeflections(0, deflRad, 0, 0, 1, ele.MaxDeflRad, 1, ele.LimitFor(-1.0)).Elevator);
        Assert.Equal(34.0, Drawn(-0.5934), 1);                  // full up (trailing edge up is negative in the sim)
        Assert.Equal(17.0, Drawn(-17.0 * Math.PI / 180), 1);
        Assert.Equal(-29.0, Drawn(0.5061), 1);                  // full down
        Assert.Equal(-14.5, Drawn(14.5 * Math.PI / 180), 1);
        // A symmetric type is unchanged by the new argument.
        Assert.Equal(RigInputs.FromDeflections(0, -0.2, 0, 0, 1, 0.4, 1).Elevator,
                     RigInputs.FromDeflections(0, -0.2, 0, 0, 1, 0.4, 1, 0.0).Elevator, 12);
    }
}
