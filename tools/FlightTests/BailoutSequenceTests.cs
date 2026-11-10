using FlyingGame.Core;
using FlyingGame.Core.Vr;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE BAIL-OUT'S OWN TIMING, AND WHAT THE G DOES TO IT (Ty 2026-11-10: canopy off at 1 s, harness off at 1.5 s,
/// "the g's will slow down his exit more positive/negative slower exit", 2 s between 0 and +1 g with a second per g
/// outside that band, and — "if it has a side door negative g is also slowing but if it has a top canopy the pilot
/// drops or is 'ejected'").
/// </summary>
public class BailoutSequenceTests
{
    private readonly ITestOutputHelper _o;
    public BailoutSequenceTests(ITestOutputHelper o) { _o = o; }

    private static Bailout Run(double nz, EgressOpening opening, double seconds = 20.0, double dt = 0.005)
    {
        var b = new Bailout();
        for (int i = 0; i < (int)(seconds / dt) && !b.Out; i++) b.Step(dt, nz, opening);
        return b;
    }

    /// <summary>The two fixed moments: the canopy at 1 s, the harness at 1.5 s, and nothing before either.</summary>
    [Fact]
    public void TheCanopyGoesAtOneSecondAndTheHarnessAtOnePointFive()
    {
        var b = new Bailout();
        double dt = 0.005;
        double canopy = double.NaN, harness = double.NaN;
        for (int i = 0; i < 1000; i++)
        {
            bool c0 = b.CanopyGone, h0 = b.HarnessReleased;
            b.Step(dt, 1.0, EgressOpening.TopCanopy);
            if (!c0 && b.CanopyGone) canopy = b.TimeSec;
            if (!h0 && b.HarnessReleased) harness = b.TimeSec;
        }
        _o.WriteLine($"  canopy off at {canopy:F3} s, harness off at {harness:F3} s");
        Assert.Equal(BailoutSequence.CanopyOffSec, canopy, 2);
        Assert.Equal(BailoutSequence.HarnessOffSec, harness, 2);
        Assert.Equal(1.0, BailoutSequence.CanopyOffSec, 9);
        Assert.Equal(1.5, BailoutSequence.HarnessOffSec, 9);
    }

    /// <summary>
    /// THE CLIMB OUT TAKES 2 s BETWEEN 0 AND +1 g AND A SECOND PER g OUTSIDE IT, either way. Read literally from
    /// his sentence: the penalty is the distance outside the 0..+1 band.
    /// </summary>
    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(0.5, 2.0)]
    [InlineData(0.0, 2.0)]
    [InlineData(2.0, 3.0)]
    [InlineData(3.0, 4.0)]
    [InlineData(4.5, 5.5)]
    [InlineData(-1.0, 3.0)]
    [InlineData(-2.0, 4.0)]
    public void TheClimbOutTakesTwoSecondsPlusASecondPerGOutsideTheBand(double nz, double wantSec)
    {
        Assert.Equal(wantSec, BailoutSequence.ClimbOutSec(nz), 9);
        // And the stepper takes that long in the air, measured from the harness release.
        Bailout b = Run(nz, EgressOpening.SideDoor);
        _o.WriteLine($"  nz {nz,5:F1}: climb out {BailoutSequence.ClimbOutSec(nz):F2} s, clear of the cockpit at {b.OutAtSec:F2} s from the button");
        Assert.True(b.Out);
        Assert.Equal(BailoutSequence.HarnessOffSec + wantSec, b.OutAtSec, 1);
        Assert.False(b.WasFlung);
    }

    /// <summary>
    /// A SIDE DOOR MAKES NEGATIVE g SLOW HIM, A TOP OPENING MAKES IT THROW HIM. Same −2 g, same aeroplane state,
    /// and the only difference is where the way out is. Out of the top he is gone almost at once and leaves with the
    /// speed the g gave him; out of a side door he is slower than he would have been at +1 g.
    /// </summary>
    [Theory]
    [InlineData(EgressOpening.TopCanopy)]
    [InlineData(EgressOpening.OpenTop)]
    public void NegativeGThrowsHimOutOfATopOpeningAndSlowsHimOutOfASideDoor(EgressOpening top)
    {
        Bailout up = Run(-2.0, top);
        Bailout side = Run(-2.0, EgressOpening.SideDoor);
        _o.WriteLine($"  {top,-10} at -2 g: clear at {up.OutAtSec:F3} s ({up.OutAtSec - BailoutSequence.HarnessOffSec:F3} s after the harness), "
                     + $"leaving at {up.ExitSpeedMs:F2} m/s, flung {up.WasFlung}");
        _o.WriteLine($"  side door  at -2 g: clear at {side.OutAtSec:F3} s ({side.OutAtSec - BailoutSequence.HarnessOffSec:F3} s after the harness), "
                     + $"leaving at {side.ExitSpeedMs:F2} m/s, flung {side.WasFlung}");
        Assert.True(up.WasFlung);
        Assert.False(side.WasFlung);
        // Thrown out, he clears the sill under 2 g in √(2·0.6/19.6) = 0.25 s, not in the 4 s the timer would have given.
        Assert.True(up.OutAtSec - BailoutSequence.HarnessOffSec < 0.4,
            $"the g took {up.OutAtSec - BailoutSequence.HarnessOffSec:F2} s to throw him out");
        Assert.True(side.OutAtSec > up.OutAtSec + 3.0, $"side {side.OutAtSec:F2} s vs top {up.OutAtSec:F2} s");
        // And the speed he leaves at is the g's: v = √(2·a·d), not his muscles.
        double a = BailoutSequence.TowardTheRailsMs2(-2.0);
        Assert.InRange(up.ExitSpeedMs, 0.9 * System.Math.Sqrt(2 * a * BailoutSequence.SillClearanceM),
                                       1.1 * System.Math.Sqrt(2 * a * BailoutSequence.SillClearanceM));
        Assert.Equal(BailoutSequence.MuscleMs, side.ExitSpeedMs, 6);
    }

    /// <summary>The harder the push-over, the harder he is thrown and the sooner he is gone — the g decides both,
    /// and nothing is on a clock.</summary>
    [Fact]
    public void TheHarderTheNegativeGTheHarderHeIsThrown()
    {
        double lastT = double.MaxValue, lastV = 0;
        foreach (double nz in new[] { -0.25, -0.5, -1.0, -2.0, -4.0 })
        {
            Bailout b = Run(nz, EgressOpening.TopCanopy, 60.0);
            double after = b.OutAtSec - BailoutSequence.HarnessOffSec;
            _o.WriteLine($"  nz {nz,5:F2}: thrown clear {after:F3} s after the harness, leaving at {b.ExitSpeedMs:F2} m/s");
            Assert.True(after < lastT, $"nz {nz} took {after:F3} s, no quicker than the gentler push-over's {lastT:F3}");
            Assert.True(b.ExitSpeedMs > lastV, $"nz {nz} left at {b.ExitSpeedMs:F2}, no faster than {lastV:F2}");
            lastT = after; lastV = b.ExitSpeedMs;
        }
    }

    /// <summary>
    /// THE G IS READ EVERY STEP, NOT FROZEN. He starts climbing out of a Pitts at +1 g, and a second in the pilot
    /// (or the trim, or the stall) pushes it to −1 g: from there the g has him, and he goes from wherever he had got
    /// to rather than finishing the climb. The other way round too — pull back while he is being thrown and the
    /// throw stops.
    /// </summary>
    [Fact]
    public void AGChangeHalfWayOutChangesWhatHappensToHim()
    {
        var b = new Bailout();
        double dt = 0.005;
        double at = double.NaN;
        for (int i = 0; i < 4000 && !b.Out; i++)
        {
            double nz = b.TimeSec < 2.5 ? 1.0 : -1.0;          // one second of climbing, then pushed over
            b.Step(dt, nz, EgressOpening.TopCanopy);
            if (double.IsNaN(at) && b.TimeSec >= 2.5) at = b.Exit01;
        }
        _o.WriteLine($"  climbed to {at:P0} of the way out at +1 g, then thrown the rest: clear at {b.OutAtSec:F3} s, "
                     + $"leaving at {b.ExitSpeedMs:F2} m/s, flung {b.WasFlung}");
        Assert.True(b.WasFlung);
        Assert.InRange(at, 0.3, 0.7);                           // half way out when the g changed
        // He is clear well before the 3.5 s a steady +1 g would have taken.
        Assert.True(b.OutAtSec < 3.3, $"he was clear at {b.OutAtSec:F2} s");
        Assert.True(b.ExitSpeedMs > 0.5, $"he left at {b.ExitSpeedMs:F2} m/s");
    }

    /// <summary>
    /// WHICH OPENING EACH AEROPLANE HAS COMES FROM THE PART, not from a list of names. The one question is
    /// <see cref="CanopySpec.IsDoor"/>'s, which the canopy code already asks: thinner across the aeroplane than half
    /// its height is a door. The Pitts' and the Extra's own written-down canopies come out as bubbles; a door-shaped
    /// part comes out as a door; a model with no canopy part at all is an open cockpit.
    /// </summary>
    [Fact]
    public void TheOpeningComesFromTheCanopyPartsOwnBounds()
    {
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like" })
        {
            CanopySpec s = CanopySpec.For(id);
            EgressOpening o = BailoutSequence.OpeningFor(true, s.SpanM, s.HeightM);
            _o.WriteLine($"  {id,-16} canopy {s.SpanM:F3} m across x {s.HeightM:F3} m tall => {o}");
            Assert.Equal(EgressOpening.TopCanopy, o);
        }
        // A cabin door: 0.08 m of bulge across the aeroplane, 1.0 m tall. CanopySpec.ForPart turns exactly this into
        // a door, and so does this.
        _o.WriteLine($"  a 0.08 x 1.00 m door => {BailoutSequence.OpeningFor(true, 0.08, 1.00)}");
        Assert.Equal(EgressOpening.SideDoor, BailoutSequence.OpeningFor(true, 0.08, 1.00));
        Assert.True(CanopySpec.IsDoor(0.08, 1.00));
        // No part at all: an open cockpit, and the way out is up.
        _o.WriteLine($"  no canopy part at all => {BailoutSequence.OpeningFor(false, 0, 0)}");
        Assert.Equal(EgressOpening.OpenTop, BailoutSequence.OpeningFor(false, 0, 0));
        Assert.True(BailoutSequence.FlungOut(BailoutSequence.OpeningFor(false, 0, 0), -1.0));
    }

    /// <summary>Positive g never throws anybody anywhere, whatever the opening: it holds him in the seat, which is
    /// what the climb-out penalty is about.</summary>
    [Theory]
    [InlineData(EgressOpening.OpenTop)]
    [InlineData(EgressOpening.TopCanopy)]
    [InlineData(EgressOpening.SideDoor)]
    public void PositiveGNeverThrowsHimOut(EgressOpening opening)
    {
        foreach (double nz in new[] { 0.0, 0.5, 1.0, 3.0, 6.0 })
            Assert.False(BailoutSequence.FlungOut(opening, nz), $"{opening} at {nz} g");
        Bailout b = Run(4.0, opening);
        Assert.False(b.WasFlung);
        Assert.Equal(BailoutSequence.HarnessOffSec + 5.0, b.OutAtSec, 1);
    }
}
