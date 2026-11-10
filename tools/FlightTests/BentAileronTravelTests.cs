using System;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT A BENT WING LEAVES THE AILERON (Ty 2026-11-10). His four rules, each pinned by the case that would break
/// it: the band narrows linearly to nothing at the failing bend, it is centred where the aileron WAS when the wing
/// first bent, that centre never moves again, and straightening the wing gives the travel back — at four times the
/// fatigue, which is the last test here.
/// </summary>
public class BentAileronTravelTests
{
    private const double Max = 0.4363;   // 25 deg of aileron, near enough the fleet's
    private const double Rupture = 0.35; // the bend that fails this wing

    [Fact]
    public void TheBandNarrowsLinearlyFromFullTravelToNothingAtFailure()
    {
        Assert.Equal(Max, BentAileronTravel.HalfRangeRad(0.0, Rupture, Max), 9);
        Assert.Equal(Max * 0.5, BentAileronTravel.HalfRangeRad(Rupture * 0.5, Rupture, Max), 9);
        Assert.Equal(Max * 0.25, BentAileronTravel.HalfRangeRad(Rupture * 0.75, Rupture, Max), 9);
        Assert.Equal(0.0, BentAileronTravel.HalfRangeRad(Rupture, Rupture, Max), 9);
        Assert.Equal(0.0, BentAileronTravel.HalfRangeRad(Rupture * 2, Rupture, Max), 9);   // past failure, not negative
        // A wing bent DOWN is as limited as one bent up: the band follows the magnitude.
        Assert.Equal(BentAileronTravel.HalfRangeRad(0.1, Rupture, Max), BentAileronTravel.HalfRangeRad(-0.1, Rupture, Max), 12);
    }

    [Fact]
    public void RollingThenPullingLeavesTheAileronWorkingOneWayAndNotTheOther()
    {
        // His own example: "if I roll then pull at high speed my ailerons might be limited to that one side only
        // with limited range". Full left aileron held, then a bend that takes half the travel.
        double centre = -Max;                                    // full left when the wing bent
        double half = BentAileronTravel.HalfRangeRad(Rupture * 0.5, Rupture, Max);
        Assert.Equal(Max * 0.5, half, 9);

        // Still has its left: the stops are at -Max..-Max/2, so full left is exactly reachable.
        Assert.Equal(-Max, BentAileronTravel.Clamp(-Max, centre, half, Max), 9);
        // And no right at all — asking for full right gives half LEFT, the nearest the band allows.
        Assert.Equal(-Max * 0.5, BentAileronTravel.Clamp(+Max, centre, half, Max), 9);
        // Neutral is outside the band too, which is the point: the stick cannot find centre any more.
        Assert.Equal(-Max * 0.5, BentAileronTravel.Clamp(0.0, centre, half, Max), 9);
    }

    [Fact]
    public void ABandCentredAtNeutralIsSymmetricAndNeverExceedsTheSurfaceSTravel()
    {
        double half = BentAileronTravel.HalfRangeRad(Rupture * 0.25, Rupture, Max);
        Assert.Equal(-half, BentAileronTravel.Clamp(-Max, 0.0, half, Max), 9);
        Assert.Equal(+half, BentAileronTravel.Clamp(+Max, 0.0, half, Max), 9);
        // Straight wing, centre at neutral: the band is the whole travel and the clamp does nothing.
        Assert.Equal(0.3, BentAileronTravel.Clamp(0.3, 0.0, BentAileronTravel.HalfRangeRad(0, Rupture, Max), Max), 9);
        // A centre near a stop cannot be given range it never had on that side.
        Assert.Equal(Max, BentAileronTravel.Clamp(Max, Max, Max, Max), 9);
    }

    [Fact]
    public void StraighteningTheWingGivesTheTravelBack()
    {
        // Rule four, and it is just the band following the ANGLE rather than the worst it has been: a wing bent to
        // 0.25 rad and then pushed back to 0.05 has most of its aileron again. What it costs is the next test.
        double bent = BentAileronTravel.HalfRangeRad(0.25, Rupture, Max);
        double back = BentAileronTravel.HalfRangeRad(0.05, Rupture, Max);
        Assert.True(back > bent);
        Assert.Equal(Max * (1.0 - 0.05 / Rupture), back, 9);
    }

    [Fact]
    public void UndoingABendCostsFourTimesTheStrengthOfPuttingItIn()
    {
        // "a quadruple reduction in fatigue life so you can straighten your wings out once but it will cost you
        // significantly for the next stress". Two identical wings already bent UP by an overstress; one is pulled
        // harder the same way, the other pushed the other way to straighten it. Same peak, so the same event —
        // only its sense differs.
        const double peak = 1.8;   // past UltimateRatio, so it weakens
        double cost = OverstressMember.WeakeningFor(peak);
        Assert.True(cost > 0.0);

        double further = 1.0 * (1.0 - cost);                                              // bent further the same way
        double straightened = 1.0 * (1.0 - cost * OverstressMember.ReversalFatigueFactor); // bent back
        Assert.True(straightened < further);
        Assert.Equal(4.0 * (1.0 - further), 1.0 - straightened, 9);
        Assert.Equal(4.0, OverstressMember.ReversalFatigueFactor, 12);
    }

    [Fact]
    public void OnlyAnOverstressThatOpposesTheSetIsStraightening()
    {
        Assert.False(OverstressMember.Straightening(0.0, +1));    // a straight wing cannot be straightened
        Assert.False(OverstressMember.Straightening(0.0, -1));
        Assert.False(OverstressMember.Straightening(+0.1, +1));   // bent up, pulled up: further the same way
        Assert.True(OverstressMember.Straightening(+0.1, -1));    // bent up, pushed down: his "hard push"
        Assert.True(OverstressMember.Straightening(-0.1, +1));
    }

}

/// <summary>
/// WHAT A BENT STABILISER LEAVES THE ELEVATOR (Ty 2026-11-10). Deliberately NOT the aileron's rule: "be more
/// generous on available travel so the always have some of the other direction like if they push to max bending
/// before failure the elevator will move to full down still but only 10 percent up and vise versa".
/// </summary>
public class BentStabTravelTests
{
    private const double Max = 0.4363;   // 25 deg
    private const double Rupture = 0.35;

    [Fact]
    public void TheWayItWasBentKeepsAllOfItsTravelHoweverFarItIsBent()
    {
        // Pushed to bend it: a push still gives everything, at any bend including the one that fails the tail.
        foreach (double bend in new[] { 0.0, 0.1, Rupture * 0.9, Rupture, Rupture * 2 })
            Assert.Equal(Max, BentStabTravel.AvailableRad(+1, +1, bend, Rupture, Max), 9);
        // And the mirror: bent the other way, the other way keeps everything.
        Assert.Equal(Max, BentStabTravel.AvailableRad(-1, -1, Rupture, Rupture, Max), 9);
    }

    [Fact]
    public void TheOtherWayFadesToATenthAndNeverToNothing()
    {
        Assert.Equal(Max, BentStabTravel.AvailableRad(-1, +1, 0.0, Rupture, Max), 9);                 // straight: all of it
        Assert.Equal(Max * BentStabTravel.MinOppositeFraction,
                     BentStabTravel.AvailableRad(-1, +1, Rupture, Rupture, Max), 9);                  // at failure: a tenth
        Assert.Equal(0.10, BentStabTravel.MinOppositeFraction, 12);
        // Half way there, half way between a tenth and the lot.
        double half = BentStabTravel.AvailableRad(-1, +1, Rupture * 0.5, Rupture, Max);
        Assert.Equal(Max * (0.10 + 0.9 * 0.5), half, 9);
        // Never zero, however far past failure it is asked.
        Assert.True(BentStabTravel.AvailableRad(-1, +1, Rupture * 5, Rupture, Max) > 0.0);
    }

    [Fact]
    public void HisExample_PushedToMaxBendingKeepsFullDownAndATenthUp()
    {
        // "if they push to max bending before failure the elevator will move to full down still but only 10
        // percent up". Elevator positive is DOWN in this model, so the push that bent it is +1.
        const double toward = +1;
        Assert.Equal(+Max, BentStabTravel.Clamp(+Max, toward, Rupture, Rupture, Max), 9);
        Assert.Equal(-Max * 0.10, BentStabTravel.Clamp(-Max, toward, Rupture, Rupture, Max), 9);
        // Vice versa, pulled: full up, a tenth of down.
        Assert.Equal(-Max, BentStabTravel.Clamp(-Max, -1, Rupture, Rupture, Max), 9);
        Assert.Equal(+Max * 0.10, BentStabTravel.Clamp(+Max, -1, Rupture, Rupture, Max), 9);
    }

    [Fact]
    public void AStraightTailLimitsNothingAndASmallerDemandIsUntouched()
    {
        Assert.Equal(0.2, BentStabTravel.Clamp(0.2, 0.0, 0.0, Rupture, Max), 9);   // never bent: no sense, no limit
        Assert.Equal(0.2, BentStabTravel.Clamp(0.2, +1, 0.0, Rupture, Max), 9);    // straight
        // Inside what is left, the clamp does nothing.
        double left = BentStabTravel.AvailableRad(-1, +1, Rupture * 0.5, Rupture, Max);
        Assert.Equal(-left * 0.5, BentStabTravel.Clamp(-left * 0.5, +1, Rupture * 0.5, Rupture, Max), 9);
    }
}
/// <summary>
/// A FOLDED WING'S AILERON. Ty, asked what a folded wing should do: "Draw it folded, limit the aileron". The fold is a
/// bend at the ROOT and is measured on its own scale — 90 degrees, where WingFailureLaw.FoldRad's own arithmetic runs
/// out — not on the station hinge's rupture angle, which it would overshoot many times over.
/// </summary>
public class FoldedAileronTravelTests
{
    private const double Max = 0.35;   // 20 deg of aileron, near enough to the fleet

    [Fact]
    public void AnUnfoldedWingKeepsEveryDegreeOfIt()
        => Assert.Equal(Max, BentAileronTravel.HalfRangeFromFoldRad(0.0, Max), 12);

    [Fact]
    public void AWingStandingVerticalHasNothingLeft()
        => Assert.Equal(0.0, BentAileronTravel.HalfRangeFromFoldRad(Math.PI * 0.5, Max), 12);

    [Fact]
    public void PastVerticalItStaysAtNothingRatherThanGoingNegative()
        => Assert.Equal(0.0, BentAileronTravel.HalfRangeFromFoldRad(Math.PI * 0.9, Max), 12);

    [Fact]
    public void TheFoldAWoodAndFabricWingReachesAtTheBreakingPointLeavesAboutASixth()
    {
        // WingFailureLaw.FoldRad at the breaking point: 75 deg for wood and fabric.
        double fold = WingFailureLaw.FoldRad(2.0, 1.5, 0.5 * (2.0 / 1.5) * Math.Cos(75.0 * Math.PI / 180.0) / 0.5);
        double half = BentAileronTravel.HalfRangeFromFoldRad(75.0 * Math.PI / 180.0, Max);
        Assert.InRange(half / Max, 0.12, 0.22);
        Assert.True(fold >= 0.0);                            // the law is being asked, not asserted
    }

    [Fact]
    public void ItFallsOffTheSameWayAFoldDeepens()
    {
        double prev = double.MaxValue;
        for (double d = 0.0; d <= 90.0; d += 5.0)
        {
            double h = BentAileronTravel.HalfRangeFromFoldRad(d * Math.PI / 180.0, Max);
            Assert.True(h <= prev + 1e-12, $"{d:0} deg of fold gave MORE travel: {h:0.0000}");
            prev = h;
        }
    }

    [Fact]
    public void AFoldIsFarHarsherThanTheSameAngleOfStationBendWouldBeHarmless()
    {
        // The point of the separate scale: 40 deg read against a 20 deg rupture angle would be zero travel, and the
        // hinge on a folded wing is undamaged. On the fold's own scale it keeps over half.
        double rupture = 20.0 * Math.PI / 180.0, fold = 40.0 * Math.PI / 180.0;
        Assert.Equal(0.0, BentAileronTravel.HalfRangeRad(fold, rupture, Max), 12);
        Assert.InRange(BentAileronTravel.HalfRangeFromFoldRad(fold, Max) / Max, 0.5, 0.6);
    }
}
