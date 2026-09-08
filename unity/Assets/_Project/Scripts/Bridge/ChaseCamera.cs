using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Chase cam v2: follows the VELOCITY VECTOR, not the nose (owner request). The camera sits behind the
    /// aircraft along its flight path and looks along the flight path, so the nose visibly swings in the
    /// frame with sideslip, gyroscopic yaw, AoA and tumbles — instead of the view panning with the nose and
    /// hiding them. Below <see cref="MinTrackSpeed"/> (taxi/rest) it falls back to the nose direction.
    /// Direction is smoothed so a momentary velocity swing doesn't whip the view.
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

        private Vector3 _dir = Vector3.forward; // smoothed follow direction (world)

        /// <summary>Fit the chase distance to the airframe (a 737 needs ~3x the glider's 14 m).</summary>
        public void FitTo(float spanM)
        {
            // Closer than v1 (owner: "more zoomed in" now that the HUD sits over the aircraft).
            Distance = Mathf.Clamp(spanM * 0.75f, 7f, 45f);   // owner: closer, aircraft bigger on screen
            Height = Distance * 0.22f;
        }

        private Vector3 DesiredDirection()
        {
            if (Driver != null)
            {
                Vector3 v = Driver.WorldVelocityUnity;
                if (v.magnitude >= MinTrackSpeed) return v.normalized;
            }
            return Target.forward;
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            Vector3 want = DesiredDirection();
            _dir = Vector3.Slerp(_dir, want, 1f - Mathf.Exp(-DirectionDamp * Time.deltaTime)).normalized;

            // Reference up: world up, unless the path is near-vertical (tumble/loop), then the aircraft's up
            // keeps the horizon from flipping.
            Vector3 up = Mathf.Abs(Vector3.Dot(_dir, Vector3.up)) > 0.9f ? Target.up : Vector3.up;

            Vector3 desired = Target.position - _dir * Distance + up * Height;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-PositionDamp * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(Target.position + _dir * 4f - transform.position, up);
        }

        public void SnapBehind()
        {
            if (Target == null)
            {
                return;
            }

            _dir = DesiredDirection();
            transform.position = Target.position - _dir * Distance + Vector3.up * Height;
        }
    }
}
