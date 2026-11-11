using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// t961 / t983 / t989 (false aircraft image over the water; diagnosis state/water-image-t989-diagnosis.md): Camera.CopyFrom
/// copies the source camera's normalized viewport rect. In portrait the main camera draws above the touch tray, rect
/// (0, f, 1, 1−f), so a camera copied from it and rendered into a texture fills only part of that texture. WaterReflection
/// did exactly that and the water, which samples the whole mirror target, drew every reflection lifted toward the horizon.
/// ClipRecorder (503cde02) and VrRig already reset the rect after their CopyFrom; this holds every runtime camera to it.
/// Ty 2026-11-10, a J-3 on a lake at 1 KT and 1 FT: "The reflection is still above the airplane so that has not been nixed".
/// STILL: this test and the one-line fix it guards were written on 2026-11-07 on lane/claude-q007-pitts-spin-t918, which merged
/// into nothing, so neither ever reached a build. The editor check (WaterMirrorViewportCheck) measures the real geometry but
/// needs the exclusive Unity slot and a GPU and has never been run; THIS is the check that would have caught it in the suite,
/// and the only reason it did not is that it was sitting on an orphaned lane. Keep it where the suite runs.
/// A camera copied for the SCREEN (LocalCoop's second player, whose split rect is the point) does not set a target and is
/// not covered.
/// </summary>
public class CameraCopyViewportTests
{
    private readonly ITestOutputHelper _o;
    public CameraCopyViewportTests(ITestOutputHelper o) { _o = o; }

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }

    [Fact]
    public void ACameraCopiedIntoATextureFillsIt()
    {
        string scripts = Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts");
        string editor = Path.Combine(scripts, "Editor") + Path.DirectorySeparatorChar;
        var copy = new Regex(@"(\w+)\.CopyFrom\(");
        var offenders = new List<string>();
        int checkedCopies = 0;
        foreach (string file in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories).Where(p => !p.StartsWith(editor)))
        {
            string src = File.ReadAllText(file);
            foreach (Match m in copy.Matches(src))
            {
                string cam = m.Groups[1].Value;
                string after = src[m.Index..];
                bool toTexture = Regex.IsMatch(after, $@"\b{Regex.Escape(cam)}\.targetTexture\s*=\s*(?!null\b)\w");
                if (!toTexture) continue;
                checkedCopies++;
                if (!Regex.IsMatch(after, $@"\b{Regex.Escape(cam)}\.rect\s*=\s*new\s+Rect\(\s*0\s*,\s*0\s*,\s*1\s*,\s*1\s*\)"))
                    offenders.Add($"{Path.GetRelativePath(scripts, file)}: {cam}.CopyFrom(...) renders into a texture without {cam}.rect = new Rect(0, 0, 1, 1)");
            }
        }
        _o.WriteLine($"camera copies into a texture: {checkedCopies}");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        // ClipRecorder, VrRig and WaterReflection: if this falls the scan has stopped seeing them, not passed.
        Assert.True(checkedCopies >= 3, $"only {checkedCopies} texture camera copies found");
    }
}
