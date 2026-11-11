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
        int handle = code.IndexOf("private string Handle(JObject j, string src)", StringComparison.Ordinal);
        Assert.True(handle > 0, "WidgetServer.cs no longer declares Handle(JObject, string) — this test reads it");
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
        Assert.Contains("ReadLineCapped(reader, writer)", code, StringComparison.Ordinal);
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
