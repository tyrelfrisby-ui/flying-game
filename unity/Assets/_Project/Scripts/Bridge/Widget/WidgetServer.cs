using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
        public const int TcpPort = 47830, HttpPort = 47831;
        public AeroWidget Widget;
        public static string LocalIp { get; private set; } = "127.0.0.1";

        private TcpListener _tcp;
        private HttpListener _http;
        private Thread _tcpThread, _httpThread;
        private volatile bool _run = true;
        private readonly ConcurrentQueue<(string json, System.Action<string> reply, string src)> _inbox = new();
        private int _connSeq;
        /// <summary>The widget's own web remotes (protocol 4): id → (name, last seen). Main thread only.</summary>
        private readonly Dictionary<string, (string name, float seen)> _remotes = new();
        private readonly List<(StreamWriter w, float hz, float next)> _subs = new();
        private readonly object _subLock = new();

        private void Start()
        {
            // Everything network-ish off the main thread: a DNS lookup of the Mac's own name can hang for seconds on macOS
            // (it froze the widget's first frame), and HttpListener's start can block too.
            new Thread(() =>
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
                // Dual-stack (2026-10-09): IPv6 with DualMode also accepts IPv4, so a device that only reaches the Mac by an IPv6
                // (link-local) address can connect directly. IPv4-only if the stack refuses.
                try
                {
                    _tcp = new TcpListener(IPAddress.IPv6Any, TcpPort); _tcp.Server.DualMode = true; _tcp.Start(); TcpDualStack = true;
                }
                catch (System.Exception e6)
                {
                    Debug.LogWarning("[Widget] TCP IPv6 dual-stack unavailable (" + e6.Message + "): IPv4 only");
                    try { _tcp?.Stop(); } catch { }
                    try { _tcp = new TcpListener(IPAddress.Any, TcpPort); _tcp.Start(); } catch (System.Exception e) { Debug.LogWarning("[Widget] TCP: " + e.Message); _tcp = null; }
                }
                if (_tcp != null) { _tcpThread = new Thread(AcceptLoop) { IsBackground = true }; _tcpThread.Start(); }
                // HTTP: a small HTTP/1.1 server on its own dual-stack socket (Mono's HttpListener answered 400 to IPv6 Host headers).
                try
                {
                    try { _httpTcp = new TcpListener(IPAddress.IPv6Any, HttpPort); _httpTcp.Server.DualMode = true; _httpTcp.Start(); HttpIPv6 = true; }
                    catch { try { _httpTcp?.Stop(); } catch { } _httpTcp = new TcpListener(IPAddress.Any, HttpPort); _httpTcp.Start(); }
                    _httpThread = new Thread(HttpAcceptLoop) { IsBackground = true }; _httpThread.Start();
                }
                catch (System.Exception e) { Debug.LogWarning("[Widget] HTTP: " + e.Message); }
                Advertise();
                Debug.Log($"[Widget] control tcp {TcpPort}, remote http://{LocalIp}:{HttpPort}/");
            }) { IsBackground = true }.Start();
        }

        public static bool TcpDualStack { get; private set; }
        public static bool HttpIPv6 { get; private set; }
        // Bonjour through the system's mDNS responder (dns_sd.h; Process.Start isn't available in the IL2CPP player).
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
        private static extern int DNSServiceRegister(out System.IntPtr sdRef, uint flags, uint interfaceIndex, string name, string regtype, string domain, string host,
                                                     ushort portNetworkOrder, ushort txtLen, byte[] txtRecord, System.IntPtr callBack, System.IntPtr context);
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
        private static extern void DNSServiceRefDeallocate(System.IntPtr sdRef);
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
            lock (_bonjour) foreach (var sd in _bonjour) { try { DNSServiceRefDeallocate(sd); } catch { } }
            try { _tcp?.Stop(); } catch { }
            try { _http?.Stop(); } catch { }
            try { _httpTcp?.Stop(); } catch { }
        }

        // ---- TCP ----
        private void AcceptLoop()
        {
            while (_run)
            {
                TcpClient c;
                try { c = _tcp.AcceptTcpClient(); } catch { return; }
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
            try
            {
                string line;
                while (_run && (line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string l = line;
                    _inbox.Enqueue((l, reply => { lock (writer) { try { writer.WriteLine(reply); } catch { } } }, src));
                    if (l.Contains("\"subscribe\""))
                    {
                        float hz = 10f; try { hz = (float)(JObject.Parse(l)["hz"]?.Value<double>() ?? 10); } catch { }
                        lock (_subLock) _subs.Add((writer, Mathf.Clamp(hz, 1f, 60f), 0f));
                    }
                }
            }
            catch { }
            lock (_subLock) _subs.RemoveAll(s => s.w == writer);
            c.Close();
        }

        // ---- HTTP (phone / iPad remote): GET / (the page), GET /state[?from=…&name=…], POST /cmd ----
        private TcpListener _httpTcp;
        private void HttpAcceptLoop()
        {
            while (_run)
            {
                TcpClient c;
                try { c = _httpTcp.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => ServeRaw(c));
            }
        }

        private void ServeRaw(TcpClient c)
        {
            try
            {
                c.NoDelay = true; c.ReceiveTimeout = 5000;
                using var ns = c.GetStream();
                // Request line + headers (ASCII), then the body by Content-Length.
                var head = new StringBuilder(); int b; int crlf = 0;
                while ((b = ns.ReadByte()) >= 0)
                {
                    head.Append((char)b);
                    crlf = (b == '\r' || b == '\n') ? crlf + 1 : 0;
                    if (crlf == 4 || head.Length > 16384) break;
                }
                string[] lines = head.ToString().Split(new[] { "\r\n" }, System.StringSplitOptions.None);
                string[] rl = lines[0].Split(' ');
                if (rl.Length < 2) return;
                string method = rl[0], target = rl[1];
                int len = 0;
                foreach (var l in lines) if (l.StartsWith("Content-Length:", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out len);
                string body = "";
                if (len > 0)
                {
                    var buf = new byte[len]; int got = 0;
                    while (got < len) { int n = ns.Read(buf, got, len - got); if (n <= 0) break; got += n; }
                    body = Encoding.UTF8.GetString(buf, 0, got);
                }
                string path = target, query = "";
                int qi = target.IndexOf('?'); if (qi >= 0) { path = target.Substring(0, qi); query = target.Substring(qi + 1); }
                string text, type = "application/json";
                if (method == "OPTIONS") text = "";
                else if (path == "/cmd" && method == "POST") text = Ask(body);
                else if (path == "/state")
                {
                    var o = new JObject { ["cmd"] = "state" };
                    foreach (var kv in query.Split('&'))
                    {
                        int eq = kv.IndexOf('='); if (eq <= 0) continue;
                        string k = System.Uri.UnescapeDataString(kv.Substring(0, eq)), v = System.Uri.UnescapeDataString(kv.Substring(eq + 1).Replace('+', ' '));
                        if (k == "from" || k == "name") o[k] = v;
                    }
                    if (o["from"] != null && o["name"] == null) o["name"] = "";
                    text = Ask(o.ToString(Newtonsoft.Json.Formatting.None));
                }
                else { text = WidgetRemotePage.Html; type = "text/html; charset=utf-8"; }
                byte[] payload = Encoding.UTF8.GetBytes(text);
                string hdr = $"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {payload.Length}\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: *\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                byte[] hb = Encoding.ASCII.GetBytes(hdr);
                ns.Write(hb, 0, hb.Length); ns.Write(payload, 0, payload.Length); ns.Flush();
            }
            catch { }
            finally { try { c.Close(); } catch { } }
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
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                if (path == "/cmd" && ctx.Request.HttpMethod == "POST")
                {
                    string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
                    Respond(ctx, Ask(body), "application/json");
                }
                else if (path == "/state")
                {
                    // The remote polls this with its id and name (?from=web-…&name=…): that's how it shows up in "remotes".
                    var qs = ctx.Request.QueryString; var o = new JObject { ["cmd"] = "state" };
                    if (qs["from"] != null) { o["from"] = qs["from"]; o["name"] = qs["name"] ?? ""; }
                    Respond(ctx, Ask(o.ToString(Newtonsoft.Json.Formatting.None)), "application/json");
                }
                else Respond(ctx, WidgetRemotePage.Html, "text/html; charset=utf-8");
            }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        /// <summary>Enqueue a command for the main thread and wait (≤ 1 s) for its reply.</summary>
        private string Ask(string json)
        {
            var done = new ManualResetEventSlim(false); string result = "{\"ok\":false,\"error\":\"timeout\"}";
            _inbox.Enqueue((json, r => { result = r; done.Set(); }, null));
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
                try { reply = Handle(JObject.Parse(m.json), m.src); }
                catch (System.Exception e) { reply = new JObject { ["ok"] = false, ["error"] = e.Message }.ToString(Newtonsoft.Json.Formatting.None); }
                m.reply?.Invoke(reply);
            }
            PruneRemotes();
            lock (_subLock)
            {
                if (_subs.Count == 0) return;
                string st = null;
                for (int i = 0; i < _subs.Count; i++)
                {
                    var s = _subs[i];
                    if (Time.unscaledTime < s.next) continue;
                    st ??= StateJson().ToString(Newtonsoft.Json.Formatting.None);
                    try { lock (s.w) s.w.WriteLine(st); } catch { }
                    _subs[i] = (s.w, s.hz, Time.unscaledTime + 1f / s.hz);
                }
            }
        }

        /// <summary>Push an event line to every subscribed connection (Glass Overlay listens on its subscribe stream).</summary>
        public void PushEvent(JObject e)
        {
            string line = e.ToString(Newtonsoft.Json.Formatting.None);
            lock (_subLock) foreach (var s in _subs) { try { lock (s.w) s.w.WriteLine(line); } catch { } }
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

        private string Handle(JObject j, string src)
        {
            string cmd = (string)j["cmd"] ?? "";
            var w = Widget; var ok = new JObject { ["ok"] = true, ["cmd"] = cmd };
            // A web remote names itself on every request ("from":"web-…").
            string from = (string)j["from"];
            if (from != null && from.StartsWith("web-")) { SeeRemote(from, (string)j["name"]); src = from; }
            src ??= "http";
            switch (cmd)
            {
                case "scenario":
                {
                    w.Lessons.Stop();
                    string n = ((string)j["name"] ?? w.Current.ToString()).ToLowerInvariant();
                    var sc = n == "spin" ? AeroWidget.Scenario.Spin : n == "cruise" ? AeroWidget.Scenario.Cruise : AeroWidget.Scenario.Flare;
                    w.Load(sc, (string)j["aircraft"] ?? w.AircraftId, j["flaps"]?.Value<double>() ?? w.Flaps);
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
                    if (j["autothrottle"] != null) ap.AutoThrottle = j["autothrottle"].Value<bool>();
                    bool? on = j["on"]?.Value<bool>();
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
                case "wind":
                    w.SetWind(j["fromDeg"]?.Value<double>() ?? w.WindFromDeg, j["kt"]?.Value<double>() ?? w.WindKt);
                    ok["fromDeg"] = w.WindFromDeg; ok["kt"] = w.WindKt; break;
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
                case "vectorStyle":
                    // Protocol 7: line width (×) and the drawing filter's time constant.
                    if (j["width"] != null) w.Vectors.WidthScale = Mathf.Clamp((float)j["width"].Value<double>(), 1f, 3f);
                    if (j["smoothingMs"] != null) w.Vectors.SmoothingMs = Mathf.Clamp((float)j["smoothingMs"].Value<double>(), 0f, 400f);
                    ok["width"] = w.Vectors.WidthScale; ok["smoothingMs"] = w.Vectors.SmoothingMs; break;
                case "display":
                {
                    // Protocol 6: "ntsb" (instrument plates below the airplane, all on transparency) or "classic".
                    string mode = ((string)j["mode"])?.ToLowerInvariant();
                    if (mode != null && mode != "ntsb" && mode != "classic") { ok["ok"] = false; ok["error"] = "mode: ntsb or classic"; break; }
                    if (mode != null) w.DisplayMode = mode;
                    if (j["split"] != null) w.Split = Mathf.Clamp((float)j["split"].Value<double>(), 0.35f, 0.6f);
                    ok["mode"] = w.DisplayMode; ok["split"] = w.Split; break;
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
                    if (!w.Presets.Run((string)j["name"] ?? "")) { ok["ok"] = false; ok["error"] = "unknown preset"; }
                    break;
                case "pause": w.Review.Pause(); return Reviewed(ok);
                case "resume":
                    w.Review.Resume(((string)j["from"])?.ToLowerInvariant() == "live");
                    ok["offsetMs"] = 0; ok["frame"] = w.Review.FrameJson(); break;
                case "step": w.Review.Step(j["frames"]?.Value<int>() ?? -1); return Reviewed(ok);
                case "rewind": w.Review.Rewind(j["seconds"]?.Value<double>() ?? 1); return Reviewed(ok);
                case "seek": w.Review.Seek(j["offsetMs"]?.Value<double>() ?? 0); return Reviewed(ok);
                case "play": w.Review.Play((float)(j["rate"]?.Value<double>() ?? 0.25), (string)j["direction"] ?? "reverse"); return Reviewed(ok);
                case "reset": w.Load(w.Current, w.AircraftId, w.Flaps); break;
                case "timescale": w.TimeScale = Mathf.Clamp((float)(j["value"]?.Value<double>() ?? 1), 0.05f, 1f); break;
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
                    string vn = ((string)j["name"] ?? "side").ToLowerInvariant();
                    if (System.Array.IndexOf(AeroWidget.Views, vn) < 0) { ok["ok"] = false; ok["error"] = "unknown view"; break; }
                    string verr = w.CheckView(vn, ((string)j["from"])?.ToLowerInvariant());
                    if (verr != null) { ok["ok"] = false; ok["error"] = verr; ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break; }
                    if (w.Condition == "spin" && vn == "locked") { w.SetView("locked", "side"); ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break; }
                    w.SetView(vn, ((string)j["from"])?.ToLowerInvariant());   // the flare is always side-on: accepted, no change there
                    ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break;
                }
                case "hello":
                    ok["app"] = "Aero Widget"; ok["version"] = Application.version; ok["protocol"] = Protocol;
                    ok["network"] = new JObject { ["tcpDualStack"] = TcpDualStack, ["httpIPv6"] = HttpIPv6, ["bonjour"] = BonjourOk ? new JArray("_aerowidget._tcp", "_http._tcp") : new JArray() };
                    ok["features"] = new JArray("review", "controlsDisplay", "controlTraces", "pilot", "remotes", "conditions", "ntsbDisplay", "controlsLayout", "inertialForces", "moments", "smoothVectors", "lessons", "insets", "loading", "wind", "autopilot");
                    { var la = new JArray(); foreach (var l in WidgetLessons.All) la.Add(new JObject { ["id"] = l.Id, ["name"] = l.Name, ["concept"] = l.Concept }); ok["lessons"] = la; }
                    ok["insets"] = new JArray(WidgetControlsDisplay.InsetNames);
                    ok["autopilotModes"] = new JObject { ["pitch"] = new JArray("alt", "vs", "flc"), ["roll"] = new JArray("rol", "hdg"), ["other"] = new JArray("yawDamper", "autothrottle", "headingSelector"),
                        ["future"] = new JArray("lnav", "loc", "app"), ["futureNote"] = "buttons present, not active yet" };
                    if (LayoutJson() is JObject lay) ok["controlsLayout"] = lay;
                    ok["display"] = new JObject { ["modes"] = new JArray("classic", "ntsb"), ["default"] = "ntsb", ["split"] = new JArray(0.35, 0.6) };
                    ok["conditions"] = JArray.Parse(@"[{""id"":""cruise"",""name"":""Cruise"",""views"":""any"",""default"":""chase""},
                        {""id"":""final"",""name"":""On final"",""view"":""side"",""fixed"":true},
                        {""id"":""spin"",""name"":""Developed spin"",""views"":[{""name"":""locked"",""from"":""side""},{""name"":""body"",""from"":""left""}],""default"":""locked"",""hold"":true}]");
                    ok["bodyFrom"] = new JArray("left");
                    ok["commands"] = new JArray("hello", "start", "display", "vectorStyle", "lesson", "loading", "autopilot", "wind", "inset", "scenario", "controls", "preset", "pause", "resume", "step", "rewind", "seek", "play", "pilot", "controlsDisplay", "requestControls", "reset", "timescale", "view", "show", "fleet", "state", "subscribe", "snapshot", "gamepad");
                    ok["scenarios"] = new JArray("flare", "spin", "cruise");
                    ok["views"] = new JArray(AeroWidget.Views); ok["viewFrom"] = new JArray(AeroWidget.LockFrom);
                    ok["presets"] = new JArray(System.Linq.Enumerable.Concat(WidgetPresets.SpinPresets, WidgetPresets.FlarePresets));
                    ok["show"] = new JArray(w.Show.Keys);
                    ok["frame"] = AeroWidget.FrameSize; ok["syphon"] = AeroWidget.StreamName; ok["ndi"] = AeroWidget.StreamName;
                    ok["tcpPort"] = TcpPort; ok["httpPort"] = HttpPort;
                    ok["controlsDisplay"] = new JObject { ["places"] = new JArray("bottom", "left", "right"), ["size"] = new JArray(0.2, 0.5), ["default"] = new JObject { ["shown"] = false, ["place"] = "bottom", ["size"] = 0.28 } };
                    ok["controlInputs"] = new JArray("aileron", "elevator", "rudder", "throttle", "brake", "brakeL", "brakeR", "handsOff", "flaps", "spoilers", "spoilersArmed", "gear");
                    { var fl = new JArray(); foreach (var fa in SessionSettings.Fleet) fl.Add(new JObject { ["id"] = fa.id, ["name"] = fa.name, ["controls"] = ControlsFit(fa.id), ["loading"] = LoadingFit(fa.id) }); ok["fleet"] = fl; }
                    ok["review"] = new JObject { ["historySeconds"] = WidgetHistory.Seconds, ["fps"] = WidgetHistory.Hz, ["commands"] = new JArray("step", "rewind", "seek", "play") };
                    break;
                case "show":
                    foreach (var p in j.Properties()) if (p.Name != "cmd" && w.Show.ContainsKey(p.Name)) w.Show[p.Name] = p.Value.Value<bool>();
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
                    string path = (string)j["path"] ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aero-widget.png");
                    var was = RenderTexture.active; RenderTexture.active = w.Frame;
                    var tex = new Texture2D(w.Frame.width, w.Frame.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, w.Frame.width, w.Frame.height), 0, 0); tex.Apply();
                    RenderTexture.active = was;
                    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
                    ok["path"] = path; break;
                }
                case "state": return StateJson().ToString(Newtonsoft.Json.Formatting.None);
                case "subscribe": break;
                default: ok["ok"] = false; ok["error"] = "unknown cmd"; break;
            }
            return ok.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Review replies carry the shown frame: {"ok":true,"cmd":"step","offsetMs":-33,"frame":{…}}.</summary>
        private string Reviewed(JObject ok)
        {
            var rv = Widget.Review;
            ok["offsetMs"] = System.Math.Round(rv.OffsetMs); ok["frame"] = rv.FrameJson();
            return ok.ToString(Newtonsoft.Json.Formatting.None);
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
                ["momentAero"] = System.Math.Round(p.MomAero * 0.7376), ["momentInertia"] = System.Math.Round(p.MomInertia * 0.7376),
                ["tailAlphaL"] = System.Math.Round(p.TailAlphaL, 2), ["tailAlphaR"] = System.Math.Round(p.TailAlphaR, 2), ["downwashDeg"] = System.Math.Round(p.Eps, 2),
                ["tailEta"] = System.Math.Round((p.EtaL + p.EtaR) * 0.5, 3), ["tailEtaL"] = System.Math.Round(p.EtaL, 3), ["tailEtaR"] = System.Math.Round(p.EtaR, 3),
                ["tailSpeedL"] = System.Math.Round(p.TailSpdL * 1.943844, 1), ["tailSpeedR"] = System.Math.Round(p.TailSpdR * 1.943844, 1), ["freestreamKt"] = System.Math.Round(p.VInf * 1.943844, 1),
                ["wingAlphaL"] = System.Math.Round(p.WingAlphaL, 2), ["wingAlphaR"] = System.Math.Round(p.WingAlphaR, 2),
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
        private JObject ApJson()
        {
            var a = Widget.Autopilot;
            return new JObject
            {
                ["on"] = a.On, ["pitchMode"] = a.PitchMode, ["rollMode"] = a.RollMode, ["altitudeFt"] = System.Math.Round(a.AltFt), ["vsFpm"] = System.Math.Round(a.VsFpm),
                ["kias"] = System.Math.Round(a.Kias, 1), ["headingBug"] = System.Math.Round(a.HdgDeg), ["yawDamper"] = a.YawDamper, ["autothrottle"] = a.AutoThrottle,
                ["elevator"] = System.Math.Round(a.Elevator, 3), ["trim"] = System.Math.Round(a.Trim, 3), ["power"] = System.Math.Round(a.Power, 3),
                ["status"] = a.Status, ["disc"] = a.Disc, ["fma"] = a.Annunciation,
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
                ["kias"] = r.Kias, ["ktas"] = r.Ktas, ["alpha"] = r.AlphaDeg, ["beta"] = r.BetaDeg, ["q"] = r.Q,
                ["pitch"] = r.PitchDeg, ["roll"] = r.RollDeg, ["heading"] = r.HeadingDeg, ["sinkFpm"] = r.SinkFpm, ["heightFt"] = r.HeightFt,
                ["rollRate"] = r.RollRateDps, ["pitchRate"] = r.PitchRateDps, ["yawRate"] = r.YawRateDps, ["nz"] = r.LoadFactor,
                ["leftWingAlpha"] = r.LeftAlphaDeg, ["rightWingAlpha"] = r.RightAlphaDeg, ["leftStalled"] = r.LeftStalled, ["rightStalled"] = r.RightStalled, ["onGround"] = r.OnGround,
                ["controls"] = ControlsJson(w),
                ["syphon"] = AeroWidget.StreamName, ["ndi"] = AeroWidget.StreamName, ["frame"] = AeroWidget.FrameSize,
                ["display"] = new JObject { ["mode"] = w.DisplayMode, ["split"] = w.Split },
                ["controlsLayout"] = LayoutJson(),
                ["lesson"] = w.Lesson, ["lessonPhase"] = w.Lessons.Caption, ["lessonResults"] = new JArray(w.Lessons.Results.ToArray()),
                ["beta"] = System.Math.Round(r.BetaDeg, 2), ["ball"] = System.Math.Round(w.BallDeg, 2), ["loadFactor"] = System.Math.Round(r.LoadFactor, 3),
                ["flightPathDeg"] = System.Math.Round(System.Math.Atan2(-r.SinkFpm / 196.85, System.Math.Max(0.1, r.Ktas / 1.943844)) * 57.2958, 2),
                ["cgPercentMac"] = System.Math.Round(w.Loading.CgMac, 2), ["loading"] = LoadingJson(), ["autopilot"] = ApJson(),
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
