using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The hands in the headset's cockpit: the stick's gimbal, the throttle's arc, the two canopy latches, the two-bone
/// IK and who-holds-what. Engine-free (src/FlyingGame.Core/Vr). Every expected value is a literal worked by hand
/// from the geometry in the comment beside it (CLAUDE.md: "A test that reads the file it is checking proves nothing").
/// Cockpit frame throughout: +x right, +y up, +z toward the nose.
/// </summary>
public sealed class VrCockpitTests
{
    private const int P = 6;

    // A stick on a pivot at the origin, 0.5 m to the grip, the Extra's travel: roll ±18, 18 aft, 12 forward.
    private static StickGimbal Stick() => new(Vec3.Zero, 0.5, 18.0, 18.0, 12.0);

    // ---- the stick: signs ----

    [Fact]
    public void StickUprightIsNeutral()
    {
        (double r, double p) = StickGimbal.AnglesOf(Vec3.Zero, new Vec3(0, 0.5, 0));
        Assert.Equal(0.0, r, P);
        Assert.Equal(0.0, p, P);
    }

    [Fact]
    public void HandToTheRightIsRollRight()
    {
        // 0.5·(sin 10°, cos 10°, 0)
        (double r, double p) = StickGimbal.AnglesOf(Vec3.Zero, new Vec3(0.0868241, 0.4924039, 0));
        Assert.Equal(10.0, r, 4);
        Assert.Equal(0.0, p, 4);
    }

    [Fact]
    public void HandAftIsPitchAftNoseUp()
    {
        // Aft is −z. 0.5·(0, cos 10°, −sin 10°)
        (double r, double p) = StickGimbal.AnglesOf(Vec3.Zero, new Vec3(0, 0.4924039, -0.0868241));
        Assert.Equal(0.0, r, 4);
        Assert.Equal(10.0, p, 4);
    }

    [Fact]
    public void GimbalOrderIsRollOuter()
    {
        // Hand (0.3, 0.4, −0.2): roll = atan2(0.3, 0.4) = 36.8699°, pitch = atan2(0.2, √(0.09+0.16)) = atan2(0.2, 0.5)
        // = 21.8014°. With PITCH outer the roll would be atan2(0.3, √(0.16+0.04)) = 33.8545° instead.
        (double r, double p) = StickGimbal.AnglesOf(Vec3.Zero, new Vec3(0.3, 0.4, -0.2));
        Assert.Equal(36.8699, r, 3);
        Assert.Equal(21.8014, p, 3);
    }

    [Fact]
    public void GripAtNeutralIsStraightUpAndRoundTrips()
    {
        var s = new StickGimbal(new Vec3(1, 2, 3), 0.5, 18, 18, 12);
        Vec3 g = s.GripAt(0, 0);
        Assert.Equal(1.0, g.X, P); Assert.Equal(2.5, g.Y, P); Assert.Equal(3.0, g.Z, P);
        // roll 10, pitch 5: (sin10·cos5, cos10·cos5, −sin5)·0.5 = (0.0864937, 0.4905300, −0.0435779)
        Vec3 h = s.GripAt(10, 5) - new Vec3(1, 2, 3);
        Assert.Equal(0.0864937, h.X, 6); Assert.Equal(0.4905300, h.Y, 6); Assert.Equal(-0.0435779, h.Z, 6);
        (double r, double p) = StickGimbal.AnglesOf(new Vec3(1, 2, 3), s.GripAt(10, 5));
        Assert.Equal(10.0, r, P); Assert.Equal(5.0, p, P);
    }

    // ---- the stick: limits, normalisation, grab ----

    [Fact]
    public void StickStopsAtItsLimits()
    {
        var s = Stick();
        s.Grab(new Vec3(0, 0.5, 0));
        s.Follow(new Vec3(0.5, 0.1, 0));          // atan2(0.5, 0.1) = 78.7° → the 18° stop
        Assert.Equal(18.0, s.RollDeg, P);
        Assert.Equal(1.0, s.Aileron, P);
        s.Follow(new Vec3(-0.5, 0.1, 0));
        Assert.Equal(-18.0, s.RollDeg, P);
        Assert.Equal(-1.0, s.Aileron, P);
        s.Follow(new Vec3(0, 0.1, -0.5));         // far aft → 18° aft stop
        Assert.Equal(18.0, s.PitchDeg, P);
        Assert.Equal(1.0, s.ElevatorAft, P);
        s.Follow(new Vec3(0, 0.1, 0.5));          // far forward → the 12° forward stop, not 18
        Assert.Equal(-12.0, s.PitchDeg, P);
        Assert.Equal(-1.0, s.ElevatorAft, P);
    }

    [Fact]
    public void ForwardTravelNormalisesOnItsOwnLimit()
    {
        var s = Stick();
        s.SetFromControls(0.0, -0.5);             // half forward = 6° of the 12
        Assert.Equal(-6.0, s.PitchDeg, P);
        s.SetFromControls(0.0, 0.5);              // half aft = 9° of the 18
        Assert.Equal(9.0, s.PitchDeg, P);
        s.SetFromControls(0.5, 0.0);
        Assert.Equal(9.0, s.RollDeg, P);
    }

    [Fact]
    public void GrabbingOffTheGripDoesNotJumpTheStick()
    {
        var s = Stick();
        s.SetFromControls(0.5, 0.0);              // the air has the stick at 9° right
        s.Grab(new Vec3(0, 0.5, 0));              // the hand closes straight above the pivot (0°)
        s.Follow(new Vec3(0, 0.5, 0));
        Assert.Equal(9.0, s.RollDeg, P);          // no jump
        s.Follow(new Vec3(0.0435779, 0.4980973, 0));   // hand 5° right: 0.5·(sin5, cos5)
        Assert.Equal(14.0, s.RollDeg, 4);
    }

    // ---- the throttle's arc ----

    // Pivot at the origin, 0.2 m arm, idle 20° aft of vertical, 50° sweep (the Extra's rig).
    private static LeverArc Lever() => new(Vec3.Zero, 0.2, -20.0, 50.0);

    [Fact]
    public void LeverKnobSitsOnItsArc()
    {
        var l = Lever();
        Vec3 idle = l.GripAt(0.0);                // 0.2·(0, cos −20°, sin −20°)
        Assert.Equal(0.0, idle.X, P); Assert.Equal(0.18794, idle.Y, 5); Assert.Equal(-0.06840, idle.Z, 5);
        Vec3 full = l.GripAt(1.0);                // 0.2·(0, cos 30°, sin 30°)
        Assert.Equal(0.1732051, full.Y, 6); Assert.Equal(0.1, full.Z, 6);
    }

    [Fact]
    public void HandAlongTheArcIsTheLever()
    {
        var l = Lever();
        l.Grab(l.GripAt(0.0), 0.0);
        Assert.Equal(0.5, l.Follow(new Vec3(0, 0.1992389, 0.0174311)), 4);    // 5° = idle + 25°: half
        Assert.Equal(0.5, l.Follow(new Vec3(0.3, 0.1992389, 0.0174311)), 4);  // sideways does not move a lever
        Assert.Equal(1.0, l.Follow(new Vec3(0, 0.0, 0.2)), P);               // 90°: against the full stop
        Assert.Equal(0.0, l.Follow(new Vec3(0, 0.0, -0.2)), P);              // −90°: against idle
    }

    [Fact]
    public void LeverGrabKeepsItsOffset()
    {
        var l = Lever();
        // The lever at 0.4 (−20 + 20 = 0°); the hand closes at 10°; then moves to 20°: 0 + 10 → (10 + 20)/50 = 0.6.
        l.Grab(new Vec3(0, 0.1969616, 0.0347296), 0.4);
        Assert.Equal(0.4, l.Follow(new Vec3(0, 0.1969616, 0.0347296)), 4);
        Assert.Equal(0.6, l.Follow(new Vec3(0, 0.1879385, 0.0684040)), 4);
    }

    // ---- the Pitts' sliding ball ----

    [Fact]
    public void PittsStartsLockedFullyRight()
    {
        var b = new PittsCanopyLatch();
        Assert.True(b.Locked);
        Assert.Equal(0.0762, b.PositionM, P);
        Assert.Equal(0.0, b.Latch01, P);
    }

    [Fact]
    public void PittsDetentHoldsUntilTheBreakout()
    {
        var b = new PittsCanopyLatch();
        b.Grab(0.0);
        b.Follow(-0.005);                         // 5 mm left: inside the 10 mm breakout
        Assert.True(b.Locked);
        Assert.Equal(0.0762, b.PositionM, P);
        b.Follow(-0.011);                         // 11 mm: out of the detent, at 76.2 − 11 = 65.2 mm
        Assert.False(b.Locked);
        Assert.Equal(0.0652, b.PositionM, P);
        Assert.Equal(0.1443570, b.Latch01, 6);    // 1 − 65.2/76.2
        b.Follow(-0.2);                           // against the left end
        Assert.Equal(0.0, b.PositionM, P);
        Assert.Equal(1.0, b.Latch01, P);
    }

    [Fact]
    public void PittsStaysWhereItWasLeft()
    {
        var b = new PittsCanopyLatch();
        b.Grab(0.0);
        b.Follow(-0.04);
        b.Release();
        Assert.False(b.Follow(0.5));              // not held: nothing moves it
        Assert.Equal(0.0362, b.PositionM, P);
        Assert.False(b.Locked);
    }

    [Fact]
    public void PittsClicksHomeIntoTheDetentOnce()
    {
        var b = new PittsCanopyLatch();
        b.Grab(0.0); b.Follow(-0.2); b.Release(); // open, at 0
        b.Grab(0.0);
        Assert.False(b.Follow(0.070));            // 70 mm: short of the 72.2 mm capture
        Assert.False(b.Locked);
        Assert.True(b.Follow(0.0725));            // 72.5 mm: inside 4 mm of the end → snaps home, click
        Assert.True(b.Locked);
        Assert.Equal(0.0762, b.PositionM, P);
        Assert.False(b.Follow(0.0725));           // one click only
    }

    // ---- the Extra's squeeze handles ----

    [Fact]
    public void ExtraNeedsBothHandlesSqueezedHome()
    {
        var x = new ExtraCanopyLatch();
        Assert.True(x.Locked);
        Assert.False(x.Step(0.5, 0.5, 0.0));
        Assert.False(x.Step(0.9, 0.8, 0.0));      // one handle short of 0.85
        Assert.True(x.Locked);
        Assert.Equal(0.85, x.Latch01, P);         // mean of 0.9 and 0.8
        Assert.True(x.Step(0.9, 0.9, 0.0));       // both: the pins come out (click)
        Assert.False(x.Locked);
    }

    [Fact]
    public void ExtraSpringsBackAndRelocksWhenShut()
    {
        var x = new ExtraCanopyLatch();
        x.Step(1, 1, 0);
        Assert.False(x.Step(0.6, 0.6, 0.0));      // easing off, above 0.5: still out
        Assert.False(x.Locked);
        Assert.True(x.Step(0.0, 0.0, 0.0));       // let go with the canopy shut: pins back in
        Assert.True(x.Locked);
    }

    [Fact]
    public void ExtraStaysUnlockedWhileTheCanopyIsOpen()
    {
        var x = new ExtraCanopyLatch();
        x.Step(1, 1, 0);
        Assert.False(x.Step(0, 0, 0.3));          // canopy lifted: the pins spring out against nothing
        Assert.False(x.Locked);
        Assert.True(x.Step(0, 0, 0.0));           // shut it: it latches by itself
        Assert.True(x.Locked);
    }

    // ---- two-bone IK ----

    [Fact]
    public void IkReachesA345Triangle()
    {
        // Bones 0.3 and 0.4, target 0.5 straight ahead: x = (0.09 − 0.16 + 0.25)/1.0 = 0.18, h = √(0.09 − 0.0324) = 0.24.
        var r = TwoBoneIk.Solve(Vec3.Zero, new Vec3(0, 0, 0.5), 0.3, 0.4, new Vec3(0, -1, 0));
        Assert.True(r.Reached);
        Assert.Equal(0.0, r.Middle.X, P); Assert.Equal(-0.24, r.Middle.Y, P); Assert.Equal(0.18, r.Middle.Z, P);
        Assert.Equal(0.5, r.End.Z, 5);
        Assert.Equal(0.3, r.Middle.Length, P);
        Assert.Equal(0.4, (r.End - r.Middle).Length, P);
    }

    [Fact]
    public void IkElbowFollowsThePole()
    {
        var up = TwoBoneIk.Solve(Vec3.Zero, new Vec3(0, 0, 0.5), 0.3, 0.4, new Vec3(0, 1, 0));
        Assert.Equal(0.24, up.Middle.Y, P);
        // A pole with a component along the reach line bends the same way: only its perpendicular part counts.
        var slanted = TwoBoneIk.Solve(Vec3.Zero, new Vec3(0, 0, 0.5), 0.3, 0.4, new Vec3(0, -1, 0.7));
        Assert.Equal(-0.24, slanted.Middle.Y, P);
        Assert.Equal(0.18, slanted.Middle.Z, P);
    }

    [Fact]
    public void IkOutOfReachGoesStraightAtTheTarget()
    {
        var r = TwoBoneIk.Solve(Vec3.Zero, new Vec3(0, 0, 2.0), 0.3, 0.4, new Vec3(0, -1, 0));
        Assert.False(r.Reached);
        Assert.Equal(0.7, r.End.Z, 4);            // the arm's full length, pointing at it
        Assert.Equal(0.3, r.Middle.Z, 3);
        Assert.Equal(0.0, r.Middle.Y, 2);         // straight (h ≈ 0)
        Assert.Equal(0.3, r.Middle.Length, P);
    }

    [Fact]
    public void IkTooCloseFoldsAsFarAsItGoes()
    {
        // 0.05 m is inside |0.3 − 0.4| = 0.1: d clamps to 0.1, x = (0.09 − 0.16 + 0.01)/0.2 = −0.3, h = 0.
        var r = TwoBoneIk.Solve(Vec3.Zero, new Vec3(0, 0, 0.05), 0.3, 0.4, new Vec3(0, -1, 0));
        Assert.False(r.Reached);
        Assert.Equal(0.1, r.End.Z, 4);
        Assert.Equal(-0.3, r.Middle.Z, 3);
    }

    // ---- who holds what ----

    private static HandPose Hand(double x, double grip) =>
        new() { Source = HandSource.Controller, Position = new Vec3(x, 0, 0), Rotation = Quat.Identity, Grip = grip };

    private static CockpitGrab Grab()
    {
        var g = new CockpitGrab();
        g.Set(Grabbable.Stick, Vec3.Zero, true);
        return g;
    }

    [Fact]
    public void GrabWithinSixCentimetresAndHoldUntilTheGripOpens()
    {
        var g = Grab();
        Assert.Equal(GrabEvents.HoverEnter, g.Step(HandSide.Right, Hand(0.05, 0.0), false));
        Assert.Equal(GrabEvents.Grabbed, g.Step(HandSide.Right, Hand(0.05, 0.7), false));
        Assert.Equal(Grabbable.Stick, g.Held(HandSide.Right));
        Assert.Equal(GrabEvents.None, g.Step(HandSide.Right, Hand(0.5, 0.5), false));   // far away, grip 0.5 > 0.4: still held
        Assert.Equal(Grabbable.Stick, g.Held(HandSide.Right));
        Assert.Equal(GrabEvents.Released, g.Step(HandSide.Right, Hand(0.5, 0.3), false));
        Assert.Equal(Grabbable.None, g.Held(HandSide.Right));
    }

    [Fact]
    public void SevenCentimetresIsOutOfReach()
    {
        var g = Grab();
        Assert.Equal(GrabEvents.None, g.Step(HandSide.Right, Hand(0.07, 1.0), false));
        Assert.Equal(Grabbable.None, g.Held(HandSide.Right));
    }

    [Fact]
    public void AClosedFistSweptPastDoesNotCatchIt()
    {
        var g = Grab();
        g.Step(HandSide.Right, Hand(1.0, 1.0), false);
        var ev = g.Step(HandSide.Right, Hand(0.01, 1.0), false);
        Assert.Equal(GrabEvents.HoverEnter, ev);
        Assert.Equal(Grabbable.None, g.Held(HandSide.Right));
    }

    [Fact]
    public void NothingIsGrabbedWhileTheMenuIsOpen()
    {
        var g = Grab();
        g.Step(HandSide.Right, Hand(0.0, 0.0), false);
        g.Step(HandSide.Right, Hand(0.0, 1.0), false);
        Assert.Equal(Grabbable.Stick, g.Held(HandSide.Right));
        Assert.Equal(GrabEvents.Released, g.Step(HandSide.Right, Hand(0.0, 1.0), true));   // menu opens: let go
        Assert.Equal(Grabbable.None, g.Held(HandSide.Right));
        g.Step(HandSide.Right, Hand(0.0, 0.0), true);
        Assert.Equal(GrabEvents.None, g.Step(HandSide.Right, Hand(0.0, 1.0), true));        // a fresh squeeze: still nothing
        Assert.Equal(Grabbable.None, g.Held(HandSide.Right));
    }

    [Fact]
    public void OneHandPerControl()
    {
        var g = Grab();
        g.Step(HandSide.Left, Hand(0.0, 0.0), false);
        g.Step(HandSide.Left, Hand(0.0, 1.0), false);
        g.Step(HandSide.Right, Hand(0.01, 0.0), false);
        Assert.Equal(GrabEvents.None, g.Step(HandSide.Right, Hand(0.01, 1.0), false));
        Assert.Equal(Grabbable.Stick, g.Held(HandSide.Left));
        Assert.Equal(Grabbable.None, g.Held(HandSide.Right));
    }

    [Fact]
    public void TheNearestControlWins()
    {
        var g = Grab();
        g.Set(Grabbable.Throttle, new Vec3(0.04, 0, 0), true);
        Assert.Equal(Grabbable.Throttle, g.Nearest(new Vec3(0.03, 0, 0), HandSide.Left));   // 1 cm against 3 cm
        g.Set(Grabbable.Throttle, new Vec3(0.04, 0, 0), false);
        Assert.Equal(Grabbable.Stick, g.Nearest(new Vec3(0.03, 0, 0), HandSide.Left));      // a disabled point is not there
    }

    // ---- the body and the fleet ----

    [Fact]
    public void BodyIsTheAnsurFiftiethPercentile()
    {
        Assert.Equal(0.31, SeatedPilot.UpperArmM, P);
        Assert.Equal(0.26, SeatedPilot.ForearmM, P);
        Assert.Equal(0.42, SeatedPilot.ThighM, P);
        Assert.Equal(0.45, SeatedPilot.ShinM, P);
        Assert.Equal(-0.23, SeatedPilot.ShoulderR.Y, P);
        Assert.Equal(0.17, SeatedPilot.ShoulderR.X, P);
    }

    [Fact]
    public void EveryProxyControlIsWithinReachOfTheSeat()
    {
        // Right shoulder (0.17, −0.23, −0.08) to the stick grip (0, −0.50, 0.42): √(0.0289 + 0.0729 + 0.25) = 0.5931,
        // inside 0.31 + 0.26 + 0.08 = 0.65.
        CockpitProxy x = CockpitProxy.For("pitts-s2b-like");
        Vec3 grip = x.StickPivot + new Vec3(0, x.StickLengthM, 0);
        Assert.Equal(0.5931, (grip - SeatedPilot.ShoulderR).Length, 4);
        // Left shoulder to the throttle at full: pivot (−0.26, −0.58, 0.20) + 0.2·(0, cos 30, sin 30) = (−0.26, −0.4068, 0.30);
        // offset (−0.09, −0.1768, 0.38) → 0.4287.
        var lever = new LeverArc(x.ThrottlePivot, x.ThrottleArmM, x.ThrottleIdleDeg, x.ThrottleSweepDeg);
        Assert.Equal(0.4287, (lever.GripAt(1.0) - SeatedPilot.ShoulderL).Length, 4);
        // Right ankle at full right rudder: ball (0.13, −0.85, 0.85 + 0.07) less (0, −0.08, 0.15) = (0.13, −0.77, 0.77);
        // from the hip (0.09, −0.70, −0.05): (0.04, −0.07, 0.82) → 0.8240, inside 0.42 + 0.45 = 0.87.
        Vec3 ankle = x.FootR + new Vec3(0, SeatedPilot.AnkleToBallDownM, x.PedalTravelM - SeatedPilot.AnkleToBallFwdM);
        Assert.Equal(0.8240, (ankle - SeatedPilot.HipR).Length, 4);
    }

    [Fact]
    public void LatchKindsAreTheOnesTyDescribed()
    {
        Assert.Equal(CanopyLatchKind.SlideBall, CockpitProxy.LatchFor("pitts-s2b-like"));
        Assert.Equal(CanopyLatchKind.Squeeze, CockpitProxy.LatchFor("extra-300-like"));
        Assert.Equal(CanopyLatchKind.None, CockpitProxy.LatchFor("great-lakes-like"));   // open cockpits
        Assert.Equal(CanopyLatchKind.None, CockpitProxy.LatchFor("j3-cub-like"));        // a door, not a canopy
    }

    [Fact]
    public void TheRigCanAskForTheLatch()
    {
        var r = new RigInputs { CanopyLatch = 0.25 };
        Assert.True(r.TryGet("canopy_latch", out double v));
        Assert.Equal(0.25, v, P);
    }
}
