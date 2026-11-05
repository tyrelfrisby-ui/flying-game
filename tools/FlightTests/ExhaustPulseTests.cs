using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The exhaust pulse train the smoke tubes are shaped from (Ty 2026-11-04: one tube per exhaust-valve event, the
/// exit velocity a bell per event — open, blowdown peak, taper as the valve closes — at cylinders/2 events per rev).
/// Expected values are literals worked by hand from the formulas in ExhaustJet / ExhaustPulse, not read back from them.</summary>
public class ExhaustPulseTests
{
    private readonly ITestOutputHelper _out;
    public ExhaustPulseTests(ITestOutputHelper o) => _out = o;

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Fact]
    public void PulseRateIsHalfTheCylindersPerRevolution()
    {
        Assert.Equal(135.0, ExhaustPulse.PulseHz(6, 2700), 9);   // the AEIO-540 at full power
        Assert.Equal(90.0, ExhaustPulse.PulseHz(4, 2700), 9);    // the 172's IO-360
        Assert.Equal(35.0, ExhaustPulse.PulseHz(6, 700), 9);     // idle
        Assert.Equal(0.0, ExhaustPulse.PulseHz(6, 0), 9);
    }

    /// <summary>The bell carries the mean: averaged over whole revolutions the pipe's velocity is the mean exit velocity —
    /// the pulses only say WHEN the gas leaves, not how much. And there is one distinct peak per valve event, at the blowdown
    /// (50° of crank after the valve opens), with a real trough between: 1.61× / 0.28× the mean on a six, 2.39× / 0.007× on a
    /// four (whose events barely overlap).</summary>
    [Theory]
    [InlineData(6, 1.607, 0.281)]
    [InlineData(4, 2.392, 0.007)]
    public void ProfileConservesTheMeanAndPeaksOncePerEvent(int cylinders, double peakRatio, double troughRatio)
    {
        const int n = 40000;
        double sum = 0, max = 0, min = double.MaxValue; int peaks = 0;
        double prev2 = 0, prev1 = 0;
        for (int i = 0; i < n; i++)
        {
            double crank = 10 + 2.0 * i / n;                         // two revolutions, well away from crank 0
            double u = ExhaustPulse.Velocity(crank, 50, cylinders);
            sum += u; max = System.Math.Max(max, u); min = System.Math.Min(min, u);
            if (i >= 2 && prev1 > prev2 && prev1 >= u) peaks++;
            prev2 = prev1; prev1 = u;
        }
        double mean = sum / n;
        _out.WriteLine($"{cylinders} cyl: mean {mean:0.000} max {max:0.000} min {min:0.000} peaks {peaks}");
        Assert.InRange(mean, 49.9, 50.1);
        Assert.Equal(cylinders, peaks);                              // two revolutions × cylinders/2
        Assert.InRange(max / 50, peakRatio - 0.01, peakRatio + 0.01);
        Assert.InRange(min / 50, troughRatio - 0.01, troughRatio + 0.01);
        // the peak is 50° (0.2 of the 250° window) after the valve opens; an event opens at crank 10 exactly
        double atPeak = ExhaustPulse.Velocity(10 + 0.2 * ExhaustPulse.WindowRev, 50, cylinders);
        Assert.InRange(atPeak, ExhaustPulse.Velocity(10 + 0.15 * ExhaustPulse.WindowRev, 50, cylinders), double.MaxValue);
        Assert.InRange(atPeak, ExhaustPulse.Velocity(10 + 0.25 * ExhaustPulse.WindowRev, 50, cylinders), double.MaxValue);
    }

    /// <summary>The tube's parcels: a tenth, half and nine tenths of an event's gas are out by these points of its window
    /// (I_x(3, 9) inverted: 0.1048, 0.2358, 0.4152). The middle sits just behind the peak — the tube is fattest there.</summary>
    [Fact]
    public void MassQuantilesShapeTheTube()
    {
        Assert.InRange(ExhaustPulse.MassQuantile(0.1), 0.1038, 0.1058);
        Assert.InRange(ExhaustPulse.MassQuantile(0.5), 0.2348, 0.2368);
        Assert.InRange(ExhaustPulse.MassQuantile(0.9), 0.4142, 0.4162);
        Assert.Equal(0.0, ExhaustPulse.MassBefore(0), 12);
        Assert.Equal(1.0, ExhaustPulse.MassBefore(1), 12);
        Assert.InRange(ExhaustPulse.MassBefore(0.5), 0.966, 0.968);   // the blowdown and early stroke carry nearly all of it
    }

    /// <summary>Faster gas carries itself further — the tube stretches where the pulse peaks — and gas no faster than the air
    /// carries itself nowhere. 4 in pipe (x_c 0.61 m): 40 m/s excess for 0.15 s (spent) → √(0.61² + 2·40·0.61·0.15) − 0.61.</summary>
    [Fact]
    public void PenetrationGrowsWithExcessSpeedAndStops()
    {
        double d = 4 * 0.0254;
        Assert.InRange(ExhaustPulse.Penetration(40, 1.0, d), 2.153, 2.173);
        Assert.Equal(ExhaustPulse.Penetration(40, 0.15, d), ExhaustPulse.Penetration(40, 5.0, d), 12);   // spent: no further
        Assert.True(ExhaustPulse.Penetration(60, 0.1, d) > ExhaustPulse.Penetration(20, 0.1, d));
        Assert.Equal(0.0, ExhaustPulse.Penetration(-5, 0.1, d), 12);
        Assert.Equal(0.0, ExhaustPulse.Penetration(0, 0.1, d), 12);
    }

    /// <summary>Mean exit speeds at sea level from each type's own smoke config: idle (700 rpm, throttle closed), cruise
    /// (2,400 rpm, 0.65) and full power (2,700 rpm, open). Worked by hand: ṁ = ρ·VE·fresh·MAP·V_d·rpm/120·(1 + 1/AFR),
    /// fresh = (1 − (1/MAP)^(1/1.35)/r_c)/(1 − 1/r_c), U = ṁ/(ρ_e·A), ρ_e = ρ·288.15/900, 4 in pipe.</summary>
    [Theory]
    [InlineData("pitts-s2b-like", 6, 4.47, 45.96, 70.61)]
    [InlineData("extra-300-like", 6, 4.45, 45.84, 70.41)]
    [InlineData("c172-like", 4, 2.96, 30.62, 47.07)]
    public void ExitSpeedsAtIdleCruiseAndFullPower(string id, int cylinders, double idle, double cruise, double full)
    {
        var cfg = Load(id);
        Assert.Equal(cylinders, cfg.Smoke!.Cylinders);
        double At(double rpm, double thr) => ExhaustJet.ExitVelocity(cfg.Smoke, cfg.Propulsion!, rpm, thr, 1.225);
        double ui = At(700, 0), uc = At(2400, 0.65), uf = At(2700, 1);
        _out.WriteLine($"{id}: idle {ui:0.00}  cruise {uc:0.00}  full {uf:0.00} m/s; full-power peak {ExhaustPulse.Velocity(0.2 * ExhaustPulse.WindowRev, uf, cylinders):0.0}");
        Assert.InRange(ui, idle - 0.02, idle + 0.02);
        Assert.InRange(uc, cruise - 0.05, cruise + 0.05);
        Assert.InRange(uf, full - 0.05, full + 0.05);
    }
}
