using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty 2026-11-02 (iPhone + 8BitDo): 50 % more chase distance on every aircraft, framing kept; and with a gamepad
/// connected the 3D view fills the whole screen instead of stopping at the (now empty) portrait tray.</summary>
public sealed class ChaseFramingTests
{
    [Theory]
    [InlineData(10.7, 6.9)]    // Cub-sized
    [InlineData(5.3, 5.7)]     // Pitts: under the 11 m floor before
    [InlineData(17.0, 7.2)]    // 2-33 glider
    [InlineData(35.8, 39.5)]   // 737
    [InlineData(97.5, 66.6)]   // H-4: at the 200 m cap before
    public void EveryAircraftIsHalfAgainFurtherAway(double span, double length)
    {
        double before = ChaseFraming.BaseDistanceM(span, length);
        Assert.Equal(1.5 * before, ChaseFraming.FollowDistanceM(span, length), 9);
    }

    [Fact]
    public void TheBaseDistanceIsWhatFitToUsedToSet()
    {
        Assert.Equal(11.0, ChaseFraming.BaseDistanceM(5.3, 5.7), 9);          // floor
        Assert.Equal(200.0, ChaseFraming.BaseDistanceM(160, 10), 9);          // cap
        Assert.Equal(17.0 * 1.3, ChaseFraming.BaseDistanceM(17.0, 7.2), 9);   // span governs
        Assert.Equal(40 * 1.15 * 1.3, ChaseFraming.BaseDistanceM(30, 40), 9); // length governs
    }

    [Fact]
    public void TheLookDownAngleIsUnchangedSoTheAircraftStaysWhereItWasOnScreen()
    {
        double before = 17.0 * 1.3, after = ChaseFraming.FollowDistanceM(17.0, 7.2);
        Assert.Equal(System.Math.Atan2(before * 0.22, before), System.Math.Atan2(ChaseFraming.FollowHeightM(after), after), 12);
    }

    [Fact]
    public void WithAGamepadThePortraitViewIsTheWholeScreen()
    {
        Assert.Equal((0.0, 0.0, 1.0, 1.0), ChaseFraming.CameraViewport(trayPx: 900, screenHeightPx: 2556, fullScreen: true));
        var vp = ChaseFraming.CameraViewport(trayPx: 900, screenHeightPx: 2556, fullScreen: false);
        Assert.Equal(900.0 / 2556.0, vp.Y, 12);
        Assert.Equal(1.0 - 900.0 / 2556.0, vp.H, 12);
        Assert.Equal((0.0, 0.0, 1.0, 1.0), ChaseFraming.CameraViewport(0, 1179, fullScreen: false));   // landscape: no tray
    }
}
