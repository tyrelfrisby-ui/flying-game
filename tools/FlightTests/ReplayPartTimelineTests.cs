using System;
using System.Collections.Generic;
using FlyingGame.Sim.Replay;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The replay's history of a part that came off (Ty pt1001, 2026-11-07, on 2026.1107.0822: "after a crash the replay begins
/// with the wings already missing, before the actual crash later in the recording"). The mechanism: a break cuts the part
/// out of the aeroplane's meshes onto a debris object posed at the aeroplane; the replay re-posed only the aeroplane, so
/// from the first replayed frame the aeroplane was drawn cut and the wings lay where they fell. FlightReplay now keeps a
/// <see cref="ReplayPartTimeline"/> per part: before its break the part rides on the replayed aeroplane, from the break on
/// its recorded pose. These pin the timing rules; SmokeAirframeRender's part section shows the Pitts drawn by them.
/// </summary>
public class ReplayPartTimelineTests
{
    /// <summary>A part that broke at 12 s, sampled at 30 Hz from then to 40 s (the recorder's own frames).</summary>
    private static ReplayPartTimeline Wing(double breakT = 12, double endT = 40)
    {
        var w = new ReplayPartTimeline(breakT);
        for (int i = 0; ; i++) { double t = breakT + i / 30.0; if (t > endT + 1e-9) break; w.Add(t); }
        return w;
    }

    [Fact]
    public void BeforeTheBreakThePartIsOnTheAeroplaneAndFromItOnItIsWhereItWasRecorded()
    {
        var w = Wing();
        foreach (double t in new[] { 0.0, 5.0, 11.0, 11.999 })
        {
            w.At(t, out bool attached, out _, out _);
            Assert.True(attached, $"t {t}: the wing should still be on the aeroplane");
        }
        w.At(12.0, out bool a12, out int i12, out double u12);
        Assert.False(a12); Assert.Equal(0, i12); Assert.Equal(0.0, u12);
        w.At(20.05, out bool a20, out int i20, out double u20);
        Assert.False(a20);
        Assert.Equal(20.05, w.TimeAt(i20) + u20 * (w.TimeAt(i20 + 1) - w.TimeAt(i20)), 9);
        w.At(1e6, out bool aEnd, out int iEnd, out _);
        Assert.False(aEnd); Assert.Equal(w.Count - 1, iEnd);   // past the end: the last recorded pose
    }

    /// <summary>Seeking back past the break puts the wing back on, forward past it takes it off again, any number of times,
    /// and the answer at a head never depends on where the head was before (restart from the top included).</summary>
    [Fact]
    public void SeekingBackAndForthAcrossTheBreakIsCoherent()
    {
        var w = Wing();
        var seen = new Dictionary<double, (bool, int, double)>();
        double[] heads = { 30, 2, 12.5, 11.9, 40, 0, 12, 25, 11.99, 30, 0 };
        foreach (double h in heads)
        {
            w.At(h, out bool a, out int i, out double u);
            Assert.Equal(h < 12, a);
            if (seen.TryGetValue(h, out var prev)) Assert.Equal(prev, (a, i, u));
            seen[h] = (a, i, u);
        }
    }

    /// <summary>A recording that starts damaged stays damaged: a part that broke before the replay's window (or before the
    /// recorder held anything) is off from the first frame. And trimming the 10-minute window never moves the break: a part
    /// that broke inside the window is still on before its break after old samples are dropped.</summary>
    [Fact]
    public void ARecordingThatStartsDamagedStaysDamagedAndTrimmingNeverMovesTheBreak()
    {
        var early = Wing(breakT: 3, endT: 700);   // broke at 3 s; the recorder's window now starts at 100 s
        int dropped = early.TrimBefore(100);
        Assert.True(dropped > 2000);
        Assert.Equal(3.0, early.BreakT);
        Assert.True(early.TimeAt(0) <= 100 && early.TimeAt(1) > 100, "the last sample before the window is kept");
        early.At(100, out bool atStart, out _, out _);
        Assert.False(atStart);   // the window's first frame: already off, as it was

        var mid = Wing(breakT: 150, endT: 700);
        mid.TrimBefore(100);   // nothing before 150 to drop
        mid.At(120, out bool before, out _, out _);
        Assert.True(before);
        mid.TrimBefore(400);
        mid.At(399, out bool stillOff, out _, out _);   // (the window has moved past the break: off, at its first kept pose)
        Assert.False(stillOff);
        Assert.Equal(150.0, mid.BreakT);
    }

    [Fact]
    public void ABreakWithNoSampleYetIsStillOnTheAeroplaneAndSamplesMustAscend()
    {
        var w = new ReplayPartTimeline(5);
        w.At(6, out bool a, out _, out _);
        Assert.True(a);   // registered at the break, not sampled yet: drawn where it was cut from
        w.Add(5); w.Add(5.1);
        Assert.Throws<ArgumentException>(() => w.Add(5.05));
        w.At(5.05, out bool b, out int i, out double u);
        Assert.False(b); Assert.Equal(0, i); Assert.Equal(0.5, u, 9);
    }
}
