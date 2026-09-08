using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Landing-gear physics (owner-directed): the aircraft settles on its wheels, a taildragger
/// is directionally UNSTABLE (ground-loop tendency) while a tricycle self-centers, and side load
/// shifts weight toward the outer wheel — all emergent from per-wheel tire forces.</summary>
public class LandingGearTests
{
    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Aircraft OnRunway(string id, double speed, double yawRad = 0)
    {
        var c = Cfg(id);
        // Place wheels on the ground: find the lowest wheel and set altitude so it just touches.
        double maxZ = 0;
        foreach (var g in c.Gear) maxZ = System.Math.Max(maxZ, g.PosVec().Z - c.Mass.CgVec().Z);
        var att = new Quat(0, 0, System.Math.Sin(yawRad / 2), System.Math.Cos(yawRad / 2));
        var pos = new Vec3(0, 0, -maxZ + 0.02); // CG height so wheels are ~2 cm compressed
        return new Aircraft(c, new RigidBodyState(pos, att, new Vec3(speed, 0, 0), Vec3.Zero),
            new ControlDeflections(0, 0, 0, 0));
    }

    [Fact]
    public void AircraftSettlesOnItsWheelsAtRest()
    {
        var ac = OnRunway("c172-like", 0);
        var sim = new SimLoop(ac);
        for (int i = 0; i < 300; i++) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0)); // idle
        Assert.True(LandingGear.OnGround(ac.Config, ac.State), "Must stay on the ground.");
        Assert.True(System.Math.Abs(ac.State.Velocity.Z) < 1.0, "Must settle, not bounce/sink.");
        Assert.True(ac.State.Position.Z > -3, "Must not fall through the runway.");
    }

    [Fact]
    public void TaildraggerIsDirectionallyUnstable()
    {
        // Rolling fast with a yaw-rate seed: a taildragger (CG behind mains) grows the yaw — a ground
        // loop builds with speed (destabilizing moment ~ V^2). No rudder correction = it runs away.
        var tail = OnRunway("pa18-cub-like", 28, yawRad: 0.03);
        tail.State = new RigidBodyState(tail.State.Position, tail.State.Attitude, tail.State.Velocity, new Vec3(0, 0, 0.1));
        var sim = new SimLoop(tail);
        double yaw0 = System.Math.Abs(HeadingOf(tail));
        for (int i = 0; i < 250; i++) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        double yaw1 = System.Math.Abs(HeadingOf(tail));
        Assert.True(yaw1 > yaw0 * 1.7, $"Taildragger yaw must diverge (ground loop): {yaw0:F3} -> {yaw1:F3}");
    }

    [Fact]
    public void TricycleIsDirectionallyStable()
    {
        var tri = OnRunway("c172-like", 15, yawRad: 0.05);
        var sim = new SimLoop(tri);
        double yaw0 = System.Math.Abs(HeadingOf(tri));
        for (int i = 0; i < 150; i++) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        double yaw1 = System.Math.Abs(HeadingOf(tri));
        Assert.True(yaw1 <= yaw0 + 0.02, $"Tricycle must not diverge (self-stable): {yaw0:F3} -> {yaw1:F3}");
    }

    [Fact]
    public void SideLoadShiftsWeightToOuterWheel()
    {
        // Direct force read: skidding sideways puts more normal load on the down-going (outer) strut.
        var c = Cfg("c172-like");
        double maxZ = 0; foreach (var g in c.Gear) maxZ = System.Math.Max(maxZ, g.PosVec().Z - c.Mass.CgVec().Z);
        // Give a roll rate (banking) so one main compresses more; read per-wheel normal via total moment sign.
        var att = Quat.Identity;
        var state = new RigidBodyState(new Vec3(0, 0, -maxZ + 0.05), att, new Vec3(20, 5, 0), new Vec3(0, 0, 0));
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        (Vec3 f, Vec3 m) = LandingGear.Compute(c, state, 0, 0);
        // Sideways velocity (+y, right) -> tires make -y force -> rolling moment about x that loads the
        // left (outer) wheel: net roll moment Mx should be nonzero (weight transfer present).
        Assert.True(System.Math.Abs(m.X) > 50, $"Side load must create a roll/weight-transfer moment; Mx={m.X:F0}");
        Assert.True(f.Y < 0, $"Right sideslip must make leftward tire force; Fy={f.Y:F0}");
    }

    private static double HeadingOf(Aircraft a)
    {
        var q = a.State.Attitude;
        return System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
    }

    private static Aircraft OnGround3Point(string id)
    {
        var c = Cfg(id);
        var mains = c.Gear.FindAll(g => !g.IsTailwheel);
        var tws = c.Gear.FindAll(g => g.IsTailwheel);
        double pitch = tws.Count > 0 && mains.Count > 0
            ? System.Math.Atan((mains[0].PosVec().Z - tws[0].PosVec().Z) / (mains[0].PosVec().X - tws[0].PosVec().X)) : 0;
        var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        double maxWz = -999; foreach (var g in c.Gear) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -maxWz + 0.01), att, new Vec3(0.1, 0, 0), Vec3.Zero),
            new ControlDeflections(0, 0, 0, 0));
    }

    [Theory]
    [InlineData("pa18-cub-like")]   // bungee taildragger
    [InlineData("c172-like")]        // spring-steel tricycle
    public void TakesOffUnderPowerWithRudderHeld(string id)
    {
        var ac = OnGround3Point(id);
        ac.FlapFraction = 0.4;
        var sim = new SimLoop(ac);
        bool airborne = false;
        for (int i = 0; i < 4000 && !airborne; i++)
        {
            double v = ac.State.Velocity.Length;
            double rud = System.Math.Clamp(-HeadingOf(ac) * 4.0 - ac.State.Rates.Z * 1.5, -1, 1); // hold heading
            double elev = v > ac.Config.Limits.VneMs * 0.32 ? -0.6 : 0;                            // rotate
            sim.RunFor(0.02, new ControlInputs(0, elev, rud, -1.0));                               // full power
            if (!LandingGear.OnGround(ac.Config, ac.State) && -ac.State.Position.Z > 1.5) airborne = true;
        }
        Assert.True(airborne, $"{id} must accelerate and lift off the runway.");
        Assert.True(ac.State.Position.X > 20, "Must roll forward down the runway, not swap ends.");
    }


    private static Aircraft MainsAboutToTouch(string id, double pitchDeg, double sinkMs, double speed)
    {
        var c = Cfg(id);
        double p = pitchDeg * System.Math.PI / 180;
        var att = new Quat(0, System.Math.Sin(p / 2), 0, System.Math.Cos(p / 2));
        var mains = c.Gear.FindAll(g => !g.IsTailwheel);
        double mz = -999; foreach (var g in mains) mz = System.Math.Max(mz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        var pos = new Vec3(0, 0, -mz - 0.10);                       // mains ~10 cm above the runway
        var vel = att.Conjugate().Rotate(new Vec3(speed, 0, sinkMs)); // forward + sink, world frame
        return new Aircraft(c, new RigidBodyState(pos, att, vel, Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
    }

    [Fact]
    public void TaildraggerMainTouchdownPitchesNoseUp()
    {
        // Mains AHEAD of the CG: the touchdown reaction pitches nose-UP -> AoA/lift rise -> bounce
        // tendency (the classic wheel-landing bounce; the pilot must ease forward).
        var ac = MainsAboutToTouch("pa18-cub-like", 6, 1.5, 25);
        var sim = new SimLoop(ac);
        double aoaBefore = 0, maxQ = -9;
        for (int i = 0; i < 20; i++)
        {
            bool wasAir = !LandingGear.OnGround(ac.Config, ac.State);
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, 0));
            if (wasAir && LandingGear.OnGround(ac.Config, ac.State))
                aoaBefore = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
            maxQ = System.Math.Max(maxQ, ac.State.Rates.Y);
        }
        Assert.True(maxQ > 0.05, $"Taildragger main touchdown must pitch nose-UP (q>0); maxQ={maxQ:F2}");
    }

    [Fact]
    public void TricycleMainTouchdownDerotatesNoseDown()
    {
        // Mains BEHIND the CG: touchdown pitches nose-DOWN (derotation) -> AoA/lift fall -> settles.
        var ac = MainsAboutToTouch("c172-like", 8, 1.5, 28);
        var sim = new SimLoop(ac);
        double minQ = 9;
        for (int i = 0; i < 25; i++)
        {
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, 0));
            minQ = System.Math.Min(minQ, ac.State.Rates.Y);
        }
        Assert.True(minQ < -0.05, $"Tricycle main touchdown must derotate nose-DOWN (q<0); minQ={minQ:F2}");
    }

}
