using System.Collections.Generic;
using FlyingGame.Sim.Support;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// HELP, and the diagnostics block behind it (handoff 2026-10-17, Glass ↔ Aero; docs/support/).
    ///
    /// HELP opens a web page in the browser and that is all it does. There is no chat in the app, nothing is
    /// sent, and the URL carries the build and the platform — see <see cref="SupportLink"/>, which holds the
    /// rules and is tested headless. This class is only the part that needs Unity: what the build is, what
    /// the device is, and what the run has complained about.
    ///
    /// The warning log is a RING of the last few warnings and errors, kept in memory. It exists because a
    /// flight-model report is nearly useless without it ("the Cub porpoised" versus "the trim solver did not
    /// converge at 46 kt") and nearly impossible to get out of a customer by asking. A crash is kept across
    /// launches in PlayerPrefs, because the run that crashed is not the run that comes and asks for help.
    ///
    /// Nothing here ever transmits any of it. <see cref="CopyDiagnostics"/> puts text on the clipboard; the
    /// customer pastes it into the chat, or does not, and the privacy answer stays Data Not Collected.
    /// </summary>
    public static class SupportDesk
    {
        /// <summary>The ring's size. Twelve lines reach the block (SupportLink.MaxNotes); a few more are kept
        /// so a burst of repeats cannot push the interesting one out before anybody asks.</summary>
        public const int RingSize = 24;

        private const string PrefLastCrash = "support.lastCrash";

        private static readonly List<string> _notes = new();
        private static string _lastNote = "";
        private static int _repeat;
        private static bool _hooked;

        /// <summary>What the last run died of, if it died. Read once at startup; survives a reinstall only as
        /// long as PlayerPrefs do, which is the right lifetime for it.</summary>
        public static string LastCrash { get; private set; } = "";

        /// <summary>Set by the UI after a copy, so the button can say it worked.</summary>
        public static string Note = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            LastCrash = PlayerPrefs.GetString(PrefLastCrash, "");
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Assert) return;
            if (type == LogType.Exception)
            {
                // A crash outlives its run: the pilot who comes asking is on the launch AFTER it.
                string when = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                PlayerPrefs.SetString(PrefLastCrash, $"{when}  {condition}");
                PlayerPrefs.Save();
            }
            // Unity repeats a per-frame warning thousands of times; one line with a count is the useful form.
            if (condition == _lastNote) { _repeat++; if (_notes.Count > 0) _notes[_notes.Count - 1] = $"{condition}  (×{_repeat + 1})"; return; }
            _lastNote = condition; _repeat = 0;
            _notes.Add(type == LogType.Exception ? "crash: " + condition : condition);
            if (_notes.Count > RingSize) _notes.RemoveAt(0);
        }

        /// <summary>Which of the three the handoff asks for. iPadOS reports itself as iOS, so the model decides.</summary>
        public static SupportPlatform Platform
        {
            get
            {
                if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
                    return SupportPlatform.MacOs;
                string model = SystemInfo.deviceModel ?? "";
                return model.StartsWith("iPad") ? SupportPlatform.IpadOs : SupportPlatform.Ios;
            }
        }

        public static string Url => SupportLink.Url(Application.version, Platform);

        /// <summary>Hands the URL to the browser. The only outward-facing thing in this file.</summary>
        public static void OpenHelp()
        {
            Note = "";
            Application.OpenURL(Url);
        }

        private static SupportLink.Facts Facts() => new()
        {
            Version = Application.version,
            Platform = Platform,
            DeviceModel = SystemInfo.deviceModel,
            OperatingSystem = SystemInfo.operatingSystem,
            Aircraft = SessionSettings.AircraftId,
            LastCrash = LastCrash,
            Notes = _notes,
            PilotName = SessionSettings.PilotName,
        };

        /// <summary>The block, as text. Nothing sends it.</summary>
        public static string Diagnostics() => SupportLink.Diagnostics(Facts());

        /// <summary>
        /// Puts the block on the clipboard, which is the customer starting it — the handoff's word is
        /// "customer-started only", and a clipboard is the one channel where they can read what they are
        /// about to hand over and change their mind.
        /// </summary>
        public static void CopyDiagnostics()
        {
            string d = Diagnostics();
            GUIUtility.systemCopyBuffer = d;
            int lines = d.Split('\n').Length;
            Note = $"Copied {lines} lines. Paste it into the chat if you want to — it is not sent from here.";
        }
    }
}
