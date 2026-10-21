using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (BOOK_SPEEDS=1): the step `NEW-AIRPLANE-PROTOCOL.md` §1b calls "calibrate cd0Area against the book
/// cruise speed and best glide", done as a sweep instead of by hand.
///
/// `fuselage.cd0Area` is the equivalent flat-plate area of everything that is NOT a strip — fuselage, gear, struts,
/// wires, cowl, radiator. It is the only number in a config the protocol allows to be calibrated rather than
/// measured, so it is the one worth being able to re-derive. This prints, for a range of values, the level-flight
/// speed the aeroplane would reach at full power and at 75 %, using the same relation PracticeScenario uses
/// (P_req = W / glide ratio x V / eta), and the idle glide at best L/D.
///
/// Read the FULL-POWER column against the type's published maximum level speed. That is the firmest number a
/// 1917 aeroplane has: "cruise" in a period spec sheet is somebody's habit, and a rate of climb depends on the
/// propeller, but maximum level flight is drag against power and nothing else.
/// </summary>
[Collection("WorldTerrainActive")]
public class BookSpeedsProbe
{
    private readonly ITestOutputHelper _out;
    public BookSpeedsProbe(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("jn4-jenny-like", 33.53, 20.12)]    // JN-4D: 75 mph maximum level, 45 mph MINIMUM level
    [InlineData("stearman-pt17-like", 55.4, 23.2)]  // PT-17: 124 mph maximum level, 52 mph stall
    [InlineData("pitts-s2b-like", 71.5, 25.0)]      // S-2B: 160 mph maximum level, 56 mph stall clean
    [InlineData("c172-like", 63.0, 24.7)]           // 172: 141 mph maximum level, 55 mph stall clean -- the MONOPLANE control
    public void Cd0AreaAgainstTheBookSpeeds(string id, double bookMaxMs, double bookCruiseMs)
    {
        if (Environment.GetEnvironmentVariable("BOOK_SPEEDS") == null) return;
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        double cd0 = c.Fuselage.Cd0Area;
        _out.WriteLine($"== {id}: book max level {bookMaxMs / 0.44704:F0} mph, book min level {bookCruiseMs / 0.44704:F0} mph; " +
                       $"config cd0Area {cd0:F2} m2");
        _out.WriteLine("   cd0Area    level band at FULL power     75 % power    best glide      Vso");
        foreach (double a in new[] { cd0 * 0.8, cd0 * 0.9, cd0, cd0 * 1.05, cd0 * 1.1, cd0 * 1.2, cd0 * 1.3 })
        {
            c.Fuselage.Cd0Area = a;
            double vso = PracticeScenario.EstimateVso(c, 0);
            (double lo, double hi) = LevelBand(c, 1.00);
            (_, double hi75) = LevelBand(c, 0.75);
            (double gr, double gv) = BestGlide(c);
            _out.WriteLine($"   {a,7:F2}   {Mph(lo),4:F0} - {Mph(hi),3:F0} mph" +
                           $"   {Mph(hi75),8:F0} mph   {gr,6:F1}:1 at {Mph(gv):F0} mph   {Mph(vso),4:F0} mph");
        }
        c.Fuselage.Cd0Area = cd0;
    }

    private static double Mph(double ms) => ms / 0.44704;

    /// <summary>The BAND of speeds at which the propeller can supply the power the trimmed glide ratio asks for —
    /// the same relation PracticeScenario.EstimateCruise75 uses, swept in 0.25 m/s steps.
    ///
    /// Both ends matter, and for an underpowered aeroplane the SLOW end is the interesting one: a period spec
    /// sheet's "minimum speed, level flight" is not a stall speed. On the back of the drag curve an aeroplane with
    /// 90 hp runs out of power before it runs out of lift, so the minimum level speed can sit well above the
    /// aerodynamic stall and the two must not be compared with each other.</summary>
    internal static (double Lo, double Hi) LevelBand(AircraftConfig c, double powerFrac)
    {
        double eff = c.Propulsion!.Efficiency > 0 ? c.Propulsion.Efficiency : 0.75;
        double weight = c.Mass.MassKg * 9.81, target = powerFrac * c.Propulsion.MaxPowerW;
        double lo = 0, hi = 0;
        for (double v = 12; v <= 90; v += 0.25)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 0);
            if (!t.Converged || t.GlideRatio <= 0) continue;
            if (weight / t.GlideRatio * v / eff > target) continue;
            if (lo <= 0) lo = v;
            hi = v;
        }
        return (lo, hi);
    }

    /// <summary>Best power-off L/D, computed here rather than through ApproachSpawn.FindBestGlide: that one
    /// MEMOISES on config.Id, so it hands back the first answer for every later variation of the same config and
    /// a cd0Area sweep through it reads as a flat line.</summary>
    private static (double Ratio, double SpeedMs) BestGlide(AircraftConfig c)
    {
        double bestR = 0, bestV = 0;
        for (double v = 14; v <= 45; v += 0.25)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 0, idleProp: true);
            if (!t.Converged || t.GlideRatio <= 0 || double.IsNaN(t.GlideRatio)) continue;
            if (t.AlphaRad > 14 * Math.PI / 180) continue;
            if (t.GlideRatio > bestR) { bestR = t.GlideRatio; bestV = v; }
        }
        return (bestR, bestV);
    }
}
