using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The HUD rule and the "press MENU" tip for the Quest cockpit (owner Ty, 2026-11-02: "the hud display is not
/// necessary and really confusing while in the airplane and i couldn't figure out how to get to options"), and the
/// held left stick click that opens the menu when ☰ is not found.
/// </summary>
public sealed class QuestCockpitTests
{
    private const double Dt = 1.0 / 72.0;

    [Fact]
    public void InAHeadsetTheCockpitHasNoHudUnlessAskedFor()
    {
        Assert.False(QuestCockpit.ShowHud(headset: true, inCockpit: true, hudInCockpitOption: false));
        Assert.True(QuestCockpit.ShowHud(headset: true, inCockpit: true, hudInCockpitOption: true));
    }

    [Fact]
    public void OutsideViewsAndFlatScreensAreUnchanged()
    {
        Assert.True(QuestCockpit.ShowHud(headset: true, inCockpit: false, hudInCockpitOption: false));
        Assert.True(QuestCockpit.ShowHud(headset: false, inCockpit: true, hudInCockpitOption: false));
        Assert.True(QuestCockpit.ShowHud(headset: false, inCockpit: false, hudInCockpitOption: false));
    }

    // ---- fly by hand (Ty 2026-11-03: "the user selects fly by hand and the body shows up") ----

    [Fact]
    public void FlyByHandIsOffUntilThePilotChoosesIt()
    {
        Assert.False(QuestCockpit.FlyByHandDefault);
        Assert.False(QuestCockpit.HandsInCockpit(headset: true, inCockpit: true, flyByHand: QuestCockpit.FlyByHandDefault));
    }

    [Fact]
    public void FlyByHandOnPutsTheHandsInTheHeadsetCockpitOnly()
    {
        Assert.True(QuestCockpit.HandsInCockpit(headset: true, inCockpit: true, flyByHand: true));
        Assert.False(QuestCockpit.HandsInCockpit(headset: true, inCockpit: false, flyByHand: true));   // outside view
        Assert.False(QuestCockpit.HandsInCockpit(headset: false, inCockpit: true, flyByHand: true));   // phone / flat screen
        Assert.False(QuestCockpit.HandsInCockpit(headset: false, inCockpit: false, flyByHand: true));
    }

    [Fact]
    public void FlyByHandOffTakesTheHandsAwayEverywhere()
    {
        foreach (bool headset in new[] { false, true })
            foreach (bool inCockpit in new[] { false, true })
                Assert.False(QuestCockpit.HandsInCockpit(headset, inCockpit, flyByHand: false));
    }

    [Fact]
    public void TheTipShowsOnTheFirstThreeFlightsUntilTheMenuIsFound()
    {
        Assert.True(QuestCockpit.ShowTip(0, menuFound: false));
        Assert.True(QuestCockpit.ShowTip(2, menuFound: false));
        Assert.False(QuestCockpit.ShowTip(3, menuFound: false));
        Assert.False(QuestCockpit.ShowTip(0, menuFound: true));
    }

    [Fact]
    public void TheTipStaysTwelveSecondsAndFadesOverTheLastTwo()
    {
        Assert.Equal(1.0, QuestCockpit.TipOpacity(0.0), 9);
        Assert.Equal(1.0, QuestCockpit.TipOpacity(10.0), 9);
        Assert.Equal(0.5, QuestCockpit.TipOpacity(11.0), 9);
        Assert.Equal(0.0, QuestCockpit.TipOpacity(12.0), 9);
        Assert.Equal(0.0, QuestCockpit.TipOpacity(-1.0), 9);
    }

    // ---- the menu, from the controllers ----

    [Fact]
    public void TheMenuButtonOpensTheMenuOnThePress()
    {
        var m = new QuestControlMapper();
        Assert.True(m.Step(new QuestControllerFrame { Menu = true }, Dt, 0.5).Menu);
        Assert.False(m.Step(new QuestControllerFrame { Menu = true }, Dt, 0.5).Menu);   // held: once
    }

    [Fact]
    public void HoldingTheLeftStickClickOneSecondOpensTheMenuOnce()
    {
        var m = new QuestControlMapper();
        var held = new QuestControllerFrame { LeftStickClick = true };
        var first = m.Step(held, Dt, 0.5);
        Assert.True(first.Recentre);          // the click still recentres at once
        Assert.False(first.Menu);
        int menus = 0, frames = (int)(1.5 / Dt);
        int openedAt = -1;
        for (int i = 1; i < frames; i++)
        {
            var c = m.Step(held, Dt, 0.5);
            Assert.False(c.Recentre);         // a hold is one recentre, not seventy
            if (c.Menu) { menus++; if (openedAt < 0) openedAt = i; Assert.Equal(QuestMenuTrigger.StickClickHeld, c.MenuBy); }
        }
        Assert.Equal(1, menus);
        Assert.InRange(openedAt * Dt, 0.95, 1.05);
    }

    [Fact]
    public void AShortStickClickIsOnlyARecentre()
    {
        var m = new QuestControlMapper();
        for (int i = 0; i < (int)(0.5 / Dt); i++) Assert.False(m.Step(new QuestControllerFrame { LeftStickClick = true }, Dt, 0.5).Menu);
        Assert.False(m.Step(new QuestControllerFrame(), Dt, 0.5).Menu);
        // Released and pressed again: the count starts over.
        for (int i = 0; i < (int)(0.5 / Dt); i++) Assert.False(m.Step(new QuestControllerFrame { LeftStickClick = true }, Dt, 0.5).Menu);
    }

    // ---- the MENU tab (build a222bda: the menu could not be opened in flight at all) ----

    private static QuestMenuTrigger Run(QuestMenuTab t, int frames, bool poke = false, bool ray = false, double trig = 0.0)
    {
        var got = QuestMenuTrigger.None;
        for (int i = 0; i < frames; i++)
        {
            var r = t.Step(Dt, poke, ray, trig);
            if (r != QuestMenuTrigger.None)
            {
                Assert.Equal(QuestMenuTrigger.None, got);   // never twice in one gesture
                got = r;
            }
        }
        return got;
    }

    [Fact]
    public void APokeFromOutsideHeldAFifthOfASecondOpensTheMenuOnce()
    {
        var t = new QuestMenuTab();
        Assert.Equal(QuestMenuTrigger.None, Run(t, 10));                       // hand elsewhere
        Assert.Equal(QuestMenuTrigger.None, Run(t, 10, poke: true));           // 0.14 s: not yet
        Assert.Equal(QuestMenuTrigger.TabPoked, Run(t, 10, poke: true));       // 0.28 s: once
        Assert.Equal(QuestMenuTrigger.None, Run(t, 200, poke: true));          // staying there does not fire again
        Assert.Equal(QuestMenuTrigger.None, Run(t, 1));                        // out ...
        Assert.Equal(QuestMenuTrigger.TabPoked, Run(t, 20, poke: true));       // ... and in again: a new poke
    }

    [Fact]
    public void AHandAlreadyInsideWhenTheTabAppearsDoesNothingUntilItLeaves()
    {
        var t = new QuestMenuTab();
        t.Reset();
        Assert.Equal(QuestMenuTrigger.None, Run(t, 300, poke: true));
        Assert.Equal(QuestMenuTrigger.None, Run(t, 1));
        Assert.Equal(QuestMenuTrigger.TabPoked, Run(t, 20, poke: true));
    }

    [Fact]
    public void ABrushThroughTheTabDoesNothing()
    {
        var t = new QuestMenuTab();
        Run(t, 1);
        for (int k = 0; k < 20; k++)
        {
            Assert.Equal(QuestMenuTrigger.None, Run(t, 12, poke: true));       // 0.17 s inside
            Assert.Equal(QuestMenuTrigger.None, Run(t, 2));
        }
    }

    [Fact]
    public void PointingNeedsAHoverThenAFreshTriggerPull()
    {
        var t = new QuestMenuTab();
        Assert.Equal(QuestMenuTrigger.None, Run(t, 10, ray: true));                         // hovering 0.14 s
        Assert.Equal(QuestMenuTrigger.None, Run(t, 2, ray: true, trig: 1.0));               // pulled too soon
        Assert.Equal(QuestMenuTrigger.None, Run(t, 30, ray: true, trig: 1.0));              // still held: no repeat
        Assert.Equal(QuestMenuTrigger.None, Run(t, 5, ray: true));                          // let go
        Assert.Equal(QuestMenuTrigger.TabPointedTrigger, Run(t, 5, ray: true, trig: 0.9));  // a fresh pull, hovered 0.6 s
        Assert.True(t.Glow01 > 0.99);
    }

    [Fact]
    public void ATriggerHeldAsTheRaySweepsOntoTheTabDoesNotOpenIt()
    {
        // Braking on the runway with the left hand pointing down at the panel: the trigger was already down.
        var t = new QuestMenuTab();
        Assert.Equal(QuestMenuTrigger.None, Run(t, 20, trig: 1.0));
        Assert.Equal(QuestMenuTrigger.None, Run(t, 200, ray: true, trig: 1.0));
        Assert.Equal(QuestMenuTrigger.None, Run(t, 3, ray: true));
        Assert.Equal(QuestMenuTrigger.TabPointedTrigger, Run(t, 3, ray: true, trig: 1.0));
    }

    [Fact]
    public void ATriggerPulledWhileTheRayIsElsewhereNeverCounts()
    {
        var t = new QuestMenuTab();
        Assert.Equal(QuestMenuTrigger.None, Run(t, 30, ray: true));
        Assert.Equal(QuestMenuTrigger.None, Run(t, 1));                       // ray slips off
        Assert.Equal(QuestMenuTrigger.None, Run(t, 1, trig: 1.0));            // pulled off the tab
        Assert.Equal(QuestMenuTrigger.None, Run(t, 30, ray: true, trig: 1.0)); // back on, still held
    }
}
