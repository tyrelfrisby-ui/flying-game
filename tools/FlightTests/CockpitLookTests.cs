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

    // ---- moving the DEVICE to look around (owner Ty, 2026-11-01) ----

    /// <summary>"1x (1 deg of device = 1 deg of view, the default)". The default is the only setting that reads as
    /// a window rather than a control, so it is the one that must not need choosing.</summary>
    [Fact]
    public void TheGainStartsAtOneForOne()
    {
        var look = new CockpitLook();
        Assert.Equal(1.0, look.DeviceGain, 9);
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Yaw(20));
        Assert.Equal(20.0, look.YawDeg, 4);
    }

    /// <summary>"up to 10x, continuous" — not a set of notches. 3.7 is a gain a pilot can actually land on with a
    /// slider, so 2 degrees of device has to be 7.4 of view.</summary>
    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(2.5, 5.0)]
    [InlineData(3.7, 7.4)]
    [InlineData(10.0, 20.0)]
    public void TheGainMultipliesTheDeviceOneForOne(double gain, double expectedYaw)
    {
        var look = new CockpitLook { DeviceGain = gain };
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Yaw(2));
        Assert.Equal(expectedYaw, look.YawDeg, 3);
    }

    /// <summary>Below 1 the device would be worse than useless and above 10 a tremor reaches the stops, so the
    /// slider's ends are the model's ends and nothing outside them can be stored.</summary>
    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-5.0, 1.0)]
    [InlineData(0.99, 1.0)]
    [InlineData(10.5, 10.0)]
    [InlineData(1e9, 10.0)]
    [InlineData(double.NaN, 1.0)]
    public void TheGainIsClampedToItsSlider(double asked, double stored)
    {
        var look = new CockpitLook { DeviceGain = asked };
        Assert.Equal(stored, look.DeviceGain, 9);
    }

    /// <summary>The gain applies to the TURN SINCE the zero, not to the absolute attitude: changing it must not
    /// move the view until the device moves. Zeroed at 30 left, held still, any gain still reads straight ahead.</summary>
    [Fact]
    public void ChangingTheGainDoesNotMoveAViewNobodyIsTurning()
    {
        var look = new CockpitLook();
        look.Recentre(Yaw(-30));
        foreach (double g in new[] { 1.0, 4.0, 10.0 })
        {
            look.DeviceGain = g;
            look.FromDevice(Yaw(-30));
            Assert.Equal(0.0, look.YawDeg, 4);
        }
    }

    /// <summary>"keep it working alongside the existing touch look". Before this they were exclusive — turning the
    /// option on silently disabled the look pad. A snap over the shoulder and a lean of the phone must ADD.</summary>
    [Fact]
    public void TheHatAndTheDeviceComposeInsteadOfOverwritingEachOther()
    {
        var look = new CockpitLook();
        look.Recentre(new Quat(0, 0, 0, 1));
        look.Snap(CockpitLook.Direction.Left);          // -90
        Assert.Equal(-90.0, look.YawDeg, 4);
        look.FromDevice(Yaw(-20));                      // lean the phone another 20 left
        Assert.Equal(-110.0, look.YawDeg, 3);
        Assert.Equal(-90.0, look.ManualYawDeg, 4);      // the two halves stay separate
        Assert.Equal(-20.0, look.DeviceYawDeg, 3);
        // And the device holding still does not stop the hat panning on top of it.
        look.Hat(1, 0, 0.5);                            // +70 at the default 140 deg/s
        Assert.Equal(-40.0, look.YawDeg, 3);
    }

    /// <summary>Letting go of the hat must not snatch the view away from where the phone is pointing: the pilot's
    /// hands are still, so the view should be too. Spring-centre returns the HAT's offset only.</summary>
    [Fact]
    public void SpringCentreReturnsTheHatButLeavesTheDeviceWhereItIs()
    {
        var look = new CockpitLook { SpringCentre = true };
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Yaw(25));
        look.Snap(CockpitLook.Direction.Right);         // +90 on top of the device's 25
        Assert.Equal(115.0, look.YawDeg, 3);
        for (int i = 0; i < 200; i++) look.Hat(0, 0, 0.02);
        Assert.Equal(0.0, look.ManualYawDeg, 6);
        Assert.Equal(25.0, look.YawDeg, 3);
    }

    /// <summary>At 10x a quarter turn of the wrist asks for 900 degrees. The view stops at the neck, and — the part
    /// that matters — turning back by the same amount brings it straight back, instead of making the pilot unwind
    /// hundreds of degrees of stored offset first.</summary>
    [Fact]
    public void AHighGainStopsAtTheNeckAndComesStraightBack()
    {
        var look = new CockpitLook { DeviceGain = 10.0 };
        look.Recentre(new Quat(0, 0, 0, 1));
        look.FromDevice(Yaw(90));
        Assert.Equal(CockpitLook.MaxYawDeg, look.YawDeg, 6);
        look.FromDevice(Yaw(10));
        Assert.Equal(100.0, look.YawDeg, 3);
        look.FromDevice(new Quat(0, 0, 0, 1));
        Assert.Equal(0.0, look.YawDeg, 4);
    }

    /// <summary>However the two halves are stacked, the view stays inside a neck — including the sum, which is the
    /// case a per-source clamp would miss.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void TheSumOfBothHalvesIsStillInsideTheNeck(double gain)
    {
        var look = new CockpitLook { DeviceGain = gain };
        look.Recentre(new Quat(0, 0, 0, 1));
        look.Snap(CockpitLook.Direction.OverRightShoulder);
        look.FromDevice(Yaw(80));
        Assert.InRange(look.YawDeg, -CockpitLook.MaxYawDeg, CockpitLook.MaxYawDeg);
        look.Snap(CockpitLook.Direction.Up);
        look.FromDevice(Pitch(80));
        Assert.InRange(look.PitchDeg, -CockpitLook.MaxPitchDownDeg, CockpitLook.MaxPitchUpDeg);
    }

    /// <summary>RECENTRE is the one call that zeroes everything: the hat's offset AND the device's, against however
    /// the device is being held at that moment.</summary>
    [Fact]
    public void RecentreZeroesBothHalvesAtWhateverTheDeviceIsDoing()
    {
        var look = new CockpitLook { DeviceGain = 5.0 };
        look.Recentre(new Quat(0, 0, 0, 1));
        look.Snap(CockpitLook.Direction.Right);
        look.FromDevice(Yaw(10));
        Assert.True(look.LookingAway);
        look.Recentre(Yaw(10));
        Assert.Equal(0.0, look.YawDeg, 6);
        Assert.Equal(0.0, look.ManualYawDeg, 9);
        Assert.Equal(0.0, look.DeviceYawDeg, 9);
        Assert.False(look.LookingAway);
    }

    /// <summary>Switching the option off must not leave the device's last offset holding the view off-centre with
    /// nothing driving it, and the next time it is switched on the first reading becomes the new zero.</summary>
    [Fact]
    public void TurningTheDeviceOffDropsItsOffsetAndItsZero()
    {
        var look = new CockpitLook();
        look.Recentre(new Quat(0, 0, 0, 1));
        look.Snap(CockpitLook.Direction.Left);
        look.FromDevice(Yaw(30));
        Assert.Equal(-60.0, look.YawDeg, 3);
        look.ForgetDevice();
        Assert.False(look.HasReference);
        Assert.Equal(-90.0, look.YawDeg, 4);            // the hat's own offset survives, as it should
        look.FromDevice(Yaw(30));                        // switched back on, wherever he is holding it
        Assert.Equal(-90.0, look.YawDeg, 4);
    }
}
