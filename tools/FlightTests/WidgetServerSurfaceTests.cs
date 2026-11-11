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
        int at = src.IndexOf($"case \"{cmd}\":", StringComparison.Ordinal);
        Assert.True(at >= 0, $"WidgetServer.cs no longer has a `case \"{cmd}\":` — this test reads it");
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
