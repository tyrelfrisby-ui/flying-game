using System.Text.Json;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The shared Extra panel (iOS/Mac and Quest, docs/SHARED-COCKPIT.md): every needle, ball and card the rig names
/// must be fed by a param the sim supplies. Until 2026-11-04 the Extra's ammeter, EGT and both fuel needles were in
/// the rig on params no code knew (amps, egt_f, fuel_wing_gal, fuel_akro_gal), so they sat at rest on both
/// platforms with only a once-per-bind warning.
/// </summary>
public class SharedPanelRigTests
{
    [Fact]
    public void Every_Extra_instrument_node_is_driven()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "ea300-own-rig.json")));
        var r = new RigInputs();
        var dead = new List<string>();
        int count = 0;
        foreach (JsonElement n in doc.RootElement.GetProperty("rig").EnumerateArray())
        {
            string node = n.GetProperty("node").GetString()!;
            if (!(node.StartsWith("Needle_") || node.StartsWith("Ball_") || node.StartsWith("Compass_"))) continue;
            count++;
            string param = n.GetProperty("param").GetString()!;
            if (!r.TryGet(param, out _)) dead.Add($"{node} ({param})");
        }
        Assert.Empty(dead);
        // Ty's panel (2026-11-04, extra_cockpit_shared.py), per seat: ASI, altimeter (2 hands), TC (+ ball), G (+ 2
        // tell-tales), tach, MP, the three-in-one (oil P, oil T, fuel P) and the ammeter = 14. Nothing else on the panel.
        var names = doc.RootElement.GetProperty("rig").EnumerateArray().Select(n => n.GetProperty("node").GetString()!)
                       .Where(n => n.StartsWith("Needle_") || n.StartsWith("Ball_")).ToHashSet();
        foreach (string sfx in new[] { "_R", "_F" })
            foreach (string n in new[] { "Needle_ASI", "Needle_Alt100", "Needle_Alt1000", "Needle_TC", "Ball_TC", "Needle_G", "Needle_GMax",
                                         "Needle_GMin", "Needle_Tach", "Needle_MP", "Needle_OilP", "Needle_OilT", "Needle_FuelP", "Needle_Amps" })
                Assert.Contains(n + sfx, names);
        Assert.Equal(28, names.Count);
    }

    [Theory]
    [InlineData("amps")] [InlineData("fuel_press_psi")] [InlineData("egt_f")] [InlineData("fuel_wing_gal")]
    [InlineData("fuel_akro_gal")] [InlineData("fuel_flow_gph")] [InlineData("fuel_used_gal")]
    public void The_new_engine_params_read_back(string param)
    {
        var r = new RigInputs { Amps = 1, FuelPressPsi = 2, EgtF = 3, FuelWingGal = 4, FuelAkroGal = 5, FuelFlowGph = 6, FuelUsedGal = 7 };
        Assert.True(r.TryGet(param, out double v));
        double expected = param switch
        {
            "amps" => 1, "fuel_press_psi" => 2, "egt_f" => 3, "fuel_wing_gal" => 4, "fuel_akro_gal" => 5, "fuel_flow_gph" => 6, _ => 7,
        };
        Assert.Equal(expected, v);
    }
}
