using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Sign pins for every propeller effect (right-hand prop conventions).</summary>
public class PropEffectsTests
{
    private static readonly PropulsionConfig Rh = new()
    { MaxPowerW = 134000, PropDiameterM = 1.9, RotationSign = 1, PropInertia = 1.7 };

    [Fact]
    public void ThrustForwardTorqueRollsLeft()
    {
        (Vec3 f, Vec3 m) = PropModel.Compute(Rh, 1.0, new Vec3(30, 0, 0), Vec3.Zero, 1.225);
        Assert.True(f.X > 500, $"Full power at 30 m/s must give solid thrust; got {f.X:F0} N");
        Assert.True(m.X < -100, $"RH prop torque reaction must roll LEFT (Mx<0); got {m.X:F0}");
    }

    [Fact]
    public void PFactorYawsLeftAtPositiveAlpha()
    {
        (_, Vec3 m0) = PropModel.Compute(Rh, 1.0, new Vec3(30, 0, 0), Vec3.Zero, 1.225);
        (_, Vec3 m1) = PropModel.Compute(Rh, 1.0, new Vec3(30, 0, 6), Vec3.Zero, 1.225); // alpha ~11 deg
        Assert.True(m1.Z < m0.Z - 20, $"P-factor at +alpha must add LEFT yaw; dMz={m1.Z - m0.Z:F0}");
    }

    [Fact]
    public void SlipstreamYawsLeftStrongestSlow()
    {
        (_, Vec3 slow) = PropModel.Compute(Rh, 1.0, new Vec3(15, 0, 0), Vec3.Zero, 1.225);
        (_, Vec3 fast) = PropModel.Compute(Rh, 1.0, new Vec3(50, 0, 0), Vec3.Zero, 1.225);
        Assert.True(slow.Z < -100, $"Spiral slipstream must yaw LEFT; got {slow.Z:F0}");
        Assert.True(slow.Z < fast.Z, "Slipstream yaw must be strongest at low speed");
    }

    [Fact]
    public void GyroscopicCrossCouples()
    {
        // RH prop: pitch nose-down (q<0) -> yaw LEFT; yaw right (r>0) -> pitch DOWN.
        (_, Vec3 mq) = PropModel.Compute(Rh, 1.0, new Vec3(30, 0, 0), new Vec3(0, -1.0, 0), 1.225);
        Assert.True(mq.Z < -50, $"Nose-down pitch rate must gyro-yaw LEFT (taildragger rule); got {mq.Z:F0}");
        (_, Vec3 mr) = PropModel.Compute(Rh, 1.0, new Vec3(30, 0, 0), new Vec3(0, 0, 1.0), 1.225);
        Assert.True(mr.Y < -50, $"Right yaw rate must gyro-pitch DOWN; got {mr.Y:F0}");
    }
}
