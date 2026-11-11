using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE ENGINE MODEL AGAINST PUBLISHED DATA — the three anchors PistonMep is built on, asserted so none of them can
/// drift. Every number here is from a manufacturer's manual or a NACA report, not from this repository.
/// </summary>
public class PistonMepTests
{
    private readonly ITestOutputHelper _o;
    public PistonMepTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));

    /// <summary>
    /// CONTINENTAL'S PUBLISHED IDLE, which is the J-3's own engine and the one anchor the low-speed indicated-efficiency
    /// loss is solved from. Operator's Manual X30012, Table XXI "Test Operating Limits": the C-85 idles at 475-525 rpm
    /// against 17 inHg of manifold vacuum (12.9 inHg absolute), and turns its test club at 2,575-2,625 rpm at full
    /// throttle. A club is fixed pitch, so its torque goes as the square of the speed, and the two lines together say
    /// the engine makes Q_rated * (500/2600)^2 = 8.7 N.m at that idle.
    /// </summary>
    [Fact]
    public void TheC85LandsOnContinentalsPublishedIdle()
    {
        PropulsionConfig p = Load("j3-cub-like").Propulsion!;
        var e = PistonEngine.Spec.For(p);
        double club = e.RatedTorqueNm * Math.Pow(PistonMep.C85IdleRpm / PistonMep.C85ClubStaticRpm, 2.0);
        // The throttle that puts the manifold at Continental's published idle vacuum, at Continental's idle speed.
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (PistonEngine.ManifoldInHg(mid, 1.0, PistonMep.C85IdleRpm, p.MaxRpm) < PistonMep.C85IdleManifoldInHg) lo = mid; else hi = mid;
        }
        double q = PistonEngine.TorqueNm(p, PistonMep.C85IdleRpm, 0.5 * (lo + hi), 1.0, 1.0);
        _o.WriteLine($"C-85 at Continental's {PistonMep.C85IdleRpm:F0} rpm and {PistonMep.C85IdleManifoldInHg:F1} inHg: " +
                     $"{q:F2} N.m against the test club's {club:F2} N.m");
        Assert.Equal(club, q, 1.0);     // within 1 N.m of the published club load
    }

    /// <summary>
    /// LYCOMING'S OWN PART-THROTTLE CHARTS, as the zero-power manifold pressure at rated rpm. Fitted off the sea-level
    /// performance figures: O-360-A 6.42 inHg at 2,700 (Fig 3-17, manual 60297-12); O-540-A 6.94 at 2,575 and
    /// IO-540-K 7.22 at 2,700 (Figs 3-4 and 3-23, manual 60297-10), each +/- 0.2 inHg for the extrapolation from the
    /// plotted 18-29 inHg band. Three draughtsmen disagree about whether it RISES with rpm — the IO-540 sheet says
    /// yes, the O-540-A sheet is flat and the O-360 sheet falls — so this asserts the BAND and not a trend.
    /// </summary>
    [Theory]
    [InlineData("great-lakes-like")]   // AEIO-360, 180 hp at 2700 — the O-360-A sheet's class
    [InlineData("pitts-s2b-like")]     // AEIO-540, 260 hp at 2700
    [InlineData("extra-300-like")]     // AEIO-540, 300 hp at 2700 — the IO-540-K sheet's class
    public void ZeroTorqueManifoldAtRatedRpmIsInLycomingsBand(string id)
    {
        PropulsionConfig p = Load(id).Propulsion!;
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 48; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (PistonEngine.TorqueNm(p, p.MaxRpm, mid, 1.0, 1.0) < 0.0) lo = mid; else hi = mid;
        }
        double mp = PistonEngine.ManifoldInHg(0.5 * (lo + hi), 1.0, p.MaxRpm, p.MaxRpm);
        _o.WriteLine($"{id}: zero torque at {p.MaxRpm:F0} rpm needs {mp:F2} inHg (Lycoming's sheets: 6.4 to 7.2)");
        Assert.InRange(mp, 5.3, 8.4);
    }

    /// <summary>
    /// RATED POWER IS EXACT, and it has to be: if a friction model eats rated power it is wrong. The whole
    /// decomposition is normalised at this one point (PistonEngine.Spec), so this is a statement about the
    /// bookkeeping rather than about the physics — which is why it is asserted to five figures.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")] [InlineData("j3-cub-like")] [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")] [InlineData("j3-cub-floats-like")] [InlineData("c172-like")]
    public void FullThrottleAtRatedRpmIsExactlyRatedPower(string id)
    {
        PropulsionConfig p = Load(id).Propulsion!;
        double w = PistonEngine.PowerW(p, p.MaxRpm, 1.0, 1.0, 1.0);
        _o.WriteLine($"{id}: {w / 745.7:F3} hp against a rated {p.MaxPowerW / 745.7:F3}");
        Assert.Equal(1.0, w / p.MaxPowerW, 4);
    }

    /// <summary>
    /// LYCOMING'S OWN INVARIANT: AT A FIXED MANIFOLD PRESSURE THE TORQUE IS FLAT. On the IO-540-K sea-level sheet
    /// (Fig 3-23, manual 60297-10) the quantity bhp@24 inHg divided by rpm is 0.0856 to 0.0859 across all ten rpm
    /// lines from 1800 to 2700 — a spread of 0.15 %. Power over speed IS torque, so Lycoming are saying that at
    /// 24 inHg their engine makes the same torque at 1,800 as at 2,700.
    ///
    /// THIS IS THE TEST THAT SAYS WHERE ENGINE BRAKING COMES FROM, and it is worth being clear because it corrects
    /// what this test originally asserted. Braking is NOT the engine making less torque at a given manifold pressure
    /// as it speeds up — at fixed manifold pressure it very nearly does not. It is that a fixed THROTTLE cannot hold
    /// the manifold pressure up as the engine speeds up and swallows more (PistonMep.ManifoldPa). The friction rise
    /// is real and it is in here, but on this evidence it is the smaller half.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")] [InlineData("extra-300-like")] [InlineData("great-lakes-like")]
    public void AtAFixedManifoldPressureTheTorqueIsNearlyFlat(string id)
    {
        PropulsionConfig p = Load(id).Propulsion!;
        double min = double.MaxValue, max = double.MinValue;
        foreach (double rpm in new[] { 1800.0, 2000.0, 2200.0, 2400.0, 2600.0, 2700.0 })
        {
            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 44; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (PistonEngine.ManifoldInHg(mid, 1.0, rpm, p.MaxRpm) < 24.0) lo = mid; else hi = mid;
            }
            double q = PistonEngine.TorqueNm(p, rpm, 0.5 * (lo + hi), 1.0, 1.0);
            _o.WriteLine($"{id} at 24.0 inHg, {rpm,5:F0} rpm: {q,7:F1} N.m");
            min = Math.Min(min, q); max = Math.Max(max, q);
        }
        _o.WriteLine($"{id}: spread {100 * (max / min - 1):F1} % (Lycoming's IO-540-K sheet: 0.15 %)");
        Assert.InRange(max / min, 1.0, 1.12);
    }

    /// <summary>
    /// ENGINE BRAKING: the behaviour this whole model exists for, and it lives at a CRACKED THROTTLE. The old law's
    /// torque had no rpm in it and was flat to the digit — +22.6 N.m on the Pitts at 700, 1,200, 1,800 and 2,500
    /// alike. Now a nearly shut throttle must make the torque fall away hard and go NEGATIVE well before the red
    /// line, which is what a pilot feels closing the throttle and what the two idle rows of PropBladeDamageTests are
    /// about.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")] [InlineData("j3-cub-like")] [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")] [InlineData("j3-cub-floats-like")]
    public void ACrackedThrottleBrakesHarderTheFasterItIsTurned(string id)
    {
        PropulsionConfig p = Load(id).Propulsion!;
        foreach (double t in new[] { 0.0, 0.1, 0.25 })
        {
            double peak = double.MinValue, peakRpm = 0.0;
            for (double rpm = 400; rpm <= p.MaxRpm + 1e-9; rpm += 25)
            {
                double q = PistonEngine.TorqueNm(p, rpm, t, 1.0, 1.0);
                if (q > peak) { peak = q; peakRpm = rpm; }
            }
            double atRed = PistonEngine.TorqueNm(p, p.MaxRpm, t, 1.0, 1.0);
            _o.WriteLine($"{id} throttle {t:F2}: peaks {peak,8:F1} N.m at {peakRpm,5:F0} rpm, {atRed,8:F1} at the red line");
            double prev = double.MaxValue;
            for (double rpm = peakRpm; rpm <= p.MaxRpm + 1e-9; rpm += 50)
            {
                double q = PistonEngine.TorqueNm(p, rpm, t, 1.0, 1.0);
                Assert.True(q < prev, $"{id} at throttle {t:F2}: past its peak the torque must fall, {prev:F1} then {q:F1} at {rpm:F0}");
                prev = q;
            }
            Assert.True(atRed < peak - 1.0, $"{id} at throttle {t:F2}: must brake by the red line");
        }
        Assert.True(PistonEngine.TorqueNm(p, p.MaxRpm, 0.1, 1.0, 1.0) < 0.0,
                    $"{id}: a cracked throttle must ABSORB torque at the red line");
        // The full-throttle curve is a different animal and Lycoming's sheets say so: see
        // AtAFixedManifoldPressureTheTorqueIsNearlyFlat. All that is asserted wide open is that nothing runs away.
        double wotPeak = double.MinValue;
        for (double rpm = 400; rpm <= p.MaxRpm; rpm += 25) wotPeak = Math.Max(wotPeak, PistonEngine.TorqueNm(p, rpm, 1.0, 1.0, 1.0));
        wotPeak = Math.Max(wotPeak, PistonEngine.TorqueNm(p, p.MaxRpm, 1.0, 1.0, 1.0));   // the stepped scan can miss the red line by a float
        _o.WriteLine($"{id}: full throttle peaks at {wotPeak / PistonEngine.Spec.For(p).RatedTorqueNm:F3} of rated-rpm torque");
        Assert.InRange(wotPeak / PistonEngine.Spec.For(p).RatedTorqueNm, 1.0, 1.15);
    }

    /// <summary>
    /// GAGG AND FERRAR ARE DERIVED NOW, NOT APPLIED. The altitude lapse used to be a factor multiplying the power
    /// curve; the 0.117 in it is a friction statement, so PistonEngine.Spec reads it that way to set FMEP at the
    /// rating and the lapse falls out of the decomposition instead. The two agree closely but not exactly, and the
    /// difference is physical: Gagg and Ferrar hold friction and pumping constant with height, where the
    /// decomposition lets the gas load and the exhaust back pressure fall with the air, so the model is slightly the
    /// less lossy of the two. This pins that gap so it cannot grow without someone noticing.
    /// </summary>
    [Fact]
    public void TheWideOpenAltitudeLapseIsStillGaggAndFerrars()
    {
        PropulsionConfig p = Load("pitts-s2b-like").Propulsion!;
        foreach ((double delta, double sigma, string ft) in new[] { (0.8320, 0.8617, "5,000"), (0.6877, 0.7385, "10,000"), (0.5643, 0.6295, "15,000") })
        {
            double model = PistonEngine.PowerW(p, p.MaxRpm, 1.0, delta, sigma) / p.MaxPowerW;
            double gagg = PistonEngine.AltitudePowerFraction(sigma);
            _o.WriteLine($"{ft,7} ft: decomposition {model:F4}, Gagg-Ferrar {gagg:F4}, {100 * (model / gagg - 1):+0.00;-0.00} %");
            Assert.InRange(model / gagg, 1.0, 1.06);
        }
    }
}
