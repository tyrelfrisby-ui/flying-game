using System;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// What a gust feels like (Ty 2026-11-08, after flying KMYL: "Gusty winds seemed pretty good but the direction should
/// remain within a 3 degree band And gusts should have a randomness built in for timing intensity duration and onset
/// rate and bleed off rate so they aren't too predictable").
///
/// The timing, duration and strength were already random. The SHAPE was not: every gust was the same raised cosine,
/// rising and falling in exactly half its duration, so once you had felt one you had felt them all. And the direction
/// was pinned to a single bearing for ever.
/// </summary>
public class GustShapeTests
{
    private static GustyWind Wind(double bandDeg = 3.0, int seed = 3) =>
        new(5 * 0.514444, 15 * 0.514444, 60, 18, 4, 10, seed, bandDeg);

    [Fact]
    public void TheMeanSpeedIsStillTheMeanSpeed()
    {
        // The calibration that sets the lull assumes each gust averages half its amplitude. The shape changed and the
        // amplitude spread widened, so this is the test that says the airfield did not quietly get windier or calmer.
        GustyWind g = Wind();
        double sum = 0; int n = 0;
        for (double t = 0; t < 20000; t += 0.25) { sum += g.SpeedAt(t); n++; }
        double mean = sum / n, want = 5 * 0.514444;
        Assert.True(Math.Abs(mean - want) < 0.12 * want, $"mean {mean / 0.514444:F2} kt against the {want / 0.514444:F1} kt asked for");
    }

    [Fact]
    public void AGustArrivesFasterThanItLeaves()
    {
        // Every one of them: a gust that faded in as slowly as it faded out would not read as a gust.
        GustyWind g = Wind();
        int gusts = 0;
        for (double t = 0; t < 4000; t += 0.05)
        {
            if (g.GustAt(t) > 1e-9 || g.GustAt(t + 0.05) <= 1e-9) continue;
            // t+0.05 is the first sample of a gust: find its peak and its end.
            double start = t + 0.05, peakT = start, peak = 0, end = start;
            for (double u = start; u < start + 20; u += 0.05)
            {
                double v = g.GustAt(u);
                if (v > peak) { peak = v; peakT = u; }
                if (v <= 1e-9 && u > start + 0.1) { end = u; break; }
            }
            if (peak <= 0 || end <= peakT) continue;
            gusts++;
            Assert.True(peakT - start < end - peakT,
                $"gust at {start:F1}s rose in {peakT - start:F2}s and fell in {end - peakT:F2}s");
        }
        Assert.True(gusts > 20, $"only {gusts} gusts seen — the sweep is not finding them");
    }

    [Fact]
    public void NoTwoGustsAreQuiteTheSame()
    {
        // Strength and shape both vary, which is what stops them being predictable.
        GustyWind g = Wind();
        var peaks = new System.Collections.Generic.List<double>();
        double last = 0;
        for (double t = 0; t < 4000; t += 0.1)
        {
            double v = g.GustAt(t);
            if (v > 1e-9 && last <= 1e-9) peaks.Add(0);
            if (peaks.Count > 0 && v > peaks[^1]) peaks[^1] = v;
            last = v;
        }
        Assert.True(peaks.Count > 20);
        double lo = double.MaxValue, hi = 0;
        foreach (double p in peaks) { lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
        Assert.True(lo < 0.75 * hi, $"the weakest gust was {lo / hi * 100:F0} % of the strongest — too alike");
    }

    [Fact]
    public void TheDirectionNeverLeavesItsBand()
    {
        GustyWind g = Wind(3.0);
        double half = 1.5 * Math.PI / 180, worst = 0;
        for (double t = 0; t < 4000; t += 0.05)
            worst = Math.Max(worst, Math.Abs(g.FromRadAt(t) - g.FromRad));
        Assert.True(worst <= half + 1e-9, $"the wind veered {worst * 180 / Math.PI:F2}° — the band is ±{half * 180 / Math.PI:F1}°");
        Assert.True(worst > 0.4 * half, $"it only ever veered {worst * 180 / Math.PI:F2}° — the band is not being used");
    }

    [Fact]
    public void BetweenGustsTheWindIsBackOnItsMeanBearing()
    {
        GustyWind g = Wind(3.0);
        int quiet = 0;
        for (double t = 0; t < 2000; t += 0.1)
            if (g.GustAt(t) <= 1e-9) { Assert.Equal(g.FromRad, g.FromRadAt(t), 12); quiet++; }
        Assert.True(quiet > 100);
    }

    [Fact]
    public void ABandOfZeroPinsItExactlyWhereItUsedToBe()
    {
        GustyWind g = Wind(0.0);
        for (double t = 0; t < 500; t += 0.3) Assert.Equal(g.FromRad, g.FromRadAt(t), 12);
    }
}
