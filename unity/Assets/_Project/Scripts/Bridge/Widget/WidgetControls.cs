using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's controls from every source the owner asked for (2026-10-07): the keyboard, a gamepad/joystick, the TCP
    /// port (Glass Overlay / MCP) and the phone/iPad web remote. Whichever source moved last has the airplane; a preset, while
    /// it runs, overrides them all. Stick values −1…1 (elevator + = nose DOWN, as in the game), throttle 0…1, brake 0…1.
    /// Protocol 4 (2026-10-08): the levers (flaps on the type's detents with the actual position lagging, spoilers with ARM,
    /// gear with its lights, split toe brakes) and the PILOT FLYING — when Glass Overlay manages it, only that pilot's inputs
    /// move the airplane, and a change of pilot is eased (no jerk).
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

        // ---- protocol 4: the levers ----
        /// <summary>The flap HANDLE (0…1 of full travel, on a detent) and the flaps' ACTUAL position, which follows it at the type's rate.</summary>
        public double FlapsHandle, FlapsActual;
        /// <summary>Spoiler / speed-brake lever 0 (RET) … 1 (EXT); ARMED deploys them at touchdown.</summary>
        public double Spoilers; public bool SpoilersArmed;
        public bool GearDown = true;
        public double BrakeL, BrakeR;
        public WidgetAircraftControls.Spec Spec { get; private set; } = WidgetAircraftControls.None;
        /// <summary>HOLD (protocol 5, the spin condition): every source's stick stays where it was put — no spring, no
        /// auto-centre; a gamepad stick nudges the held position instead of setting it.</summary>
        public bool Hold;
        /// <summary>Has anyone moved the power since the start? (FINAL flies the lesson's power until then.)</summary>
        public bool ThrottleTouched { get; private set; }

        // ---- protocol 4: the pilot flying ----
        public string PilotId, PilotName, PilotColor = "#FFFFFF";
        public bool Managed;
        /// <summary>Where the pilot's inputs come from: the Glass Overlay connection that named a GO pilot, or "web-…" for a remote.</summary>
        public string PilotSource;
        public float BannerUntil { get; private set; } = -10f;
        // Handover: the previous pilot's last inputs are held, then eased to the new pilot's over 0.3 s.
        private bool _handing, _handGot;
        private float _handAt, _handGotAt;
        private double _hA, _hE, _hR, _hT, _tA, _tE, _tR, _tT;
        public const float HandoverEase = 0.3f, HandoverWait = 2f, BannerSeconds = 2.5f;

        public void SetTrimStick(double stick) { _trimStick = stick; Elevator = stick; }
        /// <summary>Cruise (protocol 5): the aileron and rudder that hold it straight at 75 % power (torque, P-factor) — where
        /// the stick and pedals go back to when let go, like a rudder trim tab.</summary>
        public void SetLateralTrim(double ail, double rud) { _trimAil = ail; _trimRud = rud; Aileron = ail; Rudder = rud; }
        private double _trimAil, _trimRud;
        public void ResetToTrim() { _trimAil = _trimRud = 0; Aileron = 0; Rudder = 0; Elevator = _trimStick; Brake01 = 0; BrakeL = BrakeR = 0; ElevatorFree = false; }

        /// <summary>A new aircraft / scenario: its control fit, and the levers where the scenario starts.</summary>
        public void Configure(AircraftConfig cfg, string id, double flaps, double spoilers)
        {
            Spec = WidgetAircraftControls.For(id, cfg);
            Hold = false; ThrottleTouched = false;
            _handing = false;   // a (re)start sets every input: a handover still waiting must not put the old ones back
            FlapsHandle = FlapsActual = Spec.Flaps.Length > 1 ? Spec.Snap(flaps) : 0;
            Spoilers = Spec.Spoilers ? spoilers : 0; SpoilersArmed = false;
            GearDown = true;
        }

        /// <summary>The stick, rudder and power into the sim. A glider's throttle lever is its spoiler lever: stowed here,
        /// the spoilers come from <see cref="Spoilers"/> (Aircraft.SpoilerCommand).</summary>
        public ControlInputs Inputs => new(Aileron, Elevator, Rudder, Spec.Engines == 0 ? -1.0 : 1.0 - 2.0 * Throttle01, false, ElevatorFree, false);

        /// <summary>The levers into the aircraft each step (flaps move toward the handle at the type's rate).</summary>
        public void ApplyLevers(Aircraft ac, double dt)
        {
            if (Spec.Flaps.Length > 1) FlapsActual = Mathf.MoveTowards((float)FlapsActual, (float)FlapsHandle, (float)(dt / Spec.FlapTravelSec));
            ac.FlapFraction = FlapsActual;
            // ARMED: the spoilers deploy on the touchdown (weight on the main wheels) and the lever goes to EXT.
            if (Spec.Spoilers && SpoilersArmed && LandingGear.AnyMainWheelOnGround(ac.Config, ac.State)) { Spoilers = 1; SpoilersArmed = false; }
            ac.SpoilerCommand = Spec.Spoilers ? Spoilers : 0;
            if (Spec.Retractable) { ac.GearTravelTime = WidgetAircraftControls.GearTransitSec; ac.SetGear(GearDown); }
            double b = System.Math.Max(BrakeL, BrakeR);
            ac.BrakeInput = b; ac.BrakeBias = b > 1e-6 ? (BrakeR - BrakeL) / b : 0;
        }

        public static string GearLights(Aircraft ac) => !ac.Config.RetractableGear ? "down" : ac.GearExtension >= 0.999 ? "down" : ac.GearExtension <= 0.001 ? "up" : "transit";

        /// <summary>Is this source allowed to fly? Unmanaged: everyone. Managed: only the pilot's source.</summary>
        public bool Accepts(string sourceId) => !Managed || (sourceId != null && sourceId == PilotSource);

        /// <summary>From the TCP port / web remote (any field may be absent). <paramref name="sourceId"/> = the connection
        /// ("tcp-3") or the remote ("web-…"). Returns false when the source isn't the pilot flying (managed) or in review.</summary>
        public bool FromNetwork(JInputs n, string source, string sourceId)
        {
            if (Widget.Paused) return false;   // review is read-only: nothing moves the airplane until resume
            if (!Accepts(sourceId)) return false;
            // Pilot stick or rudder input disconnects the autopilot (protocol 8: "AP DISC"); power alone doesn't.
            if (Widget.Autopilot.On && (n.Aileron.HasValue || n.Elevator.HasValue || n.Rudder.HasValue)) Widget.Autopilot.Off(disconnect: true);
            if (n.Rudder.HasValue) Widget.Autopilot.YawDamper = false;   // the pilot's feet take the rudder back from the yaw damper
            if (n.Aileron.HasValue || n.Elevator.HasValue || n.Rudder.HasValue) Widget.Lessons.Interrupt();
            double ail = Aileron, ele = Elevator, rud = Rudder, thr = Throttle01;
            if (n.Aileron.HasValue) ail = Mathf.Clamp((float)n.Aileron.Value, -1f, 1f);
            if (n.Elevator.HasValue) ele = Mathf.Clamp((float)n.Elevator.Value, -1f, 1f);
            if (n.Rudder.HasValue) rud = Mathf.Clamp((float)n.Rudder.Value, -1f, 1f);
            if (n.Throttle.HasValue) { thr = Mathf.Clamp01((float)n.Throttle.Value); if (System.Math.Abs(thr - Throttle01) > 0.005) ThrottleTouched = true; }
            if (n.HandsOff.HasValue) { ElevatorFree = n.HandsOff.Value; if (n.HandsOff.Value) ele = _trimStick; }
            if (_handing) { _tA = ail; _tE = ele; _tR = rud; _tT = thr; if (!_handGot) { _handGot = true; _handGotAt = Time.unscaledTime; } }
            else { Aileron = ail; Elevator = ele; Rudder = rud; Throttle01 = thr; }
            if (n.Brake.HasValue) { Brake01 = Mathf.Clamp01((float)n.Brake.Value); BrakeL = BrakeR = Brake01; }
            if (n.BrakeL.HasValue) BrakeL = Mathf.Clamp01((float)n.BrakeL.Value);
            if (n.BrakeR.HasValue) BrakeR = Mathf.Clamp01((float)n.BrakeR.Value);
            if (n.Flaps.HasValue && Spec.Flaps.Length > 1) FlapsHandle = Spec.Snap(n.Flaps.Value);
            if (n.Spoilers.HasValue && Spec.Spoilers) Spoilers = System.Math.Clamp(n.Spoilers.Value, 0, 1);
            if (n.SpoilersArmed.HasValue && Spec.Spoilers) SpoilersArmed = n.SpoilersArmed.Value;
            if (n.Gear != null && Spec.Retractable) GearDown = n.Gear != "up";
            _netAt = Time.unscaledTime; SourceLabel = source;
            Widget.Presets.Stop();
            return true;
        }

        public struct JInputs
        {
            public double? Aileron, Elevator, Rudder, Throttle, Brake, BrakeL, BrakeR, Flaps, Spoilers;
            public bool? HandsOff, SpoilersArmed; public string Gear;
        }

        /// <summary>Set (or clear, id = null) the pilot flying. The airplane is handed over without a jerk.</summary>
        public void SetPilot(string id, string name, string color, bool managed, string sourceId)
        {
            bool changed = id != PilotId;
            PilotId = id; PilotName = id == null ? null : (string.IsNullOrEmpty(name) ? id : name);
            PilotColor = string.IsNullOrEmpty(color) ? "#FFFFFF" : color;
            Managed = id != null && managed;
            PilotSource = id == null ? null : id.StartsWith("web-") ? id : sourceId;
            if (!changed || id == null) { if (id == null) _handing = false; return; }
            // Hold the last inputs; ease to the new pilot's first input over 0.3 s (or centre the stick after 2 s of nothing).
            _handing = true; _handGot = false; _handAt = Time.unscaledTime;
            _hA = Aileron; _hE = Elevator; _hR = Rudder; _hT = Throttle01;
            _tA = _hA; _tE = _hE; _tR = _hR; _tT = _hT;
            BannerUntil = Time.unscaledTime + BannerSeconds;
            Widget.Presets.Stop();
        }

        public bool Handing => _handing;

        private void TickHandover()
        {
            if (!_handing) return;
            float now = Time.unscaledTime;
            if (!_handGot && Hold) { Aileron = _hA; Elevator = _hE; Rudder = _hR; Throttle01 = _hT; return; }   // HOLD: nothing centres — the held inputs stay until the new pilot moves them
            if (!_handGot && now - _handAt > HandoverWait)
            {
                // Nobody took it: centre the stick and rudder, keep the power.
                _tA = _trimAil; _tE = _trimStick; _tR = _trimRud; _tT = _hT; _handGot = true; _handGotAt = now;
            }
            if (!_handGot) { Aileron = _hA; Elevator = _hE; Rudder = _hR; Throttle01 = _hT; return; }
            float k = Mathf.Clamp01((now - _handGotAt) / HandoverEase);
            k = k * k * (3f - 2f * k);   // smooth in and out
            Aileron = _hA + (_tA - _hA) * k; Elevator = _hE + (_tE - _hE) * k; Rudder = _hR + (_tR - _hR) * k; Throttle01 = _hT + (_tT - _hT) * k;
            if (now - _handGotAt >= HandoverEase) { _handing = false; Aileron = _tA; Elevator = _tE; Rudder = _tR; Throttle01 = _tT; }
        }

        /// <summary>The control inputs into a history snapshot: the levers, brakes and pilot (so review replays them).</summary>
        public void FillSnap(WidgetHistory.Snap s)
        {
            s.FlapsCmd = FlapsHandle; s.FlapsActual = FlapsActual; s.Spoilers = Spoilers; s.SpoilersArmed = SpoilersArmed;
            s.Gear = !Spec.Retractable ? "fixed" : GearDown ? "down" : "up"; s.GearLights = GearLights(Widget.Ac);
            s.BrakeL = BrakeL; s.BrakeR = BrakeR; s.PilotName = PilotName; s.PilotColor = PilotColor;
        }

        /// <summary>Put the levers back as a history frame had them (resume from a past frame).</summary>
        public void FromSnap(WidgetHistory.Snap s)
        {
            Aileron = s.Ail; Elevator = s.Ele; Rudder = s.Rud; Throttle01 = s.Thr; Brake01 = s.Brake; ElevatorFree = s.HandsOff;
            FlapsHandle = s.FlapsCmd; FlapsActual = s.FlapsActual; Spoilers = s.Spoilers; SpoilersArmed = s.SpoilersArmed;
            if (s.Gear != "fixed") GearDown = s.Gear == "down";
            BrakeL = s.BrakeL; BrakeR = s.BrakeR; _handing = false;
        }

        private static float Axis(int i) { try { return Input.GetAxisRaw("WJoy" + i); } catch (System.ArgumentException) { return 0f; } }

        public void Poll()
        {
            // Keyboard (always live): 1/2 scenarios, arrows stick, A/D rudder, W/S power, B brakes, H hands-off, Space pause,
            // R reset, P next preset, [ ] slow-motion.
            if (Input.GetKeyDown(KeyCode.Alpha1)) Widget.Load(AeroWidget.Scenario.Flare, Widget.AircraftId, Widget.Flaps);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Widget.Load(AeroWidget.Scenario.Spin, Widget.AircraftId, Widget.Flaps);
            if (Input.GetKeyDown(KeyCode.Space)) { if (Widget.Paused) Widget.Review.Resume(false); else Widget.Review.Pause(); }
            // Review (protocol 3): , / . a frame back / forward; with Shift, a second.
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Comma)) Widget.Review.Step(shift ? -WidgetHistory.Hz : -1);
            if (Input.GetKeyDown(KeyCode.Period)) Widget.Review.Step(shift ? WidgetHistory.Hz : 1);
            if (Input.GetKeyDown(KeyCode.R)) Widget.Load(Widget.Current, Widget.AircraftId, Widget.Flaps);
            if (Input.GetKeyDown(KeyCode.P)) Widget.Presets.Next();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) Widget.TimeScale = Mathf.Max(0.1f, Widget.TimeScale * 0.5f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) Widget.TimeScale = Mathf.Min(1f, Widget.TimeScale * 2f);
            if (Widget.Paused) return;   // review is read-only: the keys and the gamepad don't move the airplane
            TickHandover();
            if (Managed) return;         // Glass Overlay names the pilot: the keyboard and gamepad are view-only
            if (Input.GetKeyDown(KeyCode.H)) ElevatorFree = !ElevatorFree;
            float kx = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float ky = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);   // up = push (nose down)
            float kr = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            bool keyStick = kx != 0 || ky != 0 || kr != 0;
            if (keyStick && Widget.Autopilot.On) Widget.Autopilot.Off(disconnect: true);
            float dt = Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.W)) { Throttle01 = Mathf.Clamp01((float)Throttle01 + dt * 0.6f); SourceLabel = "keyboard"; ThrottleTouched = true; }
            if (Input.GetKey(KeyCode.S)) { Throttle01 = Mathf.Clamp01((float)Throttle01 - dt * 0.6f); SourceLabel = "keyboard"; ThrottleTouched = true; }
            if (Input.GetKey(KeyCode.B)) { Brake01 = BrakeL = BrakeR = 1.0; }
            else if (SourceLabel == "keyboard") { Brake01 = BrakeL = BrakeR = 0.0; }
            if (keyStick && Hold)
            {
                // HOLD: a held key moves that control; let go and it stays (the other axes stay where they are).
                Widget.Presets.Stop(); SourceLabel = "keyboard"; ElevatorFree = false;
                if (kx != 0) Aileron = Mathf.Clamp((float)Aileron + kx * dt * 1.5f, -1f, 1f);
                if (ky != 0) Elevator = Mathf.Clamp((float)Elevator + ky * dt * 1.5f, -1f, 1f);
                if (kr != 0) Rudder = Mathf.Clamp((float)Rudder + kr * dt * 1.5f, -1f, 1f);
            }
            else if (keyStick)
            {
                Widget.Presets.Stop(); SourceLabel = "keyboard"; ElevatorFree = false;
                Aileron = Mathf.MoveTowards((float)Aileron, kx, dt * 3f);
                Elevator = Mathf.MoveTowards((float)Elevator, ky != 0 ? ky : (float)_trimStick, dt * 3f);
                Rudder = Mathf.MoveTowards((float)Rudder, kr, dt * 4f);
            }
            // Gamepad / joystick: takes the airplane when its sticks move.
            float ga = Axis(AxAileron), ge = Axis(AxElevator) * (InvertElevator ? -1f : 1f), gr = Axis(AxRudder), gt = Axis(AxThrottle);
            bool padMoved = Mathf.Abs(ga) > 0.08f || Mathf.Abs(ge) > 0.08f || Mathf.Abs(gr) > 0.08f;
            if (padMoved) { _padAt = Time.unscaledTime; if (Widget.Autopilot.On) Widget.Autopilot.Off(disconnect: true); }
            if (Hold)
            {
                // HOLD: the pad's sticks spring back, so they NUDGE the held position (deflection = rate); released, it stays.
                if (padMoved && Time.unscaledTime - _netAt > 0.5f && !keyStick)
                {
                    Widget.Presets.Stop(); SourceLabel = "gamepad"; ElevatorFree = false;
                    if (Mathf.Abs(ga) > 0.08f) Aileron = Mathf.Clamp((float)Aileron + ga * dt * 1.5f, -1f, 1f);
                    if (Mathf.Abs(ge) > 0.08f) Elevator = Mathf.Clamp((float)Elevator - ge * dt * 1.5f, -1f, 1f);
                    if (Mathf.Abs(gr) > 0.08f) Rudder = Mathf.Clamp((float)Rudder + gr * dt * 1.5f, -1f, 1f);
                }
                if (ThrottleFromAxis && padMoved) { Throttle01 = (gt + 1f) * 0.5f; ThrottleTouched = true; }
            }
            else if (Time.unscaledTime - _padAt < 1.5f && Time.unscaledTime - _netAt > 0.5f && !keyStick)
            {
                Widget.Presets.Stop(); SourceLabel = "gamepad"; ElevatorFree = false;
                Aileron = ga; Elevator = Mathf.Abs(ge) > 0.05f ? -ge : _trimStick; Rudder = gr;   // stick back (−y on most pads… mapped) = pull
                if (ThrottleFromAxis) { Throttle01 = (gt + 1f) * 0.5f; ThrottleTouched = true; }
            }
            else if (!Hold && !keyStick && Time.unscaledTime - _netAt > 2f && SourceLabel == "keyboard" && Widget.Presets.Running == null && !Widget.Autopilot.On && !Widget.Lessons.Driving)
            {
                // Released keys: back to trim (the stick centres).
                Aileron = Mathf.MoveTowards((float)Aileron, (float)_trimAil, dt * 3f);
                Rudder = Mathf.MoveTowards((float)Rudder, (float)_trimRud, dt * 4f);
                if (!ElevatorFree) Elevator = Mathf.MoveTowards((float)Elevator, (float)_trimStick, dt * 3f);
            }
        }
    }
}
