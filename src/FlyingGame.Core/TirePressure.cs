using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// What every type's tyres are blown up to, because that one number decides when a wheel hydroplanes
/// (<see cref="WheelHydro"/>: V_p in knots = 9√p) and therefore whether a pilot can skim a gravel bar at all.
///
/// The owner's rule, 2026-10-16: the AFM figure where there is one, and otherwise
///
///     standard tyres   psi = gross weight in lb / 100
///     BUSHWHEELS       psi = gross weight in lb / 200
///
/// (He wrote "1 psi for every 10 lb" and "1/2 psi for every 1000 lb". Per 10 lb would put a 1,750 lb Cub at
/// 175 psi — airliner pressure on a Cub — while per 100 lb gives 17.5 psi against a POH figure of 18, and the
/// bushwheel rule per 100 lb gives 8.75 psi against real bushwheel practice of 8–10. So it is read as per
/// 100 lb, and it lands on the books.)
///
/// The table below is only the figures worth trusting. Everything else falls to the rule, which is what the
/// rule is for — and a type gets the book figure the moment someone puts it in.
/// </summary>
public static class TirePressure
{
    public const double PoundsPerPsiStandard = 100.0;
    public const double PoundsPerPsiBushwheel = 200.0;
    /// <summary>What a bushwheel does to the tyre: twice the diameter, and no wheel pants.</summary>
    public const double BushwheelRadiusFactor = 2.0;

    /// <summary>Book figures, main and nose/tail, in psi. Added only where the number is worth more than the
    /// rule of thumb that would replace it.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, (double main, double other)> Book = new()
    {
        ["pa18-cub-like"] = (18, 18),        // PA-18 POH: 8.00-6 at 18 psi, tailwheel the same
        ["pa18-bush-like"] = (8, 8),         // the same aeroplane on bushwheels, run soft on purpose
        ["c172-like"] = (38, 29),            // 172S POH: mains 38, nose 29
        ["pa28-archer-like"] = (24, 18),     // Archer POH
        ["boeing-737-like"] = (205, 190),    // airliner mains and nose, the order of magnitude that matters
    };

    /// <summary>Is this leg a main wheel? (The others — nose and tail — often run softer.)</summary>
    private static bool IsMain(GearConfig g) => !g.IsTailwheel && System.Math.Abs(g.Pos[1]) > 0.3;

    /// <summary>
    /// Tyre pressure for one leg, in psi. <paramref name="bushwheels"/> is the option that fits any aeroplane
    /// with big soft tyres: half the pressure, twice the tyre.
    /// </summary>
    public static double Psi(AircraftConfig c, GearConfig g, bool bushwheels)
    {
        if (g.TirePressurePsi > 0 && !bushwheels) return g.TirePressurePsi;

        string id = c.Id ?? "";
        if (!bushwheels && Book.TryGetValue(id, out var book)) return IsMain(g) ? book.main : book.other;
        if (bushwheels && Book.TryGetValue("pa18-bush-like", out var bush) && id == "pa18-bush-like")
            return IsMain(g) ? bush.main : bush.other;

        double grossLb = (c.Mass != null ? c.Mass.MassKg : 800) * 2.20462;
        double psi = grossLb / (bushwheels ? PoundsPerPsiBushwheel : PoundsPerPsiStandard);
        // A nose or tail wheel carries far less and is run softer; and no tyre is below 5 psi or above 260.
        if (!IsMain(g)) psi *= 0.8;
        return System.Math.Clamp(psi, 5.0, 260.0);
    }

    /// <summary>The planing speed that pressure gives, in m/s — the number a pilot skimming a bar lives by.</summary>
    public static double HydroplaneSpeedMs(AircraftConfig c, GearConfig g, bool bushwheels)
        => WheelHydro.HydroplaneSpeedMs(Psi(c, g, bushwheels));
}
