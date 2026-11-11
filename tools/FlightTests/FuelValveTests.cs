using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>The Extra's fuel shut-off (Ty 2026-11-04 19:06: the Great Lakes' type of valve, a lever in each cockpit on
/// one shaft, and it "actually shuts off fuel"). Steps an aeroplane, so it sits in the WorldTerrainActive collection.</summary>
[Collection("WorldTerrainActive")]
public class FuelValveTests
{
    private const double Dt = 1.0 / 200.0;

    private static void Fly(Aircraft ac, double seconds)
    {
        var loop = new SimLoop(ac, Dt);
        for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) loop.RunFor(Dt, new ControlInputs(0, 0, 0, -1.0));
    }

    [Fact]
    public void Shut_valve_stops_the_engine_after_the_lines_run_dry_and_open_relights_it()
    {
        WorldTerrain? oldTerrain = WorldTerrain.Active; double? oldWater = FloatHydro.FlatWaterOverride;
        try
        {
            WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = null;
            var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "extra-300-like.json"));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero),
                                  ControlDeflections.Neutral);
            ac.Load.FillFuel(); ac.ApplyLoad();
            Assert.True(ac.FuelValveOpen);                          // a new aeroplane flies with the valve ON
            Fly(ac, 0.2);
            Assert.True(ac.EngineRpm > 2600, $"not at full power: {ac.EngineRpm:F0} rpm");

            ac.FuelValveOpen = false;
            Fly(ac, 2.0);                                           // still running on the fuel in the lines (4 s)
            Assert.True(ac.EngineRunning);
            Assert.True(ac.FuelFlowKgPerSec > 0.0);
            Fly(ac, 2.5);
            // Dead for want of fuel, like a cut-off mixture (AircraftEngine): not damage, so EngineStopped stays false and
            // the prop windmills.
            Assert.False(ac.EngineRunning, "the engine still runs 4.5 s after the valve was shut");
            Assert.True(ac.FuelStarved);
            Assert.True(ac.EngineStarved);
            Assert.False(ac.EngineStopped);
            Assert.Equal(0.0, ac.FuelFlowKgPerSec, 12);
            Assert.Equal(0.0, ac.ShaftPower01, 12);                 // full throttle, no power
            // ON ITS WAY DOWN TO THE WINDMILLING BALANCE, NOT ON IT. The Extra integrates its governed shaft in normal
            // flight since 2026-11-11, so when the fuel runs out the propeller carries the rpm it had and decays
            // against cranking torque — it no longer jumps to the closed-form windmilling figure the instant the
            // engine quits. Both are still checked: the balance itself (the model) and the needle (above it, and
            // already below the governed rpm).
            Assert.InRange(ac.WindmillRpm, 800.0, 1800.0);
            Assert.True(ac.EngineRpm > ac.WindmillRpm,
                        $"2.5 s after it quit the needle should still be above the windmilling balance ({ac.EngineRpm:F0} vs {ac.WindmillRpm:F0})");
            Assert.True(ac.EngineRpm < 2600, $"a dead engine at {ac.EngineRpm:F0} rpm");

            ac.FuelValveOpen = true;
            Fly(ac, 1.0);
            Assert.False(ac.EngineRunning, "relit before the fuel could reach the injectors (2 s)");
            Fly(ac, 1.5);
            Assert.True(ac.EngineRunning);
            Assert.False(ac.FuelStarved);
            // AND THEN IT HAS TO SPOOL UP, WHICH TAKES LONGER THAN IT USED TO, because the blade has further to come
            // back: the Extra's coarse stop moved from 31.12 deg to 34.42 on 2026-11-11 (re-derived at Vd at 10,000 ft
            // density altitude, BladeElement.GovernorStops), and with no oil pressure the dead engine's governor had
            // driven the blades all the way onto it. The head can move the blade 12 deg/s (GovernorRateDegPerSec), so
            // the extra 3.3 deg is a further quarter of a second of blade on top of a shaft that was windmilling
            // slower to begin with. Measured: 2,436 rpm at 1.5 s after it caught, 2,696 at 2.5 s. The pin is now at
            // 2.5 s rather than looser at 1.5, so what it still measures is that full power COMES BACK.
            Fly(ac, 1.0);
            Assert.True(ac.EngineRpm > 2600, $"did not come back to full power: {ac.EngineRpm:F0} rpm");
        }
        finally { WorldTerrain.Active = oldTerrain; FloatHydro.FlatWaterOverride = oldWater; }
    }

    [Fact]
    public void The_valve_param_is_one_value_for_both_cockpits_levers()
    {
        RigInputs r = RigInputs.Parked();
        Assert.True(r.TryGet("fuel_valve", out double v)); Assert.Equal(1.0, v);
        r.FuelValve = 0.0;
        Assert.True(r.TryGet("fuel_valve", out v)); Assert.Equal(0.0, v);
        Assert.True(r.TryGet("alternator", out v)); Assert.Equal(1.0, v);
    }
}
