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
    /// </summary>
    public sealed class WidgetPresets : MonoBehaviour
    {
        public AeroWidget Widget;
        public static readonly string[] SpinPresets = { "spin-entry", "spin-developed", "spin-recovery" };
        public static readonly string[] FlarePresets = { "flare-demo", "flare-hands-off" };
        public string Running { get; private set; }
        public bool FlareDemo => Running == "flare-demo";
        private float _t; private int _stage; private int _next;
        private double _spinDir = -1;   // −1 = left spin (left rudder)

        public void Stop() { Running = null; _stage = 0; _t = 0; }

        public void Next()
        {
            var list = Widget.Current == AeroWidget.Scenario.Spin ? SpinPresets : FlarePresets;
            Run(list[_next++ % list.Length]);
        }

        public bool Run(string name)
        {
            bool spin = System.Array.IndexOf(SpinPresets, name) >= 0, flare = System.Array.IndexOf(FlarePresets, name) >= 0;
            if (!spin && !flare) return false;
            if (spin && Widget.Current != AeroWidget.Scenario.Spin) Widget.Load(AeroWidget.Scenario.Spin, Widget.AircraftId, Widget.Flaps);
            if (flare && Widget.Current != AeroWidget.Scenario.Flare) Widget.Load(AeroWidget.Scenario.Flare, Widget.AircraftId, Widget.Flaps);
            if (name == "spin-entry" || name == "spin-developed" || name.StartsWith("flare")) Widget.Load(Widget.Current, Widget.AircraftId, Widget.Flaps);
            Running = name; _stage = 0; _t = 0;
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
            }
        }
    }
}
