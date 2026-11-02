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
        Assert.Equal(0.65, GamepadShaping.DefaultExpo);
        Assert.InRange(GamepadShaping.DefaultStickDeadZone, 0.05, 0.08);
        Assert.InRange(GamepadShaping.DefaultTriggerDeadZone, 0.05, 0.08);
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
        Assert.True(p < 0);   // stick pulled back toward you → negative (the caller makes that nose UP)
    }

    // ---- rudder ----

    [Fact]
    public void RightTriggerIsRightRudderAndLeftIsLeft()
    {
        Assert.Equal(1.0, GamepadShaping.Rudder(0.0, 1.0, 0.0), Tol);
        Assert.Equal(-1.0, GamepadShaping.Rudder(1.0, 0.0, 0.0), Tol);
        Assert.Equal(0.0, GamepadShaping.Rudder(1.0, 1.0, 0.0), Tol);   // both pedals pressed: they cancel
        Assert.Equal(0.0, GamepadShaping.Rudder(0.04, 0.0, 0.0), Tol);  // a resting trigger is nothing
        Assert.True(GamepadShaping.Rudder(0.0, 0.5, 0.0) > 0.0);
    }

    [Fact]
    public void RightStickIsASecondRudderAndTheSumIsClamped()
    {
        Assert.Equal(1.0, GamepadShaping.Rudder(0.0, 0.0, 1.0), Tol);
        Assert.Equal(-1.0, GamepadShaping.Rudder(0.0, 0.0, -1.0), Tol);
        Assert.Equal(1.0, GamepadShaping.Rudder(0.0, 1.0, 1.0), Tol);     // both full right: still only full
        Assert.Equal(0.0, GamepadShaping.Rudder(0.0, 1.0, -1.0), Tol);    // opposed: cancel
        double half = GamepadShaping.Expo(GamepadShaping.DeadZone(0.535, 0.05), 0.65);
        Assert.Equal(half, GamepadShaping.Rudder(0.0, 0.535, 0.0), Tol);
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

    // ---- throttle ramp ----

    [Fact]
    public void RampMovesWhileHeldAndStopsAtTheEnds()
    {
        Assert.Equal(0.3, GamepadShaping.Ramp(0.2, up: true, down: false, ratePerSec: 1.0, dt: 0.1, min: -0.5, max: 1.0), Tol);
        Assert.Equal(0.1, GamepadShaping.Ramp(0.2, up: false, down: true, ratePerSec: 1.0, dt: 0.1, min: -0.5, max: 1.0), Tol);
        Assert.Equal(0.2, GamepadShaping.Ramp(0.2, up: true, down: true, ratePerSec: 1.0, dt: 0.1, min: -0.5, max: 1.0), Tol);
        Assert.Equal(0.2, GamepadShaping.Ramp(0.2, up: false, down: false, ratePerSec: 1.0, dt: 0.1, min: -0.5, max: 1.0), Tol);
        Assert.Equal(1.0, GamepadShaping.Ramp(0.95, up: true, down: false, ratePerSec: 1.0, dt: 0.1, min: -0.5, max: 1.0), Tol);
        Assert.Equal(-0.5, GamepadShaping.Ramp(-0.45, up: false, down: true, ratePerSec: 1.0, dt: 0.1, min: -0.5, max: 1.0), Tol);
    }
}
