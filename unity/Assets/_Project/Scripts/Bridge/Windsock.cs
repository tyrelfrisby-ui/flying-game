using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A windsock (owner 2026-09-10): reads the live wind at its own position every frame and points DOWNWIND, drooping
    /// when the wind is light (a sock stands out straight at about 15 kt), with a little flutter. Orange with white
    /// bands. The cone is built by WorldBuilder; this only turns it.
    /// </summary>
    public sealed class Windsock : MonoBehaviour
    {
        public Transform Cone;                 // pivot at the ring, cone extends along +z (local)
        public const float StraightOutMs = 7.7f;   // ~15 kt
        private float _flutter;

        private void Update()
        {
            if (Cone == null) return;
            var w = Atmosphere.WindAtPosition(CoordinateMap.ToSim(transform.position));
            Vector3 wind = CoordinateMap.ToUnity(w);
            Vector3 horiz = new Vector3(wind.x, 0f, wind.z);
            float speed = horiz.magnitude;
            _flutter += Time.deltaTime * (1.5f + speed * 0.6f);
            Vector3 dir = speed > 0.05f ? horiz / speed : Cone.forward;
            // Droop: hangs 70° down in a calm, straight out by 15 kt.
            float droop = Mathf.Lerp(70f, 0f, Mathf.Clamp01(speed / StraightOutMs));
            float yawFlutter = Mathf.Sin(_flutter * 3.1f) * 4f * Mathf.Clamp01(speed / 4f);
            float pitchFlutter = Mathf.Sin(_flutter * 2.3f) * 3f * Mathf.Clamp01(speed / 4f);
            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
            Cone.rotation = look * Quaternion.Euler(droop + pitchFlutter, yawFlutter, 0f);
        }
    }
}
