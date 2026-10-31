using FlyingGame.Core.Engine;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The randomizer has one job above all others: break the engine at the rate it was told to, whatever it breaks it
/// for. These tests fly millions of simulated hours and count, rather than trusting the algebra.
/// </summary>
public class EngineReliabilityTests
{
    private readonly ITestOutputHelper _o;
    public EngineReliabilityTests(ITestOutputHelper o) { _o = o; }

    /// <summary>
    /// The weights must sum to exactly 1, or the aggregate rate is not the rate asked for: at 1.02 the engine would
    /// fail 2 % more often than 1 in 3700 and no amount of reading the hazard code would show it. (My first table
    /// summed to 1.02.)
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCauseWeightsSumToExactlyOne(bool radial)
    {
        double sum = EngineReliability.WeightSum(EngineReliability.Table(radial));
        Assert.Equal(1.0, sum, 9);
    }

    /// <summary>
    /// Fly 20 million hours at a stiff multiplier and count the failures. The realized MTBF must come back to
    /// 3700/m hours. This is the test that says the feature does what the owner asked for.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(50.0)]
    [InlineData(1000.0)]
    public void TheRealizedFailureRateIsTheRateItWasGiven(double unreliability)
    {
        // One draw per simulated step. A coarse step keeps the run quick without biasing the result, because
        // StepProbability is the exact exponential rather than lambda*dt.
        const double stepSeconds = 60.0;
        double wantMtbf = EngineReliability.StandardMtbfHours / unreliability;
        // Enough hours for ~20000 expected failures, so the 1/sqrt(n) noise is well under 1 %.
        double hours = wantMtbf * 20000.0;
        long steps = (long)(hours * 3600.0 / stepSeconds);

        var rng = new Random(20261030);
        double p = EngineReliability.StepProbability(EngineReliability.RatePerHour(unreliability), stepSeconds);
        long failures = 0;
        for (long i = 0; i < steps; i++) if (rng.NextDouble() < p) failures++;

        double gotMtbf = hours / failures;
        _o.WriteLine($"m={unreliability}: {failures} failures in {hours:N0} h → MTBF {gotMtbf:F1} h, want {wantMtbf:F1} h");
        Assert.InRange(gotMtbf, wantMtbf * 0.97, wantMtbf * 1.03);
    }

    /// <summary>
    /// Reweighting the causes must not change how often the engine breaks — only what breaks. A radial sheds
    /// cylinders where a flat four burns valves, and both must still fail once per 3700 hours.
    /// </summary>
    [Fact]
    public void ChangingWhatBreaksDoesNotChangeHowOftenItBreaks()
    {
        foreach (bool radial in new[] { false, true })
            Assert.Equal(1.0, EngineReliability.WeightSum(EngineReliability.Table(radial)), 9);

        // And the drawn distribution follows the weights it was given, for both tables.
        foreach (bool radial in new[] { false, true })
        {
            var table = EngineReliability.Table(radial);
            var count = new Dictionary<FailureCause, int>();
            const int n = 2_000_000;
            var rng = new Random(7);
            for (int i = 0; i < n; i++)
            {
                FailureCause c = EngineReliability.PickByWeight(rng.NextDouble(), table).Cause;
                count[c] = count.TryGetValue(c, out int k) ? k + 1 : 1;
            }
            foreach (FailureMode m in table)
            {
                double got = count.GetValueOrDefault(m.Cause) / (double)n;
                _o.WriteLine($"{(radial ? "radial" : "flat  ")} {m.Cause,-26} want {m.Weight:F3} got {got:F4}");
                Assert.InRange(got, m.Weight - 0.004, m.Weight + 0.004);
            }
        }
    }

    /// <summary>A fuel problem is the most likely single thing to go wrong, as the owner asked.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFuelProblemIsTheMostLikelyCause(bool radial)
    {
        var table = EngineReliability.Table(radial);
        FailureCause[] fuel =
        {
            FailureCause.WaterInFuel, FailureCause.FuelContamination,
            FailureCause.FuelLineRestriction, FailureCause.EngineFuelPumpFailure,
        };
        double fuelShare = table.Where(m => fuel.Contains(m.Cause)).Sum(m => m.Weight);
        double biggestOther = table.Where(m => !fuel.Contains(m.Cause)).Sum(m => m.Weight);
        _o.WriteLine($"{(radial ? "radial" : "flat")}: fuel {fuelShare:P0}, everything else {biggestOther:P0}");
        Assert.True(fuelShare > 0.33, $"fuel is only {fuelShare:P0} of the pool");
        // And no single non-fuel cause beats the whole fuel bucket.
        Assert.True(table.Max(m => m.Weight) < fuelShare);
    }

    /// <summary>
    /// The setting makes an engine worse, never better. The owner was explicit: up to 1000 times less reliable,
    /// never more reliable than standard.
    /// </summary>
    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-5.0, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(250.0, 250.0)]
    [InlineData(1000.0, 1000.0)]
    [InlineData(5000.0, 1000.0)]
    [InlineData(double.NaN, 1.0)]
    public void ReliabilityOnlyEverGetsWorse(double asked, double want)
    {
        Assert.Equal(want, EngineReliability.ClampUnreliability(asked));
        Assert.True(EngineReliability.RatePerHour(asked) <= 1.0 / EngineReliability.StandardMtbfHours * 1000.0 + 1e-12);
        Assert.True(EngineReliability.RatePerHour(asked) >= 1.0 / EngineReliability.StandardMtbfHours - 1e-12);
    }

    /// <summary>
    /// The step probability is the exact exponential, so a long step cannot push it over 1 or let the realized rate
    /// sag. lambda*dt alone would read 2.7 for an hour-long step at the worst setting.
    /// </summary>
    [Fact]
    public void TheStepProbabilityStaysAProbabilityHoweverLongTheStep()
    {
        double rate = EngineReliability.RatePerHour(1000.0);   // one per 3.7 h
        foreach (double dt in new[] { 0.001, 0.02, 1.0, 60.0, 3600.0, 36000.0, 1e7 })
        {
            double p = EngineReliability.StepProbability(rate, dt);
            Assert.InRange(p, 0.0, 1.0);
        }
        Assert.Equal(0.0, EngineReliability.StepProbability(rate, 0.0));
        Assert.Equal(0.0, EngineReliability.StepProbability(0.0, 1.0));
        // An hour at one-per-3.7-hours is 1 - exp(-1/3.7) = 23.68 %, not the 27.03 % that lambda*dt would claim.
        // Getting this wrong the naive way overstates the chance by a seventh at the worst setting.
        double hour = EngineReliability.StepProbability(rate, 3600.0);
        Assert.Equal(0.236827, hour, 6);
        Assert.True(hour < rate, $"{hour:F6} should be below lambda*dt = {rate:F6}");
    }

    /// <summary>Cockpit electrical smoke runs on its own 10 000-hour clock, not out of the engine's pool.</summary>
    [Fact]
    public void ElectricalSmokeHasItsOwnIndependentRate()
    {
        const double stepSeconds = 60.0;
        double hours = EngineReliability.StandardElectricalMtbfHours * 2000.0;
        long steps = (long)(hours * 3600.0 / stepSeconds);
        var rng = new Random(99);
        long smoke = 0;
        for (long i = 0; i < steps; i++) if (EngineReliability.DrawElectricalSmoke(rng, stepSeconds, 1.0)) smoke++;

        double gotMtbf = hours / smoke;
        _o.WriteLine($"{smoke} electrical events in {hours:N0} h → MTBF {gotMtbf:F0} h, want 10000");
        Assert.InRange(gotMtbf, 9700.0, 10300.0);
    }

    /// <summary>Every cause the table can draw describes itself, so a failure can be named in the log and the HUD.</summary>
    [Fact]
    public void EveryDrawableCauseSaysWhatItIs()
    {
        foreach (bool radial in new[] { false, true })
            foreach (FailureMode m in EngineReliability.Table(radial))
            {
                Assert.NotEqual(FailureCause.None, m.Cause);
                Assert.False(string.IsNullOrWhiteSpace(m.Describe), $"{m.Cause} has no description");
                Assert.InRange(m.EndPowerFraction, 0.0, 1.0);
                Assert.True(m.OnsetSeconds >= 0.0);
                Assert.True(m.Weight > 0.0, $"{m.Cause} has no weight, so it can never be drawn");
            }
    }
}
