using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Engine;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 (Great Lakes): "remove the ignition switch and add TWO MAG switches on the switch panel", wired to the
/// engine. Each toggle is one magneto (Aircraft.MagLeft / MagRight, Core Magnetos): one off is the mag drop, both off is a
/// dead engine with a windmilling prop, and either back on fires it again. Expected numbers are literals: 0.95 is the
/// stated single-mag factor (Magnetos.SingleMagPowerFactor, an estimate), the rest follow from it.
/// </summary>
[Collection("WorldTerrainActive")]
public class MagnetoSwitchTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public MagnetoSwitchTests(ITestOutputHelper o) { _out = o; Calm(); }
    public void Dispose() => Calm();

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private static Aircraft GreatLakesFlying() =>
        new(AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "great-lakes-like.json")),
            new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(50, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    [Fact]
    public void BothMagsAreFullPowerOneIsTheMagDropNoneIsNoSpark()
    {
        Assert.Equal(1.0, Magnetos.PowerFactor(true, true));
        Assert.Equal(0.95, Magnetos.PowerFactor(true, false));
        Assert.Equal(0.95, Magnetos.PowerFactor(false, true));
        Assert.Equal(0.0, Magnetos.PowerFactor(false, false));
        Assert.Equal(3.0, Magnetos.KeyPosition(true, true));
        Assert.Equal(2.0, Magnetos.KeyPosition(true, false));
        Assert.Equal(1.0, Magnetos.KeyPosition(false, true));
        Assert.Equal(0.0, Magnetos.KeyPosition(false, false));
    }

    [Fact]
    public void TheGreatLakesFliesOnBothMagsAsItAlwaysHas()
    {
        var ac = GreatLakesFlying();
        Assert.True(ac.MagLeft && ac.MagRight);
        new SimLoop(ac).RunFor(1.0, new ControlInputs(0, 0, 0, -0.5));
        Assert.True(ac.EngineRunning);
        var rig = new RigInputs();
        Assert.True(RigInputs.Parked().TryGet("mag_left", out double l) && l == 1.0);
        Assert.True(RigInputs.Parked().TryGet("mag_right", out double r) && r == 1.0);
        Assert.True(rig.TryGet("mag_left", out _));
    }

    [Fact]
    public void OneMagOffTakesFivePercentBothOffStopsTheEngineAndOneBackOnRestartsIt()
    {
        var ac = GreatLakesFlying();
        var sim = new SimLoop(ac);
        var cruise = new ControlInputs(0, 0, 0, -0.5);
        sim.RunFor(1.0, cruise);
        double both = ac.ShaftPower01;
        Assert.True(both > 0.3, $"shaft power {both}");

        ac.MagLeft = false;
        sim.RunFor(0.2, cruise);
        double right = ac.ShaftPower01;
        _out.WriteLine($"both {both:F4}  right only {right:F4}  ratio {right / both:F4}");
        Assert.True(ac.EngineRunning);
        Assert.Equal(0.95, right / both, 3);

        ac.MagRight = false;
        sim.RunFor(0.2, cruise);
        _out.WriteLine($"both off: running {ac.EngineRunning} rpm {ac.EngineRpm:F0} windmill {ac.WindmillRpm:F0} fuel flow {ac.FuelFlowKgPerSec}");
        Assert.False(ac.EngineRunning);
        Assert.Equal(0.0, ac.ShaftPower01);
        Assert.Equal(0.0, ac.FuelFlowKgPerSec);
        Assert.Equal(ac.WindmillRpm, ac.EngineRpm);
        Assert.True(ac.WindmillRpm > 0.0, "a dead engine's prop windmills in flight");
        Assert.False(ac.EngineStarved, "no spark is not want of fuel");

        ac.MagLeft = true;
        sim.RunFor(0.2, cruise);
        Assert.True(ac.EngineRunning);
        Assert.Equal(0.95, ac.ShaftPower01 / both, 3);
    }
}
