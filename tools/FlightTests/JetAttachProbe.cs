using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE FIN SEES, as the free column alone, as the attached limit alone, and as the blend. Printed per fin strip
/// so the in/out question is answered strip by strip rather than argued about.
/// </summary>
public class JetAttachProbe
{
    private readonly ITestOutputHelper _o;
    public JetAttachProbe(ITestOutputHelper o) { _o = o; }
    private const double Rho0 = 1.225, KtMs = 0.514444;

    [Fact]
    public void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("JET_ATTACH") == null) return;
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = System.IO.Path.Combine(d.FullName, "configs", "aircraft");

        foreach ((string id, double rigKt) in new[] { ("j3-cub-like", 60.1), ("pitts-s2b-like", 128.3) })
        {
            var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(dir, id + ".json"));
            foreach ((string what, Vec3 vel, Vec3 rates, double thr) in Cases(rigKt))
            {
                PropModel.PropDrive drive = PropModel.SteadyDrive(c.Propulsion, thr, vel.X, Rho0);
                (double du, double r, double swirl) = Aircraft.TailSlipstream(c, thr, vel, rates, Rho0, drive, false);
                _o.WriteLine($"\n=== {id}  {what}   du {du:F1} m/s, tube {r:F3} m, swirl {swirl:F2} m/s");
                _o.WriteLine("   fin strip z |   free only |  attached only |      blend 50/50");
                foreach (SurfaceConfig sc in c.Surfaces)
                {
                    if (!sc.Id.Contains("vstab", System.StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (StripConfig st in sc.Strips)
                    {
                        var at = new Vec3(st.Pos[0], st.Pos[1], st.Pos[2]);
                        PropJet.AttachedBias = 0.0;
                        Vec3 free = PropJet.RelWindAt(c, vel, rates, c.Mass.CgVec(), du, r, swirl, at, true);
                        PropJet.AttachedBias = 1.0;
                        Vec3 att = PropJet.RelWindAt(c, vel, rates, c.Mass.CgVec(), du, r, swirl, at, true);
                        PropJet.AttachedBias = 0.5;
                        Vec3 mix = PropJet.RelWindAt(c, vel, rates, c.Mass.CgVec(), du, r, swirl, at, true);
                        _o.WriteLine($"  {st.Pos[2],11:F2} | {free.Length,11:F2} | {att.Length,14:F2} | {mix.Length,16:F2}");
                    }
                }
                PropJet.AttachedBias = 0.5;
            }
        }
    }

    /// <summary>The spread sweep: what k buys at the fin, across the owner's 0 to 25 % per diameter range.</summary>
    [Fact]
    public void SpreadSweep()
    {
        if (System.Environment.GetEnvironmentVariable("JET_SPREAD") == null) return;
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string dir = System.IO.Path.Combine(d.FullName, "configs", "aircraft");
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(dir, "j3-cub-like.json"));
        var vel = new Vec3(60.1 * KtMs, 0, 0);
        _o.WriteLine("J-3, full throttle. The spread rate now follows the shear parameter, so it falls off with speed.");
        _o.WriteLine("  mph |  du  | lambda | k_eff | tube at fin |  z-0.36 |  z-0.69 |  z-1.01 |  z-1.34");
        foreach (double mph in new[] { 0.0, 20.0, 40.0, 60.0, 70.0, 84.0 })
        {
            double v = mph / 2.23694;
            var vv = new Vec3(v, 0, 0);
            PropModel.PropDrive drive = PropModel.SteadyDrive(c.Propulsion, 1.0, v, Rho0);
            (double du, double r, double swirl) = Aircraft.TailSlipstream(c, 1.0, vv, Vec3.Zero, Rho0, drive, false);
            double lambda = du / (2.0 * v + du);
            double kEff = 0.25 * lambda;
            double arm = PropJet.TailArm(c);
            double rFin = r * (1.0 + kEff * arm / c.Propulsion!.PropDiameterM);
            var line = new System.Text.StringBuilder($" {mph,4:F0} | {du,4:F1} | {lambda,6:F3} | {kEff,5:F3} | {rFin,11:F3} |");
            foreach (double z in new[] { -0.36, -0.69, -1.01, -1.34 })
            {
                Vec3 w = PropJet.RelWindAt(c, vv, Vec3.Zero, c.Mass.CgVec(), du, r, swirl, new Vec3(-5.06, 0, z), true);
                line.Append($" {w.Length,7:F2} |");
            }
            _o.WriteLine(line.ToString());
        }
    }

    private static (string, Vec3, Vec3, double)[] Cases(double rigKt) => new[]
    {
        ("rig, level", new Vec3(rigKt * KtMs, 0, 0), Vec3.Zero, 0.75),
        ("climb, 10 deg alpha", new Vec3(rigKt * KtMs * 0.80, 0, rigKt * KtMs * 0.80 * 0.176), Vec3.Zero, 1.0),
        ("upright spin", new Vec3(22.0, 0, 18.0), new Vec3(0.3, 0.2, 2.6), 0.2),
    };
}
