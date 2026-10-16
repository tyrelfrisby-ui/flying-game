using FlyingGame.Sim.Commerce;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The commerce rules, driven through the cases that actually bite: two providers at once, a revoked grant,
/// a dead network, a clock moved backwards, a trial that must not restart itself, an unlock whose included
/// month runs out, the monthly hour cap, and a top-up that outlives the month it was bought in.
///
/// The model these encode is the owner's, 2026-10-15: free download; a fourteen-day trial of everything in
/// single player; a one-time unlock that also carries thirty days of online flying; an online pass after
/// that; a hard cap of seventy hours a month per pilot; and a top-up offered only once the cap is reached.
/// </summary>
public class EntitlementTests
{
    private static readonly DateTime T0 = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static EntitlementGrant Store(double expiresInDays, double verifiedDaysAgo = 0, bool revoked = false)
        => new(EntitlementSource.AppStore, T0.AddDays(expiresInDays), T0.AddDays(-verifiedDaysAgo), revoked);

    private static EntitlementGrant External(double expiresInDays, bool revoked = false)
        => new(EntitlementSource.External, T0.AddDays(expiresInDays), T0, revoked);

    private static EntitlementResolution Resolved(params EntitlementGrant[] grants)
        => new EntitlementResolver { GraceDays = 14 }.Resolve(grants, T0);

    private static EntitlementResolution NoProvider => default;

    // ---- the resolver: one product, one answer ----

    [Fact]
    public void NoGrantsIsNotEntitled()
    {
        var r = new EntitlementResolver().Resolve(Array.Empty<EntitlementGrant>(), T0);
        Assert.Equal(EntitlementTrust.None, r.Trust);
        Assert.False(r.PaidContent);
        Assert.False(r.Confirmed);
    }

    [Fact]
    public void AnActiveSubscriptionFliesEverything()
    {
        var r = new EntitlementResolver().Resolve(new[] { Store(20) }, T0);
        Assert.Equal(EntitlementTrust.Fresh, r.Trust);
        Assert.Equal(EntitlementSource.AppStore, r.Source);
        Assert.True(r.PaidContent);
        Assert.True(r.Confirmed);
    }

    [Fact]
    public void TheLongestLivedGrantWins()
    {
        // Entitled twice over — somebody who bought from us and also linked a membership. Nobody in that
        // position may ever be told they have no access.
        var r = new EntitlementResolver().Resolve(new[] { Store(3), External(40) }, T0);
        Assert.Equal(EntitlementTrust.Fresh, r.Trust);
        Assert.Equal(EntitlementSource.External, r.Source);
        Assert.Equal(T0.AddDays(40), r.ExpiresUtc);
    }

    [Fact]
    public void ARevokedGrantIsDeadEvenWithTimeLeft()
    {
        var r = new EntitlementResolver().Resolve(new[] { Store(90, revoked: true) }, T0);
        Assert.Equal(EntitlementTrust.Expired, r.Trust);
        Assert.False(r.PaidContent);
        Assert.Contains("cancelled", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARevokedGrantDoesNotHideAGoodOne()
    {
        var r = new EntitlementResolver().Resolve(new[] { External(5, revoked: true), Store(30) }, T0);
        Assert.Equal(EntitlementTrust.Fresh, r.Trust);
        Assert.Equal(EntitlementSource.AppStore, r.Source);
    }

    [Fact]
    public void AnExpiredGrantKeepsFlyingThroughTheGraceWindow()
    {
        // The flight trainer rule: a dropped connection must never cost somebody a flight they paid for.
        var r = new EntitlementResolver { GraceDays = 14 }.Resolve(new[] { Store(-3) }, T0);
        Assert.Equal(EntitlementTrust.Grace, r.Trust);
        Assert.True(r.PaidContent);
        // ... but the online pass is checked with Confirmed, which grace does not satisfy: you cannot reach
        // the relay without reaching the store.
        Assert.False(r.Confirmed);
        Assert.Contains("11 more days", r.Reason);
    }

    [Fact]
    public void PastGraceItIsOver()
    {
        var r = new EntitlementResolver { GraceDays = 14 }.Resolve(new[] { Store(-15) }, T0);
        Assert.Equal(EntitlementTrust.Expired, r.Trust);
        Assert.False(r.PaidContent);
    }

    [Fact]
    public void GraceIsSingularOnItsLastDay()
    {
        var r = new EntitlementResolver { GraceDays = 14 }.Resolve(new[] { Store(-13.5) }, T0);
        Assert.Contains("1 more day.", r.Reason);
    }

    [Fact]
    public void WindingTheClockBackDoesNotReviveAnExpiredGrant()
    {
        var res = new EntitlementResolver { GraceDays = 14 };
        Assert.Equal(EntitlementTrust.Expired, res.Resolve(new[] { Store(-20) }, T0).Trust);
        // Going back to a time when it was still good must not help.
        Assert.Equal(EntitlementTrust.Expired, res.Resolve(new[] { Store(-20) }, T0.AddDays(-30)).Trust);
    }

    [Fact]
    public void TheHighWaterMarkSurvivesARestart()
    {
        var first = new EntitlementResolver { GraceDays = 14 };
        first.Resolve(new[] { Store(-20) }, T0);
        var restored = new EntitlementResolver(first.HighWaterUtc) { GraceDays = 14 };
        Assert.Equal(EntitlementTrust.Expired, restored.Resolve(new[] { Store(-20) }, T0.AddDays(-30)).Trust);
    }

    // ---- what you may fly: free, trial, unlocked ----

    [Fact]
    public void AFreshInstallIsFreeAndTheTrialIsOnOffer()
    {
        var c = new ContentAccessResolver().Resolve(false, DateTime.MinValue, NoProvider, DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Free, c.Level);
        Assert.False(c.Everything);
        Assert.True(c.TrialAvailable);
        Assert.False(c.TrialSpent);
        Assert.Contains("glider", c.Reason);
    }

    [Fact]
    public void AStartedTrialOpensEverythingAndCountsDown()
    {
        var r = new ContentAccessResolver { TrialDays = 14 };
        var c = r.Resolve(false, T0.AddDays(-3), NoProvider, DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Trial, c.Level);
        Assert.True(c.Everything);
        Assert.False(c.TrialAvailable);               // already running, not on offer
        Assert.Equal(T0.AddDays(11), c.TrialEndsUtc);
        Assert.Contains("11 more days", c.Reason);
    }

    [Fact]
    public void AFinishedTrialFallsBackToFreeAndIsNotOfferedAgain()
    {
        var r = new ContentAccessResolver { TrialDays = 14 };
        var c = r.Resolve(false, T0.AddDays(-20), NoProvider, DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Free, c.Level);
        Assert.True(c.TrialSpent);
        Assert.False(c.TrialAvailable);
        Assert.Contains("trial has ended", c.Reason);
    }

    [Fact]
    public void WindingTheClockBackDoesNotExtendTheTrial()
    {
        // The start date is Apple-signed, but "now" is the device's. Without the high-water mark, setting the
        // clock back a week would be a week of free trial.
        var r = new ContentAccessResolver { TrialDays = 14 };
        Assert.Equal(AccessLevel.Free, r.Resolve(false, T0.AddDays(-20), NoProvider, DateTime.MinValue, T0).Level);
        Assert.Equal(AccessLevel.Free, r.Resolve(false, T0.AddDays(-20), NoProvider, DateTime.MinValue, T0.AddDays(-14)).Level);
    }

    [Fact]
    public void TheUnlockIsPermanentAndBeatsALiveTrial()
    {
        var r = new ContentAccessResolver { TrialDays = 14 };
        var c = r.Resolve(true, T0.AddDays(-3), NoProvider, DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Unlocked, c.Level);
        Assert.Equal(EntitlementSource.AppStore, c.Source);
    }

    [Fact]
    public void AMemberGetsTheUnlocksContentWhileTheyAreAMember()
    {
        var c = new ContentAccessResolver().Resolve(false, DateTime.MinValue, Resolved(External(30)), DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Unlocked, c.Level);
        Assert.Equal(EntitlementSource.External, c.Source);

        // ... and keeps it through the grace window when their provider cannot be reached.
        var g = new ContentAccessResolver().Resolve(false, DateTime.MinValue, Resolved(External(-3)), DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Unlocked, g.Level);
        Assert.Contains("keep flying", g.Reason);

        // When the membership is well and truly gone, so is the content.
        var x = new ContentAccessResolver().Resolve(false, DateTime.MinValue, Resolved(External(-30)), DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Free, x.Level);
    }

    [Fact]
    public void ACompUnlocksEverythingUntilItRunsOut()
    {
        Assert.Equal(AccessLevel.Unlocked, new ContentAccessResolver()
            .Resolve(false, DateTime.MinValue, NoProvider, T0.AddDays(5), T0).Level);
        Assert.Equal(AccessLevel.Free, new ContentAccessResolver()
            .Resolve(false, DateTime.MinValue, NoProvider, T0.AddDays(-1), T0).Level);
    }

    // ---- whether you may go online at all ----

    [Fact]
    public void FlyingOnlineNeedsThePassOrTheUnlocksIncludedMonth()
    {
        var none = new MultiplayerAccessResolver().Resolve(NoProvider, DateTime.MinValue, T0);
        Assert.False(none.Permitted);
        Assert.Contains("needs a pass", none.Reason);
    }

    [Fact]
    public void ATrialDoesNotIncludeFlyingOnline()
    {
        // The owner was explicit: fourteen days of everything, single player. A trial user with no pass and
        // no unlock is not permitted online, however much content is open to them.
        var content = new ContentAccessResolver { TrialDays = 14 }
            .Resolve(false, T0.AddDays(-1), NoProvider, DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Trial, content.Level);
        Assert.True(content.Everything);

        Assert.False(new MultiplayerAccessResolver().Resolve(NoProvider, DateTime.MinValue, T0).Permitted);
    }

    [Fact]
    public void AMemberStillBuysThePass()
    {
        // An external membership grants content and nothing else — the relay is ours and everyone pays for it.
        var content = new ContentAccessResolver().Resolve(false, DateTime.MinValue, Resolved(External(300)), DateTime.MinValue, T0);
        Assert.Equal(AccessLevel.Unlocked, content.Level);
        Assert.False(new MultiplayerAccessResolver().Resolve(NoProvider, DateTime.MinValue, T0).Permitted);
    }

    [Fact]
    public void AConfirmedPassLetsYouOnline()
    {
        var o = new MultiplayerAccessResolver().Resolve(Resolved(Store(20)), DateTime.MinValue, T0);
        Assert.True(o.Permitted);
        Assert.Equal(EntitlementSource.AppStore, o.Source);
    }

    [Fact]
    public void APassInGraceDoesNotLetYouOnline()
    {
        // No grace for the relay: if you can reach it you can reach the store, so there is no case where
        // believing an unverified claim is the kind thing to do.
        var o = new MultiplayerAccessResolver().Resolve(Resolved(Store(-3)), DateTime.MinValue, T0);
        Assert.False(o.Permitted);
        Assert.Contains("Connect to the internet", o.Reason);
    }

    [Fact]
    public void TheUnlockCarriesItsFirstThirtyDaysOnline()
    {
        var inside = new MultiplayerAccessResolver { IncludedDaysWithUnlock = 30 }
            .Resolve(NoProvider, T0.AddDays(-10), T0);
        Assert.True(inside.Permitted);
        Assert.Equal(T0.AddDays(20), inside.IncludedUntilUtc);
        Assert.Contains("20 more days", inside.Reason);

        var outside = new MultiplayerAccessResolver { IncludedDaysWithUnlock = 30 }
            .Resolve(NoProvider, T0.AddDays(-40), T0);
        Assert.False(outside.Permitted);
        Assert.Contains("included with your unlock has ended", outside.Reason);
    }

    [Fact]
    public void WindingTheClockBackDoesNotExtendTheIncludedMonth()
    {
        var r = new MultiplayerAccessResolver { IncludedDaysWithUnlock = 30 };
        Assert.False(r.Resolve(NoProvider, T0.AddDays(-40), T0).Permitted);
        Assert.False(r.Resolve(NoProvider, T0.AddDays(-40), T0.AddDays(-20)).Permitted);
    }

    // ---- the hours: a hard cap, and a top-up that outlives the month ----

    [Fact]
    public void AFreshMeterCarriesTheMonthsHoursAndCanJoin()
    {
        var m = new AirtimeMeter();
        Assert.Equal(70, m.IncludedHoursPerPeriod, 6);
        Assert.Equal(70, m.RemainingHours, 6);
        Assert.True(m.CanJoin(out _));
        Assert.False(m.CapReached);
    }

    [Fact]
    public void FlyingSpendsTheMonthsHoursAndCountsTheCost()
    {
        var m = new AirtimeMeter();
        m.BeginSession(T0);
        Assert.True(m.Record(3600));
        Assert.Equal(69, m.RemainingHours, 6);
        Assert.Equal(1, m.PeriodHours, 6);            // the cost signal counts every second flown
        Assert.Equal(1, m.LifetimeHours, 6);
        Assert.Equal(1, m.PeriodSessions);
    }

    [Fact]
    public void AStrayReportOutsideASessionCannotInventAirtime()
    {
        var m = new AirtimeMeter();
        m.Record(3600);
        Assert.Equal(0, m.LifetimeSeconds);
        m.BeginSession(T0);
        m.EndSession();
        m.Record(3600);
        Assert.Equal(0, m.LifetimeSeconds);
    }

    [Fact]
    public void SessionsAreCountedBecauseWakingARoomIsTheExpensivePart()
    {
        // Ten one-minute joins cost the relay far more per minute than one ten-minute flight, so hours alone
        // would hide the shape of the bill.
        var m = new AirtimeMeter();
        for (int i = 0; i < 10; i++) { m.BeginSession(T0.AddMinutes(i)); m.Record(60); m.EndSession(); }
        Assert.Equal(10, m.PeriodSessions);
        Assert.Equal(10, m.LifetimeSessions);
        Assert.Equal(600, m.PeriodSeconds, 6);
    }

    [Fact]
    public void TheCapIsHardAndEndsTheFlight()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        m.BeginSession(T0);
        Assert.False(m.Record(4000));                 // past the hour: the transport's cue to leave
        Assert.True(m.CapReached);
        Assert.True(m.Exhausted);
        Assert.Equal(0, m.RemainingSeconds, 6);       // never billed for the overshoot
        Assert.False(m.CanJoin(out string why));
        Assert.Contains("used this month", why);
    }

    [Fact]
    public void AlmostNoTimeLeftIsRefusedBeforeTheFlightStarts()
    {
        // Being dropped thirty seconds after joining is worse than being told there is not time for a flight.
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1, MinimumToJoinSeconds = 120 };
        m.BeginSession(T0);
        m.Record(3540);                               // 60 s left
        Assert.False(m.CanJoin(out string why));
        Assert.Contains("Not enough", why);
    }

    [Fact]
    public void ATopUpIsSpentOnlyAfterTheMonthsHours()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 2 };
        m.CreditHours(70);
        m.BeginSession(T0);
        m.Record(3600);
        Assert.Equal(3600, m.IncludedRemainingSeconds, 6);        // one of the two included hours left
        Assert.Equal(70 * 3600, m.PurchasedRemainingSeconds, 6);  // the top-up is untouched
        m.Record(3600);
        Assert.Equal(0, m.IncludedRemainingSeconds, 6);
        Assert.True(m.CapReached);
        m.Record(3600);
        Assert.Equal(69 * 3600, m.PurchasedRemainingSeconds, 6);  // now it spills into what was bought
        Assert.True(m.CanJoin(out _));                            // and flying carries on
    }

    [Fact]
    public void TheTopUpIsOnlyWorthOfferingOnceTheCapIsReached()
    {
        // The owner's rule: not sold before then.
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        Assert.False(m.CapReached);
        m.BeginSession(T0);
        m.Record(3600);
        Assert.True(m.CapReached);
    }

    [Fact]
    public void ToppedUpHoursSurviveTheEndOfTheMonth()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        m.CreditHours(5);
        m.BeginSession(T0);
        m.Record(3600);                               // the month's hour is gone
        m.Tick(T0.AddDays(31));
        Assert.Equal(3600, m.IncludedRemainingSeconds, 6);        // the allowance came back
        Assert.Equal(5 * 3600, m.PurchasedRemainingSeconds, 6);   // and the bought hours never left
    }

    [Fact]
    public void TheMonthsHoursResetAndTheCostCountersStartOver()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        m.BeginSession(T0);
        m.Record(1800);
        m.Tick(T0.AddDays(31));
        Assert.Equal(3600, m.IncludedRemainingSeconds, 6);
        Assert.Equal(0, m.PeriodSeconds, 6);
        Assert.Equal(0, m.PeriodSessions);
        Assert.Equal(1800, m.LifetimeSeconds, 6);     // the lifetime total is not a period
    }

    [Fact]
    public void MonthsAwayGrantsOneMonthsHoursNotOnePerMonthSince()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        m.BeginSession(T0);
        m.Record(3600);
        m.Tick(T0.AddDays(400));
        Assert.Equal(3600, m.IncludedRemainingSeconds, 6);
        Assert.True(m.PeriodEndUtc > T0.AddDays(400));
        Assert.True(m.PeriodEndUtc <= T0.AddDays(430));
    }

    [Fact]
    public void APeriodNeverUnrollsSoTheClockCannotRefillTheHours()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        m.BeginSession(T0);
        m.Record(3600);
        m.Tick(T0.AddDays(-100));                     // a clock wound back
        Assert.Equal(0, m.IncludedRemainingSeconds, 6);
        Assert.Equal(T0.AddDays(30), m.PeriodEndUtc);
    }

    [Fact]
    public void RaisingTheCapMidMonthHandsOverTheDifference()
    {
        // Otherwise a pilot who has used their hours waits a month for a change that was meant to help them.
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 1 };
        m.BeginSession(T0);
        m.Record(3600);
        Assert.Equal(0, m.IncludedRemainingSeconds, 6);
        m.IncludedHoursPerPeriod = 3;
        Assert.Equal(2 * 3600, m.IncludedRemainingSeconds, 6);
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        var m = new AirtimeMeter { IncludedHoursPerPeriod = 10 };
        m.CreditHours(4);
        m.BeginSession(T0);
        m.Record(5400);
        var s = m.Save();

        var back = new AirtimeMeter();
        back.Load(s);
        Assert.Equal(m.IncludedRemainingSeconds, back.IncludedRemainingSeconds, 6);
        Assert.Equal(m.PurchasedRemainingSeconds, back.PurchasedRemainingSeconds, 6);
        Assert.Equal(m.IncludedHoursPerPeriod, back.IncludedHoursPerPeriod, 6);
        Assert.Equal(m.PeriodSeconds, back.PeriodSeconds, 6);
        Assert.Equal(m.PeriodSessions, back.PeriodSessions);
        Assert.Equal(m.LifetimeSeconds, back.LifetimeSeconds, 6);
        Assert.Equal(m.PeriodEndUtc, back.PeriodEndUtc);
        Assert.False(back.InSession);                 // nothing survives a relaunch mid-flight
    }

    [Fact]
    public void AHandEditedSaveCannotGrantItselfHours()
    {
        // Claims more of the month than the cap holds, more bought hours than were ever bought, and negative
        // totals. Every one is clamped rather than trusted.
        var back = new AirtimeMeter();
        back.Load(new AirtimeMeter.Snapshot(999 * 3600, 999 * 3600, -1, -1, -1, -1, 2 * 3600, 70 * 3600, T0, 30, T0));
        Assert.Equal(70 * 3600, back.IncludedRemainingSeconds, 6);
        Assert.Equal(2 * 3600, back.PurchasedRemainingSeconds, 6);   // clamped to what was actually bought
        Assert.Equal(0, back.PeriodSeconds, 6);
        Assert.Equal(0, back.PeriodSessions);
        Assert.Equal(0, back.LifetimeSeconds, 6);
        Assert.Equal(0, back.LifetimeSessions);
    }
}
