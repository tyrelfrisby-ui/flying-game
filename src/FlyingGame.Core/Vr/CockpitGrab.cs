using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>What a hand can take hold of. Phase 1: the stick, the throttle, the canopy latch. Phase 2 (the bail-out):
    /// the canopy's frame / rail, the harness releases (main and lap), the headset plug, the cockpit sills to climb out
    /// by, and — out of the aeroplane — the ripcord handle.</summary>
    public enum Grabbable
    {
        None = 0, Stick = 1, Throttle = 2, CanopyLatch = 3,
        CanopyRail = 4, HarnessMain = 5, HarnessLap = 6, HeadsetPlug = 7, SillLeft = 8, SillRight = 9, Ripcord = 10,
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
    ///
    /// Grabbing NEVER moves the head: nothing here, or in the Bridge that drives it, touches the camera.
    /// </summary>
    public sealed class CockpitGrab
    {
        public const double ReachM = 0.06;

        public struct Point
        {
            public Grabbable Id;
            public Vec3 Position;
            public bool Enabled;
        }

        /// <summary>One slot per <see cref="Grabbable"/> value.</summary>
        public const int Slots = 11;

        public readonly Point[] Points = new Point[Slots];

        private readonly Grabbable[] _held = new Grabbable[2], _hover = new Grabbable[2];
        private readonly bool[] _gripClosed = new bool[2];

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

        /// <summary>Let go of everything (menu open, outside view, a new aeroplane).</summary>
        public void ReleaseAll()
        {
            _held[0] = _held[1] = Grabbable.None;
            _hover[0] = _hover[1] = Grabbable.None;
        }

        /// <summary>The nearest enabled point within reach of <paramref name="hand"/> that the other hand isn't holding.</summary>
        public Grabbable Nearest(Vec3 hand, HandSide side)
        {
            Grabbable best = Grabbable.None;
            double bestD2 = ReachM * ReachM;
            Grabbable other = _held[1 - (int)side];
            for (int i = 1; i < Points.Length; i++)
            {
                Point p = Points[i];
                if (!p.Enabled || p.Id == other) continue;
                double d2 = (p.Position - hand).LengthSquared;
                if (d2 <= bestD2) { bestD2 = d2; best = p.Id; }
            }
            return best;
        }

        /// <summary>One hand, one frame. <paramref name="blocked"/>: the menu is open (or the hands are elsewhere).</summary>
        public GrabEvents Step(HandSide side, in HandPose hand, bool blocked)
        {
            int s = (int)side;
            GrabEvents ev = GrabEvents.None;
            bool wasClosed = _gripClosed[s];
            bool closed = hand.Tracked && (wasClosed ? hand.Grip > QuestControlMapper.GripOff : hand.Grip >= QuestControlMapper.GripOn);
            _gripClosed[s] = closed;

            if (blocked || !hand.Tracked)
            {
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
            if (near != Grabbable.None && closed && !wasClosed)
            {
                _held[s] = near;
                ev |= GrabEvents.Grabbed;
            }
            return ev;
        }
    }
}
