using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Chase cam v2: follows the AIR-RELATIVE velocity vector, not the nose (owner request). The camera sits behind the
    /// aircraft along its flight path and looks along the flight path, so the nose visibly swings in the
    /// frame with sideslip, gyroscopic yaw, AoA and tumbles — instead of the view panning with the nose and
    /// hiding them. Below <see cref="MinTrackSpeed"/> (taxi/rest) it falls back to the nose direction.
    /// Direction is smoothed so a momentary velocity swing doesn't whip the view.
    ///
    /// PILOT FOLLOW (bail out / eject): <see cref="PilotEgress"/> sets <see cref="OverrideTarget"/> to the
    /// pilot body and feeds <see cref="OverrideVelocity"/>; the same velocity-follow logic then tracks the
    /// pilot (path pitch limited so a vertical free fall doesn't put the camera straight overhead), framed
    /// a little wider and looking slightly up so pilot + canopy are both in shot. Clearing the override
    /// (reset / aircraft switch) returns to the aircraft.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after FlightReplay (900) has posed the replayed aircraft and pilot
    public sealed class ChaseCamera : MonoBehaviour
    {
        /// <summary>Camera view points (owner 2026-10-01). RelativeWind = the flight-path camera above (downstream on the
        /// relative wind). The Fixed* views are bolted to the airframe (they roll and pitch with it). Flyby parks the camera
        /// ahead of the aircraft, up and off to one side of its path, and watches it go by.</summary>
        public enum View { RelativeWind, Cockpit, Tail, SideLeft, SideRight, Top, Bottom, Front, Flyby, RelativeWindAhead, Locked }
        public static readonly string[] ViewNames = { "Relative wind", "Cockpit", "Tail", "Left side", "Right side", "Top", "Bottom", "Front", "Fly-by", "Relative wind, ahead", "Direction lock" };
        /// <summary>The camera is in the cockpit this frame (instruments become a panel, nothing is "kept out" of the view).</summary>
        /// The FIRST player's camera (the only one, unless split screen is on): a second player's camera never writes it.
        public static bool InCockpit { get; private set; }
        /// <summary>THIS camera is in the cockpit this frame. For the first player's camera the same as <see cref="InCockpit"/>.</summary>
        public bool IsInCockpit => _inCockpit;
        private bool _inCockpit;
        /// <summary>LOCAL 2-PLAYER: 0 = the first player's camera; 1 = the second player's (split screen).</summary>
        public int PlayerIndex;
        private void SetInCockpit(bool v) { _inCockpit = v; if (PlayerIndex == 0) InCockpit = v; }
        /// <summary>Re-evaluated per camera render: stale InCockpit cannot leak sights after a view change.</summary>
        public bool CockpitSightsAllowed(FlightSimDriver owner) => isActiveAndEnabled && owner != null
            && Driver == owner && Target == owner.transform && OverrideTarget == null
            && !SideView && CurrentView == View.Cockpit && !SessionSettings.ReplayActive && !owner.Replaying;

        // Crash view (CrashFraming): latched by the first broken-off piece to arrive on land or water, until the aircraft changes.
        private bool _crashView, _crashStarted;
        private object _crashAircraft;
        private Vector3 _crashFocus, _focusVelocity, _crashVelocity, _crashBearing;
        private float _crashRange, _rangeVelocity, _largestRange;
        // Two seconds filters bobbing and membership changes without a sudden zoom on a phone.
        private const float CrashSmoothSeconds = 2f;
        private readonly List<Renderer> _crashRenderers = new(128);
        private readonly List<GameObject> _roots = new(256);
        private readonly List<WingDebris> _wings = new(128);
        private readonly List<DebrisBody> _bodies = new(128);
        // Discovery walks every scene object, so it runs every DiscoverySeconds, not every frame; positions are read each frame.
        private readonly List<WingDebris> _liveWings = new(128);
        private readonly List<DebrisBody> _liveBodies = new(128);
        private readonly List<float> _wingRadii = new(128), _bodyRadii = new(128);
        private float _nextDiscovery;
        private const float DiscoverySeconds = .5f;   // debris leaves within one physics step; 0.5 s lag is under the 2 s smoothing
        private readonly List<FlyingGame.Core.MathTypes.Vec3> _points = new(256);
        private readonly List<double> _masses = new(256), _radii = new(256), _distances = new(256);
        private void OnEnable() => FlyingGame.Core.CrashEvents.Impact += CrashImpact;
        private void CrashImpact(FlyingGame.Core.CrashImpact hit)
        {
            // A broken-off piece on land or water (2026-11-05). On land the view used to stay on the flight path, which for
            // a cartwheeling wreck is its bounces and then its nose, not a direction to look along.
            if (FlyingGame.Core.CrashFraming.OpensCrashView(hit) == false || Target == null) return;
            // Split screen: crash events carry no aircraft, so each camera takes only the pieces near its own aeroplane
            // (the other player's crash must not swing this player's view onto that wreck).
            if (LocalSplit.Active && (CoordinateMap.ToUnity(hit.Position) - Target.position).sqrMagnitude > SplitCrashRadiusM * SplitCrashRadiusM) return;
            _crashView = true;
            _crashAircraft = Driver?.Sim?.Aircraft;
        }

        /// <summary>Split screen: how near this camera's aeroplane a broken-off piece must land to open its crash view.</summary>
        public const float SplitCrashRadiusM = 400f;

        private void OnDisable()
        {
            FlyingGame.Core.CrashEvents.Impact -= CrashImpact;
            _crashView = _crashStarted = false;
            SetInCockpit(false);
            SetCockpitExtras(false);
        }

        public float CockpitFov = 72f;
        public float CockpitLookDownDeg = 4f;
        private Renderer _hidCanopy;
        private float _baseNear = -1f;
        public View CurrentView = View.RelativeWind;

        public Transform Target;
        /// <summary>Which seat, and where the pilot is looking from it. Found from the Driver when first needed.</summary>
        public CockpitView Cockpit;
        public FlightSimDriver Driver;
        public float Distance = 14f;
        public float Height = 3.5f;
        public float PositionDamp = 3.0f;
        public float DirectionDamp = 4.0f;      // how fast the followed direction tracks the flight path
        /// <summary>The camera sits EXACTLY on the flight path (air-relative aloft, ground track in ground-reference mode), with no
        /// follow lag, so any nose offset the pilot sees is real slip, skid or crab (owner 2026-09-16: all phases of flight).</summary>
        public bool ExactFlightPath = true;   // owner 2026-09-16: the foundation of the app — every phase of flight, not just the drills
        public float MinTrackSpeed = 4f;        // m/s; below this follow the nose

        /// <summary>When set, the camera follows this instead of <see cref="Target"/> (the pilot after egress).</summary>
        public Transform OverrideTarget;
        /// <summary>Pilot follow: what to keep in the BACKGROUND (the abandoned aircraft) — the camera sits on the far side
        /// of the pilot from it, looking through the pilot at it, offset a little to the side so the canopy never covers it.</summary>
        public Transform OverrideBackdrop;
        public float BackdropSideOffsetM = 4.5f;  // camera and look-at slide this far to the pilot's left
        public float BackdropMaxDownDeg = 55f;
        /// <summary>World velocity (m/s) of the override target — the path the camera follows.</summary>
        public Vector3 OverrideVelocity;
        public float OverrideDistance = 16f;
        public float OverrideHeight = 3f;
        public float OverrideLookUp = 3f;       // look-at point this far above the pilot (canopy in frame)
        public float OverrideMaxDownDeg = 40f;  // follow-direction pitch limit in free fall

        /// <summary>Side view (flare / approach practice): the camera sits beside the runway, level, tracking the aircraft
        /// along the runway so it stays centred left–right; its height follows <see cref="SideFocusY"/> (the glideslope, then
        /// the runway), so the aircraft moves up and down in the frame.</summary>
        public bool SideView;
        public Vector3 SideRight = Vector3.right;   // unit vector to the right of the runway (Unity)
        public float SideFocusY;                    // world height the frame is centred on
        public float SideDistance = 30f;

        // Fly-by: where the camera is parked, how far away the aircraft was when it was parked there, which side is next.
        private Vector3 _flyPos;
        private float _flyStartDist;
        private bool _flyValid;
        private int _flySide = 1;
        private float _baseFov = -1f;

        private Vector3 _dir = Vector3.forward; // smoothed follow direction (world)
        private Vector3 _offset;                // smoothed camera offset from the target (world)
        private Camera _cam;
        /// <summary>The aeroplane's visual, for the eye points a rigged model brings. Looked up from Target the
        /// first time it is wanted and whenever Target changes (switching aircraft rebuilds the hierarchy).</summary>
        private AirframeVisual _visualCache; private Transform _visualFor;
        private AirframeVisual _visual
        {
            get
            {
                if (Target == null) return null;
                if (_visualFor != Target) { _visualFor = Target; _visualCache = Target.GetComponent<AirframeVisual>(); }
                return _visualCache;
            }
        }

        /// <summary>Fit the chase distance to the airframe (a 737 needs ~3x the glider's 14 m).</summary>
        public void FitTo(float spanM, float lengthM = 0f)
        {
            SpanM = spanM; LengthM = lengthM > 0f ? lengthM : spanM * 0.8f;
            _flyValid = false;
            // Closer than v1 (owner: "more zoomed in" now that the HUD sits over the aircraft).
            // The old world-position lag added ~7 m of trail to this; now that the camera rides exactly at its
            // offset the distance itself carries that (owner: "a little too close" at 0.75 span).
            // Sized by the larger of span and length (owner 2026-10-07: "the H-4 is huge and needs a wider zoom" — the old
            // 66 m cap framed its 98 m span from inside the wing). Small types are unchanged (span governs, ≥ 11 m).
            // Ty 2026-11-02: "zoom out a bit on all airplanes … use 50% more distance" — ChaseFraming.ZoomOut (1.5) on top
            // of the old fit (owner 2026-09-10: "zoom out just a little", was 1.15 span); height keeps the same fraction, so
            // the aircraft stays where it was on the screen. The bailed-out pilot's framing keeps the old base distance.
            float baseDistance = (float)FlyingGame.Core.ChaseFraming.BaseDistanceM(spanM, LengthM);
            Distance = (float)FlyingGame.Core.ChaseFraming.FollowDistanceM(spanM, LengthM);
            Height = (float)FlyingGame.Core.ChaseFraming.FollowHeightM(Distance);
            OverrideDistance = Mathf.Max(16f, baseDistance * 1.15f);
            OverrideHeight = OverrideDistance * 0.19f;   // 16 m / 3 m before
        }
        /// <summary>Airframe length (m), nose to tail.</summary>
        public float LengthM { get; private set; } = 12f;

        public float SpanM { get; private set; } = 15f;

        public void SetView(View v)
        {
            // Direction lock (owner 2026-10-08): freeze the direction the camera is looking at the moment it's engaged.
            if (v == View.Locked && CurrentView != View.Locked)
            {
                Vector3 d = Target != null ? transform.position - Target.position : -transform.forward;
                // From the cockpit the camera is AT the aeroplane: lock behind and a little above it instead.
                if (CurrentView == View.Cockpit && Target != null)
                {
                    Vector3 back = Vector3.ProjectOnPlane(-Target.forward, Vector3.up);
                    d = (back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back) + Vector3.up * 0.25f;
                }
                _lockDir = d.sqrMagnitude > 1e-4f ? d.normalized : -transform.forward;
                _beforeLock = CurrentView;
            }
            // Entering the cockpit: eye point, looking forward (Ty, 2026-11-02: "all he sees is sky"). Whatever the
            // look was left at -- a snap over the shoulder, a device offset -- does not carry into a fresh selection.
            if (v == View.Cockpit && CurrentView != View.Cockpit)
            {
                if (Cockpit == null) Cockpit = Driver != null ? Driver.GetComponent<CockpitView>() : null;
                if (Cockpit != null) Cockpit.OnEnterCockpitView();
            }
            CurrentView = v;
            _flyValid = false;
        }
        private Vector3 _lockDir = new(0f, 0.25f, -1f);
        private View _beforeLock = View.RelativeWind;

        /// <summary>V: direction lock on / back to the view before it.</summary>
        public void ToggleLock() => SetView(CurrentView == View.Locked ? _beforeLock : View.Locked);

        /// <summary>Direction lock (owner 2026-10-08): the camera keeps the aircraft centred at the chase distance, but its
        /// DIRECTION is fixed in the world — it does not turn with heading, pitch or roll, and the horizon stays level — so a
        /// spin, a roll or a loop is seen as the aircraft really moves, not as the world spinning round it.</summary>
        private void LockedView(Transform t)
        {
            Vector3 dir = _lockDir.normalized;
            transform.position = t.position + dir * Distance;
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            transform.rotation = Quaternion.LookRotation(-dir, up);
        }

        /// <summary>Cockpit: wide view, a near clip plane right at the eye, the canopy hidden (put back on the way out unless
        /// it has been jettisoned meanwhile).</summary>
        private void SetCockpitExtras(bool on)
        {
            if (_cam == null) return;
            if (_baseNear < 0f) _baseNear = _cam.nearClipPlane;
            _cam.nearClipPlane = on ? 0.05f : _baseNear;
            if (on) _cam.fieldOfView = CockpitFov;
            Transform canopy = Target != null ? Target.Find("Canopy") : null;
            Renderer r = canopy != null ? canopy.GetComponent<Renderer>() : null;
            if (on && r != null && r.enabled) { r.enabled = false; _hidCanopy = r; }
            if (!on && _hidCanopy != null)
            {
                var egress = Target != null ? Target.GetComponent<PilotEgress>() : null;
                bool jettisoned = egress != null && egress.Current != PilotEgress.Phase.InCockpit && egress.Current != PilotEgress.Phase.BailingOut;
                if (!jettisoned) _hidCanopy.enabled = true;
                _hidCanopy = null;
            }
        }

        public void NextView() => SetView((View)(((int)CurrentView + 1) % ViewNames.Length));

        /// <summary>EDITOR TOOLS ONLY (QuestViewShots): place the camera as LateUpdate does, with no player loop.</summary>
        public void PlaceForTesting() => LateUpdate();

        /// <summary>In a headset there is no zoom (the runtime owns the field of view), so the fly-by parks much closer:
        /// at the desktop's 5 s ahead a 9 m aeroplane would be a speck. Set by <see cref="QuestRig"/> while it runs.</summary>
        public static bool CompactFlyby;

        /// <summary>The lowest a camera may sit at <paramref name="pos"/>: <see cref="MinAboveSurfaceM"/> over the ground
        /// or the water, whichever is higher.</summary>
        public static float SurfaceFloorAt(Vector3 pos)
        {
            double simX = pos.z, simY = pos.x;
            double floor = FlyingGame.Core.WorldTerrain.GroundHeightAt(simX, simY);
            double? water = FlyingGame.Core.FloatHydro.WaterSurfaceAt(simX, simY, pos.y);
            if (water.HasValue) floor = System.Math.Max(floor, water.Value);
            return (float)floor + MinAboveSurfaceM;
        }

        /// <summary>Fixed views: the camera is bolted to the airframe — position and "up" both in body axes, so it rolls,
        /// pitches and yaws with the aircraft and the world moves around it.</summary>
        private void FixedView(Transform t)
        {
            Vector3 p = t.position, f = t.forward, u = t.up, r = t.right;
            float d = Distance;
            switch (CurrentView)
            {
                case View.Cockpit:
                    // Cockpit (owner 2026-10-01; per-seat view point and look-around 2026-10-31): the eye at the
                    // seat's own authored view point, looking along the nose a few degrees down, rolling with the
                    // aircraft — the cowl, wings and prop in view, the instruments as a panel below. The pilot's
                    // head turns on top of that (CockpitView: a hat, buttons or goggles).
                    if (Cockpit == null) Cockpit = Driver != null ? Driver.GetComponent<CockpitView>() : null;
                    // With no authored seat eye, fall back to the MODEL's own eye empty (the Extra's Eye_Rear /
                    // Eye_Front, drawn where its draughtsman sat the pilot) before the old canopy-top estimate.
                    // The ORIENTATION still comes from the airframe either way: an eye point is a position, and a
                    // stray rotation on one would cant the whole view with nothing on screen to say why.
                    Transform modelEye = _visual != null ? _visual.CockpitEye() : null;
                    bool authored = Cockpit != null && Cockpit.HasAuthoredViewPoint;
                    Vector3 eye = authored ? Cockpit.EyeLocal
                                : modelEye != null ? t.InverseTransformPoint(modelEye.position)
                                : Cockpit != null ? Cockpit.EyeLocal
                                : PilotEgress.CockpitLocal(Driver) + Vector3.up * 0.1f;
                    // Unity's Euler is (pitch, yaw, roll) and a look UP is a NEGATIVE pitch, hence the sign.
                    float lookYaw = Cockpit != null ? (float)Cockpit.Look.YawDeg : 0f;
                    float lookPitch = Cockpit != null ? (float)Cockpit.Look.PitchDeg : 0f;
                    transform.position = t.TransformPoint(eye);
                    transform.rotation = t.rotation * Quaternion.Euler(CockpitLookDownDeg - lookPitch, lookYaw, 0f);
                    break;
                case View.Tail:
                    transform.position = p - f * d + u * Height;
                    transform.rotation = Quaternion.LookRotation(p + f * 4f - transform.position, u);
                    break;
                case View.SideLeft:
                    transform.position = p - r * d;
                    transform.rotation = Quaternion.LookRotation(r, u);
                    break;
                case View.SideRight:
                    transform.position = p + r * d;
                    transform.rotation = Quaternion.LookRotation(-r, u);
                    break;
                case View.Top:
                    transform.position = p + u * d;
                    transform.rotation = Quaternion.LookRotation(-u, f);
                    break;
                case View.Bottom:
                    transform.position = p - u * d;
                    transform.rotation = Quaternion.LookRotation(u, f);
                    break;
                case View.Front:
                    transform.position = p + f * d + u * Height * 0.5f;
                    transform.rotation = Quaternion.LookRotation(p - transform.position, u);
                    break;
            }
        }

        /// <summary>Fly-by: park the camera ahead of the aircraft on its flight path, up and off to one side, fixed in space,
        /// and keep the aircraft centred (zooming so it stays a sensible size). Once it has gone past and is as far away as
        /// it was when the camera was parked, re-park ahead of it on the other side.</summary>
        private void FlybyView(Transform t)
        {
            Vector3 p = t.position;
            Vector3 v = Driver != null ? Driver.WorldVelocityUnity : Vector3.zero;
            float speed = v.magnitude;
            Vector3 vhat = speed >= MinTrackSpeed ? v / speed : t.forward;
            if (_flyValid)
            {
                Vector3 rel = p - _flyPos;
                float dist = rel.magnitude;
                bool past = Vector3.Dot(rel, vhat) > 0f;
                if ((past && dist >= _flyStartDist) || dist > _flyStartDist * 2.5f) _flyValid = false;   // gone by, or turned away / scrubbed
            }
            if (!_flyValid)
            {
                float ahead = CompactFlyby
                    ? Mathf.Clamp(Mathf.Max(speed, 15f) * 2.5f, Mathf.Max(30f, SpanM * 3f), 150f)
                    : Mathf.Clamp(Mathf.Max(speed, 15f) * 5f, Mathf.Max(60f, SpanM * 5f), 450f);
                Vector3 side = Vector3.Cross(Vector3.up, vhat);
                if (side.sqrMagnitude < 0.01f) side = t.right;
                side.Normalize();
                Vector3 pos = p + vhat * ahead + side * (_flySide * (ahead * 0.15f + SpanM)) + Vector3.up * (ahead * 0.06f + SpanM * 0.3f);
                // Never under the ground (or the water).
                var sim = CoordinateMap.ToSim(pos);
                float ground = (float)FlyingGame.Core.WorldTerrain.GroundHeightAt(sim.X, sim.Y) + 3f;
                if (pos.y < ground) pos.y = ground;
                _flyPos = pos;
                _flyStartDist = (pos - p).magnitude;
                _flySide = -_flySide;
                _flyValid = true;
            }
            transform.position = _flyPos;
            Vector3 look = p - _flyPos;
            transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            if (_cam != null)
            {
                // Zoom so the aircraft fills about the same share of the frame as in the chase view.
                float want = 2f * Mathf.Atan(SpanM * 0.9f / Mathf.Max(1f, look.magnitude)) * Mathf.Rad2Deg;
                _cam.fieldOfView = Mathf.Clamp(want, 6f, _baseFov);
            }
        }

        private Vector3 DesiredDirection()
        {
            if (Driver != null)
            {
                // Air-relative, not ground: in a crosswind the nose crabs into the wind while the ground
                // track stays put — following the ground vector made a 5 m/s crosswind look like a
                // permanent right yaw. Following the air vector centres the nose in steady flight and
                // still shows genuine sideslip / gyroscopic yaw / tumbles.
                // ... but only once actually MOVING: parked in a 5 m/s crosswind the air vector is the wind from the
                // side, and the launch view sat off the wingtip (owner). Ground track speed gates it.
                // Flight Path or Track is the pilot's choice now (the CAM button; Ty 2026-11-03), not the height: below
                // 500 ft the old automatic switch put the chase view off at 7:30 on a crosswind final. A runway lesson
                // can still force Track (CameraReference.Effective).
                FlyingGame.Core.CameraReference.Mode mode = FlyingGame.Core.CameraReference.Effective(SessionSettings.CameraReferenceMode, Driver.GroundReferenceForced);
                Vector3 a = Driver.AirVelocityUnity, g = Driver.WorldVelocityUnity;
                FlyingGame.Core.MathTypes.Vec3? d = FlyingGame.Core.CameraReference.Direction(mode,
                    new FlyingGame.Core.MathTypes.Vec3(a.x, a.y, a.z), new FlyingGame.Core.MathTypes.Vec3(g.x, g.y, g.z), MinTrackSpeed);
                if (d.HasValue) return new Vector3((float)d.Value.X, (float)d.Value.Y, (float)d.Value.Z);
            }
            return Target.forward;
        }

        /// <summary>Pilot follow: along the pilot's velocity, pitch-limited; hold the last direction when stopped (landed).</summary>
        private Vector3 OverrideDirection()
        {
            if (OverrideBackdrop != null && OverrideTarget != null)
            {
                // Look from the pilot toward the aircraft (pitch-limited so a crash far below stays watchable).
                Vector3 to = OverrideBackdrop.position - OverrideTarget.position;
                if (to.magnitude > 2f)
                {
                    Vector3 bh = new Vector3(to.x, 0f, to.z);
                    if (bh.magnitude < 0.5f) bh = new Vector3(_dir.x, 0f, _dir.z);
                    if (bh.magnitude < 0.01f) bh = Vector3.forward;
                    bh.Normalize();
                    float bp = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                    bp = Mathf.Clamp(bp, -BackdropMaxDownDeg, 35f) * Mathf.Deg2Rad;
                    return bh * Mathf.Cos(bp) + Vector3.up * Mathf.Sin(bp);
                }
            }
            Vector3 v = OverrideVelocity;
            if (v.magnitude < MinTrackSpeed) return _dir;
            Vector3 h = new Vector3(v.x, 0f, v.z);
            if (h.magnitude < 1f) h = new Vector3(_dir.x, 0f, _dir.z);
            if (h.magnitude < 0.01f) h = Vector3.forward;
            h.Normalize();
            float pitch = Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -OverrideMaxDownDeg, 20f) * Mathf.Deg2Rad;
            return h * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
        }

        /// <summary>How close the camera may get to the ground or the water. Owner 2026-10-16: it never goes
        /// under either — when an aeroplane flips, the camera gives up the angle rather than the view.</summary>
        public const float MinAboveSurfaceM = 1.524f;      // 5 ft

        private void LateUpdate()
        {
            PlaceCamera();
            PropShake();
            KeepAboveSurface();
        }

        /// <summary>A bent prop shakes the airframe once a revolution (Aircraft.PropVibrationG, Ty 2026-11-05: "a bent prop
        /// ... comes with reduced thrust and a vibration"), and the pilot's head with it. The real shake is 12-45 Hz, faster
        /// than the frame rate, so each frame takes a fresh random offset of that amplitude: the blur of a vibrating seat,
        /// not a slow wobble. Cockpit view only (it is placed afresh every frame, so nothing accumulates); ~6 cm and 3° per
        /// g, which puts a 0.05 g prop at a 3 mm, 0.15° buzz. Not in a headset: shaking the world there is a comfort risk,
        /// so the Quest gets it through the sound.</summary>
        private void PropShake()
        {
#if !UNITY_ANDROID
            if (CurrentView != View.Cockpit || Driver == null || Driver.Sim == null) return;
            float g = Mathf.Min(0.25f, (float)Driver.Sim.Aircraft.PropVibrationG());
            if (g < 1e-4f) return;
            transform.position += transform.rotation * (Random.insideUnitSphere * (0.06f * g));
            transform.rotation *= Quaternion.Euler(Random.Range(-3f, 3f) * g, Random.Range(-3f, 3f) * g, 0f);
#endif
        }

        /// <summary>
        /// The floor under every external view. The camera may be looking from anywhere, but it is never under
        /// the ground and never under the water: it rises to five feet above whichever is higher and keeps the
        /// aeroplane in frame from there, which costs the angle and saves the shot.
        ///
        /// The cockpit is exempt — that is a pilot's eye, not a camera, and when he is upside down in a river
        /// his head really is down there.
        /// </summary>
        private void KeepAboveSurface()
        {
            if (_inCockpit) return;
            Vector3 pos = transform.position;
            // Unity (east, up, north) → sim (north, east, up).
            double simX = pos.z, simY = pos.x;
            double floor = FlyingGame.Core.WorldTerrain.GroundHeightAt(simX, simY);
            double? water = FlyingGame.Core.FloatHydro.WaterSurfaceAt(simX, simY, pos.y);
            if (water.HasValue) floor = System.Math.Max(floor, water.Value);
            float min = (float)floor + MinAboveSurfaceM;
            if (pos.y >= min) return;
            pos.y = min;
            transform.position = pos;
            // The angle is gone; keep the aeroplane.
            Transform tgt = OverrideTarget != null ? OverrideTarget : Target;
            if (tgt != null)
            {
                Vector3 look = (_crashStarted && !_inCockpit && OverrideTarget == null ? _crashFocus : tgt.position) - pos;
                if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
        }

        private void PlaceCamera()
        {
            // Portrait: the view is the area above the control tray (ScreenLayout); landscape, or any orientation with a
            // gamepad connected (the touch sticks hidden, Ty 2026-11-02): full screen.
            // Re-evaluated every frame so an autorotation mid-flight takes effect immediately.
            _cam ??= GetComponent<Camera>();
            if (_cam != null)
            {
                // Split screen (LocalSplit): each player's half; the touch tray is the first player's and is hidden with pads.
                Rect vp = LocalSplit.Active ? LocalSplit.ViewportFor(PlayerIndex) : ScreenLayout.CameraViewport;
                if (_cam.rect != vp) _cam.rect = vp;
            }

            if (_cam != null && _baseFov < 0f) _baseFov = _cam.fieldOfView;
            // In a replay FlightReplay sets the override itself (the replayed pilot after the recorded bail-out).
            bool ovr = OverrideTarget != null;
            Transform tgt = ovr ? OverrideTarget : Target;
            if (tgt == null)
            {
                SetInCockpit(false);
                SetCockpitExtras(false);
                return;
            }
            bool cockpit = !ovr && !SideView && CurrentView == View.Cockpit;
            SetInCockpit(cockpit);
            SetCockpitExtras(cockpit);
            if (_cam != null && CurrentView != View.Flyby && !cockpit && _baseFov > 0f && _cam.fieldOfView != _baseFov) _cam.fieldOfView = _baseFov;
            if (_crashView && !ReferenceEquals(_crashAircraft, Driver?.Sim?.Aircraft)) _crashView = _crashStarted = false;
            if (_crashView && !cockpit && !ovr) { CrashView(); return; }
            if (!ovr && !SideView && CurrentView == View.Flyby) { FlybyView(tgt); return; }
            if (!ovr && !SideView && CurrentView == View.Locked) { LockedView(tgt); return; }   // side-view lessons keep their camera
            if (!ovr && !SideView && CurrentView != View.RelativeWind && CurrentView != View.RelativeWindAhead) { FixedView(tgt); return; }
            if (SideView && !ovr)
            {
                Vector3 tp = tgt.position;
                Vector3 sidePos = new Vector3(tp.x, SideFocusY, tp.z) + SideRight * SideDistance;
                // Along the runway the camera keeps up with the aircraft exactly (a 6/s lag left it swimming 6 m ahead of centre
                // and drifting back as it slowed); the height eases.
                Vector3 cur = transform.position;
                float hy = Mathf.Lerp(cur.y, sidePos.y, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                transform.position = new Vector3(sidePos.x, hy, sidePos.z);
                transform.rotation = Quaternion.LookRotation(-SideRight, Vector3.up);
                return;
            }

            Vector3 want = ovr ? OverrideDirection() : DesiredDirection();
            _dir = ExactFlightPath && !ovr ? want.normalized : Vector3.Slerp(_dir, want, 1f - Mathf.Exp(-DirectionDamp * Time.unscaledDeltaTime)).normalized;

            // Reference up: world up, unless the path is near-vertical (tumble/loop), then the aircraft's up
            // keeps the horizon from flipping. (The pilot path is pitch-limited, so always world up there.)
            Vector3 up = !ovr && Mathf.Abs(Vector3.Dot(_dir, Vector3.up)) > 0.9f ? Target.up : Vector3.up;

            float dist = ovr ? OverrideDistance : Distance;
            float hgt = ovr ? OverrideHeight : Height;
            // Damp the OFFSET from the target, not the world position: a lag on a world position that moves at
            // 20+ m/s leaves a steady 7 m trail along the GROUND track, which in a crosswind pulled the camera off
            // the air-vector line and showed the aircraft from the side (owner: "camera follows the ground").
            bool backdrop = ovr && OverrideBackdrop != null;
            Vector3 side = backdrop ? -Vector3.Cross(up, _dir).normalized * BackdropSideOffsetM : Vector3.zero;   // pilot's left
            // "Relative wind, ahead" (owner 2026-10-06: "opposite to the standard flight path vector view … in front looking back"):
            // the same flight-path line, the camera out AHEAD of the aircraft on it, looking back at it.
            bool ahead = !ovr && CurrentView == View.RelativeWindAhead;
            Vector3 desiredOffset = (ahead ? _dir : -_dir) * dist + up * hgt + side;
            _offset = ExactFlightPath && !ovr ? desiredOffset : Vector3.Lerp(_offset, desiredOffset, 1f - Mathf.Exp(-PositionDamp * Time.unscaledDeltaTime));
            transform.position = tgt.position + _offset;
            // With a backdrop the look-at sits between the pilot and the aircraft's direction, nudged the same way, so the
            // pilot hangs left of centre with the canopy above him and the aircraft shows clear to the right.
            Vector3 lookAt = tgt.position + (ahead ? -_dir : _dir) * (backdrop ? 12f : 4f) + (ovr ? Vector3.up * OverrideLookUp * (backdrop ? 0.5f : 1f) : Vector3.zero) + side * 0.35f;
            transform.rotation = Quaternion.LookRotation(lookAt - transform.position, up);
        }

        /// <summary>How big a part is drawn: its meshes only. The aircraft also carries the crash spray and foam (particles,
        /// CrashWaterVisuals), the splash rings (lines) and the fuel sheen (a quad scaled to the spreading slick), and
        /// sizing the wreck by those climbed the camera into the sky for as long as the slick spread (Ty 2026-11-05).</summary>
        private float CrashRadius(Transform body, Vector3 centre)
        {
            body.GetComponentsInChildren(false, _crashRenderers);
            float radius = 0;
            foreach (var renderer in _crashRenderers)
                if (renderer.enabled && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    radius = Mathf.Max(radius, Vector3.Distance(centre, renderer.bounds.center) + renderer.bounds.extents.magnitude);
            return (float)FlyingGame.Core.CrashFraming.ClampRadiusM(radius, SpanM, LengthM);
        }

        // Unity axes in a Core Vec3: the cluster is frame-free, and "up" is handed to CrashFraming as +y.
        private void AddCrashPoint(Vector3 point, float mass, float radius)
        {
            _points.Add(new FlyingGame.Core.MathTypes.Vec3(point.x, point.y, point.z)); _masses.Add(mass); _radii.Add(radius);
        }

        /// <summary>The wreck, framed from 30 degrees above the horizon (CrashFraming), on land or on water.</summary>
        private void CrashView()
        {
            _points.Clear(); _masses.Clear(); _radii.Clear();
            AddCrashPoint(Target.position, (float)(Driver?.Sim?.Aircraft?.MassProperties.MassKg ?? 800), CrashRadius(Target, Target.position));
            if (!_crashStarted || Time.unscaledTime >= _nextDiscovery)
            {
                _nextDiscovery = Time.unscaledTime + DiscoverySeconds;
                _liveWings.Clear(); _liveBodies.Clear(); _wingRadii.Clear(); _bodyRadii.Clear();
                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    var scene = SceneManager.GetSceneAt(sceneIndex);
                    if (!scene.isLoaded) continue;
                    // Unity allocates an intermediate array unless capacity is strictly greater than rootCount.
                    if (_roots.Capacity <= scene.rootCount) _roots.Capacity = scene.rootCount + 1;
                    scene.GetRootGameObjects(_roots);
                    foreach (var root in _roots)
                    {
                        root.GetComponentsInChildren(false, _wings);
                        foreach (var wing in _wings) if (wing.isActiveAndEnabled)
                        { _liveWings.Add(wing); _wingRadii.Add(CrashRadius(wing.transform, wing.transform.position)); }
                        root.GetComponentsInChildren(false, _bodies);
                        foreach (var body in _bodies) if (body.isActiveAndEnabled) { _liveBodies.Add(body); _bodyRadii.Add(body.RestHeightM); }
                    }
                }
            }
            for (int i = 0; i < _liveWings.Count; i++)
            {
                var wing = _liveWings[i];
                if (wing == null || !wing.isActiveAndEnabled) continue;
                var rb = wing.GetComponent<Rigidbody>();
                // Unbound ballistic fragments have no sim mass: 15 kg matches the canopy estimate.
                AddCrashPoint(wing.Piece != null ? CoordinateMap.ToUnity(wing.Piece.State.Position) : wing.transform.position,
                    wing.Piece != null ? (float)wing.Piece.MassKg : rb != null ? rb.mass : 15f, _wingRadii[i]);
            }
            for (int i = 0; i < _liveBodies.Count; i++)
            {
                var body = _liveBodies[i];
                if (body != null && body.isActiveAndEnabled) AddCrashPoint(body.transform.position, body.MassKg, _bodyRadii[i]);
            }
            var (f, r) = FlyingGame.Core.CrashFraming.Cluster(_points, _masses, _radii, _distances);
            Vector3 focus = new((float)f.X, (float)f.Y, (float)f.Z); float radius = (float)r;
            float dt = Time.unscaledDeltaTime;
            if (!_crashStarted)
            {
                _crashStarted = true;
                _crashRange = Vector3.Distance(transform.position, Target.position); _largestRange = _crashRange;
                // Start on the existing sightline, so entering crash view does not snap the aim.
                _crashFocus = transform.position + transform.forward * _crashRange;
                // Stay on the side the camera is on. A flight-path camera looking straight up or down has no bearing
                // along its sightline: take it from where the camera stands relative to the wreck.
                _crashBearing = Vector3.ProjectOnPlane(transform.position - _crashFocus, Vector3.up);
                if (_crashBearing.magnitude < .25f * _crashRange) _crashBearing = Vector3.ProjectOnPlane(transform.position - focus, Vector3.up);
                _crashBearing = _crashBearing.sqrMagnitude < .01f ? Vector3.back : _crashBearing.normalized;
                _focusVelocity = _crashVelocity = Vector3.zero; _rangeVelocity = 0;
            }
            _crashFocus = Vector3.SmoothDamp(_crashFocus, focus, ref _focusVelocity, CrashSmoothSeconds, Mathf.Infinity, dt);
            float half = (_cam != null ? _cam.fieldOfView : 60f) * Mathf.Deg2Rad * .5f;
            if (_cam != null) half = Mathf.Min(half, Mathf.Atan(Mathf.Tan(half) * _cam.aspect));
            // 15% breathing room; account for centre smoothing so the fitted sphere stays in frame as it drifts.
            // Keep the largest range the WRECK has needed (no pumping inward when a piece sinks/disappears); the focus's lag
            // behind a sliding wreck widens this frame only — latched, it parked the camera 100+ m up for good.
            _largestRange = Mathf.Max(_largestRange, (float)FlyingGame.Core.CrashFraming.RangeM(radius, 0, half, Distance));
            float want = Mathf.Max(_largestRange, (float)FlyingGame.Core.CrashFraming.RangeM(radius, Vector3.Distance(focus, _crashFocus), half, Distance));
            _crashRange = Mathf.SmoothDamp(_crashRange, want, ref _rangeVelocity, CrashSmoothSeconds, Mathf.Infinity, dt);
            // 30 degrees above the horizon exposes the field; increasing range raises and retreats together.
            var o = FlyingGame.Core.CrashFraming.CameraOffset(new FlyingGame.Core.MathTypes.Vec3(_crashBearing.x, 0, _crashBearing.z),
                new FlyingGame.Core.MathTypes.Vec3(0, 1, 0), _crashRange);
            Vector3 desired = _crashFocus + new Vector3((float)o.X, (float)o.Y, (float)o.Z);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _crashVelocity, CrashSmoothSeconds, Mathf.Infinity, dt);
            Vector3 look = _crashFocus - transform.position;
            if (look.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        public void SnapBehind()
        {
            bool ovr = OverrideTarget != null;
            Transform tgt = ovr ? OverrideTarget : Target;
            if (tgt == null)
            {
                return;
            }

            _dir = ovr ? OverrideDirection() : DesiredDirection();
            _offset = -_dir * (ovr ? OverrideDistance : Distance) + Vector3.up * (ovr ? OverrideHeight : Height);
            transform.position = tgt.position + _offset;
        }
    }
}
