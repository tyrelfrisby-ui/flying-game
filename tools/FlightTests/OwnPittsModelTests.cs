using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Our own clean-room Pitts S-2B (tools/own_models/build_pitts_s2b.py, Ty 2026-10-27) is THE in-game Pitts: N31DG
/// (pitts-s2b-like-own.glb, Hartzell 2-blade) for every livery, N1191 (pitts-s2b-like-own-n1191.glb, MT 3-blade) as
/// livery "n1191". The Unity side (AirframeModels / Livery) can't be loaded here, so these hold the files it loads
/// and the mapping it is written with: the GLBs are there, imported, true to size, nose and top where the OwnSpecs
/// entry expects them, carry the named parts the rig binds, and each livery's GLB is the one with its own propeller.
/// </summary>
public class OwnPittsModelTests
{
    private readonly ITestOutputHelper _o;
    public OwnPittsModelTests(ITestOutputHelper o) { _o = o; }

    internal static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }
    internal static string Models => Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", "Models");
    private static string Bridge => Path.Combine(Repo, "unity", "Assets", "_Project", "Scripts", "Bridge");
    private static string Glb(string suffix) => Path.Combine(Models, "pitts-s2b-like", "pitts-s2b-like" + suffix + ".glb");

    /// <summary>A GLB's JSON chunk and binary chunk.</summary>
    internal static (JsonElement json, byte[] bin) ReadGlb(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        Assert.True(BitConverter.ToUInt32(d, 0) == 0x46546C67, path + ": not a GLB");   // "glTF"
        int n = BitConverter.ToInt32(d, 12);
        var json = JsonDocument.Parse(new ReadOnlyMemory<byte>(d, 20, n)).RootElement;
        int off = 20 + n, bl = BitConverter.ToInt32(d, off);
        return (json, d.AsSpan(off + 8, bl).ToArray());
    }

    /// <summary>Each named node's mesh vertices (glTF frame: our exports put the nose at -x, up +y, span z).</summary>
    internal static Dictionary<string, List<(float x, float y, float z)>> Vertices(string path)
    {
        var (j, bin) = ReadGlb(path);
        var res = new Dictionary<string, List<(float, float, float)>>();
        JsonElement acc = j.GetProperty("accessors"), views = j.GetProperty("bufferViews"), meshes = j.GetProperty("meshes");
        foreach (JsonElement node in j.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out JsonElement mi)) continue;
            Assert.False(node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _),
                "the build bakes every transform into the mesh");
            var list = new List<(float, float, float)>();
            foreach (JsonElement prim in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                JsonElement a = acc[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                JsonElement bv = views[a.GetProperty("bufferView").GetInt32()];
                int o = (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
                int stride = bv.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : 12;
                for (int i = 0; i < a.GetProperty("count").GetInt32(); i++)
                    list.Add((BitConverter.ToSingle(bin, o + i * stride), BitConverter.ToSingle(bin, o + i * stride + 4), BitConverter.ToSingle(bin, o + i * stride + 8)));
            }
            res[node.GetProperty("name").GetString()!] = list;
        }
        return res;
    }

    private static string Guid(string meta) => Regex.Match(File.ReadAllText(meta), @"^guid: (\w+)", RegexOptions.Multiline).Groups[1].Value;
    private static string ImporterScript(string meta) => Regex.Match(File.ReadAllText(meta), @"script: \{[^}]*guid: (\w+)").Groups[1].Value;

    [Fact]
    public void BothLiveriesShipAsImportedGlbs()
    {
        string extraMeta = Path.Combine(Models, "extra-300-like", "extra-300-like-own.glb.meta");
        var guids = new HashSet<string>();
        foreach (string suffix in new[] { "-own", "-own-n1191" })
        {
            string glb = Glb(suffix), meta = glb + ".meta";
            Assert.True(File.Exists(glb), glb + " is missing");
            Assert.True(File.Exists(meta), meta + " is missing — Unity would import it under a GUID nobody has committed");
            Assert.True(guids.Add(Guid(meta)), meta + ": duplicate GUID");
            // The same glTFast importer, with the same settings, as the own EA300.
            Assert.Equal(ImporterScript(extraMeta), ImporterScript(meta));
            string Settings(string m) => Regex.Match(File.ReadAllText(m), @"importSettings:[\s\S]*?assetDependencies").Value;
            Assert.Equal(Settings(extraMeta), Settings(meta));
        }
        Assert.True(File.Exists(Path.Combine(Models, "pitts-s2b-like", "LICENSE.txt")));
    }

    [Theory]
    [InlineData("-own")]
    [InlineData("-own-n1191")]
    public void TrueToSizeNoseAndTopWhereTheSpecSaysAndRigPartsNamed(string suffix)
    {
        var v = Vertices(Glb(suffix));
        foreach (string part in new[] { "Aileron_UL", "Aileron_UR", "Aileron_LL", "Aileron_LR", "Elevator_L", "Elevator_R", "Rudder", "Prop", "Spinner", "Wing_Upper", "Wing_Lower" })
            Assert.True(v.ContainsKey(part) && v[part].Count > 0, $"{suffix}: no '{part}' mesh (AirframeVisual.CarveModel binds by these names)");
        var all = v.Where(kv => kv.Key != "Prop").SelectMany(kv => kv.Value).ToList();
        float span = all.Max(p => p.z) - all.Min(p => p.z), length = all.Max(p => p.x) - all.Min(p => p.x);
        _o.WriteLine($"{suffix}: span {span:F3} m, length {length:F3} m");
        Assert.InRange(span, 6.07f, 6.12f);    // Aviat: 6.10 m upper span
        Assert.InRange(length, 5.69f, 5.75f);  // Aviat: 5.71 m (the model's 5.72 runs to the rudder's trailing edge)
        // OwnSpecs reads nose +x, up +y AFTER glTFast's x mirror: in the file the nose is at -x, the fin is up +y.
        Assert.True(v["Spinner"].Max(p => p.x) < v["Rudder"].Min(p => p.x), "the spinner must be at -x, the rudder at +x");
        Assert.True(v["Wing_Upper"].Average(p => p.y) > v["Wing_Lower"].Average(p => p.y) + 0.9, "upper wing above the lower (up +y)");
        Assert.True(v["Rudder"].Max(p => p.y) > 0.5 && v["Wheels"].Min(p => p.y) < -1.2, "fin up, wheels down");
        // Upper span is the model's largest extent, which is what AirframeModels.Place scales to the config.
        Assert.True(span > length);
    }

    /// <summary>Each livery is the GLB with its own propeller: N31DG a Hartzell 2-blade, N1191 an MT 3-blade.</summary>
    [Theory]
    [InlineData("-own", 2)]
    [InlineData("-own-n1191", 3)]
    public void EachLiveryHasItsOwnPropeller(string suffix, int blades)
    {
        var prop = Vertices(Glb(suffix))["Prop"];
        // Blade count = runs of occupied 10-degree sectors round the spinner axis (x), well out on the blades.
        float r = prop.Max(p => MathF.Sqrt(p.y * p.y + p.z * p.z));
        var occupied = new bool[36];
        foreach (var p in prop)
            if (MathF.Sqrt(p.y * p.y + p.z * p.z) > 0.6f * r)
                occupied[(int)((MathF.Atan2(p.z, p.y) * 180f / MathF.PI + 360f) % 360f / 10f) % 36] = true;
        int runs = 0;
        for (int i = 0; i < 36; i++) if (occupied[i] && !occupied[(i + 35) % 36]) runs++;
        _o.WriteLine($"{suffix}: {runs} blades, tip radius {r:F3} m");
        Assert.Equal(blades, runs);
    }

    /// <summary>The livery → GLB mapping the game is written with (AirframeModels.OwnSuffix → Core LiveryChoice.OwnSuffix): every Pitts and Extra suffix it
    /// can ask for has a GLB on disk, the Pitts's "n1191" goes to the N1191 GLB, the Pitts always takes our model, and Livery.cs
    /// resolves both Pitts scheme ids.</summary>
    [Fact]
    public void LiveryIdsMapOntoGlbsThatExist()
    {
        string models = File.ReadAllText(Path.Combine(Bridge, "AirframeModels.cs"));
        string choice = File.ReadAllText(Path.Combine(Repo, "src", "FlyingGame.Core", "LiveryChoice.cs"));   // the mapping lives here
        Assert.Contains("FlyingGame.Core.LiveryChoice.OwnSuffix(modelId, liveryId)", models);
        var pairs = Regex.Matches(choice, @"modelId == ""([\w-]+)"" && liveryId == ""([\w-]+)"" \? ""(-own[\w-]*)""")
            .Select(m => (model: m.Groups[1].Value, livery: m.Groups[2].Value, suffix: m.Groups[3].Value)).ToList();
        Assert.Contains(("pitts-s2b-like", "n1191", "-own-n1191"), pairs);
        Assert.Contains(("extra-300-like", "n300uy", "-own-n300uy"), pairs);
        // (The 2-33's "-own-tf" is not on this branch; PickOwn falls back when a GLB is missing, so only ours are held here.)
        foreach (var (model, livery, suffix) in pairs.Where(p => p.model is "pitts-s2b-like" or "extra-300-like"))
            Assert.True(File.Exists(Path.Combine(Models, model, model + suffix + ".glb")), $"livery {livery} of {model} asks for {model}{suffix}.glb, which is not there");
        Assert.Single(pairs, p => p.model == "pitts-s2b-like");   // every other livery → N31DG (-own)
        Assert.Matches(@"PickOwn\(string modelId\) =>[\s\S]*?modelId == ""pitts-s2b-like""", models);
        Assert.Matches(@"OwnSpecs[\s\S]*?\{ ""pitts-s2b-like"", new Spec\(", models);

        string liveries = File.ReadAllText(Path.Combine(Bridge, "Livery.cs"));
        Assert.Matches(@"liveryId == ""n1191"" && aircraftId == ""pitts-s2b-like""\) return N1191Pitts", liveries);
        Assert.Matches(@"liveryId == ""n31dg"" && aircraftId == ""pitts-s2b-like""\) return N31DGPitts", liveries);
        Assert.Matches(@"\[""pitts-s2b-like""\] = new\[\] \{ \(""n31dg"", ""N31DG""\), \(""n1191"", ""N1191""\) \}", choice);
    }

    [Fact]
    public void CreditedAsOursWithTheFont()
    {
        string credits = File.ReadAllText(Path.Combine(Repo, "docs", "CREDITS.md"));
        string line = credits.Split('\n').SkipWhile(l => !l.StartsWith("- **pitts-s2b-like**")).Take(2).Aggregate("", (a, b) => a + b + "\n");
        Assert.Contains("pitts-s2b-like-own.glb", line);
        Assert.Contains("pitts-s2b-like-own-n1191.glb", line);
        Assert.Contains("build_pitts_s2b.py", line);
        Assert.Contains("SIL Open Font License 1.1", line);
        Assert.True(File.Exists(Path.Combine(Repo, "docs", "models", "pitts-s2b-own-PROVENANCE.md")));
    }
}
