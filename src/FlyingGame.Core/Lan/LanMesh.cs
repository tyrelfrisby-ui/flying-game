using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FlyingGame.Core.Lan;

/// <summary>What arrived from the network, in the order it arrived.</summary>
public enum LanEventKind
{
    /// <summary>A pilot is here (new, or their name/aeroplane changed).</summary>
    Peer,
    /// <summary>A pilot has gone — said so, or stopped answering.</summary>
    Bye,
    /// <summary>A pilot's state packet. <see cref="LanEvent.Payload"/> is whatever the game put in it.</summary>
    State,
}

/// <summary>One thing that happened. <see cref="Payload"/> is null except on <see cref="LanEventKind.State"/>.</summary>
public readonly struct LanEvent
{
    public readonly LanEventKind Kind;
    public readonly string PeerId;
    public readonly string? Name;
    public readonly string? Aircraft;
    public readonly string? Payload;

    public LanEvent(LanEventKind kind, string peerId, string? name, string? aircraft, string? payload)
    {
        Kind = kind; PeerId = peerId; Name = name; Aircraft = aircraft; Payload = payload;
    }
}

/// <summary>
/// Flying together on one network with no internet at all (owner 2026-10-17: "i want to be sure the ios
/// devices and mac app can all join in the same local network with no external internet… we used to play
/// games like this at the dorms at my college").
///
/// There is no server and no host. Every aeroplane sends its own state straight to every other aeroplane over
/// UDP, which is exactly what the relay was doing for us, minus the relay — so the latency is one wifi hop
/// (a millisecond or two) instead of a round trip to a datacentre, and nothing is billed because nothing of
/// ours is involved. The payload is opaque here: the game hands this class the same JSON it hands the relay.
///
/// FINDING EACH OTHER is the only hard part, because since iOS 14 broadcast and multicast both need an
/// entitlement granted by Apple. So this never broadcasts. It probes the hosts of its own subnet one
/// datagram at a time (unicast needs no entitlement), and the first pilot it finds hands back the addresses
/// of everyone that pilot knows — so a room of twenty is found by one probe answered, not by twenty sweeps.
/// A typed hint (a full address, or just the last octet) skips the sweep entirely for a network that forbids
/// host-to-host traffic in one direction, or an access point that does client isolation one way.
///
/// Scale: a mesh is N² datagrams a tick, which at ten ticks a second and a 250-byte state is 25 kB/s per
/// pilot for ten pilots and 60 kB/s for twenty-five. That is nothing for wifi but it is not nothing for a
/// phone radio, so a pilot far enough away to be invisible can be put on a slower rate
/// (<see cref="SetFarPeers"/>) without leaving the roster.
/// </summary>
public sealed class LanMesh : IDisposable
{
    /// <summary>The port everyone listens on. A second instance on one machine takes the next one up, which
    /// is how the Mac app and the editor — or two test runs — find each other on loopback.</summary>
    public const int BasePort = 47801;
    public const int PortsPerHost = 4;

    /// <summary>No word from a pilot for this long and they are gone. Three missed announcements.</summary>
    public const double PeerTimeoutS = 5.0;
    /// <summary>How often we say we are still here. Also carries a changed name or aeroplane.</summary>
    public const double AnnounceEveryS = 1.0;
    /// <summary>How often the subnet is swept while we are alone. Once anybody answers, gossip does the rest.</summary>
    public const double SweepEveryS = 5.0;
    /// <summary>
    /// A sweep that finds nothing waits longer before the next one, up to this. A pilot sitting alone on the
    /// menu would otherwise put 254 probes across the network every five seconds for as long as the app is
    /// open, which is both pointless and the sort of thing that annoys a router.
    /// </summary>
    public const double SweepBackoffMaxS = 30.0;
    /// <summary>Probes per tick, so a sweep never dumps 254 datagrams into one frame.</summary>
    public const int ProbesPerTick = 48;
    /// <summary>Addresses passed on in an announcement. Enough to seed a big room from a single answer.</summary>
    public const int MaxGossip = 24;
    /// <summary>A sweep this wide is pointless (and rude); a bigger subnet is swept as the /24 around us.</summary>
    public const int MaxSweepHosts = 1024;
    /// <summary>State sent to a far pilot every Nth tick instead of every tick.</summary>
    public const int FarDivisor = 5;

    /// <summary>How often the listening thread comes up for air to see whether it has been stopped.</summary>
    public const double RxWakeS = 0.25;

    private const string Magic = "AG1";
    private const int MaxDatagram = 8192;

    private sealed class Peer
    {
        public string Id = "";
        public string Name = "";
        public string Ac = "";
        public IPEndPoint End = new(IPAddress.Loopback, 0);
        public double LastSeen;
        public double LastReplied;
        public int Every = 1;
        public int Phase;
    }

    /// <summary>
    /// THE one clock. It used to take the caller's clock in <see cref="Tick"/> while stamping LastSeen from a
    /// clock of its own, and the two bases do not have to agree: with the caller's ahead, every peer timed out
    /// the instant it was added, came back as new on its next packet, and the event queue never emptied. A
    /// class that owns a timeout owns the clock the timeout is measured on.
    /// </summary>
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    private double Now => _watch.Elapsed.TotalSeconds;

    private readonly string _room;
    private readonly string _selfId;
    private readonly int _basePort;
    private readonly object _gate = new();
    private readonly Dictionary<string, Peer> _peers = new();
    private readonly Queue<LanEvent> _inbox = new();
    private readonly List<IPEndPoint> _hints = new();

    /// <summary>
    /// Never probe anything but this machine. The suite sets it, because a test that sweeps the subnet is a
    /// test that puts a port scan across the owner's house every time it runs; the editor can set it to stay
    /// off the network it is sitting on.
    /// </summary>
    public bool LoopbackOnly;
    /// <summary>Instance copies of the three timings, so a test can run in a second and a device can be tuned.</summary>
    public double TimeoutS = PeerTimeoutS;
    public double AnnounceS = AnnounceEveryS;
    public double SweepS = SweepEveryS;

    private string _name;
    private string _ac;
    private Socket? _sock;
    private Thread? _rx;
    private volatile bool _running;
    private long _seq;
    private double _nextAnnounce, _nextSweep;
    private List<IPEndPoint>? _sweep;
    private int _sweepAt;
    private double _sweepWait;
    private IPAddress? _localV4;

    public LanMesh(string room, string selfId, string name, string aircraft, int basePort = BasePort)
    {
        _room = room ?? "";
        _selfId = string.IsNullOrEmpty(selfId) ? Guid.NewGuid().ToString("N").Substring(0, 8) : selfId;
        _name = name ?? "";
        _ac = aircraft ?? "";
        _basePort = basePort;
    }

    public string SelfId => _selfId;
    public string Room => _room;
    /// <summary>The port actually bound, which is <see cref="BasePort"/> unless something else had it.</summary>
    public int Port { get; private set; }
    public bool Running => _running;
    /// <summary>Why <see cref="Start"/> failed, or null.</summary>
    public string? Error { get; private set; }
    public int PeerCount { get { lock (_gate) return _peers.Count; } }
    /// <summary>Probes sent since the start — what to look at when nobody is finding anybody.</summary>
    public int ProbesSent { get; private set; }
    /// <summary>Datagrams dropped for being bigger than a datagram can be.</summary>
    public int Oversize { get; private set; }

    /// <summary>Bind and start listening. False with <see cref="Error"/> set if no port could be had.</summary>
    public bool Start()
    {
        for (int i = 0; i < PortsPerHost; i++)
        {
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Bind(new IPEndPoint(IPAddress.Any, _basePort + i));
                // The receive is blocking, so it must be able to give up: on Unix, disposing a socket that a
                // thread is sitting in recvfrom() on waits for that call to return, and a call waiting for a
                // datagram that never comes never returns. A quarter-second wake costs nothing and means the
                // listening thread notices it has been asked to stop.
                s.ReceiveTimeout = (int)(RxWakeS * 1000);
                _sock = s;
                Port = _basePort + i;
                break;
            }
            catch (SocketException) { }
        }
        if (_sock == null)
        {
            Error = $"no free port in {_basePort}..{_basePort + PortsPerHost - 1}";
            return false;
        }
        _localV4 = LocalV4();
        _running = true;
        _rx = new Thread(ReceiveLoop) { IsBackground = true, Name = "LanMesh" };
        _rx.Start();
        return true;
    }

    /// <summary>The name and aeroplane announced from now on (the pilot changed one on the menu).</summary>
    public void SetSelf(string name, string aircraft)
    {
        bool changed = _name != (name ?? "") || _ac != (aircraft ?? "");
        _name = name ?? "";
        _ac = aircraft ?? "";
        if (changed) _nextAnnounce = 0;      // tell everyone now rather than in a second
    }

    /// <summary>
    /// Somewhere to look that the sweep would not find: "192.168.1.42", "192.168.1.42:47801", or just "42",
    /// which means that host on our own subnet — four digits a pilot can read out across a room.
    /// </summary>
    public bool AddHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text!.Trim();
        int port = _basePort;
        int colon = t.LastIndexOf(':');
        if (colon > 0 && int.TryParse(t.Substring(colon + 1), out int p) && p > 0 && p < 65536)
        {
            port = p;
            t = t.Substring(0, colon);
        }
        IPAddress? ip;
        if (!IPAddress.TryParse(t, out ip))
        {
            // A bare number is the last octet of our own subnet.
            if (!int.TryParse(t, out int last) || last < 0 || last > 255 || _localV4 == null) return false;
            byte[] b = _localV4.GetAddressBytes();
            b[3] = (byte)last;
            ip = new IPAddress(b);
        }
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        lock (_gate)
        {
            var end = new IPEndPoint(ip, port);
            foreach (IPEndPoint h in _hints) if (h.Equals(end)) return true;
            _hints.Add(end);
        }
        // Say hello straight away, and to the neighbouring ports in case they are on a second instance.
        for (int i = 0; i < PortsPerHost; i++) SendTo(new IPEndPoint(ip, port + i), 'h', null);
        return true;
    }

    /// <summary>
    /// Pilots too far away to see, who get state every <see cref="FarDivisor"/>th tick instead of every tick.
    /// Pass null or an empty set to put everyone back on the full rate. Called from the main thread.
    /// </summary>
    public void SetFarPeers(ICollection<string>? ids)
    {
        lock (_gate)
        {
            int phase = 0;
            foreach (Peer pr in _peers.Values)
            {
                bool far = ids != null && ids.Contains(pr.Id);
                pr.Every = far ? FarDivisor : 1;
                pr.Phase = far ? phase++ % FarDivisor : 0;      // spread them over the ticks
            }
        }
    }

    /// <summary>This aeroplane's state, to everyone who should get it this tick.</summary>
    public void SendState(string payload)
    {
        long seq = ++_seq;
        lock (_gate)
        {
            foreach (Peer pr in _peers.Values)
                if (pr.Every <= 1 || seq % pr.Every == pr.Phase) SendTo(pr.End, 's', payload);
        }
    }

    /// <summary>Something that is not state (a radio call): to every pilot, this tick, no rate limit.</summary>
    public void SendAll(string payload)
    {
        lock (_gate)
            foreach (Peer pr in _peers.Values) SendTo(pr.End, 's', payload);
    }

    /// <summary>To one pilot, if we still know them.</summary>
    public bool SendToPeer(string peerId, string payload)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(peerId, out Peer? pr)) return false;
            SendTo(pr.End, 's', payload);
            return true;
        }
    }

    public bool TryDequeue(out LanEvent e)
    {
        lock (_gate)
        {
            if (_inbox.Count == 0) { e = default; return false; }
            e = _inbox.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Housekeeping, from the main thread: drop the pilots who stopped answering, say we are still here, and
    /// keep sweeping while we are alone. Call it every frame; it does its own timing.
    /// </summary>
    public void Tick()
    {
        if (!_running) return;
        double nowS = Now;

        List<Peer>? lost = null;
        lock (_gate)
        {
            foreach (Peer pr in _peers.Values)
                if (nowS - pr.LastSeen > TimeoutS) (lost ??= new List<Peer>()).Add(pr);
            if (lost != null)
                foreach (Peer pr in lost)
                {
                    _peers.Remove(pr.Id);
                    _inbox.Enqueue(new LanEvent(LanEventKind.Bye, pr.Id, pr.Name, pr.Ac, null));
                }
        }

        if (nowS >= _nextAnnounce)
        {
            _nextAnnounce = nowS + AnnounceS;
            lock (_gate)
            {
                foreach (Peer pr in _peers.Values) SendTo(pr.End, 'h', null);
                foreach (IPEndPoint h in _hints) SendTo(h, 'h', null);
            }
        }

        // Sweep while there is nobody here. One pilot found is the whole room found, so this stops as soon
        // as anybody answers — and starts again if everyone leaves.
        if (PeerCount == 0)
        {
            if (_sweep == null && nowS >= _nextSweep)
            {
                _sweep = PlanSweep();
                _sweepAt = 0;
                if (_sweepWait <= 0) _sweepWait = SweepS;
                _nextSweep = nowS + _sweepWait;
                _sweepWait = Math.Min(SweepBackoffMaxS, _sweepWait * 2);   // nothing out there: ask less often
            }
            if (_sweep != null)
            {
                int end = Math.Min(_sweep.Count, _sweepAt + ProbesPerTick);
                for (; _sweepAt < end; _sweepAt++) { SendTo(_sweep[_sweepAt], 'p', null); ProbesSent++; }
                if (_sweepAt >= _sweep.Count) _sweep = null;
            }
        }
        else { _sweep = null; _sweepWait = 0; _nextSweep = nowS; }
    }

    /// <summary>Who is here, for the roster line and for a frame-time probe.</summary>
    public List<(string Id, string Name, string Ac, string Address, int Every)> Peers()
    {
        var list = new List<(string, string, string, string, int)>();
        lock (_gate)
            foreach (Peer pr in _peers.Values) list.Add((pr.Id, pr.Name, pr.Ac, pr.End.ToString(), pr.Every));
        return list;
    }

    public void Dispose()
    {
        if (!_running) { _sock?.Dispose(); return; }
        try
        {
            lock (_gate) foreach (Peer pr in _peers.Values) SendTo(pr.End, 'b', null);
        }
        catch (Exception) { }
        Abandon();
    }

    /// <summary>
    /// What a crash, a dead battery or a phone leaving the building looks like from outside: stop listening
    /// and say nothing at all, so the others have to notice. Note that merely going SILENT is not this — a
    /// mesh still answers probes from its listening thread, and a pilot who is still listening is still here.
    /// </summary>
    public void Abandon()
    {
        if (!_running) return;
        _running = false;
        // Order matters: let the listening thread see _running go false and LEAVE, and only then close the
        // socket under it. The other way round hangs — see ReceiveTimeout in Start.
        try { _rx?.Join((int)(RxWakeS * 1000) + 350); } catch (Exception) { }
        _rx = null;
        try { _sock?.Dispose(); } catch (Exception) { }
        _sock = null;
    }

    // ---- the wire

    /// <summary>
    /// One datagram: a header line of fields that cannot contain the separator (the free text is base64), then
    /// a newline, then the game's own payload untouched. Deliberately not JSON — Core has no JSON library and
    /// a header this small does not need one.
    /// </summary>
    private void SendTo(IPEndPoint to, char type, string? payload)
    {
        Socket? s = _sock;
        if (s == null) return;
        var sb = new StringBuilder(256);
        sb.Append(Magic).Append('|').Append(type).Append('|')
          .Append(B64(_room)).Append('|').Append(_selfId).Append('|')
          .Append(B64(_name)).Append('|').Append(B64(_ac)).Append('|').Append(Port).Append('|');
        if (type == 'h' || type == 'p') Gossip(sb, to);
        sb.Append('\n');
        if (payload != null) sb.Append(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        if (bytes.Length > MaxDatagram) { Oversize++; return; }
        try { s.SendTo(bytes, to); }
        catch (SocketException) { }          // a host that is not there is the normal case for a probe
        catch (ObjectDisposedException) { }
    }

    /// <summary>Addresses of pilots we know, so one answered probe seeds the whole room.</summary>
    private void Gossip(StringBuilder sb, IPEndPoint exclude)
    {
        int n = 0;
        lock (_gate)
            foreach (Peer pr in _peers.Values)
            {
                if (n >= MaxGossip) break;
                if (pr.End.Equals(exclude)) continue;
                if (n > 0) sb.Append(',');
                sb.Append(pr.End.Address).Append(':').Append(pr.End.Port);
                n++;
            }
    }

    private void ReceiveLoop()
    {
        var buf = new byte[MaxDatagram];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            int n;
            try { n = _sock!.ReceiveFrom(buf, ref from); }
            catch (SocketException) { if (!_running) break; continue; }
            catch (ObjectDisposedException) { break; }
            catch (NullReferenceException) { break; }
            if (n <= 0) continue;
            try { Handle(Encoding.UTF8.GetString(buf, 0, n), (IPEndPoint)from); }
            catch (Exception) { }            // a malformed datagram is not a reason to stop listening
        }
    }

    private void Handle(string text, IPEndPoint from)
    {
        int nl = text.IndexOf('\n');
        string head = nl < 0 ? text : text.Substring(0, nl);
        string? payload = nl < 0 ? null : text.Substring(nl + 1);
        string[] f = head.Split('|');
        if (f.Length < 8 || f[0] != Magic) return;
        char type = f[1].Length == 1 ? f[1][0] : '?';
        string room = UnB64(f[2]);
        string id = f[3];
        if (id == _selfId) return;                       // our own voice coming back
        if (room != _room) return;                       // another game in the same building
        string name = UnB64(f[4]), ac = UnB64(f[5]);
        if (!int.TryParse(f[6], out int theirPort) || theirPort <= 0) theirPort = from.Port;
        var end = new IPEndPoint(from.Address, theirPort);
        double now = Now;

        bool isNew = false, changed = false;
        lock (_gate)
        {
            if (!_peers.TryGetValue(id, out Peer? pr))
            {
                pr = new Peer { Id = id, Name = name, Ac = ac, End = end, LastSeen = now };
                _peers[id] = pr;
                isNew = true;
            }
            else
            {
                changed = pr.Name != name || pr.Ac != ac;
                pr.Name = name; pr.Ac = ac; pr.End = end; pr.LastSeen = now;
            }

            if (type == 'b')
            {
                _peers.Remove(id);
                _inbox.Enqueue(new LanEvent(LanEventKind.Bye, id, name, ac, null));
                return;
            }
            if (isNew || changed) _inbox.Enqueue(new LanEvent(LanEventKind.Peer, id, name, ac, null));
            if (type == 's' && payload != null) _inbox.Enqueue(new LanEvent(LanEventKind.State, id, name, ac, payload));
        }

        // Answer a probe always; answer an announcement only if they are new to us or it has been a second,
        // or two instances would talk to each other for ever.
        if (type == 'p' || isNew)
        {
            SendTo(end, 'h', null);
            lock (_gate) if (_peers.TryGetValue(id, out Peer? pr2)) pr2.LastReplied = now;
        }

        // Everyone they know is now somebody we can ask.
        if ((type == 'h' || type == 'p') && f.Length >= 8 && f[7].Length > 0) Seed(f[7]);
    }

    /// <summary>Addresses from somebody's gossip: say hello to the ones we do not already have.</summary>
    private void Seed(string csv)
    {
        foreach (string item in csv.Split(','))
        {
            int c = item.LastIndexOf(':');
            if (c <= 0) continue;
            if (!IPAddress.TryParse(item.Substring(0, c), out IPAddress? ip)) continue;
            if (!int.TryParse(item.Substring(c + 1), out int p) || p <= 0 || p >= 65536) continue;
            var end = new IPEndPoint(ip, p);
            bool known = false;
            lock (_gate)
            {
                foreach (Peer pr in _peers.Values) if (pr.End.Equals(end)) { known = true; break; }
                if (!known && ip.Equals(_localV4) && p == Port) known = true;     // ourselves
            }
            if (!known) SendTo(end, 'h', null);
        }
    }

    // ---- the network we are on

    /// <summary>
    /// Our own address on the network, found by asking the routing table rather than by listing interfaces:
    /// a UDP socket "connected" to a documentation address sends nothing but knows which interface it would
    /// have gone out of. Works the same on iOS, macOS and a test runner.
    /// </summary>
    private static IPAddress? LocalV4()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));       // TEST-NET-1, RFC 5737
            return (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException) { return null; }
    }

    /// <summary>The mask of the interface carrying <paramref name="ip"/>, or /24 if it cannot be had.</summary>
    private static IPAddress Mask(IPAddress ip)
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                    if (u.Address.Equals(ip) && u.IPv4Mask != null && !u.IPv4Mask.Equals(IPAddress.Any))
                        return u.IPv4Mask;
        }
        catch (Exception) { }
        return IPAddress.Parse("255.255.255.0");
    }

    /// <summary>
    /// Every host of our own subnet at the base port, plus this machine's other ports — which is how the Mac
    /// app, the editor and a test runner on one box see each other. A subnet wider than
    /// <see cref="MaxSweepHosts"/> is swept as the /24 around us: a /16 is 65,000 probes and the pilot is
    /// going to be on our own /24.
    /// </summary>
    private List<IPEndPoint> PlanSweep()
    {
        var list = new List<IPEndPoint>();
        for (int i = 0; i < PortsPerHost; i++)
            if (_basePort + i != Port) list.Add(new IPEndPoint(IPAddress.Loopback, _basePort + i));

        IPAddress? me = _localV4 ??= LocalV4();
        if (me == null || LoopbackOnly) return list;
        byte[] a = me.GetAddressBytes(), m = Mask(me).GetAddressBytes();
        uint ip = (uint)(a[0] << 24 | a[1] << 16 | a[2] << 8 | a[3]);
        uint mask = (uint)(m[0] << 24 | m[1] << 16 | m[2] << 8 | m[3]);
        uint hosts = ~mask;
        if (hosts > MaxSweepHosts) { mask = 0xFFFFFF00u; hosts = 255; }
        uint net = ip & mask;
        for (uint h = 1; h < hosts; h++)
        {
            uint t = net | h;
            if (t == ip) continue;
            list.Add(new IPEndPoint(new IPAddress(new[] { (byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t }), _basePort));
        }
        return list;
    }


    private static string B64(string s) => s.Length == 0 ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('|', '_');
    private static string UnB64(string s)
    {
        if (s.Length == 0) return "";
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s.Replace('_', '|'))); }
        catch (FormatException) { return ""; }
    }
}
