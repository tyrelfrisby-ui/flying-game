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

    /// <summary>WHAT IT SAVES, measured off the GLBs themselves. Every texture embedded in a GLB ships as uncompressed
    /// RGBA32 with a full mip chain (glTFast imports them as sub-assets, so Unity's platform texture pipeline never
    /// sees them and there is no compression field in the .glb.meta at all), so in-build bytes = pixels x 4 x 4/3.
    /// That model reproduced build 2026.1105.1550's recorded resources.assets.resS to 0.04 %, which is why it is worth
    /// asserting against: if someone compresses the GLB textures or adds a fourth Extra livery, this number moves and
    /// the test says by how much.</summary>
    [Fact]
    public void TheAlternatesAreAboutTwoGigabytesOfTheBuild()
    {
        double keptMb = 0, heldMb = 0;
        foreach (string dir in Directory.GetDirectories(Models))
        {
            string folder = Path.GetFileName(dir);
            foreach (string path in Directory.GetFiles(dir, "*.glb"))
            {
                double mb = GlbTexturePixels(path) * 4.0 * 4.0 / 3.0 / 1e6;
                if (PhoneLiveries.KeepsModelFile(folder, Path.GetFileName(path))) keptMb += mb; else heldMb += mb;
            }
        }
        _o.WriteLine($"GLB textures in a build: {keptMb + heldMb:F0} MB all liveries, {keptMb:F0} MB one per type, {heldMb:F0} MB held out");
        Assert.True(heldMb > 1800 && heldMb < 2500, $"the alternates are {heldMb:F0} MB — the measurement that justified this was 2,132 MB");
        // Plus the ~267 MB of everything that is not a GLB texture (meshes, binary, loose textures) in the same
        // recorded build. The limit is 4 GB uncompressed.
        Assert.True(keptMb + 267 < 4000, $"a phone build is still over 4 GB: {keptMb + 267:F0} MB");
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

    /// <summary>Pixels of every image embedded in a GLB (PNG or JPEG, in the BIN chunk).</summary>
    private static long GlbTexturePixels(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 12 || b[0] != 'g' || b[1] != 'l' || b[2] != 'T' || b[3] != 'F') return 0;
        System.Text.Json.JsonDocument? js = null; int binOff = -1;
        for (int off = 12; off + 8 <= b.Length;)
        {
            int len = BitConverter.ToInt32(b, off), type = BitConverter.ToInt32(b, off + 4);
            if (type == 0x4E4F534A) js = System.Text.Json.JsonDocument.Parse(b.AsMemory(off + 8, len));
            else if (type == 0x004E4942) binOff = off + 8;
            off += 8 + len;
        }
        if (js == null || binOff < 0 || !js.RootElement.TryGetProperty("images", out var images)) return 0;
        var views = js.RootElement.GetProperty("bufferViews");
        long total = 0;
        foreach (var im in images.EnumerateArray())
        {
            if (!im.TryGetProperty("bufferView", out var bv)) continue;
            var view = views[bv.GetInt32()];
            int o = binOff + (view.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0);
            int n = view.GetProperty("byteLength").GetInt32();
            (int w, int h) = PngSize(b, o) ?? JpegSize(b, o, n) ?? (0, 0);
            total += (long)w * h;
        }
        return total;
    }

    private static (int, int)? PngSize(byte[] b, int o)
    {
        if (o + 24 > b.Length || b[o] != 0x89 || b[o + 1] != 'P' || b[o + 2] != 'N' || b[o + 3] != 'G') return null;
        return (Be32(b, o + 16), Be32(b, o + 20));
    }

    private static (int, int)? JpegSize(byte[] b, int o, int n)
    {
        if (o + 4 > b.Length || b[o] != 0xFF || b[o + 1] != 0xD8) return null;
        for (int i = o + 2, end = Math.Min(b.Length - 9, o + n); i < end;)
        {
            if (b[i] != 0xFF) { i++; continue; }
            int m = b[i + 1];
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            int len = (b[i + 2] << 8) | b[i + 3];
            if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
            i += 2 + len;
        }
        return null;
    }

    private static int Be32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
}
