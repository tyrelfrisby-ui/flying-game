using System.Diagnostics;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// tools/check-parity.sh (Ty 2026-11-04: "ios/mac and quest must stay in sync at all times"), run against a scratch
/// git repository built here, so each verdict is checked against a history whose answer is known — not against the
/// live lanes, whose state changes every hour.
/// </summary>
public class ParityCheckTests : IDisposable
{
    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }
    private static string Script => Path.Combine(Repo, "tools", "check-parity.sh");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "parity-test-" + Guid.NewGuid().ToString("N")[..8]);

    public ParityCheckTests()
    {
        Directory.CreateDirectory(_dir);
        Git("init -q -b base");
        Write("README", "base");
        Write("unity/Assets/_Project/Resources/Models/a/a-own.glb", "glb v1");
        Commit("base");
        Git("branch ios"); Git("branch quest");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Git(string args)
    {
        var psi = new ProcessStartInfo("git", "-c user.name=t -c user.email=t@t -c commit.gpgsign=false " + args)
        { WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
        Assert.True(p.ExitCode == 0, "git " + args);
        return o;
    }

    private void Write(string rel, string text)
    {
        string f = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    private void Commit(string msg) { Git("add -A"); Git($"commit -q -m \"{msg}\""); }

    private void On(string branch, string rel, string text, string msg) { Git("checkout -q " + branch); Write(rel, text); Commit(msg); }

    private (int code, string output) Check(params string[] extra)
    {
        var psi = new ProcessStartInfo("/bin/bash") { WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Script);
        foreach (string a in extra) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("ios"); psi.ArgumentList.Add("quest");
        psi.Environment["PARITY_ART_RE"] = "art";
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
        return (p.ExitCode, o);
    }

    [Fact]
    public void TwoLanesAtTheSameCommitAreInSync()
    {
        var (code, o) = Check();
        Assert.Equal(0, code);
        Assert.Contains("PARITY: OK", o);
        Assert.Contains("GLBs: all 1 identical", o);
    }

    [Fact]
    public void AnIosCommitTheQuestLaneLacksFailsAndIsListed()
    {
        On("ios", "src/FlyingGame.Core/Pitts.cs", "panel", "Pitts panel and canopy");
        var (code, o) = Check();
        Assert.Equal(1, code);
        Assert.Contains("FAIL  1 commit(s) of ios are not in quest", o);
        Assert.Contains("Pitts panel and canopy", o);
        Assert.Contains("PARITY: FAIL", o);
    }

    [Fact]
    public void MergingIosIntoQuestPutsThemBackInSync()
    {
        On("ios", "configs/aircraft/x.json", "{}", "config");
        Git("checkout -q quest"); Git("merge -q --no-ff ios -m merge");
        Assert.Equal(0, Check().code);
    }

    [Fact]
    public void AQuestCommitOnSharedPathsWarnsAndFailsWhenStrict()
    {
        On("quest", "unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs", "x", "shared change on the quest lane");
        var (code, o) = Check();
        Assert.Equal(0, code);
        Assert.Contains("WARN  1 commit(s) on quest touch SHARED paths", o);
        Assert.Contains("shared change on the quest lane", o);
        Assert.Equal(1, Check("--strict").code);
    }

    [Fact]
    public void AQuestOnlyCommitIsAllowed()
    {
        On("quest", "unity/Assets/_Project/Scripts/Bridge/QuestRig.cs", "x", "quest rig");
        On("quest", "unity/Assets/_Project/Scripts/Quest/Hands.cs", "x", "quest hands");
        On("quest", "tools/deploy/deploy-quest.sh", "x", "quest deploy");
        var (code, o) = Check("--strict");
        Assert.Equal(0, code);
        Assert.Contains("no commit on quest alone touches shared paths", o);
        Assert.Contains("3 Quest-only commit(s)", o);
    }

    [Fact]
    public void ADifferentGlbIsReportedAndFailsWhenStrict()
    {
        On("quest", "unity/Assets/_Project/Resources/Models/a/a-own.glb", "glb v2", "quest windshield");
        var (code, o) = Check();
        Assert.Equal(0, code);
        Assert.Contains("DIFFERENT      a/a-own.glb", o);
        Assert.Equal(1, Check("--strict").code);
    }

    [Fact]
    public void AnArtLaneInNeitherIsListed()
    {
        Git("checkout -q -b lane/art-cockpit base");
        Write("unity/Assets/_Project/Resources/Models/b/b-own.glb", "new");
        Commit("new cockpit");
        var (_, o) = Check();
        Assert.Contains("lane/art-cockpit", o);
    }

    [Fact]
    public void AMissingBranchIsAUsageError()
    {
        var psi = new ProcessStartInfo("/bin/bash") { WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Script); psi.ArgumentList.Add("ios"); psi.ArgumentList.Add("no-such-lane");
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
        Assert.Equal(2, p.ExitCode);
    }
}
