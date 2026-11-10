using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHERE IS THE GUST ENERGY, AND IS THE AEROPLANE BIGGER THAN IT? (Ty 2026-11-09: "The jitter is on all airplanes no
/// matter the speed or power including the glider so it is either poorly modeled turbulence or some kind of bug", at
/// KSNA.) The wind is sampled at ONE point (the CG) and applied as a uniform gust to the whole airframe. A real wing
/// integrates a gust across its span, so everything smaller than the aeroplane is averaged away. If a large share of
/// the field's variance sits at wavelengths below a span, a point sample shakes the aeroplane with eddies it should
/// barely feel -- at every speed, on every type, worst near the ground where the length scale collapses.
/// </summary>
public class GustScaleProbe
{
    private readonly ITestOutputHelper _o;
    public GustScaleProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        var t = new Turbulence(TurbulenceSpectrum.VonKarman, 48, 7, 15, 762);
        _o.WriteLine("Variance by eddy size, for the length scale the atmosphere picks at each height (MIL L ~ h).");
        _o.WriteLine("  AGL     L     share of variance in eddies SMALLER than:   6 m     15 m     30 m");
        foreach (double aglFt in new[] { 50.0, 174.0, 500.0, 1000.0, 3000.0 })
        {
            double agl = aglFt / TurbulenceScales.FtPerM;
            var sc = TurbulenceScales.LowAltitude(agl, 15, TurbulenceSpectrum.VonKarman);
            double L = sc.LengthW;
            double tot = 0, u6 = 0, u15 = 0, u30 = 0;
            for (double lam = 1.0; lam < 5000; lam *= 1.02)
            {
                double k = 2 * System.Math.PI / lam;
                double e = Turbulence.EnergySpectrumShape(TurbulenceSpectrum.VonKarman, k, L) * k;  // per ln k
                tot += e;
                if (lam < 6) u6 += e;
                if (lam < 15) u15 += e;
                if (lam < 30) u30 += e;
            }
            _o.WriteLine($"  {aglFt,5:F0} ft  {L,6:F1} m        {100 * u6 / tot,8:F1} % {100 * u15 / tot,8:F1} % {100 * u30 / tot,8:F1} %");
        }
        _o.WriteLine("");
        _o.WriteLine("What a 15 m span averages away: a sinusoidal gust of wavenumber k across a span b has mean");
        _o.WriteLine("sinc(k*b/2), so the aeroplane feels only that fraction of it as a whole-body gust.");
        foreach (double lam in new[] { 2.0, 4.0, 8.0, 15.0, 30.0, 60.0, 150.0 })
        {
            double k = 2 * System.Math.PI / lam, x = 0.5 * k * 15.0;
            double sinc = System.Math.Abs(x) < 1e-9 ? 1.0 : System.Math.Sin(x) / x;
            _o.WriteLine($"   wavelength {lam,6:F0} m  ->  a 15 m span feels {100 * System.Math.Abs(sinc),5:F1} % of it");
        }
    }
}
