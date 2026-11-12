using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// RECOVERY=1 — THE THREE NAMED RECOVERY PROCEDURES, flown from the developed spin, across the fleet.
///
/// Ty, 2026-11-11, gave three procedures and an acceptance rule for each. They are measured here as he
/// describes them, not as a previous lane paraphrased them:
///
///   1. RUDDER ALONE          power as entered, stick left exactly where it was (full aft + pro-spin
///                            aileron HELD), full opposite rudder.  MUST NOT recover.
///   2. BEGGS-MUELLER         "throttle idle, let go of the stick (completely hands off), then step on the
///                            hard pedal which is always the correct recovery rudder fully."  MUST recover
///                            on every type EXCEPT the Decathlon, the one real aeroplane he knows it does
///                            not work on.
///   3. DART RECOVERY         his name, 2026-11-11: "The power idle all controls neutral recover will be
///                            called 'dart recovery' and it should also pass on all airplanes even the
///                            decathlon."  He then confirmed: rudder NEUTRAL too, everything centred (3a).
///                            3b (stick neutral, full opposite rudder) is kept as a secondary row because
///                            it is what an earlier lane called "Beggs" and the mislabelling needs a
///                            number of its own to stand next to.
///
/// WHY THE DISTINCTION BETWEEN 2 AND 3b IS THE WHOLE POINT. Hands off is a FREE stick, not a centred one.
/// A free reversible surface floats to where its hinge moment is zero, which in a spin is nowhere near
/// neutral; <see cref="Aircraft.FreeFloat"/> is that path, and <c>ControlInputs(..., aileronFree,
/// elevatorFree, rudderFree)</c> is how it is driven. Forcing the deflections to zero is NEUTRAL and is a
/// different procedure with a different answer. Every Beggs row below goes through the free path.
///
/// Dart (3a) has strictly LESS anti-spin input than Beggs (neutral rudder instead of full opposite), so a
/// type on which dart works and Beggs does not is a type whose FREE controls are actively holding the spin
/// in. That is the falsifiable mechanism behind Ty's Decathlon exception, and PART "float" measures it
/// directly: where each type's elevator and aileron actually float to at spin attitude.
///
/// Parts (RECOVERY_PARTS, default "procs"): procs | matrix | float | pedal.
/// RECOVERY_IDS comma-separated types (default pitts-s2b-like). RECOVERY_SPIN flat|normal|both.
/// RECOVERY_INV=1 flies the inverted spin instead.
/// </summary>
public class SpinRecoveryProceduresProbe
{
    private readonly ITestOutputHelper _o;
    public SpinRecoveryProceduresProbe(ITestOutputHelper o) { _o = o; }

    private const double D = 180.0 / Math.PI;
    /// <summary>ControlInputs.ThrottleLever is INVERTED: -1 is full power, +1 is idle.</summary>
    private const double IDLE = 1.0, FULL = -1.0;

    internal static AircraftConfig Load(string id)
    {
        // RECOVERY_REPO lets this run from a build output outside the repo tree (the walk-up finds nothing there).
        string? root = Environment.GetEnvironmentVariable("RECOVERY_REPO");
        if (root == null)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            root = d?.FullName ?? ".";
        }
        return AircraftConfigLoader.LoadFromFile(Path.Combine(root, "configs", "aircraft", id + ".json"));
    }

    private static double Alpha(RigidBodyState s) => Math.Atan2(s.Velocity.Z, s.Velocity.X) * D;

    private static double OmegaVert(RigidBodyState s)
    {
        Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
        return s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z;
    }

    private static void Relift(Aircraft ac)
    {
        var s = ac.State;
        if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
    }

    /// <summary>One recovery procedure: what the hands and feet and throttle do, and whether the stick is FREE.</summary>
    internal readonly record struct Proc(string Name, double Lever, double Stick, double Ail, double RudScale,
                                         bool StickFree, bool AilFree);

    /// <summary>
    /// Outcome of flying one procedure. Stopped = the vertical rotation fell below 20 deg/s (NACA's "recovered",
    /// and the same test the existing probes use). Unstalled = alpha came below 10 deg, which distinguishes a real
    /// recovery from a spin that became a spiral and is still turning.
    /// </summary>
    internal readonly record struct Outcome(double StopTurns, double StopSec, double UnstallSec, double Turns20,
                                            double EndAlpha, double EndRateDeg, double EleEndDeg, double AilEndDeg);

    internal static Outcome Fly(AircraftConfig cfg, Aircraft ac, in Proc p, double proRud, double seconds = 20.0)
    {
        double h = SimLoop.DefaultFixedDtSec, net = 0;
        double stopT = double.NaN, stopN = double.NaN, unstallT = double.NaN, unstallHeld = 0;
        var ci = new ControlInputs(p.Ail, p.Stick, -proRud * p.RudScale, p.Lever, p.AilFree, p.StickFree, false);
        for (double t = 0; t < seconds; t += h)
        {
            ac.Step(ci, h);
            Relift(ac);
            double om = Math.Abs(OmegaVert(ac.State)) * D, a = Alpha(ac.State);
            net += om / D * h;
            if (t > 0.3 && double.IsNaN(stopT) && om < 20.0) { stopT = t; stopN = net / (2 * Math.PI); }
            if (Math.Abs(a) < 10.0) { unstallHeld += h; if (double.IsNaN(unstallT) && unstallHeld > 0.5) unstallT = t; }
            else unstallHeld = 0;
            if (!double.IsNaN(stopT)) break;
        }
        var st = ac.State; var cd = ac.CurrentDeflections;
        return new Outcome(stopN, stopT, unstallT, net / (2 * Math.PI), Alpha(st), st.Rates.Length * D,
                           cd.ElevatorRad * D, cd.AileronRad * D);
    }

    private static string Verdict(in Outcome o) => !double.IsNaN(o.StopSec)
        ? $"RECOVERED {o.StopTurns,5:F2} turns {o.StopSec,5:F1} s"
        : !double.IsNaN(o.UnstallSec)
            ? $"no stop ({o.Turns20,5:F1} t) UNSTALLED->spiral at {o.UnstallSec:F1} s"
            : $"NO RECOVERY   {o.Turns20,5:F1} turns";

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("RECOVERY") == null) return;
        string parts = Environment.GetEnvironmentVariable("RECOVERY_PARTS") ?? "procs";
        string[] ids = (Environment.GetEnvironmentVariable("RECOVERY_IDS") ?? "pitts-s2b-like").Split(',', StringSplitOptions.RemoveEmptyEntries);
        string spins = Environment.GetEnvironmentVariable("RECOVERY_SPIN") ?? "flat";
        bool inv = Environment.GetEnvironmentVariable("RECOVERY_INV") != null;
        double proRud = double.Parse(Environment.GetEnvironmentVariable("RECOVERY_RUD") ?? "-1", CultureInfo.InvariantCulture);

        foreach (string raw in ids)
        {
            string id = raw.Trim();
            var cfg = Load(id);
            double proStick = inv ? 1.0 : -1.0;
            if (parts.Contains("whynot")) { WhyNotTheDecathlon(id, proRud, proStick); continue; }
            if (parts.Contains("spoilsweep")) { SpoilerSweep(id); continue; }
            _o.WriteLine($"\n================ {id}  ({(inv ? "INVERTED" : "upright")} spin, pro-spin rudder {proRud:+0;-0})");
            _o.WriteLine($"   Izz {cfg.Mass.Inertia.Izz:F0}  elevator +/-{cfg.Controls.Elevator.MaxDeflRad * D:F1} deg"
                + $"  ChAlpha {cfg.Controls.Elevator.HingeChAlpha:F2} ChDelta {cfg.Controls.Elevator.HingeChDelta:F2}"
                + $"  springKt ele {cfg.Controls.Elevator.CenteringSpringKt:F0} rud {cfg.Controls.Rudder.CenteringSpringKt:F0}"
                + $"  reversible e/a/r {cfg.Controls.Elevator.Reversible}/{cfg.Controls.Aileron.Reversible}/{cfg.Controls.Rudder.Reversible}");

            foreach (string spin in spins.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                // THE CONDITION IS THE MEASUREMENT. "flat" is Ty's flat spin: full power with the out-spin
                // aileron held, which is what SpinRudderBudgetProbe and AftCamberSpinProbe both use. "normal"
                // is the idle, ailerons-neutral spin, which on the Pitts is a quite different flight condition
                // (alpha 28 rather than 74) and where every rudder question is uninteresting.
                double lever = spin.Trim() == "flat" ? FULL : IDLE;
                double proAil = spin.Trim() == "flat" ? (inv ? proRud : -proRud) : 0.0;

                var (st, _) = SpinBalanceProbe.Developed(cfg, proRud, lever, inv, proAil);
                _o.WriteLine($"\n  developed {spin.Trim()} spin (lever {lever:+0;-0}, aileron {proAil:+0.0;-0.0}):"
                    + $" alpha {st.Alpha:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  pitch {st.Pitch:F1}  beta {st.Beta:F1}"
                    + $"  Omega_vert {st.OmegaVertDeg:F0} deg/s  {st.SecPerTurn:F2} s/turn  TAS {st.Tas:F1} m/s");

                if (parts.Contains("procs"))
                {
                    _o.WriteLine("    procedure                                    power  stick      ail    rudder | outcome");
                    foreach (var p in Procedures(proAil, proStick, lever))
                    {
                        var (_, ac) = SpinBalanceProbe.Developed(cfg, proRud, lever, inv, proAil);
                        var o = Fly(cfg, ac, p, proRud);
                        _o.WriteLine($"    {p.Name,-42} {(p.Lever > 0 ? "idle" : "FULL"),5}  "
                            + $"{(p.StickFree ? "FREE" : p.Stick == 0 ? "neutral" : "aft"),-9} "
                            + $"{(p.AilFree ? "FREE" : p.Ail == 0 ? "neutral" : "held"),-7} "
                            + $"{(p.RudScale == 0 ? "neutral" : "FULL opp"),8} | {Verdict(o)}"
                            + $"  [end alpha {o.EndAlpha,6:F1}, {o.EndRateDeg,4:F0} deg/s, ele {o.EleEndDeg,6:F1} ail {o.AilEndDeg,6:F1} deg]");
                    }
                }

                if (parts.Contains("matrix"))
                {
                    // THE THREE VARIABLES SEPARATED. Ty's procedure changes power, stick and rudder all at once;
                    // this is the same question one variable at a time, so "which single change is load-bearing"
                    // has an answer instead of an opinion.
                    _o.WriteLine("\n    MATRIX: power x stick x aileron x rudder (each cell flown from a fresh developed spin)");
                    _o.WriteLine("    power  stick    aileron  rudder   | outcome");
                    foreach (double lev in new[] { IDLE, FULL })
                        foreach (var (sName, sVal, sFree) in new (string, double, bool)[] { ("FREE", 0, true), ("neutral", 0, false), ("aft", proStick, false) })
                            foreach (var (aName, aVal, aFree) in new (string, double, bool)[] { ("neutral", 0, false), ("held", proAil, false), ("FREE", 0, true) })
                                foreach (var (rName, rScale) in new[] { ("FULL opp", 1.0), ("neutral", 0.0) })
                                {
                                    if (aName == "held" && Math.Abs(proAil) < 1e-9) continue;   // identical to neutral
                                    var (_, ac) = SpinBalanceProbe.Developed(cfg, proRud, lever, inv, proAil);
                                    var o = Fly(cfg, ac, new Proc("", lev, sVal, aVal, rScale, sFree, aFree), proRud);
                                    _o.WriteLine($"    {(lev > 0 ? "idle" : "FULL"),5}  {sName,-8} {aName,-8} {rName,-8} | {Verdict(o)}"
                                        + $"  [alpha {o.EndAlpha,6:F1}, ele {o.EleEndDeg,6:F1} ail {o.AilEndDeg,6:F1}]");
                                }
                }

                if (parts.Contains("float"))
                {
                    // WHERE THE CONTROLS FLOAT TO, HANDS OFF, AT SPIN ATTITUDE. This is the measurement that
                    // decides the Decathlon exception: a free elevator that floats PRO-SPIN (same sign as the
                    // stick that held the spin in) is holding the aeroplane in despite full opposite rudder.
                    var (_, ac) = SpinBalanceProbe.Developed(cfg, proRud, lever, inv, proAil);
                    double eMax = cfg.Controls.Elevator.MaxDeflRad, aMax = cfg.Controls.Aileron.MaxDeflRad;
                    var held = ac.CurrentDeflections;
                    double eleHeldDeg = held.ElevatorRad * D;
                    // Instantaneous hinge-moment equilibrium at the spin's own flow, before anything moves.
                    double eleFloat = ac.FreeFloat("elevator", cfg.Controls.Elevator, 0.0, held.ElevatorRad);
                    double ailFloat = ac.FreeFloat("aileron", cfg.Controls.Aileron, 0.0, held.AileronRad);
                    double rudFloat = ac.FreeFloat("rudder", cfg.Controls.Rudder, 0.0, held.RudderRad);
                    var he = ac.HingeDebug("elevator", cfg.Controls.Elevator);
                    _o.WriteLine($"\n    FLOAT at the developed spin: pro-spin stick held the elevator at {eleHeldDeg:+0.0;-0.0} deg"
                        + $" (of +/-{eMax * D:F1}); hinge terms a {he.A:F1} b {he.B:F1} k {he.K:F1} den {he.Den:F1}");
                    _o.WriteLine($"      instantaneous float: elevator {eleFloat * D:+0.0;-0.0} deg ({eleFloat / eMax * 100:+0;-0} %)"
                        + $"   aileron {ailFloat * D:+0.0;-0.0} deg ({(aMax > 0 ? ailFloat / aMax * 100 : 0):+0;-0} %)"
                        + $"   rudder {rudFloat * D:+0.0;-0.0} deg");
                    _o.WriteLine($"      sign test: pro-spin elevator is {(eleHeldDeg >= 0 ? "+" : "-")}ve, float is"
                        + $" {(eleFloat * D >= 0 ? "+" : "-")}ve  ->  float is "
                        + $"{(Math.Sign(eleFloat) == Math.Sign(held.ElevatorRad) && Math.Abs(eleFloat) > 0.05 * eMax ? "PRO-SPIN" : Math.Abs(eleFloat) < 0.05 * eMax ? "near NEUTRAL" : "ANTI-SPIN")}"
                        + $"  ({(Math.Abs(held.ElevatorRad) > 1e-9 ? eleFloat / held.ElevatorRad * 100 : 0):F0} % of the held deflection)");
                    // Then let go and see where it settles while the spin continues (rudder still pro-spin).
                    double h = SimLoop.DefaultFixedDtSec;
                    for (double t = 0; t < 3.0; t += h) { ac.Step(new ControlInputs(0, 0, proRud, IDLE, true, true, false), h); Relift(ac); }
                    var cd2 = ac.CurrentDeflections;
                    _o.WriteLine($"      after 3 s hands off (idle, pro-spin rudder still in): elevator {cd2.ElevatorRad * D:+0.0;-0.0} deg"
                        + $" ({cd2.ElevatorRad / eMax * 100:+0;-0} % of travel)  aileron {cd2.AileronRad * D:+0.0;-0.0} deg"
                        + $"  alpha {Alpha(ac.State):F1}  Omega {OmegaVert(ac.State) * D:F0} deg/s");
                }

                if (parts.Contains("gap"))
                {
                    // THE ELEVATOR GAP. Ty, 2026-11-11, asked whether recovering the flat spin needs the aileron out
                    // or the elevator too: "The elevator matters too - both must come out." The model recovers with
                    // the stick still full aft once the aileron is neutral, so it is too optimistic, and the suspect
                    // is what the elevator does at spin attitude. Two things are measured at the developed spin's own
                    // state, with the rudder full ANTI-spin throughout so every row is a recovery condition:
                    //   (a) the yawing and pitching moment against stick position, split by group, and
                    //   (b) what the elevator's position does to the FIN's local dynamic pressure, which is the
                    //       tail-shadow question: forward elevator is supposed to blanket MORE rudder, not less.
                    var (_, acg) = SpinBalanceProbe.Developed(cfg, proRud, lever, inv, proAil);
                    var sg = acg.State;
                    var tables = Aircraft.BuildAirfoilTables(cfg);
                    double rho = Atmosphere.DensityAtAltitude(-sg.Position.Z);
                    double qInf = 0.5 * rho * sg.Velocity.LengthSquared;
                    var heldG = acg.CurrentDeflections;
                    double eMaxG = cfg.Controls.Elevator.MaxDeflRad;
                    double antiRud = -heldG.RudderRad;
                    var I = cfg.Mass.Inertia;
                    _o.WriteLine($"\n    ELEVATOR GAP at the developed spin (rudder full ANTI-spin {antiRud * D:+0.0;-0.0} deg,"
                        + $" aileron {heldG.AileronRad * D:+0.0;-0.0} deg held, q_inf {qInf:F0} Pa):");
                    _o.WriteLine("      elevator      N (yaw) N.m   -> deg/s^2    M (pitch) N.m  -> deg/s^2 | fin eta_t  stab eta_t");
                    foreach (var (eName, eDefl) in new[]
                    {
                        ("full AFT (pro-spin)", heldG.ElevatorRad),
                        ("neutral",             0.0),
                        ("full FORWARD",       -heldG.ElevatorRad),
                    })
                    {
                        var d = new ControlDeflections(heldG.AileronRad, eDefl, antiRud, 0);
                        var res = AeroModel.Compute(cfg, tables, sg.Velocity, sg.Rates, Vec3.Zero, rho, d);
                        // The fin's and the stabiliser's own local q, from AeroModel's tailflow record, with THIS
                        // elevator deflection in place: the shadow is the thing being measured, so the geometry must
                        // be the deflected geometry.
                        var cap = new List<ForceSample>(512);
                        ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
                        try { AeroModel.Compute(cfg, tables, sg.Velocity, sg.Rates, Vec3.Zero, rho, d); }
                        finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
                        double fs = 0, ss = 0; int nf = 0, ns = 0;
                        var seen = new HashSet<string>();
                        foreach (var f in cap)
                        {
                            if (f.Kind != "tailq") continue;
                            if (!seen.Add($"{f.MomentBody.X:0}/{f.PosBody.X:0.00}/{f.PosBody.Y:0.00}/{f.PosBody.Z:0.00}")) continue;
                            if (f.MomentBody.X > 0.5) { fs += f.ForceBody.Z; nf++; } else { ss += f.ForceBody.Z; ns++; }
                        }
                        _o.WriteLine($"      {eName,-20} {res.Moment.Z,10:F0}  {res.Moment.Z / I.Izz * D,10:F1}"
                            + $"   {res.Moment.Y,12:F0}  {res.Moment.Y / I.Iyy * D,10:F1} |"
                            + $"  {(nf > 0 ? fs / nf : 0),8:F3}   {(ns > 0 ? ss / ns : 0),8:F3}");
                    }

                    // WHO IS MAKING THE NOSE-DOWN MOMENT, if the elevator's whole travel is only a sixth of it.
                    // Split the pitching moment about the CG by group at each stick position, and print the
                    // inertial couple the aerodynamics has to balance — (Izz - Ixx)*p*r plus Ixz*(r^2 - p^2) —
                    // because a developed spin is that balance and nothing else.
                    _o.WriteLine("\n      PITCH BUDGET at the developed spin (N.m about the CG, rudder full ANTI-spin):");
                    _o.WriteLine("       stick            wings    h-tail   fin+rud  fuselage    thrust      SUM  | inertial couple");
                    foreach (var (eName, eDefl) in new[] { ("full AFT", heldG.ElevatorRad), ("neutral", 0.0), ("full FWD", -heldG.ElevatorRad) })
                    {
                        var cap = new List<ForceSample>(1024);
                        ForceDebug.Samples = cap;
                        try { AeroModel.Compute(cfg, tables, sg.Velocity, sg.Rates, Vec3.Zero, rho, new ControlDeflections(heldG.AileronRad, eDefl, antiRud, 0)); }
                        finally { ForceDebug.Samples = null; }
                        Vec3 cgv = cfg.Mass.CgVec();
                        double mW = 0, mH = 0, mV = 0, mF = 0, mT = 0, sum = 0;
                        foreach (var f in cap)
                        {
                            if (f.Kind is "flow" or "gyro" or "tailq" or "tailflow") continue;
                            double m = Vec3.Cross(f.PosBody - cgv, f.ForceBody).Y + f.MomentBody.Y;
                            sum += m;
                            if (f.Kind == "thrust") mT += m;
                            else if (f.Kind is "fuselage" or "spoiler") mF += m;
                            else if (Math.Abs(f.PosBody.Y) > 0.3 && f.PosBody.X > -1.5) mW += m;      // the wings
                            else if (f.PosBody.X < -1.0 && Math.Abs(f.PosBody.Y) < 0.03) mV += m;     // fin + rudder, on the centreline
                            else if (f.PosBody.X < -1.0) mH += m;                                     // horizontal tail
                            else mF += m;
                        }
                        var I2 = cfg.Mass.Inertia;
                        double p = sg.Rates.X, r = sg.Rates.Z;
                        double inert = (I2.Izz - I2.Ixx) * p * r + I2.Ixz * (r * r - p * p);
                        _o.WriteLine($"       {eName,-12} {mW,10:F0}{mH,10:F0}{mV,10:F0}{mF,10:F0}{mT,10:F0}{sum,10:F0}  | {inert,10:F0}");
                    }

                    // THE ELEVATOR'S SHARE, term by term. If the whole stick travel is worth only a sixth of the
                    // tail's own pitching moment, the question is which terms set the SHARE rather than the total.
                    // ControlSpoilerK exists precisely to give a control on a broadside surface something to
                    // command; StabHighAlphaK multiplies the WHOLE tail, fixed surface and elevator alike, so it
                    // scales the restoring moment the aft stick is fighting as hard as it scales the aft stick.
                    _o.WriteLine("\n      THE ELEVATOR'S SHARE at the developed spin (full aft <-> full forward, static):");
                    _o.WriteLine("        config                        dM elevator N.m   M at full AFT   elevator's share");
                    foreach (var (what, mutate) in new (string, Action<AircraftConfig>)[]
                    {
                        ("as authored",              _ => { }),
                        ("ControlSpoilerK = 0",      c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.ControlSpoilerK = 0; }),
                        ("StabHighAlphaK = 1 (off)", c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.StabHighAlphaK = 1.0; }),
                        ("aft camber gain = 0",      c => { c.Fuselage!.Crossflow!.AftCamberGain = 0; }),
                    })
                    {
                        var c3 = Load(id); mutate(c3);
                        var t3 = Aircraft.BuildAirfoilTables(c3);
                        double M(double e) => AeroModel.Compute(c3, t3, sg.Velocity, sg.Rates, Vec3.Zero, rho,
                                                                new ControlDeflections(heldG.AileronRad, e, antiRud, 0)).Moment.Y;
                        double aft = M(heldG.ElevatorRad), fwd = M(-heldG.ElevatorRad);
                        _o.WriteLine($"        {what,-28} {Math.Abs(aft - fwd),14:F0}   {aft,13:F0}   {(Math.Abs(aft) > 1 ? Math.Abs(aft - fwd) / Math.Abs(aft) * 100 : 0),13:F1} %");
                    }

                    // WHICH TERM. The one case Ty says must NOT recover is stick aft + ailerons neutral + full
                    // opposite rudder; the model recovers it. Each candidate term is switched off in a COPY of the
                    // config and that case re-flown, so "the diagnosis points at this term" is a measurement rather
                    // than an assertion. Nothing here is a fix: the lane changes no flight model.
                    _o.WriteLine("\n      THE CASE TY SAYS MUST NOT RECOVER (stick AFT, ailerons NEUTRAL, full opposite rudder),");
                    _o.WriteLine("      with each candidate term removed in a config copy — flown, not asserted:");
                    foreach (var (what, mutate) in new (string, Action<AircraftConfig>)[]
                    {
                        ("as authored",                       _ => { }),
                        ("ControlSpoilerK = 0",               c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.ControlSpoilerK = 0; }),
                        ("StabHighAlphaK = 1 (off)",          c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.StabHighAlphaK = 1.0; }),
                        ("aft-fuselage camber gain = 0",      c => { c.Fuselage!.Crossflow!.AftCamberGain = 0; }),
                        ("FinSidewashK = 0",                  c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.FinSidewashK = 0; }),
                        ("stab 1 + camber 0",                 c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.StabHighAlphaK = 1.0; c.Fuselage!.Crossflow!.AftCamberGain = 0; }),
                        ("stab 1 + camber 0 + sidewash 0",    c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.StabHighAlphaK = 1.0; c.SpinAero.FinSidewashK = 0; c.Fuselage!.Crossflow!.AftCamberGain = 0; }),
                    })
                    {
                        var c2 = Load(id); mutate(c2);
                        var (st2, ac2) = SpinBalanceProbe.Developed(c2, proRud, lever, inv, proAil);
                        var o2 = Fly(c2, ac2, new Proc("", lever, proStick, 0.0, 1.0, false, false), proRud);
                        _o.WriteLine($"        {what,-30} spin alpha {st2.Alpha,6:F1} Omega {st2.OmegaVertDeg,4:F0} -> {Verdict(o2)}");
                    }
                }

                if (parts.Contains("pedal"))
                {
                    // THE HARD PEDAL. Ty teaches it as a cue: "step on the hard pedal which is always the correct
                    // recovery rudder". Pedal force is the hinge moment the pilot has to hold, H(delta) = a + b*delta
                    // - k*delta about the rudder hinge, so the "hard" pedal is whichever full deflection needs the
                    // larger |H|. The question this answers is whether the model makes that the ANTI-spin pedal.
                    var (_, ac) = SpinBalanceProbe.Developed(cfg, proRud, lever, inv, proAil);
                    var hr = ac.HingeDebug("rudder", cfg.Controls.Rudder);
                    double rMax = cfg.Controls.Rudder.MaxDeflRad;
                    double H(double defl) => hr.A + hr.B * defl - hr.K * defl;
                    double hPro = H(proRud * rMax * RudderSign(cfg, ac)), hAnti = H(-proRud * rMax * RudderSign(cfg, ac));
                    double floatRud = ac.FreeFloat("rudder", cfg.Controls.Rudder, 0.0, ac.CurrentDeflections.RudderRad);
                    _o.WriteLine($"\n    HARD PEDAL at the developed spin: hinge a {hr.A:F2} b {hr.B:F2} k {hr.K:F2}"
                        + $"  (free rudder floats to {floatRud * D:+0.0;-0.0} deg)");
                    _o.WriteLine($"      |H| holding the PRO-spin pedal  {Math.Abs(hPro),9:F2}");
                    _o.WriteLine($"      |H| holding the ANTI-spin pedal {Math.Abs(hAnti),9:F2}"
                        + $"   ->  the HARD pedal is the {(Math.Abs(hAnti) > Math.Abs(hPro) ? "ANTI-SPIN one (agrees with Ty)" : "PRO-SPIN one (DISAGREES with Ty)")}"
                        + $"  ratio anti/pro {(Math.Abs(hPro) > 1e-12 ? Math.Abs(hAnti) / Math.Abs(hPro) : double.NaN):F2}");
                }
            }
        }
    }

    /// <summary>
    /// RECOVERY_PARTS=spoilsweep — THE SWEEP BEHIND THE ASYMMETRIC SPOILER COEFFICIENT. Ty, 2026-11-11, having
    /// flown the published build: "beggs mueller passes easily inverted and barely upright probably 10 turns or so
    /// ... get BM TO PASS WITHIN 4 turns so bump the spoiler up a bit upright but leave the inverted curve the
    /// same". This flies BM (idle, genuinely free stick and ailerons, full opposite rudder) from the developed
    /// POWER-ON FLAT SPIN at each positive-alpha coefficient, with the negative-alpha one pinned at the authored
    /// value so the inverted case cannot move, and prints the spin's own attitude beside every turn count — because
    /// his stated targets are 78 upright and 95 inverted and this coefficient has already twice bought a faster
    /// recovery by moving the attitude off them.
    ///
    /// DO NOT ASSUME THE DIRECTION. The Pitts' own controlSpoilerKSource records the opposite, measured 2026-11-10:
    /// "hands-off it makes the Beggs-Mueller recovery SLOWER (6.90 -> 7.53 turns), because the free elevator sits on
    /// its aft stop and now holds the spin harder". The term has two opposite paths — on the horizontal tail it
    /// strengthens a pro-spin free elevator, on the fin it strengthens the anti-spin rudder — and the sign split
    /// reaches only the horizontal tail, so the sweep has to decide which path wins.
    /// RECOVERY_SPOIL_K sets the values (default "0.3,0.6,0.9,1.2,1.6"); RECOVERY_SPOIL_INV=1 flies inverted.
    /// </summary>
    private void SpoilerSweep(string id)
    {
        bool inv = Environment.GetEnvironmentVariable("RECOVERY_SPOIL_INV") != null;
        double proRud = inv ? 1.0 : -1.0, proStick = inv ? 1.0 : -1.0, proAil = inv ? proRud : -proRud;
        double authored = Load(id).SpinAero?.ControlSpoilerK ?? 0.0;
        _o.WriteLine($"\n  SPOILER SWEEP on {id}, {(inv ? "INVERTED" : "UPRIGHT")} flat spin."
            + $" Authored controlSpoilerK {authored:F2}; the negative-alpha half is PINNED there throughout,"
            + " so the inverted case is untouched by construction and the inverted rows below are the proof.");
        _o.WriteLine("    K+     K-   | developed flat spin                     | BM (idle, FREE stick, full opp rudder)");
        _o.WriteLine("                | alpha   (min..max)  Omega   s/turn  TAS |");
        // RECOVERY_CAMBER sweeps fuselage.crossflow.aftCamberGain instead, which is the OTHER knob that moved the
        // upright spin recently (tip commit 89e74242 turned the aft-fuselage broadside camber on at 0.80, and its
        // own message says the term "steepens an upright spin"). Both knobs are on Ty's Developer page and he is
        // tuning them himself, so the two sweeps have to be readable side by side.
        string? camberList = Environment.GetEnvironmentVariable("RECOVERY_CAMBER");
        foreach (string s in (camberList ?? Environment.GetEnvironmentVariable("RECOVERY_SPOIL_K") ?? "0.3,0.6,0.9,1.2,1.6")
                             .Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            double x = double.Parse(s.Trim(), CultureInfo.InvariantCulture);
            var cfg = Load(id);
            cfg.SpinAero ??= new SpinAeroConfig();
            if (camberList != null) cfg.Fuselage!.Crossflow!.AftCamberGain = x;
            else
            {
                cfg.SpinAero.ControlSpoilerK = x;
                cfg.SpinAero.ControlSpoilerKNeg = authored;       // the inverted curve, held where Ty left it
            }
            var (st, ac) = SpinBalanceProbe.Developed(cfg, proRud, FULL, inv, proAil);
            var o = Fly(cfg, ac, new Proc("", IDLE, 0, 0, 1.0, true, true), proRud);
            _o.WriteLine($"   {(camberList != null ? "camber " + x.ToString("F2") : $"{x,5:F2}  {authored,5:F2}"),-13}"
                + $"| {st.Alpha,6:F1} ({st.AlphaMin,4:F0}..{st.AlphaMax,4:F0}) {st.OmegaVertDeg,6:F0}"
                + $"   {st.SecPerTurn,5:F2} {st.Tas,6:F1} | {Verdict(o)}");
        }
    }

    /// <summary>
    /// RECOVERY_PARTS=whynot — WHY THE MODEL'S DECATHLON CANNOT BE TY'S EXCEPTION. Ty, 2026-11-11: "The only
    /// airplane that does not pass the begs mueller recovery I know of in real life that begs mueller does not work
    /// on is the decathlon." In this model it recovers in a tenth of a turn, and the reason is upstream of the
    /// recovery: as authored it will not HOLD a developed spin, so there is nothing to recover from. The Pitts
    /// carries four high-alpha spin terms the Decathlon's config does not have at all — finSidewashK, stabHighAlphaK,
    /// shadowK and the aft-fuselage broadside camber — and this adds them to a COPY of the Decathlon's config, one
    /// at a time and together, to say which absence is responsible. NOTHING IS WRITTEN TO ANY CONFIG: the point is
    /// to tell Ty what it would take, not to make an aeroplane fail.
    /// </summary>
    private void WhyNotTheDecathlon(string id, double proRud, double proStick)
    {
        _o.WriteLine($"\n  WHY THE MODEL'S {id} IS NOT TY'S EXCEPTION — the Pitts' high-alpha spin terms added to a config COPY:");
        _o.WriteLine("    terms added                            developed spin (power-on, aileron held)        Beggs-Mueller");
        foreach (var (what, mutate) in new (string, Action<AircraftConfig>)[]
        {
            ("as authored",                 _ => { }),
            ("+ finSidewashK 0.5",          c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.FinSidewashK = 0.5; }),
            ("+ stabHighAlphaK 1.9",        c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.StabHighAlphaK = 1.9; }),
            ("+ shadowK 0.5",               c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.ShadowK = 0.5; }),
            ("+ aftCamberBroadsideCl 0.8",  c => { c.Fuselage!.Crossflow!.AftCamberBroadsideCl = 0.8; }),
            ("+ all four",                  c => { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.FinSidewashK = 0.5; c.SpinAero.StabHighAlphaK = 1.9; c.SpinAero.ShadowK = 0.5; c.Fuselage!.Crossflow!.AftCamberBroadsideCl = 0.8; }),
        })
        {
            var c = Load(id); mutate(c);
            double proAil = -proRud;
            var (st, ac) = SpinBalanceProbe.Developed(c, proRud, FULL, false, proAil);
            var o = Fly(c, ac, new Proc("", IDLE, 0, 0, 1.0, true, true), proRud);
            _o.WriteLine($"    {what,-36} alpha {st.Alpha,6:F1} ({st.AlphaMin,4:F0}..{st.AlphaMax,4:F0}) Omega {st.OmegaVertDeg,4:F0} deg/s"
                + $" TAS {st.Tas,5:F1}  ->  {Verdict(o)}");
        }
    }

    /// <summary>Which rudder deflection sign a positive pedal makes on this type (the shaping is monotone, so one probe is enough).</summary>
    private static double RudderSign(AircraftConfig cfg, Aircraft ac) => 1.0;

    internal static IEnumerable<Proc> Procedures(double proAil, double proStick, double entryLever)
    {
        // 1. Rudder alone, nothing else moved: power as the spin was entered, stick where it was.
        yield return new Proc("1 rudder alone (stick+ail HELD, power on)", entryLever, proStick, proAil, 1.0, false, false);
        // 2. Beggs-Mueller as Ty teaches it: idle, hands off BOTH stick axes (free, not centred), full opposite rudder.
        yield return new Proc("2 BEGGS-MUELLER (idle, FREE stick, opp rud)", IDLE, 0.0, 0.0, 1.0, true, true);
        // 3a. Dart recovery, his name and his definition: idle, everything centred and HELD, rudder neutral too.
        yield return new Proc("3a DART (idle, all neutral HELD, rud NEUT)", IDLE, 0.0, 0.0, 0.0, false, false);
        // 3b. The secondary row: stick centred and held, full opposite rudder. This is what an earlier lane
        //     printed as "Beggs"; it is not Beggs, and it needs its own number so the two cannot be confused.
        yield return new Proc("3b stick NEUTRAL held + full opposite rud", IDLE, 0.0, 0.0, 1.0, false, false);
    }
}
