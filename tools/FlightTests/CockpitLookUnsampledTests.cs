using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// "Cockpit view is all sky" (Ty, 2026-11-02, iPhone, build 2026.1102.1603, 8BitDo connected, device look ON at gain
/// 4.9). Unity's Input.gyro.attitude reads exactly the identity -- phone flat, screen up -- until CoreMotion's first
/// sample arrives, and the view zeroed against that placeholder at start-up. A phone then held up in landscape read
/// as +90 degrees of pitch, times 4.9, clamped at MaxPitchUpDeg: the view parked looking up. These pin the fix:
/// a placeholder attitude is never a zero and never followed; the first REAL reading is straight ahead.
/// </summary>
public sealed class CockpitLookUnsampledTests
{
    private static Quat About(double ax, double ay, double az, double deg)
    {
        double h = deg * System.Math.PI / 360.0, s = System.Math.Sin(h);
        return new Quat(ax * s, ay * s, az * s, System.Math.Cos(h));
    }

    // Same construction as CockpitLookTests.Held: turn, tilt, the 90 degrees that stands the phone up, then its roll.
    private static Quat Held(double turnRightDeg, double tiltUpDeg, double rollDeg) =>
        Quat.Multiply(
            Quat.Multiply(About(0, 0, 1, -turnRightDeg), Quat.Multiply(About(1, 0, 0, tiltUpDeg), About(1, 0, 0, 90))),
            About(0, 0, 1, rollDeg));

    private const double TysGain = 4.9;   // view.deviceGain on his iPhone (4.920382)

    [Fact]
    public void ZeroingAgainstThePlaceholderDoesNotParkTheViewInTheSky()
    {
        var look = new CockpitLook { DeviceGain = TysGain };
        look.Recentre(Quat.Identity);              // start-up: gyro just enabled, no sample yet
        Assert.False(look.HasReference);
        look.FromDevice(Quat.Identity);            // still no sample on the next frame
        Assert.False(look.HasReference);
        look.FromDevice(Held(0, 0, 90));           // first real reading: phone held up, landscape, level
        Assert.True(look.HasReference);
        Assert.Equal(0.0, look.PitchDeg, 6);
        Assert.Equal(0.0, look.YawDeg, 6);
        look.FromDevice(Held(0, 0, 90));           // held still: still straight ahead (was +75 = clamp before)
        Assert.Equal(0.0, look.PitchDeg, 6);
        Assert.False(look.LookingAway);
    }

    [Fact]
    public void AfterTheRealZeroTheDeviceStillLooksAtTheGain()
    {
        var look = new CockpitLook { DeviceGain = TysGain };
        look.Recentre(Quat.Identity);
        look.FromDevice(Held(0, 0, 90));
        look.FromDevice(Held(0, 5, 90));           // tip the phone back 5 degrees -> 4.9 x 5 = 24.5 up
        Assert.Equal(5.0 * TysGain, look.PitchDeg, 3);
    }

    [Fact]
    public void ThePlaceholderMidFlightIsIgnoredNotFollowed()
    {
        var look = new CockpitLook { DeviceGain = TysGain };
        look.Recentre(Held(0, 0, 90));
        look.FromDevice(Quat.Identity);            // a dropped / reset sample must not swing the view
        Assert.Equal(0.0, look.PitchDeg, 6);
    }

    [Fact]
    public void WhatCountsAsUnsampled()
    {
        Assert.True(CockpitLook.IsUnsampled(Quat.Identity));
        Assert.True(CockpitLook.IsUnsampled(new Quat(0, 0, 0, 0)));
        Assert.True(CockpitLook.IsUnsampled(new Quat(double.NaN, 0, 0, 1)));
        Assert.False(CockpitLook.IsUnsampled(Held(0, 0, 90)));
        Assert.False(CockpitLook.IsUnsampled(About(1, 0, 0, 0.001)));   // a phone really lying flat still has noise
    }
}
