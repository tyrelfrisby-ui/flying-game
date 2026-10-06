using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;
namespace FlyingGame.FlightTests;
[Collection("WorldTerrainActive")]
/// <summary>The game's rudder on a tailwheel rollout (owner 2026-10-06: "your pilot moves the rudder rather slowly on the stearman
/// landing — it ground looped"): it must catch the swing (a yaw-rate cascade, latched to the ground law after touchdown).
/// The Stearman in the GUSTY crosswind is left out: full rudder and the inside brake run out of authority at walking pace.</summary>
public class TailwheelRolloutTests
{
    private readonly ITestOutputHelper _o; public TailwheelRolloutTests(ITestOutputHelper o) { _o = o; }
    [Theory]
    [InlineData("stearman-pt17-like.json", PracticeKind.LandingAileron, PracticeWind.Steady)]
    [InlineData("stearman-pt17-like.json", PracticeKind.LandingRudder, PracticeWind.Steady)]
    [InlineData("pa18-cub-like.json", PracticeKind.LandingAileron, PracticeWind.Gusty)]
    public void TheGameKeepsATailwheelStraightOnTheRollout(string file, PracticeKind kind, PracticeWind wind)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", file)); WorldTerrain.Active = null;
        var sc = new PracticeScenario(kind, wind, c, PracticeScenarioTests.TestRunway(), 0.0);
        var ac = sc.Spawn(); var sim = new SimLoop(ac);
        ControlInputs user = ControlInputs.Neutral; double maxAlign = 0, maxR = 0, tTd = -1;
        for (double t = 0; t < 200 && sc.Phase != PracticePhase.Finished; t += 0.02)
        {
            var inputs = sc.Step(ac, user, 0.02);
            ac.BrakeInput = sc.GameBrake; ac.BrakeBias = sc.GameBrakeBias;
            sim.RunFor(0.02, inputs);
            user = sc.Autopilot;
            if (sc.TouchedDown && tTd < 0) tTd = t;
            if (sc.TouchedDown) { maxAlign = Math.Max(maxAlign, Math.Abs(sc.AlignmentDeg)); maxR = Math.Max(maxR, Math.Abs(ac.State.Rates.Z) * 57.3); }
        }
        _o.WriteLine($"{file} {kind} {wind}: {sc.Phase} [{sc.EndReason}] after touchdown max misalignment {maxAlign:F0}°, max yaw rate {maxR:F0}°/s, verdict {sc.Verdict}");
        Assert.True(sc.TouchedDown);
        Assert.True(maxAlign < 20, $"swung {maxAlign:F0}° on the rollout");
        Assert.NotEqual("Ground loop.", sc.Verdict);
    }
}
