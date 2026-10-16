using FlyingGame.Sim.Commerce;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The entitlement rules and the data meter, driven through the cases that actually bite: two providers at
/// once, a revoked grant, a dead network, a clock moved backwards, a plan period rolling over after months
/// away, and running dry mid-session.
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
        Assert.False(r.MultiplayerEntitled);
    }

    [Fact]
    public void AnActiveSubscriptionFliesEverything()
    {
        var r = new EntitlementResolver().Resolve(new[] { Store(20) }, T0);
        Assert.Equal(EntitlementTrust.Fresh, r.Trust);
        Assert.Equal(EntitlementSource.AppStore, r.Source);
        Assert.True(r.PaidContent);
        Assert.True(r.MultiplayerEntitled);
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
        // ... but multiplayer costs money per session, so it wants a grant we actually confirmed.
        Assert.False(r.MultiplayerEntitled);
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

    // ---- the data meter ----

    private const long Mb = 1024 * 1024;

    [Fact]
    public void AnEmptyMeterCannotJoin()
    {
        var m = new DataMeter();
        Assert.False(m.CanJoin(out string why));
        Assert.Contains("out of multiplayer data", why);
    }

    [Fact]
    public void AlmostEmptyIsAlsoRefused()
    {
        var m = new DataMeter();
        m.Credit(Mb / 2);                       // below MinimumToJoinBytes
        Assert.False(m.CanJoin(out string why));
        Assert.Contains("Not enough", why);
    }

    [Fact]
    public void CreditingLetsYouJoinAndSpend()
    {
        var m = new DataMeter();
        m.Credit(100 * Mb);
        Assert.True(m.CanJoin(out _));
        m.BeginSession();
        Assert.True(m.Record(10 * Mb, 1.0));
        Assert.Equal(90 * Mb, m.AvailableBytes);
        Assert.Equal(10 * Mb, m.SessionBytes);
        Assert.Equal(10 * Mb, m.LifetimeBytes);
    }

    [Fact]
    public void ThePlanIsSpentBeforePurchasedBytes()
    {
        // Otherwise a plan holder burns bytes they paid extra for while their allowance sits unused.
        var m = new DataMeter();
        m.Credit(50 * Mb);
        m.SetPlan(20 * Mb, T0, 30);
        m.BeginSession();
        m.Record(15 * Mb, 1.0);
        Assert.Equal(5 * Mb, m.PlanRemainingBytes);
        Assert.Equal(50 * Mb, m.BalanceBytes);
    }

    [Fact]
    public void SpendingSpillsFromThePlanIntoTheBalance()
    {
        var m = new DataMeter();
        m.Credit(50 * Mb);
        m.SetPlan(20 * Mb, T0, 30);
        m.BeginSession();
        m.Record(30 * Mb, 1.0);
        Assert.Equal(0, m.PlanRemainingBytes);
        Assert.Equal(40 * Mb, m.BalanceBytes);
    }

    [Fact]
    public void RunningDryEndsTheSessionRatherThanGoingNegative()
    {
        var m = new DataMeter();
        m.Credit(5 * Mb);
        m.BeginSession();
        Assert.False(m.Record(9 * Mb, 1.0));     // the transport's cue to leave
        Assert.Equal(0, m.AvailableBytes);       // never billed for the overshoot
        Assert.True(m.Exhausted);
    }

    [Fact]
    public void ThePlanPeriodRollsOver()
    {
        var m = new DataMeter();
        m.SetPlan(20 * Mb, T0, 30);
        m.BeginSession();
        m.Record(20 * Mb, 1.0);
        Assert.Equal(0, m.PlanRemainingBytes);
        m.Tick(T0.AddDays(31));
        Assert.Equal(20 * Mb, m.PlanRemainingBytes);
    }

    [Fact]
    public void MonthsAwayLandsOnOnePeriodNotManyRefills()
    {
        var m = new DataMeter();
        m.SetPlan(20 * Mb, T0, 30);
        m.BeginSession();
        m.Record(20 * Mb, 1.0);
        m.Tick(T0.AddDays(400));                 // a long-dormant install
        Assert.Equal(20 * Mb, m.PlanRemainingBytes);          // one allowance, not thirteen
        Assert.True(m.PlanPeriodEndUtc > T0.AddDays(400));
        Assert.True(m.PlanPeriodEndUtc <= T0.AddDays(430));
    }

    [Fact]
    public void APeriodNeverUnrolls()
    {
        var m = new DataMeter();
        m.SetPlan(20 * Mb, T0, 30);
        m.Tick(T0.AddDays(31));
        DateTime after = m.PlanPeriodEndUtc;
        m.Tick(T0.AddDays(-100));
        Assert.Equal(after, m.PlanPeriodEndUtc);
    }

    [Fact]
    public void TheEstimateIsInTimeNotBytes()
    {
        var m = new DataMeter();
        m.Credit(100 * Mb);
        m.BeginSession();
        Assert.True(double.IsPositiveInfinity(m.SecondsRemaining));   // nothing flowing yet
        for (int i = 0; i < 200; i++) m.Record(64 * 1024, 1.0);       // ~64 kB/s for 200 s
        Assert.InRange(m.RateBytesPerSec, 60_000, 68_000);
        Assert.InRange(m.SecondsRemaining, 1000, 1600);
    }

    [Fact]
    public void SaveAndLoadRoundTrips()
    {
        var m = new DataMeter();
        m.Credit(70 * Mb);
        m.SetPlan(20 * Mb, T0, 30);
        m.BeginSession();
        m.Record(25 * Mb, 1.0);
        var s = m.Save();

        var back = new DataMeter();
        back.Load(s);
        Assert.Equal(m.BalanceBytes, back.BalanceBytes);
        Assert.Equal(m.PlanRemainingBytes, back.PlanRemainingBytes);
        Assert.Equal(m.LifetimeBytes, back.LifetimeBytes);
        Assert.Equal(m.PlanPeriodEndUtc, back.PlanPeriodEndUtc);
    }

    [Fact]
    public void LoadedBalancesCannotBeTamperedUpwards()
    {
        // A hand-edited save that claims more plan allowance than the plan has is clamped, not trusted.
        var back = new DataMeter();
        back.Load(new DataMeter.Snapshot(-5, 10 * Mb, 999 * Mb, -1, T0, T0, 30));
        Assert.Equal(0, back.BalanceBytes);
        Assert.Equal(10 * Mb, back.PlanRemainingBytes);
        Assert.Equal(0, back.LifetimeBytes);
    }
}
