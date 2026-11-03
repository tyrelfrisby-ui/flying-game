using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The physics spray (Ty 2026-11-02: "make it purely physics based"): emitted on the hull's waterline, jets within
/// the spray-root geometry, droplets that fall like droplets.</summary>
[Collection("WorldTerrainActive")]
public class SprayFieldTests
{
    private readonly ITestOutputHelper _out;
    public SprayFieldTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ATwoMillimetreDropFallsAtItsMeasuredTerminalSpeed()
    {
        // Gunn & Kinzer 1949: 2.0 mm drop, 6.49 m/s in still air at sea level.
        Vec3 v = Vec3.Zero;
        for (int i = 0; i < 4000; i++) v += SprayField.DropAcceleration(v, Vec3.Zero, 0.002) * 0.002;
        _out.WriteLine($"2 mm terminal speed {v.Z:F2} m/s");
        Assert.InRange(v.Z, 6.0, 7.0);
    }

    [Fact]
    public void SprayLeavesTheWaterlineAtTheSprayRootSpeed()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));
            // Planing at 13 m/s (29 mph), 6° nose-up, the step 0.1 m in: the CG 1.33 m above the water.
            double th = 6 / 57.2958, v = 13.0;
            var s = new RigidBodyState(new Vec3(0, 0, -1.33), new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2)), new Vec3(v * Math.Cos(th), 0, v * Math.Sin(th)), Vec3.Zero);
            FloatHydro.Compute(cfg, s, 0);
            var roots = new FloatHydro.SprayRoot[256];
            int n = FloatHydro.SprayRoots(cfg, s, roots);
            Assert.True(n > 4, $"only {n} spray roots");
            double flux = 0;
            for (int i = 0; i < n; i++)
            {
                FloatHydro.SprayRoot r = roots[i];
                flux += r.MassFlux;
                // on the waterline (the pile-up lifts the root at most a few cm above the undisturbed surface)
                Assert.InRange(-r.Position.Z, -0.05, 0.12);   // (the fluted V: the facet line is a chord of the drawn flute)
                // earth-frame jet: 2 V sin α ≤ 2 V; and it leaves upward or along the surface, never down into the water
                Assert.True(r.JetVelocity.Length <= 2 * v + 0.5, $"jet {r.JetVelocity.Length:F1} m/s");
                Assert.True(r.JetVelocity.Z <= 0.5, $"jet heading down at {r.JetVelocity.Z:F2} m/s");
            }
            _out.WriteLine($"{n} roots, {flux:F1} kg/s of spray at 29 mph");
            Assert.InRange(flux, 0.1, 200);
            // flown: every parcel ends on the water, none goes under it or flies for ever
            var f = new SprayField();
            for (int k = 0; k < 60; k++) f.Step(cfg, s, 1 / 60.0);
            int alive = 0; foreach (SprayField.Parcel p in f.Parcels) if (p.Alive) { alive++; Assert.True(-p.Pos.Z > -0.1, "a parcel under the water"); }
            Assert.InRange(alive, 1, SprayField.MaxParcels);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
