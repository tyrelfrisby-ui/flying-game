using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// Scripted moments to talk over (owner 2026-10-07): they fly the controls until done or until anyone touches them.
    ///   spin-entry      power off, stall it wings level with full aft stick, then full rudder (left) — into the incipient spin
    ///   spin-developed  the same, fast-forwarded ~8 s so it's already turning steadily
    ///   spin-recovery   PARE: Power idle, Ailerons neutral, Rudder full opposite the rotation, Elevator briskly forward —
    ///                   rudder neutral when the rotation stops, then ease out of the dive
    ///   flare-demo      the flare lesson's own law flies the round-out and hold-off
    ///   flare-hands-off the elevator floats on its trim (no flare) — watch it arrive
    ///   hammerhead      (owner 2026-10-11, the outro) from cruise: full power, a ~4 g pull to the vertical, the up-line held, full
    ///                   rudder at the pivot speed (left — the way a right-hand propeller helps), opposite aileron and forward
    ///                   stick against the roll and the gyroscopic pitch, the yaw stopped on the down-line, then a 4 g pull-out.
    ///                   A pilot's inputs only — the airplane does what the physics makes of them.
    /// </summary>
    public sealed class WidgetPresets : MonoBehaviour
    {
        public AeroWidget Widget;
        public static readonly string[] SpinPresets = { "spin-entry", "spin-developed", "spin-recovery" };
        public static readonly string[] FlarePresets = { "flare-demo", "flare-hands-off" };
        public static readonly string[] AeroPresets = { "hammerhead" };
        public string Running { get; private set; }
        public bool FlareDemo => Running == "flare-demo";
        private float _t; private int _stage; private int _next;
        private double _spinDir = -1;   // −1 = left spin (left rudder)
        private float _nzI, _ailI;      // the hammerhead's pitch-rate and wings-level integrators
        /// <summary>The hammerhead's pivot speed (kt): the rudder kick while there is still slipstream and airspeed to yaw it over.</summary>
        public float PivotKias = 65f;

        public void Stop() { Running = null; _stage = 0; _t = 0; }

        public void Next()
        {
            var list = Widget.Current == AeroWidget.Scenario.Spin ? SpinPresets : FlarePresets;
            Run(list[_next++ % list.Length]);
        }

        public bool Run(string name)
        {
            bool spin = System.Array.IndexOf(SpinPresets, name) >= 0, flare = System.Array.IndexOf(FlarePresets, name) >= 0;
            bool aero = System.Array.IndexOf(AeroPresets, name) >= 0;
            if (!spin && !flare && !aero) return false;
            if (aero && Widget.Current != AeroWidget.Scenario.Cruise) Widget.Load(AeroWidget.Scenario.Cruise, Widget.AircraftId, 0);
            if (spin && Widget.Current != AeroWidget.Scenario.Spin) Widget.Load(AeroWidget.Scenario.Spin, Widget.AircraftId, Widget.Flaps);
            if (flare && Widget.Current != AeroWidget.Scenario.Flare) Widget.Load(AeroWidget.Scenario.Flare, Widget.AircraftId, Widget.Flaps);
            if (name == "spin-entry" || name == "spin-developed" || name.StartsWith("flare")) Widget.Load(Widget.Current, Widget.AircraftId, Widget.Flaps);
            Running = name; _stage = 0; _t = 0; _nzI = 0; _ailI = 0;
            var c = Widget.Controls;
            if (name == "flare-hands-off") { c.ElevatorFree = true; c.Elevator = 0; }
            if (name == "spin-developed")
            {
                // Fast-forward the entry offscreen: the same inputs, 8 s of sim.
                for (int i = 0; i < 400; i++) { Tick(0.02f); Widget.StepOffscreen(0.02f); }
                Running = "spin-entry"; _stage = 2;
            }
            if (name == "spin-recovery")
            {
                double r = Widget.Ac.State.Rates.Z;
                _spinDir = r < 0 ? -1 : 1;   // the way it's turning (yaw rate sign)
            }
            return true;
        }

        public void Tick(float dt)
        {
            if (Running == null || dt <= 0) return;
            _t += dt;
            var c = Widget.Controls; var rd = Widget.Read();
            switch (Running)
            {
                case "spin-entry":
                    c.Throttle01 = 0; c.Aileron = 0;
                    if (_stage == 0) { c.Elevator = Mathf.MoveTowards((float)c.Elevator, -1f, dt * 0.5f); c.Rudder = 0; if (rd.AlphaDeg > 14 || _t > 12f) { _stage = 1; _t = 0; } }
                    else { c.Elevator = -1; c.Rudder = _spinDir; if (_stage == 1 && _t > 3f) _stage = 2; }   // stage 2: held, developed
                    break;
                case "spin-recovery":
                    c.Throttle01 = 0; c.Aileron = 0;
                    if (_stage == 0) { c.Rudder = -_spinDir; c.Elevator = -1; if (_t > 0.6f) { _stage = 1; _t = 0; } }                    // P, A, R
                    else if (_stage == 1) { c.Rudder = -_spinDir; c.Elevator = Mathf.MoveTowards((float)c.Elevator, 0.25f, dt * 2.5f);    // E: briskly forward
                                            if (System.Math.Abs(rd.YawRateDps) < 10 || _t > 8f) { _stage = 2; _t = 0; } }
                    else if (_stage == 2) { c.Rudder = 0; c.Elevator = Mathf.MoveTowards((float)c.Elevator, -0.35f, dt * 0.6f);        // ease out of the dive
                                            if (rd.PitchDeg > -2 || _t > 6f) { _stage = 3; _t = 0; } }
                    else { c.ResetToTrim(); Running = null; }
                    break;
                case "flare-hands-off":
                    c.ElevatorFree = true; break;
                case "hammerhead": Hammerhead(c, rd, dt); break;
            }
        }

        /// <summary>The hammerhead, flown on the airplane's true attitude (no Euler angles — they fold at the vertical): the nose's
        /// elevation above the horizon, the right wing's tilt, the body rates.</summary>
        private void Hammerhead(WidgetControls c, AeroHost.Readout rd, float dt)
        {
            var s = Widget.Ac.State;
            var nose = s.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0));
            var wing = s.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 1, 0));
            float elev = Mathf.Asin(Mathf.Clamp((float)-nose.Z, -1f, 1f)) * Mathf.Rad2Deg;   // nose above the horizon, °
            float tilt = Mathf.Asin(Mathf.Clamp((float)wing.Z, -1f, 1f));                     // right wing down, rad
            float p = (float)s.Rates.X, q = (float)s.Rates.Y, r = (float)s.Rates.Z;           // rad/s
            float alpha = (float)rd.AlphaDeg, kias = (float)rd.Kias;
            float WingsLevel() { _ailI = Mathf.Clamp(_ailI - dt * 0.4f * tilt, -0.3f, 0.3f); return Mathf.Clamp(_ailI + 0.15f * (-6f * tilt - p), -0.6f, 0.6f); }
            float Damp() => Mathf.Clamp(-0.15f * p, -0.6f, 0.6f);
            // Pitch flown as a RATE (the short period is quick; a rate loop on it is steady): elevator from the pitch-rate error,
            // with an integrator for the trim. Above ~12° α it pushes instead (a wing drop in the pull-out spirals it).
            float Rate(float qCmd)
            {
                if (alpha > 12f) qCmd = Mathf.Min(qCmd, -(alpha - 12f) * 2f * Mathf.Deg2Rad);   // past ~12° α: ease off, then push
                float e = q - qCmd;                                  // nose coming up faster than wanted → push (+)
                _nzI = Mathf.Clamp(_nzI + dt * 0.8f * e, -0.9f, 0.5f);
                return Mathf.Clamp(_nzI + 0.25f * e, -1f, 1f);
            }
            float v = Mathf.Max(15f, (float)s.Velocity.Length);
            float Pull(float g) => Rate(9.81f * g / v);
            float Hold(float target) => Rate(Mathf.Clamp(1.5f * (target - elev), -40f, 40f) * Mathf.Deg2Rad);
            // A vertical line: the line's direction in body axes — pitch the nose onto it with the elevator, yaw it on with the rudder.
            (float ele, float rud) Line(double vz)
            {
                var t = s.Attitude.Conjugate().Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 0, vz));
                float qCmd = Mathf.Clamp(2f * (float)-t.Z, -0.6f, 0.6f);
                return (Rate(qCmd), Mathf.Clamp(3f * (float)t.Y - 0.4f * r, -1f, 1f));
            }
            switch (_stage)
            {
                case 0: c.Throttle01 = 1; c.Aileron = WingsLevel(); c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f); c.Elevator = Hold(1f);   // build the speed
                        if (_t > 4f) { _stage = 1; _t = 0; } break;
                case 1: c.Elevator = Pull(4f); c.Aileron = WingsLevel(); c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f);             // up to the vertical
                        if (elev > 72f) { _stage = 2; _t = 0; } break;
                case 2: { var (e, ru) = Line(-1); c.Elevator = e; c.Rudder = ru; c.Aileron = Damp();                              // the up-line
                        if (kias < PivotKias) { _stage = 3; _t = 0; } break; }
                case 3: c.Rudder = -1; c.Aileron = Damp(); c.Elevator = Rate(0f);                       // the pivot
                        if (elev < -55f) { _stage = 4; _t = 0; } break;
                case 4: { c.Throttle01 = 0; var (e, ru) = Line(1); c.Elevator = e; c.Rudder = _t < 0.5f ? Mathf.Clamp(-0.8f * r, -1f, 1f) : ru; c.Aileron = Damp(); // stop the yaw: the down-line
                        if ((_t > 1.8f && kias > 95f) || _t > 5f) { _stage = 5; _t = 0; } break; }
                case 5: c.Elevator = Pull(4f); c.Aileron = WingsLevel(); c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f); c.Throttle01 = 0.3; // pull out
                        if (elev > 0f) { _stage = 6; _t = 0; } break;
                default: c.Throttle01 = 0.75; c.Elevator = Hold(2f); c.Aileron = WingsLevel(); c.Rudder = 0;
                        if (_t > 2.5f) Running = null; break;
            }
        }
    }
}
