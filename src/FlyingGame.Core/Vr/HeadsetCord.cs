using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    public enum CordState
    {
        /// <summary>Plug in the aircraft's jack, headset on the head.</summary>
        Plugged = 0,
        /// <summary>Plug out of the jack (by hand or pulled out): the plug hangs on its cord from the headset.</summary>
        Unplugged = 1,
        /// <summary>The cord held and the headset was pulled off the head instead: it goes with the aeroplane.</summary>
        HeadsetOff = 2,
    }

    [Flags]
    public enum CordEvents { None = 0, Taut = 1, PluggedOutByHand = 2, PulledOut = 4, HeadsetPulledOff = 8 }

    /// <summary>
    /// THE HEADSET CORD, as a load path: headset (Headset_Cable_Root) → cord → plug → the aeroplane's jack.
    ///
    /// NUMBERS (all E unless said):
    ///   length 1.5 m — CONTRACT.md's default until Codex's handoff gives a real headset's; diameter 4 mm (drawn).
    ///   stiffness k = 2000 N/m — a PVC-jacketed cord stretching about a centimetre at 20 N; it only decides how fast the
    ///     tension rises once the cord is straight, which is a few milliseconds whatever it is.
    ///   plug retention 25 N — the pair of GA plugs together. No PJ-055 / PJ-068 withdrawal figure was found; phone-jack
    ///     datasheets give 3–40 N for insertion/extraction (e.g. Haoyu PJ-611), and GA plugs are known to come out of the
    ///     panel when a pilot stands up still plugged in.
    ///   headset retention 40 N — what it takes to drag a clamped headset off a head by its cord. Only below the plug's
    ///     figure (a locking LEMO 6-pin, or the jack's spring clip tightened) does the headset go instead.
    ///   neck 1000 N/m — the head's give against a sideways tug, for the view offset only.
    ///
    /// PHYSICS. The cord is slack until the straight-line distance d from the headset to the jack exceeds its length L;
    /// then T = k·(d − L). The weakest link lets go at its retention force: the plug comes out (→ <see cref="CordState.Unplugged"/>)
    /// or the headset comes off (→ <see cref="CordState.HeadsetOff"/>). Separating at v, the pull lasts about
    /// F/(k·v) — 3 ms at 4 m/s — so on the pilot's 105 kg it is an impulse of well under 1 N·s: a TUG, not a tow. On the
    /// HEAD it is a T-newton sideways pull, which the view shows as an offset T / k_neck along the cord, capped at
    /// <see cref="MaxYankM"/>, relaxing with <see cref="YankTauSec"/>, translation only (docs/vr-cockpit.md, comfort).
    /// </summary>
    public sealed class HeadsetCord
    {
        public const double DefaultLengthM = 1.5, DiameterM = 0.004, StiffnessNpm = 2000.0;
        public const double PlugRetentionN = 25.0, HeadsetRetentionN = 40.0, NeckStiffnessNpm = 1000.0;
        public const double MaxYankM = 0.04, YankTauSec = 0.15;
        /// <summary>How far a hand must draw the plug out along the jack axis: the plug's engaged length, 3 cm (E).</summary>
        public const double UnplugM = 0.03;

        public readonly double LengthM, PlugN, HeadsetN;
        public CordState State { get; private set; } = CordState.Plugged;
        public double TensionN { get; private set; }
        /// <summary>The largest tension it has carried (for the log).</summary>
        public double PeakTensionN { get; private set; }
        /// <summary>The plug's travel out of the jack, 0..1, while a hand is drawing it.</summary>
        public double PlugOut01 { get; private set; }
        public bool Held { get; private set; }

        private Vec3 _grabHand, _axis;
        private bool _taut;

        public HeadsetCord(double lengthM = DefaultLengthM, double plugN = PlugRetentionN, double headsetN = HeadsetRetentionN)
        {
            LengthM = lengthM; PlugN = plugN; HeadsetN = headsetN;
        }

        public void Reset() { State = CordState.Plugged; TensionN = PeakTensionN = 0.0; PlugOut01 = 0.0; Held = false; _taut = false; }

        public bool Plugged => State == CordState.Plugged;

        /// <summary>Tension for a straight-line distance d (N): 0 slack, k·(d − L) taut.</summary>
        public static double Tension(double distanceM, double lengthM) => Math.Max(0.0, distanceM - lengthM) * StiffnessNpm;

        /// <summary>The head's displacement along the cord for a tension, m, bounded.</summary>
        public static double YankM(double tensionN) => Math.Min(MaxYankM, Math.Max(0.0, tensionN) / NeckStiffnessNpm);

        /// <summary>Close a hand on the plug. <paramref name="outAxis"/> = the direction the plug comes out of the jack.</summary>
        public void Grab(Vec3 hand, Vec3 outAxis)
        {
            if (State != CordState.Plugged) return;
            Held = true; _grabHand = hand;
            _axis = outAxis.LengthSquared > 1e-12 ? outAxis.Normalized() : new Vec3(-1, 0, 0);
        }

        public void Release() { Held = false; if (State == CordState.Plugged) PlugOut01 = 0.0; }

        /// <summary>One frame of a hand on the plug: true on the frame it comes out.</summary>
        public bool Follow(Vec3 hand)
        {
            if (!Held || State != CordState.Plugged) return false;
            double t = (hand - _grabHand).Dot(_axis) / UnplugM;
            PlugOut01 = Math.Clamp(t, 0.0, 1.0);
            if (t < 1.0) return false;
            State = CordState.Unplugged;
            Held = false;
            return true;
        }

        /// <summary>One step with the straight-line distance from the headset's cable root to the jack.</summary>
        public CordEvents Step(double distanceM)
        {
            CordEvents ev = CordEvents.None;
            if (State != CordState.Plugged) { TensionN = 0.0; _taut = false; return ev; }
            TensionN = Tension(distanceM, LengthM);
            if (TensionN > 0.0 && !_taut) ev |= CordEvents.Taut;
            _taut = TensionN > 0.0;
            // The weaker link lets go at its own force; the tension can never exceed it.
            double weakest = Math.Min(PlugN, HeadsetN);
            if (TensionN >= weakest)
            {
                TensionN = weakest;
                if (PlugN <= HeadsetN) { State = CordState.Unplugged; ev |= CordEvents.PulledOut; }
                else { State = CordState.HeadsetOff; ev |= CordEvents.HeadsetPulledOff; }
            }
            PeakTensionN = Math.Max(PeakTensionN, TensionN);
            return ev;
        }
    }

    /// <summary>
    /// A ROPE drawn between two points: position-based (Verlet) dynamics, inextensible segments, either end pinned.
    /// Visual only — the load the cord carries is <see cref="HeadsetCord"/>'s, from the straight-line distance, which is
    /// exact once the rope is taut and zero while it is slack. N segments of L/N; gravity and a light air damping;
    /// <see cref="Iterations"/> constraint passes a step.
    /// </summary>
    public sealed class VerletRope
    {
        public readonly Vec3[] Points;
        public readonly double SegmentM;
        public int Iterations = 12;
        public double Damping = 0.02;
        private readonly Vec3[] _prev;

        public VerletRope(int segments, double lengthM, Vec3 start, Vec3 dir)
        {
            int n = Math.Max(2, segments) + 1;
            Points = new Vec3[n];
            _prev = new Vec3[n];
            SegmentM = lengthM / (n - 1);
            Vec3 d = dir.LengthSquared > 1e-12 ? dir.Normalized() : new Vec3(0, -1, 0);
            for (int i = 0; i < n; i++) Points[i] = _prev[i] = start + d * (SegmentM * i);
        }

        /// <summary>Total length along the points (the cord's drawn length; stays ≈ L whatever the ends do).</summary>
        public double Length
        {
            get { double s = 0; for (int i = 1; i < Points.Length; i++) s += (Points[i] - Points[i - 1]).Length; return s; }
        }

        /// <summary>Teleport all of it (the whole frame jumped, e.g. a new aeroplane).</summary>
        public void Snap(Vec3 start, Vec3 dir)
        {
            Vec3 d = dir.LengthSquared > 1e-12 ? dir.Normalized() : new Vec3(0, -1, 0);
            for (int i = 0; i < Points.Length; i++) Points[i] = _prev[i] = start + d * (SegmentM * i);
        }

        /// <summary>One step. <paramref name="a"/> pins point 0; <paramref name="b"/> pins the last point when
        /// <paramref name="pinB"/>. Positions in any frame, with <paramref name="gravity"/> in that frame.</summary>
        public void Step(double dt, Vec3 gravity, Vec3 a, Vec3 b, bool pinB)
        {
            int n = Points.Length;
            double dt2 = dt * dt;
            for (int i = 0; i < n; i++)
            {
                Vec3 p = Points[i];
                Vec3 v = (p - _prev[i]) * (1.0 - Damping);
                _prev[i] = p;
                Points[i] = p + v + gravity * dt2;
            }
            for (int k = 0; k < Iterations; k++)
            {
                Points[0] = a;
                if (pinB) Points[n - 1] = b;
                for (int i = 1; i < n; i++)
                {
                    Vec3 d = Points[i] - Points[i - 1];
                    double len = d.Length;
                    if (len < 1e-9) continue;
                    double diff = (len - SegmentM) / len;
                    bool fixA = i - 1 == 0, fixB = pinB && i == n - 1;
                    if (fixA && fixB) continue;
                    if (fixA) Points[i] -= d * diff;
                    else if (fixB) Points[i - 1] += d * diff;
                    else { Points[i - 1] += d * (0.5 * diff); Points[i] -= d * (0.5 * diff); }
                }
            }
            Points[0] = a;
            if (pinB) Points[n - 1] = b;
        }
    }
}
