using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The gamepad's stick and trigger shaping (Ty, 2026-11-02: an 8BitDo Pro 2 on the iPhone). The numbers here are
/// literals worked by hand from out = (1 − e)·x + e·x³, not read back out of <see cref="GamepadShaping"/>.
/// </summary>
public sealed class GamepadShapingTests
{
    private const double Tol = 1e-12;

    [Fact]
    public void DefaultsAreTheAskedForOnes()
    {
        Assert.Equal(0.0, GamepadShaping.DefaultExpo);              // Ty 2026-11-04: was 0.65; InputExpo is the one curve now
        Assert.Equal(0.02, GamepadShaping.DefaultStickDeadZone);   // Ty 2026-11-03: was 0.07
        Assert.Equal(0.05, GamepadShaping.DefaultTriggerDeadZone);  // unchanged
        Assert.Equal(0.25, GamepadShaping.ThrottleStickDeadZone);   // unchanged: the Quest's
    }

    // ---- dead zone ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.03)]
    [InlineData(-0.069)]
    [InlineData(0.07)]
    public void InsideTheDeadZoneIsExactlyZero(double x) => Assert.Equal(0.0, GamepadShaping.DeadZone(x, 0.07));

    [Fact]
    public void DeadZoneRescalesSoTheStopIsStillFull()
    {
        Assert.Equal(1.0, GamepadShaping.DeadZone(1.0, 0.07), Tol);
        Assert.Equal(-1.0, GamepadShaping.DeadZone(-1.0, 0.07), Tol);
        Assert.Equal(0.5, GamepadShaping.DeadZone(0.535, 0.07), Tol);   // (0.535 − 0.07) / 0.93
        Assert.Equal(1.0, GamepadShaping.DeadZone(1.3, 0.07), Tol);     // past the stop: clamped, not over-full
    }

    [Fact]
    public void DeadZoneHasNoStepAtItsEdge() =>
        Assert.True(GamepadShaping.DeadZone(0.0701, 0.07) < 1e-3);

    [Fact]
    public void RadialDeadZoneKeepsTheDirection()
    {
        // A small diagonal just outside the zone: two per-axis zones of 0.07 would zero both components (each is
        // 0.0566). The radial one keeps it, on the same diagonal.
        var (x, y) = GamepadShaping.RadialDeadZone(0.08 / System.Math.Sqrt(2), 0.08 / System.Math.Sqrt(2), 0.07);
        Assert.True(x > 0 && y > 0);
        Assert.Equal(x, y, Tol);
        Assert.Equal((0.08 - 0.07) / 0.93, System.Math.Sqrt(x * x + y * y), 1e-9);
        Assert.Equal((0.0, 0.0), GamepadShaping.RadialDeadZone(0.04, -0.05, 0.07));   // |(0.04, −0.05)| = 0.064
    }

    [Fact]
    public void RadialDeadZoneReachesTheStopsOnEachAxisAndTheCorners()
    {
        Assert.Equal((1.0, 0.0), GamepadShaping.RadialDeadZone(1.0, 0.0, 0.07));
        Assert.Equal((0.0, -1.0), GamepadShaping.RadialDeadZone(0.0, -1.0, 0.07));
        var (x, y) = GamepadShaping.RadialDeadZone(1.0, 1.0, 0.07);   // a square-gated pad's corner
        Assert.Equal(1.0, x, Tol); Assert.Equal(1.0, y, Tol);
    }

    // ---- expo ----

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.25625)]        // 0.35·0.5 + 0.65·0.125
    [InlineData(-0.5, -0.25625)]
    [InlineData(0.2, 0.0752)]         // 0.35·0.2 + 0.65·0.008
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, -1.0)]
    public void ExpoAtTheDefaultIsTheCubicBlend(double x, double expected) =>
        Assert.Equal(expected, GamepadShaping.Expo(x, 0.65), Tol);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.65)]
    [InlineData(1.0)]
    public void FullDeflectionIsExactlyFullForAnyExpo(double e)
    {
        Assert.Equal(1.0, GamepadShaping.Expo(1.0, e), Tol);
        Assert.Equal(-1.0, GamepadShaping.Expo(-1.0, e), Tol);
        var (sx, sy) = GamepadShaping.Stick(1.0, -1.0, 0.07, e);
        Assert.Equal(1.0, sx, Tol); Assert.Equal(-1.0, sy, Tol);
    }

    [Fact]
    public void ExpoSoftensTheMiddle() =>
        Assert.True(GamepadShaping.Expo(0.3, 0.65) < 0.3 * 0.5);   // 0.1225: well under half the linear value

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.65)]
    [InlineData(1.0)]
    public void ShapingIsMonotonicAndOddAcrossTheWholeTravel(double e)
    {
        double prev = double.NegativeInfinity;
        for (int i = -1000; i <= 1000; i++)
        {
            double x = i / 1000.0;
            double y = GamepadShaping.Stick(x, 0.0, 0.07, e).X;
            Assert.True(y >= prev, $"not monotonic at x = {x}: {y} < {prev}");
            Assert.Equal(-y, GamepadShaping.Stick(-x, 0.0, 0.07, e).X, Tol);
            Assert.InRange(y, -1.0, 1.0);
            prev = y;
        }
    }

    [Fact]
    public void SignFollowsTheStick()
    {
        var (r, p) = GamepadShaping.Stick(0.6, -0.6);
        Assert.True(r > 0);   // stick right → roll right
        Assert.True(p < 0);   // right stick pulled back toward you → negative (the caller makes that nose UP)
    }

    // ---- rudder: left stick X, triggers as a second way ----

    [Fact]
    public void LeftStickIsTheRudderRightIsRight()
    {
        Assert.Equal(1.0, GamepadShaping.Rudder(1.0, 0.0, 0.0), Tol);
        Assert.Equal(-1.0, GamepadShaping.Rudder(-1.0, 0.0, 0.0), Tol);
        Assert.Equal(0.0, GamepadShaping.Rudder(0.015, 0.0, 0.0), Tol);   // inside the stick's 0.02 dead zone
        // No pad expo (the sim's InputExpo does it): half stick after the 0.02 zone is 0.51 → (0.51 − 0.02) / 0.98 = 0.5.
        Assert.Equal(0.5, GamepadShaping.Rudder(0.51, 0.0, 0.0), Tol);
    }

    [Fact]
    public void RightTriggerIsRightRudderAndLeftIsLeft()
    {
        Assert.Equal(1.0, GamepadShaping.Rudder(0.0, 0.0, 1.0), Tol);
        Assert.Equal(-1.0, GamepadShaping.Rudder(0.0, 1.0, 0.0), Tol);
        Assert.Equal(0.0, GamepadShaping.Rudder(0.0, 1.0, 1.0), Tol);   // both pedals pressed: they cancel
        Assert.Equal(0.0, GamepadShaping.Rudder(0.0, 0.04, 0.0), Tol);  // a resting trigger is nothing
        Assert.True(GamepadShaping.Rudder(0.0, 0.0, 0.5) > 0.0);
    }

    [Fact]
    public void TriggersAreTheLeftAndRightToeBrakes()
    {
        Assert.Equal((0.0, 0.0), GamepadShaping.TriggerBrakes(0.04, 0.0));          // resting triggers: nothing
        Assert.Equal((1.0, 0.0), GamepadShaping.TriggerBrakes(1.0, 0.0));
        Assert.Equal((0.0, 1.0), GamepadShaping.TriggerBrakes(0.0, 1.0));
        Assert.Equal(0.5, GamepadShaping.TriggerBrakes(0.525, 0.0).Left, Tol);     // proportional: (0.525 − 0.05) / 0.95
        Assert.Equal(0.0, GamepadShaping.StickRudder(0.0), Tol);                    // and they are not rudder any more
        // The left stick is the whole rudder, and LINEAR here: the pad's own expo went to zero when the one shared
        // InputExpo curve took over (Ty 2026-11-04), so (0.51 - 0.02) / 0.98 and the curve is applied by the sim.
        Assert.Equal(0.5, GamepadShaping.StickRudder(0.51), Tol);
    }

    [Fact]
    public void WheelBrakesReachTheSimPerWheel()
    {
        var (b, bias) = GamepadShaping.CombineWheelBrakes(0.0, 0.0, 1.0, 0.0);
        var (l, r) = PedalBlend.FromSim(b, bias);
        Assert.Equal(1.0, l, Tol); Assert.Equal(0.0, r, Tol);                        // left trigger: left wheel only
        (b, bias) = GamepadShaping.CombineWheelBrakes(0.0, 0.0, 0.3, 0.6);
        (l, r) = PedalBlend.FromSim(b, bias);
        Assert.Equal(0.3, l, Tol); Assert.Equal(0.6, r, Tol);
        (b, bias) = GamepadShaping.CombineWheelBrakes(1.0, 0.0, 0.2, 0.0);           // X button (both) still wins
        (l, r) = PedalBlend.FromSim(b, bias);
        Assert.Equal(1.0, l, Tol); Assert.Equal(1.0, r, Tol);
        Assert.Equal((0.4, 0.1), GamepadShaping.CombineWheelBrakes(0.4, 0.1, 0.0, 0.0));
    }

    [Fact]
    public void StickAndTriggersSumAndTheSumIsClamped()
    {
        Assert.Equal(1.0, GamepadShaping.Rudder(1.0, 0.0, 1.0), Tol);     // both full right: still only full
        Assert.Equal(0.0, GamepadShaping.Rudder(-1.0, 0.0, 1.0), Tol);    // opposed: cancel
        Assert.Equal(0.75, GamepadShaping.Rudder(0.51, 0.0, 0.2875), Tol);   // 0.5 + 0.25 (stick 0.49/0.98, trigger 0.2375/0.95)
    }

    // ---- throttle: left stick Y, a rate, linear ----

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.24, 0.0)]       // inside the Quest's 0.25 dead zone: the lever holds
    [InlineData(0.625, 0.5)]      // (0.625 − 0.25) / 0.75, and NO expo on top
    [InlineData(-0.625, -0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, -1.0)]
    public void ThrottleStickIsLinearWithADeadZone(double y, double expected) =>
        Assert.Equal(expected, GamepadShaping.ThrottleRate(y), Tol);

    [Fact]
    public void FullStickTakesTheLeverFromIdleToFullInTwoSecondsAsOnTheQuest()
    {
        Assert.Equal(0.25, GamepadShaping.ThrottleStickDeadZone);
        double idle = -0.5, full = 1.0, rate = GamepadShaping.StickLeverRatePerSec(idle, full), lever = idle;
        int frames = 0;
        while (lever < full && frames < 1000) { lever = GamepadShaping.Lever(lever, GamepadShaping.ThrottleRate(1.0), false, false, rate, 0.8, 1.0 / 60, idle, full); frames++; }
        Assert.InRange(frames, 119, 121);   // 2 s at 60 Hz
        Assert.Equal(0.5, GamepadShaping.StickLeverRatePerSec(0.0, 1.0), Tol);   // the Quest's 0…1 lever: 0.5 a second
    }

    [Fact]
    public void ThrottleRateIsMonotonic()
    {
        double prev = double.NegativeInfinity;
        for (int i = -1000; i <= 1000; i++)
        {
            double r = GamepadShaping.ThrottleRate(i / 1000.0);
            Assert.True(r >= prev);
            prev = r;
        }
    }

    // ---- the aeroplane's own dead zone ----

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.1, 0.127)]      // 0.03 + 0.1·0.97
    [InlineData(-0.5, -0.515)]
    [InlineData(1.0, 1.0)]
    public void PreCompensationLiftsPastTheDownstreamZone(double shaped, double expected) =>
        Assert.Equal(expected, GamepadShaping.PreCompensateDeadZone(shaped, 0.03), Tol);

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.2)]
    [InlineData(0.77)]
    public void DownstreamZoneHandsBackWhatWentIn(double shaped)
    {
        // Aircraft.ShapeAxis's dead zone step, applied to the compensated value: (|v| − dz) / (1 − dz).
        double v = GamepadShaping.PreCompensateDeadZone(shaped, 0.05);
        Assert.Equal(shaped, (v - 0.05) / (1.0 - 0.05), 1e-12);
    }

    // ---- the lever ----

    [Fact]
    public void LeverMovesAtTheSticksRateAndHoldsWhenLetGo()
    {
        // stick full rate 1.0/s, buttons 0.8/s, a 0.1 s frame, lever ends −0.5 (idle) … 1.
        Assert.Equal(0.3, GamepadShaping.Lever(0.2, 1.0, false, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(0.15, GamepadShaping.Lever(0.2, -0.5, false, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(0.2, GamepadShaping.Lever(0.2, 0.0, false, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);   // centred: holds
    }

    [Fact]
    public void LeverButtonsAddToTheStickAndTheEndsHold()
    {
        Assert.Equal(0.28, GamepadShaping.Lever(0.2, 0.0, true, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(0.12, GamepadShaping.Lever(0.2, 0.0, false, true, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(0.2, GamepadShaping.Lever(0.2, 0.0, true, true, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(0.38, GamepadShaping.Lever(0.2, 1.0, true, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(1.0, GamepadShaping.Lever(0.95, 1.0, false, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
        Assert.Equal(-0.5, GamepadShaping.Lever(-0.45, -1.0, false, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
    }

    [Fact]
    public void LeverAtRestIsNotPulledBackIntoRange() =>
        // The touch pad's brake band sits below idle; a pad at rest must not drag the lever up to idle.
        Assert.Equal(-0.8, GamepadShaping.Lever(-0.8, 0.0, false, false, 1.0, 0.8, 0.1, -0.5, 1.0), Tol);
}
