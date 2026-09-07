using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Numeric pins for every frame/orientation convention (owner concern 2026-09-06: "could be a simple
/// frame of reference problem"). Conventions under test:
///   body axes x fwd / y right / z DOWN; world z DOWN (altitude = -z);
///   quaternion is body-to-world, Hamilton, qDot = 0.5 * q (x) omega_body;
///   positive p rolls RIGHT WING DOWN, positive q raises the nose, positive r yaws nose RIGHT.
/// If any frame convention regresses, one of these fails with a physical description.
/// </summary>
public class FrameConventionTests
{
    private static readonly MassProperties SymmetricMass = new(100, 1000, 1000, 1000, 0);

    private static RigidBodyState Spin(Vec3 rates, double seconds)
    {
        var s = new RigidBodyState(Vec3.Zero, Quat.Identity, Vec3.Zero, rates);
        for (int i = 0; i < (int)(seconds / 0.001); i++)
        {
            s = RigidBody6DOF.IntegrateRK4(s, 0.001, SymmetricMass, _ => (Vec3.Zero, Vec3.Zero));
        }

        return s;
    }

    [Fact]
    public void PositiveRollRateDropsTheRightWing()
    {
        RigidBodyState s = Spin(new Vec3(0.5, 0, 0), 1.0);
        Vec3 rightWingWorld = s.Attitude.Rotate(new Vec3(0, 1, 0));
        Assert.True(rightWingWorld.Z > 0.3, $"p>0 must roll right wing DOWN (world +z); got z={rightWingWorld.Z:F3}");
    }

    [Fact]
    public void PositivePitchRateRaisesTheNose()
    {
        RigidBodyState s = Spin(new Vec3(0, 0.5, 0), 1.0);
        Vec3 noseWorld = s.Attitude.Rotate(new Vec3(1, 0, 0));
        Assert.True(noseWorld.Z < -0.3, $"q>0 must pitch nose UP (world -z); got z={noseWorld.Z:F3}");
    }

    [Fact]
    public void PositiveYawRateSwingsTheNoseRight()
    {
        RigidBodyState s = Spin(new Vec3(0, 0, 0.5), 1.0);
        Vec3 noseWorld = s.Attitude.Rotate(new Vec3(1, 0, 0));
        Assert.True(noseWorld.Y > 0.3, $"r>0 must yaw nose RIGHT (world +y); got y={noseWorld.Y:F3}");
    }

    [Fact]
    public void GravityInBodyFrameMatchesPitchAttitude()
    {
        // Nose-up 30 deg: gravity must pull AFT (-x body) and less strongly down along body z.
        double theta = 30 * System.Math.PI / 180;
        var noseUp = new Quat(0, System.Math.Sin(theta / 2), 0, System.Math.Cos(theta / 2));
        Vec3 gBody = noseUp.Conjugate().Rotate(new Vec3(0, 0, 9.81)); // same transform Aircraft.Step uses
        Assert.True(gBody.X < -4.5 && gBody.X > -5.3, $"Nose-up 30deg: gravity body-x should be ~-g*sin30=-4.9; got {gBody.X:F2}");
        Assert.True(System.Math.Abs(gBody.Z - 9.81 * System.Math.Cos(theta)) < 0.05, $"gravity body-z should be g*cos30; got {gBody.Z:F2}");
        // And this must equal the TrimSolver's hand-written gravity terms (-W sin, +W cos).
    }

    [Fact]
    public void FreeFallAcceleratesDownAndAltitudeDecreases()
    {
        var s = new RigidBodyState(new Vec3(0, 0, -100), Quat.Identity, Vec3.Zero, Vec3.Zero);
        for (int i = 0; i < 1000; i++)
        {
            s = RigidBody6DOF.IntegrateRK4(s, 0.001, SymmetricMass,
                _ => (new Vec3(0, 0, SymmetricMass.MassKg * 9.81), Vec3.Zero)); // gravity only, level attitude
        }

        Assert.True(s.Velocity.Z > 9.0, $"After 1s free fall, body w should be ~9.8 down; got {s.Velocity.Z:F2}");
        Assert.True(s.Position.Z > -100 + 4.0, $"Altitude must DECREASE (world z toward +); z went {s.Position.Z:F2} from -100");
    }

    [Fact]
    public void RotatingFrameCoriolisTermHasCorrectSign()
    {
        // Yawing right at rate r with forward speed u, no forces: apparent body-frame acceleration
        // vdot = -omega x v -> vdot.y = -r*u (velocity vector swings LEFT in body axes as nose yaws right).
        var s = new RigidBodyState(Vec3.Zero, Quat.Identity, new Vec3(20, 0, 0), new Vec3(0, 0, 0.5));
        RigidBodyDerivative d = RigidBody6DOF.ComputeDerivative(s, Vec3.Zero, Vec3.Zero, SymmetricMass);
        Assert.True(System.Math.Abs(d.VelocityDot.Y - (-0.5 * 20)) < 1e-9,
            $"vdot.y must be -r*u = -10; got {d.VelocityDot.Y:F3}");
    }
}
