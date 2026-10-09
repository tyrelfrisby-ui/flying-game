using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// AUTOPILOT (protocol 8; owner 2026-10-09: "more like a real autopilot with altitude hold, vertical speed, yaw damper (auto
    /// rudder coordination)"). Modes, like a GA / transport flight director:
    ///   PITCH  — ALT (hold an altitude) or VS (hold a vertical speed; with a target altitude it captures it: VS → ALT* → ALT).
    ///            Elevator servo + PITCH TRIM servo (the trim slowly takes the steady load off the elevator).
    ///            FLC (level change, owner 2026-10-09): PITCH holds the selected AIRSPEED while the power is set — climb power
    ///            toward a higher altitude, idle toward a lower one — and it captures the altitude (FLC → ALT* → ALT).
    ///   ROLL   — ROL (wings level) or HDG (the heading selector: a bug the airplane turns to, ≤ 20° bank).
    ///   LNAV / LOC / APP — the buttons exist (state, hello, the remote) but are NOT active yet: a later build.
    ///   YD     — yaw damper / auto-coordination: rudder centres the ball and damps the yaw rate. Can be on with the AP off.
    ///   A/T    — autothrottle holds the airspeed with the power.
    /// Rate-limited servos; a well-tuned GA autopilot, not a perfect one. It flies through the ordinary controls, so the
    /// control display and the state show ITS inputs. When it runs out (full power, elevator/trim at a stop, stall) it says so
    /// and keeps trying sensibly — speed over altitude, never stalling deliberately. Near/behind the neutral point the hold
    /// gets twitchy or fails: shown, not hidden. Pilot stick input disconnects the AP ("AP DISC", 2 s), rudder input the YD;
    /// loading changes never do.
    /// </summary>
    public sealed class WidgetAutopilot
    {
        private readonly AeroWidget _w;
        public WidgetAutopilot(AeroWidget w) { _w = w; }

        public bool On { get; private set; }               // AP engaged (pitch + roll)
        public bool YawDamper;                              // independent of On
        public bool AutoThrottle = true;
        /// <summary>Lesson use: pitch channel only (the lesson flies the ailerons/rudder), and allowed to fly into the stall.</summary>
        public bool PitchOnly, AllowStall;
        public string PitchMode = "alt";                    // "alt" | "vs" | "flc" | "alt*" (capturing)
        public string RollMode = "rol";                     // "rol" | "hdg"
        public double AltFt, VsFpm, Kias, HdgDeg;
        /// <summary>Lesson pitch mode "aoa": hold an angle of attack (the target ramps at ≤ 1.5°/s from where it is).</summary>
        public double AoaTarget, AoaCmd;
        public bool HasAltTarget = true;
        public double Elevator, Trim, Power;                // its outputs (stick −1…1: + = push; power 0…1)
        public string Status = "holding";
        public float DiscUntil = -10f;
        public bool Disc => Time.unscaledTime < DiscUntil;
        private double _rudI, _thrI, _lastKias;
        /// <summary>Lateral/approach modes not built yet: the buttons exist, pressing them says so.</summary>
        public static readonly string[] FutureModes = { "lnav", "loc", "app" };
        /// <summary>Heading selector: set or bump the bug (sync = the current heading).</summary>
        public void Heading(double? set, double? bump, bool sync)
        {
            if (sync) HdgDeg = _w.Read().HeadingDeg;
            if (set.HasValue) HdgDeg = set.Value;
            if (bump.HasValue) HdgDeg += bump.Value;
            HdgDeg = (HdgDeg % 360 + 360) % 360;
        }
        private float _powerSat, _eleSat, _stallT, _clock;
        private readonly System.Collections.Generic.List<(float t, double err)> _errs = new();

        /// <summary>Engage / change modes (any field omitted keeps its value; targets default to the current values).</summary>
        public void Engage(double? altFt, double? kias, string pitchMode = null, double? vsFpm = null, string rollMode = null, double? hdg = null, bool? yd = null, bool? at = null)
        {
            var r = _w.Read(); var c = _w.Controls;
            if (!On) { Trim = c.Elevator; Elevator = 0; Power = c.Throttle01; _thrI = c.Throttle01; _rudI = c.Rudder; AltFt = r.HeightFt; Kias = r.Kias; HdgDeg = r.HeadingDeg; VsFpm = 0; PitchMode = "alt"; RollMode = "rol"; HasAltTarget = true; YawDamper = true; }
            if (altFt.HasValue) { AltFt = altFt.Value; HasAltTarget = true; }
            if (kias.HasValue) Kias = kias.Value;
            if (vsFpm.HasValue) { VsFpm = vsFpm.Value; if (pitchMode == null) pitchMode = "vs"; if (!altFt.HasValue) HasAltTarget = false; }
            if (pitchMode != null) PitchMode = pitchMode == "vs" ? "vs" : pitchMode == "flc" ? "flc" : "alt";
            if (hdg.HasValue) { HdgDeg = hdg.Value; if (rollMode == null) rollMode = "hdg"; }
            if (rollMode != null) RollMode = rollMode == "hdg" ? "hdg" : "rol";
            if (yd.HasValue) YawDamper = yd.Value;
            if (at.HasValue) AutoThrottle = at.Value;
            On = true; Status = "holding"; _errs.Clear(); _powerSat = _eleSat = _stallT = 0;
        }

        public void Off(bool disconnect = false)
        {
            if (On && disconnect) DiscUntil = Time.unscaledTime + 2f;
            On = false; PitchOnly = false; AllowStall = false;
        }

        /// <summary>One control step (before the sim step).</summary>
        public void Tick(float dt)
        {
            if (dt <= 0) return;
            var r = _w.Read(); var s = _w.Ac.State; var c = _w.Controls;
            // YAW DAMPER / auto-coordination (also without the AP): rudder centres the ball and damps the yaw rate.
            if (YawDamper && !(On && PitchOnly))
            {
                double beta = r.BetaDeg * System.Math.PI / 180;
                _rudI = System.Math.Clamp(_rudI + 1.2 * beta * dt, -0.6, 0.6);
                c.Rudder = System.Math.Clamp(_rudI + 2.0 * beta - 0.35 * s.Rates.Z * System.Math.Cos(r.RollDeg / 57.3) * (On ? 1 : 0.6), -0.8, 0.8);
            }
            else _rudI = c.Rudder;
            if (!On) return;
            _clock += dt;
            double vs = -r.SinkFpm, altErr = AltFt - r.HeightFt, spdErr = Kias - r.Kias;
            double crit = _w.CriticalAlphaDeg(_w.Ac.FlapFraction), warn = crit - 3;

            // PITCH: ALT holds the altitude through a VS command; VS holds the set rate and (with a target) captures the altitude.
            double vsCmd;
            if (PitchMode == "vs")
            {
                vsCmd = VsFpm;
                if (HasAltTarget && System.Math.Abs(altErr) < System.Math.Max(80, System.Math.Abs(VsFpm) * 0.12) && System.Math.Sign(altErr) == System.Math.Sign(VsFpm)) PitchMode = "alt*";
            }
            else if (PitchMode == "flc")
            {
                // Level change: power to the climb/descent setting, pitch for the airspeed (handled below), capture the altitude.
                vsCmd = 0;
                if (System.Math.Abs(altErr) < System.Math.Max(80, System.Math.Abs(vs) * 0.12)) PitchMode = "alt*";
            }
            else vsCmd = System.Math.Clamp(altErr * 4.0, -700, 700);
            if (PitchMode == "alt*" && System.Math.Abs(altErr) < 20 && System.Math.Abs(vs) < 150) PitchMode = "alt";
            // Speed over altitude: too slow (or near the stall) → less climb, even a descent.
            if (PitchMode == "aoa") vsCmd = vs;
            if (!AllowStall && AutoThrottle && spdErr > 8) vsCmd = System.Math.Min(vsCmd, -(spdErr - 8) * 40);
            if (!AllowStall && r.AlphaDeg > warn) vsCmd = System.Math.Min(vsCmd, -300);
            double q = s.Rates.Y;
            double eCmd = -0.0002 * (vsCmd - vs) + 0.28 * q;   // stick − = pull (nose up); q + = nose-up → damp with push
            if (PitchMode == "aoa")
            {
                AoaCmd = Mathf.MoveTowards((float)AoaCmd, (float)AoaTarget, 1.5f * dt);
                eCmd = 0.035 * (r.AlphaDeg - AoaCmd) + 0.28 * q;   // α too high → push
            }
            else if (PitchMode == "flc")
            {
                // Pitch for airspeed: too slow → push (nose down), too fast → pull; the speed trend damps it.
                double accel = (r.Kias - _lastKias) / System.Math.Max(1e-3, dt); _lastKias = r.Kias;
                eCmd = 0.012 * spdErr - 0.02 * accel + 0.2 * q;
            }
            else _lastKias = r.Kias;
            // A GA servo: about half the stick travel per second (so it can't mask a statically unstable airplane).
            Elevator = Mathf.MoveTowards((float)Elevator, (float)System.Math.Clamp(eCmd, -0.6, 0.6), dt * 0.5f);
            Trim = Mathf.MoveTowards((float)Trim, (float)System.Math.Clamp(Trim + Elevator, -1, 1), dt * 0.12f);   // the trim servo
            double ele = System.Math.Clamp(Trim + Elevator, -1, 1);
            // A/T: PI on the airspeed error, rate-limited.
            if (PitchMode == "flc")
            {
                // FLC sets the power: climb power going up, idle coming down (A/T stays out of the speed loop).
                Power = Mathf.MoveTowards((float)Power, altErr > 0 ? 1f : 0f, dt * 0.25f);
                c.Throttle01 = Power;
            }
            else if (AutoThrottle)
            {
                _thrI = System.Math.Clamp(_thrI + 0.006 * spdErr * dt, 0, 1);
                Power = Mathf.MoveTowards((float)Power, (float)System.Math.Clamp(_thrI + 0.03 * spdErr, 0, 1), dt * 0.25f);
                c.Throttle01 = Power;
            }
            else Power = c.Throttle01;
            // ROLL: wings level, or a heading (bank ≤ 20°, 1:1 with the heading error up to that).
            double bankCmd = RollMode == "hdg" ? System.Math.Clamp(Mathf.DeltaAngle((float)r.HeadingDeg, (float)HdgDeg) * 1.0, -20, 20) : 0;
            if (!PitchOnly) c.Aileron = System.Math.Clamp(0.025 * (bankCmd - r.RollDeg) - 0.3 * s.Rates.X, -0.5, 0.5);
            c.Elevator = ele; c.ElevatorFree = false;

            // What it can't do.
            _powerSat = AutoThrottle && Power > 0.985 && spdErr > 3 ? _powerSat + dt : 0;
            _eleSat = System.Math.Abs(ele) > 0.97 ? _eleSat + dt : 0;
            _stallT = r.AlphaDeg > warn ? _stallT + dt : 0;
            _errs.Add((_clock, PitchMode == "vs" ? vs - VsFpm : altErr)); while (_errs.Count > 0 && _clock - _errs[0].t > 20f) _errs.RemoveAt(0);
            int crossings = 0; double amp = 0;
            for (int i = 1; i < _errs.Count; i++) { if (System.Math.Sign(_errs[i].err) != System.Math.Sign(_errs[i - 1].err)) crossings++; amp = System.Math.Max(amp, System.Math.Abs(_errs[i].err)); }
            Status = _stallT > 1.5f ? "cantHold:stall" : _eleSat > 2f ? "cantHold:elevator" : _powerSat > 4f ? "cantHold:power"
                   : crossings >= 4 && amp > (PitchMode == "vs" ? 300 : 60) ? "oscillating" : "holding";
        }

        /// <summary>The flight-mode annunciation: "AP · ALT 3000 · HDG 270 · YD · A/T 100".</summary>
        public string Annunciation
        {
            get
            {
                if (!On) return YawDamper ? "YD" : null;
                string why = Status switch { "cantHold:power" => "CAN'T HOLD — FULL POWER", "cantHold:elevator" => "CAN'T HOLD — ELEVATOR/TRIM LIMIT", "cantHold:stall" => "CAN'T HOLD — STALL", "oscillating" => "OSCILLATING", _ => null };
                string pm = PitchMode == "vs" ? $"VS {VsFpm:+0;-0}" : PitchMode == "flc" ? $"FLC {Kias:0} → {AltFt:0}" : PitchMode == "alt*" ? $"ALT* {AltFt:0}" : $"ALT {AltFt:0}";
                string rm = RollMode == "hdg" ? $"HDG {HdgDeg:000}" : "ROL";
                string fma = $"AP · {pm} · {rm}{(YawDamper ? " · YD" : "")}{(AutoThrottle && PitchMode != "flc" ? $" · A/T {Kias:0}" : "")}";
                return why != null ? $"{why} · {pm}" : fma;
            }
        }
    }
}
