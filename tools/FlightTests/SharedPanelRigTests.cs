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
        Assert.True(count >= 30, $"only {count} instrument nodes in the rig");
        Assert.Empty(dead);
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
