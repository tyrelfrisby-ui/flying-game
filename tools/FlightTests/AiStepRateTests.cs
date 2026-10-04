using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The game flies its AI traffic at <see cref="SimLoop.AiFixedDtSec"/> (50 Hz) to stay inside an iPad's CPU
/// (owner 2026-10-04: 3 fps for 20 s at a time with fifteen drones at 200 Hz). Each drone type must fly the same at 50 Hz as
/// at 200 Hz: the same height band and speed after three minutes of its route, and nothing diverging.</summary>
public class AiStepRateTests
{
    private readonly ITestOutputHelper _o; public AiStepRateTests(ITestOutputHelper o) { _o = o; }

    private static (double minAlt, double maxAlt, double speed, double maxRateDeg) Fly(string f, double h)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
        double v = c.SpawnIasMs > 1 ? c.SpawnIasMs : 50;
        var t = TrimSolver.SolveGliderTrim(c, v, 900);
        var att = new Quat(0, Math.Sin(t.ThetaRad / 2), 0, Math.Cos(t.ThetaRad / 2));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(2000, 4000, -1052), att, att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero));
        var p = new DronePilot(7, false, v);
        double minAlt = 1e9, maxAlt = 0, maxRate = 0;
        for (double tt = 0; tt < 180; tt += h)
        {
            new SimLoop(ac, h).RunFor(h, p.Update(ac, h));
            double alt = -ac.State.Position.Z;
            Assert.False(double.IsNaN(alt), $"{f} diverged at h={h}");
            minAlt = Math.Min(minAlt, alt); maxAlt = Math.Max(maxAlt, alt); maxRate = Math.Max(maxRate, ac.State.Rates.Length * 57.3);
        }
        return (minAlt, maxAlt, ac.State.Velocity.Length, maxRate);
    }

    [Theory]
    [InlineData("cassutt-f1-like.json")]
    [InlineData("dc3-like.json")]
    [InlineData("p51d-like.json")]
    [InlineData("target-drone-like.json")]
    public void DronesFlyTheSameAtTheAiStepRate(string f)
    {
        var fine = Fly(f, SimLoop.DefaultFixedDtSec);
        var ai = Fly(f, SimLoop.AiFixedDtSec);
        _o.WriteLine($"{f}: 200 Hz alt {fine.minAlt:F0}..{fine.maxAlt:F0} m, {fine.speed:F0} m/s, max rate {fine.maxRateDeg:F0}°/s | 50 Hz alt {ai.minAlt:F0}..{ai.maxAlt:F0} m, {ai.speed:F0} m/s, max rate {ai.maxRateDeg:F0}°/s");
        Assert.InRange(ai.minAlt - fine.minAlt, -30, 30);
        Assert.InRange(ai.maxAlt - fine.maxAlt, -30, 30);
        Assert.InRange(ai.speed - fine.speed, -5, 5);
        Assert.True(ai.maxRateDeg < fine.maxRateDeg * 1.3 + 10, "no oscillation at the coarser step");
    }
}
