using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 08:37 (tools/own_models/extra_rear_diagonals.py, all three liveries):
///   "the diagonal tubes in the rear seat are in the same plane as the upper longerons they do not go up the go in"
///   "toward the center"
/// Literals copied by hand from the sill measured on the GLB (builder metres, x fwd, y left, z up): the upper longerons
/// are the canopy sills; their centre height, mean of left and right, is z 0.2606 at x -1.55 and z 0.2757 at x -1.182,
/// and the plane through both sills is that line, level across. The sills' inboard faces are at |y| 0.339 (x -1.55) and
/// 0.353 (x -1.182). The outermost rear-panel bezel (oil) reaches |y| 0.258; the padded tube's radius is 13 mm.
/// </summary>
public class ExtraRearDiagonalTests
{
    private static readonly string[] Liveries = { "extra-300-like-own", "extra-300-like-own-n300uy", "extra-300-like-own-unmarked" };
    public static IEnumerable<object[]> Files() => Liveries.Select(f => new object[] { f });

    private const double XAft = -1.55, XFwd = -1.182, ZAft = 0.2606, ZFwd = 0.2757, R = 0.013;
    private const double SillYAft = 0.339, SillYFwd = 0.353, OilBezelY = 0.258;
    private const double PlaneTol = 0.006;      // the sills themselves differ 4 mm left to right

    private static double ZPlane(double x) => ZAft + (x - XAft) / (XFwd - XAft) * (ZFwd - ZAft);
    private static double SillY(double x) => SillYAft + (x - XAft) / (XFwd - XAft) * (SillYFwd - SillYAft);

    private static Dictionary<string, List<(double x, double y, double z)>> Load(string file)
    {
        string path = Path.Combine(OwnPittsModelTests.Models, "extra-300-like", file + ".glb");
        return OwnPittsModelTests.Vertices(path).ToDictionary(kv => kv.Key,
            kv => kv.Value.Select(p => (-(double)p.x, (double)p.z, (double)p.y)).ToList());   // glTF -> builder
    }

    /// <summary>The tube's axis end nearest x = xEnd: the centre of the vertices within the end cap.</summary>
    private static (double x, double y, double z) End(List<(double x, double y, double z)> v, bool forward)
    {
        double xe = forward ? v.Max(p => p.x) : v.Min(p => p.x);
        var cap = v.Where(p => Math.Abs(p.x - xe) < 2 * R).ToList();
        return (forward ? xe - R : xe + R, cap.Average(p => p.y), cap.Average(p => p.z));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_sills_are_still_where_the_literals_say(string file)
    {
        // If the sill moves, the plane below is stale: re-measure and re-copy, do not loosen.
        var sill = Load(file)["Canopy_Sill"];
        foreach (double x in new[] { XAft, XFwd })
            foreach (int side in new[] { 1, -1 })
            {
                var cut = sill.Where(p => Math.Abs(p.x - x) < 0.035 && side * p.y > 0.25 && p.z > 0.2 && p.z < 0.32).ToList();
                Assert.True(cut.Count > 0, $"{file}: no sill at x {x} side {side}");
                double zc = (cut.Min(p => p.z) + cut.Max(p => p.z)) / 2;
                double xc = cut.Average(p => p.x);                            // sill stations are ~56 mm apart
                Assert.InRange(zc, ZPlane(xc) - PlaneTol, ZPlane(xc) + PlaneTol);
            }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Each_diagonal_lies_in_the_upper_longeron_plane(string file)
    {
        var m = Load(file);
        foreach (string tag in new[] { "L", "R" })
        {
            var v = m["Tube_P3h_PanelDiagonal_" + tag];
            foreach (bool fwd in new[] { false, true })
            {
                var e = End(v, fwd);
                Assert.True(Math.Abs(e.z - ZPlane(e.x)) < PlaneTol,
                    $"{file} {tag} {(fwd ? "forward" : "aft")} end z {e.z:F4} is off the longeron plane z {ZPlane(e.x):F4} at x {e.x:F3}");
            }
            // The whole tube, not just its ends: nothing climbs toward the canopy (the pass-13 tube rose 0.15 m).
            Assert.All(v, p => Assert.True(Math.Abs(p.z - ZPlane(p.x)) < R + PlaneTol,
                $"{file} {tag}: vertex z {p.z:F4} at x {p.x:F3} leaves the longeron plane"));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_diagonals_run_from_the_longerons_inboard_toward_the_centreline(string file)
    {
        var m = Load(file);
        foreach (var (tag, side) in new[] { ("L", 1), ("R", -1) })
        {
            var v = m["Tube_P3h_PanelDiagonal_" + tag];
            var aft = End(v, false); var fwd = End(v, true);
            Assert.True(side * aft.y > 0 && side * fwd.y > 0, $"{file} {tag} crosses the centreline");
            Assert.True(fwd.x > aft.x + 0.30, $"{file} {tag}: runs {fwd.x - aft.x:F3} m fore and aft");
            // Aft end against the sill's inboard face; forward end well inboard of the sill there.
            Assert.InRange(Math.Abs(aft.y) + R, SillY(aft.x) - 0.006, SillY(aft.x) + 0.003);
            Assert.True(Math.Abs(fwd.y) < SillY(fwd.x) - 0.06, $"{file} {tag}: forward end |y| {Math.Abs(fwd.y):F3} is not inboard of the longeron");
            Assert.True(Math.Abs(fwd.y) < Math.Abs(aft.y) - 0.04, $"{file} {tag}: does not converge ({Math.Abs(aft.y):F3} -> {Math.Abs(fwd.y):F3})");
            // ...but not across an instrument.
            Assert.True(Math.Abs(fwd.y) - R > OilBezelY, $"{file} {tag}: forward end covers the outer bezels");
        }
    }
}
