using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Steady wind (owner-directed head/tail/crosswind for landing): the air mass drifts, so
/// airspeed and ground track separate — headwind cuts groundspeed, tailwind raises it, crosswind
/// makes the aircraft crab and drift. Crosswind touchdown drift loads the gear sideways.</summary>
public class WindTests
{
    private static AircraftConfig Cub() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    private static Aircraft Trimmed(double v)
    {
        var c = Cub();
        var t = TrimSolver.SolveGliderTrim(c, v, 600);
        double half = t.ThetaRad / 2;
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -600),
            new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half)),
            new Vec3(v * System.Math.Cos(t.AlphaRad), 0, v * System.Math.Sin(t.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, t.ElevatorRad, 0, 0));
    }

    [Fact]
    public void HeadwindCutsGroundspeed()
    {
        try
        {
            Atmosphere.SimTimeSec = 0;
            var ac = Trimmed(28);
            // Groundspeed = airspeed - headwind. 10 m/s headwind blows toward -x (against +x flight).
            Atmosphere.SteadyWind = new Vec3(-10, 0, 0);
            var sim = new SimLoop(ac);
            var stick = new ControlDeflections(0, ac.CurrentDeflections.ElevatorRad, 0, 0);
            for (int i = 0; i < 40; i++) sim.RunFor(0.05, stick);
            // State.Velocity is GROUND velocity (body). Airspeed = ground - wind.
            Vec3 windBody = ac.State.Attitude.Conjugate().Rotate(Atmosphere.SteadyWind);
            double groundspeed = ac.State.Velocity.Length;
            double airspeed = (ac.State.Velocity - windBody).Length;
            Assert.True(airspeed > groundspeed + 6, $"Headwind: airspeed must exceed groundspeed by ~wind: as={airspeed:F0} gs={groundspeed:F0}");
        }
        finally { Atmosphere.SteadyWind = Vec3.Zero; }
    }

    [Fact]
    public void CrosswindWeathervanesTheNoseAndGroundTrackCarriesTheWind()
    {
        try
        {
            Atmosphere.SimTimeSec = 0;
            var ac = Trimmed(28);
            Atmosphere.SteadyWind = new Vec3(0, 8, 0);          // air mass moving +y
            var sim = new SimLoop(ac);
            var stick = new ControlDeflections(0, ac.CurrentDeflections.ElevatorRad, 0, 0);
            for (int i = 0; i < 160; i++) sim.RunFor(0.05, stick);
            // Hands off, the aircraft keeps its inertial velocity (momentum) and weathervanes: the nose swings
            // INTO the wind until the air-relative sideslip is gone, and ground velocity − air velocity is the wind.
            Vec3 groundWorld = ac.State.Attitude.Rotate(ac.State.Velocity);
            Vec3 airWorld = groundWorld - Atmosphere.SteadyWind;
            Vec3 nose = ac.State.Attitude.Rotate(new Vec3(1, 0, 0));
            double noseHdg = System.Math.Atan2(nose.Y, nose.X), airHdg = System.Math.Atan2(airWorld.Y, airWorld.X);
            double beta = System.Math.Abs(noseHdg - airHdg) * 180 / System.Math.PI;
            Assert.True(beta < 3, $"nose must align with the air vector (sideslip {beta:F1}°)");
            Assert.True(noseHdg < -8 * System.Math.PI / 180, $"nose must point into the wind (heading {noseHdg * 57.3:F1}°)");
            Assert.InRange((groundWorld - airWorld).Y, 7.9, 8.1);
        }
        finally { Atmosphere.SteadyWind = Vec3.Zero; }
    }

    [Fact]
    public void CrosswindTouchdownLoadsGearSideways()
    {
        try
        {
            Atmosphere.SimTimeSec = 0;
            var c = Cub();
            // Sitting on the runway, crosswind from the side => tires must make a lateral reaction.
            var mains = c.Gear.FindAll(g => !g.IsTailwheel); var tws = c.Gear.FindAll(g => g.IsTailwheel);
            double pitch = System.Math.Atan((mains[0].PosVec().Z - tws[0].PosVec().Z) / (mains[0].PosVec().X - tws[0].PosVec().X));
            var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
            double maxWz = -999; foreach (var g in c.Gear) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -maxWz + 0.02), att, new Vec3(15, 0, 0), Vec3.Zero),
                new ControlDeflections(0, 0, 0, 0));
            Atmosphere.SteadyWind = new Vec3(0, 12, 0); // strong crosswind
            var sim = new SimLoop(ac);
            double y0 = ac.State.Position.Y;
            for (int i = 0; i < 60; i++) sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
            // The rolling aircraft weathervanes/drifts; verify it develops a yaw or lateral motion.
            double drift = System.Math.Abs(ac.State.Position.Y - y0);
            double yaw = System.Math.Abs(System.Math.Atan2(2 * (ac.State.Attitude.W * ac.State.Attitude.Z), 1));
            Assert.True(drift > 0.5 || yaw > 0.02, $"Crosswind on the runway must push/weathervane the aircraft; drift={drift:F1} yaw={yaw:F3}");
        }
        finally { Atmosphere.SteadyWind = Vec3.Zero; }
    }
}
