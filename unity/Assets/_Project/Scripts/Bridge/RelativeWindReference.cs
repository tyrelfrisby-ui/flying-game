using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE AIR MASS YOU ARE FLYING THROUGH, NOT THE GUST YOU ARE IN THIS INSTANT.
    ///
    /// Ty 2026-11-09, after flying 2026.1109.0408 at KSNA: "The jitter is only in relative wind flight path view which
    /// makes sense because the relative wind is swirling... the correct solution is to damp it for the flight path
    /// vector and for the viewpoints."
    ///
    /// Measured from his clip: the aeroplane sits rock steady in frame (centroid moves 0.05 px a frame) while the
    /// green flight-path marker swings about nine pixels at roughly 1 Hz. The chase camera sits behind the aeroplane
    /// ALONG that direction and looks back at it, so a wandering direction orbits the camera around the aircraft --
    /// the aeroplane stays pinned in the middle of the screen and the whole world sloshes around it. Nothing is
    /// shaking; the camera's reference is.
    ///
    /// The fix is NOT to damp the marker's direction, which would make it lag in a real manoeuvre too. It is to damp
    /// THE WIND. A gust lasting a fraction of a second is not a change in the air mass you are flying through, it is
    /// noise on top of it, so the air mass is low-passed and the aeroplane's own motion is left instantaneous:
    ///
    ///     wind = worldVelocity - airVelocity          (what the air is doing here, right now)
    ///     windSmoothed  &lt;- low pass, tau             (the air MASS)
    ///     reference = worldVelocity - windSmoothed    (the path through that air mass)
    ///
    /// So a pull-up or a roll moves the marker at once, because world velocity is untouched, while the swirl that was
    /// never part of where you are going is filtered out. It also keeps the owner's 2026-09-16 rule that the chase
    /// view is EXACTLY on the flight path: it still is -- on the path through the air mass, which is what that view
    /// has always meant.
    ///
    /// TAU is 1.5 s: the marker's wander measured near 1 Hz, and the air mass itself (mean wind, shear, a thermal's
    /// core) changes over many seconds, so this is slow enough to cut the swirl and fast enough that flying into
    /// genuinely different air still shows up almost at once. It costs no manoeuvre response at all, which is why it
    /// can afford to be this slow.
    /// </summary>
    public static class RelativeWindReference
    {
        public const float TauSec = 1.5f;

        private static Vector3 _wind;
        private static bool _have;
        private static int _frame = -1;

        /// <summary>Forget the smoothed air mass: a new flight, a teleport, a replay seek.</summary>
        public static void Reset() { _have = false; _wind = Vector3.zero; }

        /// <summary>
        /// The air-relative velocity to REFERENCE a view or a marker to, given this frame's true velocities. Advances
        /// the filter once per frame however many callers ask, so the marker and every camera share one answer.
        /// </summary>
        public static Vector3 AirVelocity(Vector3 worldVelocity, Vector3 airVelocity)
        {
            Vector3 wind = worldVelocity - airVelocity;
            if (!_have) { _wind = wind; _have = true; _frame = Time.frameCount; return worldVelocity - _wind; }
            if (Time.frameCount != _frame)
            {
                float dt = Mathf.Max(0f, Time.unscaledDeltaTime);
                if (dt > 0f) _wind = Vector3.Lerp(_wind, wind, 1f - Mathf.Exp(-dt / TauSec));
                _frame = Time.frameCount;
            }
            return worldVelocity - _wind;
        }
    }
}
