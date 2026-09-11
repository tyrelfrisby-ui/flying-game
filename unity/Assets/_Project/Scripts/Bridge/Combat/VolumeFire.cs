using System.Runtime.InteropServices;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Volume-up as the gun trigger (Plugins/iOS/VolumeFireButton.mm). Armed only while the guns are hot so the
    /// volume buttons behave normally everywhere else. <see cref="Held"/> is true while volume-up steps keep arriving
    /// (holding the button repeats them every ~0.1–0.2 s).</summary>
    internal static class VolumeFire
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FlyingGame_VolumeFireArm();
        [DllImport("__Internal")] private static extern void FlyingGame_VolumeFireDisarm();
        [DllImport("__Internal")] private static extern double FlyingGame_VolumeFireSinceLastPress();
        [DllImport("__Internal")] private static extern int FlyingGame_VolumeFirePressCount();
#endif
        private static bool _armed;
        public const double HoldWindowSec = 0.35;

        public static void SetArmed(bool armed)
        {
            if (armed == _armed) return;
            _armed = armed;
#if UNITY_IOS && !UNITY_EDITOR
            try { if (armed) FlyingGame_VolumeFireArm(); else FlyingGame_VolumeFireDisarm(); } catch (System.Exception e) { Debug.LogWarning("volume fire: " + e.Message); }
#endif
        }

        public static bool Held
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (!_armed) return false;
                try { return FlyingGame_VolumeFireSinceLastPress() < HoldWindowSec; } catch (System.Exception) { return false; }
#else
                return false;
#endif
            }
        }

        public static int PressCount
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                try { return FlyingGame_VolumeFirePressCount(); } catch (System.Exception) { return 0; }
#else
                return 0;
#endif
            }
        }
    }
}
