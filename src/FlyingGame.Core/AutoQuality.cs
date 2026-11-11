using System;

namespace FlyingGame.Core;

/// <summary>
/// The automatic half of the graphics slider: it may lower the tier BELOW what the pilot chose, never above it, and
/// it comes back up when the machine proves it can carry the choice again.
///
/// <para><b>What triggers it.</b> The primary signal is <c>FlightSimDriver.CatchUpCappedFrames</c>, not the frame
/// rate. A capped catch-up means the fixed-step loop discarded simulated time (<see cref="SimCatchUp"/>): the
/// aeroplane flew a trajectory physics did not produce. That is "compromising the flight model" exactly, it is
/// already counted, and it is a different thing from an uncomfortable picture. Frame time is kept as a secondary,
/// comfort-only trigger, and the two are reported differently because the pilot should know which one happened.</para>
///
/// <para><b>Windows, not frames.</b> Everything is judged over a window of a few seconds. A single long frame — a
/// garbage collection, a forest chunk streaming in — costs one or two capped frames and must not move the tier. A
/// device that cannot carry the settings makes tens of capped frames a second: the one device measurement of this in
/// trouble (docs/quest-performance.md, 2026-11-02) read <c>sim capped 138</c> in five seconds, 28 a second.</para>
///
/// Pure: no Unity, no clock of its own. The bridge calls <see cref="Sample"/> once a frame with the frame's duration
/// and what the counters did; everything else is arithmetic.
/// </summary>
public sealed class AutoQuality
{
    /// <summary>The thresholds, with the reasoning for each number on the field.</summary>
    public sealed class Policy
    {
        /// <summary>The frame time above which a frame counts as slow, seconds.
        /// <see cref="GraphicsQuality.TargetFrameSec"/>.</summary>
        public double TargetFrameSec = 1.0 / 30.0;

        /// <summary>How long a judgement window is. Three seconds: long enough that one hitch cannot move the tier,
        /// short enough that a pilot in a flare is not left for ten seconds flying discarded physics.</summary>
        public double WindowSec = 3.0;

        /// <summary>Capped frames in a window that force a downgrade. Six is two a second sustained for the whole
        /// window; a garbage collection costs one or two in total, so this cannot be reached by transients.</summary>
        public int CappedFramesToDowngrade = 6;

        /// <summary>Capped frames that end the window early and downgrade at once, without waiting for
        /// <see cref="WindowSec"/>. Twenty: the measured device-in-trouble rate was 28 a second, so a real collapse
        /// is caught inside a second, while nothing short of a collapse gets there before the window closes anyway.
        /// </summary>
        public int CappedFramesUrgent = 20;

        /// <summary>A window's MEAN frame time above this multiple of <see cref="TargetFrameSec"/> is "slow". 1.25
        /// rather than 1.0 because a frame budget met exactly is normal: at 72 Hz this is 17.4 ms, at 30 fps 41.7 ms.
        /// </summary>
        public double SlowFrameFactor = 1.25;

        /// <summary>After a downgrade, no further downgrade for this long. The new tier needs a frame or two to take
        /// effect and the counters still hold the old tier's capped frames; without this a bad second walks the tier
        /// all the way to Minimum in three windows.</summary>
        public double SettleSec = 2.0;

        /// <summary>Clean seconds needed before stepping back up one tier the FIRST time. Twenty seconds: recovering
        /// is what caused the trouble, being one tier low is only cosmetic, and a world whose reflections come and go
        /// is worse than one without them.</summary>
        public double BaseRecoverySec = 20.0;

        /// <summary>A tier that has already failed waits <c>BaseRecoverySec * 2^failures</c> before being tried
        /// again, capped here. Five minutes: a tier that failed three times is not worth re-trying this flight, and
        /// a new flight builds a new machine.</summary>
        public double MaxRecoverySec = 300.0;

        /// <summary>No two warnings closer together than this, whatever happens. The warning is dismissible AND
        /// rate-limited: requirement, Ty 2026-11-11 — it "must not nag every frame".</summary>
        public double NoticeMinIntervalSec = 20.0;
    }

    /// <summary>A warning raised by a downgrade: what was given up, why, and the numbers behind it.</summary>
    public readonly struct Notice
    {
        public readonly GraphicsQuality.Tier From, To;
        public readonly GraphicsQuality.Reason Why;
        /// <summary>Simulated seconds the catch-up cap discarded in the window that triggered this.</summary>
        public readonly double DiscardedSec;
        /// <summary>Mean frame time over that window, seconds.</summary>
        public readonly double MeanFrameSec;
        /// <summary>The sentence to show the pilot.</summary>
        public readonly string Message;

        internal Notice(GraphicsQuality.Tier from, GraphicsQuality.Tier to, GraphicsQuality.Reason why,
                        double discardedSec, double meanFrameSec)
        {
            From = from; To = to; Why = why; DiscardedSec = discardedSec; MeanFrameSec = meanFrameSec;
            Message = GraphicsQuality.Warning(from, to, why, discardedSec, meanFrameSec);
        }
    }

    private readonly Policy _p;

    private double _winSec, _winFrameSec, _winDiscardedSec;
    private int _winFrames, _winCapped;
    private double _settleLeftSec, _cleanSec, _sinceNoticeSec;
    // Downgrades taken FROM each tier this flight, indexed by tier: the tier above a failure waits longer each time.
    private readonly int[] _failures = new int[4];

    public AutoQuality(GraphicsQuality.Tier chosen, Policy? policy = null)
    {
        _p = policy ?? new Policy();
        Chosen = GraphicsQuality.Clamp(chosen);
        Effective = Chosen;
        _sinceNoticeSec = _p.NoticeMinIntervalSec;   // the first warning of a flight is never suppressed
    }

    /// <summary>What the pilot picked. The hard ceiling: <see cref="Effective"/> is never above it.</summary>
    public GraphicsQuality.Tier Chosen { get; private set; }

    /// <summary>What is actually being drawn.</summary>
    public GraphicsQuality.Tier Effective { get; private set; }

    /// <summary>The knobs for <see cref="Effective"/>.</summary>
    public GraphicsQuality.Knobs Knobs => GraphicsQuality.Knobs.For(Effective);

    /// <summary>True while the machine is holding the picture below what the pilot asked for. The HUD shows the tier
    /// quietly for as long as this is true, which is how a pilot learns it recovered without being told twice.</summary>
    public bool Downgraded => Effective < Chosen;

    /// <summary>The warning waiting to be shown, or null. One at a time; <see cref="Dismiss"/> clears it.</summary>
    public Notice? Pending { get; private set; }

    public void Dismiss() => Pending = null;

    /// <summary>
    /// The pilot moved the slider. Whatever they pick they get: the automatic floor is lifted, the failure memory is
    /// cleared and the machine proves itself again from scratch. A pilot who picks Low gets Low; a pilot who picks
    /// High after an automatic drop gets High, and if the machine still cannot carry it they will be told again.
    /// </summary>
    public void SetChosen(GraphicsQuality.Tier tier)
    {
        Chosen = GraphicsQuality.Clamp(tier);
        Effective = Chosen;
        Pending = null;
        Array.Clear(_failures, 0, _failures.Length);
        ResetWindow();
        _settleLeftSec = 0.0; _cleanSec = 0.0;
    }

    /// <summary>
    /// One frame. <paramref name="frameSec"/> is the UNSCALED frame time, <paramref name="cappedFrames"/> is how much
    /// <c>FlightSimDriver.CatchUpCappedFrames</c> went up by (0 or 1 in practice, more if several drivers stepped),
    /// and <paramref name="discardedSec"/> is the simulated time the cap threw away, for the warning's wording.
    /// Returns true when <see cref="Effective"/> changed.
    /// </summary>
    public bool Sample(double frameSec, int cappedFrames, double discardedSec = 0.0)
    {
        if (!(frameSec > 0.0) || double.IsNaN(frameSec)) return false;
        if (cappedFrames < 0) cappedFrames = 0;
        if (!(discardedSec > 0.0) || double.IsNaN(discardedSec)) discardedSec = 0.0;

        _sinceNoticeSec += frameSec;
        if (_settleLeftSec > 0.0) _settleLeftSec -= frameSec;

        _winSec += frameSec; _winFrameSec += frameSec; _winFrames++;
        _winCapped += cappedFrames; _winDiscardedSec += discardedSec;

        bool urgent = _winCapped >= _p.CappedFramesUrgent;
        if (!urgent && _winSec < _p.WindowSec) return false;

        double mean = _winFrames > 0 ? _winFrameSec / _winFrames : frameSec;
        bool simCapped = _winCapped >= _p.CappedFramesToDowngrade || urgent;
        bool slow = mean > _p.TargetFrameSec * _p.SlowFrameFactor;
        double discarded = _winDiscardedSec;
        double windowSec = _winSec;
        ResetWindow();

        if (simCapped || slow)
        {
            _cleanSec = 0.0;
            // Settling, or already on the floor: the window still counts as dirty (so recovery does not creep up on
            // a machine that is still failing), but no second step is taken.
            if (_settleLeftSec > 0.0 || Effective <= GraphicsQuality.Floor) return false;

            GraphicsQuality.Tier from = Effective;
            Effective = GraphicsQuality.Down(from);
            _failures[(int)from]++;
            _settleLeftSec = _p.SettleSec;
            var why = simCapped ? GraphicsQuality.Reason.SimCapped : GraphicsQuality.Reason.FrameRate;
            if (_sinceNoticeSec >= _p.NoticeMinIntervalSec)
            {
                Pending = new Notice(from, Effective, why, discarded, mean);
                _sinceNoticeSec = 0.0;
            }
            return true;
        }

        // A clean window. Recovery only: the ceiling is the pilot's choice and nothing here can pass it.
        _cleanSec += windowSec;
        if (Effective >= Chosen) { _cleanSec = 0.0; return false; }
        GraphicsQuality.Tier next = GraphicsQuality.Up(Effective, Chosen);
        if (_cleanSec < RecoveryDelaySec(next)) return false;
        Effective = next;
        _cleanSec = 0.0;
        return true;
    }

    /// <summary>How long a clean run must be before <paramref name="tier"/> is tried again. The first retry waits
    /// <see cref="Policy.BaseRecoverySec"/>; every further failure of that same tier doubles the wait. That is the
    /// anti-oscillation guard that matters — a tier which has twice proved it cannot be carried is not re-tried every
    /// twenty seconds for the rest of the flight.</summary>
    public double RecoveryDelaySec(GraphicsQuality.Tier tier)
    {
        int f = _failures[(int)GraphicsQuality.Clamp(tier)];
        if (f > 0) f--;                       // coming back to a tier always means it failed at least once
        double d = _p.BaseRecoverySec * Math.Pow(2.0, Math.Min(f, 16));
        return d > _p.MaxRecoverySec ? _p.MaxRecoverySec : d;
    }

    private void ResetWindow()
    {
        _winSec = 0.0; _winFrameSec = 0.0; _winDiscardedSec = 0.0; _winFrames = 0; _winCapped = 0;
    }
}
