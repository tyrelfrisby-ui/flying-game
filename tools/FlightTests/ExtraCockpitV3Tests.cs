using System.Text.Json;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 05:07, EXTRA 300 (tools/own_models/extra_cockpit_v3.py, all three liveries). Literals copied by hand:
///   eyes (builder metres, x fwd, y left, z up, as placed): rear (-1.622, 0, 0.526), front (-0.703, 0, 0.537);
///   SeatedPilot.ShoulderL relative to the eye: 0.08 aft, 0.17 left, 0.23 down.
///   "Throttles are way too high: put them down on the left at a natural bent-elbow position." -> each idle grip
///     0.25-0.40 m ahead of the left shoulder, 0.15-0.30 m below it, outboard of it, and below the sill (z 0.24).
///   "FRONT throttle is a plain black round knob" -> the front lever's grip is a ball (~38 mm), no pistol grip.
///   "Fuel valve rod ... must run FORWARD to the engine firewall past the front seat, with handles in both seats" ->
///     the rod reaches the firewall (x +0.57 or more) and runs aft no further than the rear handle (x -1.42), and both
///     handles (x -1.40, -0.50) sit on its line.
///   "Front panel: an IAC card somewhere visible" -> FrontPanel_SequenceCard_Face, 108 x 140 mm portrait, on the front
///     panel plane (x -0.15), facing the front pilot.
/// </summary>
public class ExtraCockpitV3Tests
{
    private static readonly string[] Liveries = { "extra-300-like-own", "extra-300-like-own-n300uy", "extra-300-like-own-unmarked" };
    public static IEnumerable<object[]> Files() => Liveries.Select(f => new object[] { f });

    private static (double x, double y, double z) Eye(string seat) => seat == "Rear" ? (-1.622, 0, 0.526) : (-0.703, 0, 0.537);

    private sealed class Model
    {
        public Dictionary<string, (double x, double y, double z)> Origin = new();
        public Dictionary<string, List<(double x, double y, double z)>> Verts = new();
    }

    private static Model Load(string file)
    {
        string path = Path.Combine(OwnPittsModelTests.Models, "extra-300-like", file + ".glb");
        var (j, _) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes");
        int count = nodes.GetArrayLength(); var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        var m = new Model();
        for (int i = 0; i < count; i++)
        {
            double[] w = OwnPittsModelTests.World(nodes, parent, i);
            m.Origin[nodes[i].GetProperty("name").GetString()!] = (-w[12], w[14], w[13]);       // glTF -> builder
        }
        foreach (var (name, pts) in OwnPittsModelTests.Vertices(path))
            m.Verts[name] = pts.Select(p => (-(double)p.x, (double)p.z, (double)p.y)).ToList();
        return m;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Both_throttles_sit_low_on_the_left_at_a_bent_elbow(string file)
    {
        var m = Load(file);
        foreach (var (seat, grip) in new[] { ("Rear", "Grip_Throttle_Rear"), ("Front", "Grip_Throttle_Front") })
        {
            var e = Eye(seat); var g = m.Origin[grip];
            double sx = e.x - 0.08, sy = e.y + 0.17, sz = e.z - 0.23;                        // left shoulder
            double ahead = g.x - sx, below = sz - g.z, reach = Math.Sqrt(ahead * ahead + below * below + (g.y - sy) * (g.y - sy));
            Assert.InRange(ahead, 0.25, 0.40);
            Assert.InRange(below, 0.15, 0.30);
            Assert.True(g.y > sy, $"{seat} throttle grip y {g.y:F3} is not outboard of the shoulder (left, y > {sy:F2})");
            Assert.True(g.z < 0.24, $"{seat} throttle grip z {g.z:F3} is not below the sill");
            Assert.InRange(reach, 0.35, 0.55);                                                  // 0.65 m arm: the elbow stays bent
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_front_throttle_is_a_round_black_knob_and_the_rear_keeps_its_grip(string file)
    {
        var m = Load(file);
        var g = m.Origin["Grip_Throttle_Front"];
        // Everything of the front lever within 3 cm of its grip point is the ball: about 38 mm across in every axis.
        var ball = m.Verts["Throttle_F"].Where(p => Dist(p, g) < 0.03).ToList();
        Assert.True(ball.Count > 50, $"no knob at the front grip ({ball.Count} vertices)");
        foreach (Func<(double x, double y, double z), double> ax in new Func<(double, double, double), double>[] { p => p.Item1, p => p.Item2, p => p.Item3 })
            Assert.InRange(ball.Max(ax) - ball.Min(ax), 0.034, 0.042);
        // No pistol grip: nothing of the front lever more than 2.5 cm past the ball's centre (the old grip ran 11 cm on).
        var piv = m.Origin["Throttle_F"];
        double armLen = Dist(g, piv);
        Assert.DoesNotContain(m.Verts["Throttle_F"], p => Dist(p, piv) > armLen + 0.025);
        // The rear keeps its 11 cm ribbed grip.
        var gr = m.Origin["Grip_Throttle_Rear"]; var pr = m.Origin["Throttle_R"];
        Assert.Contains(m.Verts["Throttle_R"], p => Dist(p, pr) > Dist(gr, pr) + 0.04);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_fuel_valve_rod_runs_forward_to_the_firewall_through_both_handles(string file)
    {
        var m = Load(file);
        var rod = m.Verts["Extra_FuelValve_SharedRod"];
        Assert.True(rod.Max(p => p.x) > 0.57, $"the rod stops at x {rod.Max(p => p.x):F3}, short of the firewall (+0.60)");
        Assert.True(rod.Min(p => p.x) > -1.43, $"the rod runs aft to x {rod.Min(p => p.x):F3}, past the rear handle");
        Assert.True(m.Verts.ContainsKey("Extra_FuelValve_Body") && m.Verts["Extra_FuelValve_Body"].Min(p => p.x) > 0.50, "no valve body on the firewall");
        double ry = rod.Average(p => p.y), rz = rod.Average(p => p.z);
        foreach (var (h, x) in new[] { ("Ctl_Rotary_FuelSelector", -1.40), ("Ctl_Rotary_FuelSelector_Front", -0.50) })
        {
            var o = m.Origin[h];
            Assert.True(Math.Abs(o.x - x) < 0.01 && Math.Abs(o.y - ry) < 0.002 && Math.Abs(o.z - rz) < 0.002, $"{h} at ({o.x:F3},{o.y:F3},{o.z:F3}) is not on the rod");
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_front_panel_has_a_half_letter_IAC_card_facing_the_front_pilot(string file)
    {
        var m = Load(file);
        Assert.True(m.Verts.TryGetValue("FrontPanel_SequenceCard_Face", out var f), "no FrontPanel_SequenceCard_Face");
        double w = f.Max(p => p.y) - f.Min(p => p.y), h = f.Max(p => p.z) - f.Min(p => p.z), dx = f.Max(p => p.x) - f.Min(p => p.x);
        Assert.InRange(w, 0.106, 0.110);          // 4.25 in
        Assert.InRange(h, 0.138, 0.142);          // 5.5 in
        Assert.True(dx < 0.001, "the card is not flat in the panel plane");
        double cx = f.Average(p => p.x), cz = f.Average(p => p.z);
        Assert.InRange(cx, -0.16, -0.14);         // on the front panel (x -0.150), ahead of the front eye
        Assert.True(cz < 0.537 && cz > 0.15, $"card centre z {cz:F3} is not below the eye and above the stick");
        foreach (string part in new[] { "FrontPanel_SequenceCard_Holder", "FrontPanel_SequenceCard_Clip", "FrontPanel_SequenceCard_Stock" })
            Assert.True(m.Verts.ContainsKey(part), part + " missing");
    }

    private static double Dist((double x, double y, double z) a, (double x, double y, double z) b) =>
        Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));
}
