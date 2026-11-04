using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The Touch controllers' GRIP POSES and their haptics, for the hands in the cockpit (VrCockpitHands). Kept apart
    /// from <see cref="XrControllers"/>, which reads the buttons and sticks: that one is the flight mapping's, this one
    /// is the hands'.
    ///
    /// The grip pose (OpenXR's /input/grip/pose, which UnityEngine.XR reports as devicePosition / deviceRotation for a
    /// controller) sits at the centre of the closed hand — exactly the point a hand closes on a stick with — so it is
    /// the hand's position with no offset. Tracking space; QuestRig.TrackingToSeat turns it into the cockpit.
    ///
    /// Phase 3: a tracked hand's palm joint goes in the same place (a TrackedHand source), from the XR Hands package.
    /// </summary>
    public static class XrHandInput
    {
        private static InputDevice _left, _right;
        private static readonly List<InputDevice> _devs = new();

        /// <summary>The controller's grip pose in tracking space, or false when it is asleep or untracked.</summary>
        public static bool TryGripPose(bool left, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            InputDevice d = Controller(left);
            if (!d.isValid) return false;
            if (d.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && !t) return false;
            return d.TryGetFeatureValue(CommonUsages.devicePosition, out pos) && d.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
        }

        /// <summary>A short buzz in one controller: a light tick on reaching a control, a firmer one on closing on
        /// it, a click on a detent. Silently nothing where the device has no impulse haptics.</summary>
        public static void Tick(bool left, float amplitude, float seconds)
        {
            InputDevice d = Controller(left);
            if (!d.isValid) return;
            if (d.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                d.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), seconds);
        }

        /// <summary>The held-in-hand controller on that side (not a tracked hand, which shares the XR node).</summary>
        private static InputDevice Controller(bool left)
        {
            ref InputDevice d = ref left ? ref _left : ref _right;
            if (d.isValid) return d;
            _devs.Clear();
            var want = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand |
                       (left ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right);
            InputDevices.GetDevicesWithCharacteristics(want, _devs);
            foreach (InputDevice c in _devs)
                if (c.isValid) { d = c; break; }
            return d;
        }
    }
}
