using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
namespace FlyingGame.FlightTests;
/// <summary>
/// The remnant of a total break-up stays a stable rigid body (2026-11-03): GroundContactCullTests' C172 "crash" at
/// (5882, 4390) loses every part but the cabin. With an inertia floor of a tenth of the original the cabin was lighter in
/// inertia than its own box, the contact springs drove it from 6 to 387 m/s in one step, and the tree sweep then walked
/// cells from CellOf(NaN) for ever: the suite hung. The floor is now the remnant's own boxes (never more than it had), and
/// the sweep refuses a non-finite state.
/// </summary>
[Collection("WorldTerrainActive")]
public class WreckRemnantTests
{
    [Fact]
    public void ACabinLeftAloneComesToRestInsteadOfBlowingUp()
    {
        WorldTerrain.Active = new WorldTerrain();
        try
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));
            double x = 5882, y = 4390, surface = Math.Max(WorldTerrain.GroundHeightAt(x, y), FloatHydro.WaterSurfaceAt(x, y) ?? double.NegativeInfinity);
            var s0 = new RigidBodyState(new Vec3(x, y, -(surface + 40)), new Quat(Math.Sin(0.15), Math.Sin(-0.12), 0, Math.Cos(0.19)).Normalized(), new Vec3(45, 0, 8), new Vec3(0.3, 0, 0.1));
            var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
            double vmax = 0;
            for (int i = 0; i < 1300; i++) { ac.Step(new ControlInputs(0.4, 0.3, 0.2, 1.0), 0.005); vmax = Math.Max(vmax, ac.State.Velocity.Length); }
            Assert.True(vmax < 60, $"vmax {vmax:F1}");
        }
        finally { WorldTerrain.Active = null; }
    }
}
