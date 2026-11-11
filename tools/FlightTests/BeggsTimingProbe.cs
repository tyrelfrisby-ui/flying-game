using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// HOW LONG THE BEGGS-MUELLER RECOVERY TAKES, in TURNS (BEGGS_T=1). Ty 2026-11-10, flying 2026.1109.2122:
/// "Good test it passed the beggs mueler albeit slower than the real airplane". The real aeroplane stops
/// rotating inside a turn or two; <see cref="BeggsMuellerProbe"/> only printed seconds every second, which hid
/// how many turns that was. This one counts the turns and reports when the rotation has actually stopped, and
/// sweeps the two knobs that drive it (BEGGS_FINK / BEGGS_STABK, comma-separated) so the cost of a faster
/// recovery in flat-spin attitude is visible in the same table.
/// </summary>
public class BeggsTimingProbe
{
    private readonly ITestOutputHelper _o;
    public BeggsTimingProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("BEGGS_T") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        // BEGGS_TYPE names a type other than the Pitts. Added 2026-11-10 (lane/claude-stearman-elev) because Ty,
        // flying the plate tails, reported the SAME upright flat spin on a SECOND type: "The Great Lakes has
        // similar overly flat and overly difficult upright flat spin recovery. Inverted is fine". Two types with
        // the same tail treatment failing the same way is the evidence that moves the question off any one
        // aeroplane's numbers, so this probe had to be able to fly the second one. Note the entry below is TY'S
        // OWN PITTS ENTRY and it is not retuned per type - what another type does with that stick sequence is
        // itself a measurement, and it is reported as flown rather than adjusted until it looks like a spin.
        string type = Environment.GetEnvironmentVariable("BEGGS_TYPE") ?? "pitts-s2b-like";
        string path = Path.Combine(d.FullName, "configs", "aircraft", type + ".json");
        _o.WriteLine($"type {type}");

        var finKs = Nums("BEGGS_FINK", new[] { -1.0 });
        var stabKs = Nums("BEGGS_STABK", new[] { -1.0 });
        bool verbose = Environment.GetEnvironmentVariable("BEGGS_V") != null;
        // ATTRIBUTION SWITCHES, Ty 2026-11-10: "I still think the idle rpm gyroscopic effect is too high somehow
        // because the nose simple doesn't drop with power reduction". NOGYRO takes the propeller's polar moment
        // out for the recovery only (the gyro term is -r*h, so h=0 removes it); STICK flies the recovery with the
        // stick HELD at neutral instead of free, which is what the hands-off elevator would do if it were not
        // pegged against its aft stop. Running both separates his hypothesis from the free-control peg.
        bool noGyro = Environment.GetEnvironmentVariable("BEGGS_NOGYRO") != null;
        // BEGGS_STICK names WHICH free surface is instead held at neutral, so the two can be separated:
        // "ele", "ail", or "both". Ty 2026-11-10: "It is the ailerons that are delaying the recovery".
        string hold = Environment.GetEnvironmentVariable("BEGGS_STICK") ?? "";

        _o.WriteLine("  finK stabK |  flat spin  | turn |     rotation stopped     | out");
        _o.WriteLine("             | alpha  r    |  s   | r<2 turns  r<1 turns  s  | alpha");
        foreach (double fk in finKs)
            foreach (double sk in stabKs)
            {
                var cfg = AircraftConfigLoader.LoadFromFile(path);
                cfg.SpinAero ??= new SpinAeroConfig();
                if (fk >= 0) cfg.SpinAero.FinSidewashK = fk;
                if (sk >= 0) cfg.SpinAero.StabHighAlphaK = sk;
                if (Environment.GetEnvironmentVariable("BEGGS_SPOIL") is { } spoilK)
                    cfg.SpinAero.ControlSpoilerK = double.Parse(spoilK, CultureInfo.InvariantCulture);
                Run(cfg, verbose, noGyro, hold);
            }
    }

    private void Run(AircraftConfig cfg, bool verbose, bool noGyro = false, string hold = "")
    {
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        const double IDLE = 1.0, FULL = -1.0;
        // Ty's own entry (same as BeggsMuellerProbe): stall, two turns of normal spin left, then power and right aileron.
        for (int i = 0; i < 300 && ac.State.Velocity.Length > 27; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.15 + 0.01 * i), 0, IDLE));
        double turns = 0;
        for (int i = 0; i < 200 && turns < 2.0; i++) { sim.RunFor(0.1, new ControlInputs(0, -1, -1, IDLE)); turns += Math.Abs(ac.State.Rates.Z) * 0.1 / (2 * Math.PI); Wrap(ac); }
        for (int i = 0; i < 300; i++) { sim.RunFor(0.1, new ControlInputs(1.0, -1.0, -1, FULL)); Wrap(ac); }   // power + RIGHT aileron

        double a0 = Alpha(ac), r0 = ac.State.Rates.Z;
        // The entry is flown with the real propeller inertia either way; only the RECOVERY changes.
        if (noGyro && cfg.Propulsion != null) cfg.Propulsion.PropInertia = 0.0;
        double secPerTurn = Math.Abs(r0) > 1e-6 ? 2 * Math.PI / Math.Abs(r0) : double.NaN;

        // BEGGS-MUELLER: power OFF, hands OFF stick and ailerons, full OPPOSITE rudder held by the feet.
        double t = 0, rev = 0, t2 = double.NaN, n2 = double.NaN, t1 = double.NaN, n1 = double.NaN;
        for (int i = 0; i < 300; i++)
        {
            bool eleFree = hold != "ele" && hold != "both", ailFree = hold != "ail" && hold != "both";
            sim.RunFor(0.1, new ControlInputs(0, 0, 1, IDLE, eleFree, ailFree, false));
            t += 0.1;
            rev += Math.Abs(ac.State.Rates.Z) * 0.1 / (2 * Math.PI);
            Wrap(ac);
            double r = Math.Abs(ac.State.Rates.Z);
            if (double.IsNaN(t2) && r < 2.0) { t2 = t; n2 = rev; }
            if (double.IsNaN(t1) && r < 1.0) { t1 = t; n1 = rev; }
            if (verbose && i % 5 == 4)
            {
                var cd = ac.CurrentDeflections;
                double qInf = 0.5 * 1.225 * ac.State.Velocity.LengthSquared;
                var he = ac.HingeDebug("elevator", cfg.Controls.Elevator);
                _o.WriteLine($"        {t,5:F1} s | a {Alpha(ac),6:F1} | r {ac.State.Rates.Z,6:F2} | turns {rev,5:F2} | TAS {ac.State.Velocity.Length,5:F1}"
                    + $" | ele {cd.ElevatorRad * D,6:F1} ail {cd.AileronRad * D,6:F1} deg | hinge a {he.A,8:F1} b {he.B,8:F1} k {he.K,6:F1} den {he.Den,8:F1} | qinf {qInf,5:F0} | {Strips(ac, "elevator")}");
            }
            if (!double.IsNaN(t1) && i > 20) break;
        }
        string f2 = double.IsNaN(n2) ? "   --  " : $"{n2,6:F2} ";
        string f1 = double.IsNaN(n1) ? "   --  " : $"{n1,6:F2} ";
        string s1 = double.IsNaN(t1) ? " -- " : $"{t1,4:F1}";
        string tag = (noGyro ? " noGyro" : "") + (hold.Length > 0 ? " hold:" + hold : "");
        _o.WriteLine($"  {cfg.SpinAero!.FinSidewashK,4:F2} {cfg.SpinAero.StabHighAlphaK,4:F2} | {a0,6:F1} {r0,5:F2} | {secPerTurn,4:F2} | {f2}    {f1}   {s1} | {Alpha(ac),6:F1}{tag}");
    }

    /// <summary>Per-strip hinge flow for one surface: local flow angle (deg) / local q / chordwise flow direction.</summary>
    private static string Strips(Aircraft ac, string surface)
    {
        var fs = typeof(Aircraft).GetField("_flowState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ac)!;
        double[] al = (double[])fs.GetType().GetField("HingeAlphaRad")!.GetValue(fs)!;
        double[] q = (double[])fs.GetType().GetField("HingeQ")!.GetValue(fs)!;
        double[] dir = (double[])fs.GetType().GetField("HingeFlowDir")!.GetValue(fs)!;
        var sb = new System.Text.StringBuilder(surface.Substring(0, 3) + " ");
        int idx = 0;
        foreach (var sf in ac.Config.Surfaces)
            foreach (var st in sf.Strips)
            {
                int i = idx++;
                if (st.Control is null || st.Control.Surface != surface || Math.Abs(st.Control.Gain) < 0.9) continue;
                sb.Append($"{al[i] * D,5:F0}/{q[i],4:F0}/{dir[i],5:F2}");
            }
        return sb.ToString();
    }

    private static double Alpha(Aircraft ac) => Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * D;

    private static double[] Nums(string env, double[] fallback)
    {
        string? s = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var list = new List<double>();
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries)) list.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
