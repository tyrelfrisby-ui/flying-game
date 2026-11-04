using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// What is on screen once the pilot leaves the aeroplane (Ty 2026-11-04, Great Lakes bail-out on the Quest: "the hud
/// imagery showed up which should not happen" and "there was no corny comment when i got to the ground in my
/// parachute ... make sure it is in the quest version as well").
/// </summary>
public class PilotOutRulesTests
{
    [Fact]
    public void TheHudIsOnlyDrawnWhileThePilotIsAboard()
    {
        Assert.True(PilotOutRules.ShowHud(PilotOutRules.Stage.Aboard));
        Assert.False(PilotOutRules.ShowHud(PilotOutRules.Stage.Egressing));
        Assert.False(PilotOutRules.ShowHud(PilotOutRules.Stage.FreeFall));
        Assert.False(PilotOutRules.ShowHud(PilotOutRules.Stage.UnderCanopy));
        Assert.False(PilotOutRules.ShowHud(PilotOutRules.Stage.Landed));
    }

    [Fact]
    public void EveryStageButAboardHidesTheHud()
    {
        // A stage added later must decide; it does not get the HUD by default.
        foreach (PilotOutRules.Stage s in System.Enum.GetValues<PilotOutRules.Stage>())
            Assert.Equal(s == PilotOutRules.Stage.Aboard, PilotOutRules.ShowHud(s));
    }

    [Fact]
    public void TheRemarkIsUpOnlyOnceHeIsDownAndHasSaidIt()
    {
        const string line = "Any landing you can walk away from is a good one.";
        Assert.True(PilotOutRules.ShowRemark(PilotOutRules.Stage.Landed, spoken: true, line));
        Assert.False(PilotOutRules.ShowRemark(PilotOutRules.Stage.Landed, spoken: false, line));   // still catching his breath
        Assert.False(PilotOutRules.ShowRemark(PilotOutRules.Stage.Landed, spoken: true, ""));
        Assert.False(PilotOutRules.ShowRemark(PilotOutRules.Stage.UnderCanopy, spoken: true, line));
        Assert.False(PilotOutRules.ShowRemark(PilotOutRules.Stage.Aboard, spoken: true, line));
    }

    [Fact]
    public void TheRemarkIsNotHudSoItSurvivesTheHudGoing()
    {
        // The stage that shows the remark is one that hides the HUD.
        Assert.True(PilotOutRules.ShowRemark(PilotOutRules.Stage.Landed, true, "Nailed it."));
        Assert.False(PilotOutRules.ShowHud(PilotOutRules.Stage.Landed));
    }

    [Fact]
    public void OnlyAHeadsetGetsTheRemarkInTheWorld()
    {
        Assert.True(PilotOutRules.RemarkInWorld(flatOverlayVisible: false));    // Quest: OnGUI never reaches the eyes
        Assert.False(PilotOutRules.RemarkInWorld(flatOverlayVisible: true));    // iOS/Mac: the status line has it already
    }

    [Fact]
    public void TheWorldTextIsTheLabelThenTheQuoteWrapped()
    {
        string t = PilotOutRules.RemarkText("WALKED AWAY", "Any landing you can walk away from is a good one.");
        Assert.Equal("WALKED AWAY\n“Any landing you can walk away\nfrom is a good one.”", t);
        foreach (string l in t.Split('\n')) Assert.True(l.Length <= PilotOutRules.RemarkWrapChars, l);
    }

    [Fact]
    public void AShortLineIsOneLineUnderTheLabel()
    {
        Assert.Equal("BRUISED\n“I meant to do that.”", PilotOutRules.RemarkText("BRUISED", "I meant to do that."));
    }
}
