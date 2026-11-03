using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty 2026-11-02: "the stick free stability thing should be turned off when using a controller have it as an option
/// but defaulted off with the controller". The rule in one place (GamepadShaping.ControlsFree); the persisted choice is
/// SessionSettings.GamepadStickFree, shown on OPTIONS › Controls.</summary>
public class GamepadStickFreeTests
{
    [Fact]
    public void OffByDefaultWithAPad() => Assert.False(GamepadShaping.DefaultGamepadStickFree);

    [Fact]
    public void TouchOnlyPlayIsUnchanged()
    {
        // No pad: a released pad floats its controls, a held one does not — whatever the gamepad option says.
        Assert.True(GamepadShaping.ControlsFree(released: true, gamepadConnected: false, gamepadStickFree: false));
        Assert.True(GamepadShaping.ControlsFree(true, false, true));
        Assert.False(GamepadShaping.ControlsFree(false, false, false));
    }

    [Fact]
    public void WithAPadACentredStickHoldsUnlessTheOptionIsOn()
    {
        Assert.False(GamepadShaping.ControlsFree(released: true, gamepadConnected: true, gamepadStickFree: GamepadShaping.DefaultGamepadStickFree));
        Assert.True(GamepadShaping.ControlsFree(true, true, gamepadStickFree: true));    // the player's choice wins
        Assert.False(GamepadShaping.ControlsFree(false, true, true));                     // a deflected stick is never free
    }
}
