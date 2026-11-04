using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The real strut stations of every strut-braced type and biplane (2026-11-03), and the Pitts slave strut's real horns.
/// Every expected value is a literal copied by hand from the cited source (inches as published, converted at 0.0254),
/// never read from the config being checked.
/// </summary>
public class StrutStationTests
{
    private readonly ITestOutputHelper _out;
    public StrutStationTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private const double Deg = Math.PI / 180.0;
    private const double In = 0.0254;

    public static IEnumerable<object[]> Stations() => new[]
    {
        // C172: Cessna 172 1969-76 Service Manual Fig. 1-2, wing strut attach rib WS 100.50 (from BL 0).
        new object[] { "c172-like", "wing", 100.50 * In },
        // J-3 / PA-18: own J-3 model build_j3_cub.py Y_ST = 3.40 m (133.9 in); Piper spar drawing 14383 + supercub.org
        // spar measurements give 135-136.5 in, the PA-18 three-view 129-131 in.
        new object[] { "j3-cub-like", "wing", 3.40 },
        new object[] { "j3-cub-floats-like", "wing", 3.40 },
        new object[] { "pa18-cub-like", "wing", 3.40 },
        new object[] { "pa18-bush-like", "wing", 3.40 },
        new object[] { "pa18-floats-like", "wing", 3.40 },
        // PA-25: SSA/AIRW-D0026 Pawnee front-spar stress analysis Figs 15-1/15-2, strut at WS 139 in (from the centreline).
        new object[] { "pa25-pawnee-like", "wing", 139.0 * In },
        // DHC-2: ~122 in from the centreline, scaled off the L-20A three-view (48 ft span); 3.10 m.
        new object[] { "dhc2-beaver-like", "wing", 3.10 },
        new object[] { "dhc2-beaver-floats-like", "wing", 3.10 },
        // SGS 2-33: own model build_sgs233.py yw = 3.13 m (three-view check ~125 in).
        new object[] { "glider-2-33-like", "wing", 3.13 },
        // Stearman: US Navy N2S three-view, N-strut upper 137 in, lower 128 in from the centreline.
        new object[] { "stearman-pt17-like", "wing-upper", 137.0 * In },
        new object[] { "stearman-pt17-like", "wing-lower", 128.0 * In },
        // JN-4D: outer interplane strut 178 in from the centreline, both wings (line three-view, Wikimedia Commons).
        new object[] { "jn4-jenny-like", "wing-upper", 178.0 * In },
        new object[] { "jn4-jenny-like", "wing-lower", 178.0 * In },
    };

    [Theory]
    [MemberData(nameof(Stations))]
    public void BreakStationIsTheRealStrutAttach(string type, string surface, double expectedM)
    {
        AircraftConfig c = Load(type);
        List<WingPanel> panels = WingStructure.Build(c).Where(p => p.SurfaceId == surface).ToList();
        Assert.Equal(2, panels.Count);
        foreach (WingPanel p in panels)
        {
            _out.WriteLine($"{type} {surface} side {p.Side}: station {p.BreakY:F3} m ({p.BreakY / In:F1} in) [{p.StationSource}], tip {p.TipY:F3}");
            Assert.Equal(expectedM, p.BreakY, 2);
            Assert.StartsWith("measured", p.StationSource);
            Assert.True(p.BreakY < p.TipY, "the station lies inside the wing");
        }
        Assert.False(string.IsNullOrWhiteSpace(c.WingBreakStationSource), "every measured station names its source");
    }

    [Theory]
    // J-3: build_j3_cub.py F_ST (2.05, 0.325, -0.57) against its wing LE 1.405 m aft of the hub, chord line 0.59 m above the
    // thrust line: 0.645 m aft of the LE, BL 0.325, 1.16 m below; config root LE x 0.407, chord line z -0.75.
    [InlineData("j3-cub-like", -0.24, 0.325, 0.41)]
    [InlineData("j3-cub-floats-like", -0.24, 0.325, 0.41)]
    // 2-33: build_sgs233.py STRUT_FUS (mx(394), side_y, mz(266)): 0.519 m aft of the root LE, BL 0.336, 1.054 m below.
    [InlineData("glider-2-33-like", -0.07, 0.336, 0.754)]
    public void LiftStrutFuselageFittingIsBelowAndInboardOfTheWingEnd(string type, double x, double y, double z)
    {
        AircraftConfig c = Load(type);
        Assert.NotNull(c.LiftStrutFuselageAttach);
        double[] f = c.LiftStrutFuselageAttach!;
        Assert.Equal(x, f[0], 3); Assert.Equal(y, f[1], 3); Assert.Equal(z, f[2], 3);
        double station = c.WingBreakStationY!["wing"];
        double wingZ = c.Surfaces.First(s => s.Id == "wing").Strips[0].Pos[2];
        // The strut runs out and UP from the fuselage to the wing (+z is down).
        Assert.True(f[1] < station && f[2] > wingZ);
        double len = Math.Sqrt(Math.Pow(station - f[1], 2) + Math.Pow(f[2] - wingZ, 2));
        _out.WriteLine($"{type}: strut span/height run {len:F3} m ({len / In:F0} in), {Math.Atan2(f[2] - wingZ, station - f[1]) / Deg:F1}° to the horizontal");
    }

    [Fact]
    public void PittsHornsAreParallelSoTheUppersMoveOneToOne()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        // S-1-11 Detail A: hinge-to-pin ~20° below the forward chord line; the upper horn parallel and equal (parallelogram).
        Assert.Equal(20.0, c.SlaveStrut!.DrivenHornDownDeg);
        Assert.Equal(20.0, c.SlaveStrut.SlavedHornDownDeg);
        List<SlaveLink> links = SlaveLink.Build(c);
        Assert.Equal(2, links.Count);
        foreach (SlaveLink l in links)
        {
            Vec3 hd = l.DrivenPin - l.DrivenHinge, hs = l.SlavedPin - l.SlavedHinge;
            Assert.True(hd.X > 0 && hd.Z > 0 && hs.X > 0 && hs.Z > 0, "both pins ahead of and below their hinge lines");
            Assert.Equal(0.072, hd.Length, 4);
            Assert.Equal(0.072, hs.Length, 4);
            foreach (double d in new[] { -25.0, -12.0, 0.0, 6.0, 12.0, 25.0 })
            {
                double s = l.Solve(d * Deg, null, null, d * Deg, 0.7) / Deg;
                _out.WriteLine($"side {l.Side}: lower {d,6:F1}° -> upper {s,7:F3}°");
                Assert.InRange(s, d - 0.3, d + 0.3);
            }
        }
    }

    [Fact]
    public void TheOldModelsOppositeHornsWouldBarelyMoveTheUppers()
    {
        // The mechanism the fix removes, measured: the GLB's lower horn stood ABOVE the lower wing (pin ~0.040 ahead,
        // ~0.060 up: 56° above forward) while the upper hung BELOW the upper wing (56° below). Across the strut their arms
        // are 0.008 / 0.063 m, so the uppers would move at ~0.12 of the lowers (measured 2026-11-03: 0.118 at 1°, 0.076 at 5°).
        AircraftConfig c = Load("pitts-s2b-like");
        c.SlaveStrut!.DrivenHornDownDeg = -56; c.SlaveStrut.SlavedHornDownDeg = 56;
        SlaveLink l = SlaveLink.Build(c).First(k => k.Side > 0);
        Vec3 u = (l.SlavedPin - l.DrivenPin).Normalized();
        double armD = Vec3.Dot(Vec3.Cross(l.DrivenAxis, l.DrivenPin - l.DrivenHinge), u), armS = Vec3.Dot(Vec3.Cross(l.SlavedAxis, l.SlavedPin - l.SlavedHinge), u);
        foreach (double d in new[] { 1.0, 5.0 })
        {
            double s = l.Solve(d * Deg, null, null, 0.2 * d * Deg, 0.7) / Deg;
            _out.WriteLine($"opposite horns: lower {d}° -> upper {s:F3}° (ratio {s / d:F3}; arms across the strut {armD:F4} / {armS:F4} m)");
        }
        double r = l.Solve(1 * Deg, null, null, 0.2 * Deg, 0.7) / 1.0 / Deg;
        Assert.InRange(r, 0.05, 0.35);
    }
}
