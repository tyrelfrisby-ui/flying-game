using System.Collections.Generic;
using System.Runtime.InteropServices;
using FlyingGame.Bridge.Net;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Intercom + radio (owner 2026-10-01), over Plugins/iOS/VoiceIO.mm.
    ///   INTERCOM (OPTIONS checkbox): voice-activated, like a real intercom — talk and you hear yourself in your headset
    ///     (headphones only; on the speaker it would feed back). Your voice also goes into clips.
    ///   RADIO: hold TALK to transmit on the chosen frequency; every pilot in the room tuned to it hears you, through a
    ///     radio band with a squelch tail. 8 kHz mu-law in ~100 ms packets through the relay ("tune" / "v" messages),
    ///     relayed only to the same frequency. Received radio is mixed into the game sound (and so into clips).
    /// Lives on the aircraft root next to NetSession.
    /// </summary>
    public sealed class VoiceComms : MonoBehaviour
    {
        public static readonly string[] Frequencies = { "122.80", "122.75", "123.45" };
        public const int PacketBytes = 800;   // 100 ms of 8 kHz mu-law

        // ---- settings (OPTIONS) ----
        private static int _intercom = -1;
        public static bool IntercomOn
        {
            get { if (_intercom < 0) _intercom = PlayerPrefs.GetInt("voice.intercom", 0); return _intercom == 1; }
            set { _intercom = value ? 1 : 0; PlayerPrefs.SetInt("voice.intercom", _intercom); PlayerPrefs.Save(); }
        }
        private static string _freq;
        public static string Frequency
        {
            get { _freq ??= PlayerPrefs.GetString("voice.freq", Frequencies[0]); return _freq; }
            set { _freq = value; PlayerPrefs.SetString("voice.freq", value); PlayerPrefs.Save(); }
        }

        /// <summary>TALK button held (ViewPanel).</summary>
        public static bool PttHeld;
        /// <summary>Name of whoever is on the frequency right now (null when it's quiet).</summary>
        public static string Receiving { get; private set; }
        /// <summary>In a multiplayer room (the TALK button only shows then).</summary>
        public static bool RadioAvailable { get; private set; }
        /// <summary>One-line status for the UI when the mic can't be used (permission denied …), else null.</summary>
        public static string MicProblem { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
        private const string NativeLib = "__Internal";       // linked into the iOS app by Xcode
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private const string NativeLib = "AeroNative";       // Mac app: Plugins/macOS/AeroNative.bundle (tools/native-mac/build.sh)
#endif
#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
        [DllImport(NativeLib)] private static extern void VO_Init(int unityRate);
        [DllImport(NativeLib)] private static extern int VO_EnsureMic();
        [DllImport(NativeLib)] private static extern void VO_SetIntercom(int on);
        [DllImport(NativeLib)] private static extern void VO_SetPtt(int on);
        [DllImport(NativeLib)] private static extern int VO_ReadTx(byte[] dst, int max, out int ended);
        [DllImport(NativeLib)] private static extern int VO_TxAvailable();
        [DllImport(NativeLib)] private static extern void VO_PushRx(int slot, byte[] data, int len, int end);
        [DllImport(NativeLib)] private static extern void VO_SetRxVolume(float v);
        [DllImport(NativeLib)] private static extern unsafe void VO_RenderRx(float* data, int frames, int channels);
        [DllImport(NativeLib)] private static extern unsafe void VO_ReadClipVoice(float* data, int frames, int channels);
        private const bool Native = true;
#else
        private static void VO_Init(int unityRate) { }
        private static int VO_EnsureMic() => -2;
        private static void VO_SetIntercom(int on) { }
        private static void VO_SetPtt(int on) { }
        private static int VO_ReadTx(byte[] dst, int max, out int ended) { ended = 0; return 0; }
        private static int VO_TxAvailable() => 0;
        private static void VO_PushRx(int slot, byte[] data, int len, int end) { }
        private static void VO_SetRxVolume(float v) { }
        private static unsafe void VO_RenderRx(float* data, int frames, int channels) { }
        private static unsafe void VO_ReadClipVoice(float* data, int frames, int channels) { }
        private const bool Native = false;
#endif
        private static volatile bool _ready;

        /// <summary>FlightAudio's audio thread: add the radio receivers into the game buffer.</summary>
        public static unsafe void RenderRx(float[] data, int frames, int channels)
        {
            if (!_ready) return;
            fixed (float* p = data) VO_RenderRx(p, frames, channels);
        }

        /// <summary>ClipRecorder's audio thread: add the pilot's own voice into the clip's copy of the audio.</summary>
        public static unsafe void ReadClipVoice(float[] data, int frames, int channels)
        {
            if (!_ready) return;
            fixed (float* p = data) VO_ReadClipVoice(p, frames, channels);
        }

        private NetSession _net;
        private string _tunedSelf, _tunedFreq;
        private readonly byte[] _tx = new byte[PacketBytes * 4];
        private readonly Dictionary<string, int> _slots = new();
        private readonly Dictionary<string, float> _lastHeard = new();
        private int _nextSlot;
        private bool _micAsked;

        private void Awake()
        {
            VO_Init(AudioSettings.outputSampleRate);
            _ready = Native;
            _net = GetComponent<NetSession>();
        }

        private void OnEnable() { if (_net != null) _net.VoiceReceived += OnVoice; }
        private void OnDisable() { if (_net != null) _net.VoiceReceived -= OnVoice; }

        private void Update()
        {
            bool menu = SessionSettings.MenuOpen;
            bool ptt = PttHeld && !menu;
            if (!_micAsked && (IntercomOn || ptt)) _micAsked = true;
            if (_micAsked)
            {
                int st = VO_EnsureMic();
                MicProblem = st == -1 ? "Microphone is off - allow it in Settings > Aero Playground"
                           : st == -2 && Native ? "Microphone unavailable" : null;
            }
            VO_SetIntercom(IntercomOn && !menu ? 1 : 0);
            VO_SetPtt(ptt ? 1 : 0);
            VO_SetRxVolume(FlightAudio.MasterVolume);

            bool online = _net != null && _net.Connected;
            RadioAvailable = _net != null && _net.Active;
            // Tell the relay which frequency we're on (after every (re)connect and on a change).
            if (online && (_tunedSelf != _net.SelfId || _tunedFreq != Frequency))
            {
                _tunedSelf = _net.SelfId; _tunedFreq = Frequency;
                _net.SendQueued(new NetMsg { t = "tune", fq = Frequency });
            }

            // Transmitter: whole 100 ms packets while keyed, then the remainder flagged as the end of the call.
            while (VO_TxAvailable() >= PacketBytes)
            {
                int n = VO_ReadTx(_tx, PacketBytes, out int ended);
                if (online) _net.SendQueued(new NetMsg { t = "v", a = System.Convert.ToBase64String(_tx, 0, n), e = ended });
            }
            if (!ptt)
            {
                int n = VO_ReadTx(_tx, _tx.Length, out int ended);
                if (ended == 1 && online) _net.SendQueued(new NetMsg { t = "v", a = System.Convert.ToBase64String(_tx, 0, n), e = 1 });
            }

            // Who is on the frequency.
            string who = null; float now = Time.realtimeSinceStartup;
            foreach (var kv in _lastHeard) if (now - kv.Value < 0.5f) { who = _net?.PeerName(kv.Key) ?? "Pilot"; break; }
            Receiving = who;
        }

        private void OnVoice(NetMsg m)
        {
            if (m.fq != null && m.fq != Frequency) return;
            if (!_slots.TryGetValue(m.id, out int slot))
            {
                slot = _nextSlot++ % 7;   // slot 7 is the self-test's
                _slots[m.id] = slot;
            }
            byte[] bytes;
            try { bytes = string.IsNullOrEmpty(m.a) ? System.Array.Empty<byte>() : System.Convert.FromBase64String(m.a); }
            catch (System.FormatException) { return; }
            VO_PushRx(slot, bytes, bytes.Length, m.e == 1 ? 1 : 0);
            _lastHeard[m.id] = m.e == 1 ? -1f : Time.realtimeSinceStartup;
        }

        /// <summary>Self-test: a 2 s "transmission" (a warbling 700 Hz tone) on receiver slot 7.</summary>
        public static void InjectTestTransmission()
        {
            var b = new byte[16000];
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / 8000f;
                float v = 0.5f * Mathf.Sin(2f * Mathf.PI * 700f * t) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 3f * t));
                b[i] = MuLaw(v);
            }
            VO_PushRx(7, b, b.Length, 1);
        }

        private static byte MuLaw(float f)
        {
            int s = Mathf.RoundToInt(Mathf.Clamp(f, -1f, 1f) * 32635f);
            int sign = (s >> 8) & 0x80; if (sign != 0) s = -s; s += 0x84;
            int exp = 7; for (int m = 0x4000; (s & m) == 0 && exp > 0; m >>= 1) exp--;
            int mant = (s >> (exp + 3)) & 0x0F;
            return (byte)~(sign | (exp << 4) | mant);
        }
    }
}
