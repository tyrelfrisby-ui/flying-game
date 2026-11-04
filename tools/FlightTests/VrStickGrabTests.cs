using System;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-02 (Quest 3S, hand tracking): "the extra throttle worked but not the stick", "it did not work on the
/// 2.33", "in the Great Lakes neither the stick nor the throttle". The sticks are RAKED and the old gimbal stood every
/// stick straight up from its pivot, so the grab point was off the grip the pilot sees. These pin the rig-driven
/// gimbal (1:1 with the rig's hinges, rake and all), the grab capsule, and who flies when a pad is also connected.
/// Geometry: the measured nodes of our own GLBs (glTF +x aft → cockpit −z), cockpit frame +x right, +y up, +z nose.
/// The Unity-side twin, over every aeroplane's real rig, is EditorTools.VrHandleCheck ("STICK"/"THROTTLE" lines).
/// </summary>
public sealed class VrStickGrabTests
{
    // The Extra's rear stick: pivot glTF (1.355, −0.135), Grip_Stick_Rear (1.41, 0.327): 0.462 up, 0.055 aft.
    private static readonly Vec3 ExtraPivot = new(0, -0.135, -1.355);
    private static readonly Vec3 ExtraGrip = new(0, 0.327, -1.41);

    // Roll about the fore-aft tube (−z: top goes right for +roll), pitch about the lateral axis (−x: top goes aft for +).
    private static StickGimbal Extra() => new(ExtraPivot, new Vec3(0, 0, -1), ExtraPivot, new Vec3(-1, 0, 0), ExtraGrip,
                                              RigCurve.Symmetric(18, 18), RigCurve.Symmetric(12, 18));

    // The 2-33's rear stick rides a TorqueTube whose hinge is 0.37 m ahead of the stick's own pivot on the same line:
    // glTF TorqueTube x −1.07, Stick_R (−0.70, −0.907), top of its mesh (−0.659, −0.298) → grip 4 cm below that.
    private static StickGimbal Sgs233()
    {
        var pivot = new Vec3(0, -0.907, 0.70);
        var top = new Vec3(0, -0.298, 0.659);
        Vec3 grip = top + (pivot - top) / (pivot - top).Length * 0.04;
        return new StickGimbal(new Vec3(0, -0.907, 1.07), new Vec3(0, 0, -1), pivot, new Vec3(-1, 0, 0), grip,
                               RigCurve.Symmetric(20, 20), RigCurve.Symmetric(12, 18));
    }

    [Fact]
    public void RakedStickAtRestIsItsGrip()
    {
        Vec3 g = Extra().GripAt(0, 0);
        Assert.Equal(0.327, g.Y, 9);
        Assert.Equal(-1.41, g.Z, 9);
        // √(0.462² + 0.055²) = √0.216469 = 0.46526
        Assert.Equal(0.46526, Extra().LengthM, 5);
    }

    [Fact]
    public void ProxyConventionStillHolds()
    {
        // The upright proxy through the general constructor: 10° right, 5° aft → (sin10 cos5, cos10 cos5, −sin5)·0.5.
        var s = new StickGimbal(Vec3.Zero, 0.5, 18, 18, 12);
        Vec3 g = s.GripAt(10, 5);
        Assert.Equal(0.5 * Math.Sin(10 * Math.PI / 180) * Math.Cos(5 * Math.PI / 180), g.X, 9);
        Assert.Equal(0.5 * Math.Cos(10 * Math.PI / 180) * Math.Cos(5 * Math.PI / 180), g.Y, 9);
        Assert.Equal(-0.5 * Math.Sin(5 * Math.PI / 180), g.Z, 9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(18.0, 0.0)]
    [InlineData(-18.0, 18.0)]
    [InlineData(9.0, -12.0)]
    [InlineData(-5.0, 7.5)]
    public void HandMovesTheRakedStickOneToOne(double rollDeg, double pitchDeg)
    {
        foreach (StickGimbal s in new[] { Extra(), Sgs233() })
        {
            s.SetFromControls(0, 0);
            s.Grab(s.Grip);                         // closed exactly on the grip: no offset
            s.Follow(s.GripAt(rollDeg, pitchDeg));  // the hand carries the grip to where the rig would draw it
            Assert.Equal(rollDeg, s.RollDeg, 4);
            Assert.Equal(pitchDeg, s.PitchDeg, 4);
            Assert.True((s.Grip - s.GripAt(rollDeg, pitchDeg)).Length < 1e-5);
            // Rig params: roll ±18 (or ±20) linear; pitch −12 ↔ −1, +18 ↔ +1.
            Assert.Equal(rollDeg / s.RollMaxDeg, s.Aileron, 4);
            Assert.Equal(pitchDeg >= 0 ? pitchDeg / 18.0 : pitchDeg / 12.0, s.ElevatorAft, 4);
        }
    }

    [Fact]
    public void GrabOffTheGripDoesNotJumpAndThenFollows()
    {
        StickGimbal s = Extra();
        s.SetFromControls(0, 0);
        Vec3 hand = s.Grip + new Vec3(0.02, -0.03, 0.01);   // a fist closed a little right of and below the grip
        s.Grab(hand);
        s.Follow(hand);
        Assert.Equal(0.0, s.RollDeg, 3);
        Assert.Equal(0.0, s.PitchDeg, 3);
        s.Follow(hand + new Vec3(0.05, 0, 0));               // 5 cm right
        Assert.True(s.Aileron > 0.25, $"aileron {s.Aileron}");
        s.Follow(hand + new Vec3(0, 0, -0.05));              // back to centre, then 5 cm aft
        Assert.True(s.ElevatorAft > 0.25, $"elevator {s.ElevatorAft}");
        s.Follow(hand + new Vec3(0, 0, -1.0));               // far past the stop: held at the stop
        Assert.Equal(1.0, s.ElevatorAft, 6);
    }

    [Fact]
    public void AFistOnTheVisibleGripIsInReach_TheOldUprightPointWasNot()
    {
        StickGimbal s = Extra();
        s.SetFromControls(0, 0);
        // 5 cm down the stick from the grip point: up = (0, 0.462, −0.055)/0.46526 = (0, 0.99300, −0.11821),
        // so the mid-grip is (0, 0.27735, −1.40409).
        Vec3 mid = s.Grip - s.Up * 0.05;
        Assert.Equal(0.27735, mid.Y, 4);
        Assert.Equal(-1.40409, mid.Z, 4);
        // The old model's one point: pivot + 0.46526 straight up = (0, 0.33026, −1.355). From the mid-grip:
        // √(0.05291² + 0.04909²) = 0.07218 — beyond the 6 cm reach. That was the Extra's stick.
        Vec3 oldPoint = ExtraPivot + new Vec3(0, s.LengthM, 0);
        Assert.Equal(0.07218, (oldPoint - mid).Length, 4);
        Assert.True((oldPoint - mid).Length > CockpitGrab.ReachM);

        var grab = new CockpitGrab();
        grab.Set(Grabbable.Stick, s.Grip + s.Up * 0.05, s.Grip - s.Up * 0.12, true);
        Assert.Equal(Grabbable.Stick, grab.Nearest(mid + new Vec3(0.04, 0, 0), HandSide.Right));   // 4 cm beside the tube
        Assert.Equal(Grabbable.None, grab.Nearest(mid + new Vec3(0.07, 0, 0), HandSide.Right));    // 7 cm: not
    }

    [Fact]
    public void ATrackedHandClosingOnTheStickFliesIt()
    {
        StickGimbal s = Extra();
        s.SetFromControls(0, 0);
        var grab = new CockpitGrab();
        grab.Set(Grabbable.Stick, s.Grip + s.Up * 0.05, s.Grip - s.Up * 0.12, true);
        Vec3 at = s.Grip - s.Up * 0.04;
        var open = new HandPose { Source = HandSource.TrackedHand, Position = at, Rotation = Quat.Identity, Grip = 0.0 };
        var shut = open; shut.Grip = 1.0;
        grab.Step(HandSide.Right, open, false);
        Assert.True((grab.Step(HandSide.Right, shut, false) & GrabEvents.Grabbed) != 0);
        Assert.True(grab.IsHeld(Grabbable.Stick));
        s.Grab(at);
        shut.Position = at + new Vec3(-0.04, 0, 0.03);   // left and forward
        grab.Step(HandSide.Right, shut, false);
        s.Follow(shut.Position);
        Assert.True(s.Aileron < -0.2, $"aileron {s.Aileron}");
        Assert.True(s.ElevatorAft < -0.2, $"elevator {s.ElevatorAft}");
        Assert.True(grab.IsHeld(Grabbable.Stick));
    }

    [Fact]
    public void ThrottleLeverFromTheRigsKnobMoves()
    {
        // The Great Lakes' rear throttle: pivot glTF (1.72, −0.30, 0.351); a knob 0.12 m up the lever, swept 50° forward.
        var pivot = new Vec3(-0.351, -0.30, -1.72);
        Vec3 idle = pivot + new Vec3(0, 0.12, 0);
        Vec3 full = pivot + new Vec3(0, 0.12 * Math.Cos(50 * Math.PI / 180), 0.12 * Math.Sin(50 * Math.PI / 180));
        var lever = new HingedLever(pivot, idle, full, new Vec3(-1, 0, 0));
        Assert.Equal(50.0, lever.SweepDeg, 6);
        Assert.Equal(0.12, lever.ArmM, 9);
        var grab = new CockpitGrab();
        grab.Set(Grabbable.Throttle, lever.GripAt(0), lever.GripAt(0) + (pivot - lever.GripAt(0)) * 0.5, true);
        Vec3 hand = lever.GripAt(0) + new Vec3(-0.03, -0.02, 0);
        Assert.Equal(Grabbable.Throttle, grab.Nearest(hand, HandSide.Left));
        lever.Grab(hand, 0.0);
        Assert.Equal(0.0, lever.Follow(hand), 6);
        double t = lever.Follow(hand + new Vec3(0, -0.02, 0.06));   // pushed forward
        Assert.True(t > 0.3, $"throttle {t}");
    }

    [Fact]
    public void RigCurveRoundTripsAsymmetricTravel()
    {
        var c = RigCurve.Symmetric(12, 18);
        Assert.Equal(-12.0, c.Value(-1), 9);
        Assert.Equal(9.0, c.Value(0.5), 9);
        Assert.Equal(-0.5, c.Param(-6), 9);
        Assert.Equal(1.0, c.Param(40), 9);   // past the stop: the stop
        var down = new RigCurve(new[] { 0.0, 1.0 }, new[] { 0.0, -41.33 });
        Assert.Equal(0.5, down.Param(-20.665), 9);
    }

    [Fact]
    public void APadPastItsDeadZoneTakesTheStickFromTheHand()
    {
        Assert.True(HandPadArbiter.HandOwnsStick(true, 0.0, 0.0));
        Assert.True(HandPadArbiter.HandOwnsStick(true, 0.04, -0.05));         // drift inside the dead zone
        Assert.False(HandPadArbiter.HandOwnsStick(true, 0.051, 0.0));         // pushed: the pad flies
        Assert.False(HandPadArbiter.HandOwnsStick(true, 0.0, -0.3));
        Assert.False(HandPadArbiter.HandOwnsStick(false, 0.0, 0.0));          // no hand on it: the pad's
        Assert.True(HandPadArbiter.HandOwnsThrottle(true));
        Assert.False(HandPadArbiter.HandOwnsThrottle(false));
    }
}
