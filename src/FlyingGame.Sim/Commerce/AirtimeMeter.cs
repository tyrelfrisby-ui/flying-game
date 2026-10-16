namespace FlyingGame.Sim.Commerce;

/// <summary>
/// How long this pilot has been connected to the online relay, and how much of that they still have.
///
/// The relay is billed by the time a room stays awake, so hours are the real cost and they are counted PER
/// PILOT: four friends sharing one room each burn their own hour of our relay time. That is why there is a
/// cap at all.
///
/// The owner's rules (2026-10-15):
///  - a hard cap of <see cref="IncludedHoursPerPeriod"/> hours per pilot per month, enforced;
///  - once the month's hours are used, a top-up can be bought, and bought hours DO NOT expire at month end —
///    they sit behind the included hours and are only spent once the included ones are gone;
///  - the top-up is only offered after the cap is reached (<see cref="CapReached"/>), never before.
///
/// <see cref="PeriodSeconds"/> and <see cref="LifetimeSeconds"/> count every connected second regardless of
/// which bucket paid for it, because the cost question and the allowance question are not the same question.
///
/// Single player never touches this. Neither does flying together over the same Wi-Fi: the relay is not
/// involved, so there is nothing to pay for and nothing to count.
/// </summary>
public sealed class AirtimeMeter
{
    /// <summary>The monthly allowance, in hours. Config, not a constant anybody should read off the source.</summary>
    public double IncludedHoursPerPeriod
    {
        get => _includedPerPeriod / 3600.0;
        set
        {
            double was = _includedPerPeriod;
            _includedPerPeriod = Math.Max(0, value) * 3600.0;
            // Raising the cap mid-period hands over the difference rather than making the pilot wait a month.
            IncludedRemainingSeconds = Math.Max(0, IncludedRemainingSeconds + (_includedPerPeriod - was));
            if (PeriodDays <= 0) IncludedRemainingSeconds = _includedPerPeriod;
        }
    }

    private double _includedPerPeriod = 70 * 3600.0;

    /// <summary>What is left of this month's included hours.</summary>
    public double IncludedRemainingSeconds { get; private set; } = 70 * 3600.0;

    /// <summary>Topped-up hours. Spent only after the included ones, and they never expire.</summary>
    public double PurchasedRemainingSeconds { get; private set; }

    /// <summary>Connected seconds in the current period, whoever paid for them — the cost signal.</summary>
    public double PeriodSeconds { get; private set; }

    /// <summary>Sessions begun in the current period. Hours alone hide the shape: ten one-minute joins cost
    /// the relay far more per minute than one ten-minute flight, because waking a room is the expensive part.</summary>
    public int PeriodSessions { get; private set; }

    public DateTime PeriodEndUtc { get; private set; } = DateTime.MinValue;
    public int PeriodDays { get; private set; }

    public double LifetimeSeconds { get; private set; }
    public int LifetimeSessions { get; private set; }
    public double LifetimePurchasedSeconds { get; private set; }

    /// <summary>This session only; reset by <see cref="BeginSession"/>.</summary>
    public double SessionSeconds { get; private set; }

    /// <summary>Refuse to start a flight with less than this left: being dropped a few seconds after joining
    /// is worse than being told plainly that there is not enough time for a flight.</summary>
    public double MinimumToJoinSeconds { get; set; } = 120;

    /// <summary>Warn at this much left — one flight's worth.</summary>
    public double LowWaterSeconds { get; set; } = 600;

    private DateTime _highWaterUtc = DateTime.MinValue;
    private bool _inSession;

    public bool InSession => _inSession;
    public double RemainingSeconds => IncludedRemainingSeconds + PurchasedRemainingSeconds;
    public double RemainingHours => RemainingSeconds / 3600.0;
    public bool LowRemaining => RemainingSeconds <= LowWaterSeconds;
    public bool Exhausted => RemainingSeconds <= 0;

    /// <summary>This month's included hours are gone. The ONLY condition under which the top-up is offered —
    /// the owner was explicit that it is not sold before then.</summary>
    public bool CapReached => IncludedRemainingSeconds <= 0;

    public double SessionHours => SessionSeconds / 3600.0;
    public double PeriodHours => PeriodSeconds / 3600.0;
    public double LifetimeHours => LifetimeSeconds / 3600.0;

    /// <summary>Begin counting a month, with the included hours full.</summary>
    public void StartPeriod(DateTime startUtc, int periodDays = 30)
    {
        PeriodDays = Math.Max(1, periodDays);
        PeriodEndUtc = startUtc.AddDays(PeriodDays);
        PeriodSeconds = 0;
        PeriodSessions = 0;
        IncludedRemainingSeconds = _includedPerPeriod;
    }

    /// <summary>Roll the period forward if the time has come. Safe to call every frame, and safe against a
    /// clock moved backwards — a period never un-rolls, so winding the clock cannot refill the allowance. A
    /// long-dormant install lands on the period containing <paramref name="nowUtc"/> and gets ONE month's
    /// hours, not one per month since.</summary>
    public void Tick(DateTime nowUtc)
    {
        if (nowUtc > _highWaterUtc) _highWaterUtc = nowUtc;
        DateTime now = _highWaterUtc;
        if (PeriodDays <= 0 || now < PeriodEndUtc) return;
        double periodsOverdue = Math.Floor((now - PeriodEndUtc).TotalDays / PeriodDays) + 1;
        PeriodEndUtc = PeriodEndUtc.AddDays(periodsOverdue * PeriodDays);
        PeriodSeconds = 0;
        PeriodSessions = 0;
        IncludedRemainingSeconds = _includedPerPeriod;      // the allowance resets; purchased hours do not
    }

    /// <summary>A top-up purchase, or a comp. Always additive, and never expires.</summary>
    public void CreditHours(double hours)
    {
        if (hours <= 0) return;
        PurchasedRemainingSeconds += hours * 3600.0;
        LifetimePurchasedSeconds += hours * 3600.0;
    }

    /// <summary>Is there enough time left to be worth joining? <paramref name="reason"/> is shown to the
    /// pilot verbatim when there is not.</summary>
    public bool CanJoin(out string reason)
    {
        if (RemainingSeconds >= MinimumToJoinSeconds) { reason = ""; return true; }
        reason = CapReached && PurchasedRemainingSeconds <= 0
            ? $"You have used this month's {IncludedHoursPerPeriod:0.#} hours of online flying."
            : "Not enough online time left for a flight.";
        return false;
    }

    /// <summary>Joined a room. Starts the first period if nothing has started one yet, so no caller has to
    /// remember to.</summary>
    public void BeginSession(DateTime nowUtc)
    {
        Tick(nowUtc);
        if (PeriodDays <= 0) StartPeriod(_highWaterUtc);
        _inSession = true;
        SessionSeconds = 0;
        PeriodSessions++;
        LifetimeSessions++;
    }

    public void EndSession() => _inSession = false;

    /// <summary>
    /// A step of connected time, reported by the transport. Spends this month's included hours first, then
    /// topped-up ones. Returns false once nothing is left, which is the transport's cue to leave the session.
    /// Ignored when no session is running, so a stray call after the transport has left cannot invent airtime.
    /// </summary>
    public bool Record(double dt)
    {
        if (!_inSession || dt <= 0) return !Exhausted;
        SessionSeconds += dt;
        PeriodSeconds += dt;
        LifetimeSeconds += dt;

        double owed = dt;
        double fromIncluded = Math.Min(IncludedRemainingSeconds, owed);
        IncludedRemainingSeconds -= fromIncluded;
        owed -= fromIncluded;
        PurchasedRemainingSeconds -= owed;
        // May go under: a second already flown is not refusable. But nobody is billed for the overshoot.
        if (PurchasedRemainingSeconds < 0) PurchasedRemainingSeconds = 0;
        return !Exhausted;
    }

    /// <summary>Everything worth persisting, as a flat record.</summary>
    public readonly struct Snapshot
    {
        public readonly double IncludedRemaining, PurchasedRemaining, PeriodSeconds, LifetimeSeconds, LifetimePurchased, IncludedPerPeriod;
        public readonly int PeriodSessions, LifetimeSessions, PeriodDays;
        public readonly DateTime PeriodEndUtc, HighWaterUtc;
        public Snapshot(double includedRemaining, double purchasedRemaining, double periodSeconds, int periodSessions,
                        double lifetimeSeconds, int lifetimeSessions, double lifetimePurchased,
                        double includedPerPeriod, DateTime periodEndUtc, int periodDays, DateTime highWaterUtc)
        {
            IncludedRemaining = includedRemaining; PurchasedRemaining = purchasedRemaining;
            PeriodSeconds = periodSeconds; PeriodSessions = periodSessions;
            LifetimeSeconds = lifetimeSeconds; LifetimeSessions = lifetimeSessions;
            LifetimePurchased = lifetimePurchased; IncludedPerPeriod = includedPerPeriod;
            PeriodEndUtc = periodEndUtc; PeriodDays = periodDays; HighWaterUtc = highWaterUtc;
        }
    }

    public Snapshot Save() => new(IncludedRemainingSeconds, PurchasedRemainingSeconds, PeriodSeconds, PeriodSessions,
                                  LifetimeSeconds, LifetimeSessions, LifetimePurchasedSeconds,
                                  _includedPerPeriod, PeriodEndUtc, PeriodDays, _highWaterUtc);

    /// <summary>Restore. Everything is clamped: a hand-edited save cannot hand itself more hours than the
    /// cap allows, and purchased hours are clamped to what the store says was actually bought.</summary>
    public void Load(Snapshot s)
    {
        _includedPerPeriod = Math.Max(0, s.IncludedPerPeriod);
        IncludedRemainingSeconds = Math.Clamp(s.IncludedRemaining, 0, _includedPerPeriod);
        LifetimePurchasedSeconds = Math.Max(0, s.LifetimePurchased);
        PurchasedRemainingSeconds = Math.Clamp(s.PurchasedRemaining, 0, LifetimePurchasedSeconds);
        PeriodSeconds = Math.Max(0, s.PeriodSeconds);
        PeriodSessions = Math.Max(0, s.PeriodSessions);
        LifetimeSeconds = Math.Max(0, s.LifetimeSeconds);
        LifetimeSessions = Math.Max(0, s.LifetimeSessions);
        PeriodEndUtc = s.PeriodEndUtc;
        PeriodDays = Math.Max(0, s.PeriodDays);
        _highWaterUtc = s.HighWaterUtc;
        _inSession = false;          // nothing survives a relaunch mid-flight
    }
}
