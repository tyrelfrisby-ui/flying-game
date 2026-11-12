using System.Linq;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE DRAWN EXHAUST PIPES ARE THE AUTHORED SMOKE PIPES — the model and the smoke read one set of numbers.
///
/// Ty, 2026-11-11, asked whether to re-cut the mesh or move the smoke back: <b>"Re-cut the mesh to match."</b>
///
/// What was wrong. `configs/aircraft/pitts-s2b-like.json` `smoke.pipes` carries his dimensions — two pipes centred at
/// the firewall (body x +0.574, measured as the aft face of the drawn engine), 1.5 in bore, 8 in apart on centre, 45°
/// down — and the sim has drawn the smoke out of them since 4ab04dfc. The MODEL still tubed its visible pipes to
/// (0.74, ±0.21, −0.63): on a plate `build_pitts_s2b.py` labelled "firewall" but which sits 0.166 m inside the same
/// model's own engine, and 0.21 m off the centreline. So the aeroplane showed smoke with no pipe at it and pipes with
/// no smoke. The builder now READS `smoke.pipes` for the station, the spacing, the bore and the aim, and these tests
/// are what keeps those two from drifting apart again: they measure the committed GLBs and compare them with the
/// config the sim loads, so neither can move without the other.
///
/// SmokeGeometryTests holds the config side (where the pipes are, how they aim, what they clear, how they pulse);
/// this file holds the MESH side, and only the mesh side.
///
/// Frames. Our exports put the model in the glTF frame (nose −x, up +y, span z) and the builder works in nose +x /
/// port +y / up +z, exporting as (−x, z, y). So the config's own body frame — x forward, y starboard, z DOWN, the
/// frame `gear.pos` and `smoke.pipes` are written in — comes back out of a GLB vertex as (−gx, −gz, −gy). That
/// mapping is not asserted here on faith: <see cref="TheGlbIsInTheConfigsOwnBodyFrame"/> re-proves it against
/// `gear.pos` and the lower-wing root LE datum before any of the pipe measurements are believed.
/// </summary>
public class SmokePipeMeshTests
{
    private readonly ITestOutputHelper _o;
    public SmokePipeMeshTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Every Pitts livery. All four carry the same engine and exhaust (only the propeller differs), so all
    /// four have to agree with the one config.</summary>
    public static TheoryData<string> Liveries() => new()
    {
        "-own", "-own-n1191", "-own-checker-unmarked", "-own-factory-unmarked",
    };

    private static string Glb(string suffix) =>
        Path.Combine(OwnPittsModelTests.Models, "pitts-s2b-like", "pitts-s2b-like" + suffix + ".glb");

    /// <summary>A node's vertices IN THE CONFIG'S BODY FRAME (x forward, y starboard, z down).</summary>
    private static Vec3[] Body(string suffix, string node)
    {
        var all = OwnPittsModelTests.Vertices(Glb(suffix));
        Assert.True(all.ContainsKey(node), Glb(suffix) + ": no node " + node);
        return all[node].Select(v => new Vec3(-v.x, -v.z, -v.y)).ToArray();
    }

    // ------------------------------------------------------------------------------------ the frame, re-proved

    /// <summary>The frame proof the pipe measurements rest on, the same two datums the stations were measured by:
    /// the main wheel's axle against <c>gear.pos</c>, and the lower wing's root leading edge at the origin.</summary>
    [Fact]
    public void TheGlbIsInTheConfigsOwnBodyFrame()
    {
        var cfg = Load("pitts-s2b-like");
        GearConfig main = cfg.Gear.First(g => !g.IsTailwheel);
        Vec3[] wheels = Body("-own", "Wheels");
        // The starboard main wheel: the config's y is + starboard, and the tailwheel's tyre is much smaller.
        Vec3[] stbd = wheels.Where(v => v.Y > 0.3).ToArray();
        double axleY = 0.5 * (stbd.Min(v => v.Y) + stbd.Max(v => v.Y));
        double contactZ = stbd.Max(v => v.Z);                       // + is DOWN, so the tread is the maximum
        double axleX = 0.5 * (stbd.Min(v => v.X) + stbd.Max(v => v.X));
        _o.WriteLine($"main wheel: x {axleX:F4} y {axleY:F4} tread z {contactZ:F4}  vs gear.pos [{main.Pos[0]:F3}, {main.Pos[1]:F3}, {main.Pos[2]:F3}]");
        Assert.Equal(System.Math.Abs(main.Pos[1]), axleY, 3);        // track
        Assert.Equal(main.Pos[2], contactZ, 2);                      // the contact point is the tyre's bottom
        Assert.InRange(axleX - main.Pos[0], -0.03, 0.03);            // station, to the width of the tyre's footprint

        // The datum: the lower wing's root leading edge is the origin, and its chord is 1.016 m (40 in).
        Vec3[] lower = Body("-own", "Wing_Lower");
        Assert.Equal(0.0, lower.Max(v => v.X), 3);
        Assert.Equal(1.016, lower.Max(v => v.X) - lower.Min(v => v.X), 3);
    }

    // ------------------------------------------------------------------------------- the mouths, against the config

    /// <summary>Each drawn pipe ENDS in a flat mouth that is the authored one: at the firewall station, on the
    /// centreline at ±½ the authored spacing, bored to the authored diameter, and cut square to the authored 45°
    /// down aim — which is what makes the mesh's mouth the same place and the same size as the sim's.</summary>
    [Theory]
    [MemberData(nameof(Liveries))]
    public void TheDrawnPipeMouthsAreTheAuthoredSmokePipes(string suffix)
    {
        var cfg = Load("pitts-s2b-like");
        SmokePipesConfig pipes = cfg.Smoke!.Pipes!;
        Assert.True(pipes.TryPipes(out Vec3[] mouths, out Vec3[] aim), "pitts-s2b-like: smoke.pipes is half-authored");
        Vec3[] pipe = Body(suffix, "Exhaust");
        double bore = pipes.DiameterM;

        double sumY = 0;
        for (int i = 0; i < mouths.Length; i++)
        {
            // This pipe's own vertices: the bank is spaced in y, so nearest-in-y assigns them whatever the count is.
            Vec3[] mine = pipe.Where(v => mouths.Select((m, k) => (k, d: System.Math.Abs(v.Y - m.Y)))
                                                .OrderBy(t => t.d).First().k == i).ToArray();
            Assert.True(mine.Length > 20, $"{suffix}: pipe {i} has only {mine.Length} vertices");

            // The mouth is the end cap: the ring of vertices furthest along the way this pipe blows.
            double far = mine.Max(v => Vec3.Dot(v, aim[i]));
            Vec3[] cap = mine.Where(v => Vec3.Dot(v, aim[i]) > far - 1e-5).ToArray();
            Assert.True(cap.Length >= 8, $"{suffix}: pipe {i}'s mouth is not a ring ({cap.Length} vertices at the end)");

            var centre = new Vec3(cap.Average(v => v.X), cap.Average(v => v.Y), cap.Average(v => v.Z));
            _o.WriteLine($"{suffix} pipe {i}: {cap.Length} cap vertices, mouth ({centre.X:F5}, {centre.Y:F5}, {centre.Z:F5})");
            Assert.Equal(mouths[i].X, centre.X, 4);                  // the firewall station
            Assert.Equal(mouths[i].Y, centre.Y, 4);                  // ± half the authored spacing
            Assert.Equal(mouths[i].Z, centre.Z, 4);                  // the authored mouth line
            sumY += centre.Y;

            foreach (Vec3 v in cap)
            {
                Vec3 r = v - centre;
                // Bored to the authored diameter, to a tenth of a millimetre. (An absolute tolerance, not decimal
                // places: half a 1.5 in bore is 0.019050 m, which sits exactly on Assert.Equal's rounding boundary.)
                Assert.InRange(r.Length, 0.5 * bore - 1e-4, 0.5 * bore + 1e-4);
                Assert.InRange(Vec3.Dot(r, aim[i]), -1e-4, 1e-4);    // and cut square to the authored aim: so the
            }                                                        // mouth faces exactly 45° down, like the smoke
        }
        Assert.Equal(0.0, sumY / mouths.Length, 4);                  // the bank is centred on the centreline

        // Nothing hangs on past the mouths: the aft-most and the lowest the drawn pipes reach IS the mouth ring,
        // whose rim stands half a bore out from the mouth centre across the 45° plane — so the pipe really ends at
        // the firewall, instead of running on to 0.166 m ahead of it as it did until 2026-11-11. The ring is sampled
        // at 12 points, so the extreme vertex falls just inside that bound, not on it.
        double down = pipes.DownDeg * System.Math.PI / 180;
        double aftRim = 0.5 * bore * System.Math.Sin(down), downRim = 0.5 * bore * System.Math.Cos(down);
        Assert.InRange(pipe.Min(v => v.X), mouths.Min(m => m.X) - aftRim, mouths.Min(m => m.X) - 0.95 * aftRim);
        Assert.InRange(pipe.Max(v => v.Z), mouths.Max(m => m.Z) + 0.95 * downRim, mouths.Max(m => m.Z) + downRim);
    }

    // ------------------------------------------------------------------ the plate that used to be called a firewall

    /// <summary>The plate at the firewall is AT the firewall. `build_pitts_s2b.py` drew it at body x 0.740 and
    /// labelled it "firewall" — 0.166 m forward of the aft face of its own engine, cutting through the rearmost
    /// starboard cylinder, and with only 0.86 m from the prop flange for a 0.989 m AEIO-540. The old smoke outlet was
    /// authored off that label, which is the bug Ty reported. It has been MOVED, not renamed, so that:
    ///  - the engine still ends exactly on the config's firewall station, which is the convention that station was
    ///    measured by (`smoke.pipes.stationSource`), and
    ///  - there is no second plate left standing at 0.740 for the next reader to measure a firewall off.</summary>
    [Theory]
    [MemberData(nameof(Liveries))]
    public void TheFirewallPlateIsDrawnAtTheMeasuredFirewall(string suffix)
    {
        var cfg = Load("pitts-s2b-like");
        double firewall = cfg.Smoke!.Pipes!.FirewallXM;
        Vec3[] engine = Body(suffix, "Engine");

        // The station the firewall was measured as: the aft face of the drawn engine.
        Assert.Equal(firewall, engine.Min(v => v.X), 4);

        // A transverse plate is a station carrying four or more vertices that span most of the cowl in BOTH y and z
        // (the cooling-inlet ducts span y at a station, but only 0.12 m of z).
        double[] plates = engine.GroupBy(v => System.Math.Round(v.X, 4))
            .Where(g => g.Count() >= 4 && g.Max(v => v.Y) - g.Min(v => v.Y) > 0.5
                                       && g.Max(v => v.Z) - g.Min(v => v.Z) > 0.4)
            .Select(g => g.Key).OrderBy(x => x).ToArray();
        _o.WriteLine($"{suffix}: transverse plate faces at x {string.Join(", ", plates.Select(x => x.ToString("F4")))}");
        Assert.NotEmpty(plates);
        Assert.Equal(firewall, plates.Min(), 4);                       // its aft face is the firewall
        Assert.True(plates.Max() < firewall + 0.05,
            $"{suffix}: a transverse plate still stands at x {plates.Max():F3}, forward of the firewall at {firewall:F3}");
    }
}
