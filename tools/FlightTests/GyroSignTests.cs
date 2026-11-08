using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The propeller's gyroscopic couple, pinned against first principles and against the one case every taildragger
/// pilot knows (Ty 2026-11-08: "it almost feels like gyroscopic reaction is working incorrectly pulling nose down
/// with right rudder instead of nose up" — "Is it possible that there is a sign error +- somewhere that reverses
/// gyroscopics inverted?").
///
/// The answer this file establishes: the couple is M = −ω × H in BODY axes, and it carries no attitude term at all,
/// so there is structurally nowhere in it for an inverted-only sign error to live. What DOES change when inverted is
/// what a body-axis moment means in the world — and that is a matter of reading it, not of the formula.
/// </summary>
public class GyroSignTests
{
    /// <summary>A right-hand prop (clockwise seen from the cockpit): angular momentum along +x, nose-forward.</summary>
    private static PropulsionConfig RightHand() => new()
    { PropDiameterM = 1.9, PropInertia = 3.5, MaxPowerW = 194000, IdleRpm = 700, MaxRpm = 2700, RotationSign = 1 };

    private static double H(PropulsionConfig p, double rpm) => p.RotationSign * p.PropInertia * rpm * 2.0 * System.Math.PI / 60.0;

    [Fact]
    public void ItIsMinusOmegaCrossH()
    {
        var p = RightHand();
        var rates = new Vec3(0.3, -0.7, 1.9);          // arbitrary p, q, r
        double h = H(p, 2700);
        Vec3 got = PropModel.GyroMoment(p, 1.0, rates);
        // −ω × H with H = (h,0,0): (0, −r·h, +q·h)
        Assert.Equal(0.0, got.X, 9);
        Assert.Equal(-rates.Z * h, got.Y, 6);
        Assert.Equal(rates.Y * h, got.Z, 6);
    }

    [Fact]
    public void RaisingTheTailYawsLeft()
    {
        // The case every taildragger pilot knows, and the one that fixes the sign beyond argument: pitching the nose
        // DOWN on the takeoff roll yaws a conventional American single to the LEFT.
        var p = RightHand();
        Vec3 m = PropModel.GyroMoment(p, 1.0, new Vec3(0, -1.0, 0));   // q < 0: nose down
        Assert.True(m.Z < 0, $"nose-down gave a yaw moment of {m.Z:F0} N·m; a right-hand prop must yaw LEFT (negative)");
    }

    [Fact]
    public void YawingRightPitchesTheNoseDownInBodyAxes()
    {
        // Which is the same couple seen the other way round, and is why an UPRIGHT right spin is the steep one in a
        // clockwise-prop aeroplane.
        var p = RightHand();
        Vec3 m = PropModel.GyroMoment(p, 1.0, new Vec3(0, 0, 1.0));    // r > 0: yaw right
        Assert.True(m.Y < 0, $"right yaw gave {m.Y:F0} N·m about +y; it must be nose-down in body axes");
    }

    [Fact]
    public void InvertedTheSameCoupleRaisesTheNoseInTheWORLD()
    {
        // The crux of his question. The couple does not change — the aeroplane does. Body nose-down moves the nose
        // toward the belly; with the belly uppermost that is UP in the world, so the same right-rudder couple that
        // steepens an upright right spin FLATTENS an inverted one. No inverted sign term is needed, and adding one
        // would be the error.
        var p = RightHand();
        Vec3 bodyMoment = PropModel.GyroMoment(p, 1.0, new Vec3(0, 0, 1.0));
        Assert.True(bodyMoment.Y < 0);
        var inverted = new Quat(1, 0, 0, 0);                            // rolled 180°
        Vec3 world = inverted.Rotate(bodyMoment);
        // World +y is still "pitch" about the right-hand horizontal, but the wing now points the other way: the sign
        // of the moment about the WORLD y axis flips, which is the whole point.
        Assert.True(world.Y > 0, $"inverted, the same couple read {world.Y:F0} about world +y — it should have flipped");
    }

    [Fact]
    public void ALeftHandPropIsTheMirrorOfARightHandOne()
    {
        var r = RightHand();
        var l = RightHand(); l.RotationSign = -1;
        var rates = new Vec3(0, 0.4, -0.9);
        Vec3 mr = PropModel.GyroMoment(r, 1.0, rates), ml = PropModel.GyroMoment(l, 1.0, rates);
        Assert.Equal(-mr.Y, ml.Y, 6);
        Assert.Equal(-mr.Z, ml.Z, 6);
    }

    [Fact]
    public void AStoppedPropHasNoGyroscopicCouple()
    {
        var p = RightHand();
        Vec3 m = PropModel.GyroMoment(p, 1.0, new Vec3(0, 0, 2.0), PropModel.PropDrive.Dead(0));
        Assert.Equal(0.0, m.Length, 9);
    }
}
