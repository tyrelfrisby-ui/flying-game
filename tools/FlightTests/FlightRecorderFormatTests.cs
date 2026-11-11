using System;
using System.Text;
using FlyingGame.Core.Recording;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE RECORDER'S FORMAT, RING AND RATE POLICY. The sim-side filling is tested separately; this holds the parts
/// that would corrupt a file or quietly lose the sample that mattered.
/// </summary>
public class FlightRecorderFormatTests
{
    private const int Channels = 150;
    private static int Rec => FlightRecording.RecordBytes(Channels);

    // ------------------------------------------------------------------ record packing

    [Fact]
    public void ARecordPacksItsPreambleAndEveryChannelAndNothingElse()
    {
        var w = new RecordWriter(Rec);
        w.Begin(12.5f, FlightRecording.RateBurst, (int)(FlightRecording.Event.Stall | FlightRecording.Event.SpinEntry));
        for (int i = 0; i < Channels; i++) w.F32(i * 0.25f);
        Assert.True(w.Complete, $"wrote {w.Written} of {Rec} bytes: a schema and a filler that disagree shear every column in the file");

        byte[] b = w.Buffer;
        Assert.Equal(12.5f, BitConverter.ToSingle(b, 0));
        Assert.Equal(FlightRecording.RateBurst, b[4]);
        int events = b[6] | (b[7] << 8);
        Assert.Equal((int)(FlightRecording.Event.Stall | FlightRecording.Event.SpinEntry), events);
        Assert.Equal(7 * 0.25f, BitConverter.ToSingle(b, FlightRecording.PreambleBytes + 4 * 7));
        Assert.Equal((Channels - 1) * 0.25f, BitConverter.ToSingle(b, FlightRecording.PreambleBytes + 4 * (Channels - 1)));
    }

    [Fact]
    public void TheRateIsCarriedOnEveryRecordRatherThanInferredFromTimestamps()
    {
        var w = new RecordWriter(Rec);
        w.Begin(1f, FlightRecording.RateBaseline, 0);
        Assert.Equal(FlightRecording.RateBaseline, w.Buffer[4]);
        w.Begin(1f, FlightRecording.RateBurst, 0);
        Assert.Equal(FlightRecording.RateBurst, w.Buffer[4]);
    }

    // ------------------------------------------------------------------ the ring

    [Fact]
    public void TheRingKeepsTheMostRecentRecordsAndDropsTheOldest()
    {
        var ring = new RecordRing(Rec, Rec * 10);
        var w = new RecordWriter(Rec);
        for (int i = 0; i < 25; i++)
        {
            w.Begin(i, FlightRecording.RateBaseline, 0);
            for (int c = 0; c < Channels; c++) w.F32(i);
            ring.Append(w.Buffer, w.Length);
        }
        Assert.Equal(10, ring.Count);
        Assert.True(ring.Wrapped);
        Assert.Equal(25, ring.TotalWritten);

        byte[] snap = ring.SnapshotChronological();
        Assert.Equal(10 * Rec, snap.Length);
        // Oldest first, and the oldest surviving record is #15 — the last ten of twenty-five.
        for (int i = 0; i < 10; i++)
            Assert.Equal(15 + i, BitConverter.ToSingle(snap, i * Rec));
    }

    [Fact]
    public void AnUnwrappedRingComesOutOldestFirstToo()
    {
        var ring = new RecordRing(Rec, Rec * 10);
        var w = new RecordWriter(Rec);
        for (int i = 0; i < 4; i++)
        {
            w.Begin(i, FlightRecording.RateBaseline, 0);
            for (int c = 0; c < Channels; c++) w.F32(0f);
            ring.Append(w.Buffer, w.Length);
        }
        byte[] snap = ring.SnapshotChronological();
        Assert.Equal(4 * Rec, snap.Length);
        for (int i = 0; i < 4; i++) Assert.Equal(i, BitConverter.ToSingle(snap, i * Rec));
        Assert.False(ring.Wrapped);
    }

    /// <summary>
    /// The burst reaches BACKWARDS. The recorder cannot know an event is coming, so the seconds before it are
    /// retro-marked in records already written. If this were wrong the file would carry an event with no run-up,
    /// which is the half of the event a flight-test reader actually wants.
    /// </summary>
    [Fact]
    public void AnEventReachesBackAndMarksRecordsAlreadyWritten()
    {
        var ring = new RecordRing(Rec, Rec * 100);
        var w = new RecordWriter(Rec);
        for (int i = 0; i < 50; i++)
        {
            w.Begin(i, FlightRecording.RateBaseline, 0);
            for (int c = 0; c < Channels; c++) w.F32(0f);
            ring.Append(w.Buffer, w.Length);
        }
        ring.MarkRecent(10, (int)FlightRecording.Event.WaterContact, 6);

        byte[] snap = ring.SnapshotChronological();
        for (int i = 0; i < 50; i++)
        {
            int bits = snap[i * Rec + 6] | (snap[i * Rec + 7] << 8);
            bool shouldBeMarked = i >= 40;
            Assert.Equal(shouldBeMarked ? (int)FlightRecording.Event.WaterContact : 0, bits);
        }
    }

    [Fact]
    public void MarkingReachesBackNoFurtherThanTheRingHolds()
    {
        var ring = new RecordRing(Rec, Rec * 100);
        var w = new RecordWriter(Rec);
        for (int i = 0; i < 3; i++)
        {
            w.Begin(i, FlightRecording.RateBaseline, 0);
            for (int c = 0; c < Channels; c++) w.F32(0f);
            ring.Append(w.Buffer, w.Length);
        }
        ring.MarkRecent(500, (int)FlightRecording.Event.Damage, 6);   // an event early in the flight
        byte[] snap = ring.SnapshotChronological();
        for (int i = 0; i < 3; i++)
            Assert.Equal((int)FlightRecording.Event.Damage, snap[i * Rec + 6] | (snap[i * Rec + 7] << 8));
    }

    [Fact]
    public void MarkingOrsIntoWhateverEventsARecordAlreadyCarried()
    {
        var ring = new RecordRing(Rec, Rec * 10);
        var w = new RecordWriter(Rec);
        w.Begin(0, FlightRecording.RateBaseline, (int)FlightRecording.Event.Stall);
        for (int c = 0; c < Channels; c++) w.F32(0f);
        ring.Append(w.Buffer, w.Length);

        ring.MarkRecent(1, (int)FlightRecording.Event.SpinEntry, 6);
        byte[] snap = ring.SnapshotChronological();
        int bits = snap[6] | (snap[7] << 8);
        Assert.Equal((int)(FlightRecording.Event.Stall | FlightRecording.Event.SpinEntry), bits);
    }

    // ------------------------------------------------------------------ the rate policy

    [Fact]
    public void TheBaselineSamplesAtFiftyHertzOutOfATwoHundredHertzSim()
    {
        var clock = new RecorderClock();
        int taken = 0;
        for (int i = 0; i < 200; i++) if (clock.Advance(false)) taken++;
        Assert.Equal(FlightRecording.BaselineHz, taken);
    }

    [Fact]
    public void AnEventBurstsToTheFullSimRateForTwoSecondsAndThenFallsBack()
    {
        var clock = new RecorderClock();
        for (int i = 0; i < 400; i++) clock.Advance(false);     // two seconds of ordinary flight

        int burst = 0;
        clock.Advance(true);                                     // the event
        burst++;
        for (int i = 0; i < 399; i++) if (clock.Advance(false)) burst++;
        // Two seconds at 200 Hz = 400 steps, every one of them recorded.
        Assert.Equal(400, burst);
        Assert.True(clock.Bursting == false || clock.Step <= 800);

        int after = 0;
        for (int i = 0; i < 200; i++) if (clock.Advance(false)) after++;
        Assert.Equal(FlightRecording.BaselineHz, after);
    }

    [Fact]
    public void TheRetroReachIsTwoSecondsOfBaselineRecords()
    {
        var clock = new RecorderClock();
        // Two seconds before the event, at 50 Hz, is 100 records already in the ring.
        Assert.Equal(100, clock.RetroRecords);
    }

    // ------------------------------------------------------------------ the budget

    /// <summary>
    /// The size budget, computed rather than guessed, because "it will be fine" is how a recorder fills a phone.
    /// Ten minutes at the baseline with a handful of events must sit near 18 MB, and the 200 MB cap must hold
    /// something on the order of two hours.
    /// </summary>
    [Fact]
    public void TheSizeBudgetIsWhatWasPromised()
    {
        long tenMin = RecorderClock.EstimatedBytes(Channels, 10.0, events: 5);
        Assert.InRange(tenMin, 17_000_000, 21_000_000);

        // Five events of burst in ten minutes cost very little over the flat baseline — the claim that the burst
        // is nearly free, checked rather than asserted in prose.
        long flat = RecorderClock.EstimatedBytes(Channels, 10.0, events: 0);
        Assert.True(tenMin - flat < flat * 0.12,
                    $"the burst added {tenMin - flat} bytes to {flat}, which is not 'nearly free'");

        int cap = 200 * 1024 * 1024;
        var ring = new RecordRing(Rec, cap);
        double minutes = ring.CapacityRecords / (double)FlightRecording.BaselineHz / 60.0;
        Assert.InRange(minutes, 90.0, 130.0);
    }

    // ------------------------------------------------------------------ the file is self-describing

    [Fact]
    public void TheHeaderKeepsAMultiLineKnobReportParseable()
    {
        string header = FlightRecording.BuildHeader(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("aircraft", "Pitts S-2B"),
            new System.Collections.Generic.KeyValuePair<string, string>("tuning", "Pitts (pitts-s2b-like)\nrig.finOffsetRad: 3.44°  (shipped 1.94°)"),
        });
        Assert.StartsWith(FlightRecording.Magic, header);
        Assert.Contains("aircraft: Pitts S-2B\n", header);
        // The knob lines are indented, so nothing downstream mistakes one for a header field.
        Assert.Contains("\n    rig.finOffsetRad: 3.44", header);
        Assert.EndsWith(FlightRecording.EndHeader, header);
    }

    [Fact]
    public void EveryChannelMustDeclareAUnit()
    {
        Assert.Throws<ArgumentException>(() =>
            FlightRecording.BuildChannelTable(new[] { "alpha", "beta" }, new[] { "deg" }));

        string table = FlightRecording.BuildChannelTable(new[] { "alpha", "beta" }, new[] { "deg", "deg" });
        Assert.Contains("channels: 2\n", table);
        Assert.Contains("alpha\tdeg\n", table);
        Assert.EndsWith(FlightRecording.EndChannels, table);
    }

    [Fact]
    public void EventBitsRenderAsNamesForAHumanAndTheConverter()
    {
        Assert.Equal("", FlightRecording.EventsToText(0));
        Assert.Equal("stall", FlightRecording.EventsToText((int)FlightRecording.Event.Stall));
        Assert.Equal("stall|spinEntry",
            FlightRecording.EventsToText((int)(FlightRecording.Event.Stall | FlightRecording.Event.SpinEntry)));
        // Every bit in the enum has a name, or a file would carry an event nobody can read back.
        foreach (FlightRecording.Event e in Enum.GetValues(typeof(FlightRecording.Event)))
        {
            if (e == FlightRecording.Event.None) continue;
            Assert.NotEqual("", FlightRecording.EventsToText((int)e));
        }
    }

    // ------------------------------------------------------------------ the constraint that outranks the feature

    /// <summary>
    /// ZERO ALLOCATION IN THE STEPPING PATH. If recording triggers GC in the physics step the data describes the
    /// recorder rather than the aeroplane, which is the one way this feature can be worse than not existing. A
    /// thousand samples must allocate nothing measurable.
    /// </summary>
    [Fact]
    public void RecordingAThousandSamplesAllocatesNothing()
    {
        var ring = new RecordRing(Rec, Rec * 2000);
        var w = new RecordWriter(Rec);
        var clock = new RecorderClock();

        // Warm everything up first, so the measurement is of steady-state recording and not of first-call JIT.
        for (int i = 0; i < 100; i++)
        {
            w.Begin(i, FlightRecording.RateBaseline, 0);
            for (int c = 0; c < Channels; c++) w.F32(c * 0.5f);
            ring.Append(w.Buffer, w.Length);
            clock.Advance(false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            clock.Advance(i == 500);
            w.Begin(i * 0.02f, clock.RateCode, i == 500 ? (int)FlightRecording.Event.Stall : 0);
            for (int c = 0; c < Channels; c++) w.F32(c * 0.5f);
            ring.Append(w.Buffer, w.Length);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0,
                    $"recording 1000 samples allocated {allocated} bytes in the stepping path; it must allocate none");
    }
}
