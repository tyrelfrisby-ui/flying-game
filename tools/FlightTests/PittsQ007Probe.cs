using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (Q007=1). Owner, Ty t918u on 2026.1106.0703: "The pitts upright spin is still too flat and difficult recover
/// from the inverted spin (right right is too nose low and too easy to recover". The Pitts' upright LEFT and inverted RIGHT
/// spins at idle / 40 % / full, ailerons neutral and with aileron (out-spin upright, right inverted): the incipient trace
/// (α, θ at 2–45 s), the developed spin (last 20 of 45 s), and recovery in turns from 3 turns and from the developed spin,
/// by PARE (power idle, ailerons neutral, opposite rudder, stick to neutral) and with the power left where it was.
/// Q007_IP overrides the prop inertia (kg·m²).
/// </summary>
public class PittsQ007Probe
{
    private readonly ITestOutputHelper _out;
    public PittsQ007Probe(ITestOutputHelper o) { _out = o; }
    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        var ip = Environment.GetEnvironmentVariable("Q007_IP");
        if (!string.IsNullOrEmpty(ip)) c.Propulsion!.PropInertia = double.Parse(ip);
        var nk = Environment.GetEnvironmentVariable("Q007_K");           // propeller in-plane (normal) force coefficient
        if (!string.IsNullOrEmpty(nk)) c.Propulsion!.NormalForceK = double.Parse(nk);
        // THRUST-LINE OFFSET (Q007_TLZ). The Pitts and the Great Lakes are the only types in the fleet with a CG below
        // the body origin (0.20 and 0.25) AND thrustLineZ left at 0; the Extra, whose CG z IS 0, is consistent. The
        // origin is the thrust line, so their real offset is about -0.20 / -0.25 and it was never entered. Unlike the
        // gyro, it does NOT mirror: a thrust line above the CG is nose-down with power upright, which inverted is
        // nose-UP with respect to the earth -- exactly "upright spins are more nose low and inverted flat spins are
        // more nose high" (Ty, 2026-11-08).
        var tz = Environment.GetEnvironmentVariable("Q007_TLZ");
        if (!string.IsNullOrEmpty(tz)) c.Propulsion!.ThrustLineZ = double.Parse(tz);
        var sk = Environment.GetEnvironmentVariable("Q007_SK");          // geometric helical shadow depth (all surfaces)
        if (!string.IsNullOrEmpty(sk)) { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.ShadowK = double.Parse(sk); }
        var wk = Environment.GetEnvironmentVariable("Q007_W");           // biplane wing-on-wing wake depth
        if (!string.IsNullOrEmpty(wk)) { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.CelluleWakeK = double.Parse(wk); }
        return c;
    }

    /// <summary>
    /// WING TILT FROM HORIZONTAL (2026-11-08). Ty's spec for the inverted flat is written in BANK — "Inverted flat S2B
    /// not more than 30 degrees bank probably more like 10" — and nothing here measured it. Euler roll is useless for
    /// the comparison, because wings-level inverted is +/-180 deg of it; this is the angle of the span axis above the
    /// horizon, so 0 means wings level whichever way up the aeroplane is, and the same number reads the same way for
    /// the upright and the inverted spin.
    /// </summary>
    private static double WingTilt(RigidBodyState s)
    {
        Vec3 right = s.Attitude.Rotate(new Vec3(0, 1, 0));     // NED: +Z down, so this is the right wing's depression
        return Math.Asin(Math.Clamp(right.Z, -1, 1)) * D;
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

    /// <summary>Turns (and seconds) from the recovery input until |Ω| &lt; 20 deg/s or the wing unstalls (|α| &lt; 12°).</summary>
    private static (double turns, double sec) Recover(Aircraft ac, double rudder, bool inverted, double lever)
    {
        double h = SimLoop.DefaultFixedDtSec, net = 0, stick = inverted ? 1 : -1;
        for (double t = 0; t < 30; t += h)
        {
            ac.Step(new ControlInputs(0, t < 0.5 ? stick : 0, -rudder, lever), h);
            var s = ac.State;
            if (-s.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
            double om = OmegaVert(ac.State);
            net += Math.Abs(om) * h;
            if (t > 0.3 && (Math.Abs(om) * D < 20 || Math.Abs(Alpha(ac.State)) < 12)) return (net / (2 * Math.PI), t);
        }
        return (double.NaN, double.NaN);
    }

    /// <summary>SpinBalanceProbe.EnterSpin's entry (stalled, then pro-spin controls), stopped after <paramref name="stopTurns"/>
    /// turns of vertical rotation or <paramref name="maxS"/> s.</summary>
    private static Aircraft Spin(double rud, double lever, bool inv, double ail, double stopTurns, double maxS, Action<double, Aircraft>? each = null)
    {
        double h = SimLoop.DefaultFixedDtSec, turns = 0, t = 0;
        Aircraft ac = null!;
        bool stop = false;
        try
        {
            ac = SpinBalanceProbe.EnterSpin(Load(), rud, lever, maxS, (tt, a) =>
            {
                if (stop) throw new OperationCanceledException();
                turns += Math.Abs(OmegaVert(a.State)) * h / (2 * Math.PI); t = tt; ac = a;
                each?.Invoke(tt, a);
                if (turns >= stopTurns) stop = true;
            }, inv, ail);
        }
        catch (OperationCanceledException) { }
        return ac;
    }

    [Fact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("Q007") == null) return;
        var cases = new List<(string name, double rud, bool inv, double ail)>
        {
            ("up L neutral", -1, false, 0), ("up L out-ail", -1, false, 1),
            ("inv R neutral", 1, true, 0), ("inv R R-ail", 1, true, 1),
        };
        string only = Environment.GetEnvironmentVariable("Q007_ONLY") ?? "";
        // Q007_AIL=0.25,0.5,... sweeps the pro-spin aileron on the "-ail" cases instead of flying them at full travel.
        // The flat inverted spin needs stick toward the spin axis (Christen Eagle II manual §5) and the neutral case is a
        // normal inverted spin, so HOW MUCH is the practical question between the two.
        var ailSweep = (Environment.GetEnvironmentVariable("Q007_AIL") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                                            .Select(double.Parse).ToList();
        if (ailSweep.Count > 0)
            cases = cases.Where(c => c.ail != 0).SelectMany(c => ailSweep.Select(a => (c.name + $" {a:0.00}", c.rud, c.inv, a))).ToList();
        foreach (var (lever, pw) in new[] { (1.0, "idle"), (0.2, "40%"), (-1.0, "full") })
        foreach (var (name, rud, inv, ail) in cases)
        {
            if (only != "" && !only.Split(',').Any(o => (pw + " " + name).Contains(o))) continue;
            var trace = new System.Text.StringBuilder();
            double sa = 0, sp = 0, sw = 0, amin = 999, amax = -999, h = SimLoop.DefaultFixedDtSec; int n = 0;
            double sb = 0, bmax = 0;
            var ac = Spin(rud, lever, inv, ail, 1e9, 45, (t, a) =>
            {
                var s = a.State;
                foreach (double mark in new[] { 2.0, 4, 6, 10, 15, 20, 30 })
                    if (Math.Abs(t - mark) < h / 2) trace.Append($" {mark:F0}s:{Alpha(s):F0}/{FormationPilot.Euler(s.Attitude).Item2 * D:F0}");
                if (t >= 25)
                {
                    double al = Alpha(s); sa += al; amin = Math.Min(amin, al); amax = Math.Max(amax, al);
                    sp += FormationPilot.Euler(s.Attitude).Item2 * D; sw += Math.Abs(OmegaVert(s)) * D; n++;
                    double bk = Math.Abs(WingTilt(s)); sb += bk; bmax = Math.Max(bmax, bk);
                }
            });
            var devPare = Recover(ac, rud, inv, 1.0);
            var devHeld = Recover(Spin(rud, lever, inv, ail, 1e9, 45), rud, inv, lever);
            double t3 = 0;
            var a3 = Spin(rud, lever, inv, ail, 3, 45, (t, a) => t3 = t);
            double a3a = Alpha(a3.State), a3p = FormationPilot.Euler(a3.State.Attitude).Item2 * D;
            var p3 = Recover(a3, rud, inv, 1.0);
            var h3 = Recover(Spin(rud, lever, inv, ail, 3, 45), rud, inv, lever);
            _out.WriteLine($"{pw,-4} {name,-14} steady α {sa / n,6:F1} ({amin:F0}..{amax:F0}) θ {sp / n,6:F1} Ω {sw / n,4:F0} bank {sb / n,4:F0} (max {bmax,3:F0}) | trace α/θ{trace}");
            _out.WriteLine($"     at 3 turns ({t3:F1}s) α {a3a:F0} θ {a3p:F0}: PARE {p3.turns:F2}t {p3.sec:F1}s, power held {h3.turns:F2}t | developed: PARE {devPare.turns:F2}t {devPare.sec:F1}s, power held {devHeld.turns:F2}t {devHeld.sec:F1}s");
        }
    }

    [Fact]
    public void WingQ()
    {
        if (Environment.GetEnvironmentVariable("Q007_WQ") == null) return;
        var cfg = Load();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        // Ty's logic check (2026-11-08): from a three-view, in a truly flat spin the EXPOSED wing takes full free-stream
        // dynamic pressure across its whole area and shadows the one behind it. Inverted the exposed wing is the upper and
        // the shadowed one is the more AFT wing — losing an aft surface is destabilising, so the nose rises. Upright the
        // exposed wing is the lower and the shadowed one is the more FORWARD wing — losing a forward surface is
        // stabilising, so the nose sits lower. A fine grid either side of +-90 is what shows whether the model does BOTH
        // halves of that or only one.
        Environment.SetEnvironmentVariable("_", null);
        var grid = new System.Collections.Generic.List<double>();
        if (Environment.GetEnvironmentVariable("Q007_WQ_A") is string al && al.Length > 0)
            foreach (var t in al.Split(',')) grid.Add(double.Parse(t));
        else { for (double a = 60; a <= 120; a += 5) grid.Add(a); for (double a = -60; a >= -120; a -= 5) grid.Add(a); }
        foreach (double a in grid)
        {
            double r = a * Math.PI / 180;
            Vec3 vb = new Vec3(30 * Math.Cos(r), 0, 30 * Math.Sin(r));
            Vec3 rates = Environment.GetEnvironmentVariable("Q007_WQ_SPIN") is string sp && sp.Length > 0
                ? new Vec3(0, 0, double.Parse(sp) * Math.PI / 180.0) : Vec3.Zero;
            var q = FlyingGame.Core.Aero.TailWake.WingStripQFactors(cfg, tables, vb, rates);
            var qa = FlyingGame.Core.Aero.TailWake.StripQFactors(cfg, tables, vb, rates);
            for (int i = 0; i < q.Length; i++) if (double.IsNaN(q[i])) q[i] = qa[i];
            int k = 0; var line = new System.Text.StringBuilder($"α {a,5:F0}:");
            foreach (var sf in cfg.Surfaces) { double mn = 1, sum = 0; int n = 0; foreach (var _ in sf.Strips) { double v = q[k++]; if (!double.IsNaN(v)) { mn = Math.Min(mn, v); sum += v; n++; } } if (n > 0) line.Append($" {sf.Id} mean {sum / n:F2} min {mn:F2}"); }
            _out.WriteLine(line.ToString());
        }
    }
}
