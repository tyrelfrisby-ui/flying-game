using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty on the Quest, 2026-11-02: the stick is "far too sensitive, especially around neutral". The headset's
/// default feel (QuestStickFeel) and the gamepad that shares the controllers' path (QuestGamepadMap). Numbers are
/// literals worked by hand from out = (1 − k)·u + k·u³. Since 2026-11-04 k is the shared stick expo (InputExpo), 0.30
/// by default on every platform; it was a headset-only 0.65 before.</summary>
public sealed class QuestStickFeelTests
{
    [Fact]
    public void TheDefaultsAreTheSharedExpoAndASmallDeadZone()
    {
        Assert.Equal(0.30, QuestStickFeel.DefaultExpo);   // Ty 2026-11-04, parity: was 0.65 in a headset only
        Assert.Equal(0.0, QuestStickFeel.DefaultExtraDeadZone);
        Assert.Equal(0.05, QuestControlMapper.StickDeadZone);
    }

    [Fact]
    public void ZeroIsZeroAndTheStopsAreFullDeflection()
    {
        Assert.Equal(0.0, QuestStickFeel.StickToCommand(0.0));
        Assert.Equal(1.0, QuestStickFeel.StickToCommand(1.0), 12);
        Assert.Equal(-1.0, QuestStickFeel.StickToCommand(-1.0), 12);
        Assert.Equal(1.0, QuestStickFeel.Curve(1.0, 0.30, 0.0), 12);
        Assert.Equal(1.0, QuestStickFeel.Curve(1.3, 0.30, 0.05), 12);   // past the stop: still full, not more
        Assert.Equal(1.0, QuestStickFeel.Curve(1.0, 0.70, 0.0), 12);    // the slider's top too
    }

    [Fact]
    public void InsideTheDeadZoneNothingAndNoJumpAtItsEdge()
    {
        Assert.Equal(0.0, QuestStickFeel.StickToCommand(0.05));
        Assert.Equal(0.0, QuestStickFeel.StickToCommand(-0.04));
        // Just past the edge the command is just past zero (rescaled), not a step.
        double justPast = QuestStickFeel.StickToCommand(0.051);
        Assert.InRange(justPast, 0.0, 0.001);
    }

    [Fact]
    public void ItRisesMonotonicallyAndIsOdd()
    {
        double prev = -2;
        for (int i = -1000; i <= 1000; i++)
        {
            double x = i / 1000.0, y = QuestStickFeel.StickToCommand(x);
            Assert.True(y >= prev, $"not monotonic at {x}");
            Assert.Equal(-y, QuestStickFeel.StickToCommand(-x), 12);
            prev = y;
        }
    }

    [Fact]
    public void TheHeadsetFliesTheSharedCurveAfterTheReadersDeadZone()
    {
        // Slope at neutral: (1 − k) / (1 − dz) = 0.70 / 0.95.
        double x = 0.06, y = QuestStickFeel.StickToCommand(x);
        Assert.Equal(0.70 * (0.01 / 0.95) + 0.30 * System.Math.Pow(0.01 / 0.95, 3), y, 12);
        // Half stick: u = 0.45 / 0.95 = 0.47368…, out = 0.70u + 0.30u³ = 0.36346….
        Assert.Equal(0.363464, QuestStickFeel.StickToCommand(0.5), 5);
        // 0.3 of stick: u = 0.25 / 0.95 = 0.26316…, out = 0.18968 (it was 0.1038 at the old headset-only 0.65).
        Assert.Equal(0.18968, QuestStickFeel.StickToCommand(0.3), 5);
    }

    [Fact]
    public void APilotsOwnSettingWinsOtherwiseTheHeadsetDefaultOtherwiseTheAircrafts()
    {
        Assert.Equal(0.4, QuestStickFeel.Resolve(0.4, 0.3, headset: true, 0.65));
        Assert.Equal(0.0, QuestStickFeel.Resolve(0.0, 0.3, headset: true, 0.65));      // 0 % is a setting, not "unset"
        Assert.Equal(0.65, QuestStickFeel.Resolve(-1, 0.3, headset: true, 0.65));
        Assert.Equal(0.3, QuestStickFeel.Resolve(-1, 0.3, headset: false, 0.65));     // phones and the Mac: unchanged
    }

    [Fact]
    public void TheGamepadFliesLikeTheControllers()
    {
        // Ty 2026-11-02, all platforms: RIGHT stick = aileron / elevator, LEFT stick X = rudder, LEFT stick Y = throttle.
        var f = QuestGamepadMap.ToFrame(new GamepadState { RightX = 0.5, RightY = 0.4, LeftX = -0.7, LeftY = 0.9, LeftTrigger = 0.3, RightTrigger = 0.8 });
        Assert.Equal(0.5, f.RightX); Assert.Equal(0.4, f.RightY);        // right stick = the flying stick (forward = push)
        Assert.Equal(-0.7, f.LeftX); Assert.Equal(0.9, f.LeftY);        // left stick = rudder + throttle lever
        Assert.Equal(0.3, f.LeftTrigger); Assert.Equal(0.8, f.RightTrigger);
        var m = new QuestControlMapper();
        var c = m.Step(f, 0.1, 0.5);
        Assert.True(c.Aileron > 0); Assert.True(c.Elevator > 0);          // right roll, push
        // Triggers are the toe brakes (2026-11-04): 0.3 past the 0.05 zone = 0.25/0.95, 0.8 = 0.75/0.95.
        Assert.Equal(0.25 / 0.95, c.BrakeLeft, 9); Assert.Equal(0.75 / 0.95, c.BrakeRight, 9);
        Assert.Equal(0.0, c.Brake, 9);
        Assert.False(c.Fire);   // the trigger no longer fires
        Assert.True(c.ThrottleMoved && c.Throttle01 > 0.5);              // left stick forward opens the lever
    }

    [Fact]
    public void TheGamepadsLeftStickIsRudderAndThrottleNotTheStick()
    {
        var m = new QuestControlMapper();
        var rud = m.Step(QuestGamepadMap.ToFrame(new GamepadState { LeftX = 0.9 }), 0.1, 0.5);
        Assert.True(rud.Rudder > 0); Assert.Equal(0.0, rud.Aileron); Assert.Equal(0.0, rud.Elevator);
        Assert.False(rud.ThrottleMoved);
        // The throttle is a rate: held forward it opens, let go it stays where it was put.
        var open = m.Step(QuestGamepadMap.ToFrame(new GamepadState { LeftY = 1.0 }), 0.2, 0.5);
        Assert.True(open.ThrottleMoved && open.Throttle01 > 0.5);
        Assert.Equal(0.0, open.Aileron); Assert.Equal(0.0, open.Elevator);
        var held = m.Step(QuestGamepadMap.ToFrame(new GamepadState()), 0.2, open.Throttle01);
        Assert.False(held.ThrottleMoved);
        var fly = new QuestControlMapper().Step(QuestGamepadMap.ToFrame(new GamepadState { RightX = -0.8, RightY = -0.8 }), 0.1, 0.5);
        Assert.True(fly.Aileron < 0); Assert.True(fly.Elevator < 0);     // right stick left and back: roll left, pull
        Assert.Equal(0.0, fly.Rudder); Assert.False(fly.ThrottleMoved);
    }

    [Fact]
    public void TheGamepadsButtonsAreTheControllersButtons()
    {
        var f = QuestGamepadMap.ToFrame(new GamepadState { South = true, West = true, LeftStickPress = true, Start = true, RightShoulder = true });
        Assert.True(f.A); Assert.True(f.X); Assert.True(f.RightStickClick); Assert.True(f.Menu); Assert.Equal(1.0, f.RightGrip);
        var c = new QuestControlMapper().Step(f, 0.02, 0.5);
        Assert.True(c.Menu); Assert.Equal(QuestMenuTrigger.MenuButton, c.MenuBy);   // Start opens the menu
        Assert.True(c.FlapsDown); Assert.True(c.Gear); Assert.True(c.Smoke); Assert.True(c.Fire);   // RB: smoke, or guns where hot
        Assert.True(c.PitchTrimRate < 0);                                           // south = A = nose up
        var r = QuestGamepadMap.ToFrame(new GamepadState { RightStickPress = true });
        Assert.True(r.LeftStickClick);                                              // the other stick's click recentres
        Assert.True(QuestGamepadMap.ToFrame(new GamepadState { Select = true }).LeftStickClick);
    }

    [Fact]
    public void MergingKeepsWhicheverIsMovingAndEitherButton()
    {
        var ctl = new QuestControllerFrame { RightX = 0.02, LeftY = 0.6, B = true, LeftHandForwardM = 0.3, LeftGrip = 0.2 };
        var pad = new QuestControllerFrame { RightX = -0.8, LeftY = 0.1, A = true, Menu = true };
        var m = QuestGamepadMap.Merge(ctl, pad, padPresent: true);
        Assert.Equal(-0.8, m.RightX);        // the resting thumb on the controller does not cancel the pad
        Assert.Equal(0.6, m.LeftY);
        Assert.True(m.A && m.B && m.Menu);
        Assert.Equal(0.3, m.LeftHandForwardM);
        Assert.Equal(0.2, m.LeftGrip);
        // No pad: the controllers' frame exactly. Hands-only stands down while a pad is in use.
        var hands = new QuestControllerFrame { HandsOnly = true, LeftPinch = 0.9 };
        Assert.True(QuestGamepadMap.Merge(hands, pad, padPresent: false).HandsOnly);
        Assert.False(QuestGamepadMap.Merge(hands, pad, padPresent: true).HandsOnly);
    }
}
