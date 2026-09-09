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
    public sealed class ChaseCamera : MonoBehaviour
    {
        public Transform Target;
        public FlightSimDriver Driver;
        public float Distance = 14f;
        public float Height = 3.5f;
        public float PositionDamp = 3.0f;
        public float DirectionDamp = 4.0f;      // how fast the followed direction tracks the flight path
        public float MinTrackSpeed = 4f;        // m/s; below this follow the nose

        /// <summary>When set, the camera follows this instead of <see cref="Target"/> (the pilot after egress).</summary>
        public Transform OverrideTarget;
        /// <summary>World velocity (m/s) of the override target — the path the camera follows.</summary>
        public Vector3 OverrideVelocity;
        public float OverrideDistance = 16f;
        public float OverrideHeight = 3f;
        public float OverrideLookUp = 3f;       // look-at point this far above the pilot (canopy in frame)
        public float OverrideMaxDownDeg = 40f;  // follow-direction pitch limit in free fall

        private Vector3 _dir = Vector3.forward; // smoothed follow direction (world)
        private Vector3 _offset;                // smoothed camera offset from the target (world)
        private Camera _cam;

        /// <summary>Fit the chase distance to the airframe (a 737 needs ~3x the glider's 14 m).</summary>
        public void FitTo(float spanM)
        {
            // Closer than v1 (owner: "more zoomed in" now that the HUD sits over the aircraft).
            // The old world-position lag added ~7 m of trail to this; now that the camera rides exactly at its
            // offset the distance itself carries that (owner: "a little too close" at 0.75 span).
            Distance = Mathf.Clamp(spanM * 1.15f, 10f, 60f);
            Height = Distance * 0.22f;
        }

        private Vector3 DesiredDirection()
        {
            if (Driver != null)
            {
                // Air-relative, not ground: in a crosswind the nose crabs into the wind while the ground
                // track stays put — following the ground vector made a 5 m/s crosswind look like a
                // permanent right yaw. Following the air vector centres the nose in steady flight and
                // still shows genuine sideslip / gyroscopic yaw / tumbles.
                Vector3 v = Driver.AirVelocityUnity;
                if (v.magnitude >= MinTrackSpeed) return v.normalized;
            }
            return Target.forward;
        }

        /// <summary>Pilot follow: along the pilot's velocity, pitch-limited; hold the last direction when stopped (landed).</summary>
        private Vector3 OverrideDirection()
        {
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

            bool ovr = OverrideTarget != null;
            Transform tgt = ovr ? OverrideTarget : Target;
            if (tgt == null)
            {
                return;
            }

            Vector3 want = ovr ? OverrideDirection() : DesiredDirection();
            _dir = Vector3.Slerp(_dir, want, 1f - Mathf.Exp(-DirectionDamp * Time.deltaTime)).normalized;

            // Reference up: world up, unless the path is near-vertical (tumble/loop), then the aircraft's up
            // keeps the horizon from flipping. (The pilot path is pitch-limited, so always world up there.)
            Vector3 up = !ovr && Mathf.Abs(Vector3.Dot(_dir, Vector3.up)) > 0.9f ? Target.up : Vector3.up;

            float dist = ovr ? OverrideDistance : Distance;
            float hgt = ovr ? OverrideHeight : Height;
            // Damp the OFFSET from the target, not the world position: a lag on a world position that moves at
            // 20+ m/s leaves a steady 7 m trail along the GROUND track, which in a crosswind pulled the camera off
            // the air-vector line and showed the aircraft from the side (owner: "camera follows the ground").
            Vector3 desiredOffset = -_dir * dist + up * hgt;
            _offset = Vector3.Lerp(_offset, desiredOffset, 1f - Mathf.Exp(-PositionDamp * Time.deltaTime));
            transform.position = tgt.position + _offset;
            Vector3 lookAt = tgt.position + _dir * 4f + (ovr ? Vector3.up * OverrideLookUp : Vector3.zero);
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
