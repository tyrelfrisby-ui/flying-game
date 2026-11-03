using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The J-3 on its Edo 60-1320s on the water (Ty 2026-11-02: the J-3 on floats goes into the headset, drawn as our own
/// GLB). FloatTests covers the Super Cub and the Beaver; nothing flew the J-3's own float block. At rest at a sane
/// draft with the DRAWN floats in the water and their decks dry, a displacement taxi that turns on the water rudder,
/// a takeoff run that reaches liftoff, and a water landing that neither breaks it nor sinks it.
///
/// The drawn float numbers are literals measured by tools/own_models/check_eye_interiors.py (the floats report, as
/// AirframeModels.Place draws them, 2026-11-02): at the step the drawn keel is 1.448 m and the deck 0.943 m below the
/// CG. They are not read from the GLB here, so moving the model is a deliberate edit in both places.
/// </summary>
// Mutates the global FloatHydro.FlatWaterOverride (see FloatTests).
[Collection("WorldTerrainActive")]
public class J3FloatsWaterTests
{
    private const string Id = "j3-cub-floats-like";
    private const double DrawnKeelAtStepM = 1.448, DrawnDeckAtStepM = 0.943;

    private readonly ITestOutputHelper _out;
    public J3FloatsWaterTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", Id + ".json"));

    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }
    private static double Yaw(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.3; }

    /// <summary>Lever +1 = idle, -1 = full (the stick convention ControlInputs carries).</summary>
    private static double Lever(double throttle01) => 1.0 - 2.0 * throttle01;

    private static (Aircraft ac, SimLoop sim) Afloat(AircraftConfig cfg)
    {
        var state = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(10.0, new ControlInputs(0, 0, 0, Lever(0)));
        return (ac, sim);
    }

    [Fact]
    public void FloatsAtRestWithTheDrawnFloatsInTheWater()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            for (int i = 0; i < 50; i++)
            {
                sim.RunFor(0.1, new ControlInputs(0, 0, 0, Lever(0)));
                Assert.False(double.IsNaN(ac.State.Position.Z), "NaN while floating");
            }
            RigidBodyState s = ac.State;
            double cgAbove = -s.Position.Z, draft = FloatHydro.LastReport.DraftAtStepM;
            _out.WriteLine($"J-3 floats at rest: CG {cgAbove:F3} m above the water, physics draft at the step {draft:F3} m, "
                           + $"pitch {Pitch(s):F1}, roll {Roll(s):F1}, buoyancy {FloatHydro.LastReport.BuoyancyN:F0} N vs weight {cfg.Mass.MassKg * 9.81:F0} N; "
                           + $"drawn float: keel {DrawnKeelAtStepM - cgAbove:F3} m under, deck {cgAbove - DrawnDeckAtStepM:F3} m clear");
            // 9e3b5db7 sized the floats to float on 23.3 cm of a 36.5 cm float.
            Assert.InRange(draft, 0.18, 0.30);
            Assert.InRange(Pitch(s), -6, 6);
            Assert.InRange(Math.Abs(Roll(s)), 0, 2);
            Assert.InRange(FloatHydro.LastReport.BuoyancyN, cfg.Mass.MassKg * 9.81 * 0.9, cfg.Mass.MassKg * 9.81 * 1.1);
            Assert.True(s.Velocity.Length < 0.3, "should be at rest");
            // The water line on the DRAWN floats: the keel at least 10 cm under (it is visibly afloat, not sitting on
            // the surface), the deck at least 10 cm clear (not awash).
            Assert.True(DrawnKeelAtStepM - cgAbove > 0.10, $"drawn keel only {DrawnKeelAtStepM - cgAbove:F3} m under the water");
            Assert.True(cgAbove - DrawnDeckAtStepM > 0.10, $"drawn deck only {cgAbove - DrawnDeckAtStepM:F3} m clear of the water");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>A taxi on 10 % power, stick back, then HALF right rudder for 20 s. Half, not full: see
    /// <see cref="FullRudderOnATaxiDoesNotCapsizeIt"/>.</summary>
    [Fact]
    public void TaxisOnTheWaterAndTurnsOnTheWaterRudder()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            double maxRoll = 0, minCg = double.MaxValue;
            // Taxi: a little power, stick held back (keeps the water rudder and the tail down).
            for (double t = 0; t < 30; t += 0.1)
            {
                sim.RunFor(0.1, new ControlInputs(0, -0.6, 0, Lever(TaxiPower)));
                Assert.False(double.IsNaN(ac.State.Position.Z), $"NaN at t={t:F1}");
                maxRoll = Math.Max(maxRoll, Math.Abs(Roll(ac.State))); minCg = Math.Min(minCg, -ac.State.Position.Z);
            }
            double v = ac.State.Velocity.Length, hdg0 = Yaw(ac.State), turned = 0, last = hdg0;
            _out.WriteLine($"straight taxi: {v:F2} m/s after 30 s, {ac.State.Position.X:F0} m run, heading {hdg0:F1}");
            Assert.InRange(v, 1.0, 8.0);                 // moving, but plowing, not on the step
            Assert.True(ac.State.Position.X > 20, "it should have gone somewhere");
            // Half right rudder for 20 s: the water rudder turns it.
            for (double t = 0; t < 20; t += 0.1)
            {
                sim.RunFor(0.1, new ControlInputs(0, -0.6, 0.5, Lever(TaxiPower)));
                Assert.False(double.IsNaN(ac.State.Position.Z), $"NaN at t={t:F1} in the turn");
                double now = Yaw(ac.State);
                turned += (now - last + 540) % 360 - 180;   // unwrapped: a turn past 180 is still a turn
                last = now;
                maxRoll = Math.Max(maxRoll, Math.Abs(Roll(ac.State))); minCg = Math.Min(minCg, -ac.State.Position.Z);
            }
            _out.WriteLine($"half right rudder 20 s: turned {turned:F0} deg at {ac.State.Velocity.Length:F2} m/s; max |roll| {maxRoll:F1}, lowest CG {minCg:F2} m above the water");
            Assert.True(turned > 30, $"half right rudder turned it only {turned:F0} deg in 20 s");
            Assert.True(maxRoll < 10, $"rolled {maxRoll:F1} deg taxiing");
            Assert.True(minCg > DrawnDeckAtStepM, $"CG came down to {minCg:F2} m above the water: the drawn decks would be awash");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    private const double TaxiPower = 0.10;

    /// <summary>
    /// RED, and skipped so the suite still says what it says about everything else. Found 2026-11-02 while writing the
    /// taxi test: FULL rudder on a 10-30 % power taxi (5.5-7.3 m/s) whips the J-3 round 115-170 deg in 2 s, the speed
    /// RISES to ~10 m/s in the turn on the same power, and it rolls over (|roll| 180) within 4-6 s. The Super Cub on
    /// floats does the same, and so does lane/claude-crash-water a1e97493, which owns FloatHydro, so this is the water
    /// model and not the J-3's float block. Half rudder turns it cleanly. Also seen: at idle neither seaplane moves at
    /// all (0.00 m/s after 30 s), where a real one creeps at a few knots. Reported in AGENT-LOG, not fixed here.
    /// </summary>
    [Fact(Skip = "water-model defect, reported to lane/claude-crash-water (AGENT-LOG 2026-11-02): full rudder at a taxi capsizes every float type")]
    public void FullRudderOnATaxiDoesNotCapsizeIt()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            for (double t = 0; t < 30; t += 0.1) sim.RunFor(0.1, new ControlInputs(0, -0.6, 0, Lever(TaxiPower)));
            double maxRoll = 0, v0 = ac.State.Velocity.Length, vMax = 0;
            for (double t = 0; t < 20; t += 0.1)
            {
                sim.RunFor(0.1, new ControlInputs(0, -0.6, 1.0, Lever(TaxiPower)));
                maxRoll = Math.Max(maxRoll, Math.Abs(Roll(ac.State))); vMax = Math.Max(vMax, ac.State.Velocity.Length);
            }
            _out.WriteLine($"full right rudder from {v0:F2} m/s: max |roll| {maxRoll:F0}, max speed {vMax:F1} m/s");
            Assert.True(maxRoll < 15, $"rolled to {maxRoll:F0} deg");
            Assert.True(vMax < v0 + 1.0, $"sped up from {v0:F1} to {vMax:F1} m/s in a turn on the same power");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Fact]
    public void TakesOffFromTheWaterUnderFullPower()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            double airborneAt = -1, maxPitch = 0, runM = 0;
            for (double t = 0; t < 60; t += 0.1)
            {
                // FloatTests' technique: full back through the hump, hold about 5 deg on the step, rotate to fly it off.
                double v = ac.State.Velocity.Length, pitchDeg = Pitch(ac.State), q = ac.State.Rates.Y;
                double target = v < 20 ? 5.0 : 9.0;
                double stick = v < 6 ? -0.38 : Math.Clamp(-0.08 - 0.155 * (target - pitchDeg) + 0.39 * q, -0.9, 0.7);
                sim.RunFor(0.1, new ControlInputs(0, stick, 0, Lever(1.0)));
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                if ((int)(t * 10) % 30 == 0)
                    _out.WriteLine($"t={t,4:F1} V={v,5:F1} cg={-s.Position.Z,5:F2} pitch={Pitch(s),5:F1} draft={FloatHydro.LastReport.DraftAtStepM:F2} lift={FloatHydro.LastReport.PlaningLiftN:F0} buoy={FloatHydro.LastReport.BuoyancyN:F0}");
                if (-s.Position.Z > 4.0 && FloatHydro.LastReport.BuoyancyN < 1 && FloatHydro.LastReport.PlaningLiftN < 1)
                { airborneAt = t; runM = s.Position.X; break; }
            }
            _out.WriteLine($"airborne at {airborneAt:F1} s after {runM:F0} m, max |pitch| {maxPitch:F1}");
            Assert.True(airborneAt > 0 && airborneAt < 50, $"did not get airborne (airborneAt={airborneAt})");
            Assert.True(maxPitch < 25, $"porpoised / pitched to {maxPitch:F0} deg");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Fact]
    public void LandsOnTheWaterWithoutBreakingOrSinking()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            // Power-off glide at 23 m/s (~45 kt; a J-3 on floats is flown down final at 50-55 mph). No flaps on a J-3.
            const double V = 23.0;
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, V, 40, flapFraction: 0.0, idleProp: true);
            Assert.True(trim.Converged, "approach trim must converge");
            double half = trim.ThetaRad / 2.0;
            var state = new RigidBodyState(new Vec3(0, 0, -40.0), new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
                new Vec3(V * Math.Cos(trim.AlphaRad), 0, V * Math.Sin(trim.AlphaRad)), Vec3.Zero);
            var ac = new Aircraft(cfg, state, new ControlDeflections(0, trim.ElevatorRad, 0, 0, 1.0));
            var sim = new SimLoop(ac);
            double stick = Aircraft.StickForDeflection(trim.ElevatorRad, cfg.Controls.Elevator);
            double touchdown = -1, maxG = 0, maxPitch = 0, flareStart = -1, vAtTd = 0, sinkAtTd = 0, stoppedAt = -1;
            for (double t = 0; t < 90; t += 0.1)
            {
                RigidBodyState before = ac.State;
                double agl = -ac.State.Position.Z;
                if (flareStart < 0 && agl < 12) flareStart = t;
                double pull = flareStart < 0 ? 0.0 : Math.Min(0.4, 0.11 * (t - flareStart));
                double st = Math.Clamp(stick - pull + 0.4 * ac.State.Rates.Y, -0.85, 0.5);
                sim.RunFor(0.1, new ControlInputs(0, st, 0, Lever(0)));
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                Vec3 vw = s.Attitude.Rotate(s.Velocity), vb = before.Attitude.Rotate(before.Velocity);
                maxG = Math.Max(maxG, (vw - vb).Length / 0.1 / 9.81);
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                if (touchdown < 0 && FloatHydro.LastReport.DraftAtStepM > 0.02) { touchdown = t; vAtTd = before.Velocity.Length; sinkAtTd = vb.Z; }
                if (touchdown > 0 && s.Velocity.Length < 3.0) { stoppedAt = t; break; }
            }
            _out.WriteLine($"trim alpha {trim.AlphaRad * 57.3:F1} gamma {(trim.ThetaRad - trim.AlphaRad) * 57.3:F1}; touchdown {touchdown:F1} s at {vAtTd:F1} m/s, "
                           + $"{sinkAtTd:F2} m/s sink; below 3 m/s at {stoppedAt:F1} s; max {maxG:F1} g, max |pitch| {maxPitch:F0}");
            Assert.True(touchdown > 0, "never touched the water");
            Assert.True(stoppedAt > 0, "did not slow down on the water");
            Assert.True(maxG < 4.5, $"max {maxG:F1} g");
            Assert.True(maxPitch < 32, $"max pitch {maxPitch:F0}");
            // And afterwards it floats, upright, at its resting draft: it has not been driven under.
            for (int i = 0; i < 300; i++) sim.RunFor(0.1, new ControlInputs(0, 0, 0, Lever(0)));
            RigidBodyState end = ac.State;
            double cg = -end.Position.Z;
            _out.WriteLine($"30 s later: CG {cg:F3} m above the water, draft {FloatHydro.LastReport.DraftAtStepM:F3}, roll {Roll(end):F1}, pitch {Pitch(end):F1}");
            Assert.False(double.IsNaN(cg));
            Assert.InRange(FloatHydro.LastReport.DraftAtStepM, 0.18, 0.30);
            Assert.True(Math.Abs(Roll(end)) < 3, $"roll {Roll(end):F1} after landing");
            Assert.True(cg - DrawnDeckAtStepM > 0.10, "decks awash after landing");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
