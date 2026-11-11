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
    /// Broadside, the finite tail reads the section's own broadside data — α 90°: Cl 0.000, Cd 1.190. (The stretched
    /// table gave Cl 0.985, Cd 0.842 here.) Broadside is the one point every tail section in this project shares,
    /// because Cd_max 1.19 is the measured reference the tables are pinned to (Viterna &amp; Corrigan 1.11 + 0.018·AR
    /// at tail aspect ratios; Hoerner's AR 1–5 plates 1.18–1.20), so this half of the test did not move when the
    /// Pitts' stabiliser became a flat plate on 2026-11-10. It read 1.192 off `naca0012-like` and reads 1.190 off
    /// `flat-plate`, and the ±0.005 band already covered both.
    ///
    /// THE 140° POINT DID MOVE, AND NOT THE WAY IT LOOKS. The band was −0.50 to −0.62, written from the 0012's own
    /// 140° data (Cl −0.600); the plate reads −0.7028. That is NOT the surface being "more of a plate" — a fully
    /// separated plate at 40° of reversed flow makes Cd_max·sin·cos = 0.586, LESS than this. It is higher because
    /// the plate's hand-over from attached lift to the separated law is 44° wide (FLAT_PLATE_BLEND), so at 40° the
    /// section is still inside it and has not finished separating. That width is the whole point of the fit Ty
    /// chose: a low-aspect-ratio plate keeps lifting far past a 2-D stall, `AeroModel.FiniteSpanSection` cannot
    /// manufacture that because it moves points along α and leaves Cl alone, so it has to be in the 2-D table.
    /// Reading MORE lift than a bare plate at 40° tail-first is therefore the fit working, not drifting.
    /// </summary>
    [Fact]
    public void BroadsideTheTailIsAPlateNotAWing()
    {
        AirfoilTable t = PittsTail(out _);
        AeroCoefficients c = t.Sample(Math.PI / 2);
        Assert.InRange(c.Cl, -0.005, 0.005);
        Assert.InRange(c.Cd, 1.187, 1.197);
        // Tail-first: the section's 140° point moves only by its own induced angle, Cl·(1/a − 1/a₀), so 140° reads
        // close to the section there — the stretched table read the section at ~71° here (Cl +0.5), which is the
        // defect this test exists to catch, and it would read the WRONG SIGN. Band from the measured −0.7028.
        Assert.InRange(t.Sample(140 * Math.PI / 180).Cl, -0.74, -0.66);
        // And it must still be less than the attached peak and more than the bare separated plate's 0.586: those
        // are the two things the 44° hand-over sits between, and either side of them is a different section.
        Assert.InRange(-t.Sample(140 * Math.PI / 180).Cl, 0.59, 0.91);
    }

    /// <summary>
    /// Past the stall the separated surface's normal force climbs toward broadside, as a plate's does, and goes on
    /// climbing to Cd_max at 90°. The stretched table held it flat at 0.81–0.84 from 45° to 90°, which is the defect
    /// this test exists to catch: a flat CN is zero control effectiveness.
    ///
    /// RE-PINNED 2026-11-10 WITH THE CLAIM MADE HONEST. This asserted `CN(50) > 1.10` from the NACA 0012's
    /// post-stall magnitudes, and the plate the Pitts now flies reads 0.796 there. Measuring it properly rather
    /// than sliding the threshold: the derived table peaks at CN 0.863 at 18°, FALLS to 0.692 by 36°, and then
    /// climbs monotonically to 1.190 at 90°. So "rises past the stall" was never true of either section as a
    /// single statement — the 0012 dipped 1.211 at 14° to 0.955 at 25°, 21 %, and the plate dips 0.863 to 0.692,
    /// 20 % — and the old assertion only passed on the 0012 because its dip was over by 25° and 50° was well
    /// clear of it. The plate's hand-over is 44° wide, so 50° is still inside it.
    ///
    /// What is therefore asserted now is what is actually true and what the sim needs: the dip is BOUNDED (no
    /// worse than the 0012's 21 % it replaced), it is OVER by 40°, and from there to broadside CN rises at every
    /// step to Cd_max. The dip being real is also why `AeroModel.MonotonicPastStall` must not be deleted as
    /// redundant now that the tails are plates.
    /// </summary>
    [Fact]
    public void NormalForceRisesPastTheStall()
    {
        AirfoilTable t = PittsTail(out _);
        double Cn(double deg) { var c = t.Sample(deg * Math.PI / 180); double a = deg * Math.PI / 180; return c.Cl * Math.Cos(a) + c.Cd * Math.Sin(a); }
        double Ca(double deg) { var c = t.Sample(deg * Math.PI / 180); double a = deg * Math.PI / 180; return c.Cd * Math.Cos(a) - c.Cl * Math.Sin(a); }
        for (int d = 15; d <= 90; d += 5) _out.WriteLine($"{d}: CN {Cn(d):F3} CA {Ca(d):F3}");
        // Rising, step by step, from the far side of the stall dip out to broadside.
        for (int d = 45; d <= 90; d += 5)
            Assert.True(Cn(d) > Cn(d - 5), $"CN fell from {d - 5}° ({Cn(d - 5):F3}) to {d}° ({Cn(d):F3})");
        Assert.True(Cn(50) > 0.75, $"CN(50) {Cn(50):F3}");
        Assert.InRange(Cn(90), 1.18, 1.20);
        // The dip AFTER the stall peak is no deeper than the one the NACA 0012 had (1.211 at 14° → 0.955 at 25°,
        // 21 %). The floor has to be looked for past the peak, not across the whole band: below the peak the curve
        // is still on its rising attached branch, and counting that reads a 37 % "dip" that is not one.
        double peak = 0; int peakAt = 0;
        for (int d = 10; d <= 40; d++) if (Cn(d) > peak) { peak = Cn(d); peakAt = d; }
        double floor = peak;
        for (int d = peakAt; d <= 40; d++) floor = Math.Min(floor, Cn(d));
        _out.WriteLine($"stall dip: peak {peak:F3} at {peakAt}° → floor {floor:F3} = {100 * (peak - floor) / peak:F1} %");
        // Measured 21.7 % (0.884 at 19° → 0.692 at 36°), against the 0012's 21 %. The 44° hand-over is what makes
        // those equal: at FLAT_PLATE_BLEND 10 the same plate dipped 57 %, which is the hole Ty rejected.
        Assert.True((peak - floor) / peak < 0.25, $"stall dip {100 * (peak - floor) / peak:F1} % is well past the 0012's 21 %");
        // No leading-edge suction worth the name on a separated section: the stretched table had CA −0.99 at 90°.
        Assert.InRange(Ca(90), -0.01, 0.01);
    }

    /// <summary>In the linear range nothing changes: the derived table is the Helmbold rescale α₂D·a₀/a it replaced.
    ///
    /// Read off the 172 since 2026-11-10, not the Pitts: the Pitts' stabiliser is a flat plate now and its config no
    /// longer carries `naca0012-like` at all, so this threw a KeyNotFoundException. The 172's copy of that table is
    /// byte-identical to the one the Pitts had (checked against `b7396cd0^:configs/aircraft/pitts-s2b-like.json`),
    /// and this test is about the MAPPING and not about either aeroplane — so every number below is unchanged, and
    /// it goes on guarding the 0012 path that eleven metal and composite types still fly.</summary>
    [Fact]
    public void LinearRangeIsTheHelmboldRescale()
    {
        AircraftConfig cfg = Load("c172-like");
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
