using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The landing page's livery picker (Ty 2026-10-28): which types get one, which scheme each resolves to (AERO_LIVERY over
/// the player's saved choice over the default), that one type's choice never leaks into another's, and that every choice
/// loads its own GLB. The resolution is Core (LiveryChoice) so it is held here directly; the Unity side that keeps the
/// choice in PlayerPrefs and draws the buttons is held by its source.
/// </summary>
public class LiveryPickerTests
{
    private const string Pitts = "pitts-s2b-like", Extra = "extra-300-like";

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }
    private static string Bridge => Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge");
    private static string Models => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models");

    [Fact]
    public void OnlyThePittsAndTheExtraOfferAChoice_DefaultFirst()
    {
        // Ty 2026-10-28: the unmarked schemes are the defaults; the registered aeroplanes stay selectable.
        Assert.Equal(new[] { "checker-unmarked", "factory-unmarked", "n31dg", "n1191" }, LiveryChoice.For(Pitts).Select(o => o.Id));
        Assert.Equal(new[] { "Checkered", "Factory", "N31DG", "N1191" }, LiveryChoice.For(Pitts).Select(o => o.Name));
        Assert.Equal(new[] { "unmarked", "talk-flying", "n300uy" }, LiveryChoice.For(Extra).Select(o => o.Id));
        Assert.Equal(new[] { "Unmarked", "Talk Flying", "N300UY" }, LiveryChoice.For(Extra).Select(o => o.Name));
        Assert.Equal("checker-unmarked", LiveryChoice.Default(Pitts));
        Assert.Equal("unmarked", LiveryChoice.Default(Extra));
        Assert.True(LiveryChoice.HasChoice(Pitts));
        Assert.True(LiveryChoice.HasChoice(Extra));
        foreach (string id in new[] { "glider-2-33-like", "c172-like", "pa18-cub-like", "decathlon-8kcab-like", "boeing-737-like" })
        {
            Assert.False(LiveryChoice.HasChoice(id), id);
            Assert.Empty(LiveryChoice.For(id));
            Assert.Null(LiveryChoice.Selected(id, "type"));
            Assert.Null(LiveryChoice.Default(id));
        }
        Assert.False(LiveryChoice.HasChoice(null));
    }

    [Fact]
    public void EnvironmentOverrideWinsOverTheSavedChoiceWhichWinsOverTheDefault()
    {
        // Nothing saved, no override: the type's default, the unmarked scheme (Pitts checkered, Extra unmarked).
        Assert.Equal("checker-unmarked", LiveryChoice.Resolve(Pitts, null, null));
        Assert.Equal("checker-unmarked", LiveryChoice.Resolve(Pitts, "", ""));
        Assert.Equal("unmarked", LiveryChoice.Resolve(Extra, null, null));
        Assert.Equal("-own-checker-unmarked", LiveryChoice.OwnSuffix(Pitts, LiveryChoice.Resolve(Pitts, null, null)));
        Assert.Equal("-own-unmarked", LiveryChoice.OwnSuffix(Extra, LiveryChoice.Resolve(Extra, null, null)));
        Assert.Equal("checker-unmarked", LiveryChoice.Selected(Pitts, "type"));
        Assert.Equal("unmarked", LiveryChoice.Selected(Extra, "type"));
        // Saved, including every id a player could have saved before the unmarked schemes existed.
        foreach (string id in new[] { "checker-unmarked", "factory-unmarked", "n31dg", "n1191" })
        {
            Assert.Equal(id, LiveryChoice.Resolve(Pitts, null, id));
            Assert.Equal(id, LiveryChoice.Selected(Pitts, id));
        }
        foreach (string id in new[] { "unmarked", "talk-flying", "n300uy" })
        {
            Assert.Equal(id, LiveryChoice.Resolve(Extra, null, id));
            Assert.Equal(id, LiveryChoice.Selected(Extra, id));
        }
        // The override beats the saved choice, whatever it is.
        Assert.Equal("n31dg", LiveryChoice.Resolve(Pitts, "n31dg", "n1191"));
        Assert.Equal("n1191", LiveryChoice.Resolve(Pitts, "n1191", "n31dg"));
        Assert.Equal("talk-flying", LiveryChoice.Resolve(Extra, "talk-flying", "n300uy"));
        Assert.Equal("type", LiveryChoice.Resolve(Pitts, "type", "n1191"));
        // A saved id that is not (or no longer) one of the type's options falls back to the default.
        Assert.Equal("checker-unmarked", LiveryChoice.Resolve(Pitts, null, "n300uy"));
        Assert.Equal("checker-unmarked", LiveryChoice.Resolve(Pitts, null, "unmarked"));
        Assert.Equal("unmarked", LiveryChoice.Resolve(Extra, null, "n1191"));
        Assert.Equal("unmarked", LiveryChoice.Resolve(Extra, null, "checker-unmarked"));
        Assert.Equal("checker-unmarked", LiveryChoice.Resolve(Pitts, null, "bogus"));
        Assert.Equal("unmarked", LiveryChoice.Resolve(Extra, null, "type"));
    }

    [Fact]
    public void EveryOtherTypeIsUnchanged()
    {
        // No picker → nothing saved is ever read; the override still applies to every type, exactly as AERO_LIVERY did.
        foreach (string id in new[] { "glider-2-33-like", "c172-like", "pa18-cub-like", "stearman-pt17-like" })
        {
            Assert.Equal("type", LiveryChoice.Resolve(id, null, null));
            Assert.Equal("type", LiveryChoice.Resolve(id, null, "talk-flying"));
            Assert.Equal("talk-flying", LiveryChoice.Resolve(id, "talk-flying", null));
        }
        Assert.Equal("factory", LiveryChoice.Resolve("glider-2-33-like", "factory", null));
    }

    [Fact]
    public void EachTypesChoiceIsItsOwn()
    {
        // Two saved choices side by side, as PlayerPrefs holds them: one key per type.
        var prefs = new Dictionary<string, string> { [LiveryChoice.PrefKey(Pitts)] = "n1191" };
        string Saved(string id) => prefs.TryGetValue(LiveryChoice.PrefKey(id), out string? v) ? v : "";
        Assert.NotEqual(LiveryChoice.PrefKey(Pitts), LiveryChoice.PrefKey(Extra));
        Assert.Equal("livery.pitts-s2b-like", LiveryChoice.PrefKey(Pitts));
        Assert.Equal("n1191", LiveryChoice.Resolve(Pitts, null, Saved(Pitts)));
        Assert.Equal("unmarked", LiveryChoice.Resolve(Extra, null, Saved(Extra)));      // choosing N1191 left the Extra alone
        Assert.Equal("-own-unmarked", LiveryChoice.OwnSuffix(Extra, LiveryChoice.Resolve(Extra, null, Saved(Extra))));
        prefs[LiveryChoice.PrefKey(Extra)] = "n300uy";
        Assert.Equal("n1191", LiveryChoice.Resolve(Pitts, null, Saved(Pitts)));        // ...and N300UY leaves the Pitts alone
        Assert.Equal("n300uy", LiveryChoice.Resolve(Extra, null, Saved(Extra)));
        Assert.Equal("type", LiveryChoice.Resolve("c172-like", null, Saved("c172-like")));
    }

    [Fact]
    public void EveryChoiceLoadsItsOwnGlb()
    {
        Assert.Equal("-own-checker-unmarked", LiveryChoice.OwnSuffix(Pitts, "checker-unmarked"));
        Assert.Equal("-own-factory-unmarked", LiveryChoice.OwnSuffix(Pitts, "factory-unmarked"));
        Assert.Equal("-own", LiveryChoice.OwnSuffix(Pitts, "n31dg"));
        Assert.Equal("-own-n1191", LiveryChoice.OwnSuffix(Pitts, "n1191"));
        Assert.Equal("-own-checker-unmarked", LiveryChoice.OwnSuffix(Pitts, "type"));        // "type" = the default
        Assert.Equal("-own-checker-unmarked", LiveryChoice.OwnSuffix(Pitts, null));
        Assert.Equal("-own", LiveryChoice.OwnSuffix(Pitts, "talk-flying"));                  // its Talk Flying scheme was N31DG
        Assert.Equal("-own-unmarked", LiveryChoice.OwnSuffix(Extra, "unmarked"));
        Assert.Equal("-own", LiveryChoice.OwnSuffix(Extra, "talk-flying"));
        Assert.Equal("-own", LiveryChoice.OwnSuffix(Extra, "sunrise"));
        Assert.Equal("-own-n300uy", LiveryChoice.OwnSuffix(Extra, "n300uy"));
        Assert.Equal("-own-unmarked", LiveryChoice.OwnSuffix(Extra, "type"));
        Assert.Equal("-own-unmarked", LiveryChoice.OwnSuffix(Extra, null));
        Assert.Equal("-own-unmarked", LiveryChoice.OwnSuffix(Extra, "n1191"));              // another type's id never crosses over
        Assert.Equal("-own-checker-unmarked", LiveryChoice.OwnSuffix(Pitts, "n300uy"));
        Assert.Equal("-own-checker-unmarked", LiveryChoice.OwnSuffix(Pitts, "unmarked"));
        Assert.Equal("-own-tf", LiveryChoice.OwnSuffix("glider-2-33-like", "talk-flying"));
        Assert.Equal("-own", LiveryChoice.OwnSuffix("glider-2-33-like", "factory"));
        foreach (string id in new[] { Pitts, Extra })
        {
            var suffixes = LiveryChoice.For(id).Select(o => LiveryChoice.OwnSuffix(id, o.Id)).ToList();
            Assert.Equal(suffixes.Count, suffixes.Distinct().Count());                    // every choice its own model
            foreach (string sfx in suffixes)
                Assert.True(File.Exists(Path.Combine(Models, id, id + sfx + ".glb")), $"{id}{sfx}.glb is missing");
        }
    }

    [Fact]
    public void TheGameReadsThePickerThroughOnePlace()
    {
        string session = File.ReadAllText(Path.Combine(Bridge, "SessionSettings.cs"));
        Assert.Contains("GetEnvironmentVariable(\"AERO_LIVERY\")", session);
        Assert.Contains("PlayerPrefs.GetString(LiveryChoice.PrefKey(aircraftId)", session);
        Assert.Contains("PlayerPrefs.SetString(LiveryChoice.PrefKey(aircraftId), liveryId)", session);
        Assert.Contains("LiveryChoice.Resolve(", session);
        string models = File.ReadAllText(Path.Combine(Bridge, "AirframeModels.cs"));
        Assert.Contains("OwnSuffix(modelId, SessionSettings.LiveryFor(modelId))", models);
        string visual = File.ReadAllText(Path.Combine(Bridge, "AirframeVisual.cs"));
        Assert.Contains("Liveries.For(liveryId, cfg.Id)", visual);
        Assert.Contains("_builtLivery != SessionSettings.LiveryFor(_builtId)", visual);   // the airframe follows a change
        string livery = File.ReadAllText(Path.Combine(Bridge, "Livery.cs"));
        Assert.Contains("FlyingGame.Core.LiveryChoice.For(aircraftId)", livery);
        string menu = File.ReadAllText(Path.Combine(Bridge, "StartMenu.cs"));
        Assert.Contains("Liveries.Available(acId)", menu);
        Assert.Contains("if (livs.Count > 1)", menu);
        // One drop-down for the livery row, whatever the type (4 Pitts schemes, 3 Extra): the same shape on every screen.
        Assert.Contains("Dropdown(\"livery\"", menu);
        Assert.Contains("SessionSettings.SetLivery(acId, l.Id)", menu);
        Assert.DoesNotContain("livs.Count <= ", menu);
        // No caller left on the old single fleet-wide livery.
        foreach (string f in Directory.GetFiles(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts"), "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotMatch(@"SessionSettings\.LiveryId\b", File.ReadAllText(f));
    }
}
