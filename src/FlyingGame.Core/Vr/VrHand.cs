using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    public enum HandSide { Left = 0, Right = 1 }

    /// <summary>Where a hand's pose came from. Phase 1 has controllers only; Phase 3 adds tracked hands
    /// (OpenXR hand tracking), which fill the SAME <see cref="HandPose"/> so nothing downstream changes.</summary>
    public enum HandSource { None = 0, Controller = 1, TrackedHand = 2 }

    /// <summary>
    /// ONE HAND, INPUT-AGNOSTIC: what the cockpit needs to know about a pilot's hand, whatever is measuring it.
    ///
    /// FRAME. Everything here is in the COCKPIT frame: the aeroplane's own local axes as Unity draws them
    /// (+x to the pilot's right, +y up, +z toward the nose), metres, origin at the aeroplane's reference. That is
    /// the frame <c>CockpitView.EyeLocal</c> is in, and the frame <see cref="XrHeadPose.ToSeat(Vec3)"/> resolves a
    /// tracked point into (its seat axes ARE the aeroplane's after a recentre), so a hand is
    /// <c>EyeLocal + head.ToSeat(trackingPos)</c> and moves with the aeroplane for free.
    ///
    ///   Position   the centre of the closed hand: a controller's grip pose, a tracked hand's palm joint.
    ///   Rotation   the hand's orientation in the same frame (+z out of the front of the fist, +y back of the hand).
    ///   Grip       0..1, how closed the hand is: the controller's grip trigger, or a tracked hand's curl.
    ///   Pinch      0..1, thumb to index: the controller's index trigger, or a tracked hand's pinch strength. Used
    ///              for the second action on a held control (the Extra's squeeze latch).
    ///
    /// Controllers feed it now (Bridge: XrHandInput). Phase 3's tracked hands feed it from XR Hands joints, with
    /// Grip = average finger curl, and the grab, the latches and the IK take it unchanged.
    /// </summary>
    public struct HandPose
    {
        public HandSource Source;
        public Vec3 Position;
        public Quat Rotation;
        public double Grip;
        public double Pinch;

        public bool Tracked => Source != HandSource.None;

        public static HandPose None => new() { Source = HandSource.None, Rotation = Quat.Identity };
    }
}
