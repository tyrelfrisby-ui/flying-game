using System;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Challenge;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE GRADED SIGNALS ARE AIR-RELATIVE. A pre-ship audit (2026-11-11) found FlightSignals reading `s.Velocity`
/// raw, so "iasMs", "alphaRad" and "betaRad" were all GROUND measurements. The default wind is 5 m/s, which is
/// enough to fail a perfectly flown coordinated turn: a ground-referenced sideslip swings once per revolution
/// with amplitude asin(wind/speed) while the ball never moves.
///
/// These tests put a known wind on the world and fly prescribed states through it, so they fail if anyone takes
/// the subtraction back out.
/// </summary>
public class ChallengeSignalsInWindTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public ChallengeSignalsInWindTests(ITestOutputHelper o)
    {
        _o = o;
        _savedWind = Atmosphere.SteadyWind; _savedField = Atmosphere.Field;
        Atmosphere.Field = null;                      // a uniform wind, not the island's field
    }
    private readonly Vec3 _savedWind; private readonly AtmosphereField? _savedField;
    public void Dispose() { Atmosphere.SteadyWind = _savedWind; Atmosphere.Field = _savedField; }

    /// <summary>An aeroplane at a prescribed attitude and GROUND velocity, 10 m up — where the boundary-layer
    /// gradient is exactly 1.0, so the wind the model applies is the wind that was set. These tests are about the
    /// SUBTRACTION; the gradient has its own file (WindGradientAglTests).</summary>
    private static readonly Vec3 Where = new(0, 0, -10);
    private static Aircraft At(Quat attitude, Vec3 groundVelBody) =>
        new(AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json")),
            new RigidBodyState(Where, attitude, groundVelBody, Vec3.Zero),
            ControlDeflections.Neutral);

    /// <summary>The wind the model actually applies there — asserted to be the wind that was set, so a future
    /// change to the gradient fails HERE with a clear reason instead of corrupting every expectation below.</summary>
    private void AssertAppliedWindIs(Vec3 expected)
    {
        Vec3 got = Atmosphere.WindAtPosition(Where);
        _o.WriteLine($"applied wind at 10 m: ({got.X:F3}, {got.Y:F3}, {got.Z:F3})");
        Assert.Equal(expected.X, got.X, 6); Assert.Equal(expected.Y, got.Y, 6); Assert.Equal(expected.Z, got.Z, 6);
    }

    [Fact]
    public void AHeadwindIsNotASlowerAeroplane()
    {
        // Flying north at 50 m/s through the air, with 10 m/s of wind FROM the north: groundspeed 40, airspeed 50.
        Atmosphere.SteadyWind = new Vec3(-10, 0, 0);          // world: blowing toward -x (south)
        AssertAppliedWindIs(new Vec3(-10, 0, 0));
        var ac = At(Quat.Identity, new Vec3(40, 0, 0));       // body +x forward = north; 40 over the ground
        double ias = FlightSignals.Read("iasMs", ac);
        _o.WriteLine($"ground 40 m/s, 10 m/s headwind -> iasMs {ias:F3}");
        Assert.Equal(50.0, ias, 6);
    }

    [Fact]
    public void ATailwindIsNotAFasterAeroplane()
    {
        Atmosphere.SteadyWind = new Vec3(10, 0, 0);           // blowing toward +x (north), i.e. a tailwind
        AssertAppliedWindIs(new Vec3(10, 0, 0));
        var ac = At(Quat.Identity, new Vec3(60, 0, 0));       // 60 over the ground
        Assert.Equal(50.0, FlightSignals.Read("iasMs", ac), 6);
    }

    /// <summary>
    /// THE ONE THE AUDIT WAS ABOUT. A crosswind with the aeroplane pointing exactly into its own airflow: the ball
    /// is centred, so beta must be zero. Read off the ground velocity it is asin(10/50) = 11.5 degrees of
    /// invented sideslip, which is what failed a coordinated turn.
    /// </summary>
    [Fact]
    public void ACoordinatedAeroplaneInACrosswindIsNotSideslipping()
    {
        Atmosphere.SteadyWind = new Vec3(0, 10, 0);                   // 10 m/s from the west, toward +y (east)
        AssertAppliedWindIs(new Vec3(0, 10, 0));
        // Air-relative velocity is 50 m/s straight ahead; over the ground it is also drifting 10 m/s east.
        var ac = At(Quat.Identity, new Vec3(50, 10, 0));
        double beta = FlightSignals.Read("betaRad", ac) * 180.0 / Math.PI;
        double groundBeta = Math.Asin(10.0 / new Vec3(50, 10, 0).Length) * 180.0 / Math.PI;
        _o.WriteLine($"beta {beta:F4} deg (ground-referenced would be {groundBeta:F2})");
        Assert.True(Math.Abs(beta) < 1e-9, $"coordinated in a crosswind must read zero sideslip, got {beta:F4} deg");
        Assert.True(groundBeta > 11.0, "the bug this pins would have read >11 deg");
    }

    [Fact]
    public void AlphaIsMeasuredAgainstTheAirNotTheGround()
    {
        // Level attitude, 50 m/s forward through the air, with a 5 m/s DOWNdraught (world +z is down).
        Atmosphere.SteadyWind = new Vec3(0, 0, 5);
        var ac = At(Quat.Identity, new Vec3(50, 0, 0));       // ground velocity level
        double alpha = FlightSignals.Read("alphaRad", ac) * 180.0 / Math.PI;
        // Sinking air means the aeroplane meets it from below: alpha = atan2(-5, 50) = -5.71 deg.
        _o.WriteLine($"alpha in a 5 m/s downdraught: {alpha:F3} deg");
        Assert.Equal(Math.Atan2(-5.0, 50.0) * 180.0 / Math.PI, alpha, 6);
    }

    /// <summary>A VSI follows the aeroplane's own altitude change, so it stays on the GROUND velocity: an aeroplane
    /// holding altitude in a downdraught is not descending, whatever the air is doing.</summary>
    [Fact]
    public void VerticalSpeedStaysOnTheGroundVelocity()
    {
        Atmosphere.SteadyWind = new Vec3(0, 0, 5);
        var ac = At(Quat.Identity, new Vec3(50, 0, 0));
        Assert.Equal(0.0, FlightSignals.Read("verticalSpeedMs", ac), 9);
    }

    [Fact]
    public void WithNoWindNothingMoved()
    {
        Atmosphere.SteadyWind = Vec3.Zero;
        var ac = At(Quat.Identity, new Vec3(50, 3, 2));
        Assert.Equal(new Vec3(50, 3, 2).Length, FlightSignals.Read("iasMs", ac), 9);
        Assert.Equal(Math.Atan2(2, 50), FlightSignals.Read("alphaRad", ac), 9);
    }
}
