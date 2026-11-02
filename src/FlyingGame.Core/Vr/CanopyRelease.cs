using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE JETTISON PULL (Ty, 2026-11-02 12:38: the Pitts' red knob "pulls the hinge pins and the canopy falls away at ANY
    /// speed"; the Extra's CanopyRelease_F/R emergency pulls on the right sill, treated the same way). Grabbed and pulled
    /// along its own axis:
    ///
    ///   breakout  the first <see cref="BreakoutM"/> of hand travel moves nothing: the safety wire / detent. A controller
    ///             cannot measure force, so the knob's pull force (about 90 N = 20 lbf, E) is stood for by that travel,
    ///             like the ball latch's detent.
    ///   travel    then the knob follows the hand along its axis, 0 .. <see cref="TravelM"/>
    ///   fires     at <see cref="FireAt"/> of the travel the pins come out: ONE-WAY. The knob stays out and the canopy
    ///             cannot be un-jettisoned (until the aeroplane is reset).
    ///   short     let go before it fires and its spring returns it home: nothing happened.
    ///
    /// Travel: Pitts 38 mm (1.5 in, E); Extra 40 mm (Codex's rig for CanopyRelease_F/R). Breakout 8 mm and fire at 90 %
    /// are my estimates (questions for Ty). The rig's `canopy_jettison` = <see cref="Pulled01"/>.
    /// </summary>
    public sealed class JettisonPull
    {
        public const double BreakoutM = 0.008, FireAt = 0.9, PullForceN = 90.0;
        public readonly double TravelM;
        public double PositionM { get; private set; }
        public bool Fired { get; private set; }
        public bool Held { get; private set; }
        public double Pulled01 => PositionM / TravelM;

        private Vec3 _grabHand, _axis;
        private double _grabPos;

        public JettisonPull(double travelM) { TravelM = travelM; }

        /// <summary>The type's pull, or null. The Pitts has its knob whether or not the model draws it (Ty confirmed it;
        /// it falls back to a proxy); the Extra has one only where its model provides the release
        /// (<paramref name="modelHasPull"/>: CanopyRelease_F/R); no other type has one.</summary>
        public static JettisonPull? For(string aircraftId, bool modelHasPull)
        {
            string id = aircraftId ?? "";
            if (id.StartsWith("pitts")) return new JettisonPull(0.038);
            if (id.StartsWith("extra") && modelHasPull) return new JettisonPull(0.040);
            return null;
        }

        public void Reset() { PositionM = 0.0; Fired = false; Held = false; }

        /// <summary>The hand closes on the knob; <paramref name="pullAxis"/> is the way it pulls (unit, cockpit frame).</summary>
        public void Grab(Vec3 hand, Vec3 pullAxis)
        {
            Held = true;
            _grabHand = hand;
            double l = pullAxis.Length;
            _axis = l > 1e-9 ? pullAxis * (1.0 / l) : new Vec3(-1, 0, 0);
            _grabPos = PositionM;
        }

        /// <summary>Let go: an unfired knob springs home; a fired one stays out.</summary>
        public void Release()
        {
            Held = false;
            if (!Fired) PositionM = 0.0;
        }

        /// <summary>One frame of a held knob. Returns true on the frame it fires.</summary>
        public bool Follow(Vec3 hand)
        {
            if (!Held || Fired) return false;
            double d = (hand - _grabHand).Dot(_axis);
            // Out of the detent only once the hand has pulled through the breakout from home.
            double travel = _grabPos > 0.0 ? _grabPos + d : d - BreakoutM;
            PositionM = Math.Clamp(travel, 0.0, TravelM);
            if (PositionM < FireAt * TravelM) return false;
            PositionM = TravelM;
            Fired = true;
            return true;
        }
    }

    /// <summary>
    /// THE CANOPY ON ITS OWN, once it has left (hinges failed, or jettisoned): a rigid body of its own mass in its own
    /// airflow, in any frame the caller likes (the Bridge uses the world; the tail check the aeroplane's body frame) —
    /// give it gravity and wind in that frame.
    ///
    ///   drag   broadside a hollow shell is Cd 1.2 on its plan area (E: between a cup's convex 0.4 and concave 1.4);
    ///          edge-on, Cd 0.4 on its arch's frontal area (as attached and open). Blended by |up · v̂|.
    ///   lift   the bubble's own suction, ΔCp(shape) q A along its up, for the part of the flow that runs ALONG the
    ///          shell: (1 − (up · v̂)²), made perpendicular to the relative wind. So it first rises off the aeroplane.
    ///   tumble kinematic: the rate it left with (the hinge swing) plus a pitch-over (E 3 rad/s, the drag on its high aft
    ///          end), held; the forces follow its orientation, which the tumble keeps changing.
    /// </summary>
    public sealed class CanopyFreeBody
    {
        public const double BroadsideCd = 1.2, PitchOverRadS = 3.0;
        public Vec3 Position, Velocity, Up, Forward, Spin;
        public readonly double MassKg, PlanAreaM2, FrontalAreaM2, NetCp;
        public double TimeSec { get; private set; }

        public CanopyFreeBody(CanopyDynamics c, Vec3 position, Vec3 velocity, Vec3 up, Vec3 forward, Vec3 spin)
        {
            MassKg = c.Spec.MassKg; PlanAreaM2 = c.Bubble.PlanAreaM2; FrontalAreaM2 = c.Bubble.FrontalAreaM2; NetCp = c.Bubble.NetCp;
            Position = position; Velocity = velocity; Up = up.Normalized(); Forward = forward.Normalized(); Spin = spin;
        }

        /// <summary>Drag area, m², with the relative wind along <paramref name="vHat"/>.</summary>
        public double DragAreaM2(Vec3 vHat)
        {
            double edge = CanopyDynamics.OpenShellCd * FrontalAreaM2, broad = BroadsideCd * PlanAreaM2;
            return edge + (broad - edge) * Math.Abs(Up.Dot(vHat));
        }

        public void Step(double dt, double rho, Vec3 wind, Vec3 gravity)
        {
            if (dt <= 0.0) return;
            int n = Math.Max(1, (int)Math.Ceiling(dt / 0.005));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Vec3 vRel = Velocity - wind;
                double v = vRel.Length;
                Vec3 a = gravity;
                if (v > 1e-3)
                {
                    Vec3 vHat = vRel * (1.0 / v);
                    double q = 0.5 * rho * v * v;
                    a += vHat * (-q * DragAreaM2(vHat) / MassKg);
                    double c = Up.Dot(vHat);
                    Vec3 lift = Up - vHat * c;
                    double ll = lift.Length;
                    if (ll > 1e-6) a += lift * (q * PlanAreaM2 * NetCp * (1.0 - c * c) / (ll * MassKg));
                }
                Velocity += a * h;
                Position += Velocity * h;
                double w = Spin.Length;
                if (w > 1e-9)
                {
                    Vec3 k = Spin * (1.0 / w);
                    Up = Rotate(Up, k, w * h).Normalized();
                    Forward = Rotate(Forward, k, w * h).Normalized();
                }
                TimeSec += h;
            }
        }

        /// <summary>Rodrigues: v turned by angle about the unit axis k.</summary>
        public static Vec3 Rotate(Vec3 v, Vec3 k, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return v * c + Vec3.Cross(k, v) * s + k * (k.Dot(v) * (1.0 - c));
        }
    }

    /// <summary>
    /// WILL THE DEPARTED CANOPY HIT THE TAIL? Its path relative to the aeroplane, assuming the aeroplane flies on straight
    /// and level at <c>tas</c>: in the aeroplane's body frame (x forward, y right, z DOWN, the config's) that is a body
    /// released into air moving at (−tas, 0, 0) under true gravity (the aeroplane's own lift holds IT up). Swept against
    /// the tail and wing boxes (Core <see cref="EgressClearance"/>) for 3 s or until it is 2 m behind the last of them;
    /// a surface it is already touching at release is ignored (it starts there).
    /// Reported, not modelled: the Bridge logs a warning; nothing is damaged.
    /// </summary>
    public static class CanopyTailCheck
    {
        public readonly struct Result
        {
            public readonly string? Surface;
            public readonly double TimeSec;
            /// <summary>Where it was (body frame) when it passed the tail's leading station, or when it hit.</summary>
            public readonly Vec3 AtTail;
            public Result(string? s, double t, Vec3 at) { Surface = s; TimeSec = t; AtTail = at; }
        }

        public static Result Predict(CanopyDynamics c, Vec3 comBody, Vec3 relVelBody, Vec3 upBody, double tasMs, double rho,
                                     EgressClearance? clear)
        {
            var b = new CanopyFreeBody(c, comBody, relVelBody, upBody, new Vec3(1, 0, 0), new Vec3(0, CanopyFreeBody.PitchOverRadS, 0));
            double tailX = double.MaxValue;
            if (clear != null) foreach (EgressClearance.Box box in clear.Boxes) tailX = Math.Min(tailX, box.Min.X);
            if (tailX == double.MaxValue) tailX = comBody.X - 5.0;
            Vec3 wind = new(-tasMs, 0, 0), g = new(0, 0, 9.80665);
            double radius = 0.5 * Math.Max(c.Spec.HeightM, 0.5 * c.Spec.SpanM);
            Vec3 atTail = b.Position;
            bool passed = false;
            // A surface it is already beside when it leaves (the Pitts' top wing over the front cockpit) is not a strike.
            string? startedBy = clear?.Hit(b.Position, radius);
            for (int i = 0; i < 600; i++)
            {
                b.Step(0.005, rho, wind, g);
                if (!passed && b.Position.X <= tailX) { passed = true; atTail = b.Position; }
                string? hit = clear?.Hit(b.Position, radius);
                if (hit != null && hit != startedBy) return new Result(hit, b.TimeSec, b.Position);
                if (b.Position.X < tailX - 2.0) break;
            }
            return new Result(null, b.TimeSec, passed ? atTail : b.Position);
        }
    }
}
