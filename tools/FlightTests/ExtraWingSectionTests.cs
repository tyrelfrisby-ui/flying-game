using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The Extra 300L's wing sections, DERIVED (geometry and XFOIL from lane/claude-inertia runs 4/5e, 2026-11-02;
/// maximum lift scaled to measured data 2026-11-10 by Ty's decision; docs/aircraft/extra-300-airfoil-DERIVATION.md
/// sec. 1f; tools/aircraft_builder/extra_wing_table.py). Candidate B: the lane owner's "ice cream cone" reshape of the
/// published Eppler E473 root / E472 tip (UIUC) at full thickness (16.2 / 12.1 %), standing in for the unpublished
/// MA 15 S / MA 12 S, which exist by NAME ONLY -- no coordinates for them are published anywhere. XFOIL 6.99 at each
/// station's 1-g-stall Reynolds number gives the SHAPE of the curve and the post-break decline; the lift SLOPE is
/// XFOIL x K(Re), K being Ladson's measured/XFOIL ratio for the NACA 0012 (NASA TM 4074); and cl_max IS Ladson's
/// MEASURED 0012 cl_max at the station's Reynolds number, with the break where the corrected curve reaches it. Stall
/// type from Gault (NACA TN 3963) -- trailing-edge at every station, so XFOIL's own decline follows the break; each
/// station carried to the finite wing by its own lifting-line induced angle.
///
/// Every expected value is a LITERAL copied by hand from the derivation, so moving a number in the config has to be a
/// deliberate edit here too (CLAUDE.md: a test that reads the file it is checking proves nothing).
/// </summary>
public sealed class ExtraWingSectionTests
{
    private readonly ITestOutputHelper _out;
    public ExtraWingSectionTests(ITestOutputHelper o) { _out = o; }

    private const double D2R = Math.PI / 180.0;
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>
    /// Derivation sec. 1f (WIRED, Ty 2026-11-10), per station root -> tip: finite-wing (3-D) stall angle, deg; section
    /// cl_max; the fraction of that peak left one degree (3-D) past it. Candidate B at full thickness; Gault puts every
    /// station in the trailing-edge band.
    ///
    /// cl_max IS LADSON'S MEASURED NACA 0012 cl_max at the station's own Reynolds number (NASA TM 4074, free
    /// transition, log-interpolated over the three Reynolds numbers whose peak he brackets and held at his 2.00 M value
    /// below it -- which is why s6, s7 and s8 all read 1.408). Ty, on run 5e's honest result that the XFOIL-peak
    /// section carried 0.15-0.30 more than any MEASURED section of its thickness at its Reynolds number: "scale the
    /// cl_max to the measured data ... XFOIL's optimism on the ice-cream-cone shape is not evidence." Run 5e's values
    /// -- 23.4 deg / 1.706 down to 53.8 deg / 1.558 -- are in derivation sec. 1e and are still reproducible with
    /// `extra_wing_table.py --method r5e`.
    /// </summary>
    public static readonly (string Table, double StallDeg3D, double ClMax, double OneDegOn)[] Stations =
    {
        ("extra-ma-s1", 20.0, 1.530, 0.701),   // y 0.390  t/c 15.99 %  Re 3.40 M  y(1.25 %) 2.75
        ("extra-ma-s2", 19.0, 1.505, 0.738),   // y 1.155  t/c 15.53 %  Re 3.04 M  y(1.25 %) 2.71
        ("extra-ma-s3", 18.5, 1.477, 0.730),   // y 1.875  t/c 14.99 %  Re 2.70 M  y(1.25 %) 2.67
        ("extra-ma-s4", 18.2, 1.450, 0.907),   // y 2.523  t/c 14.37 %  Re 2.40 M  y(1.25 %) 2.61  <- the wing goes here
        ("extra-ma-s5", 18.5, 1.423, 0.740),   // y 3.075  t/c 13.70 %  Re 2.14 M  y(1.25 %) 2.55
        ("extra-ma-s6", 20.3, 1.408, 0.715),   // y 3.508  t/c 13.05 %  Re 1.94 M  y(1.25 %) 2.50
        ("extra-ma-s7", 26.3, 1.408, 0.622),   // y 3.806  t/c 12.51 %  Re 1.80 M  y(1.25 %) 2.45
        ("extra-ma-s8", 47.9, 1.408, 0.627),   // y 3.958  t/c 12.19 %  Re 1.72 M  y(1.25 %) 2.42
    };

    [Fact]
    public void EachStationStallsWhereTheDerivationPutsIt()
    {
        var tables = Aircraft.BuildAirfoilTables(Load("extra-300-like"));
        foreach (var (name, stall, clmax, _) in Stations)
        {
            AirfoilTable t = tables[name];
            double peak = t.Sample(t.AlphaClMaxRad).Cl;
            _out.WriteLine($"{name}: stall {t.AlphaClMaxRad / D2R:F2} deg, cl_max {peak:F3}");
            Assert.InRange(t.AlphaClMaxRad / D2R, stall - 0.3, stall + 0.3);
            Assert.InRange(peak, clmax - 0.01, clmax + 0.01);
            // symmetric section: the inverted stall is the mirror image
            Assert.Equal(-t.AlphaClMaxRad, t.AlphaClMinRad, 4);
        }
    }

    /// <summary>The physical band for the section cl_max: Ladson's measured 0012 at the bracketing Reynolds numbers, 1.408
    /// (2.0 M) to 1.660 (5.97 M), widened by the thicker root's XFOIL-to-0012 difference (+-0.1). Ty's estimate (1.2) is
    /// below every station; the derivation's sec. 8 explains why, and that the published Vs needs ~1.4+.
    ///
    /// This was RED from 2026-11-02 to 2026-11-10 and is the test Ty's decision closed: run 5e's sections sat at
    /// 1.558-1.705, above the band at seven of the eight stations, on XFOIL's word alone. Taking the measured cl_max
    /// instead (sec. 1f) puts every station inside it by construction -- so this test now checks that the construction
    /// survived the lifting-line map to 3-D, not that a coincidence held.</summary>
    [Fact]
    public void SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber()
    {
        var tables = Aircraft.BuildAirfoilTables(Load("extra-300-like"));
        foreach (var (name, _, _, _) in Stations)
        {
            AirfoilTable t = tables[name];
            Assert.InRange(t.Sample(t.AlphaClMaxRad).Cl, 1.30, 1.56);
        }
    }

    /// <summary>Trailing-edge stations (derivation sec. 1f): after the break each follows XFOIL's own decline moved
    /// 3.5-4.2 deg earlier (the angle the measured cl_max implies), so through its induced angle a station keeps
    /// 62-74 % of its peak one degree (3-D) on -- s4, whose peak is the flattest, 91 %.
    ///
    /// THIS SHIFT IS THE WEAKEST LINK IN THE WHOLE SECTION and the test exists to pin it so it cannot drift silently:
    /// four degrees is a longer reach than run 5e's 1.3 deg, and nothing measured says the decline of a section whose
    /// peak is 0.17 lower looks like the decline of XFOIL's optimistic one. Same construction, more extrapolation
    /// (derivation sec. 1f, last paragraph).</summary>
    [Fact]
    public void TheStationsBreakSteeplyThroughTheirOwnInducedAngle()
    {
        var tables = Aircraft.BuildAirfoilTables(Load("extra-300-like"));
        foreach (var (name, _, _, oneDegOn) in Stations)
        {
            AirfoilTable t = tables[name];
            double peak = t.Sample(t.AlphaClMaxRad).Cl;
            double oneDeg = t.Sample(t.AlphaClMaxRad + 1.0 * D2R).Cl / peak;
            _out.WriteLine($"{name}: +1 deg {oneDeg:P1}");
            Assert.InRange(oneDeg, oneDegOn - 0.02, oneDegOn + 0.02);
        }
    }

    /// <summary>The whole wing (static, no rates, elevator neutral): linear to a late stall, then a steep drop.
    /// Derivation sec. 1f (StallCharacterProbe): CL(10 deg) 0.784; peak 1.476 at 18.95 deg; -16 % one degree on,
    /// -31 % three degrees on; mid-span (s4) first, root and tips later. CL(10) is UNCHANGED from run 5e because the
    /// lift SLOPE did not move -- it was already Ladson's; only the peak and the angle it is reached at did.</summary>
    [Fact]
    public void TheWingIsLinearToALateStallAndBreaksAbruptly()
    {
        AircraftConfig c = Load("extra-300-like");
        var tables = Aircraft.BuildAirfoilTables(c);
        bool[] wing = c.Surfaces.Select(s => s.Id.StartsWith("wing")).ToArray();
        double sw = c.Surfaces.Where(s => s.Id.StartsWith("wing")).SelectMany(s => s.Strips).Sum(s => s.Area);
        double Cl(double aDeg)
        {
            double a = aDeg * D2R, v = 30, q = 0.5 * 1.225 * v * v;
            var (f, _) = AeroModel.Compute(c, tables, new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a)), Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, surfaceMask: wing);
            return Vec3.Dot(f, new Vec3(Math.Sin(a), 0, -Math.Cos(a))) / (q * sw);
        }
        double pk = 0, apk = 0;
        for (double a = 10; a <= 28; a += 0.05) { double x = Cl(a); if (x > pk) { pk = x; apk = a; } }
        _out.WriteLine($"CL(10) {Cl(10):F3}; peak {pk:F3} at {apk:F2}; +1 {Cl(apk + 1):F3}");
        Assert.InRange(Cl(10), 0.76, 0.81);
        Assert.InRange(apk, 18.5, 19.5);
        Assert.InRange(pk, 1.44, 1.52);
        Assert.InRange(Cl(apk + 1) / pk, 0.80, 0.88);
    }

    /// <summary>Extra 300L Information Manual sec. 5.4: 55 KIAS power idle, wings level, at 820 kg (ACRO I). The trimmed
    /// 1-g stall must sit within 5 % of it (52.3-57.8 KEAS; the manual's position error near the stall is a graph only).</summary>
    [Fact]
    public void OneGStallSpeedMatchesTheInformationManual()
    {
        AircraftConfig c = Load("extra-300-like");
        Assert.Equal(820.0, c.Mass.MassKg);
        double vs = PracticeScenario.EstimateVso(c, 0.0) / 0.514444;
        _out.WriteLine($"Vs {vs:F1} KEAS vs 55 KIAS published");
        Assert.InRange(vs, 52.3, 57.8);
    }

    /// <summary>The own-stall separation band is the Extra's alone: at 17 deg its strips are below their own 18.2+ deg
    /// stall, so the wing wake reports nothing stalled; the fleet's fixed 15 deg would have called it all stalled.
    ///
    /// 17 deg, not 18: with run 5e's sections (earliest station 21.3 deg) 18 deg had three degrees of margin, but the
    /// measured sections of sec. 1f stall at 18.2 deg and 18 would have left two TENTHS of a degree -- a test that
    /// passes by rounding. 17 deg is above the fleet's fixed 15 deg, which is what makes the case, and 1.2 deg below
    /// the earliest station, which is what makes it a measurement.</summary>
    [Fact]
    public void TheWingIsNotCountedStalledBelowItsOwnStall()
    {
        AircraftConfig c = Load("extra-300-like");
        Assert.Equal(0.0, c.StallDynamics.SeparationOnPastStallDeg);
        Assert.Equal(5.0, c.StallDynamics.ReattachBelowStallDeg);
        var tables = Aircraft.BuildAirfoilTables(c);
        Vec3 v17 = new(30 * Math.Cos(17 * D2R), 0, 30 * Math.Sin(17 * D2R));
        Assert.Equal(0.0, AeroModel.InstantStalledFraction(c, v17, Vec3.Zero, Vec3.Zero, airfoilTables: tables));
        c.StallDynamics.SeparationOnPastStallDeg = null;
        Assert.Equal(1.0, AeroModel.InstantStalledFraction(c, v17, Vec3.Zero, Vec3.Zero, airfoilTables: tables), 6);
    }

    /// <summary>No other aeroplane is touched: none carries the own-stall band or an Extra section.</summary>
    [Fact]
    public void EveryOtherTypeKeepsTheFleetStallBand()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json"))
        {
            AircraftConfig c;
            try { c = AircraftConfigLoader.LoadFromFile(f); } catch { continue; }
            if (c.Id == "extra-300-like" || string.IsNullOrEmpty(c.Id)) continue;
            Assert.Null(c.StallDynamics.SeparationOnPastStallDeg);
            Assert.Null(c.StallDynamics.ReattachBelowStallDeg);
            Assert.DoesNotContain(c.AirfoilTables.Keys, k => k.StartsWith("extra-ma-"));
        }
    }
}
