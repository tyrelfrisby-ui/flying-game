using UnityEngine;
using UnityEngine.XR;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A NATIVE headset (Meta Quest, through OpenXR) is driving the view. Static, like <see cref="VrMode"/>, so
    /// anything can ask without finding a component.
    ///
    /// This is NOT the Cardboard mode. <see cref="VrMode"/> is a phone in a viewer that the game splits and
    /// tracks itself from the gyro; this is a headset whose runtime does the stereo, the lens and the tracking,
    /// and hands the game a head pose. They never run together: <see cref="VrMode.Available"/> is false while
    /// this is <see cref="Active"/>, so the Cardboard rig and the mono device look both stand down, and the
    /// flat-screen overlays hide (<see cref="VrMode.SuppressFlatOverlays"/>).
    ///
    /// Only an Android build carries an XR loader (Assets/XR, Android only). On iOS and macOS there is no
    /// loader, <see cref="XRSettings.isDeviceActive"/> is false, and every line here is inert.
    ///
    /// THE KNOBS CODEX OWNS (Quest rendering quality) are here, in one place: <see cref="RenderScale"/>,
    /// <see cref="FoveationLevel"/> and <see cref="Prefer90Hz"/>. <see cref="QuestRig"/> applies them when the
    /// headset starts and whenever they change.
    /// </summary>
    public static class XrHeadset
    {
        private const string PrefHz90 = "quest.hz90";

        private static int _frame = -1;
        private static bool _active;

        /// <summary>True while a headset runtime is up and rendering. Cached per frame: it is asked from every
        /// overlay's OnGUI.</summary>
        public static bool Active
        {
            get
            {
                if (ForceActiveForTesting) return true;
                if (_frame == Time.frameCount) return _active;
                _frame = Time.frameCount;
                _active = XRSettings.enabled && XRSettings.isDeviceActive;
                return _active;
            }
        }

        /// <summary>Editor tools only: pretend a headset is up so the rig and the menu can be built and looked at
        /// in a batch run with no headset. Never set in a player build.</summary>
        public static bool ForceActiveForTesting { get; set; }

        // ---- refresh rate (Options-style setting, persisted) ----

        private static bool _loaded, _hz90;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _hz90 = PlayerPrefs.GetInt(PrefHz90, 0) == 1;
        }

        /// <summary>72 Hz by default — the Quest's own default and the budget everything here is planned for
        /// (docs/quest-performance.md). 90 Hz is opt-in from the VR menu: smoother, 2.8 ms less per frame.</summary>
        public static bool Prefer90Hz
        {
            get { Load(); return _hz90; }
            set
            {
                Load();
                if (_hz90 == value) return;
                _hz90 = value;
                PlayerPrefs.SetInt(PrefHz90, value ? 1 : 0);
                PlayerPrefs.Save();
                SettingsChanged?.Invoke();
            }
        }

        public static float WantedRefreshHz => Prefer90Hz ? 90f : 72f;

        /// <summary>
        /// Set by the Android-only FlyingGame.Quest assembly (QuestDisplayRateFeature) to ask the OpenXR runtime
        /// for a display rate through XR_FB_display_refresh_rate. Null where there is no such runtime — the request
        /// is then simply not made. Returns whether the runtime accepted it.
        /// </summary>
        public static System.Func<float, bool> RefreshRateRequester;

        /// <summary>The rate the display is actually running at, Hz, or 0 if the runtime will not say.</summary>
        public static float CurrentRefreshHz
        {
            get
            {
                var d = Display;
                return d != null && d.TryGetDisplayRefreshRate(out float hz) ? hz : 0f;
            }
        }

        // ---- render scale and fixed foveation ----

        private static float _renderScale = 1.0f, _foveation = 0.5f;

        /// <summary>Eye-buffer resolution relative to the runtime's recommended size. 1.0 on Quest 3S is the
        /// recommended eye buffer; Codex tunes it.</summary>
        public static float RenderScale
        {
            get => _renderScale;
            set { _renderScale = Mathf.Clamp(value, 0.5f, 1.5f); SettingsChanged?.Invoke(); }
        }

        /// <summary>Fixed foveated rendering, 0 (off) … 1 (strongest). 0.5 is "medium": the periphery, which the
        /// lens blurs anyway, is shaded at lower rate. Built-in pipeline: the legacy foveation API, the only one it
        /// supports (OpenXR's Meta feature says so in its own validation).</summary>
        public static float FoveationLevel
        {
            get => _foveation;
            set { _foveation = Mathf.Clamp01(value); SettingsChanged?.Invoke(); }
        }

        /// <summary>Raised when any knob above changes; <see cref="QuestRig"/> re-applies.</summary>
        public static event System.Action SettingsChanged;

        // ---- recentre and the menu ----

        /// <summary>Put the head where it is now at the seat's eye, facing the nose. Controller button, hand pinch,
        /// the VR menu, entering the cockpit and the runtime's own recentre all come through here.</summary>
        public static event System.Action RecentreRequested;
        public static void Recentre() => RecentreRequested?.Invoke();

        /// <summary>True while the VR menu is up. Flight input stands down while it is.</summary>
        public static bool MenuOpen { get; internal set; }

        /// <summary>True when the pilot has put the controllers down and is using his hands. They open the menu and
        /// recentre; they do not fly.</summary>
        public static bool HandsOnly { get; internal set; }

        /// <summary>The headset's display subsystem, if one is running.</summary>
        public static XRDisplaySubsystem Display
        {
            get
            {
                _displays.Clear();
                SubsystemManager.GetSubsystems(_displays);
                foreach (var d in _displays) if (d.running) return d;
                return null;
            }
        }
        private static readonly System.Collections.Generic.List<XRDisplaySubsystem> _displays = new();
    }
}
