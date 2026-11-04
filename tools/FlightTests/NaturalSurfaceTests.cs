using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty (2026-11-03): "i don't even want them to be perfectly flat same for grass runways they should get random
/// wavyness to them with some swooping gently like a banana and some having more of a crest and some just gently
/// undulating like natural hay fields often do". The grass and turf strips, the ski zones and the snow patches lie
/// that way in the ONE height function, so what the wheels ride is what is drawn; paved runways stay planes.
///
/// Kinds and amplitudes are LITERALS copied by hand from the shapes as generated (NaturalShapeParams; the
/// generator's site log for the patches). Changing a site's shape must be an edit here too.
/// </summary>
[Collection("WorldTerrainActive")]
public class NaturalSurfaceTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public NaturalSurfaceTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = new WorldTerrain(); WorldTerrain.SurfaceOverride = null; Atmosphere.SteadyWind = Vec3.Zero; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; Atmosphere.SteadyWind = Vec3.Zero; }

    // id, kind, amplitude (m): the shaped strips, in authoring order (kinds in turn: banana, crest, rolling).
    public static readonly (string id, string kind, double amp)[] Strips =
    {
        ("3U2 19/1", "banana", 2.09), ("KMEV 13G/31G", "crest", 1.71), ("AP1", "rolling", 0.0), ("AP3", "banana", 1.49),
        ("AP4", "crest", 1.25), ("AP5", "rolling", 0.0), ("AP6", "banana", 1.18), ("AP8", "crest", 2.00),
        ("AP14", "rolling", 0.0), ("Chamberlain", "banana", 1.71), ("Big Creek", "crest", 2.66), ("Thomas Creek", "rolling", 0.0),
        ("Sulphur Creek", "banana", 1.92),
    };

    private static double H(double x, double y) => WorldTerrain.Active!.HeightAt(x, y);
    private static double Ground(IslandPoint p) => H(p.N * 1000, p.E * 1000);

    /// <summary>The centreline profile of a strip: ground minus the strip's own plane, every 2 m, and the
    /// steepest added grade.</summary>
    private static (double lo, double hi, double ends, double grade) Profile(IslandField f)
    {
        double lo = 9, hi = -9, ends = 0, grade = 0, prev = double.NaN;
        for (double a = -f.Length / 2; a <= f.Length / 2 + 1e-6; a += 2)
        {
            var p = f.Centre + f.U * (a / 1000); double z = Ground(p) - f.Plane(p);
            lo = Math.Min(lo, z); hi = Math.Max(hi, z);
            if (!double.IsNaN(prev)) grade = Math.Max(grade, Math.Abs(z - prev) / 2);
            prev = z;
            if (Math.Abs(a) >= f.Length / 2 - 1) ends = Math.Max(ends, Math.Abs(z));
        }
        return (lo, hi, ends, grade);
    }

    [Fact]
    public void GrassStripsLieNaturallyAndPavedOnesStayPlanes()
    {
        var kinds = new HashSet<string>();
        foreach (var s in Strips)
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == s.id);
            Assert.Equal(s.kind, f.Shape.Name);
            var (lo, hi, ends, grade) = Profile(f);
            _o.WriteLine($"{s.id}: {s.kind} {lo:+0.00;-0.00}..{hi:+0.00;-0.00} m off its plane, added grade {grade * 100:F2} %, ends {ends:F3} m");
            // The thresholds stay where they are published: the shape is zero at both ends.
            Assert.InRange(ends, 0, .02);
            switch (s.kind)
            {
                case "banana": Assert.InRange(lo, -s.amp - .05, -s.amp + .05); Assert.InRange(hi, -.01, .02); Assert.InRange(grade, 0, .010); break;
                case "crest": Assert.InRange(hi, s.amp - .05, s.amp * 1.15); Assert.InRange(lo, -.02, .01); Assert.InRange(grade, 0, .010); break;
                default: Assert.InRange(Math.Max(hi, -lo), .15, .55); Assert.InRange(grade, 0, .026); break;
            }
            kinds.Add(s.kind);
        }
        Assert.Equal(3, kinds.Count);
        // Every grass/turf strip is on the list (Drake's runs into the cave and keeps its plane) ...
        Assert.Equal(Strips.Length, IslandTerrain.Fields.Count(f => (f.Surface.Contains("grass") || f.Surface == "turf") && f.Field != "DRAKE"));
        // ... and a paved runway is still exactly its plane.
        foreach (var f in IslandTerrain.Fields.Where(q => q.Surface == "asphalt"))
        {
            var (lo, hi, _, _) = Profile(f);
            Assert.True(Math.Max(hi, -lo) < .01, $"{f.Id} is {lo:F3}..{hi:F3} m off its plane");
        }
    }

    [Fact]
    public void ACrestHidesTheFarEndFromTheThreshold()
    {
        // AP8 (792 m, a 2.0 m crest): from a pilot's eye 1.8 m above one threshold, the far threshold's ground is
        // below the hump — you cannot see the far end until you are over the top.
        var f = IslandTerrain.Fields.Single(q => q.Id == "AP8");
        var near = f.Centre - f.U * (f.Length / 2000); var far = f.Centre + f.U * (f.Length / 2000);
        double eye = Ground(near) + 1.8, target = Ground(far);
        double clearest = double.PositiveInfinity;
        for (double t = .05; t < .95; t += .01)
        {
            var p = near + (far - near) * t; double sight = eye + (target - eye) * t;
            clearest = Math.Min(clearest, sight - Ground(p));
        }
        _o.WriteLine($"AP8: the line of sight from the threshold passes {clearest:+0.00;-0.00} m above the ground at its lowest");
        Assert.True(clearest < 0, "the far end is in view over the crest");
    }

    private static AircraftConfig Cub()
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", WheelSkiTests.Cub + ".json"));
        c.Skis = false; c.Bushwheels = true; return c;
    }

    /// <summary>Roll a Cub along a strip from `fromCentreM` (m along the strip from its centre; negative is short of
    /// it) at `speed`, idle, hands off. After the gear has settled from its resting estimate (1.5 s): how long its
    /// mains were off the ground, how far its CG's height above the ground strayed from its value then, and how much
    /// the ground under it rose (+) or fell along the run.</summary>
    private (double airborneS, double aglStray, double groundRise) RollAlong(IslandField f, double fromCentreM, double speed, double seconds)
    {
        AircraftConfig c = Cub();
        var start = f.Centre + f.U * (fromCentreM / 1000); double hd = f.Heading * Math.PI / 180;
        double x = start.N * 1000, y = start.E * 1000;
        RigidBodyState rest = LandingGear.RestingState(c, x, y, H(x, y), hd);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(speed, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac); double dt = SimLoop.DefaultFixedDtSec, air = 0, agl0 = double.NaN, stray = 0, g0 = double.NaN, g1 = 0;
        for (double t = 0; t < seconds; t += dt)
        {
            loop.RunFor(dt, new ControlInputs(0, 0, 0, 1));
            Vec3 p = ac.State.Position; double ground = H(p.X, p.Y), agl = -p.Z - ground;
            if (t < 1.5) { agl0 = agl; g0 = ground; continue; }
            stray = Math.Max(stray, Math.Abs(agl - agl0)); g1 = ground;
            if (!LandingGear.AnyMainWheelOnGround(c, ac.State)) air += dt;
        }
        return (air, stray, g1 - g0);
    }

    [Fact]
    public void ACubRollsOverTheCrestAndThroughTheRollsOnItsWheels()
    {
        // From 200 m short of AP8's 2 m crest, up its flank, and along AP1's hay-field rolls, at 23 kt, idle, hands
        // off: the ground under the Cub rises (AP8: cos^2 from t = -0.25 onward, ~0.6 m in the first 100 m) and the
        // aeroplane rides it — mains down throughout, the CG's height ABOVE the ground steady to a few centimetres.
        foreach (var (id, from, minRise) in new[] { ("AP8", -200.0, .4), ("AP1", -150.0, double.NegativeInfinity) })
        {
            var f = IslandTerrain.Fields.Single(q => q.Id == id);
            var r = RollAlong(f, from, 12, 12);
            _o.WriteLine($"{id}: mains off {r.airborneS:F2} s, CG height above ground strayed {r.aglStray:F3} m, ground under it rose {r.groundRise:+0.00;-0.00} m");
            Assert.True(r.airborneS < .1, $"{id}: mains off the ground {r.airborneS:F2} s");
            Assert.True(r.aglStray < .15, $"{id}: CG strayed {r.aglStray:F2} m from its height above the ground");
            Assert.True(r.groundRise > minRise, $"{id}: the ground rose only {r.groundRise:F2} m under it");
        }
    }

    [Fact]
    public void TheSkiZonesLieLikeGlacierSnow()
    {
        // Knik banana (0.97 m sag) and Fairweather crest (1.15 m), along each glacier's path through the zone: at the
        // centre the snow is 0.97 m below / 1.15 m above the zone's authored elevation, and the added grade is gentle.
        foreach (var (id, ft, centre) in new[] { ("Knik", 1100.0, -.97), ("Fairweather", 1300.0, 1.15) })
        {
            var q = IslandTerrain.World["glaciers"].Items.Single(g => g.S("id") == id); var c = q["ski_zone"]["centre"].Point;
            Assert.Equal(centre < 0 ? "banana" : "crest", NaturalShapeParams.SkiZone(id, 450, 100).Name);
            Assert.InRange(Ground(c) - ft * .3048, centre - .05, centre + .05);
            var path = q["path"].Items; var ax = path[^1].Point - path[^2].Point; ax = ax * (1 / ax.Length);
            double grade = 0, prev = double.NaN;
            for (double a = -140; a <= 140; a += 2)
            {
                double z = Ground(c + ax * (a / 1000));
                if (!double.IsNaN(prev)) grade = Math.Max(grade, Math.Abs(z - prev) / 2);
                prev = z;
            }
            _o.WriteLine($"{id}: centre {Ground(c) - ft * .3048:+0.00;-0.00} m off its elevation, steepest added grade {grade * 100:F2} % within 140 m");
            Assert.InRange(grade, .002, .015);
            Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(c.N * 1000, c.E * 1000));
        }
    }
}
