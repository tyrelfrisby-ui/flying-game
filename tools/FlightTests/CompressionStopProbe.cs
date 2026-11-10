using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHERE A DEAD PROPELLER LUMPS TO A STOP, AND WHAT IT TAKES TO START IT AGAIN (COMPSTOP=1). Ty 2026-11-10:
/// "Cranking resistance should be calibrated such that at fine pitch the prop stops right at level flight stall speed
/// ... To get prop windmilling again should take 10% under vne".
///
/// The restart speed is the CALIBRATION (Aircraft.CompressionPeakNm is solved from it), so it is a check that the
/// arithmetic closed. The stopping speed is a PREDICTION — nothing was fitted to it — so this measures it and prints
/// it beside the type's stall speed. If the two disagree, that is a finding for Ty and not a thing to quietly tune.
/// </summary>
public class CompressionStopProbe
{
    private readonly ITestOutputHelper _o;
    public CompressionStopProbe(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));

    [Fact]
    public void MeasureIt()
    {
        if (Environment.GetEnvironmentVariable("COMPSTOP") != "1") return;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like", "c172-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig? p = c.Propulsion;
            if (p is null || p.PropDiameterM <= 0.0) continue;
            double vne = c.Limits?.VneMs ?? 0.0;
            double fine = p.ConstantSpeed && p.FineStopDeg > 0.0 ? p.FineStopDeg
                        : p.BladePitch75Deg > 0.0 ? p.BladePitch75Deg : 15.0;
            int cyl = c.EngineCylinders;
            double cr = c.EngineCompressionRatio;
            double worst = PistonCompression.WorstRipple(cyl, cr);
            double qMean = -PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, p.MaxRpm, 0.0, 1.0, 1.0);

            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, fine, p.ActivityFactor);
            double qAirRestart = -BladeElement.Solve(g, 0.9 * vne, 0.0, 5.0, Atmosphere.SeaLevelDensityKgM3).TorqueNm;
            double peak = Math.Max(0.0, (qAirRestart - qMean) / Math.Max(1e-6, worst));

            _o.WriteLine($"--- {id}  {cyl} cyl  CR {cr:0.0}  fine {fine:0.0} deg  Vne {vne:0.0} m/s  " +
                         $"0.9Vne {0.9 * vne:0.0}  Imean {qMean:0.0} N·m  worstRipple {worst:0.00}  peak {peak:0.0} N·m" +
                         $"  (total resist {qMean + peak * worst:0.0} N·m)");

            // THE STOPPING SPEED: run the shaft down, blades fine, from a windmill at 0.9 Vne, easing the airspeed off
            // and letting the compressions do the work. The same integration AircraftEngine runs.
            double[] tau = PistonCompression.LeakTauSec(cyl, c.Id?.GetHashCode() ?? 1);
            var leak = new double[cyl];
            for (int i = 0; i < cyl; i++) leak[i] = 1.0;
            // SEEDED FROM A WINDMILL, because that is the scenario: it was turning and you slowed down. Started from
            // rest it can never break away at a speed the calibration made exactly marginal, which measures the
            // RESTART threshold by accident and says nothing about where a turning propeller gives up.
            BladeElement.Windmilling spun = BladeElement.Windmill(
                g, 0.9 * vne, Atmosphere.SeaLevelDensityKgM3, p.MaxRpm,
                r => PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, r, 0.0, 1.0, 1.0));
            double rpm = spun.Turning ? spun.Rpm : 0.0, crank = 0.0, dt = 0.002;
            double inertia = Math.Max(0.05, p.PropInertia);
            double stopped = double.NaN, rpmSeed = rpm;
            for (double v = 0.9 * vne; v > 2.0; v -= 0.25)
            {
                for (int step = 0; step < 400; step++)   // 0.8 s at each speed: long enough to settle
                {
                    double qProp = BladeElement.Solve(g, v, 0.0, rpm, Atmosphere.SeaLevelDensityKgM3).TorqueNm;
                    double ripple = PistonCompression.RippleFactor(crank, cyl, cr, leak);
                    double qEng = PistonEngine.MotoringTorqueNm(p.MaxPowerW, p.MaxRpm, rpm, 0.0, 1.0, 1.0) + peak * ripple;
                    double omega = Math.Max(0.0, rpm * 2.0 * Math.PI / 60.0 + (qEng - qProp) / inertia * dt);
                    rpm = Math.Min(omega * 60.0 / (2.0 * Math.PI), p.MaxRpm * 1.6);
                    crank = (crank + omega * dt) % (4.0 * Math.PI);
                    for (int i = 0; i < cyl; i++)
                    {
                        double cyc = (crank + 4.0 * Math.PI * i / cyl) % (4.0 * Math.PI);
                        bool comp = cyc >= 2.0 * Math.PI && cyc < 3.0 * Math.PI;
                        leak[i] = comp ? leak[i] * Math.Exp(-dt / Math.Max(0.05, tau[i])) : Math.Min(1.0, leak[i] + dt / 0.25);
                    }
                }
                if (rpm < 20.0 && double.IsNaN(stopped)) { stopped = v; break; }
            }
            _o.WriteLine($"    windmilling at 0.9Vne: {rpmSeed:0} rpm");
            _o.WriteLine($"    STOPS at {(double.IsNaN(stopped) ? "never (still turning at 2 m/s)" : $"{stopped:0.0} m/s = {stopped * 2.23694:0} mph")}" +
                         $"   restart anchor {0.9 * vne:0.0} m/s = {0.9 * vne * 2.23694:0} mph");
        }
    }
}
