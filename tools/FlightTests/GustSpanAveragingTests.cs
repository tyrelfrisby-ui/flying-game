using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE WING AVERAGES THE GUST ACROSS ITS SPAN (Turbulence.UnitGustAt with a span). The closed forms are checked
/// against brute-force integration along the actual span — sample the unfiltered field at many stations across the
/// wing and take the mean (heave) and the first moment (roll). If the analytic weights are right the two agree.
/// </summary>
public class GustSpanAveragingTests
{
    private readonly ITestOutputHelper _out;
    public GustSpanAveragingTests(ITestOutputHelper o) { _out = o; }

    private static Turbulence Field() => new(TurbulenceSpectrum.VonKarman, 48, 7, 15, 762);

    /// <summary>Brute force: the unfiltered field sampled across the span, averaged and first-momented.</summary>
    private static (Vec3 mean, Vec3 grad) Integrate(Turbulence t, Vec3 pos, double time, double lh, double lv, double span, Vec3 axis)
    {
        const int N = 2001;
        Vec3 sum = Vec3.Zero, mom = Vec3.Zero; double wsum = 0, msum = 0;
        for (int i = 0; i < N; i++)
        {
            double y = -0.5 * span + span * i / (N - 1.0);
            Vec3 g = t.UnitGustAt(pos + axis * y, time, lh, lv, Vec3.Zero);
            sum += g; wsum += 1;
            mom += g * y; msum += y * y;
        }
        return (sum * (1.0 / wsum), mom * (1.0 / msum));
    }

    [Theory]
    [InlineData(15.54)]   // the 2-33
    [InlineData(6.08)]    // the Pitts
    [InlineData(29.0)]    // a big span, to exercise the filter hard
    public void TheClosedFormsMatchIntegratingAlongTheSpan(double span)
    {
        var t = Field();
        Vec3 axis = new Vec3(0, 1, 0);
        double lh = 150, lv = 53;
        for (int s = 0; s < 6; s++)
        {
            Vec3 pos = new Vec3(120.0 * s + 17, -40.0 * s + 5, -60 - 11 * s);
            double time = 3.0 * s;
            Vec3 filtered = t.UnitGustAt(pos, time, lh, lv, Vec3.Zero, span, axis, out Vec3 grad);
            var (mean, gradBrute) = Integrate(t, pos, time, lh, lv, span, axis);
            _out.WriteLine($"span {span:F1} sample {s}: heave analytic {filtered.Z:+0.0000;-0.0000} vs integrated {mean.Z:+0.0000;-0.0000};  roll {grad.Z:+0.000000;-0.000000} vs {gradBrute.Z:+0.000000;-0.000000}");
            // Tolerances, not decimal places: the reference is a 2001-point trapezoid whose own quadrature error is
            // a few parts in ten thousand, and rounding to N decimals fails whenever that straddles a boundary.
            void Close(double want, double got, string what)
                => Assert.True(System.Math.Abs(got - want) <= 1e-3, $"{what}: {got:0.000000} vs integrated {want:0.000000}");
            Close(mean.X, filtered.X, "heave x");
            Close(mean.Y, filtered.Y, "heave y");
            Close(mean.Z, filtered.Z, "heave z");
            Assert.True(System.Math.Abs(grad.Z - gradBrute.Z) <= 0.01 * System.Math.Abs(gradBrute.Z) + 1e-5,
                        $"roll gradient {grad.Z:0.000000} vs integrated {gradBrute.Z:0.000000}");
        }
    }

    /// <summary>The point of it: eddies smaller than the aeroplane are averaged away, and the unfiltered sample is not.</summary>
    [Fact]
    public void SmallEddiesAreAveragedAwayAndLargeOnesAreNot()
    {
        var t = Field();
        Vec3 axis = new Vec3(0, 1, 0);
        double rawSmall = 0, filtSmall = 0, rawBig = 0, filtBig = 0;
        const int N = 400;
        for (int i = 0; i < N; i++)
        {
            Vec3 p = new Vec3(13.7 * i, 4.3 * i, -50);
            // A low-altitude length scale (small eddies) against a high-altitude one (large eddies).
            Vec3 fs = t.UnitGustAt(p, 0.3 * i, 15, 15, Vec3.Zero, 15.54, axis, out _);
            Vec3 rs = t.UnitGustAt(p, 0.3 * i, 15, 15, Vec3.Zero);
            Vec3 fb = t.UnitGustAt(p, 0.3 * i, 762, 762, Vec3.Zero, 15.54, axis, out _);
            Vec3 rb = t.UnitGustAt(p, 0.3 * i, 762, 762, Vec3.Zero);
            filtSmall += fs.Z * fs.Z; rawSmall += rs.Z * rs.Z;
            filtBig += fb.Z * fb.Z; rawBig += rb.Z * rb.Z;
        }
        double smallKept = System.Math.Sqrt(filtSmall / rawSmall), bigKept = System.Math.Sqrt(filtBig / rawBig);
        _out.WriteLine($"a 15.5 m span keeps {100 * smallKept:F0} % of a 15 m-scale field and {100 * bigKept:F0} % of a 762 m-scale one");
        // 80 % of the AMPLITUDE is 64 % of the energy, so the filter removes about a third of the gust energy at a
        // low-altitude length scale -- which is the 30 % of variance GustScaleProbe measured below a span, arriving
        // from the other direction. Large eddies pass essentially untouched, as they must.
        Assert.InRange(smallKept, 0.70, 0.90);
        Assert.InRange(bigKept, 0.95, 1.0);
    }
}
