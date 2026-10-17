using System.Runtime.InteropServices;
using FlyingGame.Core.Lan;
using UnityEngine;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// FLYING WITH NO ROUTER AT ALL (owner 2026-10-17: "then the two ipads in a field"). Apple's peer-to-peer
    /// wifi, through MultipeerConnectivity in Plugins/iOS/AeroPeer.mm: the devices build their own link out of
    /// nothing — no access point, no addresses, nothing to type, and nothing to find because finding is part
    /// of the protocol. Tens of metres, one hop of latency.
    ///
    /// Everything above this is the code that was already there: the events become the same hello / peer /
    /// bye / s messages the relay sends, exactly as <see cref="LanLink"/> does with its datagrams. Which of
    /// the two the session uses is the "No wifi router" switch on the menu, and nothing else changes.
    ///
    /// The pilot's name and aeroplane ride in the peer's display name ("id|name|aircraft"), because that is
    /// the one field MultipeerConnectivity carries for us and it saves a round of introductions.
    /// </summary>
    public sealed class PeerLink : ILocalLink
    {
#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
#if UNITY_IOS
        private const string Lib = "__Internal";
#else
        private const string Lib = "AeroNative";
#endif
        [DllImport(Lib)] private static extern int AP_Available();
        [DllImport(Lib)] private static extern int AP_Start(string room, string display);
        [DllImport(Lib)] private static extern void AP_Stop();
        [DllImport(Lib)] private static extern int AP_Count();
        [DllImport(Lib)] private static extern void AP_SetDisplay(string display);
        [DllImport(Lib)] private static extern void AP_Send(byte[] utf8, int len, int reliable);
        [DllImport(Lib)] private static extern int AP_Poll(byte[] buf, int max);
#else
        // The editor and anything that is not an Apple platform: there is no peer-to-peer wifi to be had, and
        // the menu says so rather than pretending.
        private static int AP_Available() => 0;
        private static int AP_Start(string room, string display) => 0;
        private static void AP_Stop() { }
        private static int AP_Count() => 0;
        private static void AP_SetDisplay(string display) { }
        private static void AP_Send(byte[] utf8, int len, int reliable) { }
        private static int AP_Poll(byte[] buf, int max) => 0;
#endif

        /// <summary>Is there peer-to-peer wifi on this device at all? False in the editor and on Windows.</summary>
        public static bool Available
        {
            get { try { return AP_Available() != 0; } catch (System.Exception) { return false; } }
        }

        /// <summary>Beyond this an aeroplane is a dot you cannot see — but a field group is small, so this
        /// rarely bites; it is here so the two local links behave the same way.</summary>
        public const float FarMetres = LanLink.FarMetres;

        private const int BufBytes = 16384;

        private readonly string _room;
        private readonly string _selfId;
        private readonly System.Collections.Concurrent.ConcurrentQueue<NetMsg> _inbox = new();
        private readonly byte[] _buf = new byte[BufBytes];
        private readonly System.Collections.Generic.HashSet<string> _far = new();
        private string _name, _ac;
        private bool _open, _closed, _helloSent;
        private long _seq;

        public PeerLink(string room, string selfId, string name, string aircraft)
        {
            _room = room; _selfId = selfId; _name = name ?? ""; _ac = aircraft ?? "";
        }

        public string SelfId => _selfId;
        public int PeerCount => _open ? AP_Count() : 0;
        public bool IsOpen => _open && !_closed;
        public bool IsClosed => _closed || !_open;
        public string Error { get; private set; }
        /// <summary>Not applicable: this does not probe, and an empty field is an empty field.</summary>
        public bool SuspectPermission => false;
        public string Where => "device to device";

        /// <summary>Our own line in everybody else's roster, trimmed to what MCPeerID allows.</summary>
        private string Display => PeerEvent.Display(_selfId, _name, _ac);

        public System.Threading.Tasks.Task OpenAsync()
        {
            if (!Available)
            {
                Error = "this device has no peer-to-peer wifi";
                _closed = true;
                return System.Threading.Tasks.Task.FromException(new System.NotSupportedException(Error));
            }
            if (AP_Start(_room, Display) == 0)
            {
                Error = "peer-to-peer wifi would not start";
                _closed = true;
                return System.Threading.Tasks.Task.FromException(new System.InvalidOperationException(Error));
            }
            _open = true;
            Debug.Log($"[Peer] device-to-device wifi up as {Display} in room '{_room}'");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void SetSelf(string name, string aircraft)
        {
            if (_name == (name ?? "") && _ac == (aircraft ?? "")) return;
            _name = name ?? ""; _ac = aircraft ?? "";
            // The peer id is immutable, so the native side re-forms the group. Cheap and rare: this only
            // happens when the pilot changes aeroplane, which they do on the menu, not in the air.
            if (_open) AP_SetDisplay(Display);
        }

        public void SetFarPeers(System.Collections.Generic.ICollection<string> ids)
        {
            _far.Clear();
            if (ids == null) return;
            foreach (string id in ids) _far.Add(id);
        }

        public void Service()
        {
            if (!_open || _closed) return;
            if (!_helloSent)
            {
                _helloSent = true;
                _inbox.Enqueue(new NetMsg { t = "hello", id = _selfId, solo = AP_Count() == 0, peers = new NetPeer[0] });
            }

            int drained = 0;
            while (drained++ < 512)
            {
                int n = AP_Poll(_buf, BufBytes);
                if (n <= 0) break;
                string e = System.Text.Encoding.UTF8.GetString(_buf, 0, n);
                // The taking-apart is PeerEvent, in Core, because it is the only part of this that can be
                // wrong without the compiler saying so — and the only part that can be tested off a device.
                if (!PeerEvent.TryParse(e, out PeerEventKind kind, out string id, out string name, out string ac, out string payload)) continue;
                switch (kind)
                {
                    case PeerEventKind.Peer:
                        _inbox.Enqueue(new NetMsg { t = "peer", id = id, name = name, ac = ac });
                        break;
                    case PeerEventKind.Bye:
                        _inbox.Enqueue(new NetMsg { t = "bye", id = id, solo = AP_Count() == 0 ? true : (bool?)null });
                        break;
                    case PeerEventKind.State:
                        NetMsg m = null;
                        try { m = NetMsg.FromJson(payload); }
                        catch (System.Exception) { }
                        if (m == null) break;
                        m.id = id;
                        if (string.IsNullOrEmpty(m.t)) m.t = "s";
                        _inbox.Enqueue(m);
                        break;
                    case PeerEventKind.Error:
                        Error = payload;
                        Debug.LogWarning($"[Peer] {payload}");
                        break;
                }
            }
        }

        public bool TryDequeue(out NetMsg m) => _inbox.TryDequeue(out m);

        public void Send(string json)
        {
            if (!IsOpen || json == null) return;
            // A field group is everybody in sight of everybody, and MultipeerConnectivity does the fan-out
            // itself — so "far" can only thin the rate, not the recipients.
            if (_far.Count > 0 && AP_Count() > 0 && _far.Count >= AP_Count() && ++_seq % LanMeshFarDivisor != 0) return;
            byte[] b = System.Text.Encoding.UTF8.GetBytes(json);
            AP_Send(b, b.Length, 0);
        }

        /// <summary>Kept in step with the mesh's own divisor so the two links behave alike.</summary>
        private const int LanMeshFarDivisor = LanMesh.FarDivisor;

        public void SendQueued(string json)
        {
            if (!IsOpen || json == null) return;
            if (json.Length == 0 || json[0] != '{') return;     // the relay's keepalive has nobody to keep alive
            byte[] b = System.Text.Encoding.UTF8.GetBytes(json);
            AP_Send(b, b.Length, 1);
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_open) AP_Stop();
            _open = false;
        }

        public void Dispose() => Close();
    }
}
