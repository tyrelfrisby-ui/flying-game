using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE STICK, AS A HAND MOVES IT: pure geometry about the stick's real pivot. Cockpit frame (<see cref="HandPose"/>:
    /// +x right, +y up, +z toward the nose).
    ///
    /// GIMBAL ORDER. Every stick in the fleet is a roll gimbal on a fore-aft torque tube with the pitch yoke riding on
    /// it (the Extra's rig: StickRoll_* is the parent, Stick_* the child; the Great Lakes' Stick_* rides on its
    /// TorqueTube). So the ROLL angle φ is outer and the PITCH angle θ inner, and the stick's unit direction is
    ///
    ///   d(φ, θ) = R_z(φ) · R_x(θ) · ŷ = ( sin φ cos θ,  cos φ cos θ,  −sin θ )
    ///
    /// with φ positive = top to the RIGHT (roll right) and θ positive = top AFT (stick back, nose up). Inverting it
    /// for a hand at h, with v = h − pivot:
    ///
    ///   φ = atan2(v.x, v.y)            (the roll gimbal sees only the lateral / vertical split — fore-aft is the
    ///                                   axis it turns about)
    ///   θ = atan2(−v.z, √(v.x² + v.y²)) (the pitch yoke, measured in the rolled plane)
    ///
    /// Swapping the order would make a stick pulled back AND right roll less than its lateral offset says; the test
    /// GimbalOrderIsRollOuter pins this one.
    ///
    /// LIMITS are the rig's (the Extra and Great Lakes: roll ±18°, pitch 18° aft / 12° forward), or the proxy's for a
    /// type whose model has no stick node yet. Each angle is clamped on its own, which is what the real stops do.
    ///
    /// GRAB WITHOUT A JUMP. At the moment of the grab the difference between where the stick IS and where the hand's
    /// direction would put it is kept, so closing a hand a few centimetres off the grip does not snap the stick; from
    /// then on every centimetre the hand moves is the stick's.
    /// </summary>
    public sealed class StickGimbal
    {
        public readonly Vec3 Pivot;
        /// <summary>Pivot to the centre of the grip, metres.</summary>
        public readonly double LengthM;
        public readonly double RollMaxDeg, AftMaxDeg, FwdMaxDeg;

        /// <summary>Where the stick is, degrees: roll + right, pitch + aft.</summary>
        public double RollDeg { get; private set; }
        public double PitchDeg { get; private set; }

        private double _offRoll, _offPitch;

        public StickGimbal(Vec3 pivot, double lengthM, double rollMaxDeg, double aftMaxDeg, double fwdMaxDeg)
        {
            Pivot = pivot;
            LengthM = Math.Max(0.05, lengthM);
            RollMaxDeg = Math.Max(1e-3, rollMaxDeg);
            AftMaxDeg = Math.Max(1e-3, aftMaxDeg);
            FwdMaxDeg = Math.Max(1e-3, fwdMaxDeg);
        }

        /// <summary>The gimbal angles that point the stick at <paramref name="hand"/>, degrees, unclamped.</summary>
        public static (double RollDeg, double PitchDeg) AnglesOf(Vec3 pivot, Vec3 hand)
        {
            Vec3 v = hand - pivot;
            double roll = Math.Atan2(v.X, v.Y);
            double pitch = Math.Atan2(-v.Z, Math.Sqrt(v.X * v.X + v.Y * v.Y));
            return (roll * Rad2Deg, pitch * Rad2Deg);
        }

        /// <summary>The grip's centre with the stick at these angles.</summary>
        public Vec3 GripAt(double rollDeg, double pitchDeg)
        {
            double r = rollDeg * Deg2Rad, p = pitchDeg * Deg2Rad;
            return Pivot + new Vec3(Math.Sin(r) * Math.Cos(p), Math.Cos(r) * Math.Cos(p), -Math.Sin(p)) * LengthM;
        }

        public Vec3 Grip => GripAt(RollDeg, PitchDeg);

        /// <summary>Put the stick where the aeroplane has it (not held: it is wherever the air or the thumbstick put it).</summary>
        public void SetFromControls(double aileron, double elevatorAft)
        {
            RollDeg = Math.Clamp(aileron, -1.0, 1.0) * RollMaxDeg;
            double e = Math.Clamp(elevatorAft, -1.0, 1.0);
            PitchDeg = e >= 0 ? e * AftMaxDeg : e * FwdMaxDeg;
        }

        /// <summary>The hand closes on the grip: remember the offset between the stick and the hand's direction.</summary>
        public void Grab(Vec3 hand)
        {
            (double r, double p) = AnglesOf(Pivot, hand);
            _offRoll = RollDeg - r;
            _offPitch = PitchDeg - p;
        }

        /// <summary>The held stick follows the hand, against its stops.</summary>
        public void Follow(Vec3 hand)
        {
            (double r, double p) = AnglesOf(Pivot, hand);
            RollDeg = Math.Clamp(r + _offRoll, -RollMaxDeg, RollMaxDeg);
            PitchDeg = Math.Clamp(p + _offPitch, -FwdMaxDeg, AftMaxDeg);
        }

        /// <summary>−1..+1, +1 = full right — the rig's `aileron` and the pads' aileron.</summary>
        public double Aileron => RollDeg / RollMaxDeg;

        /// <summary>−1..+1, +1 = full AFT — the rig's `elevator` (its map is −1 → −fwd°, +1 → +aft°). The pads'
        /// elevator is PUSH positive, i.e. the negative of this.</summary>
        public double ElevatorAft => PitchDeg >= 0 ? PitchDeg / AftMaxDeg : PitchDeg / FwdMaxDeg;

        internal const double Deg2Rad = Math.PI / 180.0, Rad2Deg = 180.0 / Math.PI;
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
