using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The J-3's blade, fitted to the static rpm Ty gave (2150 at full power, C-85), and what follows from it.</summary>
public class CubBladeFitProbe
{
    private readonly ITestOutputHelper _o;
    public CubBladeFitProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", "j3-cub-like.json"));
        var p = c.Propulsion!;
        const double targetStatic = 2150.0, MphPerMs = 2.23694;

        double pitch = PistonEngine.FitPitchForStaticRpm(p.PropBladeCount, 0.5 * p.PropDiameterM, p.ActivityFactor, 0.15,
                                                         targetStatic, PistonEngine.Spec.For(p), p.IdleRpm);
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
        double got = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, 0.0, 1.225);
        var stat = BladeElement.Solve(g, 0.0, 0.0, got, 1.225);
        _o.WriteLine($"J-3 / C-85: {p.MaxPowerW / 745.7:F0} hp, {p.PropDiameterM:F2} m {p.PropBladeCount}-blade {p.PropMaterial}, redline {p.MaxRpm:F0}");
        _o.WriteLine($"  fitted blade angle at 0.75 R: {pitch:F2} deg   (static {got:F0} rpm against the {targetStatic:F0} asked for)");
        _o.WriteLine($"  static thrust {stat.ThrustN:F0} N = {stat.ThrustN * 0.2248:F0} lb, absorbing {stat.PowerW / 745.7:F0} hp\n");

        _o.WriteLine("  speed mph   rpm    thrust lb   eta");
        double redlineMph = double.NaN;
        for (double mph = 0; mph <= 140; mph += 5)
        {
            double v = mph / MphPerMs;
            double rpm = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, v, 1.225);
            var r = BladeElement.Solve(g, v, 0.0, rpm, 1.225);
            if (double.IsNaN(redlineMph) && rpm >= p.MaxRpm) redlineMph = mph;
            if (mph % 10 == 0 || (rpm >= p.MaxRpm && double.IsNaN(redlineMph)))
                _o.WriteLine($"  {mph,9:F0} {rpm,6:F0} {r.ThrustN * 0.2248,10:F0} {r.Efficiency,7:F3}");
        }
        // Finer sweep for the crossing itself.
        for (double mph = 0; mph <= 160; mph += 0.5)
        {
            double rpm = PistonEngine.BalancedRpm(g, p, 1.0, 1.0, mph / MphPerMs, 1.225);
            if (rpm >= p.MaxRpm) { redlineMph = mph; break; }
        }
        _o.WriteLine($"\n  >>> FULL THROTTLE REACHES THE {p.MaxRpm:F0} RPM REDLINE AT {redlineMph:F0} MPH (sea level)");
    }
}
