using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05, Great Lakes:
///  - "remove the ignition switch and add TWO MAG switches on the switch panel" and "ON = FORWARD ... make sure every
///    switch's on direction" agrees: every toggle of our own Great Lakes, turned by its own rig row to ON, must throw its
///    grip FORWARD (glTF -x), and the ignition rotary is gone.
///  - the glass fuel sight gauge on the right, at the aft cabane's wing fitting, with a red ball on the cork float's wire
///    that drops as the top-wing tank burns: its rig row, its marks at 1/4 1/2 3/4, and the chain from the sim's own fuel
///    burn to the ball's height.
/// Literals: node names and the cabane fitting (glTF 0.801, 0.620, -0.700) from the GLB as built; the mark heights are
/// the tube's own design numbers (gl_panel_v2.py: ball 0.590 full, 0.482 empty, linear), copied here by hand.
/// </summary>
[Collection("WorldTerrainActive")]
public class GreatLakesSwitchSightTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public GreatLakesSwitchSightTests(ITestOutputHelper o) { _o = o; Calm(); }
    public void Dispose() => Calm();
    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private static readonly string[] Toggles =
        { "MagL", "MagR", "Master", "Alternator", "FuelPump", "NavLights", "Strobe", "LandingLight", "Smoke" };

    private static JsonDocument Rig() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "great-lakes-own-rig.json")));

    private static (JsonElement nodes, int[] parent, Dictionary<string, int> index) Nodes()
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(GreatLakesCardSpaceTests.Glb);
        JsonElement nodes = j.GetProperty("nodes");
        int n = nodes.GetArrayLength();
        var parent = Enumerable.Repeat(-1, n).ToArray();
        var index = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            index[nodes[i].GetProperty("name").GetString()!] = i;
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        }
        return (nodes.Clone(), parent, index);
    }

    private static double[] Pos(JsonElement nodes, int[] parent, int i)
    {
        double[] m = OwnPittsModelTests.World(nodes, parent, i);
        return new[] { m[12], m[13], m[14] };
    }

    /// <summary>Rodrigues, right-handed, in the glTF frame the rig's axes are written in.</summary>
    private static double[] Rotate(double[] v, double[] axis, double deg)
    {
        double len = Math.Sqrt(axis.Sum(a => a * a)); double[] k = axis.Select(a => a / len).ToArray();
        double t = deg * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t), d = k[0] * v[0] + k[1] * v[1] + k[2] * v[2];
        double[] x = { k[1] * v[2] - k[2] * v[1], k[2] * v[0] - k[0] * v[2], k[0] * v[1] - k[1] * v[0] };
        return new[] { v[0] * c + x[0] * s + k[0] * d * (1 - c), v[1] * c + x[1] * s + k[1] * d * (1 - c), v[2] * c + x[2] * s + k[2] * d * (1 - c) };
    }

    [Fact]
    public void Every_switch_throws_its_grip_FORWARD_to_turn_on()
    {
        using JsonDocument rig = Rig();
        var (nodes, parent, index) = Nodes();
        var rows = rig.RootElement.GetProperty("rig").EnumerateArray().GroupBy(r => r.GetProperty("node").GetString()!).ToDictionary(g => g.Key, g => g.First());
        var controls = rig.RootElement.GetProperty("controls").EnumerateArray().ToList();
        var failures = new List<string>();
        int seen = 0;
        foreach (string seat in new[] { "", "_Front" })
            foreach (string t in Toggles)
            {
                string node = "Ctl_Toggle_" + t + seat, grip = "Grip_" + t + seat;
                Assert.True(index.ContainsKey(node), node + " missing from the GLB");
                Assert.True(rows.ContainsKey(node), node + " has no rig row");
                Assert.Contains(controls, c => c.GetProperty("node").GetString() == node && c.GetProperty("kind").GetString() == "Toggle");
                JsonElement r = rows[node];
                double[] axis = r.GetProperty("axis_gltf").EnumerateArray().Select(a => a.GetDouble()).ToArray();
                var map = r.GetProperty("map").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
                double off = map.First(p => p.Item1 == 0.0).Item2, on = map.First(p => p.Item1 == 1.0).Item2;
                double[] pv = Pos(nodes, parent, index[node]), g = Pos(nodes, parent, index[grip]);
                double[] arm = { g[0] - pv[0], g[1] - pv[1], g[2] - pv[2] };
                double[] a0 = Rotate(arm, axis, off), a1 = Rotate(arm, axis, on);
                _o.WriteLine($"{node}: grip x OFF {pv[0] + a0[0]:F4} -> ON {pv[0] + a1[0]:F4} (forward is -x)");
                if (!(a1[0] < a0[0] - 0.010)) failures.Add($"{node}: ON moves the grip {(a1[0] - a0[0]) * 1000:F1} mm along +x (aft), not forward");
                seen++;
            }
        Assert.Equal(18, seen);
        Assert.Empty(failures);
    }

    [Fact]
    public void The_ignition_key_is_gone_and_two_mag_switches_are_on_the_switch_console()
    {
        using JsonDocument rig = Rig();
        var (nodes, parent, index) = Nodes();
        var controls = rig.RootElement.GetProperty("controls").EnumerateArray().ToList();
        Assert.DoesNotContain(controls, c => c.GetProperty("param").GetString() == "magneto");
        Assert.False(index.ContainsKey("Ctl_Rotary_Magneto") || index.ContainsKey("Ctl_Rotary_Magneto_Front") || index.ContainsKey("MagKey_R"));
        foreach (var (seat, strip) in new[] { ("", "GL_SwitchStrip_R"), ("_Front", "GL_SwitchStrip_F") })
            foreach (var (t, param) in new[] { ("MagL", "mag_left"), ("MagR", "mag_right") })
            {
                string node = "Ctl_Toggle_" + t + seat;
                Assert.Contains(controls, c => c.GetProperty("node").GetString() == node && c.GetProperty("param").GetString() == param);
                double[] p = Pos(nodes, parent, index[node]), s = Pos(nodes, parent, index[strip]);
                // on the lid of its console: within its 130 x 70 mm footprint, 2 mm above its top (strip origin = lid top)
                Assert.True(Math.Abs(p[0] - s[0]) < 0.065 && Math.Abs(p[2] - s[2]) < 0.035 && Math.Abs(p[1] - s[1] - 0.002) < 0.003,
                            $"{node} at ({p[0]:F3},{p[1]:F3},{p[2]:F3}) is not on {strip} ({s[0]:F3},{s[1]:F3},{s[2]:F3})");
            }
        // MAG L is on the pilot's left of MAG R (glTF +z is his left)
        Assert.True(Pos(nodes, parent, index["Ctl_Toggle_MagL"])[2] > Pos(nodes, parent, index["Ctl_Toggle_MagR"])[2]);
        Assert.True(new RigInputs().TryGet("mag_left", out _) && new RigInputs().TryGet("mag_right", out _));
    }

    private const double BallFull = 0.590, BallEmpty = 0.482;
    private static readonly (string label, double frac, double y)[] Marks = { ("1/4", 0.25, 0.509), ("1/2", 0.50, 0.536), ("3/4", 0.75, 0.563) };

    private static (double[] rest, double[] axis, List<(double p, double m)> map, string param) FloatRow()
    {
        using JsonDocument rig = Rig();
        JsonElement r = rig.RootElement.GetProperty("rig").EnumerateArray().Single(x => x.GetProperty("node").GetString() == "GL_FuelSight_Float");
        return (r.GetProperty("pivot_gltf").EnumerateArray().Select(a => a.GetDouble()).ToArray(),
                r.GetProperty("axis_gltf").EnumerateArray().Select(a => a.GetDouble()).ToArray(),
                r.GetProperty("map").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList(),
                r.GetProperty("kind").GetString() == "move" ? r.GetProperty("param").GetString()! : "not a slide");
    }

    private static double BallY(double frac)
    {
        var (rest, axis, map, _) = FloatRow();
        double m = map[0].m + (map[1].m - map[0].m) * (frac - map[0].p) / (map[1].p - map[0].p);
        return rest[1] + axis[1] * m;
    }

    [Fact]
    public void The_sight_gauge_hangs_on_the_right_at_the_aft_cabane_fitting_and_its_ball_reads_the_marks()
    {
        var (nodes, parent, index) = Nodes();
        var (rest, axis, map, param) = FloatRow();
        Assert.Equal("fuel_top_wing_frac", param);
        Assert.Equal(new[] { 0.0, 1.0, 0.0 }, axis);
        Assert.True(index.ContainsKey("GL_FuelSight") && index.ContainsKey("GL_FuelSight_Marks"));
        double[] p = Pos(nodes, parent, index["GL_FuelSight_Float"]);
        Assert.Equal(rest, p.Select(v => Math.Round(v, 4)).ToArray());
        // RIGHT side (glTF -z is the pilot's right), within 60 mm of the right aft cabane leg's wing fitting
        Assert.True(p[2] < -0.5, "the sight gauge is not on the right");
        double d = Math.Sqrt(Math.Pow(p[0] - 0.801, 2) + Math.Pow(p[2] + 0.700, 2));
        Assert.True(d < 0.060, $"the sight gauge is {d * 1000:F0} mm from the aft cabane fitting");
        Assert.True(BallY(1.0) < 0.620 && BallY(0.0) > 0.470, "the ball leaves the glass");
        Assert.Equal(BallFull, BallY(1.0), 4);
        Assert.Equal(BallEmpty, BallY(0.0), 4);
        foreach (var (label, frac, y) in Marks)
        {
            _o.WriteLine($"{label}: ball {BallY(frac):F4}, mark {y:F3}");
            Assert.Equal(y, BallY(frac), 3);
        }
        // the marks the GLB itself records
        var (j, _) = OwnPittsModelTests.ReadGlb(GreatLakesCardSpaceTests.Glb);
        JsonElement mh = j.GetProperty("nodes")[index["GL_FuelSight"]].GetProperty("extras").GetProperty("mark_heights_gltf_y");
        foreach (var (label, _, y) in Marks) Assert.Equal(y, mh.GetProperty(label).GetDouble(), 3);
        // clear glass
        JsonElement mats = j.GetProperty("materials");
        JsonElement glass = mats.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "GL_FuelSight_Glass");
        Assert.Equal("BLEND", glass.GetProperty("alphaMode").GetString());
        Assert.True(glass.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[3].GetDouble() < 0.3);
    }

    [Fact]
    public void The_ball_drops_as_the_engine_burns_the_top_wing_tank()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "great-lakes-like.json"));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(55, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        AircraftLoad.Slot top = ac.Load.Find("topWing")!;
        Assert.Equal(70.86, top.MaxKg, 2);
        ac.Load.Set("topWing", top.MaxKg / 2); ac.Load.Set("header", ac.Load.Find("header")!.MaxKg);
        ac.ApplyLoad();
        double f0 = RigInputs.TankFraction(ac.Load, "topWing");
        Assert.Equal(0.5, f0, 6);
        Assert.Equal(0.536, BallY(f0), 3);                      // on the 1/2 mark

        double kg0 = ac.Load.FuelKg;
        new SimLoop(ac).RunFor(120.0, new ControlInputs(0, 0, 0, -1.0));   // two minutes at full throttle
        double burnt = kg0 - ac.Load.FuelKg, f1 = RigInputs.TankFraction(ac.Load, "topWing");
        _o.WriteLine($"burnt {burnt:F2} kg in 120 s; top wing {f0:F4} -> {f1:F4}; ball {BallY(f0):F4} -> {BallY(f1):F4} m");
        Assert.True(burnt > 0.5, $"only {burnt:F2} kg burnt");
        Assert.True(f1 < f0 - 0.005, "the top-wing tank did not go down");
        Assert.True(BallY(f1) < BallY(f0) - 0.0005, "the ball did not drop");
        // the share of the burn the top-wing tank carries (both tanks burn together, in proportion to what each holds)
        Assert.Equal(burnt * (35.43 / (35.43 + 3.82)) / 70.86, f0 - f1, 2);

        // and an aeroplane flown on the reference loading (no fuel in any tank) reads FULL, as the cockpit's other gauges do
        var empty = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(55, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        empty.Load.Set("topWing", 0); empty.Load.Set("header", 0);
        Assert.Equal(1.0, RigInputs.TankFraction(empty.Load, "topWing"));
    }
}
