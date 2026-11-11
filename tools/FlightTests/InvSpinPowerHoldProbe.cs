using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (INVHOLD=1). THE CHRISTEN EAGLE REALITY CHECK, and the pitch budget behind it.
///
/// The Eagle manual on the inverted flat spin: FULL POWER HOLDS IT, and POWER OFF WITH THE CONTROLS NEUTRAL
/// RECOVERS. Our model does the opposite — power on and the Pitts leaves the spin in 3.5 s. So this flies the
/// inverted spin and then branches at a fixed time into the four cells (power on/off x controls held/neutral),
/// and reports which of them hold and which recover.
///
/// INVHOLD_BUDGET=1 instead logs the PITCH moment budget down the divergence, because the trajectory says the
/// departure is a pitch tumble and not a yaw divergence: alpha runs -64 -> -121 -> through tail-first flow at
/// q = -186 deg/s. The budget is split the way the aeroplane is built — wing, horizontal tail, fin, fuselage —
/// plus the three prop terms separately (gyroscopic couple, disc normal force, the rest of the thrust group) and
/// the rigid-body inertia coupling, so the term that pitches it over can be named rather than guessed at.
///
/// INVHOLD_ID picks the type(s), INVHOLD_AIL the held aileron, INVHOLD_T the branch time.
/// </summary>
public class InvSpinPowerHoldProbe
{
    private readonly ITestOutputHelper _out;
    public InvSpinPowerHoldProbe(ITestOutputHelper o) { _out = o; }

    private const double Full = -1.0, Idle = 1.0;   // ControlInputs.ThrottleLever: -1 is full power, +1 is idle

    private static string Repo
    {
        get
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            return d!.FullName;
        }
    }

    private static double Env(string k, double d)
    {
        string? s = Environment.GetEnvironmentVariable(k);
        return s is null ? d : double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("INVHOLD") is null) return;
        string ids = Environment.GetEnvironmentVariable("INVHOLD_ID") ?? "pitts-s2b-like";
        double ail = Env("INVHOLD_AIL", 1.0);
        bool budget = Environment.GetEnvironmentVariable("INVHOLD_BUDGET") is not null;
        bool upright = Environment.GetEnvironmentVariable("INVHOLD_UPRIGHT") is not null;
        foreach (string id in ids.Split(','))
        {
            var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", id.Trim() + ".json"));
            if (Environment.GetEnvironmentVariable("INVHOLD_IPROP") is { } ip)
            { cfg.Propulsion!.PropInertia = double.Parse(ip, System.Globalization.CultureInfo.InvariantCulture); cfg.Id += $"[Ip={ip}]"; }
            if (Environment.GetEnvironmentVariable("INVHOLD_SPOIL") is { } sp)
            { cfg.SpinAero ??= new SpinAeroConfig(); cfg.SpinAero.ControlSpoilerK = double.Parse(sp, System.Globalization.CultureInfo.InvariantCulture); cfg.Id += $"[spoil={sp}]"; }
            // The upright flat spin's two keys, overridable here so the cost of an UPRIGHT fix can be read on the
            // INVERTED spin in the same session. Ty 2026-11-10 on the Great Lakes: "similar overly flat and overly
            // difficult upright flat spin recovery. Inverted is fine" — so the pair that fixes the upright side has
            // to be measured HERE before it ships. BeggsTimingProbe (BEGGS_FINK / BEGGS_STABK) sweeps the same two
            // keys for the upright side; this is the other half of that table.
            if (Environment.GetEnvironmentVariable("INVHOLD_FINK") is { } fk)
            { cfg.SpinAero ??= new SpinAeroConfig(); cfg.SpinAero.FinSidewashK = double.Parse(fk, System.Globalization.CultureInfo.InvariantCulture); cfg.Id += $"[finK={fk}]"; }
            if (Environment.GetEnvironmentVariable("INVHOLD_STABK") is { } sk)
            { cfg.SpinAero ??= new SpinAeroConfig(); cfg.SpinAero.StabHighAlphaK = double.Parse(sk, System.Globalization.CultureInfo.InvariantCulture); cfg.Id += $"[stabK={sk}]"; }
            if (budget) _out.WriteLine(Budget(cfg, ail, upright));
            else _out.WriteLine(Reality(cfg, ail, upright));
        }
    }

    /// <summary>Enter the spin, then branch into the four cells and report each.</summary>
    private string Reality(AircraftConfig cfg, double ail, bool upright)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"=== {cfg.Id}  {(upright ? "UPRIGHT" : "INVERTED")} spin, right rudder, pro-spin aileron {ail:0.00}\n");
        sb.Append($"    entry: {(upright ? "pull" : "push")} to the break at full power, rudder in, hold {Env("INVHOLD_T", 12.0):0}s, then branch for 25 s\n");
        sb.Append("    cell                      outcome                        mean alpha   mean |w|   |bank|>120\n");
        foreach ((string what, double thr, bool hold) in new[]
                 { ("power ON,  controls HELD ", Full, true),
                   ("power OFF, controls NEUTRAL", Idle, false),
                   ("power ON,  controls NEUTRAL", Full, false),
                   ("power OFF, controls HELD ", Idle, true) })
            sb.Append(Cell(cfg, ail, upright, what, thr, hold));
        return sb.ToString();
    }

    private static string Cell(AircraftConfig cfg, double ail, bool upright, string what, double thr, bool hold)
    {
        double tBranch = Env("INVHOLD_T", 12.0);
        var (ac, sim, s) = Enter(cfg, upright, tBranch);
        var ci = hold ? new ControlInputs(ail, s, 1, thr) : new ControlInputs(0, 0, 0, thr);
        double aSum = 0, wSum = 0, invSec = 0, stoppedSec = 0, stoppedAt = -1, wMin = 9e9;
        int n = 0;
        for (int step = 0; step < 100; step++)
        {
            sim.RunFor(0.25, ci);
            Wrap(ac);
            var st = ac.State;
            double alpha = Math.Atan2(st.Velocity.Z, st.Velocity.X) * 57.2958;
            var yb = st.Attitude.Rotate(new Vec3(0, 1, 0));
            double bank = Math.Atan2(yb.Z, st.Attitude.Rotate(new Vec3(0, 0, 1)).Z) * 57.2958;
            double w = st.Rates.Length * 57.2958;
            aSum += alpha; wSum += w; n++;
            if (Math.Abs(bank) > 120) invSec += 0.25;
            wMin = Math.Min(wMin, w);
            if (w < 40 && Math.Abs(alpha) < 20) { stoppedSec += 0.25; if (stoppedAt < 0 && stoppedSec > 1.5) stoppedAt = step * 0.25; }
            else stoppedSec = 0;
        }
        string outcome = stoppedAt > 0 ? $"RECOVERED at {stoppedAt:0.0}s" : $"kept spinning (min |w| {wMin:0}/s)";
        return $"    {what}   {outcome,-30}   {aSum / n,8:0}   {wSum / n,8:0}   {invSec,6:0}s\n";
    }

    /// <summary>Fly the entry and the hold. Returns the live aircraft plus the pro-spin elevator sign.</summary>
    private static (Aircraft, SimLoop, double) Enter(AircraftConfig cfg, bool upright, double tHold, double thrEntry = Full)
    {
        var att = upright ? new Quat(0, 0, 0, 1) : new Quat(1, 0, 0, 0);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), att, new Vec3(60, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        double s = upright ? -1 : 1;      // the pro-spin stick: pull upright, push inverted
        // AT IDLE, like InvSpinRollOutProbe's "push" entry: it is the power-on HOLD that diverges, and entering
        // under power never reaches the break at all.
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
            sim.RunFor(0.1, new ControlInputs(0, s * Math.Min(1, 0.2 + 0.02 * i), 0, Idle));
        for (double t = 0; t < tHold; t += 0.25) { sim.RunFor(0.25, new ControlInputs(0, s, 1, thrEntry)); Wrap(ac); }
        return (ac, sim, s);
    }

    /// <summary>The pitch-moment budget down the departure, by the part of the aeroplane that makes it.</summary>
    private string Budget(AircraftConfig cfg, double ail, bool upright)
    {
        var sb = new System.Text.StringBuilder();
        var att = upright ? new Quat(0, 0, 0, 1) : new Quat(1, 0, 0, 0);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), att, new Vec3(60, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        double s = upright ? -1 : 1;
        double thr = Env("INVHOLD_PWR", Full);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
            sim.RunFor(0.1, new ControlInputs(0, s * Math.Min(1, 0.2 + 0.02 * i), 0, Idle));

        sb.Append($"=== {cfg.Id}  {(upright ? "UPRIGHT" : "INVERTED")} spin, right rudder, ail {ail:0.00}, "
                  + $"throttle lever {thr:0.0} ({(thr < 0 ? "FULL" : "IDLE")})   Iyy={cfg.Mass.Inertia.Iyy} Izz={cfg.Mass.Inertia.Izz}\n");
        sb.Append("  PITCH budget (N.m, + nose up).  Mcpl is the rigid-body inertia coupling as an equivalent moment.\n");
        sb.Append("   t  alpha  beta    p     q     r | Mwing  MhStab   Mfus | Mgyro Mpropn Mprest |  Msum  Mcpl | Ngyro  Nfin  Nsum\n");
        ac.CaptureForces = true;
        var cg = cfg.Mass.CgVec();
        for (int step = 0; step < 100; step++)
        {
            sim.RunFor(0.25, new ControlInputs(ail, s, 1, thr));
            Wrap(ac);
            var st = ac.State;
            double alpha = Math.Atan2(st.Velocity.Z, st.Velocity.X) * 57.2958;
            double beta = Math.Asin(Math.Clamp(st.Velocity.Y / Math.Max(st.Velocity.Length, 1e-6), -1, 1)) * 57.2958;
            double mWing = 0, mStab = 0, mFus = 0, mGyro = 0, mPropN = 0, mThrust = 0, mAll = 0;
            double nGyro = 0, nFin = 0, nAll = 0;
            foreach (var f in ac.LastForces)
            {
                Vec3 r = f.PosBody - cg;
                Vec3 mom = Vec3.Cross(r, f.ForceBody) + f.MomentBody;
                if (f.Kind == "flow" || f.Kind == "tailflow" || f.Kind == "tailq") continue;
                if (f.Kind == "gyro") { mGyro += mom.Y; nGyro += mom.Z; continue; }     // breakdown only (inside "thrust")
                if (f.Kind == "propn") { mPropN += mom.Y; continue; }                   // breakdown only (inside "thrust")
                mAll += mom.Y; nAll += mom.Z;
                if (f.Kind == "thrust") { mThrust += mom.Y; continue; }
                if (f.Kind == "fuselage" || f.Kind == "spoiler") { mFus += mom.Y; continue; }
                bool vertical = Math.Abs(f.PosBody.Y) < 0.05 && f.PosBody.X < -1.0;
                if (vertical) { nFin += mom.Z; continue; }
                if (f.PosBody.X < -1.0) mStab += mom.Y; else mWing += mom.Y;
            }
            // The prop's own moment less the two breakdowns it already contains.
            double mRest = mThrust - mGyro - mPropN;
            double p = st.Rates.X, q = st.Rates.Y, rr = st.Rates.Z;
            double ixx = cfg.Mass.Inertia.Ixx, iyy = cfg.Mass.Inertia.Iyy, izz = cfg.Mass.Inertia.Izz, ixz = cfg.Mass.Inertia.Ixz;
            double mcpl = (izz - ixx) * p * rr + ixz * (rr * rr - p * p);
            if (step % 2 == 0 || step < 24)
                sb.Append($"{step * 0.25,5:0.0} {alpha,6:0} {beta,5:0} {p * 57.3,5:0} {q * 57.3,5:0} {rr * 57.3,5:0} |"
                    + $" {mWing,6:0} {mStab,7:0} {mFus,6:0} | {mGyro,5:0} {mPropN,6:0} {mRest,6:0} | {mAll,5:0} {mcpl,5:0}"
                    + $" | {nGyro,5:0} {nFin,5:0} {nAll,5:0}\n");
        }
        return sb.ToString();
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 800)
            ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 2200),
                                          ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
