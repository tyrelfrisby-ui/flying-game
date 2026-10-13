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
/// Diagnostics for the spin's AUTOROTATION (owner 2026-10-12: "all spin rotation rates are too slow … it has to be a problem
/// with post stall aerodynamic differential in drag and/or lift"):
/// ROTARY_PROBE=1 — a rotary balance: the airframe turned about its velocity vector at fixed α (β 0, controls neutral), the
/// moment along the spin axis split by component, as C = M·Ω̂/(qSb) (+ = autorotative/propelling, − = damping), against
/// Ωb/2V. Where the total crosses zero is the rate a spin with neutral controls would settle at.
/// SPINCHAR_PROBE=1 — the developed spins themselves: Extra and Pitts upright (idle, full aft, full left rudder) and the
/// Extra inverted (full power, push, right rudder, half right aileron): turn time, α, pitch, airspeed.
/// ROTARY_REPO = repo root when run from another directory.
/// </summary>
public class RotaryBalanceProbe
{
    private readonly ITestOutputHelper _out;
    public RotaryBalanceProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(
        System.Environment.GetEnvironmentVariable("ROTARY_REPO") ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")),
        "configs", "aircraft", id + ".json"));

    [Fact]
    public void ProbeRotaryBalance()
    {
        if (System.Environment.GetEnvironmentVariable("ROTARY_PROBE") == null) return;
        _out.WriteLine(Rotary(Load("extra-300-like"), "Extra upright", false));
        _out.WriteLine(Rotary(Load("extra-300-like"), "Extra inverted", true));
        _out.WriteLine(Rotary(Load("pitts-s2b-like"), "Pitts upright", false));
    }

    [Fact]
    public void ProbeSpinCharacteristics()
    {
        if (System.Environment.GetEnvironmentVariable("SPINCHAR_PROBE") == null) return;
        _out.WriteLine(Upright(Load("extra-300-like")));
        _out.WriteLine(Inverted(Load("extra-300-like")));
        _out.WriteLine(Upright(Load("pitts-s2b-like")));
    }

    public static string Rotary(AircraftConfig cfg, string tag, bool inverted)
    {
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double rho = 1.0, V = 30, q = 0.5 * rho * V * V;
        double S = 0, b = AeroModel.WingSpan(cfg);
        foreach (var sf in cfg.Surfaces) if (sf.Id.StartsWith("wing", System.StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) S += st.Area;
        int ns = cfg.Surfaces.Count;
        bool[] Mask(System.Func<string, bool> pick) { var m = new bool[ns]; for (int i = 0; i < ns; i++) m[i] = pick(cfg.Surfaces[i].Id.ToLowerInvariant()); return m; }
        var groups = new (string name, bool[] mask)[]
        {
            ("wing", Mask(id => id.StartsWith("wing"))),
            ("stab", Mask(id => id.Contains("hstab") || id == "elevator")),
            ("fin", Mask(id => id.Contains("vstab") || id.Contains("rudder"))),
            ("fus", Mask(id => false)),   // (the fuselage terms run whatever the mask: nothing else on)
        };
        var sb = new System.Text.StringBuilder($"{tag}: S {S:0.0} m² b {b:0.00} m — C = M·Ω̂/(qSb), + propelling; cross = Ωb/2V where the total turns damping\n");
        double[] alphas = inverted ? new[] { 45.0, 60, 75, 85 } : new[] { 30.0, 45, 60, 75, 85 };
        double[] ks = { 0.05, 0.1, 0.2, 0.3, 0.4, 0.6, 0.9 };
        foreach (double aDeg in alphas)
        {
            double a = (inverted ? -1 : 1) * aDeg * System.Math.PI / 180;
            var u = new Vec3(System.Math.Cos(a), 0, System.Math.Sin(a));
            var line = new System.Text.StringBuilder($"  α {(inverted ? -aDeg : aDeg),4:0}:");
            double prevTot = double.NaN, prevK = 0; string cross = "none (damping from the start)";
            foreach (double k in ks)
            {
                var rates = u * (k * 2 * V / b);
                Vec3 mFus = AeroModel.Compute(cfg, tables, u * V, rates, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 0, 0), surfaceMask: groups[3].mask).Moment;
                double tot = 0; var parts = new System.Text.StringBuilder();
                foreach (var (gname, mask) in groups)
                {
                    Vec3 m = AeroModel.Compute(cfg, tables, u * V, rates, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 0, 0), surfaceMask: mask).Moment;
                    if (gname != "fus") m = m - mFus;
                    double c = Vec3.Dot(m, u) / (q * S * b);
                    tot += c;
                    parts.Append(gname == "wing" ? $" wing{c:+0.000;-0.000}(Cl{m.X / (q * S * b):+0.00;-0.00} Cn{m.Z / (q * S * b):+0.00;-0.00})" : $" {gname}{c:+0.000;-0.000}");
                }
                if (!double.IsNaN(prevTot) && prevTot > 0 && tot <= 0) cross = $"{prevK + (k - prevK) * prevTot / (prevTot - tot):0.00}";
                if (double.IsNaN(prevTot) && tot > 0) cross = $"> {ks[^1]:0.0}";
                if (!double.IsNaN(prevTot) && prevTot > 0 && tot > 0 && k == ks[^1]) cross = $"> {k:0.0}";
                prevTot = tot; prevK = k;
                line.Append($"\n     Ωb/2V {k:0.00}: tot {tot:+0.000;-0.000} |{parts}");
            }
            sb.Append(line).Append($"\n     → crossing Ωb/2V {cross}\n");
        }
        return sb.ToString();
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }

    public static string Upright(AircraftConfig cfg)
    {
        double vs = PracticeScenario.EstimateVso(cfg, 2500, 0.0), v = 1.15 * vs;
        var t = TrimSolver.SolveGliderTrim(cfg, v, 2500);
        var att = new Quat(0, System.Math.Sin(t.ThetaRad / 2), 0, System.Math.Cos(t.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * System.Math.Cos(t.AlphaRad), 0, v * System.Math.Sin(t.AlphaRad)), Vec3.Zero),
            new ControlDeflections(0, t.ElevatorRad, 0, 0, 0));
        var sim = new SimLoop(ac);
        double ele = Aircraft.StickForDeflection(t.ElevatorRad, cfg.Controls.Elevator); bool broke = false;
        for (int i = 0; i < 250; i++)
        {
            double a = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
            if (!broke) { ele = System.Math.Max(-1, ele - 0.05); if (a > 14) broke = true; sim.RunFor(0.1, new ControlInputs(0, ele, 0, 1.0)); }
            else sim.RunFor(0.1, new ControlInputs(0, -1, -1, 1.0));
            Wrap(ac);
        }
        return $"{cfg.Id} UPRIGHT idle, full aft, full left rudder: " + Measure(ac, sim, new ControlInputs(0, -1, -1, 1.0));
    }

    public static string Inverted(AircraftConfig cfg)
    {
        double v = 60;
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(1, 0, 0, 0), new Vec3(v, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, 1, 1, 1.0));
        for (int i = 0; i < 150; i++) { sim.RunFor(0.1, new ControlInputs(0.5, 1, 1, -1.0)); Wrap(ac); }
        return $"{cfg.Id} INVERTED full power, push, right rudder, half right aileron: " + Measure(ac, sim, new ControlInputs(0.5, 1, 1, -1.0));
    }

    private static string Measure(Aircraft ac, SimLoop sim, ControlInputs ci)
    {
        int n = 0; double aS = 0, spinS = 0, vS = 0, pitchS = 0, h0 = -ac.State.Position.Z, drop = 0;
        for (int i = 0; i < 100; i++)
        {
            double hb = -ac.State.Position.Z;
            sim.RunFor(0.1, ci);
            double ha = -ac.State.Position.Z; drop += System.Math.Max(0, hb - ha) < 500 ? hb - ha : 0;
            Wrap(ac);
            var s = ac.State; var vb = s.Velocity;
            aS += System.Math.Atan2(vb.Z, vb.X); vS += vb.Length;
            spinS += s.Attitude.Rotate(s.Rates).Z;
            pitchS += System.Math.Asin(-s.Attitude.Rotate(new Vec3(1, 0, 0)).Z);
            n++;
        }
        double spin = spinS / n * 57.3, turn = 360 / System.Math.Max(1, System.Math.Abs(spin));
        return $"α {aS / n * 57.3:0}  pitch {pitchS / n * 57.3:0}  {vS / n * 1.944:0} kt TAS  spin {spin:0}°/s = {turn:0.0} s/turn  {drop / 10 * turn * 3.281:0} ft/turn";
    }
}
