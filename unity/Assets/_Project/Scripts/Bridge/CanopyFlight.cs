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
    ///
    /// IT CAN HIT THE AEROPLANE IT JUST LEFT. Ty 2026-11-10: "if it hits the tail or other structure use a mechanical
    /// sound for impact and shatter the glass both audibly and visually". The Core body has always been able to sweep
    /// its flight against an obstacle — <see cref="CanopyTailCheck"/> uses it to predict the strike before the canopy
    /// goes — but nothing handed it one in the game, so the one place the strike actually MATTERS was the one place it
    /// could not happen. <see cref="PlaneObstacle"/> below is that obstacle; on the first strike this plays
    /// <see cref="FlightAudio.CanopyImpact"/> and breaks the drawn glass through
    /// <see cref="GlassShatter.ShatterDeparted"/>. After that it goes on flying as one body, because a quasi-steady
    /// shell model has nothing true to say about shards.
    /// </summary>
    internal sealed class CanopyFlight : MonoBehaviour
    {
        public const float LifeSec = 60f;
        private CanopyFreeBody _body;
        private Quaternion _q0, _frame0Inv;
        private Vector3 _offsetLocal;
        private float _rest, _age;
        private bool _down, _struck;
        private PlaneObstacle _obstacle;
        private string _ignore;
        private FlightAudio _audio;
        private GlassShatter _glass;

        /// <summary>
        /// The departed shell, and what it can run into: <paramref name="clear"/> is the aeroplane's own surfaces in its BODY
        /// frame (Core EgressClearance) and <paramref name="plane"/> is the transform that frame belongs to, read every
        /// step so a manoeuvring aeroplane is where it really is rather than where it was when the canopy left.
        /// Pass them null and the flight is exactly what it was: no strike, no sound, no shatter.
        /// </summary>
        public void Init(CanopyFreeBody body, Vector3 comWorld, float restHeightM,
                         FlyingGame.Core.Vr.EgressClearance clear, Transform plane, FlightAudio audio, GlassShatter glass)
        {
            _body = body;
            _q0 = transform.rotation;
            _frame0Inv = Quaternion.Inverse(Quaternion.LookRotation(U(body.Forward), U(body.Up)));
            _offsetLocal = Quaternion.Inverse(_q0) * (transform.position - comWorld);
            _rest = restHeightM;
            _audio = audio;
            _glass = glass;
            if (clear != null && plane != null)
            {
                _obstacle = new PlaneObstacle(clear, plane);
                // THE SURFACE IT IS ALREADY BESIDE IS NOT A STRIKE — the Pitts' top wing sits right over the front
                // cockpit, so the canopy starts inside that box and would "hit" it on its first sub-step. The same
                // exclusion CanopyTailCheck has always made, made the same way: whatever it is touching at release.
                _ignore = _obstacle.Hit(body.Position, body.RadiusM);
            }
        }

        private static Vector3 U(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>
        /// The aeroplane as something to run into, for a body being stepped in WORLD axes. Core's EgressClearance boxes
        /// are in the aeroplane's body frame (x nose, y right, z down — the config's), so each test converts: world →
        /// the aeroplane's current Unity-local → the sim's body axes through <see cref="CoordinateMap"/>. Reading the
        /// transform every call rather than caching a matrix is the point: the aeroplane is flying while the canopy
        /// falls behind it, and whether the shell clears the tail depends on what the aeroplane does in that second.
        /// </summary>
        private sealed class PlaneObstacle : ICanopyObstacle
        {
            private readonly FlyingGame.Core.Vr.EgressClearance _clear;
            private readonly Transform _plane;
            public PlaneObstacle(FlyingGame.Core.Vr.EgressClearance clear, Transform plane) { _clear = clear; _plane = plane; }

            public string Hit(Vec3 at, double radiusM)
            {
                if (_clear == null || _plane == null) return null;
                Vector3 local = _plane.InverseTransformPoint(new Vector3((float)at.X, (float)at.Y, (float)at.Z));
                return _clear.Hit(CoordinateMap.ToSim(local), radiusM);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > LifeSec) { Destroy(gameObject); return; }
            if (_body == null || _down || dt <= 0f) return;
            Vector3 p = U(_body.Position);
            Vector3 w = EgressAir.Wind(p);
            _body.Step(dt, EgressAir.Density(p.y), new Vec3(w.x, w.y, w.z), new Vec3(0, -EgressAir.G, 0), _obstacle, _ignore);
            Vector3 com = U(_body.Position);
            Quaternion q = Quaternion.LookRotation(U(_body.Forward), U(_body.Up)) * _frame0Inv * _q0;
            float surf = EgressAir.SurfaceHeight(com);
            if (com.y - _rest <= surf)
            {
                com.y = surf + _rest;
                _down = true;
            }
            transform.SetPositionAndRotation(com + q * _offsetLocal, q);
            if (_body.Shattered && !_struck) Struck();
        }

        /// <summary>It has just hit the aeroplane: the mechanical sound and the glass, once. Done after the move, so the
        /// shell is drawn where it struck before anything is spawned at that point.</summary>
        private void Struck()
        {
            _struck = true;
            float speed = (float)_body.StrikeSpeedMs;
            _audio?.CanopyImpact(speed);
            _glass?.ShatterDeparted(transform, U(_body.StrikeAt), U(_body.Velocity), speed);
            Debug.Log($"[Canopy] struck {_body.StruckWhat} at {_body.StrikeAtSec:0.00} s, {speed:0.0} m/s, {_body.StrikeSpinRadS:0.0} rad/s");
        }
    }
}
