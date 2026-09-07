using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using FlyingGame.Sim.Challenge;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>The challenge system must GRADE flight: a good pilot scores high, doing nothing scores
/// low, and the pass threshold discriminates. Uses the wings-level challenge with the trimmed glider
/// as a simple, deterministic autopilot (hold trim = wings level = high score).</summary>
public class ChallengeTests
{
    private static ChallengeDefinition Load(string id) =>
        ChallengeLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "challenges", id + ".json"));

    private static AircraftConfig Glider() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json"));

    [Fact]
    public void HoldingTrimScoresHigh()
    {
        var def = Load("a1c1-wings-level");
        var runner = new ChallengeRunner(def);
        var aircraft = runner.Spawn(Glider());
        var sim = new SimLoop(aircraft);
        // Do nothing but hold spawn trim (elevator only) — should stay wings-level on heading.
        for (int i = 0; i < 320 && !runner.Complete; i++)
        {
            sim.RunFor(0.1, aircraft.CurrentDeflections);
            runner.Tick(aircraft, 0.1);
        }
        Assert.True(runner.Complete, "Challenge should complete after its phase duration.");
        Assert.True(runner.Score > 80, $"Holding trim wings-level must score high; got {runner.Score:F0}.");
        Assert.True(runner.Passed, "Should pass the 80% threshold.");
    }

    [Fact]
    public void HardBankScoresLow()
    {
        var def = Load("a1c1-wings-level");
        var runner = new ChallengeRunner(def);
        var aircraft = runner.Spawn(Glider());
        var sim = new SimLoop(aircraft);
        // Full aileron the whole time — rolls off heading and bank, out of band.
        var rolling = new ControlInputs(1.0, 0, 0, 0);
        for (int i = 0; i < 320 && !runner.Complete; i++)
        {
            sim.RunFor(0.1, rolling);
            runner.Tick(aircraft, 0.1);
        }
        Assert.True(runner.Complete);
        Assert.False(runner.Passed, $"Rolling away must fail; scored {runner.Score:F0}.");
    }

    [Fact]
    public void AllChallengesLoadAndSpawn()
    {
        foreach (var id in new[] { "a1c1-wings-level", "a1c2-best-glide", "a1c3-cardinal-turn", "a1c4-stall-recover" })
        {
            var def = Load(id);
            var runner = new ChallengeRunner(def);
            var aircraft = runner.Spawn(Glider());
            Assert.False(double.IsNaN(aircraft.State.Velocity.X), $"{id} spawned NaN.");
            Assert.True(aircraft.State.Velocity.Length > 5, $"{id} spawned too slow.");
        }
    }
}
