using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Looking around from the seat (owner 2026-10-31: "looking around either by buttons like a hat control on a stick or
/// by goggles"). The arithmetic is pure, so all of it is tested here rather than on a phone.
/// </summary>
public sealed class CockpitLookTests
{
    private static Quat Yaw(double deg)
    {
        double h = deg * System.Math.PI / 360.0;
        return new Quat(0, 0, System.Math.Sin(h), System.Math.Cos(h));
    }

    private static Quat Pitch(double deg)
    {
        double h = deg * System.Math.PI / 360.0;
        return new Quat(0, System.Math.Sin(h), 0, System.Math.Cos(h));
    }

    [Fact]
    public void ItStartsLookingStraightAhead()
    {
        var look = new CockpitLook();
        Assert.Equal(0.0, look.YawDeg, 9);
        Assert.Equal(0.0, look.PitchDeg, 9);
        Assert.False(look.LookingAway);
    }

    [Fact]
    public void AHeldHatPansAtItsOwnRateAndADiagonalIsNoFaster()
    {
        var look = new CockpitLook { RateDegPerSec = 100 };
        look.Hat(1, 0, 0.5);
        Assert.Equal(50.0, look.YawDeg, 6);

        var diag = new CockpitLook { RateDegPerSec = 100 };
        diag.Hat(1, 1, 0.5);
        double speed = System.Math.Sqrt(diag.YawDeg * diag.YawDeg + diag.PitchDeg * diag.PitchDeg);
        Assert.Equal(50.0, speed, 3);
    }

    [Fact]
    public void TheHatCannotPanThePilotsHeadOffHisNeck()
    {
        var look = new CockpitLook { RateDegPerSec = 1000 };
        look.Hat(1, 0, 10.0);
        Assert.Equal(CockpitLook.MaxYawDeg, look.YawDeg, 6);
        look.Hat(0, 1, 10.0);
        Assert.Equal(CockpitLook.MaxPitchUpDeg, look.PitchDeg, 6);
        look.Hat(0, -1, 10.0);
        Assert.Equal(-CockpitLook.MaxPitchDownDeg, look.PitchDeg, 6);
        // And a pilot can look further up than down, which is what a lap and a seat pan do.
        Assert.True(CockpitLook.MaxPitchUpDeg > CockpitLook.MaxPitchDownDeg);
    }

    [Fact]
    public void LettingGoHoldsTheViewUnlessTheHatIsSetToSelfCentre()
    {
        var held = new CockpitLook { RateDegPerSec = 100 };
        held.Hat(1, 0, 0.5);
        held.Hat(0, 0, 5.0);
        Assert.Equal(50.0, held.YawDeg, 6);

        var spring = new CockpitLook { RateDegPerSec = 100, SpringCentre = true, CentreRateDegPerSec = 200 };
        spring.Hat(1, 0, 0.5);
        spring.Hat(0, 0, 0.1);                       // 20 deg back
        Assert.Equal(30.0, spring.YawDeg, 6);
        spring.Hat(0, 0, 5.0);                       // all the way, and not past
        Assert.Equal(0.0, spring.YawDeg, 9);
    }

    [Theory]
    [InlineData(CockpitLook.Direction.Left, true)]
    [InlineData(CockpitLook.Direction.Right, false)]
    [InlineData(CockpitLook.Direction.OverLeftShoulder, true)]
    [InlineData(CockpitLook.Direction.OverRightShoulder, false)]
    public void ATappedHatJumpsToItsDirectionAndLeftIsNegative(CockpitLook.Direction d, bool expectLeft)
    {
        var look = new CockpitLook();
        look.Snap(d);
        Assert.True(expectLeft ? look.YawDeg < 0 : look.YawDeg > 0, $"{d} gave yaw {look.YawDeg}");
        Assert.True(System.Math.Abs(look.YawDeg) <= CockpitLook.MaxYawDeg);
    }

    [Fact]
    public void EveryTappedDirectionIsInsideTheNeckAndCentreIsCentre()
    {
        foreach (CockpitLook.Direction d in System.Enum.GetValues<CockpitLook.Direction>())
        {
            (double yaw, double pitch) = CockpitLook.Of(d);
            Assert.InRange(yaw, -CockpitLook.MaxYawDeg, CockpitLook.MaxYawDeg);
            Assert.InRange(pitch, -CockpitLook.MaxPitchDownDeg, CockpitLook.MaxPitchUpDeg);
        }
        var look = new CockpitLook();
        look.Snap(CockpitLook.Direction.Right);
        look.Snap(CockpitLook.Direction.Centre);
        Assert.False(look.LookingAway);
    }

    // ---- goggles ----

    /// <summary>The goggles measure the CHANGE since they were zeroed, so the pilot can hold the device however he is
    /// sitting. Zeroed facing 30 degrees left, turning to 60 left is a 30 degree look, not a 60 degree one.</summary>
    [Fact]
    public void GogglesMeasureTheTurnSinceTheyWereZeroedNotAnAbsoluteDirection()
    {
        var look = new CockpitLook();
        look.Recentre(Yaw(-30));
        Assert.Equal(0.0, look.YawDeg, 6);
        look.FromDevice(Yaw(-60));
        Assert.Equal(-30.0, look.YawDeg, 4);
        look.FromDevice(Yaw(-30));
        Assert.Equal(0.0, look.YawDeg, 4);
        look.FromDevice(Yaw(20));
        Assert.Equal(50.0, look.YawDeg, 4);
    }

    [Fact]
    public void GogglesFollowPitchTooAndTheFirstReadingZeroesThem()
    {
        var look = new CockpitLook();
        Assert.False(look.HasReference);
        look.FromDevice(Pitch(15));             // first ever reading becomes the zero
        Assert.True(look.HasReference);
        Assert.Equal(0.0, look.PitchDeg, 6);
        look.FromDevice(Pitch(40));
        Assert.Equal(25.0, look.PitchDeg, 4);
    }

    [Fact]
    public void GogglesAreClampedToTheSameNeckAsTheHat()
    {
        var look = new CockpitLook();
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Yaw(179));
        Assert.InRange(look.YawDeg, -CockpitLook.MaxYawDeg, CockpitLook.MaxYawDeg);
        look.FromDevice(Pitch(89));
        Assert.InRange(look.PitchDeg, -CockpitLook.MaxPitchDownDeg, CockpitLook.MaxPitchUpDeg);
    }

    /// <summary>A device pointed straight up is where a yaw/pitch extraction goes singular. It must report a
    /// straight-up look, not a NaN that would take the camera with it.</summary>
    [Fact]
    public void ADevicePointedStraightUpDoesNotProduceANan()
    {
        var look = new CockpitLook();
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Pitch(90));
        Assert.True(double.IsFinite(look.YawDeg) && double.IsFinite(look.PitchDeg));
        Assert.Equal(CockpitLook.MaxPitchUpDeg, look.PitchDeg, 6);
    }

    [Fact]
    public void ANonFiniteLookIsRefusedRatherThanStored()
    {
        var look = new CockpitLook();
        look.Snap(CockpitLook.Direction.Right);
        look.Set(double.NaN, double.PositiveInfinity);
        Assert.Equal(0.0, look.YawDeg, 9);
        Assert.Equal(0.0, look.PitchDeg, 9);
    }

    [Fact]
    public void RecentringTheGogglesAlsoCentresTheView()
    {
        var look = new CockpitLook();
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Yaw(40));
        Assert.True(look.LookingAway);
        look.Recentre(Yaw(40));                 // zero it where he is now holding it
        Assert.False(look.LookingAway);
        look.FromDevice(Yaw(40));
        Assert.Equal(0.0, look.YawDeg, 4);
    }
}
