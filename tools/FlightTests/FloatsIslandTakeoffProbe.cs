using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (Ty 2026-11-05 08:58: "the cub acts like it cannot get on step"): the J-3 on floats spawned on the
/// island's own water exactly as FlightSimDriver does (IslandRunways.Water, keel 0.25 m in, hands off), then flown at full
/// power with four pilot techniques. Prints, and asserts the seaplane-technique run gets off in the book's time.</summary>
[Collection("WorldTerrainActive")]
public sealed class FloatsIslandTakeoffProbe : IDisposable
{
    private readonly ITestOutputHelper _o;
    public FloatsIslandTakeoffProbe(ITestOutputHelper o) { _o = o; }
    public void Dispose() => WorldTerrain.Active = null;

    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.2958; }

    [Theory]
    [InlineData("j3-cub-floats-like", "ease")]
    [InlineData("j3-cub-floats-like", "full-back")]
    [InlineData("j3-cub-floats-like", "neutral")]
    [InlineData("j3-cub-floats-like", "half-back")]
    public void IslandWaterTakeoff(string id, string technique)
    {
        WorldTerrain.Active = new WorldTerrain();
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        WorldTerrain.Airport ap = WorldTerrain.DefaultAirport;
        WorldTerrain.Lake lake = IslandRunways.Water(ap, 600) ?? throw new Exception("no water");
        var pos = new Vec3(lake.Cx - lake.Rx * 0.5, lake.Cy, -(lake.SurfaceM + cfg.Floats!.KeelZ - 0.25));
        var ac = new Aircraft(cfg, new RigidBodyState(pos, new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac); ac.CaptureForces = true;
        sim.RunFor(10, new ControlInputs(0, 0, 0, 1.0));
        _o.WriteLine($"{ap.Name} lake ({lake.Cx:F0},{lake.Cy:F0}) surface {lake.SurfaceM:F1} rx {lake.Rx:F0}; current {WaterCurrent.At(pos.X, pos.Y, 0)}");
        double peak = -99, integ = 0, airborneAt = -1; bool eased = false;
        for (double t = 0; t < 45; t += 0.05)
        {
            RigidBodyState s = ac.State;
            double v = s.Velocity.Length, p = Pitch(s), q = s.Rates.Y * 57.3, stick;
            switch (technique)
            {
                case "full-back": stick = -1; break;
                case "neutral": stick = 0; break;
                case "half-back": stick = -0.5; break;
                default:
                    if (!eased) { stick = -1; if (p > peak) peak = p; else if (peak > 6 && p < peak - 1) eased = true; }
                    else { double target = v < 18 ? 6.0 : 10.0; integ = Math.Clamp(integ + (target - p) * 0.05, -20, 20); stick = Math.Clamp(-0.15 - 0.10 * (target - p) - 0.03 * integ + 0.04 * q, -1, 1); }
                    break;
            }
            Vec3 v0 = ac.State.Velocity; Quat q0 = ac.State.Attitude;
            double yaw = Math.Atan2(2 * (s.Attitude.W * s.Attitude.Z + s.Attitude.X * s.Attitude.Y), 1 - 2 * (s.Attitude.Y * s.Attitude.Y + s.Attitude.Z * s.Attitude.Z)) * 57.2958;
            double roll = Math.Atan2(2 * (s.Attitude.W * s.Attitude.X + s.Attitude.Y * s.Attitude.Z), 1 - 2 * (s.Attitude.X * s.Attitude.X + s.Attitude.Y * s.Attitude.Y)) * 57.2958;
            double rud = Math.Clamp(-0.08 * yaw - 0.6 * s.Rates.Z, -1, 1), ail = Math.Clamp(-0.05 * roll - 0.3 * s.Rates.X, -1, 1);
            sim.RunFor(0.05, new ControlInputs(ail, stick, rud, -1.0));
            if ((int)Math.Round(t * 20) % 100 == 0)
            {
                RigidBodyState st = ac.State;
                Vec3 vW = st.Attitude.Rotate(st.Velocity); Vec3 aW = (vW - q0.Rotate(v0)) * (1 / 0.05);
                Vec3 u = new Vec3(vW.X, vW.Y, 0); u = u * (1 / Math.Max(1e-6, u.Length));
                var (fh, _) = FloatHydro.Compute(cfg, st, 0, ac.WaterRudderUp01);
                Vec3 cap = Vec3.Zero; foreach (var f in ac.LastForces) if (f.Kind != "gyro" && f.Kind != "moment") cap += f.ForceBody;
                Vec3 capW = st.Attitude.Rotate(cap);
                var wc = FlyingGame.Core.Wreck.HydroBody.Coefficients(ac.Hydro.Attached, cfg.Mass.CgVec(), st, lake.SurfaceM, false);
                Vec3 dB = new Vec3(-wc.Damping.X * st.Velocity.X, -wc.Damping.Y * st.Velocity.Y, -wc.Damping.Z * st.Velocity.Z);
                Vec3 dW = st.Attitude.Rotate(dB);
                double D(Vec3 a) => a.X * u.X + a.Y * u.Y;
                _o.WriteLine($"    AUDIT along track: m*a={ac.MassProperties.MassKg * D(aW):F0} floats={D(fh):F0} captured={D(capW):F0} airframeWaterDrag={D(dW):F0} residual={ac.MassProperties.MassKg * D(aW) - D(fh) - D(capW) - D(dW):F0}");
            }
            FloatHydro.Report r = FloatHydro.LastReport;
            if ((int)Math.Round(t * 20) % 20 == 0)
                _o.WriteLine($"t={t,4:F0} V={v * 2.237,5:F1}mph pitch={p,5:F1} trim={r.TrimDeg,5:F1} λ={r.WettedLambda,4:F1} vent={r.VentilatedFraction:F2} drag={r.DragN,6:F0} lift={r.PlaningLiftN,6:F0} buoy={r.BuoyancyN,6:F0} stern={FloatHydro.Floats[0].SternDraftM:F2} rpm={ac.EngineRpm:F0}");
            if ((int)Math.Round(t * 20) % 100 == 0)
            {
                Vec3 cgB = cfg.Mass.CgVec();
                foreach (var e in ac.Hydro.Attached)
                {
                    Vec3 w = ac.State.Position + ac.State.Attitude.Rotate(e.Centre - cgB);
                    double depth = lake.SurfaceM + w.Z, vh = e.VerticalHalf(ac.State.Attitude);
                    if (depth + vh > 0) _o.WriteLine($"    WET {e.Name} {e.Atom} centre={e.Centre} half={e.Half} depthCentre={depth:F2} vhalf={vh:F2} noDrag={e.NoDrag} buoyElse={e.BuoyancyElsewhere}");
                }
            }
            if ((int)Math.Round(t * 20) % 100 == 0)
                _o.WriteLine("    body-x by kind: " + string.Join(" ", ac.LastForces.GroupBy(f => f.Kind).Select(g => $"{g.Key}={g.Sum(f => f.ForceBody.X):F0}/{g.Sum(f => f.ForceBody.Z):F0}")));
            if (!FloatHydro.Floats[0].Wet && !FloatHydro.Floats[1].Wet && -ac.State.Position.Z - lake.SurfaceM > 1.6) { _o.WriteLine($"airborne at {t:F1}s V={v * 2.237:F1}mph"); airborneAt = t; break; }
        }
        // Seaplane technique (full back until the nose stops rising, then ~6° nose-up) on the island's own water, as the
        // game spawns it: airborne inside the J-3's book 15-25 s (HydroJ3Tests has the flat-water run's numbers).
        if (technique == "ease") Assert.InRange(airborneAt, 15, 25);
    }
}
