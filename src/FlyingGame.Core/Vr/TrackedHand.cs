using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// A TRACKED HAND'S JOINTS, and what the cockpit reads off them (docs/vr-cockpit.md, "Hands").
    ///
    /// The 26 joints are in the XR Hands package's order (<c>XRHandJointID.ToIndex()</c>, the same as OpenXR's
    /// XR_EXT_hand_tracking with wrist and palm swapped): 0 wrist, 1 palm, 2–5 thumb (metacarpal, proximal, distal,
    /// tip), then index, middle, ring, little at 6, 11, 16, 21, each metacarpal, proximal, intermediate, distal, tip.
    /// Any frame will do, as long as all 26 are in the same one; the Bridge passes the cockpit frame.
    /// </summary>
    public static class HandJoint
    {
        public const int Count = 26;
        public const int Wrist = 0, Palm = 1;
        public const int ThumbMetacarpal = 2, ThumbProximal = 3, ThumbDistal = 4, ThumbTip = 5;
        public const int IndexMetacarpal = 6, MiddleMetacarpal = 11, RingMetacarpal = 16, LittleMetacarpal = 21;
        public const int IndexTip = 10;

        /// <summary>The metacarpal of finger <paramref name="f"/> (0 index .. 3 little); its proximal, intermediate,
        /// distal and tip follow it.</summary>
        public static int Finger(int f) => IndexMetacarpal + 5 * f;
    }

    /// <summary>
    /// GRAB OR NOT, FROM A TRACKED HAND, without chatter. Two ways to take hold, either one enough:
    ///
    ///   pinch   thumb tip to index tip under <see cref="PinchCloseM"/> (2.0 cm), let go above <see cref="PinchOpenM"/>
    ///           (3.5 cm). Meta's own system pinch fires at about the same gap; the 1.5 cm between the two is wider than
    ///           the tracking's own jitter at arm's length (≈ 0.5 cm), so a held pinch does not flicker open.
    ///   grip    the four fingers' mean curl (<see cref="FingerCurl"/>) over <see cref="CurlClose"/> (0.65), let go under
    ///           <see cref="CurlOpen"/> (0.45): a fist round a lever.
    ///
    /// Closed stays closed until BOTH have opened, so a pilot who pinches the dive-brake handle and then wraps his
    /// fingers round it (or the other way) does not drop it in the change-over.
    ///
    /// TRACKING DROPS OUT for a frame or three when a hand goes behind the panel or the other hand; letting go then
    /// would drop the handle in the middle of a pull. For up to <see cref="HoldThroughLossSec"/> (0.25 s) the last
    /// pose and the last closed state are kept; after that the hand is gone and whatever it held is released.
    ///
    /// The output is a <see cref="HandPose"/> with <c>Grip</c> = 1 closed / 0 open, so <see cref="CockpitGrab"/>'s own
    /// thresholds pass it through unchanged, and <c>Pinch</c> = the pinch strength 0..1 for the second action on a
    /// held control (the Extra's squeeze latch).
    /// </summary>
    public sealed class HandGrabDetector
    {
        public const double PinchCloseM = 0.020, PinchOpenM = 0.035;
        /// <summary>The gap that counts as fully open for <see cref="PinchStrength"/> (thumb and index relaxed).</summary>
        public const double PinchFullOpenM = 0.060;
        public const double CurlClose = 0.65, CurlOpen = 0.45;
        public const double HoldThroughLossSec = 0.25;

        public bool Closed { get; private set; }
        /// <summary>The last frame's answer came from a held-over pose (tracking lost, inside the grace).</summary>
        public bool Coasting { get; private set; }

        private HandPose _last = HandPose.None;
        private double _lostSec = double.PositiveInfinity;

        /// <summary>0 at <see cref="PinchFullOpenM"/> or wider, 1 at <see cref="PinchCloseM"/> or closer.</summary>
        public static double PinchStrength(double gapM) =>
            Math.Clamp((PinchFullOpenM - gapM) / (PinchFullOpenM - PinchCloseM), 0.0, 1.0);

        /// <summary>
        /// How bent ONE finger is, 0 straight .. 1 a closed fist: the sum of the bends at its three knuckles
        /// (metacarpal→proximal, proximal→intermediate, intermediate→distal, distal→tip, angle between successive
        /// bones) over <see cref="FullCurlDeg"/>. A fist round a 3 cm handle bends a finger about 60 + 90 + 40 ≈ 190°;
        /// a relaxed open hand 15–30°, which is why curl opens under 0.45 (≈ 85°) and closes above 0.65 (≈ 125°).
        /// </summary>
        public static double FingerCurl(in Vec3 metacarpal, in Vec3 proximal, in Vec3 intermediate, in Vec3 distal, in Vec3 tip)
        {
            double sum = Bend(metacarpal, proximal, intermediate) + Bend(proximal, intermediate, distal) + Bend(intermediate, distal, tip);
            return Math.Clamp(sum / FullCurlDeg, 0.0, 1.0);
        }

        public const double FullCurlDeg = 190.0;

        /// <summary>The angle between bone a→b and bone b→c, degrees (0 = straight on).</summary>
        private static double Bend(in Vec3 a, in Vec3 b, in Vec3 c)
        {
            Vec3 u = b - a, v = c - b;
            double lu = u.Length, lv = v.Length;
            if (lu < 1e-6 || lv < 1e-6) return 0.0;
            double cos = Math.Clamp(Vec3.Dot(u, v) / (lu * lv), -1.0, 1.0);
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        /// <summary>The mean curl of the four fingers from a full joint set (<see cref="HandJoint"/> order).</summary>
        public static double MeanCurl(Vec3[] j)
        {
            double s = 0.0;
            for (int f = 0; f < 4; f++)
            {
                int m = HandJoint.Finger(f);
                s += FingerCurl(j[m], j[m + 1], j[m + 2], j[m + 3], j[m + 4]);
            }
            return 0.25 * s;
        }

        /// <summary>
        /// One frame. <paramref name="tracked"/> false = the runtime lost the hand this frame. <paramref name="pinchGapM"/>
        /// thumb tip to index tip, metres (NaN if unknown); <paramref name="curl"/> the mean finger curl 0..1 (NaN if
        /// unknown). Returns the hand as the grab reads it.
        /// </summary>
        public HandPose Step(bool tracked, in Vec3 position, in Quat rotation, double pinchGapM, double curl, double dt)
        {
            if (!tracked)
            {
                _lostSec += Math.Max(0.0, dt);
                Coasting = _last.Tracked && _lostSec <= HoldThroughLossSec;
                if (Coasting) return _last;
                Closed = false;
                _last = HandPose.None;
                return HandPose.None;
            }
            _lostSec = 0.0;
            Coasting = false;
            bool hasPinch = !double.IsNaN(pinchGapM), hasCurl = !double.IsNaN(curl);
            bool pinchClosed = hasPinch && pinchGapM < PinchCloseM, pinchOpen = !hasPinch || pinchGapM > PinchOpenM;
            bool curlClosed = hasCurl && curl > CurlClose, curlOpen = !hasCurl || curl < CurlOpen;
            Closed = Closed ? !(pinchOpen && curlOpen) : pinchClosed || curlClosed;
            _last = new HandPose
            {
                Source = HandSource.TrackedHand,
                Position = position,
                Rotation = rotation,
                Grip = Closed ? 1.0 : 0.0,
                Pinch = hasPinch ? PinchStrength(pinchGapM) : 0.0,
            };
            return _last;
        }

        public void Reset()
        {
            Closed = false; Coasting = false;
            _last = HandPose.None;
            _lostSec = double.PositiveInfinity;
        }
    }

    /// <summary>
    /// CONTROLLER OR HAND, per side, chosen every frame with no setting: a Touch controller whose pose is tracked wins
    /// (the pilot is holding it); with the controller put down, a tracked hand takes over once it has been seen for
    /// <see cref="HandSettleSec"/> (0.15 s), which keeps a controller's brief tracking blink from flipping the hand
    /// to a hand that the runtime is still reporting from the last time. A change of source is a NEW hand:
    /// <see cref="CockpitGrab"/> lets go of whatever the old one held, and the new one must close to take hold.
    /// </summary>
    public sealed class HandSourceArbiter
    {
        public const double HandSettleSec = 0.15;

        public HandSource Current { get; private set; }
        private double _handSeenSec;

        public HandSource Step(bool controllerTracked, bool handTracked, double dt)
        {
            // The settle counts from the controller going: a hand reported while the controller is still in it is stale.
            _handSeenSec = handTracked && !controllerTracked ? _handSeenSec + Math.Max(0.0, dt) : 0.0;
            if (controllerTracked) Current = HandSource.Controller;
            else if (handTracked && (Current == HandSource.TrackedHand || _handSeenSec >= HandSettleSec)) Current = HandSource.TrackedHand;
            else if (!handTracked) Current = HandSource.None;
            else if (Current == HandSource.Controller) Current = HandSource.None;   // controller gone, hand still settling
            return Current;
        }
    }

    /// <summary>
    /// The ONE-EURO FILTER (Casiez, Roussel and Vogel, CHI 2012) on a 3-D point: an exponential smoother whose cut-off
    /// rises with speed, so a hand held still on a lever loses its tracking jitter (≈ 0.5 cm at arm's length, a
    /// noticeable 1° of a 30 cm lever) while a quick pull is not lagged. Tracked hands only: a controller's pose is
    /// already clean.
    ///
    ///   α(fc) = 1 / (1 + 1 / (2π fc dt)),  fc = MinCutoffHz + Beta · |ẋ|   (ẋ itself smoothed at DCutoffHz)
    /// </summary>
    public sealed class OneEuroFilter3
    {
        public double MinCutoffHz = 1.5, Beta = 4.0, DCutoffHz = 1.0;
        private Vec3 _x, _dx;
        private bool _has;

        public void Reset() => _has = false;

        public Vec3 Step(in Vec3 x, double dt)
        {
            if (!_has || dt <= 0.0) { _x = x; _dx = Vec3.Zero; _has = true; return x; }
            Vec3 rawDx = (x - _x) / dt;
            _dx = _dx + (rawDx - _dx) * Alpha(DCutoffHz, dt);
            double fc = MinCutoffHz + Beta * _dx.Length;
            _x = _x + (x - _x) * Alpha(fc, dt);
            return _x;
        }

        private static double Alpha(double cutoffHz, double dt)
        {
            double tau = 1.0 / (2.0 * Math.PI * cutoffHz);
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
