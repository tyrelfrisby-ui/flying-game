using System.Text.RegularExpressions;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>The TestFlight slim package (Ty 2026-11-05 18:05: "only the 2-33, the J-3 both variants, the Pitts, the Extra
/// and the Great Lakes"). Expected lists are literals, copied by hand from Ty's request: adding a type to the package is a
/// deliberate edit here and in TestFlightFleet. The package is opt-in (FLYINGGAME_TF_SLIM=1 deploy.sh); these tests also
/// hold that the normal build is untouched by it.</summary>
public sealed class TestFlightFleetTests
{
    private static readonly string Repo = FindRepo();
    private static string FindRepo()
    {
        string d = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(d, "configs", "aircraft"))) d = Path.GetDirectoryName(d)!;
        return d;
    }
    private static string Models => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models");
    private static string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { Repo }.Concat(p).ToArray()));

    [Fact]
    public void ThePackageListsTyssSixTypes()
    {
        Assert.Equal(new[] { "glider-2-33-like", "j3-cub-like", "j3-cub-floats-like", "pitts-s2b-like", "extra-300-like", "great-lakes-like" },
                     TestFlightFleet.Listed);
        Assert.Equal(new[] { "pilot-body", "pilot-gear", "sequence-card" }, TestFlightFleet.SharedModelFolders);
        foreach (string id in new[] { "c172-like", "p51d-like", "pa18-cub-like", "pa18-floats-like", "glider-grob103-acro-like", "target-drone-like", null })
            Assert.False(TestFlightFleet.IsListed(id), id ?? "null");
        Assert.Equal("glider-2-33-like", TestFlightFleet.Resolve("c172-like"));
        Assert.Equal("glider-2-33-like", TestFlightFleet.Resolve(null));
        Assert.Equal("j3-cub-floats-like", TestFlightFleet.Resolve("j3-cub-floats-like"));
    }

    [Fact]
    public void FilterKeepsTheFleetsOrderAndNames()
    {
        var fleet = new[] { ("paraglider-en-b-like", "Para"), ("glider-2-33-like", "2-33"), ("c172-like", "172"), ("j3-cub-like", "J-3"),
                            ("j3-cub-floats-like", "J-3 floats"), ("extra-300-like", "Extra"), ("pitts-s2b-like", "Pitts"), ("great-lakes-like", "GL") };
        Assert.Equal(new[] { ("glider-2-33-like", "2-33"), ("j3-cub-like", "J-3"), ("j3-cub-floats-like", "J-3 floats"), ("extra-300-like", "Extra"),
                             ("pitts-s2b-like", "Pitts"), ("great-lakes-like", "GL") },
                     TestFlightFleet.Filter(fleet));
    }

    /// <summary>Each listed type and shared folder has its Resources/Models folder (the build fails without one), the
    /// package holds out every other folder on disk, and the held-out ones are still in the tree (nothing deleted).</summary>
    [Fact]
    public void EveryListedTypeHasItsModelFolderAndTheRestStayInTheTree()
    {
        var folders = Directory.GetDirectories(Models).Select(Path.GetFileName).ToArray();
        foreach (string f in TestFlightFleet.Listed.Concat(TestFlightFleet.SharedModelFolders)) Assert.Contains(f, folders);
        var held = folders.Where(f => !TestFlightFleet.KeepsModelFolder(f)).ToArray();
        foreach (string f in new[] { "c172-like", "p51d-like", "boeing-737-like", "glider-grob103-acro-like", "pa18-cub-like", "target-drone-like" })
            Assert.Contains(f, held);
        Assert.Equal(folders.Length, held.Length + TestFlightFleet.Listed.Count + TestFlightFleet.SharedModelFolders.Count);
        Assert.False(Directory.Exists(Path.Combine(Repo, "unity", "Assets", "_Project", "TfSlimHold")),
                     "TfSlimHold exists: a slim build left model folders held out; the next iOS build restores them");
    }

    /// <summary>Every listed type is in the full picker list, so filtering it really offers six (and the 2-33 first).</summary>
    [Fact]
    public void TheFullFleetOffersEveryListedType()
    {
        string src = Read("unity", "Assets", "_Project", "Scripts", "Bridge", "SessionSettings.cs");
        int a = src.IndexOf("public static readonly (string id, string name)[] Fleet", StringComparison.Ordinal);
        string block = src.Substring(a, src.IndexOf("};", a, StringComparison.Ordinal) - a);
        var ids = Regex.Matches(block, @"\(""([a-z0-9-]+-like)"",").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(TestFlightFleet.Listed.ToArray(), ids.Where(TestFlightFleet.IsListed).OrderBy(i => TestFlightFleet.Listed.ToList().IndexOf(i)).ToArray());
        Assert.Equal("glider-2-33-like", ids.First(TestFlightFleet.IsListed));
    }

    /// <summary>Normal builds are untouched: every use of the define sits behind #if, and the build only goes slim on
    /// the environment variable.</summary>
    [Fact]
    public void ThePackageIsOptIn()
    {
        string build = Read("unity", "Assets", "_Project", "Scripts", "Editor", "BuildScript.cs");
        Assert.Contains("GetEnvironmentVariable(FlyingGame.Core.TestFlightFleet.EnvVar) == \"1\"", build);
        Assert.Equal("FLYINGGAME_TF_SLIM", TestFlightFleet.EnvVar);
        Assert.Equal("FG_TF_SLIM", TestFlightFleet.Define);
        foreach (string f in new[] { "SessionSettings.cs", "TouchFlightControls.cs", "FlightSimDriver.cs" })
        {
            string src = Read("unity", "Assets", "_Project", "Scripts", "Bridge", f);
            Assert.Contains("#if FG_TF_SLIM", src);
            foreach (Match m in Regex.Matches(src, @"TestFlightFleet\.(Filter|IsListed|Listed)"))
            {
                int ifAt = src.LastIndexOf("#if FG_TF_SLIM", m.Index, StringComparison.Ordinal);
                int endAt = src.LastIndexOf("#endif", m.Index, StringComparison.Ordinal);
                Assert.True(ifAt >= 0 && ifAt > endAt, $"{f}: {m.Value} at {m.Index} is outside #if FG_TF_SLIM");
            }
        }
        Assert.DoesNotContain("FG_TF_SLIM", Read("unity", "ProjectSettings", "ProjectSettings.asset"));
    }
}
