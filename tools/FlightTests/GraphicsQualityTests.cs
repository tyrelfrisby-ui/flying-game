using System;
using System.Linq;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The graphics slider's policy (Ty 2026-11-11): one scale, an automatic downgrade that can go below the pilot's
/// choice and never above it, and a warning that says what was given up and why. All headless — the Unity side only
/// applies a <see cref="GraphicsQuality.Knobs"/>.
/// </summary>
public sealed class GraphicsQualityTests
{
    private static readonly GraphicsQuality.Tier[] AllTiers =
        (GraphicsQuality.Tier[])Enum.GetValues(typeof(GraphicsQuality.Tier));

    // ---- the scale -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryKnobIsMonotoneUpTheScale()
    {
        // One scale means one direction: nothing may get more expensive on the way DOWN, or a downgrade could cost
        // more than it saves and the whole mechanism inverts.
        for (int i = 1; i < AllTiers.Length; i++)
        {
            var lo = GraphicsQuality.Knobs.For(AllTiers[i - 1]);
            var hi = GraphicsQuality.Knobs.For(AllTiers[i]);
            Assert.True(hi.UnityLevel > lo.UnityLevel);
            Assert.True(hi.ReflectionTextureSize >= lo.ReflectionTextureSize);
            Assert.True(hi.GroundShadowMaxAltitudeM >= lo.GroundShadowMaxAltitudeM);
            Assert.True(hi.WakeGridCells >= lo.WakeGridCells);
            Assert.True(hi.CloudBankLobes >= lo.CloudBankLobes);
            Assert.True(hi.CloudCullRadiusM >= lo.CloudCullRadiusM);
            Assert.True(hi.GroundFogDrawRangeM >= lo.GroundFogDrawRangeM);
            Assert.True(!lo.ReflectScenery || hi.ReflectScenery);
            Assert.True(!lo.DetailedScenery || hi.DetailedScenery);
            Assert.True(!lo.UrbanTraffic || hi.UrbanTraffic);
            // The one knob that runs the other way: a BIGGER canyon-wall step is a cheaper ribbon.
            Assert.True(hi.CanyonWallStepM <= lo.CanyonWallStepM);
        }
    }

    [Fact]
    public void MinimumKeepsNothingOptionalAndHighIsTodaysBuild()
    {
        var min = GraphicsQuality.Knobs.For(GraphicsQuality.Tier.Minimum);
        Assert.False(min.ReflectionOn);
        Assert.False(min.GroundShadowOn);
        Assert.Equal(0.0, min.GroundFogDrawRangeM);

        // Every High number is a call site in the tree today — see the field comments. If one of these fails,
        // adopting the scale would have changed what the game draws, which it must not.
        var high = GraphicsQuality.Knobs.For(GraphicsQuality.Tier.High);
        Assert.Equal(1024, high.WakeGridCells);                 // WakeWaves.AllocateLake
        Assert.Equal(1500.0, high.GroundShadowMaxAltitudeM);    // GroundShadow.MaxAltitudeM
        Assert.Equal(20, high.CloudBankLobes);                  // CloudPlayground's horseshoe bank
        Assert.Equal(19000.0, high.CloudCullRadiusM);           // CloudPlayground's cluster cull
        Assert.Equal(14000.0, high.GroundFogDrawRangeM);        // GroundFog.DrawRangeM
        Assert.Equal(12.0, high.CanyonWallStepM);               // PlaygroundBuilder, the not-low branch
        Assert.True(high.ReflectionOn && high.ReflectScenery && high.GroundShadowOn);
        Assert.True(high.DetailedScenery && high.UrbanTraffic);
    }

    [Fact]
    public void BalancedIsTheWaterReflectionTheGameHasActuallyBeenShipping()
    {
        // SceneBootstrap adds WaterReflection and never sets its fields, so the shipped build is the component's
        // own defaults: Balanced, 768, scenery on. The default tier must reproduce that exactly.
        var b = GraphicsQuality.Knobs.For(GraphicsQuality.DefaultFor(GraphicsQuality.Platform.Handheld));
        Assert.Equal(768, b.ReflectionTextureSize);
        Assert.True(b.ReflectScenery);
    }

    [Fact]
    public void UnityLevelsKeepTheMeaningOfTheComparisonsTheyReplace()
    {
        // PlaygroundBuilder's `GetQualityLevel() <= 1` was "the two cheap levels"; UrbanFacades' `> 2` was "the top".
        Assert.True(GraphicsQuality.Knobs.For(GraphicsQuality.Tier.Low).UnityLevel <= 1);
        Assert.True(GraphicsQuality.Knobs.For(GraphicsQuality.Tier.Minimum).UnityLevel <= 1);
        Assert.False(GraphicsQuality.Knobs.For(GraphicsQuality.Tier.Balanced).UnityLevel <= 1);
        Assert.True(GraphicsQuality.Knobs.For(GraphicsQuality.Tier.High).UnityLevel > 2);
        Assert.False(GraphicsQuality.Knobs.For(GraphicsQuality.Tier.Balanced).UnityLevel > 2);
    }

    [Fact]
    public void ClampAndStepsStayOnTheScale()
    {
        Assert.Equal(GraphicsQuality.Floor, GraphicsQuality.Down(GraphicsQuality.Tier.Minimum));
        Assert.Equal(GraphicsQuality.Tier.High,
                     GraphicsQuality.Up(GraphicsQuality.Tier.High, GraphicsQuality.Tier.High));
        Assert.Equal(GraphicsQuality.Tier.Low,
                     GraphicsQuality.Up(GraphicsQuality.Tier.Low, GraphicsQuality.Tier.Low));
        Assert.Equal(GraphicsQuality.Tier.High, GraphicsQuality.Clamp((GraphicsQuality.Tier)99));
        Assert.Equal(GraphicsQuality.Tier.Minimum, GraphicsQuality.Clamp((GraphicsQuality.Tier)(-4)));
    }

    // ---- what the cap does -----------------------------------------------------------------------------------

    [Fact]
    public void TheCapDiscardsTheSurplusRatherThanDeferringIt()
    {
        // 1/20 s cap, a 120 ms frame with nothing owed: 70 ms of flying is thrown away, not carried over.
        Assert.Equal(0.07, SimCatchUp.Discarded(0.0, 0.12, 1.0 / 20.0), 9);
        Assert.Equal(0.0, SimCatchUp.Discarded(0.0, 0.016, 1.0 / 20.0), 9);
        // The headset's cap is two display periods: a 50 ms garbage collection at 72 Hz loses 22 ms.
        Assert.Equal(0.05 - 2.0 / 72.0, SimCatchUp.Discarded(0.0, 0.05, 2.0 / 72.0), 9);
    }

    // ---- requirement 1: never above the pilot's choice ---------------------------------------------------------

    [Fact]
    public void AutoNeverExceedsTheChosenTier()
    {
        foreach (GraphicsQuality.Tier chosen in AllTiers)
        {
            var q = new AutoQuality(chosen);
            var rng = new Random(7);
            for (int i = 0; i < 20000; i++)
            {
                // Anything at all: clean frames, hitches, storms of capped frames.
                double dt = 0.004 + rng.NextDouble() * 0.09;
                int capped = rng.NextDouble() < 0.3 ? rng.Next(0, 4) : 0;
                q.Sample(dt, capped, capped * 0.01);
                Assert.True(q.Effective <= chosen, $"{q.Effective} > {chosen} at frame {i}");
                Assert.True(q.Effective >= GraphicsQuality.Floor);
            }
        }
    }

    [Fact]
    public void APilotWhoPicksLowGetsLow()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.Low);
        for (int i = 0; i < 5000; i++) q.Sample(1.0 / 120.0, 0);   // perfect frames for 40 s
        Assert.Equal(GraphicsQuality.Tier.Low, q.Effective);
        Assert.False(q.Downgraded);
    }

    // ---- requirement: a capped-catch-up run downgrades ---------------------------------------------------------

    [Fact]
    public void CappedCatchUpTriggersADowngradeEvenAtAHealthyFrameRate()
    {
        // 60 fps throughout — a frame-rate proxy would see nothing — but the sim is capping two frames a second.
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        bool changed = false;
        for (int i = 0; i < 300 && !changed; i++) changed = q.Sample(1.0 / 60.0, i % 30 == 0 ? 1 : 0, 0.02);
        Assert.True(changed);
        Assert.Equal(GraphicsQuality.Tier.Balanced, q.Effective);
        Assert.True(q.Pending.HasValue);
        Assert.Equal(GraphicsQuality.Reason.SimCapped, q.Pending!.Value.Why);
    }

    [Fact]
    public void ADeviceInTroubleIsCaughtInsideASecond()
    {
        // The one measured case: 138 capped frames in 5 s at 27.6 fps (docs/quest-performance.md, 2026-11-02).
        var q = new AutoQuality(GraphicsQuality.Tier.Balanced);
        double t = 0;
        bool changed = false;
        for (int i = 0; i < 200 && !changed; i++) { changed = q.Sample(1.0 / 27.6, 1, 0.012); t += 1.0 / 27.6; }
        Assert.True(changed);
        Assert.True(t < 1.0, $"took {t:0.00} s");
        Assert.Equal(GraphicsQuality.Tier.Low, q.Effective);
    }

    [Fact]
    public void ASingleHitchDoesNotMoveTheTier()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        for (int i = 0; i < 600; i++)
        {
            // One 120 ms garbage collection every ten seconds, costing one capped frame; otherwise clean.
            bool gc = i % 600 == 599;
            q.Sample(gc ? 0.12 : 1.0 / 72.0, gc ? 1 : 0, gc ? 0.07 : 0.0);
        }
        Assert.Equal(GraphicsQuality.Tier.High, q.Effective);
        Assert.False(q.Pending.HasValue);
    }

    // ---- frame rate is the SECONDARY signal --------------------------------------------------------------------

    [Fact]
    public void SlowFramesAloneDowngradeAndSaySoDifferently()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High, new AutoQuality.Policy { TargetFrameSec = 1.0 / 72.0 });
        bool changed = false;
        for (int i = 0; i < 400 && !changed; i++) changed = q.Sample(0.030, 0);   // 33 fps on a 72 Hz display, sim fine
        Assert.True(changed);
        Assert.Equal(GraphicsQuality.Reason.FrameRate, q.Pending!.Value.Why);
        Assert.Contains("frame rate", q.Pending!.Value.Message);
    }

    [Fact]
    public void AFrameBudgetMetExactlyIsNotSlow()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High, new AutoQuality.Policy { TargetFrameSec = 1.0 / 72.0 });
        for (int i = 0; i < 2000; i++) q.Sample(1.0 / 72.0, 0);
        Assert.Equal(GraphicsQuality.Tier.High, q.Effective);
    }

    // ---- requirement 2: hysteresis and recovery -----------------------------------------------------------------

    [Fact]
    public void OneBadSecondCostsOneStepAndNoMore()
    {
        // Settling: the counters still hold the old tier's capped frames right after a downgrade, so without the
        // blackout a single bad second walks the tier to Minimum in three windows.
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        for (int i = 0; i < 30; i++) q.Sample(1.0 / 30.0, 1, 0.02);    // 1 s of nothing but capped frames
        Assert.Equal(GraphicsQuality.Tier.Balanced, q.Effective);

        // Four seconds of outright collapse is still only two steps, not four.
        var r = new AutoQuality(GraphicsQuality.Tier.High);
        for (int i = 0; i < 120; i++) r.Sample(1.0 / 30.0, 1, 0.02);
        Assert.Equal(GraphicsQuality.Tier.Low, r.Effective);
    }

    [Fact]
    public void ItRecoversTowardTheChosenTierWhenConditionsImprove()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        while (q.Effective != GraphicsQuality.Tier.Low) q.Sample(1.0 / 30.0, 1, 0.02);
        Assert.True(q.Downgraded);

        for (int i = 0; i < 100000 && q.Effective != GraphicsQuality.Tier.High; i++) q.Sample(1.0 / 144.0, 0);
        Assert.Equal(GraphicsQuality.Tier.High, q.Effective);
        Assert.False(q.Downgraded);
    }

    [Fact]
    public void RecoveryWaitsAndDoesNotOscillateOnceAFrame()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        while (q.Effective != GraphicsQuality.Tier.Balanced) q.Sample(1.0 / 30.0, 1, 0.02);

        // Perfect frames, but not yet the recovery delay: still down.
        double clean = 0;
        while (clean < 15.0) { q.Sample(1.0 / 120.0, 0); clean += 1.0 / 120.0; }
        Assert.Equal(GraphicsQuality.Tier.Balanced, q.Effective);

        while (clean < 30.0) { q.Sample(1.0 / 120.0, 0); clean += 1.0 / 120.0; }
        Assert.Equal(GraphicsQuality.Tier.High, q.Effective);
    }

    [Fact]
    public void ATierThatFailedTwiceIsNotRetriedAsSoon()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        while (q.Effective != GraphicsQuality.Tier.Balanced) q.Sample(1.0 / 30.0, 1, 0.02);
        double first = q.RecoveryDelaySec(GraphicsQuality.Tier.High);
        Assert.Equal(20.0, first);

        // Climb back, fail again, and the wait doubles — this is the anti-oscillation guard.
        for (int i = 0; i < 200000 && q.Effective != GraphicsQuality.Tier.High; i++) q.Sample(1.0 / 144.0, 0);
        Assert.Equal(GraphicsQuality.Tier.High, q.Effective);
        while (q.Effective != GraphicsQuality.Tier.Balanced) q.Sample(1.0 / 30.0, 1, 0.02);
        double second = q.RecoveryDelaySec(GraphicsQuality.Tier.High);
        Assert.True(second > first, $"{second} should exceed {first}");
        Assert.True(second <= 300.0);
    }

    [Fact]
    public void ASustainedProblemDoesNotFlapTheTier()
    {
        // Bad enough to sit at Minimum, and it must STAY there rather than climb and fall for the whole flight.
        var q = new AutoQuality(GraphicsQuality.Tier.Balanced);
        int changes = 0;
        for (int i = 0; i < 60000; i++) if (q.Sample(1.0 / 20.0, 1, 0.03)) changes++;
        Assert.Equal(GraphicsQuality.Tier.Minimum, q.Effective);
        Assert.Equal(2, changes);   // Balanced -> Low -> Minimum, and then nothing for the next 50 minutes
    }

    // ---- requirement 3: the warning ------------------------------------------------------------------------------

    [Fact]
    public void TheWarningNamesWhatWasGivenUpAndWhy()
    {
        string m = GraphicsQuality.Warning(GraphicsQuality.Tier.High, GraphicsQuality.Tier.Balanced,
                                           GraphicsQuality.Reason.SimCapped, 0.42, 1.0 / 30.0);
        Assert.Contains("clouds", m, StringComparison.OrdinalIgnoreCase);   // the dearest thing given up, named first
        Assert.Contains("not keeping up", m);
        Assert.Contains("0.4 s", m);
        Assert.Contains("BALANCED", m);
        // A warning a pilot will actually read: never the whole table, however many knobs moved.
        Assert.True(m.Length < 230, $"{m.Length} chars: {m}");

        // And when the reflection is what went, the reflection is what it says.
        string r = GraphicsQuality.Warning(GraphicsQuality.Tier.Balanced, GraphicsQuality.Tier.Low,
                                           GraphicsQuality.Reason.SimCapped, 0.1, 1.0 / 30.0);
        Assert.Contains("reflect", r, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoWarningIsEverLongerThanAToast()
    {
        foreach (GraphicsQuality.Tier from in AllTiers)
            foreach (GraphicsQuality.Tier to in AllTiers)
                foreach (GraphicsQuality.Reason why in new[] { GraphicsQuality.Reason.SimCapped, GraphicsQuality.Reason.FrameRate })
                {
                    string m = GraphicsQuality.Warning(from, to, why, 0.42, 1.0 / 22.0);
                    Assert.True(m.Length < 230, $"{from}->{to} {why}, {m.Length} chars: {m}");
                    Assert.EndsWith(".", m);
                }
    }

    [Fact]
    public void AWarningThatIncludesStartupWorkSaysSo()
    {
        string m = GraphicsQuality.Warning(GraphicsQuality.Tier.High, GraphicsQuality.Tier.Balanced,
                                           GraphicsQuality.Reason.SimCapped, 0.4, 0.03);
        Assert.Contains("next flight", m);   // cloud lobes and scenery detail are built, not drawn, per flight
    }

    [Fact]
    public void EveryDowngradeHasSomethingToSay()
    {
        for (int i = 1; i < AllTiers.Length; i++)
        {
            var d = GraphicsQuality.Describe(AllTiers[i], AllTiers[i - 1]);
            Assert.NotEmpty(d.Lost);
            Assert.All(d.Lost, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        }
        Assert.Empty(GraphicsQuality.Describe(GraphicsQuality.Tier.Low, GraphicsQuality.Tier.Low).Lost);
    }

    [Fact]
    public void TheWarningIsDismissibleAndDoesNotNag()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        while (!q.Pending.HasValue) q.Sample(1.0 / 20.0, 1, 0.03);
        q.Dismiss();
        Assert.False(q.Pending.HasValue);

        // The next downgrade follows within the rate limit, so it does not raise a second sentence.
        int notices = 0;
        for (int i = 0; i < 1200; i++) { q.Sample(1.0 / 20.0, 1, 0.03); if (q.Pending.HasValue) { notices++; q.Dismiss(); } }
        Assert.True(notices <= 1, $"{notices} warnings in 60 s");
    }

    // ---- the pilot's own choice wins -----------------------------------------------------------------------------

    [Fact]
    public void MovingTheSliderClearsTheAutomaticFloorAndTheFailureMemory()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        while (q.Effective != GraphicsQuality.Tier.Minimum) q.Sample(1.0 / 20.0, 1, 0.03);

        q.SetChosen(GraphicsQuality.Tier.High);
        Assert.Equal(GraphicsQuality.Tier.High, q.Effective);   // they asked; they get it, and it re-proves itself
        Assert.False(q.Pending.HasValue);
        Assert.Equal(20.0, q.RecoveryDelaySec(GraphicsQuality.Tier.High));
    }

    [Fact]
    public void LoweringTheSliderBelowTheAutomaticFloorIsObeyed()
    {
        var q = new AutoQuality(GraphicsQuality.Tier.High);
        while (q.Effective != GraphicsQuality.Tier.Balanced) q.Sample(1.0 / 20.0, 1, 0.03);
        q.SetChosen(GraphicsQuality.Tier.Minimum);
        Assert.Equal(GraphicsQuality.Tier.Minimum, q.Effective);
        for (int i = 0; i < 100000; i++) q.Sample(1.0 / 144.0, 0);
        Assert.Equal(GraphicsQuality.Tier.Minimum, q.Effective);
    }

    // ---- platforms -----------------------------------------------------------------------------------------------

    [Fact]
    public void TheHeadsetIsJudgedAgainstItsDisplayAndAPhoneAgainstThirtyFps()
    {
        Assert.Equal(1.0 / 72.0, GraphicsQuality.TargetFrameSec(GraphicsQuality.Platform.Headset, 72), 9);
        Assert.Equal(1.0 / 90.0, GraphicsQuality.TargetFrameSec(GraphicsQuality.Platform.Headset, 90), 9);
        Assert.Equal(1.0 / 30.0, GraphicsQuality.TargetFrameSec(GraphicsQuality.Platform.Handheld, 60), 9);
        Assert.Equal(GraphicsQuality.Tier.Balanced, GraphicsQuality.DefaultFor(GraphicsQuality.Platform.Handheld));
        Assert.Equal(GraphicsQuality.Tier.Balanced, GraphicsQuality.DefaultFor(GraphicsQuality.Platform.Headset));
        Assert.Equal(GraphicsQuality.Tier.High, GraphicsQuality.DefaultFor(GraphicsQuality.Platform.Desktop));
    }

    [Fact]
    public void EveryTierHasADistinctLabel()
    {
        var labels = AllTiers.Select(GraphicsQuality.Label).ToArray();
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }
}
