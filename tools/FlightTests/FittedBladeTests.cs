using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// EACH TYPE'S FITTED BLADE, LOCKED (Phase 1, the per-type fit). A fixed-pitch blade is fitted to the rpm at full
/// throttle standing still — the number a pilot reads on every run-up — and everything else about that propeller
/// then follows from it with nothing left to tune. Ty 2026-11-09: "Use full power static rpm (zero airspeed) as the
/// anchor for fixed pitch. Set the J3 with C85 to 2150 rpm static and then see what speed in mph it gets to the 2575
/// redline", and of the answer: "yes that is good rpm/airspeed corelation lock it in".
///
/// Speeds are in each aeroplane's OWN units, because a number he cannot check is no anchor: mph on the J-3, the
/// 2-33, the Great Lakes and the Pitts, knots on the Extra.
///
/// THE 2150 IS HIS OWN AEROPLANE'S FIGURE, from the metal propeller ("my 76ak metal prop is 2150 static i don't
/// have the wood prop"), and it is used for BOTH Cubs at his direction: "don't over think this just use 2150 static
/// on both the wood and metal". The two do carry different blades — a wooden Sensenich W72 on wheels, a metal one on
/// floats — and they could be fitted separately from their own TCDS figures later; one anchor for both is the
/// deliberate simplification, not an oversight.
/// </summary>
public class FittedBladeTests
{
    private readonly ITestOutputHelper _out;
    public FittedBladeTests(ITestOutputHelper o) { _out = o; }
    private const double MphPerMs = 2.23694;

    private static AircraftConfig Load(string id)
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
    }

    private static BladeElement.Geometry BladeOf(PropulsionConfig p) =>
        new(p.PropBladeCount, 0.5 * p.PropDiameterM, p.BladePitch75Deg, p.ActivityFactor);

    /// <summary>The J-3's blade holds the anchor it was fitted to: 2150 rpm at full throttle, standing still.</summary>
    [Fact]
    public void BothCubsBladesStandAt2150()
    {
        foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like" })
        {
            var p = Load(id).Propulsion!;
            Assert.True(p.BladePitch75Deg > 0, $"{id}'s blade has not been fitted");
            double stat = PistonEngine.BalancedRpm(BladeOf(p), p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, 1.0, 0.0, 1.225);
            _out.WriteLine($"{id,-22} blade {p.BladePitch75Deg:F2} deg at 0.75 R -> {stat:F0} rpm static");
            Assert.InRange(stat, 2130, 2170);
        }
    }

    /// <summary>
    /// AND THE REST FOLLOWS, which is the whole point of fitting to one number: the owner confirmed the resulting
    /// rpm-against-airspeed correlation, so these are now his figures and not the model's. Full throttle at sea level
    /// reaches the 2575 redline at 84 mph.
    /// </summary>
    [Fact]
    public void AndReachesRedlineAt84Mph()
    {
        var p = Load("j3-cub-floats-like").Propulsion!;
        var g = BladeOf(p);
        double redline = double.NaN;
        for (double mph = 0; mph <= 160; mph += 0.5)
            if (PistonEngine.BalancedRpm(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, 1.0, mph / MphPerMs, 1.225) >= p.MaxRpm)
            { redline = mph; break; }
        _out.WriteLine($"full throttle reaches {p.MaxRpm:F0} rpm at {redline:F0} mph");
        Assert.InRange(redline, 80.0, 88.0);
    }

    /// <summary>Between those two the rpm rises with speed, and the propeller is doing sensible work: a big slow
    /// wooden climb blade pulling hard standing still and giving it back as it accelerates.</summary>
    [Fact]
    public void ThrustFallsAndEfficiencyRisesAcrossTheRange()
    {
        var p = Load("j3-cub-floats-like").Propulsion!;
        var g = BladeOf(p);
        double prevThrust = double.MaxValue, prevEta = -1;
        foreach (double mph in new[] { 0.0, 20.0, 40.0, 60.0, 80.0, 100.0 })
        {
            double v = mph / MphPerMs;
            double rpm = PistonEngine.BalancedRpm(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, 1.0, v, 1.225);
            var r = BladeElement.Solve(g, v, 0.0, rpm, 1.225);
            _out.WriteLine($"{mph,5:F0} mph: {rpm,5:F0} rpm, {r.ThrustN * 0.2248,4:F0} lb, eta {r.Efficiency:F3}");
            Assert.True(r.ThrustN < prevThrust, "thrust must fall with speed");
            Assert.True(r.Efficiency > prevEta, "efficiency must rise across this range");
            prevThrust = r.ThrustN; prevEta = r.Efficiency;
        }
        var s = BladeElement.Solve(g, 0, 0, 2150, 1.225);
        Assert.InRange(s.ThrustN * 0.2248, 400, 550);           // ~475 lb static on 85 hp
        Assert.InRange(s.PowerW / 745.7, 60, 85);               // and it absorbs what the engine gives it
    }
}
