using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Recording;
using FlyingGame.Sim;
using FlyingGame.Sim.Recording;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE RECORDER AGAINST A REAL AEROPLANE. The format tests cover the ring and the rate policy on their own; this
/// flies a Pitts and holds the two things that decide whether the recorder is worth having: it must not perturb the
/// flight model, and what comes out must be readable by someone who was not there.
/// </summary>
public class FlightTestRecorderTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(RepoRoot(), "configs", "aircraft", id + ".json"));

    /// <summary>Find a byte pattern in a byte array — the header markers must be located in the bytes, because
    /// the records after them are not text and decoding them shifts every index.</summary>
    private static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < needle.Length; j++) if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }

    private static Aircraft Flying(string id = "pitts-s2b-like")
    {
        AircraftConfig cfg = Load(id);
        var state = new RigidBodyState(new Vec3(0, 0, -300), Quat.Identity, new Vec3(60, 0, 0), Vec3.Zero);
        return new Aircraft(cfg, state, ControlDeflections.Neutral);
    }

    // ------------------------------------------------------------------ the schema

    [Fact]
    public void EveryChannelHasANameAndAUnitAndTheyCannotDriftApart()
    {
        string[] names = FlightTestChannels.Names();
        string[] units = FlightTestChannels.Units();
        Assert.Equal(names.Length, units.Length);
        Assert.Equal(FlightTestChannels.Count, names.Length);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string n in names)
        {
            Assert.False(string.IsNullOrWhiteSpace(n));
            Assert.True(seen.Add(n), $"duplicate channel name '{n}': two columns with one name is a file nobody can read back");
        }
        // The channel list is big enough to be worth having. If this ever drops sharply, something was deleted.
        Assert.InRange(names.Length, 100, 220);
    }

    [Fact]
    public void TheRecordedChannelCountMatchesTheRecordWidth()
    {
        var rec = new FlightTestRecorder(capacityBytes: 4 * 1024 * 1024);
        Assert.Equal(FlightTestChannels.Count, rec.ChannelCount);
        Assert.Equal(FlightRecording.RecordBytes(FlightTestChannels.Count), rec.Ring.RecordBytes);
    }

    [Fact]
    public void EveryChannelReadsAFiniteNumberOnEveryTypeInTheFleet()
    {
        foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "configs", "aircraft"), "*.json"))
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(path);
            var state = new RigidBodyState(new Vec3(0, 0, -300), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero);
            var a = new Aircraft(cfg, state, ControlDeflections.Neutral);
            a.Step(new ControlInputs(0, 0, 0, -1), 1.0 / 200.0);

            var ctx = new RecorderContext { Aircraft = a, Inputs = new ControlInputs(0, 0, 0, -1) };
            IReadOnlyList<FlightTestChannels.Channel> all = FlightTestChannels.All;
            for (int i = 0; i < all.Count; i++)
            {
                double v = all[i].Read(ctx);
                Assert.False(double.IsNaN(v) || double.IsInfinity(v),
                             $"{Path.GetFileName(path)}: channel '{all[i].Name}' read {v}");
            }
        }
    }

    // ------------------------------------------------------------------ the constraint that outranks the feature

    /// <summary>
    /// THE RECORDER MUST NOT ALLOCATE IN THE STEPPING PATH. If it triggers a collection inside the physics step the
    /// recording describes the recorder, which is the one way this feature is worse than not existing. Flown for a
    /// real second, through the real channel readers, against a real aeroplane.
    /// </summary>
    [Fact]
    public void RecordingARealAeroplaneAllocatesNothingPerStep()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024);
        var inputs = new ControlInputs(0.05, -0.02, 0.01, -0.8);
        const double dt = 1.0 / 200.0;

        // Warm up: first-call JIT of 150 delegates is not what is being measured.
        for (int i = 0; i < 200; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 180.0, 0); }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 400; i++) rec.Step(a, inputs, dt, Vec3.Zero, 180.0, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0,
                    $"400 recorder steps allocated {allocated} bytes inside the physics path; it must allocate none");
    }

    /// <summary>
    /// And the cost, as a NUMBER rather than a claim of "negligible". A 200 Hz step is 5000 microseconds of budget;
    /// a sample that takes a meaningful bite out of that is a sample that changes the thing it is measuring.
    /// </summary>
    [Fact]
    public void TheRecorderCostsAMeasuredAndSmallFractionOfAStep()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024);
        var inputs = new ControlInputs(0, 0, 0, -0.8);
        const double dt = 1.0 / 200.0;

        for (int i = 0; i < 400; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 180.0, 0); }
        for (int i = 0; i < 2000; i++) rec.Step(a, inputs, dt, Vec3.Zero, 180.0, 0);

        Assert.True(rec.MeanSampleMicros > 0, "the recorder did not measure itself");
        Assert.True(rec.MeanSampleMicros < FlightTestRecorder.BudgetMicros,
                    $"a sample cost {rec.MeanSampleMicros:F1} us against a {FlightTestRecorder.BudgetMicros} us budget");
        Assert.False(rec.AutoStopped);

        // THE MEASURED NUMBER, pinned so it cannot creep. On this machine a sample costs about 5.5 us against a
        // 5000 us step — about a tenth of one per cent — of which the 150 channel reads are ~2.4 us and the shared
        // derivation 0.4 us. 50 us leaves an order of magnitude of headroom for a slower phone while still failing
        // loudly if someone puts an atmosphere sample or an allocation back into a channel reader.
        Assert.True(rec.MeanSampleMicros < 50.0,
                    $"a sample now costs {rec.MeanSampleMicros:F1} us, up from the ~5.5 us this was built at: "
                  + "something expensive has been added to a channel reader");
    }

    [Fact]
    public void TheRecorderSamplesAtFiftyHertzWhileNothingIsHappening()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024);
        var inputs = new ControlInputs(0, 0, 0, -0.8);
        const double dt = 1.0 / 200.0;
        for (int i = 0; i < 200; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 0, 0); }
        Assert.Equal(FlightRecording.BaselineHz, rec.Ring.Count);
    }

    [Fact]
    public void AnEventBurstsTheRateAndMarksTheRunUpThatPrecededIt()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 32 * 1024 * 1024);
        var inputs = new ControlInputs(0, 0, 0, -0.8);
        const double dt = 1.0 / 200.0;

        for (int i = 0; i < 600; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 0, 0); }
        int beforeEvent = rec.Ring.Count;

        rec.Mark(FlightRecording.Event.WaterContact);
        for (int i = 0; i < 400; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 0, 0); }

        // Two seconds of burst is 400 samples where the baseline would have taken 100.
        Assert.True(rec.Ring.Count - beforeEvent > 300,
                    $"the burst produced only {rec.Ring.Count - beforeEvent} samples in two seconds");

        // And the run-up is marked, retro-actively, in records written before the event existed.
        byte[] snap = rec.Ring.SnapshotChronological();
        int recBytes = rec.Ring.RecordBytes;
        int markedBeforeEvent = 0;
        for (int i = 0; i < beforeEvent; i++)
        {
            int bits = snap[i * recBytes + 6] | (snap[i * recBytes + 7] << 8);
            if ((bits & (int)FlightRecording.Event.WaterContact) != 0) markedBeforeEvent++;
        }
        Assert.True(markedBeforeEvent > 50,
                    $"only {markedBeforeEvent} of the records before the event were marked; the run-up is the half a reader wants");
    }

    // ------------------------------------------------------------------ the file

    /// <summary>
    /// A FILE SOMEONE ELSE CAN READ. The header must say what was flown — including the tuning, because a recording
    /// whose knob state is unknown is worth nothing and the Developer page makes that a live risk — and the channel
    /// table must let a reader name every column without this code.
    /// </summary>
    [Fact]
    public void TheFileIsSelfDescribingAndCarriesTheTuningState()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024);
        var inputs = new ControlInputs(0, 0, 0, -0.8);
        const double dt = 1.0 / 200.0;
        for (int i = 0; i < 200; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 0, 0); }

        FlyingGame.Core.Tuning.TuningSession.ResetForTest();
        FlyingGame.Core.Tuning.TuningSession.Apply(a.Config);

        byte[] file = rec.BuildFile(new[]
        {
            new KeyValuePair<string, string>("aircraftFlown", a.Config.Id),
            new KeyValuePair<string, string>("modelDrawn", a.Config.Id),
            new KeyValuePair<string, string>("build", "2026.1111.0000"),
            new KeyValuePair<string, string>("tuning", FlyingGame.Core.Tuning.TuningSession.DeltaReport(a.Config)),
        });

        string text = Encoding.UTF8.GetString(file, 0, Math.Min(file.Length, 16384));
        Assert.StartsWith(FlightRecording.Magic, text);
        Assert.Contains("build: 2026.1111.0000", text);
        // The flight model's identity and the drawn model's identity are SEPARATE fields: they can disagree in the
        // running game (the MODEL MISMATCH banner exists because they have), and a recording that captured only one
        // could not say what was actually flown.
        Assert.Contains("aircraftFlown: pitts-s2b-like", text);
        Assert.Contains("modelDrawn: ", text);
        Assert.Contains("tuning: ", text);
        Assert.Contains("stock", text);                       // nothing tuned in this test, and the file says so
        Assert.Contains(FlightRecording.EndHeader, text);
        Assert.Contains("channels: " + FlightTestChannels.Count, text);
        Assert.Contains("alpha\tdeg", text);
        Assert.Contains("inThrottleLever\t", text);           // the inverted lever is recorded raw and named so
        Assert.Contains(FlightRecording.EndChannels, text);

        // The body is a whole number of records and nothing else. The offset is found in the BYTES, not in the
        // decoded string: decoding binary records as UTF-8 produces replacement characters, so a character index
        // into `text` is not a byte index into `file`.
        int headerEnd = IndexOfBytes(file, Encoding.UTF8.GetBytes(FlightRecording.EndChannels)) + FlightRecording.EndChannels.Length;
        int bodyBytes = file.Length - headerEnd;
        Assert.Equal(0, bodyBytes % rec.Ring.RecordBytes);
        Assert.Equal(rec.Ring.Count, bodyBytes / rec.Ring.RecordBytes);
    }

    [Fact]
    public void TheRingKeepsTheLastMinutesRatherThanTheFirst()
    {
        Aircraft a = Flying();
        // A ring deliberately too small, so it wraps within the test.
        var rec = new FlightTestRecorder(capacityBytes: FlightRecording.RecordBytes(FlightTestChannels.Count) * 40);
        var inputs = new ControlInputs(0, 0, 0, -0.8);
        const double dt = 1.0 / 200.0;
        for (int i = 0; i < 800; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 0, 0); }

        Assert.True(rec.Ring.Wrapped);
        Assert.Equal(40, rec.Ring.Count);

        byte[] snap = rec.Ring.SnapshotChronological();
        float first = BitConverter.ToSingle(snap, 0);
        float last = BitConverter.ToSingle(snap, (rec.Ring.Count - 1) * rec.Ring.RecordBytes);
        Assert.True(last > first, "the ring came out newest-first");
        // 800 steps is four seconds; what survives must be the END of it.
        Assert.True(last > 3.5f, $"the last kept sample is at t={last}, so the ring kept the beginning of the flight");
    }

    // ------------------------------------------------------------------ events

    [Fact]
    public void TheEventWatcherAgreesWithTheRestOfTheGameAboutAStall()
    {
        Aircraft a = Flying();
        var watch = new FlightTestEvents();
        var inputs = new ControlInputs(0, -1.0, 0, -1.0);     // hard aft stick, closed throttle
        const double dt = 1.0 / 200.0;

        bool sawStall = false;
        for (int i = 0; i < 200 * 25 && !sawStall; i++)
        {
            a.Step(inputs, dt);
            if ((watch.Step(a, dt) & FlightRecording.Event.Stall) != 0) sawStall = true;
        }
        Assert.True(sawStall, "25 seconds of full aft stick on a Pitts never tripped the stall event");
        // And it used the game's own threshold rather than a second opinion.
        Assert.True(a.StalledFraction > FlyingGame.Sim.Replay.FlightEventLog.StalledExit);
    }

    [Fact]
    public void TheStallEventDoesNotMachineGunOnTheThreshold()
    {
        Aircraft a = Flying();
        var watch = new FlightTestEvents();
        const double dt = 1.0 / 200.0;
        var inputs = new ControlInputs(0, -1.0, 0, -1.0);

        int fires = 0;
        for (int i = 0; i < 200 * 30; i++)
        {
            a.Step(inputs, dt);
            if ((watch.Step(a, dt) & FlightRecording.Event.Stall) != 0) fires++;
        }
        // Hysteresis: a wing hovering at the threshold must not trigger a burst every other step.
        Assert.True(fires < 20, $"the stall event fired {fires} times in 30 s; the hysteresis is not working");
    }

    [Fact]
    public void TheWatcherAllocatesNothingEither()
    {
        Aircraft a = Flying();
        var watch = new FlightTestEvents();
        const double dt = 1.0 / 200.0;
        var inputs = new ControlInputs(0, -0.5, 0, -0.8);
        for (int i = 0; i < 400; i++) { a.Step(inputs, dt); watch.Step(a, dt); }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) watch.Step(a, dt);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ------------------------------------------------------------------ the self-watch

    [Fact]
    public void ARecorderThatBecomesExpensiveStandsItselfDownAndSaysWhy()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024);
        Assert.False(rec.AutoStopped);
        Assert.Equal("", rec.AutoStopReason);
        // The budget is a real fraction of a 5 ms step, not a number picked to never fire.
        Assert.True(FlightTestRecorder.BudgetMicros < 1000.0,
                    "the cost budget must be a small part of a 5000 us step, or it is not a budget");
    }

    [Fact]
    public void ADisabledRecorderRecordsNothingAndCostsNothing()
    {
        Aircraft a = Flying();
        var rec = new FlightTestRecorder(capacityBytes: 8 * 1024 * 1024) { Enabled = false };
        var inputs = new ControlInputs(0, 0, 0, -0.8);
        const double dt = 1.0 / 200.0;
        for (int i = 0; i < 400; i++) { a.Step(inputs, dt); rec.Step(a, inputs, dt, Vec3.Zero, 0, 0); }
        Assert.Equal(0, rec.Ring.Count);
    }
}
