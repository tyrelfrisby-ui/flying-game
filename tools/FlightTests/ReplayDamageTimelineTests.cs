using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHEN each part broke (Ty 2026-11-07: replaying a crash, "slight wing damage did show up on replay"). The pieces
/// that came off are posed by <see cref="ReplayPartTimeline"/>; this pins the other half, the damage left in the
/// aeroplane's own meshes, which the replay drew for every frame because it rebuilt nothing.
/// </summary>
public class ReplayDamageTimelineTests
{
    [Fact]
    public void TheAeroplaneIsWholeBeforeAnythingBreaksAndStaysBrokenAfter()
    {
        var t = new ReplayDamageTimeline();
        t.Add(2.0, AirframeComponent.WingLeft);
        t.Add(5.0, AirframeComponent.TailVertical);

        Assert.Equal(0, t.CountAt(0.0));            // taxiing out: whole
        Assert.Equal(0, t.CountAt(1.999));
        Assert.Equal(1, t.CountAt(2.0));            // the wing goes, exactly on its moment
        Assert.Equal(1, t.CountAt(4.999));
        Assert.Equal(2, t.CountAt(5.0));
        Assert.Equal(2, t.CountAt(600.0));          // nothing grows back

        Assert.False(t.IsLostAt(1.9, AirframeComponent.WingLeft));
        Assert.True(t.IsLostAt(2.0, AirframeComponent.WingLeft));
        Assert.False(t.IsLostAt(4.9, AirframeComponent.TailVertical));
        Assert.False(t.IsLostAt(600.0, AirframeComponent.WingRight));   // this one never broke
    }

    [Fact]
    public void TheDamageAtAnyMomentIsThePrefixThatHadHappenedByThen()
    {
        var t = new ReplayDamageTimeline();
        t.Add(1.0, AirframeComponent.WingLeft);
        t.Add(3.0, AirframeComponent.Nose);
        var got = new List<AirframeComponent>();

        t.LostAt(0.5, got);
        Assert.Empty(got);

        t.LostAt(1.0, got);
        Assert.Equal(new[] { AirframeComponent.WingLeft }, got);

        t.LostAt(4.0, got);
        Assert.Equal(new[] { AirframeComponent.WingLeft, AirframeComponent.Nose }, got);   // in the order they broke

        // Scrubbing back is the same answer as never having gone forward: the list is rebuilt, not undone.
        t.LostAt(0.5, got);
        Assert.Empty(got);
    }

    [Fact]
    public void APartCannotBreakTwiceAndReReportingItNeverMovesWhenItHappened()
    {
        var t = new ReplayDamageTimeline();
        t.Add(2.0, AirframeComponent.WingLeft);
        t.Add(9.0, AirframeComponent.WingLeft);     // the structure keeps saying so, every frame
        Assert.Equal(1, t.Count);
        Assert.Equal(2.0, t.TimeAt(0), 6);
        Assert.Equal(1, t.CountAt(2.0));            // still gone from its own moment, not from 9
    }

    [Fact]
    public void ABreakThatScrolledOutOfTheWindowStillCountsForEveryFrameLeftInIt()
    {
        var t = new ReplayDamageTimeline();
        t.Add(2.0, AirframeComponent.WingLeft);
        t.Add(40.0, AirframeComponent.TailVertical);
        // The rolling recorder now only holds from 30 s on. The wing left at 2 s: it is gone for the whole window,
        // so forgetting it would draw a wing that departed half a minute ago.
        t.TrimBefore(30.0);

        Assert.Equal(2, t.Count);
        Assert.Equal(30.0, t.TimeAt(0), 6);         // moved up to the window's start, not dropped
        Assert.Equal(1, t.CountAt(30.0));
        Assert.True(t.IsLostAt(30.0, AirframeComponent.WingLeft));
        Assert.Equal(40.0, t.TimeAt(1), 6);         // a break inside the window is untouched
        Assert.Equal(2, t.CountAt(40.0));
    }

    [Fact]
    public void AnOutOfOrderReportIsClampedSoTheListIsAlwaysAscending()
    {
        var t = new ReplayDamageTimeline();
        t.Add(5.0, AirframeComponent.WingLeft);
        t.Add(3.0, AirframeComponent.TailVertical);          // a clock that went backwards
        Assert.Equal(5.0, t.TimeAt(1), 6);
        Assert.Equal(2, t.CountAt(5.0));        // the clamped report lands on the same moment, so both are gone there
        Assert.Equal(0, t.CountAt(4.0));        // and nothing at all is gone before the FIRST break
    }

    [Fact]
    public void AFreshAeroplaneHasNothingBroken()
    {
        var t = new ReplayDamageTimeline();
        t.Add(2.0, AirframeComponent.WingLeft);
        t.Clear();
        Assert.Equal(0, t.Count);
        Assert.Equal(0, t.CountAt(600.0));
    }
}
