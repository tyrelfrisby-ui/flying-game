namespace FlyingGame.Core;

/// <summary>
/// WHAT A BENT WING LEAVES THE AILERON. Owner's feature, 2026-11-10, in his own words:
///
///   "The ailerons get limited travel with bending linearly with initial bend to failure and the center of that
///    travel is the aileron position at the time the bending happened (if I roll then pull at high speed my
///    ailerons might be limited to that one side only with limited range). Subsequent over stresses (additional
///    bending) further limit range but do not move the new center. This is based on the bend angle so a bend that
///    reduces bend angle like a hard push after bending wings up releases the travel limit but is accounted for in
///    fatigue and has a quadruple reduction in fatigue life."
///
/// Four rules, and the third is the one that makes it a flying problem rather than a number:
///
/// 1. THE BAND NARROWS LINEARLY with the bend, from the full travel at straight to nothing at the bend that fails
///    the wing (<see cref="PlasticHinge.Law"/>'s rupture rotation — the same angle the structure itself calls
///    failure, so this cannot disagree with it).
/// 2. IT IS CENTRED WHERE THE AILERON WAS when the wing first bent. Pull hard while holding roll and the surviving
///    travel is around THAT deflection: a wing bent with left aileron in leaves an aileron that can still give
///    left and cannot give right.
/// 3. THE CENTRE NEVER MOVES AGAIN. Bend it further and the band narrows about the same place.
/// 4. IT FOLLOWS THE BEND ANGLE, not the worst it has ever been: straighten the wing and the travel comes back.
///    That is free here and paid for in <see cref="PlasticHinge"/>, where undoing a set costs four times the life.
///
/// Pure arithmetic, no state: <see cref="WingPanel"/> holds the centre and the hinge holds the bend.
/// </summary>
public static class BentAileronTravel
{
    /// <summary>
    /// Half the travel the panel still allows, rad, given its bend and the bend that would fail it.
    /// Full <paramref name="maxDeflRad"/> when straight, 0 at failure, linear between.
    /// </summary>
    public static double HalfRangeRad(double bendRad, double ruptureRad, double maxDeflRad)
    {
        if (maxDeflRad <= 0.0) return 0.0;
        if (!(ruptureRad > 0.0)) return maxDeflRad;                  // no failure angle known: nothing to scale against
        double f = 1.0 - System.Math.Abs(bendRad) / ruptureRad;
        return maxDeflRad * System.Math.Clamp(f, 0.0, 1.0);
    }

    /// <summary>
    /// Where the aileron may actually go: <paramref name="commandedRad"/> held to the band of half-width
    /// <paramref name="halfRangeRad"/> about <paramref name="centreRad"/>, and the band itself held inside the
    /// surface's own travel — a centre near one stop cannot be given range it never had on that side.
    /// </summary>
    public static double Clamp(double commandedRad, double centreRad, double halfRangeRad, double maxDeflRad)
    {
        double lo = System.Math.Max(-maxDeflRad, centreRad - halfRangeRad);
        double hi = System.Math.Min(maxDeflRad, centreRad + halfRangeRad);
        if (lo > hi) return System.Math.Clamp(centreRad, -maxDeflRad, maxDeflRad);   // centre outside the stops
        return System.Math.Clamp(commandedRad, lo, hi);
    }
}
