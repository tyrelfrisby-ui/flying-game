namespace FlyingGame.Sim.Commerce;

/// <summary>
/// How long this pilot has actually been connected to the online relay.
///
/// It gates NOTHING. Going online is decided by the Multiplayer Pass subscription alone (owner
/// 2026-10-15, replacing the metered-data design this file used to implement); nobody is warned, throttled
/// or disconnected for flying too much, and there is no fair-use line in the code because none has been
/// decided. If one is ever wanted, it belongs here and nowhere else.
///
/// It exists because the relay is billed by the time a room stays awake, not by the byte, so the only
/// number that tells us whether the Pass covers its costs is connected hours — PER PILOT, because four
/// friends sharing one room each burn their own hour of our relay time. <see cref="PeriodSeconds"/> is
/// that number for the current month.
///
/// Single player never touches this. Neither does anything on a local network: the relay is not involved,
/// so there is nothing to pay for and nothing to count.
/// </summary>
public sealed class AirtimeMeter
{
    /// <summary>Connected seconds in the current accounting period — what the cost check reads.</summary>
    public double PeriodSeconds { get; private set; }

    /// <summary>Sessions begun in the current period. Hours alone hide the shape: ten one-minute joins cost
    /// the relay far more per minute than one ten-minute flight, because waking a room is the expensive part.</summary>
    public int PeriodSessions { get; private set; }

    public DateTime PeriodEndUtc { get; private set; } = DateTime.MinValue;
    public int PeriodDays { get; private set; }

    /// <summary>Lifetime totals, for the "time online" line in settings.</summary>
    public double LifetimeSeconds { get; private set; }
    public int LifetimeSessions { get; private set; }

    /// <summary>This session only; reset by <see cref="BeginSession"/>.</summary>
    public double SessionSeconds { get; private set; }

    private DateTime _highWaterUtc = DateTime.MinValue;
    private bool _inSession;

    public bool InSession => _inSession;
    public double SessionHours => SessionSeconds / 3600.0;
    public double PeriodHours => PeriodSeconds / 3600.0;
    public double LifetimeHours => LifetimeSeconds / 3600.0;

    /// <summary>Begin counting a month. Called once, the first time the pilot goes online.</summary>
    public void StartPeriod(DateTime startUtc, int periodDays = 30)
    {
        PeriodDays = Math.Max(1, periodDays);
        PeriodEndUtc = startUtc.AddDays(PeriodDays);
        PeriodSeconds = 0;
        PeriodSessions = 0;
    }

    /// <summary>Roll the period forward if the time has come. Safe to call every frame, and safe against a
    /// clock moved backwards — a period never un-rolls. A long-dormant install lands on the period that
    /// contains <paramref name="nowUtc"/> rather than looping once per month since.</summary>
    public void Tick(DateTime nowUtc)
    {
        if (nowUtc > _highWaterUtc) _highWaterUtc = nowUtc;
        DateTime now = _highWaterUtc;
        if (PeriodDays <= 0 || now < PeriodEndUtc) return;
        double periodsOverdue = Math.Floor((now - PeriodEndUtc).TotalDays / PeriodDays) + 1;
        PeriodEndUtc = PeriodEndUtc.AddDays(periodsOverdue * PeriodDays);
        PeriodSeconds = 0;
        PeriodSessions = 0;
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

    /// <summary>A step of connected time. Ignored when no session is running, so a stray call after the
    /// transport has left cannot invent airtime.</summary>
    public void Record(double dt)
    {
        if (!_inSession || dt <= 0) return;
        SessionSeconds += dt;
        PeriodSeconds += dt;
        LifetimeSeconds += dt;
    }

    /// <summary>Everything worth persisting, as a flat record.</summary>
    public readonly struct Snapshot
    {
        public readonly double PeriodSeconds, LifetimeSeconds;
        public readonly int PeriodSessions, LifetimeSessions, PeriodDays;
        public readonly DateTime PeriodEndUtc, HighWaterUtc;
        public Snapshot(double periodSeconds, int periodSessions, double lifetimeSeconds, int lifetimeSessions,
                        DateTime periodEndUtc, int periodDays, DateTime highWaterUtc)
        {
            PeriodSeconds = periodSeconds; PeriodSessions = periodSessions;
            LifetimeSeconds = lifetimeSeconds; LifetimeSessions = lifetimeSessions;
            PeriodEndUtc = periodEndUtc; PeriodDays = periodDays; HighWaterUtc = highWaterUtc;
        }
    }

    public Snapshot Save() => new(PeriodSeconds, PeriodSessions, LifetimeSeconds, LifetimeSessions,
                                  PeriodEndUtc, PeriodDays, _highWaterUtc);

    public void Load(Snapshot s)
    {
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
