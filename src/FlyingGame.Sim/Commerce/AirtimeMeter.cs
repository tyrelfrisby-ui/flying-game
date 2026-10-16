namespace FlyingGame.Sim.Commerce;

/// <summary>
/// How long this pilot has been connected to the online relay. A meter, and nothing else: it counts, it
/// never refuses.
///
/// The relay is billed by the time a room stays awake, so hours are the real cost and they are counted PER
/// PILOT: four friends sharing one room each burn their own hour of our relay time. Knowing that number is
/// what tells us whether the pass covers what it costs.
///
/// The owner dropped the hour cap and the top-up (2026-10-16): the pass is unlimited, so there is no
/// allowance to spend, nothing to buy back, and no reason this class can give for turning anybody away. It
/// also means nothing about a pilot's hours needs a home outside their own device, which is what keeps the
/// App Store privacy answer at "Data Not Collected".
///
/// <see cref="PeriodSessions"/> is counted alongside the hours because hours alone hide the shape: ten
/// one-minute joins cost the relay far more per minute than one ten-minute flight, since waking a room is
/// the expensive part.
///
/// Single player never touches this. Neither does flying together over the same Wi-Fi: the relay is not
/// involved, so there is nothing to pay for and nothing to count.
/// </summary>
public sealed class AirtimeMeter
{
    /// <summary>Connected seconds in the current period — the cost signal.</summary>
    public double PeriodSeconds { get; private set; }

    /// <summary>Sessions begun in the current period.</summary>
    public int PeriodSessions { get; private set; }

    public DateTime PeriodEndUtc { get; private set; } = DateTime.MinValue;
    public int PeriodDays { get; private set; }

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

    /// <summary>Begin counting a month.</summary>
    public void StartPeriod(DateTime startUtc, int periodDays = 30)
    {
        PeriodDays = Math.Max(1, periodDays);
        PeriodEndUtc = startUtc.AddDays(PeriodDays);
        PeriodSeconds = 0;
        PeriodSessions = 0;
    }

    /// <summary>Roll the period forward if the time has come. Safe to call every frame, and safe against a
    /// clock moved backwards — a period never un-rolls. A long-dormant install lands on the period containing
    /// <paramref name="nowUtc"/> rather than walking through every month since.</summary>
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

    /// <summary>
    /// A step of connected time, reported by the transport. Returns nothing, deliberately: there is no answer
    /// a meter could give that would end a flight, and a void return means no call site can grow one later.
    /// Ignored when no session is running, so a stray call after the transport has left cannot invent airtime.
    /// </summary>
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

    /// <summary>Restore. Negatives are clamped away — a hand-edited save can no longer buy anything with
    /// these numbers, but a nonsense one should not make the cost figures nonsense too.</summary>
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
