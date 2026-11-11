using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (INVROLL=1): fly an INVERTED spin with right rudder and log the trajectory —
/// bank, alpha, beta, p/q/r and the roll/pitch/yaw moment split by group (upper wing L/R, lower wing
/// L/R, tail, fuselage) plus the inertia-coupling term. INVROLL_ID picks the type (default pitts).
/// INVROLL_UPRIGHT=1 flies upright instead. INVROLL_AIL sets the held aileron (default 0).
/// Hunting Ty's 2026-11-10 report: "the inverted spin with right rudder rolls to knife edge then upright".</summary>
public class InvSpinRollOutProbe
{
    private readonly ITestOutputHelper _out;
    public InvSpinRollOutProbe(ITestOutputHelper o) { _out = o; }

    private static string Repo => System.Environment.GetEnvironmentVariable("INVROLL_REPO")
        ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));

    private static double Env(string k, double d)
    {
        string? s = System.Environment.GetEnvironmentVariable(k);
        return s == null ? d : double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ProbeInvertedSpinRollOut()
    {
        if (System.Environment.GetEnvironmentVariable("INVROLL") == null) return;
        string ids = System.Environment.GetEnvironmentVariable("INVROLL_ID") ?? "pitts-s2b-like";
        bool inverted = System.Environment.GetEnvironmentVariable("INVROLL_UPRIGHT") == null;
        string ails = System.Environment.GetEnvironmentVariable("INVROLL_AIL") ?? "0";
        bool verbose = System.Environment.GetEnvironmentVariable("INVROLL_V") != null;
        foreach (string id in ids.Split(','))
        {
            var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(Repo, "configs", "aircraft", id.Trim() + ".json"));
            string kill = System.Environment.GetEnvironmentVariable("INVROLL_KILL") ?? "";
            string fo = System.Environment.GetEnvironmentVariable("INVROLL_FINOFF");
            if (fo != null) { cfg.Rig.FinOffsetRad = double.Parse(fo, System.Globalization.CultureInfo.InvariantCulture); cfg.Id += "[fin=" + fo + "]"; }
            string ip = System.Environment.GetEnvironmentVariable("INVROLL_IPROP");
            if (ip != null) { cfg.Propulsion.PropInertia = double.Parse(ip, System.Globalization.CultureInfo.InvariantCulture); cfg.Id += "[Ip=" + ip + "]"; }
            if (kill.Contains("gyro")) cfg.Propulsion.PropInertia = 0.0;
            if (kill.Contains("slip")) cfg.Propulsion.SlipstreamK = 0.0;
            if (kill.Contains("pfactor")) cfg.Propulsion.PFactorK = 0.0;
            if (kill.Contains("thrustline")) cfg.Propulsion.ThrustLineZ = 0.0;
            if (kill.Length > 0) cfg.Id = cfg.Id + "[-" + kill + "]";
            foreach (string pw in (System.Environment.GetEnvironmentVariable("INVROLL_PWR") ?? "1").Split(','))
            foreach (string en in (System.Environment.GetEnvironmentVariable("INVROLL_ENTRY") ?? "push").Split(','))
            foreach (string a in ails.Split(','))
                _out.WriteLine(Run(cfg, inverted, double.Parse(a.Trim(), System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(pw.Trim(), System.Globalization.CultureInfo.InvariantCulture), en.Trim(), verbose));
        }
    }

    private static string Run(AircraftConfig cfg, bool inverted, double ail, double pwr, string entry, bool verbose)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"=== {cfg.Id}  {(inverted ? "INVERTED" : "UPRIGHT")} spin, right rudder, ail {ail:0.00} thr {pwr:0.00} entry {entry}  "
                  + $"m={cfg.Mass.MassKg} Ixx={cfg.Mass.Inertia.Ixx} Iyy={cfg.Mass.Inertia.Iyy} Izz={cfg.Mass.Inertia.Izz} Ixz={cfg.Mass.Inertia.Ixz}\n");

        var att = inverted ? new Quat(1, 0, 0, 0) : new Quat(0, 0, 0, 1);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), att, new Vec3(60, 0, 0), Vec3.Zero),
            new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        double s = inverted ? 1 : -1;        // the pro-spin stick: push inverted, pull upright
        double rud = 1;                      // RIGHT rudder, both attitudes (Ty's report)

        if (entry == "push")
        {
            // Decelerate at idle holding the attitude, stick easing toward the spin side.
            for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
                sim.RunFor(0.1, new ControlInputs(0, s * System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        }
        else if (entry == "rudderfirst")
        {
            // Rudder in BEFORE the break (how a flick/accelerated entry goes).
            for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
                sim.RunFor(0.1, new ControlInputs(0, s * System.Math.Min(1, 0.2 + 0.02 * i), rud, 1.0));
        }
        else if (entry == "idlethenpower")
        {
            // Establish the spin at IDLE (which holds), then add power at t=15 s. Isolates power from entry.
            for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
                sim.RunFor(0.1, new ControlInputs(0, s * System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        }
        else if (entry == "fromupright")
        {
            // Upright spin first (pull + right rudder), then PUSH through to the inverted spin.
            for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
                sim.RunFor(0.1, new ControlInputs(0, -System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
            sim.RunFor(8, new ControlInputs(0, -1, rud, 1.0));
            sim.RunFor(4, new ControlInputs(0, 1, rud, 1.0));
        }

        sb.Append("   t    bank   pitch   alpha    beta      p      q      r   | Lwing_net  Ltail  Lfus  Lprop |  Lsum  Icpl |  Nsum Nprop  Msum Mprop  kt\n");
        ac.CaptureForces = true;
        double t = 0, rolledOutAt = -1, bankInvSec = 0, bankUprSec = 0, minAbsBank = 999;
        double aSum = 0, bSum = 0, pSum = 0, rSum = 0, wSum = 0; int nAvg = 0;
        double stoppedSec = 0, stoppedAt = -1, wMin = 9999;
        for (int step = 0; step < 240; step++)
        {
            double thrNow = entry == "idlethenpower" ? (t < 15.0 ? 1.0 : pwr) : pwr;
            sim.RunFor(0.25, new ControlInputs(ail, s, rud, thrNow));
            t += 0.25;
            if (-ac.State.Position.Z < 800)
                ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 2200),
                    ac.State.Attitude, ac.State.Velocity, ac.State.Rates);

            var st = ac.State;
            var vb = st.Velocity;
            var xb = st.Attitude.Rotate(new Vec3(1, 0, 0));
            var yb = st.Attitude.Rotate(new Vec3(0, 1, 0));
            double pitch = System.Math.Asin(System.Math.Clamp(-xb.Z, -1, 1)) * 57.2958;
            // Bank: rotation of the body y axis about the velocity-independent horizon.
            double bank = System.Math.Atan2(yb.Z, (st.Attitude.Rotate(new Vec3(0, 0, 1))).Z) * 57.2958;
            double alpha = System.Math.Atan2(vb.Z, vb.X) * 57.2958;
            double beta = System.Math.Asin(System.Math.Clamp(vb.Y / System.Math.Max(vb.Length, 1e-6), -1, 1)) * 57.2958;

            // Moment split, from the captured per-force samples of the last RK4 stage.
            double lUpL = 0, lUpR = 0, lLoL = 0, lLoR = 0, lTail = 0, lFus = 0, lAll = 0, nAll = 0, mAll = 0;
            double lProp = 0, nProp = 0, mProp = 0;
            // Upper/lower wing split by the strip z (upper wing sits at negative z = above the body axis).
            foreach (var f in ac.LastForces)
            {
                Vec3 r = f.PosBody - cfg.Mass.CgVec();
                Vec3 mom = Vec3.Cross(r, f.ForceBody) + f.MomentBody;
                if (f.Kind == "flow" || f.Kind == "gyro") continue;
                lAll += mom.X; mAll += mom.Y; nAll += mom.Z;
                if (f.Kind == "thrust") { lProp += mom.X; mProp += mom.Y; nProp += mom.Z; continue; }
                bool wing = System.Math.Abs(f.PosBody.Y) > 0.3 && f.PosBody.X > -1.5;
                if (f.Kind == "fuselage" || f.Kind == "spoiler") lFus += mom.X;
                else if (wing)
                {
                    bool upper = f.PosBody.Z < -0.3;
                    if (f.PosBody.Y < 0) { if (upper) lUpL += mom.X; else lLoL += mom.X; }
                    else { if (upper) lUpR += mom.X; else lLoR += mom.X; }
                }
                else if (f.PosBody.X < -1.5) lTail += mom.X;
                else lFus += mom.X;
            }

            // The inertia cross-coupling roll term: Gamma*pdot contribution with L = N = 0.
            double p = st.Rates.X, q = st.Rates.Y, rr = st.Rates.Z;
            double ixx = cfg.Mass.Inertia.Ixx, iyy = cfg.Mass.Inertia.Iyy, izz = cfg.Mass.Inertia.Izz, ixz = cfg.Mass.Inertia.Ixz;
            double gamma = ixx * izz - ixz * ixz;
            double icpl = (ixz * (ixx - iyy + izz) * p * q - (izz * (izz - iyy) + ixz * ixz) * q * rr) / gamma * ixx; // as an equivalent roll moment

            // Outcome detection: inverted spin holds |bank|>120 with alpha<0; a ROLL-OUT reaches
            // |bank|<60 with alpha>0 and keeps rotating, or stops rotating altogether.
            if (t > (entry == "idlethenpower" ? 15.5 : 2.0))
            {
                bool upr = System.Math.Abs(bank) < 60 && alpha > 0;
                if (upr && rolledOutAt < 0) rolledOutAt = t;
                if (System.Math.Abs(bank) > 120) { bankInvSec += 0.25; } else { bankUprSec += 0.25; }
                minAbsBank = System.Math.Min(minAbsBank, System.Math.Abs(bank));
                // Attitude-free recovery test: the rotation stops and the wing unstalls.
                double wNow = st.Rates.Length * 57.3;
                if (wNow < 40 && System.Math.Abs(alpha) < 20) { stoppedSec += 0.25; if (stoppedAt < 0 && stoppedSec > 1.5) stoppedAt = t; }
                else stoppedSec = 0;
                wMin = System.Math.Min(wMin, wNow);
                aSum += alpha; bSum += beta; pSum += p * 57.3; rSum += rr * 57.3; wSum += st.Rates.Length * 57.3; nAvg++;
            }
            if (verbose && (step % 2 == 0 || step < 40))
                sb.Append($"{t,5:0.0} {bank,7:0} {pitch,7:0} {alpha,7:0} {beta,7:0} {p * 57.3,7:0} {q * 57.3,7:0} {rr * 57.3,7:0}  | "
                    + $"{lUpL + lUpR + lLoL + lLoR,9:0} {lTail,6:0} {lFus,5:0} {lProp,6:0} | {lAll,5:0} {icpl,5:0} | {nAll,5:0} {nProp,5:0} {mAll,5:0} {mProp,5:0} {vb.Length * 1.9438,4:0}\n");
        }
        sb.Append($"  OUTCOME ail={ail,5:0.00}: {(stoppedAt > 0 ? $"*** ROTATION STOPPED at t={stoppedAt:0.0}s" : "kept rotating")} "
            + $"{(rolledOutAt > 0 ? $"/ upright+rolled at t={rolledOutAt:0.0}s" : "")} min|w| {wMin:0}/s"
            + $"   inverted {bankInvSec:0}s / other {bankUprSec:0}s   min|bank| {minAbsBank:0}deg"
            + $"   mean alpha {aSum / nAvg:0} beta {bSum / nAvg:0} p {pSum / nAvg:0} r {rSum / nAvg:0} |w| {wSum / nAvg:0}/s\n");
        return sb.ToString();
    }
}
