using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Twin III Acro's own model (Codex, lane/codex-grob103, the Blanc conversion rebuilt at 18 m, 2026-11-04) against
/// the config it draws. Expected numbers are LITERALS, copied by hand from the sources:
///   config   glider-grob103-acro-like.json / build_grob103.py: span 18.0 m, main wheel x -0.5397, nose +1.4595,
///            tail -4.8768 (the [N41KP] weighing's 21.25 / 192.00 in), wheel contacts 0.69 / 0.69 / 0.33 m below the datum
///   model    docs/models/grob103-own-RIG.md: tyre bottoms at author z -0.600 / -0.600 / -0.260 m; approved eyes,
///            native glTF, front (-1.15, 0.16, 0), rear (0.04, 0.22, 0) (Ty 2026-11-03)
/// AirframeModels' Grob spec is `atDatum`: the GLB is authored in metres on the config's datum, so it is placed at
/// scale 1 with no longitudinal shift, and only grounded on its wheels (the drawn wheels sit 90 mm higher on the
/// airframe than the config's: recorded, not fudged).
/// </summary>
public class Grob103ModelTests
{
    private readonly ITestOutputHelper _out;
    public Grob103ModelTests(ITestOutputHelper o) { _out = o; }

    private const string Id = "glider-grob103-acro-like";
    private static string Glb(string suffix) => Path.Combine(OwnPittsModelTests.Models, Id, Id + suffix + ".glb");
    private static string RigJson => Path.Combine(OwnPittsModelTests.Repo, "docs", "models", "grob103-own-rig.json");

    /// <summary>How far Place drops the model to stand it on the config's wheels: the config's deepest contact (0.69 m)
    /// less the model's tyre bottom (0.600 m).</summary>
    private const double GroundShift = 0.69 - 0.600;

    public static TheoryData<string> Liveries => new() { "-own", "-own-tf" };

    /// <summary>In its own frame the GLB has the config's span and the config's three wheel stations.</summary>
    [Theory]
    [MemberData(nameof(Liveries))]
    public void TheModelHasTheConfigsSpanAndWheelStations(string suffix)
    {
        var parts = OwnPittsModelTests.Vertices(Glb(suffix));
        var all = parts.Values.SelectMany(v => v).ToList();
        double span = all.Max(p => p.z) - all.Min(p => p.z);
        Assert.InRange(span, 17.995, 18.005);                  // [POH] 1.4 18.0 m

        (double x, double zDown) Contact(string node)
        {
            var w = parts[node];
            return (-(w.Min(p => p.x) + w.Max(p => p.x)) / 2, -w.Min(p => p.y));   // glTF nose -x, up +y
        }
        var nose = Contact("Nose_Wheel"); var main = Contact("Main_Wheel"); var tail = Contact("Tail_Wheel");
        _out.WriteLine($"{suffix}: span {span:F4} m; nose ({nose.x:F4}, {nose.zDown:F3}) main ({main.x:F4}, {main.zDown:F3}) " +
                       $"tail ({tail.x:F4}, {tail.zDown:F3}) against the config's 1.4595/0.69, -0.5397/0.69, -4.8768/0.33");
        Assert.InRange(nose.x, 1.4545, 1.4645);
        Assert.InRange(main.x, -0.5447, -0.5347);
        Assert.InRange(tail.x, -4.8818, -4.8718);
        // The lowest point of the model is the nose and main tyre bottoms, which is what Place stands on the config's 0.69.
        Assert.Equal(0.600, all.Max(p => -p.y), 3);
        Assert.Equal(0.600, main.zDown, 3); Assert.Equal(0.600, nose.zDown, 3); Assert.Equal(0.260, tail.zDown, 3);
        // RECORDED DISAGREEMENT (not fudged): once Place grounds the model, the drawn tail wheel sits 0.35 m below the
        // datum against the config's 0.33 -- and the whole airframe 90 mm lower on its wheels than the config's datum.
        Assert.Equal(0.350, tail.zDown + GroundShift, 3);
    }

    /// <summary>The camera sits where the model's own eye nodes land once the model is placed: the approved native glTF
    /// eyes (Ty 2026-11-03) carried through the same drop Place applies. Front (-1.15, 0.16, 0) -> body (1.15, 0, -0.07);
    /// rear (0.04, 0.22, 0) -> (-0.04, 0, -0.13).</summary>
    [Fact]
    public void TheSeatEyesAreTheApprovedEyesWhereTheModelIsPlaced()
    {
        AircraftConfig c = Grob103.Load();
        foreach (var (seat, gx, gy) in new[] { ("front", -1.15, 0.16), ("rear", 0.04, 0.22) })
        {
            var eye = c.Loading.Seats.Single(s => s.Id == seat).EyeVec();
            Assert.Equal(-gx, eye.X, 3);
            Assert.Equal(0.0, eye.Y, 3);
            Assert.Equal(-(gy - GroundShift), eye.Z, 3);
        }
    }

    private static (double X, double Y, double Z) Turn((double X, double Y, double Z) v, (double X, double Y, double Z) k, double deg)
    {
        // Rodrigues, right-handed, in native glTF (the importer's reflection applies to every node alike).
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), d = k.X * v.X + k.Y * v.Y + k.Z * v.Z;
        (double X, double Y, double Z) x = (k.Y * v.Z - k.Z * v.Y, k.Z * v.X - k.X * v.Z, k.X * v.Y - k.Y * v.X);
        return (v.X * c + x.X * s + k.X * d * (1 - c), v.Y * c + x.Y * s + k.Y * d * (1 - c), v.Z * c + x.Z * s + k.Z * d * (1 - c));
    }

    private static double MapValue(JsonElement node, double param)
    {
        var pts = node.GetProperty("map").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
        for (int i = 1; i < pts.Count; i++)
            if (param <= pts[i].Item1 || i == pts.Count - 1)
            {
                var (p0, v0) = pts[i - 1]; var (p1, v1) = pts[i];
                return v0 + (v1 - v0) * (param - p0) / (p1 - p0);
            }
        return pts[^1].Item2;
    }

    /// <summary>The drawn nose wheel turns with the SIM's steering angle (RigInputs.NoseWheelSteerDeg, + = right), and the
    /// right way: right pedal swings the rudder's trailing edge to starboard AND the nose wheel's front to starboard
    /// (native glTF: aft +x, port +z). The rig as delivered drove it off `rudder` with the rudder's own sign, which turns
    /// the wheel's front to PORT -- a part ahead of its pivot and a part behind it must turn opposite ways.</summary>
    [Fact]
    public void TheNoseWheelIsDrawnAtTheSimsSteeringAngleTheRightWay()
    {
        JsonElement rig = JsonDocument.Parse(File.ReadAllText(RigJson)).RootElement.GetProperty("rig");
        JsonElement steer = rig.EnumerateArray().Single(n => n.GetProperty("node").GetString() == "Nose_Steering");
        JsonElement rudder = rig.EnumerateArray().Single(n => n.GetProperty("node").GetString() == "Rudder");
        Assert.Equal("nose_wheel_steer_deg", steer.GetProperty("param").GetString());
        Assert.True(new RigInputs().TryGet("nose_wheel_steer_deg", out _), "the sim must supply the param the rig asks for");

        (double, double, double) Axis(JsonElement n) { var a = n.GetProperty("axis_gltf"); return (a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        var te = Turn((1, 0, 0), Axis(rudder), MapValue(rudder, 1.0));          // trailing edge, right pedal
        Assert.True(te.Z < -0.3, $"right rudder must move the trailing edge to starboard (-z): {te.Z:F3}");

        // The sim at full right pedal: 25 deg right (the ESTIMATED lock, Grob103Tests.SteerLockDeg).
        RigInputs r = RigInputs.FromDeflections(0, 0, 0.5236, 0, 0.4363, 0.4065, 0.5236, LandingGear.NoseWheelSteerMaxRad(Grob103.Load()));
        Assert.Equal(25.0, r.NoseWheelSteerDeg, 2);
        double drawn = MapValue(steer, r.NoseWheelSteerDeg);
        var front = Turn((-1, 0, 0), Axis(steer), drawn);                       // the wheel's front
        _out.WriteLine($"right pedal: rudder TE z {te.Z:F3}, nose wheel front z {front.Z:F3} (drawn {drawn:F1} deg about {Axis(steer)})");
        Assert.True(front.Z < 0, $"right pedal must point the nose wheel to starboard (-z): {front.Z:F3}");
        Assert.Equal(25.0, Math.Atan2(-front.Z, -front.X) * 180 / Math.PI, 2);  // and by exactly the sim's angle
    }
}
