using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>How far a point is from the cockpit's structure, metres (zero on a surface; an implementation may return
    /// a negative value where it knows it is inside a solid). The Bridge answers it, unsigned, from the aeroplane's own
    /// meshes (VrArmClearance): a capsule closer than its radius to any surface is in the structure.</summary>
    public interface IArmClearance
    {
        double Distance(Vec3 p);
    }

    /// <summary>
    /// A WHOLE ARM ON A CONTROL GRIP: where the wrist goes, which way the hand faces, and where the elbow swings so the
    /// arm stays out of the cockpit's structure (Ty 2026-11-04, Quest 3S: "the wrists are super unrealistic on the hands
    /// and look really weird. the arms go into the structure on the great lakes like the switch box on the left").
    ///
    /// What was wrong: the wrist was put on the line from the grip back to the SHOULDER and the hand aimed down it, its
    /// roll taken from the controller in the real hand. The forearm runs from the ELBOW, which sits well off that line,
    /// so the hand met the forearm at 30–50° of sideways bend, rolled however the controller happened to be held, and
    /// the elbow went where one fixed pole (45 cm outboard of the eye) sent it: through the Great Lakes' left switch box.
    ///
    /// Now, for a hand holding a control:
    ///  1. The hand wraps the control's own GRIP AXIS (a stick's shaft, a lever's arm): its long axis (wrist → knuckles)
    ///     lies across that axis, thumb along it, palm against it. Of the directions across the grip it takes the one
    ///     nearest the forearm, tilted along the grip by up to <see cref="MaxTiltDeg"/> (a real fist is not square to a
    ///     stick), so the wrist is nearly straight: the bend left over is reported in <see cref="Result.WristBendDeg"/>.
    ///  2. The wrist sits a fist's depth behind the grip along that long axis.
    ///  3. The elbow swings about the shoulder→wrist line (the arm's one free angle) from where the natural pole puts it,
    ///     in <see cref="SwivelStepDeg"/> steps up to <see cref="MaxSwivelDeg"/> either way, and takes the angle whose
    ///     upper arm and forearm (capsules <see cref="UpperArmRadiusM"/> / <see cref="ForearmRadiusM"/>) go least into the
    ///     structure, the smaller swing winning ties. Steps 1–3 are iterated, the wrist depending on the elbow and back.
    /// Pure maths, any frame; the clearance query is the only thing that knows about meshes.
    /// </summary>
    public static class ArmPose
    {
        public const double MaxTiltDeg = 20.0;
        /// <summary>A round lever knob does not square the hand to anything: it may sit this far off square to the hinge.</summary>
        public const double KnobTiltDeg = 75.0;
        public const double MaxSwivelDeg = 80.0, SwivelStepDeg = 10.0;
        public const double UpperArmRadiusM = 0.050, ForearmRadiusM = 0.042;
        /// <summary>Penetration per degree of swing that buys the swing: below it, the natural elbow wins.</summary>
        public const double SwivelCostMPerDeg = 0.00005;
        /// <summary>The last few centimetres of the forearm are the hand's business (it is ON a control).</summary>
        public const double ForearmFreeEndM = 0.05;

        public struct Result
        {
            public Vec3 Shoulder, Elbow, Wrist, Grip;
            /// <summary>Unit: wrist → knuckles (the fist's long axis), the palm's outward normal, the thumb.</summary>
            public Vec3 HandForward, Palm, Thumb;
            /// <summary>Angle between the forearm and the hand, degrees.</summary>
            public double WristBendDeg;
            /// <summary>Swing of the elbow from the natural pole, degrees (+ = right-handed about shoulder → wrist).</summary>
            public double SwivelDeg;
            /// <summary>Deepest point of the arm inside the structure, metres (0 = clear).</summary>
            public double PenetrationM, NaturalPenetrationM;
            public bool Reached;
        }

        /// <summary>The arm for a hand closed on a control whose grip centre is <paramref name="grip"/> and whose grip
        /// axis (stick: pivot → grip; lever: pivot → knob) is <paramref name="gripAxis"/>.</summary>
        public static Result OnGrip(bool left, Vec3 shoulder, Vec3 grip, Vec3 gripAxis, double upper, double lower, Vec3 pole,
                                    double wristToGrip, IArmClearance? clearance = null, double preferSwivelDeg = 0.0, double maxTiltDeg = MaxTiltDeg)
        {
            Vec3 a = gripAxis.LengthSquared > 1e-12 ? gripAxis.Normalized() : new Vec3(0, 1, 0);
            // Start: the hand points away from the shoulder, across the grip.
            Vec3 h = Across(grip - shoulder, a, Vec3.Zero, maxTiltDeg);
            Result r = default;
            for (int it = 0; it < 4; it++)
            {
                Vec3 wrist = grip - h * wristToGrip;
                r = Swing(shoulder, wrist, upper, lower, pole, clearance, preferSwivelDeg);
                Vec3 fore = r.Wrist - r.Elbow;
                Vec3 hNew = Across(fore, a, h, maxTiltDeg);
                if ((hNew - h).LengthSquared < 1e-8) { h = hNew; break; }
                h = hNew;
            }
            r.Grip = r.Reached ? grip : r.Wrist + h * wristToGrip;
            r.HandForward = h;
            // Thumb along the grip axis, the side of it the hand is on; palm against the grip.
            Vec3 thumb = a - h * a.Dot(h);
            r.Thumb = thumb.LengthSquared > 1e-12 ? thumb.Normalized() : a;
            Vec3 c = r.Thumb.Cross(h);   // right-handed: thumb × forward
            r.Palm = left ? c : -c;
            Vec3 f = r.Wrist - r.Elbow;
            r.WristBendDeg = f.LengthSquared > 1e-12 ? AngleDeg(f, h) : 0.0;
            return r;
        }

        /// <summary>A lever KNOB (a throttle quadrant's, a sidewall throttle's) is held across the lever, fingers over the
        /// top and the palm on it: its grip axis is the lever's HINGE axis, turned so the thumb points inboard
        /// (+x for the left hand, −x for the right; cockpit frame +x right).</summary>
        public static Vec3 KnobAxis(Vec3 hinge, bool left)
        {
            if (hinge.LengthSquared < 1e-12) return new Vec3(left ? 1 : -1, 0, 0);
            Vec3 a = hinge.Normalized();
            return (left ? a.X : -a.X) < 0 ? -a : a;
        }

        /// <summary>The elbow swung <paramref name="swivelDeg"/> about shoulder → wrist from where the pole puts it.</summary>
        public static Vec3 ElbowAt(Vec3 shoulder, Vec3 wrist, double upper, double lower, Vec3 pole, double swivelDeg)
        {
            TwoBoneIk.Result n = TwoBoneIk.Solve(shoulder, wrist, upper, lower, pole);
            Vec3 axis = n.End - shoulder;
            if (Math.Abs(swivelDeg) < 1e-9 || axis.LengthSquared < 1e-10) return n.Middle;
            return shoulder + StickGimbal.Rotate(n.Middle - shoulder, axis.Normalized(), swivelDeg);
        }

        /// <summary>A free hand (or a tracked one): the wrist is given, only the elbow's swing is chosen.</summary>
        public static Result Free(Vec3 shoulder, Vec3 wrist, double upper, double lower, Vec3 pole, IArmClearance? clearance = null, double preferSwivelDeg = 0.0)
            => Swing(shoulder, wrist, upper, lower, pole, clearance, preferSwivelDeg);

        /// <summary>The direction across unit axis <paramref name="a"/> nearest <paramref name="want"/>, tilted toward it
        /// along the axis by at most <see cref="MaxTiltDeg"/>. <paramref name="fallback"/> when <paramref name="want"/> runs
        /// along the axis.</summary>
        public static Vec3 Across(Vec3 want, Vec3 a, Vec3 fallback, double maxTiltDeg = MaxTiltDeg)
        {
            if (want.LengthSquared < 1e-12) return fallback.LengthSquared > 1e-12 ? fallback : Perp(a);
            Vec3 w = want.Normalized();
            Vec3 flat = w - a * w.Dot(a);
            if (flat.LengthSquared < 1e-8) return fallback.LengthSquared > 1e-12 ? fallback : Perp(a);
            flat = flat.Normalized();
            double along = Math.Asin(Math.Clamp(w.Dot(a), -1.0, 1.0));
            double tilt = Math.Clamp(along, -maxTiltDeg * Math.PI / 180.0, maxTiltDeg * Math.PI / 180.0);
            return (flat * Math.Cos(tilt) + a * Math.Sin(tilt)).Normalized();
        }

        private static Vec3 Perp(Vec3 a) => Math.Abs(a.Y) < 0.9 ? new Vec3(0, 1, 0).Cross(a).Normalized() : new Vec3(1, 0, 0).Cross(a).Normalized();

        private static Result Swing(Vec3 shoulder, Vec3 wrist, double upper, double lower, Vec3 pole, IArmClearance? clearance, double preferSwivelDeg)
        {
            TwoBoneIk.Result natural = TwoBoneIk.Solve(shoulder, wrist, upper, lower, pole);
            var best = new Result { Shoulder = shoulder, Elbow = natural.Middle, Wrist = natural.End, Reached = natural.Reached };
            if (clearance == null) return best;
            Vec3 axis = natural.End - shoulder;
            if (axis.LengthSquared < 1e-10) return best;
            axis = axis.Normalized();
            double pNatural = Penetration(shoulder, natural.Middle, natural.End, clearance);
            best.NaturalPenetrationM = best.PenetrationM = pNatural;
            if (pNatural <= 0.0 && Math.Abs(preferSwivelDeg) < 1e-9) return best;
            double bestScore = double.MaxValue;
            int n = (int)Math.Round(MaxSwivelDeg / SwivelStepDeg);
            for (int k = -n; k <= n; k++)
            {
                double deg = k * SwivelStepDeg;
                Vec3 elbow = shoulder + StickGimbal.Rotate(natural.Middle - shoulder, axis, deg);
                double pen = deg == 0.0 ? pNatural : Penetration(shoulder, elbow, natural.End, clearance);
                // Hysteresis: the swing it had last frame is cheap to keep, so the elbow does not hop between two near ties.
                double score = pen + SwivelCostMPerDeg * Math.Min(Math.Abs(deg), Math.Abs(deg - preferSwivelDeg) + 5.0);
                if (score < bestScore - 1e-12)
                {
                    bestScore = score;
                    best.Elbow = elbow; best.SwivelDeg = deg; best.PenetrationM = pen;
                }
            }
            return best;
        }

        /// <summary>Deepest intrusion of the two arm capsules into the structure, sampled every ~2.5 cm.</summary>
        public static double Penetration(Vec3 shoulder, Vec3 elbow, Vec3 wrist, IArmClearance c)
        {
            double worst = 0.0;
            worst = Math.Max(worst, Segment(shoulder, elbow, UpperArmRadiusM, 0.06, 0.0, c));   // the first 6 cm are the shoulder's own
            worst = Math.Max(worst, Segment(elbow, wrist, ForearmRadiusM, 0.0, ForearmFreeEndM, c));
            return worst;
        }

        private static double Segment(Vec3 a, Vec3 b, double radius, double skipStart, double skipEnd, IArmClearance c)
        {
            Vec3 d = b - a;
            double len = d.Length;
            if (len < 1e-6) return Math.Max(0.0, radius - c.Distance(a));
            int steps = Math.Max(2, (int)Math.Ceiling(len / 0.025));
            double worst = 0.0;
            for (int i = 0; i <= steps; i++)
            {
                double s = len * i / steps;
                if (s < skipStart || s > len - skipEnd) continue;
                worst = Math.Max(worst, radius - c.Distance(a + d * (s / len)));
            }
            return worst;
        }

        public static double AngleDeg(Vec3 u, Vec3 v)
        {
            double l = u.Length * v.Length;
            return l < 1e-12 ? 0.0 : Math.Acos(Math.Clamp(u.Dot(v) / l, -1.0, 1.0)) * 180.0 / Math.PI;
        }
    }
}
