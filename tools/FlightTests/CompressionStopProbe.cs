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
/// ONE OF THOSE IS STILL AN ANCHOR AND THE OTHER IS NOW A RESULT. The STOPPING speed solves each engine's dry boundary
/// friction (<see cref="PistonEngine.BoundaryExcessFor"/>) against the type's own stall speed —
/// <see cref="PracticeScenario.EstimateVso"/>, the number the rest of the suite is tuned against rather than a book
/// figure. The RESTART speed is a measurement: Ty gave that anchor up on 2026-11-10, shown that the same AEIO-540 came
/// out at 99 N·m in the Pitts and 683 in the Extra purely because their airframes have different Vne — "that settles
/// it - compute from displacement" — so <see cref="PistonCompression.PeakNmFor"/> is the engine's own gas load now and
/// whatever speed the propeller breaks away at is a finding. This prints it, with 0.9 Vne beside it as the figure he
/// expected and NOT as a target.
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

            double displacement = PistonCompression.DisplacementM3(c, out bool authored);
            _o.WriteLine($"--- {id}  {c.EngineCylinders} cyl  CR {c.EngineCompressionRatio:0.0}  fine {fine:0.0} deg  " +
                         $"Vne {vne:0.0} m/s  Vso {vso:0.0} m/s = {vso * 2.23694:0} mph  " +
                         $"displacement {displacement / PistonCompression.CubicInchM3:0} cu in ({(authored ? "authored" : "ESTIMATED from rated torque at the fleet BMEP")})");
            _o.WriteLine($"    dry excess {excess:0.00} ({1.0 + excess:0.00}x the running friction at rest)  " +
                         $"mean at rest {qRest:0.0} N·m, at rated {qRated:0.0}  worstRipple {worst:0.00}  peak {peak:0.0} N·m" +
                         $"  (ripple {peak * worst / qRest * 100:0}% of the mean at rest; total {qRest + peak * worst:0.0} N·m)");

            double start = SweepStartVsoMultiple * vso;
            double stopped = PistonCompression.PropStopSpeedMs(c, start);
            _o.WriteLine($"    STOPS at {(double.IsNaN(stopped) ? "never (still turning at 2 m/s)" : $"{stopped:0.0} m/s = {stopped * 2.23694:0} mph")}" +
                         $"   against Vso {vso:0.0} m/s = {vso * 2.23694:0} mph   err {(stopped - vso) / vso * 100:+0.0;-0.0}%   (ANCHOR)");

            // THE RESTART IS A RESULT NOW, not a target: whatever speed the air can shove this engine's own compression
            // over from rest. 0.9 Vne is printed because it is what Ty expected, and a type that lands nowhere near it
            // is a finding for him rather than something to tune back.
            double restart = PistonCompression.RestartSpeedMs(c);
            string Say(double v) => double.IsNaN(v) ? "NEVER within 400 m/s"
                                  : $"{v:0.0} m/s = {v * 2.23694:0} mph = {v / vne:0.00} Vne";
            _o.WriteLine($"    RESTARTS at {Say(restart)}   (he expected 0.90 Vne; this is a measurement, not an anchor)");
            // And again on cylinders that have been standing a few seconds: his "occasional flips through a compression
            // stroke can occur at speeds below" is the leak-down, and this is how far below.
            _o.WriteLine($"      sealed {Say(restart)};  bled to 1/2 {Say(PistonCompression.RestartSpeedMs(c, 0.5))};" +
                         $"  bled to 1/5 {Say(PistonCompression.RestartSpeedMs(c, 0.2))}");

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
