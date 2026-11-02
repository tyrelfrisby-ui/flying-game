using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Meta Quest control mapping and the head pose measured from the seat's eye. Engine-free, so the signs
/// are pinned here instead of on Ty's head. Expected values are literals (CLAUDE.md, "A test that reads the
/// file it is checking proves nothing").
/// </summary>
public sealed class QuestControlsTests
{
    private const double Dt = 1.0 / 72.0;

    private static QuestFlightCommand Once(QuestControllerFrame f, double throttle = 0.5, bool invert = false)
    {
        var m = new QuestControlMapper { InvertElevator = invert };
        return m.Step(f, Dt, throttle);
    }

    // ---- axis signs ----

    [Fact]
    public void RightStickRightRollsRight()
    {
        var c = Once(new QuestControllerFrame { RightX = 1.0 });
        Assert.Equal(1.0, c.Aileron, 9);
        Assert.True(c.StickHeld);
    }

    [Fact]
    public void RightStickForwardIsPushNoseDown()
    {
        // The pad convention: elevator positive is PUSH. Forward on the thumbstick is +y.
        Assert.Equal(1.0, Once(new QuestControllerFrame { RightY = 1.0 }).Elevator, 9);
        Assert.Equal(-1.0, Once(new QuestControllerFrame { RightY = -1.0 }).Elevator, 9);
    }

    [Fact]
    public void InvertElevatorFlipsOnlyPitch()
    {
        var c = Once(new QuestControllerFrame { RightX = 0.6, RightY = 1.0 }, invert: true);
        Assert.Equal(-1.0, c.Elevator, 9);
        Assert.True(c.Aileron > 0);
    }

    [Fact]
    public void LeftStickRightIsRightRudder()
    {
        var c = Once(new QuestControllerFrame { LeftX = 1.0 });
        Assert.Equal(1.0, c.Rudder, 9);
        Assert.True(c.RudderHeld);
    }

    // ---- dead zone ----

    [Theory]
    [InlineData(0.05, 0.0)]
    [InlineData(0.10, 0.0)]
    [InlineData(-0.09, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, -1.0)]
    [InlineData(0.55, 0.5)]      // (0.55 - 0.10) / 0.90
    public void DeadZoneRemovesDriftAndKeepsFullThrow(double raw, double expected)
    {
        Assert.Equal(expected, QuestControlMapper.DeadZone(raw, 0.10), 9);
    }

    [Fact]
    public void ADriftingStickIsLetGo()
    {
        var c = Once(new QuestControllerFrame { RightX = 0.06, RightY = -0.07, LeftX = 0.05 });
        Assert.Equal(0.0, c.Aileron);
        Assert.Equal(0.0, c.Elevator);
        Assert.Equal(0.0, c.Rudder);
        Assert.False(c.StickHeld);
        Assert.False(c.RudderHeld);
    }

    // ---- the throttle is a lever: it holds ----

    [Fact]
    public void ThrottleStickIsARateAndTheLeverHoldsWhenReleased()
    {
        var m = new QuestControlMapper();
        double lever = 0.0;
        // One second at full forward: 0.5 of the travel.
        for (int i = 0; i < 72; i++)
        {
            var c = m.Step(new QuestControllerFrame { LeftY = 1.0 }, Dt, lever);
            Assert.True(c.ThrottleMoved);
            lever = c.Throttle01;
        }
        Assert.Equal(0.5, lever, 6);
        // Let go: it does not spring back, and it does not claim to have moved.
        for (int i = 0; i < 72; i++)
        {
            var c = m.Step(new QuestControllerFrame(), Dt, lever);
            Assert.False(c.ThrottleMoved);
            Assert.Equal(0.5, c.Throttle01, 9);
        }
    }

    [Fact]
    public void ThrottleStopsAtIdleAndFull()
    {
        var m = new QuestControlMapper();
        Assert.Equal(1.0, m.Step(new QuestControllerFrame { LeftY = 1.0 }, 10.0, 0.9).Throttle01, 9);
        Assert.Equal(0.0, m.Step(new QuestControllerFrame { LeftY = -1.0 }, 10.0, 0.1).Throttle01, 9);
    }

    [Fact]
    public void ThrottleDeadZoneIsWiderThanTheStick()
    {
        // 0.2 forward would move the stick, but must not creep the throttle.
        var c = Once(new QuestControllerFrame { LeftY = 0.2 });
        Assert.False(c.ThrottleMoved);
    }

    [Fact]
    public void RudderWithAWanderingThumbDoesNotOpenTheThrottle()
    {
        var c = Once(new QuestControllerFrame { LeftX = 1.0, LeftY = 0.35 });
        Assert.Equal(1.0, c.Rudder, 9);
        Assert.False(c.ThrottleMoved);
    }

    [Fact]
    public void EasingThePowerDoesNotKickTheRudder()
    {
        var c = Once(new QuestControllerFrame { LeftX = 0.3, LeftY = -1.0 });
        Assert.Equal(0.0, c.Rudder);
        Assert.True(c.ThrottleMoved);
    }

    [Fact]
    public void GrippedLeverFollowsTheHandAndStaysWhenLetGo()
    {
        var m = new QuestControlMapper();
        // Squeeze with the lever at 0.3 and the hand at an arbitrary 0.42 m.
        var c = m.Step(new QuestControllerFrame { LeftGrip = 1.0, LeftHandForwardM = 0.42 }, Dt, 0.3);
        Assert.Equal(0.3, c.Throttle01, 9);
        // 10 cm forward is half the 20 cm travel.
        c = m.Step(new QuestControllerFrame { LeftGrip = 1.0, LeftHandForwardM = 0.52 }, Dt, 0.3);
        Assert.True(c.ThrottleMoved);
        Assert.Equal(0.8, c.Throttle01, 9);
        // Past the stop: held at full.
        c = m.Step(new QuestControllerFrame { LeftGrip = 1.0, LeftHandForwardM = 0.90 }, Dt, 0.8);
        Assert.Equal(1.0, c.Throttle01, 9);
        // Let go and move the hand back: the lever stays.
        c = m.Step(new QuestControllerFrame { LeftGrip = 0.0, LeftHandForwardM = 0.10 }, Dt, 1.0);
        Assert.False(c.ThrottleMoved);
        Assert.Equal(1.0, c.Throttle01, 9);
    }

    [Fact]
    public void ReGrippingTakesTheLeverFromWhereTheGameLeftIt()
    {
        // A reset moves the lever under the pilot's hand; the next grab must start from there, not jump back.
        var m = new QuestControlMapper();
        m.Step(new QuestControllerFrame { LeftGrip = 1.0, LeftHandForwardM = 0.0 }, Dt, 0.9);
        m.Step(new QuestControllerFrame { LeftGrip = 0.0 }, Dt, 0.9);
        var c = m.Step(new QuestControllerFrame { LeftGrip = 1.0, LeftHandForwardM = 0.3 }, Dt, 0.0);
        Assert.Equal(0.0, c.Throttle01, 9);
        c = m.Step(new QuestControllerFrame { LeftGrip = 1.0, LeftHandForwardM = 0.34 }, Dt, 0.0);
        Assert.Equal(0.2, c.Throttle01, 9);
    }

    [Fact]
    public void GripHasHysteresis()
    {
        var m = new QuestControlMapper();
        m.Step(new QuestControllerFrame { LeftGrip = 0.65, LeftHandForwardM = 0.0 }, Dt, 0.5);
        // Sagging to 0.45 is still a grab (off below 0.4)...
        var c = m.Step(new QuestControllerFrame { LeftGrip = 0.45, LeftHandForwardM = 0.02 }, Dt, 0.5);
        Assert.Equal(0.6, c.Throttle01, 9);
        // ...and 0.5 from cold is not.
        var m2 = new QuestControlMapper();
        Assert.False(m2.Step(new QuestControllerFrame { LeftGrip = 0.5, LeftHandForwardM = 0.1 }, Dt, 0.5).ThrottleMoved);
    }

    // ---- buttons ----

    [Fact]
    public void BrakesFollowTheLeftTrigger()
    {
        Assert.Equal(0.7, Once(new QuestControllerFrame { LeftTrigger = 0.7 }).Brake, 9);
        Assert.Equal(0.0, Once(new QuestControllerFrame()).Brake, 9);
    }

    [Fact]
    public void TrimButtonsRunNoseUpAndNoseDown()
    {
        Assert.Equal(-0.5, Once(new QuestControllerFrame { A = true }).PitchTrimRate, 9);   // nose up
        Assert.Equal(0.5, Once(new QuestControllerFrame { B = true }).PitchTrimRate, 9);    // nose down
        Assert.Equal(0.0, Once(new QuestControllerFrame { A = true, B = true }).PitchTrimRate, 9);
    }

    [Fact]
    public void PressesFireOnceOnTheWayDown()
    {
        var m = new QuestControlMapper();
        var c1 = m.Step(new QuestControllerFrame { X = true, Menu = true, LeftStickClick = true }, Dt, 0.5);
        var c2 = m.Step(new QuestControllerFrame { X = true, Menu = true, LeftStickClick = true }, Dt, 0.5);
        Assert.True(c1.FlapsDown); Assert.True(c1.Menu); Assert.True(c1.Recentre);
        Assert.Equal(QuestMenuTrigger.MenuButton, c1.MenuBy);
        Assert.Equal(QuestMenuTrigger.None, c2.MenuBy);
        Assert.False(c2.FlapsDown); Assert.False(c2.Menu); Assert.False(c2.Recentre);
        var c3 = m.Step(new QuestControllerFrame { Y = true, RightStickClick = true }, Dt, 0.5);
        Assert.True(c3.FlapsUp); Assert.True(c3.Gear);
    }

    [Fact]
    public void SmokeTogglesOncePerSqueezeAndGunsAreHeld()
    {
        var m = new QuestControlMapper();
        Assert.True(m.Step(new QuestControllerFrame { RightGrip = 1.0 }, Dt, 0.5).Smoke);
        Assert.False(m.Step(new QuestControllerFrame { RightGrip = 0.5 }, Dt, 0.5).Smoke);   // hysteresis: still held
        Assert.False(m.Step(new QuestControllerFrame { RightGrip = 0.0 }, Dt, 0.5).Smoke);
        Assert.True(m.Step(new QuestControllerFrame { RightGrip = 0.9 }, Dt, 0.5).Smoke);
        Assert.True(m.Step(new QuestControllerFrame { RightTrigger = 0.8 }, Dt, 0.5).Fire);
        Assert.False(m.Step(new QuestControllerFrame { RightTrigger = 0.3 }, Dt, 0.5).Fire);
    }

    // ---- hands ----

    [Fact]
    public void HandsDoNotFlyButAHeldPinchOpensTheMenuOnce()
    {
        var m = new QuestControlMapper();
        bool menu = false; int fired = 0;
        for (int i = 0; i < 72 * 3; i++)
        {
            var c = m.Step(new QuestControllerFrame { HandsOnly = true, LeftPinch = 1.0, RightX = 1.0, LeftY = 1.0 }, Dt, 0.4);
            Assert.Equal(0.0, c.Aileron);
            Assert.False(c.ThrottleMoved);
            Assert.Equal(0.4, c.Throttle01, 9);
            if (c.Menu) { menu = true; fired++; if (fired == 1) Assert.InRange(i * Dt, 0.98, 1.02); Assert.Equal(QuestMenuTrigger.LeftPinchHeld, c.MenuBy); }
        }
        Assert.True(menu);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void AShortPinchDoesNothing()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < 36; i++)
        {
            var c = m.Step(new QuestControllerFrame { HandsOnly = true, RightPinch = 1.0 }, Dt, 0.4);
            Assert.False(c.Recentre);
        }
        Assert.False(m.Step(new QuestControllerFrame { HandsOnly = true }, Dt, 0.4).Recentre);
    }

    // ---- the head, measured from the seat's eye ----

    private static Quat Yaw(double deg) => XrHeadPose.AboutUp(deg * System.Math.PI / 180.0);
    private static Quat Pitch(double degDown)
    {
        double h = degDown * System.Math.PI / 360.0;   // about +x: positive tips the nose of the head DOWN in Unity's axes
        return new Quat(System.Math.Sin(h), 0, 0, System.Math.Cos(h));
    }

    [Fact]
    public void RecentreMakesTheCurrentHeadTheEyeFacingTheNose()
    {
        var head = new XrHeadPose();
        // Seated somewhere odd in the room: 1.2 m up, off to one side, turned 70° right.
        var pos = new Vec3(0.4, 1.2, -0.3);
        var rot = Yaw(70);
        head.Recentre(pos, rot);
        var (p, r) = head.Relative(pos, rot);
        Assert.Equal(0.0, p.Length, 9);
        Vec3 fwd = r.Rotate(new Vec3(0, 0, 1));
        Assert.Equal(0.0, fwd.X, 9);
        Assert.Equal(1.0, fwd.Z, 9);
        Assert.Equal(70.0, head.ReferenceYawDeg, 6);
    }

    [Fact]
    public void RecentringWhileLookingDownDoesNotTiltTheWorld()
    {
        // From an awkward reference: turned 40° left AND looking 35° down at the panel.
        var head = new XrHeadPose();
        var rot = Quat.Multiply(Yaw(-40), Pitch(35));
        head.Recentre(Vec3.Zero, rot);
        var (_, r) = head.Relative(Vec3.Zero, rot);
        Vec3 fwd = r.Rotate(new Vec3(0, 0, 1));
        // Still looking 35° down, now straight at the nose. The pitch survived; only the heading went.
        Assert.Equal(0.0, fwd.X, 9);
        Assert.Equal(-System.Math.Sin(35 * System.Math.PI / 180), fwd.Y, 9);
        // And lifting the head afterwards levels it against the world, not against the recentre pose.
        var (_, level) = head.Relative(Vec3.Zero, Yaw(-40));
        Vec3 lf = level.Rotate(new Vec3(0, 0, 1));
        Assert.Equal(0.0, lf.Y, 9);
        Assert.Equal(1.0, lf.Z, 9);
    }

    [Fact]
    public void TurningTheHeadIsOneForOne()
    {
        var head = new XrHeadPose();
        head.Recentre(Vec3.Zero, Yaw(25));
        var (_, r) = head.Relative(Vec3.Zero, Yaw(25 + 30));
        Assert.Equal(30.0, XrHeadPose.HeadingOf(r) * 180 / System.Math.PI, 6);
    }

    [Fact]
    public void LeaningIsMeasuredInTheSeatsAxesNotTheRooms()
    {
        // Recentred facing 90° right in the room: leaning toward the room's +x is leaning FORWARD in the seat.
        var head = new XrHeadPose();
        head.Recentre(new Vec3(1, 1, 1), Yaw(90));
        var (p, _) = head.Relative(new Vec3(1.1, 1, 1), Yaw(90));
        Assert.Equal(0.0, p.X, 9);
        Assert.Equal(0.1, p.Z, 9);
    }

    [Fact]
    public void LeaningIsClampedToTheCockpitBox()
    {
        var head = new XrHeadPose();
        head.Recentre(Vec3.Zero, Quat.Identity);
        var (p, _) = head.Relative(new Vec3(-0.9, 0.5, 0.8), Quat.Identity);
        Assert.Equal(-0.30, p.X, 9);   // left
        Assert.Equal(0.15, p.Y, 9);    // up: the canopy is close
        Assert.Equal(0.30, p.Z, 9);    // forward
        var (q, _) = head.Relative(new Vec3(0.9, -0.9, -0.9), Quat.Identity);
        Assert.Equal(0.30, q.X, 9);
        Assert.Equal(-0.30, q.Y, 9);
        Assert.Equal(-0.15, q.Z, 9);   // aft: the headrest
        Assert.True(head.Clamped(new Vec3(0, 0.2, 0)));
        Assert.False(head.Clamped(new Vec3(0.1, 0.1, 0.1)));
    }

    [Fact]
    public void HeadingStraightDownOrUpIsNotNaN()
    {
        double down = XrHeadPose.HeadingOf(Quat.Multiply(Yaw(30), Pitch(90)));
        double up = XrHeadPose.HeadingOf(Quat.Multiply(Yaw(30), Pitch(-90)));
        Assert.Equal(30.0, down * 180 / System.Math.PI, 6);
        Assert.Equal(30.0, up * 180 / System.Math.PI, 6);
    }
}
