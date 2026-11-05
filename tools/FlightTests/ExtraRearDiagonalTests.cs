using System.Text.Json;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty 2026-11-05: remove completely; panel must stay inside the dash's diagonal shoulders.</summary>
public class ExtraRearDiagonalTests
{
    public static IEnumerable<object[]> Files() => new[] { "extra-300-like-own", "extra-300-like-own-n300uy", "extra-300-like-own-unmarked" }.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Files))]
    public void Diagonal_nodes_and_meshes_are_absent_even_if_unused(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(OwnPittsModelTests.Models, "extra-300-like", file + ".glb"));
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        foreach (string collection in new[] { "nodes", "meshes", "materials" })
            foreach (var item in doc.RootElement.GetProperty(collection).EnumerateArray())
                if (item.TryGetProperty("name", out var name))
                    Assert.DoesNotContain("PanelDiagonal", name.GetString()!);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Rear_panel_follows_the_dash_bevel_and_keeps_lower_wall_attachments(string file)
    {
        var m = OwnPittsModelTests.Vertices(Path.Combine(OwnPittsModelTests.Models, "extra-300-like", file + ".glb"));
        // Existing hood: flat halfwidth 200 mm at z 428 mm; shoulder run/rise 110/90.
        // Copy literals independently of the authoring pass. Every exported vertex must be inside it.
        var panel = m["Panel_P3b_R"];
        Assert.All(panel, p => Assert.True(Math.Abs(p.z) <= .200 + (.428 - p.y) * (110.0 / 90.0) - .0019,
            $"{file}: panel extends outside the dash diagonal at y={p.z}, z={p.y}"));
        Assert.InRange(panel.Where(p => p.y > .359).Max(p => Math.Abs(p.z)), .2809, .2813);
        // Original GLB literals: 293.5 mm halfwidth at the bottom, 391.0 mm at the side-wall attachment.
        Assert.InRange(panel.Where(p => p.y < .1).Max(p => Math.Abs(p.z)), .2934, .2936);
        Assert.InRange(panel.Where(p => p.y > .18 && p.y < .21).Max(p => Math.Abs(p.z)), .3908, .3912);
        // Front subpanel has no dash hood and already fits within its small rectangular outline.
        Assert.All(m["Panel_P3b_F"], p => Assert.InRange(Math.Abs(p.z), 0, .12501));
    }
}
