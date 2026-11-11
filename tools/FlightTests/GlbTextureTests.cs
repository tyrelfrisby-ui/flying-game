using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE TEXTURES OUR AEROPLANES WEAR ARE ORDINARY UNITY TEXTURES (Ty 2026-11-10: <i>"Do it - 4x4, keep all
/// liveries"</i>).
///
/// <para>A texture embedded in a GLB is imported by glTFast's <c>ScriptedImporter</c>, so Unity's platform
/// texture pipeline never sees it: there is no compression field in a <c>.glb.meta</c> at all and the pixels
/// ship uncompressed at four bytes each with a full mip chain. That is how build 2026.1105.1550 reached
/// 4.29 GB with 4,024,558,864 B of <c>resources.assets.resS</c> — 93.8 % of it — and was refused by App Store
/// Connect. <c>tools/models/glb_textures.py extract</c> moved every embedded image out into a sidecar texture
/// asset the GLB names by URI, which is glTFast's own documented path for an external texture, and from there
/// <c>ModelTextureFormat</c> gives it ASTC 4x4 on iOS and Android.</para>
///
/// <para>These tests exist because the state they hold is easy to lose by accident: a re-export out of Blender
/// embeds the images again, and so does running any of the offline tools that do surgery on an embedded image
/// (<c>floor_windows.py</c>, <c>glb_visuals.py</c>, <c>instrument_face_style.py</c>,
/// <c>update_engine_faces.py</c>, <c>pitts_cockpit_v2.py</c>, <c>vr_phase2e.py</c>, <c>pilot_body.py</c>). The
/// recovery is one command — <c>python3 tools/models/glb_textures.py extract</c> — but nothing would have said
/// so, and the cost of not noticing is a build four times bigger than it should be.</para>
/// </summary>
public class GlbTextureTests
{
    private readonly ITestOutputHelper _o;
    public GlbTextureTests(ITestOutputHelper o) { _o = o; }

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }
    private static string Models => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models");
    private static string TextureRoot => Path.Combine(Repo, "unity", "Assets", "_Project", "ModelTextures");
    private static string Manifest => Path.Combine(Repo, "docs", "models", "glb-textures.json");

    /// <summary>Exactly the prefix <c>glb_textures.py</c> writes, and the one glTFast resolves. See
    /// <see cref="TheSidecarsAreExactlyThreeLevelsUpFromEveryModelFolder"/> for why it is three.</summary>
    private const string UriPrefix = "../../../ModelTextures/";

    private static IEnumerable<string> Glbs() =>
        Directory.EnumerateFiles(Models, "*.glb", SearchOption.AllDirectories).OrderBy(p => p);

    /// <summary>The JSON chunk of a .glb, without reading the (large) binary chunk into managed memory.</summary>
    private static JsonElement Gltf(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        Assert.Equal(0x46546C67u, br.ReadUInt32());     // "glTF"
        Assert.Equal(2u, br.ReadUInt32());
        br.ReadUInt32();                                 // total length
        uint len = br.ReadUInt32();
        Assert.Equal(0x4E4F534Au, br.ReadUInt32());      // "JSON" is always the first chunk
        return JsonDocument.Parse(Encoding.UTF8.GetString(br.ReadBytes((int)len))).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Images(JsonElement gltf) =>
        gltf.TryGetProperty("images", out var images) ? images.EnumerateArray() : Enumerable.Empty<JsonElement>();

    /// <summary>
    /// THE ONE THAT CATCHES A RE-EXPORT. An embedded image is invisible to the texture pipeline, so one GLB
    /// quietly going back to embedded costs four bytes a pixel in the build and nothing else complains.
    /// </summary>
    [Fact]
    public void NoShippedGlbCarriesAnEmbeddedTexture()
    {
        var bad = new List<string>();
        foreach (string glb in Glbs())
        {
            int embedded = Images(Gltf(glb)).Count(i => i.TryGetProperty("bufferView", out _));
            if (embedded > 0) bad.Add($"{Path.GetRelativePath(Repo, glb)}: {embedded} embedded image(s)");
        }
        Assert.True(bad.Count == 0,
            "These GLBs have textures embedded in them again, so they ship uncompressed at 4 bytes a pixel.\n"
            + "Run: python3 tools/models/glb_textures.py extract\n  " + string.Join("\n  ", bad));
        _o.WriteLine($"{Glbs().Count()} GLB(s), none with an embedded texture");
    }

    /// <summary>Every URI an aeroplane names has to be a file that is actually there, with a .meta beside it —
    /// a missing one is an import error at build time, not a missing pixel at run time.</summary>
    [Fact]
    public void EveryTextureUriPointsAtATrackedSidecar()
    {
        int checkedUris = 0;
        foreach (string glb in Glbs())
        {
            foreach (var image in Images(Gltf(glb)))
            {
                if (!image.TryGetProperty("uri", out var uriElement)) continue;
                string uri = uriElement.GetString()!;
                Assert.StartsWith(UriPrefix, uri, StringComparison.Ordinal);
                string file = Path.Combine(TextureRoot, uri[UriPrefix.Length..].Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(file), $"{Path.GetRelativePath(Repo, glb)} names {uri}, which is not there");
                Assert.True(File.Exists(file + ".meta"), $"{uri} has no .meta; Unity would invent a GUID per machine");
                checkedUris++;
            }
        }
        _o.WriteLine($"{checkedUris} texture reference(s), all resolved");
    }

    /// <summary>
    /// THE ONE THAT BREAKS A BUILD IF SOMEBODY MOVES A FOLDER. glTFast resolves a relative image URI against
    /// the GLB's own directory, walking up one level per leading "..". Three levels up from
    /// <c>Assets/_Project/Resources/Models/&lt;id&gt;</c> is <c>Assets/_Project</c>. A GLB moved anywhere else
    /// for the length of a build — the TestFlight slim hold-out, the one-livery-per-phone hold-out, or any
    /// third one — has to sit at the same depth or its textures resolve to nothing and the import fails.
    /// </summary>
    [Fact]
    public void TheSidecarsAreExactlyThreeLevelsUpFromEveryModelFolder()
    {
        string project = Path.GetFullPath(Path.Combine(Repo, "unity", "Assets", "_Project"));
        foreach (string glb in Glbs())
        {
            string up = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(glb)!, "..", "..", ".."));
            Assert.Equal(project, up);
        }

        string build = File.ReadAllText(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Editor", "BuildScript.cs"));
        foreach (string hold in new[] { "SlimHoldDir", "LiveryHoldDir" })
        {
            Assert.Contains($"{hold} = ", build);
            int at = build.IndexOf($"{hold} = ", StringComparison.Ordinal);
            string line = build[at..build.IndexOf('\n', at)];
            Assert.True(line.Contains("+ \"/Models\""),
                $"{hold} is {line.Trim()} — it must end in a /Models level, or a GLB moved into it is one "
                + "directory too shallow and '../../../ModelTextures/...' resolves outside _Project.");
            _o.WriteLine($"{hold}: {line.Trim()}");
        }
    }

    /// <summary>
    /// Colour must be imported as colour and data as data. glTF already says which is which (base colour and
    /// emissive are sRGB; metallic-roughness, occlusion and normals are linear), and the generated .meta
    /// carries that. Getting it backwards is the classic silent error: a washed-out livery, or lighting that
    /// is subtly wrong everywhere and attributed to the shader.
    /// </summary>
    [Fact]
    public void ColourIsSrgbDataIsLinearAndNormalsAreNormals()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Manifest));
        var textures = doc.RootElement.GetProperty("textures");
        int srgb = 0, linear = 0, normal = 0;
        foreach (var entry in textures.EnumerateObject())
        {
            string meta = Path.Combine(TextureRoot, entry.Name.Replace('/', Path.DirectorySeparatorChar)) + ".meta";
            string text = File.ReadAllText(meta);
            string space = entry.Value.GetProperty("colourSpace").GetString()!;
            bool wantSrgb = space == "srgb";
            Assert.True(text.Contains($"sRGBTexture: {(wantSrgb ? 1 : 0)}"), $"{entry.Name} is {space} but its .meta disagrees");
            Assert.True(text.Contains($"textureType: {(space == "normal" ? 1 : 0)}"), $"{entry.Name} is {space} but its .meta disagrees");
            // Non-power-of-two is everywhere here (4096x3870, 4096x1672, 1275x1650). Unity's default would
            // RESCALE them to the nearest power of two and nobody would see it until the paint looked soft.
            Assert.Contains("nPOTScale: 0", text);
            Assert.Contains("maxTextureSize: 4096", text);
            Assert.Contains("enableMipMap: 1", text);
            if (wantSrgb) srgb++; else if (space == "normal") normal++; else linear++;
        }
        _o.WriteLine($"{srgb} sRGB, {linear} linear, {normal} normal map(s)");
        Assert.True(normal > 0 && srgb > 300);
    }

    /// <summary>The manifest is the record of what was extracted and what each file is for; if it drifts from
    /// the tree, the next person reads a description of textures that are not there.</summary>
    [Fact]
    public void TheManifestDescribesExactlyTheFilesOnDisk()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Manifest));
        var listed = doc.RootElement.GetProperty("textures").EnumerateObject().Select(p => p.Name).ToHashSet();
        var onDisk = Directory.EnumerateFiles(TextureRoot, "*", SearchOption.AllDirectories)
                              .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                              .Select(p => Path.GetRelativePath(TextureRoot, p).Replace(Path.DirectorySeparatorChar, '/'))
                              .ToHashSet();
        Assert.Equal(listed.OrderBy(s => s), onDisk.OrderBy(s => s));
        _o.WriteLine($"{listed.Count} sidecar(s), manifest and tree agree");
    }

    /// <summary>
    /// The decision itself, in the one place that states it. ASTC 4x4 for the phone and the headset is what Ty
    /// chose; RGBA32 for the Mac is deliberate rather than "Automatic", so the desktop's pixels cannot quietly
    /// turn into DXT; and normal maps stay uncompressed because they measured 25.2 dB where the paint measured
    /// 70+. A quiet edit to any of those three should fail here and be argued for on purpose.
    /// </summary>
    [Fact]
    public void ThePhoneAndTheHeadsetGetAstc4x4AndTheMacIsLeftAlone()
    {
        string rule = File.ReadAllText(Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Editor", "ModelTextureFormat.cs"));
        Assert.Contains("TextureImporterFormat.ASTC_4x4", rule);
        Assert.Contains("NamedBuildTarget.iOS.TargetName", rule);
        Assert.Contains("NamedBuildTarget.Android.TargetName", rule);
        Assert.Contains("NamedBuildTarget.Standalone.TargetName", rule);
        Assert.Contains("Set(importer, Standalone, TextureImporterFormat.RGBA32)", rule);
        Assert.Contains("TextureImporterType.NormalMap", rule);
        Assert.DoesNotContain("ASTC_6x6", rule);     // not authorised; 29.7 dB against 4x4's 43.9 when measured
        Assert.DoesNotContain("crunchedCompression = true", rule);
    }
}
