using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The float hydrodynamics deep dive (Ty 2026-11-02: "the water drag is too high on the j3 on floats … the boyancy and
/// planing on step all seem weak do a deep dive on the hydrodynamics"). Every expected value is a literal copied by hand
/// from its source, never read from the config being checked:
///  - Savitsky 1964 lift equations and the Savitsky &amp; Morabito 2010 wake equation, worked by hand (python, 2026-11-02);
///  - the EDO 60-1320's rating (FAA A-691 Item 206; Edo names the float for the pounds it displaces);
///  - the drawn float as measured off our GLB by tools/own_models/j3_float_stations.py (placement frame + cg, 2026-11-02);
///  - the J-3 on floats in the water: run 15–25 s / 600–1000 ft, hump 12–18 mph, on the step by 20–25 mph, liftoff
///    ~45 mph (Ty's brief, from a J-3 on floats at gross in calm water).
/// docs/pending/HYDRO-J3-CLAUDE.md has the term table and the before/after.
/// </summary>
[Collection("WorldTerrainActive")]
public class HydroJ3Tests
{
    private const string Id = "j3-cub-floats-like";
    private readonly ITestOutputHelper _out;
    public HydroJ3Tests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id = Id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private static double Pitch(RigidBodyState s) { Quat q = s.Attitude; return Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.2958; }
    private static double Roll(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.2958; }
    private static double Yaw(RigidBodyState s) { Quat q = s.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.2958; }
    private static double Lever(double throttle01) => 1.0 - 2.0 * throttle01;

    private static (Aircraft, SimLoop) Afloat(AircraftConfig cfg)
    {
        var st = new RigidBodyState(new Vec3(0, 0, -(cfg.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(cfg, st, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(15, new ControlInputs(0, 0, 0, Lever(0)));
        return (ac, sim);
    }

    // ---- the published equations, against hand-worked numbers ----

    [Fact]
    public void SavitskyLiftMatchesTheHandWorkedEquation()
    {
        // C_L0 = τ^1.1 (0.012 λ^½ + 0.0055 λ^2.5 / C_V²), C_Lβ = C_L0 − 0.0065 β C_L0^0.6 (Savitsky 1964 eqs. 15–16).
        // τ 4°, λ 2, C_V 5, β 10°: 4^1.1 = 4.5948; 0.012·1.41421 + 0.0055·5.65685/25 = 0.0182152 → C_L0 0.083694;
        // C_L0^0.6 = 0.22572 → C_Lβ = 0.083694 − 0.065·0.22572 = 0.069021.
        (double cl0, double clb) = FloatHydro.SavitskyLift(4, 2, 5, 10);
        Assert.Equal(0.083694, cl0, 5);
        Assert.Equal(0.069021, clb, 5);
        // τ 6°, λ 3, C_V 4, β 20° (the J-3's float is 18.8°): C_L0 0.187639, C_Lβ 0.140003.
        (cl0, clb) = FloatHydro.SavitskyLift(6, 3, 4, 20);
        Assert.Equal(0.187639, cl0, 5);
        Assert.Equal(0.140003, clb, 5);
    }

    [Fact]
    public void WakeAndFrictionMatchTheirSources()
    {
        // Savitsky & Morabito 2010 centreline, β 20° (A = 2.0): X 1.5 b, L_k 3 b, τ 4°, C_V 6:
        // 0.17 (2 + 0.03·3·8) sin(π/6 · 0.5^1.5) = 0.17 · 2.72 · 0.18406 = 0.085111 beams.
        Assert.Equal(0.085111, FloatHydro.WakeHeightBeams(1.5, 3, 4, 6, 20), 5);
        // ITTC-1957 at Re 1e7: 0.075/(7 − 2)² = 0.0030, plus Savitsky's ΔC_F 0.0004.
        Assert.Equal(0.0034, FloatHydro.FrictionCoefficient(1e7), 6);
        // Transom ventilation: wet below F_T 2, dry above 4 (0.25 m step: 3.13 m/s and 6.26 m/s).
        Assert.Equal(0.0, FloatHydro.VentilatedFraction(3.0, 0.25), 6);
        Assert.Equal(1.0, FloatHydro.VentilatedFraction(6.5, 0.25), 6);
    }

    // ---- the drawn float: geometry, volume, statics ----

    [Fact]
    public void ThePhysicsFloatIsTheDrawnFloat()
    {
        // tools/own_models/j3_float_stations.py, 2026-11-02 (config datum frame): Float_L centre y −0.9435, bow tip x
        // +1.9945, transom x −2.3888, step x −0.4437, keel at the step 1.4419 (the GLB's lowest point, the keel plate,
        // 1.4496), chine beam at the step 0.5358.
        AircraftConfig c = Load();
        FloatHull h = FloatHull.For(c.Floats!);
        Assert.InRange(c.Floats!.SpreadM / 2, 0.9435 - 0.01, 0.9435 + 0.01);
        Assert.InRange(h.BowX, 1.9945 - 0.01, 1.9945 + 0.01);
        Assert.InRange(h.SternX, -2.3888 - 0.01, -2.3888 + 0.01);
        Assert.InRange(h.StepX, -0.4437 - 0.01, -0.4437 + 0.01);
        Assert.InRange(h.KeelZStep, 1.4419 - 0.01, 1.4496 + 0.01);
        Assert.InRange(h.Beam, 0.5358 - 0.01, 0.5358 + 0.01);
        // the float-keel contact points sit on the drawn keel (the step one on its lowest point)
        foreach (GearConfig g in c.Gear)
            if (g.GearType == "float-keel" && Math.Abs(g.Pos[0] + 0.4422) < 0.01) Assert.InRange(g.Pos[2], 1.4496 - 0.01, 1.4496 + 0.01);
    }

    [Fact]
    public void TheDrawnFloatDisplacesItsRating()
    {
        // EDO 60-1320: 1,320 lb of fresh water per float = 0.59874 m³. The drawn hull integrates to within 3 %.
        AircraftConfig c = Load();
        double v = FloatHull.For(c.Floats!).Volume;
        _out.WriteLine($"drawn float volume {v:F4} m³ = {v * 1000 / 0.45359237:F0} lb of fresh water (rated 1,320)");
        Assert.InRange(v, 0.59874 * 0.97, 0.59874 * 1.03);
        // CAR 3 / FAR 23.751: the pair displaces at least 1.8 × the weight (1,300 lb gross → 2,340 lb); 2 × 1,320 = 2,640.
        Assert.True(2 * v * 1000 >= 1.8 * c.Mass.MassKg, $"reserve {2 * v * 1000 / c.Mass.MassKg:F2} W");
    }

    [Fact]
    public void RestsOnItsDisplacementWithTheRightMetacentricHeights()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig c = Load();
            double w = c.Mass.MassKg * Atmosphere.GravityMs2;
            (Aircraft ac, _) = Afloat(c);
            RigidBodyState s0 = ac.State;
            Assert.InRange(FloatHydro.LastReport.BuoyancyN, w * 0.995, w * 1.005);
            // GM from the model the sim floats on: the righting moment of a 2° heel and the restoring moment of a 1° trim.
            // Hand (level waterline 1.157 m below the datum): waterplane 1.66 m² per float at y ±0.9435, so
            // I_T ≈ 2 (1.66 · 0.9435² + 0.02) = 3.0 m⁴, I_L ≈ 4.2 m⁴, V = 0.590 m³ → BM_T 5.1 m, BM_L 7.1 m;
            // the CG is 1.26 m above the centre of buoyancy → GM_T ≈ 3.8 m, GM_L ≈ 5.9 m.
            double GmFrom(Quat dq, int axis, double rad)
            {
                Quat qq = Quat.Multiply(s0.Attitude, dq);
                (_, Vec3 m1) = FloatHydro.Compute(c, new RigidBodyState(s0.Position, qq, Vec3.Zero, Vec3.Zero), 0);
                (_, Vec3 m0) = FloatHydro.Compute(c, new RigidBodyState(s0.Position, s0.Attitude, Vec3.Zero, Vec3.Zero), 0);
                Vec3 d = qq.Conjugate().Rotate(m1) - s0.Attitude.Conjugate().Rotate(m0);
                return -(axis == 0 ? d.X : d.Y) / (w * Math.Sin(rad));
            }
            double r2 = 2 / 57.2958, r1 = 1 / 57.2958;
            double gmT = GmFrom(new Quat(Math.Sin(r2 / 2), 0, 0, Math.Cos(r2 / 2)), 0, r2);
            double gmL = GmFrom(new Quat(0, Math.Sin(r1 / 2), 0, Math.Cos(r1 / 2)), 1, r1);
            _out.WriteLine($"at rest: draft at step {FloatHydro.LastReport.DraftAtStepM:F3} m, pitch {Pitch(s0):F1}, GM_T {gmT:F2} m, GM_L {gmL:F2} m");
            Assert.InRange(gmT, 3.4, 4.2);
            Assert.InRange(gmL, 5.3, 6.5);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    // ---- the water run ----

    [Fact]
    public void TheWaterRunHasAHumpAStepAndALiftoffWhereARealOneDoes()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig c = Load();
            double w = c.Mass.MassKg * Atmosphere.GravityMs2;
            (Aircraft ac, SimLoop sim) = Afloat(c);
            double x0 = ac.State.Position.X, peakDrag = 0, humpV = 0, stepV = -1, peak = -99, integ = 0, unstickV = -1, unstickT = 0, unstickX = 0;
            bool eased = false;
            for (double t = 0; t < 40; t += 0.05)
            {
                RigidBodyState s = ac.State;
                double v = s.Velocity.Length, pitch = Pitch(s), q = s.Rates.Y * 57.3, stick;
                if (!eased) { stick = -1; if (pitch > peak) peak = pitch; else if (peak > 6 && pitch < peak - 1) eased = true; }
                else { double target = v < 18 ? 6.0 : 10.0; integ = Math.Clamp(integ + (target - pitch) * 0.05, -20, 20); stick = Math.Clamp(-0.15 - 0.10 * (target - pitch) - 0.03 * integ + 0.04 * q, -1, 1); }
                double rud = Math.Clamp(-0.08 * Yaw(s) - 0.6 * s.Rates.Z, -1, 1), ail = Math.Clamp(-0.05 * Roll(s) - 0.3 * s.Rates.X, -1, 1);
                sim.RunFor(0.05, new ControlInputs(ail, stick, rud, Lever(1.0)));
                FloatHydro.Report r = FloatHydro.LastReport;
                Assert.False(double.IsNaN(ac.State.Position.Z), $"NaN at t={t:F1}");
                if (r.DragN > peakDrag && r.BuoyancyN > 0) { peakDrag = r.DragN; humpV = v; }
                if (stepV < 0 && r.VentilatedFraction >= 0.99 && FloatHydro.Floats[0].SternDraftM < 0.005 && FloatHydro.Floats[1].SternDraftM < 0.005 && r.PlaningLiftN > r.BuoyancyN && v > 5) stepV = v;
                bool carried = r.BuoyancyN + r.PlaningLiftN >= 0.02 * w;
                if (unstickV < 0 && eased && !carried) { unstickV = v; unstickT = t; unstickX = ac.State.Position.X - x0; }
                else if (carried) unstickV = -1;
                if (!FloatHydro.Floats[0].Wet && !FloatHydro.Floats[1].Wet && -ac.State.Position.Z > 1.6) break;
            }
            const double mph = 2.23694;
            _out.WriteLine($"hump (peak water drag {peakDrag:F0} N = {peakDrag / w:F2} W) at {humpV * mph:F1} mph; on the step at {stepV * mph:F1} mph; "
                           + $"unstick {unstickV * mph:F1} mph after {unstickT:F1} s and {unstickX / 0.3048:F0} ft");
            Assert.InRange(humpV * mph, 12, 18);
            Assert.InRange(stepV * mph, 20, 25);
            Assert.InRange(unstickV * mph, 40, 50);
            Assert.InRange(unstickT, 15, 25);
            Assert.InRange(unstickX / 0.3048, 600, 1000);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    /// <summary>Stick held still on the step: at a low trim (≈2–4° keel) the forebody falls off the step and comes back up
    /// in a growing cycle — lower-limit porpoising; at the normal planing trim (≈5–6° keel at 40 mph) it runs steady.
    /// Nothing damps it but the physics (Savitsky's quasi-steady lift at its own centre of pressure, the added mass).</summary>
    [Theory]
    [InlineData(2.0, true)]
    [InlineData(9.0, false)]
    public void PorpoisesAtTooLowATrimAndNotAtThePlaningTrim(double attitude, bool porpoises)
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig c = Load();
            (Aircraft ac, SimLoop sim) = Afloat(c);
            double peak = -99, integ = 0; bool eased = false;
            double Stick(double target)
            {
                RigidBodyState s = ac.State; double pitch = Pitch(s), q = s.Rates.Y * 57.3;
                integ = Math.Clamp(integ + (target - pitch) * 0.05, -20, 20);
                return Math.Clamp(-0.15 - 0.10 * (target - pitch) - 0.03 * integ + 0.04 * q, -1, 1);
            }
            for (double t = 0; t < 60 && ac.State.Velocity.Length < 13.4; t += 0.05)
            {
                double pitch = Pitch(ac.State), stick;
                if (!eased) { stick = -1; if (pitch > peak) peak = pitch; else if (peak > 6 && pitch < peak - 1) eased = true; }
                else stick = Stick(6.0);
                sim.RunFor(0.05, new ControlInputs(0, stick, 0, Lever(0.75)));
            }
            double hold = 0;
            for (double u = 0; u < 3; u += 0.05) { hold = Stick(attitude); sim.RunFor(0.05, new ControlInputs(0, hold, 0, Lever(0.75))); }
            double lo = 99, hi = -99;
            for (double u = 0; u < 8; u += 0.05)
            {
                sim.RunFor(0.05, new ControlInputs(0, hold, 0, Lever(0.75)));
                if (u > 2) { double p = Pitch(ac.State); lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
            }
            _out.WriteLine($"attitude {attitude}°, stick held at {hold:F2}: pitch {lo:F1}..{hi:F1} (swing {hi - lo:F1}°) at {ac.State.Velocity.Length * 2.237:F0} mph");
            if (porpoises) Assert.True(hi - lo > 5, $"swing only {hi - lo:F1}°");
            else Assert.True(hi - lo < 3, $"swing {hi - lo:F1}° at the planing trim");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
