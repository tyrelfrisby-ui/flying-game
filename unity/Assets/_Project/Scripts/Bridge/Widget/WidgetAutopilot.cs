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
    ///   PIT / BANK — CONTROL WHEEL STEERING (owner 2026-10-11): hold a selected pitch ATTITUDE (−30…+30°) and/or BANK angle
    ///            (45° L … 45° R), nothing else — no speed, altitude or path target, so the airplane's own answer to power and
    ///            attitude is what you see (the FOUR FORCES lesson). The pilot's stick doesn't disconnect it: while the pilot
    ///            flies, the AP lets go; on release it holds the attitude it was left at. Selecting PIT turns A/T off (the
    ///            throttle is the pilot's). Stall protection stays on (α kept below the warning) unless a lesson allows it.
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
        private readonly AeroHost _w;
        public WidgetAutopilot(AeroHost w) { _w = w; }

        public bool On { get; private set; }               // AP engaged (pitch + roll)
        public bool YawDamper;                              // independent of On
        /// <summary>A/T ENGAGED: holds Kias with the power — with or without the AP (owner 2026-10-11: on every airplane with an
        /// engine, for the examples, whether the type has one or not). Moving the throttle disconnects it ("A/T DISC").</summary>
        public bool AutoThrottle;
        /// <summary>Engaging the AP from off brings the A/T in with it (the widget: yes; the game: no).</summary>
        public bool AtWithAp = true;
        public float AtDiscUntil = -10f;
        public bool AtDisc => Time.unscaledTime < AtDiscUntil;
        public bool HasEngine => _w.Config?.Propulsion != null;
        /// <summary>Engage / disengage the A/T (target: the given speed, else the current one when the AP isn't already holding one).</summary>
        public void SetAutoThrottle(bool on, double? kias = null)
        {
            var c = _w.AeroControls;
            if (on && !HasEngine) on = false;
            if (on && !AutoThrottle) { _thrI = c.Throttle01; Power = c.Throttle01; if (!On) Kias = _w.Read().Kias; }
            if (kias.HasValue) Kias = System.Math.Clamp(kias.Value, 30, 400);
            AutoThrottle = on;
        }
        /// <summary>The pilot moved the throttle: the A/T lets go.</summary>
        public void PilotThrottle() { if (AutoThrottle) { AutoThrottle = false; AtDiscUntil = Time.unscaledTime + 2f; } }
        /// <summary>Lesson use: pitch channel only (the lesson flies the ailerons/rudder), and allowed to fly into the stall.</summary>
        public bool PitchOnly, AllowStall;
        public string PitchMode = "alt";                    // "alt" | "vs" | "flc" | "alt*" (capturing)
        public string RollMode = "rol";                     // "rol" | "hdg"
        public double AltFt, VsFpm, Kias, HdgDeg;
        /// <summary>Lesson pitch mode "aoa": hold an angle of attack (the target ramps at ≤ 1.5°/s from where it is).</summary>
        public double AoaTarget, AoaCmd;
        /// <summary>CWS targets (degrees; pitch + = nose up, bank + = right wing down) and the ramped commands (≤ 3°/s pitch, ≤ 6°/s bank).</summary>
        public double PitchDeg, PitchCmd, BankDeg, BankCmd;
        public const double PitchLimit = 30, BankLimit = 45;
        public bool Cws => On && (PitchMode == "pit" || RollMode == "bank");
        private float _cwsUntil = -10f;
        /// <summary>The pilot is flying through CWS right now (the AP has let go of the stick).</summary>
        public bool CwsOverride => Cws && Time.unscaledTime < _cwsUntil;
        /// <summary>The pilot moved the stick: in CWS the AP lets go (and holds the new attitude on release); otherwise it disconnects.</summary>
        public void PilotInput() { if (Cws) _cwsUntil = Time.unscaledTime + 0.3f; else if (On) Off(disconnect: true); }
        /// <summary>Select a pitch attitude and/or a bank angle (engages the AP in PIT / BANK if needed).</summary>
        public void Attitude(double? pitchDeg, double? bankDeg)
        {
            var r = _w.Read();
            if (!On) Engage(null, null, "pit", null, "bank");   // CWS from AP-off: both axes hold the present attitude
            if (pitchDeg.HasValue)
            {
                if (!On || PitchMode != "pit") { Engage(null, null, "pit"); PitchCmd = r.PitchDeg; }
                PitchDeg = System.Math.Clamp(pitchDeg.Value, -PitchLimit, PitchLimit);
            }
            if (bankDeg.HasValue)
            {
                if (!On || RollMode != "bank") { Engage(null, null, null, null, "bank"); BankCmd = r.RollDeg; }
                BankDeg = System.Math.Clamp(bankDeg.Value, -BankLimit, BankLimit);
            }
        }
        public bool HasAltTarget = true;
        public double Elevator, Trim, Power;                // its outputs (stick −1…1: + = push; power 0…1)
        public string Status = "holding";
        public float DiscUntil = -10f;
        public bool Disc => Time.unscaledTime < DiscUntil;
        private double _rudI, _thrI, _lastKias, _ailI;
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
            var r = _w.Read(); var c = _w.AeroControls;
            if (!On) { Trim = c.Elevator; Elevator = 0; if (!AutoThrottle) { Power = c.Throttle01; _thrI = c.Throttle01; Kias = r.Kias; } _rudI = c.Rudder; AltFt = r.HeightFt; HdgDeg = r.HeadingDeg;
                       if (AtWithAp && at == null && HasEngine) AutoThrottle = true; VsFpm = 0; PitchMode = "alt"; RollMode = "rol"; HasAltTarget = true; YawDamper = true;
                       PitchDeg = PitchCmd = System.Math.Round(r.PitchDeg); BankDeg = BankCmd = 0; }
            if (altFt.HasValue) { AltFt = altFt.Value; HasAltTarget = true; }
            if (kias.HasValue) Kias = kias.Value;
            if (vsFpm.HasValue) { VsFpm = vsFpm.Value; if (pitchMode == null) pitchMode = "vs"; if (!altFt.HasValue) HasAltTarget = false; }
            if (pitchMode == "pit" && PitchMode != "pit") { PitchCmd = r.PitchDeg; PitchDeg = System.Math.Clamp(System.Math.Round(r.PitchDeg), -PitchLimit, PitchLimit); AutoThrottle = false; }
            if (rollMode == "bank" && RollMode != "bank") { BankCmd = r.RollDeg; BankDeg = System.Math.Clamp(System.Math.Round(r.RollDeg), -BankLimit, BankLimit); }
            if (pitchMode != null) PitchMode = pitchMode == "vs" ? "vs" : pitchMode == "flc" ? "flc" : pitchMode == "pit" ? "pit" : "alt";
            if (hdg.HasValue) { HdgDeg = hdg.Value; if (rollMode == null) rollMode = "hdg"; }
            if (rollMode != null) RollMode = rollMode == "hdg" ? "hdg" : rollMode == "bank" ? "bank" : "rol";
            if (yd.HasValue) YawDamper = yd.Value;
            if (at.HasValue) SetAutoThrottle(at.Value, kias.HasValue ? Kias : null);   // (the target set above — engaging from off reset it to the present speed)
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
            var r = _w.Read(); var s = _w.Ac.State; var c = _w.AeroControls;
            // YAW DAMPER / auto-coordination (also without the AP): rudder centres the ball and damps the yaw rate.
            if (YawDamper && !(On && PitchOnly))
            {
                double beta = r.BetaDeg * System.Math.PI / 180;
                _rudI = System.Math.Clamp(_rudI + 1.2 * beta * dt, -0.6, 0.6);
                c.Rudder = System.Math.Clamp(_rudI + 2.0 * beta - 0.35 * s.Rates.Z * System.Math.Cos(r.RollDeg / 57.3) * (On ? 1 : 0.6), -0.8, 0.8);
            }
            else _rudI = c.Rudder;
            if (!On) { if (AutoThrottle) SpeedHold(dt, r.Kias); return; }   // the A/T on its own
            _clock += dt;
            // CWS: while the pilot flies, the AP lets go and follows (on release it holds the attitude it was left at).
            bool pilotFlying = CwsOverride;
            if (pilotFlying)
            {
                if (PitchMode == "pit") PitchDeg = PitchCmd = System.Math.Clamp(r.PitchDeg, -PitchLimit, PitchLimit);
                if (RollMode == "bank") BankDeg = BankCmd = System.Math.Clamp(r.RollDeg, -BankLimit, BankLimit);
                Trim = c.Elevator; Elevator = 0; Status = "holding";
                if (AutoThrottle && PitchMode != "flc") SpeedHold(dt, r.Kias);
                return;
            }
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
            else if (PitchMode == "pit")
            {
                // CWS pitch: hold the selected attitude (the target ramps ≤ 3°/s); never past the stall warning unless allowed.
                PitchCmd = Mathf.MoveTowards((float)PitchCmd, (float)PitchDeg, 3f * dt);
                eCmd = 0.03 * (r.PitchDeg - PitchCmd) + 0.28 * q;   // nose above the target → push
                if (!AllowStall && r.AlphaDeg > warn) eCmd = System.Math.Max(eCmd, 0.05 * (r.AlphaDeg - warn + 0.5));
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
            else if (AutoThrottle) SpeedHold(dt, r.Kias);
            else Power = c.Throttle01;
            // ROLL: wings level, or a heading (bank ≤ 20°, 1:1 with the heading error up to that).
            if (RollMode == "bank") BankCmd = Mathf.MoveTowards((float)BankCmd, (float)BankDeg, 6f * dt);
            double bankCmd = RollMode == "hdg" ? System.Math.Clamp(Mathf.DeltaAngle((float)r.HeadingDeg, (float)HdgDeg) * 1.0, -20, 20) : RollMode == "bank" ? BankCmd : 0;
            // BANK (CWS) adds a slow integral: it holds the selected bank exactly (torque, a slip) — wings level means level.
            if (RollMode == "bank") _ailI = System.Math.Clamp(_ailI + 0.004 * (bankCmd - r.RollDeg) * dt, -0.25, 0.25); else _ailI = 0;
            if (!PitchOnly) c.Aileron = System.Math.Clamp(0.025 * (bankCmd - r.RollDeg) + _ailI - 0.3 * s.Rates.X, -0.5, 0.5);
            c.Elevator = ele; c.ElevatorFree = false;

            // What it can't do.
            _powerSat = AutoThrottle && Power > 0.985 && spdErr > 3 ? _powerSat + dt : 0;
            _eleSat = System.Math.Abs(ele) > 0.97 ? _eleSat + dt : 0;
            _stallT = r.AlphaDeg > warn ? _stallT + dt : 0;
            _errs.Add((_clock, PitchMode == "vs" ? vs - VsFpm : PitchMode == "pit" ? (r.PitchDeg - PitchCmd) * 20 : altErr)); while (_errs.Count > 0 && _clock - _errs[0].t > 20f) _errs.RemoveAt(0);
            int crossings = 0; double amp = 0;
            for (int i = 1; i < _errs.Count; i++) { if (System.Math.Sign(_errs[i].err) != System.Math.Sign(_errs[i - 1].err)) crossings++; amp = System.Math.Max(amp, System.Math.Abs(_errs[i].err)); }
            Status = _stallT > 1.5f ? "cantHold:stall" : _eleSat > 2f ? "cantHold:elevator" : _powerSat > 4f ? "cantHold:power"
                   : crossings >= 4 && amp > (PitchMode == "vs" ? 300 : 60) ? "oscillating" : "holding";
        }

        /// <summary>A/T speed mode: PI on the airspeed error, rate-limited (≤ 25 % of the lever per second).</summary>
        private void SpeedHold(float dt, double kias)
        {
            double spdErr = Kias - kias;
            _thrI = System.Math.Clamp(_thrI + 0.006 * spdErr * dt, 0, 1);
            Power = Mathf.MoveTowards((float)Power, (float)System.Math.Clamp(_thrI + 0.03 * spdErr, 0, 1), dt * 0.25f);
            _w.AeroControls.Throttle01 = Power;
        }

        /// <summary>The flight-mode annunciation: "AP · ALT 3000 · HDG 270 · YD · A/T 100".</summary>
        public string Annunciation
        {
            get
            {
                if (!On) { string at = AutoThrottle ? $"A/T {Kias:0}" : null; return YawDamper ? (at != null ? $"YD · {at}" : "YD") : at; }
                string why = Status switch { "cantHold:power" => "CAN'T HOLD — FULL POWER", "cantHold:elevator" => "CAN'T HOLD — ELEVATOR/TRIM LIMIT", "cantHold:stall" => "CAN'T HOLD — STALL", "oscillating" => "OSCILLATING", _ => null };
                string pm = PitchMode == "pit" ? $"PIT {PitchDeg:+0.0;-0.0;0.0}°" : PitchMode == "vs" ? $"VS {VsFpm:+0;-0}" : PitchMode == "flc" ? $"FLC {Kias:0} → {AltFt:0}" : PitchMode == "alt*" ? $"ALT* {AltFt:0}" : $"ALT {AltFt:0}";
                string rm = RollMode == "hdg" ? $"HDG {HdgDeg:000}" : RollMode == "bank" ? (System.Math.Abs(BankDeg) < 0.5 ? "BANK 0°" : $"BANK {System.Math.Abs(BankDeg):0}° {(BankDeg < 0 ? "L" : "R")}") : "ROL";
                string fma = $"{(CwsOverride ? "CWS (PILOT FLYING) · " : "")}AP · {pm} · {rm}{(YawDamper ? " · YD" : "")}{(AutoThrottle && PitchMode != "flc" ? $" · A/T {Kias:0}" : "")}";
                return why != null ? $"{why} · {pm}" : fma;
            }
        }
    }
}
