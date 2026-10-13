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
        public static readonly string[] AeroPresets = { "hammerhead", "torque-roll", "tow-release", "tailslide" };
        /// <summary>The tow-release preset: seconds on tow before the glider pulls the release.</summary>
        public float ReleaseAfterS = 12f;
        /// <summary>High-tow law: height error → climb-rate correction (1/s), and flight-path error → pitch rate (1/s).</summary>
        public float TowHeightGain = 0.15f, TowPathGain = 0.4f;   // soft: the tug's own speed cycle bows the rope a metre or two, no snatch
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
            if (name == "tow-release" && !Widget.Tow.Active) Widget.Tow.Start();
            if (spin && Widget.Current != AeroWidget.Scenario.Spin) Widget.Load(AeroWidget.Scenario.Spin, Widget.AircraftId, Widget.Flaps);
            if (flare && Widget.Current != AeroWidget.Scenario.Flare) Widget.Load(AeroWidget.Scenario.Flare, Widget.AircraftId, Widget.Flaps);
            // A flare preset restarts the approach — on FINAL it restarts FINAL (its 300 ft start, its runway, the view in use).
            if (flare && Widget.Condition == "final") Widget.StartCondition("final", Widget.AircraftId, Widget.View, null);
            else if (name == "spin-entry" || name == "spin-developed" || name.StartsWith("flare")) Widget.Load(Widget.Current, Widget.AircraftId, Widget.Flaps);
            Running = name; _stage = 0; _t = 0; _nzI = 0; _ailI = 0;
            if (name == "tow-release") _nzI = (float)Widget.Controls.TrimStick;   // on tow from the trimmed stick (WidgetTow.Start)
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
                case "torque-roll": Hammerhead(c, rd, dt, torque: true); break;
                case "tailslide": Hammerhead(c, rd, dt, slide: true); break;
                case "tow-release": TowRelease(c, rd, dt); break;
            }
        }

        /// <summary>TOW, THEN RELEASE (the outro): the glider flown in HIGH TOW — a few metres above the tug, on its track — then
        /// the yellow knob and the classic clearing turn: a climbing turn to the RIGHT (the tug goes left), held at 50 kt.
        /// Stick and rudder only; the rope and the tug are the game's own physics.</summary>
        private void TowRelease(WidgetControls c, AeroHost.Readout rd, float dt)
        {
            var tow = Widget.Tow; var s = Widget.Ac.State;
            var wing = s.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 1, 0));
            float tilt = Mathf.Asin(Mathf.Clamp((float)wing.Z, -1f, 1f));
            float p = (float)s.Rates.X, q = (float)s.Rates.Y, r = (float)s.Rates.Z, alpha = (float)rd.AlphaDeg;
            // Firmer than the hammerhead's: the 2-33's long span overbanks in a turn (the outer wing faster) and must be held off.
            float Bank(float deg) { _ailI = Mathf.Clamp(_ailI - dt * 0.6f * (tilt - deg * Mathf.Deg2Rad), -0.3f, 0.3f); return Mathf.Clamp(_ailI + 0.15f * (-14f * (tilt - deg * Mathf.Deg2Rad) - 2f * p), -0.7f, 0.7f); }
            float Rate(float qCmd)
            {
                if (alpha > 12f) qCmd = Mathf.Min(qCmd, -(alpha - 12f) * 2f * Mathf.Deg2Rad);
                float e = q - qCmd;
                _nzI = Mathf.Clamp(_nzI + dt * 0.8f * e, -0.9f, 0.5f);
                return Mathf.Clamp(_nzI + 0.25f * e, -1f, 1f);
            }
            var vW = s.Attitude.Rotate(s.Velocity);
            float v = Mathf.Max(15f, (float)vW.Length), vs = (float)-vW.Z;
            c.Throttle01 = 0;
            if (_stage == 0)
            {
                if (!tow.Active) { Running = null; return; }
                var t = tow.Tug.State; var tv = t.Attitude.Rotate(t.Velocity);
                var tf = new FlyingGame.Core.MathTypes.Vec3(tv.X, tv.Y, 0); tf = tf * (1.0 / System.Math.Max(1e-3, tf.Length));
                var right = new FlyingGame.Core.MathTypes.Vec3(-tf.Y, tf.X, 0);
                var rel = s.Position - t.Position;
                float lat = (float)(rel.X * right.X + rel.Y * right.Y), latV = (float)((vW.X - tv.X) * right.X + (vW.Y - tv.Y) * right.Y);
                float hErr = (float)((-t.Position.Z + 3.0) - -s.Position.Z);        // high tow: 3 m above the tug
                // Fly the tug's flight path, plus a gentle correction toward the slot (a hard height loop porpoised the glider:
                // slack, snatch, weak link).
                float vsWant = Mathf.Clamp(TowHeightGain * hErr, -1.5f, 1.5f) + (float)-tv.Z;
                c.Elevator = Rate(TowPathGain * (vsWant - vs) / v);
                c.Aileron = Bank(Mathf.Clamp(-1.2f * lat - 3f * latV, -20f, 20f));
                c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f);
                if (_t > ReleaseAfterS) { tow.Release(); _stage = 1; _t = 0; }
            }
            else if (_stage == 1)
            {
                // The clearing turn: 30° right, climbing on the energy of the tow, settling at 50 kt.
                // Rolled in at 10°/s (a step to 30° overbanked to 42°), and the pull for the bank it actually has.
                c.Aileron = Bank(Mathf.Min(30f, 10f * _t)); c.Rudder = Mathf.Clamp(-0.3f * r + 0.05f, -0.3f, 0.3f);
                float phi = Mathf.Clamp(tilt, -0.8f, 0.8f);
                c.Elevator = Rate(Mathf.Clamp(0.004f * ((float)rd.Kias - 50f), -0.15f, 0.3f) + 9.81f * Mathf.Tan(phi) * Mathf.Sin(phi) / v);
                if (_t > 10f) { _stage = 2; _t = 0; }
            }
            else { c.Aileron = Bank(0f); c.Elevator = Rate(Mathf.Clamp(0.004f * ((float)rd.Kias - 50f), -0.15f, 0.3f)); c.Rudder = 0; if (_t > 3f) Running = null; }
        }

        /// <summary>The hammerhead, flown on the airplane's true attitude (no Euler angles — they fold at the vertical): the nose's
        /// elevation above the horizon, the right wing's tilt, the body rates.</summary>
        /// <param name="torque">TORQUE ROLL (owner 2026-10-12, the Ep4 open): the same pull to the vertical at full power, then the
        /// up-line held with elevator and rudder only — ailerons neutral, nothing damping the roll — so whatever roll there is
        /// comes from the engine (its torque, the slipstream's swirl, the prop's gyroscopics) as the speed dies. Slow, the controls
        /// are held still at neutral (full power) while it stops, slides back and falls through; then the down-line and pull-out.</param>
        /// <summary>TAILSLIDE (owner 2026-10-12, via Glass: "a tailslide with smoke on with the extra … and with the swift glider"):
        /// the hammerhead's up-line, power to idle on it, and at the top the controls held firmly neutral — the airplane stops,
        /// slides backwards down its own smoke and the nose falls through; then the hammerhead's down-line and pull-out. A
        /// glider dives for its entry speed first.</summary>
        public float SlideEntryKias = 160f, GliderSlideEntryKias = 115f;

        private void Hammerhead(WidgetControls c, AeroHost.Readout rd, float dt, bool torque = false, bool slide = false)
        {
            var s = Widget.Ac.State;
            var nose = s.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(1, 0, 0));
            var wing = s.Attitude.Rotate(new FlyingGame.Core.MathTypes.Vec3(0, 1, 0));
            float elev = Mathf.Asin(Mathf.Clamp((float)-nose.Z, -1f, 1f)) * Mathf.Rad2Deg;   // nose above the horizon, °
            float tilt = Mathf.Asin(Mathf.Clamp((float)wing.Z, -1f, 1f));                     // right wing down, rad
            float p = (float)s.Rates.X, q = (float)s.Rates.Y, r = (float)s.Rates.Z;           // rad/s
            float alpha = (float)rd.AlphaDeg, kias = (float)rd.Kias;
            bool glider = Widget.Config.Propulsion == null;
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
                case 0: c.Throttle01 = 1; c.Aileron = WingsLevel(); c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f);              // build the speed
                        if (slide && glider) { c.Elevator = Hold(-35f); if (kias > GliderSlideEntryKias) { _stage = 1; _t = 0; } }      // (a glider dives for it)
                        else if (slide) { c.Elevator = Hold(kias < SlideEntryKias - 20f ? -20f : 1f); if (kias > SlideEntryKias || _t > 25f) { _stage = 1; _t = 0; } }
                        else { c.Elevator = Hold(1f); if (_t > 4f) { _stage = 1; _t = 0; } }
                        break;
                case 1: c.Elevator = Pull(4f); c.Aileron = WingsLevel(); c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f);             // up to the vertical
                        if (elev > 72f) { _stage = 2; _t = 0; } break;
                case 2: { var (e, ru) = Line(-1); c.Elevator = e; c.Rudder = ru; c.Aileron = torque ? 0 : Damp();                // the up-line
                        if (slide) { if (_t > 0.6f) c.Throttle01 = 0; if (kias < 20f) { _stage = 8; _t = 0; } }                            // (tailslide: power off on the line)
                        else if (torque) { if (kias < 25f) { _stage = 7; _t = 0; } }
                        else if (kias < PivotKias) { _stage = 3; _t = 0; } break; }
                case 7: c.Throttle01 = 1; c.Elevator = 0; c.Rudder = 0; c.Aileron = 0;                                              // (torque roll) hold still
                        if (elev < -45f || _t > 10f) { _stage = 4; _t = 0; } break;
                case 8: c.Throttle01 = 0; c.Elevator = 0; c.Rudder = 0; c.Aileron = 0;                                              // (tailslide) controls held neutral: stop, slide back, fall through
                        if (elev < -45f || _t > 12f) { _stage = 4; _t = 0; } break;
                case 3: c.Rudder = -1; c.Aileron = Damp(); c.Elevator = Rate(0f);                       // the pivot
                        if (elev < -55f) { _stage = 4; _t = 0; } break;
                case 4: { c.Throttle01 = 0; var (e, ru) = Line(1); c.Elevator = e; c.Rudder = _t < 0.5f ? Mathf.Clamp(-0.8f * r, -1f, 1f) : ru; c.Aileron = Damp(); // stop the yaw: the down-line
                        if ((_t > 1.8f && kias > 95f) || _t > 5f) { _stage = 5; _t = 0; } break; }
                case 5: c.Elevator = Pull(4f); c.Aileron = WingsLevel(); c.Rudder = Mathf.Clamp(-0.3f * r, -0.3f, 0.3f); c.Throttle01 = 0.3; // pull out
                        if (elev > 0f) { _stage = 6; _t = 0; } break;
                case 6: c.Throttle01 = 0.75; c.Elevator = Hold(2f); c.Aileron = WingsLevel(); c.Rudder = 0;
                        if (_t > 2.5f) Running = null; break;
            }
        }
    }
}
