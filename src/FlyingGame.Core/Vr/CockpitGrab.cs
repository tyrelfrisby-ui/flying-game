using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>What a hand can take hold of. Phase 1: the stick, the throttle, the canopy latch. Phase 2 (the bail-out):
    /// the canopy's frame / rail, the harness releases (main and lap), the headset plug, the cockpit sills to climb out
    /// by, and — out of the aeroplane — the ripcord handle. P2c: the Extra's two latch handles one at a time (the
    /// CanopyLatch slot is then the PAIR, between them), and the canopy jettison knob / emergency release. Phase 3:
    /// the glider's dive-brake handle, trim lever and tow release.</summary>
    public enum Grabbable
    {
        None = 0, Stick = 1, Throttle = 2, CanopyLatch = 3,
        CanopyRail = 4, HarnessMain = 5, HarnessLap = 6, HeadsetPlug = 7, SillLeft = 8, SillRight = 9, Ripcord = 10,
        CanopyLatchAft = 11, CanopyLatchFwd = 12, CanopyJettison = 13,
        // Phase 3 (hands): the 2-33's dive-brake handle (whichever cockpit you sit in), its trim lever and tow release.
        SpoilerHandle = 14, TrimLever = 15, TowRelease = 16,
        /// <summary>P2d: a cabin door's handle; a canopy's generic release lever (types without a hands-on latch).
        /// Numbered 17 / 18, not the 14 / 15 lane/claude-vr-cockpit wrote: phase 3 took 14-16 for the 2-33's handles
        /// while that lane sat unmerged, and nothing stores these as numbers — every use is by name.</summary>
        DoorHandle = 17, CanopyRelease = 18,
        // Phase 3 (switches and knobs): the seat's own panel controls, one slot each, PanelFirst + their index.
        PanelFirst = 32, PanelLast = 79,
    }

    /// <summary>One frame's events, for haptics and the log.</summary>
    [Flags]
    public enum GrabEvents
    {
        None = 0,
        /// <summary>The hand came within reach of something (a light tick).</summary>
        HoverEnter = 1,
        /// <summary>The hand closed on it (a firmer tick).</summary>
        Grabbed = 2,
        Released = 4,
    }

    /// <summary>
    /// WHICH HAND HOLDS WHAT. Engine-free arbitration over the grab points, in the cockpit frame:
    ///
    ///   reach    a hand is "on" a point when its centre is within <see cref="ReachM"/> (6 cm) of it; the nearest wins
    ///   grab     the grip CLOSING (rising through <see cref="QuestControlMapper.GripOn"/>) while on a point takes it —
    ///            a fist already closed and swept past the stick does not catch it, which is how every VR grab
    ///            avoids snagging things by accident
    ///   hold     until the grip opens past <see cref="QuestControlMapper.GripOff"/> (hysteresis, no chatter), however
    ///            far the hand then moves: the control's own geometry decides where it can go
    ///   one each a control is held by one hand at a time; a hand holds one control
    ///   menu     while the VR menu is open nothing can be grabbed, and anything held is let go
    ///   late     a TRACKED hand (no trigger to squeeze — the fingers ARE the trigger) usually finishes closing a few
    ///            centimetres before it reaches the grip: a hand that closed less than <see cref="LateGrabS"/> ago and
    ///            has not yet taken anything still takes the first control it reaches. A fist held closed longer than
    ///            that and swept past still catches nothing. Controllers keep the strict closing-edge rule, and the
    ///            harness, headset plug and jettison (<see cref="Deliberate"/>) always need it.
    ///   priority the stick and throttle win against a nearby harness release / switch unless that is clearly
    ///            (<see cref="FlightControlBiasM"/>) closer — reaching for the stick must never pop the harness
    ///   hand-off a hand whose SOURCE changes (controller put down, tracked hand takes over, or back) is a new hand:
    ///            what the old one held is let go, and a hand that arrives already closed must open and close again
    ///
    /// Grabbing NEVER moves the head: nothing here, or in the Bridge that drives it, touches the camera.
    /// </summary>
    public sealed class CockpitGrab
    {
        public const double ReachM = 0.06;

        /// <summary>How long after a tracked hand closes it may still take the first control it reaches.</summary>
        public const double LateGrabS = 0.6;

        /// <summary>The stick and throttle are taken as this much nearer than they are when choosing between points.</summary>
        public const double FlightControlBiasM = 0.025;

        /// <summary>The reach of the <see cref="Deliberate"/> releases: a hand has to be right on the buckle / plug /
        /// knob (the Great Lakes' harness buckle sits a few centimetres from the stick's foot).</summary>
        public const double DeliberateReachM = 0.04;

        public struct Point
        {
            public Grabbable Id;
            public Vec3 Position;
            public bool Enabled;
            /// <summary>A CAPSULE rather than a ball: the reach is measured to the segment
            /// <see cref="Position"/> → <see cref="SegmentEnd"/> (a stick's grip is 10–15 cm of tube, and a fist closed
            /// anywhere on it holds the stick — not only within 6 cm of one point on its axis).</summary>
            public bool HasSegment;
            public Vec3 SegmentEnd;

            /// <summary>Squared distance from <paramref name="hand"/> to the point, or to the segment.</summary>
            public double Distance2(Vec3 hand)
            {
                if (!HasSegment) return (Position - hand).LengthSquared;
                Vec3 ab = SegmentEnd - Position;
                double l2 = ab.LengthSquared;
                double t = l2 > 1e-12 ? Math.Clamp(Vec3.Dot(hand - Position, ab) / l2, 0.0, 1.0) : 0.0;
                return (Position + ab * t - hand).LengthSquared;
            }
        }

        /// <summary>One slot per <see cref="Grabbable"/> value.</summary>
        public const int Slots = 80;

        public readonly Point[] Points = new Point[Slots];

        private readonly Grabbable[] _held = new Grabbable[2], _hover = new Grabbable[2];
        private readonly bool[] _gripClosed = new bool[2], _armed = new bool[2];
        private readonly double[] _closedAge = new double[2];
        private readonly HandSource[] _source = new HandSource[2];

        public Grabbable Held(HandSide h) => _held[(int)h];
        public Grabbable Hover(HandSide h) => _hover[(int)h];
        public bool IsHeld(Grabbable g) => _held[0] == g || _held[1] == g;
        public bool IsHeldBy(Grabbable g, out HandSide side)
        {
            side = _held[0] == g ? HandSide.Left : HandSide.Right;
            return g != Grabbable.None && _held[(int)side] == g;
        }

        public void Set(Grabbable id, Vec3 position, bool enabled)
        {
            int i = (int)id;
            if (i <= 0 || i >= Points.Length) return;
            Points[i] = new Point { Id = id, Position = position, Enabled = enabled };
        }

        /// <summary>A capsule grabbable: anywhere within <see cref="ReachM"/> of the segment <paramref name="a"/> →
        /// <paramref name="b"/>.</summary>
        public void Set(Grabbable id, Vec3 a, Vec3 b, bool enabled)
        {
            int i = (int)id;
            if (i <= 0 || i >= Points.Length) return;
            Points[i] = new Point { Id = id, Position = a, Enabled = enabled, HasSegment = true, SegmentEnd = b };
        }

        /// <summary>Let go of everything (menu open, outside view, a new aeroplane).</summary>
        public void ReleaseAll()
        {
            _held[0] = _held[1] = Grabbable.None;
            _hover[0] = _hover[1] = Grabbable.None;
        }

        /// <summary>Take a control out of one hand without the grip opening (the canopy was torn out of it). The hand
        /// cannot take hold again until it opens and closes.</summary>
        public void Drop(HandSide side)
        {
            _held[(int)side] = Grabbable.None;
            _hover[(int)side] = Grabbable.None;
        }

        /// <summary>The nearest enabled point within reach of <paramref name="hand"/> that the other hand isn't holding.</summary>
        public Grabbable Nearest(Vec3 hand, HandSide side)
        {
            Grabbable best = Grabbable.None;
            double bestScore = double.MaxValue;
            Grabbable other = _held[1 - (int)side];
            for (int i = 1; i < Points.Length; i++)
            {
                Point p = Points[i];
                if (!p.Enabled || p.Id == other) continue;
                double d2 = p.Distance2(hand);
                double reach = Deliberate(p.Id) ? DeliberateReachM : ReachM;
                if (d2 > reach * reach) continue;
                double score = Math.Sqrt(d2) - (p.Id == Grabbable.Stick || p.Id == Grabbable.Throttle ? FlightControlBiasM : 0.0);
                if (score <= bestScore) { bestScore = score; best = p.Id; }
            }
            return best;
        }

        /// <summary>The releases a late grab never takes — a hand on its way to the stick must not pop the harness,
        /// pull the headset plug or jettison the canopy; those still take a hand that closes ON them.</summary>
        public static bool Deliberate(Grabbable g) =>
            g == Grabbable.HarnessMain || g == Grabbable.HarnessLap || g == Grabbable.HeadsetPlug || g == Grabbable.CanopyJettison;

        /// <summary>One hand, one frame. <paramref name="blocked"/>: the menu is open (or the hands are elsewhere).
        /// <paramref name="dt"/> (seconds) enables the tracked-hand late grab; negative (the default) keeps the strict
        /// closing-edge rule for every source.</summary>
        public GrabEvents Step(HandSide side, in HandPose hand, bool blocked, double dt = -1.0)
        {
            int s = (int)side;
            GrabEvents ev = GrabEvents.None;
            bool wasClosed = _gripClosed[s];
            bool closed = hand.Tracked && (wasClosed ? hand.Grip > QuestControlMapper.GripOff : hand.Grip >= QuestControlMapper.GripOn);
            _gripClosed[s] = closed;
            if (closed && !wasClosed) { _armed[s] = dt >= 0.0 && hand.Source == HandSource.TrackedHand; _closedAge[s] = 0.0; }
            else if (closed) _closedAge[s] += Math.Max(dt, 0.0);
            else _armed[s] = false;
            if (_closedAge[s] > LateGrabS) _armed[s] = false;
            if (hand.Source != HandSource.None && hand.Source != _source[s])
            {
                // A different kind of hand from the last one seen on this side (the first one ever is not a change).
                if (_source[s] != HandSource.None)
                {
                    if (_held[s] != Grabbable.None) ev |= GrabEvents.Released;
                    _held[s] = Grabbable.None;
                    wasClosed = closed;   // arriving closed is not a grab
                    _armed[s] = false;
                }
                _source[s] = hand.Source;
            }

            if (blocked || !hand.Tracked)
            {
                _armed[s] = false;
                if (_held[s] != Grabbable.None) ev |= GrabEvents.Released;
                _held[s] = Grabbable.None;
                _hover[s] = Grabbable.None;
                return ev;
            }

            if (_held[s] != Grabbable.None)
            {
                if (closed) return ev;
                _held[s] = Grabbable.None;
                ev |= GrabEvents.Released;
            }

            Grabbable near = Nearest(hand.Position, side);
            if (near != Grabbable.None && near != _hover[s]) ev |= GrabEvents.HoverEnter;
            _hover[s] = near;
            if (near != Grabbable.None && closed && (!wasClosed || (_armed[s] && !Deliberate(near))))
            {
                _held[s] = near;
                _armed[s] = false;
                ev |= GrabEvents.Grabbed;
            }
            return ev;
        }
    }
}
