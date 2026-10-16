using FlyingGame.Sim.Commerce;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The entitlement rules and the airtime meter, driven through the cases that actually bite: two providers
/// at once, two subscriptions at once, a revoked grant, a dead network, a clock moved backwards, and a
/// counting period rolling over after months away.
/// </summary>
public class EntitlementTests
{
    private static readonly DateTime T0 = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static EntitlementGrant Store(double expiresInDays, double verifiedDaysAgo = 0, bool revoked = false)
        => new(EntitlementSource.AppStore, T0.AddDays(expiresInDays), T0.AddDays(-verifiedDaysAgo), revoked);

    private static EntitlementGrant External(double expiresInDays, bool revoked = false)
        => new(EntitlementSource.External, T0.AddDays(expiresInDays), T0, revoked);

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
        // Entitled twice over — an App Store subscriber who also linked a membership. Nobody in that
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
        var res = new EntitlementResolver { GraceDays = 14 };
        var r = res.Resolve(new[] { Store(-3) }, T0);
        Assert.Equal(EntitlementTrust.Grace, r.Trust);
        Assert.True(r.PaidContent);
        // ... but the Multiplayer Pass is checked with Confirmed, which grace does not satisfy: you cannot
        // reach the relay without reaching the store.
        Assert.False(r.Confirmed);
        Assert.Contains("11 more days", r.Reason);
    }

    [Fact]
    public void PastGraceItIsOver()
    {
        var res = new EntitlementResolver { GraceDays = 14 };
        var r = res.Resolve(new[] { Store(-15) }, T0);
        Assert.Equal(EntitlementTrust.Expired, r.Trust);
        Assert.False(r.PaidContent);
    }

    [Fact]
    public void GraceIsSingularOnItsLastDay()
    {
        var res = new EntitlementResolver { GraceDays = 14 };
        var r = res.Resolve(new[] { Store(-13.5) }, T0);
        Assert.Contains("1 more day.", r.Reason);
    }

    [Fact]
    public void WindingTheClockBackDoesNotReviveAnExpiredGrant()
    {
        var res = new EntitlementResolver { GraceDays = 14 };
        // Seen well past grace once...
        Assert.Equal(EntitlementTrust.Expired, res.Resolve(new[] { Store(-20) }, T0).Trust);
        // ... so going back to a time when it was still good must not help.
        var r = res.Resolve(new[] { Store(-20) }, T0.AddDays(-30));
        Assert.Equal(EntitlementTrust.Expired, r.Trust);
    }

    [Fact]
    public void TheHighWaterMarkSurvivesARestart()
    {
        var first = new EntitlementResolver { GraceDays = 14 };
        first.Resolve(new[] { Store(-20) }, T0);
        var restored = new EntitlementResolver(first.HighWaterUtc) { GraceDays = 14 };
        Assert.Equal(EntitlementTrust.Expired, restored.Resolve(new[] { Store(-20) }, T0.AddDays(-30)).Trust);
    }

    [Fact]
    public void TheTwoSubscriptionsAreResolvedIndependently()
    {
        // One resolver per product (EntitlementRuntime keeps two), so holding the content subscription
        // never implies the Multiplayer Pass and a lapsed pass never locks the airplanes.
        var content = new EntitlementResolver().Resolve(new[] { Store(20) }, T0);
        var pass = new EntitlementResolver().Resolve(Array.Empty<EntitlementGrant>(), T0);
        Assert.True(content.PaidContent);
        Assert.False(pass.Confirmed);

        var lapsedPass = new EntitlementResolver { GraceDays = 14 }.Resolve(new[] { Store(-20) }, T0);
        Assert.False(lapsedPass.Confirmed);
        Assert.True(new EntitlementResolver().Resolve(new[] { External(40) }, T0).PaidContent);
    }

    // ---- the airtime meter: accounting only. It must never refuse anybody anything. ----

    [Fact]
    public void AirtimeStartsAtZeroAndCountsOnlyInASession()
    {
        var m = new AirtimeMeter();
        m.Record(10);                              // no session: cannot invent airtime
        Assert.Equal(0, m.LifetimeSeconds);

        m.BeginSession(T0);
        m.Record(60);
        m.Record(30);
        Assert.Equal(90, m.SessionSeconds);
        Assert.Equal(90, m.PeriodSeconds);
        Assert.Equal(90, m.LifetimeSeconds);
        Assert.Equal(1, m.PeriodSessions);
        Assert.True(m.InSession);

        m.EndSession();
        m.Record(1000);                            // a stray report after leaving changes nothing
        Assert.Equal(90, m.LifetimeSeconds);
    }

    [Fact]
    public void SessionsAreCountedBecauseWakingARoomIsTheExpensivePart()
    {
        var m = new AirtimeMeter();
        for (int i = 0; i < 10; i++) { m.BeginSession(T0.AddMinutes(i)); m.Record(60); m.EndSession(); }
        Assert.Equal(10, m.PeriodSessions);
        Assert.Equal(10, m.LifetimeSessions);
        Assert.Equal(600, m.PeriodSeconds);
        Assert.Equal(60, m.SessionSeconds);        // the last one only
    }

    [Fact]
    public void HoursAreWhatTheCostCheckReads()
    {
        var m = new AirtimeMeter();
        m.BeginSession(T0);
        m.Record(5400);                            // 1.5 h
        Assert.Equal(1.5, m.PeriodHours, 6);
        Assert.Equal(1.5, m.LifetimeHours, 6);
        Assert.Equal(1.5, m.SessionHours, 6);
    }

    [Fact]
    public void TheFirstJoinStartsTheCountingPeriod()
    {
        var m = new AirtimeMeter();
        Assert.Equal(0, m.PeriodDays);
        m.BeginSession(T0);
        Assert.Equal(30, m.PeriodDays);
        Assert.Equal(T0.AddDays(30), m.PeriodEndUtc);
    }

    [Fact]
    public void ThePeriodRollsOverAndTheMonthStartsEmpty()
    {
        var m = new AirtimeMeter();
        m.BeginSession(T0);
        m.Record(3600);
        Assert.Equal(3600, m.PeriodSeconds);
        m.Tick(T0.AddDays(31));
        Assert.Equal(0, m.PeriodSeconds);
        Assert.Equal(0, m.PeriodSessions);
        Assert.Equal(3600, m.LifetimeSeconds);     // the lifetime total is not a period
    }

    [Fact]
    public void MonthsAwayLandsOnOnePeriodNotManyRollovers()
    {
        var m = new AirtimeMeter();
        m.BeginSession(T0);
        m.Record(3600);
        m.Tick(T0.AddDays(400));                   // a long-dormant install
        Assert.True(m.PeriodEndUtc > T0.AddDays(400));
        Assert.True(m.PeriodEndUtc <= T0.AddDays(430));
        Assert.Equal(0, m.PeriodSeconds);
    }

    [Fact]
    public void APeriodNeverUnrolls()
    {
        var m = new AirtimeMeter();
        m.BeginSession(T0);
        m.Tick(T0.AddDays(31));
        DateTime after = m.PeriodEndUtc;
        m.Tick(T0.AddDays(-100));                  // a clock wound back
        Assert.Equal(after, m.PeriodEndUtc);
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        var m = new AirtimeMeter();
        m.BeginSession(T0);
        m.Record(1234);
        var s = m.Save();

        var back = new AirtimeMeter();
        back.Load(s);
        Assert.Equal(m.PeriodSeconds, back.PeriodSeconds);
        Assert.Equal(m.PeriodSessions, back.PeriodSessions);
        Assert.Equal(m.LifetimeSeconds, back.LifetimeSeconds);
        Assert.Equal(m.LifetimeSessions, back.LifetimeSessions);
        Assert.Equal(m.PeriodEndUtc, back.PeriodEndUtc);
        Assert.False(back.InSession);              // nothing survives a relaunch mid-flight
    }

    [Fact]
    public void ANegativeSaveIsClampedNotTrusted()
    {
        var back = new AirtimeMeter();
        back.Load(new AirtimeMeter.Snapshot(-5, -2, -9, -1, T0, -7, T0));
        Assert.Equal(0, back.PeriodSeconds);
        Assert.Equal(0, back.PeriodSessions);
        Assert.Equal(0, back.LifetimeSeconds);
        Assert.Equal(0, back.LifetimeSessions);
        Assert.Equal(0, back.PeriodDays);
    }

    [Fact]
    public void TheMeterHasNoWayToRefuseAnybody()
    {
        // The whole point of the owner's 2026-10-15 decision: the pass decides who flies online, and flying
        // a lot is never punished. If this type ever grows a CanJoin, that decision has been reversed.
        Assert.Null(typeof(AirtimeMeter).GetMethod("CanJoin"));
        Assert.Null(typeof(AirtimeMeter).GetProperty("Exhausted"));
    }
}
