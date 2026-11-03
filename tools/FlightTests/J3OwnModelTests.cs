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
        RigNode mag = rig.Find("MagSwitch_Knob")!;
        Assert.Equal((0.0, 0.0, -1.0), (Math.Round(mag.Ax, 4), Math.Round(mag.Ay, 4), Math.Round(mag.Az, 4)));
        Assert.Equal(0.2988, mag.Pz, 3);                        // 1.2 mm inboard of the root rib at 0.300 m
        Assert.Equal(0.7085, mag.Py, 3);                        // up in the wing well, above the left window
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
