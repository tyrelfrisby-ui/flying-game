using System;
using FlyingGame.Sim;
using UnityEngine;
using LandingOutcome = FlyingGame.Core.LandingOutcome;

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
    ///
    /// HANDS ON (headset, docs/vr-cockpit.md phase 2): <see cref="LeaveByHand"/> is the other way out — the pilot
    /// opened the canopy, the belts and climbed out himself, so there is no timed sequence and NO automatic chute:
    /// he stays in first person (VrCockpitHands) and opens it with <see cref="PullRipcord"/>. Same body, same air,
    /// same canopy model as the menu's BAIL OUT, which stays as the shortcut (timed, over the right side, the chute
    /// opening by itself 3 s after he leaves, seen from outside).
    /// </summary>
    [RequireComponent(typeof(FlightSimDriver))]
    public sealed class PilotEgress : MonoBehaviour
    {
        public enum Phase { InCockpit, BailingOut, CanopyGone, FreeFall, ChuteDeploying, UnderCanopy, Tumbling, Landed, Ascending }

        public Phase Current { get; private set; } = Phase.InCockpit;
        public bool PilotOut => Current >= Phase.FreeFall;
        /// <summary>He left by hand (headset): first person, ripcord by hand, no automatic opening.</summary>
        public bool HandsOn { get; private set; }
        /// <summary>The pilot's world velocity (m/s) once out; zero in the cockpit.</summary>
        public Vector3 PilotVelocity => _pilot != null ? _pilot.Velocity : Vector3.zero;
        /// <summary>0 packed / streaming .. 1 full canopy.</summary>
        public float ChuteInflate01 => _pilot?.Chute != null ? _pilot.Chute.Fill : 0f;
        public bool ChuteOut => _pilot?.Chute != null;
        public bool PilotLanded => _pilot != null && _pilot.Landed;
        /// <summary>The non-gravitational acceleration on the pilot (drag, opening shock), in g.</summary>
        public float PilotLoadG => _pilot != null ? _pilot.AeroAccelG : 0f;

        /// <summary>The phase as the screen rules see it (<see cref="FlyingGame.Core.PilotOutRules"/>).</summary>
        public FlyingGame.Core.PilotOutRules.Stage Stage => StageOf(Current);

        public static FlyingGame.Core.PilotOutRules.Stage StageOf(Phase p) => p switch
        {
            Phase.InCockpit => FlyingGame.Core.PilotOutRules.Stage.Aboard,
            Phase.BailingOut or Phase.CanopyGone => FlyingGame.Core.PilotOutRules.Stage.Egressing,
            Phase.FreeFall or Phase.ChuteDeploying => FlyingGame.Core.PilotOutRules.Stage.FreeFall,
            Phase.UnderCanopy => FlyingGame.Core.PilotOutRules.Stage.UnderCanopy,
            Phase.Ascending => FlyingGame.Core.PilotOutRules.Stage.Ascending,
            _ => FlyingGame.Core.PilotOutRules.Stage.Landed,   // Tumbling, Landed
        };

        /// <summary>May a HUD draw for this aeroplane now? False from the moment a bail-out starts until the next
        /// flight (Ty 2026-11-04: the HUD came back over the falling pilot on the Quest). Every HUD asks this — the
        /// green HUD, the floating dials, the flight-path line, the dev status lines — on every platform.</summary>
        public static bool HudAllowed(FlightSimDriver driver)
        {
            PilotEgress e = driver != null ? driver.GetComponent<PilotEgress>() : null;
            return e == null || FlyingGame.Core.PilotOutRules.ShowHud(e.Stage);
        }

        /// <summary>Can the pilot see a flat OnGUI overlay? Not through a headset: a stereo camera renders the eyes,
        /// and OnGUI only reaches the mirror window. A platform with its own notion (the Quest lane's VrMode) may
        /// replace this at start-up.</summary>
        public static Func<bool> FlatOverlayVisible = () =>
        {
            Camera c = Camera.main;
            return c == null || !c.stereoEnabled;
        };

        /// <summary>Camera target once the pilot has left the aircraft (null while in the cockpit).</summary>
        public Transform PilotTransform { get; private set; }
        /// <summary>The pilot's own wind-relative airspeed (m/s) once out — the wind in his ears; 0 in the cockpit.</summary>
        public float PilotAirspeedMs => _pilot != null && !_pilot.Landed ? _pilot.AirspeedMs : 0f;

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
        private ControlInputs _abandoned = ControlInputs.Neutral;

        private float _t;                 // seconds since BailOut()/Eject()
        private float _pilotLeftT;
        private bool _eject, _canopyDone, _pilotDone, _pilotLeftFired, _seatDone, _chuteDone;
        private string _status = "";
        // Landing outcome (owner 2026-09-09): grunts/winces on every ground strike, a one-liner once he's at rest.
        public LandingOutcome Outcome { get; private set; }
        /// <summary>What the CRASH did to him, taken from the aeroplane (Sim Aircraft.Occupant, Core OccupantInjury) the
        /// moment he leaves it (Ty 2026-11-05: bailing out of a wreck read as walking away). Unhurt for a pilot who left a
        /// flying aeroplane, so a normal bail-out is graded on the landing alone, as before.</summary>
        public LandingOutcome CrashOutcome { get; private set; }
        /// <summary>The crash killed him: he climbs out translucent, his canopy opens and carries him UP, and he says
        /// something about where he is going (Core PilotOutRules.GhostAscends / HeavenLines).</summary>
        public bool IsGhost { get; private set; }
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
            if (_remarkGo != null) Destroy(_remarkGo);
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
            for (int i = 0; i < 400 && !CanopyClear; i++) StepCanopy(0.005f);   // the seat waits for it (2 s at most)

            Transform ac = _driver.transform;
            Vector3 pos = ac.TransformPoint(CockpitLocal(_driver));
            Vector3 vel = _driver.WorldVelocityUnity;
            _pilot = new PilotBody(pos, vel, new Vector3(0.6f, 0.2f, 0.4f), ac.eulerAngles.y,
                withSeat: true, seatUp: ac.up, initialRotation: ac.rotation);
            _pilot.Hit += OnPilotHit;
            PilotTransform = _pilot.Transform;
            TakeCrashVerdict();
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
            Current = Phase.InCockpit;
            _t = 0f;
            _eject = _canopyDone = _pilotDone = _pilotLeftFired = _seatDone = _chuteDone = false;
            HandsOn = false;
            _canopy = null;
            IsGhost = false;
            CrashOutcome = LandingOutcome.Unhurt;
            _status = ""; LastLine = ""; _restT = _voiceT = -1f; _spoken = false;
            PilotVoice.Stop();
            UpdateRemark();   // takes the world-space one-liner down
            var cam = Cam();
            if (cam != null && cam.OverrideTarget != null)
            {
                cam.OverrideTarget = null;
                cam.OverrideBackdrop = null;
                cam.OverrideVelocity = Vector3.zero;
                cam.SnapBehind();
            }
        }

        /// <summary>Cockpit position in the aircraft's local Unity frame (right, up, fwd): the model's own EYE
        /// POINT where it has one, else the Canopy part, else ≈0.5 m above the centreline a little aft of the
        /// wing-root LE. The eye point is where the draughtsman sat the pilot, so a bail-out from a rigged
        /// aeroplane leaves from the seat rather than from an estimate.</summary>
        public static Vector3 CockpitLocal(FlightSimDriver driver)
        {
            var visual = driver.GetComponent<AirframeVisual>();
            Transform eye = visual != null ? visual.CockpitEye() : null;
            if (eye != null) return driver.transform.InverseTransformPoint(eye.position);
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

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One frame of the sequence with a stated dt: for an editor batch tool, where Update never fires
        /// (BailoutHeavenCheck).</summary>
        public void TickForTesting(float dt) => Tick(dt);

        private void Tick(float dt)
        {
            if (Current == Phase.InCockpit) return;
            _t += dt;

            if (!_eject)
            {
                if (!_canopyDone && _t >= BailCanopySec) JettisonCanopy();
                StepCanopy(dt);
                if (!_pilotDone && _t >= BailJumpSec && CanopyClear) JumpOverTheSide();   // not through a canopy still on
                // A ghost's opens by itself, soon, even after a hands-on exit: nobody pulls a ripcord on the way up.
                float chuteSec = IsGhost ? (float)FlyingGame.Core.PilotOutRules.GhostChuteSec : BailChuteSec;
                if (_pilotDone && !_chuteDone && (!HandsOn || IsGhost) && _t - _pilotLeftT >= chuteSec) DeployChute();
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
            _pilot.BodyPos = ReadBodyPos();
            _pilot.Tick(dt);

            var cam = Cam();
            if (cam != null && cam.OverrideTarget == PilotTransform) cam.OverrideVelocity = _pilot.Velocity;

            if (Current == Phase.ChuteDeploying && _pilot.Chute != null && _pilot.Chute.Inflated)
            {
                Current = IsGhost ? Phase.Ascending : Phase.UnderCanopy;
                Log($"canopy full — fill {_pilot.Chute.FillTimeSec:F1} s, {(IsGhost ? "rising" : "sink")} {Mathf.Abs(_pilot.SinkMs):F1} m/s");
                ChuteInflated?.Invoke();
                GetComponent<FlightAudio>()?.ChuteInflate();
                if (IsGhost)
                {
                    // Up he goes, and says so once the canopy has him.
                    Outcome = LandingOutcome.Dead;
                    LastLine = PilotPhrases.Heaven();
                    _restT = 0f; _spoken = false;
                }
            }
            if (_pilot.Tumbling && Current != Phase.Tumbling)
            {
                Current = Phase.Tumbling;
                Log($"pilot hit a slope at {_pilot.LandingSpeedMs:F1} m/s — tumbling");
            }
            if (_pilot.Landed && Current != Phase.Landed)
            {
                Current = Phase.Landed;
                LandingOutcome landing = PilotPhrases.Outcome(_pilot.MaxImpactMs, _pilot.LandedOnWater);
                Outcome = FlyingGame.Core.PilotOutRules.Combined(CrashOutcome, landing);   // the crash's injuries came down with him
                LastLine = PilotPhrases.Pick(Outcome, _pilot.LandedOnWater, _pilot.TumbleHits);
                _restT = 0f; _spoken = false;
                Log($"pilot down {(_pilot.LandedOnWater ? "in the water" : "on the ground")} at {_pilot.LandingSpeedMs:F1} m/s, hardest hit {_pilot.MaxImpactMs:F1} m/s, {_pilot.TumbleHits} tumble hits → landing {landing}, crash {CrashOutcome} → {Outcome}");
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
                if (IsGhost) PilotVoice.Say(LastLine, rate: 0.48f, pitch: 1.05f);   // cheerful about it
                else PilotVoice.Say(LastLine, rate: Outcome == LandingOutcome.Dead ? 0.42f : 0.5f, pitch: Outcome == LandingOutcome.Injured ? 0.85f : 0.92f);
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

        /// <summary>
        /// THE REAL CANOPY OR DOOR, WORKED (Ty 2026-11-05 06:37 / 06:38): the red handle pulls, the latch opens and the hinge
        /// pins come out, and from then on the canopy's own physics (Core Vr.CanopyDynamics, the headset's canopy where it has
        /// one) decides when it goes — at once in the air, after his hand shoves it up and over on the ground (Core
        /// Vr.CanopyBailout). It leaves as the model's own parts (AirframeVisual.DetachCanopy → CanopyFlight). An open
        /// cockpit or a fold-out door loses nothing, and nothing is ever spawned in its place.
        /// </summary>
        private void JettisonCanopy()
        {
            _canopyDone = true;
            if (Current == Phase.BailingOut) Current = Phase.CanopyGone;
            var visual = GetComponent<AirframeVisual>();
            FlyingGame.Core.Vr.CanopyDynamics c = visual != null ? visual.EgressCanopy() : null;
            if (c == null)
            {
                Log("nothing to jettison: open cockpit, a door that folds out of the way, or no separate canopy on this model");
                return;
            }
            visual.Cockpit.PullCanopyRelease();   // the red handle moves, wherever the hand came from
            visual.Cockpit.SetCanopyLock(false);  // and the latch opens
            visual.Cockpit.EgressCanopy = c;
            _canopy = new FlyingGame.Core.Vr.CanopyBailout(c);
            Log($"canopy unlatched, hinge pins out ({c.Spec.Motion}, {c.Spec.MassKg:F1} kg)");
            StepCanopy(0.005f);
        }

        /// <summary>The bail-out's canopy, from the pins coming out until it has gone (null when there is none).</summary>
        private FlyingGame.Core.Vr.CanopyBailout _canopy;

        /// <summary>He can go: there is no canopy to wait for, or it has gone.</summary>
        private bool CanopyClear => _canopy == null || _canopy.Departed;

        private void StepCanopy(float dt)
        {
            if (_canopy == null || _canopy.Departed) return;
            var visual = GetComponent<AirframeVisual>();
            if (visual == null || visual.Cockpit.CanopyGone) return;
            double ias = _driver.IasMs;
            double q = 0.5 * 1.225 * ias * ias;   // IAS is equivalent airspeed here: q = ½ρ₀V_e², as the headset's canopy
            FlyingGame.Sim.Aircraft ac = _driver.Sim?.Aircraft;
            // Flying: the load factor, as the headset's canopy. At rest (a wreck): its weight, whichever way up the wreck lies.
            Vector3 g = _driver.transform.InverseTransformDirection(Vector3.down) * 9.80665f;
            var gApp = ac != null && ias >= 1.0
                ? new FlyingGame.Core.MathTypes.Vec3(0, -ac.LoadFactorZ * 9.80665, 0)
                : new FlyingGame.Core.MathTypes.Vec3(g.x, g.y, g.z);
            FlyingGame.Core.Vr.CanopyEvents ev = _canopy.Step(dt, q, gApp);
            if ((ev & FlyingGame.Core.Vr.CanopyEvents.Departed) != 0) DepartCanopy(visual, ias);
        }

        /// <summary>It has gone: the parts fly off with the aeroplane's velocity plus the canopy's own swing, and the
        /// aeroplane is that much lighter (as the headset's VrCockpitHands.DepartCanopy).</summary>
        private void DepartCanopy(AirframeVisual visual, double ias)
        {
            FlyingGame.Core.Vr.CanopyDynamics c = _canopy.Canopy;
            Transform air = _driver.transform;
            static Vector3 U(FlyingGame.Core.MathTypes.Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
            Vector3 vel = _driver.WorldVelocityUnity + air.rotation * U(c.ComVelocityLocal());
            Vector3 up = air.rotation * U(FlyingGame.Core.Vr.CanopyDynamics.Normal(c.AngleDeg));
            Vector3 spin = -air.forward * (float)(c.RateDps * Mathf.Deg2Rad) + air.right * (float)FlyingGame.Core.Vr.CanopyFreeBody.PitchOverRadS;
            Vector3? centre = visual.DetachCanopy(c, vel, up, spin, c.DepartedBy == FlyingGame.Core.Vr.CanopyDeparture.HingesFailed);
            visual.Cockpit.CanopyGone = true;
            FlyingGame.Sim.Aircraft ac = _driver.Sim?.Aircraft;
            if (ac != null && centre.HasValue)
            {
                Vector3 l = air.InverseTransformPoint(centre.Value);
                ac.ShedMass(c.Spec.MassKg, ac.Config.Mass.CgVec() + new FlyingGame.Core.MathTypes.Vec3(l.z, l.x, -l.y));
            }
            Log($"canopy {c.DepartedBy} at {ias * 1.9438445:F0} KIAS after {_canopy.TimeSec:F2} s, open {c.AngleDeg:F0}°" +
                $"{(c.SlideM > 0 ? $", slid {c.SlideM * 1000:F0} mm" : "")}: {c.Spec.MassKg:F1} kg gone");
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
            TakeCrashVerdict();
            AbandonAircraft();
            Current = Phase.FreeFall;
            FirePilotLeft();
        }

        /// <summary>
        /// The crash's verdict, read off the aeroplane as he leaves it (whatever it does after that is not done to him):
        /// the worst his seat took, through the body's own dynamics, and how far the cabin folded round him. A fatal one
        /// makes him a ghost from this moment.
        /// </summary>
        private void TakeCrashVerdict()
        {
            FlyingGame.Core.OccupantInjury occ = _driver.Sim?.Aircraft?.Occupant;
            CrashOutcome = occ != null ? occ.Level : LandingOutcome.Unhurt;
            IsGhost = FlyingGame.Core.PilotOutRules.GhostAscends(CrashOutcome);
            Log($"crash verdict at exit: {(occ != null ? occ.Describe() : "no sim")}{(IsGhost ? " — leaving as a ghost" : "")}");
            if (IsGhost) _pilot.SetGhost();
        }

        /// <summary>
        /// THE HANDS-ON EXIT: he has climbed out of the cockpit himself (VrCockpitHands). <paramref name="pos"/> is his
        /// body centre and <paramref name="vel"/> the aeroplane's velocity at that point plus his push (Core
        /// JumperAero.ExitVelocity), both world. He tumbles for JumperAero.StabiliseSec, then flies belly to earth; the
        /// chute waits for <see cref="PullRipcord"/>. The figure is not drawn: the camera is his head.
        /// </summary>
        public void LeaveByHand(Vector3 pos, Vector3 vel, float headingDeg)
        {
            if (Current != Phase.InCockpit) return;
            HandsOn = true;
            _eject = false;
            _t = 0f;
            _canopyDone = true;
            _chuteDone = false;
            _pilot = new PilotBody(pos, vel, Vector3.zero, headingDeg, withSeat: false, seatUp: Vector3.up,
                initialRotation: Quaternion.Euler(0f, headingDeg, 0f));
            _pilot.FirstPerson = true;
            _pilot.TumbleSec = (float)FlyingGame.Core.Vr.JumperAero.StabiliseSec;
            _pilot.Hit += OnPilotHit;
            PilotTransform = _pilot.Transform;
            TakeCrashVerdict();
            AbandonAircraft();
            Current = Phase.FreeFall;
            Log($"pilot climbed out by hand at {(vel - _driver.WorldVelocityUnity).magnitude:F1} m/s relative to the aircraft");
            FirePilotLeft();
        }

        /// <summary>Show or hide the primitive canopy (when the pilot-gear asset's own Parachute_Deployed is drawn).</summary>
        public void SetChuteVisualVisible(bool on) => _pilot?.Chute?.SetVisible(on);

        /// <summary>The ripcord, pulled by hand (headset): the pins clear and the pilot chute goes. False if there is
        /// nothing to pull (still aboard, already open, on the ground).</summary>
        public bool PullRipcord()
        {
            if (!PilotOut || _chuteDone || _pilot == null || _pilot.Landed) return false;
            DeployChute();
            return true;
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
                cam.OverrideBackdrop = _driver.transform;   // keep the abandoned aircraft in frame behind the pilot
            }
            Log("pilot out — camera on the pilot, aircraft in the background");
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

        /// <summary>Free-fall body position off the PITCH axis, which the canopy does not use: hands neutral is
        /// the steepest useful track (so a pilot who has just left an aeroplane flies AWAY from it without
        /// being told to), back is flat and stable and slow, forward is head down and fast. See
        /// <see cref="FlyingGame.Core.SkydiverAero"/> for what each costs.</summary>
        private float ReadBodyPos()
        {
            float pitch = _touch != null ? _touch.StickElevator : (float)_driver.Inputs.Elevator;
            // Hands on (first person): neutral is the stable belly-to-earth arch a pilot is taught to fall in, and the
            // stick forward trades it for speed (track, head down); there is no "back" from flat.
            if (HandsOn) return Mathf.Clamp01(pitch);
            return Mathf.Clamp01(0.5f + 0.5f * pitch);
        }

        private ChaseCamera Cam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<ChaseCamera>();
            return _cam;
        }

        private void Log(string msg) => Debug.Log($"[PilotEgress] t={_t:F1}s {msg}");

        // ---- the landing one-liner, in the world (headsets) ----------------------------------------
        //
        // Ty 2026-11-04 on the Quest: "there was no corny comment when i got to the ground in my parachute i like
        // that make sure it is in the quest version as well". It was there — on the egress status line, which is
        // OnGUI, and a headset never sees OnGUI. Where the flat overlay cannot be seen the same text (label and
        // quote, as the status line has it) floats 2 m ahead of the eyes and a little below the line of sight,
        // follows the head lazily and faces it. A flat screen keeps only the status line, so nothing shows twice.

        private GameObject _remarkGo;
        private TextMesh _remark;
        private bool _remarkPlaced;

        private void LateUpdate() => UpdateRemark();

        private void UpdateRemark()
        {
            bool show = FlyingGame.Core.PilotOutRules.ShowRemark(Stage, _spoken, LastLine)
                && FlyingGame.Core.PilotOutRules.RemarkInWorld(FlatOverlayVisible())
                && !SessionSettings.MenuOpen && !SessionSettings.ReplayActive;
            Camera cam = show ? Camera.main : null;
            if (cam == null)
            {
                if (_remarkGo != null && _remarkGo.activeSelf) _remarkGo.SetActive(false);
                _remarkPlaced = false;
                return;
            }
            if (_remark == null)
            {
                _remarkGo = new GameObject("PilotRemark");
                _remark = _remarkGo.AddComponent<TextMesh>();
                _remark.font = UiFont.Get();
                _remarkGo.GetComponent<MeshRenderer>().sharedMaterial = _remark.font.material;
                _remark.fontSize = 64;
                _remark.characterSize = 0.008f;            // ~5 cm capitals: about 1.5 degrees at 2 m
                _remark.anchor = TextAnchor.MiddleCenter;
                _remark.alignment = TextAlignment.Center;
                _remark.color = new Color(1f, 0.85f, 0.3f, 1f);   // the status line's colour
            }
            string text = FlyingGame.Core.PilotOutRules.RemarkText(Label(), LastLine);
            if (_remark.text != text) _remark.text = text;
            if (!_remarkGo.activeSelf) _remarkGo.SetActive(true);

            // Ahead along the head's HEADING (level), so looking down at his boots does not put it in the grass.
            Transform eye = cam.transform;
            Vector3 f = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(eye.up, Vector3.up);
            f.Normalize();
            float below = (float)FlyingGame.Core.PilotOutRules.RemarkBelowDeg * Mathf.Deg2Rad;
            Vector3 want = eye.position + (f * Mathf.Cos(below) + Vector3.down * Mathf.Sin(below)) * (float)FlyingGame.Core.PilotOutRules.RemarkDistanceM;
            Transform t = _remarkGo.transform;
            t.position = _remarkPlaced ? Vector3.Lerp(t.position, want, 1f - Mathf.Exp(-Time.deltaTime / 0.6f)) : want;
            _remarkPlaced = true;
            // A TextMesh shows its -Z face: forward points AWAY from the reader.
            Vector3 away = t.position - eye.position;
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        // ---- status line ---------------------------------------------------------------------------

        private string Label() => IsGhost ? FlyingGame.Core.PilotOutRules.GhostLabel : PilotPhrases.Label(Outcome);

        private void UpdateStatus()
        {
            switch (Current)
            {
                case Phase.BailingOut: _status = $"BAIL OUT  canopy in {Mathf.Max(0f, BailCanopySec - _t):F1} s"; break;
                case Phase.CanopyGone: _status = $"CANOPY GONE  jump in {Mathf.Max(0f, BailJumpSec - _t):F1} s"; break;
                case Phase.FreeFall when IsGhost:
                case Phase.ChuteDeploying when IsGhost:
                    _status = $"{FlyingGame.Core.PilotOutRules.GhostLabel}  {_pilot.HeightAglM:F0} m and climbing";
                    break;
                case Phase.Ascending:
                    _status = _spoken ? $"{Label()}  “{LastLine}”" : $"{Label()}  {_pilot.HeightAglM:F0} m and climbing";
                    break;
                case Phase.FreeFall:
                    float tChute = _eject ? EjectChuteSec - _t : BailChuteSec - (_t - _pilotLeftT);
                    _status = HandsOn ? $"FREE FALL  {_pilot.AirspeedMs:F0} m/s  pull the ripcord"
                        : _eject && _t < EjectSeatSepSec ? $"EJECT  {_pilot.AirspeedMs:F0} m/s"
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
            if (SessionSettings.MenuOpen || SessionSettings.ReplayActive || Current == Phase.InCockpit || string.IsNullOrEmpty(_status)) return;
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
