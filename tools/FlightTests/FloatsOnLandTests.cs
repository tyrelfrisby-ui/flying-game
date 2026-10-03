using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-10-03: a floatplane put down on LAND must stop like a skid, not roll like wheels. Float keels slide
/// with kinetic friction (WorldTerrain.KeelSlidingCoefficient: pavement 0.55, grass 0.35). Trace: touch down level at
/// ~50 kt, idle, and measure the slide.</summary>
public class FloatsOnLandTests
{
    private readonly ITestOutputHelper _out;
    public FloatsOnLandTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("pa18-floats-like", 22.0)]
    [InlineData("dhc2-beaver-floats-like", 26.0)]
    public void SlidesToAStopOnPavement(string id, double v0)
    {
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double keel = 0; foreach (GearConfig g in cfg.Gear) keel = Math.Max(keel, g.Pos[2]);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -(keel - 0.02)), new Quat(0, 0, 0, 1), new Vec3(v0, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        double t = 0;
        for (; t < 60 && ac.State.Velocity.Length > 0.3; t += 0.1)
        {
            sim.RunFor(0.1, new ControlInputs(0, 0, 0, 1.0));   // idle, hands off
            if ((int)(t * 10) % 10 == 0) _out.WriteLine($"t={t,4:F1} V {ac.State.Velocity.Length,5:F1} m/s  x {ac.State.Position.X,6:F1} m");
        }
        double dist = ac.State.Position.X;
        _out.WriteLine($"{id}: touched down at {v0 * 1.944:F0} kt, stopped after {dist:F0} m in {t:F1} s");
        Assert.True(ac.State.Velocity.Length < 0.5, "should slide to a stop");
        Assert.InRange(dist, 25, 200);   // metal keel on pavement: tens of metres, not the runway length
    }
}
