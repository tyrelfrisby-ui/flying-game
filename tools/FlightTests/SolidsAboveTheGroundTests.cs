using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// iOS ground-slowdown work (t256u, 2026-11-02): the clear-of-the-ground shortcut may skip the HEIGHT queries, never
/// the buildings. Before this, AirframeContact.Compute returned nothing above 60 m agl (and the per-step skip did the
/// same), so a tower or the arch could be flown through up there. A box standing far higher than 60 m above the sea
/// must stop the airframe at every altitude, by Compute alone and by a whole Aircraft step, with the per-step skip on.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class SolidsAboveTheGroundTests : IDisposable
{
    public void Dispose() { WorldTerrain.Active = null; AirframeContact.StageCullEnabled = true; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // Open sea far from the island: the ground under it is the sea bed, the water at 0 m.
    private const double X = 8544, Y = 2673;

    private static WorldSolids.Box AddTower()
    {
        double sea = Math.Max(WorldTerrain.GroundHeightAt(X, Y), FloatHydro.WaterSurfaceAt(X, Y) ?? double.NegativeInfinity);
        var box = new WorldSolids.Box(X + 30, Y, 20, 20, sea - 1, sea + 400);
        WorldSolids.Boxes.Add(box);
        return box;
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(250.0)]
    public void ComputePushesAHardPointOutOfATowerWellAboveTheGround(double agl)
    {
        WorldTerrain.Active = new WorldTerrain();
        var box = AddTower();
        try
        {
            var cfg = Load("pitts-s2b-like");
            var pts = AirframeContact.BuildPoints(cfg);
            double sea = box.Bottom + 1;
            // Nose 1 m inside the tower's west face, flying east into it.
            var st = new RigidBodyState(new Vec3(box.Cx - box.Hx - 0.2, Y, -(sea + agl)), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero);
            var (f, _) = AirframeContact.Compute(pts, cfg.Mass.CgVec(), st, null);
            Assert.True(f.Length > 1.0, $"no force from a tower at {agl} m agl: {f}");
            Assert.Contains(pts, p => p.Touching);
            var (fe, _) = AirframeContact.ComputeEveryPoint(pts, cfg.Mass.CgVec(), st, null);
            Assert.Equal(fe.X, f.X, 9); Assert.Equal(fe.Y, f.Y, 9); Assert.Equal(fe.Z, f.Z, 9);
        }
        finally { WorldSolids.Boxes.Remove(box); WorldTerrain.Active = null; }
    }

    [Fact]
    public void AnAeroplaneFlownIntoATowerAt200MStopsAgainstIt()
    {
        WorldTerrain.Active = new WorldTerrain();
        var box = AddTower();
        try
        {
            var cfg = Load("pitts-s2b-like");
            double sea = box.Bottom + 1;
            var st = new RigidBodyState(new Vec3(box.Cx - box.Hx - 15, Y, -(sea + 200)), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero);
            var ac = new Aircraft(cfg, st, ControlDeflections.Neutral);
            double hit = 0;
            ac.HardImpact += (v, _) => hit = Math.Max(hit, v);
            for (int i = 0; i < 200; i++) ac.Step(new ControlInputs(0, 0, 0, 1.0), 1.0 / 200.0);
            Assert.True(ac.State.Position.X < box.Cx, $"flew through the tower: x {ac.State.Position.X:F1}, face at {box.Cx - box.Hx:F1}");
            Assert.True(hit > 5, $"no impact recorded ({hit:F1} m/s)");
        }
        finally { WorldSolids.Boxes.Remove(box); WorldTerrain.Active = null; }
    }
}
