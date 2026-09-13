using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-09-12: a real castoring tailwheel — soft steering springs, ground force through a trail, breakout
/// to free swivel, small contact patch, load = whatever the tail spring carries.</summary>
public class TailwheelTests
{
    private readonly ITestOutputHelper _out;
    public TailwheelTests(ITestOutputHelper o) { _out = o; }
    private static AircraftConfig Cub() => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    [Fact]
    public void SpringsFollowTheRudderWhenTheWheelIsUnloaded()
    {
        var c = Cub(); WorldTerrain.Active = null;
        var tw = new LandingGear.TailwheelState();
        // Tail in the air (tail-up wheel landing): no ground force, the springs alone swing the wheel to the command.
        var s = new RigidBodyState(new Vec3(0, 0, -3), new Quat(0, 0, 0, 1), new Vec3(20, 0, 0), Vec3.Zero);
        for (int i = 0; i < 120; i++) LandingGear.UpdateTailwheel(c, s, tw, 1.0, 1.0 / 120);
        Assert.Equal(0.0, tw.LastLoadN, 3);
        Assert.InRange(tw.AngleRad, 0.4, 0.46);   // full rudder → MaxSteerRad 0.45
    }

    [Fact]
    public void LoadedWheelCastorsTowardItsVelocityAgainstTheSprings()
    {
        var c = Cub(); WorldTerrain.Active = null;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var tw = new LandingGear.TailwheelState();
        // Rolling at 6 m/s while the rudder holds full right: on the ground the tyre force overpowers the soft springs
        // and the wheel castors back toward straight ahead (it never fully stops castoring).
        var s = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(6, 0, 0), Vec3.Zero);
        for (int i = 0; i < 240; i++) LandingGear.UpdateTailwheel(c, s, tw, 1.0, 1.0 / 120);
        _out.WriteLine($"loaded: angle {tw.AngleRad * 57.3:F1}°, load {tw.LastLoadN:F0} N, side {tw.LastLateralN:F0} N");
        Assert.True(tw.LastLoadN > 300, "the tail spring carries the tail weight");
        Assert.True(tw.AngleRad < 0.3 && tw.AngleRad > 0.02, $"castor gives against the springs ({tw.AngleRad * 57.3:F1}°)");
        // Sliding sideways: the wheel swings toward the direction it is being dragged.
        var side = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(1.5, 4, 0), Vec3.Zero);
        var tw2 = new LandingGear.TailwheelState();
        for (int i = 0; i < 240; i++) LandingGear.UpdateTailwheel(c, side, tw2, 0.0, 1.0 / 120);
        _out.WriteLine($"sideways drag: angle {tw2.AngleRad * 57.3:F1}° free {tw2.FreeSwivel}");
        Assert.True(tw2.AngleRad < -0.5, "wheel swings right toward a rightward drag (steer frame: + = left, as Compute applies it)");
    }

    [Fact]
    public void BeyondBreakoutTheWheelSwivelsFree()
    {
        var c = Cub(); WorldTerrain.Active = null;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var tw = new LandingGear.TailwheelState { AngleRad = 1.2 };   // 69°: past the 35° breakout
        var s = new RigidBodyState(rest.Position, rest.Attitude, Vec3.Zero, Vec3.Zero);   // stopped: no ground force
        LandingGear.UpdateTailwheel(c, s, tw, 0.0, 1.0 / 120);
        Assert.True(tw.FreeSwivel);
        Assert.InRange(tw.AngleRad, 1.19, 1.21);   // nothing pulls it back while stopped and broken out
        // Inside the breakout the springs act.
        tw.AngleRad = 0.3;
        LandingGear.UpdateTailwheel(c, s, tw, 0.0, 1.0 / 120);
        Assert.False(tw.FreeSwivel);
        Assert.True(tw.AngleRad < 0.3);
    }

    [Fact]
    public void TailUpLandingPutsNoLoadOnTheTailwheel()
    {
        var c = Cub(); WorldTerrain.Active = null;
        // Level attitude, mains on the ground, tail wheel 0.3 m up: aerodynamic tail load keeps it there — zero load.
        double lowestMain = 0; foreach (GearConfig g in c.Gear) if (!g.IsTailwheel) lowestMain = System.Math.Max(lowestMain, g.Pos[2]);
        var s = new RigidBodyState(new Vec3(0, 0, -(lowestMain - 0.02)), new Quat(0, 0, 0, 1), new Vec3(25, 0, 0), Vec3.Zero);
        var tw = new LandingGear.TailwheelState();
        LandingGear.UpdateTailwheel(c, s, tw, 0.5, 1.0 / 120);
        Assert.Equal(0.0, tw.LastLoadN, 3);
    }

    [Fact]
    public void TaxiTurnWorksThroughTheSprungWheel()
    {
        var c = Cub(); WorldTerrain.Active = null;
        var ac = new Aircraft(c, LandingGear.RestingState(c, 0, 0, 0), ControlDeflections.Neutral);
        Assert.NotNull(ac.Tailwheel);
        var sim = new SimLoop(ac);
        // Roll straight for 3 s at modest power, then full right rudder: the nose comes right through the wheel.
        for (double t = 0; t < 3; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 0.3));
        var q0 = ac.State.Attitude; double psi0 = System.Math.Atan2(2 * (q0.W * q0.Z + q0.X * q0.Y), 1 - 2 * (q0.Y * q0.Y + q0.Z * q0.Z));
        for (double t = 0; t < 3; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, 0, 1, 0.3));
        var q = ac.State.Attitude; double psi = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        _out.WriteLine($"heading change {(psi - psi0) * 57.3:F1}°, tailwheel {ac.Tailwheel!.AngleRad * 57.3:F1}°, load {ac.Tailwheel.LastLoadN:F0} N, speed {ac.State.Velocity.Length:F1}");
        Assert.True(psi - psi0 > 0.15, "full rudder turns a taxiing Cub right");
        Assert.True(ac.Tailwheel.LastLoadN > 100, "the tailwheel is carrying the tail");   // it may be castoring or broken out: it does not hold the command
    }
}
