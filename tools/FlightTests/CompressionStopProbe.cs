using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHERE A DEAD PROPELLER LUMPS TO A STOP, AND WHAT IT TAKES TO START IT AGAIN (COMPSTOP=1). Ty 2026-11-10:
/// "Cranking resistance should be calibrated such that at fine pitch the prop stops right at level flight stall speed
/// ... To get prop windmilling again should take 10% under vne".
///
/// Both are now CALIBRATIONS, one anchor each: the restart speed solves <see cref="PistonCompression.PeakNmFor"/> and
/// the stopping speed solves <see cref="PistonEngine.BoundaryExcessFor"/>. So this is where both get measured back out
/// of the model — the stop against the type's own stall speed (<see cref="PracticeScenario.EstimateVso"/>, which is
/// the number the rest of the suite is tuned against, not a book figure) and the restart against its own Vne.
///
/// SOLVE=1 as well and it bisects each type's dry boundary excess instead of reading it: that is how the authored
/// propulsion.dryBoundaryExcess values were derived, and how to re-derive one when a type's aerodynamics move.
/// </summary>
public class CompressionStopProbe
{
    private readonly ITestOutputHelper _o;
    public CompressionStopProbe(ITestOutputHelper o) { _o = o; }

    internal static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));

    /// <summary>Every type the mechanism is live on: a fitted fixed-pitch blade, or a governor with fitted stops that
    /// becomes a fixed-pitch propeller the moment it loses its oil (AircraftEngine.SolvesShaft).</summary>
    internal static readonly string[] LiveTypes =
    {
        "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like", "j3-cub-floats-like",
    };

    /// <summary>The sweep starts from a windmill at 1.7 Vso rather than at 0.9 Vne. Measured on all five the two agree
    /// to within 0.3 m/s, and from 0.9 Vne the sweep spends most of its time at speeds where nothing is in doubt — two
    /// to four times the cost on the Extra, whose 0.9 Vne is 102 m/s.</summary>
    internal const double SweepStartVsoMultiple = 1.7;

    [Fact]
    public void MeasureIt()
    {
        if (Environment.GetEnvironmentVariable("COMPSTOP") != "1") return;
        bool solve = Environment.GetEnvironmentVariable("SOLVE") == "1";
        foreach (string id in LiveTypes)
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double vne = c.Limits?.VneMs ?? 0.0, vso = PracticeScenario.EstimateVso(c, 0.0, 0.0);
            double fine = PistonCompression.FinePitchDeg(p);
            double worst = PistonCompression.WorstRipple(c.EngineCylinders, c.EngineCompressionRatio);
            double excess = PistonEngine.BoundaryExcessFor(p);
            double qRest = -PistonEngine.MotoringTorqueNm(p, PistonCompression.AtRestRpm, 0.0, 1.0, 1.0);
            double qRated = -PistonEngine.MotoringTorqueNm(p, p.MaxRpm, 0.0, 1.0, 1.0);
            double peak = PistonCompression.PeakNmFor(c);

            _o.WriteLine($"--- {id}  {c.EngineCylinders} cyl  CR {c.EngineCompressionRatio:0.0}  fine {fine:0.0} deg  " +
                         $"Vne {vne:0.0} m/s  Vso {vso:0.0} m/s = {vso * 2.23694:0} mph");
            _o.WriteLine($"    dry excess {excess:0.00} ({1.0 + excess:0.00}x the running friction at rest)  " +
                         $"mean at rest {qRest:0.0} N·m, at rated {qRated:0.0}  worstRipple {worst:0.00}  peak {peak:0.0} N·m" +
                         $"  (total at rest {qRest + peak * worst:0.0} N·m)");

            double start = SweepStartVsoMultiple * vso;
            double stopped = PistonCompression.PropStopSpeedMs(c, start);
            _o.WriteLine($"    STOPS at {(double.IsNaN(stopped) ? "never (still turning at 2 m/s)" : $"{stopped:0.0} m/s = {stopped * 2.23694:0} mph")}" +
                         $"   against Vso {vso:0.0} m/s = {vso * 2.23694:0} mph   err {(stopped - vso) / vso * 100:+0.0;-0.0}%");

            // THE RESTART, measured rather than assumed: the slowest speed at which the air on a stopped blade beats the
            // mean at rest plus the worst of the ripple. PeakNmFor is solved so this lands on 0.9 Vne.
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, fine, p.ActivityFactor);
            double QAir(double v) => -BladeElement.Solve(g, v, 0.0, PistonCompression.AtRestRpm, Atmosphere.SeaLevelDensityKgM3).TorqueNm;
            double need = qRest + peak * worst, lo = 1.0, hi = 400.0;
            for (int i = 0; i < 40; i++) { double m = 0.5 * (lo + hi); if (QAir(m) >= need) hi = m; else lo = m; }
            double restart = 0.5 * (lo + hi);
            _o.WriteLine($"    RESTARTS at {restart:0.0} m/s = {restart / vne:0.000} Vne   (his anchor: 0.900)");

            if (!solve) continue;
            // THE SOLVE: the one excess that puts the stopping speed on Vso. More friction stops it sooner and sooner
            // means a HIGHER speed, so the relation is monotone and a bisection finds it.
            double elo = 0.0, ehi = 20.0;
            for (int i = 0; i < 14; i++)
            {
                double mid = 0.5 * (elo + ehi);
                p.DryBoundaryExcess = mid;
                double s = PistonCompression.PropStopSpeedMs(c, start);
                if (!double.IsNaN(s) && s >= vso) ehi = mid; else elo = mid;
            }
            p.DryBoundaryExcess = excess;
            _o.WriteLine($"    SOLVE: dryBoundaryExcess {0.5 * (elo + ehi):0.00} puts it on Vso (in use: {excess:0.00})");
        }
    }
}
