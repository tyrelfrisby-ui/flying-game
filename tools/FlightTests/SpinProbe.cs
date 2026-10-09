using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (run with SPIN_PROBE=1): a type's developed spin with held pro-spin controls, the widget's way.</summary>
public class SpinProbe
{
    private readonly ITestOutputHelper _out;
    public SpinProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbePittsSpin()
    {
        if (System.Environment.GetEnvironmentVariable("SPIN_PROBE") == null) return;
        string id = System.Environment.GetEnvironmentVariable("SPIN_PROBE_ID") ?? "pitts-s2b-like";
        var cfg0 = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));
        foreach (var (swirl, rudCmd, noProp) in new[] { (0.2, -1.0, false), (0.2, 1.0, false), (0.2, -1.0, true), (0.2, 1.0, true) })
        {
            Aircraft.SwirlFactor = swirl;
            var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));
            if (noProp) { if (System.Environment.GetEnvironmentVariable("SPIN_NOGYRO") != null) cfg.Propulsion.PropInertia = 0; else { cfg.Propulsion = null; cfg.Engines.Clear(); } }
            double vs = PracticeScenario.EstimateVso(cfg, 2500, 0.0), v = 1.15 * vs;
            var t = TrimSolver.SolveGliderTrim(cfg, v, 2500);
            double pitch = t.ThetaRad, alpha = t.AlphaRad;
            var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * System.Math.Cos(alpha), 0, v * System.Math.Sin(alpha)), Vec3.Zero),
                new FlyingGame.Core.Aero.ControlDeflections(0, t.ElevatorRad, 0, 0, 0));
            var sim = new SimLoop(ac);
            double ele = Aircraft.StickForDeflection(t.ElevatorRad, cfg.Controls.Elevator), rud = 0; bool broke = false;
            var line = new System.Text.StringBuilder($"rudder {rudCmd:+0;-0} {(noProp ? (System.Environment.GetEnvironmentVariable("SPIN_NOGYRO") != null ? "prop idle, NO GYRO" : "NO PROP") : "prop idle")}: ");
            for (int i = 0; i < 300; i++)
            {
                double a = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
                if (!broke) { ele = System.Math.Max(-1, ele - 0.05); if (a > 14) broke = true; } else { ele = -1; rud = rudCmd; }
                sim.RunFor(0.1, new ControlInputs(0, ele, rud, noProp ? -1.0 : 1.0));
                if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
                if (i % 10 == 9) line.Append($"{ac.State.Rates.Z * 57.3:0} ");
            }
            _out.WriteLine(line.ToString());
        }
        Aircraft.SwirlFactor = 0.2;
    }
}

public class SpinMomentProbe
{
    private readonly ITestOutputHelper _out;
    public SpinMomentProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbeYawBudget()
    {
        if (System.Environment.GetEnvironmentVariable("SPIN_PROBE") == null) return;
        string id = System.Environment.GetEnvironmentVariable("SPIN_PROBE_ID") ?? "pitts-s2b-like";
        double rudCmd = double.Parse(System.Environment.GetEnvironmentVariable("SPIN_RUDDER") ?? "-1");
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));
        cfg.Propulsion = null; cfg.Engines.Clear();
        double vs = PracticeScenario.EstimateVso(cfg, 2500, 0.0), v = 1.15 * vs;
        var t = TrimSolver.SolveGliderTrim(cfg, v, 2500);
        var att = new Quat(0, System.Math.Sin(t.ThetaRad / 2), 0, System.Math.Cos(t.ThetaRad / 2));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * System.Math.Cos(t.AlphaRad), 0, v * System.Math.Sin(t.AlphaRad)), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, t.ElevatorRad, 0, 0, 0)) { CaptureForces = true };
        var sim = new SimLoop(ac);
        double ele = Aircraft.StickForDeflection(t.ElevatorRad, cfg.Controls.Elevator), rud = 0; bool broke = false;
        Vec3 cg = cfg.Mass.CgVec();
        for (int i = 0; i < 300; i++)
        {
            double a = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
            if (!broke) { ele = System.Math.Max(-1, ele - 0.05); if (a > 14) broke = true; } else { ele = -1; rud = rudCmd; }
            sim.RunFor(0.1, new ControlInputs(0, ele, rud, -1.0));
            if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
            if (i % 25 == 24)
            {
                double fin = 0, wingL = 0, wingR = 0, htail = 0, fus = 0, mom = 0, rollW = 0;
                foreach (var f in ac.LastForces)
                {
                    if (f.Kind == "tailflow") continue;
                    Vec3 r = f.PosBody - cg;
                    double nz = r.X * f.ForceBody.Y - r.Y * f.ForceBody.X + f.MomentBody.Z;
                    double nx = r.Y * f.ForceBody.Z - r.Z * f.ForceBody.Y + f.MomentBody.X;
                    bool aft = f.PosBody.X < cg.X - 1.5;
                    if (f.Kind == "fuselage") fus += nz;
                    else if (f.Kind == "moment") mom += nz;
                    else if (aft && System.Math.Abs(f.ForceBody.Y) > System.Math.Abs(f.ForceBody.Z)) fin += nz;
                    else if (aft) htail += nz;
                    else if (f.PosBody.Y < 0) { wingL += nz; rollW += nx; } else { wingR += nz; rollW += nx; }
                }
                double al = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
                _out.WriteLine($"t {(i + 1) * 0.1:0.0} r {ac.State.Rates.Z * 57.3,6:0} p {ac.State.Rates.X * 57.3,6:0} α {al,5:0} β {System.Math.Asin(System.Math.Clamp(ac.State.Velocity.Y / ac.State.Velocity.Length, -1, 1)) * 57.3,5:0} | YAW fin {fin,7:0} wingL {wingL,7:0} wingR {wingR,7:0} htail {htail,6:0} fus {fus,6:0} mom {mom,6:0} | ROLL wings {rollW,7:0}");
            }
        }
    }
}
