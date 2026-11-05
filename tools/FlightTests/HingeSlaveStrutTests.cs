using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 19:16 (Quest): the Extra's 30 % aileron hinge was missing, and the Great Lakes' aileron slave struts
/// stayed "stuck in space" while the ailerons moved. Both are fixed in what every platform ships from the same files:
/// the Extra's GLBs + docs/models/ea300-own-rig.json (mirrored into StreamingAssets/rigs by ConfigSync at every
/// build), and the Great Lakes' GLB + rig table driven by AirframeRig, whose strut follower is <see cref="SlaveStrut"/>.
///
/// The Quest build that showed the old hinge was made before the hinge commit (8a5737c9) reached the Quest lane, and
/// that lane's gitignored StreamingAssets/rigs still held the leading-edge pivot (x 0.8267).
/// <see cref="TheStreamingRigCopyIsNotStale"/> fails while a local copy disagrees with docs/models.
/// </summary>
public class HingeSlaveStrutTests
{
    private readonly ITestOutputHelper _o;
    public HingeSlaveStrutTests(ITestOutputHelper o) { _o = o; }

    private static string Docs(string file) => Path.Combine(OwnPittsModelTests.Repo, "docs", "models", file);
    private static string Model(string folder, string file) => Path.Combine(OwnPittsModelTests.Models, folder, file + ".glb");

    // Copied by hand from 8a5737c9's rig table (30 % chord line), NOT read from the file under test. The old
    // leading-edge pivot was x 0.8267, y 0.0749.
    private static readonly Vec3 ExtraPivotL = new(0.91266, 0.07489, 1.01947), ExtraPivotR = new(0.91266, 0.07489, -1.01947);

    private static Vec3 V(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());

    private static Dictionary<string, JsonElement> RigRows(string file)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
        var rows = new Dictionary<string, JsonElement>();
        foreach (JsonElement r in doc.RootElement.GetProperty("rig").EnumerateArray())
        {
            string n = r.GetProperty("node").GetString()!;
            if (!rows.ContainsKey(n)) rows[n] = r.Clone();
        }
        return rows;
    }

    private static RigNode Node(JsonElement row)
    {
        var mp = new List<double>(); var mv = new List<double>();
        foreach (JsonElement pt in row.GetProperty("map").EnumerateArray()) { mp.Add(pt[0].GetDouble()); mv.Add(pt[1].GetDouble()); }
        JsonElement a = row.GetProperty("axis_gltf"), p = row.GetProperty("pivot_gltf");
        return new RigNode(row.GetProperty("node").GetString()!, "", row.GetProperty("param").GetString()!, false,
                           a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble(),
                           mp.ToArray(), mv.ToArray(), 0.0, false, null, 0.0, "");
    }

    /// <summary>Each named node's origin in the model frame, and its parent's name.</summary>
    private static Dictionary<string, (Vec3 at, string parent)> Origins(string path)
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes");
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        var res = new Dictionary<string, (Vec3, string)>();
        for (int i = 0; i < count; i++)
        {
            if (!nodes[i].TryGetProperty("name", out JsonElement nm)) continue;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            res[nm.GetString()!] = (new Vec3(m[12], m[13], m[14]), parent[i] >= 0 ? nodes[parent[i]].GetProperty("name").GetString()! : "");
        }
        return res;
    }

    private static double Dist(Vec3 a, Vec3 b) => (a - b).Length;

    // ---- the Extra: hinge line at 30 % of the aileron chord ----------------------------------------------------

    [Fact]
    public void TheExtrasRigPutsTheAileronHingeOnTheThirtyPercentLine()
    {
        Dictionary<string, JsonElement> rows = RigRows(Docs("ea300-own-rig.json"));
        Assert.True(Dist(V(rows["Aileron_L"].GetProperty("pivot_gltf")), ExtraPivotL) < 0.001, "Aileron_L pivot moved off the 30 % line");
        Assert.True(Dist(V(rows["Aileron_R"].GetProperty("pivot_gltf")), ExtraPivotR) < 0.001, "Aileron_R pivot moved off the 30 % line");
        // ...and not the leading edge it used to be on (a stale table would put the nose back flush).
        Assert.True(V(rows["Aileron_L"].GetProperty("pivot_gltf")).X - 0.8267 > 0.05);
    }

    [Theory]
    [InlineData("extra-300-like-own")]
    [InlineData("extra-300-like-own-unmarked")]
    [InlineData("extra-300-like-own-n300uy")]
    public void EveryExtraGlbHingesItsAileronsAtThirtyPercentChord(string glb)
    {
        Dictionary<string, JsonElement> rows = RigRows(Docs("ea300-own-rig.json"));
        Dictionary<string, List<(float x, float y, float z)>> verts = OwnPittsModelTests.Vertices(Model("extra-300-like", glb));
        Dictionary<string, (Vec3 at, string parent)> org = Origins(Model("extra-300-like", glb));
        foreach (string s in new[] { "L", "R" })
        {
            string n = "Aileron_" + s;
            Vec3 pivot = V(rows[n].GetProperty("pivot_gltf")), axis = V(rows[n].GetProperty("axis_gltf"));
            axis = axis / axis.Length;
            // The mesh turns about its node origin, so that must be the rig's pivot, and the build's own hinge
            // marker must lie on the rig's axis.
            Assert.True(Dist(org[n].at, pivot) < 0.001, $"{glb} {n}: node origin {org[n].at} is not the rig pivot {pivot}");
            Vec3 tip = org[n + "_HingeAxis"].at - pivot;
            Assert.True(Vec3.Cross(tip / tip.Length, axis).Length < 0.002, $"{glb} {n}: HingeAxis marker is off the rig axis");

            // The aileron's skin (y > 0 leaves out the horn and pushrod below it), sliced along the span. The chord
            // is the streamwise extent of each slice: nose at min x, trailing edge at max x.
            var skin = verts[n].Where(v => v.y > 0f).ToList();
            double z0 = Math.Min(pivot.Z, org[n + "_HingeAxis"].at.Z), z1 = Math.Max(pivot.Z, org[n + "_HingeAxis"].at.Z);
            var fr = new List<double>();
            for (int k = 1; k <= 7; k++)
            {
                double zc = z0 + (z1 - z0) * k / 8.0;
                var sl = skin.Where(v => Math.Abs(v.z - zc) < 0.08).ToList();
                Assert.True(sl.Count > 10, $"{glb} {n}: no skin at z {zc:F2}");
                double le = sl.Min(v => v.x), te = sl.Max(v => v.x);
                double hx = (pivot + axis * ((zc - pivot.Z) / axis.Z)).X;
                fr.Add((hx - le) / (te - le));
            }
            _o.WriteLine($"{glb} {n}: hinge chord fraction {string.Join(" ", fr.Select(f => f.ToString("F3")))}");
            Assert.All(fr, f => Assert.InRange(f, 0.24, 0.36));
            Assert.InRange(fr.Average(), 0.28, 0.32);
        }
    }

    [Fact]
    public void TheExtrasAileronNoseRisesProudWhenTheTrailingEdgeGoesDown()
    {
        Dictionary<string, JsonElement> rows = RigRows(Docs("ea300-own-rig.json"));
        var verts = OwnPittsModelTests.Vertices(Model("extra-300-like", "extra-300-like-own"));
        foreach ((string s, double stick) in new[] { ("L", 1.0), ("R", -1.0) })   // full right roll = left TE down
        {
            string n = "Aileron_" + s;
            RigNode rig = Node(rows[n]);
            Vec3 pivot = new(rig.Px, rig.Py, rig.Pz), axis = new(rig.Ax, rig.Ay, rig.Az);
            double deg = rig.Value(stick);
            var skin = verts[n].Where(v => v.y > 0f).Select(v => new Vec3(v.x, v.y, v.z)).ToList();
            var moved = skin.Select(p => SlaveStrut.AboutHinge(p, pivot, axis, deg)).ToList();
            var nose = Enumerable.Range(0, skin.Count).Where(i => skin[i].X < pivot.X + (skin[i].Z - pivot.Z) * axis.X / axis.Z).ToList();
            var tail = Enumerable.Range(0, skin.Count).Where(i => !nose.Contains(i)).ToList();
            double noseUp = nose.Max(i => moved[i].Y) - nose.Max(i => skin[i].Y);
            double teDown = tail.Min(i => moved[i].Y) - tail.Min(i => skin[i].Y);
            _o.WriteLine($"{n} at {deg:F0} deg: nose top +{noseUp * 1000:F0} mm, TE {teDown * 1000:F0} mm");
            Assert.True(teDown < -0.05, $"{n}: TE should go down at full travel ({teDown:F3} m)");
            // Hinged at the leading edge the nose cannot rise at all; at 30 % chord it stands well proud.
            Assert.True(noseUp > 0.02, $"{n}: the nose should rise above the skin TE-down ({noseUp:F3} m)");
        }
    }

    [Fact]
    public void TheStreamingRigCopyIsNotStale()
    {
        // StreamingAssets/rigs is gitignored and regenerated by ConfigSync.SyncAndRequireComplete at every build; a
        // merge does not touch it. Where a local copy exists, its aileron pivots must be docs/models' pivots.
        foreach ((string doc, string model, string[] nodes) in new[]
                 {
                     ("ea300-own-rig.json", "extra-300-like-own", new[] { "Aileron_L", "Aileron_R" }),
                     ("great-lakes-own-rig.json", "great-lakes-like-own", new[] { "Aileron_UL_Pivot", "Aileron_UR_Pivot", "Aileron_LL_Pivot", "Aileron_LR_Pivot" }),
                 })
        {
            string sa = Path.Combine(OwnPittsModelTests.Repo, "unity", "Assets", "StreamingAssets", "rigs", model + ".json");
            if (!File.Exists(sa)) { _o.WriteLine($"{sa}: absent (a build regenerates it)"); continue; }
            Dictionary<string, JsonElement> want = RigRows(Docs(doc)), have = RigRows(sa);
            foreach (string n in nodes)
                Assert.True(Dist(V(want[n].GetProperty("pivot_gltf")), V(have[n].GetProperty("pivot_gltf"))) < 1e-6,
                            $"{sa}: {n} pivot is stale against docs/models/{doc} — run ConfigSync (any build does)");
        }
    }

    // ---- the Great Lakes: slave struts follow both ailerons ----------------------------------------------------

    // The legacy -red-sunburst / -white-stripes GLBs are unrigged and no code loads them (see
    // CockpitControlInteriorTests); great-lakes-like-own is the one AirframeModels places.
    [Fact]
    public void TheGreatLakesSlaveStrutsHangOnBothAileronHorns()
    {
        const string glb = "great-lakes-like-own";
        var org = Origins(Model("great-lakes-like", glb));
        foreach (string s in new[] { "L", "R" })
        {
            string n = "Strut_Slave_" + s;
            Assert.Equal("Aileron_U" + s, org[n + "_PinUpper"].parent);
            Assert.Equal("Aileron_L" + s, org[n + "_PinLower"].parent);
            Assert.Equal("Aileron_U" + s + "_Pivot", org["Aileron_U" + s].parent);
            Assert.Equal("Aileron_L" + s + "_Pivot", org["Aileron_L" + s].parent);
            Assert.True(Dist(org[n].at, org[n + "_PinUpper"].at) < 0.001, $"{glb} {n}: origin is not on the upper pin");
        }
    }

    [Fact]
    public void TheGreatLakesSlaveStrutsFollowBothAileronsToFullTravel()
    {
        var org = Origins(Model("great-lakes-like", "great-lakes-like-own"));
        Dictionary<string, JsonElement> rows = RigRows(Docs("great-lakes-own-rig.json"));
        foreach (string s in new[] { "L", "R" })
        {
            RigNode up = Node(rows["Aileron_U" + s + "_Pivot"]), lo = Node(rows["Aileron_L" + s + "_Pivot"]);
            // The pins ride with the ailerons, so they turn about the pivots exactly as the rig turns the surfaces.
            Assert.True(Dist(org["Aileron_U" + s + "_Pivot"].at, new Vec3(up.Px, up.Py, up.Pz)) < 0.001);
            Assert.True(Dist(org["Aileron_L" + s + "_Pivot"].at, new Vec3(lo.Px, lo.Py, lo.Pz)) < 0.001);
            Vec3 pu0 = org["Strut_Slave_" + s + "_PinUpper"].at, pl0 = org["Strut_Slave_" + s + "_PinLower"].at, o0 = org["Strut_Slave_" + s].at;
            foreach (double stick in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
            {
                Vec3 pu = SlaveStrut.AboutHinge(pu0, new Vec3(up.Px, up.Py, up.Pz), new Vec3(up.Ax, up.Ay, up.Az), up.Value(stick));
                Vec3 pl = SlaveStrut.AboutHinge(pl0, new Vec3(lo.Px, lo.Py, lo.Pz), new Vec3(lo.Ax, lo.Ay, lo.Az), lo.Value(stick));
                SlaveStrut.Pose p = SlaveStrut.Solve(pu0, pl0, o0, pu, pl);
                Vec3 upperEnd = p.Origin + p.Turn.Rotate(pu0 - o0), lowerEnd = p.Origin + p.Turn.Rotate(pl0 - o0);
                Vec3 aim = p.Turn.Rotate(pl0 - pu0);
                double offAim = Vec3.Cross(aim / aim.Length, (pl - pu) / (pl - pu).Length).Length;
                _o.WriteLine($"{s} stick {stick:+0.0;-0.0}: upper {up.Value(stick):F1} lower {lo.Value(stick):F1} deg, pins moved {Dist(pu, pu0) * 1000:F0} / {Dist(pl, pl0) * 1000:F0} mm, " +
                             $"upper end off {Dist(upperEnd, pu) * 1000:F2} mm, lower end off {Dist(lowerEnd, pl) * 1000:F2} mm, length error {p.LengthError * 1000:F2} mm");
                Assert.True(Dist(upperEnd, pu) < 1e-6, "the strut's upper end must sit on the upper pin");
                Assert.True(offAim < 1e-6, "the strut must aim at the lower pin");
                Assert.True(Dist(lowerEnd, pl) < 0.003, $"the strut's lower end is {Dist(lowerEnd, pl) * 1000:F1} mm off the lower pin");
                if (Math.Abs(stick) == 1.0)
                {
                    // Not a trivial pass: at full travel both horns really have moved.
                    Assert.True(Dist(pu, pu0) > 0.05 && Dist(pl, pl0) > 0.05, "both pins should move at full aileron");
                    Assert.True(Dist(upperEnd, o0 + (pu0 - o0)) > 0.05, "a strut left at rest would be this far off the pin");
                }
            }
        }
    }

    [Fact]
    public void FromToTurnsOneDirectionOntoAnother()
    {
        var cases = new[] { (new Vec3(1, 0, 0), new Vec3(0, 1, 0)), (new Vec3(0.3, -0.9, 0.2), new Vec3(0.31, -0.88, 0.25)),
                            (new Vec3(0, 0, 1), new Vec3(0, 0, -1)), (new Vec3(0, 2, 0), new Vec3(0, 5, 0)) };
        foreach ((Vec3 a, Vec3 b) in cases)
        {
            Vec3 r = SlaveStrut.FromTo(a, b).Rotate(a / a.Length);
            Assert.True(Dist(r, b / b.Length) < 1e-9, $"{a} -> {b} gave {r}");
        }
        // Right-hand rule: +90 about +z takes +x to +y.
        Assert.True(Dist(SlaveStrut.AboutHinge(new Vec3(2, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 3), 90), new Vec3(1, 1, 0)) < 1e-9);
    }
}
