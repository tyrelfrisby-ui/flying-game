using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The horizontal tail's finite-span section table (AeroModel.FiniteSpanSection), owner 2026-11-03: "the pitts is still
/// spinning too flat … the elevator control is also weak at high aoa". The 2-D → finite-span rescale used to stretch the
/// whole α axis by a₀/a, which on the Pitts put the table's 90° point at 177°: a stab broadside to the flow read the section
/// at 51°, and its normal force was flat at ~0.83 from 35° to 90°, so the elevator moved nothing at α 45. Each point now
/// moves by its own lift's induced angle (nonlinear lifting line, NACA Report 865), which is the same rescale in the
/// linear range and nothing at all where the lift is zero.
/// </summary>
public class TailSectionTests
{
    private readonly ITestOutputHelper _out;
    public TailSectionTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static AirfoilTable PittsTail(out AircraftConfig cfg)
    {
        cfg = Load("pitts-s2b-like");
        var tables = Aircraft.BuildAirfoilTables(cfg);
        return tables[AeroModel.SectionTableKey(cfg, cfg.Surfaces.FindIndex(s => s.Id == "hStab"))!];
    }

    /// <summary>
    /// Broadside, the finite tail reads the section's own broadside data — copied by hand from pitts-s2b-like.json
    /// "naca0012-like" at α 90°: Cl 0.000, Cd 1.192. (The stretched table gave Cl 0.985, Cd 0.842 here.)
    /// </summary>
    [Fact]
    public void BroadsideTheTailIsAPlateNotAWing()
    {
        AirfoilTable t = PittsTail(out _);
        AeroCoefficients c = t.Sample(Math.PI / 2);
        Assert.InRange(c.Cl, -0.005, 0.005);
        Assert.InRange(c.Cd, 1.187, 1.197);
        // Tail-first: the section's 140° point (Cl −0.600, Cd 0.663) moves only by its own induced angle, −0.6·(1/a − 1/a₀),
        // so 140° reads close to it — the stretched table read the section at ~71° here (Cl +0.5).
        Assert.InRange(t.Sample(140 * Math.PI / 180).Cl, -0.62, -0.50);
    }

    /// <summary>
    /// Past the stall the separated surface's normal force rises toward broadside, as a plate's does (the section table's
    /// own CN: 1.17 at 40°, 1.19 at 90°). The stretched table held it flat at 0.81–0.84 from 45° to 90°.
    /// </summary>
    [Fact]
    public void NormalForceRisesPastTheStall()
    {
        AirfoilTable t = PittsTail(out _);
        double Cn(double deg) { var c = t.Sample(deg * Math.PI / 180); double a = deg * Math.PI / 180; return c.Cl * Math.Cos(a) + c.Cd * Math.Sin(a); }
        double Ca(double deg) { var c = t.Sample(deg * Math.PI / 180); double a = deg * Math.PI / 180; return c.Cd * Math.Cos(a) - c.Cl * Math.Sin(a); }
        for (int d = 35; d <= 90; d += 5) _out.WriteLine($"{d}: CN {Cn(d):F3} CA {Ca(d):F3}");
        Assert.True(Cn(50) > 1.10, $"CN(50) {Cn(50):F3}");
        Assert.InRange(Cn(90), 1.18, 1.20);
        // No leading-edge suction worth the name on a separated section: the stretched table had CA −0.99 at 90°.
        Assert.InRange(Ca(90), -0.01, 0.01);
    }

    /// <summary>In the linear range nothing changes: the derived table is the Helmbold rescale α₂D·a₀/a it replaced.</summary>
    [Fact]
    public void LinearRangeIsTheHelmboldRescale()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        AirfoilTableData raw = cfg.AirfoilTables["naca0012-like"];
        double a0 = 6.3, a = 3.2;    // section slope of naca0012-like about 0 (0.55 at 5°), and the tail's finite slope
        AirfoilTableData d = AeroModel.FiniteSpanSection(raw, a0, a);
        // The 5° point (Cl 0.55): induced angle 0.55·(1/3.2 − 1/6.3) rad = 4.84°, against the rescale's 5°·(6.3/3.2 − 1) = 4.84°.
        int i = Array.FindIndex(d.Cl, Array.FindIndex(d.AlphaRad, x => x > 0), c => Math.Abs(c - 0.55) < 1e-9);   // the positive branch
        Assert.InRange(d.AlphaRad[i] * 180 / Math.PI, 9.80, 9.89);
    }

    /// <summary>
    /// Every derived tail table in the fleet is a function of α (strictly increasing) that ends where the section ends: the
    /// fold past a sharp stall is resolved as the finite surface's stall jump, not left as two lifts at one angle.
    /// </summary>
    [Fact]
    public void EveryDerivedTailTableIsMonotonicAndEndsAtTheSectionsEnds()
    {
        int n = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json"))
        {
            AircraftConfig cfg;
            try { cfg = AircraftConfigLoader.LoadFromFile(file); } catch { continue; }
            if (cfg.Surfaces.Count == 0) continue;
            foreach (var (key, t) in AeroModel.DerivedSectionTables(cfg))
            {
                for (int k = 1; k < t.AlphaRad.Length; k++)
                    Assert.True(t.AlphaRad[k] > t.AlphaRad[k - 1], $"{cfg.Id} {key}: α not increasing at {k}");
                string baseAf = key[..key.IndexOf('@')];
                double[] src = cfg.AirfoilTables[baseAf].AlphaRad;
                Assert.Equal(src[^1], t.AlphaRad[^1], 6);     // Cl 0 at the section's last point (180°): no induced angle
                Assert.InRange(t.AlphaRad.Length, src.Length - 4, src.Length);   // the fold drops a point or two, never the data
                n++;
            }
        }
        _out.WriteLine($"{n} derived tail tables");
        Assert.True(n >= 20, $"only {n} derived tables found");
    }

    /// <summary>
    /// The elevator moves the tail at α 45. Full up against full down, static, no rotation, in Cm on the wing's area and
    /// mean chord: 0.003 with the stretched table (0.00 of its α 0 value), 0.135 now (0.21). At 60–75° it is small again,
    /// and that is the plate: broadside, turning the section changes its normal force very little.
    /// </summary>
    [Fact]
    public void PittsElevatorWorksAtAlpha45()
    {
        AirfoilTable _ = PittsTail(out AircraftConfig cfg);
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad;
        double DCm(double deg)
        {
            double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
            double m(double de) => AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0, new ControlDeflections(0, de, 0, 0)).Moment.Y;
            return Math.Abs(m(-e) - m(e));
        }
        double r45 = DCm(45) / DCm(0);
        _out.WriteLine($"elevator authority at 45 / at 0: {r45:F3}");
        Assert.True(r45 > 0.15, $"elevator at α 45 has {r45:F3} of its α 0 authority");
    }
}
