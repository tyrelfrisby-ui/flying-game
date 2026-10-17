using FlyingGame.Core.Lan;
using UnityEngine;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// Flying together on one network with no internet (owner 2026-10-17). This is the relay's job done by
    /// the aeroplanes themselves: <see cref="LanMesh"/> in Core finds the other copies of the game on the
    /// network and carries datagrams between them, and this turns what it sees into the same messages the
    /// relay sends — so <see cref="NetSession"/>, <see cref="RemoteAircraft"/> and the radio are the code
    /// they already were, and anything that works online works here.
    ///
    /// What is NOT here, because there is no server to pay for: no shard assignment, no room hibernation, no
    /// idle eviction, no airtime. A room on your own wifi costs nothing and nobody can be charged for it.
    /// </summary>
    public sealed class LanLink : INetLink
    {
        /// <summary>Beyond this an aeroplane is a dot you cannot see, and gets state at a fifth of the rate.</summary>
        public const float FarMetres = 12000f;

        private readonly LanMesh _mesh;
        private readonly System.Collections.Concurrent.ConcurrentQueue<NetMsg> _inbox = new();
        private bool _closed;
        private bool _helloSent;

        public LanLink(string room, string selfId, string name, string aircraft)
        {
            _mesh = new LanMesh(room, selfId, name, aircraft);
        }

        public string SelfId => _mesh.SelfId;
        public int Port => _mesh.Port;
        public int PeerCount => _mesh.PeerCount;
        public int ProbesSent => _mesh.ProbesSent;
        public bool IsOpen => !_closed && _mesh.Running;
        public bool IsClosed => _closed || !_mesh.Running;
        public string Error { get; private set; }

        /// <summary>
        /// Bind and start listening. Shaped as a Task so the session's connect path is the same for both
        /// links, but there is nothing to wait for — on a LAN you are "connected" the moment you can listen.
        /// </summary>
        public System.Threading.Tasks.Task OpenAsync()
        {
            if (!_mesh.Start())
            {
                Error = _mesh.Error;
                _closed = true;
                return System.Threading.Tasks.Task.FromException(new System.Net.Sockets.SocketException(48));   // address in use
            }
            Debug.Log($"[Lan] listening on {_mesh.Port} as {_mesh.SelfId} in room '{_mesh.Room}'");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>Somewhere to look that a sweep would not find: an address, or the last octet of one.</summary>
        public bool AddHint(string text) => _mesh.AddHint(text);

        /// <summary>The pilot changed their name or their aeroplane on the menu.</summary>
        public void SetSelf(string name, string aircraft) => _mesh.SetSelf(name, aircraft);

        /// <summary>Who is out of sight, so the radio is not used on them ten times a second.</summary>
        public void SetFarPeers(System.Collections.Generic.ICollection<string> ids) => _mesh.SetFarPeers(ids);

        public void Service()
        {
            if (_closed) return;
            _mesh.Tick();

            // The session expects to be told who it is before anything else, exactly as the relay tells it.
            if (!_helloSent)
            {
                _helloSent = true;
                _inbox.Enqueue(new NetMsg { t = "hello", id = _mesh.SelfId, solo = _mesh.PeerCount == 0, peers = new NetPeer[0] });
            }

            int drained = 0;
            while (drained++ < 512 && _mesh.TryDequeue(out LanEvent e))
            {
                switch (e.Kind)
                {
                    case LanEventKind.Peer:
                        _inbox.Enqueue(new NetMsg { t = "peer", id = e.PeerId, name = e.Name, ac = e.Aircraft });
                        break;
                    case LanEventKind.Bye:
                        // The last one out puts the session back to solo, which is what stops it sending.
                        _inbox.Enqueue(new NetMsg { t = "bye", id = e.PeerId, solo = _mesh.PeerCount == 0 ? true : (bool?)null });
                        break;
                    case LanEventKind.State:
                        NetMsg m = null;
                        try { m = NetMsg.FromJson(e.Payload); }
                        catch (System.Exception) { }
                        if (m == null) break;
                        m.id = e.PeerId;                      // the sender's own word for who they are is not used
                        if (string.IsNullOrEmpty(m.t)) m.t = "s";
                        _inbox.Enqueue(m);
                        break;
                }
            }
        }

        public bool TryDequeue(out NetMsg m) => _inbox.TryDequeue(out m);

        public void Send(string json)
        {
            if (!IsOpen) return;
            _mesh.SendState(json);
        }

        public void SendQueued(string json)
        {
            if (!IsOpen || json == null) return;
            if (json.Length == 0 || json[0] != '{') return;    // the relay's keepalive has nobody to keep alive here
            _mesh.SendAll(json);
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _mesh.Dispose();
        }

        public void Dispose() => Close();
    }
}
