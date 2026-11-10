using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge.Companion
{
    /// <summary>One line on the link: newline-delimited JSON, every field optional (docs/TV-COMPANION-PLAN.md §5.3).</summary>
    [System.Serializable]
    internal sealed class LinkMsg
    {
        public string kind, nonce, mac, dev, name, mode, proof, key, reason;
        public bool ok;
    }

    /// <summary>A game found on the network (Bonjour name and its TXT record).</summary>
    [System.Serializable] public sealed class FoundRow { public string name, role, id, v; }

    /// <summary>An event from AeroLink.swift.</summary>
    [System.Serializable]
    internal sealed class LinkEvent
    {
        public string ev, line, err;
        public int c, port, keys, suite;
        public bool @out, handshake;
        public FoundRow[] found;
    }

    /// <summary>
    /// COMPANION LINK, the pairing and the encrypted channel (Ty 2026-11-04: Q9 "both QR and PIN, QR recommended",
    /// Q10 "TLS-PSK encryption in v1"). The Mac is the game (<see cref="Host"/>), an iPad or iPhone the companion
    /// (<see cref="Client"/>). Every connection is TLS-PSK (AeroLink.swift); inside it the Mac challenges and the device
    /// proves which secret it holds (Core CompanionAuth), because a TLS server cannot say which of its keys a client used.
    ///
    ///   pairing (QR or PIN):  Mac → challenge{nonce, mac}  device → pair{mode, dev, name, proof}
    ///                         Mac → paired{ok, key, name, proof}       (the fresh link key, inside TLS)
    ///   every later link:     Mac → challenge{nonce, mac}  device → link{dev, proof}   Mac → result{ok, proof}
    ///
    /// What travels over the link once it is up (controls, commands, state) is the next phase of feature 4; today an
    /// authenticated link carries hello / state and nothing that moves the aeroplane.
    ///
    /// Nothing runs until it is asked for: the Mac listens only while a device is paired or PAIR A DEVICE is open, and a
    /// device browses only while it has a Mac paired or is pairing. Inert on the Quest and in the Editor.
    /// </summary>
    public static class CompanionLink
    {
        /// <summary>The Mac is the game; iPad and iPhone are companions.</summary>
        public static bool IsHost => AeroLinkNative.Available && Application.platform == RuntimePlatform.OSXPlayer && !Widget.AeroWidget.IsWidget;
        public static bool IsClient => AeroLinkNative.Available && Application.platform == RuntimePlatform.IPhonePlayer;

        private const string IdKey = "companion.id";
        private const double ProofTimeout = 10;

        /// <summary>This install's id (not secret: it names the device, the keys are in the Keychain).</summary>
        public static string SelfId
        {
            get
            {
                string id = PlayerPrefs.GetString(IdKey, "");
                if (!CompanionPairing.IsDeviceId(id)) { id = CompanionPairing.NewDeviceId(); PlayerPrefs.SetString(IdKey, id); PlayerPrefs.Save(); }
                return id;
            }
        }

        public static string SelfName => string.IsNullOrWhiteSpace(SystemInfo.deviceName) ? "Aero Playground" : SystemInfo.deviceName;

        private static Runner _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!IsHost && !IsClient) return;
            var go = new GameObject("CompanionLink") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            if (IsClient)
            {
                // QR (recommended): the Camera app opens aeroplay://pair?... in this app.
                Application.deepLinkActivated += Client.OpenLink;
                if (!string.IsNullOrEmpty(Application.absoluteURL)) Client.OpenLink(Application.absoluteURL);
            }
            Host.Wake();
            Client.Wake();
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                string e;
                int guard = 0;
                while (guard++ < 64 && (e = AeroLinkNative.Poll()) != null)
                {
                    LinkEvent ev;
                    try { ev = JsonUtility.FromJson<LinkEvent>(e); } catch { continue; }
                    if (ev == null) continue;
                    if (IsHost) Host.OnEvent(ev); else Client.OnEvent(ev);
                }
                if (IsHost) Host.Tick(); else if (IsClient) Client.Tick();
            }

            private void OnApplicationQuit() => AeroLinkNative.StopAll();
        }

        internal static void Send(int conn, LinkMsg m) => AeroLinkNative.Send(conn, JsonUtility.ToJson(m));

        internal static LinkMsg Parse(string line)
        {
            try { return JsonUtility.FromJson<LinkMsg>(line); } catch { return null; }
        }

        // =============================================================================================================
        /// <summary>The Mac: PAIR A DEVICE, the paired devices, and the listener that admits them.</summary>
        public static class Host
        {
            private const string DevicesKey = "companion.devices";   // "id|name" lines; the keys are in the Keychain
            private static readonly Dictionary<string, string> _devices = new();   // id → name
            private static readonly Dictionary<int, Pending> _pending = new();
            private static readonly Dictionary<int, string> _linked = new();     // conn → device id
            private static PairingWindow _window;
            private static bool _listening, _keysDirty = true;
            private static double _nextState;

            private sealed class Pending { public string Nonce; public double At; }

            public static PairingWindow Window => _window != null && _window.IsOpen(Now) ? _window : null;
            public static int PairedCount => _devices.Count;
            public static int ConnectedCount => _linked.Count;
            public static string Status { get; private set; } = "";
            public static string LastResult { get; private set; } = "";
            private static double Now => Time.realtimeSinceStartupAsDouble;

            internal static void Wake()
            {
                if (!IsHost) return;
                _devices.Clear();
                foreach (string row in PlayerPrefs.GetString(DevicesKey, "").Split('\n'))
                {
                    int bar = row.IndexOf('|');
                    if (bar > 0 && CompanionPairing.IsDeviceId(row.Substring(0, bar))) _devices[row.Substring(0, bar)] = row.Substring(bar + 1);
                }
                _keysDirty = true;
            }

            /// <summary>PAIR A DEVICE: a new PIN and QR secret, two minutes, and the listener on.</summary>
            public static void OpenPairing()
            {
                if (!IsHost) return;
                _window = new PairingWindow(CompanionPairing.NewPin(), CompanionPairing.NewKey(), Now);
                _qrTexture = null;
                _keysDirty = true;
                LastResult = "";
                Debug.Log("[Companion] pairing window open (QR and PIN, 2 min)");
            }

            public static void ClosePairing() { _window = null; _qrTexture = null; _keysDirty = true; }

            public static void ForgetAll()
            {
                foreach (string id in _devices.Keys) AeroLinkNative.SecretDelete("dev:" + id);
                _devices.Clear();
                Save();
                foreach (int c in new List<int>(_linked.Keys)) AeroLinkNative.Close(c);
                _linked.Clear();
                _keysDirty = true;
            }

            private static void Save()
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in _devices) sb.Append(kv.Key).Append('|').Append(kv.Value.Replace('\n', ' ')).Append('\n');
                PlayerPrefs.SetString(DevicesKey, sb.ToString());
                PlayerPrefs.Save();
            }

            private static byte[] KeyOf(string dev)
            {
                string hex = AeroLinkNative.SecretGet("dev:" + dev);
                return hex != null && hex.Length == 64 ? FromHex(hex) : null;
            }

            /// <summary>The QR code's link (what the Camera reads): the Mac's id and name and the one-time secret.</summary>
            public static string OfferUri => Window != null ? CompanionPairing.OfferUri(SelfId, SelfName, Window.QrSecret) : null;

            private static Texture2D _qrTexture;

            /// <summary>The QR code as a texture (point-filtered modules with a white quiet zone), or null.</summary>
            public static Texture2D QrTexture
            {
                get
                {
                    if (Window == null) return null;
                    if (_qrTexture != null) return _qrTexture;
                    byte[] m = AeroLinkNative.Qr(OfferUri, out int side);
                    if (m == null) return null;
                    const int quiet = 4;
                    int n = side + 2 * quiet;
                    var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[n * n];
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            int mx = x - quiet, my = y - quiet;
                            bool dark = mx >= 0 && my >= 0 && mx < side && my < side && m[my * side + mx] == 1;
                            px[(n - 1 - y) * n + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);   // texture rows are bottom-up
                        }
                    t.SetPixels32(px); t.Apply();
                    return _qrTexture = t;
                }
            }

            internal static void Tick()
            {
                bool want = _devices.Count > 0 || Window != null;
                if (_window != null && !_window.IsOpen(Now)) { _window = null; _qrTexture = null; _keysDirty = true; }
                if (want && (!_listening || _keysDirty))
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (string dev in _devices.Keys)
                    {
                        byte[] k = KeyOf(dev);
                        if (k != null) sb.Append(CompanionPairing.LinkIdentity(dev)).Append('\t').Append(CompanionPairing.Hex(CompanionPairing.LinkPsk(k, SelfId, dev))).Append('\n');
                    }
                    if (Window != null)
                    {
                        sb.Append(CompanionPairing.PinIdentity).Append('\t').Append(CompanionPairing.Hex(CompanionPairing.PinPsk(Window.Pin, SelfId))).Append('\n');
                        sb.Append(CompanionPairing.QrIdentity).Append('\t').Append(CompanionPairing.Hex(CompanionPairing.QrPsk(Window.QrSecret, SelfId))).Append('\n');
                    }
                    AeroLinkNative.SetServerKeys(sb.ToString().TrimEnd('\n'));
                    if (!_listening) AeroLinkNative.Listen("Aero Playground " + SelfName, SelfId);
                    _listening = true; _keysDirty = false;
                }
                else if (!want && _listening) { AeroLinkNative.StopListening(); _listening = false; }

                // A connection that has not proved itself in 10 s is closed (Glass's rule).
                foreach (var kv in new List<KeyValuePair<int, Pending>>(_pending))
                    if (Now - kv.Value.At > ProofTimeout) { AeroLinkNative.Close(kv.Key); _pending.Remove(kv.Key); }

                if (_linked.Count > 0 && Now >= _nextState)
                {
                    _nextState = Now + 2;
                    var state = new LinkMsg { kind = "state", name = SelfName, ok = true };
                    foreach (int c in _linked.Keys) Send(c, state);
                }
                Status = !_listening ? "" : $"{_devices.Count} paired · {_linked.Count} connected";
            }

            internal static void OnEvent(LinkEvent e)
            {
                switch (e.ev)
                {
                    case "listening": Debug.Log($"[Companion] listening on port {e.port} with {e.keys} key(s)"); break;
                    case "listen-failed": Debug.LogWarning($"[Companion] listener failed: {e.err}"); _listening = false; break;
                    case "open":
                        if (e.@out) return;
                        string nonce = CompanionAuth.NewNonce();
                        _pending[e.c] = new Pending { Nonce = nonce, At = Now };
                        Send(e.c, new LinkMsg { kind = "challenge", nonce = nonce, mac = SelfId, name = SelfName });
                        Debug.Log($"[Companion] connection {e.c} through TLS (suite 0x{e.suite:X4}); challenged");
                        break;
                    case "line": OnLine(e.c, e.line); break;
                    case "closed":
                        _pending.Remove(e.c);
                        if (_linked.Remove(e.c)) Debug.Log($"[Companion] link {e.c} closed: {e.err}");
                        // A handshake that never got through TLS while the window is open counts against it: that is what
                        // guessing PINs looks like from here.
                        if (e.handshake && Window != null) { Window.Failed(); Debug.Log($"[Companion] failed pairing handshake ({Window?.TriesLeft ?? 0} tries left)"); }
                        break;
                }
            }

            private static void OnLine(int c, string line)
            {
                LinkMsg m = Parse(line);
                if (m == null) return;
                if (_linked.TryGetValue(c, out string who))
                {
                    if (m.kind == "hello") Send(c, new LinkMsg { kind = "state", name = SelfName, ok = true });
                    return;   // controls and commands: the next phase of feature 4
                }
                if (!_pending.TryGetValue(c, out Pending p)) return;
                _pending.Remove(c);
                string dev = m.dev;
                if (!CompanionPairing.IsDeviceId(dev)) { Refuse(c, "bad device id"); return; }

                if (m.kind == "link")
                {
                    byte[] k = KeyOf(dev);
                    byte[] psk = k != null ? CompanionPairing.LinkPsk(k, SelfId, dev) : null;
                    if (psk == null || !CompanionAuth.Same(m.proof, CompanionAuth.LinkProof(psk, p.Nonce, SelfId, dev))) { Refuse(c, "not paired"); return; }
                    foreach (var kv in new List<KeyValuePair<int, string>>(_linked))
                        if (kv.Value == dev) { AeroLinkNative.Close(kv.Key); _linked.Remove(kv.Key); }   // one live link per device
                    _linked[c] = dev;
                    Send(c, new LinkMsg { kind = "result", ok = true, name = SelfName, proof = CompanionAuth.ServerProof(psk, p.Nonce, SelfId, dev) });
                    Debug.Log($"[Companion] {Name(dev)} linked");
                    return;
                }
                if (m.kind == "pair")
                {
                    PairingWindow w = Window;
                    byte[] psk = w == null ? null : m.mode == "pin" ? CompanionPairing.PinPsk(w.Pin, SelfId) : m.mode == "qr" ? CompanionPairing.QrPsk(w.QrSecret, SelfId) : null;
                    if (psk == null || !CompanionAuth.Same(m.proof, CompanionAuth.PairProof(psk, p.Nonce, SelfId, dev)))
                    {
                        w?.Failed();
                        Refuse(c, w == null ? "pairing is not open" : "wrong code");
                        return;
                    }
                    byte[] key = CompanionPairing.NewKey();
                    if (!AeroLinkNative.SecretSet("dev:" + dev, CompanionPairing.Hex(key))) { Refuse(c, "could not store the key"); return; }
                    string name = string.IsNullOrWhiteSpace(m.name) ? "device" : m.name.Length > 40 ? m.name.Substring(0, 40) : m.name;
                    _devices[dev] = name;
                    Save();
                    w.Used();
                    _window = null; _qrTexture = null; _keysDirty = true;
                    Send(c, new LinkMsg { kind = "paired", ok = true, key = CompanionPairing.Base64Url(key), mac = SelfId, name = SelfName,
                                          proof = CompanionAuth.ServerProof(psk, p.Nonce, SelfId, dev) });
                    LastResult = $"Paired with {name} ({m.mode.ToUpperInvariant()})";
                    Debug.Log($"[Companion] paired {name} by {m.mode}");
                    return;
                }
                Refuse(c, "expected link or pair");
            }

            private static string Name(string dev) => _devices.TryGetValue(dev, out string n) ? n : dev;

            private static void Refuse(int c, string why)
            {
                Send(c, new LinkMsg { kind = "result", ok = false, reason = why });
                AeroLinkNative.Close(c);
                Debug.Log($"[Companion] refused connection {c}: {why}");
            }
        }

        // =============================================================================================================
        /// <summary>The iPad / iPhone: find the Mac, pair with it by QR or PIN, and keep a link to it.</summary>
        public static class Client
        {
            private const string MacsKey = "companion.macs";   // "id|name" lines; the keys are in the Keychain
            private static readonly Dictionary<string, string> _macs = new();
            private static FoundRow[] _found = new FoundRow[0];
            private static bool _browsing;
            private static int _conn = -1;
            private static string _connMac, _connMode;   // mode: "link", "qr", "pin"
            private static byte[] _connPsk;
            private static double _retryAt;
            private static PairingOffer _offer;           // a scanned QR, waiting for its Mac to show up
            private static string _pinFor, _pin;          // a typed PIN, and the Mac it is for
            private static bool _linked;

            public static string Status { get; private set; } = "";
            public static bool Linked => _linked;
            public static IReadOnlyList<FoundRow> Found => _found;
            public static int PairedCount => _macs.Count;
            private static double Now => Time.realtimeSinceStartupAsDouble;

            internal static void Wake()
            {
                if (!IsClient) return;
                _macs.Clear();
                foreach (string row in PlayerPrefs.GetString(MacsKey, "").Split('\n'))
                {
                    int bar = row.IndexOf('|');
                    if (bar > 0 && CompanionPairing.IsDeviceId(row.Substring(0, bar))) _macs[row.Substring(0, bar)] = row.Substring(bar + 1);
                }
            }

            /// <summary>A link opened the app (the Camera read the Mac's QR code).</summary>
            public static void OpenLink(string url)
            {
                if (!CompanionPairing.TryParseOffer(url, out PairingOffer offer)) { if (url != null && url.StartsWith(CompanionPairing.Scheme + ":")) Status = "That code is not an Aero Playground pairing code"; return; }
                _offer = offer; _pin = null; _pinFor = null;
                Status = $"Pairing with {offer.MacName}…";
                Debug.Log($"[Companion] QR offer from {offer.MacName}");
                Drop();
            }

            /// <summary>The device typed a Mac's PIN (Options › Controls).</summary>
            public static void PairWithPin(string macId, string pin)
            {
                if (!CompanionPairing.IsDeviceId(macId) || !CompanionPairing.IsPin(pin)) { Status = "The PIN is 6 digits"; return; }
                _pinFor = macId; _pin = pin; _offer = null;
                Status = "Pairing…";
                Drop();
            }

            /// <summary>Keep looking for Macs to pair with for a few seconds (Options is showing the companion rows).</summary>
            public static void KeepListing() => _listUntil = Now + 3;
            private static double _listUntil;

            public static void ForgetAll()
            {
                foreach (string id in _macs.Keys) AeroLinkNative.SecretDelete("mac:" + id);
                _macs.Clear();
                Save();
                Drop();
                Status = "";
            }

            private static void Save()
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in _macs) sb.Append(kv.Key).Append('|').Append(kv.Value.Replace('\n', ' ')).Append('\n');
                PlayerPrefs.SetString(MacsKey, sb.ToString());
                PlayerPrefs.Save();
            }

            private static void Drop()
            {
                if (_conn > 0) AeroLinkNative.Close(_conn);
                _conn = -1; _linked = false; _connMac = null; _connMode = null; _connPsk = null; _retryAt = 0;
            }

            internal static void Tick()
            {
                bool want = _macs.Count > 0 || _offer != null || _pin != null || Now < _listUntil;
                if (want != _browsing) { AeroLinkNative.Browse(want); _browsing = want; if (!want) _found = new FoundRow[0]; }
                if (_conn > 0 || Now < _retryAt) return;

                // Pairing first (QR, then PIN), else the link to a paired Mac that is on the network.
                foreach (FoundRow f in _found)
                {
                    if (f.role != "game" || f.v != CompanionPairing.ProtocolVersion.ToString()) continue;
                    if (_offer != null && f.id == _offer.MacId) { Dial(f, "qr", CompanionPairing.QrIdentity, CompanionPairing.QrPsk(_offer.Key, f.id)); return; }
                    if (_pin != null && f.id == _pinFor) { Dial(f, "pin", CompanionPairing.PinIdentity, CompanionPairing.PinPsk(_pin, f.id)); return; }
                }
                foreach (FoundRow f in _found)
                {
                    if (f.role != "game" || !_macs.ContainsKey(f.id ?? "")) continue;
                    string hex = AeroLinkNative.SecretGet("mac:" + f.id);
                    if (hex == null || hex.Length != 64) continue;
                    Dial(f, "link", CompanionPairing.LinkIdentity(SelfId), CompanionPairing.LinkPsk(FromHex(hex), f.id, SelfId));
                    return;
                }
            }

            private static void Dial(FoundRow f, string mode, string identity, byte[] psk)
            {
                _conn = AeroLinkNative.Connect(f.name, identity, psk);
                _connMac = f.id; _connMode = mode; _connPsk = psk;
                if (_conn <= 0) { Drop(); _retryAt = Now + 2; }
            }

            internal static void OnEvent(LinkEvent e)
            {
                switch (e.ev)
                {
                    case "browse": _found = e.found ?? new FoundRow[0]; break;
                    case "open": if (e.c == _conn) Debug.Log($"[Companion] through TLS to the Mac (suite 0x{e.suite:X4}, {_connMode})"); break;
                    case "line": if (e.c == _conn) OnLine(e.line); break;
                    case "closed":
                        if (e.c != _conn) return;
                        bool wasPairing = _connMode == "qr" || _connMode == "pin";
                        if (e.handshake && wasPairing) { Status = _connMode == "pin" ? "Wrong PIN, or pairing is not open on the Mac" : "The code has expired: open PAIR A DEVICE again"; _offer = null; _pin = null; }
                        else if (_linked) Status = "Lost the Mac; looking for it…";
                        Drop();
                        _retryAt = Now + (e.handshake ? 20 : 2);   // after a refusal, wait (Glass's backoff)
                        break;
                }
            }

            private static void OnLine(string line)
            {
                LinkMsg m = Parse(line);
                if (m == null) return;
                string self = SelfId;
                switch (m.kind)
                {
                    case "challenge":
                        if (m.mac != _connMac || string.IsNullOrEmpty(m.nonce)) { Drop(); return; }   // not the Mac we dialled
                        _nonce = m.nonce;
                        if (_connMode == "link") Send(_conn, new LinkMsg { kind = "link", dev = self, proof = CompanionAuth.LinkProof(_connPsk, m.nonce, _connMac, self) });
                        else Send(_conn, new LinkMsg { kind = "pair", mode = _connMode, dev = self, name = SelfName, proof = CompanionAuth.PairProof(_connPsk, m.nonce, _connMac, self) });
                        break;
                    case "paired":
                        byte[] key = CompanionPairing.FromBase64Url(m.key);
                        if (!m.ok || key == null || key.Length != CompanionPairing.KeyBytes || !CompanionAuth.Same(m.proof, CompanionAuth.ServerProof(_connPsk, _nonce, _connMac, self))) { Status = "Pairing failed"; Drop(); return; }
                        AeroLinkNative.SecretSet("mac:" + _connMac, CompanionPairing.Hex(key));
                        _macs[_connMac] = string.IsNullOrWhiteSpace(m.name) ? "Mac" : m.name;
                        Save();
                        Status = $"Paired with {_macs[_connMac]}";
                        Debug.Log($"[Companion] paired with {_macs[_connMac]} by {_connMode}");
                        _offer = null; _pin = null;
                        Drop();   // and link with the new key: in a second, once the Mac's listener has restarted with it
                        _retryAt = Now + 1;
                        break;
                    case "result":
                        if (!m.ok || _connMode != "link" || !CompanionAuth.Same(m.proof, CompanionAuth.ServerProof(_connPsk, _nonce, _connMac, self)))
                        {
                            Status = m.ok ? "That Mac did not prove itself" : $"The Mac refused: {m.reason}";
                            Drop(); _retryAt = Now + 20; return;
                        }
                        _linked = true;
                        Status = $"Connected to {(_macs.TryGetValue(_connMac, out string n) ? n : "the Mac")}";
                        Send(_conn, new LinkMsg { kind = "hello", dev = self, name = SelfName });
                        Debug.Log("[Companion] linked to the Mac");
                        break;
                    case "state":
                        break;   // the Mac's snapshot: drawn by the companion UI in the next phase
                }
            }

            private static string _nonce;
        }

        private static byte[] FromHex(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = System.Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
