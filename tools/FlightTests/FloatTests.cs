using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Super Cub on EDO 2000 floats and the DHC-2 Beaver on EDO 4930s: floats at rest at a sane draft, takes off from the water
/// under full power, and lands on the water without drama.</summary>
public class FloatTests
{
    private readonly ITestOutputHelper _out;
    public FloatTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }

    [Theory]
    [InlineData("pa18-floats-like")]
    [InlineData("dhc2-beaver-floats-like")]
    public void FloatsAtRestAtASaneDraft(string id)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load(id);
            // Drop it in: CG 1.3 m above the water, level, at rest.
            var state = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
            var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            for (int i = 0; i < 100; i++)
            {
                sim.RunFor(0.1, new ControlInputs(0, 0, 0, 1.0)); // idle
                Assert.False(double.IsNaN(ac.State.Position.Z), "NaN while floating");
            }
            RigidBodyState s = ac.State;
            double cgAbove = -s.Position.Z;
            _out.WriteLine($"settled: CG {cgAbove:F2} m above water, draft at step {FloatHydro.LastReport.DraftAtStepM:F2} m, pitch {Pitch(s):F1}°, roll {Roll(s):F1}°, buoyancy {FloatHydro.LastReport.BuoyancyN:F0} N vs weight {cfg.Mass.MassKg * 9.81:F0} N");
            Assert.InRange(FloatHydro.LastReport.DraftAtStepM, 0.12, 0.5);
            Assert.InRange(Pitch(s), -8, 8);
            Assert.InRange(Math.Abs(Roll(s)), 0, 3);
            Assert.InRange(FloatHydro.LastReport.BuoyancyN, cfg.Mass.MassKg * 9.81 * 0.9, cfg.Mass.MassKg * 9.81 * 1.1);
            Assert.True(s.Velocity.Length < 0.3, "should be at rest");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Theory]
    [InlineData("pa18-floats-like")]
    [InlineData("dhc2-beaver-floats-like")]
    public void TakesOffFromTheWaterUnderFullPower(string id)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load(id);
            var state = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
            var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
            var sim = new SimLoop(ac);
            sim.RunFor(4.0, new ControlInputs(0, 0, 0, 1.0)); // settle at idle
            double airborneAt = -1, maxPitch = 0;
            for (double t = 0; t < 50; t += 0.1)
            {
                // Full power; a little back stick (≈ 0.34 of the elevator travel) to get on the step, then relax as it accelerates.
                double v = ac.State.Velocity.Length;
                // Float technique: full back through the hump, then HOLD ~5° nose-up on the step (attitude
                // hold, as a pilot does — a fixed stick lets it porpoise off the step), fly it off.
                double pitchDeg = Pitch(ac.State), qRate = ac.State.Rates.Y;
                double target = v < 26 ? 5.0 : 9.0;   // hold ~5° on the step, rotate to ~9° to fly it off
                double stick = v < 7 ? -0.38 : Math.Clamp(-0.08 - 0.155 * (target - pitchDeg) + 0.39 * qRate, -0.9, 0.7);
                sim.RunFor(0.1, new ControlInputs(0, stick, 0, -1.0));
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                if ((int)(t * 10) % 20 == 0) _out.WriteLine($"t={t,4:F1} V={v,5:F1} agl={-s.Position.Z,5:F2} pitch={Pitch(s),5:F1} λ={FloatHydro.LastReport.WettedLambda:F2} CV={FloatHydro.LastReport.SpeedCoefficient:F2} lift={FloatHydro.LastReport.PlaningLiftN:F0} buoy={FloatHydro.LastReport.BuoyancyN:F0}");
                if (-s.Position.Z > 4.0 && FloatHydro.LastReport.BuoyancyN < 1 && FloatHydro.LastReport.PlaningLiftN < 1) { airborneAt = t; break; }
            }
            _out.WriteLine($"airborne at {airborneAt:F1} s, max |pitch| {maxPitch:F1}°");
            Assert.True(airborneAt > 0 && airborneAt < 45, $"did not get airborne (airborneAt={airborneAt})");
            Assert.True(maxPitch < 30, $"porpoised/pitched to {maxPitch:F0}°");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Theory]
    [InlineData("pa18-floats-like", 26.0)]          // ~50 kt full-flap glide
    [InlineData("dhc2-beaver-floats-like", 31.0)]   // ~60 kt (Beaver float approach ~65 mph)
    public void LandsOnTheWaterAndSlowsDown(string id, double approachMs)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load(id);
            // Power-off full-flap glide at 26 m/s (~50 kt): the aircraft holds this hands-off, so the approach is
            // the trim itself; the flare is a steady elevator ramp from 8 m (real technique: pull steadily, let it
            // settle on the step as the speed bleeds).
            double V = approachMs;
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, V, 40, flapFraction: 1.0, idleProp: true);   // power off: idling prop drag
            Assert.True(trim.Converged, "approach trim must converge");
            _out.WriteLine($"approach {V:F0} m/s full flap glide: α {trim.AlphaRad * 57.3:F1}° elev {trim.ElevatorRad * 57.3:F1}° γ {(trim.ThetaRad - trim.AlphaRad) * 57.3:F1}°");
            double half = trim.ThetaRad / 2.0;
            var state = new RigidBodyState(new Vec3(0, 0, -40.0), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
                new Vec3(V * Math.Cos(trim.AlphaRad), 0, V * Math.Sin(trim.AlphaRad)), Vec3.Zero);
            var ac = new Aircraft(cfg, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0, 1.0)) { FlapFraction = 1.0 };
            var sim = new SimLoop(ac);
            double stick = Aircraft.StickForDeflection(trim.ElevatorRad, cfg.Controls.Elevator);
            double touchdown = -1, maxG = 0, maxPitch = 0, sinkAtTd = 0, vAtTd = 0, flareStart = -1;
            for (double t = 0; t < 90; t += 0.1)
            {
                RigidBodyState before = ac.State;
                double agl = -ac.State.Position.Z;
                double qr = ac.State.Rates.Y;
                if (flareStart < 0 && agl < 16) flareStart = t;   // begin the flare at ~16 m on the full-flap idle glide
                // Steady pull, capped (the steeper full-flap glide since 2026-10-06 — the flap's lift carries its induced drag — starts
                // the flare a little higher).
                double pull = flareStart < 0 ? 0.0 : Math.Min(0.4, 0.11 * (t - flareStart));
                double st = Math.Clamp(stick - pull + 0.4 * qr, -0.85, 0.5);
                sim.RunFor(0.1, new ControlInputs(0, st, 0, 1.0)); // power off throughout
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                Vec3 vw = s.Attitude.Rotate(s.Velocity), vb = before.Attitude.Rotate(before.Velocity);
                maxG = Math.Max(maxG, (vw - vb).Length / 0.1 / 9.81);
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                if (touchdown < 0 && FloatHydro.LastReport.DraftAtStepM > 0.02) { touchdown = t; sinkAtTd = vb.Z; vAtTd = before.Velocity.Length; }
                if (touchdown > 0 && (t < touchdown + 1.0 || (System.Environment.GetEnvironmentVariable("FLOATTRACE") != null && Math.Abs(t * 2 - Math.Round(t * 2)) < 1e-6)))
                    _out.WriteLine($"  td+{t - touchdown:F1}: V={s.Velocity.Length:F1} pitch={Pitch(s):F1} vz={vw.Z:F2} draft={FloatHydro.LastReport.DraftAtStepM:F2} τ={FloatHydro.LastReport.TrimDeg:F1} λ={FloatHydro.LastReport.WettedLambda:F2} lift={FloatHydro.LastReport.PlaningLiftN:F0} buoy={FloatHydro.LastReport.BuoyancyN:F0} stick={st:F2} q={s.Rates.Y * 57.3:F0}°/s");
                if (touchdown > 0 && s.Velocity.Length < 3.0)
                {
                    _out.WriteLine($"touchdown {touchdown:F1} s at {vAtTd:F1} m/s / {sinkAtTd:F1} m/s sink, below 3 m/s at {t:F1} s, max {maxG:F1} g, max |pitch| {maxPitch:F0}°");
                    Assert.True(maxG < 4.5, $"max {maxG:F1} g");
                    // 32 deg: the test holds full back stick through the slow-down, so the Cub skips nose-high near 30 kt
                    // (29.6 deg with the old box floats; the realistic tapered floats, 2026-10-02, add ~0.6 deg).
                    Assert.True(maxPitch < 32, $"max pitch {maxPitch:F0}");
                    return;
                }
            }
            Assert.Fail($"did not slow down on the water (touchdown={touchdown}, maxG {maxG:F1}, maxPitch {maxPitch:F0})");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

}
