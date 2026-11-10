namespace FlyingGame.Core;

/// <summary>
/// WHAT A BENT STABILISER LEAVES THE ELEVATOR. The owner's rule, 2026-11-10, and it is DELIBERATELY not the
/// aileron's (<see cref="BentAileronTravel"/>):
///
///   "For horizontal stab bending apply the same limited travel rule as the ailerons but be more generous on
///    available travel so the always have some of the other direction like if they push to max bending before
///    failure the elevator will move to full down still but only 10 percent up and vise versa"
///
/// So it is not a band that closes to nothing around where the stick was. It is ASYMMETRIC, and in the pilot's
/// favour on the side that matters:
///
/// * THE WAY IT WAS BENT keeps its FULL travel. Push hard enough to bend the stab and you can still push: the
///   surface is already loaded that way and the structure is already deflected to suit it.
/// * THE OTHER WAY fades from full travel at straight down to <see cref="MinOppositeFraction"/> at the bend that
///   would fail the tail — never to nothing. A pilot who bends the stab still has something to flare with, which
///   is the whole reason he asked for this one to be generous.
///
/// Which way "the way it was bent" IS comes from the elevator's own deflection when the tail first took a set,
/// captured once and kept — the same device as the aileron's centre, and for the same reason: the structure
/// remembers the load that bent it, not the stick's position now.
/// </summary>
public static class BentStabTravel
{
    /// <summary>What is left the OTHER way at the bend that fails the tail: a tenth of the travel (his number).</summary>
    public const double MinOppositeFraction = 0.10;

    /// <summary>
    /// The travel still available, rad, for a deflection of the given sign.
    /// <paramref name="towardSign"/> is the sign of the deflection that bent the tail (+1 or −1; 0 = never bent).
    /// </summary>
    public static double AvailableRad(double commandedSign, double towardSign, double bendRad, double ruptureRad, double maxDeflRad)
    {
        if (maxDeflRad <= 0.0) return 0.0;
        if (towardSign == 0.0 || !(ruptureRad > 0.0)) return maxDeflRad;      // never bent, or no failure angle known
        if (commandedSign == 0.0 || System.Math.Sign(commandedSign) == System.Math.Sign(towardSign))
            return maxDeflRad;                                                // the way it was bent: all of it
        double f = 1.0 - System.Math.Abs(bendRad) / ruptureRad;               // 1 straight, 0 at failure
        return maxDeflRad * (MinOppositeFraction + (1.0 - MinOppositeFraction) * System.Math.Clamp(f, 0.0, 1.0));
    }

    /// <summary>The commanded elevator held to what the bend has left it.</summary>
    public static double Clamp(double commandedRad, double towardSign, double bendRad, double ruptureRad, double maxDeflRad)
    {
        double available = AvailableRad(System.Math.Sign(commandedRad), towardSign, bendRad, ruptureRad, maxDeflRad);
        return System.Math.Clamp(commandedRad, -available, available);
    }
}
