using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// Multiplayer session on the aircraft root. StartMenu.Fly() calls <see cref="Join"/> (room from
    /// SessionSettings.EffectiveRoom: "ffa" or a private code; null = solo) and Open() calls <see cref="Leave"/>.
    /// Connects to the relay over a ClientWebSocket, sends this aircraft's state at 10 Hz, drains the
    /// inbound queue on the main thread, and owns one RemoteAircraft per peer. Reconnects with backoff;
    /// closes cleanly on destroy/pause (resume reconnects).
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        public const float SendHz = 10f;

        public bool Active { get; private set; }          // a room session is wanted
        public string Room { get; private set; }
        public string SelfId { get; private set; }
        public bool Connected => _client != null && _client.IsOpen && SelfId != null;
        public int PeerCount => _remotes.Count;
        public string LastError { get; private set; }

        private FlightSimDriver _driver;
        private StructuralDamage _damage;
        private PilotEgress _egress;
        private RelayClient _client;
        private Task _connectTask;
        private float _nextSend, _nextConnect, _nextPing;
        private string _shard;   // the free-for-all shard joined (ffa, ffa2 …)
        private int _attempt;
        private string _joinedAc, _joinedName;
        private readonly Dictionary<string, RemoteAircraft> _remotes = new();
        private readonly NetMsg _out = new() { t = "s", p = new float[3], q = new float[4], v = new float[3], d = new float[5], f = new NetFlags() };

        /// <summary>HUD line: "FFA · 12 pilots" / "Room K7Q2ZP · 3 pilots" / "connecting…".</summary>
        public string StatusLine
        {
            get
            {
                if (!Active) return null;
                string room = Room == SessionSettings.FfaRoom ? (_shard != null && _shard.Length > 3 ? $"FFA {_shard.Substring(3)}" : "FFA") : $"Room {Room}";
                if (Connected) { int n = PeerCount + 1; return $"{room} · {n} pilot{(n == 1 ? "" : "s")}"; }
                if (_client != null && !_client.IsClosed) return $"{room} · connecting…";
                float wait = _nextConnect - Time.realtimeSinceStartup;
                return wait > 0.5f ? $"{room} · reconnecting in {Mathf.CeilToInt(wait)} s" : $"{room} · connecting…";
            }
        }

        private void Awake()
        {
            _driver = GetComponent<FlightSimDriver>();
        }

        /// <summary>Start (or restart) the session for the room chosen on the landing page.</summary>
        public void Join()
        {
            string room = SessionSettings.EffectiveRoom;
            if (room == null) { Leave(); return; }
            bool same = Active && room == Room && _client != null && _client.IsOpen;
            Room = room;
            Active = true;
            if (same) return;                 // re-Fly in the same room: keep the socket, a "join" refresh follows
            DropConnection();
            DestroyRemotes();
            _attempt = 0;
            _nextConnect = 0f;
        }

        public void Leave()
        {
            Active = false;
            Room = null;
            DropConnection();
            DestroyRemotes();
        }

        private void Update()
        {
            if (!Active) return;
            float now = Time.realtimeSinceStartup;

            // ---- connection state machine
            if (_client == null || _client.IsClosed)
            {
                if (_client != null)
                {
                    LastError = _client.Error;
                    Debug.Log($"[Net] {(SelfId != null ? "disconnected" : "connect failed")} (attempt {_attempt}): {LastError}");
                    DropConnection();
                    DestroyRemotes();
                    ScheduleReconnect();
                }
                if (now >= _nextConnect && _connectTask == null) StartConnect();
                return;
            }
            if (_connectTask != null)
            {
                if (!_connectTask.IsCompleted) return;
                bool ok = _connectTask.Status == TaskStatus.RanToCompletion;
                _connectTask = null;
                if (!ok)
                {
                    LastError = _client.Error ?? "connect failed";
                    Debug.Log($"[Net] connect failed: {LastError}");
                    DropConnection();
                    ScheduleReconnect();
                    return;
                }
                _attempt = 0;
                _nextSend = now;
            }

            // ---- inbound
            while (_client.Inbox.TryDequeue(out NetMsg m)) Handle(m);

            // ---- outbound
            if (!_client.IsOpen || SelfId == null || _driver == null || _driver.Sim == null) return;
            if (_driver.AircraftId != _joinedAc || SessionSettings.PilotName != _joinedName)
            {
                _joinedAc = _driver.AircraftId; _joinedName = SessionSettings.PilotName;
                _client.SendQueued(new NetMsg { t = "join", name = _joinedName, ac = _joinedAc }.ToJson());
            }
            if (now >= _nextPing) { _nextPing = now + 15f; _client.SendQueued("ping"); }   // keepalive (the server answers without waking the room)
            if (SessionSettings.MenuOpen || now < _nextSend) return;
            _nextSend = now + 1f / SendHz;
            SendState();
        }

        private void StartConnect()
        {
            string name = string.IsNullOrEmpty(SessionSettings.PilotName) ? "Pilot" : SessionSettings.PilotName;
            string ac = _driver != null ? _driver.AircraftId : SessionSettings.AircraftId;
            _joinedAc = ac; _joinedName = SessionSettings.PilotName;
            SelfId = null;
            _client = new RelayClient();
            _connectTask = ConnectRoom(_client, Room, name, ac);
            _attempt++;
        }

        /// <summary>The free-for-all is sharded (10 × 100 pilots): ask the platform which shard has room, then open the
        /// socket — protocol 2 (the room's 10 Hz tick batches). A relay without shards (404) means plain "ffa".</summary>
        private async Task ConnectRoom(RelayClient client, string room, string name, string ac)
        {
            string target = room;
            if (room == SessionSettings.FfaRoom)
            {
                // (UnityWebRequest on the main thread — started from Update — so iOS/IL2CPP TLS is Unity's own.)
                string http = SessionSettings.RelayUrl.TrimEnd('/').Replace("wss://", "https://").Replace("ws://", "http://");
                using var req = UnityEngine.Networking.UnityWebRequest.Get($"{http}/rooms/ffa/assign");
                req.timeout = 6;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                try
                {
                    if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        string code = (string)Newtonsoft.Json.Linq.JObject.Parse(req.downloadHandler.text)["code"];
                        if (!string.IsNullOrEmpty(code)) target = code;
                    }
                    else Debug.Log($"[Net] shard assign: {req.error} — joining ffa");
                }
                catch (System.Exception e) { Debug.Log($"[Net] shard assign failed ({e.Message}) — joining ffa"); }
            }
            _shard = target;
            string url = $"{SessionSettings.RelayUrl.TrimEnd('/')}/rooms/{target}/ws?v=2&name={System.Uri.EscapeDataString(name)}&ac={System.Uri.EscapeDataString(ac)}";
            await client.ConnectAsync(url);
        }

        private void ScheduleReconnect()
        {
            float delay = Mathf.Min(15f, Mathf.Pow(2f, Mathf.Max(0, _attempt - 1)));   // 1, 2, 4, 8, 15 s
            _nextConnect = Time.realtimeSinceStartup + delay;
        }

        /// <summary>Radio voice from a pilot on our frequency (VoiceComms plays it).</summary>
        public event System.Action<NetMsg> VoiceReceived;

        /// <summary>Send a message that must not be dropped (radio voice / tune): queued behind any send in flight.</summary>
        public void SendQueued(NetMsg m)
        {
            if (_client != null && _client.IsOpen && SelfId != null) _client.SendQueued(m.ToJson());
        }

        /// <summary>Display name of a peer (radio "who is talking"), or null.</summary>
        public string PeerName(string id) => id != null && _remotes.TryGetValue(id, out RemoteAircraft r) && r != null ? r.PilotName : null;

        private void Handle(NetMsg m)
        {
            switch (m.t)
            {
                case "v":
                    if (m.id != null && m.id != SelfId) VoiceReceived?.Invoke(m);
                    break;
                case "hello":
                    SelfId = m.id;
                    DestroyRemotes();
                    if (m.peers != null)
                    {
                        foreach (NetPeer p in m.peers)
                        {
                            if (p?.id == null || p.id == SelfId) continue;
                            RemoteAircraft r = Upsert(p.id, p.name, p.ac);
                            if (p.s != null) r.Push(p.s);
                        }
                    }
                    Debug.Log($"[Net] joined {Room} as {SelfId}; {PeerCount} peers");
                    break;
                case "peer":
                    if (m.id != null && m.id != SelfId) Upsert(m.id, m.name, m.ac);
                    break;
                case "bye":
                    if (m.id != null && _remotes.TryGetValue(m.id, out RemoteAircraft gone))
                    {
                        _remotes.Remove(m.id);
                        if (gone != null) Destroy(gone.gameObject);
                    }
                    break;
                case "S":   // protocol 2: the tick's batch
                    if (m.b != null) foreach (NetMsg x in m.b) if (x != null) { x.t = "s"; Handle(x); }
                    break;
                case "s":
                    if (m.id != null && m.id != SelfId && _remotes.TryGetValue(m.id, out RemoteAircraft r2) && r2 != null) r2.Push(m.AsState());
                    break;
            }
        }

        private RemoteAircraft Upsert(string id, string name, string ac)
        {
            if (_remotes.TryGetValue(id, out RemoteAircraft r) && r != null)
            {
                r.SetName(name);
                r.SetAircraft(ac);
                return r;
            }
            r = RemoteAircraft.Create(id, name, ac);
            _remotes[id] = r;
            return r;
        }

        private void SendState()
        {
            Transform t = _driver.transform;
            Vector3 p = t.position; Quaternion q = t.rotation; Vector3 v = _driver.WorldVelocityUnity;
            _out.p[0] = p.x; _out.p[1] = p.y; _out.p[2] = p.z;
            _out.q[0] = q.x; _out.q[1] = q.y; _out.q[2] = q.z; _out.q[3] = q.w;
            _out.v[0] = v.x; _out.v[1] = v.y; _out.v[2] = v.z;
            var d = _driver.Sim.Aircraft.CurrentDeflections;
            _out.d[0] = R3((float)d.AileronRad); _out.d[1] = R3((float)d.ElevatorRad); _out.d[2] = R3((float)d.RudderRad);
            _out.d[3] = R3((float)d.SpoilerFraction); _out.d[4] = R3((float)_driver.Sim.Aircraft.FlapFraction);
            for (int i = 0; i < 3; i++) { _out.p[i] = R2(_out.p[i]); _out.v[i] = R2(_out.v[i]); }
            for (int i = 0; i < 4; i++) _out.q[i] = R4(_out.q[i]);
            if (_damage == null) _damage = GetComponent<StructuralDamage>();
            if (_egress == null) _egress = GetComponent<PilotEgress>();
            _out.f.w = _damage != null && _damage.WingsGone ? 1 : 0;
            _out.f.o = _egress != null && _egress.PilotOut ? 1 : 0;
            _client.Send(_out.ToJson());
        }

        private static float R2(float x) => Mathf.Round(x * 100f) / 100f;
        private static float R3(float x) => Mathf.Round(x * 1000f) / 1000f;
        private static float R4(float x) => Mathf.Round(x * 10000f) / 10000f;

        private void DropConnection()
        {
            _connectTask = null;
            SelfId = null;
            _client?.Close();
            _client = null;
        }

        private void DestroyRemotes()
        {
            foreach (RemoteAircraft r in _remotes.Values) if (r != null) Destroy(r.gameObject);
            _remotes.Clear();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) { DropConnection(); DestroyRemotes(); }
            else if (Active) { _attempt = 0; _nextConnect = 0f; }
        }

        private void OnDestroy()
        {
            Active = false;
            DropConnection();
            DestroyRemotes();
        }
    }
}
