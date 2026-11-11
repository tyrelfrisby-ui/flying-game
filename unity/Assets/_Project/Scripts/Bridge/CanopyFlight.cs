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
    ///
    /// HOW HARD IT HITS NOW DECIDES WHETHER IT BREAKS (2026-11-11). It used to be that ANY contact at ANY speed
    /// shattered the glass, so a shell leaning on the turtledeck of a parked aeroplane broke exactly as hard as one
    /// that caught the fin at 60 m/s — which is what Ty flew into: "the canopy shattered even though it didn't hit
    /// anything". The test is the CLOSING speed against this aeroplane (<see cref="PlaneObstacle.VelocityAt"/>, not
    /// the shell's speed through the air) against the type's own glazing
    /// (<see cref="FlyingGame.Core.Glazing.ShatterClosingMs"/>), and a touch below it leaves the glass whole.
    /// </summary>
    internal sealed class CanopyFlight : MonoBehaviour
    {
        public const float LifeSec = 60f;
        private CanopyFreeBody _body;
        private Quaternion _q0, _frame0Inv;
        private Vector3 _offsetLocal;
        private float _rest, _age;
        private bool _down, _struck, _touched;
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
            private Matrix4x4 _was;
            private float _dt;
            public PlaneObstacle(FlyingGame.Core.Vr.EgressClearance clear, Transform plane)
            {
                _clear = clear; _plane = plane;
                _was = plane != null ? plane.localToWorldMatrix : Matrix4x4.identity;
            }

            /// <summary>Once a frame BEFORE the body is stepped: how long since the pose in <c>_was</c> was taken.
            /// <c>_was</c> itself is last frame's, kept by <see cref="EndFrame"/>.</summary>
            public void BeginFrame(float dt) { _dt = dt; }

            /// <summary>And once AFTER: this frame's pose becomes the one the next frame differences against.</summary>
            public void EndFrame() { if (_plane != null) _was = _plane.localToWorldMatrix; }

            public string Hit(Vec3 at, double radiusM)
            {
                if (_clear == null || _plane == null) return null;
                Vector3 local = _plane.InverseTransformPoint(new Vector3((float)at.X, (float)at.Y, (float)at.Z));
                return _clear.Hit(CoordinateMap.ToSim(local), radiusM);
            }

            /// <summary>
            /// THE AEROPLANE'S OWN VELOCITY AT THAT POINT, world axes — what the shell is closing on it at, which is
            /// what breaks the glass (Core CanopyFreeBody.ShatterSpeedMs). Finite-differenced through the point's
            /// place on the airframe rather than taken from the CG, so a rolling aeroplane's fin is moving as fast as
            /// it really is. With no previous pose (dt 0) it answers zero, which is the old behaviour: the shell's
            /// speed through the world stands in, and in the air that is far above any threshold anyway.
            /// </summary>
            public Vec3 VelocityAt(Vec3 at)
            {
                if (_plane == null || _dt <= 1e-6f) return Vec3.Zero;
                var w = new Vector3((float)at.X, (float)at.Y, (float)at.Z);
                Vector3 local = _plane.InverseTransformPoint(w);
                Vector3 v = (w - _was.MultiplyPoint3x4(local)) / _dt;
                return new Vec3(v.x, v.y, v.z);
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
            if (_obstacle != null) _obstacle.BeginFrame(dt);
            _body.Step(dt, EgressAir.Density(p.y), new Vec3(w.x, w.y, w.z), new Vec3(0, -EgressAir.G, 0), _obstacle, _ignore);
            if (_obstacle != null) _obstacle.EndFrame();
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
            else if (_body.Touched && !_touched) Touched();
        }

        /// <summary>It has just hit the aeroplane hard enough: the mechanical sound and the glass, once. Done after the
        /// move, so the shell is drawn where it struck before anything is spawned at that point.</summary>
        private void Struck()
        {
            _struck = true;
            // The IMPACT's own speed drives the sound and the shards, not the shell's speed through the air: a shell
            // brushing the fin at 150 kt is closing on it at nothing, and should not sound like a sledgehammer.
            float closing = (float)_body.StrikeClosingMs;
            _audio?.CanopyImpact(closing);
            _glass?.ShatterDeparted(transform, U(_body.StrikeAt), U(_body.Velocity), closing);
            Debug.Log($"[Canopy] struck {_body.StruckWhat} at {_body.StrikeAtSec:0.00} s, closing {closing:0.0} m/s " +
                      $"(threshold {_body.ShatterSpeedMs:0.0}), {_body.StrikeSpeedMs:0.0} m/s through the air, " +
                      $"{_body.StrikeSpinRadS:0.0} rad/s");
        }

        /// <summary>
        /// IT HAS MET THE AEROPLANE AND NOT BROKEN — below <see cref="CanopyFreeBody.ShatterSpeedMs"/> of closing
        /// speed. A canopy is tough plastic (Ty 2026-11-05: it "breaks/shatters ONLY if it takes a hard enough DIRECT
        /// impact"), so nothing is broken and nothing is heard.
        ///
        /// WHETHER IT STAYS THERE depends on whether there is any air to carry it off again: lying on the turtledeck
        /// of an aeroplane that is barely moving, it has nowhere to go, so it comes to rest where it is. In flight it
        /// has a slipstream and goes on falling back past the tail, which is what the drag model is for.
        /// </summary>
        private void Touched()
        {
            _touched = true;
            bool stays = _body.Velocity.Length < _body.ShatterSpeedMs;
            if (stays) _down = true;
            Debug.Log($"[Canopy] touched {_body.TouchedWhat} at {_body.TouchAtSec:0.00} s, closing {_body.TouchClosingMs:0.0} m/s " +
                      $"— under the {_body.ShatterSpeedMs:0.0} m/s that breaks it, so the glass is whole" +
                      (stays ? "; it comes to rest on the aeroplane" : ""));
        }
    }
}
