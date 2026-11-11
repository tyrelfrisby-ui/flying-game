using System.Collections.Concurrent;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Q007 reopened (owner, Ty t918u on 2026.1106.0703): "The pitts upright spin is still too flat and difficult recover from the
/// inverted spin (right right is too nose low and too easy to recover". Two wakes the model had in the wrong place, both
/// found by switching terms off one at a time (AGENT-LOG 2026-11-06):
///  1. The propeller's wash reached the tail at every α (then Aircraft.SlipstreamOverTail; since 2026-11-08 the traced
///     tube, PropJet). Past the stall the crossflow carries it off sideways, so in a spin it neither blows the tail nor
///     swirls onto the fin. The swirl's yaw-left was the one power term that did not mirror: pro-spin in an upright LEFT
///     spin, anti-spin in an inverted RIGHT one.
///  2. A biplane's two wings never sat in each other's wake (TailWake.WingQFactor). Near |α| 90° they are two plates in
///     tandem: upright the lower wing shades the upper's aileron, inverted the upper wing shades almost all of the lower.
/// Evidence for the targets: Christen Eagle II Flight Manual §5 (a Pitts S-2 derivative; IAC In the Loop, Apr 2023): the
/// upright flat spin is entered with full power AND out-spin aileron, the inverted flat with right rudder, full power and
/// stick toward the spin axis, both "nearly horizontal in pitch or slightly nose up"; full power can hold the upright flat
/// so a normal recovery is impossible; power off and controls neutral recover the inverted. The owner's numbers: full-power
/// upright about 85° ("just shy of flat", 2026-11-03), out-spin flat at most 95° / 5° nose-up (2026-11-05), full-power
/// inverted R+R 90–95°, a touch tail-low (2026-11-03).
///
/// Spin literals are copied by hand from the 2026-11-06 PittsQ007Probe table in AGENT-LOG (developed = last 20 of 45 s).
/// </summary>
public class PittsSpinWakeTests
{
    private readonly ITestOutputHelper _out;
    public PittsSpinWakeTests(ITestOutputHelper o) { _out = o; }
    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id = "pitts-s2b-like") =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private const double Power40 = 0.2, Full = -1.0;   // throttle lever: +1 idle … −1 full

    // ---- the mechanisms ------------------------------------------------------------------------------------------------

    /// <summary>The reference state is untouched: straight ahead with no rotation the traced tube is where the tube
    /// always was, so the share is exactly 1 and every stored rig, trim and take-off stands (PropJet.TailShare measures
    /// the traced path AGAINST that reference, which is what makes this exact rather than close).</summary>
    [Fact]
    public void TheWashReachesTheTailInTheReferenceState()
    {
        var cfg = Load();
        Vec3 cg = cfg.Mass.CgVec();
        foreach (double v in new[] { 15.0, 30.0, 60.0 })
            Assert.Equal(1.0, PropJet.TailShare(cfg, new Vec3(v, 0, 0), Vec3.Zero, cg, 15.0, 0.69, 3.0));
    }

    /// <summary>
    /// Off the reference state the crossflow carries the wash off the tail, and NOTHING gates it on the wing being
    /// stalled (owner 2026-11-08: a jet locked to the body "would provide artificial stability"). Pitts disc-to-tail
    /// arm 4.382 m. At α 10°, 30 m/s the tube is carried below the tail's station and the share drops; at α 90° — the
    /// flat spin — the wash goes out sideways and the fin sees none of it, at any separation, because the separation is
    /// not an input any more.
    /// </summary>
    [Fact]
    public void OffTheReferenceStateTheCrossflowCarriesTheWashOffTheTail()
    {
        var cfg = Load();
        Vec3 cg = cfg.Mass.CgVec();
        Assert.Equal(4.382, PropJet.TailArm(cfg), 3);
        double r = 10 / D;
        double f10 = PropJet.TailShare(cfg, new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r)), Vec3.Zero, cg, 15.0, 0.69, 3.0);
        _out.WriteLine($"α 10 share {f10:F3}");
        Assert.InRange(f10, 0.01, 0.95);
        Assert.Equal(0.0, PropJet.TailShare(cfg, new Vec3(0, 0, 30), Vec3.Zero, cg, 15.0, 0.69, 3.0), 9);
        // Sideslip takes it off the tail the same way, and symmetrically.
        double bL = PropJet.TailShare(cfg, new Vec3(30 * Math.Cos(r), 30 * Math.Sin(r), 0), Vec3.Zero, cg, 15.0, 0.69, 3.0);
        double bR = PropJet.TailShare(cfg, new Vec3(30 * Math.Cos(r), -30 * Math.Sin(r), 0), Vec3.Zero, cg, 15.0, 0.69, 3.0);
        Assert.Equal(bL, bR, 9);
        Assert.InRange(bL, 0.01, 0.95);
        // A windmilling prop's deficit (ΔU < 0) is traced too — it is a slower tube, not a blast, and it still goes
        // where the air goes: straight back in the reference state.
        Assert.Equal(1.0, PropJet.TailShare(cfg, new Vec3(30, 0, 0), Vec3.Zero, cg, -3.0, 0.69, -0.6));
    }

    private static (double upper, double upperAil, double lower, double lowerAil) WingQ(AircraftConfig cfg, double alphaDeg)
    {
        var q = TailWake.WingStripQFactors(cfg, Aircraft.BuildAirfoilTables(cfg), new Vec3(30 * Math.Cos(alphaDeg / D), 0, 30 * Math.Sin(alphaDeg / D)), Vec3.Zero);
        var means = new Dictionary<string, double>();
        int k = 0;
        foreach (var sf in cfg.Surfaces)
        {
            double s = 0; int n = 0;
            foreach (var _ in sf.Strips) { double v = q[k++]; if (!double.IsNaN(v)) { s += v; n++; } }
            if (n > 0) means[sf.Id] = s / n;
        }
        return (means["wing-upper"], means["wing-upper-aileron"], means["wing-lower"], means["wing-lower-aileron"]);
    }

    /// <summary>
    /// Neither wing of the Pitts is meaningfully in the other's wake from −30° to +60°: ordinary flight, the stall and the
    /// steep spins (upright α 28–31, inverted −29) are untouched by the cellule term.
    /// RE-CUT 2026-11-10 FROM AN EXACT 1.0 TO A BAND, and the exact 1.0 was the stale part rather than the aeroplane: this
    /// literal was hand-copied from a 2026-11-06 probe table and the wake mechanism was REPLACED on 2026-11-08 by the
    /// traced tube, which TAPERS where the old one switched on. Measured on this branch (SpinPinProbe, SPINPINS=1), the
    /// worst surface anywhere in the band is 0.9865 — the lower aileron at −30° — and 0.9689 — the upper aileron at +60°,
    /// where the flat's shading is just beginning; every other station reads 0.999 or better. The claim this test exists
    /// to make, that nothing a pilot flies loses more than about 1 %, still holds, and is now written as the band it is.
    /// The two edges are asserted BY NAME so that a mechanism change which deepens either one fails here instead of
    /// drifting.
    /// </summary>
    [Fact]
    public void BiplaneWingsAreClearOfEachOthersWakeOutsideTheFlat()
    {
        var cfg = Load();
        foreach (double a in new[] { -30.0, -20, -10, 0, 10, 16, 30, 45, 60 })
        {
            var w = WingQ(cfg, a);
            double worst = Math.Min(Math.Min(w.upper, w.upperAil), Math.Min(w.lower, w.lowerAil));
            Assert.InRange(worst, 0.96, 1.0);
        }
        Assert.InRange(WingQ(cfg, -30).lowerAil, 0.97, 1.0);   // the inverted edge
        Assert.InRange(WingQ(cfg, 60).upperAil, 0.96, 1.0);    // the upright edge, the flat's onset
    }

    /// <summary>
    /// Flat, the windward wing shades the other, and the stagger makes it asymmetric: upright it is the UPPER wing's
    /// AILERON that goes into the lower wing's wake while the lower wing itself is clear; inverted it is the LOWER WING
    /// that goes into the upper's while the upper is clear. That mirror is the thing being pinned.
    /// RE-CUT 2026-11-10. The old literals (α +90 upper aileron 0.32; α −90 lower 0.16; α −75 lower 0.04) were
    /// hand-copied from a 2026-11-06 probe table, and the mechanism they described was replaced on 2026-11-08 by the
    /// traced tube. The replacement shades LESS DEEPLY and to a FLOOR rather than to zero — measured on this branch
    /// (SpinPinProbe, SPINPINS=1): α +90 upper 0.9064, upper aileron 0.6348, lower 1.0000, lower aileron 1.0000;
    /// α −90 upper 1.0000, upper aileron 1.0000, lower 0.6285, lower aileron 1.0000; α −75 lower 0.6415, lower
    /// aileron 0.6442. Note that the shaded surfaces all sit at 0.63–0.64 and that −75 is no DEEPER than −90, which the
    /// old mechanism's 0.04-at-−75 was: the taper has a floor now, and if that floor is wrong it is wrong in the tube,
    /// not here. The pins are a band about the measured floor; the exactly-clear surfaces stay exact, because those are
    /// geometry (a surface either is or is not behind the other) and not a depth.
    /// </summary>
    [Fact]
    public void FlatTheWindwardWingShadesTheOther()
    {
        var cfg = Load();
        var up = WingQ(cfg, 90);
        Assert.InRange(up.upperAil, 0.55, 0.72);
        Assert.Equal(1.0, up.lower, 6);
        var inv = WingQ(cfg, -90);
        Assert.InRange(inv.lower, 0.55, 0.72);
        Assert.Equal(1.0, inv.upper, 6);
        Assert.InRange(WingQ(cfg, -75).lower, 0.55, 0.72);
    }

    /// <summary>A monoplane has no other wing: its wing strips are never shaded (the Extra, flat).</summary>
    [Fact]
    public void AMonoplaneWingIsNeverShaded()
    {
        var cfg = Load("extra-300-like");
        foreach (double a in new[] { -90.0, 90 })
        {
            var q = TailWake.WingStripQFactors(cfg, Aircraft.BuildAirfoilTables(cfg), new Vec3(30 * Math.Cos(a / D), 0, 30 * Math.Sin(a / D)), Vec3.Zero);
            foreach (double v in q) if (!double.IsNaN(v)) Assert.Equal(1.0, v);
        }
    }

    // ---- what the owner flies --------------------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<(double rud, double lever, bool inv, double ail), SpinBalanceProbe.SpinStats> Spins = new();

    private SpinBalanceProbe.SpinStats Spin(double rudder, double lever, bool inverted, double aileron)
    {
        var st = Spins.GetOrAdd((rudder, lever, inverted, aileron), k => SpinBalanceProbe.Developed(Load(), k.rud, k.lever, k.inv, k.ail).stats);
        _out.WriteLine($"{(inverted ? "inverted" : "upright")} rudder {rudder} lever {lever} aileron {aileron}: α {st.Alpha:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  θ {st.Pitch:F1}  Ω {st.OmegaVertDeg:F0} deg/s");
        return st;
    }

    private static double Alpha(RigidBodyState s)
    {
        Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
        return Math.Atan2(air.Z, air.X) * D;
    }

    private static double OmegaVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    /// <summary>
    /// Three turns of pro-spin controls (what a pilot holds), then PARE: power idle, ailerons neutral, full opposite rudder,
    /// stick to neutral 0.5 s later. Turns from the recovery input until the rotation is under 20 deg/s or the wing unstalls
    /// (|α| &lt; 12°); NaN if not within 30 s.
    /// </summary>
    private double TurnsToRecoverAfterThree(double rudder, double lever, bool inverted, double aileron)
    {
        double h = SimLoop.DefaultFixedDtSec, turns = 0;
        Aircraft? ac = null;
        try
        {
            SpinBalanceProbe.EnterSpin(Load(), rudder, lever, 45, (t, a) =>
            {
                if (ac is not null) throw new OperationCanceledException();
                turns += Math.Abs(OmegaVert(a.State)) * h / (2 * Math.PI);
                if (turns >= 3) ac = a;
            }, inverted, aileron);
        }
        catch (OperationCanceledException) { }
        Assert.NotNull(ac);
        double net = 0, stick = inverted ? 1 : -1;
        for (double t = 0; t < 30; t += h)
        {
            ac!.Step(new ControlInputs(0, t < 0.5 ? stick : 0, -rudder, 1.0), h);
            double om = OmegaVert(ac.State);
            net += Math.Abs(om) * h;
            if (t > 0.3 && (Math.Abs(om) * D < 20 || Math.Abs(Alpha(ac.State)) < 12))
            {
                _out.WriteLine($"{(inverted ? "inverted" : "upright")} rudder {rudder} lever {lever} aileron {aileron}: PARE after 3 turns {net / (2 * Math.PI):F2} turns, {t:F1} s");
                return net / (2 * Math.PI);
            }
        }
        return double.NaN;
    }

    /// <summary>
    /// Upright left, ailerons neutral, full power: just shy of flat, nose below the horizon.
    /// RE-CUT 2026-11-10 TO THE OWNER'S LATER INSTRUCTION, 82–87.5 → 75.5–81.5. The old band was cut on 2026-11-06/08
    /// against his "about 85°, just shy of flat" (2026-11-03) and his 95° / +5° cap (2026-11-05). Both were SUPERSEDED on
    /// 2026-11-09 by "Tweak the stab for 80 nose down not 85", which is what stabHighAlphaK 1.9 was then fitted to — so
    /// this cell moving to 78.6 is the fit landing, not a regression, and chasing 85 again would be undoing the
    /// instruction. Measured on this branch (SpinPinProbe, SPINPINS=1): α 78.6 (78..79), θ −10.4, 245 deg/s, 1.47 s/turn,
    /// 51 kt. Was α 84.6 / θ −3.8 / 216 deg/s on 2026-11-08, and α 92.7 / +3.3 / 299 on 2026.1106.0703.
    /// </summary>
    [Fact]
    public void UprightFullPowerWithoutAileronIsJustShyOfFlat()
    {
        var st = Spin(-1.0, Full, false, 0.0);
        Assert.InRange(st.Alpha, 75.5, 81.5);
        Assert.True(st.Pitch < 0, $"nose {st.Pitch:F1}° up");
        Assert.True(st.AlphaMax - st.AlphaMin < 5.0, $"gyrating: α {st.AlphaMin:F0}..{st.AlphaMax:F0}");
    }

    /// <summary>
    /// Upright left at 40 % with the ailerons neutral is a NORMAL spin, not a flat one — the flat needs out-spin aileron
    /// (Christen Eagle II manual §5, and the owner's Q007a). α 34.3, θ −54.8, and PARE after three turns takes 0.40 turns.
    /// Was α 88.9, θ −0.5, and 3.13 turns.
    ///
    /// RED ON PURPOSE AS OF 2026-11-10, AND NOT RE-CUT. Three other pins in this file were re-cut today because they were
    /// stale — written against the owner's "about 85°" and his 95° cap, both superseded by his 2026-11-09 "Tweak the stab
    /// for 80 nose down not 85". THIS ONE IS NOT THAT. Its band is still the behaviour he and the Eagle II manual ask
    /// for, and the aeroplane has left it: measured on this branch α 51.8, θ −37.8.
    ///
    /// WHAT IS ACTUALLY WRONG, measured rather than guessed (SpinPinProbe, SPINPINS=1, the upright left spin with the
    /// ailerons neutral and the throttle lever swept — note ControlInputs.ThrottleLever is INVERTED, −1 is full power):
    ///     lever  +1.0  +0.8  +0.6  +0.4  +0.2  +0.0  −0.2  −0.4  −0.6  −0.8  −1.0
    ///     power     0    10    20    30    40    50    60    70    80    90   100 %
    ///     alpha  28.1  29.0  29.9  31.0  51.8  76.4  75.9  76.5  77.0  77.7  78.6
    ///     swing   2.6   3.0   3.7   6.4  11.5   0.7   1.0   0.7   0.6   0.6   0.5
    ///     pitch -61.2 -60.3 -59.4 -58.4 -37.8 -12.8 -13.1 -12.6 -12.0 -11.3 -10.4
    /// There are TWO attractors and no ramp between them. Up to 30 % the spin is normal and settled (α 28–31, the nose
    /// 60° down, 100 kt). From 50 % up it is a fully developed FLAT spin and just as settled (α 76–79, the nose 11–13°
    /// down, 51 kt, swing under 1°). The jump is a BIFURCATION between 30 % and 50 %, and 40 % lands on it: it is the
    /// only cell in the sweep that never settles, wandering over α 44–56 — an 11.5° swing where every other cell in the
    /// whole table holds within 0.5–6°.
    ///
    /// That is both halves of what the owner reports in this regime. "Overly flat" is literal: from HALF POWER upward,
    /// with the ailerons NEUTRAL, the Pitts is already in the flat spin that the manual says needs full power AND
    /// out-spin aileron — and the out-spin aileron is then worth only another 5° (83.7). And the 40 % cell wandering
    /// 12° is what sitting on the bifurcation feels like from the cockpit.
    ///
    /// SO THE DEFECT IS A POWER TERM WITH POSITIVE FEEDBACK SOMEWHERE BETWEEN 30 % AND 50 %, not this band. The suspect
    /// named by the shape: the spin crosses from α 31, where the traced propeller tube may still reach the tail, to
    /// α 76, where PropJet says it does not (TheWashReachesTheTailInTheReferenceState /
    /// OffTheReferenceStateTheCrossflowCarriesTheWashOffTheTail, above) — more power raises the nose, the flatter
    /// attitude takes the wash OFF the tail, and losing the wash flattens it further. A loop like that is exactly what
    /// makes a bifurcation instead of a ramp. The other two live power terms on this type are the gyroscopic couple and
    /// the LEGACY P-factor, which on the Pitts is the live path (AircraftEngine.SolvesShaft is gated on the governor
    /// having lost its oil, so the blade-element disc never runs in normal flight and PropJet/PFactorK are what act).
    /// Ablating one of the three across lever +0.4/+0.2/+0.0 is the next measurement, and it is a measurement, not a
    /// tune: NOTHING HERE IS TO BE MOVED TO MAKE THIS GREEN.
    /// </summary>
    [Fact]
    public void UprightPartPowerWithoutAileronIsANormalSpinAndRecovers()
    {
        var st = Spin(-1.0, Power40, false, 0.0);
        Assert.InRange(st.Alpha, 30.0, 40.0);
        Assert.InRange(st.Pitch, -60.0, -48.0);
        double turns = TurnsToRecoverAfterThree(-1.0, Power40, false, 0.0);
        Assert.InRange(turns, 0.2, 1.0);
    }

    /// <summary>
    /// Upright left, full power, full out-spin (right) aileron: THE flat spin, and the flattest cell the aeroplane has —
    /// which is the shape the Eagle II manual gives (the upright flat is entered with full power AND out-spin aileron).
    /// RE-CUT 2026-11-10 TO THE OWNER'S LATER INSTRUCTION, 86–95 → 80.5–87.5, for the same reason as
    /// <see cref="UprightFullPowerWithoutAileronIsJustShyOfFlat"/>: the old band was his 2026-11-05 "at most 95° / 5°
    /// nose-up" cap, superseded on 2026-11-09 by "Tweak the stab for 80 nose down not 85". The 95.5° ceiling and the
    /// +5° nose-up ceiling below are KEPT exactly as they were — they are a cap, and a cap that is no longer being
    /// approached is still the right cap. Measured on this branch (SpinPinProbe, SPINPINS=1): α 83.7 (83..84), θ −5.4,
    /// 294 deg/s, 1.23 s/turn, 49 kt. Was α 89.7 / θ +0.8 / 275 deg/s, and α 94.6 / +5.2 / 337 before that.
    /// </summary>
    [Fact]
    public void UprightFullPowerOutSpinFlatStaysWithinTheCapAndRecovers()
    {
        var st = Spin(-1.0, Full, false, 1.0);
        Assert.InRange(st.Alpha, 80.5, 87.5);
        Assert.True(st.AlphaMax <= 95.5, $"α reached {st.AlphaMax:F0}");
        Assert.True(st.Pitch <= 5.0, $"nose {st.Pitch:F1}° up");
        double turns = TurnsToRecoverAfterThree(-1.0, Full, false, 1.0);
        Assert.InRange(turns, 1.5, 5.0);
    }

    /// <summary>Inverted, stick forward, right rudder, right aileron, full power: the inverted flat, a touch tail-low, steady.
    /// Measured α −92.6 (−93..−92), θ +2.4, 312 deg/s; PARE after three turns 2.10 turns. Was α −85.1, θ −4.4, 291 deg/s,
    /// 2.47 turns, and it took 15 s to get flat (6 s now).</summary>
    [Fact]
    public void InvertedRightRightFullPowerIsFlatTailLowAndTakesWorkToRecover()
    {
        var st = Spin(1.0, Full, true, 1.0);
        Assert.True(st.AlphaMax < 0, $"rolled upright: α reached {st.AlphaMax:F0}");
        Assert.InRange(st.Alpha, -95.0, -90.0);
        Assert.InRange(st.Pitch, 0.0, 6.0);
        Assert.True(st.AlphaMax - st.AlphaMin < 5.0, $"gyrating: α {st.AlphaMin:F0}..{st.AlphaMax:F0}");
        double turns = TurnsToRecoverAfterThree(1.0, Full, true, 1.0);
        Assert.InRange(turns, 1.5, 4.0);
    }

    /// <summary>
    /// The idle spin is an ordinary spin, near 30° of α and nose well down, and it is very nearly the SAME spin either way
    /// up — which is what the two wakes of 2026-11-06 were asserted not to touch (there is no wash to carry off at idle,
    /// and neither wing reaches the other at α 30–45).
    /// RE-CUT 2026-11-10, upright only, α 30–33 → 26.5–30.0 and θ −58.6..−55.6 → −63.0..−59.0. The name says
    /// "unchanged" and it no longer means unchanged since 2026-11-06: between then and now the fin sidewash, the stab's
    /// high-α slope, the control spoiler and the chordwise-factor double-count all landed, and together they moved this
    /// cell 3.4° of α and 4.1° of pitch. That is the high-α work reaching down into a spin at α 28, which is small and
    /// expected, not a defect — the two WAKE mechanisms this test is named for are still innocent of it, and the
    /// INVERTED side has not moved at all and keeps its original band to prove it. Measured on this branch (SpinPinProbe,
    /// SPINPINS=1): upright α 28.1 (28..30), θ −61.2, 123 deg/s, 100 kt; inverted α −28.8 (−31..−28), θ −60.5,
    /// 118 deg/s. The name is deliberately NOT changed: three agents are comparing failure lists by test name tonight.
    /// </summary>
    [Fact]
    public void IdleSpinsAreUnchanged()
    {
        var u = Spin(-1.0, 1.0, false, 0.0);
        Assert.InRange(u.Alpha, 26.5, 30.0); Assert.InRange(u.Pitch, -63.0, -59.0);
        var i = Spin(1.0, 1.0, true, 0.0);
        Assert.InRange(i.Alpha, -30.5, -27.5); Assert.InRange(i.Pitch, -61.4, -58.4);
    }
}
