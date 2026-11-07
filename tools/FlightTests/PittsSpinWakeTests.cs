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
///  1. The propeller's wash reached the tail at every α (Aircraft.SlipstreamOverTail). Past the stall the crossflow carries
///     it off sideways (Glauert's skewed wake), so in a spin it neither blows the tail nor swirls onto the fin. The swirl's
///     yaw-left was the one power term that did not mirror: pro-spin in an upright LEFT spin, anti-spin in an inverted
///     RIGHT one.
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

    /// <summary>Attached flow is untouched: with no separation the share is exactly 1 at any α (rigs, trim and take-off unchanged).</summary>
    [Fact]
    public void SlipstreamReachesTheTailWhileTheWingIsAttached()
    {
        var cfg = Load();
        foreach (double a in new[] { 0.0, 10, 45, 90 })
        {
            double r = a / D;
            Assert.Equal(1.0, Aircraft.SlipstreamOverTail(cfg, new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r)), 15.0, 0.69, 0.0));
        }
    }

    /// <summary>
    /// Separated, the skewed wake by hand. Pitts disc-to-tail arm: hub 0.1 m ahead of the nose (rearmost trailing edge
    /// −3.8064 − 0.2 + fuselage 5.189 + 0.1 = 1.2826 m) to the tail strips' area centroid (−3.0995 m) = 4.382 m. At α 10°,
    /// V 30 m/s, ΔU 15 m/s, tube radius 0.69 m: crossflow 5.2094, axial 29.5442 + 11.25 = 40.7942, shift 0.5596 m,
    /// d = 0.4055, share (2/π)(acos d − d·√(1 − d²)) = 0.498. At α 90° the wash goes out sideways: 0.
    /// </summary>
    [Fact]
    public void SeparatedTheCrossflowCarriesTheWashOffTheTail()
    {
        var cfg = Load();
        double arm = Aircraft.DiscToTailArm(cfg);
        _out.WriteLine($"arm {arm:F3} m");
        Assert.Equal(4.382, arm, 3);
        double r = 10 / D;
        double f10 = Aircraft.SlipstreamOverTail(cfg, new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r)), 15.0, 0.69, 1.0);
        _out.WriteLine($"α 10 share {f10:F3}");
        Assert.Equal(0.498, f10, 3);
        Assert.Equal(0.0, Aircraft.SlipstreamOverTail(cfg, new Vec3(0, 0, 30), 15.0, 0.69, 1.0), 9);
        // Half separated: half the loss.
        Assert.Equal(1.0 - 0.5 * (1.0 - f10), Aircraft.SlipstreamOverTail(cfg, new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r)), 15.0, 0.69, 0.5), 9);
        // A windmilling prop's deficit (ΔU < 0) and a twin (no single nose prop) are left alone.
        Assert.Equal(1.0, Aircraft.SlipstreamOverTail(cfg, new Vec3(0, 0, 30), -3.0, 0.69, 1.0));
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

    /// <summary>Neither wing of the Pitts sees the other's wake from −30° to +60° (every strip exactly 1): ordinary flight, the
    /// stall and the steep spins (upright α 31–43, inverted −29 to −34) are unchanged by the cellule term. Inverted, the upper
    /// wing's wake starts to reach the lower ailerons at −35° (q 0.98), −45° (0.27).</summary>
    [Fact]
    public void BiplaneWingsAreClearOfEachOthersWakeOutsideTheFlat()
    {
        var cfg = Load();
        foreach (double a in new[] { -30.0, -20, -10, 0, 10, 16, 30, 45, 60 })
        {
            var w = WingQ(cfg, a);
            Assert.Equal(1.0, Math.Min(Math.Min(w.upper, w.upperAil), Math.Min(w.lower, w.lowerAil)), 9);
        }
    }

    /// <summary>
    /// Flat, the windward wing shades the other, and the stagger makes it asymmetric. Measured 2026-11-06 (mean q per surface,
    /// V 30 m/s, no rotation): α +90 upper 0.99, upper aileron 0.32, lower 1.00, lower aileron 1.00; α −90 upper 1.00, upper
    /// aileron 1.00, lower 0.16, lower aileron 0.96; α −75 lower 0.04.
    /// </summary>
    [Fact]
    public void FlatTheWindwardWingShadesTheOther()
    {
        var cfg = Load();
        var up = WingQ(cfg, 90);
        Assert.InRange(up.upperAil, 0.22, 0.42);
        Assert.Equal(1.0, up.lower, 6);
        var inv = WingQ(cfg, -90);
        Assert.InRange(inv.lower, 0.06, 0.26);
        Assert.Equal(1.0, inv.upper, 6);
        Assert.InRange(WingQ(cfg, -75).lower, 0.0, 0.14);
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

    /// <summary>Upright left, ailerons neutral, full power: just shy of flat, nose below the horizon. Measured α 84.6 (84..85),
    /// θ −3.8, 216 deg/s. Was (2026.1106.0703) α 92.7, θ +3.3, 299 deg/s.</summary>
    [Fact]
    public void UprightFullPowerWithoutAileronIsJustShyOfFlat()
    {
        var st = Spin(-1.0, Full, false, 0.0);
        Assert.InRange(st.Alpha, 82.0, 87.5);
        Assert.True(st.Pitch < 0, $"nose {st.Pitch:F1}° up");
        Assert.True(st.AlphaMax - st.AlphaMin < 5.0, $"gyrating: α {st.AlphaMin:F0}..{st.AlphaMax:F0}");
    }

    /// <summary>Upright left at 40 % with the ailerons neutral is a normal spin now (the flat needs out-spin aileron): α 34.3,
    /// θ −54.8; and PARE after three turns takes 0.40 turns. Was α 88.9, θ −0.5, and 3.13 turns.</summary>
    [Fact]
    public void UprightPartPowerWithoutAileronIsANormalSpinAndRecovers()
    {
        var st = Spin(-1.0, Power40, false, 0.0);
        Assert.InRange(st.Alpha, 30.0, 40.0);
        Assert.InRange(st.Pitch, -60.0, -48.0);
        double turns = TurnsToRecoverAfterThree(-1.0, Power40, false, 0.0);
        Assert.InRange(turns, 0.2, 1.0);
    }

    /// <summary>Upright left, full power, full out-spin (right) aileron: the flat, within the owner's 95° / 5°. Measured α 89.7
    /// (89..92), θ +0.8, 275 deg/s; PARE after three turns 3.85 turns (Eagle II manual: the upright flat wants its own
    /// procedure; PARE still gets it out). Was α 94.6, θ +5.2, 337 deg/s, 5.75 turns.</summary>
    [Fact]
    public void UprightFullPowerOutSpinFlatStaysWithinTheCapAndRecovers()
    {
        var st = Spin(-1.0, Full, false, 1.0);
        Assert.InRange(st.Alpha, 86.0, 95.0);
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

    /// <summary>Idle is unchanged by both wakes (no wash to carry off; neither wing reaches the other at α 30–45): upright left
    /// neutral α 31.5, θ −57.1; inverted right neutral α −29.0, θ −59.9 (as on 2026.1106.0703).</summary>
    [Fact]
    public void IdleSpinsAreUnchanged()
    {
        var u = Spin(-1.0, 1.0, false, 0.0);
        Assert.InRange(u.Alpha, 30.0, 33.0); Assert.InRange(u.Pitch, -58.6, -55.6);
        var i = Spin(1.0, 1.0, true, 0.0);
        Assert.InRange(i.Alpha, -30.5, -27.5); Assert.InRange(i.Pitch, -61.4, -58.4);
    }
}
