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
            if (c.Menu) { menus++; if (openedAt < 0) openedAt = i; }
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
}
