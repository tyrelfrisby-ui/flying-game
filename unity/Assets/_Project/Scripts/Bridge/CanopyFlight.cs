using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A departed canopy in the air (AirframeVisual.DetachCanopy): steps the Core <see cref="CanopyFreeBody"/> in Unity
    /// world axes — gravity down, the regional wind, ISA density — and puts the detached model part where the body is,
    /// turned as the body has turned since it left. It comes to rest on the surface and is removed after a minute.
    /// No Unity physics; the same ballistic integration as the egress debris.
    /// </summary>
    internal sealed class CanopyFlight : MonoBehaviour
    {
        public const float LifeSec = 60f;
        private CanopyFreeBody _body;
        private Quaternion _q0, _frame0Inv;
        private Vector3 _offsetLocal;
        private float _rest, _age;
        private bool _down;

        public void Init(CanopyFreeBody body, Vector3 comWorld, float restHeightM)
        {
            _body = body;
            _q0 = transform.rotation;
            _frame0Inv = Quaternion.Inverse(Quaternion.LookRotation(U(body.Forward), U(body.Up)));
            _offsetLocal = Quaternion.Inverse(_q0) * (transform.position - comWorld);
            _rest = restHeightM;
        }

        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > LifeSec) { Destroy(gameObject); return; }
            if (_body == null || _down || dt <= 0f) return;
            Vector3 p = U(_body.Position);
            Vector3 w = EgressAir.Wind(p);
            _body.Step(dt, EgressAir.Density(p.y), new Vec3(w.x, w.y, w.z), new Vec3(0, -EgressAir.G, 0));
            Vector3 com = U(_body.Position);
            Quaternion q = Quaternion.LookRotation(U(_body.Forward), U(_body.Up)) * _frame0Inv * _q0;
            float surf = EgressAir.SurfaceHeight(com);
            if (com.y - _rest <= surf)
            {
                com.y = surf + _rest;
                _down = true;
            }
            transform.SetPositionAndRotation(com + q * _offsetLocal, q);
        }
    }
}
