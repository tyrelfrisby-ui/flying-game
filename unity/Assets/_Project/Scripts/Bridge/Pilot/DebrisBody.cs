using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A free body shed during egress (jettisoned cockpit canopy, separated ejection seat): ballistic with
    /// quadratic drag in ISA air + the sim's wind, tumbling at a fixed angular rate, comes to rest on the
    /// terrain/water and is removed after <see cref="LifeSec"/>. Kinematics are integrated here (semi-implicit
    /// Euler, implicit drag) — no Unity physics.
    /// </summary>
    internal sealed class DebrisBody : MonoBehaviour
    {
        public Vector3 Velocity;                 // m/s, Unity world
        public Vector3 AngularVelocityRad;       // rad/s, body axes
        public float MassKg = 15f;
        public float CdA = 1.0f;                 // m²
        public float LifeSec = 25f;
        public float RestHeightM = 0.3f;         // centre height above the surface once down

        private float _age;
        private bool _down;

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > LifeSec) { Destroy(gameObject); return; }
            if (_down || dt <= 0f) return;

            Vector3 p = transform.position;
            float rho = EgressAir.Density(p.y);
            Vector3 vRel = Velocity - EgressAir.Wind(p);
            Velocity += Vector3.down * EgressAir.G * dt;
            Velocity += EgressAir.ImplicitDragDelta(vRel, rho, CdA, MassKg, dt);
            p += Velocity * dt;

            float surf = EgressAir.SurfaceHeight(p);
            if (p.y - RestHeightM <= surf)
            {
                p.y = surf + RestHeightM;
                Velocity = Vector3.zero;
                _down = true;
                // Settle flat on the surface (a canopy shell lands on its back; a seat on its side).
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 180f);
            }
            else
            {
                transform.rotation *= Quaternion.Euler(AngularVelocityRad * Mathf.Rad2Deg * dt);
            }
            transform.position = p;
        }
    }
}
