using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// LOCAL 2-PLAYER, SPLIT SCREEN (Mode 1: one iPhone / iPad / Mac, two gamepads, two aeroplanes; Ty 2026-11-05).
    /// The setting and the state. Off by default, and with it off nothing in the game changes: no second aeroplane, no
    /// second camera, <see cref="GamepadInput.Now"/> is the single-player read, every view is the one it always was.
    ///
    /// Not in a headset: the Quest's two-player game is two headsets on the same network (Mode 2, the LAN room), never
    /// two views inside one headset.
    /// </summary>
    public static class LocalSplit
    {
        private const string PrefEnabled = "set.localSplit2p";
        private static bool _enabled, _loaded;

        /// <summary>The player's choice (OPTIONS › Controls), saved. Takes effect on the next FLY / restart.</summary>
        public static bool Enabled
        {
            get { if (!_loaded) { _enabled = PlayerPrefs.GetInt(PrefEnabled, 0) == 1; _loaded = true; } return _enabled; }
            set { _enabled = value; _loaded = true; PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Split screen is possible here: a flat screen (not a headset).</summary>
        public static bool Allowed => !XrHeadset.Active;

        /// <summary>The second player's aeroplane is flying and the screen is split (set by <see cref="LocalCoop"/>).</summary>
        public static bool Active { get; internal set; }

        /// <summary>Why the second player did not get an aeroplane on the last FLY (shown beside the option), or null.</summary>
        public static string NotStartedReason { get; internal set; }

        public static int Players => Active ? 2 : 1;

        /// <summary>Player <paramref name="index"/>'s normalised viewport (Camera.rect, bottom-left origin).</summary>
        public static Rect ViewportFor(int index)
        {
            var r = FlyingGame.Core.LocalMp.SplitLayout.Viewport(index, Players, Screen.width, Screen.height);
            return new Rect((float)r.X, (float)r.Y, (float)r.W, (float)r.H);
        }

        /// <summary>Player <paramref name="index"/>'s view in GUI pixels (top-left origin), for OnGUI drawing.</summary>
        public static Rect GuiRectFor(int index)
        {
            Rect v = ViewportFor(index);
            float w = Screen.width, h = Screen.height;
            return new Rect(v.x * w, (1f - v.y - v.height) * h, v.width * w, v.height * h);
        }
    }
}
