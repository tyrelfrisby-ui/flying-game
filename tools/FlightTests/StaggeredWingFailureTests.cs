using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// STAGGERED, PROGRESSIVE WING FAILURE — Ty 2026-11-10, verbatim:
///
///   "Create a staggered failure where outboard sections break off first unless the overstress is severe (rapid onset
///    1 g over breaking point for outboards) and account for aileron load so one tip could break off but not the other
///    in some situations. This for strut braced. For cantilever have the wings fold up 1 g before departing using same
///    logic and continue to account for their aerodynamic force and treat them independently based on aileron etc so
///    one wing could fold while the other doesn't or one wing could fold while the other departs. We just need more
///    progressive stagger to the failures."
///
/// One test per rule, so a regression says which rule it broke. The law and the derivations are in
/// <see cref="WingFailureLaw"/>; <see cref="StructuralState"/>'s StepWings is the ladder.
///
/// A NOTE ON ONSET, because it bites every test here. The member feels the load through a 50 ms lag, and "rapid onset"
/// is measured as the gap between what the air is asking for and what the structure has felt
/// (<see cref="OverstressMember.CommandedRatio"/>). So HOLDING a load factor from one g — which is what
/// <see cref="Aircraft.HoldLoadFactor"/> does — is itself the most rapid onset there is: the demand is at its full
/// value on the first step. A SLOW pull has to be ramped, and <see cref="Ramp"/> does that.
/// </summary>
[Collection("WorldTerrainActive")]
public sealed class StaggeredWingFailureTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public StaggeredWingFailureTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; }
    public void Dispose() { WorldTerrain.Active = null; }

    private const double Deg = Math.PI / 180.0;
    private const double Dt = 0.005;

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static Aircraft Spawn(AircraftConfig c) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    /// <summary>The type with its gross placard set so the wings are loaded exactly <paramref name="weightRatio"/> ×
    /// gross — the same rig <see cref="OverstressPatternTests"/> uses, so one g in the wings' own units is
    /// weightRatio / published limit and the expected numbers can be worked by hand.</summary>
    private static Aircraft At(string type, double weightRatio = 1.0)
    {
        AircraftConfig c = Load(type);
        double m = Spawn(c).MassProperties.MassKg;
        c.Loading ??= new LoadingConfig();
        c.Loading.MaxGrossMassKg = m / weightRatio;
        Aircraft ac = Spawn(c);
        Assert.Equal(weightRatio, ac.WingWeightRatio, 9);
        return ac;
    }

    /// <summary>One g in the wings' own units (multiples of their published limit) for this aeroplane.</summary>
    private static double OneG(Aircraft ac) => ac.WingWeightRatio / ac.Config.Limits.PublishedLimits().Pos;

    /// <summary>A load factor HELD, both wings alike, for <paramref name="sec"/> seconds. The demand is at its full
    /// value from the first step, so this is a rapid onset.</summary>
    private static void Hold(Aircraft ac, double n, double sec = 1.0)
    {
        for (int i = 0; i < (int)(sec / Dt); i++) ac.HoldLoadFactor(n, n, Dt);
    }

    /// <summary>A SLOW pull: the load factor ramped from one g to <paramref name="n"/> over <paramref name="sec"/>
    /// seconds and then held. Slow means slow against the structure's own 50 ms response, so the demand and what the
    /// structure feels stay within a whisker of each other and the onset is never severe.</summary>
    private static void Ramp(Aircraft ac, double n, double sec = 2.0, double holdSec = 0.5)
    {
        int steps = (int)(sec / Dt);
        for (int i = 0; i < steps; i++)
        {
            double u = (i + 1.0) / steps;
            ac.HoldLoadFactor(1.0 + (n - 1.0) * u, 1.0 + (n - 1.0) * u, Dt);
        }
        Hold(ac, n, holdSec);
    }

    /// <summary>A rolling pull without flying one: each wing held at its own load factor.</summary>
    private static void HoldAsymmetric(Aircraft ac, double nLeft, double nRight, double sec = 1.0)
    {
        for (int i = 0; i < (int)(sec / Dt); i++) ac.HoldWingLoadFactors(nLeft, nRight, 0.5 * (nLeft + nRight), Dt);
    }

    private string Report(Aircraft ac) => string.Join(" | ", new[] { -1, 1 }.Select(s =>
    {
        WingSideStructure w = ac.Structure.Wing(s);
        return $"{(s < 0 ? "L" : "R")}: {w.Stage} braced {w.Braced} eff {w.Member.EffectiveRatio:F3} set {w.Member.SetRad / Deg:F2}° " +
               $"fold {w.FoldRad / Deg:F1}° strength {w.Member.Strength:F3}";
    })) + $"  lost [{string.Join(",", ac.LostComponents)}]";

    // ---- RULE 1: a strut-braced wing sheds its OUTBOARD sections first ----------------------------------------------

    /// <summary>
    /// "outboard sections break off first unless the overstress is severe". A SLOW pull to just past the breaking point
    /// takes a strut-braced wing's outboard panels and nothing else: the braced inner bay is still on the aeroplane,
    /// still lifting, and the wings have NOT failed.
    /// </summary>
    [Fact]
    public void ASlowPullPastTheBreakTakesOnlyAStrutBracedWingsOutboardSections()
    {
        Aircraft ac = At("j3-cub-like");
        Assert.True(ac.Structure.LeftWing.Braced && ac.Structure.RightWing.Braced, "the J-3 is strut-braced");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        _o.WriteLine($"J-3: published limit {limit:F2} g, one g = {OneG(ac):F4} of it; outboard sections at " +
                     $"{WingFailureLaw.StageRatio(OneG(ac)) * limit:F2} g, the whole wing at {2.0 * limit:F2} g");

        Ramp(ac, WingFailureLaw.StageRatio(OneG(ac)) * limit * 1.01, sec: 3.0);
        _o.WriteLine($"after a 3 s ramp to {WingFailureLaw.StageRatio(OneG(ac)) * 1.01:F3} × limit: " + Report(ac));

        Assert.False(ac.Structure.WingsFailed, "a slow pull just past the break must not take the wings off");
        Assert.Equal(WingSideStage.OutboardGone, ac.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.OutboardGone, ac.Structure.RightWing.Stage);
        // The outboard panels really have gone — through the same path a strike that broke them uses.
        Assert.All(ac.Panels, p => Assert.Equal(PanelState.Broken, p.State));
        Assert.Contains(AirframeComponent.WingTipLeft, ac.LostComponents);
        Assert.Contains(AirframeComponent.WingTipRight, ac.LostComponents);
        // And the wing itself is still there: the inner bay's rows are still flying.
        Assert.False(ac.IsLost(AirframeComponent.WingLeft));
        Assert.False(ac.IsLost(AirframeComponent.WingRight));
        Assert.True(ac.StripMask!.Count(b => b) > 0);
        Assert.Contains(ac.Panels, p => ac.StripMask is { } m && p.SideStrips.Any(i => m[i]));
    }

    /// <summary>
    /// "unless the overstress is severe (rapid onset 1 g over breaking point for outboards)". The same aeroplane and the
    /// same peak load as the slow pull above, SNATCHED: the outboard rung is skipped altogether and the whole wing goes
    /// at its own breaking point, both sides. The only difference between this test and the one above is how fast the
    /// load arrived — the slow pull ends up at the same place and leaves the wing on.
    /// </summary>
    [Fact]
    public void ASevereSnatchSkipsTheOutboardRungAndTakesTheWholeWing()
    {
        Aircraft ac = At("j3-cub-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        double severe = 2.05 * limit;
        _o.WriteLine($"snatching straight to {severe:F2} g, past the {2.0 * limit:F2} g break");
        bool raised = false;
        ac.Structure.OnWingsFailed += () => raised = true;

        Hold(ac, severe, sec: 0.5);
        _o.WriteLine("after the snatch: " + Report(ac));

        Assert.Equal(WingSideStage.Departed, ac.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.Departed, ac.Structure.RightWing.Stage);
        Assert.True(ac.Structure.WingsFailed);
        Assert.True(raised, "both wings gone must raise OnWingsFailed");
    }

    /// <summary>
    /// HOW FAR THE SNATCH GOES decides whether the tips go first or the wing goes whole. A snatch that stops short of
    /// one g beyond the outboard rung is not severe: the tips go and the wing stays, however suddenly it arrived. A
    /// snatch that carries one g past the rung — which is the wing's own breaking point — skips the rung and takes the
    /// wing. Replace the lag with a peak comparison and every row of this table reads the same.
    /// </summary>
    [Theory]
    [InlineData(0.05, false)]   // a snatch barely past the outboard rung: the tips, nothing more
    [InlineData(0.90, false)]   // nine tenths of a g past it: still only the tips
    [InlineData(1.05, true)]    // one g past the rung is the wing's own break: it goes whole
    [InlineData(2.00, true)]
    public void HowFarBeyondTheOutboardRungTheSnatchGoesDecidesWhetherTheWingGoesWhole(double gBeyond, bool wholeWing)
    {
        Aircraft ac = At("j3-cub-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        double oneG = OneG(ac);
        Hold(ac, (WingFailureLaw.StageRatio(oneG) + gBeyond * oneG) * limit, sec: 0.5);
        _o.WriteLine($"snatch to the rung + {gBeyond:F2} g: " + Report(ac));
        Assert.Equal(wholeWing ? WingSideStage.Departed : WingSideStage.OutboardGone, ac.Structure.LeftWing.Stage);
        Assert.Equal(wholeWing, ac.Structure.WingsFailed);
    }

    /// <summary>
    /// THE RUNG IS A RUNG, NOT A REPRIEVE: keep pulling after the tips have gone and the wing follows them at its own
    /// breaking point, in the same pull. Two separate events, one after the other, where before 2026-11-10 there was
    /// one — which is the whole of "we just need more progressive stagger to the failures".
    /// </summary>
    [Fact]
    public void KeepPullingAfterTheTipsHaveGoneAndTheWingFollowsAtItsOwnBreak()
    {
        Aircraft ac = At("j3-cub-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        var seen = new List<string>();

        // One slow pull, all the way through both rungs, watched step by step. It has to go a long way past 2.0 ×
        // the limit for the second rung, because the outboard sections took their MASS with them: the aeroplane is
        // lighter, so the same load factor is less load on what is left of the wings (LimitsConfig.WingWeightRatio).
        int steps = 1200;
        for (int i = 0; i < steps; i++)
        {
            double n = 1.0 + (3.4 * limit - 1.0) * (i + 1.0) / steps;
            ac.HoldLoadFactor(n, n, Dt);
            string stage = ac.Structure.LeftWing.Stage.ToString();
            if (seen.Count == 0 || seen[^1] != stage)
            {
                seen.Add(stage);
                _o.WriteLine($"at {n:F2} g ({n / limit:F3} × limit): " + Report(ac));
            }
        }
        Assert.Equal(new[] { "Intact", "OutboardGone", "Departed" }, seen);
        Assert.True(ac.Structure.WingsFailed);
    }

    // ---- RULE 2 and RULE 4: each wing on its own load, so one goes and the other does not ---------------------------

    /// <summary>
    /// "account for aileron load so one tip could break off but not the other in some situations" and "treat them
    /// independently". A rolling pull that puts one wing past its breaking point and the other inside it takes ONE
    /// tip. Before 2026-11-10 the wings were one member on the whole aeroplane's load factor and this was impossible:
    /// they failed as one symmetric event or not at all.
    /// </summary>
    [Fact]
    public void ARollingPullBreaksOneTipAndLeavesTheOther()
    {
        Aircraft ac = At("j3-cub-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        // The left wing past its outboard rung, the right wing short of it — what a rolling pull does.
        _o.WriteLine($"the outboard rung is at {WingFailureLaw.StageRatio(OneG(ac)):F3} × limit");
        HoldAsymmetric(ac, nLeft: 1.85 * limit, nRight: 1.60 * limit, sec: 0.5);
        _o.WriteLine("rolling pull, left loaded harder: " + Report(ac));

        Assert.Equal(WingSideStage.OutboardGone, ac.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.Intact, ac.Structure.RightWing.Stage);
        Assert.False(ac.Structure.WingsFailed);
        // The left tip has gone; the right one is still on the aeroplane, and bent, because 1.70 is past its ultimate.
        Assert.All(ac.Panels.Where(p => p.Side < 0), p => Assert.Equal(PanelState.Broken, p.State));
        Assert.All(ac.Panels.Where(p => p.Side > 0), p => Assert.NotEqual(PanelState.Broken, p.State));
        Assert.Equal(1.60 * limit, ac.Structure.RightWing.LoadFactor, 9);
        Assert.Contains(AirframeComponent.WingTipLeft, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.WingTipRight, ac.LostComponents);
    }

    /// <summary>
    /// The two wings are bent DIFFERENTLY by an asymmetric pull — "asymmetric aerodynamic bending is shown as bent more
    /// on one side than the other" (Ty, same day). Each panel takes its own wing's permanent set, so what is drawn and
    /// what flies are asymmetric too.
    /// </summary>
    [Fact]
    public void AnAsymmetricPullLeavesTheTwoWingsBentByDifferentAmounts()
    {
        Aircraft ac = At("j3-cub-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        // Both short of the outboard rung, so nothing departs and the two sets can be read against the arithmetic.
        HoldAsymmetric(ac, nLeft: 1.7 * limit, nRight: 1.55 * limit, sec: 1.0);
        HoldAsymmetric(ac, 1.0, 1.0, sec: 0.5);                      // back inside the limit: both excursions booked
        _o.WriteLine("1.70 × limit on the left, 1.55 on the right: " + Report(ac));

        double setL = ac.Structure.LeftWing.Member.SetRad, setR = ac.Structure.RightWing.Member.SetRad;
        // Half the bending at the peak stays (Ty's overstress pattern): 20° × 0.70 / 2 = 7.0° left, 20° × 0.55 / 2 = 5.5° right.
        Assert.Equal(7.0 * Deg, setL, 4);
        Assert.Equal(5.5 * Deg, setR, 4);
        foreach (WingPanel p in ac.Panels)
            Assert.Equal(p.Side < 0 ? 7.0 * Deg : 5.5 * Deg, p.OverstressSetRad, 4);
        // And the more loaded wing is the weaker one from now on.
        Assert.True(ac.Structure.LeftWing.Member.Strength < ac.Structure.RightWing.Member.Strength);
        // "The wings" as a single reading is the wing closer to coming apart — the left one here.
        Assert.Same(ac.Structure.LeftWing.Member, ac.Structure.Wings);
    }

    /// <summary>
    /// The per-side load is not a test fixture: an aeroplane really flying a rolling pull loads its two wings
    /// differently, and the aero rolling moment is what says so (<see cref="Aircraft.WingSideLoadFactor"/>). Full
    /// aileron in a pull, flown through the sim, must put far more g on one wing than the other.
    ///
    /// Flown with the power OFF, so the only asymmetry in the air is the one the stick makes. (At full power a
    /// single-engine propeller aeroplane is already a little asymmetric — the slipstream's swirl and the descending
    /// blade put more lift on one wing than the other, about a thirtieth of a g on a J-3, and it has always been in the
    /// aero model's roll moment. That is real, and it is why this test takes the power off rather than asserting zero.)
    /// </summary>
    [Fact]
    public void FlownRollingPullLoadsTheTwoWingsUnevenlyAndACentredStickDoesNot()
    {
        // Fast, power off, and only the first instants of the roll: the aileron's own asymmetry, before the roll rate
        // has built and roll damping started cancelling it.
        Aircraft Fast()
        {
            Aircraft a = At("extra-300-like");
            a.State = new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(90, 0, 0), Vec3.Zero);
            return a;
        }
        Aircraft level = Fast();
        new SimLoop(level).RunFor(0.15, new ControlInputs(0, -0.5, 0, 1.0));
        double dLevel = Math.Abs(level.WingSideLoadFactor(-1) - level.WingSideLoadFactor(1));
        _o.WriteLine($"stick centred, power off: n {level.AeroLoadFactorZ:F4}, roll moment {level.AeroRollMomentNm:F1} N·m, " +
                     $"left {level.WingSideLoadFactor(-1):F4}, right {level.WingSideLoadFactor(1):F4} (difference {dLevel:F4} g)");
        Assert.True(dLevel < 0.02, $"a centred stick in still air must load the two wings alike; difference {dLevel:F4} g");

        Aircraft rolling = Fast();
        new SimLoop(rolling).RunFor(0.15, new ControlInputs(1.0, -0.5, 0, 1.0));
        double nL = rolling.WingSideLoadFactor(-1), nR = rolling.WingSideLoadFactor(1);
        double roll = rolling.AeroRollMomentNm;
        _o.WriteLine($"full aileron in a pull: n {rolling.AeroLoadFactorZ:F4}, roll moment {roll:F1} N·m, " +
                     $"left {nL:F4}, right {nR:F4} (difference {nL - nR:F4} g)");
        Assert.True(Math.Abs(nL - nR) > 0.1, $"a rolling pull must load the two wings measurably differently; difference {nL - nR:F4} g");
        Assert.True(Math.Abs(nL - nR) > 10 * dLevel, "and far more than a centred stick does");
        // Which wing carries more follows the rolling moment's sign, and nothing else: a positive roll moment (right
        // wing down) is the LEFT wing lifting harder.
        Assert.Equal(roll > 0, nL > nR);
        // And the two still average to the aeroplane's own load factor: nothing has been invented, only split.
        Assert.Equal(rolling.AeroLoadFactorZ, 0.5 * (nL + nR), 9);
    }

    // ---- RULE 3: a cantilever wing folds up one g before it departs, and keeps flying ------------------------------

    /// <summary>
    /// "For cantilever have the wings fold up 1 g before departing." The Extra 300 is a cantilever composite wing: a
    /// slow pull to one g below its breaking point folds both wings UP and leaves them on the aeroplane. The fold angle
    /// is not a chosen number — it is where the demand's cos φ meets what still bridges the root
    /// (<see cref="WingFailureLaw.FoldRad"/>), which for a composite wing is far over.
    /// </summary>
    [Fact]
    public void ACantileverWingFoldsUpOneGBeforeItDeparts()
    {
        Aircraft ac = At("extra-300-like");
        Assert.False(ac.Structure.LeftWing.Braced || ac.Structure.RightWing.Braced, "the Extra 300 is a cantilever wing");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        double foldRatio = WingFailureLaw.FoldRatio(OneG(ac));
        _o.WriteLine($"Extra 300: limit {limit:F2} g, one g = {OneG(ac):F4} of it, folds at {foldRatio:F3} × limit = {foldRatio * limit:F2} g");

        Ramp(ac, foldRatio * limit * 1.005, sec: 3.0);
        _o.WriteLine("after a slow pull to the fold ratio: " + Report(ac));

        Assert.Equal(WingSideStage.Folded, ac.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.Folded, ac.Structure.RightWing.Stage);
        Assert.False(ac.Structure.WingsFailed, "a folded wing is still on the aeroplane");
        Assert.Empty(ac.LostComponents);
        Assert.True(ac.Structure.LeftWing.FoldRad > 45 * Deg, $"a fold is a fold, not a bend: {ac.Structure.LeftWing.FoldRad / Deg:F1}°");
        // The expected angle, worked from the law: cos φ = residual / (demand / ultimate).
        double expected = WingFailureLaw.FoldRad(ac.Structure.LeftWing.Member.EffectiveRatio,
            ac.Config.Limits.UltimateFactor, MemberMaterials.Law(MemberMaterials.Of(ac.Config)).ResidualRatio);
        Assert.Equal(expected, ac.Structure.LeftWing.FoldRad, 3);
        // Both wings' panels carry it, and the fold is PLASTIC: it does not come back when the load does.
        Assert.All(ac.Panels, p => Assert.Equal(ac.Structure.Wing(p.Side).FoldRad, p.FoldRad, 9));
        double held = ac.Structure.LeftWing.FoldRad;
        Hold(ac, 1.0, sec: 1.0);
        Assert.Equal(held, ac.Structure.LeftWing.FoldRad, 9);
    }

    /// <summary>
    /// "continue to account for their aerodynamic force". A folded wing is not masked and not inert: its rows are still
    /// in the aero model, at the folded geometry. So it still makes force — and much LESS lift than it did, which is the
    /// whole point, because that is what relieves its own load and lets it stay on the aeroplane.
    /// </summary>
    [Fact]
    public void AFoldedWingStillMakesForceAndMakesLessLift()
    {
        Aircraft ac = At("extra-300-like");
        AircraftConfig c = ac.Config;
        var tables = Aircraft.BuildAirfoilTables(c);
        int n = c.Surfaces.Sum(s => s.Strips.Count);

        (Vec3 f, Vec3 m) Aero(StripFlowState? shape)
        {
            var fs = new StripFlowState(); fs.EnsureSize(n);
            if (shape is not null) { fs.BendRad = shape.BendRad; fs.BendOffset = shape.BendOffset; }
            return AeroModel.Compute(c, tables, new Vec3(70, 0, 3.5), Vec3.Zero, Vec3.Zero, 1.225,
                ControlDeflections.Neutral, 0.0, fs, 0, 0, null, null);
        }
        Vec3 straight = Aero(null).f;

        Ramp(ac, WingFailureLaw.FoldRatio(OneG(ac)) * c.Limits.PublishedLimits().Pos * 1.005, sec: 3.0);
        Assert.Equal(WingSideStage.Folded, ac.Structure.LeftWing.Stage);
        Vec3 folded = Aero(ac.FlowStateForTests).f;
        _o.WriteLine($"lift at 70 m/s: straight {-straight.Z:F0} N, both wings folded {ac.Structure.LeftWing.FoldRad / Deg:F1}° {-folded.Z:F0} N");

        Assert.True(folded.LengthSquared > 1.0, "a folded wing must still make FORCE, not vanish");
        Assert.True(-folded.Z < 0.6 * -straight.Z, $"a wing folded {ac.Structure.LeftWing.FoldRad / Deg:F0}° cannot still make its lift: {-folded.Z:F0} vs {-straight.Z:F0} N");
        // The rows are still there: nothing has been masked out of the aero.
        Assert.All(ac.Panels, p => Assert.All(p.SideStrips, i => Assert.True(ac.StripMask is null || ac.StripMask[i], $"strip {i} of a FOLDED wing was masked out")));
        // And the fold has moved the INBOARD rows too, which a kink at the station could not do: a fold is at the root.
        WingPanel w = ac.Panels.First();
        int inboard = w.SideStrips.First(i => Math.Abs(ac.Config.Surfaces.SelectMany(s => s.Strips).ElementAt(i).Pos[1]) < w.BreakY);
        Assert.True(ac.FlowStateForTests.BendOffset![inboard].LengthSquared > 1e-6,
            "a fold at the root must move the rows inboard of the station as well");
    }

    /// <summary>
    /// "one wing could fold while the other doesn't or one wing could fold while the other departs." Both halves, on the
    /// same aeroplane, from one asymmetric pull: the left wing taken past its break and the right one only past the
    /// fold.
    /// </summary>
    [Fact]
    public void OneCantileverWingCanFoldWhileTheOtherDoesNotAndWhileTheOtherDeparts()
    {
        double oneG, limit;
        {
            Aircraft probe = At("extra-300-like");
            oneG = OneG(probe); limit = probe.Config.Limits.PublishedLimits().Pos;
        }
        double fold = WingFailureLaw.FoldRatio(oneG);

        // One folds, the other does not: the left past the fold ratio, the right inside it.
        Aircraft a = At("extra-300-like");
        HoldAsymmetric(a, nLeft: fold * limit * 1.01, nRight: 1.6 * limit, sec: 0.5);
        _o.WriteLine("one folds, the other does not: " + Report(a));
        Assert.Equal(WingSideStage.Folded, a.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.Intact, a.Structure.RightWing.Stage);
        Assert.Equal(0.0, a.Structure.RightWing.FoldRad);
        Assert.All(a.Panels.Where(p => p.Side > 0), p => Assert.Equal(0.0, p.FoldRad));
        Assert.False(a.Structure.WingsFailed);

        // One folds, the other departs. Both wings taken to the fold first, then the LEFT alone taken through its
        // break — in that order, because an asymmetric wing loss makes the aeroplane lighter and so relieves what is
        // left (LimitsConfig.WingWeightRatio), which is a real effect and would otherwise unfold nothing but confuse
        // the arithmetic.
        Aircraft b = At("extra-300-like");
        HoldAsymmetric(b, fold * limit * 1.01, fold * limit * 1.01, sec: 0.5);
        Assert.Equal(WingSideStage.Folded, b.Structure.RightWing.Stage);
        HoldAsymmetric(b, nLeft: 2.05 * limit, nRight: fold * limit * 1.01, sec: 0.5);
        _o.WriteLine("one departs, the other folds: " + Report(b));
        Assert.Equal(WingSideStage.Departed, b.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.Folded, b.Structure.RightWing.Stage);
        Assert.True(b.IsLost(AirframeComponent.WingLeft), "the left wing has left the aeroplane");
        Assert.False(b.IsLost(AirframeComponent.WingRight), "the right wing has only folded");
        Assert.False(b.Structure.WingsFailed, "ONE wing gone is not 'the wings failed'");
        // An asymmetric wing loss really takes its mass, with the CG shift that follows — the thing that makes losing
        // one wing different from losing two.
        Assert.True(b.ShedMassKg > 0, "a departed wing must take its mass with it");
        Assert.True(Math.Abs(b.CgShiftBody.Y) > 1e-3, $"losing ONE wing must move the CG sideways; shift {b.CgShiftBody.Y:F4} m");
    }

    // ---- RULE 5: the whole point — the symmetric event is still there, and it is the LAST rung -----------------------

    /// <summary>
    /// "We just need more progressive stagger to the failures." The ladder in one test: a braced aeroplane flown
    /// through three separate events — bent, then its outboard sections gone, then (on a snatch, which is the only
    /// thing that does it) its wings gone — where before 2026-11-10 it had exactly one.
    /// </summary>
    [Fact]
    public void TheFailureNowHasRungsInsteadOfOneSymmetricEvent()
    {
        Aircraft ac = At("j3-cub-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        var rungs = new List<string>();
        void Note(string what) { rungs.Add(what); _o.WriteLine($"{what}: " + Report(ac)); }

        // A pull past the ultimate load and back inside the limit: the set is booked on the way out, so the panels are
        // only Bent once the load has come off.
        Ramp(ac, 1.7 * limit, sec: 2.0);
        Ramp(ac, 1.0, sec: 0.5, holdSec: 0.5);
        if (ac.Panels.Any(p => p.State == PanelState.Bent)) Note("bent");

        Ramp(ac, 2.02 * limit, sec: 2.5);
        if (ac.Structure.LeftWing.Stage == WingSideStage.OutboardGone) Note("outboard sections gone");
        Ramp(ac, 1.0, sec: 0.5, holdSec: 0.5);

        // Only a severe overstress takes the whole wing on a braced type. A fresh aeroplane for it, because the one
        // above has shed its tips and so no longer has an outboard station to break (see
        // WhatIsLeftOfABracedWingIsFarStrongerPerGThanThePanelThatDeparted).
        Aircraft snatched = At("j3-cub-like");
        Hold(snatched, (2.0 + OneG(snatched)) * limit, sec: 0.5);
        if (snatched.Structure.WingsFailed) { rungs.Add("wings gone"); _o.WriteLine("wings gone: " + Report(snatched)); }

        Assert.Equal(new[] { "bent", "outboard sections gone", "wings gone" }, rungs);
    }

    /// <summary>
    /// And the rung that was always there is unchanged: a SYMMETRIC pull loads the two wings identically, so they reach
    /// every threshold on the same step and "the wings failed" still means both of them at once. Everything in the sim
    /// that reads a single number about the wings — the groan, the HUD, the grading, CrashRelay's two wreck pieces —
    /// depends on this.
    /// </summary>
    [Fact]
    public void ASymmetricPullStillTreatsTheTwoWingsAsOne()
    {
        Aircraft ac = At("j3-cub-like", 1.1);
        int failures = 0;
        ac.Structure.OnWingsFailed += () => failures++;
        Hold(ac, 2.0 * ac.Config.Limits.PublishedLimits().Pos, sec: 1.0);
        _o.WriteLine("2.0 × limit at 110 % of gross: " + Report(ac));

        Assert.True(ac.Structure.WingsFailed);
        Assert.Equal(1, failures);
        Assert.Equal(ac.Structure.LeftWing.Stage, ac.Structure.RightWing.Stage);
        Assert.Equal(ac.Structure.LeftWing.Member.SetRad, ac.Structure.RightWing.Member.SetRad, 12);
        Assert.Equal(ac.Structure.LeftWing.Member.Strength, ac.Structure.RightWing.Member.Strength, 12);
    }

    // ---- the law itself, as arithmetic -------------------------------------------------------------------------------

    /// <summary>
    /// The fold angle against the hand-worked numbers for the three constructions, at the breaking point: a composite
    /// wing goes right over, an aluminium one folds least far, and the order is the order of the residual strengths.
    /// cos φ = residual / (2.0 / 1.5).
    /// </summary>
    [Theory]
    [InlineData(WingConstruction.WoodFabric, 74.78)]
    [InlineData(WingConstruction.AluminumSkin, 67.98)]
    [InlineData(WingConstruction.Composite, 83.54)]
    public void TheFoldAngleIsWhereTheResidualStrengthStopsTheFold(WingConstruction w, double expectedDeg)
    {
        double residual = MemberMaterials.Law(w).ResidualRatio;
        double fold = WingFailureLaw.FoldRad(OverstressMember.BreakRatio, 1.5, residual);
        _o.WriteLine($"{w}: residual {residual:F2} of ultimate → folds {fold / Deg:F2}°");
        Assert.Equal(expectedDeg, fold / Deg, 2);
        Assert.Equal(residual, Math.Cos(fold) * OverstressMember.BreakRatio / 1.5, 9);
    }

    /// <summary>The fold is never inside the ultimate ratio, however low the aeroplane's limit is: below the ultimate
    /// the structure is elastic and a fold is a plastic collapse. A 1.5 g aeroplane's one g is two thirds of its whole
    /// limit, which would otherwise fold it at 1.33 — inside its elastic range.</summary>
    [Fact]
    public void AVeryLowLimitStillFoldsOnlyOnceTheStructureHasYielded()
    {
        Assert.Equal(1.737, WingFailureLaw.FoldRatio(1.0 / 3.8), 3);        // a J-3
        Assert.Equal(1.900, WingFailureLaw.FoldRatio(1.0 / 10.0), 3);       // an Extra 300
        Assert.Equal(OverstressMember.UltimateRatio, WingFailureLaw.FoldRatio(1.0 / 1.5), 9);
        Assert.True(WingFailureLaw.FoldRatio(5.0) >= OverstressMember.UltimateRatio);
    }

}
