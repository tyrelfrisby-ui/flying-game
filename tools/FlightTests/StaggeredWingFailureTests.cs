using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
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

    // ---- the AERODYNAMIC bend is a CURVE from the root; a STRIKE bend is still a kink (Ty 2026-11-10) -------------

    /// <summary>
    /// "Make bent wings from symmetrical aerodynamic load curve from the root instead of pivot at a strut or root."
    /// The g-load bend has the shape of a loaded spar: level where it leaves the fuselage, most curvature inboard, the
    /// tip displaced most — and it moves the wing INBOARD of the station, which a pivot at the station cannot.
    /// </summary>
    [Fact]
    public void AnAerodynamicBendIsASmoothCurveFromTheRootAndNotAPivotAtTheStation()
    {
        Aircraft ac = At("j3-cub-like");
        WingPanel p = ac.Panels.First(q => q.Side < 0);
        Ramp(ac, 1.7 * ac.Config.Limits.PublishedLimits().Pos, sec: 2.0);
        Ramp(ac, 1.0, sec: 0.5, holdSec: 0.5);                 // booked: a 7° permanent set at the TIP
        Assert.Equal(7.0 * Deg, p.AeroBendRad, 4);
        Assert.Equal(0.0, p.StrikeBendRad, 9);                 // nothing struck it

        // The slope along the span: zero at the root, the whole of it at the tip, and rising all the way.
        double last = -1;
        _o.WriteLine($"J-3 left wing, {p.AeroBendRad / Deg:F2}° at the tip (root {p.RootY:F2} m, station {p.BreakY:F2} m, tip {p.TipY:F2} m):");
        foreach (double f in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            double ay = p.RootY + f * (p.TipY - p.RootY);
            double slope = p.CurveSection(ay, p.AeroBendRad).SlopeRad;
            Vec3 at = p.Shape(new Vec3(0, p.Side * ay, p.BreakZ));
            _o.WriteLine($"   eta {f:F2} (|y| {ay:F2} m): slope {slope / Deg:F2}°, risen {(p.BreakZ - at.Z) * 100:F1} cm");
            Assert.True(slope > last, "the slope must rise all the way out");
            last = slope;
        }
        Assert.Equal(0.0, p.CurveSection(p.RootY, p.AeroBendRad).SlopeRad, 9);        // level at the root
        Assert.Equal(p.AeroBendRad, p.CurveSection(p.TipY, p.AeroBendRad).SlopeRad, 9);   // the whole angle at the tip

        // MOST CURVATURE INBOARD: the first quarter of the span turns through more than the last quarter.
        double Slope(double f) => p.CurveSection(p.RootY + f * (p.TipY - p.RootY), p.AeroBendRad).SlopeRad;
        Assert.True(Slope(0.25) - Slope(0.0) > Slope(1.0) - Slope(0.75),
            $"inboard quarter {(Slope(0.25) - Slope(0.0)) / Deg:F2}° vs outboard {(Slope(1.0) - Slope(0.75)) / Deg:F2}°");

        // And the wing INBOARD of the station has moved, which is the whole difference from a pivot there.
        Vec3 mid = new(0, p.Side * 0.5 * (p.RootY + p.BreakY), p.BreakZ);
        _o.WriteLine($"half way to the station: risen {(p.BreakZ - p.Shape(mid).Z) * 100:F1} cm (a pivot at the station gives 0)");
        Assert.True(p.BreakZ - p.Shape(mid).Z > 0.01, "an aerodynamic bend must lift the wing inboard of the station");
        Assert.Equal(p.Side * 0.5 * (p.RootY + p.BreakY), p.Deform(mid).Y, 9);   // the STATION kink alone would not
    }

    /// <summary>
    /// A WING STRIKE'S BEND IS STILL A SHARP KINK MID SPAN ("Wing strike bends are still sharp mid span"), and the
    /// aileron split at the station rests on exactly that: nothing inboard of the station moves, everything outboard
    /// moves rigidly. The curve is a second shape alongside it, not a replacement.
    /// </summary>
    [Fact]
    public void AStrikeBendIsStillASharpKinkAtTheStation()
    {
        Aircraft ac = At("j3-cub-like");
        Assert.True(ac.BendPanel(AirframeComponent.WingTipLeft, 8 * Deg));
        WingPanel p = ac.Panels.First(q => q.Side < 0);
        Assert.Equal(8 * Deg, p.StrikeBendRad, 6);
        Assert.Equal(0.0, p.AeroBendRad, 9);                   // no g load: the curve is flat

        // Inboard of the station: untouched, to the last bit.
        foreach (double f in new[] { 0.1, 0.5, 0.9 })
        {
            Vec3 v = new(0.1, p.Side * f * p.BreakY, p.BreakZ + 0.05);
            Vec3 d = p.Shape(v);
            Assert.Equal(v.Y, d.Y, 12);
            Assert.Equal(v.Z, d.Z, 12);
        }
        // Outboard: rigid about the station — the distance from the station axis is preserved.
        foreach (double ay in new[] { 3.6, 4.4, 5.2 })
        {
            Vec3 v = new(0.1, p.Side * ay, p.BreakZ + 0.05);
            Vec3 d = p.Shape(v);
            double r0 = Math.Sqrt(Math.Pow(v.Y - p.Side * p.BreakY, 2) + Math.Pow(v.Z - p.BreakZ, 2));
            double r1 = Math.Sqrt(Math.Pow(d.Y - p.Side * p.BreakY, 2) + Math.Pow(d.Z - p.BreakZ, 2));
            Assert.Equal(r0, r1, 9);
        }
        _o.WriteLine("a strike's 8° bend: a kink at the station, nothing inboard moved, rigid outboard");
    }

    /// <summary>
    /// THE AILERON LIMIT READS THE SAME NUMBER IT ALWAYS DID. The curve changes the SHAPE a bend angle is realised as,
    /// not the angle: a panel still reports its hinge's set plus its wing's overstress set as <see cref="WingPanel.BendRad"/>,
    /// which is what BentAileronTravel measures the lost travel against (and the worst side of the pair still binds).
    /// This is the test that would catch the curve quietly moving that number.
    /// </summary>
    [Fact]
    public void TheCurveDoesNotChangeTheBendAngleAPanelReports()
    {
        Aircraft ac = At("j3-cub-like");
        Ramp(ac, 1.7 * ac.Config.Limits.PublishedLimits().Pos, sec: 2.0);
        Ramp(ac, 1.0, sec: 0.5, holdSec: 0.5);
        foreach (WingPanel p in ac.Panels)
        {
            Assert.Equal(7.0 * Deg, p.OverstressSetRad, 4);                      // the member's set, per side
            Assert.Equal(p.StrikeBendRad + p.OverstressSetRad, p.BendRad, 9);     // the reported angle, unchanged
        }
        // And with a strike on top, the two still add into the one reported number.
        Assert.True(ac.BendPanel(AirframeComponent.WingTipLeft, 4 * Deg));
        WingPanel l = ac.Panels.First(q => q.Side < 0);
        _o.WriteLine($"left wing: strike {l.StrikeBendRad / Deg:F2}° + aero {l.OverstressSetRad / Deg:F2}° = reported {l.BendRad / Deg:F2}°");
        Assert.Equal(11.0 * Deg, l.BendRad, 4);
    }

    /// <summary>The curve does not stretch the spar: the deformed span is the span it started with, to a millimetre,
    /// which is what makes a sharp kink the exact limiting case of it rather than something that disagrees.</summary>
    [Fact]
    public void TheCurveBendsTheSparWithoutStretchingIt()
    {
        Aircraft ac = At("j3-cub-like");
        WingPanel p = ac.Panels.First(q => q.Side < 0);
        foreach (double tipDeg in new[] { 5.0, 10.0, 20.0 })
        {
            double tip = tipDeg * Deg;
            double len = 0; double y0 = p.RootY, z0 = p.BreakZ;
            for (int i = 1; i <= 2000; i++)
            {
                double ay = p.RootY + (p.TipY - p.RootY) * i / 2000.0;
                (_, double yc, double zc) = p.CurveSection(ay, tip);
                len += Math.Sqrt((yc - y0) * (yc - y0) + (zc - z0) * (zc - z0));
                y0 = yc; z0 = zc;
            }
            _o.WriteLine($"tip {tipDeg:F0}°: spine {len:F4} m against a straight {p.TipY - p.RootY:F4} m");
            Assert.Equal(p.TipY - p.RootY, len, 3);
        }
    }

    /// <summary>
    /// TY'S DEFECT, 2026-11-10, flying a Pitts at a 6.6° permanent set: "only the tips stay 'bent' and now they are
    /// disjointed from the rest of the wing when they get bent as a result."
    ///
    /// The set was already a CURVE in <see cref="WingPanel.Shape"/> — what was wrong is that only the panel's OWN rows
    /// (outboard of the station) were ever written with it. Measured at the time: the Pitts' inner wing flat at every
    /// station while the outer panel sat 18.9 cm up at the station, an 18.9 cm step. And it was not a drawing fault —
    /// the STRIPS were unmoved too, so the aeroplane was flying the kinked wing as well as being drawn as one, with
    /// the inboard lift in the wrong place.
    ///
    /// So: every row of the wing side flies exactly what Shape gives it, inboard rows included, and the shape is
    /// CONTINUOUS across the station. A step there is the defect, and this is what would catch it coming back.
    /// </summary>
    [Fact]
    public void APermanentAerodynamicSetCurvesTheWholeWingWithNoStepAtTheStation()
    {
        Aircraft ac = At("pitts-s2b-like");
        double limit = ac.Config.Limits.PublishedLimits().Pos;
        Ramp(ac, 1.7 * limit, sec: 2.0);
        Ramp(ac, 1.0, sec: 0.5, holdSec: 0.5);                 // a set, no fold, no strike
        var all = ac.Config.Surfaces.SelectMany(sf => sf.Strips).ToList();
        StripFlowState fs = ac.FlowStateForTests;

        foreach (WingPanel p in ac.Panels)
        {
            Assert.True(Math.Abs(p.OverstressSetRad) > 0.003, "the pull must have left a set");
            Assert.Equal(0.0, p.FoldRad);
            Assert.Equal(0.0, p.StrikeBendRad, 12);
            int inboard = 0;
            foreach (int i in p.SideStrips)
            {
                Vec3 pos = all[i].PosVec();
                Vec3 want = p.Shape(pos) - pos;
                Assert.Equal(want.Y, fs.BendOffset![i].Y, 9);
                Assert.Equal(want.Z, fs.BendOffset[i].Z, 9);
                Assert.Equal(p.LocalShapeRad(Math.Abs(pos.Y)), fs.BendRad![i], 9);
                if (Math.Abs(pos.Y) < p.BreakY)
                {
                    inboard++;
                    Assert.True(Math.Abs(fs.BendOffset[i].Z) > 1e-4,
                        $"the row at |y| {Math.Abs(pos.Y):F2} is INBOARD of the station and must have risen with the curve");
                }
            }
            Assert.True(inboard > 0, $"{p.Component} has no inboard rows to check");

            // NO STEP AT THE STATION: the rise a hair either side of it differs by less than a millimetre. A kink at
            // the station — the old behaviour — puts the whole of the set's rise into that gap.
            double InRise(double ay) => p.BreakZ - p.Shape(new Vec3(0, p.Side * ay, p.BreakZ)).Z;
            double step = Math.Abs(InRise(p.BreakY + 0.001) - InRise(p.BreakY - 0.001));
            _o.WriteLine($"{p.Component}: set {p.OverstressSetRad * 180 / Math.PI:F2}°, rise at the station " +
                         $"{InRise(p.BreakY) * 100:F1} cm, step across it {step * 1000:F3} mm, {inboard} inboard rows curved");
            Assert.True(step < 0.001, $"a {step * 100:F1} cm step at the station is the disjoint Ty reported");
        }
    }

    // ---- what a wing that has lost its outboard panels costs (Ty 2026-11-10) --------------------------------------

    /// <summary>
    /// Ty, having ripped the outboard panels off a J-3: "the glide ratio was inexplicably good it should be bad with
    /// short stubby wing roots I assume it is counting the missing wing in the aero."
    ///
    /// He was right that the missing wing was being counted, and wrong about which part of it. The LIFT of a departed
    /// strip really does go — the strip mask drops it from the sum, measured below. What was still being counted was
    /// the wing's GEOMETRY:
    ///
    ///  • the trailing-vortex system's aspect ratio was measured once per config from the strips as AUTHORED, so the
    ///    stub flew at the whole wing's A = 6.96 instead of its own 4.17 and paid two thirds of the induced drag it
    ///    owed — and induced drag is most of the drag at best glide, which is where the glide ratio is read;
    ///  • and the torn end — spar stubs, ribs and fabric square to the airflow — cost nothing at all.
    ///
    /// Measured: best L/D 7.50 whole (against the 8:1 the glide table carries), 6.29 with the panels gone before this,
    /// 5.09 after. The ratio 5.09 / 7.50 = 0.68 is what the textbook says it should be: best L/D goes as √(A·e/Cd0),
    /// so √(4.17 / 6.96) = 0.77 before the extra Cd0 of a stub that is all torn end.
    /// </summary>
    [Fact]
    public void AWingThatHasLostItsOutboardPanelsGlidesLikeTheStubItIs()
    {
        Aircraft whole = At("j3-cub-like");
        Aircraft stub = At("j3-cub-like");
        stub.LoseComponent(AirframeComponent.WingTipLeft);
        stub.LoseComponent(AirframeComponent.WingTipRight);

        (double ldW, _) = BestGlide(whole);
        (double ldS, double areaS) = BestGlide(stub);
        double areaW = ActiveWingArea(whole);
        _o.WriteLine($"J-3 best L/D: whole {ldW:F2} over {areaW:F2} m² of wing; outboard panels gone {ldS:F2} over {areaS:F2} m²");

        Assert.InRange(ldW, 7.0, 8.5);                    // the intact Cub, against the 8:1 the glide table carries
        Assert.True(ldS < 0.75 * ldW, $"a stub-winged Cub must glide far worse than a whole one; {ldS:F2} against {ldW:F2}");
        Assert.True(ldS > 3.0, $"and it is still a wing, not a brick; {ldS:F2}");

        // THE LIFT IS GONE: the departed rows are out of the aero sum entirely.
        Assert.True(areaS < 0.7 * areaW, $"the departed panels' area must leave the sum; {areaS:F2} of {areaW:F2} m²");

        // THE ASPECT RATIO FOLLOWED THE WING. This is the half he spotted.
        double[] arW = AeroModel.MeasureSystemAspectRatios(whole.Config, whole.StripMask);
        double[] arS = AeroModel.MeasureSystemAspectRatios(stub.Config, stub.StripMask);
        int wing = whole.Config.Surfaces.ToList().FindIndex(sf => WingStructure.IsWingBase(sf.Id));
        _o.WriteLine($"wing system aspect ratio: whole {arW[wing]:F2}, stub {arS[wing]:F2}");
        Assert.True(arS[wing] < 0.75 * arW[wing], $"the stub's aspect ratio must fall with its span; {arS[wing]:F2} of {arW[wing]:F2}");
        // And the aeroplane really hands it to the aero model, rather than it being true only in this test.
        Assert.NotNull(stub.FlowStateForTests.SystemAspectRatio);
        Assert.Equal(arS[wing], stub.FlowStateForTests.SystemAspectRatio![wing], 9);
        Assert.Null(whole.FlowStateForTests.SystemAspectRatio);   // nothing lost: the config's own cache is right

        // THE TORN END COSTS SOMETHING. It is charged to the outermost surviving row, so losing ONE panel also yaws.
        Assert.NotNull(stub.FlowStateForTests.ExtraCd);
        Assert.True(stub.FlowStateForTests.ExtraCd!.Sum() > 0.0, "a torn-off wing must leave a draggy end");
    }

    /// <summary>Best L/D over an alpha sweep, straight from the aero model with this aeroplane's own masks and flow
    /// state, and the wing area still in the sum.</summary>
    private (double Ld, double WingArea) BestGlide(Aircraft ac)
    {
        var tables = Aircraft.BuildAirfoilTables(ac.Config);
        double best = 0;
        for (double a = 0.0; a <= 16.0; a += 0.25)
        {
            double ca = Math.Cos(a * Deg), sa = Math.Sin(a * Deg);
            (Vec3 f, _) = AeroModel.Compute(ac.Config, tables, new Vec3(30 * ca, 0, 30 * sa), Vec3.Zero, Vec3.Zero, 1.225,
                ControlDeflections.Neutral, 0.0, ac.FlowStateForTests, 0, 0, ac.SurfaceMask, ac.StripMask, null, double.PositiveInfinity);
            double drag = -(f.X * ca + f.Z * sa), lift = -(f.Z * ca - f.X * sa);
            if (drag > 1e-6) best = Math.Max(best, lift / drag);
        }
        return (best, ActiveWingArea(ac));
    }

    private static double ActiveWingArea(Aircraft ac)
    {
        double area = 0; int idx = 0;
        foreach (SurfaceConfig sf in ac.Config.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                if ((ac.StripMask is null || ac.StripMask[idx]) && sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) area += st.Area;
                idx++;
            }
        return area;
    }

    // ---- the ladder works upside down too (Ty 2026-11-10) --------------------------------------------------------

    /// <summary>
    /// Ty: "Negative g to failure on the cub splits it in half down the fuselage instead of folding the wings."
    ///
    /// He was right, and the cause was a sign. <see cref="StructuralState.Ratio"/> NEGATES for a push so the load ratio
    /// comes out positive; OneGRatio divided by the signed negative limit and did not, so one g came out NEGATIVE under
    /// negative g — which put the rung at 2.0 − (−0.667) = 2.667 on a J-3, ABOVE the breaking point, where it could
    /// never fire. A push therefore went straight to the break with no outboard shed and no fold at all, and because a
    /// Cub's engine mount is rated at the same −1.5 g, the wings and the NOSE departed on the same step: the fuselage
    /// splitting instead of the wings coming apart.
    ///
    /// Measured before: everything gone together at −3.07 g. After: the outboard panels at −2.32 g and the nose not
    /// until −3.07, with the inner wings still on the aeroplane. Which is what the structure says should happen —
    /// a strut-braced wing is weakest in negative g, because the lift struts go from tension into compression, and
    /// that is why the Cub's limit is only −1.5 against +3.8.
    ///
    /// Every other test in this file pulls. That is why none of them caught it.
    /// </summary>
    [Fact]
    public void APushToFailureShedsTheWingsBeforeAnythingElseGoes()
    {
        Aircraft ac = At("j3-cub-like");
        double neg = ac.Config.Limits.PublishedLimits().Neg;
        Assert.Equal(-1.5, neg, 6);
        double oneG = ac.WingWeightRatio / Math.Abs(neg);

        // One g is a POSITIVE fraction of the limit whichever way the load goes, so the rung sits BELOW the break.
        _o.WriteLine($"J-3 at {neg:F2} g: one g is {oneG:F3} of the limit, rung {WingFailureLaw.StageRatio(oneG):F3}, break {OverstressMember.BreakRatio:F2}");
        Assert.True(WingFailureLaw.StageRatio(oneG) < OverstressMember.BreakRatio,
            $"the rung must come before the break; rung {WingFailureLaw.StageRatio(oneG):F3}");

        // Flown as a push to failure, watched for the ORDER things come apart in.
        var order = new List<string>();
        int steps = 1200;
        for (int i = 0; i < steps; i++)
        {
            double n = 1.0 + (-3.2 - 1.0) * (i + 1.0) / steps;
            ac.HoldLoadFactor(n, n, Dt);
            string now = $"{ac.Structure.LeftWing.Stage}|{ac.IsLost(AirframeComponent.Nose)}";
            if (order.Count == 0 || order[^1] != now)
            {
                order.Add(now);
                _o.WriteLine($"  at {n:F2} g: left wing {ac.Structure.LeftWing.Stage}, nose lost {ac.IsLost(AirframeComponent.Nose)}");
            }
        }
        // The WINGS come apart first, and the aeroplane is not split in half to do it.
        Assert.Equal(WingSideStage.OutboardGone, ac.Structure.LeftWing.Stage);
        Assert.Equal(WingSideStage.OutboardGone, ac.Structure.RightWing.Stage);
        Assert.Contains(AirframeComponent.WingTipLeft, ac.LostComponents);
        Assert.Contains(AirframeComponent.WingTipRight, ac.LostComponents);
        // "Intact|False" then "OutboardGone|False" then the nose: the tips go while the fuselage is still whole.
        Assert.Equal("Intact|False", order[0]);
        Assert.Equal("OutboardGone|False", order[1]);
        // And the set a push leaves is tips DOWN, which is the other half of the pattern being upside down. Booked on
        // the way out of the overstress, as every set is, so the load has to come off first.
        Ramp(ac, 1.0, sec: 1.0, holdSec: 0.5);
        _o.WriteLine($"  after the recovery: set {ac.Structure.LeftWing.Member.SetRad / Deg:F2}° (negative = tips DOWN)");
        Assert.True(ac.Structure.LeftWing.Member.SetRad < 0, "a push must bend the wings DOWN");
        Assert.All(ac.Panels.Where(p => p.State != PanelState.Broken), p => Assert.True(p.OverstressSetRad < 0));
    }

    /// <summary>
    /// A FOLDED WING ACROSS THE DOOR TRAPS THE PILOT (Ty 2026-11-10: "Negative g wing folds prevent escape/bailout
    /// unless the wing actually departs if a door is blocked").
    ///
    /// The clearance boxes the tail strike already uses are built from the strips as AUTHORED, so they could never say
    /// where a folded wing is. Built with the PANELS instead, every wing row sits where its shape puts it — and a wing
    /// folded down about its root on a high-wing aeroplane lies across the side door. The climb then caps at
    /// <see cref="ClimbOut.WingBlockedRiseM"/>: he gets out of the seat and no further, and only the wing DEPARTING
    /// clears it, because a folded wing is still attached and nothing he can do will move it.
    /// </summary>
    [Fact]
    public void AWingFoldedAcrossTheDoorTrapsThePilotUntilItDeparts()
    {
        Aircraft ac = At("j3-cub-like");
        WingPanel p = ac.Panels.First(q => q.Side > 0);
        // The doorway: out at the cabin side at the CG's own height — the same point the bridge asks about. Measured,
        // the J-3's wing sits 0.75 m ABOVE the CG, so a straight wing leaves that doorway clear and a wing folded down
        // about its root is squarely across it.
        Vec3 cg = ac.Config.Mass.CgVec();
        var door = new Vec3(cg.X, 0.42, cg.Z);

        Assert.Null(EgressClearance.From(ac.Config, ac.Panels).Hit(door));   // wings straight: the door is clear
        Assert.Null(EgressClearance.From(ac.Config).Hit(door));              // and the old boxes agree

        // Fold the wing DOWN about its root, which is what a push does to it.
        foreach (WingPanel q in ac.Panels) q.FoldRad = -70 * Deg;
        string blocked = EgressClearance.From(ac.Config, ac.Panels).Hit(door);
        _o.WriteLine($"wing folded {-70.0:F0}°: the door is blocked by '{blocked ?? "nothing"}'");
        Assert.NotNull(blocked);
        Assert.Contains("wing", blocked, StringComparison.OrdinalIgnoreCase);

        // THE CLIMB: he gets out of the seat and no further, and it reports itself blocked.
        // The climb is driven by the hand on the sill being pushed DOWN (cockpit frame, y up): the body rises by what
        // the hand pushes, 1:1. So a grab at y 0 and a hand at y −0.6 is a commanded rise of 0.6 m.
        var climb = new ClimbOut();
        climb.Grab(Vec3.Zero, ExitSide.Right);
        ClimbEvents ev = climb.Step(new Vec3(0, -0.6, 0), strapped: false, canopyClear: true, q: 500.0, exitClear: false);
        _o.WriteLine($"  blocked climb: rise {climb.RiseM * 100:F1} cm, events {ev}");
        Assert.True((ev & ClimbEvents.Blocked) != 0, "a wing across the door must report the climb blocked");
        Assert.Equal(ClimbOut.WingBlockedRiseM, climb.RiseM, 6);
        Assert.False(climb.Out);

        // THE WING DEPARTS and the way is clear: the same pull now takes him over the side.
        foreach (WingPanel q in ac.Panels) q.FoldRad = 0.0;
        var free = new ClimbOut();
        free.Grab(Vec3.Zero, ExitSide.Right);
        free.Step(new Vec3(0, -0.6, 0), strapped: false, canopyClear: true, q: 0.0, exitClear: true);
        _o.WriteLine($"  cleared climb: rise {free.RiseM * 100:F1} cm");
        Assert.True(free.RiseM > ClimbOut.OverRiseM - 1e-9, $"with the wing gone he must get over the side; {free.RiseM:F3} m");
    }

    // ---- the law itself, as arithmetic -------------------------------------------------------------------------------

    // ---- what the RENDERER is promised (Ty 2026-11-10: "draw it folded and limit the aileron") -----------------------

    /// <summary>
    /// THE DRAWN SHAPE IS THE FLOWN SHAPE, pinned where it can be: every row of a folded wing flies the offset that
    /// <see cref="WingPanel.Shape"/> gives its load point, and the renderer places every drawn VERTEX through that same
    /// call (AirframeVisual.PosePanelPieces). So the two cannot disagree about the shape the aeroplane is in — which is
    /// the standard Ty set when a bent wingtip was still drawn on an aeroplane that had no wings.
    ///
    /// This is the half of it that a .NET test can reach. What it does NOT prove is that the Unity side compiles or that
    /// the meshes end up where the maths says: no Unity build has seen any of this.
    /// (APermanentAerodynamicSetCurvesTheWholeWingWithNoStepAtTheStation is the same contract for a wing that has only
    /// a g-load set, which is the case Ty found broken.)
    /// </summary>
    [Fact]
    public void EveryRowOfAFoldedWingFliesExactlyWhatWingPanelShapeGivesIt()
    {
        Aircraft ac = At("extra-300-like");
        Ramp(ac, WingFailureLaw.FoldRatio(OneG(ac)) * ac.Config.Limits.PublishedLimits().Pos * 1.005, sec: 3.0);
        Assert.Equal(WingSideStage.Folded, ac.Structure.LeftWing.Stage);
        StripFlowState fs = ac.FlowStateForTests;
        var all = ac.Config.Surfaces.SelectMany(sf => sf.Strips).ToList();
        int checked_ = 0, inboard = 0;
        foreach (WingPanel p in ac.Panels)
        {
            Assert.NotEqual(0.0, p.FoldRad);
            foreach (int i in p.SideStrips)
            {
                Vec3 pos = all[i].PosVec();
                Vec3 want = p.Shape(pos) - pos;   // the aero flex is inside AeroBendRad; nothing extra to pass
                Assert.Equal(want.X, fs.BendOffset![i].X, 9);
                Assert.Equal(want.Y, fs.BendOffset[i].Y, 9);
                Assert.Equal(want.Z, fs.BendOffset[i].Z, 9);
                checked_++;
                if (Math.Abs(pos.Y) < p.BreakY) { inboard++; Assert.True(fs.BendOffset[i].LengthSquared > 1e-6); }
            }
        }
        _o.WriteLine($"{checked_} rows match WingPanel.Shape to 9 decimals, {inboard} of them INBOARD of the station " +
                     $"(a fold at the root moves those; a bend at the station cannot)");
        Assert.True(checked_ > 0 && inboard > 0);
    }

    /// <summary>
    /// A FOLD IS A KINK AT THE ROOT, and the renderer's cut depends on exactly that: nothing inboard of
    /// <see cref="WingPanel.RootY"/> moves, and everything outboard turns RIGIDLY, so the drawn wing can be one cut
    /// piece on one pivot rather than a deformed mesh. (A BEND stays a kink at the STATION — the other agent's aileron
    /// split at the station rests on that and is untouched here.)
    /// </summary>
    [Fact]
    public void AFoldTurnsTheWholeWingRigidlyAboutItsRootAndMovesNothingInboardOfIt()
    {
        Aircraft ac = At("extra-300-like");
        WingPanel p = ac.Panels.First(q => q.Side < 0);
        p.FoldRad = 60 * Deg;
        // Inboard of the root: untouched.
        Vec3 inboard = new(0.1, p.Side * 0.5 * p.RootY, 0.2);
        Assert.Equal(inboard.Y, p.FoldAt(inboard, p.FoldRad).Y, 12);
        Assert.Equal(inboard.Z, p.FoldAt(inboard, p.FoldRad).Z, 12);
        // Outboard of it: rigid — the distance from the root axis is preserved for every point, and the angle turned
        // is the fold, which is what lets one rigid transform carry an aileron (AirframeVisual.PanelMotion).
        foreach (double ay in new[] { 1.0, 2.0, 3.0, 4.0 })
        {
            Vec3 v = new(0.3, p.Side * ay, 0.1);
            Vec3 f = p.FoldAt(v, p.FoldRad);
            double r0 = Math.Sqrt(Math.Pow(v.Y - p.Side * p.RootY, 2) + Math.Pow(v.Z - p.BreakZ, 2));
            double r1 = Math.Sqrt(Math.Pow(f.Y - p.Side * p.RootY, 2) + Math.Pow(f.Z - p.BreakZ, 2));
            Assert.Equal(r0, r1, 9);                       // rigid
            Assert.Equal(v.X, f.X, 12);                    // about a fore-and-aft axis
            Assert.True(f.Z < v.Z, "a positive fold must take the wing UP (body z is DOWN)");
        }
        _o.WriteLine($"fold {p.FoldRad / Deg:F0}° about RootY {p.RootY:F2} m: rigid outboard, nothing inboard moved");
        p.FoldRad = 0.0;
    }

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
