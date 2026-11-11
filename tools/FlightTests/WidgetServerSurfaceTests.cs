using System.Text.RegularExpressions;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE WIDGET SERVER'S WIRING, held against its own source.
///
/// <c>WidgetServer</c> is a MonoBehaviour: it needs UnityEngine, RenderTexture and a running player, so this suite
/// cannot call it. What it CAN do is read the file and check that the dangerous shapes the pre-ship audit of
/// 2026-11-11 found are still gone — the same trick <c>StreamingAssetsTests</c> plays on ConfigSync, and for the
/// same reason: a rule nothing enforces comes back. The rules themselves live in <see cref="FlyingGame.Core.WidgetAccess"/>
/// and are tested properly in <c>WidgetAccessTests</c>; these are the wiring checks that go with them.
///
/// A test that reads source is a blunt instrument and it is written to fail LOUDLY and legibly rather than
/// silently stop checking: every one of them first asserts that the anchor it searches for is still in the file.
/// </summary>
public sealed class WidgetServerSurfaceTests
{
    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }

    private static string Source(string name)
        => File.ReadAllText(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", "Widget", name));

    private static string BridgeSource(string name)
        => File.ReadAllText(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", name));

    /// <summary>The same source with every comment removed. These files explain themselves at length and several of
    /// those explanations QUOTE the dangerous shape they replaced — "this used to be File.WriteAllBytes(path, …)" —
    /// so a check run over the raw text fails on the very comment that documents the fix.</summary>
    private static string Code(string src)
    {
        src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(src, @"//[^\n]*", " ");
    }

    /// <summary>The body of one `case "x": { … }` arm of WidgetServer.Handle, by brace counting.</summary>
    private static string CommandCase(string src, string cmd)
    {
        // Anchored on the arm's own indentation: Handle's switch is at 16 spaces, and `case "start":` also exists
        // INSIDE the joystick arm's wizard switch at 24. Without this the test read the wrong arm and still passed
        // its other assertion, which is the failure mode a source-reading test has to be built against.
        var m = Regex.Match(src, $"^ {{16}}case \"{Regex.Escape(cmd)}\":", RegexOptions.Multiline);
        Assert.True(m.Success, $"WidgetServer.cs no longer has a top-level `case \"{cmd}\":` — this test reads it");
        int at = m.Index;
        int open = src.IndexOf('{', at);
        Assert.True(open > 0);
        int depth = 0, i = open;
        for (; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) break;
        }
        return src.Substring(open, i - open + 1);
    }

    // ---- snapshot: the arbitrary file write ------------------------------------------------------------------

    [Fact]
    public void SnapshotNeverWritesAPathTheCallerChose()
    {
        string body = CommandCase(Code(Source("WidgetServer.cs")), "snapshot");
        Assert.Contains("WidgetAccess.SnapshotPath(SnapshotDir", body, StringComparison.Ordinal);
        // The shape the audit found: the caller's "path" reaching a write unfiltered.
        Assert.DoesNotMatch(new Regex(@"string\s+path\s*=\s*\(string\)\s*j\[""path""\]"), body);
        Assert.Matches(new Regex(@"WriteAllBytes\(\s*path\s*,"), body);   // it still writes `path`…
        Assert.DoesNotMatch(new Regex(@"WriteAllBytes\(\s*\(string\)"), body);
    }

    // ---- non-finite numbers off the wire --------------------------------------------------------------------

    [Fact]
    public void EveryCommandIsCheckedForNaNBeforeItIsDispatched()
    {
        string code = Code(Source("WidgetServer.cs"));
        // The guard is at the one door every command comes through, not sprinkled over ~60 read sites — and it is
        // BEFORE the dispatch, not somewhere further down where only the commands that remembered it are covered.
        int handle = code.IndexOf("private string Handle(JObject j, string src, Auth auth)", StringComparison.Ordinal);
        Assert.True(handle > 0, "WidgetServer.cs no longer declares Handle(…) — this test reads it");
        int check = code.IndexOf("NonFinite(j, null)", handle, StringComparison.Ordinal);
        int dispatch = code.IndexOf("switch (cmd)", handle, StringComparison.Ordinal);
        Assert.True(check > 0, "Handle no longer checks the command for non-finite numbers");
        Assert.True(dispatch > 0 && check < dispatch, "the non-finite check must run before the command is dispatched");
        Assert.Contains("NonFinite(JToken t, string path)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNaNCheckLooksAtStringsAndArraysAndNestedObjectsToo()
    {
        // Json.NET turns the STRING "NaN" into double.NaN as readily as the bare literal, and the widget's
        // commands nest ({"loading":{…}}, "crowdNed":[n,e,d]) — a check that walked only top-level numbers would
        // leave both doors open.
        string walker = Code(Source("WidgetServer.cs"));
        int at = walker.IndexOf("NonFinite(JToken t, string path)", StringComparison.Ordinal);
        Assert.True(at > 0);
        string body = walker.Substring(at, Math.Min(2000, walker.Length - at));
        Assert.Contains("JTokenType.String", body, StringComparison.Ordinal);
        Assert.Contains("JTokenType.Object", body, StringComparison.Ordinal);
        Assert.Contains("JTokenType.Array", body, StringComparison.Ordinal);
        Assert.Contains("JTokenType.Float", body, StringComparison.Ordinal);
    }

    [Theory]
    // The three sites the audit named, each with a caller that does NOT come through WidgetServer.Handle, so each
    // keeps its own guard: the web remote and Glass reach FromNetwork, the W&B lessons call Loading.Set directly,
    // and JoyLink's axes arrive as raw bytes on a UDP socket with no JSON anywhere.
    [InlineData("Widget/WidgetControls.cs", "n.Aileron = WidgetAccess.Finite(n.Aileron)")]
    [InlineData("Widget/WidgetLoading.cs", "kg = FlyingGame.Core.WidgetAccess.Finite(kg)")]
    [InlineData("JoyLink.cs", "WidgetAccess.FiniteOr(System.BitConverter.ToSingle(_buf, n), 0f)")]
    public void TheThreeSitesThatFeedThePhysicsGuardThemselves(string file, string needle)
    {
        string path = Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", file.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains(needle, File.ReadAllText(path), StringComparison.Ordinal);
    }

    // ---- the token and the binding --------------------------------------------------------------------------

    [Fact]
    public void ThePortsAreLoopbackOnlyUnlessTheLanIsAskedFor()
    {
        string code = Code(Source("WidgetServer.cs"));
        // The wildcard bind is now reachable ONLY inside Bind's `if (Lan)` arm. A port that is not listening
        // cannot be scanned, cannot prompt the firewall, and does not appear on Bonjour.
        int bind = code.IndexOf("List<TcpListener> Bind(", StringComparison.Ordinal);
        Assert.True(bind > 0, "WidgetServer.cs no longer declares Bind() — this test reads it");
        foreach (string wildcard in new[] { "IPAddress.IPv6Any", "IPAddress.Any" })
        {
            int at = code.IndexOf(wildcard, StringComparison.Ordinal);
            Assert.True(at > bind, $"{wildcard} is bound outside Bind() — the loopback default has a hole");
        }
        Assert.Contains("IPAddress.Loopback", code, StringComparison.Ordinal);
        Assert.Contains("IPAddress.IPv6Loopback", code, StringComparison.Ordinal);
        Assert.Contains("WidgetAccess.LanEnvVar", code, StringComparison.Ordinal);
        // Bonjour must not advertise a service nothing on the network can reach.
        Assert.Matches(new Regex(@"if \(!AeroWidget\.Capture && Lan\) Advertise\(\)"), code);
    }

    [Fact]
    public void LoopbackIsDecidedFromTheSocketAndNeverFromTheRequest()
    {
        // A "loopback" or "local" field in the JSON would be the client marking its own homework.
        string code = Code(Source("WidgetServer.cs"));
        Assert.Contains("RemoteEndPoint", code, StringComparison.Ordinal);
        Assert.Contains("WidgetAccess.IsLoopback", code, StringComparison.Ordinal);
        int isLocal = code.IndexOf("static bool IsLocal(TcpClient c)", StringComparison.Ordinal);
        Assert.True(isLocal > 0);
        Assert.DoesNotMatch(new Regex(@"Loopback\s*=\s*\(?\(bool\)?\s*j\["), code);
        Assert.DoesNotMatch(new Regex(@"j\[""loopback""\]"), code);
    }

    [Fact]
    public void EveryCommandPassesTheGateAndHelloCarriesTheTokenCheck()
    {
        string code = Code(Source("WidgetServer.cs"));
        int handle = code.IndexOf("private string Handle(JObject j, string src, Auth auth)", StringComparison.Ordinal);
        Assert.True(handle > 0, "Handle no longer takes the request's Auth — this test reads it");
        int gate = code.IndexOf("WidgetAccess.CommandRefusal(auth.Loopback, auth.Authed, cmd)", handle, StringComparison.Ordinal);
        int dispatch = code.IndexOf("switch (cmd)", handle, StringComparison.Ordinal);
        Assert.True(gate > 0 && gate < dispatch, "the gate must run before the command is dispatched");
        Assert.Contains("WidgetAccess.HelloRefusal(auth.Loopback, (string)j[\"token\"], Token)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedClientIsToldWhyAndTheConnectionStaysOpen()
    {
        // Ty's rule 5. Glass Overlay connects over the LAN; a silent drop there is invisible, and an invisible
        // break is the expensive kind. The refusal names the reason, says needsToken, and still identifies the
        // protocol so a client knows what it reached.
        string code = Code(Source("WidgetServer.cs"));
        int refused = code.IndexOf("static string Refused(string cmd, string why)", StringComparison.Ordinal);
        Assert.True(refused > 0, "WidgetServer.cs no longer declares Refused() — this test reads it");
        string body = code.Substring(refused, Math.Min(400, code.Length - refused));
        Assert.Contains("[\"error\"] = why", body, StringComparison.Ordinal);
        Assert.Contains("[\"needsToken\"] = true", body, StringComparison.Ordinal);
        Assert.Contains("[\"protocol\"] = Protocol", body, StringComparison.Ordinal);
        // The TCP arm answers the refusal; it does not close the socket on an unauthenticated client.
        Assert.DoesNotContain("if (gate != null) { conn.Close()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SubscribingIsGatedTooAndHappensOnTheMainThread()
    {
        // It used to be registered on the READER thread, by looking for the word "subscribe" anywhere in the raw
        // line — so an unauthenticated LAN connection would have been streamed the whole state at up to 60 Hz
        // before its hello was ever judged, and a caption containing the word would have subscribed as well.
        string code = Code(Source("WidgetServer.cs"));
        Assert.DoesNotContain("l.Contains(\"\\\"subscribe\\\"\")", code, StringComparison.Ordinal);
        string body = CommandCase(code, "subscribe");
        Assert.Contains("_subs.Add((auth.Conn", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTokenIsTheWidgetsOwnFileAndIsNeverInTheRepository()
    {
        string code = Code(Source("WidgetServer.cs"));
        Assert.Contains("WidgetAccess.ReadOrCreateToken(Application.persistentDataPath)", code, StringComparison.Ordinal);
        // No literal token anywhere: this repository is public.
        Assert.DoesNotMatch(new Regex(@"Token\s*=\s*""[0-9a-f]{8,}"""), code);
    }

    [Fact]
    public void TheTokenIsShownWhereThePersonAtTheMacCanReadIt()
    {
        // Ty's rule 2: a secret nobody can see is a secret nobody can use.
        // Raw source, not Code(): the status line it lives on contains "http://", and the comment stripper eats
        // from the // to the end of the line. (It did exactly that, and this test caught it.)
        Assert.Contains("WidgetServer.Token", Source("AeroWidget.cs"), StringComparison.Ordinal);
        // And on the remote page — but ONLY when that page is being served to a browser on this machine, which
        // can read the token file anyway and needs no token to drive the widget.
        string server = Code(Source("WidgetServer.cs"));
        Assert.Matches(new Regex(@"local \? WidgetRemotePage\.WithToken\(Token\) : WidgetRemotePage\.Html"), server);
        Assert.Contains("/*TOKEN*/null", Source("WidgetRemotePage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnHttpRequestFromTheLanCarriesItsOwnToken()
    {
        // HTTP has no connection to remember a handshake on, so every request is judged on its own.
        string code = Code(Source("WidgetServer.cs"));
        Assert.Contains("WidgetAccess.RequestRefusal(local, presented, Token)", code, StringComparison.Ordinal);
        Assert.Contains("needsToken: true", code, StringComparison.Ordinal);
        // The page itself is still served to the LAN: a device has to be able to open it to be ASKED for a token.
        Assert.Contains("WidgetRemotePage.Html", code, StringComparison.Ordinal);
        // And the page sends it with every request.
        string page = Source("WidgetRemotePage.cs");
        Assert.Contains("if(TOKEN)o.token=TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("&token='+encodeURIComponent(TOKEN)", page, StringComparison.Ordinal);
    }

    // ---- the main thread ------------------------------------------------------------------------------------

    [Fact]
    public void TheMainThreadNeverWritesToAClientSocket()
    {
        // A StreamWriter with AutoFlush on a TCP socket BLOCKS when the peer stops reading. Writing the state
        // stream from Update() meant one suspended subscriber could stop the widget's frame — rendering, physics,
        // Syphon, NDI — until the socket drained or TCP gave up, which is minutes.
        string code = Code(Source("WidgetServer.cs"));
        int update = code.IndexOf("private void Update()", StringComparison.Ordinal);
        Assert.True(update > 0, "WidgetServer.cs no longer declares Update() — this test reads it");
        string rest = code.Substring(update);
        int nextMethod = rest.IndexOf("public void PushEvent", StringComparison.Ordinal);
        string body = nextMethod > 0 ? rest.Substring(0, nextMethod) : rest;
        Assert.DoesNotContain(".WriteLine(", body, StringComparison.Ordinal);
        Assert.Contains("s.c.Send(st)", body, StringComparison.Ordinal);

        // PushEvent runs on the main thread too (SeeRemote, PruneRemotes and the lessons all call it).
        int push = code.IndexOf("public void PushEvent", StringComparison.Ordinal);
        string pushBody = code.Substring(push, Math.Min(400, code.Length - push));
        Assert.DoesNotContain(".WriteLine(", pushBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AStalledSubscribersQueueIsBoundedAndEventuallyHungUpOn()
    {
        // An unbounded queue turns a stalled subscriber into a memory leak at up to 60 state lines a second, in a
        // process that is already watched for its footprint (the capture guard exists because this widget was once
        // found at 50 GB).
        string code = Code(Source("WidgetServer.cs"));
        Assert.Contains("MaxQueued", code, StringComparison.Ordinal);
        Assert.Contains("DropsBeforeHangUp", code, StringComparison.Ordinal);
        var m = Regex.Match(code, @"MaxQueued\s*=\s*(\d+)");
        Assert.True(m.Success && int.Parse(m.Groups[1].Value) is > 0 and <= 4096, "the output queue must be bounded, and bounded small");
    }

    // ---- CORS -----------------------------------------------------------------------------------------------

    [Fact]
    public void NeitherHttpPathSendsWildcardCors()
    {
        // "Access-Control-Allow-Origin: *" with no authentication behind it is the browser being told to let any
        // page in the world drive this server. It was on BOTH paths — the raw one that serves the remote page and
        // the legacy HttpListener one that nothing reaches today.
        string code = Code(Source("WidgetServer.cs"));
        Assert.DoesNotContain("Access-Control-Allow-Origin", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Access-Control-Allow-Headers", code, StringComparison.Ordinal);
        // Both response writers still exist, so this is not passing because the code went away.
        Assert.Contains("HTTP/1.1 200 OK", code, StringComparison.Ordinal);
        Assert.Contains("ctx.Response.ContentType = type", code, StringComparison.Ordinal);
    }

    // ---- the fleet check ------------------------------------------------------------------------------------

    [Fact]
    public void EveryCommandThatLoadsAnAeroplaneChecksTheFleet()
    {
        // "start" has always checked; "scenario" did not, and the id reaches
        // LoadFromStreamingAssets -> "aircraft/" + id + ".json", so it was a read of any .json file the process
        // could reach — and the key of two static caches that nothing bounds.
        string code = Code(Source("WidgetServer.cs"));
        foreach (string cmd in new[] { "scenario", "start" })
        {
            string body = CommandCase(code, cmd);
            Assert.Contains("SessionSettings.Fleet", body, StringComparison.Ordinal);
            Assert.Contains("unknown aircraft", body, StringComparison.Ordinal);
        }
    }

    // ---- request sizes --------------------------------------------------------------------------------------

    [Fact]
    public void TheTcpPortNoLongerReadsAnUnboundedLine()
    {
        string code = Code(Source("WidgetServer.cs"));
        // A bare StreamReader.ReadLine() on a socket has no limit: a peer that never sends a newline grows one
        // string until the process dies, and it costs the peer nothing.
        Assert.DoesNotMatch(new Regex(@"reader\.ReadLine\(\)"), code);
        Assert.Contains("ReadLineCapped(reader, conn)", code, StringComparison.Ordinal);
        Assert.Contains("WidgetAccess.MaxLineBytes", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHttpBodyIsNotAllocatedFromTheRequestsOwnClaim()
    {
        string code = Code(Source("WidgetServer.cs"));
        int alloc = code.IndexOf("new byte[len]", StringComparison.Ordinal);
        Assert.True(alloc > 0, "WidgetServer.cs no longer allocates the body as new byte[len] — this test reads it");
        int cap = code.IndexOf("WidgetAccess.MaxBodyBytes", StringComparison.Ordinal);
        Assert.True(cap > 0 && cap < alloc, "Content-Length must be capped BEFORE the array is allocated");
        Assert.Contains("WidgetAccess.MaxHeadBytes", code, StringComparison.Ordinal);
        // A truncated header block used to be parsed as though the request were complete.
        Assert.Contains("headComplete", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnapshotDirectoryIsTheWidgetsOwnAndNotTheRepository()
    {
        string src = Source("WidgetServer.cs");
        var m = Regex.Match(src, @"SnapshotDir\s*=>\s*([^;]+);");
        Assert.True(m.Success, "WidgetServer.cs no longer declares SnapshotDir — this test reads it");
        Assert.Contains("Application.persistentDataPath", m.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("WidgetAccess.SnapshotDirName", m.Groups[1].Value, StringComparison.Ordinal);
    }
}
