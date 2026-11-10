using System;
using System.Collections.Generic;
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
/// WHERE THE ELEVATOR'S PITCHING MOMENT GOES, STRIP BY STRIP (ELE_BRK=1). The whole-aircraft number says the
/// elevator is dead in the spin (3.7 deg/s^2 at alpha 80, against 43 at alpha 50); this says WHICH factor is
/// missing — the local dynamic pressure, the area, the arm, or the normal-force coefficient itself. Full UP
/// against full DOWN, at the spin's own velocity and rates, alpha swept by turning the relative wind.
/// </summary>
public class ElevatorBreakdownProbe
{
    private readonly ITestOutputHelper _o;
    public ElevatorBreakdownProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("ELE_BRK") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        if (Environment.GetEnvironmentVariable("ELE_SPOIL") is { } spoilK)
        { cfg.SpinAero ??= new SpinAeroConfig(); cfg.SpinAero.ControlSpoilerK = double.Parse(spoilK, System.Globalization.CultureInfo.InvariantCulture); }
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, -1, -1, 1.0));
        var ci = new ControlInputs(1, -1, -1, -1.0);
        for (int i = 0; i < 500; i++) { sim.RunFor(0.1, ci); if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates); }

        var st = ac.State; var om = st.Rates; var v = st.Velocity;
        var I = ac.MassProperties; var cg = cfg.Mass.CgVec();
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double e = cfg.Controls.Elevator.MaxDeflRad;
        double rho = 1.225 * Math.Exp(-(-st.Position.Z) / 8500.0);

        // The horizontal tail's own area, from the config: every strip aft of x -1 that is not on the centreline plane.
        double htArea = 0.0;
        foreach (var sf in cfg.Surfaces)
            foreach (var sp in sf.Strips)
                if (sp.PosVec().X < -1.0 && Math.Abs(sp.PosVec().Y) > 0.03) htArea += sp.Area;

        _o.WriteLine($"developed spin: alpha {Math.Atan2(v.Z, v.X) * 57.3:F1}, {v.Length:F1} m/s, rates {om.Length * 57.3:F0} deg/s, Iyy {I.Iyy:F0}");
        _o.WriteLine($"horizontal tail area {htArea:F3} m^2, elevator travel +/-{e * 57.3:F1} deg, q_inf {0.5 * rho * v.LengthSquared:F0} Pa\n");
        // WHAT THE STABILISER ITSELF IS FLYING AT, in the spin as flown — not at a swept free-stream alpha. The
        // tail is 3.2 m aft of a 4.7 rad/s rotation, so its own flow is nothing like the free stream's, and whether
        // the elevator can work at all is decided by THIS angle, not by the aeroplane's alpha.
        {
            var cap = new List<ForceSample>(512);
            ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
            try { AeroModel.Compute(cfg, tables, v, om, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 0)); }
            finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
            var seen = new HashSet<string>();
            _o.WriteLine("  horizontal-tail strips in the spin AS FLOWN:  x / y  ->  local alpha, eta_t");
            foreach (var fs in cap)
            {
                if (fs.Kind != "tailflow" || fs.PosBody.X > -1.0 || Math.Abs(fs.PosBody.Y) <= 0.03) continue;
                string k = $"{fs.PosBody.X:0.00}/{fs.PosBody.Y:0.00}";
                if (!seen.Add(k)) continue;
                _o.WriteLine($"    {fs.PosBody.X,6:F2} {fs.PosBody.Y,6:F2}  ->  alpha_t {fs.MomentBody.X * 57.3,7:F1} deg   eta_t {fs.MomentBody.Z,5:F2}");
            }
            _o.WriteLine("");
        }

        _o.WriteLine("  alpha |   dFz N   dFx N  |  dMy N.m   deg/s^2 | q_loc Pa  (eta)  | implied dCn | dCn needed for 60 deg/s^2");

        foreach (int deg in new[] { 90, 80, 70, 60, 50, 40, 30 })
        {
            double a = deg * Math.PI / 180;
            Vec3 vs = new(v.Length * Math.Cos(a), 0, v.Length * Math.Sin(a));
            double qInf = 0.5 * rho * vs.LengthSquared;

            double dFz = 0, dFx = 0, dMy = 0, qLoc = 0; int nq = 0;
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var cap = new List<ForceSample>(512);
                ForceDebug.Samples = cap;
                AeroModel.Compute(cfg, tables, vs, om, Vec3.Zero, rho, new ControlDeflections(0, sign * e, 0, 0));
                ForceDebug.Samples = null;
                foreach (var fs in cap)
                {
                    if (fs.Kind != "lift" && fs.Kind != "drag") continue;
                    if (fs.PosBody.X > -1.0 || Math.Abs(fs.PosBody.Y) <= 0.03) continue;   // horizontal tail only
                    double armX = fs.PosBody.X - cg.X, armZ = fs.PosBody.Z - cg.Z;
                    dFz += sign * fs.ForceBody.Z;
                    dFx += sign * fs.ForceBody.X;
                    dMy += sign * (fs.ForceBody.X * armZ - fs.ForceBody.Z * armX);   // My = Fx*z - Fz*x
                }
            }
            // The tail's own dynamic pressure, from the gated flow diagnostic.
            {
                var cap = new List<ForceSample>(512);
                ForceDebug.FlowDiagnostics = true; ForceDebug.Samples = cap;
                try { AeroModel.Compute(cfg, tables, vs, om, Vec3.Zero, rho, new ControlDeflections(0, 0, 0, 0)); }
                finally { ForceDebug.Samples = null; ForceDebug.FlowDiagnostics = false; }
                foreach (var fs in cap) if (fs.Kind == "tailq" && fs.MomentBody.X < 0.5) { qLoc += qInf * fs.ForceBody.Z; nq++; }
            }
            if (nq > 0) qLoc /= nq;
            double dCn = qLoc * htArea > 1e-6 ? Math.Abs(dFz) / (qLoc * htArea) : double.NaN;
            double need = qLoc * htArea > 1e-6 ? 60.0 / 57.3 * I.Iyy / 3.2 / (qLoc * htArea) : double.NaN;   // 3.2 m tail arm
            _o.WriteLine($"   {deg,3}   | {dFz,7:F1} {dFx,7:F1}  | {dMy,8:F1} {Math.Abs(dMy) / I.Iyy * 57.3,8:F1} | {qLoc,7:F0} ({qLoc / qInf,5:F2}) | {dCn,10:F3}  | {need,10:F3}");
        }
    }
}
