using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Crash debris wears the aeroplane on screen (Ty 2026-11-03: "When the pitts crashes and breaks apart it turns into the
/// old pitts render not the new one ... new broken pieces ... that do not change or lose their paint"). The break-up
/// itself is Unity code and is measured by the editor's DebrisPaintCheck (every face of every piece against what was drawn,
/// for every type and livery); what can be held here is the naming the prebuilt debris sets are looked up by, that none
/// is installed until Ty has approved the renders, and the wiring that keeps the hidden procedural shell out of the debris.
/// </summary>
public class DebrisWiringTests
{
    private const string Pitts = "pitts-s2b-like";

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }
    private static string Bridge => Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge");
    private static string Models => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models");

    /// <summary>Each Pitts livery's debris set is the GLB it flies in plus "-debris" — copied by hand from the
    /// lane/codex-pitts-debris naming, so a change to either side is a deliberate edit here.</summary>
    [Fact]
    public void EachPittsLiveryHasItsOwnDebrisSetName()
    {
        var want = new Dictionary<string, string>
        {
            ["checker-unmarked"] = "pitts-s2b-like-own-checker-unmarked-debris",
            ["factory-unmarked"] = "pitts-s2b-like-own-factory-unmarked-debris",
            ["n31dg"] = "pitts-s2b-like-own-debris",
            ["n1191"] = "pitts-s2b-like-own-n1191-debris",
        };
        Assert.Equal(want.Keys.OrderBy(k => k), LiveryChoice.For(Pitts).Select(o => o.Id).OrderBy(k => k));
        foreach (var (livery, _) in LiveryChoice.For(Pitts))
            Assert.Equal(want[livery], Pitts + LiveryChoice.OwnSuffix(Pitts, livery) + "-debris");
        Assert.Equal(want.Count, want.Values.Distinct().Count());   // no two liveries can share a set
    }

    /// <summary>The Codex lane's debris GLBs wait for Ty's approval of the renders: none ships yet.</summary>
    [Fact]
    public void NoDebrisSetIsInstalledBeforeTheRendersAreApproved()
    {
        var installed = Directory.GetFiles(Models, "*-debris*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")).ToList();
        Assert.True(installed.Count == 0, "debris sets installed before approval: " + string.Join(", ", installed));
    }

    /// <summary>The break-up's wiring: every cut goes through DebrisMesh (all UVs and material slots), every piece is
    /// drawn with the source's whole material array, and nothing hidden under a model is a source.</summary>
    [Fact]
    public void TheBreakUpCutsOnlyWhatIsDrawnAndKeepsItsPaint()
    {
        string visual = File.ReadAllText(Path.Combine(Bridge, "AirframeVisual.cs"));
        Assert.DoesNotContain("CloneMesh(", visual);
        Assert.DoesNotContain("mr.sharedMaterial :", visual);
        Assert.Contains("AddPiece(Transform parent, string name, Mesh mesh, Material[] mats", visual);
        Assert.Contains("_builder.IsDebrisSource(mf)", visual);
        Assert.Contains("TakePrebuiltDebris(comp, debris.transform)", visual);
        Assert.Contains("PlacedModelPath + \"-debris\"", visual);
        string models = File.ReadAllText(Path.Combine(Bridge, "AirframeModels.cs"));
        Assert.Contains("Resources.Load<GameObject>(ResourcePath(id, own))", models);   // Place and the debris lookup share one path
        string mesh = File.ReadAllText(Path.Combine(Bridge, "DebrisMesh.cs"));
        foreach (string attr in new[] { "SetNormals", "SetTangents", "SetColors", "SetUVs" }) Assert.Contains(attr, mesh);
    }
}
