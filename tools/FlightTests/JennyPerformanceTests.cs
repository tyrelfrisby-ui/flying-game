using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Curtiss JN-4D against the numbers a 1917 aeroplane actually has. Data and sources:
/// docs/research/JN4-JENNY-DATA.md.
///
/// It is not in <see cref="NewAircraftPerformanceTests"/> because that test checks a book stall speed, a book Vy
/// and a book 75 %-power cruise, and the JN-4D publishes none of the three. What its spec sheet publishes is a
/// **maximum level speed of 75 mph**, a **minimum level speed of 45 mph**, and a climb to 2,000 ft. Maximum level
/// flight is the firm one: it is drag against power and nothing else, where a period "cruise" figure is somebody's
/// habit and a rate of climb depends on the propeller.
/// </summary>
[Collection("WorldTerrainActive")]
public class JennyPerformanceTests
{
    private readonly ITestOutputHelper _out;
    public JennyPerformanceTests(ITestOutputHelper o) { _out = o; }

    private const double Mph = 0.44704;

    private static AircraftConfig Jenny() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "jn4-jenny-like.json"));

    /// <summary>75 mph on 90 hp. fuselage.cd0Area is calibrated to this and to nothing else, so this test is the
    /// thing that holds the calibration: if a change to the strip tables or the drag model moves it, this fails and
    /// cd0Area gets re-derived (BOOK_SPEEDS=1 sweeps it) rather than quietly drifting.</summary>
    [Fact]
    public void MaximumLevelSpeedIsTheBookSeventyFive()
    {
        AircraftConfig c = Jenny();
        (double lo, double hi) = BookSpeedsProbe.LevelBand(c, 1.00);
        _out.WriteLine($"full power, level: {lo / Mph:F0} - {hi / Mph:F0} mph   (book maximum 75, book minimum 45)");
        Assert.InRange(hi / Mph, 73.0, 77.0);
    }

    /// <summary>"Climb: 2,000 ft in 10 minutes" on the JN-4D sheet, 7 min 30 s in the other source — 200 to
    /// 267 ft/min. The suite's own note on NewAircraftPerformanceTests is that the sim reads rate of climb high
    /// (the C172 gives 1,274 fpm against a book 730), so the band here is the book's bottom to half again its
    /// top rather than the book band itself.</summary>
    [Fact]
    public void ClimbsLikeNinetyHorsepower()
    {
        AircraftConfig c = Jenny();
        var sc = new PracticeScenario(PracticeKind.ClimbLevelDescend, PracticeWind.Calm, c,
                                      PracticeScenarioTests.TestRunway(), 0);
        sc.Spawn();
        _out.WriteLine($"Vx {sc.VxMs / Mph:F0} mph  Vy {sc.VyMs / Mph:F0} mph  ROC {sc.RocAtVyMs * 196.85:F0} fpm " +
                       $"(book 200-267)  best glide {sc.BestGlideRatio:F1}:1 at {sc.BestLdMs / Mph:F0} mph");
        Assert.InRange(sc.RocAtVyMs * 196.85, 200.0, 400.0);
        Assert.InRange(sc.VyMs / Mph, 40.0, 60.0);        // unpublished; a 90 hp trainer climbs best near 50 mph
    }

    /// <summary>
    /// THE KNOWN GAP, pinned so it cannot widen unnoticed.
    ///
    /// The aerodynamic stall must sit BELOW the published 45 mph minimum level speed — an aeroplane cannot have a
    /// minimum level speed below its stall. It does, at about 38 mph, but the 7 mph between them is larger than it
    /// should be: 45 mph at 1,920 lb over 352.56 sq ft needs a system C_L of only 1.05, and the config's wing
    /// reaches 1.48, which is exactly the peak of the `clarkY-like` table it borrows.
    ///
    /// Two candidate causes, and the measurement rules one of them out:
    ///   - `clarkY-like` standing in for the JN-4D's **Eiffel 36**, a thin high-camber 1917 section (§5 of the
    ///     research doc). No measured Eiffel 36 table was found, so this is unresolved.
    ///   - **Biplane interference**, which the strip model does not have at all: each strip sees the free stream,
    ///     so two wings one chord apart make as much lift as two wings a long way apart. This was the first
    ///     suspect and the numbers do NOT support it being the whole story — BOOK_SPEEDS=1 on the other two
    ///     biplanes gives the Stearman 53 mph against a book 52 and the Pitts 68 against a book 56, so there is no
    ///     biplane-wide bias to find. (Both of those are limited by elevator authority before the wing stalls,
    ///     which is its own known item; the Jenny has the elevator to reach its wing's real peak, which is why it
    ///     is the type that exposes this.)
    /// </summary>
    [Fact]
    public void StallsBelowItsPublishedMinimumLevelSpeed()
    {
        AircraftConfig c = Jenny();
        double vso = PracticeScenario.EstimateVso(c, 0);
        double clAtBookMin = c.Mass.MassKg * 9.81 / (0.5 * 1.225 * 20.12 * 20.12 * 32.754);
        double clAtVso = c.Mass.MassKg * 9.81 / (0.5 * 1.225 * vso * vso * 32.754);
        _out.WriteLine($"Vso {vso / Mph:F1} mph (C_L {clAtVso:F3})  against the book minimum level speed of " +
                       $"45 mph (C_L {clAtBookMin:F3}) — the wing makes {clAtVso / clAtBookMin:F2}x the lift the book asks for");
        Assert.True(vso < 20.12, $"Vso {vso / Mph:F1} mph is at or above the published 45 mph MINIMUM level speed");
        Assert.InRange(vso / Mph, 34.0, 45.0);
    }

    /// <summary>
    /// On the ground, with a tail SKID that cannot steer and no wheel brakes at all.
    ///
    /// GroundLoopTests.GroundStabilityMatchesTheBenchmarks asserts a yaw-rate growth BAND for ten types, and each
    /// of those bands came from the owner flying the real aeroplane. There is no benchmark for a JN-4D, so no band
    /// is invented here: this asserts only the two things that are true of every taildragger in that test — the
    /// heading does not swing back against a swerve, and the yaw rate does not reverse — and PRINTS the growth
    /// ratio so it can be banded once somebody who has flown one says what it should be.
    /// </summary>
    [Fact]
    public void OnTheGroundItSwingsAndNothingBringsItBack()
    {
        AircraftConfig c = Jenny();
        foreach (double v in new[] { 8.0, 14.0 })
        {
            var r = GroundStabilityProbe.YawRateGrowth(c, v, 5, 3);
            _out.WriteLine($"{v:F0} m/s: yaw rate x{r.ratio:F2} after 3 s, heading {r.heading:F1} deg, " +
                           $"mains lead {r.lead:F3} m, CG height {r.h:F3} m, Izz {r.izz:F0}  <- NO BAND ASSERTED");
            Assert.True(r.heading > 0, $"{v:F0} m/s: the heading swung back against the swerve ({r.heading:F1} deg)");
            Assert.True(r.ratio > -0.05, $"{v:F0} m/s: the yaw rate reversed (x{r.ratio:F2})");
        }
    }
}
