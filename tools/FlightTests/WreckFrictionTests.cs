using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04 6:31 PM PT: "after a crash the fuselage/cockpit wreck slides an unrealistically long way." A wreck at
/// 30 m/s must stop within these distances on each surface (literals from v²/(2μg) with the wreck table's μ worked by hand,
/// AirframeContact.SlidingMu: paved 0.60 → 76.5 m, gravel 0.75 → 61.2, dirt 0.80 → 57.3, grass 0.65 → 70.6, pasture 0.75 →
/// 61.2, snow 0.40 → 114.7, packed snow 0.30 → 152.9, ice 0.20 → 229.4; air drag only shortens it). Before the change the
/// same slide went 131 m on grass, 459 m on snow and 1,529 m on ice (the float-keel table).
/// </summary>
[Collection("WorldTerrainActive")]
public class WreckFrictionTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public WreckFrictionTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; CrashEvents.ClearForTests(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static double Horizontal(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>A detached piece (a 300 kg cabin-sized block) at 30 m/s on each surface.</summary>
    [Theory]
    [InlineData(WorldTerrain.Surface.Paved, 55.0, 77.0)]
    [InlineData(WorldTerrain.Surface.Gravel, 45.0, 62.0)]
    [InlineData(WorldTerrain.Surface.Dirt, 42.0, 58.0)]
    [InlineData(WorldTerrain.Surface.Grass, 52.0, 71.0)]
    [InlineData(WorldTerrain.Surface.Rough, 45.0, 62.0)]
    [InlineData(WorldTerrain.Surface.Snow, 85.0, 116.0)]
    [InlineData(WorldTerrain.Surface.PackedGlacierSnow, 110.0, 154.0)]
    [InlineData(WorldTerrain.Surface.GlacierIce, 165.0, 230.0)]
    public void AWreckPieceAtThirtyMetresASecondStopsInARealisticDistance(WorldTerrain.Surface surface, double minM, double maxM)
    {
        var w = new WreckSim(new FlatWreckWorld(null, 0.0) { Surface = surface });
        var e = new HydroElement("block", WreckAtom.Cabin, Vec3.Zero, new Vec3(0.8, 0.5, 0.4), 0.6, 300, 0.6);
        WreckPiece p = w.Add("block", AirframeComponent.Cabin, new List<HydroElement> { e }, Vec3.Zero,
            new RigidBodyState(new Vec3(0, 0, -0.41), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero));
        Vec3 p0 = p.State.Position;
        for (int i = 0; i < 20000 && !(p.Phase == WreckPhase.OnLand && p.WorldVelocity.Length < 0.05); i++) w.StepOnce();
        double d = Horizontal(p.State.Position, p0);
        _o.WriteLine($"{surface}: piece slid {d:F1} m");
        Assert.InRange(d, minM, maxM);
    }

    /// <summary>The fuselage itself (a Pitts with wings, gear and prop gone) put down level at 30 m/s: the hard points
    /// slide on the same table.</summary>
    [Theory]
    [InlineData(WorldTerrain.Surface.Paved, 55.0, 77.0)]
    [InlineData(WorldTerrain.Surface.Grass, 52.0, 71.0)]
    [InlineData(WorldTerrain.Surface.Snow, 85.0, 116.0)]
    [InlineData(WorldTerrain.Surface.GlacierIce, 165.0, 230.0)]
    public void TheFuselageWreckAtThirtyMetresASecondStopsInARealisticDistance(WorldTerrain.Surface surface, double minM, double maxM)
    {
        WorldTerrain.SurfaceOverride = surface;
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -50), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        foreach (var comp in new[] { AirframeComponent.WingLeft, AirframeComponent.WingRight, AirframeComponent.GearLeft, AirframeComponent.GearRight, AirframeComponent.GearTail, AirframeComponent.Propeller })
            ac.LoseComponent(comp);
        double low = double.NegativeInfinity; Vec3 cg = c.Mass.CgVec();
        foreach (ContactPoint pt in ac.ContactPoints) if (!pt.PropDisc) low = Math.Max(low, pt.Body.Z - cg.Z);
        ac.State = new RigidBodyState(new Vec3(0, 0, -(low + 0.02)), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero);
        Vec3 p0 = ac.State.Position; double t = 0;
        while (t < 60)
        {
            ac.Step(new ControlInputs(0, 0, 0, 1), 0.005); t += 0.005;
            Vec3 v = ac.State.Attitude.Rotate(ac.State.Velocity);
            if (Math.Sqrt(v.X * v.X + v.Y * v.Y) < 0.3) break;
        }
        double d = Horizontal(ac.State.Position, p0);
        _o.WriteLine($"{surface}: fuselage slid {d:F1} m in {t:F1} s");
        Assert.InRange(d, minM, maxM);
    }
}
