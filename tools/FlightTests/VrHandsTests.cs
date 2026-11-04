using System;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Phase 3, hands: tracked-hand grab (pinch and fist, with hysteresis and a grace through tracking loss), the hand /
/// controller hand-off, the 2-33's dive-brake handle (lever arc, spoiler and wheel-brake mapping, knob agreement),
/// its trim lever and its tow release (spring and trigger). Engine-free. Every expected value is a literal worked by
/// hand from the geometry in the comment beside it. Cockpit frame: +x right, +y up, +z toward the nose.
/// </summary>
public sealed class VrHandsTests
{
    private const int P = 6;
    private const double Dt = 1.0 / 72.0;

    private static HandPose Step(HandGrabDetector d, double gapM, double curl) =>
        d.Step(true, Vec3.Zero, Quat.Identity, gapM, curl, Dt);

    // ---- pinch / fist hysteresis ----

    [Fact]
    public void PinchClosesUnderTwoCentimetresAndOpensOnlyAboveThreeAndAHalf()
    {
        var d = new HandGrabDetector();
        Assert.Equal(0.0, Step(d, 0.050, 0.1).Grip);
        Assert.Equal(0.0, Step(d, 0.021, 0.1).Grip);   // not yet
        Assert.Equal(1.0, Step(d, 0.019, 0.1).Grip);   // closed
        Assert.Equal(1.0, Step(d, 0.030, 0.1).Grip);   // jitter inside the band: still closed
        Assert.Equal(1.0, Step(d, 0.034, 0.1).Grip);
        Assert.Equal(0.0, Step(d, 0.036, 0.1).Grip);   // open
        Assert.Equal(0.0, Step(d, 0.030, 0.1).Grip);   // and stays open inside the band
    }

    [Fact]
    public void ANoisyPinchAtTheThresholdDoesNotChatter()
    {
        // ±7 mm of jitter about a 2.5 cm gap after a real close: never opens. Before it closes: never closes.
        var d = new HandGrabDetector();
        int flips = 0; double last = 0;
        for (int i = 0; i < 200; i++)
        {
            double gap = 0.025 + 0.007 * Math.Sin(i * 1.7);
            double g = Step(d, gap, 0.1).Grip;
            if (g != last) flips++;
            last = g;
        }
        Assert.Equal(1, flips);   // closed once when the jitter first dipped under 2.0 cm, never opened again
    }

    [Fact]
    public void AFistGrabsAndAPinchToFistChangeOverDoesNotDropIt()
    {
        var d = new HandGrabDetector();
        Assert.Equal(1.0, Step(d, 0.060, 0.70).Grip);   // fist (curl over 0.65)
        Assert.Equal(1.0, Step(d, 0.015, 0.50).Grip);   // fingers ease off, thumb on index: still held
        Assert.Equal(1.0, Step(d, 0.015, 0.20).Grip);   // a pure pinch now
        Assert.Equal(0.0, Step(d, 0.040, 0.20).Grip);   // both open: let go
    }

    [Fact]
    public void PinchStrengthRunsFromSixCentimetresOpenToTwoClosed()
    {
        Assert.Equal(0.0, HandGrabDetector.PinchStrength(0.080), P);
        Assert.Equal(0.0, HandGrabDetector.PinchStrength(0.060), P);
        Assert.Equal(0.5, HandGrabDetector.PinchStrength(0.040), P);   // (0.06 − 0.04) / (0.06 − 0.02)
        Assert.Equal(1.0, HandGrabDetector.PinchStrength(0.020), P);
        Assert.Equal(1.0, HandGrabDetector.PinchStrength(0.005), P);
    }

    [Fact]
    public void AStraightFingerHasNoCurlAndAFistHasFull()
    {
        // Straight: five joints on a line, 4 cm apart.
        Vec3 a = new(0, 0, 0), b = new(0, 0, 0.04), c = new(0, 0, 0.08), e = new(0, 0, 0.11), t = new(0, 0, 0.13);
        Assert.Equal(0.0, HandGrabDetector.FingerCurl(a, b, c, e, t), P);
        // Bent 90° at the first knuckle only (down −y), straight after: 90 / 190.
        Vec3 c2 = new(0, -0.04, 0.04), e2 = new(0, -0.07, 0.04), t2 = new(0, -0.09, 0.04);
        Assert.Equal(90.0 / 190.0, HandGrabDetector.FingerCurl(a, b, c2, e2, t2), P);
        // 90° at the first, 90° more at the second (now pointing back, −z), 10°+ is more than full: clamped to 1.
        Vec3 e3 = new(0, -0.04, 0.01), t3 = new(0, -0.03, -0.01);
        Assert.Equal(1.0, HandGrabDetector.FingerCurl(a, b, c2, e3, t3), P);
    }

    [Fact]
    public void ABriefTrackingLossKeepsTheHandAndALongOneLetsGo()
    {
        var d = new HandGrabDetector();
        Vec3 at = new(0.1, 0.2, 0.3);
        d.Step(true, at, Quat.Identity, 0.010, 0.1, Dt);
        Assert.True(d.Closed);
        // 0.20 s lost (14 frames at 72 Hz = 0.194 s): still the same hand, closed, where it was.
        HandPose h = default;
        for (int i = 0; i < 14; i++) h = d.Step(false, Vec3.Zero, Quat.Identity, double.NaN, double.NaN, Dt);
        Assert.True(h.Tracked);
        Assert.Equal(1.0, h.Grip);
        Assert.Equal(0.3, h.Position.Z, P);
        Assert.True(d.Coasting);
        // Past 0.25 s (19 frames = 0.264 s): gone.
        for (int i = 0; i < 5; i++) h = d.Step(false, Vec3.Zero, Quat.Identity, double.NaN, double.NaN, Dt);
        Assert.False(h.Tracked);
        Assert.False(d.Closed);
    }

    [Fact]
    public void AHandBackFromALongLossMustCloseAgain()
    {
        var d = new HandGrabDetector();
        d.Step(true, Vec3.Zero, Quat.Identity, 0.010, 0.1, Dt);
        for (int i = 0; i < 30; i++) d.Step(false, Vec3.Zero, Quat.Identity, double.NaN, double.NaN, Dt);
        // It comes back open: open.
        Assert.Equal(0.0, Step(d, 0.050, 0.1).Grip);
    }

    // ---- controller / hand hand-off ----

    [Fact]
    public void AControllerWinsAndAHandTakesOverOnlyOnceSettled()
    {
        var a = new HandSourceArbiter();
        Assert.Equal(HandSource.Controller, a.Step(true, true, Dt));
        // Controller put down: the hand needs 0.15 s (11 frames = 0.153 s) before it counts.
        for (int i = 0; i < 10; i++) Assert.Equal(HandSource.None, a.Step(false, true, Dt));
        Assert.Equal(HandSource.TrackedHand, a.Step(false, true, Dt));
        // A controller picked up wins at once.
        Assert.Equal(HandSource.Controller, a.Step(true, true, Dt));
        // Nothing at all: none.
        Assert.Equal(HandSource.None, a.Step(false, false, Dt));
    }

    [Fact]
    public void AHandOffLetsGoAndTheNewHandMustCloseAfresh()
    {
        var g = new CockpitGrab();
        g.Set(Grabbable.SpoilerHandle, new Vec3(0, 0, 0), true);
        HandPose ctl = new() { Source = HandSource.Controller, Position = new Vec3(0.01, 0, 0), Rotation = Quat.Identity, Grip = 0.0 };
        g.Step(HandSide.Left, ctl, false);
        ctl.Grip = 1.0;
        Assert.Equal(GrabEvents.Grabbed, g.Step(HandSide.Left, ctl, false) & GrabEvents.Grabbed);
        Assert.Equal(Grabbable.SpoilerHandle, g.Held(HandSide.Left));
        // The controller is put down, the tracked hand arrives already in a fist on the handle: released, not grabbed.
        HandPose hand = new() { Source = HandSource.TrackedHand, Position = new Vec3(0.01, 0, 0), Rotation = Quat.Identity, Grip = 1.0 };
        GrabEvents ev = g.Step(HandSide.Left, hand, false);
        Assert.True((ev & GrabEvents.Released) != 0);
        Assert.Equal(Grabbable.None, g.Held(HandSide.Left));
        g.Step(HandSide.Left, hand, false);   // still closed: still nothing
        Assert.Equal(Grabbable.None, g.Held(HandSide.Left));
        // Open, close: now it holds.
        hand.Grip = 0.0; g.Step(HandSide.Left, hand, false);
        hand.Grip = 1.0; g.Step(HandSide.Left, hand, false);
        Assert.Equal(Grabbable.SpoilerHandle, g.Held(HandSide.Left));
    }

    [Fact]
    public void TheFirstHandEverSeenGrabsAsBefore()
    {
        // No previous source: a controller closing on the handle in its first frame grabs, as Phase 1 did.
        var g = new CockpitGrab();
        g.Set(Grabbable.Stick, Vec3.Zero, true);
        HandPose ctl = new() { Source = HandSource.Controller, Position = Vec3.Zero, Rotation = Quat.Identity, Grip = 1.0 };
        g.Step(HandSide.Right, ctl, false);
        Assert.Equal(Grabbable.Stick, g.Held(HandSide.Right));
    }

    // ---- the hinged lever ----

    // A lever on a pivot at the origin, 0.20 m to the knob, upright (+y) at the min end and swung 33° AFT (−z) at max:
    // the 2-33's dive-brake handle in miniature. tip(max) = 0.2·(0, cos 33°, −sin 33°) = (0, 0.1677341, −0.1089278).
    private static HingedLever DiveBrake() => new(Vec3.Zero, new Vec3(0, 0.2, 0), new Vec3(0, 0.1677341, -0.1089278));

    [Fact]
    public void TheLeverKnowsItsArmAndSweepFromItsPoints()
    {
        HingedLever l = DiveBrake();
        Assert.Equal(0.2, l.ArmM, 5);
        Assert.Equal(33.0, l.SweepDeg, 4);
        Assert.Equal(0.0, l.GripAt(0).Z, P);
        Assert.Equal(0.2, l.GripAt(0).Y, P);
        Assert.Equal(-0.1089278, l.GripAt(1).Z, 5);
        // Half way: 16.5° aft. 0.2·(cos 16.5°, −sin 16.5°) = (0.1917650, −0.0567976).
        Assert.Equal(0.1917650, l.GripAt(0.5).Y, 5);
        Assert.Equal(-0.0567976, l.GripAt(0.5).Z, 5);
    }

    [Fact]
    public void PullingTheHandAftExtendsAndSidewaysDoesNothing()
    {
        HingedLever l = DiveBrake();
        l.Grab(new Vec3(0, 0.2, 0), 0.0);
        Assert.Equal(0.5, l.Follow(new Vec3(0, 0.1917650, -0.0567976)), 4);
        // 10 cm to the right of the same point: the hinge axis is lateral, so the same 0.5.
        Assert.Equal(0.5, l.Follow(new Vec3(0.10, 0.1917650, -0.0567976)), 4);
        // Further out along the same radius (a longer reach): the same angle.
        Assert.Equal(0.5, l.Follow(new Vec3(0, 0.1917650 * 1.5, -0.0567976 * 1.5)), 4);
    }

    [Fact]
    public void TheLeverStopsAtBothEndsAndDoesNotWrap()
    {
        HingedLever l = DiveBrake();
        l.Grab(new Vec3(0, 0.2, 0), 0.0);
        Assert.Equal(0.0, l.Follow(new Vec3(0, 0.2, 0.1)), P);      // pushed forward past stowed: stays stowed
        Assert.Equal(1.0, l.Follow(new Vec3(0, 0.0, -0.2)), P);     // 90° aft: full
        Assert.Equal(1.0, l.Follow(new Vec3(0, -0.1, -0.2)), P);    // 117° aft, below the pivot: still full, not 0
        Assert.Equal(1.0, l.Follow(new Vec3(0, -0.2, -0.01)), P);   // 177° aft: still full
    }

    [Fact]
    public void GrabbingOffTheKnobDoesNotMoveTheLever()
    {
        HingedLever l = DiveBrake();
        // The handle stands at 0.4; the hand closes 3 cm forward of the knob.
        Vec3 knob = l.GripAt(0.4);
        Vec3 hand = knob + new Vec3(0, 0, 0.03);
        l.Grab(hand, 0.4);
        Assert.Equal(0.4, l.Follow(hand), P);
    }

    [Fact]
    public void ALeverWhoseMaxIsTheOtherWayRoundStillRunsMinToMax()
    {
        // The trim lever's map is −20° .. +20°: given tip(−1) and tip(+1), +1 is whichever way the points say.
        // Pivot at the origin, 0.15 m; tip(−1) 20° forward of up, tip(+1) 20° aft. 0.15·(cos 20°, sin 20°) = (0.1409539, 0.0513030).
        var l = new HingedLever(Vec3.Zero, new Vec3(0, 0.1409539, 0.0513030), new Vec3(0, 0.1409539, -0.0513030));
        Assert.Equal(40.0, l.SweepDeg, 4);
        l.Grab(l.GripAt(0.5), 0.5);
        Assert.Equal(1.0, l.Follow(new Vec3(0, 0.1409539, -0.0513030)), 4);   // aft = max
        Assert.Equal(0.0, l.Follow(new Vec3(0, 0.1409539, 0.0513030)), 4);    // forward = min
        Assert.Equal(0.5, l.Follow(new Vec3(0, 0.15, 0)), 4);                 // upright = the middle
    }

    [Fact]
    public void AKnobOffsetAlongItsHingeStillSweepsItsRealAngle()
    {
        // The 2-33's handle: the knob 11 cm inboard (+x) of a lateral hinge, 0.30 m out, swinging 33° aft.
        // Without the axis the two points read a tilted hinge and a short sweep; with it, 33° and 0.30 m.
        Vec3 k0 = new(0.11, 0.30, 0), k1 = new(0.11, 0.30 * 0.8386706, -0.30 * 0.5446390);   // cos 33°, sin 33°
        var bare = new HingedLever(Vec3.Zero, k0, k1);
        Assert.True(bare.SweepDeg < 32.0);
        var l = new HingedLever(Vec3.Zero, k0, k1, new Vec3(-1, 0, 0));   // either sense of the axis will do
        Assert.Equal(33.0, l.SweepDeg, 4);
        Assert.Equal(0.30, l.ArmM, 6);
        Assert.Equal(0.11, Math.Abs(l.AxialM), 6);   // signed along whichever sense the axis came out
        Assert.Equal(1.0, Math.Abs(l.Axis.X), 6);
        // The knob is drawn where it really is, off the pivot plane.
        Assert.Equal(0.11, l.GripAt(0.5).X, 6);
        Assert.Equal(-0.30 * 0.2840153, l.GripAt(0.5).Z, 5);   // sin 16.5°
        l.Grab(k0, 0.0);
        Assert.Equal(1.0, l.Follow(k1), 4);
        Assert.Equal(0.5, l.Follow(l.GripAt(0.5) + new Vec3(0.05, 0, 0)), 4);   // a hand further inboard: same angle
    }

    // ---- detents and stops ----

    [Fact]
    public void TheHandFeelsTheWheelBrakeStartAndTheStops()
    {
        var f = new LeverDetents(SpoilerHandle.WheelBrakeFrom);
        f.Reset(0.0);
        Assert.Equal(LeverFeel.None, f.Step(0.5));
        Assert.Equal(LeverFeel.None, f.Step(0.84));
        Assert.Equal(LeverFeel.Detent, f.Step(0.86));   // into the wheel brake
        Assert.Equal(LeverFeel.None, f.Step(0.90));
        Assert.Equal(LeverFeel.Stop, f.Step(1.0));      // full aft
        Assert.Equal(LeverFeel.None, f.Step(1.0));      // resting on the stop: no buzz
        Assert.Equal(LeverFeel.Detent, f.Step(0.80));   // back out of the brake
        Assert.Equal(LeverFeel.Stop, f.Step(0.0));      // stowed
    }

    // ---- the spoiler handle's mapping ----

    [Fact]
    public void TheHandleIsTheSpoilerAndItsLastFifteenPercentIsTheWheelBrake()
    {
        Assert.Equal(0.0, SpoilerHandle.Spoiler(0.0), P);
        Assert.Equal(0.5, SpoilerHandle.Spoiler(0.5), P);
        Assert.Equal(1.0, SpoilerHandle.Spoiler(1.0), P);
        Assert.Equal(0.0, SpoilerHandle.WheelBrake(0.0), P);
        Assert.Equal(0.0, SpoilerHandle.WheelBrake(0.85), P);
        Assert.Equal(0.5, SpoilerHandle.WheelBrake(0.925), P);   // half way into the last 15 %
        Assert.Equal(1.0, SpoilerHandle.WheelBrake(1.0), P);
        Assert.Equal(1.0, SpoilerHandle.WheelBrake(1.2), P);
    }

    [Fact]
    public void TheKnobFollowsTheHandleIntoTheSpoilerBand()
    {
        // TouchFlightControls: spoiler = (0.5 − k) / (0.5 − idle), idle 0.25. Stowed = 0.5 (top of the band);
        // spoiler 0.4 → k = 0.5 − 0.4·0.25 = 0.40; full → 0.25.
        Assert.Equal(0.50, SpoilerHandle.KnobFor(0.0, 0.25), P);
        Assert.Equal(0.40, SpoilerHandle.KnobFor(0.4, 0.25), P);
        Assert.Equal(0.25, SpoilerHandle.KnobFor(1.0, 0.25), P);
        // And back: (0.5 − 0.40) / 0.25 = 0.4.
        Assert.Equal(0.4, (0.5 - SpoilerHandle.KnobFor(0.4, 0.25)) / 0.25, P);
    }

    [Fact]
    public void TheHandleIsDrawnWhereTheKnobPutTheSpoilerAndBrake()
    {
        Assert.Equal(0.3, SpoilerHandle.FromKnob(0.3, 0.0), P);
        Assert.Equal(1.0, SpoilerHandle.FromKnob(1.0, 1.0), P);
        Assert.Equal(0.925, SpoilerHandle.FromKnob(1.0, 0.5), P);
        // And the round trip through the handle's own brake: 0.925 → brake 0.5.
        Assert.Equal(0.5, SpoilerHandle.WheelBrake(SpoilerHandle.FromKnob(1.0, 0.5)), P);
    }

    // ---- the slide knob and the tow release ----

    [Fact]
    public void TheTowKnobPullsStraightOutFiftyMillimetres()
    {
        // Home at the origin, full out 50 mm AFT (−z).
        var k = new SlideKnob(Vec3.Zero, new Vec3(0, 0, -0.05));
        Assert.Equal(0.05, k.TravelM, P);
        k.Grab(new Vec3(0.005, 0, 0.002), 0.0);
        Assert.Equal(0.5, k.Follow(new Vec3(0.005, 0, 0.002 - 0.025)), P);       // 25 mm aft
        Assert.Equal(0.5, k.Follow(new Vec3(0.08, 0.03, 0.002 - 0.025)), P);     // sideways / up: nothing
        Assert.Equal(1.0, k.Follow(new Vec3(0, 0, -0.30)), P);                   // past the end: stops
        Assert.Equal(0.0, k.Follow(new Vec3(0, 0, 0.10)), P);                    // pushed in: home
        Assert.Equal(-0.025, k.GripAt(0.5).Z, P);
    }

    [Fact]
    public void TheReleaseFiresOnceNearTheEndOfThePull()
    {
        var k = new SpringPullKnob();
        Assert.False(k.Step(true, 0.5, Dt));
        Assert.False(k.Step(true, 0.89, Dt));
        Assert.True(k.Step(true, 0.92, Dt));    // fires
        Assert.False(k.Step(true, 1.0, Dt));    // held out: not again
        Assert.False(k.Step(true, 0.5, Dt));    // eased back but not re-armed (0.3)
        Assert.False(k.Step(true, 0.95, Dt));
        Assert.False(k.Step(true, 0.2, Dt));    // re-armed
        Assert.True(k.Step(true, 0.95, Dt));    // a second pull fires
    }

    [Fact]
    public void LetGoTheKnobSpringsHomeInThreeTenthsOfASecond()
    {
        var k = new SpringPullKnob();
        k.Step(true, 1.0, Dt);
        k.Step(false, 0, 0.15);
        Assert.Equal(0.5, k.Pull01, P);
        k.Step(false, 0, 0.15);
        Assert.Equal(0.0, k.Pull01, P);
        k.Step(false, 0, 0.15);
        Assert.Equal(0.0, k.Pull01, P);
    }

    [Fact]
    public void AKnobBrushedPartWayReleasesNothing()
    {
        var k = new SpringPullKnob();
        for (int i = 0; i < 50; i++) Assert.False(k.Step(true, 0.6, Dt));
        Assert.False(k.Step(false, 0, Dt));
    }

    // ---- the one-euro filter ----

    [Fact]
    public void TheFilterQuietsJitterButFollowsAPull()
    {
        var f = new OneEuroFilter3();
        // Held still at z = 0 with ±5 mm of noise: the output stays within ±2.5 mm after it settles.
        double worst = 0;
        for (int i = 0; i < 300; i++)
        {
            Vec3 o = f.Step(new Vec3(0, 0, 0.005 * Math.Sin(i * 2.3)), Dt);
            if (i > 50) worst = Math.Max(worst, Math.Abs(o.Z));
        }
        Assert.True(worst < 0.0025, $"held-still jitter {worst * 1000:F2} mm");
        // A 30 cm/s pull for 0.5 s: the output is within 1.5 cm of the hand at the end.
        double z = 0;
        Vec3 last = default;
        for (int i = 0; i < 36; i++) { z -= 0.30 * Dt; last = f.Step(new Vec3(0, 0, z), Dt); }
        Assert.True(Math.Abs(last.Z - z) < 0.015, $"lag {Math.Abs(last.Z - z) * 100:F1} cm");
    }
}
