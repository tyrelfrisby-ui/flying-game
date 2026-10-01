using System.Runtime.InteropServices;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Quick clips (owner 2026-10-01) over Plugins/iOS/QuickClips.mm: CLIP saves the last <see cref="ClipSeconds"/> of the
    /// screen (live flying or a replay) to Photos; REC records an open-ended clip. iOS keeps the rolling buffer from the
    /// first flight on; it shows its own screen-recording consent the first time.
    /// </summary>
    public static class QuickClips
    {
        public const double ClipSeconds = 15.0;   // ReplayKit's rolling buffer holds 15 s

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void QC_StartBuffering();
        [DllImport("__Internal")] private static extern int QC_IsRecording();
        [DllImport("__Internal")] private static extern int QC_IsBusy();
        [DllImport("__Internal")] private static extern void QC_ExportClip(double seconds);
        [DllImport("__Internal")] private static extern void QC_StartRecording();
        [DllImport("__Internal")] private static extern void QC_StopRecording();
        [DllImport("__Internal")] private static extern int QC_MessageSeq();
        [DllImport("__Internal")] private static extern string QC_Message();

        public static void EnsureBuffering() => QC_StartBuffering();
        public static bool Recording => QC_IsRecording() != 0;
        public static bool Busy => QC_IsBusy() != 0;
        public static void SaveClip() => QC_ExportClip(ClipSeconds);
        public static void ToggleRecording() { if (Recording) QC_StopRecording(); else QC_StartRecording(); }

        private static int _seenSeq;
        /// <summary>The newest status message ("Clip saved to Photos", an error …) once, or null.</summary>
        public static string PollMessage()
        {
            int seq = QC_MessageSeq();
            if (seq == _seenSeq) return null;
            _seenSeq = seq;
            return QC_Message();
        }
#else
        private static string _pending;
        public static void EnsureBuffering() { }
        public static bool Recording { get; private set; }
        public static bool Busy => false;
        public static void SaveClip() { _pending = "Clips are saved on the device only"; }
        public static void ToggleRecording() { Recording = !Recording; _pending = Recording ? "Recording (editor: nothing is saved)" : "Recording stopped"; }
        public static string PollMessage() { string m = _pending; _pending = null; return m; }
#endif
    }
}
