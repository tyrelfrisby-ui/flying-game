using System;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Soaring energy (owner-directed hill + thermals): a thermal core makes rising air that
/// beats the glider's sink so it climbs when centered; ridge lift rises on the windward slope.</summary>
// SETS the process-wide weather (Atmosphere.SteadyWind / ActiveTurbulence / Field), so it must not run in
// PARALLEL with anything that flies an aeroplane. The Dispose below stops this class leaking its weather
// FORWARD when a test throws (5b5eb46); it does nothing about xUnit running this class ALONGSIDE another,
// which puts a thermal under someone else's aeroplane mid-flight. Both halves are needed, and only the
// first one was done: without this attribute the whole fleet stalls in SpawnSpeedTests and StructuralTests
// goes NaN, exactly as the Dispose comment below describes (2026-11-01).
[Collection("WorldTerrainActive")]
public class SoaringTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. Atmosphere.SteadyWind / ActiveTurbulence /
    /// Field are global: a class that sets the weather and resets it only at the end of a test method leaves
    /// that weather blowing through every test that runs after it when the test throws. That is what put the
    /// whole fleet into a stall in SpawnSpeedTests while all 34 passed in isolation.</summary>
    public void Dispose()
    {
        Atmosphere.SteadyWind = FlyingGame.Core.MathTypes.Vec3.Zero;
        Atmosphere.ActiveTurbulence = null;
        Atmosphere.Field = null;
    }

    private static Aircraft Glider(double x, double y, double alt, double v)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json"));
        var t = TrimSolver.SolveGliderTrim(c, v, alt);
        double half = t.ThetaRad / 2;
        return new Aircraft(c, new RigidBodyState(new Vec3(x, y, -alt),
            new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half)),
            new Vec3(v * System.Math.Cos(t.AlphaRad), 0, v * System.Math.Sin(t.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, t.ElevatorRad, 0, 0));
    }

    [Fact]
    public void ThermalCoreOutclimbsGliderSink()
    {
        try
        {
            Atmosphere.SimTimeSec = 0;
            // Strong wide thermal so a straight pass through the core shows a climb (or much reduced sink).
            Atmosphere.Thermals.Add(new Thermal(new Vec3(0, 0, 0), 120, 5.0, 2000));
            var ac = Glider(-60, 0, 600, 20);
            var sim = new SimLoop(ac);
            var stick = new ControlDeflections(0, ac.CurrentDeflections.ElevatorRad, 0, 0);
            // Fly through the core; compare sink rate over the core vs the glider's still-air sink (~1 m/s).
            double bestClimb = -99;
            for (int i = 0; i < 200; i++)
            {
                sim.RunFor(0.05, stick);
                double climbRate = -ac.State.Attitude.Rotate(ac.State.Velocity).Z; // + = climbing
                bestClimb = System.Math.Max(bestClimb, climbRate);
            }
            Assert.True(bestClimb > 1.0, $"Thermal core must overcome glider sink and lift it; best climb {bestClimb:F1} m/s");
        }
        finally { Atmosphere.Thermals.Clear(); }
    }

    [Fact]
    public void ThermalGivesUpwardWind()
    {
        var th = new Thermal(new Vec3(0, 0, 0), 100, 4.0, 1500);
        Vec3 core = th.WindAt(new Vec3(0, 0, -700));     // in the core at 700 m
        Vec3 outside = th.WindAt(new Vec3(400, 0, -700)); // well outside
        Assert.True(core.Z < -2.0, $"Thermal core must be strong rising air (world -z); wz={core.Z:F1}");
        Assert.True(System.Math.Abs(outside.Z) < System.Math.Abs(core.Z), "Outside the core must be much weaker.");
    }

    [Fact]
    public void RidgeLiftRisesOnWindwardSlope()
    {
        // Ridge running north-south (axis +x), wind from the east (-y) hitting the west... set wind
        // across the ridge and check upward air on the windward face just above the crest.
        var ridge = new Ridge(new Vec3(0, 0, -200), new Vec3(1, 0, 0), 200, 300); // crest 200 m, axis +x
        Vec3 wind = new(0, 10, 0); // blowing +y, across the ridge
        Vec3 windward = ridge.WindAt(new Vec3(0, -80, -260), wind); // upwind (−y) side, above crest
        Assert.True(windward.Z < -1.0, $"Ridge must deflect air UP on the windward slope; wz={windward.Z:F1}");
        Vec3 calm = ridge.WindAt(new Vec3(0, -80, -260), Vec3.Zero);
        Assert.True(System.Math.Abs(calm.Z) < 0.01, "No wind → no ridge lift.");
    }
}
