using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// SHAFT_SETTLE=1. With the governed shaft integrated in normal flight (PropModel.IntegratesShaft, 2026-11-11), the
/// constant-speed types' dead-engine rpm is a TRANSIENT where it used to be read straight off the closed form. This
/// prints where it goes, so the pins can assert convergence rather than an instant.
/// </summary>
public class SolvedShaftSettleProbe
{
    private readonly ITestOutputHelper _out;
    public SolvedShaftSettleProbe(ITestOutputHelper o) { _out = o; }
    private const double Rho0 = 1.225, FullThrottle = -1.0;
    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static Aircraft Flying(AircraftConfig c, double v, double altM = 1500) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -altM), Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
    private static Aircraft OnTheGround(AircraftConfig c) => new(c, LandingGear.RestingState(c, 0, 0, 0), ControlDeflections.Neutral);

    [Fact]
    public void WhereTheDeadShaftSettles()
    {
        if (Environment.GetEnvironmentVariable("SHAFT_SETTLE") == null) return;

        var c = Load("extra-300-like");
        var ac = Flying(c, 65.0);
        ac.MixtureLever = 0.0;
        var sim = new SimLoop(ac);
        double rho = Atmosphere.DensityAtAltitude(-ac.State.Position.Z);
        double closed = PropModel.WindmillRpm(c.Propulsion!, 65.0, rho);
        _out.WriteLine($"Extra windmilling at 65 m/s, 1500 m. Closed form PropModel.WindmillRpm = {closed:F3} rpm");
        double t = 0;
        foreach (double step in new[] { .05, .45, .5, 1, 1, 2, 5, 5, 10, 10, 20 })
        {
            sim.RunFor(step, new ControlInputs(0, 0, 0, FullThrottle));
            t += step;
            _out.WriteLine($"  t={t,6:F2}s  rpm {ac.EngineRpm,9:F3}   v {ac.State.Velocity.Length,6:F1}  du {ac.LastTailSlipstream.DeltaU,7:F3}");
        }

        _out.WriteLine("\nExtra on the ground, mixture cut at FULL throttle (DeadEngineFullThrottle...):");
        var g = OnTheGround(Load("extra-300-like"));
        g.MixtureLever = 0.0;
        var gs = new SimLoop(g);
        t = 0;
        foreach (double step in new[] { .5, .5, 1, 1, 2, 5, 10, 20, 30 })
        {
            gs.RunFor(step, new ControlInputs(0, 0, 0, FullThrottle));
            t += step;
            _out.WriteLine($"  t={t,6:F2}s  rpm {g.EngineRpm,9:F3}  running={g.EngineRunning}  du {g.LastTailSlipstream.DeltaU,7:F3}");
        }

        _out.WriteLine("\nIDLE rpm on the ground, authored IdleRpm vs where the solved shaft settles:");
        foreach (string id in new[] { "j3-cub-like", "pa18-cub-like", "c172-like", "pitts-s2b-like", "extra-300-like", "great-lakes-like" })
        {
            var cfg = Load(id);
            var idle = OnTheGround(cfg);
            var ls = new SimLoop(idle);
            ls.RunFor(8.0, new ControlInputs(0, 0, 0, 1.0));   // lever +1 = idle
            double full0 = double.NaN;
            var fullAc = OnTheGround(Load(id));
            new SimLoop(fullAc).RunFor(8.0, new ControlInputs(0, 0, 0, FullThrottle));
            full0 = fullAc.EngineRpm;
            _out.WriteLine($"  {id,-20} authored idle {cfg.Propulsion!.IdleRpm,6:F0} max {cfg.Propulsion.MaxRpm,6:F0}" +
                           $"  ->  solved idle {idle.EngineRpm,8:F1}   static full power {full0,8:F1}" +
                           $"   (solved={PropModel.IntegratesShaft(cfg.Propulsion)})");
        }

        _out.WriteLine("\nDoes a FIXED-PITCH type (already solved before 2026-11-11) stop? J-3, mixture cut on the ground:");
        var j = OnTheGround(Load("j3-cub-like"));
        j.MixtureLever = 0.0;
        var js = new SimLoop(j);
        t = 0;
        foreach (double step in new[] { .5, .5, 1, 3, 5, 10, 20, 30 })
        {
            js.RunFor(step, new ControlInputs(0, 0, 0, FullThrottle));
            t += step;
            _out.WriteLine($"  t={t,6:F2}s  rpm {j.EngineRpm,9:F3}  running={j.EngineRunning}");
        }

        _out.WriteLine("\nExtra static full power, SOLVED vs ESTIMATED tail blast:");
        var live = OnTheGround(Load("extra-300-like"));
        new SimLoop(live).RunFor(0.005, new ControlInputs(0, 0, 0, FullThrottle));
        (double du0, double r0, double sw0) = Aircraft.TailSlipstream(c, 1.0, Vec3.Zero, Vec3.Zero, Rho0);
        _out.WriteLine($"  estimated (PropDrive.Running): du {du0:F6}  r {r0:F3}  swirl {sw0:F3}");
        _out.WriteLine($"  solved (live aircraft)       : du {live.LastTailSlipstream.DeltaU:F6}  rpm {live.EngineRpm:F1}");
        var pf = live.Config.Propulsion!;
        double disc = Math.PI * 0.25 * pf.PropDiameterM * pf.PropDiameterM;
        foreach (double du in new[] { du0, live.LastTailSlipstream.DeltaU })
        {
            // Far-wake du implies a static thrust through momentum theory: T = rho*A*(du/0.9)^2/2 at 90% developed.
            double full = du / 0.9;
            double thrust = 0.5 * Rho0 * disc * full * full;
            _out.WriteLine($"  du {du:F2} m/s -> static thrust {thrust:F0} N = {thrust / 4.448:F0} lbf = {thrust / 4.448 / (pf.MaxPowerW / 745.7):F2} lb/hp");
        }
    }
}
