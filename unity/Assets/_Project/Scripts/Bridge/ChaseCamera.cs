using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Chase cam v1 (ARCHITECTURE.md): follows from behind with damped position so AoA and sideslip stay
    /// visible — the camera tracks where the aircraft is GOING (velocity-vector-aware lag via the damp),
    /// letting the nose visibly point away from the flight path.
    /// </summary>
    public sealed class ChaseCamera : MonoBehaviour
    {
        public Transform Target;
        public float Distance = 14f;
        public float Height = 3.5f;
        public float PositionDamp = 3.0f;

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            Vector3 desired = Target.position - Target.forward * Distance + Vector3.up * Height;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-PositionDamp * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(Target.position + Target.forward * 4f - transform.position, Vector3.up);
        }

        public void SnapBehind()
        {
            if (Target == null)
            {
                return;
            }

            transform.position = Target.position - Target.forward * Distance + Vector3.up * Height;
        }
    }
}
