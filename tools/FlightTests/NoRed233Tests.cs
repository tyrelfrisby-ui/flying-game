using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlyingGame.Core;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE RED 2-33 IS BANNED. Ty removed the red-and-white 2-33 on 2026-11-02, again on 2026-11-03, and on 2026-11-04
/// 18:35: "The red 2-33 is back, delete it permanently, the damn thing won't die." Only the yellow SGS 2-33 (our own
/// glider-2-33-like-own-tf.glb / -own.glb) may ever be drawn, on iOS, the Mac and the Quest.
///
/// How it kept coming back: the red-and-white was the procedural trainer (red fuselage, cream wings) the 2-33's "type"
/// livery drew, plus a Grob Astir download (white, red trim) sitting in the 2-33's Resources folder. The removal
/// (5d028681) reached lane/claude-island-nav only through the 2026-11-04 08:10 parity merge, and 22289144 (the
/// Grob's deletion, the yellow factory tint) was never merged at all. Every lane forked before that still draws the
/// red trainer, and building and installing any of them puts it back on Ty's device; merging such a lane carelessly
/// (taking "theirs" on AirframeVisual / Livery / LiveryChoice / AirframeModels) would bring it back here.
///
/// These checks hold every source the red one ever came from: the catalog (iOS fleet and Quest list), the livery
/// resolution, the GLBs the game loads, the Resources folder, the procedural fallback colours and the factory tint.
/// The Bridge is not compiled by this suite, so its parts are held by reading the source.
/// </summary>
public sealed class NoRed233Tests
{
    private const string G = "glider-2-33-like";

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }

    private static string Bridge(string file) => File.ReadAllText(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", file));

    private static string ModelDir => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models", G);

    /// <summary>A brace-balanced block of C# starting at the first match of <paramref name="start"/>.</summary>
    private static string Block(string src, string start)
    {
        int i = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, $"`{start}` not found — this test reads that block");
        int open = src.IndexOf('{', i), depth = 0;
        for (int k = open; k < src.Length; k++)
        {
            if (src[k] == '{') depth++;
            else if (src[k] == '}' && --depth == 0) return src.Substring(i, k - i + 1);
        }
        throw new InvalidOperationException("unbalanced block after " + start);
    }

    /// <summary>Exactly one 2-33 in every catalog a player picks from, and it is glider-2-33-like.</summary>
    [Fact]
    public void TheCatalogHasOneTwoThirtyThreeOnIosAndQuest()
    {
        static bool Is233(string id) => Regex.IsMatch(id, @"2-?33", RegexOptions.IgnoreCase);

        // Quest: the headset list.
        Assert.Equal(new[] { G }, QuestFleet.Listed.Where(Is233).ToArray());

        // iOS / Mac: SessionSettings.Fleet (the landing page) and the touch controls' aircraft cycle.
        var fleet = Regex.Matches(Block(Bridge("SessionSettings.cs"), "public static readonly (string id, string name)[] MasterFleet"),
            @"\(""([^""]+)"",\s*""([^""]+)""\)").Select(m => (Id: m.Groups[1].Value, Name: m.Groups[2].Value)).ToArray();
        Assert.True(fleet.Length > 20, $"parsed only {fleet.Length} Fleet entries");
        Assert.Equal(new[] { G }, fleet.Where(f => Is233(f.Id) || Is233(f.Name)).Select(f => f.Id).ToArray());
        Assert.DoesNotContain(fleet, f => Regex.IsMatch(f.Name, @"\b(red|white)\b", RegexOptions.IgnoreCase) && Is233(f.Name));

        // Configs, and the generated StreamingAssets copy where a build has written one (it is never committed).
        foreach (string dir in new[] { Path.Combine(Repo, "configs", "aircraft"), Path.Combine(Repo, "unity", "Assets", "StreamingAssets", "aircraft") })
        {
            if (!Directory.Exists(dir)) continue;
            var ids = Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => Is233(n!)).ToArray();
            Assert.Equal(new[] { G }, ids);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, G + ".json")));
            string name = doc.RootElement.GetProperty("displayName").GetString() ?? "";
            Assert.DoesNotMatch(new Regex(@"\b(red|white|cream)\b", RegexOptions.IgnoreCase), name);
            foreach (string key in new[] { "livery", "colour", "color", "paint" })
                Assert.False(doc.RootElement.TryGetProperty(key, out _), $"{dir}/{G}.json carries a \"{key}\": the 2-33's paint is its GLB");
        }
    }

    /// <summary>Whatever is saved, overridden or sent: the 2-33 resolves to one of its two yellow GLBs.</summary>
    [Fact]
    public void EveryLiveryResolvesToAYellowGlb()
    {
        Assert.False(LiveryChoice.HasChoice(G));
        string?[] ids = { null, "", "type", "factory", "talk-flying", "red", "red-white", "trainer", "unmarked", "bogus" };
        foreach (string? env in ids)
            foreach (string? saved in ids)
            {
                string r = LiveryChoice.Resolve(G, env, saved);
                Assert.Contains(r, new[] { "talk-flying", "factory" });
                string suffix = LiveryChoice.OwnSuffix(G, r);
                Assert.Contains(suffix, new[] { "-own-tf", "-own" });
                Assert.True(File.Exists(Path.Combine(ModelDir, G + suffix + ".glb")), $"{G}{suffix}.glb missing: the game would fall back");
            }
    }

    /// <summary>The 2-33's Resources folder holds our two yellow GLBs and nothing else: no Grob Astir, no DefaultWhite
    /// textures, no downloaded model of any kind for the type, and no 2-33 or Grob Astir file anywhere else in Resources.
    /// "astir", not "grob": the Grob G 103 Twin II Acro (glider-grob103-acro-like, 2026-11-01) is its own type and
    /// legitimately lives in Resources; only the Astir was ever a 2-33 stand-in.</summary>
    [Fact]
    public void ResourcesHoldOnlyTheYellowGlbs()
    {
        var files = Directory.GetFiles(ModelDir).Select(Path.GetFileName).Where(f => !f!.EndsWith(".meta")).OrderBy(f => f).ToArray();
        Assert.Equal(new[] { G + "-own-tf.glb", G + "-own.glb" }, files);
        var stray = Directory.GetFiles(Path.Combine(Repo, "unity", "Assets", "_Project", "Resources"), "*", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"astir|glider-2-33-like(?!-own|\.meta$)", RegexOptions.IgnoreCase)).ToArray();
        Assert.Empty(stray);
        Assert.DoesNotContain("grob-astir-like", File.ReadAllText(Path.Combine(Repo, "tools", "talkflying_livery.py")));
    }

    /// <summary>The GLBs' skin: every flat-coloured airframe surface is SGS yellow and the fuselage and wing are the
    /// painted SGS textures. No red, no white, no cream on the outside of the aeroplane.</summary>
    [Fact]
    public void TheGlbsAreYellow()
    {
        string[] skin = { "Fuselage", "Wing", "Aileron", "Stab", "Elevator", "Fin", "Rudder", "Spoiler", "Struts" };
        foreach (string suffix in new[] { "-own-tf", "-own" })
        {
            byte[] b = File.ReadAllBytes(Path.Combine(ModelDir, G + suffix + ".glb"));
            int len = BitConverter.ToInt32(b, 12);
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(b, 20, len));
            var root = doc.RootElement;
            var mats = root.GetProperty("materials");
            var meshes = root.GetProperty("meshes");
            int checkedParts = 0;
            foreach (var node in root.GetProperty("nodes").EnumerateArray())
            {
                string name = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!node.TryGetProperty("mesh", out var mi) || !skin.Any(p => name.StartsWith(p))) continue;
                foreach (var prim in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if (!prim.TryGetProperty("material", out var mat)) continue;
                    var m = mats[mat.GetInt32()];
                    string mname = m.GetProperty("name").GetString() ?? "";
                    var pbr = m.TryGetProperty("pbrMetallicRoughness", out var p) ? p : default;
                    bool textured = pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("baseColorTexture", out _);
                    if (textured) { Assert.StartsWith("Paint_sgs", mname); checkedParts++; continue; }
                    double[] c = pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("baseColorFactor", out var f)
                        ? f.EnumerateArray().Select(x => x.GetDouble()).ToArray() : new[] { 1.0, 1, 1, 1 };
                    // Yellow: red channel high, green well up, blue low. Red (g low) and white/cream (b high) both fail.
                    Assert.True(c[0] > 0.8 && c[1] > 0.4 && c[2] < 0.25, $"{G}{suffix}.glb {name} ({mname}) is not SGS yellow: [{string.Join(", ", c)}]");
                    checkedParts++;
                }
            }
            Assert.True(checkedParts >= 12, $"{G}{suffix}.glb: only {checkedParts} skin primitives found");
        }
    }

    /// <summary>The code paths that drew the red-and-white one, on every platform (none of them is behind a platform
    /// #if): the 2-33 always picks our GLB, there is no downloaded 2-33, the procedural fallback is yellow, and the
    /// factory livery and its flat recolour are yellow.</summary>
    [Fact]
    public void TheFallbacksAreYellow()
    {
        string models = Bridge("AirframeModels.cs");
        // PickOwn: ours always, for every livery (the red trainer was the "type" livery's fallback). The list lives in
        // Core since 2026-11-04 (LiveryChoice.OwnModelAlways), and PickOwn must still read it.
        Assert.True(LiveryChoice.OwnModelAlways(G), "the 2-33 must always draw our own (yellow) model");
        string pick = models.Substring(models.IndexOf("static bool PickOwn", StringComparison.Ordinal), 600);
        Assert.Contains("LiveryChoice.OwnModelAlways(modelId)", pick);
        // No downloaded 2-33 (the Grob Astir) in the downloaded-model table.
        string specs = Block(models, "public static readonly Dictionary<string, Spec> Specs");
        Assert.DoesNotMatch(new Regex(@"^\s*\{\s*""glider-2-33-like""", RegexOptions.Multiline), specs);

        string visual = Bridge("AirframeVisual.cs");
        // The procedural 2-33 (the switch default) — the red-and-white trainer itself until 2026-11-02.
        int d = visual.IndexOf("default: // glider-2-33-like", StringComparison.Ordinal);
        Assert.True(d >= 0, "the 2-33's procedural style (StyleFor default) moved — this test reads it");
        string fallback = visual.Substring(d, visual.IndexOf("};", d, StringComparison.Ordinal) - d);
        Assert.Contains("Fuselage = sgsYellow, Wing = sgsYellow, TailColor = sgsYellow, Control = sgsYellow", fallback);
        Assert.DoesNotMatch(new Regex(@"=\s*(red|cream|white)\b"), fallback);
        // The flat recolour of a not-pre-painted 2-33 GLB: yellow all over (it painted the fuselage white).
        string recolour = Block(visual, "if (cfg.Id == \"glider-2-33-like\")");
        Assert.DoesNotMatch(new Regex(@"Color\.(white|red)|\bred\b\s*[,;)]"), recolour);

        string livery = Bridge("Livery.cs");
        Assert.Matches(new Regex(@"Factory233 = new\(""factory"", ""Schweizer factory"",\s*SgsYellow, SgsYellow, SgsYellow, SgsYellow\)"), livery);
        Assert.Matches(new Regex(@"\[""glider-2-33-like""\] = new\(""talk-flying"", ""Talk Flying"", SgsYellow, SgsYellow, SgsYellow, SgsYellow"), livery);
    }
}
