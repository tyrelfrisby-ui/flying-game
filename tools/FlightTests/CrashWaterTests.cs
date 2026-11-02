using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-02: "hitting the water should be a different sound than hitting land … have the pieces of the airplane
/// that break apart go under water initially based on the physics then have some buoyancy from trapped air that
/// dissipates over time as the water fills in then sink. the heavy front part sinks right away the rest comes back up
/// and floats for a bit before sinking. if an airplane ditches and stays intact it takes a while before it sinks.
/// pontoons don't sink except with damage (lessons)".
///
/// The physics is Core/Wreck (HydroElement, HydroBody, WreckSim, AirframeFlooding, FloatFlooding); the events are the
/// CrashEvents contract with the crash-audio lane. Expected numbers are literals worked by hand (CLAUDE.md: a test that
/// reads the file it is checking proves nothing).
/// </summary>
// Mutates WorldTerrain.Active, FloatHydro.FlatWaterOverride and the process-wide CrashEvents hub.
[Collection("WorldTerrainActive")]
public sealed class CrashWaterTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public CrashWaterTests(ITestOutputHelper o) { _out = o; CrashEvents.ClearForTests(); WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = null; }
    public void Dispose() { CrashEvents.ClearForTests(); WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = null; }

    /// <summary>FloatHydro.FlatWaterOverride is THREAD-static: set and cleared on the test's own thread (Dispose can run
    /// on another, and a stale lake then sits over every later test on this thread).</summary>
    internal sealed class FlatWater : IDisposable
    {
        public FlatWater(double up) { FloatHydro.FlatWaterOverride = up; }
        public void Dispose() { FloatHydro.FlatWaterOverride = null; }
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Quat Pitch(double deg) { double h = deg * System.Math.PI / 360; return new Quat(0, System.Math.Sin(h), 0, System.Math.Cos(h)); }

    /// <summary>A piece taken off a fresh C172 (its atoms, with the cut openings the break gives them), dropped from
    /// <paramref name="heightM"/> above flat water 30 m deep at a crash-like speed.</summary>
    private static (WreckSim sim, WreckPiece piece) DropPiece(AirframeComponent comp, double heightM = 2.0, double fwdMs = 20, double downMs = 10, double rollDeg = 0)
    {
        AircraftConfig c = Load("c172-like");
        double hr = rollDeg * System.Math.PI / 360;
        Quat att = Quat.Multiply(Pitch(-10), new Quat(System.Math.Sin(hr), 0, 0, System.Math.Cos(hr)));
        var state = new RigidBodyState(new Vec3(0, 0, -heightM), att, Vec3.Zero, Vec3.Zero);
        var ac = new Aircraft(c, state);
        List<HydroElement> els = ac.Hydro.Detach(comp);
        Assert.NotEmpty(els);
        var sim = new WreckSim(new FlatWreckWorld(0.0, -30.0));
        var vBody = state.Attitude.Conjugate().Rotate(new Vec3(fwdMs, 0, downMs));
        WreckPiece p = sim.Add(comp.ToString(), comp, els, c.Mass.CgVec(), new RigidBodyState(state.Position, state.Attitude, vBody, Vec3.Zero));
        return (sim, p);
    }

    // ---- surface classification ------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnImpactOnWaterIsWaterAndOnLandIsLand(bool water)
    {
        AircraftConfig c = Load("c172-like");
        // Water 10 m deep over flat ground at 0 (the water test), or the bare ground (the land test).
        using var lake = new FlatWater(water ? 10.0 : double.NaN);
        if (!water) FloatHydro.FlatWaterOverride = null;
        double surface = water ? 10.0 : 0.0;
        var att = Pitch(-15);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(surface + 3.0)), att, att.Conjugate().Rotate(new Vec3(25, 0, 9)), Vec3.Zero));
        var relay = new CrashRelay(ac);
        var impacts = new List<CrashImpact>();
        CrashEvents.Impact += impacts.Add;
        var loop = new SimLoop(ac);
        for (int i = 0; i < 400; i++) { loop.RunFor(SimLoop.DefaultFixedDtSec, ControlInputs.Neutral); relay.Step(SimLoop.DefaultFixedDtSec); }
        foreach (CrashImpact e in impacts) _out.WriteLine($"{e.Surface} piece {e.PieceId} v_n {e.NormalSpeedMs:F1} sev {e.Severity01:F2} m {e.MassKg:F0}");
        Assert.NotEmpty(impacts);
        CrashImpact first = impacts.First(e => e.PieceId == -1);
        Assert.Equal(water ? ImpactSurface.Water : ImpactSurface.Land, first.Surface);
        Assert.All(impacts, e => Assert.Equal(water ? ImpactSurface.Water : ImpactSurface.Land, e.Surface));
    }

    [Fact]
    public void SeverityIsTheImpactEnergyAsAFractionOfTheBreakEnergy()
    {
        // (v / v_break)^2: 4 m/s against 8 m/s is a quarter, past 8 it is full.
        Assert.Equal(0.25, WreckSim.Severity(4.0, 8.0), 12);
        Assert.Equal(1.0, WreckSim.Severity(12.0, 8.0), 12);
        Assert.Equal(0.0, WreckSim.Severity(0.0, 8.0), 12);
    }

    // ---- orifice flooding ------------------------------------------------------------------------------------

    /// <summary>A 1 m cube with its top at the surface, full of air, holes top and bottom. Hand calculation: the air
    /// column is the whole cube, h = 1.0 m; Q = 0.61·A·sqrt(2·9.80665·1.0) / sqrt(1 + 1.225/1000) =
    /// 0.61 · 4.428690 / 1.000612 · A = 2.699845·A m^3/s. Four times the hole, four times the flow.</summary>
    [Theory]
    [InlineData(0.001, 0.0026998)]
    [InlineData(0.004, 0.0107994)]
    public void FloodRateFollowsTheOrificeLawAndGrowsWithTheOpening(double holeM2, double expectedQ)
    {
        var e = new HydroElement("cube", WreckAtom.Cabin, new Vec3(0, 0, 0), new Vec3(0.5, 0.5, 0.5), 1.0, 0.0, 0.0);
        e.Openings.Add(new Opening(new Vec3(0, 0, 0.5), holeM2, "bottom"));
        e.Openings.Add(new Opening(new Vec3(0, 0, -0.5), holeM2, "top"));
        // Centre 0.5 m under: top exactly at the surface (water at 0 m up, z down).
        var s = new RigidBodyState(new Vec3(0, 0, 0.5), Quat.Identity, Vec3.Zero, Vec3.Zero);
        e.Flood(Vec3.Zero, s, 0.0, 1e-3);
        _out.WriteLine($"A {holeM2} m2: Q {e.LastFlowM3s:F7} m3/s");
        Assert.Equal(expectedQ, e.LastFlowM3s, 6);
    }

    [Fact]
    public void ASealedCompartmentOnlySqueezesItsAir()
    {
        // A hole underneath and no way out on top: at 10 m (two atmospheres at the inside surface) the pocket halves,
        // no faster than that and no further — p0·V0 = p·V.
        var e = new HydroElement("tank", WreckAtom.Cabin, new Vec3(0, 0, 0), new Vec3(0.05, 0.05, 0.05), 0.001, 0.0, 0.0);
        e.Openings.Add(new Opening(new Vec3(0, 0, 0.05), 1e-4, "bottom only"));
        var s = new RigidBodyState(new Vec3(0, 0, 10.0), Quat.Identity, Vec3.Zero, Vec3.Zero);
        for (int i = 0; i < 1000; i++) e.Flood(Vec3.Zero, s, 0.0, 0.01);
        // p = 101325 + 1000·9.80665·d_inside with d_inside ≈ 10.0 m − 0.05 + 0.1·V/V0: V/V0 = 101325/(101325+98066·(9.95+0.1·x)) → x ≈ 0.5076.
        Assert.InRange(e.AirM3 / 0.001, 0.505, 0.510);
        Assert.Equal(1.0, e.AirFreeM3 / 0.001, 9);   // nothing escaped
    }

    // ---- wreck pieces ----------------------------------------------------------------------------------------

    [Fact]
    public void TheEnginePieceSinksAtOnceAndNeverComesBack()
    {
        (WreckSim sim, WreckPiece nose) = DropPiece(AirframeComponent.Nose);
        _out.WriteLine($"nose piece {nose.MassKg:F0} kg, reserve {nose.ReserveKg:F0} kg");
        double deepest = 0, sinceUnder = double.NaN, sinkingAt = double.NaN;
        bool wentUnder = false;
        for (int i = 0; i < (int)(120 / sim.Dt); i++)
        {
            sim.StepOnce();
            double d = nose.DepthM;
            if (!wentUnder && d > 0.5) wentUnder = true;
            if (wentUnder) { deepest = System.Math.Max(deepest, d); Assert.True(d > 0.3, $"t {sim.Time:F1}: nose back up to {d:F2} m"); }
            if (double.IsNaN(sinceUnder) && nose.Phase == WreckPhase.OnBottom) sinceUnder = sim.Time;
            if (double.IsNaN(sinkingAt) && nose.Sinking) sinkingAt = sim.Time;
        }
        _out.WriteLine($"sinking (no reserve left) at {sinkingAt:F1} s, on the bottom (30 m) after {sinceUnder:F1} s");
        Assert.True(wentUnder);
        // The cowl can carry a bubble of air down trapped above its torn face; the deeper it goes the more that bubble
        // is squeezed and the faster it sinks. It has lost its reserve within seconds and is on a 30 m bed within a minute.
        Assert.True(sinkingAt < 10, $"engine piece still had reserve at {sinkingAt:F1} s");
        Assert.True(sinceUnder < 60, $"engine took {sinceUnder:F1} s to reach the bed 30 m down");
    }

    /// <summary>
    /// A whole left wing (inner panel with its 75 L tank holding 40 kg, outer panel, aileron) torn off at the root,
    /// entering tip-first (rolled 80°) at 10 m/s down, 20 m/s along. It knifes in, comes back up, floats while its
    /// open root bay fills, hangs on the air in the outer panel, and sinks when that has leaked out.
    ///
    /// Hand bounds on the sink time, from the outer panel alone (void 0.217 m^3, skin leakage 4.4 cm^2 of which half
    /// lets water in): the fastest it can fill is at the full head of the panel stood on end, 2.9 m:
    /// Q = 0.61·2.2e-4·sqrt(2·9.81·2.9) = 1.0e-3 m^3/s, and it must lose at least 0.1 m^3 before the wing can go down
    /// (its reserve with the root bay flooded is about 100 kg) — so not before 100 s. The slowest is at a 0.1 m head:
    /// Q = 0.61·2.2e-4·sqrt(2·9.81·0.1) = 1.9e-4 m^3/s, all 0.217 m^3 in 1,150 s — so by 1,200 s.
    /// </summary>
    [Fact]
    public void AWingPlungesComesBackUpFloatsThenSinks()
    {
        (WreckSim sim, WreckPiece wing) = DropPiece(AirframeComponent.WingLeft, heightM: 4.0, rollDeg: 80);
        _out.WriteLine($"wing piece {wing.MassKg:F0} kg, reserve {wing.ReserveKg:F0} kg, air {wing.TrappedAirM3:F3} m3");
        double deepestPoint = double.MinValue, resurfaced = double.NaN, sank = double.NaN;
        for (int i = 0; i < (int)(1800 / sim.Dt) && double.IsNaN(sank); i++)
        {
            sim.StepOnce();
            if (sim.Time < 3) deepestPoint = System.Math.Max(deepestPoint, wing.LowestPoint().Z);   // water at 0: z is depth
            if (double.IsNaN(resurfaced) && deepestPoint > 1.0 && wing.DepthM < 0.0) resurfaced = sim.Time;
            if (double.IsNaN(sank) && wing.DepthM > 3.0) sank = sim.Time;
        }
        _out.WriteLine($"tip plunged to {deepestPoint:F2} m, centre back at the surface at {resurfaced:F1} s, sank (3 m) at {sank:F0} s");
        Assert.True(deepestPoint > 1.0, $"the wing only reached {deepestPoint:F2} m");
        Assert.False(double.IsNaN(resurfaced), "the wing never came back up");
        Assert.InRange(sank, 100.0, 1200.0);
        Assert.True(sank > resurfaced + 60, $"floated only {sank - resurfaced:F0} s");
    }

    [Fact]
    public void AFlatPieceIsStoppedAtTheSurfaceByTheSlam()
    {
        // Flat on, a wing shares its momentum with the water it has to set moving (von Kármán): its added mass
        // broadside is ρ·π·(c/2)²·span ≈ 1000·π·0.8²·4.9 ≈ 9,900 kg against 173 kg of wing, so 10 m/s becomes a few
        // tenths of a metre per second within the wing's own thickness. It does not go under.
        (WreckSim sim, WreckPiece wing) = DropPiece(AirframeComponent.WingLeft, heightM: 2.0, fwdMs: 0, downMs: 10);
        double vAfter = double.NaN, deepest = double.MinValue;
        for (int i = 0; i < (int)(3 / sim.Dt); i++)
        {
            sim.StepOnce();
            // Once its underside is a wing-thickness (0.19 m) into the water:
            if (double.IsNaN(vAfter) && wing.LowestPoint().Z > 0.19) vAfter = wing.WorldVelocity.Z;
            deepest = System.Math.Max(deepest, wing.DepthM);
        }
        _out.WriteLine($"down speed a thickness in: {vAfter:F2} m/s; deepest the centre went {deepest:F2} m");
        Assert.InRange(vAfter, -1.0, 1.5);
        Assert.True(deepest < 0.3, $"the flat wing's centre went {deepest:F2} m under");
    }

    [Fact]
    public void ImpactsAreRaisedOncePerFirstContactWithTheRightSurface()
    {
        var impacts = new List<CrashImpact>();
        CrashEvents.Impact += impacts.Add;
        (WreckSim wsim, WreckPiece wPiece) = DropPiece(AirframeComponent.TailBoom);
        for (int i = 0; i < (int)(60 / wsim.Dt); i++) wsim.StepOnce();
        Assert.Single(impacts, e => e.PieceId == wPiece.Id);
        CrashImpact w = impacts.Single(e => e.PieceId == wPiece.Id);
        Assert.Equal(ImpactSurface.Water, w.Surface);
        Assert.InRange(w.NormalSpeedMs, 8.0, 14.0);
        Assert.Equal(WreckSim.Severity(w.NormalSpeedMs, AirframeContact.CabinBreakMs), w.Severity01, 12);

        impacts.Clear();
        var land = new WreckSim(new FlatWreckWorld(null, 0.0));
        AircraftConfig c = Load("c172-like");
        var st = new RigidBodyState(new Vec3(0, 0, -2), Quat.Identity, new Vec3(15, 0, 8), Vec3.Zero);
        WreckPiece lp = land.Add("tail", AirframeComponent.TailBoom, new Aircraft(c, st).Hydro.Detach(AirframeComponent.TailBoom), c.Mass.CgVec(), st);
        for (int i = 0; i < (int)(30 / land.Dt); i++) land.StepOnce();
        Assert.Single(impacts);
        Assert.Equal(ImpactSurface.Land, impacts[0].Surface);
        Assert.Equal(lp.Id, impacts[0].PieceId);
        Assert.Equal(WreckPhase.OnLand, lp.Phase);
    }

    [Fact]
    public void FloodStateEscapeRatesIntegrateToTheAirLost()
    {
        var floods = new List<FloodState>();
        CrashEvents.Flood += floods.Add;
        (WreckSim sim, WreckPiece p) = DropPiece(AirframeComponent.TailBoom);
        double atHit = double.NaN, admittedAtHit = 0;
        for (int i = 0; i < (int)(600 / sim.Dt); i++)
        {
            double airBefore = p.TrappedAirFreeM3, admittedBefore = p.AdmittedFreeM3;
            sim.StepOnce();
            // From the start of the step it touched: everything after that is reported (before it, in the air, nothing escapes).
            if (double.IsNaN(atHit) && p.HitWater) { atHit = airBefore; admittedAtHit = admittedBefore; }
        }
        double integrated = floods.Where(f => f.PieceId == p.Id).Sum(f => f.AirEscapeM3s * sim.ReportIntervalS);
        // What left = what it had − what it has + what was let back in (a bobbing open end drains and refills).
        double admitted = p.AdmittedFreeM3 - admittedAtHit;
        double lost = atHit - p.TrappedAirFreeM3;
        _out.WriteLine($"{floods.Count} reports; integrated {integrated:F6} m3, lost {lost:F6} m3 (of {atHit:F4}), let back in {admitted:F6} m3");
        Assert.True(lost > 0.01);
        Assert.Equal(lost + admitted, integrated, 3);
    }

    // ---- the intact aeroplane --------------------------------------------------------------------------------

    /// <summary>Ditch a C172 gently (no break-up) and return the time its CG is 3 m under, or NaN if it is still up.</summary>
    private double DitchAndTimeTheSink(double maxSec, out Aircraft ac)
    {
        AircraftConfig c = Load("c172-like");
        using var lake = new FlatWater(10.0);   // 10 m of water over ground at 0
        // Set down slowly, nose up: 12 m/s along, 0.5 m/s down, 8°. (At 24 m/s the fixed gear digs in — WheelHydro —
        // and the aeroplane goes over onto its nose hard enough to break up; the probe shows that case.)
        var att = Pitch(8);
        ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(10.0 + 1.6)), att, att.Conjugate().Rotate(new Vec3(12, 0, 0.5)), Vec3.Zero));
        var loop = new SimLoop(ac);
        double dt = SimLoop.DefaultFixedDtSec;
        for (int i = 0; i < (int)(maxSec / dt); i++)
        {
            loop.RunFor(dt, new ControlInputs(0, 0, 0, 1.0));
            double depth = ac.State.Position.Z + 10.0;
            if (depth > 3.0) return i * dt;
        }
        return double.NaN;
    }

    [Fact]
    public void AnIntactDitchingFloatsFarLongerThanABrokenPiece()
    {
        double sinkIntact = DitchAndTimeTheSink(1200, out Aircraft ac);
        _out.WriteLine($"intact C172: lost {string.Join(",", ac.LostComponents)}; sank at {sinkIntact:F0} s; water inside {ac.Hydro.WaterInsideM3:F2} m3, air {ac.Hydro.TrappedAirM3:F2} m3");
        Assert.All(ac.LostComponents, lc => Assert.Equal(AirframeComponent.Propeller, lc));   // a prop strike, nothing else
        (WreckSim sim, WreckPiece tail) = DropPiece(AirframeComponent.TailBoom);
        double sinkPiece = double.NaN;
        for (int i = 0; i < (int)(1200 / sim.Dt) && double.IsNaN(sinkPiece); i++) { sim.StepOnce(); if (tail.DepthM > 3.0) sinkPiece = sim.Time; }
        _out.WriteLine($"tail boom piece sank at {sinkPiece:F0} s");
        Assert.True(double.IsNaN(sinkIntact) || sinkIntact > 5 * sinkPiece, $"intact {sinkIntact:F0} s vs piece {sinkPiece:F0} s");
        Assert.True(ac.Hydro.WaterInsideM3 > 0.01, "the cabin should be taking water through its door seals");
    }

    // ---- floats ----------------------------------------------------------------------------------------------

    private static Aircraft FloatplaneAtRest(out SimLoop loop)
    {
        AircraftConfig c = Load("pa18-floats-like");   // caller holds a FlatWater(10) scope
        // Keel 0.25 m into the water, level, at rest; it settles to its own draft.
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(10.0 + c.Floats!.KeelZ - 0.25)), Quat.Identity, Vec3.Zero, Vec3.Zero));
        loop = new SimLoop(ac);
        return ac;
    }

    private static double RollDeg(Quat q) => System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 180 / System.Math.PI;

    [Fact]
    public void FloatCompartmentsAreFourOfEqualVolume()
    {
        AircraftConfig c = Load("pa18-floats-like");
        var ff = new FloatFlooding(c);
        Assert.Equal(2, ff.Floats.Length);
        Assert.Equal(4, ff.Floats[0].Count);   // 14 CFR 23.751(b)
        double v0 = ff.Floats[0][0].VoidM3;
        foreach (HydroElement e in ff.Floats[0]) Assert.InRange(e.VoidM3 / v0, 0.97, 1.03);
        // The four add up to the float FloatHydro floats on: 2,000 lb-class EDO float ≈ 0.9-1.0 m^3 each.
        double total = ff.Floats[0].Sum(e => e.VoidM3);
        _out.WriteLine($"one float {total:F3} m3 = {total * 1000 * 2.2046:F0} lb displacement; compartment {v0:F3} m3");
        Assert.InRange(total, 0.8, 1.1);
    }

    [Fact]
    public void AnUndamagedFloatNeverTakesWater()
    {
        using var lake = new FlatWater(10.0);
        Aircraft ac = FloatplaneAtRest(out SimLoop loop);
        double z0 = double.NaN;
        for (int i = 0; i < (int)(900 / SimLoop.DefaultFixedDtSec); i++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, 0, 0, 1.0));
            if (i == (int)(20 / SimLoop.DefaultFixedDtSec)) z0 = ac.State.Position.Z;
        }
        _out.WriteLine($"after 15 min: float water {ac.FloatFlood!.WaterKg:F3} kg, CG moved {ac.State.Position.Z - z0:F4} m, roll {RollDeg(ac.State.Attitude):F2} deg");
        Assert.Equal(0.0, ac.FloatFlood!.WaterKg, 9);
        Assert.InRange(ac.State.Position.Z - z0, -0.02, 0.02);
        Assert.InRange(RollDeg(ac.State.Attitude), -1.0, 1.0);
    }

    [Fact]
    public void ALessonCanHoleAFloatAndTheAeroplaneListsTowardIt()
    {
        using var lake = new FlatWater(10.0);
        Aircraft ac = FloatplaneAtRest(out SimLoop loop);
        for (int i = 0; i < (int)(20 / SimLoop.DefaultFixedDtSec); i++) loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, 0, 0, 1.0));
        double roll0 = RollDeg(ac.State.Attitude);
        // The lesson hook: what PracticeScenario.Spawn applies from its FloatDamage list. All four compartments of the LEFT float.
        var damage = Enumerable.Range(0, 4).Select(k => FloatPuncture.RockStrike(0, k)).ToList();
        Assert.Equal(4, PracticeScenario.ApplyDamage(ac, damage));
        for (int i = 0; i < (int)(1200 / SimLoop.DefaultFixedDtSec); i++) loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, 0, 0, 1.0));
        double roll = RollDeg(ac.State.Attitude);
        _out.WriteLine($"left float water {ac.FloatFlood!.WaterKgIn(0):F0} kg, right {ac.FloatFlood.WaterKgIn(1):F0} kg; roll {roll0:F1} -> {roll:F1} deg");
        Assert.True(ac.FloatFlood.WaterKgIn(0) > 50, "the holed float should have flooded");
        Assert.Equal(0.0, ac.FloatFlood.WaterKgIn(1), 9);
        Assert.True(roll < roll0 - 5, $"should list to the LEFT (negative roll): {roll:F1} deg");
    }
}
