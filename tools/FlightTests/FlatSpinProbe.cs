using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (FLATSPIN_PROBE=1, FLATSPIN_REPO = repo root): the Extra's UPRIGHT power-on flat spin (owner 2026-10-13:
/// left rudder, full power, full RIGHT aileron; full aft, then full forward) — the stable pitch, α (and each wing's α at 85 %
/// semispan), turn time, KIAS, and the PITCH-MOMENT balance about the CG that holds that attitude, split by source: wing
/// strips, tail strips, fuselage, pure moments (strip Cm, damping), thrust, and the inertial coupling −(ω × Iω)_y.</summary>
public class FlatSpinProbe
{
    private readonly ITestOutputHelper _out;
    public FlatSpinProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbeFlatSpin()
    {
        if (System.Environment.GetEnvironmentVariable("FLATSPIN_PROBE") == null) return;
        string repo = System.Environment.GetEnvironmentVariable("FLATSPIN_REPO") ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", (System.Environment.GetEnvironmentVariable("FLATSPIN_ID") ?? "extra-300-like") + ".json"));
        double v = 60;
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(v, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        // Idle entry: slow down pulling, then full aft + full left rudder; then full power + full right aileron.
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, -1, -1, 1.0));
        double halfSpan = 0.5 * AeroModel.WingSpan(cfg), tailX = cfg.Mass.Cg[0] - 1.5;
        foreach (var (name, ele) in new[] { ("full AFT", -1.0), ("full FORWARD", 1.0) })
        {
            double ail = double.Parse(System.Environment.GetEnvironmentVariable("FLATSPIN_AIL") ?? "1.0", System.Globalization.CultureInfo.InvariantCulture);
            var ci = new ControlInputs(ail, ele, -1, -1.0);
            for (int i = 0; i < 500; i++)
            {
                // FLATSPIN_IP_AFTER: change the prop inertia once the spin has formed (does the flat equilibrium survive?).
                if (i == 100 && name == "full AFT" && System.Environment.GetEnvironmentVariable("FLATSPIN_IP_AFTER") is string ipa) cfg.Propulsion!.PropInertia = double.Parse(ipa, System.Globalization.CultureInfo.InvariantCulture);
                sim.RunFor(0.1, ci); Wrap(ac);
            }
            int n = 0; double pitch = 0, a = 0, aL = 0, aR = 0, w = 0, kt = 0;
            double mWing = 0, mTail = 0, mFus = 0, mPure = 0, mThr = 0, mIn = 0, mOther = 0;
            ac.CaptureForces = true;
            var I = ac.MassProperties; var cg = cfg.Mass.CgVec();
            for (int i = 0; i < 100; i++)
            {
                sim.RunFor(0.1, ci); Wrap(ac);
                var s = ac.State; var vb = s.Velocity; var om = s.Rates;
                var x = s.Attitude.Rotate(new Vec3(1, 0, 0));
                pitch += System.Math.Asin(-x.Z) * 57.3; a += System.Math.Atan2(vb.Z, vb.X) * 57.3;
                foreach (int side in new[] { -1, 1 })
                {
                    var r = new Vec3(0, side * 0.85 * halfSpan, 0);
                    var vl = vb + Vec3.Cross(om, r);
                    double al = System.Math.Atan2(vl.Z, vl.X) * 57.3;
                    if (side < 0) aL += al; else aR += al;
                }
                w += om.Length * 57.3; kt += vb.Length * 1.943844; n++;
                foreach (var f in ac.LastForces)
                {
                    double my = Vec3.Cross(f.PosBody - cg, f.ForceBody).Y + f.MomentBody.Y;
                    switch (f.Kind)
                    {
                        case "lift": case "drag": if (f.PosBody.X < tailX) mTail += my; else mWing += my; break;
                        case "fuselage": mFus += my; break;
                        case "moment": mPure += my; break;
                        case "thrust": mThr += my; break;
                        case "weight": break;
                        default: mOther += my; break;
                    }
                }
                double hx = I.Ixx * om.X - I.Ixz * om.Z, hz = I.Izz * om.Z - I.Ixz * om.X;
                mIn += -(om.Z * hx - om.X * hz);
            }
            ac.CaptureForces = false;
            double k = 0.7376 / n;   // N·m → ft·lb, mean
            _out.WriteLine($"{cfg.Id} UPRIGHT flat, left rudder, full power, aileron {ci.Aileron:0.0}, {name}: pitch {pitch / n:0.0}  α {a / n:0.0}  α85 L {aL / n:0} R {aR / n:0}  {360 / (w / n):0.00} s/turn  {kt / n:0} kt TAS"
                + $"\n   pitch moments (ft·lb, + nose-up): wing {mWing * k:+0;-0}  tail {mTail * k:+0;-0}  fuselage {mFus * k:+0;-0}  pure(Cm/damping) {mPure * k:+0;-0}  thrust {mThr * k:+0;-0}  other {mOther * k:+0;-0}  | inertial {mIn * k:+0;-0}");
        }
    }

    /// <summary>FLATENTRY_PROBE=1: real flat-spin ENTRY techniques (2026-10-13) from a developed idle upright left spin (~2 turns),
    /// each a timed input sequence; then forward stick held 40 s, then aft held 40 s. Reports the last 30 s of each hold: pitch
    /// mean (min…max), α, per-wing α at 85 % semispan, s/turn, KIAS (TAS × 0.885 at this probe's 2500 m).</summary>
    [Fact]
    public void ProbeFlatEntries()
    {
        if (System.Environment.GetEnvironmentVariable("FLATENTRY_PROBE") == null) return;
        string repo = System.Environment.GetEnvironmentVariable("FLATSPIN_REPO") ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        // Sequence time t (s) → inputs (aileron +right, elevator +push, rudder −left, throttle lever +1 idle … −1 full).
        var techniques = new (string name, System.Func<double, ControlInputs> seq, double len)[]
        {
            ("1 power over 2 s, out-spin aileron at 3 s, forward at 5 s", t => new ControlInputs(t < 3 ? 0 : 1, t < 5 ? -1 : 1, -1, t < 2 ? 1 - t : -1), 6),
            ("2 out-spin aileron, power over 2 s from 1 s, forward at 5 s", t => new ControlInputs(1, t < 5 ? -1 : 1, -1, t < 1 ? 1 : t < 3 ? 1 - (t - 1) : -1), 6),
            ("3 power + aileron together, aft held 10 s, ease forward over 3 s", t => new ControlInputs(1, t < 10 ? -1 : System.Math.Min(1, -1 + 2 * (t - 10) / 3), -1, -1), 13),
            ("4 aileron first, power at 4 s, forward at 8 s", t => new ControlInputs(1, t < 8 ? -1 : 1, -1, t < 4 ? 1 : -1), 9),
            ("5 power first, aileron at 4 s, forward at 6 s", t => new ControlInputs(t < 4 ? 0 : 1, t < 6 ? -1 : 1, -1, -1), 7),
            ("6 power + aileron + forward together", t => new ControlInputs(1, 1, -1, -1), 1),
            ("7 aileron + forward at idle, power at 3 s", t => new ControlInputs(1, 1, -1, t < 3 ? 1 : -1), 4),
        };
        foreach (var (name, seq, len) in techniques)
        {
            var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", "extra-300-like.json"));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
            var sim = new SimLoop(ac);
            for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
            for (int i = 0; i < 150; i++) { sim.RunFor(0.1, new ControlInputs(0, -1, -1, 1.0)); Wrap(ac); }   // the idle spin, ~2 turns
            for (double t = 0; t < len; t += 0.05) { sim.RunFor(0.05, seq(t)); Wrap(ac); }
            var line = new System.Text.StringBuilder($"{name}:");
            double halfSpan = 0.5 * AeroModel.WingSpan(cfg);
            foreach (var (hold, ele) in new[] { ("FORWARD", 1.0), ("AFT", -1.0) })
            {
                var ci = new ControlInputs(1, ele, -1, -1);
                for (int i = 0; i < 100; i++) { sim.RunFor(0.1, ci); Wrap(ac); }
                int n = 0; double p = 0, pmin = 999, pmax = -999, a = 0, aL = 0, aR = 0, w = 0, kt = 0;
                for (int i = 0; i < 300; i++)
                {
                    sim.RunFor(0.1, ci); Wrap(ac);
                    var st = ac.State; var vb = st.Velocity; var x = st.Attitude.Rotate(new Vec3(1, 0, 0));
                    double pp = System.Math.Asin(-x.Z) * 57.3; p += pp; pmin = System.Math.Min(pmin, pp); pmax = System.Math.Max(pmax, pp);
                    a += System.Math.Atan2(vb.Z, vb.X) * 57.3;
                    var vl = vb + Vec3.Cross(st.Rates, new Vec3(0, -0.85 * halfSpan, 0)); aL += System.Math.Atan2(vl.Z, vl.X) * 57.3;
                    var vr = vb + Vec3.Cross(st.Rates, new Vec3(0, 0.85 * halfSpan, 0)); aR += System.Math.Atan2(vr.Z, vr.X) * 57.3;
                    w += st.Rates.Length * 57.3; kt += vb.Length * 1.943844 * 0.885; n++;
                }
                line.Append($"\n   held {hold} 30 s: pitch {p / n:0} ({pmin:0}…{pmax:0})  α {a / n:0}  α85 L {aL / n:0} R {aR / n:0}  {360 / (w / n):0.00} s/turn  {kt / n:0} KIAS");
            }
            _out.WriteLine(line.ToString());
        }
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
