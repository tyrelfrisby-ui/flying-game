namespace FlyingGame.Sim.Commerce;

/// <summary>
/// Online multiplayer runs through a relay we pay for, so its data is metered and charged to EVERY pilot —
/// subscriber or not. This is the accounting, kept pure so the awkward cases can be tested without a socket:
/// running out mid-flight, a plan period rolling over, a clock moved backwards, a session that never ends
/// because the app was killed.
///
/// What it is NOT: a bandwidth limiter. It never drops or delays a packet. It counts what the transport
/// reports, refuses to START a session with nothing left, and ends one that runs dry — because being
/// disconnected at a known moment with a clear reason is kinder than a connection that quietly degrades.
///
/// Single player never touches this. Neither does anything on a local network.
/// </summary>
public sealed class DataMeter
{
    /// <summary>Bytes bought and not yet used (consumables, and anything comped).</summary>
    public long BalanceBytes { get; private set; }

    /// <summary>A plan's allowance for the current period, and what is left of it. Spent before the balance,
    /// so a plan holder's purchased bytes are not burned while their allowance sits unused.</summary>
    public long PlanAllowanceBytes { get; private set; }
    public long PlanRemainingBytes { get; private set; }
    public DateTime PlanPeriodEndUtc { get; private set; } = DateTime.MinValue;
    public int PlanPeriodDays { get; private set; }

    /// <summary>Lifetime total, for the "where did it go" line in settings.</summary>
    public long LifetimeBytes { get; private set; }

    /// <summary>This session only; reset by <see cref="BeginSession"/>.</summary>
    public long SessionBytes { get; private set; }

    /// <summary>Warn the pilot at this much left — about ten minutes at a typical rate.</summary>
    public long LowWaterBytes { get; set; } = 12L * 1024 * 1024;

    /// <summary>Refuse to start a session with less than this: enough that joining is worth the trouble.</summary>
    public long MinimumToJoinBytes { get; set; } = 2L * 1024 * 1024;

    private DateTime _highWaterUtc = DateTime.MinValue;
    private bool _inSession;
    private double _rateBytesPerSec;      // smoothed, for the estimate
    private const double RateTau = 8.0;

    public long AvailableBytes => PlanRemainingBytes + BalanceBytes;
    public double AvailableMb => AvailableBytes / (1024.0 * 1024.0);
    public bool LowBalance => AvailableBytes <= LowWaterBytes;
    public bool Exhausted => AvailableBytes <= 0;
    public bool InSession => _inSession;

    /// <summary>Smoothed consumption, bytes/second, while a session runs.</summary>
    public double RateBytesPerSec => _rateBytesPerSec;

    /// <summary>Seconds of multiplayer left at the current rate — <see cref="double.PositiveInfinity"/> when
    /// nothing is flowing yet. What the UI should show instead of a raw byte count.</summary>
    public double SecondsRemaining => _rateBytesPerSec > 1.0 ? AvailableBytes / _rateBytesPerSec : double.PositiveInfinity;

    /// <summary>A consumable purchase, or a comp. Always additive — it never replaces a balance.</summary>
    public void Credit(long bytes)
    {
        if (bytes <= 0) return;
        BalanceBytes += bytes;
    }

    /// <summary>Start or renew a plan. The allowance resets each period; it does NOT roll over, which is
    /// what "monthly allowance" means everywhere else and so the only honest reading of it.</summary>
    public void SetPlan(long allowanceBytes, DateTime periodStartUtc, int periodDays)
    {
        PlanAllowanceBytes = Math.Max(0, allowanceBytes);
        PlanPeriodDays = Math.Max(1, periodDays);
        PlanRemainingBytes = PlanAllowanceBytes;
        PlanPeriodEndUtc = periodStartUtc.AddDays(PlanPeriodDays);
    }

    public void ClearPlan()
    {
        PlanAllowanceBytes = PlanRemainingBytes = 0;
        PlanPeriodEndUtc = DateTime.MinValue;
        PlanPeriodDays = 0;
    }

    /// <summary>Roll the plan period forward if the time has come. Safe to call every frame, and safe
    /// against a clock moved backwards — a period never un-rolls.</summary>
    public void Tick(DateTime nowUtc)
    {
        if (nowUtc > _highWaterUtc) _highWaterUtc = nowUtc;
        DateTime now = _highWaterUtc;
        if (PlanAllowanceBytes <= 0 || PlanPeriodDays <= 0) return;
        // A long-dormant install can be several periods behind; land on the period that contains `now`
        // rather than looping once per month since.
        if (now < PlanPeriodEndUtc) return;
        double periodsOverdue = Math.Floor((now - PlanPeriodEndUtc).TotalDays / PlanPeriodDays) + 1;
        PlanPeriodEndUtc = PlanPeriodEndUtc.AddDays(periodsOverdue * PlanPeriodDays);
        PlanRemainingBytes = PlanAllowanceBytes;
    }

    /// <summary>Can a session start? <paramref name="reason"/> is shown to the pilot when it cannot.</summary>
    public bool CanJoin(out string reason)
    {
        if (AvailableBytes < MinimumToJoinBytes)
        {
            reason = AvailableBytes <= 0
                ? "You are out of multiplayer data."
                : "Not enough multiplayer data left to join.";
            return false;
        }
        reason = "";
        return true;
    }

    public void BeginSession()
    {
        _inSession = true;
        SessionBytes = 0;
        _rateBytesPerSec = 0.0;
    }

    public void EndSession()
    {
        _inSession = false;
        _rateBytesPerSec = 0.0;
    }

    /// <summary>
    /// Report traffic the transport actually moved. Spends the plan allowance first, then purchased bytes.
    /// Returns false once nothing is left, which is the transport's cue to leave the session.
    /// </summary>
    public bool Record(long bytes, double dt)
    {
        if (bytes < 0) bytes = 0;
        LifetimeBytes += bytes;
        SessionBytes += bytes;

        long owed = bytes;
        long fromPlan = Math.Min(PlanRemainingBytes, owed);
        PlanRemainingBytes -= fromPlan;
        owed -= fromPlan;
        BalanceBytes -= owed;                       // may go negative: a packet in flight is not refusable
        if (BalanceBytes < 0) BalanceBytes = 0;     // ... but the pilot is never billed for the overshoot

        if (dt > 1e-6)
        {
            double inst = bytes / dt;
            double k = 1.0 - Math.Exp(-dt / RateTau);
            _rateBytesPerSec += (inst - _rateBytesPerSec) * k;
        }
        return !Exhausted;
    }

    /// <summary>Everything worth persisting, as a flat record.</summary>
    public readonly struct Snapshot
    {
        public readonly long Balance, PlanAllowance, PlanRemaining, Lifetime;
        public readonly DateTime PlanPeriodEndUtc, HighWaterUtc;
        public readonly int PlanPeriodDays;
        public Snapshot(long balance, long planAllowance, long planRemaining, long lifetime, DateTime planPeriodEndUtc, DateTime highWaterUtc, int planPeriodDays)
        {
            Balance = balance; PlanAllowance = planAllowance; PlanRemaining = planRemaining; Lifetime = lifetime;
            PlanPeriodEndUtc = planPeriodEndUtc; HighWaterUtc = highWaterUtc; PlanPeriodDays = planPeriodDays;
        }
    }

    public Snapshot Save() => new(BalanceBytes, PlanAllowanceBytes, PlanRemainingBytes, LifetimeBytes, PlanPeriodEndUtc, _highWaterUtc, PlanPeriodDays);

    public void Load(Snapshot s)
    {
        BalanceBytes = Math.Max(0, s.Balance);
        PlanAllowanceBytes = Math.Max(0, s.PlanAllowance);
        PlanRemainingBytes = Math.Clamp(s.PlanRemaining, 0, PlanAllowanceBytes);
        LifetimeBytes = Math.Max(0, s.Lifetime);
        PlanPeriodEndUtc = s.PlanPeriodEndUtc;
        PlanPeriodDays = s.PlanPeriodDays;
        _highWaterUtc = s.HighWaterUtc;
    }
}
