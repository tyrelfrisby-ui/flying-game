using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The tracked hands' joints, for the cockpit hands (VrCockpitHands). The reader is installed by the Quest
    /// assembly (FlyingGame.Quest.QuestHandTracking, XR Hands package), which the iPhone and the Mac do not build;
    /// without it there are no joints and a tracked hand falls back to the Hand Interaction Profile's grip pose and
    /// pinch value (<see cref="XrControllers"/>).
    ///
    /// Joints in Core's <c>HandJoint</c> order (XR Hands' <c>ToIndex()</c>), tracking space.
    /// </summary>
    public static class XrTrackedHands
    {
        public delegate bool JointReader(bool left, Vector3[] positions, Quaternion[] rotations);

        public static JointReader Reader;
        public static string Status = "no reader (not a Quest build)";

        public static bool TryRead(bool left, Vector3[] positions, Quaternion[] rotations) =>
            Reader != null && Reader(left, positions, rotations);
    }
}
