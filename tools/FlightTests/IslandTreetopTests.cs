using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

[Collection("WorldTerrainActive")]
/// <summary>
/// Ty's report on the island's own forest (2026-11-03: "i flew into the tree tops at high speed and it just broke apart and
/// stopped"). The Pitts at 60 m/s through the timber 1.5 m and 4 m below the tops of a stand of 42 m firs. On the base
/// (d9a3eee8) each of these went 60 → 5.5 m/s in 0.1 s against a neighbour's crown cylinder 20 m short of the tree aimed at
/// and lost nose, cabin and tail boom. Now the tops snap and the branches drag: it keeps its speed and its fuselage.
/// </summary>
public class IslandTreetopTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public IslandTreetopTests(ITestOutputHelper o) { _o = o; }
    public void Dispose() { Forest.ClearBroken(); WorldTerrain.Active = null; WorldSolids.Boxes.Clear(); WorldSolids.Shapes.Clear(); }

    [Theory]
    [InlineData(1.5, 2.5)]
    [InlineData(4.0, 2.5)]
    [InlineData(1.5, 0.0)]
    public void TheTreetopsDoNotStopThePitts(double belowTop, double lateral)
    {
        WorldTerrain.Active = new WorldTerrain();
        Forest.RegisterSolids(); Forest.ClearBroken();
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
        Forest.Tree tr = Forest.InRect(-1500, -5200, 1500, -4000).Where(t => t.HeightM > 25).First();
        _o.WriteLine($"tree H {tr.HeightM:0.0} r {tr.RadiusM:0.00} crown {tr.CrownRadiusM:0.00}");
        var pos = new Vec3(tr.X - 40, tr.Y - lateral, -(tr.TopM - belowTop));
        var ac = new Aircraft(c, new RigidBodyState(pos, Quat.Identity, new Vec3(60, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        for (int i = 0; i < 75; i++)
        {
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, -1));
            if (i % 5 == 0) _o.WriteLine($"t {i * 0.02:0.00} x-tr {ac.State.Position.X - tr.X:0.0} up-top {-ac.State.Position.Z - tr.TopM:0.00} v {ac.State.Velocity.Length:0.0} lost [{string.Join(",", ac.LostComponents)}] strikes {ac.TreeStrikeLog.Count}");
        }
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine(r.ToString());
        Assert.NotEmpty(ac.TreeStrikeLog);
        Assert.False(ac.IsLost(AirframeComponent.Cabin));
        Assert.False(ac.IsLost(AirframeComponent.Nose));
        Assert.True(ac.State.Velocity.Length > 55.0, $"{ac.State.Velocity.Length:0.0} m/s");
    }
}
