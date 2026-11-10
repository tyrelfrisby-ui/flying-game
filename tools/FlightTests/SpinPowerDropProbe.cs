using System;
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
/// HOW FAR THE NOSE DROPS WHEN THE POWER COMES OUT of a developed flat spin (SPIN_DROP=1). Ty 2026-11-09, flying his
/// own S-2B against ours: "the nose only drops about 5 degrees with power in the real airplane it would be more like
/// 15-20 degrees." The gyroscopic couple is the only power-dependent term of any size in the spin's pitch budget
/// (+4733 ft.lb of +3210 net at full power), so this sweeps the propeller's polar moment and reports the drop.
/// </summary>
public class SpinPowerDropProbe
{
    private readonly ITestOutputHelper _o;
    public SpinPowerDropProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("SPIN_DROP") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string file = Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json");
        const double IDLE = 1.0, FULL = -1.0;

        _o.WriteLine("Pitts, developed upright flat spin, then power to idle. Real aeroplane: 15-20 deg of nose drop.");
        _o.WriteLine("");
        _o.WriteLine("  I_prop |  alpha power ON | alpha at IDLE |  DROP  | rpm on/off");
        foreach (double ip in new[] { 5.0, 6.4, 7.4, 8.5, 9.0 })
        {
            var cfg = AircraftConfigLoader.LoadFromFile(file);
            cfg.Propulsion!.PropInertia = ip;
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero),
                                  new ControlDeflections(0, 0, 0, 0, 0));
            var sim = new SimLoop(ac);
            for (int i = 0; i < 300 && ac.State.Velocity.Length > 27; i++) sim.RunFor(0.1, new ControlInputs(0, -Math.Min(1, 0.15 + 0.01 * i), 0, IDLE));
            for (int i = 0; i < 100; i++) { sim.RunFor(0.1, new ControlInputs(0, -1, -1, IDLE)); Wrap(ac); }
            for (int i = 0; i < 400; i++) { sim.RunFor(0.1, new ControlInputs(1.0, -1.0, -1, FULL)); Wrap(ac); }
            double aOn = Alpha(ac), rpmOn = ac.FreeEngineRpm;
            for (int i = 0; i < 300; i++) { sim.RunFor(0.1, new ControlInputs(1.0, -1.0, -1, IDLE)); Wrap(ac); }
            double aOff = Alpha(ac), rpmOff = ac.FreeEngineRpm;
            _o.WriteLine($"  {ip,6:F1} | {aOn,15:F1} | {aOff,13:F1} | {aOn - aOff,6:F1} | {rpmOn,4:F0}/{rpmOff,4:F0}");
        }
    }

    private static double Alpha(Aircraft ac) => Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * D;
}
