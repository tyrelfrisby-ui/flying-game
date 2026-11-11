using System;
using System.Collections.Generic;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Recording;

namespace FlyingGame.Sim.Recording;

/// <summary>
/// THE FLIGHT TEST RECORDER. Ty, 2026-11-11: "Add a flight test data recorder too with all of the data tracked for
/// review and analysis."
///
/// NOT THE SAME THING AS THE TWO RECORDERS THAT ALREADY EXIST, and worth saying so because the names collide. The
/// in-game REPLAY (FlyingGame.Sim.Replay.FlightRecorder) and the named multitrack recordings (FlightTrack /
/// RecordingLibrary) are POSE recorders: about twenty channels at 30 Hz, quantised for playback, so an aeroplane can
/// be flown again on screen. This is instrumentation: ~150 channels at 50 Hz, bursting to the sim's own 200 Hz
/// around events, in full float precision, so a number can be read off it and believed. They answer different
/// questions and neither replaces the other.
///
/// ALWAYS ARMED. It starts with the flight and keeps the last N minutes in a ring. Ty's test pilots fly cards; a
/// recorder you have to remember to switch on is off for the flight that mattered, so the only thing a pilot does
/// is decide to KEEP what has already been recorded.
///
/// THE COST IS THE FEATURE'S LICENCE TO EXIST. If recording changes step timing or triggers a collection in the
/// physics step then the data describes the recorder, so: every buffer is preallocated, the sample path allocates
/// nothing at all, nothing touches the disk in flight, and the recorder measures its own cost
/// (<see cref="LastSampleMicros"/>) and stands itself down if it ever becomes expensive
/// (<see cref="AutoStopped"/>) rather than quietly spoiling the flight model.
/// </summary>
public sealed class FlightTestRecorder
{
    private readonly RecordRing _ring;
    private readonly RecordWriter _writer;
    private readonly RecorderClock _clock;
    private readonly RecorderContext _ctx = new RecorderContext();
    private readonly IReadOnlyList<FlightTestChannels.Channel> _channels;

    private Vec3 _prevRates;
    private bool _havePrev;
    private int _pendingEvents;
    private int _eventsSeen;

    // Self-watch. A moving mean of the sample cost and a strike counter: one slow sample is a scheduler hiccup,
    // a run of them is the recorder being a problem.
    private double _meanMicros;
    private int _strikes;
    private int _samplesTaken;

    /// <summary>
    /// Samples excluded from the cost statistic. The first sample through any managed code pays for jitting 150
    /// channel delegates and is hundreds of times the steady-state cost; left in, it poisons a slow-moving average
    /// for thousands of samples afterwards. That is not a hypothetical — it read 2084 us a sample against a true
    /// 5.5 us, and it would have stood the recorder down on every flight for a cost it was not paying.
    /// </summary>
    public const int WarmupSamples = 32;

    /// <summary>Microseconds the last sample took. Recorded as a channel too, so a file can be audited for whether
    /// the recorder was interfering with the aeroplane it was measuring.</summary>
    public double LastSampleMicros { get; private set; }
    public double MeanSampleMicros => _meanMicros;

    /// <summary>Set when the recorder has stood itself down for costing too much. It says so rather than carrying
    /// on: a recording that silently degraded the flight model is worse than no recording.</summary>
    public bool AutoStopped { get; private set; }
    public string AutoStopReason { get; private set; } = "";

    /// <summary>Above this mean cost per sample the recorder is taking a visible bite out of a 5 ms step.</summary>
    public const double BudgetMicros = 250.0;
    private const int StrikesToStop = 50;

    public bool Enabled = true;
    public RecordRing Ring => _ring;
    public RecorderClock Clock => _clock;
    public int EventsSeen => _eventsSeen;
    public int ChannelCount => _channels.Count;

    public FlightTestRecorder(int capacityBytes = 200 * 1024 * 1024, int simHz = FlightRecording.BurstHz)
    {
        _channels = FlightTestChannels.All;
        int rec = FlightRecording.RecordBytes(_channels.Count);
        _ring = new RecordRing(rec, capacityBytes);
        _writer = new RecordWriter(rec);
        _clock = new RecorderClock(simHz);
    }

    /// <summary>
    /// Raise one or more events. They land on the NEXT sample and trigger the burst, and they also reach BACKWARDS
    /// over the records already in the ring, so the run-up to the event is marked too — the recorder cannot know an
    /// event is coming, and the two seconds before a gear strike are the half a reader actually wants.
    /// </summary>
    public void Mark(FlightRecording.Event e)
    {
        if (e == FlightRecording.Event.None) return;
        _pendingEvents |= (int)e;
        _eventsSeen++;
        _ring.MarkRecent(_clock.RetroRecords, (int)e, 6);
    }

    /// <summary>
    /// ONE PHYSICS STEP. Call from <see cref="SimLoop.Stepped"/>. Decides whether this step is a sample and, if it
    /// is, fills one record. Allocates nothing.
    /// </summary>
    public void Step(Aircraft aircraft, in ControlInputs inputs, double dt, Vec3 windWorld, double oilTempF, double catchUpCappedFrames)
    {
        if (!Enabled || AutoStopped || aircraft == null) return;

        bool hasEvent = _pendingEvents != 0;
        bool sample = _clock.Advance(hasEvent);

        // Angular acceleration is differenced here because the integrator never stores it. Done every step, not
        // every sample, so it is a true one-step derivative rather than a 20 ms smear.
        Vec3 rates = aircraft.State.Rates;
        _ctx.RatesDot = _havePrev && dt > 0.0 ? (rates - _prevRates) * (1.0 / dt) : default;
        _prevRates = rates;
        _havePrev = true;

        if (!sample) return;

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        _ctx.Aircraft = aircraft;
        _ctx.Inputs = inputs;
        _ctx.WindWorld = windWorld;
        _ctx.OilTempF = oilTempF;
        _ctx.CatchUpCappedFrames = catchUpCappedFrames;
        _ctx.RecorderMicros = LastSampleMicros;
        _ctx.Derive();

        _writer.Begin(_clock.TimeSec, _clock.RateCode, _pendingEvents);
        for (int i = 0; i < _channels.Count; i++)
        {
            double v = _channels[i].Read(_ctx);
            // A NaN in one channel must not make the file unreadable, and a silent 0 would be a lie: a NaN is
            // written through as a NaN so a reader sees a hole where there was one.
            _writer.F32(v);
        }
        _ring.Append(_writer.Buffer, _writer.Length);
        _pendingEvents = 0;

        double micros = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
        LastSampleMicros = micros;
        if (++_samplesTaken <= WarmupSamples) return;        // jit, not cost
        _meanMicros = _meanMicros == 0.0 ? micros : _meanMicros * 0.9 + micros * 0.1;
        if (_meanMicros > BudgetMicros)
        {
            if (++_strikes >= StrikesToStop)
            {
                AutoStopped = true;
                AutoStopReason = "recording cost " + _meanMicros.ToString("F0") + " us a sample, over the "
                               + BudgetMicros.ToString("F0") + " us budget: stopped so the flight model is not the thing being measured";
            }
        }
        else if (_strikes > 0) _strikes--;
    }

    /// <summary>
    /// Everything the ring holds, as a finished file: header, channel table, records. Allocates, and is meant to —
    /// it runs when a pilot decides to keep a flight, and the bytes are handed to a worker thread to write.
    /// </summary>
    public byte[] BuildFile(IEnumerable<KeyValuePair<string, string>> headerFields)
    {
        string header = FlightRecording.BuildHeader(headerFields);
        string table = FlightRecording.BuildChannelTable(FlightTestChannels.Names(), FlightTestChannels.Units());
        byte[] h = System.Text.Encoding.UTF8.GetBytes(header);
        byte[] t = System.Text.Encoding.UTF8.GetBytes(table);
        byte[] body = _ring.SnapshotChronological();
        var all = new byte[h.Length + t.Length + body.Length];
        Buffer.BlockCopy(h, 0, all, 0, h.Length);
        Buffer.BlockCopy(t, 0, all, h.Length, t.Length);
        Buffer.BlockCopy(body, 0, all, h.Length + t.Length, body.Length);
        return all;
    }

    public void Reset()
    {
        _ring.Clear();
        _clock.Reset();
        _pendingEvents = 0;
        _eventsSeen = 0;
        _havePrev = false;
        _strikes = 0;
        _samplesTaken = 0;
        _meanMicros = 0;
        AutoStopped = false;
        AutoStopReason = "";
    }
}
