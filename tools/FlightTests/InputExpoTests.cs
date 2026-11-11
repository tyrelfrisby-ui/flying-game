using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The shared stick expo (Ty, 2026-11-04: "hand flying needs some expo"). Expected numbers are literals worked by
/// hand from out = (1 − e)·x + e·x³, not read back out of <see cref="InputExpo"/>.
/// </summary>
public sealed class InputExpoTests
{
    private const double Tol = 1e-12;

    [Fact]
    public void DefaultsAreTheAskedForOnes()
    {
        Assert.Equal(0.30, InputExpo.DefaultPitchRoll);
        Assert.Equal(0.20, InputExpo.DefaultYaw);
        Assert.Equal(0.70, InputExpo.MaxStickExpo);
        // The rudder follows the one slider at 0.20 / 0.30 of it: the default 30 % gives 20 %.
        Assert.Equal(0.30, InputExpo.PitchRoll(0.30), Tol);
        Assert.Equal(0.20, InputExpo.Yaw(0.30), Tol);
        Assert.Equal(0.70, InputExpo.PitchRoll(0.70), Tol);
        Assert.Equal(0.7 * 2.0 / 3.0, InputExpo.Yaw(0.70), Tol);
        Assert.Equal(0.0, InputExpo.Yaw(0.0), Tol);
    }

    [Fact]
    public void TheSettingIsClampedToTheSlider()
    {
        Assert.Equal(0.70, InputExpo.PitchRoll(0.95), Tol);
        Assert.Equal(0.0, InputExpo.PitchRoll(-0.2), Tol);
        Assert.Equal(0.7 * 2.0 / 3.0, InputExpo.Yaw(5.0), Tol);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.2)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    public void EndpointsAreExactForEveryExpo(double e)
    {
        Assert.Equal(1.0, InputExpo.Apply(1.0, e), Tol);
        Assert.Equal(-1.0, InputExpo.Apply(-1.0, e), Tol);
        Assert.Equal(0.0, InputExpo.Apply(0.0, e), Tol);
        // Past the stop is still the stop: x is clamped first.
        Assert.Equal(1.0, InputExpo.Apply(1.4, e), Tol);
        Assert.Equal(-1.0, InputExpo.Apply(-3.0, e), Tol);
    }

    [Theory]
    [InlineData(0.5, 0.30, 0.3875)]     // 0.7·0.5 + 0.3·0.125
    [InlineData(0.5, 0.20, 0.425)]      // 0.8·0.5 + 0.2·0.125
    [InlineData(0.25, 0.30, 0.1796875)] // 0.7·0.25 + 0.3·0.015625
    [InlineData(-0.5, 0.30, -0.3875)]
    [InlineData(0.5, 0.70, 0.2375)]     // 0.3·0.5 + 0.7·0.125
    public void TheCurveIsTheCubicBlend(double x, double e, double expected) =>
        Assert.Equal(expected, InputExpo.Apply(x, e), Tol);

    [Fact]
    public void ZeroExpoIsTheIdentity()
    {
        for (double x = -1.0; x <= 1.0; x += 0.05)
            Assert.Equal(x, InputExpo.Apply(x, 0.0), Tol);
    }

    [Fact]
    public void MonotonicAcrossTheSliderRange()
    {
        for (double e = 0.0; e <= 0.7 + 1e-9; e += 0.05)
        {
            double prev = InputExpo.Apply(-1.0, e);
            for (int i = 1; i <= 400; i++)
            {
                double y = InputExpo.Apply(-1.0 + i * 0.005, e);
                Assert.True(y > prev, $"e={e:F2}: not increasing at x={-1.0 + i * 0.005:F3}");
                prev = y;
            }
        }
    }

    [Fact]
    public void OddSymmetric()
    {
        for (double e = 0.0; e <= 0.7 + 1e-9; e += 0.1)
            for (double x = 0.0; x <= 1.0; x += 0.04)
                Assert.Equal(-InputExpo.Apply(x, e), InputExpo.Apply(-x, e), Tol);
    }

    [Fact]
    public void ExpoSoftensTheMiddleOnly()
    {
        // Small stick does less than linear, by the slope at centre (1 − e) ...
        Assert.True(InputExpo.Apply(0.1, 0.3) < 0.1);
        Assert.Equal(0.0703, InputExpo.Apply(0.1, 0.3), Tol);   // 0.07 + 0.0003
        // ... and never crosses linear anywhere inside the travel.
        for (double x = 0.05; x < 1.0; x += 0.05) Assert.True(InputExpo.Apply(x, 0.3) < x);
    }

    // ---- the one place it is applied: Aircraft.ShapeAxis, AFTER the dead zone ----

    [Fact]
    public void TheSimAppliesItAfterTheDeadZone()
    {
        var axis = new ControlAxisConfig { MaxDeflRad = 0.4, DeadZone = 0.05, Expo = 0.30 };
        // Stick 0.525 → past the 0.05 zone, rescaled (0.525 − 0.05) / 0.95 = 0.5 → 0.3875 of the travel = 0.155 rad.
        Assert.Equal(0.525, Aircraft.StickForDeflection(0.155, axis), 9);
        // Full stick is full travel.
        Assert.Equal(1.0, Aircraft.StickForDeflection(0.4, axis), 9);
        // Inside the zone nothing has been bent: the first deflection still starts at the zone's edge.
        Assert.True(Aircraft.StickForDeflection(1e-6, axis) > 0.05);
    }
}
