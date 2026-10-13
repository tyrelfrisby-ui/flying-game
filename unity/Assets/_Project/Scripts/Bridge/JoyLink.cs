using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// JOYSTICK LINK (owner 2026-10-12: "option 1 for now"): an RC radio's dongle (or any joystick) stays plugged into the
    /// Mac, and Aero Playground on the iPad / iPhone flies from it over the local network. iOS can't read a USB joystick
    /// itself; the Mac game sends the raw channels (and its calibration) and the iPad / iPhone feeds them into the same
    /// JoystickInput, so the setup, profiles, guns and flap switch work as on the Mac. Both ends are opt-in (Options), so
    /// nobody gets the local-network prompt unless they turn it on. Transport: Plugins/iOS/JoyLink.mm (Bonjour + UDP).
    /// Packets: "AJ1D" seq u16 · 8 × float32 raw axes · u32 buttons · name (u8 length + UTF-8); "AJ1P" + profile JSON (1 Hz).
    /// </summary>
    public static class JoyLink
    {
#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
#if UNITY_IOS
        private const string Lib = "__Internal";
#else
        private const string Lib = "AeroNative";
#endif
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern int JL_ServeStart(string name);
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern void JL_ServeStop();
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern int JL_ServeState(out int peers);
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern void JL_Send(byte[] buf, int len);
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern void JL_RecvStart();
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern void JL_RecvStop();
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern int JL_Recv(byte[] buf, int max, out float age, byte[] name, int nameMax);
        [System.Runtime.InteropServices.DllImport(Lib)] private static extern int JL_RecvProfile(byte[] buf, int max);
        private const bool Available = true;
#else
        private static int JL_ServeStart(string name) => 0;
        private static void JL_ServeStop() { }
        private static int JL_ServeState(out int peers) { peers = 0; return 0; }
        private static void JL_Send(byte[] buf, int len) { }
        private static void JL_RecvStart() { }
        private static void JL_RecvStop() { }
        private static int JL_Recv(byte[] buf, int max, out float age, byte[] name, int nameMax) { age = 999f; return 0; }
        private static int JL_RecvProfile(byte[] buf, int max) => 0;
        private const bool Available = false;
#endif
        /// <summary>This end of the link: the Mac sends, the iPad / iPhone receives.</summary>
        public static bool IsSender => !Application.isMobilePlatform;

        /// <summary>The Options switch: Mac = share its joystick; iPad / iPhone = fly with the Mac's.</summary>
        public static bool On
        {
            get => PlayerPrefs.GetInt("joylink.on", 0) == 1;
            set { PlayerPrefs.SetInt("joylink.on", value ? 1 : 0); PlayerPrefs.Save(); KeepRunning(); }
        }

        private static int _frame = -1;
        private static bool _running;
        private static readonly byte[] _buf = new byte[1500], _nameBuf = new byte[128];
        private static float _sendAt, _profAt;
        private static ushort _seq;

        // Receiver state (the latest packet).
        private static readonly float[] _axes = new float[JoystickInput.AxisCount];
        private static uint _buttons;
        private static string _joyName, _macName;
        private static float _age = 999f;
        private static int _state;   // receiver: 0 off, 1 searching, 2 connected · sender: 0 off, 1 starting, 2 sharing, 3 failed
        private static int _peers;

        /// <summary>Once a frame (called from JoystickInput and the controls).</summary>
        public static void Tick()
        {
            if (!Available || _frame == Time.frameCount) return;
            _frame = Time.frameCount;
            bool want = On && (!IsSender || JoystickInput.LocalName != null);
            if (want != _running)
            {
                _running = want;
                Debug.Log($"[JoyLink] {(IsSender ? "share" : "receive")} {(want ? "on" : "off")} · joystick {JoystickInput.LocalName ?? "none"}");
                if (IsSender) { if (want) JL_ServeStart(SystemInfo.deviceName); else JL_ServeStop(); }
                else { if (want) JL_RecvStart(); else { JL_RecvStop(); _age = 999f; } }
            }
            if (!_running) { _state = 0; _peers = 0; return; }
            int was = _state, peersWas = _peers;
            if (IsSender) Send(); else Receive();
            if (_state != was || _peers != peersWas) Debug.Log($"[JoyLink] state {_state} · peers {_peers}");
        }

        private static void Send()
        {
            _state = JL_ServeState(out _peers);
            if (_peers == 0) return;
            float t = Time.unscaledTime;
            if (t - _sendAt < 0.01f) return;   // (100 Hz at most)
            _sendAt = t;
            int n = 0;
            _buf[n++] = (byte)'A'; _buf[n++] = (byte)'J'; _buf[n++] = (byte)'1'; _buf[n++] = (byte)'D';
            _seq++; _buf[n++] = (byte)(_seq & 0xff); _buf[n++] = (byte)(_seq >> 8);
            for (int i = 1; i <= JoystickInput.AxisCount; i++) { System.BitConverter.TryWriteBytes(new System.Span<byte>(_buf, n, 4), JoystickInput.LocalRaw(i)); n += 4; }
            uint b = 0; for (int i = 0; i < 32; i++) if (JoystickInput.LocalButton(i)) b |= 1u << i;
            System.BitConverter.TryWriteBytes(new System.Span<byte>(_buf, n, 4), b); n += 4;
            byte[] nm = Encoding.UTF8.GetBytes(JoystickInput.LocalName ?? ""); int nl = Mathf.Min(nm.Length, 120);
            _buf[n++] = (byte)nl; System.Array.Copy(nm, 0, _buf, n, nl); n += nl;
            JL_Send(_buf, n);
            // The calibration once a second: a new iPad flies with the Mac's setup straight away.
            var p = JoystickInput.Profile;
            if (p != null && t - _profAt > 1f)
            {
                _profAt = t;
                byte[] j = Encoding.UTF8.GetBytes("AJ1P" + JsonConvert.SerializeObject(p));
                if (j.Length <= _buf.Length) JL_Send(j, j.Length);
            }
        }

        private static void Receive()
        {
            int len = JL_Recv(_buf, _buf.Length, out _age, _nameBuf, _nameBuf.Length);
            int z = System.Array.IndexOf(_nameBuf, (byte)0); _macName = Encoding.UTF8.GetString(_nameBuf, 0, z < 0 ? _nameBuf.Length : z);
            if (len <= 0) { _state = -len; return; }
            _state = 2;
            if (len >= 4 + 2 + 32 + 4 + 1 && _buf[3] == (byte)'D')
            {
                int n = 6;
                for (int i = 0; i < JoystickInput.AxisCount; i++) { _axes[i] = System.BitConverter.ToSingle(_buf, n); n += 4; }
                _buttons = System.BitConverter.ToUInt32(_buf, n); n += 4;
                int nl = _buf[n++]; _joyName = nl > 0 && n + nl <= len ? Encoding.UTF8.GetString(_buf, n, nl) : null;
            }
            int pl = JL_RecvProfile(_buf, _buf.Length);
            if (pl > 4)
            {
                // Adopt the Mac's calibration when this device has none for that joystick (a setup here still wins).
                try
                {
                    var p = JsonConvert.DeserializeObject<JoystickProfile>(Encoding.UTF8.GetString(_buf, 4, pl - 4));
                    if (p?.Name != null && JoystickInput.Load(p.Name) == null) JoystickInput.Save(p);
                }
                catch { }
            }
        }

        // Ticks every frame from launch (the start menu too), not only while the flight controls run.
        private sealed class Runner : MonoBehaviour { private void Update() => Tick(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Available) return;
            var go = new GameObject("JoyLink") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go); go.AddComponent<Runner>();
            KeepRunning();
        }

        // The Mac sends while you fly the iPad, so the game keeps running behind other windows while it shares.
        private static bool _bgWas, _bgSet;
        private static void KeepRunning()
        {
            if (!IsSender) return;
            if (!_bgSet) { _bgSet = true; _bgWas = Application.runInBackground; }
            Application.runInBackground = On || _bgWas;
        }

        /// <summary>Receiver: a fresh packet (under half a second old).</summary>
        public static bool Connected => !IsSender && _running && _age < 0.5f && _joyName != null;
        public static string JoystickName => Connected ? _joyName : null;
        public static float Axis(int i) => Connected && i >= 1 && i <= JoystickInput.AxisCount ? _axes[i - 1] : 0f;
        public static bool Button(int b) => Connected && b >= 0 && b < 32 && (_buttons & (1u << b)) != 0;

        /// <summary>For Options: what the link is doing, in a few words.</summary>
        public static string StatusText
        {
            get
            {
                if (!On) return "";
                if (IsSender)
                {
                    if (JoystickInput.LocalName == null) return "no joystick connected";
                    return _state == 3 ? "can't share (local network blocked?)" : _peers > 0 ? $"flying {_peers} device{(_peers > 1 ? "s" : "")}" : "waiting for an iPad / iPhone";
                }
                return Connected ? $"{_joyName} via {_macName}" : _state == 2 ? "connected — no joystick on the Mac" : "looking for the Mac…";
            }
        }
    }
}
