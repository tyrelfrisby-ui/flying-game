using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Hughes H-4 Hercules (owner 2026-10-02): a flying-boat HULL on the centreline + wingtip floats. Floats at a sane
/// draft with a tip float catching the heel, and gets off the water inside the harbor (its ~2.2 km run doesn't fit the
/// field's 1 km lakes — the real one operated from Long Beach harbor).</summary>
public class FlyingBoatTests
{
    private readonly ITestOutputHelper _out;
    public FlyingBoatTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "hughes-h4-like.json"));
    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }

    [Fact]
    public void FloatsOnItsHullAndATipFloatCatchesTheHeel()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            double r = 4 * Math.PI / 180 / 2;   // dropped in with 4 deg of heel
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(Math.Sin(r), 0, 0, Math.Cos(r)), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            for (int i = 0; i < 300; i++) { sim.RunFor(0.1, new ControlInputs(0, 0, 0, 1.0)); Assert.False(double.IsNaN(ac.State.Position.Z)); }
            RigidBodyState s = ac.State;
            _out.WriteLine($"settled: CG {-s.Position.Z:F2} m above water, hull draft at step {FloatHydro.Floats[0].StepDraftM:F2} m, pitch {Pitch(s):F1}°, roll {Roll(s):F1}°, tip floats wet L {FloatHydro.Floats[2].Wet} R {FloatHydro.Floats[3].Wet}, speed {s.Velocity.Length:F2}");
            Assert.InRange(FloatHydro.Floats[0].StepDraftM, 0.5, 3.5);
            Assert.InRange(Pitch(s), -6, 8);
            Assert.InRange(Math.Abs(Roll(s)), 0, 6);            // resting on one tip float, not capsized
            Assert.True(s.Velocity.Length < 0.5, "should come to rest");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Fact]
    public void TakesOffInsideTheLake()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            sim.RunFor(10.0, new ControlInputs(0, 0, 0, 1.0));   // settle at idle
            double x0 = ac.State.Position.X, airborneAt = -1, dist = 0, maxPitch = 0, keelAgl0 = -ac.State.Position.Z;
            for (double t = 0; t < 120; t += 0.1)
            {
                double v = ac.State.Velocity.Length, pitchDeg = Pitch(ac.State), q = ac.State.Rates.Y;
                (double roll, _, _) = FormationPilot.Euler(ac.State.Attitude);
                double target = v < 30 ? 5.0 : 7.0;                    // hold the step attitude, then fly it off
                double stick = Math.Clamp(-0.1 - 0.15 * (target - pitchDeg) + 0.8 * q, -0.9, 0.7);
                double ail = Math.Clamp(-roll * 3.0 - ac.State.Rates.X * 1.0, -1, 1);
                sim.RunFor(0.1, new ControlInputs(ail, stick, 0, -1.0));
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                dist = s.Position.X - x0;
                if ((int)(t * 10) % 50 == 0) _out.WriteLine($"t={t,5:F1} V={v,5:F1} m/s ({v * 1.944,4:F0} kt) dist {dist,6:F0} m  height {-s.Position.Z,5:F1}  pitch {Pitch(s),5:F1}  hull lift {FloatHydro.Floats[0].PlaningLiftN / 1000,6:F0} kN buoy {FloatHydro.Floats[0].BuoyancyN / 1000,6:F0} kN");
                if (-s.Position.Z > keelAgl0 + 10 && !FloatHydro.Floats[0].Wet) { airborneAt = t; break; }
            }
            _out.WriteLine($"airborne at {airborneAt:F0} s after {dist:F0} m of water, max |pitch| {maxPitch:F1}°");
            Assert.True(airborneAt > 0, "did not get airborne");
            double harbor = 2 * WorldTerrain.Harbor.Rx * 0.85;   // spawned 85 % of the way back from the centre
            Assert.True(dist < harbor, $"needs {dist:F0} m — longer than the {harbor:F0} m of harbor ahead");
            Assert.True(maxPitch < 20, $"porpoised/pitched to {maxPitch:F0}°");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
