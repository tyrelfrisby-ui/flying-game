using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;
#if AERO_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace FlyingGame.Bridge
{
    /// <summary>
    /// USB RUDDER PEDALS: the device side. Lists the game controllers this platform can see, copies each one's raw
    /// axes into a <see cref="PedalDevice"/> every frame, and hands them to Core's <see cref="PedalSession"/>, which
    /// decides which device is the pedals, maps and calibrates it, and logs every device that comes or goes
    /// ("[Pedals] connected: … VID:PID … axes …" in logcat / Player.log). TouchFlightControls folds
    /// <see cref="Frame"/> into the one control path (PedalBlend), so nothing else reads pedals. docs/RUDDER-PEDALS.md.
    ///
    /// Three readers, the first that sees any game controller wins:
    ///  1. UNITY'S INPUT SYSTEM, wherever its backend runs (the Quest build switches it on for the Android build only,
    ///     QuestBuild). On Android a USB HID joystick arrives as an AndroidJoystick whose layout exposes only X / Y, so
    ///     the axes Android lists for it (motionAxes: X, Y, RZ, RUDDER, BRAKE, GAS …) are read straight out of its
    ///     state block, by Android axis code. Elsewhere (a HID joystick on a Mac or PC) every axis control the HID
    ///     layout built. VID/PID come from the device description on both.
    ///  2. THE MAC PLAYER'S OWN HID READER (AeroNative HJ_*, the one the joystick setup uses): every joystick-class
    ///     device, eight axes each, by name (it does not report VID/PID).
    ///  3. UNITY'S OLD INPUT MANAGER, the generic joystick fallback (the editor, and the Quest if the Input System
    ///     backend is off): joysticks 1-4, axes 1-12, through the PJoy{j}A{a} entries in ProjectSettings/InputManager.
    ///
    /// iOS: the system gives apps gamepads (GameController framework) but not generic USB HID devices, so pedals are not
    /// seen there; nothing here breaks, the session just never finds any.
    ///
    /// Allocation: none per frame. Device lists are rebuilt only when the Input System reports a change, or once a
    /// second for the two readers that have to poll.
    /// </summary>
    public static class RudderPedals
    {
        private sealed class PrefsStore : IPedalStore
        {
            public string Load(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            public void Save(string key, string value) { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); }
        }

        /// <summary>No pedals at all (the widget's capture instance, as for the joystick).</summary>
        private static readonly bool Off = System.Environment.GetEnvironmentVariable("AERO_NO_JOYSTICK") == "1" || System.Environment.GetEnvironmentVariable("AERO_WIDGET_CAPTURE") == "1";

        private static PedalSession _session;
        public static PedalSession Session => _session ??= new PedalSession(new PrefsStore(), s => Debug.Log(s));

        /// <summary>This frame's pedals (inactive: none, switched off, or being calibrated).</summary>
        public static PedalFrame Frame { get { Tick(); return Session.Frame; } }

        private static int _frame = -1, _calPageFrame = -1000;
        private static readonly List<PedalDevice> _devices = new();

        /// <summary>A menu showing the calibration calls this every frame it is drawn. A calibration nobody can see
        /// is cancelled: while one runs the pedals fly nothing, and a page closed mid-way must not leave them dead
        /// with no word said (CLAUDE.md, "A silent exclusive mode is usually a bug in disguise").</summary>
        public static void CalibrationShown() => _calPageFrame = Time.frameCount;
        private static string _backend = "";

        /// <summary>Once a frame (idempotent): find devices, read their axes, update the session. Called from
        /// TouchFlightControls before anything else (menus open or not), and by both menus.</summary>
        public static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            if (Off) return;
            float now = Time.unscaledTime;
            string backend = "";
#if AERO_INPUT_SYSTEM
            if (!_isBroken)
            {
                try { if (InputSystemScan()) backend = "InputSystem"; }
                catch (System.Exception ex) { _isBroken = true; _is.Clear(); Debug.LogWarning($"[Pedals] Input System unavailable, using the fallback readers: {ex.Message}"); }
            }
#endif
            if (backend.Length == 0 && MacScan(now)) backend = "MacHID";
            if (backend.Length == 0 && LegacyScan(now)) backend = "Legacy";
            if (backend != _backend)
            {
                if (backend.Length > 0) Debug.Log($"[Pedals] reading game controllers through {backend}");
                _backend = backend;
            }
            _devices.Clear();
            switch (backend)
            {
#if AERO_INPUT_SYSTEM
                case "InputSystem": foreach (var e in _is) { e.Read(); _devices.Add(e.Info); } break;
#endif
                case "MacHID": foreach (var e in _mac) { e.Read(); _devices.Add(e.Info); } break;
                case "Legacy": foreach (var e in _legacy) { e.Read(); _devices.Add(e.Info); } break;
            }
            Session.Update(_devices, Time.unscaledDeltaTime);
            if (Session.Calibration != null && Time.frameCount - _calPageFrame > 30)
            {
                Session.CancelCalibration();
                Debug.Log("[Pedals] calibration cancelled: its page was closed (nothing saved)");
            }
        }

        // ---- 1. the Input System ------------------------------------------------------------------------------

#if AERO_INPUT_SYSTEM
        private sealed class IsEntry
        {
            public InputDevice Device;
            public PedalDevice Info;
            public AxisControl[] Controls;   // HID / generic: the axis controls, in the order of Info.AxisNames
            public int[] AndroidCodes;       // Android: the motion axis codes, ditto

            public void Read()
            {
                double[] a = Info.Axes;
#if UNITY_ANDROID
                if (AndroidCodes != null) { ReadAndroid(Device, AndroidCodes, a); return; }
#endif
                for (int i = 0; i < a.Length; i++) a[i] = Controls[i].ReadValue();
            }
        }

        private static readonly List<IsEntry> _is = new();
        private static bool _isDirty = true, _isHooked, _isBroken;

        /// <summary>True when the Input System sees at least one game controller.</summary>
        private static bool InputSystemScan()
        {
            if (!_isHooked)
            {
                _isHooked = true;
                InputSystem.onDeviceChange += (d, change) => _isDirty = true;
            }
            if (_isDirty)
            {
                _isDirty = false;
                var keep = new List<IsEntry>();
                foreach (InputDevice d in InputSystem.devices)
                {
                    if (!(d is Joystick) && !(d is Gamepad) && d.description.interfaceName != "HID") continue;
                    IsEntry old = _is.Find(e => e.Device == d);
                    keep.Add(old ?? Describe(d));
                }
                _is.Clear();
                _is.AddRange(keep);
            }
            return _is.Count > 0;
        }

        private static IsEntry Describe(InputDevice d)
        {
            var info = new PedalDevice
            {
                Id = "is:" + d.deviceId, Backend = d.description.interfaceName == "Android" ? "Android" : d.description.interfaceName ?? "InputSystem",
                Name = !string.IsNullOrEmpty(d.description.product) ? (string.IsNullOrEmpty(d.description.manufacturer) || d.description.product.StartsWith(d.description.manufacturer)
                       ? d.description.product : d.description.manufacturer + " " + d.description.product) : d.displayName,
                IsGamepad = d is Gamepad,
            };
            var e = new IsEntry { Device = d, Info = info };
            string caps = d.description.capabilities;
            int[] motion = null;
            if (!string.IsNullOrEmpty(caps))
            {
                try
                {
                    var j = Newtonsoft.Json.Linq.JObject.Parse(caps);
                    info.VendorId = (int?)j["vendorId"] ?? 0;
                    info.ProductId = (int?)j["productId"] ?? 0;
                    if (j["motionAxes"] is Newtonsoft.Json.Linq.JArray ax) { motion = new int[ax.Count]; for (int i = 0; i < ax.Count; i++) motion[i] = (int)ax[i]; }
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Pedals] {info.Name}: unreadable capabilities ({ex.Message})"); }
            }
#if UNITY_ANDROID
            if (info.Backend == "Android" && motion != null && AndroidStateReadable(d))
            {
                e.AndroidCodes = motion;
                info.AxisNames = new string[motion.Length];
                for (int i = 0; i < motion.Length; i++) info.AxisNames[i] = PedalCatalog.AndroidAxisName(motion[i]);
                info.Axes = new double[motion.Length];
                return e;
            }
#endif
            var controls = new List<AxisControl>();
            var names = new List<string>();
            int buttons = 0;
            foreach (InputControl c in d.allControls)
            {
                if (c.synthetic) continue;
                if (c is ButtonControl) { if (c.parent == d) buttons++; continue; }
                if (!(c is AxisControl ax) || !(c.parent == d || c.parent is StickControl)) continue;
                controls.Add(ax);
                names.Add(c.name.ToLowerInvariant());
            }
            e.Controls = controls.ToArray();
            info.AxisNames = names.ToArray();
            info.ButtonCount = buttons;
            info.Axes = new double[e.Controls.Length];
            return e;
        }

#if UNITY_ANDROID
        // The AndroidJoystick layout exposes only the stick (X, Y); the rest of Android's 48 motion axes are in its
        // state, a float per axis code (AndroidGameControllerState.axis), copied out once a frame by the public
        // CopyState (a struct copy: no garbage).
        private static bool AndroidStateReadable(InputDevice d) =>
            d.stateBlock.format == UnityEngine.InputSystem.Android.LowLevel.AndroidGameControllerState.kFormat;

        private static unsafe void ReadAndroid(InputDevice d, int[] codes, double[] dst)
        {
            if (!d.added) { System.Array.Clear(dst, 0, dst.Length); return; }
            d.CopyState(out UnityEngine.InputSystem.Android.LowLevel.AndroidGameControllerState st);
            for (int i = 0; i < dst.Length; i++)
            {
                int code = codes[i];
                dst[i] = code >= 0 && code < UnityEngine.InputSystem.Android.LowLevel.AndroidGameControllerState.MaxAxes ? st.axis[code] : 0.0;
            }
        }
#endif
#endif

        // ---- 2. the Mac player's HID reader ---------------------------------------------------------------------

        private sealed class NativeEntry
        {
            public int Dev;
            public PedalDevice Info;
            public void Read() { for (int i = 0; i < Info.Axes.Length; i++) Info.Axes[i] = JoystickInput.NativeAxis(Dev, i); }
        }

        private static readonly List<NativeEntry> _mac = new();
        private static float _macAt = -10f;

        private static bool MacScan(float now)
        {
            if (!JoystickInput.NativeAvailable) return false;
            if (now - _macAt < 1f) return _mac.Count > 0;
            _macAt = now;
            int n = JoystickInput.NativeCount;
            bool same = n == _mac.Count;
            for (int i = 0; same && i < n; i++) same = JoystickInput.NativeName(i) == _mac[i].Info.Name;
            if (!same)
            {
                _mac.Clear();
                for (int i = 0; i < n; i++)
                {
                    var names = new string[JoystickInput.AxisCount];
                    for (int a = 0; a < names.Length; a++) names[a] = "axis " + (a + 1);
                    _mac.Add(new NativeEntry
                    {
                        Dev = i,
                        Info = new PedalDevice { Id = $"mac:{i}:{JoystickInput.NativeName(i)}", Name = JoystickInput.NativeName(i) ?? "Joystick", Backend = "MacHID",
                                                 AxisNames = names, Axes = new double[names.Length] },
                    });
                }
            }
            return _mac.Count > 0;
        }

        // ---- 3. the old Input Manager ---------------------------------------------------------------------------

        public const int LegacyJoysticks = 4, LegacyAxes = 12;
        private static readonly string[,] LegacyAxisNames = MakeLegacyNames();

        private static string[,] MakeLegacyNames()
        {
            var n = new string[LegacyJoysticks, LegacyAxes];
            for (int j = 0; j < LegacyJoysticks; j++)
                for (int a = 0; a < LegacyAxes; a++) n[j, a] = $"PJoy{j + 1}A{a + 1}";   // ProjectSettings/InputManager.asset
            return n;
        }

        private sealed class LegacyEntry
        {
            public int Joy;
            public PedalDevice Info;
            public void Read()
            {
                double[] a = Info.Axes;
                for (int i = 0; i < a.Length; i++)
                {
                    try { a[i] = Input.GetAxisRaw(LegacyAxisNames[Joy, i]); }
                    catch (System.ArgumentException) { a[i] = 0.0; }   // an InputManager without the PJoy entries
                }
            }
        }

        private static readonly List<LegacyEntry> _legacy = new();
        private static float _legacyAt = -10f;

        private static bool LegacyScan(float now)
        {
            if (now - _legacyAt < 1f) return _legacy.Count > 0;
            _legacyAt = now;
            string[] names;
            try { names = Input.GetJoystickNames(); }
            catch (System.InvalidOperationException) { return false; }   // the old Input Manager is switched off
            int n = Mathf.Min(names.Length, LegacyJoysticks);
            bool same = true;
            int live = 0;
            for (int j = 0; j < n; j++)
            {
                if (string.IsNullOrEmpty(names[j])) continue;   // a slot left empty by an unplugged device
                same &= live < _legacy.Count && _legacy[live].Joy == j && _legacy[live].Info.Name == names[j];
                live++;
            }
            same &= live == _legacy.Count;
            if (!same)
            {
                _legacy.Clear();
                for (int j = 0; j < n; j++)
                {
                    if (string.IsNullOrEmpty(names[j])) continue;
                    var axes = new string[LegacyAxes];
                    for (int a = 0; a < LegacyAxes; a++) axes[a] = "axis " + (a + 1);
                    _legacy.Add(new LegacyEntry
                    {
                        Joy = j,
                        Info = new PedalDevice { Id = $"legacy:{j}:{names[j]}", Name = names[j], Backend = "Legacy", AxisNames = axes, Axes = new double[LegacyAxes] },
                    });
                }
            }
            return _legacy.Count > 0;
        }
    }
}
