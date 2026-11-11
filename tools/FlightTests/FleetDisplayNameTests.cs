using System.Text.Json;
using System.Text.RegularExpressions;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE LANDING PAGE'S NAME FOR A TYPE IS ITS CONFIG'S displayName.
///
/// Ty flew build 2026.1111.0948 and the picker said "2-33-like Glider" while
/// <c>configs/aircraft/glider-2-33-like.json</c> had said "Glider Trainer" since the rename that morning. The names
/// came from a hard-coded pair list in <c>SessionSettings.MasterFleet</c> and nothing read the configs, so a rename
/// meant for the public release (aircraft trademark names are a release blocker) never reached a single screen.
///
/// The fix made the config the one source: <see cref="FleetNames.Resolve"/> replaces each compiled name with the
/// config's, and the compiled string survives only as a fallback. These tests are the pin — the first would have
/// failed the day the configs were renamed.
/// </summary>
public sealed class FleetDisplayNameTests
{
    private readonly ITestOutputHelper _o;
    public FleetDisplayNameTests(ITestOutputHelper o) => _o = o;

    private static string Repo
    {
        get
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "configs", "aircraft"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir!;
        }
    }

    private static string SessionSettingsPath =>
        Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", "SessionSettings.cs");

    /// <summary>The master list as the game compiles it: (id, fallback name) in menu order, read from the source.</summary>
    private static (string id, string name)[] MasterFleet()
    {
        string src = File.ReadAllText(SessionSettingsPath);
        const string marker = "public static readonly (string id, string name)[] MasterFleet";
        int a = src.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(a >= 0, "MasterFleet not found in SessionSettings.cs");
        int b = src.IndexOf("};", a, StringComparison.Ordinal);
        var fleet = Regex.Matches(src[a..b], @"\(""([a-z0-9-]+-like)"",\s*""([^""]+)""\)")
                         .Select(m => (id: m.Groups[1].Value, name: m.Groups[2].Value)).ToArray();
        Assert.True(fleet.Length > 20, $"parsed only {fleet.Length} MasterFleet entries — the parse is wrong");
        return fleet;
    }

    /// <summary>The config's displayName, or null when the file or the property is missing.</summary>
    private static string? ConfigDisplayName(string id)
    {
        string path = Path.Combine(Repo, "configs", "aircraft", id + ".json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty("displayName", out JsonElement n) ? n.GetString() : null;
    }

    /// <summary>THE BUG: every offered type's menu name is the one in its config, not the one in the code.</summary>
    [Fact]
    public void EveryTypesMenuNameComesFromItsConfig()
    {
        var master = MasterFleet();
        var resolved = FleetNames.Resolve(master, ConfigDisplayName);

        var noName = new List<string>();
        var differed = new List<string>();
        for (int i = 0; i < master.Length; i++)
        {
            string? want = ConfigDisplayName(master[i].id);
            if (string.IsNullOrWhiteSpace(want)) { noName.Add(master[i].id); continue; }
            Assert.Equal(want!.Trim(), resolved[i].name);
            if (want.Trim() != master[i].name) differed.Add($"{master[i].id}: code \"{master[i].name}\" → config \"{want.Trim()}\"");
        }
        foreach (string d in differed) _o.WriteLine(d);
        _o.WriteLine($"{differed.Count} of {master.Length} types are named something different in their config");

        // No type may fall back: a fallback on screen is a config nobody wrote a name into.
        Assert.True(noName.Count == 0, "no displayName in configs/aircraft for: " + string.Join(", ", noName));

        // The report that found this, spelled out, so the regression is named and not merely implied.
        Assert.Equal("Glider Trainer", resolved.First(f => f.id == "glider-2-33-like").name);
    }

    /// <summary>The resolution cannot be quietly taken out again: the build's fleet is built through FleetNames.</summary>
    [Fact]
    public void TheGamesFleetIsBuiltThroughFleetNames()
    {
        string src = File.ReadAllText(SessionSettingsPath);
        Assert.Contains("FlyingGame.Core.FleetNames.Resolve(offered, UnityAircraftConfigLoader.DisplayName)", src);
        // And the platform cut still happens on the way, unchanged (iOS offers six; see FleetAllowListTests).
        Assert.Contains("FlyingGame.Core.FleetAllowList.Offered(MasterFleet, PlatformAllowList)", src);
    }

    /// <summary>A missing, blank or broken config leaves the compiled name alone — the picker never empties.</summary>
    [Fact]
    public void AMissingOrSilentConfigKeepsTheCompiledName()
    {
        var master = new[] { ("a-like", "A"), ("b-like", "B"), ("c-like", "C"), ("d-like", "D") };
        var resolved = FleetNames.Resolve(master, id => id switch
        {
            "a-like" => "Alpha Trainer",
            "b-like" => null,                 // no config
            "c-like" => "   ",                // a config with a blank name
            _ => throw new IOException("unreadable"),
        });
        Assert.Equal(new[] { "Alpha Trainer", "B", "C", "D" }, resolved.Select(f => f.name));
        Assert.Equal(master.Select(f => f.Item1), resolved.Select(f => f.id));   // ids and order untouched
    }
}
