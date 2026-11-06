using FlyingGame.Core;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Sim;

/// <summary>
/// LOCAL 2-PLAYER: one gamepad flying one aeroplane with nothing else on it — no touch pads, no keyboard, no headset
/// hands. The second player's controls (Bridge <c>LocalPadControls</c>) are this and nothing more; the first player keeps
/// <c>TouchFlightControls</c>, untouched.
///
/// It is <c>TouchFlightControls.GamepadFlight</c>'s pad half, the same numbers in the same order, so the two players'
/// aeroplanes answer a pad the same way: <see cref="GamepadShaping"/> has already shaped the sticks, the output is lifted
/// past the aeroplane's own dead zone (<see cref="GamepadShaping.PreCompensateDeadZone"/>), the left stick and the
/// shoulders move the lever as a RATE (<see cref="GamepadShaping.Lever"/>, idle to full in 2 s; shoulders 0.8/s), the
/// d-pad trims at 0.5 of the trim slider a second, and <see cref="PilotControls.Compose"/> builds the step's inputs with
/// the "Gamepad: stick-free" option. A glider's lever is its spoiler handle (stick down = spoilers out).
/// </summary>
public sealed class LocalPadPilot
{
    /// <summary>One frame of the pad, already shaped (Bridge GamepadInput.Frame carries the same fields).</summary>
    public struct Pad
    {
        public bool Connected;
        public double Roll, Pitch, Rudder, Throttle;
        public bool ThrottleUp, ThrottleDown, TrimNoseDown, TrimNoseUp, Brakes, FlapsUpPressed, FlapsDownPressed;
    }

    /// <summary>The touch slider's trim authority (TouchFlightControls.TrimAuthority): full trim = this much stick.</summary>
    public const double TrimAuthority = 0.6;
    /// <summary>D-pad trim rate, slider units a second (TouchFlightControls: 0.5 × dt).</summary>
    public const double TrimRatePerSec = 0.5;
    /// <summary>The shoulders' lever rate (TouchFlightControls.GamepadButtonThrottleRate).</summary>
    public const double ButtonLeverRatePerSec = 0.8;

    /// <summary>Powered: throttle 0 idle .. 1 full. Glider: spoiler handle 0 stowed .. 1 full out.</summary>
    public double Lever01 { get; private set; }
    /// <summary>Pitch trim, trim-slider units −1..1 (+ = nose down), as the touch slider.</summary>
    public double PitchTrim { get; private set; }
    public bool Powered { get; private set; }

    /// <summary>Start of a flight (TouchFlightControls.PresetTrim): the spawn's trim; an air start at the rig power
    /// (75 %) with a glider's spoilers stowed; a ground or idle start at idle, a glider's spoilers at the start's own
    /// setting.</summary>
    public void Preset(bool powered, double trimStick, bool idleStart, double startSpoiler01)
    {
        Powered = powered;
        PitchTrim = Math.Clamp(trimStick / TrimAuthority, -1.0, 1.0);
        Lever01 = powered ? (idleStart ? 0.0 : RigSolver.RigThrottle) : (idleStart ? Math.Clamp(startSpoiler01, 0.0, 1.0) : 0.0);
    }

    public readonly struct Result
    {
        public readonly ControlInputs Inputs;
        public readonly double Brake01;
        /// <summary>−1 flaps up a step, +1 down a step, 0 nothing this frame.</summary>
        public readonly int FlapStep;
        public Result(ControlInputs i, double b, int f) { Inputs = i; Brake01 = b; FlapStep = f; }
    }

    /// <summary>One frame. <paramref name="controls"/> may be null (no aeroplane yet): no dead-zone lift then.</summary>
    public Result Step(in Pad p, double dt, ControlsConfig? controls, bool gamepadStickFree = GamepadShaping.DefaultGamepadStickFree)
    {
        dt = Math.Max(0.0, dt);
        double Lift(double v, ControlAxisConfig? axis) => axis == null ? v : GamepadShaping.PreCompensateDeadZone(v, axis.DeadZone);
        double aileron = p.Connected ? Lift(p.Roll, controls?.Aileron) : 0.0;
        double elevator = p.Connected ? Lift(p.Pitch, controls?.Elevator) : 0.0;   // pad y + = pushed away = nose down = sim +
        double rudder = p.Connected ? Lift(p.Rudder, controls?.Rudder) : 0.0;
        int flap = 0;
        double brake = 0.0;
        if (p.Connected)
        {
            // Powered: stick up = more power. Glider: the same lever is the spoiler handle, stick DOWN = spoilers out.
            double rate = Powered ? p.Throttle : -p.Throttle;
            bool up = Powered ? p.ThrottleUp : p.ThrottleDown, down = Powered ? p.ThrottleDown : p.ThrottleUp;
            Lever01 = GamepadShaping.Lever(Lever01, rate, up, down, GamepadShaping.StickLeverRatePerSec(0.0, 1.0), ButtonLeverRatePerSec, dt, 0.0, 1.0);
            if (p.TrimNoseDown) PitchTrim = Math.Clamp(PitchTrim + TrimRatePerSec * dt, -1.0, 1.0);
            if (p.TrimNoseUp) PitchTrim = Math.Clamp(PitchTrim - TrimRatePerSec * dt, -1.0, 1.0);
            if (p.Brakes) brake = 1.0;
            flap = p.FlapsDownPressed ? 1 : p.FlapsUpPressed ? -1 : 0;
        }
        double lever = Powered ? 1.0 - 2.0 * Lever01 : Lever01;
        // Not connected: hands off, the surfaces held on the trim (the parked aeroplane flies its trimmed start).
        ControlInputs u = PilotControls.Compose(aileron, elevator, rudder, 0.0, PitchTrim * TrimAuthority, 0.0, lever,
            false, false, gamepadConnected: true, gamepadStickFree: p.Connected && gamepadStickFree);
        return new Result(u, brake, flap);
    }

    /// <summary>A flap step from where the flaps ARE (0 / 50 / 100), as TouchFlightControls does.</summary>
    public static double NextFlap(double current, int step) =>
        step == 0 ? current : Math.Clamp(Math.Round(current * 2.0) + step, 0, 2) / 2.0;
}
