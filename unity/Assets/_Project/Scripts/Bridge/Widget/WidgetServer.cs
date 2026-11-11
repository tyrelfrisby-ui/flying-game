using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FlyingGame.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>
    /// The widget's control surface (owner 2026-10-07):
    ///   TCP 47830 — newline-delimited JSON commands for Glass Overlay / its MCP layer; each gets a JSON reply line;
    ///               {"cmd":"subscribe","hz":10} streams the state.
    ///   HTTP 47831 — a phone / iPad remote (GET /), POST /cmd (same JSON), GET /state.
    /// Commands: scenario, controls, preset, pause, resume, reset, timescale, view, show, state, fleet, gamepad (docs/WIDGET.md).
    /// </summary>
    public sealed class WidgetServer : MonoBehaviour
    {
        public static readonly int HttpPort = int.TryParse(System.Environment.GetEnvironmentVariable("AERO_WIDGET_PORT"), out int hp) && hp > 1024 ? hp : 47831;
        public static readonly int TcpPort = HttpPort - 1;   // 47830 / 47831 (a capture instance: its own pair)
        public AeroWidget Widget;
        public static string LocalIp { get; private set; } = "127.0.0.1";

        /// <summary>The ONE directory a <c>snapshot</c> may write into, inside the widget's own persistent data
        /// directory (its bundle id is its own, so this is the widget's folder and not the game's). Never a path
        /// in the repository, which is public, and never a path a caller gets to choose — see the snapshot case.</summary>
        public static string SnapshotDir => System.IO.Path.Combine(Application.persistentDataPath, WidgetAccess.SnapshotDirName);

        // One listener per address family: loopback-only needs 127.0.0.1 AND ::1, which a single socket cannot do.
        private readonly List<TcpListener> _tcpListeners = new(), _httpListeners = new();
        private HttpListener _http;
        private volatile bool _run = true;
        private readonly ConcurrentQueue<(string json, System.Action<string> reply, string src, Auth auth)> _inbox = new();
        private int _connSeq;

        /// <summary>
        /// WHAT A REQUEST IS ALLOWED TO DO, carried with it from the socket to the main thread.
        ///
        /// <see cref="Loopback"/> is decided once, from the connection's own remote endpoint, and never from
        /// anything the client says about itself — a field in the JSON would be the client marking its own
        /// homework. <see cref="Authed"/> is what a <c>hello</c> with the right token established on a TCP
        /// connection; on HTTP, which has no connection to remember anything on, the request is judged on its own
        /// token before it ever reaches here and arrives already decided.
        /// </summary>
        private sealed class Auth
        {
            public bool Loopback;
            public volatile bool Authed;
            /// <summary>The TCP connection this came in on, or null for HTTP: there is nothing to subscribe.</summary>
            public Conn Conn;
        }
        /// <summary>The widget's own web remotes (protocol 4): id → (name, last seen). Main thread only.</summary>
        private readonly Dictionary<string, (string name, float seen)> _remotes = new();
        private readonly List<(Conn c, float hz, float next)> _subs = new();
        private readonly object _subLock = new();

        /// <summary>
        /// ONE TCP CLIENT'S OUTPUT: a bounded queue and a thread of its own to drain it.
        ///
        /// Every line the widget sends a connected client — command replies, pushed events, and the state stream at
        /// up to 60 Hz — used to be written from <see cref="Update"/>, on Unity's MAIN THREAD, inside the
        /// subscriber lock. A StreamWriter with AutoFlush on a TCP socket blocks when the peer stops reading and
        /// the kernel's send buffer fills, which a subscriber does simply by being suspended, backgrounded, on a
        /// bad Wi-Fi hop, or stopped at a breakpoint. The widget's frame then stops — rendering, physics, Syphon,
        /// NDI, everything — until that one socket drains or the TCP timeout expires, which is minutes.
        /// (pre-ship audit, 2026-11-11.)
        ///
        /// So the main thread now only ENQUEUES, which cannot block, and this thread does the writing and takes the
        /// stall. The queue is bounded because an unbounded one turns a stalled subscriber into a memory leak at 60
        /// state lines a second; over the bound, lines are dropped and counted, and a client that has been dropping
        /// for about a minute is hung up on — it is not reading, and a connection nobody reads is not a subscriber.
        /// Dropping state frames is the right loss: each one is a complete snapshot, so a client that misses some
        /// is merely behind, not wrong.
        /// </summary>
        private sealed class Conn
        {
            public const int MaxQueued = 256;          // ~4 s of a 60 Hz stream
            public const int DropsBeforeHangUp = 600;  // ~1 minute at 10 Hz of dropping everything

            public readonly StreamWriter W;
            public readonly string Src;
            public volatile bool Alive = true;
            private readonly ConcurrentQueue<string> _q = new();
            private readonly SemaphoreSlim _wake = new(0);
            private int _depth, _dropped;

            public Conn(StreamWriter w, string src)
            {
                W = w; Src = src;
                new Thread(Pump) { IsBackground = true, Name = "widget-out-" + src }.Start();
            }

            /// <summary>Queue a line. Never blocks, never throws, and is safe from any thread.</summary>
            public void Send(string line)
            {
                if (!Alive || line == null) return;
                if (Interlocked.Increment(ref _depth) > MaxQueued)
                {
                    Interlocked.Decrement(ref _depth);
                    if (Interlocked.Increment(ref _dropped) > DropsBeforeHangUp)
                    {
                        Debug.LogWarning($"[Widget] {Src} stopped reading: {_dropped} lines dropped, hanging up");
                        Close();
                    }
                    return;
                }
                _q.Enqueue(line);
                try { _wake.Release(); } catch { }
            }

            private void Pump()
            {
                try
                {
                    while (Alive)
                    {
                        if (!_wake.Wait(1000)) continue;         // the timeout is how Alive=false gets noticed
                        if (!_q.TryDequeue(out string line)) continue;
                        Interlocked.Decrement(ref _depth);
                        lock (W) W.WriteLine(line);              // the write that can block now blocks HERE
                    }
                }
                catch { }                                        // a closed socket ends this thread, nothing else
                Alive = false;
            }

            public void Close() { Alive = false; try { _wake.Release(); } catch { } }
        }

        /// <summary>
        /// IS THE WIDGET ON THE NETWORK AT ALL? Off by default (Ty, 2026-11-11, rule 6), on with AERO_WIDGET_LAN=1.
        ///
        /// It used to bind IPAddress.IPv6Any / IPAddress.Any with no authentication of any kind, which on a laptop
        /// is two open control ports on every network it joins — a hotel, a conference hall, a client's office —
        /// each answering "fly the aeroplane" and, until earlier on this lane, "write this file wherever I say".
        /// Binding is the part that matters most: a port that is not listening cannot be scanned, cannot prompt the
        /// macOS firewall, and does not appear on Bonjour.
        ///
        /// The default costs nothing on the podcast desk. Everything that drives the widget from the Mac itself —
        /// the MCP layer, a render script, Safari on 127.0.0.1 — is loopback and is unaffected. What needs the
        /// switch is a second machine: Glass Overlay on another Mac, or the phone remote.
        /// </summary>
        public static readonly bool Lan = System.Environment.GetEnvironmentVariable(WidgetAccess.LanEnvVar) == "1";

        /// <summary>
        /// THE ACCESS TOKEN a client from the network must present. Generated on first run into the widget's OWN
        /// config file (Application.persistentDataPath/widget-token.txt — the widget's bundle id is its own, so
        /// this is its own directory, and it is never the repository, which is public).
        ///
        /// Read once, on the main thread, because Application.persistentDataPath is a Unity API and must not be
        /// touched from the network thread.
        /// </summary>
        public static string Token { get; private set; } = "";

        private void Awake()
        {
            // Even when the ports are loopback-only, the token is made now: it is what the window shows, and it is
            // what the user types into a phone after switching the LAN on. A token that only exists once you need
            // it is a token you cannot find when you need it.
            Token = WidgetAccess.ReadOrCreateToken(Application.persistentDataPath);
        }

        private void Start()
        {
            // Everything network-ish off the main thread: a DNS lookup of the Mac's own name can hang for seconds on macOS
            // (it froze the widget's first frame), and HttpListener's start can block too.
            new Thread(() =>
            {
                if (Lan)
                {
                    try
                    {
                        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address)) { LocalIp = ua.Address.ToString(); goto found; }
                        }
                        found:;
                    }
                    catch { }
                }
                _tcpListeners.AddRange(Bind(TcpPort, "TCP", out bool tcpBoth));
                foreach (var l in _tcpListeners) { var ll = l; new Thread(() => AcceptLoop(ll)) { IsBackground = true }.Start(); }
                // HTTP: a small HTTP/1.1 server on its own socket (Mono's HttpListener answered 400 to IPv6 Host headers).
                _httpListeners.AddRange(Bind(HttpPort, "HTTP", out bool httpBoth));
                foreach (var l in _httpListeners) { var ll = l; new Thread(() => HttpAcceptLoop(ll)) { IsBackground = true }.Start(); }
                // What `hello` reports: can a client reach us over BOTH address families. True of the dual-stack
                // wildcard, and true of loopback-only when both 127.0.0.1 and ::1 bound.
                TcpDualStack = tcpBoth; HttpIPv6 = httpBoth;
                // Bonjour only when there is something on the network to find. Advertising a loopback-only port
                // would be telling every device on the Wi-Fi about a service none of them can reach.
                if (!AeroWidget.Capture && Lan) Advertise();
                Debug.Log($"[Widget] control tcp {TcpPort}, remote http://{LocalIp}:{HttpPort}/ · {(Lan ? "LAN (token required off this machine)" : "loopback only — set " + WidgetAccess.LanEnvVar + "=1 for the network")}");
            }) { IsBackground = true }.Start();
        }

        /// <summary>
        /// Open <paramref name="port"/>. LAN off: 127.0.0.1 AND ::1, as two sockets.
        ///
        /// Two, not one, because a dual-stack listener on ::1 does NOT accept 127.0.0.1 — the IPv4-mapped
        /// acceptance a DualMode socket gives you comes from binding the WILDCARD, not from binding a loopback
        /// address. "localhost" resolves to ::1 first for most clients on macOS and to 127.0.0.1 for others
        /// (curl, the test harnesses), so binding only one of them breaks half the local callers.
        ///
        /// LAN on: the dual-stack wildcard exactly as before, so a device that can only reach the Mac by an IPv6
        /// link-local address still connects (2026-10-09), falling back to IPv4-only if the stack refuses.
        /// </summary>
        private static List<TcpListener> Bind(int port, string what, out bool bothFamilies)
        {
            var made = new List<TcpListener>();
            bothFamilies = false;
            if (Lan)
            {
                try { var l = new TcpListener(IPAddress.IPv6Any, port); l.Server.DualMode = true; l.Start(); made.Add(l); bothFamilies = true; return made; }
                catch (System.Exception e6) { Debug.LogWarning($"[Widget] {what} IPv6 dual-stack unavailable ({e6.Message}): IPv4 only"); }
                try { var l = new TcpListener(IPAddress.Any, port); l.Start(); made.Add(l); }
                catch (System.Exception e) { Debug.LogWarning($"[Widget] {what}: {e.Message}"); }
                return made;
            }
            foreach (var a in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                try { var l = new TcpListener(a, port); l.Start(); made.Add(l); }
                catch (System.Exception e) { Debug.LogWarning($"[Widget] {what} on {a}: {e.Message}"); }
            }
            bothFamilies = made.Count == 2;
            return made;
        }

        /// <summary>Is this connection from the machine the widget runs on? Decided from the socket, once, when the
        /// connection is accepted — never from anything the client says about itself.</summary>
        private static bool IsLocal(TcpClient c)
        {
            try { return WidgetAccess.IsLoopback((c.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString()); }
            catch { return false; }   // an endpoint we cannot read is not this machine
        }

        public static bool TcpDualStack { get; private set; }
        public static bool HttpIPv6 { get; private set; }
        // Bonjour through the system's mDNS responder (dns_sd.h; Process.Start isn't available in the IL2CPP player).
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
        private static extern int DNSServiceRegister(out System.IntPtr sdRef, uint flags, uint interfaceIndex, string name, string regtype, string domain, string host,
                                                     ushort portNetworkOrder, ushort txtLen, byte[] txtRecord, System.IntPtr callBack, System.IntPtr context);
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
        private static extern void DNSServiceRefDeallocate(System.IntPtr sdRef);
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")] private static extern int getpid();
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")] private static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);
        /// <summary>This process's physical footprint (what Activity Monitor calls Memory), MB; −1 if it can't be read.</summary>
        public static double FootprintMb()
        {
            try { var b = new byte[512]; return proc_pid_rusage(getpid(), 0, b) == 0 ? System.BitConverter.ToUInt64(b, 72) / 1048576.0 : -1; }   // (rusage_info_v0.ri_phys_footprint)
            catch { return -1; }
        }

        // CAPTURE GUARD (2026-10-13, Glass: the capture instance was found at 50 GB after 4 h 20 m and took the Mac's memory):
        // the capture logs its footprint, quits on {"cmd":"quit"}, after IdleQuitS with no command, or past MaxFootprintMb.
        public const float IdleQuitS = 600f, GuardEveryS = 30f; public const double MaxFootprintMb = 8192;
        private float _lastCmdAt, _guardAt, _logAt, _quitAt = -1f;
        private void CaptureGuard()
        {
            float now = Time.unscaledTime;
            if (_quitAt >= 0 && now >= _quitAt) { Debug.Log("[Capture] quit (asked)"); Application.Quit(); _quitAt = -1; return; }
            if (now < _guardAt) return;
            _guardAt = now + GuardEveryS;
            double mb = FootprintMb();
            if (now >= _logAt) { _logAt = now + 300f; Debug.Log($"[Capture] footprint {mb:0} MB, idle {now - _lastCmdAt:0} s, history {Widget?.Review.History.Count}"); }
            if (mb > MaxFootprintMb) { Debug.LogError($"[Capture] footprint {mb:0} MB over {MaxFootprintMb:0} MB: quitting"); Application.Quit(); return; }
            if (now - _lastCmdAt > IdleQuitS) { Debug.Log($"[Capture] no command for {IdleQuitS:0} s: quitting"); Application.Quit(); }
        }
        private readonly List<System.IntPtr> _bonjour = new();
        public static bool BonjourOk { get; private set; }

        /// <summary>Bonjour (2026-10-09): _aerowidget._tcp on 47830 with TXT control/http/protocol, and the remote as _http._tcp on
        /// 47831 — clients find the widget without typing an IP. Registered while the widget runs.</summary>
        private void Advertise()
        {
            byte[] Txt(params string[] kv)
            {
                var ms = new System.IO.MemoryStream();
                foreach (var e in kv) { var b = Encoding.UTF8.GetBytes(e); ms.WriteByte((byte)b.Length); ms.Write(b, 0, b.Length); }
                return ms.ToArray();
            }
            ushort Net(int port) => (ushort)(((port & 0xff) << 8) | ((port >> 8) & 0xff));
            void Reg(string name, string type, int port, byte[] txt)
            {
                try
                {
                    int err = DNSServiceRegister(out var sd, 0, 0, name, type, null, null, Net(port), (ushort)txt.Length, txt, System.IntPtr.Zero, System.IntPtr.Zero);
                    if (err == 0) { lock (_bonjour) _bonjour.Add(sd); BonjourOk = true; }
                    else Debug.LogWarning($"[Widget] Bonjour {type}: error {err}");
                }
                catch (System.Exception e) { Debug.LogWarning("[Widget] Bonjour: " + e.Message); }
            }
            Reg("Aero Widget", "_aerowidget._tcp", TcpPort, Txt($"control={TcpPort}", $"http={HttpPort}", $"protocol={Protocol}"));
            Reg("Aero Widget remote", "_http._tcp", HttpPort, Txt("path=/"));
        }
        public const int Protocol = 8;

        private void OnDestroy()
        {
            _run = false;
            lock (_subLock) { foreach (var s in _subs) s.c.Close(); _subs.Clear(); }   // the writer threads end with their queues
            lock (_bonjour) foreach (var sd in _bonjour) { try { DNSServiceRefDeallocate(sd); } catch { } }
            foreach (var l in _tcpListeners) { try { l.Stop(); } catch { } }
            foreach (var l in _httpListeners) { try { l.Stop(); } catch { } }
            try { _http?.Stop(); } catch { }
        }

        // ---- TCP ----
        private void AcceptLoop(TcpListener l)
        {
            while (_run)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                c.NoDelay = true;
                new Thread(() => ClientLoop(c)) { IsBackground = true }.Start();
            }
        }

        private void ClientLoop(TcpClient c)
        {
            using var stream = c.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            string src = "tcp-" + Interlocked.Increment(ref _connSeq);   // this connection's identity (the pilot's source)
            // Everything this connection is ever sent goes through its own queue and its own writer thread: the
            // main thread must never touch the socket. See Conn.
            var conn = new Conn(writer, src);
            // WHO THIS CONNECTION IS, decided from the SOCKET and not from anything the client says about itself,
            // once, here. A loopback connection is authenticated from the start (Ty's rule 4): a client on this
            // machine can already read the token file, so asking it for the token protects nothing and would break
            // every local caller there is.
            var auth = new Auth { Loopback = IsLocal(c), Conn = conn };
            auth.Authed = auth.Loopback;
            try
            {
                string line;
                while (_run && conn.Alive && (line = ReadLineCapped(reader, conn)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    _inbox.Enqueue((line, reply => conn.Send(reply), src, auth));
                    // "subscribe" used to be registered HERE, on the reader thread, by looking for the word in the
                    // raw line. It is now a command like any other (Handle's subscribe arm), for two reasons: a
                    // client that sends hello and subscribe back to back would otherwise have its subscription
                    // judged before the hello had been processed, and the old test matched any line that merely
                    // CONTAINED "subscribe" — a caption would have done it.
                }
            }
            catch { }
            conn.Close();
            lock (_subLock) _subs.RemoveAll(s => s.c == conn);
            c.Close();
        }

        /// <summary>
        /// One command line, or null at end of stream — the same contract as StreamReader.ReadLine, with a LIMIT.
        ///
        /// The reader used to be a bare ReadLine(), which has no limit at all: a peer that opens the socket and
        /// sends bytes without ever sending a newline grows one string until the process dies, and it costs the
        /// peer nothing to do it (pre-ship audit, 2026-11-11). Before this lane the port answered the whole
        /// network, so "a peer" meant anyone.
        ///
        /// Over the cap the line is DISCARDED UP TO THE NEXT NEWLINE and the connection is told why, rather than
        /// dropped: a client that sends one oversized command — a huge caption, a pasted blob — stays connected and
        /// sees what happened. The limit is ~60× the largest command the protocol has (a vectorStyle with every
        /// field set measures about 1.1 KB), so no real client can reach it by accident.
        /// </summary>
        private static string ReadLineCapped(StreamReader reader, Conn conn)
        {
            var sb = new StringBuilder(256);
            while (true)
            {
                int ch = reader.Read();
                if (ch < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (ch == '\n') return sb.ToString();
                if (ch == '\r') continue;
                if (sb.Length >= WidgetAccess.MaxLineBytes)
                {
                    // Swallow the rest of this line so the next one still parses, then say so.
                    while ((ch = reader.Read()) >= 0 && ch != '\n') { }
                    conn.Send($"{{\"ok\":false,\"error\":\"command line longer than {WidgetAccess.MaxLineBytes} characters: discarded\"}}");
                    if (ch < 0) return null;
                    sb.Clear();
                    continue;
                }
                sb.Append((char)ch);
            }
        }

        // ---- HTTP (phone / iPad remote): GET / (the page), GET /state[?from=…&name=…], POST /cmd ----
        private void HttpAcceptLoop(TcpListener l)
        {
            while (_run)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => ServeRaw(c));
            }
        }

        private void ServeRaw(TcpClient c)
        {
            try
            {
                c.NoDelay = true; c.ReceiveTimeout = 5000;
                using var ns = c.GetStream();
                // Request line + headers (ASCII), then the body by Content-Length. BOTH ARE CAPPED (pre-ship audit,
                // 2026-11-11). The head loop already stopped at 16 KB, but it stopped by BREAKING OUT and then
                // parsing whatever it had as though the request were complete — so a peer that sent 16 KB of
                // header bytes and no blank line got its truncated first line treated as a request line. It is now
                // a refusal: a request whose headers do not end is not a request.
                var head = new StringBuilder(); int b; int crlf = 0; bool headComplete = false;
                while ((b = ns.ReadByte()) >= 0)
                {
                    head.Append((char)b);
                    crlf = (b == '\r' || b == '\n') ? crlf + 1 : 0;
                    if (crlf == 4) { headComplete = true; break; }
                    if (head.Length > WidgetAccess.MaxHeadBytes) break;
                }
                if (!headComplete) { Refuse(ns, 431, "request headers too long or incomplete"); return; }
                string[] lines = head.ToString().Split(new[] { "\r\n" }, System.StringSplitOptions.None);
                string[] rl = lines[0].Split(' ');
                if (rl.Length < 2) return;
                string method = rl[0], target = rl[1];
                int len = 0;
                foreach (var l in lines) if (l.StartsWith("Content-Length:", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out len);
                // THE BODY WAS ALLOCATED FROM THE REQUEST'S OWN CLAIM: `new byte[len]` with len straight out of
                // Content-Length, so a 40-byte request saying "Content-Length: 2000000000" asked the widget for a
                // 2 GB array — a crash, or a Mac on its knees, per request and for free. The remote page's largest
                // POST is a few hundred bytes.
                if (len > WidgetAccess.MaxBodyBytes) { Refuse(ns, 413, $"body over {WidgetAccess.MaxBodyBytes} bytes"); return; }
                string body = "";
                if (len > 0)
                {
                    var buf = new byte[len]; int got = 0;
                    while (got < len) { int n = ns.Read(buf, got, len - got); if (n <= 0) break; got += n; }
                    body = Encoding.UTF8.GetString(buf, 0, got);
                }
                string path = target, query = "";
                int qi = target.IndexOf('?'); if (qi >= 0) { path = target.Substring(0, qi); query = target.Substring(qi + 1); }
                var q = new Dictionary<string, string>();
                foreach (var kv in query.Split('&'))
                {
                    int eq = kv.IndexOf('='); if (eq <= 0) continue;
                    try { q[System.Uri.UnescapeDataString(kv.Substring(0, eq))] = System.Uri.UnescapeDataString(kv.Substring(eq + 1).Replace('+', ' ')); } catch { }
                }
                bool local = IsLocal(c);
                string text, type = "application/json";
                if (method == "OPTIONS") text = "";
                else if (path == "/cmd" && method == "POST" || path == "/state")
                {
                    // HTTP HAS NO CONNECTION TO AUTHENTICATE ONCE ON, so a request from the network carries the
                    // token itself and is judged on its own — in the POST body for /cmd, in ?token= for /state.
                    // Loopback skips it entirely, which is why Safari on the Mac and every local script are
                    // unaffected (Ty's rule 4).
                    string presented = null;
                    JObject cmdJson = null;
                    if (path == "/cmd") { try { cmdJson = JObject.Parse(body); presented = (string)cmdJson["token"]; } catch { } }
                    else q.TryGetValue("token", out presented);
                    string why = WidgetAccess.RequestRefusal(local, presented, Token);
                    if (why != null)
                    {
                        // A REFUSAL THAT SAYS WHY, with needsToken so the remote page can ask for it rather than
                        // appearing to be broken (Ty's rule 5: never a silent drop).
                        Refuse(ns, 403, why, needsToken: true);
                        return;
                    }
                    if (path == "/cmd") text = Ask(body, local);
                    else
                    {
                        var o = new JObject { ["cmd"] = "state" };
                        foreach (var k in new[] { "from", "name" }) if (q.TryGetValue(k, out string v)) o[k] = v;
                        if (o["from"] != null && o["name"] == null) o["name"] = "";
                        text = Ask(o.ToString(Newtonsoft.Json.Formatting.None), local);
                    }
                }
                else
                {
                    // THE PAGE ITSELF is served to anyone who can reach the port: it is a UI shell with no data in
                    // it, and a LAN device has to be able to open it in order to be asked for the token at all.
                    // To a browser ON THIS MACHINE the page is given the token to show, so the user can read it
                    // off and type it into a phone — that browser can already read the token file.
                    text = local ? WidgetRemotePage.WithToken(Token) : WidgetRemotePage.Html;
                    type = "text/html; charset=utf-8";
                }
                byte[] payload = Encoding.UTF8.GetBytes(text);
                // NO WILDCARD CORS (pre-ship audit, 2026-11-11). This used to send
                //     Access-Control-Allow-Origin: *
                //     Access-Control-Allow-Headers: *
                // which is the browser being TOLD to let any page in the world read this server's replies. With
                // no authentication behind it, that meant any page the presenter happened to have open could
                // drive the aeroplane, read the state stream, and — before the previous commit — write a file
                // anywhere on the Mac through `snapshot`. No prompt, nothing on screen, from a tab in another
                // window. The browser's same-origin policy was the only thing standing there and the widget was
                // waving it away.
                //
                // Nothing needs it. The remote page is served BY this server, so it is same-origin already and
                // fetch() from it needs no CORS header at all; Glass Overlay and the MCP layer speak TCP, where
                // CORS does not exist. The only thing the header enabled was the attack.
                //
                // An OPTIONS preflight is still answered 200, but with no Allow-Origin, so a cross-origin
                // XHR/fetch now fails in the browser as it should — and fails visibly, in the console, rather
                // than silently succeeding.
                string hdr = $"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
                byte[] hb = Encoding.ASCII.GetBytes(hdr);
                ns.Write(hb, 0, hb.Length); ns.Write(payload, 0, payload.Length); ns.Flush();
            }
            catch { }
            finally { try { c.Close(); } catch { } }
        }

        /// <summary>A short HTTP refusal with a reason in the body. Never a silent drop: the caller is a browser or
        /// a script, and whoever reads the message is the person who can fix the request.</summary>
        private static void Refuse(NetworkStream ns, int status, string why, bool needsToken = false)
        {
            try
            {
                var o = new JObject { ["ok"] = false, ["error"] = why };
                if (needsToken) { o["needsToken"] = true; o["protocol"] = Protocol; }
                byte[] payload = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None));
                byte[] hb = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} \r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                ns.Write(hb, 0, hb.Length); ns.Write(payload, 0, payload.Length); ns.Flush();
            }
            catch { }
        }

        // ---- (legacy HttpListener path, unused) ----
        private void HttpLoop()
        {
            while (_run)
            {
                HttpListenerContext ctx;
                try { ctx = _http.GetContext(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Serve(ctx));
            }
        }

        private void Serve(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath;
                // The wildcard Access-Control-Allow-Origin header that used to be added here is gone for the same
                // reason as in ServeRaw above: it invited every page in the browser to drive this server. This
                // path is the legacy HttpListener one and is not reached (Mono's HttpListener answered 400 to IPv6
                // Host headers, which is why ServeRaw exists) — but a dead path that still carries the bug is how
                // a bug comes back the day someone revives the path.
                bool local = WidgetAccess.IsLoopback(ctx.Request.RemoteEndPoint?.Address?.ToString());
                var qs = ctx.Request.QueryString;
                if (path == "/cmd" && ctx.Request.HttpMethod == "POST")
                {
                    string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
                    string presented = null; try { presented = (string)JObject.Parse(body)["token"]; } catch { }
                    string why = WidgetAccess.RequestRefusal(local, presented, Token);
                    if (why != null) { ctx.Response.StatusCode = 403; Respond(ctx, new JObject { ["ok"] = false, ["error"] = why, ["needsToken"] = true }.ToString(Newtonsoft.Json.Formatting.None), "application/json"); return; }
                    Respond(ctx, Ask(body, local), "application/json");
                }
                else if (path == "/state")
                {
                    string why = WidgetAccess.RequestRefusal(local, qs["token"], Token);
                    if (why != null) { ctx.Response.StatusCode = 403; Respond(ctx, new JObject { ["ok"] = false, ["error"] = why, ["needsToken"] = true }.ToString(Newtonsoft.Json.Formatting.None), "application/json"); return; }
                    // The remote polls this with its id and name (?from=web-…&name=…): that's how it shows up in "remotes".
                    var o = new JObject { ["cmd"] = "state" };
                    if (qs["from"] != null) { o["from"] = qs["from"]; o["name"] = qs["name"] ?? ""; }
                    Respond(ctx, Ask(o.ToString(Newtonsoft.Json.Formatting.None), local), "application/json");
                }
                else Respond(ctx, local ? WidgetRemotePage.WithToken(Token) : WidgetRemotePage.Html, "text/html; charset=utf-8");
            }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        /// <summary>Enqueue a command for the main thread and wait (≤ 1 s) for its reply. The request has already
        /// passed its own token check in ServeRaw, so it arrives here decided: HTTP has no connection on which to
        /// authenticate once.</summary>
        private string Ask(string json, bool loopback)
        {
            var done = new ManualResetEventSlim(false); string result = "{\"ok\":false,\"error\":\"timeout\"}";
            _inbox.Enqueue((json, r => { result = r; done.Set(); }, null, new Auth { Loopback = loopback, Authed = true }));
            done.Wait(1000);
            return result;
        }

        private static void Respond(HttpListenerContext ctx, string text, string type)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            ctx.Response.ContentType = type; ctx.Response.ContentLength64 = b.Length;
            ctx.Response.OutputStream.Write(b, 0, b.Length); ctx.Response.Close();
        }

        // ---- main thread: run the commands, stream the state ----
        private void Update()
        {
            while (_inbox.TryDequeue(out var m))
            {
                string reply;
                try { reply = Handle(JObject.Parse(m.json), m.src, m.auth); }
                catch (System.Exception e) { reply = new JObject { ["ok"] = false, ["error"] = e.Message }.ToString(Newtonsoft.Json.Formatting.None); }
                m.reply?.Invoke(reply);
                _lastCmdAt = Time.unscaledTime;
            }
            if (AeroWidget.Capture) CaptureGuard();
            PruneRemotes();
            lock (_subLock)
            {
                _subs.RemoveAll(s => !s.c.Alive);
                if (_subs.Count == 0) return;
                string st = null;
                for (int i = 0; i < _subs.Count; i++)
                {
                    var s = _subs[i];
                    if (Time.unscaledTime < s.next) continue;
                    st ??= StateJson().ToString(Newtonsoft.Json.Formatting.None);
                    s.c.Send(st);                      // queued, never written, on this thread — see Conn
                    _subs[i] = (s.c, s.hz, Time.unscaledTime + 1f / s.hz);
                }
            }
        }

        /// <summary>Push an event line to every subscribed connection (Glass Overlay listens on its subscribe stream).</summary>
        public void PushEvent(JObject e)
        {
            string line = e.ToString(Newtonsoft.Json.Formatting.None);
            lock (_subLock) foreach (var s in _subs) s.c.Send(line);
        }

        private JArray RemotesJson()
        {
            var a = new JArray();
            foreach (var kv in _remotes) a.Add(new JObject { ["id"] = kv.Key, ["name"] = kv.Value.name });
            return a;
        }

        private void SeeRemote(string id, string name)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith("web-")) return;
            bool changed = !_remotes.TryGetValue(id, out var r) || r.name != (name ?? "");
            _remotes[id] = (name ?? "", Time.unscaledTime);
            if (changed) PushEvent(new JObject { ["event"] = "remotes", ["remotes"] = RemotesJson() });
        }

        private void PruneRemotes()
        {
            List<string> gone = null;
            foreach (var kv in _remotes) if (Time.unscaledTime - kv.Value.seen > 6f) (gone ??= new()).Add(kv.Key);
            if (gone == null) return;
            foreach (var id in gone) _remotes.Remove(id);
            PushEvent(new JObject { ["event"] = "remotes", ["remotes"] = RemotesJson() });
        }

        private static JObject ControlsFit(string id)
        {
            if (!_fits.TryGetValue(id, out var fit))
            {
                try
                {
                    var spec = WidgetAircraftControls.For(id, UnityAircraftConfigLoader.LoadFromStreamingAssets(id));
                    fit = new JObject { ["flaps"] = new JArray(spec.Flaps), ["spoilers"] = spec.Spoilers, ["gear"] = spec.Retractable ? "retractable" : "fixed", ["engines"] = spec.Engines };
                }
                catch { fit = new JObject(); }
                _fits[id] = fit;
            }
            return (JObject)fit.DeepClone();
        }
        private static readonly Dictionary<string, JObject> _fits = new();

        /// <summary>
        /// The first field in this command whose value is not a finite number, or null. Named with its JSON path
        /// ("loading.grossWeightLb", "crowdNed[2]") so the refusal below tells the caller exactly what to fix.
        ///
        /// Strings are checked too, and deliberately: Json.NET converts the string "NaN" to double.NaN just as
        /// happily as the bare literal (measured 2026-11-11, along with Infinity, -Infinity and 1e400 → +∞), so a
        /// check that looked only at number tokens would leave the door open. The cost is that a caption of exactly
        /// "NaN" or "Infinity" is refused; that is a price worth paying and the error says why.
        /// </summary>
        private static string NonFinite(JToken t, string path)
        {
            switch (t.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer:
                    // An integer too big for a double (Json.NET keeps it as a BigInteger) throws on the way out
                    // rather than coming back as an infinity: that is still a number this cannot use.
                    try { return WidgetAccess.Finite(t.Value<double>()) ? null : (path ?? "value"); }
                    catch { return path ?? "value"; }
                case JTokenType.String:
                {
                    string s = (string)t;
                    return s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                           && !WidgetAccess.Finite(d) ? (path ?? "value") : null;
                }
                case JTokenType.Object:
                    foreach (var p in ((JObject)t).Properties())
                    {
                        // "token" is never read as a number and must not be judged as one. A token is 32 hex
                        // digits, and about one in 40 million of them — a digit, then 'e', then 30 digits — parses
                        // as an overflowing exponent and would be refused as "not a finite number". That is rare
                        // enough never to be found in testing and specific enough to be unfixable in the field.
                        if (p.Name == "token") continue;
                        string r = NonFinite(p.Value, path == null ? p.Name : path + "." + p.Name); if (r != null) return r;
                    }
                    return null;
                case JTokenType.Array:
                {
                    var a = (JArray)t;
                    for (int i = 0; i < a.Count; i++) { string r = NonFinite(a[i], (path ?? "") + "[" + i + "]"); if (r != null) return r; }
                    return null;
                }
                default: return null;
            }
        }

        private string Handle(JObject j, string src, Auth auth)
        {
            string cmd = (string)j["cmd"] ?? "";
            var w = Widget; var ok = new JObject { ["ok"] = true, ["cmd"] = cmd };
            // THE GATE (Ty, 2026-11-11). Loopback passes untouched; a connection from the network must have said
            // hello with the token first. "hello" itself is always let through, because it is both how a client
            // authenticates and how a refused one finds out it was refused — a silent drop would be invisible to
            // Glass Overlay, which connects over the LAN, and an invisible break is the expensive kind.
            {
                string gate = WidgetAccess.CommandRefusal(auth.Loopback, auth.Authed, cmd);
                if (gate != null) return Refused(cmd, gate);
            }
            // NOT-A-NUMBER IS NOT A COMMAND (pre-ship audit, 2026-11-11). Every numeric field below is clamped on
            // the way in and NOT ONE of those clamps stops NaN: Mathf.Clamp and Math.Clamp are both
            // "if (v < min) … else if (v > max) … else return v", and NaN compares false against both. One NaN
            // reaching a control deflection, a mass, an autopilot target or a camera angle is a NaN moment, a NaN
            // quaternion, and an aeroplane that is gone for the rest of the session — from one JSON field.
            // Guarding every read site would be ~60 edits and one of them would be missed this year or next, so the
            // check is HERE, at the one door every command comes through, and it covers commands not yet written.
            // The three sites that feed the physics most directly (WidgetControls.FromNetwork, WidgetLoading.Set,
            // JoyLink's raw axes) keep their own guards: they have callers that never pass through here.
            {
                string bad = NonFinite(j, null);
                if (bad != null)
                    return new JObject { ["ok"] = false, ["cmd"] = cmd,
                                         ["error"] = $"\"{bad}\" is not a finite number (NaN or an infinity). Every number in a command must be one." }
                           .ToString(Newtonsoft.Json.Formatting.None);
            }
            // A web remote names itself on every request ("from":"web-…").
            string from = (string)j["from"];
            if (from != null && from.StartsWith("web-")) { SeeRemote(from, (string)j["name"]); src = from; }
            src ??= "http";
            switch (cmd)
            {
                case "scenario":
                {
                    // THE AIRCRAFT ID IS CHECKED AGAINST THE FLEET, the way "start" has always checked it (see the
                    // start case below). This arm did not, and the id goes straight into
                    // UnityAircraftConfigLoader.LoadFromStreamingAssets, which builds "aircraft/" + id + ".json"
                    // and reads it — so "../../../../something" was a read of any .json file the widget process
                    // could reach, from any origin (pre-ship audit, 2026-11-11). It is also the key of the static
                    // _fits / _ldFits caches, so an endless supply of invented ids was an endless supply of
                    // dictionary entries. Two commands that load an aeroplane must not disagree about what an
                    // aeroplane is.
                    string acId = (string)j["aircraft"];
                    if (acId != null && System.Array.FindIndex(SessionSettings.Fleet, f => f.id == acId) < 0)
                    { ok["ok"] = false; ok["error"] = "unknown aircraft"; break; }
                    w.Lessons.Stop();
                    string n = ((string)j["name"] ?? w.Current.ToString()).ToLowerInvariant();
                    var sc = n == "spin" ? AeroWidget.Scenario.Spin : n == "cruise" ? AeroWidget.Scenario.Cruise : AeroWidget.Scenario.Flare;
                    w.Load(sc, acId ?? w.AircraftId, j["flaps"]?.Value<double>() ?? w.Flaps);
                    break;
                }
                case "controls":
                {
                    var n = new WidgetControls.JInputs
                    {
                        Aileron = j["aileron"]?.Value<double>(), Elevator = j["elevator"]?.Value<double>(), Rudder = j["rudder"]?.Value<double>(),
                        Throttle = j["throttle"]?.Value<double>(), Brake = j["brake"]?.Value<double>(), BrakeL = j["brakeL"]?.Value<double>(), BrakeR = j["brakeR"]?.Value<double>(),
                        HandsOff = j["handsOff"]?.Value<bool>(), Flaps = j["flaps"]?.Value<double>(), Spoilers = j["spoilers"]?.Value<double>(),
                        SpoilersArmed = j["spoilersArmed"]?.Value<bool>(), Gear = ((string)j["gear"])?.ToLowerInvariant(),
                    };
                    bool took = w.Controls.FromNetwork(n, (string)j["source"] ?? (src.StartsWith("web-") ? "remote" : "network"), src);
                    ok["accepted"] = took;
                    if (!took) ok["reason"] = w.Paused ? "review (paused): read-only until resume" : $"{w.Controls.PilotName} is flying";
                    break;
                }
                case "pilot":
                {
                    string id = j["id"]?.Type == JTokenType.Null ? null : (string)j["id"];
                    w.Controls.SetPilot(id, (string)j["name"], (string)j["color"], j["managed"]?.Value<bool>() ?? true, src);
                    ok["pilot"] = PilotJson(); break;
                }
                case "requestControls":
                    // The web remote's "Request controls": tell Glass Overlay (it holds the arbitration).
                    PushEvent(new JObject { ["event"] = "controlRequest", ["from"] = from ?? src, ["name"] = (string)j["name"] ?? "" });
                    break;
                case "loading":
                {
                    // Protocol 8: live weight & balance (any time, any condition — the spin included). Out of limits allowed.
                    double? lb = j["grossWeightLb"]?.Value<double>(); double? cg = j["cgPercentMac"]?.Value<double>();
                    w.Loading.Set(lb.HasValue ? lb.Value / 2.20462 : null, cg, j["reset"]?.Value<bool>() ?? false);
                    ok["loading"] = LoadingJson(); break;
                }
                case "autopilot":
                {
                    var ap = w.Autopilot;
                    string mode = ((string)j["mode"])?.ToLowerInvariant();
                    if (mode != null && System.Array.IndexOf(WidgetAutopilot.FutureModes, mode) >= 0) { ok["ok"] = false; ok["error"] = $"{mode.ToUpperInvariant()} is not available yet (a later build)"; ok["autopilot"] = ApJson(); break; }
                    if (j["heading"] != null || j["headingBump"] != null || (j["headingSync"]?.Value<bool>() ?? false))
                        ap.Heading(j["heading"]?.Value<double>(), j["headingBump"]?.Value<double>(), j["headingSync"]?.Value<bool>() ?? false);
                    if (j["yawDamper"] != null) ap.YawDamper = j["yawDamper"].Value<bool>();
                    // A/T (2026-10-11): engages on its own (AP on or off), on any type with an engine; "kias" sets its speed.
                    if (j["autothrottle"] != null) ap.SetAutoThrottle(j["autothrottle"].Value<bool>(), j["kias"]?.Value<double>());
                    else if (j["kias"] != null && !ap.On && ap.AutoThrottle) ap.SetAutoThrottle(true, j["kias"].Value<double>());
                    if (j["autothrottle"] != null && j["on"] == null && j["mode"] == null && j["altitudeFt"] == null && j["vsFpm"] == null) { ok["autopilot"] = ApJson(); break; }
                    bool? on = j["on"]?.Value<bool>();
                    // CWS (2026-10-11): a pitch attitude and/or a bank angle to hold ("pitchDeg" −30…30, "bankDeg" −45…45, + = right).
                    if (on != false && (j["pitchDeg"] != null || j["bankDeg"] != null))
                    { ap.Attitude(j["pitchDeg"]?.Value<double>(), j["bankDeg"]?.Value<double>()); ok["autopilot"] = ApJson(); break; }
                    if (on != false && (mode == "pit" || mode == "bank"))
                    { if (mode == "pit") ap.Attitude(ap.On && ap.PitchMode == "pit" ? ap.PitchDeg : w.Read().PitchDeg, null); else ap.Attitude(null, ap.On && ap.RollMode == "bank" ? ap.BankDeg : w.Read().RollDeg); ok["autopilot"] = ApJson(); break; }
                    if (on == false) ap.Off();
                    else if (on == true || mode != null || j["altitudeFt"] != null || j["vsFpm"] != null || j["kias"] != null)
                    {
                        if (on == true || ap.On)
                        {
                            string pm = mode == "alt" || mode == "vs" || mode == "flc" ? mode : null, rm = mode == "hdg" || mode == "rol" ? mode : null;
                            ap.Engage(j["altitudeFt"]?.Value<double>(), j["kias"]?.Value<double>(), pm, j["vsFpm"]?.Value<double>(), rm, null, null, null);
                        }
                        else
                        {
                            if (j["altitudeFt"] != null) ap.AltFt = j["altitudeFt"].Value<double>();
                            if (j["kias"] != null) ap.Kias = j["kias"].Value<double>();
                            if (j["vsFpm"] != null) ap.VsFpm = j["vsFpm"].Value<double>();
                        }
                    }
                    ok["autopilot"] = ApJson(); break;
                }
                case "altitudeLock":
                {
                    // 2026-10-11: the air and the ground's distance frozen at aglFt (not an autopilot); not in final.
                    if (j["aglFt"] != null) w.AltLockFt = System.Math.Clamp(j["aglFt"].Value<double>(), 200, 40000);
                    if (j["on"] != null) { w.AltLock = j["on"].Value<bool>(); w.ApplyAltitudeLock(); }
                    ok["altitudeLock"] = AltLockJson(); break;
                }
                case "fourForces":
                    // 2026-10-11: only thrust, lift, weight, drag (+ the balance and trend insets); off restores the previous picture.
                    w.SetFourForces(j["on"]?.Value<bool>() ?? !w.Show["fourForces"]);
                    ok["fourForces"] = w.Show["fourForces"]; ok["insets"] = new JArray(w.Insets); break;
                case "wind":
                    w.SetWind(j["fromDeg"]?.Value<double>() ?? w.WindFromDeg, j["kt"]?.Value<double>() ?? w.WindKt);
                    ok["fromDeg"] = w.WindFromDeg; ok["kt"] = w.WindKt; break;
                case "tow":
                    // The aerotow (2026-10-11): {"cmd":"tow","on":true} hooks the tug up ahead; "release":true pulls the knob.
                    if (j["release"]?.Value<bool>() ?? false) w.Tow.Release();
                    else if (j["on"]?.Value<bool>() ?? true) w.Tow.Start(); else w.Tow.Stop();
                    ok["tow"] = w.Tow.Active; ok["rope"] = w.Tow.Connected; break;
                case "smoke":
                    w.Show["smoke"] = j["on"]?.Value<bool>() ?? true; ok["smoke"] = w.Show["smoke"]; break;
                case "thermal":
                    w.SetThermal(j["on"]?.Value<bool>() ?? true, (j["climbKt"]?.Value<double>() ?? 6) / 1.943844, j["radiusM"]?.Value<double>() ?? 150);
                    ok["thermal"] = w.ThermalOn; break;
                case "inset":
                {
                    string n = (string)j["name"];
                    if (System.Array.IndexOf(WidgetControlsDisplay.InsetNames, n) < 0) { ok["ok"] = false; ok["error"] = "inset: " + string.Join(", ", WidgetControlsDisplay.InsetNames); break; }
                    if (j["show"]?.Value<bool>() ?? true) w.Insets.Add(n); else w.Insets.Remove(n);
                    ok["insets"] = new JArray(w.Insets); break;
                }
                case "lesson":
                {
                    string id = (string)j["id"];
                    if (id == null || id == "stop") { w.Lessons.Stop(); ok["lesson"] = null; break; }
                    if (!w.Lessons.Start(id)) { ok["ok"] = false; ok["error"] = "unknown lesson"; break; }
                    ok["lesson"] = id; ok["name"] = w.Lessons.Current.Name; break;
                }
                case "debugLabels":
                {
                    var o = new JObject();
                    foreach (var kv in w.Vectors.LabelDebug) o[kv.Key] = new JArray(kv.Value.pos.x, kv.Value.pos.y, kv.Value.slot, System.Math.Round(kv.Value.alpha, 2));
                    ok["labels"] = o; ok["t"] = Time.unscaledTime; break;
                }
                case "caption": w.Caption = (string)j["text"]; ok["caption"] = w.Caption; break;
                case "tag":
                    w.Tag = (string)j["text"];
                    if (j["color"] != null && ColorUtility.TryParseHtmlString((string)j["color"], out var tc)) w.TagColor = tc;
                    ok["tag"] = w.Tag; break;
                case "vectorStyle":
                    // Protocol 7: line width (×) and the drawing filter's time constant.
                    if (j["width"] != null) w.Vectors.WidthScale = Mathf.Clamp((float)j["width"].Value<double>(), 1f, 3f);
                    if (j["smoothingMs"] != null) w.Vectors.SmoothingMs = Mathf.Clamp((float)j["smoothingMs"].Value<double>(), 0f, 400f);
                    // labelScale (2026-10-11, the Talk Flying renders): the vector labels' text size (×, 1…3) so they read keyed over a show.
                    if (j["labelScale"] != null && w.Display != null) w.Display.TextScale = Mathf.Clamp((float)j["labelScale"].Value<double>(), 1f, 3f);
                    if (j["dragScale"] != null) WidgetVectors.DragScale = System.Math.Clamp(j["dragScale"].Value<double>(), 1.0, 3.0);
                    ok["dragScale"] = WidgetVectors.DragScale;
                    if (j["forceValues"] != null) WidgetVectors.ForceValues = j["forceValues"].Value<bool>();
                    if (j["forceEnds"] != null) WidgetVectors.ForceEnds = j["forceEnds"].Value<bool>();
                    if (j["forceScale"] != null) WidgetVectors.ForceScale = Mathf.Clamp((float)j["forceScale"].Value<double>(), 0.5f, 3f);
                    if (j["smokeDebug"] != null) WidgetVectors.SmokeDebug = j["smokeDebug"].Value<bool>();
                    if (j["wingWindCyan"] != null) WidgetVectors.WingWindCyan = j["wingWindCyan"].Value<bool>();
                    if (j["solid3D"] != null) WidgetVectors.Solid3D = j["solid3D"].Value<bool>();
                    if (j["momentArcSweep"] != null) WidgetVectors.MomentArcSweepDeg = Mathf.Clamp((float)j["momentArcSweep"].Value<double>(), 20f, 160f);
                    if (j["momentArcRadius"] != null) WidgetVectors.MomentArcRadius = Mathf.Clamp((float)j["momentArcRadius"].Value<double>(), 0.6f, 2f);
                    if (j["wingWindSpan"] != null) WidgetVectors.WingWindSpan = System.Math.Clamp(j["wingWindSpan"].Value<double>(), 0.2, 0.95);
                    if (j["wingWindScale"] != null) WidgetVectors.WingWindScale = Mathf.Clamp((float)j["wingWindScale"].Value<double>(), 0.5f, 3f);
                    if (j["momentArcNoseSense"] != null) WidgetVectors.MomentArcNoseSense = j["momentArcNoseSense"].Value<bool>();
                    if (j["momentArcValues"] != null) WidgetVectors.MomentArcValues = j["momentArcValues"].Value<bool>();
                    if (j["inertiaSplit"] != null) WidgetVectors.InertiaSplit = j["inertiaSplit"].Value<bool>();
                    if (j["legend"] != null) WidgetVectors.Legend = j["legend"].Value<bool>();
                    if (j["wingForces"] != null) WidgetVectors.WingForces = j["wingForces"].Value<bool>();
                    if (j["lightFill"] != null && w is AeroWidget lw2) lw2.SetLightFill((float)j["lightFill"].Value<double>());
                    if (j["legendAnchor"] != null) WidgetVectors.LegendAnchor = (string)j["legendAnchor"] ?? "bottom";
                    if (j["legendScale"] != null) WidgetVectors.LegendScale = Mathf.Clamp((float)j["legendScale"].Value<double>(), 0.4f, 2f);
                    if (j["legendTitle"] != null) WidgetVectors.LegendTitle = (string)j["legendTitle"] ?? "";
                    if (j["momentArcsEqual"] != null) WidgetVectors.MomentArcsEqual = j["momentArcsEqual"].Value<bool>();
                    if (j["momentArcsSwap"] != null) WidgetVectors.MomentArcsSwap = j["momentArcsSwap"].Value<bool>();
                    if (j["momentArcHead"] != null) WidgetVectors.MomentArcHead = Mathf.Clamp((float)j["momentArcHead"].Value<double>(), 1f, 4f);
                    if (j["aeroMomentLabel"] != null) WidgetVectors.AeroMomentLabel = (string)j["aeroMomentLabel"] is { Length: > 0 } al ? al : null;
                    if (j["inertiaMomentLabel"] != null) WidgetVectors.InertiaMomentLabel = (string)j["inertiaMomentLabel"] is { Length: > 0 } il ? il : null;
                    ok["width"] = w.Vectors.WidthScale; ok["smoothingMs"] = w.Vectors.SmoothingMs; ok["labelScale"] = w.Display != null ? w.Display.TextScale : 1f; break;
                case "display":
                {
                    // Protocol 6: "ntsb" (instrument plates below the airplane, all on transparency) or "classic".
                    string mode = ((string)j["mode"])?.ToLowerInvariant();
                    if (mode != null && mode != "ntsb" && mode != "classic") { ok["ok"] = false; ok["error"] = "mode: ntsb or classic"; break; }
                    if (mode != null) w.DisplayMode = mode;
                    if (j["split"] != null) { w.Split = Mathf.Clamp((float)j["split"].Value<double>(), 0.35f, 0.6f); w.PanelW = -1f; w.SavePanel(); }   // a split puts the panel back at the bottom
                    ok["mode"] = w.DisplayMode; ok["split"] = w.Split; break;
                }
                case "joystick":
                {
                    // JOYSTICK SETUP (2026-10-11): {"setup":"start"|"skip"|"back"|"cancel"|"finish"} · {"forget":true}.
                    var jw = w.JoyWizard;
                    switch (((string)j["setup"])?.ToLowerInvariant())
                    {
                        case "start": if (!jw.Start()) { ok["ok"] = false; ok["error"] = "no joystick connected"; } break;
                        case "skip": jw.Skip(); break;
                        case "back": jw.Back(); break;
                        case "cancel": jw.Cancel(); break;
                        case "finish": jw.Finish(); break;
                    }
                    if (j["forget"]?.Value<bool>() ?? false) { string n = FlyingGame.Bridge.JoystickInput.ConnectedName; if (n != null) FlyingGame.Bridge.JoystickInput.Forget(n); }
                    ok["joystick"] = JoystickJson(w); break;
                }
                case "panel":
                {
                    // The NTSB panel box (owner 2026-10-11: draggable + resizable). Absolute {x, y, w} (0…1 of the frame, top-left,
                    // y down; the height follows the aspect), or relative {dx, dy} (move) / {dw} (resize about the top-left),
                    // or {"reset": true} (the default: bottom, full width).
                    float aspect = WidgetControlsDisplay.PanelAspectN;
                    if (j["reset"]?.Value<bool>() ?? false) w.PanelW = -1f;
                    else
                    {
                        float cw = w.PanelW > 0 ? w.PanelW : Mathf.Min(1f, AeroHost.UiBand / aspect);
                        float cx = w.PanelW > 0 ? w.PanelX : (1f - cw) * 0.5f, cy = w.PanelW > 0 ? w.PanelY : 1f - cw * aspect;
                        float nx = j["x"] != null ? (float)j["x"].Value<double>() : cx + (float)(j["dx"]?.Value<double>() ?? 0);
                        float ny = j["y"] != null ? (float)j["y"].Value<double>() : cy + (float)(j["dy"]?.Value<double>() ?? 0);
                        float nw = j["w"] != null ? (float)j["w"].Value<double>() : cw + (float)(j["dw"]?.Value<double>() ?? 0);
                        w.PlacePanel(nx, ny, nw, aspect);
                    }
                    w.SavePanel();
                    ok["panel"] = PanelJson(); break;
                }
                case "controlsDisplay":
                {
                    string place = ((string)j["place"])?.ToLowerInvariant();
                    if (place == "bottom" || place == "left" || place == "right") w.ControlsPlace = place;
                    if (j["size"] != null) w.ControlsSize = Mathf.Clamp((float)j["size"].Value<double>(), 0.2f, 0.5f);
                    if (j["show"] != null) w.Show["controlsDisplay"] = j["show"].Value<bool>();
                    ok["place"] = w.ControlsPlace; ok["size"] = w.ControlsSize; ok["shown"] = w.Show["controlsDisplay"]; break;
                }
                case "preset":
                    if (j["towHeightGain"] != null) w.Presets.TowHeightGain = Mathf.Clamp(j["towHeightGain"].Value<float>(), 0f, 2f);
                    if (j["towPathGain"] != null) w.Presets.TowPathGain = Mathf.Clamp(j["towPathGain"].Value<float>(), 0.05f, 4f);
                    if (j["releaseAfterS"] != null) w.Presets.ReleaseAfterS = Mathf.Clamp(j["releaseAfterS"].Value<float>(), 2f, 120f);
                    if (j["pivotKias"] != null) w.Presets.PivotKias = Mathf.Clamp(j["pivotKias"].Value<float>(), 30f, 120f);   // (hammerhead)
                    if (!w.Presets.Run((string)j["name"] ?? "")) { ok["ok"] = false; ok["error"] = "unknown preset"; }
                    break;
                case "pause": w.Review.Pause(); return Reviewed(ok);
                case "resume":
                    w.Review.Resume(((string)j["from"])?.ToLowerInvariant() == "live");
                    ok["offsetMs"] = 0; ok["frame"] = w.Review.FrameJson(); break;
                case "recordRate": w.SetRecordRate(Mathf.Clamp(j["rate"]?.Value<int>() ?? 1, 1, 20)); ok["rate"] = w.Review.RecordRate; break;
                case "step": w.Review.Step(j["frames"]?.Value<int>() ?? -1); return Reviewed(ok);
                case "rewind": w.Review.Rewind(j["seconds"]?.Value<double>() ?? 1); return Reviewed(ok);
                case "seek": w.Review.Seek(j["offsetMs"]?.Value<double>() ?? 0); return Reviewed(ok);
                case "play": w.Review.Play((float)(j["rate"]?.Value<double>() ?? 0.25), (string)j["direction"] ?? "reverse"); return Reviewed(ok);
                case "reset": w.Load(w.Current, w.AircraftId, w.Flaps); break;
                // Slow motion (2026-10-11: 1, 0.5, 0.25, 0.1 — any 0.05…1): the sim slows, the controls stay real time.
                case "timescale": w.TimeScale = Mathf.Clamp((float)(j["value"]?.Value<double>() ?? 1), 0.05f, 1f); ok["timescale"] = w.TimeScale; ok["steps"] = new JArray(1, 0.5, 0.25, 0.1); break;
                case "start":
                {
                    // Protocol 5: one of three known states with one tap (cruise | final | spin).
                    string cond = ((string)j["condition"] ?? "").ToLowerInvariant();
                    if (System.Array.IndexOf(AeroWidget.Conditions, cond) < 0) { ok["ok"] = false; ok["error"] = "condition: cruise, final or spin"; break; }
                    string acId = (string)j["aircraft"];
                    if (acId != null && System.Array.FindIndex(SessionSettings.Fleet, f => f.id == acId) < 0) { ok["ok"] = false; ok["error"] = "unknown aircraft"; break; }
                    w.Lessons.Stop();
                    if (j["loading"] is JObject ld) w.Loading.Set(ld["grossWeightLb"]?.Value<double>() / 2.20462, ld["cgPercentMac"]?.Value<double>(), ld["reset"]?.Value<bool>() ?? false);
                    string err = w.StartCondition(cond, acId, ((string)j["view"])?.ToLowerInvariant(), ((string)j["from"])?.ToLowerInvariant());
                    if (err != null) { ok["ok"] = false; ok["error"] = err; }   // started anyway, in the condition's default view
                    ok["condition"] = w.Condition; ok["aircraft"] = w.AircraftId; ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; ok["viewFixed"] = w.ViewFixed; ok["hold"] = w.Controls.Hold;
                    break;
                }
                case "view":
                {
                    // Frame (2026-10-11, Glass): {"cmd":"view","frame":"16:9"} → 1920 × 1080, "square" → 1080 × 1080 (persisted).
                    if (j["frame"] != null)
                    {
                        string fm = (string)j["frame"];
                        if (fm != "16:9" && fm != "square") { ok["ok"] = false; ok["error"] = "frame: square or 16:9"; break; }
                        w.SetFrame(fm);
                        if (j["name"] == null && j["zoom"] == null && j["zoomBy"] == null) { ok["frame"] = w.FrameMode; ok["frameW"] = AeroWidget.FrameW; ok["frameH"] = AeroWidget.FrameH; break; }
                    }
                    // Orbit bearing (2026-10-12): {"cmd":"view","orbitDeg":270,"orbitElev":12} — alone it just turns the orbit camera.
                    if (j["orbitWorld"] != null) w.OrbitWorld = j["orbitWorld"].Value<bool>();
                    if (j["orbitSpinAxis"] != null) w.OrbitSpinAxis = j["orbitSpinAxis"].Value<bool>();
                    // A spectator fixed at a world point (north, east, down, m — the TRUE path's frame): the fly-by.
                    if (j["crowdNed"] is JArray cn && cn.Count == 3) w.SetCrowd(new FlyingGame.Core.MathTypes.Vec3(cn[0].Value<double>(), cn[1].Value<double>(), cn[2].Value<double>()));
                    if (j["orbitDeg"] != null) w.OrbitAzDeg = (float)j["orbitDeg"].Value<double>();
                    if (j["orbitElev"] != null) w.OrbitElevDeg = Mathf.Clamp((float)j["orbitElev"].Value<double>(), -60f, 80f);
                    if ((j["orbitDeg"] != null || j["orbitElev"] != null || j["orbitWorld"] != null || j["orbitSpinAxis"] != null || j["crowdNed"] != null) && j["name"] == null && j["zoom"] == null) { ok["orbitDeg"] = w.OrbitAzDeg; ok["orbitElev"] = w.OrbitElevDeg; break; }
                    // Zoom alone (2026-10-11): {"cmd":"view","zoom":1.6} (0.6…3; 1 = the old framing) or "zoomBy": 1.2.
                    if (j["zoom"] != null || j["zoomBy"] != null)
                    {
                        w.SetZoom(j["zoom"] != null ? (float)j["zoom"].Value<double>() : w.Zoom * (float)j["zoomBy"].Value<double>());
                        if (j["name"] == null) { ok["zoom"] = w.Zoom; ok["view"] = w.View; break; }
                    }
                    string vn = ((string)j["name"] ?? "side").ToLowerInvariant();
                    if (System.Array.IndexOf(AeroWidget.Views, vn) < 0) { ok["ok"] = false; ok["error"] = "unknown view"; break; }
                    string verr = w.CheckView(vn, ((string)j["from"])?.ToLowerInvariant());
                    if (verr != null) { ok["ok"] = false; ok["error"] = verr; ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break; }
                    if (w.Condition == "spin" && vn == "locked") { w.SetView("locked", "side"); ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break; }
                    w.SetView(vn, ((string)j["from"])?.ToLowerInvariant());   // the flare is always side-on: accepted, no change there
                    ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break;
                }
                case "hello":
                {
                    // THE TOKEN CHECK (Ty, 2026-11-11, rules 3 and 5). A protocol 8 client that sends no token
                    // still works on loopback — the field is simply ignored there — and on the network gets a
                    // refusal that NAMES the field, the command and where to read the value, plus "needsToken"
                    // and the protocol number so a client can still tell what it reached. Glass Overlay connects
                    // over the LAN: it must be told, not dropped.
                    string why = WidgetAccess.HelloRefusal(auth.Loopback, (string)j["token"], Token);
                    if (why != null) return Refused(cmd, why);
                    auth.Authed = true;   // for the rest of this connection
                    ok["app"] = "Aero Widget"; ok["version"] = Application.version; ok["protocol"] = Protocol;
                    ok["access"] = new JObject { ["lan"] = Lan, ["loopback"] = auth.Loopback, ["tokenRequired"] = !auth.Loopback };
                    ok["network"] = new JObject { ["tcpDualStack"] = TcpDualStack, ["httpIPv6"] = HttpIPv6, ["bonjour"] = BonjourOk ? new JArray("_aerowidget._tcp", "_http._tcp") : new JArray() };
                    ok["features"] = new JArray("review", "controlsDisplay", "controlTraces", "pilot", "remotes", "conditions", "ntsbDisplay", "controlsLayout", "inertialForces", "moments", "smoothVectors", "lessons", "insets", "loading", "wind", "autopilot", "fourForces", "cws", "earthGrid", "altitudeLock", "panelBox", "fma", "mcp", "zoom", "frame16x9", "tools", "joystickSetup");
                    { var la = new JArray(); foreach (var l in WidgetLessons.All) la.Add(new JObject { ["id"] = l.Id, ["name"] = l.Name, ["concept"] = l.Concept }); ok["lessons"] = la; }
                    ok["insets"] = new JArray(WidgetControlsDisplay.InsetNames);
                    ok["autopilotModes"] = new JObject { ["pitch"] = new JArray("alt", "vs", "flc", "pit"), ["roll"] = new JArray("rol", "hdg", "bank"),
                        ["cws"] = new JObject { ["pitchDeg"] = new JArray(-WidgetAutopilot.PitchLimit, WidgetAutopilot.PitchLimit), ["bankDeg"] = new JArray(-WidgetAutopilot.BankLimit, WidgetAutopilot.BankLimit), ["note"] = "attitude hold; stick input doesn't disconnect — the AP holds the attitude it is left at" }, ["other"] = new JArray("yawDamper", "autothrottle", "headingSelector"),
                        ["future"] = new JArray("lnav", "loc", "app"), ["futureNote"] = "buttons present, not active yet" };
                    if (LayoutJson() is JObject lay) ok["controlsLayout"] = lay;
                    ok["display"] = new JObject { ["modes"] = new JArray("classic", "ntsb"), ["default"] = "ntsb", ["split"] = new JArray(0.35, 0.6) };
                    ok["conditions"] = JArray.Parse(@"[{""id"":""cruise"",""name"":""Cruise"",""views"":""any"",""default"":""chase""},
                        {""id"":""final"",""name"":""On final"",""views"":[""side"",""chase"",""behind"",""front"",""top"",""crowd""],""default"":""side""},
                        {""id"":""spin"",""name"":""Developed spin"",""views"":[{""name"":""locked"",""from"":""side""},{""name"":""body"",""from"":""left""}],""default"":""locked"",""hold"":true}]");
                    ok["bodyFrom"] = new JArray("left");
                    ok["commands"] = new JArray("hello", "start", "display", "caption", "fourForces", "altitudeLock", "vectorStyle", "lesson", "loading", "autopilot", "wind", "thermal", "smoke", "tow", "inset", "scenario", "controls", "preset", "pause", "resume", "step", "rewind", "seek", "play", "pilot", "controlsDisplay", "panel", "joystick", "requestControls", "reset", "timescale", "view", "show", "fleet", "state", "subscribe", "snapshot", "gamepad");
                    ok["scenarios"] = new JArray("flare", "spin", "cruise");
                    ok["views"] = new JArray(AeroWidget.Views); ok["viewFrom"] = new JArray(AeroWidget.LockFrom);
                    ok["presets"] = new JArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Concat(WidgetPresets.SpinPresets, WidgetPresets.FlarePresets), WidgetPresets.AeroPresets));
                    ok["show"] = new JArray(w.Show.Keys);
                    ok["frame"] = AeroWidget.FrameH; ok["frameW"] = AeroWidget.FrameW; ok["frameH"] = AeroWidget.FrameH; ok["frameModes"] = new JArray("square", "16:9"); ok["syphon"] = AeroWidget.StreamName; ok["ndi"] = AeroWidget.StreamName;
                    ok["tcpPort"] = TcpPort; ok["httpPort"] = HttpPort;
                    ok["controlsDisplay"] = new JObject { ["places"] = new JArray("bottom", "left", "right"), ["size"] = new JArray(0.2, 0.5), ["default"] = new JObject { ["shown"] = false, ["place"] = "bottom", ["size"] = 0.28 } };
                    ok["controlInputs"] = new JArray("aileron", "elevator", "rudder", "throttle", "brake", "brakeL", "brakeR", "handsOff", "flaps", "spoilers", "spoilersArmed", "gear");
                    { var fl = new JArray(); foreach (var fa in SessionSettings.Fleet) fl.Add(new JObject { ["id"] = fa.id, ["name"] = fa.name, ["controls"] = ControlsFit(fa.id), ["loading"] = LoadingFit(fa.id) }); ok["fleet"] = fl; }
                    ok["review"] = new JObject { ["historySeconds"] = WidgetHistory.Seconds, ["fps"] = WidgetHistory.Hz, ["commands"] = new JArray("step", "rewind", "seek", "play") };
                    break;
                }
                case "show":
                    foreach (var p in j.Properties()) if (p.Name != "cmd" && w.Show.ContainsKey(p.Name)) w.Show[p.Name] = p.Value.Value<bool>();
                    // "horizon" is the earth grid's old name (an alias for one protocol version): either sets both.
                    if (j["earthGrid"] != null) w.SetEarthGrid(j["earthGrid"].Value<bool>()); else if (j["horizon"] != null) w.SetEarthGrid(j["horizon"].Value<bool>());
                    // {"clear": true}: every vector off (a clean picture), the four-forces mode too.
                    if (j["clear"]?.Value<bool>() ?? false) { if (w.Show["fourForces"]) w.SetFourForces(false); foreach (var vk in AeroHost.VectorKeys) if (w.Show.ContainsKey(vk)) w.Show[vk] = false; w.Show["vectors"] = false; }
                    // {"toggle": "lift"} (or an array): flip those keys — what the TOOLS buttons send (2026-10-11).
                    if (j["toggle"] != null)
                        foreach (var t in j["toggle"] is JArray ta ? ta.Values<string>() : new[] { (string)j["toggle"] })
                        {
                            if (t == "earthGrid" || t == "horizon") w.SetEarthGrid(!w.Show["earthGrid"]);
                            else if (t != null && w.Show.ContainsKey(t))
                            {
                                w.Show[t] = !w.Show[t];
                                if (w.Show[t] && System.Array.IndexOf(AeroHost.VectorKeys, t) >= 0) w.Show["vectors"] = true;   // (as the game: a force on = vectors on)
                            }
                        }
                    ok["show"] = JObject.FromObject(w.Show);
                    break;
                case "gamepad":
                    var c = w.Controls;
                    c.AxAileron = j["aileron"]?.Value<int>() ?? c.AxAileron; c.AxElevator = j["elevator"]?.Value<int>() ?? c.AxElevator;
                    c.AxRudder = j["rudder"]?.Value<int>() ?? c.AxRudder; c.AxThrottle = j["throttle"]?.Value<int>() ?? c.AxThrottle;
                    c.InvertElevator = j["invertElevator"]?.Value<bool>() ?? c.InvertElevator; c.ThrottleFromAxis = j["throttleFromAxis"]?.Value<bool>() ?? c.ThrottleFromAxis;
                    break;
                case "fleet":
                {
                    var arr = new JArray();
                    foreach (var f in SessionSettings.Fleet) arr.Add(new JObject { ["id"] = f.id, ["name"] = f.name, ["controls"] = ControlsFit(f.id), ["loading"] = LoadingFit(f.id) });
                    ok["fleet"] = arr; break;
                }
                case "snapshot":
                {
                    // The current frame (with alpha) to a PNG — for checking the output, or a still for the show.
                    //
                    // CONFINED TO ONE DIRECTORY (pre-ship audit, 2026-11-11). This used to be
                    //   string path = (string)j["path"] ?? …;  File.WriteAllBytes(path, png);
                    // which is an arbitrary file write to anywhere the widget process can reach, asked for by anyone
                    // who can open the port — and before this lane, that was anyone on the network AND any web page
                    // the presenter had open, because the server also sent wildcard CORS. A PNG is not an innocent
                    // payload when you choose the path: ~/Library/LaunchAgents, a shell profile, a file the build
                    // reads. Nothing in the widget ever needed to choose a directory, so the caller's "path" is now
                    // read as a NAME ONLY (WidgetAccess.SnapshotPath: every directory component, "..", root and
                    // drive letter discarded, extension forced to .png) and the write lands in SnapshotDir.
                    // The reply still carries the full path, so a local caller knows where the file went.
                    string path = WidgetAccess.SnapshotPath(SnapshotDir, (string)j["path"]);
                    try { System.IO.Directory.CreateDirectory(SnapshotDir); } catch { }
                    var was = RenderTexture.active; RenderTexture.active = w.Frame;
                    var tex = new Texture2D(w.Frame.width, w.Frame.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, w.Frame.width, w.Frame.height), 0, 0); tex.Apply();
                    RenderTexture.active = was;
                    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
                    ok["path"] = path; ok["dir"] = SnapshotDir; break;
                }
                case "state": return StateJson().ToString(Newtonsoft.Json.Formatting.None);
                case "subscribe":
                {
                    // Registered HERE, on the main thread, rather than on the reader thread as it used to be: a
                    // client that sends hello and subscribe back to back would otherwise have its subscription
                    // judged before the hello had been processed, and an unauthenticated connection from the
                    // network would have been streamed the whole state at up to 60 Hz regardless.
                    if (auth.Conn == null) { ok["ok"] = false; ok["error"] = $"subscribe is a TCP stream: open a connection on port {TcpPort}"; break; }
                    float hz = (float)(j["hz"]?.Value<double>() ?? 10);
                    if (!WidgetAccess.Finite(hz)) hz = 10f;
                    hz = Mathf.Clamp(hz, 1f, 60f);
                    lock (_subLock) { _subs.RemoveAll(s => s.c == auth.Conn); _subs.Add((auth.Conn, hz, 0f)); }
                    ok["hz"] = hz; break;
                }
                case "quit":   // (the capture instance only: a render script ends with it, 2026-10-13)
                    if (!AeroWidget.Capture) { ok["ok"] = false; ok["error"] = "capture only"; break; }
                    _quitAt = Time.unscaledTime + 0.5f; ok["footprintMb"] = System.Math.Round(FootprintMb()); break;
                default: ok["ok"] = false; ok["error"] = "unknown cmd"; break;
            }
            return ok.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>A refusal that says why, on the connection that asked, with enough for a client to act on:
        /// the reason in words, "needsToken" so it can ask the user for one, and the protocol number so it still
        /// knows what it reached. Never a silent drop and never a closed socket (Ty, 2026-11-11, rule 5).</summary>
        private static string Refused(string cmd, string why)
            => new JObject { ["ok"] = false, ["cmd"] = cmd, ["error"] = why, ["needsToken"] = true, ["protocol"] = Protocol }
               .ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>Review replies carry the shown frame: {"ok":true,"cmd":"step","offsetMs":-33,"frame":{…}}.</summary>
        private string Reviewed(JObject ok)
        {
            var rv = Widget.Review;
            ok["offsetMs"] = System.Math.Round(rv.OffsetMs); ok["frame"] = rv.FrameJson();
            return ok.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>The connected joysticks and the raw value of axes 1–8 (for mapping a stick / an RC radio dongle).</summary>
        private static JObject JoystickJson(AeroWidget w)
        {
            var names = new JArray(); { string jn = FlyingGame.Bridge.JoystickInput.ConnectedName; if (jn != null) names.Add(jn); }
            var ax = new JArray();
            for (int i = 1; i <= 8; i++) ax.Add(System.Math.Round(FlyingGame.Bridge.JoystickInput.Raw(i), 3));
            var c = w.Controls;
            var jw = w.JoyWizard; var prof = FlyingGame.Bridge.JoystickInput.Profile;
            var setup = new JObject { ["active"] = jw.Active, ["step"] = jw.StepNumber, ["of"] = jw.StepCount, ["text"] = jw.Instruction, ["status"] = jw.Status, ["progress"] = System.Math.Round(jw.Progress, 2), ["focusAxis"] = jw.Focus, ["learnt"] = jw.Summary() };
            JObject pj = null;
            if (prof != null)
            {
                JObject A(FlyingGame.Bridge.JoyAxis a) => a.Set ? new JObject { ["axis"] = a.Index, ["center"] = System.Math.Round(a.Center, 3), ["pos"] = System.Math.Round(a.PosEnd, 3), ["neg"] = System.Math.Round(a.NegEnd, 3), ["dead"] = System.Math.Round(a.Dead, 3) } : null;
                var r = prof.Read();
                pj = new JObject { ["name"] = prof.Name, ["aileron"] = A(prof.Aileron), ["elevator"] = A(prof.Elevator), ["rudder"] = A(prof.Rudder), ["throttle"] = A(prof.Throttle), ["buttons"] = JObject.FromObject(prof.Buttons), ["axisButtons"] = JObject.FromObject(prof.AxisButtons), ["flapSwitch"] = A(prof.FlapSwitch), ["flapSwitchPos"] = prof.FlapSwitchPos(),
                                   ["now"] = new JObject { ["aileron"] = System.Math.Round(r.ail, 3), ["elevator"] = System.Math.Round(r.ele, 3), ["rudder"] = System.Math.Round(r.rud, 3), ["throttle"] = r.hasThr ? System.Math.Round(r.thr01, 3) : null } };
            }
            return new JObject { ["names"] = names, ["axes"] = ax, ["setup"] = setup, ["profile"] = pj, ["actions"] = new JArray(FlyingGame.Bridge.JoystickInput.Actions),
                ["map"] = new JObject { ["aileron"] = c.AxAileron, ["elevator"] = c.AxElevator, ["rudder"] = c.AxRudder, ["throttle"] = c.AxThrottle, ["invertElevator"] = c.InvertElevator, ["throttleFromAxis"] = c.ThrottleFromAxis } };
        }

        private JObject PanelJson()
        {
            var w = Widget; float a = WidgetControlsDisplay.PanelAspectN;
            float pw = w.PanelW > 0 ? w.PanelW : Mathf.Min(1f, AeroHost.UiBand / a);
            float px = w.PanelW > 0 ? w.PanelX : (1f - pw) * 0.5f, py = w.PanelW > 0 ? w.PanelY : 1f - pw * a;
            return new JObject { ["x"] = System.Math.Round(px, 4), ["y"] = System.Math.Round(py, 4), ["w"] = System.Math.Round(pw, 4), ["h"] = System.Math.Round(pw * a, 4),
                                 ["aspect"] = System.Math.Round(a, 4), ["minW"] = AeroHost.PanelMinW, ["custom"] = w.PanelW > 0 };
        }

        /// <summary>Protocol 6 addendum: the control instruments' hit areas, 0…1 of the frame, top-left origin (ntsb only).</summary>
        private JObject LayoutJson()
        {
            var w = Widget; var d = w.Display;
            if (w.DisplayMode != "ntsb" || d == null || d.Layout.Count == 0) return null;
            JArray R((float x, float y, float w, float h) r) => new JArray(System.Math.Round(r.x, 4), System.Math.Round(r.y, 4), System.Math.Round(r.w, 4), System.Math.Round(r.h, 4));
            var o = new JObject();
            foreach (var kv in d.Layout)
            {
                var e = new JObject { ["rect"] = R(kv.Value) };
                switch (kv.Key)
                {
                    case "yoke": e["axes"] = "elevator+aileron"; e["elevatorUp"] = "push"; e["aileronRight"] = "right"; break;
                    case "pedals": e["axis"] = "rudder"; e["orientation"] = "horizontal"; e["right"] = "right"; break;
                    case "throttle": e["axis"] = "throttle"; e["orientation"] = "vertical"; e["top"] = "full"; e["engines"] = w.Controls.Spec.Engines; break;
                    case "flaps":
                        e["axis"] = "flaps"; e["orientation"] = "vertical"; e["top"] = "up";
                        e["detents"] = new JArray(d.FlapDetentPositions ?? new double[0]);
                        { var dv = new JArray(); var fl = w.Controls.Spec.Flaps; foreach (var x in fl) dv.Add(System.Math.Round(x / fl[^1], 4)); e["detentValues"] = dv; }
                        break;
                    case "spoilers": e["axis"] = "spoilers"; e["orientation"] = "vertical"; e["top"] = "retracted"; break;
                    case "gear": e["action"] = "toggle"; e["cmd"] = "controls.gear"; break;
                    case "panel": e["action"] = "move"; e["cmd"] = "panel"; e["note"] = "drag the box (outside the controls) to move it"; break;
                    case var tk when tk.StartsWith("tap:") && d.TapCmds.TryGetValue(tk, out var tj):
                        e["action"] = "tap"; e["send"] = JObject.Parse(tj); break;
                    case "glassMenu": e["action"] = "glassMenu"; e["label"] = "MENU"; break;
                    case "panelMove": e["action"] = "move"; e["cmd"] = "panel"; e["note"] = "the MCP strip across the top: a grab bar (display only, no controls)"; break;
                    case "panelResize": e["action"] = "resize"; e["cmd"] = "panel"; e["corner"] = "bottomRight"; e["aspect"] = WidgetControlsDisplay.PanelAspectN; break;
                }
                o[kv.Key] = e;
            }
            return o;
        }

        /// <summary>Protocol 7: the frame's forces and moments (signed; moments nose-up +; tail force + = UP).</summary>
        private JObject ForcesJson()
        {
            var p = Widget.Vectors?.Current; if (p == null) return null;
            return new JObject
            {
                ["inertialG"] = System.Math.Round(p.InertialG, 3), ["aeroG"] = System.Math.Round(p.AeroG, 3), ["tailLb"] = System.Math.Round(-p.TailN * 0.2248, 1),
                ["liftLb"] = System.Math.Round(p.LiftN * 0.224809, 1), ["dragLb"] = System.Math.Round(p.DragN * 0.224809, 1), ["thrustLb"] = System.Math.Round(p.ThrustAlongN * 0.224809, 1), ["weightLb"] = System.Math.Round(p.WeightN * 0.224809, 1),
                ["momentAero"] = System.Math.Round(p.MomAero * 0.7376), ["momentInertia"] = System.Math.Round(p.MomInertia * 0.7376),
                // (momentInertia is the airframe's own coupling = momentStructure; the prop's gyroscopic part is separate)
                ["momentStructure"] = System.Math.Round(p.MomInertia * 0.7376), ["momentGyro"] = System.Math.Round(p.MomGyro * 0.7376), ["momentInertiaTotal"] = System.Math.Round((p.MomInertia + p.MomGyro) * 0.7376),
                ["tailAlphaL"] = System.Math.Round(p.TailAlphaL, 2), ["tailAlphaR"] = System.Math.Round(p.TailAlphaR, 2), ["downwashDeg"] = System.Math.Round(p.Eps, 2),
                ["tailEta"] = System.Math.Round((p.EtaL + p.EtaR) * 0.5, 3), ["tailEtaL"] = System.Math.Round(p.EtaL, 3), ["tailEtaR"] = System.Math.Round(p.EtaR, 3),
                ["tailSpeedL"] = System.Math.Round(p.TailSpdL * 1.943844, 1), ["tailSpeedR"] = System.Math.Round(p.TailSpdR * 1.943844, 1), ["freestreamKt"] = System.Math.Round(p.VInf * 1.943844, 1),
                ["wingAlphaL"] = System.Math.Round(p.WingAlphaL, 2), ["wingAlphaR"] = System.Math.Round(p.WingAlphaR, 2),
                ["wingLiftLbL"] = System.Math.Round(p.WLiftL.Length * 0.2248), ["wingLiftLbR"] = System.Math.Round(p.WLiftR.Length * 0.2248),
                ["wingDragLbL"] = System.Math.Round(p.WDragL.Length * 0.2248), ["wingDragLbR"] = System.Math.Round(p.WDragR.Length * 0.2248),
            };
        }

        private JObject ShowJson() { var o = new JObject(); foreach (var kv in Widget.Show) o[kv.Key] = kv.Value; return o; }

        private JObject LoadingJson()
        {
            var L = Widget.Loading;
            return new JObject
            {
                ["grossWeightLb"] = System.Math.Round(L.Kg * 2.20462), ["cgPercentMac"] = System.Math.Round(L.CgMac, 2), ["staticMarginMac"] = System.Math.Round(L.StaticMarginMac, 2),
                ["withinLimits"] = L.WithinLimits, ["overweight"] = L.Overweight, ["cgOut"] = L.CgOut,
                ["targetLb"] = System.Math.Round(L.TargetKg * 2.20462), ["targetCgMac"] = System.Math.Round(L.TargetCgMac, 2),
            };
        }
        private JObject AltLockJson() => new() { ["on"] = Widget.AltLock, ["aglFt"] = System.Math.Round(Widget.AltLockFt), ["active"] = Widget.AltLock && Widget.AltLockApplies };
        private JObject ApJson()
        {
            var a = Widget.Autopilot;
            return new JObject
            {
                ["on"] = a.On, ["pitchMode"] = a.PitchMode, ["rollMode"] = a.RollMode, ["altitudeFt"] = System.Math.Round(a.AltFt), ["vsFpm"] = System.Math.Round(a.VsFpm),
                ["kias"] = System.Math.Round(a.Kias, 1), ["headingBug"] = System.Math.Round(a.HdgDeg), ["yawDamper"] = a.YawDamper, ["autothrottle"] = a.AutoThrottle,
                ["elevator"] = System.Math.Round(a.Elevator, 3), ["trim"] = System.Math.Round(a.Trim, 3), ["power"] = System.Math.Round(a.Power, 3),
                ["status"] = a.Status, ["disc"] = a.Disc, ["autothrottleDisc"] = a.AtDisc, ["fma"] = a.Annunciation,
                ["pitchDeg"] = System.Math.Round(a.PitchDeg, 1), ["bankDeg"] = System.Math.Round(a.BankDeg, 1), ["cws"] = a.Cws, ["cwsPilotFlying"] = a.CwsOverride,
                ["lnav"] = "unavailable", ["loc"] = "unavailable", ["app"] = "unavailable",
            };
        }
        private static readonly Dictionary<string, JObject> _ldFits = new();
        /// <summary>Per type: MAC, default loading and this sim's limits (approximations: the configs carry no POH envelope).</summary>
        private static JObject LoadingFit(string id)
        {
            if (_ldFits.TryGetValue(id, out var o)) return o;
            try
            {
                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                double sc = 0, sc2 = 0, sle = 0;
                foreach (var sf in cfg.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (var st in sf.Strips) { double c = st.Chord, sp = st.Area / System.Math.Max(1e-6, c); sc += c * sp; sc2 += c * c * sp; sle += (st.Pos[0] + 0.25 * c) * c * sp; }
                double mac = sc > 0 ? sc2 / sc : 1.5, le = sc > 0 ? sle / sc : cfg.Mass.Cg[0] + 0.375;
                double ToMac(double x) => (le - x) / mac * 100;
                var tables = FlyingGame.Sim.Aircraft.BuildAirfoilTables(cfg);
                (double L, double M) At(double aDeg)
                {
                    double ar = aDeg * System.Math.PI / 180, V = 40;
                    var keep = FlyingGame.Core.Aero.ForceDebug.Samples; FlyingGame.Core.Aero.ForceDebug.Samples = null;
                    var (F, Mo) = FlyingGame.Core.Aero.AeroModel.Compute(cfg, tables, new FlyingGame.Core.MathTypes.Vec3(V * System.Math.Cos(ar), 0, V * System.Math.Sin(ar)), FlyingGame.Core.MathTypes.Vec3.Zero, FlyingGame.Core.MathTypes.Vec3.Zero, 1.225, new FlyingGame.Core.Aero.ControlDeflections(0, 0, 0, 0, 0));
                    FlyingGame.Core.Aero.ForceDebug.Samples = keep;
                    return (F.X * System.Math.Sin(ar) - F.Z * System.Math.Cos(ar), Mo.Y);
                }
                var (l1, m1) = At(2); var (l2, m2) = At(6);
                double np = ToMac(cfg.Mass.Cg[0] + (System.Math.Abs(l2 - l1) > 1 ? (m2 - m1) / (l2 - l1) : 0)), def = ToMac(cfg.Mass.Cg[0]);
                double lb = cfg.Mass.MassKg * 2.20462;
                o = new JObject
                {
                    ["emptyLb"] = System.Math.Round(lb * 0.62), ["maxGrossLb"] = System.Math.Round(lb), ["defaultLb"] = System.Math.Round(lb),
                    ["cgLimitsMac"] = new JArray(System.Math.Round(def - 9, 1), System.Math.Round(System.Math.Min(def + 9, np - 5), 1)),
                    ["defaultCgMac"] = System.Math.Round(def, 1), ["neutralPointMac"] = System.Math.Round(np, 1), ["macM"] = System.Math.Round(mac, 3),
                };
            }
            catch { o = new JObject(); }
            _ldFits[id] = o; return o;
        }

        private JObject PilotJson()
        {
            var c = Widget.Controls;
            return new JObject { ["id"] = c.PilotId, ["name"] = c.PilotName, ["color"] = c.PilotId == null ? null : c.PilotColor, ["managed"] = c.Managed };
        }

        /// <summary>The controls as shown (in review, the frame's): every input plus flapsActual and the gear lights.</summary>
        private static JObject ControlsJson(AeroWidget w)
        {
            var f = w.Shown ?? w.Review.Capture();
            var spec = w.Controls.Spec;
            return new JObject
            {
                ["aileron"] = f.Ail, ["elevator"] = f.Ele, ["rudder"] = f.Rud, ["throttle"] = f.Thr, ["brake"] = System.Math.Max(f.BrakeL, f.BrakeR),
                ["brakeL"] = f.BrakeL, ["brakeR"] = f.BrakeR, ["handsOff"] = f.HandsOff,
                ["surfDeg"] = new JObject { ["aileron"] = w.Ac.CurrentDeflections.AileronRad * 57.2958, ["elevator"] = w.Ac.CurrentDeflections.ElevatorRad * 57.2958, ["rudder"] = w.Ac.CurrentDeflections.RudderRad * 57.2958 },
                ["flaps"] = f.FlapsCmd, ["flapsDeg"] = System.Math.Round(f.FlapsCmd * spec.Flaps[^1], 1), ["flapsActual"] = System.Math.Round(f.FlapsActual, 4),
                ["flapsActualDeg"] = System.Math.Round(f.FlapsActual * spec.Flaps[^1], 1),
                ["spoilers"] = f.Spoilers, ["spoilersArmed"] = f.SpoilersArmed, ["gear"] = f.Gear, ["gearLights"] = f.GearLights,
            };
        }

        private JObject StateJson()
        {
            var w = Widget; var r = w.Read(); var c = w.Controls;
            return new JObject
            {
                ["ok"] = true, ["type"] = "state", ["scenario"] = w.Current.ToString().ToLowerInvariant(), ["aircraft"] = w.AircraftId, ["flaps"] = w.Flaps,
                ["paused"] = w.Paused, ["timescale"] = w.TimeScale, ["view"] = w.View, ["viewFrom"] = w.ViewFrom, ["preset"] = w.Presets.Running, ["source"] = c.SourceLabel,
                ["tow"] = w.Tow.Active ? new JObject { ["rope"] = w.Tow.Connected, ["tensionN"] = w.Tow.Rope.Tension, ["sever"] = w.Tow.Rope.SeverReason, ["tugPhase"] = w.Tow.Pilot.Status,
                    ["tugKts"] = w.Tow.Tug.State.Velocity.Length * 1.94384, ["ropeM"] = (w.Tow.Rope.TugHookWorld - w.Tow.Rope.GliderHookWorld).Length, ["tugAboveM"] = -w.Tow.Tug.State.Position.Z + w.Ac.State.Position.Z } : null,
                // The shown frame's position on the TRUE path (north, east, down, m — the altitude lock's shift removed): fly-by placement.
                ["posNed"] = new JArray(System.Math.Round(w.ShownState.Position.X, 2), System.Math.Round(w.ShownState.Position.Y, 2), System.Math.Round(w.ShownState.Position.Z - (w.Shown?.AltShift ?? w.AltShiftM), 2)),
                ["kias"] = r.Kias, ["ktas"] = r.Ktas, ["alpha"] = r.AlphaDeg, ["beta"] = r.BetaDeg, ["q"] = r.Q,
                ["pitch"] = r.PitchDeg, ["roll"] = r.RollDeg, ["heading"] = r.HeadingDeg, ["sinkFpm"] = r.SinkFpm, ["heightFt"] = r.HeightFt,
                ["rollRate"] = r.RollRateDps, ["pitchRate"] = r.PitchRateDps, ["yawRate"] = r.YawRateDps, ["nz"] = r.LoadFactor,
                ["leftWingAlpha"] = r.LeftAlphaDeg, ["rightWingAlpha"] = r.RightAlphaDeg, ["leftStalled"] = r.LeftStalled, ["rightStalled"] = r.RightStalled, ["onGround"] = r.OnGround, ["lost"] = new JArray(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(w.Ac.LostComponents, lc => (object)lc.ToString()))),
                ["controls"] = ControlsJson(w),
                ["joystick"] = JoystickJson(w),
                ["syphon"] = AeroWidget.StreamName, ["ndi"] = AeroWidget.StreamName, ["frame"] = AeroWidget.FrameH, ["frameW"] = AeroWidget.FrameW, ["frameH"] = AeroWidget.FrameH, ["frameMode"] = w.FrameMode,
                ["display"] = new JObject { ["mode"] = w.DisplayMode, ["split"] = w.Split, ["panel"] = PanelJson(), ["zoom"] = w.Zoom },
                ["controlsLayout"] = LayoutJson(),
                ["lesson"] = w.Lesson, ["lessonPhase"] = w.Lessons.Caption, ["lessonResults"] = new JArray(w.Lessons.Results.ToArray()),
                ["beta"] = System.Math.Round(r.BetaDeg, 2), ["ball"] = System.Math.Round(w.BallDeg, 2), ["loadFactor"] = System.Math.Round(r.LoadFactor, 3),
                ["flightPathDeg"] = System.Math.Round(System.Math.Atan2(-r.SinkFpm / 196.85, System.Math.Max(0.1, r.Ktas / 1.943844)) * 57.2958, 2),
                ["cgPercentMac"] = System.Math.Round(w.Loading.CgMac, 2), ["loading"] = LoadingJson(), ["autopilot"] = ApJson(), ["altitudeLock"] = AltLockJson(),
                ["wind"] = new JObject { ["fromDeg"] = w.WindFromDeg, ["kt"] = w.WindKt }, ["insets"] = new JArray(w.Insets),
                ["show"] = ShowJson(),
                ["forces"] = ForcesJson(),
                ["condition"] = w.Condition, ["viewFixed"] = w.ViewFixed, ["hold"] = w.Controls.Hold,
                ["pilot"] = PilotJson(), ["remotes"] = RemotesJson(), ["controlsDisplay"] = new JObject { ["shown"] = w.Show["controlsDisplay"], ["place"] = w.ControlsPlace, ["size"] = w.ControlsSize, ["traces"] = w.Show["controlTraces"] },
                ["review"] = new JObject { ["active"] = w.Review.Active, ["offsetMs"] = System.Math.Round(w.Review.OffsetMs), ["historyMs"] = System.Math.Round(w.Review.HistoryMs),
                    ["playing"] = w.Review.Playing, ["rate"] = w.Review.Rate, ["direction"] = w.Review.Direction },
            };
        }
    }
}
