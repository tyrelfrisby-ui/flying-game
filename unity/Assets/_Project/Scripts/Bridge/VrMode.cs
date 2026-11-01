using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Is the game in a headset, and with what lens. Static and PlayerPrefs-backed, like
    /// <see cref="CockpitView.DeviceLookEnabled"/> and for the same reason: Options must be able to show and
    /// change it whether or not a cockpit exists right now (CLAUDE.md, "Put a setting in the tab a pilot would
    /// look in").
    ///
    /// VR SUPERSEDES THE MONO DEVICE LOOK while it is on. They both read the same gyro, and both applying would
    /// double every head movement; <see cref="CockpitView"/> checks <see cref="On"/> and stands down.
    /// Turning VR off puts the mono view back exactly as it was, including the pilot's own look gain.
    /// </summary>
    public static class VrMode
    {
        private const string PrefOn = "vr.on";
        private const string PrefIpd = "vr.ipdM";

        private static bool _loaded, _on;
        private static float _ipdM = 0.063f;
        private static VrLensConfig _lens;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _on = PlayerPrefs.GetInt(PrefOn, 0) == 1;
            _ipdM = PlayerPrefs.GetFloat(PrefIpd, (float)Lens.IpdM);
        }

        /// <summary>Only offered where the phone can actually track its own attitude. A headset without a gyro
        /// is a stereoscope, which is not what this is for.</summary>
        public static bool Available => SystemInfo.supportsGyroscope || ForceAvailableForTesting;

        /// <summary>Editor checks and the shot tool run on a Mac, which reports no gyroscope. They set this to
        /// build the rig anyway and drive the head themselves; it is never set in a player build.</summary>
        public static bool ForceAvailableForTesting { get; set; }

        /// <summary>True while the game is drawing side-by-side for a headset.</summary>
        public static bool On
        {
            get { Load(); return _on && Available; }
            set
            {
                Load();
                if (_on == value) return;
                _on = value;
                PlayerPrefs.SetInt(PrefOn, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>The PILOT's interpupillary distance, metres, 55–75 mm. The lens spacing is the headset's and
        /// is not this.</summary>
        public static float IpdM
        {
            get { Load(); return _ipdM; }
            set
            {
                Load();
                float v = Mathf.Clamp(value, (float)VrLensConfig.MinIpdM, (float)VrLensConfig.MaxIpdM);
                if (Mathf.Approximately(_ipdM, v)) return;
                _ipdM = v;
                PlayerPrefs.SetFloat(PrefIpd, v);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Raised when VR is switched on or off, so the rig can build or tear down and the orientation
        /// lock can go on or come off.</summary>
        public static event System.Action Changed;

        /// <summary>Ask the rig to re-zero the head wherever it is pointing now. The rig listens.</summary>
        public static event System.Action RecentreRequested;
        public static void Recentre() => RecentreRequested?.Invoke();

        /// <summary>The viewer profile, from StreamingAssets, loaded once. Falls back to the built-in generic
        /// Cardboard v2 values if the file is missing, so VR never fails to start over a config.</summary>
        public static VrLensConfig Lens
        {
            get
            {
                if (_lens != null) return _lens;
                _lens = UnityVrLensLoader.Load();
                return _lens;
            }
        }

        /// <summary>Re-read the profile (editor tooling; the shot tool uses it after writing a variant).</summary>
        public static void ForgetLens() => _lens = null;

        /// <summary>True when the flat-screen overlays must not draw: in a headset they would be smeared across
        /// both eyes and sit at the wrong depth. Codex's VR HUD replaces them.</summary>
        public static bool SuppressFlatOverlays => On;
    }
}
