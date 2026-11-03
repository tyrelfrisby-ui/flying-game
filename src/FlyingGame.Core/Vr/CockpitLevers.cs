using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// A LEVER THAT SWINGS ABOUT A HINGE, as a hand moves it: the 2-33's dive-brake handle and trim lever, and any rig
    /// lever with a <c>kind: rot</c> node. Pure geometry in whatever frame the three points are given in (the Bridge
    /// gives the cockpit frame).
    ///
    /// Built from POINTS, not from an axis and a sign: the pivot, and where the knob is at the two ends of its travel
    /// (the Bridge asks the rig itself — RestRot · AngleAxis(angle, axis) — so no one has to reason about glTF or Unity
    /// handedness, which this project has got wrong by reasoning before). With r₀ = tip(min) − pivot and
    /// r₁ = tip(max) − pivot:
    ///
    ///   n     = r₀ × r₁ / |r₀ × r₁|                     the hinge axis, oriented so travel min → max is positive
    ///   θ(h)  = atan2(n · (r̂₀ × v), r̂₀ · v),  v = h − pivot  the hand's angle from the min end, in the lever's plane
    ///                                                     (its offset along the hinge does not move the lever)
    ///   t     = (θ + offset) / sweep, clamped to 0..1      sweep = the angle between r₀ and r₁ (&lt; 180°)
    ///
    /// The knob at fraction t is r̂₀ rotated toward r₁ by t·sweep: cos·r̂₀ + sin·(n × r̂₀). The offset is taken at the
    /// grab, so closing a hand a little off the knob never moves the lever. Given the rig's hinge direction as well,
    /// a knob that sits off the pivot along the hinge is handled by projecting onto the plane across it first.
    /// </summary>
    public sealed class HingedLever
    {
        public readonly Vec3 Pivot, Axis;
        public readonly double ArmM, SweepDeg;
        /// <summary>How far the knob sits along the hinge from the pivot (the 2-33's handle is 11 cm inboard of its
        /// hinge): the knob turns in a plane that far along the axis.</summary>
        public readonly double AxialM;
        private readonly Vec3 _u0, _w0;   // r̂₀ and n × r̂₀: the plane's axes
        private double _off;

        /// <summary>From the pivot and the knob at the two ends. With <paramref name="axisHint"/> (the rig's own hinge
        /// direction, either sense) the knob may sit off the pivot ALONG the hinge: r₀ and r₁ are projected on the plane
        /// across it first. Without one, the knob must turn in the pivot's own plane (r₀ × r₁ is then the hinge) — a
        /// knob offset along its hinge would read a tilted hinge and a short sweep (the 2-33's read 31° for 33°).</summary>
        public HingedLever(Vec3 pivot, Vec3 tipAtMin, Vec3 tipAtMax, Vec3 axisHint = default)
        {
            Pivot = pivot;
            Vec3 r0 = tipAtMin - pivot, r1 = tipAtMax - pivot;
            Vec3 n;
            if (axisHint.Length > 1e-9)
            {
                Vec3 a = axisHint / axisHint.Length;
                Vec3 p0 = r0 - a * Vec3.Dot(a, r0), p1 = r1 - a * Vec3.Dot(a, r1);
                // Orient the hint so that min → max is the positive sense (worked out from the points, not reasoned).
                n = Vec3.Dot(a, Vec3.Cross(p0, p1)) >= 0.0 ? a : -a;
                r0 = p0; r1 = p1;
            }
            else
            {
                Vec3 c = Vec3.Cross(r0, r1);
                n = c.Length > 1e-12 ? c / c.Length : new Vec3(1, 0, 0);
            }
            Axis = n;
            AxialM = Vec3.Dot(n, tipAtMin - pivot);
            ArmM = Math.Max(0.01, 0.5 * (r0.Length + r1.Length));
            _u0 = r0.Length > 1e-9 ? r0 / r0.Length : new Vec3(0, 1, 0);
            _w0 = Vec3.Cross(Axis, _u0);
            double s = Math.Atan2(Vec3.Dot(Axis, Vec3.Cross(_u0, r1)), Vec3.Dot(_u0, r1)) * Rad2Deg;
            SweepDeg = Math.Max(1e-3, s);
        }

        /// <summary>The hand's angle from the min end, degrees, + toward max. −180..180.</summary>
        public double AngleOf(Vec3 hand)
        {
            Vec3 v = hand - Pivot;
            return Math.Atan2(Vec3.Dot(Axis, Vec3.Cross(_u0, v)), Vec3.Dot(_u0, v)) * Rad2Deg;
        }

        /// <summary>The knob with the lever at fraction <paramref name="t"/> of its travel (0 = min end).</summary>
        public Vec3 GripAt(double t)
        {
            double a = Math.Clamp(t, 0.0, 1.0) * SweepDeg * Deg2Rad;
            return Pivot + Axis * AxialM + (_u0 * Math.Cos(a) + _w0 * Math.Sin(a)) * ArmM;
        }

        public void Grab(Vec3 hand, double tNow) => _off = Math.Clamp(tNow, 0.0, 1.0) * SweepDeg - AngleOf(hand);

        /// <summary>Where the held lever is, 0..1 of its travel.</summary>
        public double Follow(Vec3 hand)
        {
            // Unwrapped about the middle of the travel, so a hand swung far past either stop stays at that stop
            // instead of wrapping through ±180° to the other one.
            double a = AngleOf(hand) + _off;
            double mid = 0.5 * SweepDeg;
            while (a - mid > 180.0) a -= 360.0;
            while (a - mid < -180.0) a += 360.0;
            return Math.Clamp(a / SweepDeg, 0.0, 1.0);
        }

        internal const double Deg2Rad = Math.PI / 180.0, Rad2Deg = 180.0 / Math.PI;
    }

    /// <summary>
    /// A KNOB THAT SLIDES: pulled straight out along its own axis (the 2-33's tow release, a push-pull mixture or
    /// carb heat). The hand's movement along the axis is the knob's; across it does nothing. The offset is taken at
    /// the grab, as for the levers.
    ///
    ///   t = ((h − home) · â + offset) / travel, clamped 0..1,   â = (out − home) / |out − home|
    /// </summary>
    public sealed class SlideKnob
    {
        public readonly Vec3 Home, Out;
        public readonly double TravelM;
        private readonly Vec3 _a;
        private double _off;

        public SlideKnob(Vec3 home, Vec3 fullyOut)
        {
            Home = home; Out = fullyOut;
            Vec3 d = fullyOut - home;
            TravelM = Math.Max(1e-3, d.Length);
            _a = d.Length > 1e-9 ? d / d.Length : new Vec3(0, 0, -1);
        }

        public Vec3 GripAt(double t) => Home + _a * (Math.Clamp(t, 0.0, 1.0) * TravelM);

        public void Grab(Vec3 hand, double tNow) => _off = Math.Clamp(tNow, 0.0, 1.0) * TravelM - Vec3.Dot(hand - Home, _a);

        public double Follow(Vec3 hand) => Math.Clamp((Vec3.Dot(hand - Home, _a) + _off) / TravelM, 0.0, 1.0);
    }

    /// <summary>What a lever's movement this frame is worth in the hand: a bump at a stop or a detent.</summary>
    [Flags]
    public enum LeverFeel { None = 0, Detent = 1, Stop = 2 }

    /// <summary>
    /// DETENTS AND STOPS as the hand feels them: an event when the lever reaches either end of its travel, or crosses a
    /// detent (the 2-33's wheel brake starting at 85 % of the dive-brake handle, a trim quadrant's notches). Fires once
    /// per crossing; resting on a detent does not buzz.
    /// </summary>
    public sealed class LeverDetents
    {
        public const double StopEps = 1e-3;
        private readonly double[] _at;
        private double _prev = double.NaN;

        public LeverDetents(params double[] detents) => _at = detents ?? Array.Empty<double>();

        public void Reset(double t) => _prev = t;

        public LeverFeel Step(double t)
        {
            if (double.IsNaN(_prev)) { _prev = t; return LeverFeel.None; }
            LeverFeel f = LeverFeel.None;
            if ((t <= StopEps && _prev > StopEps) || (t >= 1.0 - StopEps && _prev < 1.0 - StopEps)) f |= LeverFeel.Stop;
            foreach (double d in _at)
                if ((_prev < d && t >= d) || (_prev > d && t <= d)) f |= LeverFeel.Detent;
            _prev = t;
            return f;
        }
    }

    /// <summary>
    /// THE 2-33 DIVE-BRAKE HANDLE AS THE SIM SEES IT. One handle, two jobs (Schweizer 2-33A flight manual: the wheel
    /// brake is applied by pulling the dive-brake handle to the end of its travel):
    ///
    ///   handle 0 .. 1               the handle's travel, forward (RETRACT, stowed) to full aft
    ///   spoiler = handle            the dive brakes, the same 0..1 the touch strip has always given the sim
    ///   wheel brake = (handle − 0.85) / 0.15, clamped     the last 15 % of the travel, exactly as the strip
    ///
    /// The sim reads the spoiler from <c>ControlInputs.ThrottleLever</c> (max(0, lever) on a glider) and the brake from
    /// <c>Aircraft.BrakeInput</c>, both written in ONE place (TouchFlightControls.PublishToDriver); the hand only moves
    /// the handle. Released, the handle stays where it was left: a 2-33's has friction and no spring.
    ///
    /// The left-thumbstick knob of the Quest keeps its own bands (stowed above half, spoiler to full at 25 %, brake
    /// below 20 %); <see cref="KnobFor"/> puts the knob where the handle's spoiler is, so after a hand has moved the
    /// handle the thumbstick carries on from there, and <see cref="FromKnob"/> goes the other way for drawing.
    /// </summary>
    public static class SpoilerHandle
    {
        public const double WheelBrakeFrom = 0.85;

        public static double Spoiler(double handle) => Math.Clamp(handle, 0.0, 1.0);

        public static double WheelBrake(double handle) => Math.Clamp((handle - WheelBrakeFrom) / (1.0 - WheelBrakeFrom), 0.0, 1.0);

        /// <summary>The left knob height (0 bottom .. 1 top) whose spoiler band gives this spoiler:
        /// spoiler = (0.5 − k) / (0.5 − idle) inverted, so 0 → 0.5 (the top of the band) and 1 → idle.</summary>
        public static double KnobFor(double handle, double idleFraction) =>
            0.5 - Spoiler(handle) * (0.5 - idleFraction);

        /// <summary>The handle that the knob's spoiler and brake stand for: spoiler 0..1 is the first 85 %, the knob's
        /// brake band the rest (the knob has its spoiler full before its brake starts, as the handle has it at 85 %).</summary>
        public static double FromKnob(double spoiler, double knobBrake) =>
            knobBrake > 0.0 ? WheelBrakeFrom + (1.0 - WheelBrakeFrom) * Math.Clamp(knobBrake, 0.0, 1.0)
                            : Math.Clamp(spoiler, 0.0, 1.0);
    }

    /// <summary>
    /// A PULL KNOB ON A SPRING THAT FIRES AT THE END OF ITS TRAVEL: the tow release. Schweizer 2-33A flight manual:
    /// "pull the release knob full out" — 50 mm on our model (the rig's TowRelease map). The cable's spring brings it
    /// home when it is let go, in <see cref="SpringBackSec"/> (0.3 s, the value the G key's animation has always used).
    ///
    ///   fires   once, when the held knob reaches <see cref="FireAt"/> (0.9 of its travel: the hook opens near the
    ///           end of the pull, not at the first millimetre — a knob brushed by a sleeve releases nothing)
    ///   re-arms when the knob is back under <see cref="RearmBelow"/> (0.3), so holding it out does not fire again
    /// </summary>
    public sealed class SpringPullKnob
    {
        public const double FireAt = 0.9, RearmBelow = 0.3, SpringBackSec = 0.30;

        public double Pull01 { get; private set; }
        private bool _armed = true;

        /// <summary>One frame. <paramref name="held"/>: a hand has it, at <paramref name="handPull01"/>. Returns true on
        /// the frame the release fires.</summary>
        public bool Step(bool held, double handPull01, double dt)
        {
            Pull01 = held ? Math.Clamp(handPull01, 0.0, 1.0) : Math.Max(0.0, Pull01 - Math.Max(0.0, dt) / SpringBackSec);
            if (Pull01 < RearmBelow) _armed = true;
            if (_armed && held && Pull01 >= FireAt) { _armed = false; return true; }
            return false;
        }

        public void Reset() { Pull01 = 0.0; _armed = true; }
    }
}
