using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Saying what just broke, and what it did to the pilot (Ty 2026-11-08). Phrasing and grouping are pure, so they can
/// be read here instead of off a phone.
/// </summary>
public class DamageReportTests
{
    // ---- one moment, one sentence -----------------------------------------------------------------

    [Fact]
    public void OneArrivalSaysEverythingItBrokeInOneBreath()
    {
        // "if I hit hard and bend both gear and strike my prop it would say all three then disappears"
        var g = new DamageGrouper();
        g.Add(0.00, DamageCause.GroundContact, "bent the left gear");
        g.Add(0.02, DamageCause.GroundContact, "bent the right gear");
        g.Add(0.05, DamageCause.GroundContact, "struck the propeller");
        Assert.False(g.TryTake(0.3, out _));                       // still gathering
        Assert.True(g.TryTake(1.0, out string line));
        Assert.Equal("Ground contact — bent the left gear, bent the right gear and struck the propeller", line);
    }

    [Fact]
    public void AlaterHitIsItsOwnMessage()
    {
        var g = new DamageGrouper();
        g.Add(0.0, DamageCause.GroundContact, "bent the left gear");
        Assert.True(g.TryTake(1.0, out string first));
        g.Add(40.0, DamageCause.Tree, "lost your right wing");
        Assert.True(g.TryTake(41.0, out string second));
        Assert.Equal("Ground contact — bent the left gear", first);
        Assert.Equal("Hit a tree — lost your right wing", second);
    }

    [Fact]
    public void APanelThatKeepsFoldingIsNotSaidTwice()
    {
        // A panel reports again every further 5° it bends; "bent your right wing" is true once.
        var g = new DamageGrouper();
        for (int i = 0; i < 6; i++) g.Add(i * 0.05, DamageCause.GroundContact, "bent your right wing");
        Assert.True(g.TryTake(2.0, out string line));
        Assert.Equal("Ground contact — bent your right wing", line);
    }

    [Fact]
    public void AStillUnfoldingCrashKeepsTheSameMessageOpen()
    {
        var g = new DamageGrouper();
        for (double t = 0; t < 3.0; t += 0.4) g.Add(t, DamageCause.GroundContact, $"bent part {t:F1}");
        Assert.True(g.Pending);
        Assert.False(g.TryTake(3.2, out _));            // the last event was 0.4 s ago: still going
        Assert.True(g.TryTake(4.0, out string line));
        Assert.StartsWith("Ground contact — ", line);
    }

    [Fact]
    public void TheHarderCauseNamesTheMoment()
    {
        var g = new DamageGrouper();
        g.Add(0.0, DamageCause.GroundContact, "bent the gear");
        g.Add(0.1, DamageCause.Tree, "tore the fabric");
        Assert.True(g.TryTake(1.0, out string line));
        Assert.StartsWith("Hit a tree — ", line);
    }

    [Fact]
    public void TheListReadsTheWayItWouldBeSaid()
    {
        Assert.Equal("one", DamageReport.JoinConditions(new[] { "one" }));
        Assert.Equal("one and two", DamageReport.JoinConditions(new[] { "one", "two" }));
        Assert.Equal("one, two and three", DamageReport.JoinConditions(new[] { "one", "two", "three" }));
    }

    // ---- the pilot --------------------------------------------------------------------------------

    [Fact]
    public void AnUnhurtPilotIsSaidSoAndNoNumbersAreInvented()
    {
        Assert.Equal("Pilot: unhurt.", FlightSummary.PilotLine(LandingOutcome.Unhurt, 0.2, 0.1, 0.05, 2, 1, 3, 0));
    }

    [Fact]
    public void TheInjuryNamesTheAxisAndTheAccelerationThatCausedIt()
    {
        // A heavy arrival: everything down the spine.
        string s = FlightSummary.PilotLine(LandingOutcome.Injured, 1.4, 1.05, 0.8, 3, 1, 16.4, 0);
        Assert.Contains("injured", s);
        Assert.Contains("16.4 g", s);
        Assert.Contains("down the spine", s);
        Assert.Contains("5", s);        // the published 5 % risk at the moderate limits
    }

    [Fact]
    public void TheRisksAreTheOnesNASAPublishes()
    {
        Assert.Equal(0.0, FlightSummary.InjuryRiskPct(0.9, 0.5, 0.3));
        Assert.Equal(FlightSummary.LowRiskInjuryPct, FlightSummary.InjuryRiskPct(1.0, 0.8, 0.5));
        Assert.Equal(FlightSummary.ModerateRiskInjuryPct, FlightSummary.InjuryRiskPct(1.6, 1.0, 0.9));
        Assert.Equal(FlightSummary.HighRiskInjuryPct, FlightSummary.InjuryRiskPct(2.0, 1.5, 1.0));
    }

    [Fact]
    public void ACrushedCockpitIsReportedAsTheCauseRatherThanTheLoads()
    {
        string s = FlightSummary.PilotLine(LandingOutcome.Dead, 0.3, 0.2, 0.1, 1, 1, 2, 0.55);
        Assert.Contains("killed", s);
        Assert.Contains("cockpit crushed", s);
        Assert.Contains("0.55", s);
    }

    [Fact]
    public void SidewaysAndForeAndAftAreNamedToo()
    {
        Assert.Contains("sideways", FlightSummary.PilotLine(LandingOutcome.Bruised, 1.0, 0.5, 0.3, 2, 20, 3, 0));
        Assert.Contains("chest-to-back", FlightSummary.PilotLine(LandingOutcome.Bruised, 1.0, 0.5, 0.3, 44, 2, 3, 0));
    }

    [Fact]
    public void AFlightThatBrokeNothingSaysSo()
    {
        Assert.Contains("No damage.", FlightSummary.Build("Back to the menu", new string[0], "Pilot: unhurt."));
    }
}
