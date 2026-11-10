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
        Assert.Equal(0.90, st.Wings.Strength, 4);
        // THE TAIL DOES NOT BEND (Ty 2026-11-09, on the overstressed Extra: "the tail should not bend"). It used to keep
        // the same 10° set as the wings, because it was stepped at the wings' load ratio; it carries the balancing
        // download instead (StructuralState.TailDesignMargin), so this pull leaves it at 2.0 / 2.5 = 0.80 × its own
        // limit — inside it, nothing flexed, nothing booked.
        Assert.Equal(0.0, st.HorizontalTail.SetRad, 9);
        Assert.Equal(0.0, st.HorizontalTail.DeflectionRad, 9);
        Assert.Equal(0, st.HorizontalTail.Overstresses);
        Assert.Equal(0.80, st.HorizontalTail.PeakEffectiveRatio, 3);
        Assert.Equal(1.0, st.HorizontalTail.Strength, 9);
        // The shapes the aeroplane flies and is drawn at: every wing panel tip-up 10°, the engine nose-down 10°, the tail straight.
        Assert.NotEmpty(ac.Panels);
        foreach (WingPanel p in ac.Panels)
        {
            Assert.Equal(PanelState.Bent, p.State);
            Assert.Equal(10.0 * Deg, p.BendRad, 4);
            Assert.Equal(0.0, p.FlightFlexRad, 6);
        }
        Assert.Equal(10.0 * Deg, ac.EngineSagRad, 4);
        Assert.Equal(0.0, ac.TailBendRad, 9);
    }

    /// <summary>
    /// WHICH WAY A BENT TAIL GOES, if one is ever loaded to its own limit (Ty 2026-11-09: "the tail should not bend and if
    /// it did it would be the other way due to the direction of the load"). A pull is a DOWNLOAD on the tail — it balances
    /// the wing's nose-down pitching moment, and the tail's own mass hangs the same way — so the tips go DOWN under
    /// positive g and UP under negative g. The member's own sign is the opposite (OverstressMember: + is "the way positive
    /// g bends it"), which is why Aircraft.TailBendRad (+ = tips up) negates it. This locks the whole chain: anyone who
    /// "fixes" one of those two negations breaks this.
    /// </summary>
    [Fact]
    public void TheTailsTipsBendDOWNUnderPositiveGAndUpUnderNegative()
    {
        Aircraft ac = J3At(1.0);
        OverstressMember tail = ac.Structure.HorizontalTail!;
        // Straight to the member, because nothing in the sim loads the tail to its own limit any more. TailBendRad reads
        // the member, so no step of the aeroplane is needed (and one would unload it).
        for (int i = 0; i < 400; i++) tail.Step(1.5, Dt);
        _o.WriteLine($"positive g: tail DeflectionRad {tail.DeflectionRad / Deg:F2}°, TailBendRad {ac.TailBendRad / Deg:F2}° (+ = tips up)");
        Assert.Equal(10.0 * Deg, tail.DeflectionRad, 4);
        Assert.Equal(-10.0 * Deg, ac.TailBendRad, 4);   // tips DOWN in a pull
        for (int i = 0; i < 800; i++) tail.Step(-1.5, Dt);
        _o.WriteLine($"negative g: tail DeflectionRad {tail.DeflectionRad / Deg:F2}°, TailBendRad {ac.TailBendRad / Deg:F2}°");
        Assert.True(ac.TailBendRad > 0.0, $"a push must bend the tips UP, and TailBendRad is {ac.TailBendRad / Deg:F2}°");
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

        // WHAT THIS NOW ASSERTS, AND WHY IT CHANGED (Ty 2026-11-10, asked which way it should come out: "I have
        // never felt one just use the physics and call it good").
        //
        // It used to assert the whole aeroplane's pitch RATE goes further nose-down with the engine drooped, and
        // it had been failing: -0.0127 drooped against -0.0136 straight. Measured (DroopedEngineProbe, DROOP=1),
        // the PROPULSION is exactly right — at 10 deg a J-3's thrust puts 212.6 N down at the hub for a moment of
        // -225.6 N.m nose-down, which equals a closed-form tilt balance to the decimal, with no prop-disc term
        // swamping it. So the mechanism he asked for is there and correct.
        //
        // The whole-aeroplane rate is the sum of that and the SLIPSTREAM moving off the tail as the engine droops:
        // a J-3's stab carries a download at full power, and easing the wash over it is nose-up. Two real effects,
        // and at 10 deg they very nearly cancel. Asserting their net was asserting an outcome nobody had measured;
        // this asserts the mechanism instead, where the physics is checkable and checked.
        AircraftConfig j3 = Load("j3-cub-like");
        double myStraight = PropulsionMy(j3, 0.0), mySagged = PropulsionMy(j3, 10 * Deg);
        _o.WriteLine($"propulsion pitching moment at full power: straight {myStraight:F1}, drooped 10° {mySagged:F1} N.m (+ is nose up)");
        Assert.True(mySagged < myStraight - 50.0, "a drooped engine's thrust line must pitch the nose down");
        Assert.True(PropulsionMy(j3, 20 * Deg) < mySagged, "and more droop must pitch it down harder");
    }

    /// <summary>The propulsion's own pitching moment at full power with the mount drooped by <paramref name="sag"/>,
    /// N.m, + nose up: the prop run in its tilted frame and turned back, and the hub moved about the firewall —
    /// the same two steps Aircraft.MountedPropulsion takes.</summary>
    private static double PropulsionMy(AircraftConfig c, double sag)
    {
        Vec3 Rot(Vec3 v, double a) => new(v.X * System.Math.Cos(a) - v.Z * System.Math.Sin(a), v.Y,
                                          v.X * System.Math.Sin(a) + v.Z * System.Math.Cos(a));
        Vec3 vel = new(30, 0, 1.5);
        (Vec3 f0, Vec3 m0) = Aircraft.PropulsionForces(c, 1.0, Rot(vel, -sag), Vec3.Zero, 1.0, false, default, null);
        Vec3 f = Rot(f0, sag), m = Rot(m0, sag);
        (double noseCut, _) = AirframeContact.FuselageStations(c);
        Vec3 cg = c.Mass.CgVec();
        Vec3 arm = cg - new Vec3(noseCut, 0.0, cg.Z + c.Propulsion!.ThrustLineZ);
        m += Vec3.Cross(Rot(arm, sag) - arm, f);
        return m.Y;
    }

    // ---- the wings go and the bent tips go with them (Ty 2026-11-10) -----------------------------------------------

    /// <summary>
    /// Ty, flying 2026.1110.0834: "I bent the wings then over stressed it to the point of failure in flight and the bent
    /// wingtips were still there so that is not fixed." His manoeuvre exactly — bend the tips with a pull, then pull the
    /// wings off — and then the state that everything which DRAWS a tip reads.
    ///
    /// FailWings masked the wing surfaces, dropped the mass and detached the hydro, but never told the PANELS, so every
    /// tip stayed Bent on an aeroplane with no wings. The renderer could not win from there: StructuralDamage redraws a
    /// panel that is Bent or still carrying flex, whatever else is true of it.
    /// </summary>
    [Fact]
    public void WhenTheWingsDepartTheBentTipsGoWithThem()
    {
        Aircraft ac = J3At(1.1);
        Pull(ac, 1.6 * 3.8);                                   // "I bent the wings": a set, no failure
        Assert.False(ac.Structure.WingsFailed);
        _o.WriteLine("after the 1.6 pull: " + string.Join(", ",
            ac.Panels.Select(p => $"{p.Component} {p.State} bend {p.BendRad / Deg:F2}° flex {p.FlightFlexRad / Deg:F2}°")));
        Assert.Contains(ac.Panels, p => p.State == PanelState.Bent);   // there is a bent tip to lose
        double[] setAtFailure = ac.Panels.Select(p => p.BendRad).ToArray();

        Pull(ac, 2.0 * 3.8);                                   // "then over stressed it to the point of failure"
        Assert.True(ac.Structure.WingsFailed);
        _o.WriteLine("after the failure: " + string.Join(", ",
            ac.Panels.Select(p => $"{p.Component} {p.State} bend {p.BendRad / Deg:F2}° flex {p.FlightFlexRad / Deg:F2}° elastic {p.ElasticRad / Deg:F2}°")));

        // No wing panel is on the aeroplane any more — the defect itself.
        Assert.All(ac.Panels, p => Assert.Equal(PanelState.Broken, p.State));
        // And nothing asks for one to be drawn. This is StructuralDamage's own redraw test, read in the sim: a panel
        // that satisfies it gets ShowPanelBend called on it every frame, which is what kept the tip on screen.
        Assert.All(ac.Panels, p => Assert.False(
            p.State == PanelState.Bent || Math.Abs(p.ElasticRad) > 0.003 || Math.Abs(p.FlightFlexRad) > 0.003,
            $"{p.Component} still asks to be drawn bent: state {p.State}, elastic {p.ElasticRad:F4}, flex {p.FlightFlexRad:F4}"));
        // The PLASTIC set stays: the piece fell in the shape it was bent into, so the thrown wing is a BENT wing. Only
        // the load-dependent flex comes off, because a wing that is not there carries no load.
        for (int i = 0; i < ac.Panels.Count; i++)
            Assert.Equal(setAtFailure[i], ac.Panels[i].BendRad, 9);
        Assert.Contains(ac.Panels, p => Math.Abs(p.BendRad) > WingStructure.VisibleSetRad);
    }

    /// <summary>The same moment as the RECORDING holds it, so a replay of the overstress draws the tip on the wing that
    /// left rather than on the aeroplane: the shape timeline reads these very fields (Ty 2026-11-10, both halves of the
    /// report).</summary>
    [Fact]
    public void TheRecordedShapeAfterAWingFailureHasTheTipsBrokenAndStillBent()
    {
        Aircraft ac = J3At(1.1);
        Pull(ac, 1.6 * 3.8);
        FlyingGame.Sim.Replay.AirframeShape bent = FlyingGame.Sim.Replay.AirframeShape.Capture(10.0, ac);
        Assert.Contains(bent.Panels, p => p.State == PanelState.Bent);

        Pull(ac, 2.0 * 3.8);
        Assert.True(ac.Structure.WingsFailed);
        FlyingGame.Sim.Replay.AirframeShape gone = FlyingGame.Sim.Replay.AirframeShape.Capture(20.0, ac);
        Assert.All(gone.Panels, p => Assert.Equal(PanelState.Broken, p.State));
        Assert.Contains(gone.Panels, p => Math.Abs(p.BendRad) > WingStructure.VisibleSetRad);
        // Two different shapes, so the timeline keeps both: the replay can show the tip on the aeroplane before the
        // failure and on the debris after it.
        Assert.False(bent.SameShapeAs(gone));
    }
}
