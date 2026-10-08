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
        private readonly ConcurrentQueue<(string json, System.Action<string> reply)> _inbox = new();
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
                try { _tcp = new TcpListener(IPAddress.Any, TcpPort); _tcp.Start(); _tcpThread = new Thread(AcceptLoop) { IsBackground = true }; _tcpThread.Start(); }
                catch (System.Exception e) { Debug.LogWarning("[Widget] TCP: " + e.Message); }
                try
                {
                    _http = new HttpListener(); _http.Prefixes.Add($"http://*:{HttpPort}/"); _http.Start();
                    _httpThread = new Thread(HttpLoop) { IsBackground = true }; _httpThread.Start();
                }
                catch (System.Exception e) { Debug.LogWarning("[Widget] HTTP: " + e.Message); }
                Debug.Log($"[Widget] control tcp {TcpPort}, remote http://{LocalIp}:{HttpPort}/");
            }) { IsBackground = true }.Start();
        }

        private void OnDestroy()
        {
            _run = false;
            try { _tcp?.Stop(); } catch { }
            try { _http?.Stop(); } catch { }
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
            try
            {
                string line;
                while (_run && (line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string l = line;
                    _inbox.Enqueue((l, reply => { lock (writer) { try { writer.WriteLine(reply); } catch { } } }));
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

        // ---- HTTP (phone / iPad remote) ----
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
                else if (path == "/state") Respond(ctx, Ask("{\"cmd\":\"state\"}"), "application/json");
                else Respond(ctx, WidgetRemotePage.Html, "text/html; charset=utf-8");
            }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        /// <summary>Enqueue a command for the main thread and wait (≤ 1 s) for its reply.</summary>
        private string Ask(string json)
        {
            var done = new ManualResetEventSlim(false); string result = "{\"ok\":false,\"error\":\"timeout\"}";
            _inbox.Enqueue((json, r => { result = r; done.Set(); }));
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
                try { reply = Handle(JObject.Parse(m.json)); }
                catch (System.Exception e) { reply = new JObject { ["ok"] = false, ["error"] = e.Message }.ToString(Newtonsoft.Json.Formatting.None); }
                m.reply?.Invoke(reply);
            }
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

        private string Handle(JObject j)
        {
            string cmd = (string)j["cmd"] ?? "";
            var w = Widget; var ok = new JObject { ["ok"] = true, ["cmd"] = cmd };
            switch (cmd)
            {
                case "scenario":
                {
                    string n = ((string)j["name"] ?? w.Current.ToString()).ToLowerInvariant();
                    var sc = n == "spin" ? AeroWidget.Scenario.Spin : AeroWidget.Scenario.Flare;
                    w.Load(sc, (string)j["aircraft"] ?? w.AircraftId, j["flaps"]?.Value<double>() ?? w.Flaps);
                    break;
                }
                case "controls":
                    w.Controls.FromNetwork(j["aileron"]?.Value<double>(), j["elevator"]?.Value<double>(), j["rudder"]?.Value<double>(),
                        j["throttle"]?.Value<double>(), j["brake"]?.Value<double>(), j["handsOff"]?.Value<bool>(), (string)j["source"] ?? "network");
                    break;
                case "preset":
                    if (!w.Presets.Run((string)j["name"] ?? "")) { ok["ok"] = false; ok["error"] = "unknown preset"; }
                    break;
                case "pause": w.Paused = true; break;
                case "resume": w.Paused = false; break;
                case "reset": w.Load(w.Current, w.AircraftId, w.Flaps); break;
                case "timescale": w.TimeScale = Mathf.Clamp((float)(j["value"]?.Value<double>() ?? 1), 0.05f, 1f); break;
                case "view":
                {
                    string vn = ((string)j["name"] ?? "side").ToLowerInvariant();
                    if (System.Array.IndexOf(AeroWidget.Views, vn) < 0) { ok["ok"] = false; ok["error"] = "unknown view"; break; }
                    w.SetView(vn, ((string)j["from"])?.ToLowerInvariant());   // the flare is always side-on: accepted, no change there
                    ok["view"] = w.View; ok["viewFrom"] = w.ViewFrom; break;
                }
                case "hello":
                    ok["app"] = "Aero Widget"; ok["version"] = Application.version; ok["protocol"] = 2;
                    ok["commands"] = new JArray("hello", "scenario", "controls", "preset", "pause", "resume", "reset", "timescale", "view", "show", "fleet", "state", "subscribe", "snapshot", "gamepad");
                    ok["scenarios"] = new JArray("flare", "spin");
                    ok["views"] = new JArray(AeroWidget.Views); ok["viewFrom"] = new JArray(AeroWidget.LockFrom);
                    ok["presets"] = new JArray(System.Linq.Enumerable.Concat(WidgetPresets.SpinPresets, WidgetPresets.FlarePresets));
                    ok["show"] = new JArray(w.Show.Keys);
                    ok["frame"] = AeroWidget.FrameSize; ok["syphon"] = AeroWidget.StreamName; ok["ndi"] = AeroWidget.StreamName;
                    ok["tcpPort"] = TcpPort; ok["httpPort"] = HttpPort;
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
                    foreach (var f in SessionSettings.Fleet) arr.Add(new JObject { ["id"] = f.id, ["name"] = f.name });
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
                ["controls"] = new JObject { ["aileron"] = c.Aileron, ["elevator"] = c.Elevator, ["rudder"] = c.Rudder, ["throttle"] = c.Throttle01, ["brake"] = c.Brake01, ["handsOff"] = c.ElevatorFree },
                ["syphon"] = AeroWidget.StreamName, ["ndi"] = AeroWidget.StreamName, ["frame"] = AeroWidget.FrameSize,
            };
        }
    }
}
