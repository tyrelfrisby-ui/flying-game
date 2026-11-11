using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (AIL_BREAK=1, AIL_BREAK_ID=pitts-s2b-like): what aileron does to an upright spin that is ALREADY
/// DEVELOPED, as against what it does to an incipient one. Ty 2026-11-09: "I was able to recover from the upright
/// flat spin in the pitts by using pro-spin aileron."
///
/// WHY IT EXISTS. <see cref="FlatSpinProbe"/> sweeps the aileron over a hold that begins after only 10 s of idle
/// pro-spin controls, and at that instant the aeroplane is NOT spinning — it is in a 97 kt nose-down autorotative
/// roll (α 28, θ −70, p −1.87, r −1.05, p/r = 1.8, the spiral's own geometry). Whatever the aileron does there, it
/// is deciding whether the spin DEVELOPS, not whether a developed spin recovers. This probe separates the two:
///   DEVELOPED: 60 s of pro-spin controls with the ailerons NEUTRAL, which reaches the flat spin, and only then is
///   the aileron applied and held 40 s.
///   INCIPIENT (AIL_BREAK_ENTRY=1): FlatSpinProbe's sequence, with the second-by-second history through it.
/// AIL_BREAK_LEVER sets the hold's throttle lever (−1 full … +1 idle; 0.2 is 40 %).
///
/// WHAT IT FOUND, 2026-11-10, and the first part of it is a correction to a measurement, not an aeroplane.
///
/// 1. THE AILERON IS NOT BREAKING A STALL THAT THE ELEVATOR IS HOLDING. The report it was sent to chase —
///    FlatSpinProbe's α 82.7 → 26.6 at 122 kt on pro-spin aileron — is FlatSpinProbe's entry, not a recovery. At the
///    instant that probe applies the aileron the Pitts is at α 28.1 and 103 kt (the figure is reproduced in this
///    probe's own idle cell), which is a steep spiral and not a spin at all. Applied to a spin that has actually
///    developed, full pro-spin aileron and full power takes α 79.9 → 72.7 and the rotation 253 → 195 deg/s: flatter
///    spin, less flat, still a spin, 55 kt. It does not unstall anything. ALWAYS DEVELOP THE SPIN BEFORE SWEEPING A
///    CONTROL; the spin is bistable and an incipient one answers a different question.
///
/// 2. WHAT IS REAL IS AT 40 % POWER, which is the regime Ty trains in: the developed spin sits at α 44.5 and 79 kt,
///    and full pro-spin aileron takes it to α 26.4 at 122 kt — out of the stall, with the rudder and the elevator
///    still pegged pro-spin, which Ty says must not be possible. Out-spin aileron from the same state goes the other
///    way to α 80.1. At 40 % the aeroplane is poised exactly on the bifurcation that
///    PittsSpinWakeTests.UprightPartPowerWithoutAileronIsANormalSpinAndRecovers documents, and an aileron either way
///    picks which attractor it falls into. The two reports are one defect.
///
/// 3. THE SINGLE LEVER IS "FLAP NORMAL" IN AeroModel, and nothing else is close. Ablated one term at a time (a
///    three-line temporary patch inside the `isAileronStrip` block: zero `controlDeltaAlpha`, or NaN
///    `hingedDragDelta`, or zero `flapSep`/`flapDelta`, each behind an env switch — not kept, src/ is parity-shared):
///    killing the aileron's CAMBER changes the departure by 0.1° of α, killing the hinged row's DRAG by 0.1°, and
///    killing FLAP NORMAL restores the flat spin outright (α 26.6 → 78.3). The camber and the drag are irrelevant
///    because both are cut by (1 − 0.85·sep)·(1 − 0.5·yawGate·stalled) to about 7 %, and `sep` reads 1.000 on every
///    aileron strip in the spin — which also retires the note in Aircraft.FreeFloat that "Separation reads ~0 for
///    these aileron strips even at α 78". It does not; it reads exactly 1.
///
/// 4. BUT FLAP NORMAL IS NOT TOO BIG, AND THIS IS THE FINDING: THE RUDDER IS GONE. Yaw moments at the ONE developed
///    state, surfaces slewed to their stops first (see the note on Yaw() below — this matters, the un-slewed numbers
///    were wrong by 15×), against the SAME rudder at the SAME airspeed with the wing attached and no rotation:
///      lever   α     the rudder, pro-spin → centred   full pro-spin aileron   the same rudder, unstalled
///      +1.0   28.2            +2729 N·m                     +1449 N·m               −2662 N·m  (103 % kept)
///      +0.2   44.5            +1369                          +1124                  −2780      ( 49 % kept)
///      −1.0   79.9             +293                           +608                  −2986      ( 10 % kept)
///    The rudder loses 90 % of its yaw power by the flat spin — the documented arithmetic wall, `fin-lowAR` holding
///    Cl flat from 25° out — while the aileron's separated-flow couple is GATED ON separation and so arrives at full
///    strength. By α 80 one aileron out-yaws the fully deflected rudder 2:1. Checked against a derivation rather than
///    assumed: FLAP NORMAL at broadside is cn·sin δ ≈ 1.9 × 0.42 = 0.80 on the aileron's own area against a plate's
///    own 1.9·sin δ·cos δ = 0.72, and the measured couple implies 0.59 — right, if anything conservative. So DO NOT
///    WEAKEN THE AILERON to close this: that would hide the rudder's absence behind a term Ty flew and approved on
///    2026-11-03 (PittsFlatSpinAileronTests), and the ratio would still be wrong. The open item this belongs to is
///    "spin control authority is absent, not weak" — the control-surface model at high α, on the FIN.
/// </summary>
[Collection("WorldTerrainActive")]
public class AileronStallBreakProbe
{
    private readonly ITestOutputHelper _out;
    public AileronStallBreakProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    private static void Wrap(Aircraft ac)
    {
        var s = ac.State;
        if (-s.Position.Z < 800) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
    }

    private static double Alpha(RigidBodyState s) => Math.Atan2(s.Velocity.Z, s.Velocity.X) * 57.2957795;

    /// <summary>Spin entry shared with FlatSpinProbe: decelerate pulling, then 10 s of full aft + full left rudder at idle.</summary>
    private static (Aircraft ac, SimLoop sim) Entry(AircraftConfig cfg)
    {
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, -1, -1, 1.0));
        return (ac, sim);
    }

    private string Mean(Aircraft ac, SimLoop sim, ControlInputs ci, double seconds, double measureLast)
    {
        int n = 0; double a = 0, th = 0, p = 0, q = 0, r = 0, kt = 0, om = 0;
        int steps = (int)Math.Round(seconds / 0.1), from = (int)Math.Round((seconds - measureLast) / 0.1);
        for (int i = 0; i < steps; i++)
        {
            sim.RunFor(0.1, ci); Wrap(ac);
            if (i < from) continue;
            var s = ac.State;
            a += Alpha(s); th += FormationPilot.Euler(s.Attitude).Item2 * 57.3;
            p += s.Rates.X; q += s.Rates.Y; r += s.Rates.Z; kt += s.Velocity.Length * 1.943844;
            Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0, 0, 1));
            om += Math.Abs(s.Rates.X * zB.X + s.Rates.Y * zB.Y + s.Rates.Z * zB.Z) * 57.3;
            n++;
        }
        return $"α {a / n,6:F1}  θ {th / n,6:F1}  p {p / n,6:+0.00;-0.00}  q {q / n,6:+0.00;-0.00}  r {r / n,6:+0.00;-0.00}  Ω {om / n,4:F0} deg/s  {kt / n,4:F0} kt";
    }

    [Fact]
    public void ProbeAileronStallBreak()
    {
        if (Environment.GetEnvironmentVariable("AIL_BREAK") == null) return;
        var cfg = Load(Environment.GetEnvironmentVariable("AIL_BREAK_ID") ?? "pitts-s2b-like");
        double lever = double.Parse(Environment.GetEnvironmentVariable("AIL_BREAK_LEVER") ?? "-1.0", System.Globalization.CultureInfo.InvariantCulture);
        _out.WriteLine($"{cfg.Id}  throttle lever {lever:+0.00;-0.00} (−1 full, +1 idle), full LEFT rudder, full AFT stick throughout");

        if (Environment.GetEnvironmentVariable("AIL_BREAK_ENTRY") != null)
        {
            double ailE = double.Parse(Environment.GetEnvironmentVariable("AIL_BREAK_AIL") ?? "-1.0", System.Globalization.CultureInfo.InvariantCulture);
            var (ace, sime) = Entry(cfg);
            _out.WriteLine($"  INCIPIENT (FlatSpinProbe's sequence): power and aileron {ailE:+0.0;-0.0} applied together after 10 s of idle pro-spin controls");
            _out.WriteLine($"    at the instant the aileron goes on: {Mean(ace, sime, new ControlInputs(0, -1, -1, 1.0), 0.1, 0.1)}");
            var hold = new ControlInputs(ailE, -1, -1, lever);
            for (int t = 1; t <= 50; t++) _out.WriteLine($"    t {t,3}s  {Mean(ace, sime, hold, 1.0, 0.1)}");
            return;
        }

        // DEVELOPED: 60 s with the ailerons neutral, then the aileron for 40 s. Re-flown from scratch per aileron so
        // every case starts from the same spin rather than from the previous case's wreckage of one.
        foreach (double ail in new[] { 0.0, -1.0, -0.5, 0.5, 1.0 })
        {
            var (ac, sim) = Entry(cfg);
            string dev = Mean(ac, sim, new ControlInputs(0, -1, -1, lever), 60.0, 10.0);
            string after = Mean(ac, sim, new ControlInputs(ail, -1, -1, lever), 40.0, 10.0);
            _out.WriteLine($"  developed (ail 0): {dev}");
            _out.WriteLine($"    then aileron {ail,5:+0.00;-0.00} 40 s: {after}");
            if (ail == 0.0)
            {
                // The aileron's yaw couple against the RUDDER'S OWN yaw power, read off the one developed state.
                var s0 = ac.State; var fs0 = ac.FlowStateForTests; double[] save = (double[])fs0.Separation.Clone();
                var cgv = cfg.Mass.CgVec();
                // THE SURFACES SLEW (Aircraft.SlewTo, Controls.*.RateRadPerSec): one step after a step input reads the
                // moment of a surface that has moved 4-5 deg, not of a deflected one. So hold the input for 1 s with
                // the BODY STATE PUT BACK after every step — the surface travels to its stop, the flight condition
                // never moves — and only then capture.
                double Yaw(double a2, double r2)
                {
                    var ci2 = new ControlInputs(a2, -1, r2, lever);
                    for (int k = 0; k < 50; k++) { ac.State = s0; Array.Copy(save, fs0.Separation, save.Length); ac.Step(ci2, SimLoop.DefaultFixedDtSec); }
                    ac.State = s0; Array.Copy(save, fs0.Separation, save.Length);
                    ac.CaptureForces = true; ac.Step(ci2, SimLoop.DefaultFixedDtSec); ac.CaptureForces = false;
                    double n2 = 0;
                    foreach (var f in ac.LastForces)
                    {
                        if (f.Kind is "tailflow" or "weight" or "gyro" or "propn") continue;
                        n2 += Vec3.Cross(f.PosBody - cgv, f.ForceBody).Z + f.MomentBody.Z;
                    }
                    return n2;
                }
                double nBase = Yaw(0, -1), nAil = Yaw(-1, -1), nAilOut = Yaw(1, -1), nRudN = Yaw(0, 0), nRudOpp = Yaw(0, 1);
                // The SAME rudder at the SAME airspeed with the wing attached and no rotation, for scale: how much of
                // its yaw power the spin has taken away.
                double tas = s0.Velocity.Length, aLvl = 5.0 / 57.2957795;
                double nLvl0 = 0, nLvl1 = 0;
                foreach (double rr in new[] { 0.0, -1.0 })
                {
                    var acL = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1),
                        new Vec3(tas * Math.Cos(aLvl), 0, tas * Math.Sin(aLvl)), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
                    var simL = new SimLoop(acL);
                    simL.RunFor(0.2, new ControlInputs(0, 0, rr, lever));
                    acL.CaptureForces = true; acL.Step(new ControlInputs(0, 0, rr, lever), SimLoop.DefaultFixedDtSec); acL.CaptureForces = false;
                    double n3 = 0;
                    foreach (var f in acL.LastForces)
                    {
                        if (f.Kind is "tailflow" or "weight" or "gyro" or "propn") continue;
                        n3 += Vec3.Cross(f.PosBody - cgv, f.ForceBody).Z + f.MomentBody.Z;
                    }
                    if (rr == 0.0) nLvl0 = n3; else nLvl1 = n3;
                }
                _out.WriteLine($"    for scale: at the SAME {tas * 1.943844:F0} kt, wings level at α 5 and no rotation, full rudder is worth {nLvl1 - nLvl0:+0;-0} N·m");
                _out.WriteLine($"    yaw moment N at that ONE state (N·m, −N = pro-spin for a LEFT spin):"
                    + $"\n      full pro-spin rudder, ailerons neutral      {nBase,8:+0;-0}"
                    + $"\n      ... rudder centred   → the rudder is worth  {nRudN - nBase,8:+0;-0}"
                    + $"\n      ... full OPPOSITE rudder → worth            {nRudOpp - nBase,8:+0;-0}"
                    + $"\n      ... full PRO-spin (in-spin) aileron → worth {nAil - nBase,8:+0;-0}"
                    + $"\n      ... full OUT-spin aileron → worth           {nAilOut - nBase,8:+0;-0}");
                ac.State = s0; Array.Copy(save, fs0.Separation, save.Length);
                var fs = ac.FlowStateForTests; int idx = 0; var sb = new System.Text.StringBuilder();
                foreach (var sf in cfg.Surfaces)
                    foreach (var st in sf.Strips)
                    {
                        int i = idx++;
                        if (st.Control is null || st.Control.Surface != "aileron" || Math.Abs(st.Control.Gain) < 0.9) continue;
                        sb.Append($"y{st.Pos[1]:+0.0;-0.0} sep {fs.Separation[i]:F2} α{fs.LocalAlphaRad[i] * 57.3:F0}  ");
                    }
                _out.WriteLine("    hinged aileron strips in that spin: " + sb);
            }
        }
    }
}
