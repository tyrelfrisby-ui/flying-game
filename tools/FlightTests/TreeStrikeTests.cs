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
/// Crashes into trees and what survives them (Ty 2026-11-03: "i flew into the tree tops at high speed and it just broke
/// apart and stopped … if a wing hits just that wing breaks off and everything should retain its momentum").
///
/// The dead stop's cause, measured on the base (d9a3eee8, TreeStrikeProbe): the forest was a WorldSolids cylinder of the
/// CROWN's radius from the ground to the top, pushed back with the hard-ground spring. A Pitts at 60 m/s, 1.5 m below the
/// tops, went from 60 to 5.5 m/s in 0.1 s against a neighbour's crown 20 m short of the tree aimed at, and lost nose,
/// cabin, tail boom and a wing by closing speed.
///
/// Trees here are a <see cref="FixedTrees"/> list on flat ground at 0 (WorldTerrain.Active = null), so each test knows its
/// stems exactly. Every expected value is a literal worked by hand from TreeStructure's constants (H/D 60, MOR 53 MPa,
/// 600 kg/m³, failure arm 2 d) and the configs.
/// </summary>
[Collection("WorldTerrainActive")]
public class TreeStrikeTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public TreeStrikeTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; CrashEvents.ClearForTests(); }
    public void Dispose() { WorldTerrain.Active = null; CrashEvents.ClearForTests(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static Forest.Tree Tree(double x, double y, double h) => new(x, y, 0.0, h, 0.22 + h / 24.0, 0, 0);

    /// <summary>Level, wings level, heading north at <paramref name="v"/> with its CG at <paramref name="up"/>.</summary>
    private static Aircraft Flying(AircraftConfig c, double v, double up, Vec3? rates = null) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -up), Quat.Identity, new Vec3(v, 0, 0), rates ?? Vec3.Zero), ControlDeflections.Neutral);

    private static void Run(Aircraft ac, double s) { var sim = new SimLoop(ac); sim.RunFor(s, new ControlInputs(0, 0, 0, -1)); }

    private static Vec3 WorldVel(Aircraft ac) => ac.State.Attitude.Rotate(ac.State.Velocity);

    // ---- the tree itself ---------------------------------------------------------------------------------------------

    [Fact]
    public void AStemIsAWoodenConeThatSnapsByItsDiameter()
    {
        var t = Tree(0, 0, 30);
        // H/D 60: 0.50 m at the ground; at 20 m up, a third of the height is left: 0.1667 m.
        Assert.Equal(0.500, TreeStructure.BaseDiameterM(t), 3);
        Assert.Equal(0.1667, TreeStructure.DiameterAt(t, 20.0), 4);
        // MOR·π·d²/64: 53e6·π·0.01/64 = 26,016 N at 10 cm; 53e6·π·0.0025/64 = 6,504 N at 5 cm.
        Assert.Equal(26016, TreeStructure.BreakForceN(0.10), 0);
        Assert.Equal(6504, TreeStructure.BreakForceN(0.05), 0);
        // The top 10 m above 20 m: 600·π/12·0.1667²·10 = 43.6 kg.
        Assert.Equal(43.6, TreeStructure.MassAboveKg(t, 20.0), 1);
        // Crown: none below 20 % of the height (6 m), widest there (2.4·(0.22 + 30/24) = 3.528 m), none at the top.
        Assert.Equal(0.0, TreeStructure.CrownRadiusAt(t, 5.9));
        Assert.Equal(3.528, TreeStructure.CrownRadiusAt(t, 6.0), 3);
        Assert.Equal(0.0, TreeStructure.CrownRadiusAt(t, 30.0), 6);
    }

    // ---- 1. only the part that is hit -------------------------------------------------------------------------------

    [Fact]
    public void AWingtipIntoATrunkTakesOnlyThatPanelAndTheAeroplaneFliesOn()
    {
        var c = Load("pa18-cub-like");
        var ac = Flying(c, 45, 10);
        WingPanel tip = ac.Panels.First(p => p.Component == AirframeComponent.WingTipRight);
        double y = tip.BreakY + 0.6 * (tip.TipY - tip.BreakY);
        _o.WriteLine($"PA-18 right panel: station {tip.BreakY:0.000} tip {tip.TipY:0.000}, ultimate {tip.UltimateNm:0}");
        // A 25 m fir: 0.417 m through at the ground, 0.250 m at 10 m — 97.6 kN, far stronger than a wing panel.
        ac.Trees = new FixedTrees(Tree(30, y, 25));
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        var twin = Flying(c, 45, 10);
        Run(ac, 1.0); Run(twin, 1.0);
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine(r.ToString());
        _o.WriteLine("lost: " + string.Join(", ", ac.LostComponents));
        Assert.Contains(ac.TreeStrikeLog, r => r.Outcome == TreeStrikeOutcome.PartBroken && r.Component == AirframeComponent.WingTipRight);
        Assert.True(ac.IsLost(AirframeComponent.WingTipRight));
        // That panel and the aileron on it, and NOTHING else: no other wing, no fuselage section, no tail.
        Assert.All(ac.LostComponents, comp => Assert.Contains(comp, new[] { AirframeComponent.WingTipRight, AirframeComponent.AileronRight }));
        // The panel is a wreck piece of its own, and the trunk stopped it where it struck.
        WreckPiece piece = relay.Wreck.Pieces.First(p => p.Component == AirframeComponent.WingTipRight);
        Assert.True(piece.MassKg > 1.0);
        _o.WriteLine($"piece {piece.MassKg:0.0} kg, now {piece.WorldVelocity.Length:0.0} m/s");
        Assert.True(piece.WorldVelocity.X < 20.0, $"the trunk holds the panel: {piece.WorldVelocity.X:0.0} m/s forward");
    }

    [Fact]
    public void TheRestOfTheAirframeKeepsItsMomentum()
    {
        // Threshold, from the impulse the strike can take out of the airframe: the panel's joint carries at most its
        // failure force F_c while the edge crushes FailureTravelM = 5 cm, J = F_c·δ / v_n. The PA-18 panel gives way at
        // F_c ≈ 32 kN at 45 m/s (outboard of the lift-strut fitting at 3.40 m): J ≈ 32 kN·0.05 / 45 ≈ 36 N·s on the
        // ~780 kg left — 0.05 m/s. Measured on the step of the
        // strike against an untouched twin flown in lock-step (the change across that one step, so what the crown's branches
        // took before it is not counted); 0.1 m/s allows
        // a tenth of a percent of the speed. After it, the aeroplane is a different shape and flies differently — that is
        // aerodynamics, not the strike, and it is checked only to keep 98 % of its speed a second on.
        var c = Load("pa18-cub-like");
        var ac = Flying(c, 45, 10);
        WingPanel tip = ac.Panels.First(p => p.Component == AirframeComponent.WingTipRight);
        ac.Trees = new FixedTrees(Tree(30, tip.BreakY + 0.6 * (tip.TipY - tip.BreakY), 25));
        var twin = Flying(c, 45, 10);
        var inputs = new ControlInputs(0, 0, 0, -1);
        double strikeDv = double.NaN;
        for (int i = 0; i < 200; i++)
        {
            int before = ac.TreeStrikeLog.Count;
            Vec3 d0 = WorldVel(ac) - WorldVel(twin);   // the crown's branches have already cost it a little
            ac.Step(inputs, 0.005); twin.Step(inputs, 0.005);
            if (before == 0 && ac.TreeStrikeLog.Count > 0) strikeDv = (WorldVel(ac) - WorldVel(twin) - d0).Length;
        }
        TreeStrikeRecord hit = ac.TreeStrikeLog.First(r => r.Outcome == TreeStrikeOutcome.PartBroken);
        _o.WriteLine($"impulse on the airframe {hit.ImpulseNs:0.0} N·s (F_c {hit.PartStrengthN / 1000:0.0} kN at {hit.ClosingMs:0.0} m/s); " +
                     $"Δv on the strike step {strikeDv:0.0000} m/s; speed a second on {WorldVel(ac).Length:0.00} vs {WorldVel(twin).Length:0.00}");
        Assert.True(hit.ImpulseNs < 100.0, $"{hit.ImpulseNs:0.0} N·s");
        Assert.True(strikeDv < 0.1, $"Δv {strikeDv:0.0000}");
        Assert.True(WorldVel(ac).Length > 0.98 * WorldVel(twin).Length);
    }

    // ---- 2. momentum at separation ----------------------------------------------------------------------------------

    [Fact]
    public void APieceLeavesWithVPlusOmegaCrossRAndConservesMomentum()
    {
        var c = Load("pa18-cub-like");
        Vec3 w = new(0.8, -0.3, 0.5);
        var ac = Flying(c, 40, 200, w);
        ac.Step(new ControlInputs(0, 0, 0, -1), 0.005);   // settle the loading model before the books are taken
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        RigidBodyState s = ac.State;
        MassProperties m0 = ac.MassProperties; Vec3 c0 = ac.CgShiftBody;
        ac.LoseComponent(AirframeComponent.WingTipRight);
        MassProperties m1 = ac.MassProperties; Vec3 c1 = ac.CgShiftBody;
        Assert.Equal(s.Velocity.X, ac.State.Velocity.X, 12);   // separation is not an impulse
        var pieces = relay.Wreck.Pieces;
        Assert.NotEmpty(pieces);
        Vec3 P = c.Mass.CgVec();

        // Linear momentum, body axes: before M·(v_P + ω×c); after the remainder's and each piece's.
        Vec3 before = (s.Velocity + Vec3.Cross(s.Rates, c0)) * m0.MassKg;
        Vec3 after = (s.Velocity + Vec3.Cross(s.Rates, c1)) * m1.MassKg;
        // Angular momentum about P, body axes: I_P·ω + M c × v_P (the sim's own reference point).
        Vec3 lBefore = m0.Times(s.Rates) + Vec3.Cross(c0, s.Velocity) * m0.MassKg;
        Vec3 lAfter = m1.Times(s.Rates) + Vec3.Cross(c1, s.Velocity) * m1.MassKg;
        foreach (WreckPiece p in pieces)
        {
            Vec3 r = p.CgBody - P;
            Vec3 vb = s.Attitude.Conjugate().Rotate(p.WorldVelocity);
            // v_piece = v + ω × r, exactly, and the airframe's own ω.
            Vec3 expect = s.Velocity + Vec3.Cross(s.Rates, r);
            Assert.Equal(expect.X, vb.X, 9); Assert.Equal(expect.Y, vb.Y, 9); Assert.Equal(expect.Z, vb.Z, 9);
            Assert.Equal(s.Rates.X, p.State.Rates.X, 12); Assert.Equal(s.Rates.Z, p.State.Rates.Z, 12);
            after += vb * p.MassKg;
            lAfter += p.MassProps.Times(p.State.Rates) + Vec3.Cross(r, vb) * p.MassKg;
            _o.WriteLine($"{p.Name}: {p.MassKg:0.00} kg at r ({r.X:0.00}, {r.Y:0.00}, {r.Z:0.00})");
        }
        _o.WriteLine($"p: {before} → {after};  L: {lBefore} → {lAfter}");
        Assert.Equal(before.X, after.X, 6); Assert.Equal(before.Y, after.Y, 6); Assert.Equal(before.Z, after.Z, 6);
        Assert.Equal(lBefore.X, lAfter.X, 4); Assert.Equal(lBefore.Y, lAfter.Y, 4); Assert.Equal(lBefore.Z, lAfter.Z, 4);
        // The remainder carries the products a one-sided loss leaves it.
        Assert.NotEqual(0.0, m1.Ixy);
    }

    // ---- 3. impacts take only what they absorb ----------------------------------------------------------------------

    [Fact]
    public void AHighSpeedTreetopStrikeCostsAModestDeltaVNotAStop()
    {
        // Pitts at 70 m/s straight through the top of a 30 m fir, 2 m below its top: the stem there is 3.3 cm through
        // (0.5·2/30), 3.6 kN — it snaps; the top it throws is 600·π/12·0.033²·2 = 0.34 kg.
        var c = Load("pitts-s2b-like");
        var ac = Flying(c, 70, 28);
        ac.Trees = new FixedTrees(Tree(40, 0, 30));
        var twin = Flying(c, 70, 28);
        Run(ac, 1.2); Run(twin, 1.2);
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine(r.ToString());
        double dv = (WorldVel(ac) - WorldVel(twin)).Length;
        _o.WriteLine($"speed {WorldVel(ac).Length:0.0} vs {WorldVel(twin).Length:0.0}; |Δv| {dv:0.000} m/s; lost [{string.Join(",", ac.LostComponents)}]");
        Assert.NotEmpty(ac.TreeStrikeLog);
        Assert.All(ac.TreeStrikeLog, r => Assert.Equal(TreeStrikeOutcome.StemSnapped, r.Outcome));
        Assert.Empty(ac.LostComponents);
        Assert.True(dv < 1.0, $"Δv {dv:0.00}");
    }

    [Fact]
    public void AGlancingWingtipOnTheGroundAtSpeedDoesNotStopTheAeroplane()
    {
        // Pitts at 60 m/s, banked 10° right and sinking 1.5 m/s onto flat ground with its lower right tip 2 cm up.
        var c = Load("pitts-s2b-like");
        double bank = 10 * Math.PI / 180;
        var att = new Quat(Math.Sin(bank / 2), 0, 0, Math.Cos(bank / 2));
        double low = double.MaxValue;
        foreach (ContactPoint p in AirframeContact.BuildPoints(c)) if (!p.PropDisc) low = Math.Min(low, -att.Rotate(p.Body - c.Mass.CgVec()).Z);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, low - 0.02), att, att.Conjugate().Rotate(new Vec3(60, 0, 1.5)), Vec3.Zero), ControlDeflections.Neutral);
        Run(ac, 0.5);
        double v = WorldVel(ac).Length;
        _o.WriteLine($"after 0.5 s: {v:0.0} m/s, lost [{string.Join(",", ac.LostComponents)}]");
        Assert.True(v > 45.0, $"{v:0.0} m/s");
    }

    // ---- 4. what is left flies like what is left ---------------------------------------------------------------------

    [Fact]
    public void LosingOneWingPanelRollsTowardTheMissingWing()
    {
        var c = Load("pa18-cub-like");
        var ac = Flying(c, 45, 500);
        Run(ac, 0.2);
        ac.LoseComponent(AirframeComponent.WingTipRight);
        Run(ac, 0.6);
        // Body roll + = right wing down.
        _o.WriteLine($"p {ac.State.Rates.X * 180 / Math.PI:0.0} deg/s");
        Assert.True(ac.State.Rates.X > 0.2, $"p {ac.State.Rates.X:0.000} rad/s");
        var ac2 = Flying(c, 45, 500);
        Run(ac2, 0.2);
        ac2.LoseComponent(AirframeComponent.WingTipLeft);
        Run(ac2, 0.6);
        Assert.True(ac2.State.Rates.X < -0.2, $"p {ac2.State.Rates.X:0.000} rad/s");
    }

    // ---- 5. progressive break-up ------------------------------------------------------------------------------------

    [Fact]
    public void ASecondTrunkTakesTheNextPartByTheSameRule()
    {
        // First a trunk at the right tip panel; 25 m on, a bigger one (35 m, 0.40 m through at 11 m: 424 kN) at the right
        // INNER wing, 2.5 m out, where the wing's root carries the blow on a 2 m lever.
        var c = Load("pa18-cub-like");
        var ac = Flying(c, 45, 10);
        WingPanel tip = ac.Panels.First(p => p.Component == AirframeComponent.WingTipRight);
        ac.Trees = new FixedTrees(Tree(30, tip.BreakY + 0.6 * (tip.TipY - tip.BreakY), 25), Tree(55, 2.5, 35));
        Run(ac, 1.5);
        foreach (var r in ac.TreeStrikeLog) _o.WriteLine(r.ToString());
        Assert.True(ac.IsLost(AirframeComponent.WingTipRight));
        Assert.True(ac.IsLost(AirframeComponent.WingRight));
        Assert.False(ac.IsLost(AirframeComponent.WingLeft));
        Assert.False(ac.IsLost(AirframeComponent.Cabin));
        Assert.True(WorldVel(ac).Length > 35.0, $"{WorldVel(ac).Length:0.0} m/s");
    }

    [Fact]
    public void ASlowTaxiIntoATrunkStopsWithoutBreakingAnything()
    {
        // 2 m/s into a 0.4 m trunk with the nose: demand 2·√(150 kN/m·m_eff) ≈ 20 kN, short of both strengths.
        var c = Load("pa18-cub-like");
        var ac = Flying(c, 2, 1.5);
        ac.Trees = new FixedTrees(Tree(3, 0, 24));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100; i++) sim.RunFor(0.01, new ControlInputs(0, 0, 0, 1));
        foreach (var r in ac.TreeStrikeLog.Take(3)) _o.WriteLine(r.ToString());
        Assert.Contains(ac.TreeStrikeLog, r => r.Outcome == TreeStrikeOutcome.Stopped);
        Assert.DoesNotContain(ac.TreeStrikeLog, r => r.Outcome != TreeStrikeOutcome.Stopped);
    }
}
