using System.Collections.Generic;
using System.Threading.Tasks;
using FlyingGame.Bridge.Commerce;
using UnityEngine;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// Multiplayer session on the aircraft root. StartMenu.Fly() calls <see cref="Join"/> (room from
    /// SessionSettings.EffectiveRoom: "ffa" or a private code; null = solo) and Open() calls <see cref="Leave"/>.
    /// Connects to the relay over a ClientWebSocket, sends this aircraft's state at 10 Hz, drains the
    /// inbound queue on the main thread, and owns one RemoteAircraft per peer. Reconnects with backoff;
    /// closes cleanly on destroy/pause (resume reconnects).
    ///
    /// This is where the commerce layer meets the game. Two things happen here and nowhere else:
    ///  - every connected second is reported to <see cref="EntitlementRuntime"/>, because the relay is billed
    ///    by the hour and that is the only honest measure of what online flying costs us. The pass is
    ///    unlimited, so the count is never a reason to end a flight;
    ///  - a join asks <see cref="EntitlementRuntime.CanJoinMultiplayer"/> whether this pilot holds the pass,
    ///    and acts on the answer only while <see cref="EntitlementRuntime.GateMultiplayer"/> is on.
    /// Single player, the lessons and the challenges never come through here at all.
    ///
    /// Two rules keep the relay's bill honest (owner 2026-10-16), and both are about not paying for nothing:
    ///  - SOLO: while the room says we are the only pilot in it, no state is sent. The server has stopped its
    ///    ticker, so with nothing arriving the room hibernates and a lone pilot costs nothing. The keepalive
    ///    ping stays, because the runtime answers it without waking the room. A "peer" message ends it, and
    ///    the newcomer gets our state and name immediately so they do not see an empty sky.
    ///  - IDLE: fifteen minutes without touching a control and we leave the room, with a warning at thirteen.
    ///    An open menu is not activity. The relay enforces the same rule on the state it receives, so this is
    ///    a courtesy to the pilot rather than the thing holding the line.
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

        /// <summary>Why the last <see cref="Join"/> did not go online, in words meant for the pilot, or null.
        /// Set only when the pass is the reason — a dead network is a reconnect, not a refusal.</summary>
        public string BlockedReason { get; private set; }

        /// <summary>Minutes without a control movement before the room is left, and the warning before it.</summary>
        public const float IdleWarnSeconds = 13f * 60f, IdleLeaveSeconds = 15f * 60f;

        /// <summary>True while the relay says nobody else is here: nothing is sent, so the room can sleep.</summary>
        public bool Solo { get; private set; }

        /// <summary>A line for the HUD when the idle timer is about to end the session, or null.</summary>
        public string IdleWarning { get; private set; }

        private FlightSimDriver _driver;
        private StructuralDamage _damage;
        private PilotEgress _egress;
        private INetLink _client;
        private ILocalLink _local;       // the same object as _client when nobody is relaying for us, else null
        private Task _connectTask;
        private float _nextSend, _nextConnect, _nextPing, _nextFar;
        private readonly List<string> _far = new();
        private string _shard;   // the free-for-all shard joined (ffa, ffa2 …)
        private int _attempt;
        private string _joinedAc, _joinedName;
        private bool _counting;          // an airtime session is open with the entitlement layer
        private float _lastInputT;       // realtime of the last control movement
        private double _ail, _ele, _rud, _thr, _flap;   // the last control positions seen
        private readonly Dictionary<string, RemoteAircraft> _remotes = new();
        private readonly NetMsg _out = new() { t = "s", p = new float[3], q = new float[4], v = new float[3], d = new float[5], f = new NetFlags() };

        /// <summary>This session is flying with no relay and no internet: one wifi, or device to device.</summary>
        public bool Lan => _local != null;

        /// <summary>Where the aeroplanes are coming from, for the HUD: "port 47801" / "device to device".</summary>
        public string LocalWhere => _local != null ? _local.Where : null;

        /// <summary>HUD line: "FFA · 12 pilots" / "Room K7Q2ZP · 3 pilots" / "connecting…".</summary>
        public string StatusLine
        {
            get
            {
                if (!Active) return IdleWarning;   // normally null; after an idle exit, says why
                string room = Lan ? (SessionSettings.LanRoomOf(Room) == SessionSettings.LanRoom ? "Same network" : $"Same network · {SessionSettings.LanRoomOf(Room)}")
                            : Room == SessionSettings.FfaRoom ? (_shard != null && _shard.Length > 3 ? $"FFA {_shard.Substring(3)}" : "FFA") : $"Room {Room}";
                if (Connected)
                {
                    int n = PeerCount + 1;
                    string line = $"{room} · {n} pilot{(n == 1 ? "" : "s")}";
                    if (Lan && PeerCount == 0)
                    {
                        // The one failure a pilot cannot diagnose: iOS asks for permission to talk to the
                        // local network on the first join, and if it was refused every probe goes nowhere for
                        // ever, in silence. Having asked the whole subnet several times over and heard
                        // nothing at all, say so — on a working network somebody answers in a frame or two.
                        line += _local.SuspectPermission
                              ? " · nobody yet — check Local Network in Settings"
                              : " · looking";
                    }
                    else if (Solo) line += " · waiting";
                    if (IdleWarning != null) line += $" · {IdleWarning}";
                    return line;
                }
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

            // Does this pilot hold the pass? The hours are unlimited, so this is the whole question, and it
            // is asked here rather than on the menu so that nothing can reach the relay around it.
            // A room on your own network uses nothing of ours — no relay, no hours, nothing to charge for —
            // so it is never gated. Whether that stays true is a decision for the owner, and it is this one
            // condition.
            EntitlementRuntime rt = SessionSettings.IsLanRoom(room) ? null : EntitlementRuntime.Instance;
            if (rt != null && rt.GateMultiplayer && !rt.CanJoinMultiplayer(out string why))
            {
                BlockedReason = string.IsNullOrEmpty(why) ? "Flying online needs the online pass." : why;
                Debug.Log($"[Net] not joining {room}: {BlockedReason}");
                Leave();                  // single player carries on exactly as it was
                return;
            }
            BlockedReason = null;
            IdleWarning = null;
            MarkInput(true);

            bool same = Active && room == Room && _client != null && _client.IsOpen;
            Room = room;
            Active = true;
            if (same) return;                 // re-Fly in the same room: keep the socket, a "join" refresh follows
            DropConnection();
            DestroyRemotes();
            _attempt = 0;
            _nextConnect = 0f;
        }

        /// <summary>Note the pilot's control positions; any real movement resets the idle clock.
        /// <paramref name="force"/> restarts it regardless (a fresh join).</summary>
        private void MarkInput(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (force) _lastInputT = now;
            if (_driver == null) return;
            var i = _driver.Inputs;
            double flap = _driver.Sim?.Aircraft != null ? _driver.Sim.Aircraft.FlapFraction : 0;
            const double eps = 0.02;
            bool moved = System.Math.Abs(i.Aileron - _ail) > eps || System.Math.Abs(i.Elevator - _ele) > eps
                      || System.Math.Abs(i.Rudder - _rud) > eps || System.Math.Abs(i.ThrottleLever - _thr) > eps
                      || System.Math.Abs(flap - _flap) > eps;
            _ail = i.Aileron; _ele = i.Elevator; _rud = i.Rudder; _thr = i.ThrottleLever; _flap = flap;
            if (moved || force) _lastInputT = now;
        }

        public void Leave()
        {
            Active = false;
            Room = null;
            DropConnection();
            DestroyRemotes();
        }

        /// <summary>Open an airtime session with the entitlement layer. Called when the relay says "hello" —
        /// the moment this pilot is actually in a room, which is the moment it starts costing. A reconnect
        /// counts as a new session on purpose: waking the room again is the expensive part.</summary>
        private void BeginCounting()
        {
            if (_counting || Lan) return;         // a LAN room is not billed: there is nothing in it but us

            _counting = true;
            EntitlementRuntime.Instance?.BeginMultiplayerSession();
        }

        private void EndCounting()
        {
            if (!_counting) return;
            _counting = false;
            EntitlementRuntime.Instance?.EndMultiplayerSession();
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

            // ---- inbound. The LAN link does its housekeeping here — finding pilots, dropping the ones who
            // have gone — because it has no thread of its own to do it on.
            _client.Service();
            int drained = 0;
            while (drained++ < 512 && _client.TryDequeue(out NetMsg m)) Handle(m);

            // ---- the bill. Unscaled: the relay keeps the room awake while the pilot reads the menu, and
            // wall-clock time is what it charges for.
            if (_counting) EntitlementRuntime.Instance?.RecordMultiplayerTime(Time.unscaledDeltaTime);

            // ---- outbound
            if (!_client.IsOpen || SelfId == null || _driver == null || _driver.Sim == null) return;
            // The idle timer. An open menu is not activity — the owner's rule is that a parked pilot leaves.
            // It exists because an idle pilot in a relay room costs money; on your own network nobody is
            // paying for the aeroplane parked on the ramp, so it can sit there all day.
            if (!SessionSettings.MenuOpen) MarkInput(false);
            float idleFor = Lan ? 0f : now - _lastInputT;
            if (idleFor > IdleLeaveSeconds)
            {
                Debug.Log("[Net] leaving: fifteen minutes without a control movement");
                IdleWarning = "Left the room — no control movement for 15 minutes. Fly again to rejoin.";
                Leave();
                return;
            }
            IdleWarning = idleFor > IdleWarnSeconds ? "Still flying? Move the stick." : null;

            if (_driver.AircraftId != _joinedAc || SessionSettings.PilotName != _joinedName)
            {
                _joinedAc = _driver.AircraftId; _joinedName = SessionSettings.PilotName;
                // On a LAN the name travels in every announcement, so there is nobody to tell but the mesh.
                if (Lan) _local.SetSelf(_joinedName, _joinedAc);
                else if (!Solo) _client.SendQueued(new NetMsg { t = "join", name = _joinedName, ac = _joinedAc, bdg = PilotRecord.Badge(), sc = PilotRecord.Score() }.ToJson());
            }
            if (Lan)
            {
                // A mesh is N² packets a tick, so the aeroplanes nobody can see are put on a slower rate.
                // The roster keeps them, and they come back to full rate when they come back into sight.
                if (now >= _nextFar)
                {
                    _nextFar = now + 1f;
                    _far.Clear();
                    Vector3 me = _driver.transform.position;
                    foreach (var kv in _remotes)
                        if (kv.Value != null && Vector3.Distance(me, kv.Value.transform.position) > LanLink.FarMetres)
                            _far.Add(kv.Key);
                    _local.SetFarPeers(_far);
                }
            }
            else if (now >= _nextPing) { _nextPing = now + 15f; _client.SendQueued("ping"); }   // keepalive (the server answers without waking the room)
            // Alone in the room: send nothing at all, so the relay can hibernate.
            if (Solo || SessionSettings.MenuOpen || now < _nextSend) return;
            _nextSend = now + 1f / SendHz;
            SendState();
        }

        private void StartConnect()
        {
            string name = string.IsNullOrEmpty(SessionSettings.PilotName) ? "Pilot" : SessionSettings.PilotName;
            string ac = _driver != null ? _driver.AircraftId : SessionSettings.AircraftId;
            _joinedAc = ac; _joinedName = SessionSettings.PilotName;
            SelfId = null;
            if (SessionSettings.IsLanRoom(Room))
            {
                string lanRoom = SessionSettings.LanRoomOf(Room);
                if (SessionSettings.LanNoRouter && PeerLink.Available)
                {
                    // No access point in the field: the devices make the link themselves.
                    var peer = new PeerLink(lanRoom, SessionSettings.DeviceId, name, ac);
                    _client = peer;
                    _local = peer;
                    _connectTask = peer.OpenAsync();
                }
                else
                {
                    var lan = new LanLink(lanRoom, SessionSettings.DeviceId, name, ac);
                    _client = lan;
                    _local = lan;
                    _connectTask = lan.OpenAsync();
                    if (!string.IsNullOrWhiteSpace(SessionSettings.LanHint)) lan.AddHint(SessionSettings.LanHint);
                }
            }
            else
            {
                var relay = new RelayClient();
                _client = relay;
                _local = null;
                _local = null;
                _connectTask = ConnectRoom(relay, Room, name, ac);
            }
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
            // Radio to an empty room is still a message that wakes it, so it waits for a peer too.
            if (Solo) return;
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
                    Solo = m.solo == true;
                    BeginCounting();
                    DestroyRemotes();
                    if (m.peers != null)
                    {
                        foreach (NetPeer p in m.peers)
                        {
                            if (p?.id == null || p.id == SelfId) continue;
                            RemoteAircraft r = Upsert(p.id, p.name, p.ac, p.bdg, p.sc);
                            if (p.s != null) r.Push(p.s);
                        }
                    }
                    Debug.Log($"[Net] joined {Room} as {SelfId}; {PeerCount} peers");
                    break;
                case "peer":
                    if (m.id != null && m.id != SelfId) Upsert(m.id, m.name, m.ac, m.bdg, m.sc);
                    if (Solo)
                    {
                        // Somebody arrived: start talking again, and let them see us this frame rather than in
                        // a tenth of a second — they have nothing of ours while we were quiet.
                        Solo = false;
                        _joinedAc = null;
                        _nextSend = 0f;
                    }
                    break;
                case "bye":
                    if (m.id != null && _remotes.TryGetValue(m.id, out RemoteAircraft gone))
                    {
                        _remotes.Remove(m.id);
                        if (gone != null) Destroy(gone.gameObject);
                    }
                    if (m.solo == true) Solo = true;   // the last one left: go quiet and let the room sleep
                    break;
                case "idle":
                    if (m.c == 1) { Debug.Log("[Net] the relay closed us for being idle"); IdleWarning = "Left the room — idle. Fly again to rejoin."; }
                    else if (m.w == 1 && IdleWarning == null) IdleWarning = "Still flying? Move the stick.";
                    break;
                case "S":   // protocol 2: the tick's batch
                    if (m.b != null) foreach (NetMsg x in m.b) if (x != null) { x.t = "s"; Handle(x); }
                    break;
                case "s":
                    if (m.id != null && m.id != SelfId && _remotes.TryGetValue(m.id, out RemoteAircraft r2) && r2 != null) r2.Push(m.AsState());
                    break;
            }
        }

        private RemoteAircraft Upsert(string id, string name, string ac) => Upsert(id, name, ac, null, null);

        /// <summary>
        /// A peer's identity. The badge and board score ride along with the name and the aeroplane because
        /// all three are PUBLIC results (owner 2026-10-18); the lesson record they came from never leaves
        /// its owner's device, and a null badge simply means a pilot on a build older than this one.
        /// </summary>
        private RemoteAircraft Upsert(string id, string name, string ac, string badge, double? score)
        {
            if (!_remotes.TryGetValue(id, out RemoteAircraft r) || r == null)
            {
                r = RemoteAircraft.Create(id, name, ac);
                _remotes[id] = r;
            }
            else
            {
                r.SetName(name);
                r.SetAircraft(ac);
            }
            if (badge != null || score.HasValue) r.SetBadge(badge ?? r.Badge, score ?? r.BoardScore);
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
            EndCounting();
            _local = null;
            Solo = false;
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
