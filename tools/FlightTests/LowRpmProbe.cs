using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE BOTTOM END OF THE RPM CURVE, measured before anything is changed (LOWRPM=1). Ty 2026-11-11:
///
///   "Sometimes lower like a tail slide where the reversed direction works against the prop and can actually stop the
///    engine if rpm falls below around 300 rpm. Model impulse couplings that make a snapping sound and retard
///    ignition to TDC at rpm below 400. Once on[,] the impulse coupling[,] if rpm drops below 300 the friction should
///    be too high for the engine to overcome static and the engine simply winds down and stops"
///
/// Three questions, three tables: what the blade does in REVERSED flow, how long a decaying shaft spends under
/// 300 rpm today, and whether a tailslide actually stops the engine.
/// </summary>
public class LowRpmProbe
{
    private readonly ITestOutputHelper _o;
    public LowRpmProbe(ITestOutputHelper o) { _o = o; }

    private static string ConfigDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return Path.Combine(d.FullName, "configs", "aircraft");
    }
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(ConfigDir(), id + ".json"));

    /// <summary>ITEM 1. The blade element through zero and into reversed axial flow — what a tailslide gives it.</summary>
    [Fact]
    public void ReversedFlowThroughTheDisc()
    {
        if (Environment.GetEnvironmentVariable("LOWRPM") != "1") return;
        double rho = Atmosphere.SeaLevelDensityKgM3;
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double pitch = PistonCompression.FinePitchDeg(p);
            var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
            _o.WriteLine($"\n=== {id}, blade {pitch:F2} deg, at 700 rpm and at 1,500 rpm");
            foreach (double rpm in new[] { 700.0, 1500.0 })
                foreach (double v in new[] { 30.0, 10.0, 2.0, 0.5, 0.0, -0.5, -2.0, -5.0, -10.0, -20.0 })
                {
                    BladeElement.Result r = BladeElement.Solve(g, v, 0.0, rpm, rho);
                    _o.WriteLine($"   {rpm,5:F0} rpm  vAxial {v,6:F1} m/s | thrust {r.ThrustN,9:F1} N  torque {r.TorqueNm,9:F1} N.m" +
                                 $"{(double.IsNaN(r.ThrustN) || double.IsNaN(r.TorqueNm) ? "   NaN!" : "")}");
                }
        }
    }

    /// <summary>ITEM 3. How a dead shaft actually comes down: logged every tenth of a second through the bottom end,
    /// so "how long does it spend below 300 rpm" is a measurement rather than a guess.</summary>
    [Fact]
    public void HowADeadShaftComesDown()
    {
        if (Environment.GetEnvironmentVariable("LOWRPM") != "1") return;
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            _o.WriteLine($"\n=== {id}: mixture cut at 25 m/s, held, the shaft logged down");
            var ac = new Aircraft(c, Held(25.0), ControlDeflections.Neutral);
            ac.MixtureLever = 0.0;
            double below300 = 0.0, below400 = 0.0, t = 0.0;
            double firstUnder300 = double.NaN;
            for (int i = 0; i < 12000; i++)
            {
                ac.Step(new ControlInputs(0, 0, 0, 1.0), SimLoop.DefaultFixedDtSec);
                ac.State = Held(25.0);
                t += SimLoop.DefaultFixedDtSec;
                double rpm = ac.ShaftRpm;
                if (rpm < 400.0) below400 += SimLoop.DefaultFixedDtSec;
                if (rpm < 300.0) { below300 += SimLoop.DefaultFixedDtSec; if (double.IsNaN(firstUnder300)) firstUnder300 = t; }
                if (i % 200 == 0 && t < 20.0) _o.WriteLine($"   t {t,5:F1} s  {rpm,8:F2} rpm");
            }
            _o.WriteLine($"   after 60 s: {ac.ShaftRpm:F2} rpm; first under 300 at {firstUnder300:F1} s; " +
                         $"{below300:F1} s spent under 300 and {below400:F1} s under 400");
        }
    }

    /// <summary>ITEM 1 in the aeroplane: a tailslide — flying BACKWARDS — with the engine at the idle stop.</summary>
    [Fact]
    public void ATailslideAtIdle()
    {
        if (Environment.GetEnvironmentVariable("LOWRPM") != "1") return;
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            _o.WriteLine($"\n=== {id}: engine running, throttle at the idle stop, flown BACKWARDS");
            foreach (double v in new[] { 5.0, 0.0, -5.0, -10.0, -20.0 })
            {
                var ac = new Aircraft(c, Held(v), ControlDeflections.Neutral);
                for (int i = 0; i < 2000; i++) { ac.Step(new ControlInputs(0, 0, 0, 1.0), SimLoop.DefaultFixedDtSec); ac.State = Held(v); }
                _o.WriteLine($"   vAxial {v,6:F1} m/s after 10 s: {ac.ShaftRpm,8:F1} rpm, running {ac.EngineRunning}");
            }
        }
    }

    private static RigidBodyState Held(double vx)
        => new(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(vx, 0, 0), Vec3.Zero);
}
