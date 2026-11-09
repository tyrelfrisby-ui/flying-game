using System;
using System.IO;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Where the traced jet goes, printed. Diagnostics only (PROPJET=1).</summary>
public class PropJetProbe
{
    private readonly ITestOutputHelper _out;
    public PropJetProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    [Fact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("PROPJET") == null) return;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            Vec3 cg = c.Mass.CgVec();
            double arm = PropJet.TailArm(c);
            _out.WriteLine($"=== {id}  tail arm {arm:F2} m  prop {c.Propulsion!.PropDiameterM:F2} m  thrustLineZ {c.Propulsion.ThrustLineZ:+0.00;-0.00}  cg.z {cg.Z:F2}");
            foreach ((string label, Vec3 v, Vec3 w) in new (string, Vec3, Vec3)[]
            {
                ("cruise          ", new Vec3(50, 0, 0), Vec3.Zero),
                ("alpha 10        ", new Vec3(49.2, 0, 8.68), Vec3.Zero),
                ("beta 15         ", new Vec3(48.3, 12.94, 0), Vec3.Zero),
                ("alpha 45        ", new Vec3(21.2, 0, 21.2), Vec3.Zero),
                ("flat spin a90   ", new Vec3(0, 0, 30), new Vec3(0, 0, 5.2)),
                ("slow full power ", new Vec3(12, 0, 2), Vec3.Zero),
                ("yaw rate 100    ", new Vec3(50, 0, 0), new Vec3(0, 0, 1.75)),
                ("pitch rate 100  ", new Vec3(50, 0, 0), new Vec3(0, 1.75, 0)),
            })
            {
                (double du, double r, double sw) = Aircraft.TailSlipstream(c, 1.0, v, w, 1.225);
                var (at, rad, dir, u) = PropJet.Probe(c, v, w, cg, du, r, sw, arm);
                double share = PropJet.TailShare(c, v, w, cg, du, r, sw);
                _out.WriteLine($"  {label} du {du,5:F1} r {r:F2} | at tail arm: y {at.Y,6:F2} z {at.Z,6:F2} rad {rad:F2} " +
                               $"dir ({dir.X:F2},{dir.Y:F2},{dir.Z:F2}) u {u,5:F1} | share {share:F3}");
            }
        }
    }
}
