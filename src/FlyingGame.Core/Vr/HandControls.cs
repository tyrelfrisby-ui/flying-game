using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE STICK, AS A HAND MOVES IT: pure geometry about the stick's real hinges. Cockpit frame (<see cref="HandPose"/>:
    /// +x right, +y up, +z toward the nose).
    ///
    /// GIMBAL ORDER. Every stick in the fleet is a roll gimbal on a fore-aft torque tube with the pitch yoke riding on
    /// it (the Extra's rig: StickRoll_* is the parent, Stick_* the child; the Great Lakes' and the 2-33's Stick_* ride on
    /// their TorqueTube; the Cub's Stick_Rear on Stick_Rear_Roll). So the ROLL angle φ is outer and the PITCH angle θ
    /// inner, and the grip is
    ///
    ///   grip(φ, θ) = Rᵣ(φ) · Rₚ(θ) · grip₀      Rᵣ about the roll hinge (axis aᵣ through pᵣ), Rₚ about the pitch hinge
    ///                                            at rest (axis aₚ through pₚ), grip₀ the grip with both at rest
    ///
    /// exactly what the rig draws (RestRot · AngleAxis(value, axis), parent then child), with φ and θ the RIG'S OWN
    /// values in degrees. So the stick a hand moves and the stick the rig draws are the same stick, 1:1, rake and all:
    /// every stick in the fleet leans (the Extra's grip is 5.5 cm aft of its pivot, the Great Lakes' 12 cm, the Cub's
    /// 7 cm FORWARD), and the old straight-up model put the grab point that far from the grip the pilot can see.
    ///
    /// The proxy (a type whose model has no stick node yet) is the same thing with an upright stick: aᵣ = −z, aₚ = −x,
    /// so d(φ, θ) = (sin φ cos θ, cos φ cos θ, −sin θ), φ + = top RIGHT, θ + = top AFT.
    ///
    /// THE HAND'S ANGLES are the (φ, θ) that bring the grip nearest the hand (two unknowns, a few Gauss–Newton steps
    /// from the last answer). With the two pivots on the roll axis that is the hand's direction from the pivot, so how
    /// far the hand is from the pivot does not matter.
    ///
    /// LIMITS and the param ↔ angle maps are the rig's (−1..+1 → degrees, piecewise linear: the Extra's elevator is 12°
    /// forward / 18° aft), so <see cref="Aileron"/> and <see cref="ElevatorAft"/> are the rig's own params: what the
    /// aero model's deflections have to be for the rig to draw the stick where the hand put it.
    ///
    /// GRAB WITHOUT A JUMP. At the moment of the grab the difference between where the stick IS and where the hand's
    /// direction would put it is kept, so closing a hand a few centimetres off the grip does not snap the stick; from
    /// then on every centimetre the hand moves is the stick's.
    /// </summary>
    public sealed class StickGimbal
    {
        /// <summary>The pitch hinge (the stick's own pivot).</summary>
        public readonly Vec3 Pivot;
        /// <summary>Pivot to the centre of the grip, metres.</summary>
        public readonly double LengthM;
        public readonly double RollMaxDeg, AftMaxDeg, FwdMaxDeg;
        public readonly Vec3 RollPivot, RollAxis, PitchAxis, GripRest;
        private readonly RigCurve _rollMap, _pitchMap;

        /// <summary>Where the stick is, the rig's degrees: roll + right, pitch + aft (on the proxy; on a rig, its own sense).</summary>
        public double RollDeg { get; private set; }
        public double PitchDeg { get; private set; }

        private double _offRoll, _offPitch;

        /// <summary>The proxy: an upright stick on one pivot, symmetric roll, <paramref name="aftMaxDeg"/> aft and
        /// <paramref name="fwdMaxDeg"/> forward.</summary>
        public StickGimbal(Vec3 pivot, double lengthM, double rollMaxDeg, double aftMaxDeg, double fwdMaxDeg)
            : this(pivot, new Vec3(0, 0, -1), pivot, new Vec3(-1, 0, 0), pivot + new Vec3(0, Math.Max(0.05, lengthM), 0),
                   RigCurve.Symmetric(rollMaxDeg, rollMaxDeg), RigCurve.Symmetric(fwdMaxDeg, aftMaxDeg))
        {
        }

        /// <summary>From the rig: each hinge's pivot and axis (either sense is NOT fine here — the Bridge checks the
        /// sense against the rig's own pose), the grip with both hinges at rest, and each hinge's param → degrees map.</summary>
        public StickGimbal(Vec3 rollPivot, Vec3 rollAxis, Vec3 pitchPivot, Vec3 pitchAxis, Vec3 gripRest, RigCurve rollMap, RigCurve pitchMap)
        {
            RollPivot = rollPivot; Pivot = pitchPivot;
            RollAxis = rollAxis.Length > 1e-9 ? rollAxis / rollAxis.Length : new Vec3(0, 0, -1);
            PitchAxis = pitchAxis.Length > 1e-9 ? pitchAxis / pitchAxis.Length : new Vec3(-1, 0, 0);
            GripRest = gripRest;
            LengthM = Math.Max(0.05, (gripRest - pitchPivot).Length);
            _rollMap = rollMap; _pitchMap = pitchMap;
            RollMaxDeg = Math.Max(1e-3, Math.Max(Math.Abs(rollMap.MinValue), Math.Abs(rollMap.MaxValue)));
            AftMaxDeg = Math.Max(1e-3, Math.Abs(pitchMap.Value(1.0)));
            FwdMaxDeg = Math.Max(1e-3, Math.Abs(pitchMap.Value(-1.0)));
        }

        /// <summary>The upright-stick angles that point the stick at <paramref name="hand"/>, degrees, unclamped (the
        /// proxy's closed form; a rig stick uses <see cref="AnglesFor"/>).</summary>
        public static (double RollDeg, double PitchDeg) AnglesOf(Vec3 pivot, Vec3 hand)
        {
            Vec3 v = hand - pivot;
            double roll = Math.Atan2(v.X, v.Y);
            double pitch = Math.Atan2(-v.Z, Math.Sqrt(v.X * v.X + v.Y * v.Y));
            return (roll * Rad2Deg, pitch * Rad2Deg);
        }

        /// <summary>Rodrigues: <paramref name="v"/> turned <paramref name="deg"/> about unit <paramref name="k"/> (the
        /// same sense as Unity's AngleAxis on the same coordinates).</summary>
        public static Vec3 Rotate(Vec3 v, Vec3 k, double deg)
        {
            double a = deg * Deg2Rad, c = Math.Cos(a), s = Math.Sin(a);
            return v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1.0 - c));
        }

        /// <summary>The grip's centre with the stick at these rig angles.</summary>
        public Vec3 GripAt(double rollDeg, double pitchDeg)
        {
            Vec3 g = Pivot + Rotate(GripRest - Pivot, PitchAxis, pitchDeg);
            return RollPivot + Rotate(g - RollPivot, RollAxis, rollDeg);
        }

        public Vec3 Grip => GripAt(RollDeg, PitchDeg);

        /// <summary>Unit direction from the stick's pivot up the stick (pivot → grip), as it stands now.</summary>
        public Vec3 Up
        {
            get
            {
                Vec3 p = RollPivot + Rotate(Pivot - RollPivot, RollAxis, RollDeg);
                Vec3 d = Grip - p;
                return d.Length > 1e-9 ? d / d.Length : new Vec3(0, 1, 0);
            }
        }

        /// <summary>The rig angles (degrees, unclamped) that bring the grip nearest <paramref name="hand"/>: Gauss–Newton
        /// from (<paramref name="r0"/>, <paramref name="p0"/>), numeric Jacobian.</summary>
        public (double RollDeg, double PitchDeg) AnglesFor(Vec3 hand, double r0, double p0)
        {
            // Aim the stick's direction, not its tip, at the hand: scale the hand onto the grip's sphere about the pivot.
            Vec3 v = hand - Pivot;
            Vec3 target = v.Length > 1e-9 ? Pivot + v * (LengthM / v.Length) : Grip;
            double r = r0, p = p0;
            const double h = 0.01;
            for (int it = 0; it < 12; it++)
            {
                Vec3 e = GripAt(r, p) - target;
                Vec3 jr = (GripAt(r + h, p) - GripAt(r - h, p)) / (2 * h);
                Vec3 jp = (GripAt(r, p + h) - GripAt(r, p - h)) / (2 * h);
                double a = Vec3.Dot(jr, jr), b = Vec3.Dot(jr, jp), c = Vec3.Dot(jp, jp);
                double gr = Vec3.Dot(jr, e), gp = Vec3.Dot(jp, e);
                double det = a * c - b * b;
                if (Math.Abs(det) < 1e-18) break;
                double dr = (c * gr - b * gp) / det, dp = (a * gp - b * gr) / det;
                dr = Math.Clamp(dr, -30, 30); dp = Math.Clamp(dp, -30, 30);
                r -= dr; p -= dp;
                if (Math.Abs(dr) < 1e-6 && Math.Abs(dp) < 1e-6) break;
            }
            return (r, p);
        }

        /// <summary>Put the stick where the aeroplane has it (not held: it is wherever the air or the thumbstick put it).</summary>
        public void SetFromControls(double aileron, double elevatorAft)
        {
            RollDeg = _rollMap.Value(Math.Clamp(aileron, -1.0, 1.0));
            PitchDeg = _pitchMap.Value(Math.Clamp(elevatorAft, -1.0, 1.0));
        }

        /// <summary>The hand closes on the grip: remember the offset between the stick and the hand's direction.</summary>
        public void Grab(Vec3 hand)
        {
            (double r, double p) = AnglesFor(hand, RollDeg, PitchDeg);
            _offRoll = RollDeg - r;
            _offPitch = PitchDeg - p;
        }

        /// <summary>The held stick follows the hand, against its stops.</summary>
        public void Follow(Vec3 hand)
        {
            (double r, double p) = AnglesFor(hand, RollDeg - _offRoll, PitchDeg - _offPitch);
            RollDeg = Math.Clamp(r + _offRoll, _rollMap.MinValue, _rollMap.MaxValue);
            PitchDeg = Math.Clamp(p + _offPitch, _pitchMap.MinValue, _pitchMap.MaxValue);
        }

        /// <summary>−1..+1, +1 = full right — the rig's `aileron` and the pads' aileron.</summary>
        public double Aileron => _rollMap.Param(RollDeg);

        /// <summary>−1..+1, +1 = full AFT — the rig's `elevator` (its map is −1 → −fwd°, +1 → +aft°). The pads'
        /// elevator is PUSH positive, i.e. the negative of this.</summary>
        public double ElevatorAft => _pitchMap.Param(PitchDeg);

        internal const double Deg2Rad = Math.PI / 180.0, Rad2Deg = 180.0 / Math.PI;
    }

    /// <summary>A rig node's param → value map (piecewise linear, monotone), and back: the stick's −1..+1 ↔ degrees.</summary>
    public sealed class RigCurve
    {
        private readonly double[] _p, _v;

        public RigCurve(double[] param, double[] value)
        {
            if (param == null || value == null || param.Length < 2 || param.Length != value.Length)
                throw new ArgumentException("a rig curve needs two or more (param, value) points");
            _p = (double[])param.Clone(); _v = (double[])value.Clone();
        }

        /// <summary>−1 → −<paramref name="negDeg"/>, 0 → 0, +1 → +<paramref name="posDeg"/>.</summary>
        public static RigCurve Symmetric(double negDeg, double posDeg) =>
            new(new[] { -1.0, 0.0, 1.0 }, new[] { -Math.Abs(negDeg), 0.0, Math.Abs(posDeg) });

        public double MinValue => Math.Min(_v[0], _v[_v.Length - 1]);
        public double MaxValue => Math.Max(_v[0], _v[_v.Length - 1]);

        public double Value(double param) => Interp(_p, _v, param);

        public double Param(double value) =>
            _v[_v.Length - 1] >= _v[0] ? Interp(_v, _p, value) : Interp(Reverse(_v), Reverse(_p), value);

        private static double[] Reverse(double[] a) { var r = (double[])a.Clone(); Array.Reverse(r); return r; }

        private static double Interp(double[] x, double[] y, double at)
        {
            if (at <= x[0]) return y[0];
            for (int i = 1; i < x.Length; i++)
                if (at <= x[i])
                {
                    double span = x[i] - x[i - 1];
                    return span > 1e-12 ? y[i - 1] + (y[i] - y[i - 1]) * (at - x[i - 1]) / span : y[i];
                }
            return y[y.Length - 1];
        }
    }

    /// <summary>
    /// A LEVER ON AN ARC: the throttle (and, in Phase 2, anything else that swings about a lateral pivot). Cockpit
    /// frame. The lever turns about the lateral (x) axis through <see cref="Pivot"/>; its angle is measured in the
    /// y-z plane from straight up, positive toward the nose:
    ///
    ///   angle(h) = atan2(v.z, v.y),  v = h − pivot          (the hand's lateral offset does not move the lever)
    ///   lever01  = (angle + offset − idle) / sweep, clamped to 0..1
    ///
    /// <see cref="IdleDeg"/> is where the lever stands at idle and <see cref="SweepDeg"/> how far forward it goes to
    /// full (the Extra's and the Great Lakes' rig: 50°). The offset is taken at the grab, as for the stick, so
    /// closing the hand never moves the lever by itself. Released, it stays: a throttle has friction, not a spring.
    /// </summary>
    public sealed class LeverArc
    {
        public readonly Vec3 Pivot;
        public readonly double ArmM, IdleDeg, SweepDeg;
        private double _off;

        public LeverArc(Vec3 pivot, double armM, double idleDeg, double sweepDeg)
        {
            Pivot = pivot; ArmM = Math.Max(0.02, armM); IdleDeg = idleDeg;
            SweepDeg = Math.Abs(sweepDeg) < 1e-3 ? 1e-3 : sweepDeg;
        }

        /// <summary>The hand's angle about the pivot, degrees, 0 = straight up, + toward the nose.</summary>
        public static double AngleOf(Vec3 pivot, Vec3 hand)
        {
            Vec3 v = hand - pivot;
            return Math.Atan2(v.Z, v.Y) * StickGimbal.Rad2Deg;
        }

        public double AngleFor(double lever01) => IdleDeg + Math.Clamp(lever01, 0.0, 1.0) * SweepDeg;

        /// <summary>The knob's centre with the lever at <paramref name="lever01"/>.</summary>
        public Vec3 GripAt(double lever01)
        {
            double a = AngleFor(lever01) * StickGimbal.Deg2Rad;
            return Pivot + new Vec3(0.0, Math.Cos(a), Math.Sin(a)) * ArmM;
        }

        public void Grab(Vec3 hand, double lever01Now) => _off = AngleFor(lever01Now) - AngleOf(Pivot, hand);

        /// <summary>Where the held lever is, 0..1.</summary>
        public double Follow(Vec3 hand) =>
            Math.Clamp((AngleOf(Pivot, hand) + _off - IdleDeg) / SweepDeg, 0.0, 1.0);
    }
}
