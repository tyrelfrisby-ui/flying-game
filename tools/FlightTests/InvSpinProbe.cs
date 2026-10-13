using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (INVSPIN_PROBE=1): the Ep4 inverted spin as the widget flies it — roll inverted, idle, slow down
/// pushing; full push + full right rudder; then full power + half right aileron — and, once stable, full push vs full
/// pull. Prints the mean pitch attitude, α, β, turn time and airspeed of each. INVSPIN_UPRIGHT=1 adds the upright
/// comparison (left rudder, right aileron, full power, full pull then full push). Owner 2026-10-12.</summary>
public class InvSpinProbe
{
    private readonly ITestOutputHelper _out;
    public InvSpinProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbeInvertedSpin()
    {
        if (System.Environment.GetEnvironmentVariable("INVSPIN_PROBE") == null) return;
        string id = System.Environment.GetEnvironmentVariable("INVSPIN_ID") ?? "extra-300-like";
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("INVSPIN_REPO") ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));
        string cowl = System.Environment.GetEnvironmentVariable("INVSPIN_COWL_CL");
        if (cowl != null) cfg.Fuselage.Crossflow.CowlCamberCl = double.Parse(cowl, System.Globalization.CultureInfo.InvariantCulture);
        _out.WriteLine(Run(cfg, inverted: true));
        if (System.Environment.GetEnvironmentVariable("INVSPIN_UPRIGHT") != null) _out.WriteLine(Run(cfg, inverted: false));
    }

    private static string Run(AircraftConfig cfg, bool inverted)
    {
        double v = 60;
        // Level, inverted (roll 180°) or upright, at 60 m/s and 2500 m.
        var att = inverted ? new Quat(1, 0, 0, 0) : new Quat(0, 0, 0, 1);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v, 0, 0), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        double s = inverted ? 1 : -1;   // the "spin" stick: push inverted, pull upright
        double rud = inverted ? 1 : -1;
        // Slow down at idle holding the attitude (stick toward the spin side, growing), then full stick + rudder.
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, s * System.Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, s, rud, 1.0));
        var sb = new System.Text.StringBuilder();
        foreach (var (name, ele) in new[] { ("spin-side stick", s), ("opposite stick", -s) })
        {
            int n = 0; double pitch = 0, a = 0, b = 0, w = 0, kt = 0, pmin = 999, pmax = -999;
            for (int i = 0; i < 300; i++)
            {
                sim.RunFor(0.1, new ControlInputs(0.5, ele, rud, -1.0));   // half right aileron, full power
                if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
                if (i < 150) continue;
                var vb = ac.State.Velocity; var x = ac.State.Attitude.Rotate(new Vec3(1, 0, 0));
                double p = System.Math.Asin(-x.Z) * 57.3;
                pitch += p; pmin = System.Math.Min(pmin, p); pmax = System.Math.Max(pmax, p);
                a += System.Math.Atan2(vb.Z, vb.X) * 57.3; b += System.Math.Asin(vb.Y / vb.Length) * 57.3;
                w += ac.State.Rates.Length * 57.3; kt += vb.Length * 1.943844; n++;
            }
            // (body velocity: the state's Velocity is body-axis here — the same as LetGoProbe's α)
            sb.Append($"{cfg.Id} {(inverted ? "INVERTED" : "UPRIGHT ")} {name,-16}: pitch {pitch / n:0} ({pmin:0}…{pmax:0})  α {a / n:0}  β {b / n:0}  |ω| {w / n:0}°/s ({360 / (w / n):0.0} s/turn)  {kt / n:0} kt TAS\n");
        }
        return sb.ToString();
    }
}
