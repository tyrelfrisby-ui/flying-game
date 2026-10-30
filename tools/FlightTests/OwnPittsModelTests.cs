using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Our own clean-room Pitts S-2B (tools/own_models/build_pitts_s2b.py, Ty 2026-10-27) is THE in-game Pitts, in four
/// liveries: the checkered scheme with the Hartzell 2-blade, unmarked (pitts-s2b-like-own-checker-unmarked.glb, the
/// default since Ty 2026-10-28) or as N31DG (pitts-s2b-like-own.glb), and the factory sunburst with diamonds with the MT
/// 3-blade, unmarked (pitts-s2b-like-own-factory-unmarked.glb) or as N1191 (pitts-s2b-like-own-n1191.glb). The Unity side (AirframeModels / Livery) can't be loaded here, so these hold the files it loads
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
    private static string Glb(string suffix) => Glb("pitts-s2b-like", suffix);
    private static string Glb(string model, string suffix) => Path.Combine(Models, model, model + suffix + ".glb");
    private static readonly string[] PittsSuffixes = { "-own-checker-unmarked", "-own-factory-unmarked", "-own", "-own-n1191" };

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

    /// <summary>Each named node's mesh vertices IN MODEL SPACE (glTF frame: our exports put the nose at -x,
    /// up +y, span z), with every node's own transform and its parents' applied.
    ///
    /// This used to assert that the build baked every transform into the mesh, and for an unrigged model it
    /// did. A RIGGED one cannot: a rig node's whole purpose is to sit at its own pivot with a transform of its
    /// own, so the Extra's v15 cockpit turned that assertion into seven red tests the moment it landed
    /// (2026-10-30) -- not because anything about the aeroplane was wrong, but because the reader was asking
    /// for something a rig is not allowed to give it. So compose the transforms instead of forbidding them,
    /// which is what the callers wanted all along: where each part really is on the aeroplane.</summary>
    internal static Dictionary<string, List<(float x, float y, float z)>> Vertices(string path)
    {
        var (j, bin) = ReadGlb(path);
        var res = new Dictionary<string, List<(float, float, float)>>();
        JsonElement acc = j.GetProperty("accessors"), views = j.GetProperty("bufferViews"), meshes = j.GetProperty("meshes");
        JsonElement nodes = j.GetProperty("nodes");

        // Parent of each node, from the children lists (glTF stores the tree downward only).
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;

        for (int i = 0; i < count; i++)
        {
            JsonElement node = nodes[i];
            if (!node.TryGetProperty("mesh", out JsonElement mi)) continue;
            double[] m = World(nodes, parent, i);
            var list = new List<(float, float, float)>();
            foreach (JsonElement prim in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                JsonElement a = acc[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                JsonElement bv = views[a.GetProperty("bufferView").GetInt32()];
                int o = (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
                int stride = bv.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : 12;
                for (int k = 0; k < a.GetProperty("count").GetInt32(); k++)
                {
                    double x = BitConverter.ToSingle(bin, o + k * stride);
                    double y = BitConverter.ToSingle(bin, o + k * stride + 4);
                    double z = BitConverter.ToSingle(bin, o + k * stride + 8);
                    list.Add(((float)(m[0] * x + m[4] * y + m[8] * z + m[12]),
                              (float)(m[1] * x + m[5] * y + m[9] * z + m[13]),
                              (float)(m[2] * x + m[6] * y + m[10] * z + m[14])));
                }
            }
            res[node.GetProperty("name").GetString()!] = list;
        }
        return res;
    }

    /// <summary>A node's transform all the way up to the scene root, column-major like glTF's own.</summary>
    private static double[] World(JsonElement nodes, int[] parent, int i)
    {
        double[] m = Local(nodes[i]);
        for (int p = parent[i]; p >= 0; p = parent[p]) m = Mul(Local(nodes[p]), m);
        return m;
    }

    /// <summary>One node's own transform: glTF gives either a 16-float matrix or translation / rotation
    /// (quaternion x y z w) / scale, in that order, T * R * S.</summary>
    private static double[] Local(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement mat))
        {
            var m = new double[16];
            for (int i = 0; i < 16; i++) m[i] = mat[i].GetDouble();
            return m;
        }
        double tx = 0, ty = 0, tz = 0, qx = 0, qy = 0, qz = 0, qw = 1, sx = 1, sy = 1, sz = 1;
        if (node.TryGetProperty("translation", out JsonElement t)) { tx = t[0].GetDouble(); ty = t[1].GetDouble(); tz = t[2].GetDouble(); }
        if (node.TryGetProperty("rotation", out JsonElement r)) { qx = r[0].GetDouble(); qy = r[1].GetDouble(); qz = r[2].GetDouble(); qw = r[3].GetDouble(); }
        if (node.TryGetProperty("scale", out JsonElement s)) { sx = s[0].GetDouble(); sy = s[1].GetDouble(); sz = s[2].GetDouble(); }
        double xx = qx * qx, yy = qy * qy, zz = qz * qz, xy = qx * qy, xz = qx * qz, yz = qy * qz, wx = qw * qx, wy = qw * qy, wz = qw * qz;
        return new[]
        {
            (1 - 2 * (yy + zz)) * sx, (2 * (xy + wz)) * sx,     (2 * (xz - wy)) * sx,     0,
            (2 * (xy - wz)) * sy,     (1 - 2 * (xx + zz)) * sy, (2 * (yz + wx)) * sy,     0,
            (2 * (xz + wy)) * sz,     (2 * (yz - wx)) * sz,     (1 - 2 * (xx + yy)) * sz, 0,
            tx, ty, tz, 1,
        };
    }

    private static double[] Mul(double[] a, double[] b)
    {
        var m = new double[16];
        for (int c = 0; c < 4; c++)
            for (int r = 0; r < 4; r++)
                m[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
        return m;
    }

    private static string Guid(string meta) => Regex.Match(File.ReadAllText(meta), @"^guid: (\w+)", RegexOptions.Multiline).Groups[1].Value;
    private static string ImporterScript(string meta) => Regex.Match(File.ReadAllText(meta), @"script: \{[^}]*guid: (\w+)").Groups[1].Value;

    [Fact]
    public void EveryLiveryShipsAsAnImportedGlb()
    {
        string extraMeta = Path.Combine(Models, "extra-300-like", "extra-300-like-own.glb.meta");
        // Every own Pitts and Extra GLB has its own committed GUID (none may collide with a sibling's, or with each other's).
        var guids = new HashSet<string>();
        var glbs = PittsSuffixes.Select(sfx => Glb(sfx))
            .Concat(new[] { "-own", "-own-n300uy", "-own-unmarked" }.Select(sfx => Glb("extra-300-like", sfx)));
        foreach (string glb in glbs)
        {
            string meta = glb + ".meta";
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
    [InlineData("-own-checker-unmarked")]
    [InlineData("-own-factory-unmarked")]
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

    /// <summary>Each livery is the GLB with its own propeller, as counted in the files (2026-10-28; the unmarked HANDOFF
    /// had the two unmarked Pittses' props the wrong way round): the checkered Pittses, unmarked and N31DG, a Hartzell
    /// 2-blade; the factory ones, unmarked and N1191, an MT 3-blade; every Extra a 3-blade.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", "-own-checker-unmarked", 2)]
    [InlineData("pitts-s2b-like", "-own", 2)]
    [InlineData("pitts-s2b-like", "-own-factory-unmarked", 3)]
    [InlineData("pitts-s2b-like", "-own-n1191", 3)]
    [InlineData("extra-300-like", "-own-unmarked", 3)]
    [InlineData("extra-300-like", "-own", 3)]
    [InlineData("extra-300-like", "-own-n300uy", 3)]
    public void EachLiveryHasItsOwnPropeller(string model, string suffix, int blades)
    {
        var prop = Vertices(Glb(model, suffix))["Prop"];
        // Blade count = runs of occupied 10-degree sectors round the spinner axis (x), well out on the blades.
        float r = prop.Max(p => MathF.Sqrt(p.y * p.y + p.z * p.z));
        var occupied = new bool[36];
        foreach (var p in prop)
            if (MathF.Sqrt(p.y * p.y + p.z * p.z) > 0.6f * r)
                occupied[(int)((MathF.Atan2(p.z, p.y) * 180f / MathF.PI + 360f) % 360f / 10f) % 36] = true;
        int runs = 0;
        for (int i = 0; i < 36; i++) if (occupied[i] && !occupied[(i + 35) % 36]) runs++;
        _o.WriteLine($"{model}{suffix}: {runs} blades, tip radius {r:F3} m");
        Assert.Equal(blades, runs);
    }

    /// <summary>An unmarked livery is its marked sibling with the markings painted out (2026-10-28): node for node the same
    /// mesh, so the gear matched to the models and the rig bindings hold for it too.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", "-own-checker-unmarked", "-own")]
    [InlineData("pitts-s2b-like", "-own-factory-unmarked", "-own-n1191")]
    [InlineData("extra-300-like", "-own-unmarked", "-own-n300uy")]
    public void UnmarkedIsTheSameAirframeAsItsMarkedSibling(string model, string unmarked, string marked)
    {
        var a = Vertices(Glb(model, unmarked));
        var b = Vertices(Glb(model, marked));
        Assert.Equal(b.Keys.OrderBy(k => k), a.Keys.OrderBy(k => k));
        foreach (var (name, verts) in b)
            Assert.True(verts.SequenceEqual(a[name]), $"{model}{unmarked}: '{name}' differs from {marked}");
    }

    /// <summary>The livery → GLB mapping the game is written with (AirframeModels.OwnSuffix → Core LiveryChoice.OwnSuffix):
    /// every Pitts and Extra livery the picker offers loads its own GLB, which is on disk, the Pitts always takes our model,
    /// and Livery.cs resolves every scheme id the picker offers to a scheme of that name.</summary>
    [Fact]
    public void LiveryIdsMapOntoGlbsThatExist()
    {
        string models = File.ReadAllText(Path.Combine(Bridge, "AirframeModels.cs"));
        Assert.Contains("FlyingGame.Core.LiveryChoice.OwnSuffix(modelId, liveryId)", models);
        var expected = new Dictionary<(string, string), string>
        {
            [("pitts-s2b-like", "checker-unmarked")] = "-own-checker-unmarked",
            [("pitts-s2b-like", "factory-unmarked")] = "-own-factory-unmarked",
            [("pitts-s2b-like", "n31dg")] = "-own",
            [("pitts-s2b-like", "n1191")] = "-own-n1191",
            [("extra-300-like", "unmarked")] = "-own-unmarked",
            [("extra-300-like", "talk-flying")] = "-own",
            [("extra-300-like", "n300uy")] = "-own-n300uy",
        };
        string liveries = File.ReadAllText(Path.Combine(Bridge, "Livery.cs"));
        foreach (string model in new[] { "pitts-s2b-like", "extra-300-like" })
            foreach (var (livery, name) in FlyingGame.Core.LiveryChoice.For(model))
            {
                string suffix = FlyingGame.Core.LiveryChoice.OwnSuffix(model, livery);
                Assert.Equal(expected[(model, livery)], suffix);
                Assert.True(File.Exists(Glb(model, suffix)), $"livery {livery} of {model} asks for {model}{suffix}.glb, which is not there");
                // Livery.For resolves the id to a scheme of the picker's name (else the picker would show "Type colours").
                if (livery == "talk-flying") continue;   // the Extra's Talk Flying is its PerType scheme
                var m = Regex.Match(liveries, $@"liveryId == ""{livery}"" && aircraftId == ""{model}""\) return (\w+);");
                Assert.True(m.Success, $"Livery.For has no scheme for {model} {livery}");
                Assert.Matches($@"{m.Groups[1].Value} = new\(""{livery}"", ""{name}""\)", liveries);
            }
        Assert.Matches(@"PickOwn\(string modelId\) =>[\s\S]*?modelId == ""pitts-s2b-like""", models);
        Assert.Matches(@"OwnSpecs[\s\S]*?\{ ""pitts-s2b-like"", new Spec\(", models);
    }

    [Fact]
    public void CreditedAsOursWithTheFont()
    {
        string credits = File.ReadAllText(Path.Combine(Repo, "docs", "CREDITS.md"));
        string line = credits.Split('\n').SkipWhile(l => !l.StartsWith("- **pitts-s2b-like**")).Take(2).Aggregate("", (a, b) => a + b + "\n");
        foreach (string sfx in PittsSuffixes) Assert.Contains("pitts-s2b-like" + sfx + ".glb", line);
        string extra = credits.Split('\n').First(l => l.StartsWith("- **extra-300-like**"));
        foreach (string sfx in new[] { "-own", "-own-n300uy", "-own-unmarked" }) Assert.Contains("extra-300-like" + sfx + ".glb", extra);
        Assert.Contains("build_pitts_s2b.py", line);
        Assert.Contains("SIL Open Font License 1.1", line);
        Assert.True(File.Exists(Path.Combine(Repo, "docs", "models", "pitts-s2b-own-PROVENANCE.md")));
    }
}
