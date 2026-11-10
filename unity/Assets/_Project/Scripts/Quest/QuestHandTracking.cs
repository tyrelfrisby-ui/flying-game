using System.Collections.Generic;
using FlyingGame.Bridge;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace FlyingGame.Quest
{
    /// <summary>
    /// TRACKED HANDS on the Quest, through Unity's XR Hands package (com.unity.xr.hands) and its OpenXR Hand Tracking
    /// feature (XR_EXT_hand_tracking, switched on for Android by QuestBuild.ConfigureXr). Lives here, in the
    /// Android-and-editor assembly, so the iPhone and the Mac never link the package; the Bridge sees only
    /// <see cref="XrTrackedHands.Reader"/>.
    ///
    /// The 26 joints come out in XR Hands' own order (<c>XRHandJointID.ToIndex()</c>: wrist, palm, thumb ×4, then four
    /// fingers ×5), which is the order Core's <c>HandJoint</c> uses. Poses are in TRACKING space (relative to the XR
    /// origin, the same space as a controller's devicePosition), so QuestRig.TrackingToSeat takes them into the
    /// cockpit exactly as it takes a controller. Nothing here touches the camera.
    /// </summary>
    public static class QuestHandTracking
    {
        private static XRHandSubsystem _hands;
        private static readonly List<XRHandSubsystem> _found = new();
        private static float _nextLook;
        private static bool _logged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install() => XrTrackedHands.Reader = Read;

        private static bool Read(bool left, Vector3[] pos, Quaternion[] rot)
        {
            XRHandSubsystem hs = Subsystem();
            if (hs == null) return false;
            XRHand hand = left ? hs.leftHand : hs.rightHand;
            if (!hand.isTracked) return false;
            int n = Mathf.Min(pos.Length, XRHandJointID.EndMarker.ToIndex());
            for (int i = 0; i < n; i++)
            {
                if (!hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out Pose p))
                {
                    // A joint the runtime has no pose for this frame: the whole hand is not trustworthy.
                    if (i <= 1) return false;
                    p = new Pose(pos[i - 1], rot[i - 1]);
                }
                pos[i] = p.position;
                rot[i] = p.rotation;
            }
            return true;
        }

        /// <summary>The running hand subsystem, looked for at most twice a second until one is found.</summary>
        private static XRHandSubsystem Subsystem()
        {
            if (_hands != null && _hands.running) return _hands;
            _hands = null;
            if (Time.unscaledTime < _nextLook) return null;
            _nextLook = Time.unscaledTime + 0.5f;
            _found.Clear();
            SubsystemManager.GetSubsystems(_found);
            foreach (XRHandSubsystem s in _found)
                if (s.running) { _hands = s; break; }
            if (_hands != null && !_logged)
            {
                _logged = true;
                Debug.Log($"[QuestHands] XR Hands subsystem running ({_hands.subsystemDescriptor?.id}): tracked hands can grab the cockpit's controls");
            }
            XrTrackedHands.Status = _hands != null ? "XR Hands running" : _found.Count > 0 ? "XR Hands subsystem present, not running" : "no XR Hands subsystem";
            return _hands;
        }
    }
}
