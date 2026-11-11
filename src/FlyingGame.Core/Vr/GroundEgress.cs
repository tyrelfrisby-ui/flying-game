using System;
using System.Collections.Generic;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE SCRIPTED CLIMB-OUT on the ground (and over the sill into the water): the pilot's EYE carried through a few
    /// waypoints — seated eye → standing in the seat → over the sill → on the step / wing walk → on the ground — each leg a
    /// minimum-jerk move (s = 10τ³ − 15τ⁴ + 6τ⁵: zero velocity AND acceleration at both ends, the smoothest a translation
    /// can start and stop, which is what keeps a moved view comfortable). Translation only: nothing here turns the view.
    ///
    /// World frame, y up (EgressWater.cs). Leg times from their length at a deliberate 0.6 m/s with a 0.8 s floor (E).
    /// </summary>
    public sealed class ClimbPath
    {
        public const double PaceMs = 0.6, MinLegSec = 0.8;
        /// <summary>Standing eye height above the soles, m: ANSUR II 50th-percentile man, stature 1.756 less the 0.115 m
        /// from the eye to the top of the head (E for the latter).</summary>
        public const double StandingEyeM = 1.641;
        /// <summary>Standing up in the seat: the eye rises this far (E: seated eye 0.79 above the cushion, standing on the
        /// seat pan's floor in a crouch under the open canopy or door frame).</summary>
        public const double StandUpRiseM = 0.45;

        private readonly Vec3[] _p;
        private readonly double[] _t;
        public double TimeSec { get; private set; }
        public double TotalSec { get; }
        public bool Done => TimeSec >= TotalSec;
        public IReadOnlyList<Vec3> Points => _p;

        public ClimbPath(Vec3[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("a climb needs two points");
            _p = points;
            _t = new double[points.Length - 1];
            for (int i = 0; i < _t.Length; i++) { _t[i] = Math.Max(MinLegSec, (points[i + 1] - points[i]).Length / PaceMs); TotalSec += _t[i]; }
        }

        public Vec3 End => _p[_p.Length - 1];

        /// <summary>Advance and return the eye.</summary>
        public Vec3 Step(double dt) { TimeSec = Math.Min(TotalSec, TimeSec + Math.Max(0.0, dt)); return EyeAt(TimeSec); }

        public Vec3 EyeAt(double t)
        {
            double acc = 0.0;
            for (int i = 0; i < _t.Length; i++)
            {
                if (t <= acc + _t[i] || i == _t.Length - 1)
                {
                    double tau = Math.Clamp((t - acc) / _t[i], 0.0, 1.0);
                    double s = tau * tau * tau * (10.0 - 15.0 * tau + 6.0 * tau * tau);
                    return _p[i] + (_p[i + 1] - _p[i]) * s;
                }
                acc += _t[i];
            }
            return End;
        }

        /// <summary>
        /// The path for a type: <paramref name="toWorld"/> maps a point in the COCKPIT frame (eye-relative: +x right, +y
        /// up, +z nose) into the world; <paramref name="groundUp"/> gives the ground (or, into the water, the surface) under
        /// a world (x, z). The step and ground points take their height from the ground under them, so the climb ends
        /// standing exactly on it, whatever the aeroplane's attitude. <paramref name="intoWater"/>: the last leg ends at
        /// the water instead of standing on the ground — the eye at the surface (PilotSwim takes over from there).
        /// </summary>
        public static ClimbPath For(ExitSpec spec, Func<Vec3, Vec3> toWorld, Func<double, double, double> groundUp, bool intoWater = false)
        {
            double o = spec.OutSign;
            Vec3 eye = toWorld(Vec3.Zero);
            Vec3 stand = toWorld(new Vec3(0.0, StandUpRiseM, 0.0));
            Vec3 sill = toWorld(new Vec3(o * spec.SillOutM, StandUpRiseM, 0.0));
            Vec3 stepXZ = toWorld(new Vec3(o * spec.StepOutM, 0.0, 0.0));
            Vec3 groundXZ = toWorld(new Vec3(o * spec.GroundOutM, 0.0, -spec.GroundAftM));
            var pts = new List<Vec3> { eye, stand, sill };
            if (!intoWater && spec.StepAboveGroundM > 0.05)
                pts.Add(new Vec3(stepXZ.X, groundUp(stepXZ.X, stepXZ.Z) + spec.StepAboveGroundM + StandingEyeM, stepXZ.Z));
            double end = groundUp(groundXZ.X, groundXZ.Z) + (intoWater ? 0.0 : StandingEyeM);
            pts.Add(new Vec3(groundXZ.X, end, groundXZ.Z));
            return new ClimbPath(pts.ToArray());
        }
    }

    /// <summary>A box the walking pilot may not enter: the airframe's parts and the propeller disc. Centre and three
    /// unit axes (right, up, fore) in the world; half sizes along them.</summary>
    public readonly struct KeepOutBox
    {
        public readonly string Name;
        public readonly Vec3 Centre, AxisX, AxisY, AxisZ, Half;
        public KeepOutBox(string name, Vec3 centre, Vec3 axisX, Vec3 axisY, Vec3 axisZ, Vec3 half)
        { Name = name; Centre = centre; AxisX = axisX; AxisY = axisY; AxisZ = axisZ; Half = half; }

        public static KeepOutBox Level(string name, Vec3 centre, double yawDeg, Vec3 half)
        {
            double a = yawDeg * Math.PI / 180.0;
            var fwd = new Vec3(Math.Sin(a), 0, Math.Cos(a));
            var right = new Vec3(Math.Cos(a), 0, -Math.Sin(a));
            return new KeepOutBox(name, centre, right, new Vec3(0, 1, 0), fwd, half);
        }
    }

    /// <summary>The on-foot turn the PLAYER chooses (XrHeadset.TurnOnFoot, VR menu): nothing turns the view unless asked.</summary>
    public enum TurnOnFoot { Snap = 0, Smooth = 1, Off = 2 }

    /// <summary>
    /// ON FOOT: thumbstick smooth locomotion over the terrain (Ty: walk ≈ 1.4 m/s, run ≈ 3–4 m/s).
    ///
    ///   speed      |stick| × <see cref="WalkMs"/> (1.4 m/s, the preferred walking speed of adults; E), or × <see cref="RunMs"/>
    ///              (3.5 m/s, an easy run; E) while the run button is held. Direction: the stick, in the HEAD's heading
    ///              (forward is where he looks), so the stick never turns him.
    ///   smoothing  the velocity follows the command with <see cref="AccelMs2"/> (4 m/s², E): no instant start or stop, the
    ///              rule the comfort notes ask of every translation.
    ///   terrain    the soles stay on the ground height under him; a rise of more than <see cref="MaxStepM"/> per metre
    ///              walked (a slope over ~45°) is a wall: he stops there.
    ///   airframe   every <see cref="KeepOutBox"/> his body capsule (radius <see cref="BodyRadiusM"/>, soles to head) overlaps
    ///              in height pushes him back out horizontally, the shortest way: he walks round a wing, not through it.
    ///   turning    the player's choice only (<see cref="TurnOnFoot"/>): a 30° snap on a flick of the right stick, or a smooth
    ///              90°/s; Off leaves turning to his own head and body.
    /// </summary>
    public sealed class GroundWalker
    {
        public const double WalkMs = 1.4, RunMs = 3.5, AccelMs2 = 4.0, BodyRadiusM = 0.25, MaxStepM = 1.0;
        public const double SnapDeg = 30.0, SmoothDps = 90.0, SnapArm = 0.7, SnapRearm = 0.3, DeadZone = 0.15;
        public const double HeadClearM = 1.75;

        public Vec3 Feet;
        public Vec3 Velocity;
        /// <summary>The body's own heading (the snap / smooth turn), deg, 0 = +z. The head's yaw adds to it.</summary>
        public double YawDeg;
        public string? BlockedBy { get; private set; }
        private bool _snapArmed = true;

        public GroundWalker(Vec3 feet, double yawDeg) { Feet = feet; YawDeg = yawDeg; }

        public Vec3 Eye => Feet + new Vec3(0, ClimbPath.StandingEyeM, 0);

        /// <summary>One frame. Stick axes −1..1 (Y forward); <paramref name="headYawDeg"/>: the head's world yaw (deg, 0 =
        /// +z, clockwise from above); <paramref name="turnX"/>: the right stick X for the turn.</summary>
        public void Step(double dt, double stickX, double stickY, bool run, double headYawDeg, double turnX, TurnOnFoot turn,
                         Func<double, double, double> groundUp, IReadOnlyList<KeepOutBox> keepOut)
        {
            if (dt <= 0.0) return;
            // Turn, only if asked.
            YawDeg = Turn(ref _snapArmed, YawDeg, turnX, turn, dt);

            // Command, in the head's heading.
            double mag = Math.Sqrt(stickX * stickX + stickY * stickY);
            Vec3 want = Vec3.Zero;
            if (mag > DeadZone)
            {
                double a = headYawDeg * Math.PI / 180.0;
                var fwd = new Vec3(Math.Sin(a), 0, Math.Cos(a));
                var right = new Vec3(Math.Cos(a), 0, -Math.Sin(a));
                double k = Math.Min(1.0, (mag - DeadZone) / (1.0 - DeadZone)) * (run ? RunMs : WalkMs) / mag;
                want = (fwd * stickY + right * stickX) * k;
            }
            Vec3 dv = want - Velocity;
            double dvMax = AccelMs2 * dt;
            if (dv.Length > dvMax) dv = dv.Normalized() * dvMax;
            Velocity += dv;

            // Move, then terrain and airframe.
            BlockedBy = null;
            Vec3 next = Feet + Velocity * dt;
            double g0 = groundUp(Feet.X, Feet.Z), g1 = groundUp(next.X, next.Z);
            double run_ = Math.Max(1e-6, Math.Sqrt((next.X - Feet.X) * (next.X - Feet.X) + (next.Z - Feet.Z) * (next.Z - Feet.Z)));
            if (g1 - g0 > MaxStepM * run_ + 0.02) { BlockedBy = "slope"; Velocity = Vec3.Zero; next = Feet; g1 = g0; }
            next = new Vec3(next.X, g1, next.Z);
            if (keepOut != null)
                foreach (KeepOutBox b in keepOut)
                {
                    if (PushOut(b, ref next)) { BlockedBy = b.Name; }
                }
            Feet = new Vec3(next.X, groundUp(next.X, next.Z), next.Z);
        }

        /// <summary>The player's turn: a 30° snap per flick (re-armed once the stick is back under 0.3), or 90°/s smooth,
        /// or none. Returns the new yaw, 0..360.</summary>
        public static double Turn(ref bool snapArmed, double yawDeg, double turnX, TurnOnFoot turn, double dt)
        {
            if (turn == TurnOnFoot.Snap)
            {
                if (snapArmed && Math.Abs(turnX) >= SnapArm) { yawDeg += Math.Sign(turnX) * SnapDeg; snapArmed = false; }
                else if (Math.Abs(turnX) < SnapRearm) snapArmed = true;
            }
            else if (turn == TurnOnFoot.Smooth && Math.Abs(turnX) > DeadZone) yawDeg += turnX * SmoothDps * dt;
            return ((yawDeg % 360.0) + 360.0) % 360.0;
        }

        /// <summary>Push the body capsule (soles at <paramref name="feet"/>, radius BodyRadiusM, height HeadClearM) out of a
        /// box, horizontally, the shortest way. True if it was inside.</summary>
        public static bool PushOut(in KeepOutBox b, ref Vec3 feet)
        {
            Vec3 d = feet - b.Centre;
            // Height overlap: the capsule spans [feet.y, feet.y + HeadClearM]; the box spans ± its extent along world up.
            double upHalf = Math.Abs(b.AxisX.Y) * b.Half.X + Math.Abs(b.AxisY.Y) * b.Half.Y + Math.Abs(b.AxisZ.Y) * b.Half.Z;
            if (feet.Y > b.Centre.Y + upHalf || feet.Y + HeadClearM < b.Centre.Y - upHalf) return false;
            // In the box's own horizontal axes (its X and Z, flattened).
            Vec3 ax = Flat(b.AxisX), az = Flat(b.AxisZ);
            double lx = d.X * ax.X + d.Z * ax.Z, lz = d.X * az.X + d.Z * az.Z;
            double hx = b.Half.X + BodyRadiusM, hz = b.Half.Z + BodyRadiusM;
            double px = hx - Math.Abs(lx), pz = hz - Math.Abs(lz);
            if (px <= 0.0 || pz <= 0.0) return false;
            if (px < pz) lx = Math.Sign(lx == 0 ? 1 : lx) * hx; else lz = Math.Sign(lz == 0 ? 1 : lz) * hz;
            double x = b.Centre.X + ax.X * lx + az.X * lz, z = b.Centre.Z + ax.Z * lx + az.Z * lz;
            feet = new Vec3(x, feet.Y, z);
            return true;
        }

        private static Vec3 Flat(Vec3 v)
        {
            var f = new Vec3(v.X, 0, v.Z);
            return f.LengthSquared > 1e-12 ? f.Normalized() : new Vec3(1, 0, 0);
        }
    }
}
