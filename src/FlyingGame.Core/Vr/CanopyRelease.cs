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

    /// <summary>Something a departed canopy can run into, in the SAME frame the body is being stepped in. The
    /// aeroplane's own surfaces are <see cref="EgressClearance"/>, which is in the aeroplane's body frame, so a caller
    /// stepping the canopy in world axes (the Bridge) has to convert; one stepping it in the aeroplane's frame (the
    /// tail check) hands it straight over. Returns the name of whatever a sphere of <paramref name="radiusM"/> at
    /// <paramref name="at"/> is inside, or null.</summary>
    public interface ICanopyObstacle
    {
        string? Hit(Vec3 at, double radiusM);
    }

    /// <summary>An <see cref="EgressClearance"/> used directly, for a caller already in the aeroplane's body frame.</summary>
    public sealed class ClearanceObstacle : ICanopyObstacle
    {
        private readonly EgressClearance? _c;
        public ClearanceObstacle(EgressClearance? c) { _c = c; }
        public string? Hit(Vec3 at, double radiusM) => _c?.Hit(at, radiusM);
    }

    /// <summary>
    /// THE CANOPY ON ITS OWN, once it has left (hinges failed, or jettisoned): a rigid body of its own mass in its own
    /// airflow, in any frame the caller likes (the Bridge uses the world; the tail check the aeroplane's body frame) —
    /// give it gravity and wind in that frame.
    ///
    /// IT CAN HIT THE AEROPLANE IT JUST LEFT (Ty 2026-11-10: "if it hits the tail or other structure use a mechanical
    /// sound for impact and shatter the glass both audibly and visually"). Give <see cref="Step"/> an
    /// <see cref="ICanopyObstacle"/> and the flight is swept against it: the FIRST strike sets
    /// <see cref="Shattered"/>, <see cref="StruckWhat"/>, <see cref="StrikeSpeedMs"/> and the rest, once, and the
    /// caller plays the mechanical impact and breaks the glass. The surface it is already beside when it leaves (the
    /// Pitts' top wing over the front cockpit) is not a strike — pass it as <c>ignore</c>, the way
    /// <see cref="CanopyTailCheck"/> always has. What happens to the shell AFTER it shatters is the caller's: this
    /// goes on flying it as one body, because a quasi-steady shell model has nothing true to say about shards.
    ///
    ///   drag   broadside a hollow shell is Cd 1.2 on its plan area (E: between a cup's convex 0.4 and concave 1.4);
    ///          edge-on, Cd 0.4 on its arch's frontal area (as attached and open); sideways, the same open-shell 0.4
    ///          on its side projection, the half-ellipse ½π·a·c (added 2026-11-10 — the blend below never needed a
    ///          side force because nothing could turn the shell side-on to the flow).
    ///   lift   the bubble's own suction, ΔCp(shape) q A along its up, for the part of the flow that runs ALONG the
    ///          shell: (1 − (up · v̂)²), made perpendicular to the relative wind. So it first rises off the aeroplane.
    ///   tumble EMERGENT since 2026-11-10 (Ty: "Make sure departing wings and canopies use real aerodynamic[s] for
    ///          movement … real forces leading to tumbles and flutter"). It was KINEMATIC: the rate it left with plus
    ///          a <see cref="PitchOverRadS"/> pitch-over, HELD for ever, so a canopy that left at 40 kt and one that
    ///          left at 150 kt tumbled at the same rate and neither ever stopped. Now the three drag areas above go
    ///          through <see cref="Wreck.DebrisAero"/> — the flat-plate normal force at a centre of pressure that
    ///          walks with the angle, resolved into strips — and <see cref="Spin"/> is integrated out of the moment
    ///          that produces, through the shell's own inertia tensor (<see cref="ShellInertia"/>). The areas are
    ///          unchanged, so how it decelerates and where it goes is what it was; what is new is that the force acts
    ///          where the shape puts it, which is off the centre of mass, which is what makes it skid and tumble.
    ///
    /// HANDEDNESS, DELIBERATELY: this body works in whatever frame the caller hands it, and the Bridge hands it UNITY
    /// world axes, which are left-handed. The body triad below is built with cross products, so it is internally
    /// right-handed and the dynamics are self-consistent; the only consequence is that the visible sense of the
    /// tumble is MIRRORED in Unity. That is a decision and not an oversight: a tumble reflected is still a tumble,
    /// nothing here is chiral (the shell is symmetric left to right), and the alternative — converting to and from the
    /// sim's own frame on every step — buys nothing anyone can see. The same was already true of the kinematic spin
    /// this replaces.
    /// </summary>
    public sealed class CanopyFreeBody
    {
        public const double BroadsideCd = 1.2;
        /// <summary>The pitch-over the hinge swing hands it as it goes, rad/s (E: the drag on its high aft end). An
        /// INITIAL CONDITION — the rate it departs with — and no longer the rate it keeps: the air decides the rest.</summary>
        public const double PitchOverRadS = 3.0;
        public Vec3 Position, Velocity, Up, Forward;
        /// <summary>Angular velocity in the caller's frame, rad/s. State, not a setting: given as the departure rate
        /// and integrated from the aerodynamic moment thereafter.</summary>
        public Vec3 Spin;
        public readonly double MassKg, PlanAreaM2, FrontalAreaM2, SideAreaM2, NetCp;
        /// <summary>Principal moments about the shell's CoM, body axes (x along <see cref="Forward"/>, y across,
        /// z down), kg·m².</summary>
        public readonly Vec3 Inertia;
        /// <summary>The shell's box, half-extents in body axes, and where its centre sits relative to the CoM.</summary>
        public readonly Vec3 HalfM, BoxCentreFromCom;
        public double TimeSec { get; private set; }
        /// <summary>The last aerodynamic moment about the CoM, caller's frame, N·m (probes).</summary>
        public Vec3 LastMomentNm { get; private set; }

        /// <summary>The sphere the strike test uses, m: the same one <see cref="CanopyTailCheck"/> has always used —
        /// half the greater of the shell's height and half its span.</summary>
        public readonly double RadiusM;
        /// <summary>It has struck the aeroplane and the glass has broken. Set once, never cleared.</summary>
        public bool Shattered { get; private set; }
        /// <summary>What it struck (the surface's name), or null.</summary>
        public string? StruckWhat { get; private set; }
        /// <summary>Where it struck, caller's frame.</summary>
        public Vec3 StrikeAt { get; private set; }
        /// <summary>Its speed through the air at the strike, m/s — how hard the impact sounds.</summary>
        public double StrikeSpeedMs { get; private set; }
        /// <summary>Its own rate at the strike, rad/s.</summary>
        public double StrikeSpinRadS { get; private set; }
        /// <summary>When it struck, s from release (NaN if it has not).</summary>
        public double StrikeAtSec { get; private set; } = double.NaN;

        public CanopyFreeBody(CanopyDynamics c, Vec3 position, Vec3 velocity, Vec3 up, Vec3 forward, Vec3 spin)
        {
            MassKg = c.Spec.MassKg; PlanAreaM2 = c.Bubble.PlanAreaM2; FrontalAreaM2 = c.Bubble.FrontalAreaM2; NetCp = c.Bubble.NetCp;
            double a = 0.5 * c.Spec.LengthM, b = 0.5 * c.Spec.SpanM, hgt = c.Spec.HeightM;
            // The side projection of a half-ellipsoid: the half-ellipse of its length against its height.
            SideAreaM2 = 0.5 * Math.PI * a * hgt;
            HalfM = new Vec3(a, b, 0.5 * hgt);
            // Body z is DOWN, the shell arches UP from its rim: its box centre is half its height above the rim and
            // its mass centroid is Bubble.ComHeightM above it, so the box sits below the CoM by the difference.
            BoxCentreFromCom = new Vec3(0, 0, c.Bubble.ComHeightM - 0.5 * hgt);
            Inertia = ShellInertia(a, b, hgt, MassKg, c.Bubble.ComHeightM);
            RadiusM = 0.5 * Math.Max(c.Spec.HeightM, 0.5 * c.Spec.SpanM);
            Position = position; Velocity = velocity; Up = up.Normalized(); Forward = forward.Normalized(); Spin = spin;
        }

        /// <summary>
        /// The principal moments of a THIN UNIFORM SHELL over the upper half of an ellipsoid of semi-axes
        /// (<paramref name="a"/> fore-and-aft, <paramref name="b"/> across, <paramref name="heightM"/> up), about its
        /// own centroid, kg·m². Integrated over the surface rather than approximated: the surface element of
        /// p(θ,φ) = (a sinθ cosφ, b sinθ sinφ, c cosθ) is |∂p/∂θ × ∂p/∂φ|, and the sums are second moments weighted
        /// by it, then moved to the centroid by the parallel-axis theorem. Checked against the closed form for a
        /// hemispherical shell, I = ⅔mR² about a diameter through the sphere's centre.
        /// </summary>
        public static Vec3 ShellInertia(double a, double b, double heightM, double massKg, double comHeightM)
        {
            double c = Math.Max(1e-6, heightM);
            a = Math.Max(1e-6, a); b = Math.Max(1e-6, b);
            const int nt = 96, np = 96;
            double area = 0, ixx = 0, iyy = 0, izz = 0;
            for (int i = 0; i < nt; i++)
            {
                double th = (i + 0.5) * (Math.PI / 2) / nt, st = Math.Sin(th), ct = Math.Cos(th);
                for (int j = 0; j < np; j++)
                {
                    double ph = (j + 0.5) * (2 * Math.PI) / np, sp = Math.Sin(ph), cp = Math.Cos(ph);
                    // dp/dth x dp/dph, magnitude.
                    double ex = b * c * st * st * cp, ey = a * c * st * st * sp, ez = a * b * st * ct;
                    double dA = Math.Sqrt(ex * ex + ey * ey + ez * ez) * ((Math.PI / 2) / nt) * ((2 * Math.PI) / np);
                    double x = a * st * cp, y = b * st * sp, z = c * ct;
                    area += dA;
                    ixx += dA * (y * y + z * z);
                    iyy += dA * (x * x + z * z);
                    izz += dA * (x * x + y * y);
                }
            }
            if (area <= 0) return new Vec3(1e-3, 1e-3, 1e-3);
            double k = massKg / area;
            // About the sphere's centre (the rim plane); shift to the centroid, which sits comHeightM above it.
            double d2 = comHeightM * comHeightM;
            return new Vec3(Math.Max(1e-4, k * ixx - massKg * d2),
                            Math.Max(1e-4, k * iyy - massKg * d2),
                            Math.Max(1e-4, k * izz));
        }

        /// <summary>Drag area, m², with the relative wind along <paramref name="vHat"/>. Unchanged: these are the
        /// numbers <see cref="Wreck.DebrisAero"/> is now given per axis, and the blend is kept because it is what the
        /// shell's broadside and edge-on figures were written down as.</summary>
        public double DragAreaM2(Vec3 vHat)
        {
            double edge = CanopyDynamics.OpenShellCd * FrontalAreaM2, broad = BroadsideCd * PlanAreaM2;
            return edge + (broad - edge) * Math.Abs(Up.Dot(vHat));
        }

        /// <summary>Cd·A along body x (edge-on), y (sideways) and z (broadside), m² — the three written-down areas.</summary>
        public Vec3 AxisDragAreaM2 => new(CanopyDynamics.OpenShellCd * FrontalAreaM2,
                                          CanopyDynamics.OpenShellCd * SideAreaM2,
                                          BroadsideCd * PlanAreaM2);

        /// <summary>The body triad in the caller's frame: x along <see cref="Forward"/>, z DOWN (against
        /// <see cref="Up"/>), y the cross product — right-handed by construction, whatever the caller's frame is.</summary>
        public (Vec3 X, Vec3 Y, Vec3 Z) Axes()
        {
            Vec3 z = -Up;
            Vec3 x = Forward - z * z.Dot(Forward);
            double xl = x.Length;
            x = xl > 1e-9 ? x * (1.0 / xl) : Forward;
            Vec3 y = Vec3.Cross(z, x);
            double yl = y.Length;
            y = yl > 1e-9 ? y * (1.0 / yl) : Vec3.Cross(z, new Vec3(z.Z, z.X, z.Y)).Normalized();
            return (x, y, z);
        }

        public void Step(double dt, double rho, Vec3 wind, Vec3 gravity) => Step(dt, rho, wind, gravity, null, null);

        /// <param name="obstacle">The aeroplane it just left, in this same frame, or null for no strike test.</param>
        /// <param name="ignore">A surface it is already touching at release, which is not a strike.</param>
        public void Step(double dt, double rho, Vec3 wind, Vec3 gravity, ICanopyObstacle? obstacle, string? ignore)
        {
            if (dt <= 0.0) return;
            // Sub-stepped on the attitude as well as the clock: the moment is strongly nonlinear in the angle, so a
            // shell that has been set spinning hard needs finer steps than one that is falling.
            int n = Math.Max(1, (int)Math.Ceiling(dt / 0.005));
            n = Math.Max(n, Math.Min(8, (int)Math.Ceiling(Spin.Length * dt / 0.05)));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                (Vec3 bx, Vec3 by, Vec3 bz) = Axes();
                Vec3 vRel = Velocity - wind;
                Vec3 a = gravity, mWorld = Vec3.Zero;
                if (vRel.Length > 1e-3)
                {
                    // The shell's air, through DebrisAero: ITS three measured drag areas, the plate normal force, and
                    // a centre of pressure that walks along whichever way the flow crosses it. Body frame in, body
                    // frame out; the box stands for where the force acts, not for how big it is.
                    Vec3 vBody = new(vRel.Dot(bx), vRel.Dot(by), vRel.Dot(bz));
                    Vec3 wBody = new(Spin.Dot(bx), Spin.Dot(by), Spin.Dot(bz));
                    var st = new RigidBodyState(Vec3.Zero, Quat.Identity, vBody, wBody);
                    (Vec3 fB, Vec3 mB) = Wreck.DebrisAero.Forces(BoxCentreFromCom, HalfM, AxisDragAreaM2, 1.0, st, rho, Vec3.Zero);
                    a += (bx * fB.X + by * fB.Y + bz * fB.Z) * (1.0 / MassKg);
                    mWorld = bx * mB.X + by * mB.Y + bz * mB.Z;
                    // THE BUBBLE'S OWN SUCTION, kept exactly as it was — it is what lifts the canopy off the
                    // aeroplane, and a flat plate has no such term. It acts on the plan, so at the box's centre.
                    double v = vRel.Length;
                    Vec3 vHat = vRel * (1.0 / v);
                    double q = 0.5 * rho * v * v;
                    double cUp = Up.Dot(vHat);
                    Vec3 lift = Up - vHat * cUp;
                    double ll = lift.Length;
                    if (ll > 1e-6)
                    {
                        Vec3 fL = lift * (q * PlanAreaM2 * NetCp * (1.0 - cUp * cUp) / ll);
                        a += fL * (1.0 / MassKg);
                        Vec3 rW = bx * BoxCentreFromCom.X + by * BoxCentreFromCom.Y + bz * BoxCentreFromCom.Z;
                        mWorld += Vec3.Cross(rW, fL);
                    }
                }
                LastMomentNm = mWorld;
                Velocity += a * h;
                Position += Velocity * h;
                // Euler's equations about the CoM, in the body triad, then back out to the caller's frame.
                Vec3 wb = new(Spin.Dot(bx), Spin.Dot(by), Spin.Dot(bz));
                Vec3 mb = new(mWorld.Dot(bx), mWorld.Dot(by), mWorld.Dot(bz));
                Vec3 iw = new(Inertia.X * wb.X, Inertia.Y * wb.Y, Inertia.Z * wb.Z);
                Vec3 gyro = Vec3.Cross(wb, iw);
                Vec3 wd = new((mb.X - gyro.X) / Inertia.X, (mb.Y - gyro.Y) / Inertia.Y, (mb.Z - gyro.Z) / Inertia.Z);
                wb += wd * h;
                Spin = bx * wb.X + by * wb.Y + bz * wb.Z;
                double w = Spin.Length;
                if (w > 1e-9)
                {
                    Vec3 k = Spin * (1.0 / w);
                    Up = Rotate(Up, k, w * h).Normalized();
                    Forward = Rotate(Forward, k, w * h).Normalized();
                }
                TimeSec += h;
                // Did it just run into the aeroplane? Tested after the move, so the first sub-step that puts it inside
                // a surface is the strike, and only the first: the glass breaks once.
                if (obstacle != null && !Shattered)
                {
                    string? what = obstacle.Hit(Position, RadiusM);
                    if (what != null && what != ignore)
                    {
                        Shattered = true;
                        StruckWhat = what;
                        StrikeAt = Position;
                        StrikeSpeedMs = (Velocity - wind).Length;
                        StrikeSpinRadS = Spin.Length;
                        StrikeAtSec = TimeSec;
                    }
                }
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
    ///
    /// SINCE 2026-11-10 this is the PREDICTION of the same strike the flight itself now makes: it runs
    /// <see cref="CanopyFreeBody.Step(double, double, Vec3, Vec3, ICanopyObstacle?, string?)"/> with
    /// the aeroplane as the obstacle and reads the body's own <see cref="CanopyFreeBody.StruckWhat"/>, so a warning
    /// here and a shatter in the air cannot disagree about whether the canopy hits the tail. What this one still does
    /// NOT do is damage anything: it is a look ahead, for the Bridge's log.
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
            Vec3 atTail = b.Position;
            bool passed = false;
            // A surface it is already beside when it leaves (the Pitts' top wing over the front cockpit) is not a strike.
            var obstacle = new ClearanceObstacle(clear);
            string? startedBy = obstacle.Hit(b.Position, b.RadiusM);
            for (int i = 0; i < 600; i++)
            {
                b.Step(0.005, rho, wind, g, obstacle, startedBy);
                if (!passed && b.Position.X <= tailX) { passed = true; atTail = b.Position; }
                if (b.StruckWhat != null) return new Result(b.StruckWhat, b.StrikeAtSec, b.StrikeAt);
                if (b.Position.X < tailX - 2.0) break;
            }
            return new Result(null, b.TimeSec, passed ? atTail : b.Position);
        }
    }
}
