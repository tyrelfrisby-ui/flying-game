using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

// INSTALLS a world into the global WorldTerrain.Active, so it must not run in parallel with anything that
// steps an aeroplane: LandingGear, AirframeContact and FloatHydro all read the ground and the water through
// that static, so a class holding a world here silently changes the ground under 29 other test classes.
[Collection("WorldTerrainActive")]
public sealed class IslandNanProbe : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. A bare reset at the end of a test method
    /// does not run when the test throws, so a failing test used to leave its world installed in the global
    /// WorldTerrain.Active for the whole rest of the run — which silently changed the ground under every
    /// class that came after it, whether or not it was serialised.</summary>
    public void Dispose() => WorldTerrain.Active = null;

    private readonly ITestOutputHelper _o;
    public IslandNanProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void IsThereAnyPlaceOnTheIslandWhoseHeightIsNotANumber()
    {
        WorldTerrain.Active = new WorldTerrain();
        int bad = 0; var first = new List<string>();
        for (double x = -20000; x <= 20000; x += 200)
            for (double y = -20000; y <= 20000; y += 200)
            {
                double h = WorldTerrain.GroundHeightAt(x, y);
                if (double.IsNaN(h) || double.IsInfinity(h))
                {
                    bad++;
                    if (first.Count < 10) first.Add($"({x:F0},{y:F0}) -> {h}");
                }
            }
        _o.WriteLine($"grid 201x201 at 200 m: {bad} non-finite heights");
        foreach (string f in first) _o.WriteLine("  " + f);
        WorldTerrain.Active = null;
        Assert.Equal(0, bad);
    }

    [Fact]
    public void WhatHappensWhenAnAeroplaneHitsTheIslandHard()
    {
        WorldTerrain.Active = new WorldTerrain();
        var config = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
        (string where, double x, double y)[] places =
        {
            ("KSNA flat", -8060, 960), ("mid island", 0, 0), ("Drake", -4180, -4900),
            ("KMYL", 2780, -880), ("3U2 slope", -760, 1820), ("Powell canyon", -4525, 4475),
            ("over the sea", 12000, 12000), ("PHDH hill", 4330, 4990),
        };
        int threw = 0, nan = 0;
        foreach ((string where, double x, double y) in places)
        {
            double g = WorldTerrain.GroundHeightAt(x, y);
            // A spin arrival: 90 ft/s down (27 m/s), 54 kt forward, well past the stall, from 40 m up.
            var pos = new Vec3(x, y, -(g + 40));
            var state = new RigidBodyState(pos, new Quat(0, 0, 0, 1), new Vec3(28, 0, 27), new Vec3(0, 0, 2.0));
            var loop = new SimLoop(new Aircraft(config, state, ControlDeflections.Neutral));
            string outcome = "survived";
            try
            {
                for (int i = 0; i < (int)(6 / SimLoop.DefaultFixedDtSec); i++)
                {
                    loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, -1, 0, 1));
                    Vec3 p = loop.Aircraft.State.Position;
                    if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)) { outcome = $"NaN after {i * SimLoop.DefaultFixedDtSec:F2} s"; nan++; break; }
                }
            }
            catch (System.Exception ex) { outcome = ex.GetType().Name + ": " + ex.Message; threw++; }
            _o.WriteLine($"{where,-14} ground {g,8:F1}  {outcome}");
        }
        WorldTerrain.Active = null;
        _o.WriteLine($"{threw} threw, {nan} went NaN out of {places.Length}");
    }
}
