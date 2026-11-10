using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHY DOES OUR INVERTED FLAT SPIN BANK? (INVBANK=1)
///
/// Ty 2026-11-08: "Inverted flat S2B not more than 30 degrees bank probably more like 10". The 2026-11-03 diagnosis
/// found the propeller's gyroscopic yaw ending the spin and concluded the physics holding a real one flat was
/// missing. With his number that reads the other way round: h·q only exists once there is a body PITCH rate, and in a
/// flat spin only BANK converts the yaw rate into one. The gyro is doing its job on a q the real aeroplane never
/// develops — so the bank is the defect, and this probe asks what is producing it.
///
/// It prints, once the spin is developed, the bank angle and the ROLLING MOMENT broken down by where it comes from:
/// each force sample's r x F about the CG plus its own pure moment, grouped by what raised it. Whatever is banking
/// the aeroplane has to show up as the large term here.
/// </summary>
public class PittsInvertedBankProbe
{
    private readonly ITestOutputHelper _out;
    public PittsInvertedBankProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbeInvertedFlatBank()
    {
        if (System.Environment.GetEnvironmentVariable("INVBANK") == null) return;
        string id = System.Environment.GetEnvironmentVariable("INVBANK_ID") ?? "pitts-s2b-like";
        string repo = System.Environment.GetEnvironmentVariable("INVBANK_REPO")
            ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", id + ".json"));
        // INVBANK_NOCAMBER=1 switches the two fuselage cross-section camber terms off, to see how much of the
        // inverted spin's pitch balance they are setting (Ty 2026-11-08: "There must be a sign error in there").
        bool noCamber = System.Environment.GetEnvironmentVariable("INVBANK_NOCAMBER") == "1";
        if (noCamber) { cfg.Fuselage.Crossflow.CowlCamberCl = 0; cfg.Fuselage.Crossflow.SectionCamberCl = 0; }

        // INVBANK_UPRIGHT=1 flies the upright comparison instead: pull, left rudder, right aileron, full power —
        // the out-spin-aileron case Ty asked to compare against (2026-11-08).
        bool upright = System.Environment.GetEnvironmentVariable("INVBANK_UPRIGHT") != null;
        double ail = double.TryParse(System.Environment.GetEnvironmentVariable("INVBANK_AIL"), out double a0) ? a0 : 0.0;
        double rud = double.TryParse(System.Environment.GetEnvironmentVariable("INVBANK_RUD"), out double r0) ? r0 : (upright ? -1.0 : 1.0);
        double stick = upright ? -1.0 : 1.0;                 // pull upright, push inverted
        var att0 = upright ? new Quat(0, 0, 0, 1) : new Quat(1, 0, 0, 0);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), att0, new Vec3(60, 0, 0), Vec3.Zero),
            new ControlDeflections(0, 0, 0, 0, 0));
        ac.CaptureForces = true;
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
            sim.RunFor(0.1, new ControlInputs(0, stick * System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(8, new ControlInputs(ail, stick, rud, -1.0));   // full power (lever -1)

        _out.WriteLine($"{id}  {(upright ? "UPRIGHT" : "INVERTED")}, full power, rudder {rud:+0.0;-0.0}, aileron {ail:+0.0;-0.0}"
            + $"   camber cowl {cfg.Fuselage.Crossflow.CowlCamberCl:0.00} section {cfg.Fuselage.Crossflow.SectionCamberCl:0.00}");
        _out.WriteLine($"{"t",5} {"bank",6} {"pitch",6} {"alpha",6} {"beta",6} {"p",6} {"q",6} {"r",6}   {(Yaw ? "YAWING" : Pitch ? "PITCHING" : "rolling")} moment by source (N·m)");
        var totals = new Dictionary<string, double>();
        var armTotals = new Dictionary<string, double>();
        double etaSum = 0, qwSum = 0; int etaN = 0;
        for (int step = 0; step < 40; step++)
        {
            sim.RunFor(0.5, new ControlInputs(ail, stick, rud, -1.0));
            if (-ac.State.Position.Z < 800) ac.State = new RigidBodyState(
                new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 2200), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);

            RigidBodyState st = ac.State;
            Vec3 right = st.Attitude.Rotate(new Vec3(0, 1, 0));      // the right wing, in the world
            Vec3 belly = st.Attitude.Rotate(new Vec3(0, 0, 1));      // body +Z: toward the belly. Up in the world = inverted.
            string att = belly.Z < 0 ? "INV" : "upr";
            Vec3 nose = st.Attitude.Rotate(new Vec3(1, 0, 0));
            double bank = System.Math.Asin(System.Math.Clamp(right.Z, -1, 1)) * 57.2958;   // NED: +Z down
            double pitch = System.Math.Asin(System.Math.Clamp(-nose.Z, -1, 1)) * 57.2958;
            Vec3 vb = st.Velocity;
            double alpha = System.Math.Atan2(vb.Z, vb.X) * 57.2958, beta = System.Math.Asin(vb.Y / System.Math.Max(1e-6, vb.Length)) * 57.2958;

            var (sepL, sepR, aL, aR, nL, nR) = Wings(ac);
            var roll = Breakdown(ac);
            if (step >= 20) foreach (var kv in roll) totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
            _out.WriteLine($"{step * 0.5 + 8,5:0.0} {att} {bank,6:0.0} {pitch,6:0.0} {alpha,6:0.0} {beta,6:0.0} " +
                           $"{st.Rates.X * 57.3,6:0.0} {st.Rates.Y * 57.3,6:0.0} {st.Rates.Z * 57.3,6:0.0}   " +
                           $"L sep {sepL:0.00} a {aL,5:0.0}  R sep {sepR:0.00} a {aR,5:0.0}   " +
                           string.Join("  ", Top(roll))
                           + (Arms ? "   | " + string.Join("  ", Top(BySurface(ac)))
                                     + $"   eta_t {TailFlow(ac).eta:0.00} qwake {TailFlow(ac).qw:0.00} Vt/V {TailFlow(ac).vRatio:0.00} a_t {TailFlow(ac).alphaDeg,5:0.0}" : ""));
            if (Arms && step >= 20) foreach (var kv in BySurface(ac)) armTotals[kv.Key] = armTotals.GetValueOrDefault(kv.Key) + kv.Value;
            if (Arms && step >= 20) { etaSum += TailFlow(ac).eta; qwSum += TailFlow(ac).qw; etaN++; }
        }
        _out.WriteLine($"\nMEAN {(Yaw ? "YAWING (+ = body nose-RIGHT)" : Pitch ? "PITCHING (+ = body nose-UP)" : "ROLLING (+ = right wing down)")} moment over the developed spin (N\u00b7m):");
        foreach (var kv in Sorted(totals)) _out.WriteLine($"   {kv.Key,-10} {kv.Value / 20.0,10:0.0}");
        if (Arms)
        {
            _out.WriteLine("\nsame moment, by SURFACE (N\u00b7m):");
            foreach (var kv in Sorted(armTotals)) _out.WriteLine($"   {kv.Key,-10} {kv.Value / 20.0,10:0.0}");
            _out.WriteLine($"   mean eta_t (q_local/q_inf at the stabiliser) {(etaN > 0 ? etaSum / etaN : double.NaN):0.000}"
                + $"   = wake {(etaN > 0 ? qwSum / etaN : double.NaN):0.000} x local speed-up");
        }
    }

    /// <summary>
    /// How stalled each wing is, and at what local angle. Ty 2026-11-08: "One wing is definitely not stalled inverted
    /// spins pitts" — in a real inverted Pitts spin one wing is still flying, and if ours has both of them separated
    /// there is no autorotative couple worth the name, only two large opposing terms that cancel.
    ///
    /// Separation is per strip, 0 attached .. 1 fully separated (StripFlowState, the two-branch stall memory).
    /// </summary>
    private static (double sepL, double sepR, double aL, double aR, int nL, int nR) Wings(Aircraft ac)
    {
        var fs = ac.FlowStateForTests;
        double sl = 0, sr = 0, al = 0, ar = 0; int nl = 0, nr = 0, i = 0;
        foreach (SurfaceConfig sf in ac.Config.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                int idx = i++;
                if (!sf.Id.Contains("wing", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (idx >= fs.Separation.Length) continue;
                double y = st.PosVec().Y, sep = fs.Separation[idx], a = fs.LocalAlphaRad[idx] * 57.2958;
                if (System.Math.Abs(y) < 0.3) continue;              // centre sections belong to neither
                if (y > 0) { sr += sep; ar += a; nr++; } else { sl += sep; al += a; nl++; }
            }
        return (nl > 0 ? sl / nl : 0, nr > 0 ? sr / nr : 0, nl > 0 ? al / nl : 0, nr > 0 ? ar / nr : 0, nl, nr);
    }

    /// <summary>Rolling moment about the CG from each source: r x F plus the sample's own pure moment.</summary>
    /// <summary>PITCH=1 breaks the PITCHING moment down instead of the rolling one (− = nose down in body axes).</summary>
    private static readonly bool Pitch = System.Environment.GetEnvironmentVariable("INVBANK_PITCH") != null;

    /// <summary>INVBANK_YAW=1 breaks the YAWING moment down instead (+ = nose right in body axes). The spin RATE is what
    /// sets the gyroscopic flattening moment (My = -r*h, linear in r), so an inverted spin that turns at half the upright
    /// rate has half the term holding it flat — which makes the yaw balance, not the pitch balance, the thing to explain.</summary>
    private static readonly bool Yaw = System.Environment.GetEnvironmentVariable("INVBANK_YAW") != null;

    private static double Axis(Vec3 v) => Yaw ? v.Z : Pitch ? v.Y : v.X;

    private static Dictionary<string, double> Breakdown(Aircraft ac)
    {
        var by = new Dictionary<string, double>();
        Vec3 cg = ac.Config.Mass.CgVec();
        foreach (ForceSample f in ac.LastForces)
        {
            if (f.Kind == "flow" || f.Kind == "tailflow") continue;
            Vec3 r = f.PosBody - cg;
            double l = Axis(Vec3.Cross(r, f.ForceBody)) + Axis(f.MomentBody);
            if (System.Math.Abs(l) < 1e-9) continue;
            by[f.Kind] = by.GetValueOrDefault(f.Kind) + l;   // "gyro" is a breakdown of thrust's moment: shown, not summed
        }
        return by;
    }

    private static IEnumerable<string> Top(Dictionary<string, double> d)
    {
        foreach (var kv in Sorted(d)) yield return $"{kv.Key} {kv.Value:0}";
    }

    private static List<KeyValuePair<string, double>> Sorted(Dictionary<string, double> d)
    {
        var l = new List<KeyValuePair<string, double>>(d);
        l.Sort((x, y) => System.Math.Abs(y.Value).CompareTo(System.Math.Abs(x.Value)));
        return l;
    }

    /// <summary>
    /// WHERE the pitching moment is raised, by SURFACE rather than by kind (INVBANK_ARMS=1). The biplane's wings
    /// and its tail stand at very different arms from the CG (upper wing 0.81 m above it, lower wing 0.27 m below,
    /// the stabiliser 3.05 m behind), so one number for "drag" hides which lever is actually steepening the spin.
    /// Buckets are read off the sample's own body position, the same way the overlay groups them.
    /// </summary>
    private static Dictionary<string, double> BySurface(Aircraft ac)
    {
        var by = new Dictionary<string, double>();
        Vec3 cg = ac.Config.Mass.CgVec();
        foreach (ForceSample f in ac.LastForces)
        {
            if (f.Kind == "flow" || f.Kind == "tailflow" || f.Kind == "gyro") continue;
            Vec3 r = f.PosBody - cg;
            double m = Axis(Vec3.Cross(r, f.ForceBody)) + Axis(f.MomentBody);
            if (System.Math.Abs(m) < 1e-9) continue;
            // The fuselage's crossflow stations are raised at (x, 0, 0) all the way aft, so position alone puts an
            // aft fuselage station in the stabiliser's bucket. Strip aero (lift/drag/moment) is bucketed by where the
            // strip is; the fuselage keeps its own buckets fore and aft of the CG; thrust and the rest stay whole.
            string b;
            if (f.Kind == "lift" || f.Kind == "drag" || f.Kind == "moment")
                b = f.PosBody.X < -2.0 ? (System.Math.Abs(f.PosBody.Z) > 0.2 ? "vstab" : f.PosBody.X < -3.3 ? "elev" : "hstab")
                  : f.PosBody.Z < -0.3 ? "wingUP"
                  : f.PosBody.Z > 0.3 ? "wingLO"
                  : "cgMoment";
            else if (f.Kind == "fuselage") b = f.PosBody.X < -1.0 ? "fuseAFT" : "fuseFWD";
            else b = f.Kind;
            by[b] = by.GetValueOrDefault(b) + m;
        }
        return by;
    }

    /// <summary>The stabiliser's own blanketing: mean eta_t = q_local / q_freestream over the hStab/elevator strips,
    /// which AeroModel already records as the Z of each "tailflow" sample. A real spin has a dead tail; if ours reads
    /// near 1.0 the tail is still making full-strength force at an attitude where it should be in the wake.</summary>
    private static (double eta, double alphaDeg, double qw, double vRatio) TailFlow(Aircraft ac)
    {
        // eta_t as the model reports it is qFactor * V_local^2 / V_inf^2 — the WAKE factor and the local speed-up
        // (slipstream, rotation) multiplied together. Split them: V_local comes out of the sample's own flow vector
        // (its X and Z are V_local cos a_t and V_local sin a_t), V_inf from the CG, and the wake factor is the ratio.
        double e = 0, a = 0, vl = 0; int n = 0;
        foreach (ForceSample f in ac.LastForces)
            if (f.Kind == "tailflow")
            {
                e += f.MomentBody.Z; a += f.MomentBody.X * 57.2958;
                vl += System.Math.Sqrt(f.ForceBody.X * f.ForceBody.X + f.ForceBody.Z * f.ForceBody.Z);
                n++;
            }
        if (n == 0) return (double.NaN, double.NaN, double.NaN, double.NaN);
        double vInf = System.Math.Max(1e-6, ac.State.Velocity.Length);
        double ratio = (vl / n) / vInf;
        return (e / n, a / n, ratio > 1e-6 ? (e / n) / (ratio * ratio) : double.NaN, ratio);
    }

    private static readonly bool Arms = System.Environment.GetEnvironmentVariable("INVBANK_ARMS") != null;
}
