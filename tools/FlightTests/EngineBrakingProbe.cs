using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE ENGINE'S TORQUE DOES WITH RPM, AT EVERY THROTTLE (ENGBRAKE=1). The whole point of an FMEP/PMEP model is
/// that a throttled engine BRAKES harder the faster you turn it; this prints the table that says whether it does.
///
/// It prints, per type: brake torque against rpm at a ladder of throttles, the manifold pressure each throttle makes,
/// the rated-power check (full throttle at rated rpm must stay exactly rated), the idle-stop throttle the bisection
/// finds, the static idle rpm it holds, and the idle rpm at 35 m/s of inflow.
/// </summary>
public class EngineBrakingProbe
{
    private readonly ITestOutputHelper _o;
    public EngineBrakingProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));

    internal static readonly string[] Types =
    {
        "pitts-s2b-like", "j3-cub-like", "extra-300-like", "great-lakes-like", "j3-cub-floats-like",
    };

    private static readonly double[] Rpms = { 700, 1000, 1200, 1500, 1800, 2100, 2400, 2500, 2700 };
    private static readonly double[] Throttles = { 0.0, 0.1, 0.25, 0.5, 0.75, 1.0 };

    private static BladeElement.Geometry BladeOf(PropulsionConfig p, double pitchDeg = 0.0)
    {
        double pitch = pitchDeg > 0 ? pitchDeg
                     : p.BladePitch75Deg > 0 ? p.BladePitch75Deg
                     : p.ConstantSpeed ? Math.Max(1.0, PropModel.OilLossPitchDeg(p)) : 15.0;
        return new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
    }

    /// <summary>ENGBRAKE=1 WOTSWEEP=1: what the fleet does as the one sensitive constant, the published wide-open
    /// manifold pressure at rated rpm, is swept across the range a real induction system could plausibly give.</summary>
    [Fact]
    public void SweepTheWideOpenManifold()
    {
        if (Environment.GetEnvironmentVariable("WOTSWEEP") != "1") return;
        double keep = PistonMep.WideOpenManifoldInHg;
        try
        {
            foreach (string id in new[] { "j3-cub-like", "pitts-s2b-like" })
            {
                AircraftConfig c = Load(id);
                PropulsionConfig p = c.Propulsion!;
                BladeElement.Geometry g = BladeOf(p);
                double r0 = 0.5 * p.PropDiameterM;
                _o.WriteLine($"\n=== {id}: WOT MP sweep (idle is RE-SOLVED at each, so static idle stays {p.IdleRpm:F0})");
                _o.WriteLine("   WOT MP | idle stop | MP@idle | rpm@25m/s | tip alpha | static WOT rpm | Q@2500 idle lever");
                foreach (double wot in new[] { 29.4, 29.0, 28.5, 28.0, 27.5, 27.0, 26.0 })
                {
                    PistonMep.WideOpenManifoldInHg = wot;
                    double bleed = PistonEngine.IdleBleedThrottle(g, p, 1.0, 1.225, 1.0);
                    double at25 = PistonEngine.BalancedRpm(g, p, 0.0, 1.0, 25.0, 1.225, 1.0);
                    double alpha = PropBladeDamage.BladeAlphaRad(p, r0, at25 * Math.PI / 30.0, 25.0, false) * 180 / Math.PI;
                    double stat = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 0.0, 1.225, 1.0);
                    _o.WriteLine($"   {wot,6:F1} |   {bleed,7:F4} | {PistonEngine.ManifoldInHg(bleed, 1.0, p.IdleRpm, p.MaxRpm),7:F2} | " +
                                 $"{at25,9:F0} | {alpha,+9:F2} | {stat,14:F0} | {PistonEngine.TorqueNm(p, 2500.0, bleed, 1.0, 1.0),8:F1}");
                }
            }
        }
        finally { PistonMep.WideOpenManifoldInHg = keep; }
    }

    /// <summary>IDLESOLVE=1: re-solve PistonMep.IdleIndicatedLossB against Continental's published C-85 idle. One
    /// equation in one unknown; run it whenever anything else in the decomposition moves.</summary>
    [Fact]
    public void SolveTheIdleIndicatedLoss()
    {
        if (Environment.GetEnvironmentVariable("IDLESOLVE") != "1") return;
        PropulsionConfig p = Load("j3-cub-like").Propulsion!;
        double club = PistonEngine.Spec.For(p).RatedTorqueNm * Math.Pow(PistonMep.C85IdleRpm / PistonMep.C85ClubStaticRpm, 2.0);
        double keep = PistonMep.IdleIndicatedLossB;
        double ThrottleForIdleMp()
        {
            double a = 0.0, b2 = 1.0;
            for (int i = 0; i < 44; i++)
            {
                double mid = 0.5 * (a + b2);
                if (PistonEngine.ManifoldInHg(mid, 1.0, PistonMep.C85IdleRpm, p.MaxRpm) < PistonMep.C85IdleManifoldInHg) a = mid; else b2 = mid;
            }
            return 0.5 * (a + b2);
        }
        try
        {
            double lo = 0.0, hi = 2.0, t = ThrottleForIdleMp();
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                PistonMep.IdleIndicatedLossB = mid;
                if (PistonEngine.TorqueNm(p, PistonMep.C85IdleRpm, t, 1.0, 1.0) > club) lo = mid; else hi = mid;
            }
            _o.WriteLine($"IdleIndicatedLossB solves to {0.5 * (lo + hi):F5} (in use {keep:F5}) " +
                         $"against Continental's {club:F2} N.m at {PistonMep.C85IdleRpm:F0} rpm / {PistonMep.C85IdleManifoldInHg:F1} inHg");
        }
        finally { PistonMep.IdleIndicatedLossB = keep; }
    }

    [Fact]
    public void MeasureIt()
    {
        if (Environment.GetEnvironmentVariable("ENGBRAKE") != "1") return;
        foreach (string id in Types)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double qRated = p.MaxPowerW / (p.MaxRpm * 2.0 * Math.PI / 60.0);
            double vd = PistonCompression.DisplacementM3(c, out bool authored);
            _o.WriteLine($"\n=== {id}: {p.MaxPowerW / 745.7:F0} hp at {p.MaxRpm:F0} rpm, idle {p.IdleRpm:F0}, " +
                         $"Q_rated {qRated:F1} N.m, Vd {vd / PistonCompression.CubicInchM3:F1} cu in ({(authored ? "authored" : "ESTIMATED")}), " +
                         $"BMEP_rated {4.0 * Math.PI * qRated / Math.Max(1e-9, vd) / 1e5:F2} bar, excess {PistonEngine.BoundaryExcessFor(p):F2}");
            _o.WriteLine("    MANIFOLD inHg      rpm  |" + string.Join("", Array.ConvertAll(Throttles, t => $"  thr {t:F2}")));
            foreach (double rpm in Rpms)
            {
                string row = $"                     {rpm,5:F0} |";
                foreach (double t in Throttles)
                    row += $"  {PistonEngine.ManifoldInHg(t, 1.0, rpm, p.MaxRpm),8:F2}";
                _o.WriteLine(row);
            }
            _o.WriteLine("    BRAKE TORQUE N.m   rpm  |" + string.Join("", Array.ConvertAll(Throttles, t => $"  thr {t:F2}")));
            foreach (double rpm in Rpms)
            {
                string row = $"                     {rpm,5:F0} |";
                foreach (double t in Throttles)
                    row += $"  {PistonEngine.TorqueNm(p, rpm, t, 1.0, 1.0),8:F1}";
                _o.WriteLine(row);
            }
            double pRated = PistonEngine.PowerW(p, p.MaxRpm, 1.0, 1.0, 1.0);
            _o.WriteLine($"    RATED CHECK: full throttle at {p.MaxRpm:F0} rpm = {pRated / 745.7:F2} hp " +
                         $"({pRated / p.MaxPowerW * 100.0:F3} % of rated)");
            // Motoring (dead engine) for reference.
            string mot = "    motoring (dead, thr 0): ";
            foreach (double rpm in Rpms) mot += $"{rpm:F0}:{PistonEngine.MotoringTorqueNm(p, rpm, 0.0, 1.0, 1.0),7:F1}  ";
            _o.WriteLine(mot);

            if (!PropModel.IntegratesShaft(p)) { _o.WriteLine("    (shaft not integrated: no blade fitted)"); continue; }
            BladeElement.Geometry g = BladeOf(p);
            double bleed = PistonEngine.IdleBleedThrottle(g, p, 1.0, 1.225, 1.0);
            _o.WriteLine($"    blade {g.PitchAt75Deg:F2} deg; idle-stop throttle {bleed:F4} -> MP at idle rpm " +
                         $"{PistonEngine.ManifoldInHg(bleed, 1.0, p.IdleRpm, p.MaxRpm):F2} inHg, and the SAME lever at 2,500 rpm " +
                         $"{PistonEngine.ManifoldInHg(bleed, 1.0, 2500.0, p.MaxRpm):F2} inHg -> " +
                         $"{PistonEngine.TorqueNm(p, 2500.0, bleed, 1.0, 1.0):F1} N.m");
            foreach (double v in new[] { 0.0, 10.0, 20.0, 25.0, 35.0, 50.0 })
            {
                double rpm = PistonEngine.BalancedRpm(g, p, 0.0, 1.0, v, 1.225, 1.0);
                _o.WriteLine($"      idle lever at {v,5:F0} m/s -> {rpm,7:F1} rpm" + (v == 0.0 ? $"   (published idle {p.IdleRpm:F0})" : ""));
            }
            // WHERE THE BLADE FLIPS FROM PULLING TO DRIVEN at 25 m/s, which is the condition PropBladeDamageTests'
            // two idle rows assert (a struck tip curls AFT only while the air is driving the blade).
            double r0 = 0.5 * p.PropDiameterM;
            double flip = 0.0;
            for (double rpm = 400; rpm <= 2000; rpm += 1.0)
            {
                double a = PropBladeDamage.BladeAlphaRad(p, r0, rpm * Math.PI / 30.0, 25.0, false);
                if (a >= 0.0) { flip = rpm; break; }
            }
            double idleAt25 = PistonEngine.BalancedRpm(g, p, 0.0, 1.0, 25.0, 1.225, 1.0);
            _o.WriteLine($"      TIP FLIPS to driven below {flip:F0} rpm at 25 m/s; the idle lever holds {idleAt25:F0} rpm there " +
                         $"-> tip alpha {PropBladeDamage.BladeAlphaRad(p, r0, idleAt25 * Math.PI / 30.0, 25.0, false) * 180 / Math.PI:+0.00;-0.00} deg " +
                         $"({(idleAt25 < flip ? "DRIVEN, curls aft" : "PULLING, curls forward")})");
            double statFull = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 0.0, 1.225, 1.0);
            _o.WriteLine($"      FULL throttle static {statFull:F1} rpm;  Vso {PracticeScenario.EstimateVso(c, 0.0, 0.0):F2} m/s");
        }
    }
}
