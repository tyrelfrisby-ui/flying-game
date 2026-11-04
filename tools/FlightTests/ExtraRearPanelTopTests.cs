using System.Text.Json;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Extra 300's REAR (PIC) panel top — the hood over the instruments and its dark glareshield cap. P3b
/// (vr_phase3b.py, 3a9e746b, "panel rebuilt on one plane") retired every old Cockpit_Shell panel primitive and took
/// the hood with them, so from build 2046 the rear pilot saw a bare flat panel edge with nothing above it (Ty
/// 2026-11-02: "the top of the Extra's rear instrument panel ... is now GONE"). vr_phase3d.py restores it. These pin
/// that it is there in every livery, the shape and place it had, seated on the P3b panel, drawable (opaque, not
/// default-hidden), in front of the rear eye, and that it hides none of the top-row gauges.
/// </summary>
public class ExtraRearPanelTopTests
{
    private static readonly string[] Liveries = { "-own", "-own-unmarked", "-own-n300uy" };
    private static string Glb(string s) => Path.Combine(OwnPittsModelTests.Models, "extra-300-like", "extra-300-like" + s + ".glb");

    private static (double x, double y, double z) NodeWorld(string path, string name)
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes");
        int n = nodes.GetArrayLength(); var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = -1;
        for (int i = 0; i < n; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement k)) foreach (JsonElement c in k.EnumerateArray()) parent[c.GetInt32()] = i;
        for (int i = 0; i < n; i++)
            if (nodes[i].GetProperty("name").GetString() == name) { double[] m = OwnPittsModelTests.World(nodes, parent, i); return (m[12], m[13], m[14]); }
        throw new Xunit.Sdk.XunitException(name + " missing in " + path);
    }

    private static (double x0, double x1, double y0, double y1, double z0, double z1) Box(List<(float x, float y, float z)> v) =>
        (v.Min(p => p.x), v.Max(p => p.x), v.Min(p => p.y), v.Max(p => p.y), v.Min(p => p.z), v.Max(p => p.z));

    [Theory]
    [InlineData("-own")] [InlineData("-own-unmarked")] [InlineData("-own-n300uy")]
    public void The_rear_panel_top_is_there_seated_on_the_panel_and_drawn(string livery)
    {
        string path = Glb(livery);
        var v = OwnPittsModelTests.Vertices(path);
        Assert.True(v.ContainsKey("RearPanel_Top_P3d"), "rear panel hood missing: " + livery);
        Assert.True(v.ContainsKey("RearPanel_Glareshield_P3d"), "rear glareshield cap missing: " + livery);
        Assert.True(v["RearPanel_Top_P3d"].Count >= 3 * 12 && v["RearPanel_Glareshield_P3d"].Count >= 3 * 12);

        var hood = Box(v["RearPanel_Top_P3d"]); var cap = Box(v["RearPanel_Glareshield_P3d"]); var panel = Box(v["Panel_P3b_R"]);
        Assert.InRange(hood.y0, panel.y1 - 0.002, panel.y1 + 0.002);          // sits on the P3b panel's top edge
        Assert.InRange(hood.y1, 0.425, 0.431);                                 // the original hood top, 0.428
        Assert.InRange(hood.z1 - hood.z0, 0.54, 0.62);                         // nearly the original 620 mm span
        Assert.InRange(hood.x1, panel.x1, panel.x1 + 0.006);                   // aft face just proud of the gauge plane
        Assert.InRange(hood.x1 - hood.x0, 0.15, 0.21);                         // the original 200 mm deep hood
        Assert.True(cap.y1 >= hood.y1 && cap.y1 <= hood.y1 + 0.004, "cap lies on the hood top");

        // Drawable: opaque material, no default-hidden flag on the node.
        var (j, _) = OwnPittsModelTests.ReadGlb(path);
        foreach (JsonElement node in j.GetProperty("nodes").EnumerateArray())
        {
            string? name = node.GetProperty("name").GetString();
            if (name != "RearPanel_Top_P3d" && name != "RearPanel_Glareshield_P3d") continue;
            if (node.TryGetProperty("extras", out JsonElement ex) && ex.TryGetProperty("default_visible", out JsonElement dv))
                Assert.True(dv.ValueKind != JsonValueKind.False, name + " is default-hidden");
            foreach (JsonElement prim in j.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                JsonElement mat = j.GetProperty("materials")[prim.GetProperty("material").GetInt32()];
                Assert.False(mat.TryGetProperty("alphaMode", out JsonElement am) && am.GetString() != "OPAQUE", name + " not opaque");
                Assert.Equal(1.0, mat.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[3].GetDouble(), 3);
            }
        }
    }

    [Theory]
    [InlineData("-own")] [InlineData("-own-unmarked")] [InlineData("-own-n300uy")]
    public void The_rear_eye_sees_the_panel_top_and_every_top_row_gauge_under_it(string livery)
    {
        string path = Glb(livery);
        var v = OwnPittsModelTests.Vertices(path);
        var eye = NodeWorld(path, "Eye_Rear");
        var hood = Box(v["RearPanel_Top_P3d"]);
        // Ahead of the eye (glTF nose is -x), below it, and inside a 45 deg half-angle forward cone.
        Assert.True(hood.x1 < eye.x - 0.3, "hood is ahead of the rear eye");
        double drop = eye.y - hood.y1, ahead = eye.x - hood.x1;
        Assert.True(drop > 0 && Math.Atan2(drop, ahead) < Math.PI / 4, $"hood top {drop:F3} m below, {ahead:F3} m ahead of the eye");
        // Each top-row bezel's top point is seen under the hood's aft lower edge, not through it.
        foreach (string g in new[] { "ASI", "Altimeter", "TC", "G" })
        {
            var b = Box(v[$"Bezel_P3b_{g}_R"]);
            double t = (eye.x - hood.x1) / (eye.x - b.x1);
            double yAtHood = eye.y + (b.y1 - eye.y) * t;
            Assert.True(yAtHood < hood.y0, $"{g}: sight line passes the hood face at {yAtHood:F4} m, hood underside {hood.y0:F4} m");
        }
    }
}
