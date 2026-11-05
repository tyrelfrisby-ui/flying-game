using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
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
        // Ty's rear panel (2026-11-04, extra_cockpit_shared.py + extra_front_seat.py): ASI, altimeter (2 hands), TC (+ ball),
        // G (+ 2 tell-tales), tach, MP, the three-in-one (oil P, oil T, fuel FLOW), the ammeter and EGT/CHT = 16. The FRONT
        // seat (Ty 19:05: "just has altitude, g meter and airspeed"): ASI, altimeter (2 hands), G (+ 2 tell-tales) = 6.
        var names = doc.RootElement.GetProperty("rig").EnumerateArray().Select(n => n.GetProperty("node").GetString()!)
                       .Where(n => n.StartsWith("Needle_") || n.StartsWith("Ball_")).ToHashSet();
        foreach (string n in new[] { "Needle_ASI", "Needle_Alt100", "Needle_Alt1000", "Needle_TC", "Ball_TC", "Needle_G", "Needle_GMax",
                                     "Needle_GMin", "Needle_Tach", "Needle_MP", "Needle_OilP", "Needle_OilT", "Needle_FuelF", "Needle_Amps",
                                     "Needle_EGT", "Needle_CHT" })
            Assert.Contains(n + "_R", names);
        foreach (string n in new[] { "Needle_ASI", "Needle_Alt100", "Needle_Alt1000", "Needle_G", "Needle_GMax", "Needle_GMin" })
            Assert.Contains(n + "_F", names);
        Assert.Equal(22, names.Count);
    }

    [Theory]
    [InlineData("amps")] [InlineData("egt_f")] [InlineData("fuel_wing_gal")]
    [InlineData("fuel_akro_gal")] [InlineData("fuel_flow_gph")] [InlineData("fuel_used_gal")]
    public void The_new_engine_params_read_back(string param)
    {
        var r = new RigInputs { Amps = 1, EgtF = 3, FuelWingGal = 4, FuelAkroGal = 5, FuelFlowGph = 6, FuelUsedGal = 7 };
        Assert.True(r.TryGet(param, out double v));
        double expected = param switch
        {
            "amps" => 1, "egt_f" => 3, "fuel_wing_gal" => 4, "fuel_akro_gal" => 5, "fuel_flow_gph" => 6, _ => 7,
        };
        Assert.Equal(expected, v);
    }
}

/// <summary>Fuel flow from the sim, on the Extra. In the WorldTerrainActive collection: it steps an aeroplane, so it must not
/// run beside a test that installs a world.</summary>
[Collection("WorldTerrainActive")]
public class ExtraFuelFlowGaugeTests
{
    /// <summary>The three-in-one's third needle is FUEL FLOW (Ty 2026-11-04 15:23), fed from the sim's own burn. The
    /// 300L POH (AEIO-540-L1B5) gives 88.2 L/h = 23.3 US gal/h at 2000 ft, 2700 rpm, full throttle; the dial's green arc
    /// is 0-35 gal/h. Engine stopped, the needle and the burn are both zero.</summary>
    [Fact]
    public void Extra_fuel_flow_reads_the_book_at_full_power_and_zero_stopped()
    {
        // Process-wide state other tests leave behind (ModelRigTests installs a WorldTerrain and keeps it): clear it
        // here, restore it in the same scope.
        WorldTerrain? oldTerrain = WorldTerrain.Active; double? oldWater = FloatHydro.FlatWaterOverride;
        try
        {
            WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = null;
            var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "extra-300-like.json"));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -610), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero),
                                  ControlDeflections.Neutral);
            ac.Load.FillFuel(); ac.ApplyLoad();
            for (int i = 0; i < 40; i++) new SimLoop(ac, 1.0 / 200.0).RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, -1.0));
            Assert.True(ac.EngineRpm > 2600, $"not at full power: {ac.EngineRpm:F0} rpm");
            double gph = RigInputs.GphFromKgPerSec(ac.FuelFlowKgPerSec);
            Assert.InRange(gph, 20.0, 25.0);

            ac.LoseComponent(AirframeComponent.Nose);              // the engine stops (prop strike)
            Assert.True(ac.EngineStopped);
            Assert.Equal(0.0, ac.FuelFlowKgPerSec, 12);
            Assert.Equal(0.0, RigInputs.GphFromKgPerSec(ac.FuelFlowKgPerSec), 12);
        }
        finally { WorldTerrain.Active = oldTerrain; FloatHydro.FlatWaterOverride = oldWater; }
    }
}
