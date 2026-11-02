using System.Collections.Generic;
using FlyingGame.Core.Vr;
using UnityEngine;
using UnityEngine.XR;
#if AERO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.Controls;
#endif

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
    /// THE MENU BUTTON IS READ TWICE. Since the rudder pedals (f238050) the Quest APK runs with "Both" input
    /// handlers, and OpenXR then feeds the Input System's XR devices as well as these legacy ones. Nothing showed
    /// that the legacy menu usage stopped working (the 2026-11-02 "menu never opens" turned out to be the menu's
    /// own object, destroyed with the aeroplane: QuestMenu), but a button a pilot cannot do without should not
    /// hang on one path: ☰ (and the left stick click) counts when EITHER the legacy usage or the Input System's
    /// left-hand XR controller (<c>menu</c> / <c>menuButton</c> / <c>start</c>) says it is down, and
    /// <see cref="MenuVia"/> says which. <see cref="LogDevices"/> writes what both systems see, at start and on
    /// every connect / disconnect, as [QuestInput] lines.
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

        /// <summary>Which path last saw ☰ down: "legacy XR", "Input System" or "both". For the [QuestInput] line.</summary>
        public static string MenuVia { get; private set; } = "";

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
            // ☰ and the left stick click through the Input System as well (see the class notes): whichever fires.
            bool legacyMenu = f.Menu;
            ReadInputSystem(out bool isMenu, out bool isClick);
            if (l || isMenu || isClick)
            {
                f.Menu = legacyMenu || isMenu;
                f.LeftStickClick |= isClick;
                if (f.Menu) MenuVia = legacyMenu && isMenu ? "both" : legacyMenu ? "legacy XR" : "Input System";
            }
            bool lh = !l && Tracked(_leftHand), rh = !r && Tracked(_rightHand);
            f.HandsOnly = !l && !r && (lh || rh);
            if (f.HandsOnly)
            {
                f.LeftPinch = lh ? Float(_leftHand, PinchValue) : 0.0;
                f.RightPinch = rh ? Float(_rightHand, PinchValue) : 0.0;
            }
            _f = f;
            _any = l || r || f.HandsOnly || f.Menu;
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

        // ---- the Input System's view of the same controller ----

#if AERO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private static bool _isDirty = true, _isBroken, _isWatching;
        private static ButtonControl _isMenu, _isClick;
        private static string _isWhat = "";
#endif

        private static void ReadInputSystem(out bool menu, out bool click)
        {
            menu = click = false;
#if AERO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (_isBroken) return;
            try
            {
                if (!_isWatching)
                {
                    _isWatching = true;
                    UnityEngine.InputSystem.InputSystem.onDeviceChange += (d, c) =>
                    {
                        if (c != UnityEngine.InputSystem.InputDeviceChange.Added && c != UnityEngine.InputSystem.InputDeviceChange.Removed
                            && c != UnityEngine.InputSystem.InputDeviceChange.Reconnected && c != UnityEngine.InputSystem.InputDeviceChange.Disconnected) return;
                        _isDirty = true;
                        if (IsXr(d)) LogDevices($"Input System {c}: {d.displayName ?? d.name}");
                    };
                }
                if (_isDirty) FindInputSystemLeft();
                menu = _isMenu != null && _isMenu.isPressed;
                click = _isClick != null && _isClick.isPressed;
            }
            catch (System.Exception e)
            {
                _isBroken = true;
                Debug.LogWarning($"[QuestInput] Input System not readable, legacy XR only: {e.Message}");
            }
#endif
        }

#if AERO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        /// <summary>An XR device in the Input System: OpenXR's devices come in on the "XRInputV1" interface, with layouts
        /// such as OculusTouchControllerProfile / HandInteraction. By name, so this does not need the Input System's
        /// optional XR assembly.</summary>
        private static bool IsXr(UnityEngine.InputSystem.InputDevice d)
        {
            string i = d.description.interfaceName ?? "", l = d.layout ?? "";
            return i.Contains("XR") || l.Contains("XR") || l.Contains("Touch") || l.Contains("Oculus") || l.Contains("Hand");
        }

        private static bool IsLeft(UnityEngine.InputSystem.InputDevice d)
        {
            foreach (var u in d.usages) if (u == UnityEngine.InputSystem.CommonUsages.LeftHand) return true;
            return false;
        }

        private static ButtonControl FirstButton(UnityEngine.InputSystem.InputDevice d, params string[] names)
        {
            foreach (string n in names) if (d.TryGetChildControl(n) is ButtonControl b) return b;
            return null;
        }

        /// <summary>The left-hand XR controller in the Input System and its menu and stick-click buttons. Looked up on
        /// a device change, never per frame.</summary>
        private static void FindInputSystemLeft()
        {
            _isDirty = false;
            _isMenu = _isClick = null;
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!d.added || !IsXr(d) || !IsLeft(d)) continue;
                ButtonControl m = FirstButton(d, "menu", "menuButton", "start", "menuPressed");
                if (m == null) continue;
                _isMenu = m;
                _isClick = FirstButton(d, "thumbstickClicked", "primary2DAxisClick", "joystickClicked", "thumbstickClick");
                break;
            }
            string what = _isMenu != null ? $"{_isMenu.path}" + (_isClick != null ? $" + {_isClick.path}" : "") : "none";
            if (what != _isWhat)
            {
                _isWhat = what;
                Debug.Log($"[QuestInput] Input System menu button: {what}");
            }
        }
#endif

        // ---- diagnostics ----

        private static bool _legacyWatching;
        private static readonly List<InputDevice> _logDevs = new();
        private static readonly List<InputFeatureUsage> _usages = new();

        /// <summary>Write what BOTH input systems see as [QuestInput] lines: each legacy XR device (its name — the OpenXR
        /// interaction profile's device — its characteristics and whether it has a menu usage), each Input System
        /// XR device (layout, usages, menu control). At the headset's start and on every connect / disconnect; never
        /// per frame.</summary>
        public static void LogDevices(string why)
        {
            try
            {
                if (!_legacyWatching)
                {
                    _legacyWatching = true;
                    InputDevices.deviceConnected += d => { _leftCtl = _rightCtl = _leftHand = _rightHand = default; LogDevices($"legacy XR connected: {d.name}"); };
                    InputDevices.deviceDisconnected += d => { _leftCtl = _rightCtl = _leftHand = _rightHand = default; LogDevices($"legacy XR disconnected: {d.name}"); };
                }
                var sb = new System.Text.StringBuilder();
                sb.Append($"[QuestInput] devices ({why}); input handler: ");
#if ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
                sb.Append("Both");
#elif ENABLE_INPUT_SYSTEM
                sb.Append("Input System only");
#else
                sb.Append("old Input Manager only");
#endif
                _logDevs.Clear();
                InputDevices.GetDevices(_logDevs);
                sb.Append($" | legacy XR {_logDevs.Count}:");
                foreach (InputDevice d in _logDevs)
                {
                    _usages.Clear();
                    d.TryGetFeatureUsages(_usages);
                    bool menu = false, click = false;
                    foreach (var u in _usages) { if (u.name == CommonUsages.menuButton.name) menu = true; if (u.name == CommonUsages.primary2DAxisClick.name) click = true; }
                    sb.Append($" [\"{d.name}\" ({d.manufacturer}) {d.characteristics}, {_usages.Count} usages, menu {(menu ? "yes" : "NO")}, stick click {(click ? "yes" : "no")}, tracked {Tracked(d)}]");
                }
#if AERO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                int n = 0;
                var isb = new System.Text.StringBuilder();
                foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
                {
                    if (!IsXr(d)) continue;
                    n++;
                    ButtonControl m = FirstButton(d, "menu", "menuButton", "start", "menuPressed");
                    isb.Append($" [\"{d.displayName ?? d.name}\" layout {d.layout}, usages {string.Join("/", d.usages)}, menu {(m != null ? m.name : "none")}]");
                }
                sb.Append($" | Input System XR {n}:").Append(isb);
                _isDirty = true;
#endif
                Debug.Log(sb.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[QuestInput] device listing failed: {e.Message}");
            }
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
