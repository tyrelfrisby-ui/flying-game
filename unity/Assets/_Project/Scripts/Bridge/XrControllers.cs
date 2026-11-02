using System.Collections.Generic;
using FlyingGame.Core.Vr;
using UnityEngine;
using UnityEngine.XR;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Reads the Quest's Touch controllers and tracked hands into a <see cref="QuestControllerFrame"/>, once a
    /// frame, for <see cref="QuestControlMapper"/>.
    ///
    /// THE OLD INPUT HANDLER STAYS (ProjectSettings activeInputHandler: 0). This uses
    /// <see cref="UnityEngine.XR.InputDevices"/> and its feature usages, which OpenXR's native input provider
    /// feeds whether or not the Input System package is the active backend: the Oculus Touch Controller Profile
    /// names its actions with the classic usages (Primary2DAxis, Trigger, Grip, PrimaryButton, SecondaryButton,
    /// Primary2DAxisClick, MenuButton), and the Hand Interaction Profile with its own (PinchValue, ...), read
    /// here by name. Switching the handler to "Both" would have changed how touch and gamepads are read on
    /// iOS, which this lane must not touch.
    ///
    /// Positions come back in the headset's TRACKING space. <see cref="SeatYawDeg"/> — the heading taken out at
    /// the last recentre, set by <see cref="QuestRig"/> — turns them into the seat's axes.
    /// </summary>
    public static class XrControllers
    {
        /// <summary>Heading removed at the last recentre, degrees, right positive.</summary>
        public static float SeatYawDeg;

        private static int _frame = -1;
        private static QuestControllerFrame _f;
        private static bool _any;
        private static InputDevice _leftCtl, _rightCtl, _leftHand, _rightHand;
        private static readonly List<InputDevice> _devs = new();

        private static readonly InputFeatureUsage<float> PinchValue = new("PinchValue");
        private static readonly InputFeatureUsage<Vector3> PointerPosition = new("PointerPosition");
        private static readonly InputFeatureUsage<Quaternion> PointerRotation = new("PointerRotation");

        /// <summary>This frame's controllers. <paramref name="any"/> is false when nothing is tracked at all
        /// (headset on a desk, controllers asleep): the caller then leaves the controls alone.</summary>
        public static QuestControllerFrame Read(out bool any)
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                Poll();
            }
            any = _any;
            return _f;
        }

        public static bool LeftControllerTracked => Tracked(_leftCtl);
        public static bool RightControllerTracked => Tracked(_rightCtl);

        private static void Poll()
        {
            Find(XRNode.LeftHand, ref _leftCtl, ref _leftHand);
            Find(XRNode.RightHand, ref _rightCtl, ref _rightHand);
            var f = new QuestControllerFrame();
            bool l = Tracked(_leftCtl), r = Tracked(_rightCtl);
            if (l)
            {
                Vector2 s = Axis(_leftCtl, CommonUsages.primary2DAxis);
                f.LeftX = s.x; f.LeftY = s.y;
                f.LeftTrigger = Float(_leftCtl, CommonUsages.trigger);
                f.LeftGrip = Float(_leftCtl, CommonUsages.grip);
                f.X = Button(_leftCtl, CommonUsages.primaryButton);
                f.Y = Button(_leftCtl, CommonUsages.secondaryButton);
                f.LeftStickClick = Button(_leftCtl, CommonUsages.primary2DAxisClick);
                f.Menu = Button(_leftCtl, CommonUsages.menuButton);
                if (_leftCtl.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p))
                    f.LeftHandForwardM = (Quaternion.Euler(0f, -SeatYawDeg, 0f) * p).z;
            }
            if (r)
            {
                Vector2 s = Axis(_rightCtl, CommonUsages.primary2DAxis);
                f.RightX = s.x; f.RightY = s.y;
                f.RightTrigger = Float(_rightCtl, CommonUsages.trigger);
                f.RightGrip = Float(_rightCtl, CommonUsages.grip);
                f.A = Button(_rightCtl, CommonUsages.primaryButton);
                f.B = Button(_rightCtl, CommonUsages.secondaryButton);
                f.RightStickClick = Button(_rightCtl, CommonUsages.primary2DAxisClick);
            }
            bool lh = !l && Tracked(_leftHand), rh = !r && Tracked(_rightHand);
            f.HandsOnly = !l && !r && (lh || rh);
            if (f.HandsOnly)
            {
                f.LeftPinch = lh ? Float(_leftHand, PinchValue) : 0.0;
                f.RightPinch = rh ? Float(_rightHand, PinchValue) : 0.0;
            }
            _f = f;
            _any = l || r || f.HandsOnly;
            XrHeadset.HandsOnly = f.HandsOnly;
        }

        /// <summary>Where a hand is pointing, in tracking space: the controller's (or the tracked hand's) aim
        /// pose, falling back to its grip pose. For the VR menu's pointer.</summary>
        public static bool TryPointer(bool left, out Vector3 pos, out Quaternion rot)
        {
            Read(out _);
            InputDevice d = left ? (Tracked(_leftCtl) ? _leftCtl : _leftHand) : (Tracked(_rightCtl) ? _rightCtl : _rightHand);
            pos = default; rot = Quaternion.identity;
            if (!Tracked(d)) return false;
            if (d.TryGetFeatureValue(PointerPosition, out pos) && d.TryGetFeatureValue(PointerRotation, out rot)) return true;
            return d.TryGetFeatureValue(CommonUsages.devicePosition, out pos) && d.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
        }

        /// <summary>The head, in tracking space: the centre eye if the runtime reports it, else the head node.</summary>
        public static bool TryHead(out Vector3 pos, out Quaternion rot)
        {
            InputDevice h = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (h.isValid && h.TryGetFeatureValue(CommonUsages.centerEyePosition, out pos) && h.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rot)) return true;
            h = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            pos = default; rot = Quaternion.identity;
            return h.isValid && h.TryGetFeatureValue(CommonUsages.devicePosition, out pos) && h.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
        }

        /// <summary>A Touch controller announces a thumbstick; a tracked hand announces a pinch. Sort the devices at
        /// a node into the two.</summary>
        private static void Find(XRNode node, ref InputDevice controller, ref InputDevice hand)
        {
            if (controller.isValid && hand.isValid) return;
            _devs.Clear();
            InputDevices.GetDevicesAtXRNode(node, _devs);
            foreach (InputDevice d in _devs)
            {
                if (!d.isValid) continue;
                if (!controller.isValid && d.TryGetFeatureValue(CommonUsages.primary2DAxis, out _)) controller = d;
                else if (!hand.isValid && d.TryGetFeatureValue(PinchValue, out _)) hand = d;
            }
        }

        private static bool Tracked(InputDevice d) =>
            d.isValid && (!d.TryGetFeatureValue(CommonUsages.isTracked, out bool t) || t);

        private static Vector2 Axis(InputDevice d, InputFeatureUsage<Vector2> u) => d.TryGetFeatureValue(u, out Vector2 v) ? v : Vector2.zero;
        private static float Float(InputDevice d, InputFeatureUsage<float> u) => d.TryGetFeatureValue(u, out float v) ? v : 0f;
        private static bool Button(InputDevice d, InputFeatureUsage<bool> u) => d.TryGetFeatureValue(u, out bool v) && v;
    }
}
