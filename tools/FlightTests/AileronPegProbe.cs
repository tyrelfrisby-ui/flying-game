using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>AIL_PEG=1 — owner, twice: "the problem is the ailerons sticking at full opposite", and after three more
/// builds "The ailerons still peg and stick at full opposite too which is not 'real' behavior". Flies the Pitts into
/// its developed flat spin, lets go of everything (Beggs-Mueller), and prints the free-aileron hinge balance each
/// second: where it floats to, and WHICH TERM of H = a + b·δ − k·δ is putting it there.</summary>
public class AileronPegProbe
{
    private readonly ITestOutputHelper _out;
    public AileronPegProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("AIL_PEG") == null) return;
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.2 + 0.02 * i), 0, 1.0));
        sim.RunFor(10, new ControlInputs(0, -1, -1, 1.0));
        for (int i = 0; i < 400; i++) { sim.RunFor(0.1, new ControlInputs(1, -1, -1, -1.0)); Wrap(ac); }

        double max = cfg.Controls.Aileron.MaxDeflRad;
        _out.WriteLine($"aileron travel ±{max * 57.3:0.0}°, trimType '{cfg.Controls.Aileron.TrimType}'");
        _out.WriteLine("   t   α     δ_ail     a        b        k      k−b     vane    → where it floats");
        // Let go of EVERYTHING (reversible controls free), as Beggs-Mueller asks.
        for (int s = 0; s < 12; s++)
        {
            sim.RunFor(1.0, new ControlInputs(0, 0, 1, 1.0, true, true, false));   // stick + ailerons FREE, full opposite rudder
            Wrap(ac);
            var (a, b, k, den, left, right) = ac.HingeDebug("aileron", cfg.Controls.Aileron);
            double vb = Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * 57.3;
            double free = ac.FreeFloat("aileron", cfg.Controls.Aileron, 0.0, ac.CurrentDeflections.AileronRad);
            _out.WriteLine($"  {s + 1,2}  {vb,4:0}  {ac.CurrentDeflections.AileronRad * 57.3,+7:+0.0;-0.0}  {a,8:+0.000;-0.000} {b,8:+0.000;-0.000} {k,8:+0.000;-0.000} {den,8:+0.000;-0.000} {left,7:+0.0;-0.0}  {free * 57.3,+7:+0.0;-0.0}"
                + (Math.Abs(ac.CurrentDeflections.AileronRad) > 0.98 * max ? "   PEGGED" : ""));
        }
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
