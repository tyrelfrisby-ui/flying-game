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
            _lever = Mathf.Clamp(_lever + leverMove * RampPerSec * 0.4f * Time.deltaTime, 0f, 1f);

            if (Input.GetKeyDown(KeyCode.R))
            {
                _aileron = _elevator = _rudder = _lever = 0;
                _driver.ResetFlight();
            }

            _driver.Inputs = new ControlInputs(_aileron, _elevator, _rudder, _lever);
        }

        private static float Axis(KeyCode positive, KeyCode negative) =>
            (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);

        private float Ramp(float current, float target) =>
            Mathf.MoveTowards(current, target, RampPerSec * Time.deltaTime * (Mathf.Approximately(target, 0f) ? 2f : 1f));
    }
}
