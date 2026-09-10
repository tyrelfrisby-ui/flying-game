using System;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// BAIL OUT / EJECT sequencer (owner request 2026-09-08). Public surface is the contract other systems
    /// compile against (FlightAudio / ChaseCamera / TouchFlightControls / NetSession) — do not rename.
    ///
    /// BAIL OUT (t from the button): 0 s BailOutStarted · 3 s canopy jettisons (translucent shell leaves
    /// up + aft with the airstream, tumbling) · 5 s the pilot goes over the RIGHT side (aircraft velocity
    /// + 3 m/s right + 1.5 m/s up, tumbling) · +3 s the chute deploys — line stretch then a real inflation
    /// (NOT instant, see <see cref="Parachute"/>).
    /// EJECT: immediate — canopy off and the seat rocket fires at t = 0 (12 g for 0.25 s then 4 g to
    /// 0.5 s along the aircraft's up axis, a zero-zero seat), PilotLeft at 0.1 s, seat separates at 1.0 s,
    /// chute automatic at 1.5 s once below 5 km (barostat).
    /// After the pilot leaves the camera follows the PILOT (ChaseCamera.OverrideTarget) and the right pad
    /// steers the canopy (aileron → yaw ≤ 30°/s); the abandoned aircraft flies on under the sim with a
    /// neutral stick/rudder (trim kept) and the throttle it had.
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class PilotEgress : MonoBehaviour
    {
        public enum Phase { InCockpit, BailingOut, CanopyGone, FreeFall, ChuteDeploying, UnderCanopy, Tumbling, Landed }

        public Phase Current { get; private set; } = Phase.InCockpit;
        public bool PilotOut => Current >= Phase.FreeFall;

        /// <summary>Camera target once the pilot has left the aircraft (null while in the cockpit).</summary>
        public Transform PilotTransform { get; private set; }

        public event Action BailOutStarted;    // t = 0 of the 5 s sequence
        public event Action CanopyJettisoned;  // t = 3 s (also on eject, t = 0)
        public event Action PilotLeft;         // t = 5 s (eject: t ≈ 0.1 s)
        public event Action ChuteDeploying;    // pilot chute out, line stretch begins
        public event Action ChuteInflated;     // full canopy
        public event Action Ejected;

        // Sequence timings (s).
        public const float BailCanopySec = 3f, BailJumpSec = 5f, BailChuteSec = 3f;
        public const float EjectPilotLeftSec = 0.1f, EjectSeatSepSec = 1.0f, EjectChuteSec = 1.5f;
        public const float EjectBaroAltM = 5000f;   // no main canopy above this: wait for it (barostat)

        private FlightSimDriver _driver;
        private TouchFlightControls _touch;
        private ChaseCamera _cam;
        private PilotBody _pilot;
        private Renderer _hiddenCanopy;
        private ControlInputs _abandoned = ControlInputs.Neutral;

        private float _t;                 // seconds since BailOut()/Eject()
        private float _pilotLeftT;
        private bool _eject, _canopyDone, _pilotDone, _pilotLeftFired, _seatDone, _chuteDone;
        private string _status = "";
        // Landing outcome (owner 2026-09-09): grunts/winces on every ground strike, a one-liner once he's at rest.
        public LandingOutcome Outcome { get; private set; }
        public string LastLine { get; private set; } = "";
        private float _restT = -1f, _voiceT = -1f, _voiceSeverity;
        private bool _voiceWince, _spoken;
        private GUIStyle _style;
        private Texture2D _bg;
        private int _fs;

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
        }

        private void Start()
        {
            _touch = GetComponent<TouchFlightControls>();
            _driver.AircraftChanged += ResetToCockpit;
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.AircraftChanged -= ResetToCockpit;
            _pilot?.Destroy();
        }

        // ---- public commands ---------------------------------------------------------------------

        public void BailOut()
        {
            if (Current != Phase.InCockpit) return;
            Current = Phase.BailingOut;
            _t = 0f;
            _eject = false;
            Log("BAIL OUT — canopy in 3 s, over the side in 5 s");
            BailOutStarted?.Invoke();
        }

        public void Eject()
        {
            if (PilotOut) return;   // allowed from the cockpit or mid bail-out
            _eject = true;
            _t = 0f;
            _touch ??= GetComponent<TouchFlightControls>();
            if (!_canopyDone) JettisonCanopy();

            Transform ac = _driver.transform;
            Vector3 pos = ac.TransformPoint(CockpitLocal(_driver));
            Vector3 vel = _driver.WorldVelocityUnity;
            _pilot = new PilotBody(pos, vel, new Vector3(0.6f, 0.2f, 0.4f), ac.eulerAngles.y,
                withSeat: true, seatUp: ac.up, initialRotation: ac.rotation);
            _pilot.Hit += OnPilotHit;
            PilotTransform = _pilot.Transform;
            AbandonAircraft();
            Current = Phase.FreeFall;
            Log("EJECT");
            Ejected?.Invoke();
            GetComponent<FlightAudio>()?.EjectionSeat();
        }

        /// <summary>Back in the cockpit of a fresh aircraft (ResetFlight / aircraft switch).</summary>
        public void ResetToCockpit()
        {
            _pilot?.Destroy();
            _pilot = null;
            PilotTransform = null;
            if (_hiddenCanopy != null) _hiddenCanopy.enabled = true;
            _hiddenCanopy = null;
            Current = Phase.InCockpit;
            _t = 0f;
            _eject = _canopyDone = _pilotDone = _pilotLeftFired = _seatDone = _chuteDone = false;
            _status = ""; LastLine = ""; _restT = _voiceT = -1f; _spoken = false;
            PilotVoice.Stop();
            var cam = Cam();
            if (cam != null && cam.OverrideTarget != null)
            {
                cam.OverrideTarget = null;
                cam.OverrideVelocity = Vector3.zero;
                cam.SnapBehind();
            }
        }

        /// <summary>Cockpit position in the aircraft's local Unity frame (right, up, fwd): the Canopy part
        /// when the airframe has one, else ≈0.5 m above the centreline a little aft of the wing-root LE.</summary>
        public static Vector3 CockpitLocal(FlightSimDriver driver)
        {
            Transform canopy = driver.transform.Find("Canopy");
            if (canopy != null) return canopy.localPosition + Vector3.up * 0.15f;
            float le = 0f, bestSpan = -1f;
            var cfg = driver.Sim?.Aircraft?.Config;
            if (cfg != null)
            {
                foreach (var sf in cfg.Surfaces)
                {
                    float span = 0f, rootLe = 0f, rootY = float.MaxValue;
                    foreach (var st in sf.Strips)
                    {
                        float y = Mathf.Abs((float)st.Pos[1]);
                        span = Mathf.Max(span, y);
                        if (y < rootY) { rootY = y; rootLe = (float)st.Pos[0]; }
                    }
                    if (span > bestSpan) { bestSpan = span; le = rootLe; }
                }
            }
            return new Vector3(0f, 0.5f, le - 0.4f);
        }

        // ---- sequence --------------------------------------------------------------------------------

        private void Update()
        {
            if (Current == Phase.InCockpit) return;
            float dt = Time.deltaTime;
            _t += dt;

            if (!_eject)
            {
                if (!_canopyDone && _t >= BailCanopySec) JettisonCanopy();
                if (!_pilotDone && _t >= BailJumpSec) JumpOverTheSide();
                if (_pilotDone && !_chuteDone && _t - _pilotLeftT >= BailChuteSec) DeployChute();
            }
            else
            {
                if (!_pilotLeftFired && _t >= EjectPilotLeftSec) FirePilotLeft();
                if (!_seatDone && _t >= EjectSeatSepSec) { _seatDone = true; _pilot.SeparateSeat(); Log("seat separated"); }
                if (_seatDone && !_chuteDone && _t >= EjectChuteSec && _pilot.AltitudeM < EjectBaroAltM) DeployChute();
            }

            if (PilotOut)
            {
                _driver.Inputs = _abandoned;   // the aircraft flies on hands-off
            }

            if (_pilot == null) return;
            _pilot.Steer = ReadSteer();
            _pilot.Tick(dt);

            var cam = Cam();
            if (cam != null && cam.OverrideTarget == PilotTransform) cam.OverrideVelocity = _pilot.Velocity;

            if (Current == Phase.ChuteDeploying && _pilot.Chute != null && _pilot.Chute.Inflated)
            {
                Current = Phase.UnderCanopy;
                Log($"canopy full — fill {_pilot.Chute.FillTimeSec:F1} s, sink {_pilot.SinkMs:F1} m/s");
                ChuteInflated?.Invoke();
                GetComponent<FlightAudio>()?.ChuteInflate();
            }
            if (_pilot.Tumbling && Current != Phase.Tumbling)
            {
                Current = Phase.Tumbling;
                Log($"pilot hit a slope at {_pilot.LandingSpeedMs:F1} m/s — tumbling");
            }
            if (_pilot.Landed && Current != Phase.Landed)
            {
                Current = Phase.Landed;
                Outcome = PilotPhrases.Outcome(_pilot.MaxImpactMs, _pilot.LandedOnWater);
                LastLine = PilotPhrases.Pick(Outcome, _pilot.LandedOnWater, _pilot.TumbleHits);
                _restT = 0f; _spoken = false;
                Log($"pilot down {(_pilot.LandedOnWater ? "in the water" : "on the ground")} at {_pilot.LandingSpeedMs:F1} m/s, hardest hit {_pilot.MaxImpactMs:F1} m/s, {_pilot.TumbleHits} tumble hits → {Outcome}");
            }
            // Grunt/wince a beat after the thud; the one-liner once he has caught his breath.
            if (_voiceT >= 0f && (_voiceT -= dt) < 0f)
            {
                var au = GetComponent<FlightAudio>();
                if (_voiceWince) au?.PilotWince(_voiceSeverity); else au?.PilotGrunt(_voiceSeverity);
            }
            if (_restT >= 0f && !_spoken && (_restT += dt) > (Outcome == LandingOutcome.Dead ? 1.6f : 1.0f))
            {
                _spoken = true;
                PilotVoice.Say(LastLine, rate: Outcome == LandingOutcome.Dead ? 0.42f : 0.5f, pitch: Outcome == LandingOutcome.Injured ? 0.85f : 0.92f);
            }
            UpdateStatus();
        }

        /// <summary>Every ground strike (first contact and each tumble hit): a body thud now, a grunt (light) or a
        /// wince (hard) a tenth of a second later. Severity from the impact speed (a 5 m/s canopy landing is soft).</summary>
        private void OnPilotHit(float impactMs, bool stillTumbling)
        {
            float sev = Mathf.Clamp01((impactMs - 1.5f) / 10f);
            var au = GetComponent<FlightAudio>();
            au?.PilotThud(sev);
            if (impactMs < 2.5f) return;                     // a soft touch: the thud is enough
            _voiceSeverity = sev;
            _voiceWince = impactMs >= 7f || (stillTumbling && UnityEngine.Random.value < 0.35f);
            _voiceT = 0.10f + UnityEngine.Random.Range(0f, 0.06f);
        }

        private void JettisonCanopy()
        {
            _canopyDone = true;
            if (Current == Phase.BailingOut) Current = Phase.CanopyGone;
            Transform part = _driver.transform.Find("Canopy");
            CanopyJettison.Spawn(_driver, part);
            if (part != null && part.TryGetComponent(out Renderer r)) { r.enabled = false; _hiddenCanopy = r; }
            Log("canopy jettisoned");
            CanopyJettisoned?.Invoke();
            GetComponent<FlightAudio>()?.CanopyJettison();
        }

        private void JumpOverTheSide()
        {
            _pilotDone = true;
            _pilotLeftT = _t;
            Transform ac = _driver.transform;
            Vector3 pos = ac.TransformPoint(CockpitLocal(_driver)) + ac.right * 0.6f;
            Vector3 vel = _driver.WorldVelocityUnity + ac.right * 3f + ac.up * 1.5f;
            _pilot = new PilotBody(pos, vel, new Vector3(1.5f, 2.5f, 1.0f), ac.eulerAngles.y,
                withSeat: false, seatUp: Vector3.up, initialRotation: ac.rotation * Quaternion.Euler(0f, 0f, -60f));
            _pilot.Hit += OnPilotHit;
            PilotTransform = _pilot.Transform;
            AbandonAircraft();
            Current = Phase.FreeFall;
            FirePilotLeft();
        }

        private void FirePilotLeft()
        {
            _pilotDone = true;
            _pilotLeftFired = true;
            _pilotLeftT = _t;
            var cam = Cam();
            if (cam != null)
            {
                cam.OverrideTarget = PilotTransform;
                cam.OverrideVelocity = _pilot.Velocity;
            }
            Log("pilot out — camera on the pilot");
            PilotLeft?.Invoke();
        }

        private void DeployChute()
        {
            _chuteDone = true;
            if (_pilot == null || _pilot.Landed) return;
            _pilot.DeployChute();
            Current = Phase.ChuteDeploying;
            Log($"chute deploying at {_pilot.AirspeedMs:F0} m/s, {_pilot.HeightAglM:F0} m AGL");
            ChuteDeploying?.Invoke();
            GetComponent<FlightAudio>()?.ChuteDeploy();
        }

        /// <summary>Freeze the abandoned aircraft's controls: neutral stick/rudder (pitch trim kept), last throttle lever.</summary>
        private void AbandonAircraft()
        {
            double lever = _driver.Inputs.ThrottleLever;
            double trim = _touch != null ? _touch.TrimElevator : 0.0;
            _abandoned = new ControlInputs(0, trim, 0, lever);
            _driver.Inputs = _abandoned;
            if (_driver.Sim?.Aircraft != null) { _driver.Sim.Aircraft.BrakeInput = 0; _driver.Sim.Aircraft.BrakeBias = 0; }
        }

        private float ReadSteer()
        {
            if (_touch != null) return _touch.StickAileron;
            return (float)_driver.Inputs.Aileron;
        }

        private ChaseCamera Cam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<ChaseCamera>();
            return _cam;
        }

        private void Log(string msg) => Debug.Log($"[PilotEgress] t={_t:F1}s {msg}");

        // ---- status line ---------------------------------------------------------------------------

        private void UpdateStatus()
        {
            switch (Current)
            {
                case Phase.BailingOut: _status = $"BAIL OUT  canopy in {Mathf.Max(0f, BailCanopySec - _t):F1} s"; break;
                case Phase.CanopyGone: _status = $"CANOPY GONE  jump in {Mathf.Max(0f, BailJumpSec - _t):F1} s"; break;
                case Phase.FreeFall:
                    float tChute = _eject ? EjectChuteSec - _t : BailChuteSec - (_t - _pilotLeftT);
                    _status = _eject && _t < EjectSeatSepSec ? $"EJECT  {_pilot.AirspeedMs:F0} m/s"
                        : $"FREE FALL  {_pilot.HeightAglM:F0} m AGL  {_pilot.AirspeedMs:F0} m/s  chute in {Mathf.Max(0f, tChute):F1} s";
                    break;
                case Phase.ChuteDeploying:
                    var c = _pilot.Chute;
                    _status = c != null && !c.LinesStretched ? $"LINE STRETCH  {_pilot.AirspeedMs:F0} m/s"
                        : $"INFLATING {Mathf.RoundToInt((c?.Fill ?? 0f) * 100f)}%  {_pilot.AirspeedMs:F0} m/s";
                    break;
                case Phase.UnderCanopy:
                    _status = $"CANOPY  {_pilot.HeightAglM:F0} m AGL  sink {_pilot.SinkMs:F1} m/s  steer: right pad";
                    break;
                case Phase.Tumbling:
                    _status = $"TUMBLING  {_pilot.TumbleHits} hits  {_pilot.Velocity.magnitude:F0} m/s";
                    break;
                case Phase.Landed:
                    string how = _pilot.TumbleHits > 0 ? $"  after {_pilot.TumbleHits} hits down the slope" : _pilot.LandedOnWater ? "  (water)" : "";
                    _status = _spoken ? $"{PilotPhrases.Label(Outcome)}  “{LastLine}”"
                        : $"{PilotPhrases.Label(Outcome)}  {_pilot.MaxImpactMs:F1} m/s{how}";
                    break;
            }
        }

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen || Current == Phase.InCockpit || string.IsNullOrEmpty(_status)) return;
            EnsureStyle();
            Rect view = ScreenLayout.ViewRect;            // screen px, bottom-left origin
            float h = _fs * 1.9f;
            float y = Screen.height - view.yMax + view.height * 0.30f;   // GUI space (top-left origin)
            var r = new Rect(view.x + view.width * 0.08f, y, view.width * 0.84f, h);
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(r, _bg);
            GUI.color = Color.white;
            GUI.Label(r, _status, _style);
        }

        private void EnsureStyle()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * ScreenLayout.FontFrac * 1.1f);
            if (_style != null && fs == _fs) return;
            _fs = fs;
            if (_bg == null)
            {
                _bg = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                _bg.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                _bg.Apply();
            }
            // From scratch — never GUI.skin.* (the default skin is stripped on iOS).
            _style = new GUIStyle
            {
                fontSize = fs,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                font = UiFont.Get(),
                normal = { textColor = new Color(1f, 0.85f, 0.3f, 1f) },
            };
        }
    }
}
