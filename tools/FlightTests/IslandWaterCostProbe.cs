using System.Diagnostics;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>An aeroplane resting on the sea runs at 8-35 fps on the device while one sitting on a runway runs at 60.
/// Both have every contact point touching, so the difference must be what a point over WATER costs. Codex indexed the
/// height field; this asks whether the water surface got the same treatment.</summary>
// INSTALLS a world into the global WorldTerrain.Active, so it must not run in parallel with anything that
// steps an aeroplane: LandingGear, AirframeContact and FloatHydro all read the ground and the water through
// that static, so a class holding a world here silently changes the ground under 29 other test classes.
[Collection("WorldTerrainActive")]
public sealed class IslandWaterCostProbe : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _o;
    public IslandWaterCostProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void WhatDoesAWaterQueryCost()
    {
        WorldTerrain.Active = new WorldTerrain();
        var rnd = new System.Random(7);
        // Where the Pitts came to rest in the sea, and a runway for comparison.
        (string what, double x, double y)[] spots = { ("open sea", 6137, 3637), ("KSNA runway", -8756, 590) };
        foreach ((string what, double x, double y) in spots)
        {
            // Warm up, then 20,000 fresh coordinates within a metre or two, as a settled airframe produces.
            for (int i = 0; i < 2000; i++) { _ = FloatHydro.WaterSurfaceAt(x + rnd.NextDouble(), y + rnd.NextDouble(), 0); }
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++) { _ = FloatHydro.WaterSurfaceAt(x + rnd.NextDouble() * 3, y + rnd.NextDouble() * 3, 0); }
            sw.Stop();
            double water = sw.Elapsed.TotalMilliseconds * 1000 / 20000;

            sw.Restart();
            for (int i = 0; i < 20000; i++) { _ = WorldTerrain.GroundHeightAt(x + rnd.NextDouble() * 3, y + rnd.NextDouble() * 3); }
            sw.Stop();
            double ground = sw.Elapsed.TotalMilliseconds * 1000 / 20000;

            sw.Restart();
            for (int i = 0; i < 20000; i++) { _ = WorldTerrain.WheelGroundHeightAt(x + rnd.NextDouble() * 3, y + rnd.NextDouble() * 3, 0); }
            sw.Stop();
            double wheel = sw.Elapsed.TotalMilliseconds * 1000 / 20000;

            sw.Restart();
            for (int i = 0; i < 20000; i++) { _ = WorldTerrain.SurfaceAt(x + rnd.NextDouble() * 3, y + rnd.NextDouble() * 3); }
            sw.Stop();
            double surf = sw.Elapsed.TotalMilliseconds * 1000 / 20000;

            _o.WriteLine($"{what,-14} water {water,8:F2} us   ground {ground,8:F2} us   wheelGround {wheel,8:F2} us   surfaceAt {surf,8:F2} us");
        }
        WorldTerrain.Active = null;
    }
}
