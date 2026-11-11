using System.Text.Json;
using System.Text.RegularExpressions;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// ONE LIVERY PER TYPE ON THE PHONE (Ty 2026-11-10: "ship a single default livery for each aeroplane; the alternates
/// stay in desktop and Quest builds"). These hold the packaging honest: the file the phone keeps is the file the phone
/// loads, nothing but an alternate livery is ever held out, Mac and Quest are untouched, and no build has left files
/// out of the tree. <see cref="PhoneLiveries"/> has the measurement.
/// </summary>
public class PhoneLiveryTests
{
    private readonly ITestOutputHelper _o;
    public PhoneLiveryTests(ITestOutputHelper o) { _o = o; }

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }
    private static string Models => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models");

    /// <summary>
    /// THE ONE THAT WOULD SHIP A BUILD WITH NO AEROPLANE IN IT. The packager runs in the editor, where the scripting
    /// define is OFF, so LiveryChoice.Resolve returns the type's first option; the player runs with the define ON, so
    /// the picker has one entry, HasChoice is false and Resolve returns FleetDefault "type". Those two must map to the
    /// SAME GLB through OwnSuffix, or the build keeps one file and the game asks for another.
    /// </summary>
    [Fact]
    public void TheKeptLiveryIsTheSameWithOrWithoutTheDefine()
    {
        foreach (string id in LiveryChoice.TypesWithOptions)
        {
            string editor = LiveryChoice.OwnSuffix(id, LiveryChoice.Resolve(id, null, null));   // define off: the first option
            string player = LiveryChoice.OwnSuffix(id, LiveryChoice.FleetDefault);              // define on: no choice left
            Assert.Equal(editor, player);
            Assert.Equal(editor, PhoneLiveries.KeptSuffix(id));
            _o.WriteLine($"{id,-26} keeps {id}{editor}.glb");
        }
        // The 2-33 has no picker at all: LiveryChoice.Only pins it to the yellow Talk Flying GLB, with the plain
        // factory yellow reachable only through AERO_LIVERY. The phone keeps the yellow one either way.
        Assert.Equal("-own-tf", PhoneLiveries.KeptSuffix("glider-2-33-like"));
    }

    /// <summary>Every GLB the phone keeps is on disk, and every type with a picker keeps exactly one.</summary>
    [Fact]
    public void EveryTypeKeepsOneLiveryAndItExists()
    {
        foreach (string dir in Directory.GetDirectories(Models))
        {
            string folder = Path.GetFileName(dir);
            string[] own = Directory.GetFiles(dir, folder + "-own*.glb").Select(Path.GetFileName).OrderBy(f => f).ToArray()!;
            if (own.Length == 0) continue;
            string[] kept = own.Where(f => PhoneLiveries.KeepsModelFile(folder, f)).ToArray();
            Assert.True(kept.Length == 1, $"{folder}: keeps {kept.Length} of its own GLBs ({string.Join(", ", kept)}) — expected exactly one");
            Assert.Equal(folder + PhoneLiveries.KeptSuffix(folder) + ".glb", kept[0]);
            _o.WriteLine($"{folder,-26} keeps {kept[0]}   holds out {own.Length - 1}: {string.Join(", ", own.Where(f => !PhoneLiveries.KeepsModelFile(folder, f)))}");
        }
    }

    /// <summary>NOTHING BUT AN ALTERNATE LIVERY IS EVER HELD OUT. The downloaded model (which most types draw with the
    /// "own models" option off), the .obj meshes, the loose textures the 172's own GLB reads and every .meta stay.</summary>
    [Fact]
    public void OnlyOurOwnAlternateLiveryGlbsAreHeldOut()
    {
        int held = 0, keptNonGlb = 0;
        foreach (string dir in Directory.GetDirectories(Models))
        {
            string folder = Path.GetFileName(dir);
            foreach (string path in Directory.GetFiles(dir))
            {
                string file = Path.GetFileName(path);
                bool keeps = PhoneLiveries.KeepsModelFile(folder, file);
                if (!file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) { Assert.True(keeps, $"{folder}/{file} is not a GLB and must ship"); keptNonGlb++; continue; }
                if (keeps) continue;
                Assert.StartsWith(folder + "-own", Path.GetFileNameWithoutExtension(file), StringComparison.Ordinal);
                held++;
            }
            // The downloaded model always ships: it is what the game draws unless "own models" is on.
            string downloaded = folder + ".glb";
            if (File.Exists(Path.Combine(dir, downloaded))) Assert.True(PhoneLiveries.KeepsModelFile(folder, downloaded), $"{folder}/{downloaded}");
        }
        Assert.True(keptNonGlb > 50, $"only {keptNonGlb} non-GLB files kept — is Models right?");
        Assert.True(held >= 8, $"only {held} alternate livery GLBs held out — expected the Extra's 2, the Pitts' 3, the Great Lakes' 2, the 2-33's 1 and the Grob's 1");
        _o.WriteLine($"{held} alternate livery GLB(s) held out of a phone build; {keptNonGlb} non-GLB files untouched");
    }

    /// <summary>
    /// WHAT IT SAVES — AND IT IS NO LONGER TWO GIGABYTES. When this policy landed on 2026-11-10 the alternates were
    /// 2,133 MB of a 4,292 MB build, because every texture was embedded in its GLB, glTFast imported it as a
    /// sub-asset where Unity's texture pipeline could not reach it, and so each alternate livery carried a FULL
    /// DUPLICATE of its aeroplane's texture set, uncompressed at four bytes a pixel.
    ///
    /// <para>Later the same day the textures moved out into ordinary texture assets
    /// (<c>tools/models/glb_textures.py</c>, Ty: <i>"Do it - 4x4, keep all liveries"</i>) and both halves of that
    /// number collapsed: the duplicates are now ONE file that several liveries share, and what is left is ASTC 4x4
    /// at 8 bits a pixel instead of 32. An alternate livery costs only the paint that is genuinely different.</para>
    ///
    /// <para>So this test now measures the thing it is actually for: a phone build with EVERY livery is far enough
    /// under 4 GB that the restriction is a choice rather than a necessity. It is deliberately still asserted from
    /// the files rather than from a remembered number, so that adding a fourth Extra livery moves it and says so.
    /// Whether the restriction stays on is Ty's, not the test's.</para>
    /// </summary>
    [Fact]
    public void EveryLiveryNowFitsOnThePhoneAndTheRestrictionIsAChoice()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "docs", "models", "glb-textures.json")));
        double everyLivery = 0, oneLiveryPerType = 0;
        foreach (var entry in doc.RootElement.GetProperty("textures").EnumerateObject())
        {
            var t = entry.Value;
            var usedBy = t.GetProperty("materialUsedBy").EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (usedBy.Length == 0) continue;          // in no material: outside Resources, it does not ship at all
            // A normal map stays uncompressed (ModelTextureFormat: the pilot's twill measured 25.2 dB under ASTC);
            // everything else is ASTC 4x4. Mip chain adds a third either way.
            double bytesPerPixel = t.GetProperty("colourSpace").GetString() == "normal" ? 4.0 : 1.0;
            double mb = t.GetProperty("width").GetInt32() * (double)t.GetProperty("height").GetInt32()
                        * bytesPerPixel * 4.0 / 3.0 / 1e6;
            everyLivery += mb;
            // Each entry is "<model id>/<file>.glb", which is exactly what KeepsModelFile asks for.
            if (usedBy.Any(g => PhoneLiveries.KeepsModelFile(g[..g.IndexOf('/')], g[(g.IndexOf('/') + 1)..])))
                oneLiveryPerType += mb;
        }
        _o.WriteLine($"iOS textures, ASTC 4x4: {everyLivery:F0} MB with every livery, {oneLiveryPerType:F0} MB with "
                     + $"one per type — the restriction saves {everyLivery - oneLiveryPerType:F0} MB (it saved 2,133 MB "
                     + "when every texture was embedded)");
        // Plus the ~267 MB of everything that is not a texture — meshes, binary, loose files — in the recorded
        // 2026.1105.1550 build. The limit is 4 GB uncompressed.
        Assert.True(everyLivery + 267 < 2000,
            $"a phone build with every livery is {everyLivery + 267:F0} MB; it used to be 4,292 MB");
        Assert.True(everyLivery - oneLiveryPerType < 600,
            $"the restriction is back to saving {everyLivery - oneLiveryPerType:F0} MB — did the textures get "
            + "embedded in the GLBs again? Run: python3 tools/models/glb_textures.py extract");
    }

    /// <summary>Normal builds are untouched: every use of the define sits behind #if, nothing puts it in
    /// ProjectSettings, and no build has left a hold directory in the tree.</summary>
    [Fact]
    public void NormalBuildsAreUntouchedAndNothingIsHeldOutRightNow()
    {
        Assert.Equal("FG_PHONE_ONE_LIVERY", PhoneLiveries.Define);
        Assert.False(PhoneLiveries.OneLiveryPerType, "the tests are not a phone player");
        Assert.False(Directory.Exists(Path.Combine(Repo, "unity", "Assets", "_Project", "PhoneLiveryHold")),
                     "PhoneLiveryHold exists: a phone build left livery GLBs held out; the next build restores them");
        string settings = File.ReadAllText(Path.Combine(Repo, "unity", "ProjectSettings", "ProjectSettings.asset"));
        Assert.DoesNotContain("FG_PHONE_ONE_LIVERY", settings);
        foreach (string f in Directory.GetFiles(Path.Combine(Repo, "src"), "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts"), "*.cs", SearchOption.AllDirectories)))
        {
            string src = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(src, @"\bFG_PHONE_ONE_LIVERY\b"))
            {
                // either the #if itself, or the constant's own definition in PhoneLiveries
                if (Path.GetFileName(f) == "PhoneLiveries.cs") continue;
                int ifAt = src.LastIndexOf("#if FG_PHONE_ONE_LIVERY", m.Index, StringComparison.Ordinal);
                Assert.True(ifAt >= 0, $"{f}: FG_PHONE_ONE_LIVERY at {m.Index} is outside #if FG_PHONE_ONE_LIVERY");
            }
        }
    }
}
