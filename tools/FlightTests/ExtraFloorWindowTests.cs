using System.Text.Json;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Extra's clear Lexan floor panels, FRONT and REAR cockpit (Ty 2026-11-04 19:32; tools/own_models/floor_windows.py,
/// run after extra_front_seat.py on every livery). A pane is only a window if the floor and the belly above and below it
/// are really open, so this checks the hole as well as the glass. Openings copied by hand from floor_windows.WINDOWS
/// (builder metres: x forward, |y| half-width): rear x -1.492 .. -0.950, |y| 0.195; front x -0.520 .. +0.140, |y| 0.170.
/// </summary>
public class ExtraFloorWindowTests
{
    private static readonly string[] Liveries = { "extra-300-like-own", "extra-300-like-own-n300uy", "extra-300-like-own-unmarked" };
    private static readonly (string name, double x0, double x1, double hw)[] Panes =
    {
        ("rear", -1.492, -0.950, 0.195),
        ("front", -0.520, 0.140, 0.170),
    };

    private static string Path_(string file) => System.IO.Path.Combine(OwnPittsModelTests.Models, "extra-300-like", file + ".glb");

    [Theory]
    [MemberData(nameof(Files))]
    public void Both_cockpits_have_a_clear_Lexan_floor_panel(string file)
    {
        var v = OwnPittsModelTests.Vertices(Path_(file));
        Assert.True(v.ContainsKey("Floor_Window_Glass"), "no Floor_Window_Glass");
        Assert.True(v.ContainsKey("Floor_Window_Frame"), "no Floor_Window_Frame");
        foreach (var (name, x0, x1, hw) in Panes)
        {
            // builder = (-gx, gz, gy)
            int glass = v["Floor_Window_Glass"].Count(p => -p.x > x0 - 0.01 && -p.x < x1 + 0.01 && Math.Abs(p.z) < hw + 0.01 && p.y < -0.45);
            Assert.True(glass > 20, $"{file}: no {name} pane under the {name} cockpit ({glass} glass vertices)");
            int frame = v["Floor_Window_Frame"].Count(p => -p.x > x0 - 0.05 && -p.x < x1 + 0.05 && Math.Abs(p.z) < hw + 0.05);
            Assert.True(frame > 100, $"{file}: no frame round the {name} pane");
            // The hole: nothing of the belly skin or the cockpit floor tub inside the clear opening (1 cm in from its edge).
            bool Inside((float x, float y, float z) p) => -p.x > x0 + 0.01 && -p.x < x1 - 0.01 && Math.Abs(p.z) < hw - 0.01;
            foreach (string node in new[] { "Fuselage_Painted", "Window_Glass" })
                if (v.TryGetValue(node, out var pts))
                    Assert.DoesNotContain(pts, p => p.y < -0.45 && Inside(p));
            // Each stick stands in its own well through the floor, a 126 mm box (z -0.158 .. -0.084) round its pivot
            // (Stick_R x -1.355, Stick_F x -0.352, ea300-own-rig.json): its rim is at floor height and stays.
            bool StickWell((float x, float y, float z) p) => Math.Abs(p.z) < 0.066 && (Math.Abs(-p.x + 1.355) < 0.066 || Math.Abs(-p.x + 0.352) < 0.066);
            foreach (string node in new[] { "Cockpit_Shell", "Cabin", "Footwell_Liner" })
                if (v.TryGetValue(node, out var pts))
                    Assert.DoesNotContain(pts, p => p.y > -0.110 && p.y < -0.080 && Inside(p) && !StickWell(p));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_panes_are_Lexan_and_the_frames_are_Floor_Frame(string file)
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(Path_(file));
        JsonElement nodes = j.GetProperty("nodes"), meshes = j.GetProperty("meshes"), mats = j.GetProperty("materials");
        string MatOf(string node)
        {
            foreach (JsonElement n in nodes.EnumerateArray())
                if (n.GetProperty("name").GetString() == node)
                    return mats[meshes[n.GetProperty("mesh").GetInt32()].GetProperty("primitives")[0].GetProperty("material").GetInt32()]
                        .GetProperty("name").GetString()!;
            return "";
        }
        Assert.Equal("Lexan", MatOf("Floor_Window_Glass"));
        Assert.Equal("Floor_Frame", MatOf("Floor_Window_Frame"));
        foreach (JsonElement m in mats.EnumerateArray())
            if (m.GetProperty("name").GetString() == "Lexan")
                Assert.Equal("BLEND", m.GetProperty("alphaMode").GetString());
    }

    public static IEnumerable<object[]> Files() => Liveries.Select(f => new object[] { f });
}
