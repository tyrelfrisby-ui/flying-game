using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty's overstress pattern, 2026-11-05 06:28 (verbatim in ~/Developer/agent-lanes/prompts/ty-overstress-pattern-1105.md):
/// "At the limit load the wing begins to visibly flex … 10 degrees at 1.5 and 20 degrees at 2.0 where they would snap and
/// depart … 50% of any bending after the ultimate load is permanent … 2% per .1 overage … So a single pull to 2.0 at gross
/// weight would permanently bend the wings and engine 10 degrees. If the airplane was above gross weight the wings would
/// snap off … but the engine would remain attached. Conversely a lightly loaded plane that pulls to 2.01 will not loose
/// its wings but will loose its engine because the fixed weight doesn't care about the total aircraft weight."
/// And 06:29: the Pitts' negative limit is −5 g, the pattern symmetric on the negative side.
/// Expected values are literals worked by hand from those sentences.
/// </summary>
[Collection("WorldTerrainActive")]
public class OverstressPatternTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public OverstressPatternTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; }
    public void Dispose() { WorldTerrain.Active = null; }

    private const double Deg = Math.PI / 180.0;
    private const double Dt = 0.005;

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static Aircraft Spawn(AircraftConfig c) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    /// <summary>The J-3 with its gross placard set so the wings are loaded <paramref name="weightRatio"/> × gross.</summary>
    private static Aircraft J3At(double weightRatio)
    {
        AircraftConfig c = Load("j3-cub-like");
        double m = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero), ControlDeflections.Neutral).MassProperties.MassKg;
        c.Loading ??= new LoadingConfig();
        c.Loading.MaxGrossMassKg = m / weightRatio;
        Aircraft ac = Spawn(c);
        Assert.Equal(weightRatio, ac.WingWeightRatio, 9);
        return ac;
    }

    /// <summary>Hold the load factor <paramref name="n"/> (air and whole aeroplane alike) for a second, then 1 g for half.</summary>
    private static void Pull(Aircraft ac, double n)
    {
        for (int i = 0; i < 200; i++) ac.HoldLoadFactor(n, n, Dt);
        for (int i = 0; i < 100; i++) ac.HoldLoadFactor(1.0, 1.0, Dt);
    }

    private static void Pull(OverstressMember m, double r)
    {
        for (int i = 0; i < 200 && !m.Broken; i++) m.Step(r, Dt);
        for (int i = 0; i < 200 && !m.Broken; i++) m.Step(0.5, Dt);
    }

    // ---- the curve ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheFlexStartsAtTheLimitAndIsTenDegreesAtOnePointFiveAndTwentyAtTwo()
    {
        Assert.Equal(0.0, OverstressMember.CurveRad(0.99), 12);
        Assert.Equal(0.0, OverstressMember.CurveRad(1.0), 12);
        Assert.Equal(5.0 * Deg, OverstressMember.CurveRad(1.25), 12);
        Assert.Equal(10.0 * Deg, OverstressMember.CurveRad(1.5), 12);
        Assert.Equal(20.0 * Deg, OverstressMember.CurveRad(2.0), 12);
        // Held at 1.25: 5° of flex, all of it elastic.
        var m = new OverstressMember("wing");
        for (int i = 0; i < 200; i++) m.Step(1.25, Dt);
        Assert.Equal(5.0 * Deg, m.DeflectionRad, 6);
        for (int i = 0; i < 200; i++) m.Step(1.0, Dt);
        Assert.Equal(0.0, m.SetRad, 12);
        Assert.Equal(0.0, m.DeflectionRad, 6);   // the response lag leaves 1.0 approached from above
        Assert.Equal(1.0, m.Strength, 12);
        Assert.Equal(0, m.Overstresses);
    }

    [Fact]
    public void HalfOfTheBendingPastUltimateStaysAndTheNextOneIsWeaker()
    {
        // 1.6: 12° at the peak, 6° stays, 2 % weaker.
        var m = new OverstressMember("wing");
        for (int i = 0; i < 200; i++) m.Step(1.6, Dt);
        Assert.Equal(12.0 * Deg, m.DeflectionRad, 6);
        for (int i = 0; i < 200; i++) m.Step(0.5, Dt);
        Assert.Equal(6.0 * Deg, m.SetRad, 6);
        Assert.Equal(6.0 * Deg, m.DeflectionRad, 6);
        Assert.Equal(0.98, m.Strength, 6);
        Assert.Equal(1, m.Overstresses);
        // 1.7 → 4 %, 2.0 → 10 %.
        Assert.Equal(0.04, OverstressMember.WeakeningFor(1.7), 12);
        Assert.Equal(0.10, OverstressMember.WeakeningFor(2.0), 12);
        Assert.Equal(0.0, OverstressMember.WeakeningFor(1.5), 12);
    }

    [Fact]
    public void RepeatedPullsToOnePointSixEndInAFailureBelowOnePointFive()
    {
        // Worked by hand: each pull to 1.6 is 1.6/s of what is left, so the overage (and the 2 % per 0.1) grows:
        // s = 0.98, 0.9540, 0.9202, 0.8763, 0.8191, 0.7449 after pulls 1..6; the sixth peaks at 1.953 of what was left
        // and holds, and after it the member breaks at 2 × 0.7449 = 1.490 of its original limit.
        var m = new OverstressMember("wing");
        double[] expect = { 0.98, 0.95400, 0.92017, 0.87626, 0.81914, 0.74488 };
        for (int k = 0; k < expect.Length; k++)
        {
            Pull(m, 1.6);
            Assert.False(m.Broken, $"pull {k + 1} to 1.6 broke it");
            Assert.Equal(expect[k], m.Strength, 4);
        }
        Assert.True(2.0 * m.Strength < 1.5, $"after six pulls it still breaks at {2 * m.Strength:F3}");
        for (int i = 0; i < 400 && !m.Broken; i++) m.Step(1.495, Dt);
        Assert.True(m.Broken, "a pull to 1.495 of the original limit did not break it");
        // The set is never more than half the bending at the break: 10°.
        Assert.InRange(m.SetRad, 9.0 * Deg, 10.0 * Deg + 1e-9);
    }

    [Fact]
    public void BelowTheLimitNothingFlexesAtAll()
    {
        Aircraft ac = J3At(1.0);
        int pops = 0; ac.Structure.OnPop += _ => pops++;
        for (int i = 0; i < 400; i++) ac.HoldLoadFactor(0.99 * 3.8, 0.99 * 3.8, Dt);
        Assert.Equal(0.0, ac.Structure.Wings.DeflectionRad, 12);
        Assert.Equal(0.0, ac.Structure.EngineMount!.DeflectionRad, 12);
        Assert.Equal(0.0, ac.Structure.HorizontalTail!.DeflectionRad, 12);
        Assert.Equal(0.0, ac.EngineSagRad, 12);
        Assert.All(ac.Panels, p => Assert.Equal(0.0, p.FlightFlexRad, 12));
        Assert.Equal(0, pops);
    }

    // ---- Ty's three cases ---------------------------------------------------------------------------------------------

    [Fact]
    public void APullToTwiceTheLimitAtGrossBendsWingsAndEngineTenDegreesAndNothingDeparts()
    {
        Aircraft ac = J3At(1.0);
        Pull(ac, 2.0 * 3.8);
        StructuralState st = ac.Structure;
        _o.WriteLine($"wings set {st.Wings.SetRad / Deg:F3}°, engine {st.EngineMount!.SetRad / Deg:F3}°, tail {st.HorizontalTail!.SetRad / Deg:F3}°, strength {st.Wings.Strength:F4}; lost [{string.Join(",", ac.LostComponents)}]");
        Assert.False(st.WingsFailed);
        Assert.Empty(ac.LostComponents);
        Assert.False(ac.EngineStopped);
        Assert.Equal(10.0 * Deg, st.Wings.SetRad, 4);
        Assert.Equal(10.0 * Deg, st.EngineMount.SetRad, 4);
        Assert.Equal(10.0 * Deg, st.HorizontalTail.SetRad, 4);
        Assert.Equal(0.90, st.Wings.Strength, 4);
        // The shapes the aeroplane flies and is drawn at: every wing panel tip-up 10°, the engine nose-down 10°, the tail tips down.
        Assert.NotEmpty(ac.Panels);
        foreach (WingPanel p in ac.Panels)
        {
            Assert.Equal(PanelState.Bent, p.State);
            Assert.Equal(10.0 * Deg, p.BendRad, 4);
            Assert.Equal(0.0, p.FlightFlexRad, 6);
        }
        Assert.Equal(10.0 * Deg, ac.EngineSagRad, 4);
        Assert.Equal(-10.0 * Deg, ac.TailBendRad, 4);
    }

    [Fact]
    public void OverGrossTheSamePullTakesTheWingsButTheEngineStays()
    {
        Aircraft ac = J3At(1.1);
        Pull(ac, 2.0 * 3.8);
        _o.WriteLine($"lost [{string.Join(",", ac.LostComponents)}], wings failed {ac.Structure.WingsFailed}, engine set {ac.Structure.EngineMount!.SetRad / Deg:F2}°");
        Assert.True(ac.Structure.WingsFailed, "2.0 × limit at 110 % of gross is 2.2 × on the wings: they must go");
        Assert.False(ac.IsLost(AirframeComponent.Nose), "the engine's mass is fixed: 2.0 × on its mount, it stays");
        Assert.False(ac.EngineStopped);
        Assert.Equal(10.0 * Deg, ac.Structure.EngineMount.SetRad, 4);
    }

    [Fact]
    public void LightTheWingsSurviveTwoPointZeroOneButTheEngineDeparts()
    {
        Aircraft ac = J3At(0.8);
        Pull(ac, 2.01 * 3.8);
        _o.WriteLine($"lost [{string.Join(",", ac.LostComponents)}], wings set {ac.Structure.Wings.SetRad / Deg:F2}°");
        Assert.False(ac.Structure.WingsFailed, "at 80 % of gross 2.01 × limit is 1.608 × on the wings");
        Assert.True(ac.IsLost(AirframeComponent.Nose), "2.01 × limit on the engine mount: it departs");
        Assert.True(ac.EngineStopped);
        // 1.608: half of 20° × 0.608 = 6.08° would stay — a little less, because the engine left with its mass halfway
        // through the pull and the wings had that much less to carry for the rest of it.
        Assert.InRange(ac.Structure.Wings.SetRad, 5.5 * Deg, 6.08 * Deg + 1e-9);
    }

    [Fact]
    public void ThePittsGoesToMinusFiveAndThePatternIsTheSameUpsideDown()
    {
        Aircraft p = Spawn(Load("pitts-s2b-like"));
        Assert.Equal(-5.0, p.Structure.LimitNegG, 6);
        Assert.Equal(6.0, p.Structure.LimitPosG, 6);
        double w = p.WingWeightRatio;
        Pull(p, -2.0 * 5.0 / w);
        _o.WriteLine($"Pitts at {w:F3} × gross, −2.0 × limit: wings set {p.Structure.Wings.SetRad / Deg:F2}°, engine {p.Structure.EngineMount!.SetRad / Deg:F2}°; lost [{string.Join(",", p.LostComponents)}]");
        Assert.False(p.Structure.WingsFailed);
        Assert.Equal(-10.0 * Deg, p.Structure.Wings.SetRad, 4);
        Assert.All(p.Panels, q => Assert.Equal(-10.0 * Deg, q.BendRad, 4));   // tips DOWN
        Aircraft p2 = Spawn(Load("pitts-s2b-like"));
        Pull(p2, -2.05 * 5.0 / p2.WingWeightRatio);
        Assert.True(p2.Structure.WingsFailed, "past −2.0 × the −5 g limit the wings go");
    }

    // ---- the bent shapes fly -------------------------------------------------------------------------------------------

    [Fact]
    public void BentWingsTailAndEngineChangeHowItFlies()
    {
        Aircraft bent = J3At(1.0);
        Pull(bent, 2.0 * 3.8);
        AircraftConfig c = bent.Config;
        var tables = Aircraft.BuildAirfoilTables(c);
        int n = c.Surfaces.Sum(s => s.Strips.Count);
        StripFlowState shaped = bent.FlowStateForTests;
        (Vec3 Force, Vec3 Moment) Aero(bool isBent, double betaRad)
        {
            var fs = new StripFlowState(); fs.EnsureSize(n);
            if (isBent) { fs.BendRad = shaped.BendRad; fs.BendOffset = shaped.BendOffset; }
            Vec3 v = new Vec3(30 * Math.Cos(betaRad), 30 * Math.Sin(betaRad), 1.5);
            return AeroModel.Compute(c, tables, v, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, -1.0, fs, 0, 0, null, null);
        }
        double beta = 5 * Deg;
        double clbIntact = (Aero(false, beta).Moment.X - Aero(false, -beta).Moment.X) / (2 * beta);
        double clbBent = (Aero(true, beta).Moment.X - Aero(true, -beta).Moment.X) / (2 * beta);
        _o.WriteLine($"roll moment per rad of sideslip: intact {clbIntact:F0}, bent 10° {clbBent:F0} N·m/rad");
        Assert.True(clbIntact < 0 && clbBent < 1.1 * clbIntact, "10° more dihedral on the outer panels must strengthen the dihedral effect (roll away from the slip)");

        // The drooped engine at full power, and nothing else bent: its thrust line points down at the nose, so it
        // pitches the nose down.
        Aircraft straight = J3At(1.0), sagged = J3At(1.0);
        sagged.Structure.EngineMount!.Impose(10 * Deg);
        Assert.Equal(10 * Deg, sagged.EngineSagRad, 12);
        double QAfter(Aircraft ac)
        {
            ac.State = new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(30, 0, 1.5), Vec3.Zero);
            var sim = new SimLoop(ac);
            sim.RunFor(0.05, new ControlInputs(0, 0, 0, -1.0));
            return ac.State.Rates.Y;
        }
        double qStraight = QAfter(straight), qSagged = QAfter(sagged);
        _o.WriteLine($"pitch rate after 50 ms at full power: straight {qStraight:F4}, engine drooped 10° {qSagged:F4} rad/s");
        Assert.True(qSagged < qStraight - 1e-3, "a drooped engine at full power must pitch the nose down");
    }
}
