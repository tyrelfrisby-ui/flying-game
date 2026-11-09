using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>
/// What happened to the aeroplane and to the pilot, said once at the end of a flight (Ty 2026-11-08: "After
/// completing a flight either by switching planes or going back to menu or crashing a summary of all damage should be
/// listed including the pilots injuries or death" — "And the cause of the injuries including accelerations etc").
///
/// THE INJURY NUMBERS ARE REAL AND THEY ARE NOT OURS. <see cref="OccupantInjury"/> is Brinkley's dynamic-response
/// model with the limit sets tabulated for NASA-STD-3001, and those three sets carry published risks: reaching the
/// LOW limits is about a 0.5 % chance of injury, MODERATE about 5 %, HIGH about 50 %. β ≥ 1 against a set is what
/// "reaching" it means — β being each axis's response divided by that axis's limit, combined in quadrature — so the
/// model says how likely an injury was, not merely whether one happened.
///
/// WHAT IS NOT REAL, said plainly because the difference matters: Brinkley predicts INJURY, not death. There is no
/// published fatal β, and the tolerance curves that do exist (Eiband 1959) are voluntary-exposure limits which their
/// own literature warns are "not design values". So the fatal threshold in OccupantInjury is an extrapolation, and
/// the summary never dresses it up as measured: it reports the risk the model does give, and the accelerations it
/// was computed from, which are the honest part.
/// </summary>
public static class FlightSummary
{
    /// <summary>Published chance of injury on reaching each Brinkley limit set (NASA-STD-3001, %).</summary>
    public const double LowRiskInjuryPct = 0.5, ModerateRiskInjuryPct = 5.0, HighRiskInjuryPct = 50.0;

    /// <summary>The chance of injury the model gives for what the occupant actually took.</summary>
    public static double InjuryRiskPct(double betaLow, double betaModerate, double betaHigh) =>
        betaHigh >= 1.0 ? HighRiskInjuryPct
        : betaModerate >= 1.0 ? ModerateRiskInjuryPct
        : betaLow >= 1.0 ? LowRiskInjuryPct
        : 0.0;

    /// <summary>Which axis took the occupant closest to its limit, and what it read. The axes are the body's, not the
    /// aeroplane's: X fore-and-aft, Y sideways, Z along the spine.</summary>
    public static (string Axis, double Dr, double Share) WorstAxis(double drX, double drY, double drZ, in OccupantInjury.Limits lim)
    {
        double bx = System.Math.Abs(drX / (drX >= 0 ? lim.XPos : lim.XNeg));
        double by = System.Math.Abs(drY / (drY >= 0 ? lim.YPos : lim.YNeg));
        double bz = System.Math.Abs(drZ / (drZ >= 0 ? lim.ZPos : lim.ZNeg));
        if (bz >= bx && bz >= by) return (drZ >= 0 ? "down the spine" : "up the spine", drZ, bz);
        if (bx >= by) return (drX >= 0 ? "chest-to-back" : "back-to-chest", drX, bx);
        return ("sideways", drY, by);
    }

    /// <summary>The pilot's line: the outcome, what it was caused by, and the numbers behind it.</summary>
    public static string PilotLine(LandingOutcome level, double betaLow, double betaModerate, double betaHigh,
                                   double drX, double drY, double drZ, double crushM)
    {
        if (level == LandingOutcome.Unhurt) return "Pilot: unhurt.";
        bool byCrush = OccupantInjury.FromCrush(crushM) >= OccupantInjury.FromLoads(betaLow, betaModerate, betaHigh) && crushM > 0;
        string what = level switch
        {
            LandingOutcome.Dead => "Pilot: killed",
            LandingOutcome.Injured => "Pilot: injured",
            _ => "Pilot: bruised",
        };
        if (byCrush) return $"{what} — the cockpit crushed {crushM:F2} m around him.";
        var (axis, dr, _) = WorstAxis(drX, drY, drZ, OccupantInjury.HighRisk);
        double risk = InjuryRiskPct(betaLow, betaModerate, betaHigh);
        string riskText = risk > 0 ? $", about a {risk:0.#} % chance of injury at that load" : "";
        return $"{what} — {System.Math.Abs(dr):F1} g {axis}{riskText}.";
    }

    /// <summary>The whole report: how the flight ended, what broke, and what it did to the pilot.</summary>
    public static string Build(string ending, IReadOnlyList<string> damage, string pilotLine)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.IsNullOrEmpty(ending) ? "Flight ended" : ending);
        sb.Append('\n');
        if (damage == null || damage.Count == 0) sb.Append("No damage.");
        else for (int i = 0; i < damage.Count; i++) { if (i > 0) sb.Append('\n'); sb.Append("• ").Append(damage[i]); }
        if (!string.IsNullOrEmpty(pilotLine)) sb.Append('\n').Append(pilotLine);
        return sb.ToString();
    }
}
