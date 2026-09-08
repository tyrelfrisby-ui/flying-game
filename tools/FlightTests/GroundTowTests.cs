using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Aerotow from the runway: tug at full power pulls the glider from rest; the rope must not break
/// and both must reach flying speed. Mirrors TowController's ground-launch logic.</summary>
public class GroundTowTests
{
    private readonly ITestOutputHelper _out;
    public GroundTowTests(ITestOutputHelper o) { _out = o; }

    private static Aircraft OnGround(AircraftConfig c, double x)
    {
        var mains = c.Gear.FindAll(g => !g.IsTailwheel && g.GearType != "nose-skid" && System.Math.Abs(g.Pos[1]) < 2);
        var tws = c.Gear.FindAll(g => g.IsTailwheel && g.Pos[0] < -2);
        double pitch = tws.Count > 0 ? System.Math.Atan((mains[0].Pos[2] - tws[0].Pos[2]) / (mains[0].Pos[0] - tws[0].Pos[0])) : 0;
        var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        double maxWz = -999; foreach (var g in c.Gear) if (System.Math.Abs(g.Pos[1]) < 2) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        return new Aircraft(c, new RigidBodyState(new Vec3(x, 0, -maxWz + 0.01), att, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
    }

    [Fact]
    public void TugPullsGliderToFlyingSpeed()
    {
        var gcfg = TestAircraftConfig.Load();
        var tcfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        var glider = OnGround(gcfg, 0);
        var tug = OnGround(tcfg, 61.0 + 2.0 + 3.4 - 0.2);
        var tow = new AeroTow(tug, glider, 61.0);
        double dt = SimLoop.DefaultFixedDtSec, maxTension = 0;
        for (double t = 0; t < 20; t += dt)
        {
            tow.Apply(dt);
            // Glider: neutral stick, spoilers closed once rolling (pad above 50 %), no brakes.
            glider.Step(new ControlInputs(0, 0, 0, 0.0), dt);
            // Tug autopilot as in TowController: heading hold with rudder, elevator schedule, power ramped to full over 2 s.
            var s = tug.State; double v = s.Velocity.Length;
            double psi = System.Math.Atan2(2 * (s.Attitude.W * s.Attitude.Z + s.Attitude.X * s.Attitude.Y), 1 - 2 * (s.Attitude.Y * s.Attitude.Y + s.Attitude.Z * s.Attitude.Z));
            double rud = System.Math.Clamp(-psi * 2.0 - s.Rates.Z * 0.5, -1, 1);
            double elev = v < 10 ? 0.0 : v < 24 ? 0.08 : -0.25;
            double power = System.Math.Min(1.0, t / 2.0);
            tug.Step(new ControlInputs(0, elev, rud, 1.0 - 2.0 * power), dt);
            maxTension = System.Math.Max(maxTension, tow.Tension);
            if (System.Math.Abs(t - System.Math.Round(t)) < dt / 2 || (t > 1.4 && t < 2.6 && System.Math.Abs(t * 10 - System.Math.Round(t * 10)) < dt * 5))
            {
                var gq = glider.State.Attitude; double gp = System.Math.Asin(System.Math.Clamp(2 * (gq.W * gq.Y - gq.Z * gq.X), -1, 1)) * 57.3;
                _out.WriteLine($"t={t:F1} tug V={v:F1} x={tug.State.Position.X:F1} | glider V={glider.State.Velocity.Length:F2} x={glider.State.Position.X:F2} pitch={gp:F1} agl={-glider.State.Position.Z:F2} | hookDist={(tow.TugHookWorld - tow.GliderHookWorld).Length:F2} tension={tow.Tension:F0} ext=({glider.ExternalForceWorld.X:F0},{glider.ExternalForceWorld.Z:F0}) {tow.SeverReason}");
            }
            if (!tow.Connected) break;
        }
        Assert.True(tow.Connected, $"rope severed: {tow.SeverReason} (max tension {maxTension:F0} N)");
        Assert.True(glider.State.Velocity.Length > 18, $"glider only reached {glider.State.Velocity.Length:F1} m/s");
    }
}
