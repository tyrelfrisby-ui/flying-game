using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (B737_PROBE=1): the widget's cruise start inputs for a type — Vso, the 75 % cruise estimate, the
/// glide-trim solve at that speed — and 10 s hands-off from that start. 2026-10-13: the 737's widget cruise start came up at ~0 kt.</summary>
public class B737CruiseProbe
{
    private readonly ITestOutputHelper _out;
    public B737CruiseProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("B737_PROBE") == null) return;
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        foreach (var id in new[] { "boeing-737-like", "dc3-like" })
        {
            var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", id + ".json"));
            double alt = 762, vso = PracticeScenario.EstimateVso(cfg, alt, 0.0), v = PracticeScenario.EstimateCruise75(cfg, alt, vso);
            var g = TrimSolver.SolveGliderTrim(cfg, v, alt);
            _out.WriteLine($"{id}: Vso {vso * 1.944:0} kt  cruise75 {v * 1.944:0} kt  trim converged {g.Converged} α {g.AlphaRad * 57.3:0.0}° elev {g.ElevatorRad * 57.3:0.0}°");
            // The widget's SpawnCruise (AeroWidget.cs), stage by stage.
            bool gearUp = cfg.RetractableGear; double thr = 0.75;
            double aa0 = g.Converged ? g.AlphaRad : 0.05, e0 = g.Converged ? g.ElevatorRad : 0;
            RigidBodyState Make(double vv, double aa) { var att = new Quat(0, System.Math.Sin(aa / 2), 0, System.Math.Cos(aa / 2)); return new RigidBodyState(new Vec3(0, 0, -alt), att, new Vec3(vv * System.Math.Cos(aa), 0, vv * System.Math.Sin(aa)), Vec3.Zero); }
            Aircraft Trial(double vv, double aa, double ee) { var t = new Aircraft(cfg, Make(vv, aa), new FlyingGame.Core.Aero.ControlDeflections(0, ee, 0, 0, 0)); if (gearUp) t.SetGear(false, immediate: true); return t; }
            const double T = 0.25;
            double[] Res(double[] x)
            {
                var t = Trial(x[0], x[1], x[2]);
                new SimLoop(t).RunFor(T, new ControlInputs(x[3], Aircraft.StickForDeflection(x[2], cfg.Controls.Elevator), x[4], 1 - 2 * thr));
                var st = t.State; Vec3 vw = st.Attitude.Rotate(st.Velocity); double V = vw.Length;
                return new[] { (V - x[0]) / T, System.Math.Asin(System.Math.Clamp(-vw.Z / V, -1, 1)) / T, st.Rates.Y / T, st.Rates.X / T, st.Rates.Z / T };
            }
            double[] X = { v, aa0, e0, 0, 0 }, H = { 0.5, 0.003, 0.003, 0.02, 0.02 }, Lim = { 3, 0.0175, 0.0175, 0.1, 0.1 };
            for (int it = 0; it < 16; it++)
            {
                double[] r0 = Res(X);
                _out.WriteLine($"   it {it}: V {X[0] * 1.944:0} α {X[1] * 57.3:0.00} e {X[2] * 57.3:0.00} ail {X[3]:0.000} rud {X[4]:0.000} | res {string.Join(" ", System.Array.ConvertAll(r0, q => q.ToString("0.0000")))}");
                if (System.Math.Abs(r0[0]) < 0.01 && System.Math.Abs(r0[1]) < 0.0005 && System.Math.Abs(r0[2]) < 0.0005 && System.Math.Abs(r0[3]) < 0.0005 && System.Math.Abs(r0[4]) < 0.0005) break;
                var J = new double[5, 5];
                for (int k = 0; k < 5; k++) { var xk = (double[])X.Clone(); xk[k] += H[k]; double[] rk = Res(xk); for (int i = 0; i < 5; i++) J[i, k] = (rk[i] - r0[i]) / H[k]; }
                double[] dx = Solve(J, new[] { -r0[0], -r0[1], -r0[2], -r0[3], -r0[4] });
                if (dx == null) { _out.WriteLine("   singular"); break; }
                double lim = 1; for (int i = 0; i < 5; i++) lim = System.Math.Min(lim, Lim[i] / System.Math.Max(1e-9, System.Math.Abs(dx[i])));
                for (int i = 0; i < 5; i++) X[i] += dx[i] * lim;
                X[0] = System.Math.Clamp(X[0], 1.2 * vso, 8 * vso); X[3] = System.Math.Clamp(X[3], -0.5, 0.5); X[4] = System.Math.Clamp(X[4], -0.6, 0.6);
            }
            var ac = Trial(X[0], X[1], X[2]); var loop = new SimLoop(ac);
            double eI = Aircraft.StickForDeflection(X[2], cfg.Controls.Elevator), aI = X[3], rI = X[4];
            for (int i = 0; i < 3000; i++)
            {
                var st = ac.State; var q = st.Attitude;
                double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                Vec3 vw = q.Rotate(st.Velocity); double V = st.Velocity.Length;
                double climb = -vw.Z, beta = System.Math.Asin(System.Math.Clamp(st.Velocity.Y / System.Math.Max(1, V), -1, 1));
                aI = System.Math.Clamp(aI - 0.25 * roll * 0.02, -0.5, 0.5); rI = System.Math.Clamp(rI + 0.8 * beta * 0.02, -0.6, 0.6); eI = System.Math.Clamp(eI + 0.004 * climb * 0.02, -1, 1);
                loop.RunFor(0.02, new ControlInputs(aI - 0.6 * roll - 0.15 * st.Rates.X, eI + 0.01 * climb + 0.15 * st.Rates.Y, rI + 2.0 * beta - 0.2 * st.Rates.Z, 1 - 2 * thr));
                if (i % 500 == 0 || i == 2999) _out.WriteLine($"   settle t {i * 0.02:0}s: {ac.State.Velocity.Length * 1.944:0} kt α {System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3:0.0} eI {eI:0.000}");
            }
        }
    }
    private static double[] Solve(double[,] A, double[] b)
    {
        int n = b.Length; var m = (double[,])A.Clone(); var r = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c; for (int i = c + 1; i < n; i++) if (System.Math.Abs(m[i, c]) > System.Math.Abs(m[piv, c])) piv = i;
            if (System.Math.Abs(m[piv, c]) < 1e-12) return null;
            if (piv != c) { for (int k = 0; k < n; k++) (m[c, k], m[piv, k]) = (m[piv, k], m[c, k]); (r[c], r[piv]) = (r[piv], r[c]); }
            for (int i = c + 1; i < n; i++) { double f = m[i, c] / m[c, c]; for (int k = c; k < n; k++) m[i, k] -= f * m[c, k]; r[i] -= f * r[c]; }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--) { double sum = r[i]; for (int k = i + 1; k < n; k++) sum -= m[i, k] * x[k]; x[i] = sum / m[i, i]; }
        return x;
    }
}
