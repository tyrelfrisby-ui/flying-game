using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>How a canopy opens. Both aerobatic types hinge on the RIGHT sill; the Pitts first slides aft.</summary>
    public enum CanopyMotion
    {
        /// <summary>Extra 300: up and over to the right about a hinge line on the right sill.</summary>
        HingeRight = 0,
        /// <summary>Pitts S-2: slides AFT on its left mushroom-button track, then hinges up and over to the right.</summary>
        SlideThenHingeRight = 1,
    }

    /// <summary>
    /// One canopy's numbers. Every one is an estimate (E) unless the model's own geometry replaces it: the width is the
    /// distance from the hinge line to the free (left) edge, which the Bridge measures from Codex's nodes where it can.
    /// </summary>
    public readonly struct CanopySpec
    {
        public readonly CanopyMotion Motion;
        public readonly double MassKg, WidthM, LengthM, SlideM, StopDeg;
        /// <summary>Plan area the pressure acts on, m². A bubble's plan is about 0.8 of its bounding rectangle (E).</summary>
        public readonly double AreaM2;

        public CanopySpec(CanopyMotion motion, double massKg, double widthM, double lengthM, double slideM, double stopDeg)
        {
            Motion = motion; MassKg = massKg; WidthM = widthM; LengthM = lengthM; SlideM = slideM; StopDeg = stopDeg;
            AreaM2 = PlanFill * widthM * lengthM;
        }

        public const double PlanFill = 0.8;

        /// <summary>
        /// Extra 300 (two-seat): one canopy over both cockpits, 2.0 m long, 0.75 m hinge to free edge, 14 kg (all E);
        /// 100° stop (the model's rig, itself E). Pitts S-2B: 1.5 m, 0.70 m (Codex's hinge at y −0.36 to the rail grip at
        /// y +0.34), 9 kg (E), 0.1016 m (4 in) of aft slide and a 100° stop (Codex's rig, both E).
        /// </summary>
        public static CanopySpec For(string aircraftId)
        {
            string id = aircraftId ?? "";
            if (id.StartsWith("pitts")) return new CanopySpec(CanopyMotion.SlideThenHingeRight, 9.0, 0.70, 1.5, 0.1016, 100.0);
            return new CanopySpec(CanopyMotion.HingeRight, 14.0, 0.75, 2.0, 0.0, 100.0);
        }
    }

    /// <summary>
    /// THE CANOPY ON ITS HINGE, IN THE AIR. Cockpit frame (+x right, +y up, +z nose). The canopy is a plate from the hinge
    /// line on the RIGHT sill to its free LEFT edge, width w; β is its opening angle (0 shut, + = left edge up, over to the
    /// right). Unit vectors:
    ///
    ///   u(β) = (−cos β, sin β, 0)   hinge → free edge          n(β) = (sin β, cos β, 0)   outer-surface normal
    ///
    /// A force F at the centroid (w/2 along u) opens it by M = (w/2)·(F·n), since n = du/dβ is the way that point moves.
    ///
    /// THE AIRLOAD. In flight the outside of a canopy is in suction and the cockpit inside sits near free-stream static,
    /// so the net load is OUTWARD — NACA RM L50B03 (Huston &amp; Skopinski 1950, flight and full-scale-tunnel pressures on a
    /// sliding canopy): "the highest net aerodynamic load for the front canopy was in the exploding direction and occurred
    /// with the front and rear canopies closed", with the internal pressure "slightly less than free stream". The size
    /// of that net coefficient on OUR canopies is not in the report's text (it is in its figures); I take
    ///
    ///   ΔCp = 0.5 (E)   →   M_aero = (w/2) · ΔCp · q · A,   q = ½ρV²
    ///
    /// and hold it constant as the canopy opens (E: the convex outer skin stays in suction; nothing better is known here).
    ///
    /// GRAVITY (apparent: the aeroplane's load factor included) closes it: M_g = (w/2) · m · (g_app · n).
    /// THE HAND, holding the frame at the free edge (lever arm w), can apply up to <see cref="HandForceN"/> (E, a seated
    /// pilot reaching up with one arm). A canopy the hand cannot hold is torn out of it.
    ///
    ///   I β¨ = M_aero + M_g + M_hand − c β˙,   I = m w² / 3 (a plate about its edge)
    ///
    /// against a stop at <see cref="CanopySpec.StopDeg"/> (restitution 0.2, E). So with the latch off the canopy FLIPS
    /// OPEN once M_aero beats gravity (and the hand), which is what the Extra's POH says it does. Whether it then
    /// DEPARTS at high q is a question for Ty: without a hinge-strength figure it stays on its stop here.
    ///
    /// THE PITTS' SLIDE. The first <see cref="CanopySpec.SlideM"/> is aft along the left track, driven 1:1 by the hand
    /// (the airload's aft component on a faired canopy is small against the track's friction under that suction, E);
    /// it cannot hinge until it is fully aft, because the mushroom buttons hold it down.
    ///
    /// DRAG. An open canopy stands in the free stream and opens the cockpit: ΔCdA = <see cref="OpenDragAreaM2"/> ·
    /// min(1, β / 60°) (E: 1.2 × the canopy's ~0.17 m² frontal arch; the Extra's whole airframe is about 0.27 m²).
    /// </summary>
    public sealed class CanopyDynamics
    {
        public const double NetOpeningCp = 0.5;
        public const double HandForceN = 200.0;
        public const double OpenDragAreaM2 = 0.20;
        public const double DampingNmsPerRad = 2.0;
        public const double StopRestitution = 0.2;
        /// <summary>Angle above which the pilot can climb out past it (E: the frame is clear of his shoulders).</summary>
        public const double ClearToLeaveDeg = 60.0;

        public readonly CanopySpec Spec;
        /// <summary>Metres slid aft (Pitts), 0 .. SlideM.</summary>
        public double SlideM { get; private set; }
        public double AngleDeg { get; private set; }
        public double RateDps { get; private set; }
        public bool Held { get; private set; }

        private double _grabZ, _grabSlide, _grabAngleOff;

        public CanopyDynamics(CanopySpec spec) { Spec = spec; }

        public void Reset() { SlideM = 0.0; AngleDeg = 0.0; RateDps = 0.0; Held = false; }

        public bool SlidComplete => Spec.Motion == CanopyMotion.HingeRight || SlideM >= Spec.SlideM - 1e-6;
        public bool Shut => AngleDeg < 0.5 && SlideM < 1e-3;
        public bool ClearToLeave => AngleDeg >= ClearToLeaveDeg;

        /// <summary>The rig's `canopy_open` (CONTRACT / Codex's rigs): Extra 0..1 = 0..stop°; Pitts 0..0.2 = the slide,
        /// 0.2..1 = 0..stop°.</summary>
        public double Open01 => Spec.Motion == CanopyMotion.HingeRight
            ? Math.Clamp(AngleDeg / Spec.StopDeg, 0.0, 1.0)
            : (SlideM < Spec.SlideM - 1e-6 ? 0.2 * SlideM / Spec.SlideM : 0.2 + 0.8 * Math.Clamp(AngleDeg / Spec.StopDeg, 0.0, 1.0));

        public double DragAreaM2 => OpenDragAreaM2 * Math.Min(1.0, AngleDeg / 60.0);

        public double InertiaKgM2 => Spec.MassKg * Spec.WidthM * Spec.WidthM / 3.0;

        public static Vec3 Normal(double angleDeg)
        {
            double b = angleDeg * Math.PI / 180.0;
            return new Vec3(Math.Sin(b), Math.Cos(b), 0.0);
        }

        /// <summary>Opening moment of the airload, N·m (positive opens).</summary>
        public static double AeroMomentNm(in CanopySpec s, double q) => 0.5 * s.WidthM * NetOpeningCp * q * s.AreaM2;

        /// <summary>Opening moment of (apparent) gravity, N·m; level 1 g: −(w/2)·m·g·cos β.</summary>
        public static double GravityMomentNm(in CanopySpec s, Vec3 gApp, double angleDeg) =>
            0.5 * s.WidthM * s.MassKg * gApp.Dot(Normal(angleDeg));

        /// <summary>Dynamic pressure at which a shut, unlatched canopy flips open against level 1 g and a hand holding
        /// <paramref name="holdN"/> at its free edge: q = ((w/2)·m·g + w·F) / ((w/2)·ΔCp·A).</summary>
        public static double FlipOpenQ(in CanopySpec s, double holdN, double g = 9.80665) =>
            (0.5 * s.WidthM * s.MassKg * g + s.WidthM * holdN) / (0.5 * s.WidthM * NetOpeningCp * s.AreaM2);

        /// <summary>The hand closes on the frame. <paramref name="handZ"/> = fore-aft (for the slide); the angle the
        /// hand points the free edge at is kept as an offset, so closing on it never jumps it.</summary>
        public void Grab(Vec3 hand, Vec3 hinge)
        {
            Held = true;
            _grabZ = hand.Z; _grabSlide = SlideM;
            _grabAngleOff = AngleDeg - HandAngle(hand, hinge);
        }

        public void Release() => Held = false;

        /// <summary>The angle the hand would hold the free edge at: atan2 of its height over the hinge and its distance
        /// inboard (left) of it.</summary>
        public static double HandAngle(Vec3 hand, Vec3 hinge) =>
            Math.Atan2(hand.Y - hinge.Y, hinge.X - hand.X) * 180.0 / Math.PI;

        /// <summary>
        /// One step. <paramref name="locked"/>: the latch (it holds it shut). <paramref name="hand"/> / <paramref name="hinge"/>
        /// are used while <see cref="Held"/>. Returns events for haptics and the log.
        /// </summary>
        public CanopyEvents Step(double dt, double q, Vec3 gApp, bool locked, Vec3 hand, Vec3 hinge)
        {
            CanopyEvents ev = CanopyEvents.None;
            if (dt <= 0.0) return ev;
            if (locked && Shut) { RateDps = 0.0; return ev; }

            // The Pitts' slide: the hand drives it, aft = −z. It stays where it is left.
            // Only while it is down on the track (β ≈ 0): pushed back forward, it slides shut again.
            if (Spec.Motion == CanopyMotion.SlideThenHingeRight && AngleDeg < 0.5)
            {
                bool was = SlidComplete;
                if (Held && !locked)
                    SlideM = Math.Clamp(_grabSlide + (_grabZ - hand.Z), 0.0, Spec.SlideM);
                if (!SlidComplete) { AngleDeg = 0.0; RateDps = 0.0; return ev; }
                // Just reached the end of the track: re-take the hinge offset so the swing starts where the hand is.
                if (!was && Held) _grabAngleOff = AngleDeg - HandAngle(hand, hinge);
            }

            double I = InertiaKgM2;
            double m = AeroMomentNm(Spec, q) + GravityMomentNm(Spec, gApp, AngleDeg);
            double rate = RateDps * Math.PI / 180.0;
            if (Held)
            {
                // The hand is a stiff, bounded servo on the angle it points the edge at: critically damped at 12 rad/s
                // (E, an arm's bandwidth), capped at what one arm can push or pull at the free edge.
                double want = Math.Clamp(HandAngle(hand, hinge) + _grabAngleOff, 0.0, Spec.StopDeg);
                double err = (want - AngleDeg) * Math.PI / 180.0;
                const double wn = 12.0;
                double need = I * (wn * wn * err - 2.0 * wn * rate) - m;
                double cap = HandForceN * Spec.WidthM;
                if (Math.Abs(need) > cap)
                {
                    Held = false;
                    ev |= CanopyEvents.TornFromHand;
                    m += Math.Sign(need) * cap;   // he was still pushing this step
                }
                else m += need;
            }
            m -= DampingNmsPerRad * rate;

            rate += m / I * dt;
            double angle = AngleDeg + rate * dt * 180.0 / Math.PI;
            if (angle >= Spec.StopDeg)
            {
                angle = Spec.StopDeg;
                if (rate > 0.0) { if (rate * 180.0 / Math.PI > 20.0) ev |= CanopyEvents.HitStop; rate = -StopRestitution * rate; }
            }
            if (angle <= 0.0)
            {
                angle = 0.0;
                if (rate < 0.0) rate = 0.0;
            }
            bool wasOpen = AngleDeg >= 0.9 * Spec.StopDeg;
            AngleDeg = angle;
            RateDps = rate * 180.0 / Math.PI;
            if (!wasOpen && AngleDeg >= 0.9 * Spec.StopDeg && !Held) ev |= CanopyEvents.FlippedOpen;
            return ev;
        }
    }

    [Flags]
    public enum CanopyEvents { None = 0, FlippedOpen = 1, HitStop = 2, TornFromHand = 4 }
}
