using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>"On final" start: 300 ft on the centreline at best glide, idle, trimmed — hands off it should ride
/// its own glide path down to the aim point.</summary>
public class ApproachSpawnTests
{
    private readonly ITestOutputHelper _out;
    public ApproachSpawnTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("glider-2-33-like", 19, 27, 15, 30)]
    [InlineData("pa18-cub-like", 24, 34, 6, 13)]
    [InlineData("c172-like", 28, 40, 7, 13)]
    [InlineData("extra-300-like", 35, 55, 6, 12)]
    public void BestGlideIsPlausible(string id, double vMin, double vMax, double ldMin, double ldMax)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var g = ApproachSpawn.FindBestGlide(c, 100);
        _out.WriteLine($"{id}: best glide {g.SpeedMs:F1} m/s ({g.SpeedMs * 1.944:F0} kt), L/D {g.GlideRatio:F1}, γ {g.GammaRad * 57.3:F1}°, α {g.AlphaRad * 57.3:F1}°");
        Assert.InRange(g.SpeedMs, vMin, vMax);
        Assert.InRange(g.GlideRatio, ldMin, ldMax);
    }

    [Theory]
    [InlineData("glider-2-33-like")]
    [InlineData("pa18-cub-like")]
    public void HandsOffFromFinalRidesTheGlidePathToTheAimPoint(string id)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var t = new WorldTerrain(); WorldTerrain.Active = t; Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.SlopeLiftEnabled = false; Atmosphere.Thermals.Clear();
        var a = WorldTerrain.Airports[0];
        var (state, g, aimX) = ApproachSpawn.Compute(c, a);
        var ac = new Aircraft(c, state, new ControlDeflections(0, g.ElevatorRad, 0, 0));
        var sim = new SimLoop(ac);
        double stick = Aircraft.StickForDeflection(g.ElevatorRad, c.Controls.Elevator);
        double idle = c.Propulsion != null ? 1.0 : 0.0;   // lever +1 = idle power; glider: lever ≤ 0 = spoilers stowed
        double agl0 = -state.Position.Z - a.ElevationM, x0 = state.Position.X;
        Assert.InRange(agl0, 91, 92);
        double maxDev = 0;
        for (double tt = 0; tt < 120; tt += 0.05)
        {
            // Pilot keeps the wings level (the 2-33's spiral mode wanders off hands-off over a 2 km final); pitch is
            // left on the trim — that is what proves the trimmed best-glide state.
            var q = ac.State.Attitude;
            double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
            double ail = System.Math.Clamp(-roll * 1.5 - ac.State.Rates.X * 0.4, -0.5, 0.5);
            sim.RunFor(0.05, new ControlInputs(ail, stick, 0, idle));
            var s = ac.State;
            double agl = -s.Position.Z - a.ElevationM;
            double expected = agl0 - (s.Position.X - x0) * System.Math.Tan(g.GammaRad);
            maxDev = System.Math.Max(maxDev, System.Math.Abs(agl - expected));
            if (agl < 3) { _out.WriteLine($"{id}: touched at x = {s.Position.X - aimX:+0;-0} m from the aim point, max path deviation {maxDev:F1} m"); Assert.InRange(s.Position.X - aimX, -250, 250); Assert.True(maxDev < 25, $"path deviation {maxDev:F1} m"); WorldTerrain.Active = null; return; }
        }
        WorldTerrain.Active = null;
        Assert.Fail("never reached the runway");
    }
}
