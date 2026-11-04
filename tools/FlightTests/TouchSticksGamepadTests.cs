using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner 2026-11-02 (Ty, iPhone + 8BitDo): with a gamepad connected the on-screen touch sticks are hidden entirely;
/// with none connected (the default) they are shown. TouchFlightControls asks this every frame with the live pad
/// state, so a hot-plug connect hides them and a disconnect brings them back on the next frame.
/// </summary>
public sealed class TouchSticksGamepadTests
{
    [Fact]
    public void NoControllerShowsTheTouchSticks() => Assert.True(GamepadShaping.TouchSticksShown(deskMode: false, gamepadConnected: false));

    [Fact]
    public void AConnectedControllerHidesThem() => Assert.False(GamepadShaping.TouchSticksShown(deskMode: false, gamepadConnected: true));

    [Fact]
    public void HotPlugFlipsItBothWays()
    {
        bool[] connected = { false, true, true, false };
        bool[] shown = { true, false, false, true };
        for (int i = 0; i < connected.Length; i++)
            Assert.Equal(shown[i], GamepadShaping.TouchSticksShown(false, connected[i]));
    }

    [Fact]
    public void TheMacNeverHasThem()
    {
        Assert.False(GamepadShaping.TouchSticksShown(deskMode: true, gamepadConnected: false));
        Assert.False(GamepadShaping.TouchSticksShown(deskMode: true, gamepadConnected: true));
    }
}
