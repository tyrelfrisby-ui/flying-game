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
            double sinkMs = s.Attitude.Rotate(s.Velocity).Z, hullDragN = FloatHydro.Floats[0].DragN + FloatHydro.Floats[1].DragN;
            _out.WriteLine($"J-3 floats at rest: CG {cgAbove:F3} m above the water, physics draft at the step {draft:F3} m, "
                           + $"pitch {Pitch(s):F1}, roll {Roll(s):F1}, buoyancy {FloatHydro.LastReport.BuoyancyN:F0} N vs weight {cfg.Mass.MassKg * 9.81:F0} N; "
                           + $"drawn float: keel {DrawnKeelAtStepM - cgAbove:F3} m under, deck {cgAbove - DrawnDeckAtStepM:F3} m clear; "
                           + $"settling {sinkMs:+0.000;-0.000} m/s, idling ahead at {s.Velocity.Length:F3} m/s ({s.Velocity.Length / 0.44704:F1} mph) against {hullDragN:F0} N of hull resistance at {ac.EngineRpm:F0} rpm");
            // THE DRAWN EDO 60-1320 AT THE WEIGHT IT IS FLOWN AT. The range used to be 0.26-0.32 around a level
            // waterline of 0.285 m above the keel at the step, which was the bisection of the drawn sections for
            // 589.7 kg / 2 of fresh water each -- and 589.7 kg was the PLACARD, A-691's 1,300 lb seaplane gross. On
            // 2026-11-11 mass.massKg became the aeroplane as flown, 451.142 kg (805 lb empty, Piper Service Memo 41
            // plus the 125 lb float exchange, one pilot in the rear seat, tanks dry), and a float that displaces
            // 23.5 % less water floats higher. The same bisection, re-run on the same drawn sections:
            //     589.670 kg   0.2948 m3 per float   level draft at the step 0.2851 m   (the old range's centre)
            //     451.142 kg   0.2256 m3 per float   level draft at the step 0.2421 m
            //     365.142 kg   0.1826 m3 per float   level draft at the step 0.2140 m   (empty, for scale)
            // It still rests ~2 deg bow-up, which puts the step about a centimetre deeper: 0.254 m, which is what the
            // sim reads to the millimetre. The window keeps the old one's shape about the new level figure.
            Assert.InRange(draft, 0.22, 0.28);
            Assert.InRange(Pitch(s), -6, 6);
            Assert.InRange(Math.Abs(Roll(s)), 0, 2);
            Assert.InRange(FloatHydro.LastReport.BuoyancyN, cfg.Mass.MassKg * 9.81 * 0.9, cfg.Mass.MassKg * 9.81 * 1.1);
            // AT REST IS VERTICAL. A seaplane with its engine running is never at rest over the ground -- "you cannot
            // stop" is the first thing about float flying -- so the old `Velocity.Length < 0.3` was asking the model to
            // be wrong, and it has never passed: on the pristine base it read 2.33 m/s, and at the OLD 589.7 kg mass
            // (measured, same binaries, the pre-2026-11-11 config) it read 2.01, so the weight is not what put it
            // there. What is there is an ordinary equilibrium, and it is measured, not assumed: 192 N of hull
            // resistance at 2.33 m/s balanced by the propeller's thrust at a 691 rpm idle -- about 2 hp at the shaft
            // through a 1.83 m disc by momentum theory, which is what a C-65 idles at. 2.33 m/s is 5.2 mph.
            //
            // TY: 5.2 mph of idle taxi is the one number in this test nobody has checked against your aeroplane. It is
            // in the band the books give for a Cub on Edo floats (4-6 mph) and it is a consequence of the hull
            // resistance and the idle thrust rather than a tuned figure, but you are the one who idles one around a
            // lake. If it should be slower, the idle rpm or the idle thrust is where to put it, not the hull.
            Assert.True(Math.Abs(sinkMs) < 0.02, $"still settling at {sinkMs:F3} m/s after 15 s: it has not found its waterline");
            // It idles ahead very slightly crabbed -- 0.066 m/s of drift on 2.33 ahead, 1.6 deg -- which is the
            // slipstream and the P-factor on the fin, and a real one does it too. What would be wrong is a float
            // sliding bodily sideways, so the assertion is on the drift ANGLE and not on a raw velocity.
            double driftDeg = Math.Atan2(Math.Abs(s.Velocity.Y), Math.Max(0.1, s.Velocity.X)) * 57.3;
            Assert.True(driftDeg < 5.0, $"idling ahead {driftDeg:F1} deg crabbed: it is sliding, not taxiing");
            Assert.InRange(s.Velocity.Length, 1.0, 4.0);   // an idle taxi, not a standstill and not a getaway
            // And nothing spurious is pushing it: switch the magnetos off and the water stops it.
            ac.MagLeft = false; ac.MagRight = false;
            for (int i = 0; i < 600; i++) sim.RunFor(0.1, new ControlInputs(0, 0, 0, Lever(0)));
            _out.WriteLine($"magnetos off, 60 s later: {ac.State.Velocity.Length:F3} m/s, {ac.EngineRpm:F0} rpm, draft {FloatHydro.LastReport.DraftAtStepM:F3}");
            Assert.True(ac.State.Velocity.Length < 0.35, $"dead engine and it is still doing {ac.State.Velocity.Length:F2} m/s after a minute");
            Assert.InRange(FloatHydro.LastReport.DraftAtStepM, 0.22, 0.28);
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
    /// Ty 2026-11-02: no flip at idle or low power on full rudder. Found red on lane/claude-vr-quest (full rudder at a
    /// 10-30 % taxi whipped it round and capsized it in 4-6 s, speed RISING in the turn): the old water model's fudged
    /// keel and crossflow terms. With the first-principles side force (slender-body lift at mid-draft + crossflow) and
    /// the drawn floats, full right rudder at 10, 20 and 30 % power (6-11 mph), with full aileron either way, in calm
    /// air and in 15 kt from the right, must not roll it past 15 deg and must not speed it up. A capsize in a fast plow
    /// turn IS real: measured 2026-11-02, 60 % power (13 mph) in 15 kt with aileron out of the turn rolls it to 79 deg.
    /// </summary>
    [Theory]
    [InlineData(0.10)]
    [InlineData(0.20)]
    [InlineData(0.30)]
    public void FullRudderOnATaxiDoesNotCapsizeIt(double power)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            foreach (double wind in new[] { 0.0, 7.72 })
                foreach (double ail in new[] { 0.0, 1.0, -1.0 })
                {
                    Atmosphere.SteadyWind = new Vec3(0, -wind, 0);
                    try
                    {
                        (Aircraft ac, SimLoop sim) = Afloat(cfg);
                        sim.RunFor(30, new ControlInputs(0, -0.6, 0, Lever(power)));
                        double maxRoll = 0, v0 = ac.State.Velocity.Length, vMax = 0;
                        for (double t = 0; t < 20; t += 0.1)
                        {
                            sim.RunFor(0.1, new ControlInputs(ail, -0.6, 1.0, Lever(power)));
                            Assert.False(double.IsNaN(ac.State.Position.Z), $"NaN at t={t:F1}");
                            maxRoll = Math.Max(maxRoll, Math.Abs(Roll(ac.State))); vMax = Math.Max(vMax, ac.State.Velocity.Length);
                        }
                        _out.WriteLine($"power {power:P0}, wind {wind / 0.5144:F0} kt, aileron {ail:+0;-0;0}: from {v0:F2} m/s, max |roll| {maxRoll:F1}, max speed {vMax:F2} m/s");
                        Assert.True(maxRoll < 15, $"power {power:P0} wind {wind:F1} ail {ail}: rolled to {maxRoll:F0} deg");
                        // In still air a turn on the same power must not speed it up. In the 15 kt wind the full-rudder turn
                        // swings it downwind and the wind's push on the airframe raises its speed over the water — real, and
                        // larger or smaller with the hull's drag (+0.37 m/s on island-nav b7d1a680, +0.61 here): not asserted.
                        if (wind == 0) Assert.True(vMax < v0 + 0.5, $"sped up from {v0:F2} to {vMax:F2} m/s in a turn on the same power");
                    }
                    finally { Atmosphere.SteadyWind = Vec3.Zero; }
                }
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
            double airborneAt = -1, maxPitch = 0, runM = 0, peak = -99, integ = 0;
            bool easedOver = false;
            for (double t = 0; t < 60; t += 0.05)
            {
                // Seaplane technique: full power, stick FULL back until the nose has risen and starts down by itself (over
                // the hump), then ease forward to the planing attitude (6 deg) and hold it smoothly, raise it 4 deg past
                // 40 mph and let it fly; rudder holds the heading. (The old test's stiff attitude hold pumped the stick
                // stop to stop and porpoised the new float model into the water: pilot-induced, not physics.)
                RigidBodyState s0 = ac.State;
                double v = s0.Velocity.Length, pitchDeg = Pitch(s0), q = s0.Rates.Y * 57.3, stick;
                if (!easedOver) { stick = -1; if (pitchDeg > peak) peak = pitchDeg; else if (peak > 6 && pitchDeg < peak - 1) easedOver = true; }
                else
                {
                    double target = v < 18 ? 6.0 : 10.0;
                    integ = Math.Clamp(integ + (target - pitchDeg) * 0.05, -20, 20);
                    stick = Math.Clamp(-0.15 - 0.10 * (target - pitchDeg) - 0.03 * integ + 0.04 * q, -1, 1);
                }
                double rud = Math.Clamp(-0.08 * Yaw(s0) - 0.6 * s0.Rates.Z, -1, 1), ail = Math.Clamp(-0.05 * Roll(s0) - 0.3 * s0.Rates.X, -1, 1);
                sim.RunFor(0.05, new ControlInputs(ail, stick, rud, Lever(1.0)));
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                if ((int)Math.Round(t * 20) % 60 == 0)
                    _out.WriteLine($"t={t,4:F1} V={v,5:F1} cg={-s.Position.Z,5:F2} pitch={Pitch(s),5:F1} draft={FloatHydro.LastReport.DraftAtStepM:F2} lift={FloatHydro.LastReport.PlaningLiftN:F0} buoy={FloatHydro.LastReport.BuoyancyN:F0}");
                if (!FloatHydro.Floats[0].Wet && !FloatHydro.Floats[1].Wet && -s.Position.Z > 1.6)
                { airborneAt = t; runM = s.Position.X; break; }
            }
            _out.WriteLine($"airborne at {airborneAt:F1} s after {runM:F0} m, max |pitch| {maxPitch:F1}");
            Assert.True(airborneAt > 0 && airborneAt < 30, $"did not get airborne (airborneAt={airborneAt})");
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
            // THE FLARE IS FLOWN TO A SINK RATE, NOT TO A STOPWATCH.
            //
            // It used to be an open-loop ramp: at 12 m, start pulling 0.11 of stick a second up to 0.4, and hope. That
            // is a scripted pull with no idea what the aeroplane is doing, and the aeroplane it was scripted for
            // weighed 589.7 kg. MEASURED at the 451.142 kg this type is now authored at: the same ramp arrives at the
            // water at 17.5 m/s -- 1.02 Vso, the stall -- still sinking 3.13 m/s (616 fpm), drops the right wing into
            // the lake at 3.9 g, and 30 s later the aeroplane is floating at 130 deg of roll with the wing, the nose
            // and the tailboom off it. None of that is the float: its peak water load was 13.4 kN of a 17.8 kN
            // ultimate, and it never split. It was a stall-in, scripted.
            //
            // A flare is a closed loop and always was: hold the sink rate proportional to the height left, so it goes
            // to nothing as the floats reach the water. Target sink 0.25 x the height above touchdown (so the flare
            // begins of its own accord around 14 m, where the 3.6 m/s glide sink first exceeds it), 0.20 of stick per
            // m/s of error, the same pitch-rate damping as before, and no pull at all below 1.12 Vso so the loop
            // cannot fly itself into the stall it is there to avoid. MEASURED over kp 0.15-0.30 x rate 0.20-0.30:
            // every one of the eight touches at 92-298 fpm and 1.0-1.4 g, undamaged, settling at 0.254 m and 0.2 deg
            // of roll. It is not a tuned point, it is a basin.
            double touchAglM = cfg.Floats!.KeelZ - 0.25;   // the height the floats are in the water at
            const double FlareRate = 0.25, FlareGain = 0.20;
            double vso = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(cfg, 0.0);
            double touchdown = -1, maxG = 0, maxPitch = 0, flareStart = -1, vAtTd = 0, sinkAtTd = 0, stoppedAt = -1;
            for (double t = 0; t < 90; t += 0.1)
            {
                RigidBodyState before = ac.State;
                double agl = -before.Position.Z, sink = before.Attitude.Rotate(before.Velocity).Z;
                double targetSink = Math.Max(0.15, FlareRate * Math.Max(0.0, agl - touchAglM));
                double err = sink - targetSink;                               // + = coming down faster than wanted
                if (flareStart < 0 && err > 0.05 && agl - touchAglM < 20) flareStart = t;
                double pull = before.Velocity.Length > 1.12 * vso ? FlareGain * err : 0.0;
                double st = Math.Clamp(stick - pull + 0.4 * before.Rates.Y, -0.85, 0.5);
                // The pilot keeps the wings level, as FloatLandingTests.Land does and for the same reason: a float
                // landing on one float is a different, harder landing, and this test is about the water.
                double ail = Math.Clamp(-0.08 * Roll(before) - 0.03 * before.Rates.X * 57.3, -1, 1);
                sim.RunFor(0.1, new ControlInputs(ail, st, 0, Lever(0)));
                RigidBodyState s = ac.State;
                Assert.False(double.IsNaN(s.Position.Z), $"NaN at t={t:F1}");
                Vec3 vw = s.Attitude.Rotate(s.Velocity), vb = before.Attitude.Rotate(before.Velocity);
                maxG = Math.Max(maxG, (vw - vb).Length / 0.1 / 9.81);
                maxPitch = Math.Max(maxPitch, Math.Abs(Pitch(s)));
                if (touchdown < 0 && FloatHydro.LastReport.DraftAtStepM > 0.02) { touchdown = t; vAtTd = before.Velocity.Length; sinkAtTd = vb.Z; }
                if (touchdown > 0 && s.Velocity.Length < 3.0) { stoppedAt = t; break; }
            }
            _out.WriteLine($"trim alpha {trim.AlphaRad * 57.3:F1} gamma {(trim.ThetaRad - trim.AlphaRad) * 57.3:F1}; flare from {flareStart:F1} s; "
                           + $"touchdown {touchdown:F1} s at {vAtTd:F1} m/s ({vAtTd / vso:F2} Vso), "
                           + $"{sinkAtTd:F2} m/s sink ({sinkAtTd * 196.85:F0} fpm); below 3 m/s at {stoppedAt:F1} s; max {maxG:F1} g, max |pitch| {maxPitch:F0}");
            Assert.True(touchdown > 0, "never touched the water");
            Assert.True(stoppedAt > 0, "did not slow down on the water");
            Assert.True(maxG < 4.5, $"max {maxG:F1} g");
            Assert.True(maxPitch < 32, $"max pitch {maxPitch:F0}");
            // The flare did its job: it is PUT on the water, not dropped on it, and not stalled on. 14 CFR 23.473's
            // design descent velocity for this wing loading is 2.2 m/s (433 fpm), and a landing a pilot would call
            // normal is a quarter of that.
            Assert.True(sinkAtTd < 1.0, $"arrived at {sinkAtTd:F2} m/s ({sinkAtTd * 196.85:F0} fpm): dropped in, not flared");
            Assert.True(vAtTd > 1.1 * vso, $"touched at {vAtTd / vso:F2} Vso: it stalled on");
            Assert.Empty(ac.LostComponents);
            Assert.Empty(ac.Hydro.DamageLog);
            // And afterwards it floats, upright, at its resting draft: it has not been driven under.
            for (int i = 0; i < 300; i++) sim.RunFor(0.1, new ControlInputs(0, 0, 0, Lever(0)));
            RigidBodyState end = ac.State;
            double cg = -end.Position.Z;
            _out.WriteLine($"float peak water load {ac.FloatPeakWaterLoadN[0] / 1000:F1}/{ac.FloatPeakWaterLoadN[1] / 1000:F1} kN of {ac.FloatUltimateWaterLoadN / 1000:F1}; damage [{string.Join("; ", ac.Hydro.DamageLog)}]");
            _out.WriteLine($"30 s later: CG {cg:F3} m above the water, draft {FloatHydro.LastReport.DraftAtStepM:F3}, roll {Roll(end):F1}, pitch {Pitch(end):F1}");
            Assert.False(double.IsNaN(cg));
            Assert.InRange(FloatHydro.LastReport.DraftAtStepM, 0.18, 0.30);
            Assert.True(Math.Abs(Roll(end)) < 3, $"roll {Roll(end):F1} after landing");
            Assert.True(cg - DrawnDeckAtStepM > 0.10, "decks awash after landing");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
