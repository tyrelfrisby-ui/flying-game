using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (LETGO_PROBE=1): a developed spin, then the pilot lets go (all three free) vs neutralises (held at
/// neutral) vs keeps pro-spin — at idle. Prints yaw rate, α and the surfaces' deflections.</summary>
public class LetGoProbe
{
    private readonly ITestOutputHelper _out;
    public LetGoProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbeLetGo()
    {
        if (System.Environment.GetEnvironmentVariable("LETGO_PROBE") == null) return;
        string id = System.Environment.GetEnvironmentVariable("LETGO_ID") ?? "pitts-s2b-like";
        foreach (string mode in new[] { "pro", "free", "neutral" })
        {
            var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("LETGO_REPO") ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));
            double vs = PracticeScenario.EstimateVso(cfg, 2500, 0.0), v = 1.15 * vs;
            var t = TrimSolver.SolveGliderTrim(cfg, v, 2500);
            var att = new Quat(0, System.Math.Sin(t.ThetaRad / 2), 0, System.Math.Cos(t.ThetaRad / 2));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v * System.Math.Cos(t.AlphaRad), 0, v * System.Math.Sin(t.AlphaRad)), Vec3.Zero),
                new FlyingGame.Core.Aero.ControlDeflections(0, t.ElevatorRad, 0, 0, 0));
            var sim = new SimLoop(ac);
            double trimStick = Aircraft.StickForDeflection(t.ElevatorRad, cfg.Controls.Elevator), ele = trimStick; bool broke = false;
            var line = new System.Text.StringBuilder($"{id} {mode,-8}: ");
            for (int i = 0; i < 500; i++)
            {
                double a = System.Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
                ControlInputs ci;
                if (i < 250)
                {
                    if (!broke) { ele = System.Math.Max(-1, ele - 0.05); if (a > 14) broke = true; ci = new ControlInputs(0, ele, 0, 1.0); }
                    else ci = new ControlInputs(0, -1, -1, 1.0);
                }
                else ci = mode switch
                {
                    "free" => new ControlInputs(0, trimStick, 0, 1.0, true, true, true),
                    "neutral" => new ControlInputs(0, 0, 0, 1.0),
                    _ => new ControlInputs(0, -1, -1, 1.0),
                };
                sim.RunFor(0.1, ci);
                if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
                if (i >= 230 && i % 20 == 9)
                {
                    var d = ac.CurrentDeflections;
                    line.Append($"[{i / 10.0:0}s r{ac.State.Rates.Z * 57.3:0} α{a:0} β{System.Math.Asin(ac.State.Velocity.Y / ac.State.Velocity.Length) * 57.3:0} e{d.ElevatorRad * 57.3:0} R{d.RudderRad * 57.3:0} {Hinge(ac, "elevator")} {Hinge(ac, "rudder")}] ");
                }
            }
            _out.WriteLine(line.ToString());
        }
    }

    private static string Hinge(Aircraft ac, string surface)
    {
        var fs = typeof(Aircraft).GetField("_flowState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ac)!;
        double[] al = (double[])fs.GetType().GetField("HingeAlphaRad")!.GetValue(fs)!, q = (double[])fs.GetType().GetField("HingeQ")!.GetValue(fs)!, dir = (double[])fs.GetType().GetField("HingeFlowDir")!.GetValue(fs)!;
        var sb = new System.Text.StringBuilder(surface[0] + ":"); int idx = 0;
        foreach (var sf in ac.Config.Surfaces) foreach (var st in sf.Strips)
        {
            int i = idx++;
            if (st.Control is null || st.Control.Surface != surface || System.Math.Abs(st.Control.Gain) < 0.9) continue;
            sb.Append($"{al[i] * 57.3:0}/{q[i]:0}/{dir[i]:0.0},");
        }
        return sb.ToString();
    }
}
