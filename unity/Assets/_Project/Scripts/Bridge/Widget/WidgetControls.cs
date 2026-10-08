using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's controls from every source the owner asked for (2026-10-07): the keyboard, a gamepad/joystick, the TCP
    /// port (Glass Overlay / MCP) and the phone/iPad web remote. Whichever source moved last has the airplane; a preset, while
    /// it runs, overrides them all. Stick values −1…1 (elevator + = nose DOWN, as in the game), throttle 0…1, brake 0…1.
    /// </summary>
    public sealed class WidgetControls : MonoBehaviour
    {
        public AeroWidget Widget;
        public double Aileron, Elevator, Rudder, Throttle01, Brake01;
        public bool ElevatorFree;   // "hands off": the elevator floats on its trim (a reversible control)
        private double _trimStick;
        public string SourceLabel { get; private set; } = "keyboard";
        private float _netAt = -10f, _padAt = -10f;
        // Gamepad axis map (1-based joystick axes; configurable over the port): aileron, elevator, rudder, throttle.
        public int AxAileron = 1, AxElevator = 2, AxRudder = 4, AxThrottle = 3;
        public bool InvertElevator = false, ThrottleFromAxis = false;

        public void SetTrimStick(double stick) { _trimStick = stick; Elevator = stick; }
        public void ResetToTrim() { Aileron = 0; Rudder = 0; Elevator = _trimStick; Brake01 = 0; ElevatorFree = false; }

        public ControlInputs Inputs => new(Aileron, Elevator, Rudder, 1.0 - 2.0 * Throttle01, false, ElevatorFree, false);

        /// <summary>From the TCP port / web remote (any field may be absent).</summary>
        public void FromNetwork(double? ail, double? ele, double? rud, double? thr, double? brake, bool? handsOff, string source)
        {
            if (ail.HasValue) Aileron = Mathf.Clamp((float)ail.Value, -1f, 1f);
            if (ele.HasValue) Elevator = Mathf.Clamp((float)ele.Value, -1f, 1f);
            if (rud.HasValue) Rudder = Mathf.Clamp((float)rud.Value, -1f, 1f);
            if (thr.HasValue) Throttle01 = Mathf.Clamp01((float)thr.Value);
            if (brake.HasValue) Brake01 = Mathf.Clamp01((float)brake.Value);
            if (handsOff.HasValue) { ElevatorFree = handsOff.Value; if (handsOff.Value) Elevator = _trimStick; }
            _netAt = Time.unscaledTime; SourceLabel = source;
            Widget.Presets.Stop();
        }

        private static float Axis(int i) { try { return Input.GetAxisRaw("WJoy" + i); } catch (System.ArgumentException) { return 0f; } }

        public void Poll()
        {
            // Keyboard (always live): 1/2 scenarios, arrows stick, A/D rudder, W/S power, B brakes, H hands-off, Space pause,
            // R reset, P next preset, [ ] slow-motion.
            if (Input.GetKeyDown(KeyCode.Alpha1)) Widget.Load(AeroWidget.Scenario.Flare, Widget.AircraftId, Widget.Flaps);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Widget.Load(AeroWidget.Scenario.Spin, Widget.AircraftId, Widget.Flaps);
            if (Input.GetKeyDown(KeyCode.Space)) Widget.Paused = !Widget.Paused;
            if (Input.GetKeyDown(KeyCode.R)) Widget.Load(Widget.Current, Widget.AircraftId, Widget.Flaps);
            if (Input.GetKeyDown(KeyCode.P)) Widget.Presets.Next();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) Widget.TimeScale = Mathf.Max(0.1f, Widget.TimeScale * 0.5f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) Widget.TimeScale = Mathf.Min(1f, Widget.TimeScale * 2f);
            if (Input.GetKeyDown(KeyCode.H)) ElevatorFree = !ElevatorFree;
            float kx = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float ky = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);   // up = push (nose down)
            float kr = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            bool keyStick = kx != 0 || ky != 0 || kr != 0;
            float dt = Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.W)) { Throttle01 = Mathf.Clamp01((float)Throttle01 + dt * 0.6f); SourceLabel = "keyboard"; }
            if (Input.GetKey(KeyCode.S)) { Throttle01 = Mathf.Clamp01((float)Throttle01 - dt * 0.6f); SourceLabel = "keyboard"; }
            Brake01 = Input.GetKey(KeyCode.B) ? 1.0 : (SourceLabel == "keyboard" ? 0.0 : Brake01);
            if (keyStick)
            {
                Widget.Presets.Stop(); SourceLabel = "keyboard"; ElevatorFree = false;
                Aileron = Mathf.MoveTowards((float)Aileron, kx, dt * 3f);
                Elevator = Mathf.MoveTowards((float)Elevator, ky != 0 ? ky : (float)_trimStick, dt * 3f);
                Rudder = Mathf.MoveTowards((float)Rudder, kr, dt * 4f);
            }
            // Gamepad / joystick: takes the airplane when its sticks move.
            float ga = Axis(AxAileron), ge = Axis(AxElevator) * (InvertElevator ? -1f : 1f), gr = Axis(AxRudder), gt = Axis(AxThrottle);
            bool padMoved = Mathf.Abs(ga) > 0.08f || Mathf.Abs(ge) > 0.08f || Mathf.Abs(gr) > 0.08f;
            if (padMoved) _padAt = Time.unscaledTime;
            if (Time.unscaledTime - _padAt < 1.5f && Time.unscaledTime - _netAt > 0.5f && !keyStick)
            {
                Widget.Presets.Stop(); SourceLabel = "gamepad"; ElevatorFree = false;
                Aileron = ga; Elevator = Mathf.Abs(ge) > 0.05f ? -ge : _trimStick; Rudder = gr;   // stick back (−y on most pads… mapped) = pull
                if (ThrottleFromAxis) Throttle01 = (gt + 1f) * 0.5f;
            }
            else if (!keyStick && Time.unscaledTime - _netAt > 2f && SourceLabel == "keyboard")
            {
                // Released keys: back to trim (the stick centres).
                Aileron = Mathf.MoveTowards((float)Aileron, 0f, dt * 3f);
                Rudder = Mathf.MoveTowards((float)Rudder, 0f, dt * 4f);
                if (!ElevatorFree) Elevator = Mathf.MoveTowards((float)Elevator, (float)_trimStick, dt * 3f);
            }
        }
    }
}
