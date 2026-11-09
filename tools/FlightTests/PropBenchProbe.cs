using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE PROPELLER BENCH (Phase 1). Before the blade-element propeller is allowed anywhere near the sim it has to
/// behave like a propeller on its own: thrust falling with forward speed, efficiency rising to a peak near the design
/// advance ratio and collapsing either side of it, torque that is the power divided by the shaft speed, and a static
/// case that is a solution rather than a special branch. Every number here is checkable against published propeller
/// charts (NACA Report 640's family, or any Clark-Y two-blader's Ct/Cq/eta curves against J).
/// </summary>
public class PropBenchProbe
{
    private readonly ITestOutputHelper _o;
    public PropBenchProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Bench()
    {
        // A J-3-sized propeller: 1.78 m, two blades, 2300 rpm, moderate pitch.
        var cub = new BladeElement.Geometry(2, 0.89, 16.0, 95.0);
        Sweep("J-3 sized  1.78 m, 2 blade, 16 deg at 0.75R, 2300 rpm", cub, 2300, new[] { 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0 });
        // A cruiser: finer disc loading, coarser pitch, faster.
        var cruiser = new BladeElement.Geometry(2, 0.95, 24.0, 100.0);
        Sweep("Cruiser    1.90 m, 2 blade, 24 deg at 0.75R, 2400 rpm", cruiser, 2400, new[] { 0.0, 20.0, 40.0, 55.0, 70.0, 85.0, 100.0 });
    }

    private void Sweep(string title, BladeElement.Geometry g, double rpm, double[] speeds)
    {
        _o.WriteLine($"=== {title}");
        _o.WriteLine("     V m/s      J    thrust N   torque Nm    power kW     eta    swirl C m2/s");
        double d = 2 * g.RadiusM, n = rpm / 60.0;
        foreach (double v in speeds)
        {
            var r = BladeElement.Solve(g, v, 0.0, rpm, 1.225);
            double J = v / (n * d);
            _o.WriteLine($"   {v,7:F1} {J,7:F3} {r.ThrustN,11:F0} {r.TorqueNm,11:F1} {r.PowerW / 1000.0,11:F1} {r.Efficiency,8:F3} {r.SwirlC,12:F2}");
        }
    }

    /// <summary>P-factor, as geometry rather than a coefficient: at angle of attack the disc's thrust centre moves.</summary>
    [Fact]
    public void PFactorFallsOutOfTheAzimuth()
    {
        var g = new BladeElement.Geometry(2, 0.89, 16.0, 95.0);
        _o.WriteLine("  alpha   thrust N   in-plane N   (30 m/s, 2300 rpm)");
        foreach (double aDeg in new[] { 0.0, 5.0, 10.0, 15.0, 20.0 })
        {
            double a = aDeg * System.Math.PI / 180.0, V = 30.0;
            var r = BladeElement.Solve(g, V * System.Math.Cos(a), V * System.Math.Sin(a), 2300, 1.225);
            _o.WriteLine($"  {aDeg,5:F0} {r.ThrustN,11:F0} {r.NormalN,12:F1}");
        }
    }
}
