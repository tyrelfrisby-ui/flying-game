using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Tuning;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE SMOKE FEATHERS IN AND OUT, AND NEVER REACHES ZERO. Ty, 2026-11-11, after looking at the first cut: "The smoke
/// oil is sprayed continuously but the exhaust pulses are cyclical. The exhaust temp is rather co stent [constant] so
/// rather than creating pulses with no smoke at all feather the smoke in and out but never zero."
///
/// The RATE is unchanged and still the engine's own — cylinders and rpm, keyed to the crank phase. What changed is the
/// SHAPE: an amplitude modulation on a continuous base, where it used to be an on/off train with holes in it.
/// </summary>
public class SmokeFeatherTests
{
    private readonly ITestOutputHelper _o;
    public SmokeFeatherTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>A pipe fed by three of a six's cylinders: the Pitts' and the Extra's case.</summary>
    private const int SplitSix = 3;

    private static double Lowest(int cylPerPipe, double depth, double floor, out double highest, out double mean)
    {
        double lo = double.MaxValue, hi = 0, sum = 0;
        int n = 20000;
        double period = 1.0 / ExhaustPulse.EventsPerRev(cylPerPipe);
        for (int i = 0; i < n; i++)
        {
            double m = ExhaustPulse.SmokeDensity(20 * period + period * i / (n - 1.0), cylPerPipe, depth, floor);
            lo = Math.Min(lo, m); hi = Math.Max(hi, m); sum += m;
        }
        highest = hi; mean = sum / n;
        return lo;
    }

    // ---- never zero -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(3)]    // the Pitts and the Extra: three cylinders into each of two stacks
    [InlineData(6)]    // a single-stack six
    [InlineData(4)]    // a four, whose carrier nearly stops between firings (trough 0.007)
    [InlineData(2)]
    [InlineData(1)]
    public void TheDensityNeverReachesZeroAtAnyPhaseOrAnyDepth(int cylPerPipe)
    {
        foreach (double depth in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            double lo = Lowest(cylPerPipe, depth, 0.28, out double hi, out _);
            Assert.True(lo >= 0.28 - 1e-9, $"{cylPerPipe} cyl/pipe at depth {depth}: trough {lo:0.0000} is under the floor");
            Assert.True(lo > 0, $"{cylPerPipe} cyl/pipe at depth {depth}: the smoke went out");
            Assert.True(hi >= lo);
        }
        // Even asked for an absurd depth, which the knob cannot give, it still cannot gate.
        Assert.True(Lowest(cylPerPipe, 5.0, 0.28, out _, out _) >= 0.28 - 1e-9);
        Assert.True(Lowest(cylPerPipe, -1.0, 0.28, out _, out _) > 0);
    }

    // ---- the depth moves the trough and not the mean ----------------------------------------------------------

    [Fact]
    public void TheDepthKnobMovesTheTroughAndLeavesTheMeanAlone()
    {
        _o.WriteLine("three cylinders into one stack (the Pitts, the Extra), floor 0.28:");
        _o.WriteLine("  depth   trough   peak    mean");
        double prev = double.MaxValue;
        foreach (double depth in new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 })
        {
            double lo = Lowest(SplitSix, depth, 0.28, out double hi, out double mean);
            _o.WriteLine($"  {depth,5:0.00} {lo,8:0.000} {hi,7:0.000} {mean,7:0.000}");
            // The mean density of the trail is the same at every setting: the carrier it rides on averages to 1.
            Assert.Equal(1.0, mean, 2);
            // And the trough is exactly the promised 1 - depth(1 - floor), monotonically deeper with the knob.
            Assert.Equal(ExhaustPulse.FeatherTrough(depth, 0.28), lo, 3);
            Assert.True(lo <= prev + 1e-9, "the trough did not deepen with the knob");
            prev = lo;
        }
        // The ends: flat at 0, on the floor at 1.
        Assert.Equal(1.0, Lowest(SplitSix, 0.0, 0.28, out double flatHi, out _), 3);
        Assert.Equal(1.0, flatHi, 3);
        Assert.Equal(0.28, Lowest(SplitSix, 1.0, 0.28, out _, out _), 3);
    }

    [Fact]
    public void TheFloorKnobSetsWhereTheTroughBottomsOut()
    {
        foreach (double floor in new[] { 0.1, 0.28, 0.5, 0.9 })
        {
            double lo = Lowest(SplitSix, 1.0, floor, out _, out double mean);
            Assert.Equal(floor, lo, 3);
            Assert.Equal(1.0, mean, 2);
        }
    }

    // ---- the rate is still the engine's -----------------------------------------------------------------------

    [Fact]
    public void TheFeatherRunsAtTheDerivedFiringRateAndNowhereElse()
    {
        // One cycle of a split six's pipe is 2/3 of a crank revolution, so there is exactly ONE trough and ONE peak in
        // it: the feather is at the pipe's own firing rate, not the engine's and not twice it.
        double period = 1.0 / ExhaustPulse.EventsPerRev(SplitSix);
        Assert.Equal(2.0 / 3.0, period, 9);
        int peaks = 0, n = 6000;
        double At(int i) => ExhaustPulse.SmokeDensity(20 * period + period * i / n, SplitSix, 1.0, 0.28);
        for (int i = 1; i < n; i++)
        {
            double a = At(i - 1), b = At(i), c = At(i + 1);
            if (b > a && b >= c) peaks++;
        }
        // ONE surge per pipe cycle: the feather is at the pipe's own firing rate, not the engine's and not twice it.
        // Three cylinders 240 deg apart inside a 250 deg window leave only a narrow neck where both bells are in their
        // tails, so the quiet part is a brief deep dip rather than a long plateau - and what stops that dip reaching
        // zero is the hot stack's floor, which is the whole of Ty's point.
        int lowHalf = 0, atFloor = 0;
        double peakV = 0, minV = double.MaxValue;
        for (int i = 0; i <= n; i++) { double m = At(i); peakV = Math.Max(peakV, m); minV = Math.Min(minV, m); }
        for (int i = 0; i <= n; i++)
        {
            double m = At(i);
            if (m < 0.5 * (minV + peakV)) lowHalf++;
            if (m <= 0.28 + 1e-9) atFloor++;
        }
        _o.WriteLine($"one pipe cycle ({period:0.000} rev): {peaks} surge, density {minV:0.00}..{peakV:0.00} of mean, " +
                     $"{100.0 * lowHalf / (n + 1):0} % of the cycle in the lower half of the swing, " +
                     $"{100.0 * atFloor / (n + 1):0.0} % on the floor itself");
        Assert.Equal(1, peaks);
        Assert.Equal(0.28, minV, 6);                 // it bottoms out exactly on the floor, and not below
        Assert.True(atFloor >= 1, "the floor is never actually reached");
        Assert.InRange(lowHalf, n / 4, n);           // most of the cycle is the quiet part, with one surge in it

        // And in Hz it is the figure derived from the cylinder count and the rpm, unchanged by this lane.
        AircraftConfig cfg = Load("pitts-s2b-like");
        int cyl = cfg.Smoke!.Cylinders;
        int strokes = cfg.Smoke.Pipes!.CombustionStrokesPerPuff;
        Assert.Equal(SplitSix, ExhaustPulse.CylindersPerPipe(cyl, strokes));
        Assert.Equal(67.5, ExhaustPulse.PuffHz(cyl, cfg.Propulsion!.MaxRpm, strokes), 9);
        Assert.Equal(67.5, ExhaustPulse.EventsPerRev(SplitSix) * cfg.Propulsion.MaxRpm / 60.0, 9);
        Assert.Equal(17.5, ExhaustPulse.PuffHz(cyl, cfg.Propulsion.IdleRpm, strokes), 9);
    }

    // ---- it stops when the engine does ------------------------------------------------------------------------

    [Fact]
    public void AStoppedEngineMakesNoSmokeAtAll()
    {
        // The floor is a floor on the MODULATION, not on the smoke: the density is a factor the oil flow is multiplied
        // by, so a dead engine's zero flow stays zero however high the floor is set. (SmokeTrail also draws no pulse
        // tubes at all below 1 rpm, and the flow a stopped engine reports is 0 - ReplaySmoke.FlowIfOnNow.)
        foreach (double floor in new[] { 0.28, 1.0 })
            foreach (double crank in new[] { 0.0, 0.17, 0.33, 0.5, 0.91 })
            {
                double m = ExhaustPulse.SmokeDensity(crank, SplitSix, 1.0, floor);
                Assert.True(m > 0);
                Assert.Equal(0.0, 0.0f * m, 12);          // no oil flow, no smoke
            }
        // And the rate itself goes to nothing with the rpm, so there is no residual breathing either.
        Assert.Equal(0.0, ExhaustPulse.PuffHz(6, 0, 2), 12);
        Assert.Equal(0.0, ExhaustPulse.PulseHz(6, 0), 12);
    }

    // ---- what it looks like at the three conditions that matter -----------------------------------------------

    [Fact]
    public void TheThreeConditionsThatMatter()
    {
        AircraftConfig cfg = Load("pitts-s2b-like");
        int cyl = cfg.Smoke!.Cylinders, strokes = cfg.Smoke.Pipes!.CombustionStrokesPerPuff;
        int perPipe = ExhaustPulse.CylindersPerPipe(cyl, strokes);
        double lo = Lowest(perPipe, 1.0, 0.28, out double hi, out _);
        foreach ((string name, double rpm) in new[] { ("full power", 2700.0), ("idle", 700.0), ("dying", 250.0) })
        {
            double hz = ExhaustPulse.PuffHz(cyl, rpm, strokes);
            _o.WriteLine($"  {name,-11} {rpm,5:0} rpm -> {hz,5:0.0} Hz a pipe, density {lo:0.00}..{hi:0.00} of mean " +
                         $"({(hz > 25 ? "past resolving: reads continuous" : "visibly breathing")})");
        }
        // 67 Hz is past anything an eye or a 72-90 Hz headset resolves; idle is not.
        Assert.True(ExhaustPulse.PuffHz(cyl, 2700, strokes) > 25);
        Assert.True(ExhaustPulse.PuffHz(cyl, 700, strokes) < 25);
        Assert.True(ExhaustPulse.PuffHz(cyl, 250, strokes) < 10);
        // The payoff: a weaker firing deepens the trough, it does not punch a hole. Half the carrier surge still leaves
        // the floor underneath it.
        double weak = Lowest(perPipe, 1.0, 0.28, out double weakHi, out _);
        Assert.True(weak >= 0.28 - 1e-9 && weakHi > weak);
    }

    // ---- the knobs are on the page ----------------------------------------------------------------------------

    [Fact]
    public void BothKnobsAreRegisteredWithTheShippedValuesTyIsTuningFrom()
    {
        GlobalTunable depth = GlobalTunableRegistry.ById("smoke.featherDepth");
        GlobalTunable floor = GlobalTunableRegistry.ById("smoke.minDensity");
        Assert.NotNull(depth);
        Assert.NotNull(floor);
        Assert.Equal(GlobalTunableRegistry.SmokeGroup, depth!.Group);
        Assert.Equal(GlobalTunableRegistry.SmokeGroup, floor!.Group);
        Assert.Equal(1.0, depth.Shipped, 6);
        Assert.Equal(0.28, floor.Shipped, 6);
        Assert.Equal(0.0, depth.Min, 6); Assert.Equal(1.0, depth.Max, 6);
        Assert.Contains("2026-11-11", depth.Description);
        Assert.Contains("never zero", floor.Description);
        // The floor's default is the model's own number, not a chosen one: the trough of a six into ONE collector.
        Assert.Equal(0.28, ExhaustPulse.TroughRatio(6), 2);
        _o.WriteLine($"carrier trough/mean: 6-into-one {ExhaustPulse.TroughRatio(6):0.000}, " +
                     $"4-into-one {ExhaustPulse.TroughRatio(4):0.000}, 3-into-one {ExhaustPulse.TroughRatio(3):0.000}; " +
                     $"peaks {ExhaustPulse.PeakRatio(6):0.000} / {ExhaustPulse.PeakRatio(3):0.000}");
        // The split pipe's own carrier nearly stops, which is why the hot stack's floor is doing the work.
        Assert.True(ExhaustPulse.TroughRatio(3) < 0.1);
    }
}
