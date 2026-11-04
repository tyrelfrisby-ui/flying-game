namespace FlyingGame.Sim;

/// <summary>
/// The pilot's hands to <see cref="ControlInputs"/>: stick, pedals and lever plus the three trims, and whether each
/// control is LET GO. Lifted verbatim out of <c>TouchFlightControls.PublishToDriver</c> (2026-11-02) so the tests fly
/// the same composition the game does — touch pads, keys and the gamepad all end here.
///
/// Reversible controls (owner 2026-10-02): a control with no hand on it (no finger on its pad and no stick, key or pad
/// deflecting it) is FREE — on a cable-controlled aeroplane its surface then trails with the air loads and the axis
/// value is read as the trim only. A gamepad at rest is exactly zero inside its dead zone (<c>GamepadShaping</c>), so
/// at rest it is "released" the same way a lifted finger is; whether a released control then goes free with a pad
/// CONNECTED is the player's choice (<see cref="FlyingGame.Core.GamepadShaping.ControlsFree"/>: off by default, owner
/// 2026-11-02, lane/claude-hydro).
/// </summary>
public static class PilotControls
{
    /// <param name="stickAileron">Stick / pad roll, −1…1 (+ = right).</param>
    /// <param name="stickElevator">Stick / pad pitch, −1…1, already in the sim's sense (+ = push).</param>
    /// <param name="stickRudder">Pedals, −1…1 (+ = right).</param>
    /// <param name="aileronTrim">Trims as stick fractions (already × the trim authority).</param>
    /// <param name="lever">The throttle lever as <see cref="ControlInputs.ThrottleLever"/>.</param>
    /// <param name="rightHandOn">A finger is on the stick pad (it holds the stick even when centred).</param>
    /// <param name="leftHandOn">A finger is on the rudder / throttle pad.</param>
    /// <param name="gamepadConnected">A gamepad is connected this frame.</param>
    /// <param name="gamepadStickFree">The player's "Gamepad: stick-free" option (SessionSettings.GamepadStickFree).</param>
    public static ControlInputs Compose(double stickAileron, double stickElevator, double stickRudder,
        double aileronTrim, double pitchTrim, double rudderTrim, double lever, bool rightHandOn, bool leftHandOn,
        bool gamepadConnected = false, bool gamepadStickFree = FlyingGame.Core.GamepadShaping.DefaultGamepadStickFree)
    {
        double aileron = Math.Clamp(stickAileron + aileronTrim, -1.0, 1.0);
        double elevator = Math.Clamp(stickElevator + pitchTrim, -1.0, 1.0);
        double rudder = Math.Clamp(stickRudder + rudderTrim, -1.0, 1.0);
        bool rightFree = FlyingGame.Core.GamepadShaping.ControlsFree(!rightHandOn && stickAileron == 0.0 && stickElevator == 0.0, gamepadConnected, gamepadStickFree);
        bool leftFree = FlyingGame.Core.GamepadShaping.ControlsFree(!leftHandOn && stickRudder == 0.0, gamepadConnected, gamepadStickFree);
        return new ControlInputs(aileron, elevator, rudder, lever, rightFree, rightFree, leftFree);
    }
}
