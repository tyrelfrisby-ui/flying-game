using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The J-3 on floats as a pilot meets it (Ty 2026-11-04, float-hydro spec): the water loop off a nose-low step, the heel
/// dragging when it is held too nose-high, the floats' roll coupling in a sideways slide, and the water rudder's
/// authority down and retracted. Every outcome here comes out of FloatHydro's per-station forces at their own positions
/// (buoyancy, Savitsky planing, step-wake afterbody, slender-body side force and crossflow per station, the drawn water
/// rudder): nothing in the model knows what a water loop or a porpoise is.
/// </summary>
[Collection("WorldTerrainActive")]
public class FloatHandlingTests
{
    private readonly ITestOutputHelper _out;
    public FloatHandlingTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "j3-cub-floats-like.json"));
    private static double Lever(double throttle01) => 1.0 - 2.0 * throttle01;
    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.3; }
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.3; }
    private static double Yaw(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.3; }
    private static double SideslipDeg(RigidBodyState s) => Math.Atan2(s.Velocity.Y, Math.Max(Math.Abs(s.Velocity.X), 0.1)) * 57.3;

    private static (Aircraft ac, SimLoop sim) Afloat(AircraftConfig cfg)
    {
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(10.0, new ControlInputs(0, 0, 0, Lever(0)));
        return (ac, sim);
    }

    /// <summary>A pilot holding a pitch attitude with the elevator and the wings level with the ailerons (the rudder is
    /// left alone unless given).</summary>
    private sealed class Pilot
    {
        private double _integ;
        public double Stick(RigidBodyState s, double target)
        {
            double pitch = Pitch(s), q = s.Rates.Y * 57.3;
            _integ = Math.Clamp(_integ + (target - pitch) * 0.05, -20, 20);
            return Math.Clamp(-0.15 - 0.10 * (target - pitch) - 0.03 * _integ + 0.04 * q, -1, 1);
        }
        public static double Ail(RigidBodyState s) => Math.Clamp(-0.05 * Roll(s) - 0.3 * s.Rates.X, -1, 1);
        public static double Rud(RigidBodyState s, double yaw0) => Math.Clamp(-0.08 * ((Yaw(s) - yaw0 + 540) % 360 - 180) - 0.6 * s.Rates.Z, -1, 1);
    }

    /// <summary>From rest to the step the way it is flown (full aft stick until the nose comes up, then a 6° attitude),
    /// heading held with the rudder, then power to hold <paramref name="targetMs"/>.</summary>
    private static (Aircraft ac, SimLoop sim, Pilot p) OnTheStep(AircraftConfig cfg, double targetMs)
    {
        (Aircraft ac, SimLoop sim) = Afloat(cfg);
        var p = new Pilot();
        double peak = -99; bool eased = false, reached = false;
        for (double t = 0; t < 60; t += 0.05)
        {
            RigidBodyState s = ac.State;
            double pitch = Pitch(s), v = s.Velocity.Length, stick, thr = 1.0;
            if (!eased) { stick = -1; if (pitch > peak) peak = pitch; else if (peak > 6 && pitch < peak - 1) eased = true; }
            else stick = p.Stick(s, 6.0);
            if (v >= targetMs) reached = true;
            if (reached) thr = Math.Clamp(0.45 + 0.15 * (targetMs - v), 0, 1);
            sim.RunFor(0.05, new ControlInputs(Pilot.Ail(s), stick, Pilot.Rud(s, 0), Lever(thr)));
            if (reached && t > 25) break;
        }
        return (ac, sim, p);
    }

    /// <summary>Hold <paramref name="attitude"/> at about 30 mph on the step, then kick the sideslip by 3° and let go of the
    /// rudder: returns the largest sideslip and yaw rate, and the heading change, over 6 s.</summary>
    private (double maxBeta, double maxR, double headingChange, double minPitch) Perturbed(double attitude)
    {
        AircraftConfig cfg = Load();
        (Aircraft ac, SimLoop sim, Pilot p) = OnTheStep(cfg, 13.4);
        for (double u = 0; u < 3; u += 0.05)
        {
            RigidBodyState s = ac.State; double v = s.Velocity.Length;
            sim.RunFor(0.05, new ControlInputs(Pilot.Ail(s), p.Stick(s, attitude), Pilot.Rud(s, 0), Lever(Math.Clamp(0.45 + 0.15 * (13.4 - v), 0, 1))));
        }
        RigidBodyState k = ac.State;
        // The kick: 3° of sideslip (the water's velocity turned 3° to the right of the keels), nothing else.
        double b0 = 3.0 / 57.3; Vec3 vb = k.Velocity;
        ac.State = new RigidBodyState(k.Position, k.Attitude, new Vec3(vb.X * Math.Cos(b0) - vb.Y * Math.Sin(b0), vb.X * Math.Sin(b0) + vb.Y * Math.Cos(b0), vb.Z), k.Rates);
        double yaw0 = Yaw(k);
        double maxBeta = 0, maxR = 0, endR = 0, minPitch = 99;
        for (double u = 0; u < 6; u += 0.05)
        {
            RigidBodyState s = ac.State; double v = s.Velocity.Length;
            sim.RunFor(0.05, new ControlInputs(Pilot.Ail(s), p.Stick(s, attitude), 0, Lever(Math.Clamp(0.45 + 0.15 * (13.4 - v), 0, 1))));
            maxBeta = Math.Max(maxBeta, Math.Abs(SideslipDeg(ac.State)));
            maxR = Math.Max(maxR, Math.Abs(ac.State.Rates.Z * 57.3));
            endR = (Yaw(ac.State) - yaw0 + 540) % 360 - 180;   // the heading change, deg
            if (Math.Abs(endR) > 90) break;
            minPitch = Math.Min(minPitch, Pitch(ac.State));
        }
        return (maxBeta, maxR, endR, minPitch);
    }

    /// <summary>The mechanism behind the water loop, measured: the water's side force from a ±3° sideslip on the step at
    /// 30 mph, and where it acts. At the planing attitude it acts behind the CG (the floats weathervane); nose low the wet
    /// forward keels put it ahead of the CG and the yaw moment it makes turns the nose further from the slip.</summary>
    [Fact]
    public void NoseLowTheFloatsSideForceActsAheadOfTheCg()
    {
        var at = new System.Collections.Generic.Dictionary<double, (double dN, double x)>();
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            foreach (double att in new[] { 6.0, 0.0 })
            {
                AircraftConfig cfg = Load();
                (Aircraft ac, SimLoop sim, Pilot p) = OnTheStep(cfg, 13.4);
                for (double u = 0; u < 3; u += 0.05)
                {
                    RigidBodyState s = ac.State; double v = s.Velocity.Length;
                    sim.RunFor(0.05, new ControlInputs(Pilot.Ail(s), p.Stick(s, att), Pilot.Rud(s, 0), Lever(Math.Clamp(0.45 + 0.15 * (13.4 - v), 0, 1))));
                }
                RigidBodyState k = ac.State;
                double b = 3.0 / 57.3;
                Vec3 vb = k.Velocity;
                Vec3 vp = new(vb.X * Math.Cos(b) - vb.Y * Math.Sin(b), vb.X * Math.Sin(b) + vb.Y * Math.Cos(b), vb.Z);
                Vec3 vm = new(vb.X * Math.Cos(-b) - vb.Y * Math.Sin(-b), vb.X * Math.Sin(-b) + vb.Y * Math.Cos(-b), vb.Z);
                (Vec3 fP, Vec3 mP) = FloatHydro.Compute(cfg, new RigidBodyState(k.Position, k.Attitude, vp, k.Rates), 0, 0);
                string wet = $"bow draft {FloatHydro.Floats[0].BowDraftM:F3} step {FloatHydro.Floats[0].StepDraftM:F3} stern {FloatHydro.Floats[0].SternDraftM:F3} bowX {FloatHydro.Floats[0].BowWorld.X - k.Position.X:F2}";
                (Vec3 fM, Vec3 mM) = FloatHydro.Compute(cfg, new RigidBodyState(k.Position, k.Attitude, vm, k.Rates), 0, 0);
                Vec3 dm = k.Attitude.Conjugate().Rotate(mP - mM), df = k.Attitude.Conjugate().Rotate(fP - fM);
                double xcp = Math.Abs(df.Y) > 1 ? dm.Z / df.Y : double.NaN;
                _out.WriteLine($"att {att} (pitch {Pitch(k):F1}, V {k.Velocity.Length:F1}): dN/dbeta {dm.Z / 6:F1} N·m/deg, dY/dbeta {df.Y / 6:F1} N/deg, lateral centre x {xcp:F2} m from CG (+ ahead); {wet}");
                at[att] = (dm.Z / 6, xcp);
            }
            Assert.True(at[6.0].x < -0.3 && at[6.0].dN > 0, $"6°: centre {at[6.0].x:F2} m, dN/dβ {at[6.0].dN:F0}");
            Assert.True(at[0.0].x > 0.3 && at[0.0].dN < 0, $"0°: centre {at[0.0].x:F2} m, dN/dβ {at[0.0].dN:F0}");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The water loop: nose low on the step the forward keels are wet, their side force is ahead of the CG, and a
    /// small yaw kick grows; at the planing attitude the same kick dies away.</summary>
    [Fact]
    public void NoseLowOnTheStepAYawKickDivergesAndAtTheRightTrimItDies()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            var low = Perturbed(0.0);
            var right = Perturbed(6.0);
            _out.WriteLine($"nose low (0°): |beta| max {low.maxBeta:F1}°, |r| max {low.maxR:F1} °/s, heading change {low.headingChange:F1}°, pitch min {low.minPitch:F1}");
            _out.WriteLine($"right (6°):    |beta| max {right.maxBeta:F1}°, |r| max {right.maxR:F1} °/s, heading change {right.headingChange:F1}°, pitch min {right.minPitch:F1}");
            // At the planing attitude the heading hardly moves and the yaw rate stays small; nose low the nose turns AWAY
            // from the slip (left, for a slip to the right) and keeps going.
            Assert.True(Math.Abs(right.headingChange) < 5, $"at the planing attitude: heading change {right.headingChange:F1}°");
            Assert.True(right.maxR < 6, $"at the planing attitude the yaw rate stayed small? max {right.maxR:F1} °/s");
            Assert.True(low.headingChange < -15, $"nose low it should loop away from the slip: heading change {low.headingChange:F1}°");
            Assert.True(low.maxR > 2 * right.maxR, $"and fast: |r| {low.maxR:F1} vs {right.maxR:F1} °/s");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Too nose-high from the start (12° held): the heels drag, the water drag through the hump is higher than at
    /// the right attitude and it gets on the step later.</summary>
    [Fact]
    public void HeldTooNoseHighTheHeelsDragAndItDoesNotGetOnTheStep()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            (double stepT, double drag10, double maxV, double peak, double d10) Run(double attitude)
            {
                AircraftConfig cfg = Load();
                (Aircraft ac, SimLoop sim) = Afloat(cfg);
                var p = new Pilot();
                double stepT = -1, drag10 = -1, maxV = 0, peak = 0, d10 = -1;
                for (double t = 0; t < 40; t += 0.05)
                {
                    RigidBodyState s = ac.State;
                    sim.RunFor(0.05, new ControlInputs(Pilot.Ail(s), p.Stick(s, attitude), Pilot.Rud(s, 0), Lever(1.0)));
                    FloatHydro.Report r = FloatHydro.LastReport;
                    double v = ac.State.Velocity.Length; maxV = Math.Max(maxV, v);
                    if (drag10 < 0 && v >= 8.0) drag10 = r.DragN;
                    if (d10 < 0 && v >= 10.0) d10 = r.DragN;
                    if (r.BuoyancyN > 0) peak = Math.Max(peak, r.DragN);
                    if (stepT < 0 && r.VentilatedFraction >= 0.99 && FloatHydro.Floats[0].SternDraftM < 0.005 && FloatHydro.Floats[1].SternDraftM < 0.005 && r.PlaningLiftN > r.BuoyancyN && v > 5) stepT = t;
                    if (!FloatHydro.Floats[0].Wet && !FloatHydro.Floats[1].Wet) break;
                }
                return (stepT, drag10, maxV, peak, d10);
            }
            var right = Run(6.0);
            var high = Run(12.0);
            _out.WriteLine($"6°:  on the step at {right.stepT:F1} s, water drag at 8 m/s {right.drag10:F0} N, at 10 m/s {right.d10:F0}, peak {right.peak:F0}, max {right.maxV:F1} m/s");
            _out.WriteLine($"12°: on the step at {high.stepT:F1} s, water drag at 8 m/s {high.drag10:F0} N, at 10 m/s {high.d10:F0}, peak {high.peak:F0}, max {high.maxV:F1} m/s");
            // Measured: the heels are deep while it ploughs (stern 0.31 m at 3 m/s against 0.18), and at 12° the planing
            // pressure's own drag L·tan τ is the bigger share (≈ 400–485 N against 280–305 at 6°); the hump is ~19 % higher
            // and the step comes ~3 s later. With 85 hp on 1,300 lb the J-3 still pushes through it: delayed, not never.
            Assert.True(right.stepT > 0, "at the right attitude it should get on the step");
            Assert.True(high.peak > 1.15 * right.peak, $"the hump should be higher held nose-high: {high.peak:F0} vs {right.peak:F0} N");
            Assert.True(high.drag10 > right.drag10 && high.d10 > right.d10, "and the drag higher all the way through it");
            Assert.True(high.stepT < 0 || high.stepT > right.stepT + 2, $"too nose-high it should get on the step late or never ({high.stepT:F1} vs {right.stepT:F1} s)");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Twin floats, each at its own offset: slid sideways at a slow taxi, the water's side force on the floats
    /// acts below the CG, the aeroplane heels toward the way it is sliding and the leading float digs deeper.</summary>
    [Fact]
    public void SlidingSidewaysTheLeadingFloatDigsInAndItHeels()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, SimLoop sim) = Afloat(cfg);
            RigidBodyState k = ac.State;
            ac.State = new RigidBodyState(k.Position, k.Attitude, new Vec3(2.0, 1.5, 0), Vec3.Zero);   // drifting right
            double maxRoll = 0, dL = 0, dR = 0;
            for (double u = 0; u < 1.0; u += 0.01)
            {
                sim.RunFor(0.01, new ControlInputs(0, 0, 0, Lever(0)));
                if (Roll(ac.State) > maxRoll) { maxRoll = Roll(ac.State); dL = FloatHydro.Floats[0].StepDraftM; dR = FloatHydro.Floats[1].StepDraftM; }
            }
            _out.WriteLine($"drifting right at 1.5 m/s: heel {maxRoll:F1}° right, step draft left {dL:F3} right {dR:F3} m");
            Assert.True(maxRoll > 1.0, $"heeled only {maxRoll:F1}°");
            Assert.True(dR > dL + 0.01, $"the right (leading) float should be deeper: {dR:F3} vs {dL:F3}");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>The water rudder: a full pedal swing at a 3 m/s taxi is a large yaw moment with the blade down, and next to
    /// nothing with it hung on the hook; at 5.5 m/s, with the heels up, nothing at all.</summary>
    [Fact]
    public void TheWaterRudderSteersDownAndHardlyAtAllUp()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig cfg = Load();
            (Aircraft ac, _) = Afloat(cfg);
            double Swing(double v, double up)
            {
                var m = new RigidBodyState(ac.State.Position, ac.State.Attitude, new Vec3(v, 0, 0), Vec3.Zero);
                (_, Vec3 mL) = FloatHydro.Compute(cfg, m, -1.0, up);
                (_, Vec3 mR) = FloatHydro.Compute(cfg, m, 1.0, up);
                return ac.State.Attitude.Conjugate().Rotate(mR - mL).Z;
            }
            double d3 = Swing(3.0, 0), u3 = Swing(3.0, 1), u55 = Swing(5.5, 1);
            _out.WriteLine($"pedal swing yaw moment at 3 m/s: down {d3:F0} N·m, up {u3:F0}; up at 5.5 m/s {u55:F0}");
            Assert.True(d3 > 400, $"down: only {d3:F0} N·m");
            Assert.True(Math.Abs(u3) < 0.15 * d3, $"up: {u3:F0} N·m against {d3:F0} down");
            Assert.True(Math.Abs(u55) < 1.0);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
