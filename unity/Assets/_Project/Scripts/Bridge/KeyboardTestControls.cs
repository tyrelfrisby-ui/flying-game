using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// TEMPORARY editor/dev input until the touch pads (build-order step 5). Arrow keys = stick
    /// (Down = pull/nose-up), A/D = rudder, S/W = speed-brake lever aft/forward, R = reset.
    /// Keys ramp toward full deflection so control inputs aren't square waves.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class KeyboardTestControls : MonoBehaviour
    {
        private const float RampPerSec = 2.5f;
        private FlightSimDriver _driver;
        private float _aileron, _elevator, _rudder, _lever;

        private void Awake() => _driver = GetComponent<FlightSimDriver>();

        private void Update()
        {
            _aileron = Ramp(_aileron, Axis(KeyCode.RightArrow, KeyCode.LeftArrow));
            // Pull (Down arrow) commands nose-up, which is NEGATIVE elevator deflection in this config.
            _elevator = Ramp(_elevator, Axis(KeyCode.UpArrow, KeyCode.DownArrow));
            _rudder = Ramp(_rudder, Axis(KeyCode.D, KeyCode.A));

            float leverMove = Axis(KeyCode.S, KeyCode.W);
            _lever = Mathf.Clamp(_lever + leverMove * RampPerSec * 0.4f * Time.deltaTime, -1f, 1f); // fwd(-)=throttle, aft(+)=brake/idle

            if (Input.GetKeyDown(KeyCode.Alpha1)) _driver.SwitchAircraft("glider-2-33-like");
            if (Input.GetKeyDown(KeyCode.Alpha2)) _driver.SwitchAircraft("c172-like");
            if (Input.GetKeyDown(KeyCode.Alpha3)) _driver.SwitchAircraft("pitts-s2b-like");

            if (Input.GetKeyDown(KeyCode.Alpha4)) _driver.SwitchAircraft("stearman-pt17-like");
            if (Input.GetKeyDown(KeyCode.Alpha5)) _driver.SwitchAircraft("extra-300-like");
            if (Input.GetKeyDown(KeyCode.Alpha6)) _driver.SwitchAircraft("p51d-like");
            if (Input.GetKeyDown(KeyCode.Alpha7)) _driver.SwitchAircraft("f86-sabre-like");
            if (Input.GetKeyDown(KeyCode.Alpha8)) _driver.SwitchAircraft("seminole-like");
            if (Input.GetKeyDown(KeyCode.Alpha9)) _driver.SwitchAircraft("dc3-like");
            if (Input.GetKeyDown(KeyCode.Alpha0)) _driver.SwitchAircraft("boeing-737-like");

            if (Input.GetKeyDown(KeyCode.F1)) _driver.SwitchAircraft("pa18-cub-like");
            if (Input.GetKeyDown(KeyCode.F2)) _driver.SwitchAircraft("decathlon-8kcab-like");

            if (Input.GetKeyDown(KeyCode.R))
            {
                _aileron = _elevator = _rudder = _lever = 0;
                _driver.ResetFlight();
            }

            _driver.Sim.Aircraft.BrakeInput = Input.GetKey(KeyCode.B) ? 1f : 0f;
            _driver.Inputs = new ControlInputs(_aileron, _elevator, _rudder, _lever);
        }

        private static float Axis(KeyCode positive, KeyCode negative) =>
            (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);

        private float Ramp(float current, float target) =>
            Mathf.MoveTowards(current, target, RampPerSec * Time.deltaTime * (Mathf.Approximately(target, 0f) ? 2f : 1f));
    }
}
