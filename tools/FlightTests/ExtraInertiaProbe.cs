using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE EXTRA'S BUILT-UP INERTIA DOES, MEASURED ON TODAY'S AEROPLANE.
///
/// Ty, offered the choice of having the inertia change put on a build by itself so he could feel it without the rest
/// of lane/claude-inertia: <em>"I will fly it and say."</em> This probe is the "and say" half — the numbers he is
/// flying against, measured on the code that is actually going to be built, not on the lane the change came from.
///
/// THE CHANGE. configs/aircraft/extra-300-like.json carried Ixx/Iyy/Izz/Ixz = 800/1100/1800/30 from
/// tools/aircraft_builder/build_batch1.py (f1acf66, 2026-09-07), where the comment said "from agent research" and
/// the aeroplane was a single-seat 300S whose config tail arm was 1.10 m SHORT of the model's. The part-by-part
/// build-up in docs/aircraft/extra-300-inertia-DERIVATION.md (engine dry weight from FAA TCDS 1E4 Rev 25 Note 5,
/// propeller, occupant and fuel at the 300L POH arms, structure groups by Raymer Table 15.2 scaled to the published
/// 1470 lb empty weight, each distributed over its real geometry, parallel-axis about the composite CG, the fuselage
/// density ramp solved for the certificated CG) gives 701/2038/2575/80. That landed on the config in 6cd3e904
/// (2026-11-09) together with the Pitts', because Ty flying 2026.1109.1334 said the Extra "is too stable. it has the
/// opposite problem it doesn't spin flat enough and can't tumble".
///
/// PITCH IS THE ONE THAT MOVED: Iyy +85 %, ky 1.16 -> 1.58 m. The derivation's sensitivity run says the old number is
/// not inside any reasonable version of this airframe (halve BOTH tail groups and Iyy is still about 1780), so this
/// is not a tuning knob — it is a correction. But a correction he can feel is still a correction he has to accept,
/// and a sharp pull is where he will feel it.
///
/// WHY THE 2 NOVEMBER FIGURES (280 -> 169 deg/s^2) ARE NOT REPEATED ANYWHERE AS FACT. They were measured on
/// lane/claude-inertia, and between then and now this aeroplane got: all 26 fins moved to the low-aspect-ratio plate
/// (daf52725), a propeller that is a state rather than a thrust number with sourced blade inertia (04501162,
/// e15d397c), the measured propeller station (1452c53a), the elevator's saturation removed from every type
/// (591e3279), two degrees of toe-out and hinged gear legs (ae2d0408, 4805726a) and the rudder-as-spoiler work that
/// explicitly gave "the Extra ... both controls back" (51c2841b). Any of those moves a pitch step. So this probe
/// re-measures both inertias on TODAY's config and reports today's pair.
///
/// EXTRA_INERTIA=1 prints the table. The OLD inertia is a literal here, exactly as InertiaTailCaseProbe held both
/// sets as literals on the lane: the "before" column has to keep meaning the same thing whatever the config file
/// later says, and this file must never be the thing that decides what ships.
/// </summary>
[Collection("WorldTerrainActive")]
public class ExtraInertiaProbe
{
    private readonly ITestOutputHelper _out;
    public ExtraInertiaProbe(ITestOutputHelper o) { _out = o; }
    private const double D = 180.0 / Math.PI;

    /// <summary>(Ixx, Iyy, Izz, Ixz) kg m^2 — the Extra's config before 6cd3e904, i.e. build_batch1.py's 300S guess.</summary>
    public static readonly double[] OldInertia = { 800.0, 1100.0, 1800.0, 30.0 };

    private static string Repo { get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; } }

    /// <summary>Today's config; <paramref name="old"/> puts the pre-build-up inertia back and changes nothing else.</summary>
    public static AircraftConfig Case(string id, bool old)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", id + ".json"));
        if (old) c.Mass.Inertia = new InertiaConfig { Ixx = OldInertia[0], Iyy = OldInertia[1], Izz = OldInertia[2], Ixz = OldInertia[3] };
        return c;
    }

    private static double AlphaDeg(RigidBodyState s)
    {
        Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
        return Math.Atan2(air.Z, air.X) * D;
    }

    /// <summary>Rotation about the EARTH vertical: what a spin's turn rate means (FleetSpinSweep's own measure).</summary>
    private static double OmVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    private static Aircraft Trimmed(AircraftConfig cfg, double v, double alt)
    {
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, v, alt, idleProp: true);
        var att = new Quat(0, Math.Sin(t.ThetaRad / 2), 0, Math.Cos(t.ThetaRad / 2));
        return new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -alt), att,
            new Vec3(v * Math.Cos(t.AlphaRad), 0, v * Math.Sin(t.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, t.ElevatorRad, 0, 0));
    }

    /// <summary>A long manoeuvre must not be allowed to fly into the ground; put it back up where it started.</summary>
    private static void Lift(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600)
            ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 2500),
                ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }

    /// <summary>
    /// THE MEASUREMENT TY WILL FEEL: step the stick to full aft from an idle glide trim at 1.5 Vso, 2500 m, and take
    /// the peak pitch angular acceleration in the first half second. Identical recipe to
    /// InertiaTailCaseProbe.PitchStep on lane/claude-inertia, so the before/after here is comparable with the lane's
    /// own numbers even though the aeroplane under it has changed.
    ///
    /// The peak is taken over 0.5 s and not from the first step because the stick shaping and the surface rate limit
    /// mean the elevator is not at full travel on step one: the first sample under-reads the step response's initial
    /// acceleration, which is what dM/Iyy actually is.
    /// </summary>
    public static (double QDot0, double Q05, double Q10) PitchStep(AircraftConfig cfg)
    {
        double vs = PracticeScenario.EstimateVso(cfg, 2500, 0.0), v = 1.5 * vs;
        Aircraft ac = Trimmed(cfg, v, 2500);
        double h = SimLoop.DefaultFixedDtSec, qd = 0, q5 = 0;
        var aft = new ControlInputs(0, -1.0, 0, 1.0);
        double qPrev = ac.State.Rates.Y;
        for (int i = 1; i <= (int)Math.Round(1.0 / h); i++)
        {
            ac.Step(aft, h);
            if (i <= (int)Math.Round(0.5 / h)) qd = Math.Max(qd, (ac.State.Rates.Y - qPrev) / h);
            qPrev = ac.State.Rates.Y;
            if (i == (int)Math.Round(0.5 / h)) q5 = ac.State.Rates.Y;
        }
        return (qd * D, q5 * D, ac.State.Rates.Y * D);
    }

    public sealed class SpinResult { public double EntryS = double.NaN, RateDps, SecPerTurn, FtPerTurn, PitchDeg, AlphaDeg, Stalled; }

    /// <summary>
    /// The widget's / FleetSpinSweep's upright spin recipe: min-sink trim, idle, full aft stick until alpha passes the
    /// type's own stall by 2 deg, then full pro-spin rudder held 45 s; reported over the LAST 20 s so the incipient
    /// phase is excluded. This is the one the inertia change was MADE for — Izz - Ixx is the term in (Izz-Ixx).p.r
    /// that holds a spin flat, and it goes from 1000 to 1874.
    /// </summary>
    public static SpinResult Spin(AircraftConfig cfg)
    {
        double stallDeg = StallAuthorityTests.StallDeg(cfg);
        double v = ThermalSpawn.MinSinkSpeed(cfg, 2500);
        Aircraft ac = Trimmed(cfg, v, 2500);
        double h = SimLoop.DefaultFixedDtSec;
        for (double t = 0; t < 25; t += h) { ac.Step(new ControlInputs(0, -1.0, 0, 1.0), h); if (AlphaDeg(ac.State) > stallDeg + 2) break; }
        var pro = new ControlInputs(0, -1.0, -1.0, 1.0);
        var om = new List<double>(); var a = new List<double>(); var pit = new List<double>();
        double net = 0, lost = 0;
        for (double t = 0; t < 45; t += h)
        {
            double z0 = ac.State.Position.Z;
            ac.Step(pro, h);
            RigidBodyState s = ac.State;
            if (double.IsNaN(s.Position.Z)) throw new InvalidOperationException("NaN in the spin");
            om.Add(OmVert(s));
            if (t >= 25)
            {
                a.Add(AlphaDeg(s)); pit.Add(FormationPilot.Euler(s.Attitude).pitch * D);
                net += OmVert(s) * h; lost += s.Position.Z - z0;
            }
            Lift(ac);
        }
        var r = new SpinResult();
        double turns = Math.Abs(net) / (2 * Math.PI), devRate = net / 20.0;
        r.RateDps = Math.Abs(devRate) * D;
        r.SecPerTurn = 20 / Math.Max(0.01, turns);
        r.FtPerTurn = lost * 3.2808 / Math.Max(0.01, turns);
        r.AlphaDeg = a.Average();
        r.PitchDeg = pit.Average();
        r.Stalled = a.Count(x => x > stallDeg + 1) / (double)a.Count;
        double cum = 0;
        for (int i = 0; i < om.Count; i++)
        {
            cum += om[i] * h;
            if (Math.Abs(om[i]) >= 0.9 * Math.Abs(devRate) && Math.Sign(om[i]) == Math.Sign(devRate)) { r.EntryS = (i + 1) * h; break; }
        }
        return r;
    }

    public sealed class SnapResult { public double PeakP, PeakR, To360 = double.NaN, AlphaMax, AlphaAt360 = double.NaN; }

    /// <summary>
    /// SNAP ROLL (flick). From level cruise at <paramref name="iasMph"/>: full aft stick and full rudder together,
    /// everything else neutral, power on. A snap is an autorotation of the stalled wing, so what the inertia change
    /// reaches it through is the ENTRY — the pitch rate that takes the wing past its stall before the rudder's yaw
    /// can start it rotating. Iyy is the denominator of that pitch rate, so a snap that used to break cleanly can
    /// come out late or mushy with Iyy nearly doubled, and that is a thing Ty would notice without being told.
    /// Reported: peak roll and yaw rate, seconds to the first 360 deg of roll, and the alpha it got there on.
    /// </summary>
    public static SnapResult Snap(AircraftConfig cfg, double iasMph)
    {
        double v = iasMph * 0.44704, alpha = 2.0 * Math.PI / 180.0, h = SimLoop.DefaultFixedDtSec;
        var st = new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1),
                                    new Vec3(v * Math.Cos(alpha), 0, v * Math.Sin(alpha)), Vec3.Zero);
        var ac = new Aircraft(cfg, st, ControlDeflections.Neutral);
        var r = new SnapResult();
        var snap = new ControlInputs(0, -1.0, 1.0, -1.0);
        double roll = 0;
        for (double t = 0; t < 5.0; t += h)
        {
            ac.Step(snap, h);
            RigidBodyState s = ac.State;
            roll += Math.Abs(s.Rates.X) * h;
            r.PeakP = Math.Max(r.PeakP, Math.Abs(s.Rates.X) * D);
            r.PeakR = Math.Max(r.PeakR, Math.Abs(s.Rates.Z) * D);
            r.AlphaMax = Math.Max(r.AlphaMax, AlphaDeg(s));
            if (double.IsNaN(r.To360) && roll >= 2 * Math.PI) { r.To360 = t + h; r.AlphaAt360 = AlphaDeg(s); }
            Lift(ac);
        }
        return r;
    }

    public sealed class EndoResult { public double PitchTurns, PeakQ, FinalQ, AlphaMin = 999, AlphaMax = -999, MinTas = 999; }

    /// <summary>
    /// THE ENDO, on ExtraEndoProbe's own entry: at the top of a decaying vertical line (pitch 88 deg, 10 m/s, no
    /// rotation), full aft stick and full power, 10 s. An endo is end-over-end rotation about the SPAN axis, so Iyy
    /// is literally its denominator: the propeller's gyroscopic couple and the fuselage crossflow are the numerator
    /// and nothing at the tail can stop it. Ty 2026-11-09: the Extra "can't tumble". Nearly doubling Iyy pushes
    /// against that, which is exactly why it has to be measured and not assumed.
    /// </summary>
    public static EndoResult Endo(AircraftConfig cfg)
    {
        double th = 88.0 / D;
        var att = new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(10, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral);
        double h = SimLoop.DefaultFixedDtSec, netQ = 0;
        var r = new EndoResult();
        var inp = new ControlInputs(0, -1.0, 0, -1.0);
        for (double t = 0; t < 10.0; t += h)
        {
            ac.Step(inp, h);
            RigidBodyState s = ac.State;
            netQ += s.Rates.Y * h;
            r.PeakQ = Math.Max(r.PeakQ, Math.Abs(s.Rates.Y) * D);
            r.FinalQ = s.Rates.Y * D;
            double a = AlphaDeg(s);
            r.AlphaMin = Math.Min(r.AlphaMin, a); r.AlphaMax = Math.Max(r.AlphaMax, a);
            r.MinTas = Math.Min(r.MinTas, s.Velocity.Length);
            Lift(ac);
        }
        r.PitchTurns = Math.Abs(netQ) / (2 * Math.PI);
        return r;
    }

    /// <summary>
    /// THE GUARD THAT WAS MISSING. The build-up was derived on 2026-11-01 and sat UNUSED on lane/claude-inertia for
    /// eight days: 6cd3e904's own message says so ("THE INERTIAS WERE NEVER APPLIED"), and what Ty flew in the
    /// meantime was an aeroplane with about half its real pitch and yaw inertia. Nothing in the suite noticed,
    /// because nothing in the suite knew what the number was supposed to be. So assert it: the Extra's config
    /// carries the component build-up, and it says where the build-up is written down.
    ///
    /// This is deliberately a literal-against-literal check and not a recomputation. build_inertia.py is not on this
    /// branch (it is lane/claude-inertia's, and that lane does not merge — it conflicts on AeroModel.cs, PropModel.cs,
    /// Aircraft.cs, AircraftConfig.cs, glide-table.json and 13 configs), so the only thing available to check against
    /// here is the derivation's published result. If the build-up is ever re-run and gives something else, this test
    /// is where that argument happens.
    /// </summary>
    [Fact]
    public void TheExtraCarriesItsDerivedBuildUpInertia()
    {
        AircraftConfig c = Case("extra-300-like", old: false);
        InertiaConfig i = c.Mass.Inertia!;
        Assert.Equal(701.0, i.Ixx, 0);
        Assert.Equal(2038.0, i.Iyy, 0);
        Assert.Equal(2575.0, i.Izz, 0);
        Assert.Equal(80.0, i.Ixz, 0);
        // "inertiaSource" is a comment in the file, not a field on MassConfig (the loader ignores keys it has no
        // property for), so it is checked as text. Never a number without saying where it came from.
        string json = File.ReadAllText(Path.Combine(Repo, "configs", "aircraft", "extra-300-like.json"));
        Assert.Contains("extra-300-inertia-DERIVATION.md", json);
    }

    /// <summary>
    /// Initial pitch acceleration from a step to full aft is the elevator's moment over Iyy,
    /// q-dot ~ q_bar.S_e.tau.a_t.delta.l_t / Iyy. MEASURED ON THIS BRANCH 2026-11-10: 187 deg/s^2 with the shipped
    /// build-up inertia, 315 with the pre-6cd3e904 1100 — a factor of 1.68 on a factor of 1.85 in Iyy, the rest being
    /// the half-second the peak is taken over.
    ///
    /// The band is +/- 40 % of that measurement, which is the spread of the estimate itself: +/- 10 % on Iyy (the
    /// derivation's own sensitivity run), plus the stick ramp and the surface rate limit inside the 0.5 s window. It
    /// is NOT a tolerance on the model — it is wide enough that this week's aerodynamic work does not trip it and
    /// narrow enough that putting 1100 back does. That is the failure it exists to catch: on 2026-11-09 the derived
    /// inertia had to be applied by hand because nothing had noticed it was missing.
    /// </summary>
    [Fact]
    public void TheExtrasPitchAccelerationIsItsElevatorMomentOverTheBuildUpIyy()
    {
        var (qd, q5, q10) = PitchStep(Case("extra-300-like", old: false));
        _out.WriteLine($"extra-300-like: peak q-dot {qd:F0} deg/s2, q {q5:F0} deg/s at 0.5 s, {q10:F0} at 1.0 s");
        Assert.InRange(qd, 112, 262);
    }

    /// <summary>EXTRA_INERTIA=1: the before/after table on today's code. EXTRA_INERTIA_ID=&lt;id&gt; for another type.</summary>
    [Fact]
    public void BeforeAndAfter()
    {
        if (Environment.GetEnvironmentVariable("EXTRA_INERTIA") == null) return;
        string id = Environment.GetEnvironmentVariable("EXTRA_INERTIA_ID") ?? "extra-300-like";
        foreach (bool old in new[] { true, false })
        {
            AircraftConfig c = Case(id, old);
            InertiaConfig i = c.Mass.Inertia!;
            double ky = Math.Sqrt(i.Iyy / c.Mass.MassKg);
            var ps = PitchStep(c);
            SpinResult sp = Spin(c);
            SnapResult sn = Snap(c, 160.0);
            EndoResult en = Endo(c);
            _out.WriteLine($"== {id} {(old ? "OLD   " : "SHIPPED")} Ixx {i.Ixx:F0} Iyy {i.Iyy:F0} Izz {i.Izz:F0} Ixz {i.Ixz:F0}  " +
                           $"ky {ky:F2} m  Izz-Ixx {i.Izz - i.Ixx:F0}");
            _out.WriteLine($"   pitch step  peak q-dot {ps.QDot0,6:F0} deg/s2   q(0.5s) {ps.Q05,5:F0}   q(1.0s) {ps.Q10,5:F0} deg/s");
            _out.WriteLine($"   spin        entry {sp.EntryS,5:F1} s  {sp.RateDps,4:F0} deg/s  {sp.SecPerTurn,5:F2} s/turn  {sp.FtPerTurn,4:F0} ft/turn  " +
                           $"alpha {sp.AlphaDeg,4:F0}  pitch {sp.PitchDeg,4:F0}  stalled {sp.Stalled,4:P0}");
            _out.WriteLine($"   snap roll   peak p {sn.PeakP,5:F0} deg/s  peak r {sn.PeakR,4:F0}  to 360 {sn.To360,5:F2} s  " +
                           $"alpha at 360 {sn.AlphaAt360,4:F0}  alpha max {sn.AlphaMax,4:F0}");
            _out.WriteLine($"   endo        {en.PitchTurns,5:F2} turns  peak q {en.PeakQ,4:F0} deg/s  final q {en.FinalQ,5:F0}  " +
                           $"alpha {en.AlphaMin,4:F0}..{en.AlphaMax,-4:F0}  min TAS {en.MinTas,5:F1} m/s");
        }
    }
}
