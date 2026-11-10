using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Flying by tilting the phone (Ty 2026-11-08, for a beta tester). The sensors and the reference are the phone's
/// business; this is the half that can be held to account without one — an angle away from where the pilot centred
/// the phone becoming a stick command.
/// </summary>
public class TiltControlsTests
{
    private const double Full = TiltControls.DefaultFullScaleDeg;   // 30
    private const double Dz = TiltControls.DefaultDeadzoneDeg;      // 2

    [Fact]
    public void HeldWhereItWasCentredTheStickIsCentred()
    {
        Assert.Equal(0.0, TiltControls.Axis(0.0, Full));
    }

    [Fact]
    public void AHandThatIsMerelyNotPerfectlyStillDoesNotFly()
    {
        Assert.Equal(0.0, TiltControls.Axis(Dz * 0.99, Full));
        Assert.Equal(0.0, TiltControls.Axis(-Dz * 0.99, Full));
    }

    [Fact]
    public void FullScaleTiltIsFullDeflection()
    {
        Assert.Equal(1.0, TiltControls.Axis(Full, Full), 1e-9);
        Assert.Equal(-1.0, TiltControls.Axis(-Full, Full), 1e-9);
    }

    [Fact]
    public void TurningThePhoneFurtherThanFullScaleIsStillFullDeflection()
    {
        // Rolling the phone right over must not wrap, invert or exceed the stop.
        foreach (double deg in new[] { Full + 1, 90.0, 179.0, 1000.0 })
        {
            Assert.Equal(1.0, TiltControls.Axis(deg, Full), 1e-9);
            Assert.Equal(-1.0, TiltControls.Axis(-deg, Full), 1e-9);
        }
    }

    [Fact]
    public void TiltingRightRollsRightAndTiltingLeftRollsLeft()
    {
        Assert.True(TiltControls.Axis(10, Full) > 0);
        Assert.True(TiltControls.Axis(-10, Full) < 0);
    }

    [Fact]
    public void TheFirstDegreePastTheDeadzoneCommandsALittleAndNotALurch()
    {
        double justOut = TiltControls.Axis(Dz + 0.5, Full);
        Assert.True(justOut > 0.0 && justOut < 0.05, $"a half degree past the deadzone gave {justOut:F3}");
    }

    [Fact]
    public void ItOnlyEverGrowsWithTheTilt()
    {
        double previous = -1.0;
        for (double d = 0; d <= Full * 1.5; d += 0.5)
        {
            double now = TiltControls.Axis(d, Full);
            Assert.True(now >= previous - 1e-12, $"the command went backwards at {d}°");
            previous = now;
        }
    }

    [Fact]
    public void ExpoSoftensTheMiddleWithoutMovingTheStop()
    {
        double mid = Full * 0.5;
        Assert.True(TiltControls.Axis(mid, Full, Dz, 0.6) < TiltControls.Axis(mid, Full, Dz, 0.0));
        Assert.Equal(1.0, TiltControls.Axis(Full, Full, Dz, 0.6), 1e-9);
    }

    // ---- the sensitivity slider ------------------------------------------------------------------

    [Fact]
    public void MoreSensitiveMeansLessTiltForTheSameCommand()
    {
        double sensitive = TiltControls.FullScaleForSensitivity(1.0);
        double gentle = TiltControls.FullScaleForSensitivity(0.0);
        Assert.True(sensitive < gentle, "the sensitive end must need LESS tilt");
        Assert.Equal(TiltControls.MinFullScaleDeg, sensitive, 1e-9);
        Assert.Equal(TiltControls.MaxFullScaleDeg, gentle, 1e-9);
        Assert.True(TiltControls.Axis(10, sensitive) > TiltControls.Axis(10, gentle));
    }

    [Fact]
    public void TheSliderAndTheAngleAgreeBothWays()
    {
        for (double t = 0; t <= 1.0; t += 0.1)
            Assert.Equal(t, TiltControls.SensitivityForFullScale(TiltControls.FullScaleForSensitivity(t)), 1e-9);
    }

    [Fact]
    public void ANonsenseReadingIsNotAControlInput()
    {
        Assert.Equal(0.0, TiltControls.Axis(double.NaN, Full));
    }
}
