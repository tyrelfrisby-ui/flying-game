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
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));
        foreach (double swirl in new[] { 0.2, 0.0 })
        {
            Aircraft.SwirlFactor = swirl;
            double vs = PracticeScenario.EstimateVso(cfg, 2500, 0.0), v = 1.15 * vs;
            var t = TrimSolver.SolveGliderTrim(cfg, v, 2500);
            double pitch = t.ThetaRad, alpha = t.AlphaRad;
            var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * System.Math.Cos(alpha), 0, v * System.Math.Sin(alpha)), Vec3.Zero),
                new FlyingGame.Core.Aero.ControlDeflections(0, t.ElevatorRad, 0, 0, 0));
            var sim = new SimLoop(ac);
            double ele = Aircraft.StickForDeflection(t.ElevatorRad, cfg.Controls.Elevator), rud = 0; bool broke = false;
            var line = new System.Text.StringBuilder($"swirl {swirl}: ");
            for (int i = 0; i < 300; i++)
            {
                double a = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
                if (!broke) { ele = System.Math.Max(-1, ele - 0.05); if (a > 14) broke = true; } else { ele = -1; rud = -1; }
                sim.RunFor(0.1, new ControlInputs(0, ele, rud, 1.0));
                if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
                if (i % 10 == 9) line.Append($"{ac.State.Rates.Z * 57.3:0} ");
            }
            _out.WriteLine(line.ToString());
        }
        Aircraft.SwirlFactor = 0.2;
    }
}
