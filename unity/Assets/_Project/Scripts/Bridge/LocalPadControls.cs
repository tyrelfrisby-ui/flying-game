using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// LOCAL 2-PLAYER: the second player's hands — their gamepad (<see cref="GamepadInput.For"/>) and nothing else. The
    /// flying is <see cref="FlyingGame.Sim.LocalPadPilot"/> (Core-tested, the same numbers as the first player's pad
    /// path); this reads the pad, hands the result to the driver and works the buttons. Same mapping as P1's pad:
    /// right stick roll / pitch, left stick X + triggers rudder, left stick Y and RB / LB power, d-pad up / down trim,
    /// left / right flaps, X brakes, B smoke, Y next view; L3 + d-pad looks and R3 recentres in the cockpit (CockpitView reads
    /// this player's pad). Menu opens the shared OPTIONS.
    ///
    /// No pad on this seat: hands off on the start's trim, so the aeroplane flies on, level, until a second pad is
    /// paired (PadBinding gives it the empty seat on its first frame).
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class LocalPadControls : MonoBehaviour
    {
        public int PlayerIndex = 1;
        public FlightSimDriver Driver;
        /// <summary>This player's camera (the Y button changes its view).</summary>
        public ChaseCamera Chase;

        private readonly FlyingGame.Sim.LocalPadPilot _pilot = new();
        private SmokeController _smoke;
        private OptionsPanel _options;

        private void Awake()
        {
            if (Driver == null) Driver = GetComponent<FlightSimDriver>();
            Driver.AircraftChanged += Preset;
        }

        private void OnDestroy() { if (Driver != null) Driver.AircraftChanged -= Preset; }

        /// <summary>Start of a flight: the spawn's trim and power (TouchFlightControls.PresetTrim's rule).</summary>
        public void Preset()
        {
            if (Driver == null || Driver.Sim == null) return;
            _pilot.Preset(Driver.Powered, Driver.TrimStick, Driver.GroundStart || Driver.IdleStart, Driver.StartSpoilerFraction);
            Driver.Inputs = _pilot.Step(default, 0, Driver.Sim.Aircraft.Config.Controls).Inputs;
        }

        private void Update()
        {
            var ac = Driver?.Sim?.Aircraft;
            if (ac == null) return;
            var gp = GamepadInput.For(PlayerIndex);
            if (gp.Connected && gp.MenuPressed && !SessionSettings.MenuOpen && (_options != null || (_options = Object.FindFirstObjectByType<OptionsPanel>()) != null)) _options.Toggle();
            if (SessionSettings.MenuOpen || JoystickInput.SetupActive) return;
            var pad = new FlyingGame.Sim.LocalPadPilot.Pad
            {
                Connected = gp.Connected, Roll = gp.Roll, Pitch = gp.Pitch, Rudder = gp.Rudder, Throttle = gp.Throttle,
                ThrottleUp = gp.ThrottleUp, ThrottleDown = gp.ThrottleDown, TrimNoseDown = gp.TrimNoseDown, TrimNoseUp = gp.TrimNoseUp,
                Brakes = gp.Brakes, FlapsUpPressed = gp.FlapsUpPressed, FlapsDownPressed = gp.FlapsDownPressed,
            };
            // Real time, as the first player's pad: the lever and the trim move at the same rate in slow motion.
            var r = _pilot.Step(pad, Time.unscaledDeltaTime, ac.Config.Controls, SessionSettings.GamepadStickFree);
            Driver.Inputs = r.Inputs;
            ac.BrakeInput = r.Brake01;
            ac.BrakeBias = 0;
            if (r.FlapStep != 0 && Driver.HasFlaps) ac.FlapFraction = FlyingGame.Sim.LocalPadPilot.NextFlap(ac.FlapFraction, r.FlapStep);
            if (!gp.Connected) return;
            if (gp.SmokePressed && (_smoke != null || (_smoke = GetComponent<SmokeController>()) != null)) _smoke.Toggle();
            if (gp.ViewPressed && Chase != null) Chase.NextView();
        }
    }
}
