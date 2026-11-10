using System.Text.Json;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Pitts S-2B rear cockpit, v3.1 (Ty 2026-11-03: "The two in back are good but lower the panel 2 inches and make
/// the tubes conform to the panel so there is less forward obstruction"). tools/own_models/pitts_cockpit_v3.py put
/// the rear panel assembly 77.8 mm below where the base model had it (27 mm in v3, 50.8 mm more in v3.1) with the
/// eye unchanged, and rebuilt the two rear canopy bows so that, seen from the rear eye, they run just under the
/// lowered rim instead of standing 50 mm above it. These pin those numbers as literals (copied from the change, not
/// read back from the builder) in every livery, and that the switch row, the bows and the latch went down with
/// them. The full skin / stick / leg containment check is tools/own_models/check_pitts_rear_view.py.
/// glTF frame: x = aft, y = up, z = pilot left.
/// </summary>
public class PittsRearViewTests
{
    private static string Glb(string s) => Path.Combine(OwnPittsModelTests.Models, "pitts-s2b-like", "pitts-s2b-like" + s + ".glb");

    // The rear eye (configs/aircraft/pitts-s2b-like.json, rear seat; builder (-1.325, 0, 0.300)) in glTF.
    private const double EyeAft = 1.325, EyeUp = 0.300;
    private const double RimTop = 0.2190;       // 0.2968 before v3, - 0.027 (v3) - 0.0508 (v3.1)
    private const double RimAft = 0.808;        // rim centre station, unchanged

    private static double Max(List<(float x, float y, float z)> v) => v.Max(p => p.y);

    [Theory]
    [InlineData("-own")] [InlineData("-own-checker-unmarked")] [InlineData("-own-factory-unmarked")] [InlineData("-own-n1191")]
    public void The_rear_panel_is_two_inches_lower_and_its_switch_row_went_with_it(string livery)
    {
        var v = OwnPittsModelTests.Vertices(Glb(livery));
        Assert.InRange(Max(v["Panel_Rim_Rear"]), RimTop - 0.0005, RimTop + 0.0005);
        // From the unchanged rear eye the panel top is now 8.9 deg below the horizon (it was 3.3 at v3, 0.35 before).
        double deg = Math.Atan2(RimTop - EyeUp, EyeAft - RimAft) * 180 / Math.PI;
        Assert.InRange(deg, -9.0, -8.8);
        // The switch row's legends and the bottom of the panel extension moved the same 50.8 mm (v3: -0.201 / -0.229).
        // The legends are v4's MASTER / ALTERNATOR / FUEL PUMP / SMOKE strip (pitts_reference_visuals.py), on the old row.
        Assert.InRange(v["Pitts_Reference_Switch_Legends"].Min(p => p.y), -0.2522, -0.2512);
        Assert.InRange(v["Pitts_RearPanel_Extension"].Min(p => p.y), -0.2803, -0.2793);
        // Nothing of the switch row sits above the instruments or below the extension.
        double floor = v["Pitts_RearPanel_Extension"].Min(p => p.y);
        foreach (var (name, pts) in v)
            if ((name.StartsWith("Ctl_") || name.StartsWith("Mount_")) && !name.EndsWith("_Front") && pts.Average(p => p.x) > 0.7 && pts.Average(p => p.x) < 0.9)
                Assert.True(pts.Min(p => p.y) > floor && pts.Max(p => p.y) < RimTop, name + " is off the rear panel");
    }

    [Theory]
    [InlineData("-own")] [InlineData("-own-checker-unmarked")] [InlineData("-own-factory-unmarked")] [InlineData("-own-n1191")]
    public void The_two_rear_bows_stay_and_sit_under_the_panel_line_from_the_rear_eye(string livery)
    {
        var v = OwnPittsModelTests.Vertices(Glb(livery));
        foreach (string bow in new[] { "Canopy_Bow_F", "Canopy_Bow_R" })
        {
            Assert.True(v.ContainsKey(bow) && v[bow].Count > 100, bow + " is gone: Ty kept the two rear bows");
            // The crown of each bow, where the panel line is flat: below the eye-to-rim-top sight line at its station.
            var crown = v[bow].Where(p => Math.Abs(p.z) < 0.02).ToList();
            double aft = crown.Average(p => p.x), top = crown.Max(p => p.y);
            double line = EyeUp + (RimTop - EyeUp) * (EyeAft - aft) / (EyeAft - RimAft);
            Assert.True(top <= line + 0.0005, $"{bow} crown {top:F4} m is above the panel line {line:F4} m at x {aft:F3}");
            Assert.True(top > line - 0.004, $"{bow} crown {top:F4} m is not on the panel line {line:F4} m (it should trace it)");
        }
        // The latch block rides on the bows (v3: 0.304 .. 0.328); it must not stand over the panel any more than that.
        var block = v["Canopy_Latch_Block"];
        Assert.InRange(block.Max(p => p.y), 0.230, 0.234);
        // The front bows stay removed (v3: "remove the bars across the front of the canopy").
        Assert.False(v.ContainsKey("Canopy_Bow_F_Front") || v.ContainsKey("Canopy_Bow_R_Front"));
    }
}
