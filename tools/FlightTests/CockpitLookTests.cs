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
    // ---- synthetic device attitudes, as CoreMotion / Input.gyro.attitude would report them ----
    //
    // The attitude frame's identity is the phone LYING FLAT, SCREEN UP: +x across the screen, +y toward the top of
    // the screen, +z out of the screen, which is straight up. So the back of the phone faces (0, 0, -1).

    private static Quat About(double ax, double ay, double az, double deg)
    {
        double h = deg * System.Math.PI / 360.0, s = System.Math.Sin(h);
        return new Quat(ax * s, ay * s, az * s, System.Math.Cos(h));
    }

    /// <summary>
    /// A phone held up like a window and aimed somewhere: turned <paramref name="turnRightDeg"/> to the pilot's
    /// right, tilted <paramref name="tiltUpDeg"/> so its back aims that much above the horizon, and rolled
    /// <paramref name="rollDeg"/> in its own plane — which is all that separates portrait from landscape.
    ///
    /// Built the way the movement actually happens: the world turns and tilts pre-multiply, the phone's own roll
    /// post-multiplies, and the 90 degrees about x in the middle is what stands the phone up from flat.
    /// </summary>
    private static Quat Held(double turnRightDeg, double tiltUpDeg, double rollDeg) =>
        Quat.Multiply(
            Quat.Multiply(About(0, 0, 1, -turnRightDeg), Quat.Multiply(About(1, 0, 0, tiltUpDeg), About(1, 0, 0, 90))),
            About(0, 0, 1, rollDeg));

    /// <summary>A look with the gain pinned at ONE FOR ONE. Most of this file is about DIRECTION — is a turn a
    /// pure yaw, does roll do nothing, do the two halves compose — and those answers must not move when the
    /// default gain changes. They used to read the default implicitly, so raising it to 3.5 multiplied a dozen
    /// unrelated expectations by 3.5. The gain's own tests construct their own.</summary>
    private static CockpitLook OneToOne() => new() { DeviceGain = CockpitLook.MinDeviceGain };

    /// <summary>The roll that each screen orientation puts on the phone. Portrait is the frame's own zero; the two
    /// landscapes are a quarter turn either way; upside down is a half turn.</summary>
    public static TheoryData<string, double> Orientations => new()
    {
        { "portrait", 0.0 },
        { "landscape left", 90.0 },
        { "landscape right", -90.0 },
        { "portrait upside down", 180.0 },
    };

    [Fact]
    public void ItStartsLookingStraightAhead()
    {
        var look = OneToOne();
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
        var look = OneToOne();
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
        var look = OneToOne();
        look.Snap(CockpitLook.Direction.Right);
        look.Snap(CockpitLook.Direction.Centre);
        Assert.False(look.LookingAway);
    }

    // ---- the device: where the phone is AIMED ----
    //
    // Ty flew build 2026.1101.0549 and reported three things: turning the phone and rolling it did the same thing,
    // neither moved the view purely (everything came out diagonal, down-left or up-right), and tilting it did
    // nothing at all. Every test in this section is one of those three sentences.

    /// <summary>Turning the phone about the vertical turns the view, and does NOTHING else. Any pitch here is the
    /// cross-axis leakage that made every movement diagonal.</summary>
    [Theory]
    [InlineData(0.0, 10.0)]
    [InlineData(0.0, -25.0)]
    [InlineData(90.0, 10.0)]
    [InlineData(90.0, -25.0)]
    [InlineData(-90.0, 40.0)]
    [InlineData(180.0, -40.0)]
    public void TurningTheDeviceGivesPureYaw(double rollDeg, double turnDeg)
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, rollDeg));
        look.FromDevice(Held(turnDeg, 0, rollDeg));
        Assert.Equal(turnDeg, look.YawDeg, 3);
        Assert.Equal(0.0, look.PitchDeg, 6);
    }

    /// <summary>Tilting the phone toward or away pitches the view, and does NOTHING else. This is the one that was
    /// ignored entirely: it landed on the roll component of a device-frame extraction and was thrown away.</summary>
    [Theory]
    [InlineData(0.0, 15.0)]
    [InlineData(0.0, -15.0)]
    [InlineData(90.0, 30.0)]
    [InlineData(-90.0, -30.0)]
    [InlineData(180.0, 45.0)]
    public void TiltingTheDeviceGivesPurePitch(double rollDeg, double tiltDeg)
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, rollDeg));
        look.FromDevice(Held(0, tiltDeg, rollDeg));
        Assert.Equal(tiltDeg, look.PitchDeg, 3);
        Assert.Equal(0.0, look.YawDeg, 6);
    }

    /// <summary>Rolling the phone in its own plane does not change where it is AIMED, so the view does not move.
    /// This is the one that used to feel identical to turning.</summary>
    [Theory]
    [InlineData(30.0)]
    [InlineData(-30.0)]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    [InlineData(180.0)]
    public void RollingTheDeviceMovesNothing(double rollDeg)
    {
        var look = OneToOne();
        look.Recentre(Held(20, 10, 0));
        look.FromDevice(Held(20, 10, rollDeg));
        Assert.Equal(0.0, look.YawDeg, 6);
        Assert.Equal(0.0, look.PitchDeg, 6);
    }

    /// <summary>The bug in one line: turning and rolling are different movements and must not give the same view.</summary>
    [Fact]
    public void TurningAndRollingAreNotTheSameMovement()
    {
        var turn = OneToOne(); turn.Recentre(Held(0, 0, 0)); turn.FromDevice(Held(30, 0, 0));
        var roll = OneToOne(); roll.Recentre(Held(0, 0, 0)); roll.FromDevice(Held(0, 0, 30));
        Assert.Equal(30.0, turn.YawDeg, 3);
        Assert.Equal(0.0, roll.YawDeg, 6);
        Assert.True(System.Math.Abs(turn.YawDeg - roll.YawDeg) > 25.0,
            $"turning gave {turn.YawDeg:F1} deg and rolling gave {roll.YawDeg:F1} — they are the same movement again");
    }

    /// <summary>Portrait and landscape must behave identically, because holding the phone the other way up IS a
    /// roll about the aim and the aim is what the view follows. No orientation offset is needed, and this is the
    /// test that says so rather than leaving it to be rediscovered.</summary>
    [Theory]
    [MemberData(nameof(Orientations))]
    public void EveryScreenOrientationGivesTheSameView(string name, double rollDeg)
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, rollDeg));
        look.FromDevice(Held(35, -20, rollDeg));
        Assert.Equal(35.0, look.YawDeg, 3);
        Assert.Equal(-20.0, look.PitchDeg, 3);
        Assert.False(string.IsNullOrEmpty(name));
    }

    /// <summary>Turn AND tilt at once: each still reads its own movement, with nothing of the other in it.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    public void TurningAndTiltingTogetherStayIndependent(double rollDeg)
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, rollDeg));
        look.FromDevice(Held(45, 25, rollDeg));
        Assert.Equal(45.0, look.YawDeg, 3);
        Assert.Equal(25.0, look.PitchDeg, 3);
    }

    /// <summary>
    /// THE ONE THAT WOULD HAVE CAUGHT IT. Zero from an awkward pose — turned 37, tilted 23, rolled 61, which is
    /// just "however he happened to be holding it" — and a pure turn from there must STILL be a pure yaw.
    ///
    /// The old code read yaw and pitch off `reference⁻¹ · current`, whose axes are the phone's own, so the answer
    /// depended on the reference pose: from a canonical pose it looked almost right, and from a real one every
    /// movement came out diagonal. Every test above zeroes from a tidy pose and would have passed either way.
    /// </summary>
    [Theory]
    [InlineData(37.0, 23.0, 61.0, 20.0, 0.0)]
    [InlineData(37.0, 23.0, 61.0, 0.0, 20.0)]
    [InlineData(-112.0, -41.0, -168.0, 15.0, 0.0)]
    [InlineData(-112.0, -41.0, -168.0, 0.0, -15.0)]
    [InlineData(5.0, 62.0, 90.0, -30.0, 0.0)]
    public void FromAnyPoseAMovementIsStillPure(double turn0, double tilt0, double roll0, double dTurn, double dTilt)
    {
        var look = OneToOne();
        look.Recentre(Held(turn0, tilt0, roll0));
        look.FromDevice(Held(turn0 + dTurn, tilt0 + dTilt, roll0));
        Assert.Equal(dTurn, look.YawDeg, 3);
        Assert.Equal(dTilt, look.PitchDeg, 3);
    }

    /// <summary>It measures the CHANGE since it was zeroed, so the pilot can sit however he likes. Zeroed aiming
    /// 30 to the right, aiming 60 right is a 30 degree look, not a 60 degree one.</summary>
    [Fact]
    public void ItMeasuresTheTurnSinceItWasZeroedNotAnAbsoluteDirection()
    {
        var look = OneToOne();
        look.Recentre(Held(30, 0, 0));
        Assert.Equal(0.0, look.YawDeg, 6);
        look.FromDevice(Held(60, 0, 0));
        Assert.Equal(30.0, look.YawDeg, 3);
        look.FromDevice(Held(30, 0, 0));
        Assert.Equal(0.0, look.YawDeg, 4);
        look.FromDevice(Held(10, 0, 0));
        Assert.Equal(-20.0, look.YawDeg, 3);
    }

    [Fact]
    public void TheFirstReadingBecomesTheZero()
    {
        var look = OneToOne();
        Assert.False(look.HasReference);
        look.FromDevice(Held(0, 15, 0));
        Assert.True(look.HasReference);
        Assert.Equal(0.0, look.PitchDeg, 6);
        look.FromDevice(Held(0, 40, 0));
        Assert.Equal(25.0, look.PitchDeg, 3);
    }

    [Fact]
    public void TheDeviceIsClampedToTheSameNeckAsTheHat()
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, 0));
        look.FromDevice(Held(179, 0, 0));
        Assert.InRange(look.YawDeg, -CockpitLook.MaxYawDeg, CockpitLook.MaxYawDeg);
        look.FromDevice(Held(0, 89, 0));
        Assert.InRange(look.PitchDeg, -CockpitLook.MaxPitchDownDeg, CockpitLook.MaxPitchUpDeg);
    }

    /// <summary>A phone laid flat on a knee is aimed at the floor, and a direction straight down has no bearing.
    /// The yaw must HOLD there rather than spinning or going NaN, and the pitch must still read.</summary>
    [Fact]
    public void APhoneLaidFlatHoldsItsYawInsteadOfSpinning()
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, 0));
        look.FromDevice(Held(40, 0, 0));
        Assert.Equal(40.0, look.YawDeg, 3);
        // Flat on the knee, aimed at the floor. A REAL flat reading, not the bit-exact identity: that one is the
        // gyro's "no sample yet" placeholder and is ignored (CockpitLook.IsUnsampled, 2026-11-02). Turning about the
        // vertical leaves the back pointing straight down, so this is still exactly "aimed at the floor".
        look.FromDevice(About(0, 0, 1, 0.001));
        Assert.True(double.IsFinite(look.YawDeg) && double.IsFinite(look.PitchDeg));
        Assert.Equal(40.0, look.YawDeg, 3);             // held, not spun
        Assert.Equal(-CockpitLook.MaxPitchDownDeg, look.PitchDeg, 6);
    }

    [Fact]
    public void ANonFiniteLookIsRefusedRatherThanStored()
    {
        var look = OneToOne();
        look.Snap(CockpitLook.Direction.Right);
        look.Set(double.NaN, double.PositiveInfinity);
        Assert.Equal(0.0, look.YawDeg, 9);
        Assert.Equal(0.0, look.PitchDeg, 9);
    }

    // ---- the gain, and living alongside the touch look (owner Ty, 2026-11-01) ----

    /// <summary>The gain a pilot gets before he touches anything. It was 1.0 — one degree of view per degree of
    /// phone — because every default read the slider's FLOOR. Ty flew it at 1.0, 7.0 and 5.0 and asked for "3 to
    /// 4" (2026-11-01), so it is 3.5, and the floor is still 1.0 for anyone who wants the window.</summary>
    [Fact]
    public void TheGainStartsWhereTyAskedForIt()
    {
        var look = new CockpitLook();
        Assert.Equal(3.5, look.DeviceGain, 9);
        Assert.Equal(3.5, CockpitLook.DefaultDeviceGain, 9);
        look.Recentre(Held(0, 0, 0));
        look.FromDevice(Held(20, 0, 0));
        Assert.Equal(70.0, look.YawDeg, 3);          // 20 degrees of phone, 70 of view
    }

    /// <summary>The default is NOT the floor. They were the same constant, which is how the feature shipped at
    /// 1.0, and a slider whose bottom end is also its default hides that it was never chosen.</summary>
    [Fact]
    public void TheDefaultIsItsOwnNumberInsideTheSlidersTravel()
    {
        Assert.NotEqual(CockpitLook.MinDeviceGain, CockpitLook.DefaultDeviceGain);
        Assert.InRange(CockpitLook.DefaultDeviceGain, CockpitLook.MinDeviceGain, CockpitLook.MaxDeviceGain);
        Assert.InRange(CockpitLook.DefaultDeviceGain, 3.0, 4.0);     // Ty asked for 3 to 4
        // ...and the slider the Options panel draws spans the whole range, so 3.5 is reachable by hand and so
        // are both ends.
        var look = new CockpitLook();
        foreach (double g in new[] { CockpitLook.MinDeviceGain, 3.5, CockpitLook.MaxDeviceGain })
        {
            look.DeviceGain = g;
            Assert.Equal(g, look.DeviceGain, 9);
        }
    }

    /// <summary>A saved setting still wins: assigning any legal value overrides the default and sticks, so an
    /// existing pref is neither migrated nor reset by the default moving.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(7.0)]
    public void ASavedGainOverridesTheDefault(double saved)
    {
        var look = new CockpitLook { DeviceGain = saved };
        Assert.Equal(saved, look.DeviceGain, 9);
        look.Recentre(Held(0, 0, 0));
        look.FromDevice(Held(2, 0, 0));
        Assert.Equal(2.0 * saved, look.YawDeg, 3);
    }

    /// <summary>"up to 10x, continuous" — not a set of notches. 3.7 is a gain a pilot can land on with a slider,
    /// so 2 degrees of device has to be 7.4 of view, and the same multiple must apply to pitch.</summary>
    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(2.5, 5.0)]
    [InlineData(3.7, 7.4)]
    [InlineData(10.0, 20.0)]
    public void TheGainMultipliesBothAxesOneForOne(double gain, double expected)
    {
        var yaw = new CockpitLook { DeviceGain = gain };
        yaw.Recentre(Held(0, 0, 0));
        yaw.FromDevice(Held(2, 0, 0));
        Assert.Equal(expected, yaw.YawDeg, 3);

        var pitch = new CockpitLook { DeviceGain = gain };
        pitch.Recentre(Held(0, 0, 0));
        pitch.FromDevice(Held(0, 2, 0));
        Assert.Equal(expected, pitch.PitchDeg, 3);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-5.0, 1.0)]
    [InlineData(0.99, 1.0)]
    [InlineData(10.5, 10.0)]
    [InlineData(1e9, 10.0)]
    [InlineData(double.NaN, 3.5)]          // not a number is not a setting: fall back to the DEFAULT
    public void TheGainIsClampedToItsSlider(double asked, double stored)
    {
        var look = new CockpitLook { DeviceGain = asked };
        Assert.Equal(stored, look.DeviceGain, 9);
    }

    /// <summary>The gain applies to the TURN SINCE the zero, so changing it must not move a view nobody is
    /// turning.</summary>
    [Fact]
    public void ChangingTheGainDoesNotMoveAViewNobodyIsTurning()
    {
        var look = OneToOne();
        look.Recentre(Held(-30, 12, 0));
        foreach (double g in new[] { 1.0, 4.0, 10.0 })
        {
            look.DeviceGain = g;
            look.FromDevice(Held(-30, 12, 0));
            Assert.Equal(0.0, look.YawDeg, 4);
            Assert.Equal(0.0, look.PitchDeg, 4);
        }
    }

    /// <summary>"keep it working alongside the existing touch look". A snap over the shoulder and a lean of the
    /// phone must ADD, and the two halves stay separate.</summary>
    [Fact]
    public void TheHatAndTheDeviceComposeInsteadOfOverwritingEachOther()
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, 0));
        look.Snap(CockpitLook.Direction.Left);          // -90
        Assert.Equal(-90.0, look.YawDeg, 4);
        look.FromDevice(Held(-20, 0, 0));               // aim the phone another 20 left
        Assert.Equal(-110.0, look.YawDeg, 3);
        Assert.Equal(-90.0, look.ManualYawDeg, 4);
        Assert.Equal(-20.0, look.DeviceYawDeg, 3);
        look.Hat(1, 0, 0.5);                            // +70 at the default 140 deg/s
        Assert.Equal(-40.0, look.YawDeg, 3);
    }

    /// <summary>Letting go of the hat must not snatch the view away from where the phone is aimed.</summary>
    [Fact]
    public void SpringCentreReturnsTheHatButLeavesTheDeviceWhereItIs()
    {
        var look = new CockpitLook { SpringCentre = true, DeviceGain = CockpitLook.MinDeviceGain };
        look.Recentre(Held(0, 0, 0));
        look.FromDevice(Held(25, 0, 0));
        look.Snap(CockpitLook.Direction.Right);         // +90 on top of the device's 25
        Assert.Equal(115.0, look.YawDeg, 3);
        for (int i = 0; i < 200; i++) look.Hat(0, 0, 0.02);
        Assert.Equal(0.0, look.ManualYawDeg, 6);
        Assert.Equal(25.0, look.YawDeg, 3);
    }

    /// <summary>At 10x a 20 degree turn asks for 200. The view stops at the neck, and — the part that matters —
    /// aiming back brings it straight back instead of unwinding stored offset first.</summary>
    [Fact]
    public void AHighGainStopsAtTheNeckAndComesStraightBack()
    {
        var look = new CockpitLook { DeviceGain = 10.0 };
        look.Recentre(Held(0, 0, 0));
        look.FromDevice(Held(20, 0, 0));
        Assert.Equal(CockpitLook.MaxYawDeg, look.YawDeg, 6);
        look.FromDevice(Held(10, 0, 0));
        Assert.Equal(100.0, look.YawDeg, 3);
        look.FromDevice(Held(0, 0, 0));
        Assert.Equal(0.0, look.YawDeg, 4);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void TheSumOfBothHalvesIsStillInsideTheNeck(double gain)
    {
        var look = new CockpitLook { DeviceGain = gain };
        look.Recentre(Held(0, 0, 0));
        look.Snap(CockpitLook.Direction.OverRightShoulder);
        look.FromDevice(Held(80, 0, 0));
        Assert.InRange(look.YawDeg, -CockpitLook.MaxYawDeg, CockpitLook.MaxYawDeg);
        look.Snap(CockpitLook.Direction.Up);
        look.FromDevice(Held(0, 80, 0));
        Assert.InRange(look.PitchDeg, -CockpitLook.MaxPitchDownDeg, CockpitLook.MaxPitchUpDeg);
    }

    /// <summary>RECENTRE zeroes the hat's offset AND the device's, against however the phone is aimed then.</summary>
    [Fact]
    public void RecentreZeroesBothHalvesAtWhateverTheDeviceIsDoing()
    {
        var look = new CockpitLook { DeviceGain = 5.0 };
        look.Recentre(Held(0, 0, 0));
        look.Snap(CockpitLook.Direction.Right);
        look.FromDevice(Held(10, 0, 0));
        Assert.True(look.LookingAway);
        look.Recentre(Held(10, 0, 0));
        Assert.Equal(0.0, look.YawDeg, 6);
        Assert.Equal(0.0, look.ManualYawDeg, 9);
        Assert.Equal(0.0, look.DeviceYawDeg, 9);
        Assert.False(look.LookingAway);
    }

    [Fact]
    public void TurningTheDeviceOffDropsItsOffsetAndItsZero()
    {
        var look = OneToOne();
        look.Recentre(Held(0, 0, 0));
        look.Snap(CockpitLook.Direction.Left);
        look.FromDevice(Held(30, 0, 0));
        Assert.Equal(-60.0, look.YawDeg, 3);
        look.ForgetDevice();
        Assert.False(look.HasReference);
        Assert.Equal(-90.0, look.YawDeg, 4);
        look.FromDevice(Held(30, 0, 0));
        Assert.Equal(-90.0, look.YawDeg, 4);
    }
}
