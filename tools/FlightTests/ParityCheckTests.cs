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

    /// <summary>
    /// Ty 2026-11-10: "Keep the aero widget in parity with the app" — "Enforce it like the others." The widget is the
    /// same project, the same scene and the same src/ as the game, so it cannot be a third branch target
    /// (docs/PARITY.md, "The widget"). What CAN drift is the widget's own files between the two lanes, and three of
    /// the paths it is built from are NEUTRAL to the commit lock. tools/native-mac/ is the one that bit: it is the
    /// SOURCE of the AeroNative bundle whose committed product is already shared, so on 2026-11-10 the two lanes
    /// differed by 98 lines of it with the parity gate green throughout. This is the GLB check's own mechanism —
    /// blob against blob — pointed at that surface.
    /// </summary>
    [Fact]
    public void ADriftedWidgetFileIsReportedAndFailsWhenStrict()
    {
        On("ios", "tools/native-mac/build.sh", "mac v1", "widget native side");
        Git("checkout -q quest"); Git("merge -q --no-ff ios -m merge");
        Assert.Contains("widget: all 1 files identical", Check().output);

        // The neutral path drifts on the Quest lane alone: the commit lock cannot see it, this check must.
        On("quest", "tools/native-mac/build.sh", "mac v2", "quest-only change to the widget's native source");
        var (code, o) = Check();
        Assert.Equal(0, code);
        Assert.Contains("DIFFERENT      tools/native-mac/build.sh", o);
        Assert.Contains("the widget is built from the same scene and src/ as the app", o);
        Assert.Equal(1, Check("--strict").code);
    }

    /// <summary>A widget file on one lane only is drift too: the widget would build differently from each lane.</summary>
    [Fact]
    public void AWidgetFileOnOneLaneOnlyIsReported()
    {
        On("ios", "unity/Assets/_Project/Widget/Resources/WidgetCopy.shader", "alpha blend", "widget copy shader");
        var (_, o) = Check();
        Assert.Contains("only on iOS    unity/Assets/_Project/Widget/Resources/WidgetCopy.shader", o);
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

    /// <summary>
    /// Runs the LOCK (<c>--hook</c>) against the index of the scratch repository, on whatever branch it is on, with no
    /// positional arguments — because the thing under test is how the script classifies the CHECKED-OUT branch, and a
    /// positional iOS branch would short-circuit exactly that. The Quest lane is named through the environment, as a
    /// real worktree would name it in <c>$PARITY_QUEST_BRANCH</c>.
    /// </summary>
    private (int code, string output) Hook(params (string k, string v)[] env)
    {
        var psi = new ProcessStartInfo("/bin/bash") { WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Script); psi.ArgumentList.Add("--hook");
        psi.Environment["PARITY_QUEST_BRANCH"] = "quest";
        psi.Environment["PARITY_TRUNK_RE"] = "^(main|master|split-surfaces)$";
        foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
        return (p.ExitCode, o);
    }

    private void Stage(string rel, string text) { Write(rel, text); Git("add -- " + rel); }

    /// <summary>
    /// THE LOCK ON A TRUNK (Ty 2026-11-10: "Install enforcement on main too"). A triage that day found that none of
    /// the parity apparatus existed on main or split-surfaces, so the installed dispatcher ran nothing and exited 0:
    /// anything committed from the main checkout — the checkout that builds and publishes — bypassed parity
    /// entirely. The trunk rule is NOT the lane rule, because the lane rule's only remedy is "sync with the other
    /// lane" and a trunk is not one of the pair. Shared work may not originate on a trunk at all.
    ///
    /// The allowances are what make the rule affordable in the checkout Ty actually works in, so each is a case
    /// here: a non-shared commit (his AGENT-LOG line and his tools/deploy/ scripts are both outside SHARED_RE), and
    /// PARITY_OVERRIDE=1, the escape hatch deploy-quest.sh already uses for the same rule.
    /// </summary>
    [Fact]
    public void TheLockRefusesASharedChangeOnATrunkAndAllowsEverythingElse()
    {
        Git("checkout -q -b main base");

        Stage("AGENT-LOG.md", "a log line");                       // neutral: the main checkout's own daily commit
        Assert.Equal(0, Hook().code);
        Stage("tools/deploy/publish-ota.sh", "#!/bin/bash");       // neutral: the scripts that checkout exists to run
        Assert.Equal(0, Hook().code);
        Commit("log and deploy tooling");

        Stage("configs/aircraft/x.json", "{}");                    // shared: this is the hole being closed
        var (code, o) = Hook();
        Assert.Equal(1, code);
        Assert.Contains("main is a TRUNK, not a lane", o);
        Assert.Contains("configs/aircraft/x.json", o);
        Assert.Contains("in no lane at all", o);

        // The same commit, forced the way deploy-quest.sh's override is forced: through, and it says so out loud.
        var (fcode, fo) = Hook(("PARITY_OVERRIDE", "1"));
        Assert.Equal(0, fcode);
        Assert.Contains("PARITY_OVERRIDE=1", fo);
        Assert.Contains("AGENT-LOG.md", fo);
    }

    /// <summary>A merge brings shared code onto a trunk legitimately: it came from a branch, and it is how a lane
    /// would ever reach main. Blocking it would deadlock the merge rather than prevent anything.</summary>
    [Fact]
    public void TheLockLetsATrunkMergeABranchThatCarriesSharedCode()
    {
        On("ios", "configs/aircraft/x.json", "{}", "shared change on a lane");
        Git("checkout -q -b main base");
        Git("merge --no-ff --no-commit ios");
        Assert.Equal(0, Hook().code);
        Git("merge --abort");
    }

    /// <summary>
    /// A FEATURE BRANCH IS NOT A TRUNK. The 15 codex-* branches and wip/* commit shared code all day and must keep
    /// being able to: their shared work meets the lock when it is merged into a lane, and nobody builds from one. The
    /// asymmetry with lane/* is real and surprising enough to be worth a test rather than a sentence — a regex wide
    /// enough to catch split-surfaces by shape would catch all of these with it, which is why the trunks are named.
    /// </summary>
    [Theory]
    [InlineData("codex-foo")]
    [InlineData("wip/scratch")]
    [InlineData("spin-step3-hysteresis")]
    public void TheLockIsSilentOnABranchThatIsNeitherALaneNorATrunk(string branch)
    {
        Git($"checkout -q -b {branch} base");
        Stage("src/FlyingGame.Core/X.cs", "shared");
        Assert.Equal(0, Hook().code);
    }

    /// <summary>
    /// THE CONSEQUENCE THAT BIT SEVERAL AGENTS ON 2026-11-10, pinned down so docs/PARITY.md cannot drift away from it
    /// again. The lock fires on EVERY lane/* branch, not only the two shipping lanes, because the iOS lane defaults
    /// to the branch the worktree is on (2026-11-08). So a feature lane that is merely BEHIND the Quest lane — it has
    /// changed nothing shared itself; the Quest lane moved — is refused its next shared commit. Ty kept that scope
    /// ("the lock being live on every lane is arguably right — it is what stopped several agents shipping unsynced
    /// work today"), so the remedy is the thing that has to be written down: merge the Quest lane in.
    /// </summary>
    [Fact]
    public void ALaneMerelyBehindTheQuestLaneIsBlockedAndMergingTheQuestLaneInIsTheRemedy()
    {
        On("quest", "src/FlyingGame.Core/A.cs", "quest moved on", "a shared change on the quest lane");
        Git("checkout -q -b lane/feature base");
        Stage("src/FlyingGame.Core/B.cs", "my own work");
        var (code, o) = Hook();
        Assert.Equal(1, code);
        Assert.Contains("lane/feature already differs from quest in shared code", o);
        Assert.Contains("git merge --no-ff quest", o);

        // The remedy, as a worktree would actually do it: git will not merge over a staged change, so unstage (the
        // file stays in the working tree), merge the Quest lane in, stage again. The same commit then goes through.
        Git("restore --staged -- src/FlyingGame.Core/B.cs");
        Git("merge --no-ff --no-verify -m \"merge quest\" quest");
        Git("add -- src/FlyingGame.Core/B.cs");
        Assert.Equal(0, Hook().code);
    }

    /// <summary>
    /// From a trunk, "the iOS lane" must NOT come out as the trunk itself. deploy.sh prints this report on every iOS
    /// build out of the main checkout, and defaulting the iOS lane to the branch the worktree is on made that read
    /// "main vs quest": every lane commit listed as missing, with the advice to merge main into the Quest lane. A
    /// trunk reads the lane pair out of the Quest lane's history instead, as a Quest worktree does, and says plainly
    /// that what is being built here is in neither lane.
    /// </summary>
    [Fact]
    public void TheReportFromATrunkNamesTheLanePairAndSaysTheTrunkIsInNeither()
    {
        Git("checkout -q -b lane/ios-real ios");
        Write("configs/aircraft/x.json", "{}"); Commit("shared change on the ios lane");
        Git("checkout -q quest"); Git("merge -q --no-ff lane/ios-real -m merge");
        Git("checkout -q -b split-surfaces base");

        var psi = new ProcessStartInfo("/bin/bash") { WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Script); psi.ArgumentList.Add("--brief");
        psi.Environment["PARITY_QUEST_BRANCH"] = "quest";
        psi.Environment["PARITY_TRUNK_RE"] = "^(main|master|split-surfaces)$";
        psi.Environment["PARITY_ART_RE"] = "art";
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();

        Assert.Contains("this checkout is on split-surfaces, a TRUNK and not a lane", o);
        Assert.Contains("in NEITHER lane", o);
        Assert.DoesNotContain("iOS split-surfaces", o);
        Assert.Contains("lane/ios-real", o);
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
