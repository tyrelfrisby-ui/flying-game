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
        private static int _narrate = -1;
        /// <summary>
        /// Put the pilot's own voice into clips so a tutorial can be talked over (Ty 2026-11-08: "next request for
        /// record is audio input (mic) for my tutorial recordings which gamers might like too").
        ///
        /// This rides the INTERCOM's voice-activated path rather than adding a native mode of its own, and that is a
        /// deliberate trade. VoiceIO.mm gates the mic with `ptt || (intercom &amp;&amp; vox)`, and the clip is fed the gated
        /// signal, so narration only needs that gate opened — which the intercom already does, with its compression and
        /// its headphones-only sidetone for monitoring. A new native entry point would mean a new export from
        /// VoiceIO.mm, and the Mac bundle built from it is a TRACKED binary: the committed one would no longer have the
        /// symbol and the Mac app would throw on the first call. Not worth it for a gate that exists.
        ///
        /// What that inherits: it is voice-activated, so it opens when he talks and closes between sentences, which for
        /// narration is if anything what you want. Unlike the intercom it is NOT cut while a menu is open, because
        /// talking through the options screen is the point (see ClipRecorder.ShowMyScreen).
        ///
        /// The mic is open the whole time this is ticked, because the clip buffer is always rolling — CLIP saves the
        /// last minutes, so a voice that only arrived after REC would be too late.
        /// </summary>
        public static bool NarrateClips
        {
            get { if (_narrate < 0) _narrate = PlayerPrefs.GetInt("voice.narrate", 0); return _narrate == 1; }
            set { _narrate = value ? 1 : 0; PlayerPrefs.SetInt("voice.narrate", _narrate); PlayerPrefs.Save(); }
        }
        private static string _freq;
        public static string Frequency
        {
            get { _freq ??= PlayerPrefs.GetString("voice.freq", Frequencies[0]); return _freq; }
            set { _freq = value; PlayerPrefs.SetString("voice.freq", value); PlayerPrefs.Save(); }
        }

        /// <summary>TALK button held (ViewPanel).</summary>
        public static bool PttHeld;

        /// <summary>
        /// THE VOX BUTTON on the flight screen (Ty 2026-11-10: "Add a vox button on the screen"). Armed, the
        /// microphone is held open and the voice gate opens on voice activity; off, the microphone opens only while
        /// PTT is held. PTT is unchanged either way.
        ///
        /// DELIBERATELY NOT PERSISTED, and this is the one design decision here worth arguing about. Every other
        /// voice setting goes in PlayerPrefs, and that is exactly what cost Ty an evening: "Record my voice in
        /// clips" is persisted under voice.narrate, so once ticked it held the microphone open from launch, every
        /// launch — "Force quit and restart did not fix it", because a force-quit reloads the tick. A hot mic must
        /// not outlive the session that asked for it, so this one resets to off every launch. The cost is one tap
        /// per flight; the benefit is that a forgotten VOX can never listen across launches. The within-session
        /// case is covered by <see cref="MicOpen"/>, which the button shows whatever the reason.
        /// </summary>
        public static bool VoxOn;

        /// <summary>
        /// IS THE MICROPHONE ACTUALLY OPEN RIGHT NOW? Read from the native engine's own state, not derived from the
        /// settings, so the indicator cannot drift from the behaviour: VoiceIO.mm keeps the engine's lifetime equal
        /// to its MicWanted(), so "gState is 1 or 2" and "the mic is open" are the same statement.
        ///
        /// This is true for reasons the pilot did not choose this flight — clip narration holding it, or the 1.5 s
        /// unkey linger — which is the whole point of showing it.
        /// </summary>
        public static bool MicOpen { get; private set; }

        /// <summary>Peak level the microphone is hearing, 0..1, for the VOX button's meter. 0 while it is shut.</summary>
        public static float MicLevel01 { get; private set; }
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
        [DllImport(NativeLib)] private static extern void VO_SetTvWantsAudio(int wants);
        [DllImport(NativeLib)] private static extern int VO_EnsureMic();
        // Both of these have been exported since 2026-10-01 and are in the committed Mac bundle, so declaring them
        // now costs nothing. Nothing had called them until the VOX button needed to show the real mic state.
        [DllImport(NativeLib)] private static extern int VO_State();
        [DllImport(NativeLib)] private static extern float VO_MicLevel();
        [DllImport(NativeLib)] private static extern void VO_SetIntercom(int on);
        [DllImport(NativeLib)] private static extern void VO_SetPtt(int on);
        [DllImport(NativeLib)] private static extern int VO_ReadTx(byte[] dst, int max, out int ended);
        [DllImport(NativeLib)] private static extern int VO_TxAvailable();
        [DllImport(NativeLib)] private static extern void VO_PushRx(int slot, byte[] data, int len, int end);
        [DllImport(NativeLib)] private static extern void VO_SetRxVolume(float v);
        [DllImport(NativeLib)] private static extern unsafe void VO_RenderRx(float* data, int frames, int channels);
        [DllImport(NativeLib)] private static extern unsafe void VO_ReadClipVoice(float* data, int frames, int channels);
        [DllImport(NativeLib)] private static extern void VO_SetRoute(int keepOffTv, int forceSpeaker);
        [DllImport(NativeLib)] private static extern int VO_RouteOutputs(byte[] dst, int max);
        private const bool Native = true;
#else
        private static void VO_Init(int unityRate) { }
        private static void VO_SetTvWantsAudio(int wants) { }
        private static int VO_EnsureMic() => -2;
        private static int VO_State() => 0;
        private static float VO_MicLevel() => 0f;
        private static void VO_SetIntercom(int on) { }
        private static void VO_SetPtt(int on) { }
        private static int VO_ReadTx(byte[] dst, int max, out int ended) { ended = 0; return 0; }
        private static int VO_TxAvailable() => 0;
        private static void VO_PushRx(int slot, byte[] data, int len, int end) { }
        private static void VO_SetRxVolume(float v) { }
        private static unsafe void VO_RenderRx(float* data, int frames, int channels) { }
        private static unsafe void VO_ReadClipVoice(float* data, int frames, int channels) { }
        private static void VO_SetRoute(int keepOffTv, int forceSpeaker) { }
        private static int VO_RouteOutputs(byte[] dst, int max) => 0;
        private const bool Native = false;
#endif
        private static volatile bool _ready;

        /// <summary>Only iOS has a TV route to keep the radio off (Q7); the Mac's output is the one its owner picked.</summary>
#if UNITY_IOS && !UNITY_EDITOR
        private const bool RoutesAroundTv = true;
#else
        private const bool RoutesAroundTv = false;
#endif

        /// <summary>One line for Options › Audio while the radio is being kept off a TV, else empty.</summary>
        public static string RouteNote { get; private set; } = "";

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
        private readonly byte[] _routeBuf = new byte[256];
        private float _nextRouteCheck;
        private bool _routeSent;
        private bool _tvWantsAudioSent;
        private FlyingGame.Core.VoiceRoute _route;

        private void Awake()
        {
            VO_Init(AudioSettings.outputSampleRate);
            _ready = Native;
            _net = GetComponent<NetSession>();
        }

        /// <summary>
        /// Q7 (Ty 2026-11-04: "radio/intercom audio stays on the phone or headset, not the TV"). Four times a second: is voice
        /// in use, is a TV connected, where is the sound going; Core TvAudioPolicy decides and VoiceIO.mm carries it out.
        /// A radio call that arrives while the route is still on the TV is held silent by VoiceIO itself, not by this poll.
        /// </summary>
        private void KeepOffTheTv()
        {
            _nextRouteCheck = Time.unscaledTime + 0.25f;
            int n = VO_RouteOutputs(_routeBuf, _routeBuf.Length);
            string outputs = n > 0 ? System.Text.Encoding.UTF8.GetString(_routeBuf, 0, n) : "";
            // "Voice in use" means the microphone can actually be open, or a radio call can actually arrive — not
            // that a checkbox is ticked. Narration and the VOX button are the two things that hold the mic with
            // nobody talking (VoiceIO.mm MicWanted), so a TV cannot have the audio while either is on: play-and-
            // record has no HDMI output to give it. Left out, the policy would ask for the mic to be released,
            // MicWanted would ask for it straight back, and the two would fight every quarter second.
            //
            // THE INTERCOM TICK IS NOT IN THIS LIST, and that is the fix for the silent TV rather than a hole in
            // the rule. A tick with the VOX off opens no microphone and plays no sidetone, so there is nothing to
            // keep off the TV — and counting it would hold the session in play-and-record, which is precisely how
            // an idle intercom kept HDMI silent for ever.
            bool voice = RadioAvailable || NarrateClips || VoxOn;
            var route = FlyingGame.Core.TvAudioPolicy.Decide(voice, TvOutput.ExternalDisplay, FlyingGame.Core.TvAudioPolicy.FromPortTypes(outputs));
            // A TV with nobody talking has to have the mic RELEASED, not just the route changed: play-and-record
            // has no HDMI output to offer (VoiceIO.mm ApplySession).
            bool tvWantsAudio = TvOutput.ExternalDisplay && !voice;
            if (!_routeSent || tvWantsAudio != _tvWantsAudioSent)
            {
                VO_SetTvWantsAudio(tvWantsAudio ? 1 : 0);
                _tvWantsAudioSent = tvWantsAudio;
            }
            if (!_routeSent || route.KeepOffTv != _route.KeepOffTv || route.ForceSpeaker != _route.ForceSpeaker)
            {
                // The first call too: VoiceIO starts with both off, and saying so costs nothing.
                VO_SetRoute(route.KeepOffTv ? 1 : 0, route.ForceSpeaker ? 1 : 0);
                Debug.Log($"[Voice] {route} · outputs {outputs} · voice {voice} · display {TvOutput.ExternalDisplay}");
                _routeSent = true;
            }
            _route = route;
            RouteNote = !route.KeepOffTv ? ""
                      : route.ForceSpeaker ? "TV connected: the radio and intercom play on this device, not the TV"
                      : "TV connected: the radio and intercom stay in your headset, not the TV";
        }

        private void OnEnable() { if (_net != null) _net.VoiceReceived += OnVoice; }
        private void OnDisable() { if (_net != null) _net.VoiceReceived -= OnVoice; }

        private void Update()
        {
            bool menu = SessionSettings.MenuOpen;
            bool ptt = PttHeld && !menu;
            bool vox = VoxOn && !menu;
            if (!_micAsked && (ptt || vox || NarrateClips)) _micAsked = true;
            if (_micAsked)
            {
                int st = VO_EnsureMic();
                MicProblem = st == -1 ? "Microphone is off - allow it in Settings > Aero Playground"
                           : st == -2 && Native ? "Microphone unavailable" : null;
            }
            // Narration keeps the gate open through a menu: the options screen is a thing he is filming and talking
            // over. VOX does not — a menu is not a flight, and a hot mic should not survive one.
            //
            // The mask is VO_SetIntercom's: bit 0 the intercom (gate + sidetone), bit 1 narration (gate, and it
            // holds the mic), bit 2 the VOX button (holds the mic). Ty 2026-11-10: "The app is listening to my mic
            // even when I do[n't] have PTT pressed unchecked intercom just now" — the intercom tick used to hold
            // the mic by itself whenever a headset was attached; now only bits 1 and 2 do, and bit 2 is the one the
            // pilot controls from the flight screen and which resets every launch.
            VO_SetIntercom(((IntercomOn && !menu) ? 1 : 0) | (NarrateClips ? 2 : 0) | (vox ? 4 : 0));
            VO_SetPtt(ptt ? 1 : 0);
            // The real state, straight back out of the engine, for the VOX button's live indicator. 1 is "starting"
            // and counts as open: the mic is about to be, and an indicator like this should light early, not late.
            int micState = Native ? VO_State() : 0;
            MicOpen = micState == 1 || micState == 2;
            MicLevel01 = MicOpen ? Mathf.Clamp01(VO_MicLevel() * 6f) : 0f;   // peak is small for speech; 6x to read
            VO_SetRxVolume(FlightAudio.MasterVolume);
            if (RoutesAroundTv && Time.unscaledTime >= _nextRouteCheck) KeepOffTheTv();

            bool online = _net != null && _net.Connected;
            RadioAvailable = _net != null && _net.Active;
            // Tell the relay which frequency we're on (after every (re)connect and on a change).
            if (online && (_tunedSelf != _net.SelfId || _tunedFreq != Frequency))
            {
                _tunedSelf = _net.SelfId; _tunedFreq = Frequency;
                _net.SendQueued(new NetMsg { t = "tune", fq = Frequency });
            }

            // Transmitter: whole 100 ms packets while keyed, then the remainder flagged as the end of the call.
            //
            // The packet carries the FREQUENCY it was spoken on (2026-10-17). The relay does not need it —
            // it stamps the frequency it recorded from our "tune" and sends the call only to the pilots on
            // it — but on a shared network or a field link there is no relay, every pilot hears every
            // packet, and the only thing standing between them is the test in OnVoice below. That test
            // passes a packet with no frequency on it, so until now a LAN room's radio did nothing at all:
            // all twelve pilots heard all twelve, whatever any of them had the box tuned to.
            while (VO_TxAvailable() >= PacketBytes)
            {
                int n = VO_ReadTx(_tx, PacketBytes, out int ended);
                if (online) _net.SendQueued(new NetMsg { t = "v", fq = Frequency, a = System.Convert.ToBase64String(_tx, 0, n), e = ended });
            }
            if (!ptt)
            {
                int n = VO_ReadTx(_tx, _tx.Length, out int ended);
                if (ended == 1 && online) _net.SendQueued(new NetMsg { t = "v", fq = Frequency, a = System.Convert.ToBase64String(_tx, 0, n), e = 1 });
            }

            // Who is on the frequency.
            string who = null; float now = Time.realtimeSinceStartup;
            foreach (var kv in _lastHeard) if (now - kv.Value < 0.5f) { who = _net?.PeerName(kv.Key) ?? "Pilot"; break; }
            Receiving = who;
        }

        private void OnVoice(NetMsg m)
        {
            // A packet with no frequency on it is from a build older than 2026-10-17 (the relay always
            // stamps one): let it through rather than go deaf to it.
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
