using System.Text.Json;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 5:25 PM PT, iPhone: "The Cessna 172 cockpit is TRANSPARENT, with NO interior at all."
///
/// The GLB was never the problem: its cabin was whole and opaque. The game was not DRAWING it. With "own models" off
/// (the shipped default; Ty's phone had set.ownModels=0) AirframeModels.PickOwn placed the downloaded c172-like.obj, an
/// exterior shell with no cabin, whose outward faces are culled from the pilot's eye, so he looked straight through
/// the aeroplane. Every render that approved the panel, the seats and the yoke was taken with own models forced on.
///
/// So two things are held here: the 172 is one of the types whose own model is drawn whatever the option says
/// (LiveryChoice.OwnModelAlways, read by PickOwn), and the cabin that model carries is there and opaque, every
/// primitive of it with a material of its own (a missing material is glTFast's default, which is not our cabin).
/// Node names are literals, copied by hand from the shipped c172-like-own.glb (2026-11-04, yoke-v1 + suction 5.4).
/// </summary>
public class C172InteriorVisibleTests
{
    private readonly ITestOutputHelper _o;
    public C172InteriorVisibleTests(ITestOutputHelper o) { _o = o; }

    private static string Model => Path.Combine(OwnPittsModelTests.Models, "c172-like", "c172-like-own.glb");

    private static readonly string[] Interior =
    {
        "C172_Interior_static", "C172_Headliner", "C172_Door_L", "C172_Door_R",
        "C172_Seat_base_L", "C172_Seat_base_R", "C172_Rear_bench_base", "C172_Sunvisor_L", "C172_Sunvisor_R",
        "C172_Yoke_L_roll", "C172_Yoke_L_pitch", "C172_Yoke_R_roll", "C172_Yoke_R_pitch",
        "C172_Pedal_L_L", "C172_Pedal_L_R", "C172_Pedal_R_L", "C172_Pedal_R_R",
        "C172_Throttle", "C172_Mixture", "C172_Carb_heat", "C172_Trim_wheel", "C172_Fuel_selector", "C172_Flap_switch",
        "C172_Airspeed_case", "C172_Attitude_case", "C172_Altimeter_case", "C172_Turn_coordinator_case",
        "C172_Airspeed_needle", "C172_Altimeter_needle", "C172_Suction_needle",
        "C172_KX170B_1", "C172_KX170B_2", "C172_KR85_ADF", "C172_Master_BAT", "C172_Master_ALT", "C172_Mags_key",
    };

    [Fact]
    public void The_172_draws_our_own_model_with_own_models_off()
    {
        Assert.True(LiveryChoice.OwnModelAlways("c172-like"));
        // The whole "ours always" list, so a type cannot fall off it quietly.
        foreach (string id in new[] { "extra-300-like", "pitts-s2b-like", "great-lakes-like", "glider-2-33-like",
                                      "j3-cub-like", "j3-cub-floats-like", "c172-like", "glider-grob103-acro-like", "dhc2-beaver-like" })
            Assert.True(LiveryChoice.OwnModelAlways(id), id);
        Assert.False(LiveryChoice.OwnModelAlways("pa28-archer-like"));
        Assert.False(LiveryChoice.OwnModelAlways(null));
        // The 172's default livery loads the GLB this file checks.
        Assert.Equal("-own", LiveryChoice.OwnSuffix("c172-like", LiveryChoice.Resolve("c172-like", null, null)));

        // ...and PickOwn reads that list rather than a private copy of it (Unity-only code: a wiring check).
        string models = File.ReadAllText(Path.Combine(OwnPittsModelTests.Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", "AirframeModels.cs"));
        Assert.Contains("SessionSettings.UseOwnModels || FlyingGame.Core.LiveryChoice.OwnModelAlways(modelId)", models);
    }

    /// <summary>The first render after the 172 went "ours always" put the pilot's eye between the fin and the stabiliser:
    /// tools/model_c172.py exports the nose at glTF +X, every other own model at -X, and OwnSpecs gave the 172 the
    /// others' Vector3.right, so it was placed tail first (flagged in AGENT-LOG 2026-11-03, never fixed, never seen
    /// because the game never drew this GLB). Measured from the GLB: which end the spinner is at, against the fin;
    /// and the Spec has to point that way (glTFast negates x, so a +X nose is the instance's -x: Vector3.left).</summary>
    [Fact]
    public void Each_own_model_is_placed_nose_first()
    {
        string models = File.ReadAllText(Path.Combine(OwnPittsModelTests.Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", "AirframeModels.cs"));
        // (type, GLB, nose part) -- literals; the expected glTF nose sign is +1 for the 172 only (measured 2026-11-04).
        foreach (var (id, glb, nose, wantSign) in new[]
                 {
                     ("c172-like", "c172-like-own.glb", "Spinner", +1),
                     ("extra-300-like", "extra-300-like-own-unmarked.glb", "Spinner", -1),
                     ("great-lakes-like", "great-lakes-like-own.glb", "Spinner", -1),
                     ("j3-cub-like", "j3-cub-like-own.glb", "Prop", -1),
                 })
        {
            var v = OwnPittsModelTests.Vertices(Path.Combine(OwnPittsModelTests.Models, id, glb));
            double noseX = v[nose].Average(p => p.x), finX = v["Fin"].Average(p => p.x);
            int sign = Math.Sign(noseX - finX);
            _o.WriteLine($"{glb}: {nose} x {noseX:F2}, Fin x {finX:F2} -> nose at glTF {(sign > 0 ? "+X" : "-X")}");
            Assert.Equal(wantSign, sign);
            string spec = sign > 0 ? "Vector3.left" : "Vector3.right";
            Assert.Contains($"{{ \"{id}\", new Spec({spec}, Vector3.up", models);
        }
    }

    /// <summary>The second render put the eye AHEAD of the windscreen, over the cowl: the 172's wing is two sections
    /// (root and tip), so AirframeModels' outer-wing alignment found no vertices between 65 and 92 % of the span and
    /// the length-centre fallback moved the model 1.73 m aft of the datum its eyes were measured in. model_c172.py draws
    /// the aeroplane in the config's own frame at true size, so it is placed AT the datum (Spec atDatum). Here the
    /// approved eyes (literals, CockpitEyeTests.ApprovedEyes, body frame +x fwd +y right +z down) are held inside the
    /// GLB's cabin in its own frame (glTF +x fwd, +y up, +z right): behind the windscreen, above the seat cushion, under
    /// the headliner, over the seat of the right side.</summary>
    [Fact]
    public void The_approved_eyes_are_in_the_cabin_of_the_model_placed_at_the_datum()
    {
        string models = File.ReadAllText(Path.Combine(OwnPittsModelTests.Repo, "unity", "Assets", "_Project", "Scripts", "Bridge", "AirframeModels.cs"));
        Assert.Contains("{ \"c172-like\", new Spec(Vector3.left, Vector3.up, atDatum: true) }", models);
        var v = OwnPittsModelTests.Vertices(Model);
        (double lo, double hi) X(string n) => (v[n].Min(p => p.x), v[n].Max(p => p.x));
        (double lo, double hi) Y(string n) => (v[n].Min(p => p.y), v[n].Max(p => p.y));
        (double lo, double hi) Z(string n) => (v[n].Min(p => p.z), v[n].Max(p => p.z));
        foreach (var (seat, bx, by, bz, cushion) in new[] { ("pilot", -0.346, -0.337, -0.548, "C172_Seat_base_L"), ("copilot", -0.346, 0.332, -0.548, "C172_Seat_base_R") })
        {
            double gx = bx, gy = -bz, gz = by;
            _o.WriteLine($"{seat}: eye glTF ({gx:F3}, {gy:F3}, {gz:F3}); windscreen x from {X("Glass.Windscreen").lo:F3}, headliner y from {Y("C172_Headliner").lo:F3}, "
                         + $"cushion top y {Y(cushion).hi:F3}, cushion z {Z(cushion).lo:F3}..{Z(cushion).hi:F3}");
            Assert.True(gx < X("Glass.Windscreen").lo, seat + ": eye ahead of the windscreen");
            Assert.True(gx > X(cushion).lo, seat + ": eye behind the seat");
            Assert.True(gy < Y("C172_Headliner").lo && gy > Y(cushion).hi + 0.5, seat + ": eye not between cushion and headliner");
            Assert.True(gz > Z(cushion).lo && gz < Z(cushion).hi, seat + ": eye not over its own seat");
        }
    }

    [Fact]
    public void The_own_172_cabin_is_present_and_every_primitive_is_opaque()
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(Model);
        JsonElement meshes = j.GetProperty("meshes");
        JsonElement mats = j.GetProperty("materials");
        var byName = new Dictionary<string, JsonElement>();
        foreach (JsonElement n in j.GetProperty("nodes").EnumerateArray())
            if (n.TryGetProperty("name", out JsonElement nm)) byName[nm.GetString()!] = n;

        var faults = new List<string>();
        int prims = 0;
        foreach (string name in Interior)
        {
            if (!byName.TryGetValue(name, out JsonElement node)) { faults.Add(name + ": missing"); continue; }
            if (!node.TryGetProperty("mesh", out JsonElement mi)) { faults.Add(name + ": no mesh"); continue; }
            foreach (JsonElement p in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                prims++;
                if (!p.TryGetProperty("material", out JsonElement m)) { faults.Add(name + ": a primitive with no material"); continue; }
                JsonElement mat = mats[m.GetInt32()];
                string mname = mat.TryGetProperty("name", out JsonElement x) ? x.GetString()! : "#" + m.GetInt32();
                string mode = mat.TryGetProperty("alphaMode", out JsonElement am) ? am.GetString()! : "OPAQUE";
                if (mode != "OPAQUE") faults.Add($"{name}: {mname} is {mode}");
                if (mat.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("baseColorFactor", out JsonElement bc)
                    && bc[3].GetDouble() < 1.0) faults.Add($"{name}: {mname} base colour alpha {bc[3].GetDouble()}");
            }
        }
        // And the other way round for the glazing: with no material glTFast drew the panes opaque, and from the seat
        // the windscreen and both door windows were grey walls (render 2026-11-04). Each pane: BLEND, see-through.
        foreach (string name in new[] { "Glass.Windscreen", "Glass.Door.L", "Glass.Door.R", "Glass.Rear.L", "Glass.Rear.R" })
        {
            if (!byName.TryGetValue(name, out JsonElement node)) { faults.Add(name + ": missing"); continue; }
            foreach (JsonElement p in meshes[node.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                if (!p.TryGetProperty("material", out JsonElement m)) { faults.Add(name + ": no material (drawn opaque)"); continue; }
                JsonElement mat = mats[m.GetInt32()];
                string mode = mat.TryGetProperty("alphaMode", out JsonElement am) ? am.GetString()! : "OPAQUE";
                double a = mat.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[3].GetDouble();
                if (mode != "BLEND" || a > 0.5) faults.Add($"{name}: {mode} alpha {a}, not see-through");
            }
        }
        _o.WriteLine($"{Interior.Length} interior nodes, {prims} primitives checked; {faults.Count} faults");
        Assert.True(faults.Count == 0, string.Join("\n", faults));
    }
}
