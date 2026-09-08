using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The jettisoned cockpit canopy: a translucent half-ellipsoid shell sized to the airframe's "Canopy"
    /// part (or a generic hatch when the type has none), released from the cockpit with an UP + AFT kick.
    /// The airstream then does the rest — with ~1 m² of drag area on 15 kg it decelerates hard relative
    /// to the aircraft, so it visibly leaves up and aft, tumbling, then falls (<see cref="DebrisBody"/>).
    /// </summary>
    internal static class CanopyJettison
    {
        private static Mesh _dome;

        /// <summary>
        /// Spawn the shell. `canopyPart` is the airframe's Canopy child (may be null); its renderer is
        /// disabled by the caller so the cockpit reads as open afterwards.
        /// </summary>
        public static DebrisBody Spawn(FlightSimDriver driver, Transform canopyPart)
        {
            _dome ??= EgressAir.Dome(20, 6);
            Transform ac = driver.transform;

            Vector3 worldPos;
            Vector3 halfSize;        // (w/2, h/2, l/2) of the ellipsoid the shell is the top half of
            if (canopyPart != null)
            {
                worldPos = canopyPart.position;
                Vector3 s = canopyPart.localScale;   // sphere primitive: scale = full diameters (wid, hgt, len)
                halfSize = new Vector3(s.x * 0.5f, s.y * 0.5f, s.z * 0.5f);
            }
            else
            {
                worldPos = ac.TransformPoint(PilotEgress.CockpitLocal(driver));
                halfSize = new Vector3(0.45f, 0.25f, 0.8f);
            }

            var go = EgressAir.MeshObject("JettisonedCanopy", _dome,
                EgressAir.Transparent(new Color(0.25f, 0.4f, 0.55f, 0.55f)), null);
            go.transform.SetPositionAndRotation(worldPos, ac.rotation);
            go.transform.localScale = halfSize;

            var body = go.AddComponent<DebrisBody>();
            body.MassKg = 15f;
            body.CdA = Mathf.Clamp(0.9f * (halfSize.x * 2f) * (halfSize.z * 2f), 0.4f, 3f);   // a scoop broadside to the flow
            body.Velocity = driver.WorldVelocityUnity + ac.up * 6f - ac.forward * 2f;
            body.AngularVelocityRad = new Vector3(6f, 1.5f, 3.5f);   // pitches over backwards as it leaves
            body.RestHeightM = halfSize.y * 0.5f;
            body.LifeSec = 30f;
            return body;
        }
    }
}
