using System;

namespace FlyingGame.Core.Recording;

/// <summary>
/// WHEN TO TAKE A SAMPLE: 50 Hz normally, 200 Hz for two seconds either side of an event.
///
/// Pure arithmetic over the sim's own step count, so it is testable without a sim and deterministic — the decision
/// cannot drift with frame rate or wall-clock, and two runs of the same flight sample at the same steps.
///
/// THE BACKWARD HALF OF THE BURST IS THE INTERESTING PART. The recorder has no way to know an event is coming, so
/// "two seconds before" cannot be sampled on demand. It is instead RETRO-FLAGGED: the ring is already holding those
/// records, and <see cref="RetroRecords"/> says how many of them to reach back and mark. They were taken at the
/// baseline rate, so they stay 50 Hz samples and say so in their own rate byte — the burst gives full resolution
/// AFTER the trigger and honest, labelled, coarser data before it. Claiming 200 Hz for samples that were taken at
/// 50 would be worse than not having them.
/// </summary>
public sealed class RecorderClock
{
    private readonly int _simHz;
    private readonly int _baselineEvery;     // steps between baseline samples
    private readonly int _burstSteps;        // steps a trigger keeps the rate up for
    private long _step;
    private long _burstUntilStep = long.MinValue;

    public RecorderClock(int simHz = FlightRecording.BurstHz,
                         int baselineHz = FlightRecording.BaselineHz,
                         double burstSeconds = FlightRecording.BurstSeconds)
    {
        if (simHz <= 0 || baselineHz <= 0) throw new ArgumentOutOfRangeException(nameof(simHz));
        _simHz = simHz;
        // A baseline that does not divide the sim rate would beat against it and jitter the spacing; round to the
        // nearest whole number of steps and let the file's own timestamps carry the truth.
        _baselineEvery = Math.Max(1, (int)Math.Round(simHz / (double)baselineHz));
        _burstSteps = Math.Max(1, (int)Math.Round(burstSeconds * simHz));
    }

    public long Step => _step;
    public bool Bursting => _step <= _burstUntilStep;

    /// <summary>How many already-written baseline records an event should reach back and mark.</summary>
    public int RetroRecords => Math.Max(1, _burstSteps / _baselineEvery);

    /// <summary>Call once per physics step. Returns true when this step should be recorded.</summary>
    public bool Advance(bool eventThisStep)
    {
        _step++;
        // -1 because the triggering step is itself the first of the burst: without it "two seconds" is 401 steps
        // at 200 Hz, and every burst would run one sample long.
        if (eventThisStep) _burstUntilStep = _step + _burstSteps - 1;
        return Bursting || _step % _baselineEvery == 0;
    }

    public byte RateCode => Bursting ? FlightRecording.RateBurst : FlightRecording.RateBaseline;
    public float TimeSec => (float)(_step / (double)_simHz);

    public void Reset() { _step = 0; _burstUntilStep = long.MinValue; }

    /// <summary>
    /// What a given length of flight costs on disk, so the budget is computed rather than guessed. Burst seconds
    /// are counted at the full rate and the rest at the baseline.
    /// </summary>
    public static long EstimatedBytes(int channelCount, double minutes, int events = 0)
    {
        int rec = FlightRecording.RecordBytes(channelCount);
        double seconds = minutes * 60.0;
        double burstSec = Math.Min(seconds, events * FlightRecording.BurstSeconds);
        double baseSec = Math.Max(0.0, seconds - burstSec);
        return (long)(rec * (baseSec * FlightRecording.BaselineHz + burstSec * FlightRecording.BurstHz));
    }
}
