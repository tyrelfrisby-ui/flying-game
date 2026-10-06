using UnityEngine;

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
        public enum View { RelativeWind, Cockpit, Tail, SideLeft, SideRight, Top, Bottom, Front, Flyby, RelativeWindAhead }
        public static readonly string[] ViewNames = { "Relative wind", "Cockpit", "Tail", "Left side", "Right side", "Top", "Bottom", "Front", "Fly-by", "Relative wind, ahead" };
        /// <summary>The camera is in the cockpit this frame (instruments become a panel, nothing is "kept out" of the view).</summary>
        public static bool InCockpit { get; private set; }
        public float CockpitFov = 72f;
        public float CockpitLookDownDeg = 4f;
        private Renderer _hidCanopy;
        private float _baseNear = -1f;
        public View CurrentView = View.RelativeWind;

        public Transform Target;
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

        /// <summary>Fit the chase distance to the airframe (a 737 needs ~3x the glider's 14 m).</summary>
        public void FitTo(float spanM)
        {
            SpanM = spanM;
            _flyValid = false;
            // Closer than v1 (owner: "more zoomed in" now that the HUD sits over the aircraft).
            // The old world-position lag added ~7 m of trail to this; now that the camera rides exactly at its
            // offset the distance itself carries that (owner: "a little too close" at 0.75 span).
            Distance = Mathf.Clamp(spanM * 1.3f, 11f, 66f);   // owner 2026-09-10: "zoom out just a little" (was 1.15 span)
            Height = Distance * 0.22f;
        }

        public float SpanM { get; private set; } = 15f;

        public void SetView(View v)
        {
            CurrentView = v;
            _flyValid = false;
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

        /// <summary>Fixed views: the camera is bolted to the airframe — position and "up" both in body axes, so it rolls,
        /// pitches and yaws with the aircraft and the world moves around it.</summary>
        private void FixedView(Transform t)
        {
            Vector3 p = t.position, f = t.forward, u = t.up, r = t.right;
            float d = Distance;
            switch (CurrentView)
            {
                case View.Cockpit:
                    // Simple cockpit (owner 2026-10-01): the eye at the canopy, looking along the nose a few degrees down,
                    // rolling with the aircraft — the cowl, wings and prop in view, the instruments as a panel below.
                    transform.position = t.TransformPoint(PilotEgress.CockpitLocal(Driver) + Vector3.up * 0.1f);
                    transform.rotation = t.rotation * Quaternion.Euler(CockpitLookDownDeg, 0f, 0f);
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
                float ahead = Mathf.Clamp(Mathf.Max(speed, 15f) * 5f, Mathf.Max(60f, SpanM * 5f), 450f);
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
                // Ground reference (runway lessons, low altitude): follow the ground track instead — the runway is the reference.
                Vector3 v = Driver.GroundReference ? Driver.WorldVelocityUnity : Driver.AirVelocityUnity;
                if (v.magnitude >= MinTrackSpeed && Driver.WorldVelocityUnity.magnitude >= MinTrackSpeed) return v.normalized;
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

        private void LateUpdate()
        {
            // Portrait: the view is the area above the control tray (ScreenLayout); landscape: full screen.
            // Re-evaluated every frame so an autorotation mid-flight takes effect immediately.
            _cam ??= GetComponent<Camera>();
            if (_cam != null)
            {
                Rect vp = ScreenLayout.CameraViewport;
                if (_cam.rect != vp) _cam.rect = vp;
            }

            if (_cam != null && _baseFov < 0f) _baseFov = _cam.fieldOfView;
            // In a replay FlightReplay sets the override itself (the replayed pilot after the recorded bail-out).
            bool ovr = OverrideTarget != null;
            Transform tgt = ovr ? OverrideTarget : Target;
            if (tgt == null)
            {
                return;
            }
            bool cockpit = !ovr && !SideView && CurrentView == View.Cockpit;
            InCockpit = cockpit;
            SetCockpitExtras(cockpit);
            if (_cam != null && CurrentView != View.Flyby && !cockpit && _baseFov > 0f && _cam.fieldOfView != _baseFov) _cam.fieldOfView = _baseFov;
            if (!ovr && !SideView && CurrentView == View.Flyby) { FlybyView(tgt); return; }   // side-view lessons keep their camera
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
