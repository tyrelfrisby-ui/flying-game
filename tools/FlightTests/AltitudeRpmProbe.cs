using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-09: "high altitude actually has the opposite effect. while it is true that the engine makes less power
/// the air is thinner and creates less drag for the propeller blade too. the two roughly cancel eachother out but the
/// pilot infers higher rpm per airspeed at higher altitude because the prop cares about true airspeed not indicated
/// airspeed." Does the model agree? Held at constant INDICATED airspeed, which is the comparison a pilot makes.
/// </summary>
public class AltitudeRpmProbe
{
    private readonly ITestOutputHelper _o;
    public AltitudeRpmProbe(ITestOutputHelper o) { _o = o; }
    private const double MphPerMs = 2.23694, Rho0 = 1.225;

    [Fact]
    public void Probe()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", "j3-cub-like.json"));
        var p = c.Propulsion!;
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, p.BladePitch75Deg, p.ActivityFactor);

        _o.WriteLine("J-3, full throttle, held at 70 mph INDICATED — what the pilot compares.");
        _o.WriteLine("   altitude |  rho   | TAS mph | engine torque share | prop torque share |  rpm");
        foreach (double ft in new[] { 0.0, 2500.0, 5000.0, 7500.0, 10000.0 })
        {
            double m = ft / 3.28084;
            double rho = Atmosphere.DensityAtAltitude(m);
            double pressureRatio = Atmosphere.PressureAtAltitude(m) / Atmosphere.SeaLevelPressurePa;
            double ias = 70.0 / MphPerMs;
            double tas = ias / System.Math.Sqrt(rho / Rho0);
            double rpm = PistonEngine.BalancedRpm(g, p, 1.0, pressureRatio, tas, rho, rho / Rho0);
            _o.WriteLine($"  {ft,8:F0} ft | {rho,6:F3} | {tas * MphPerMs,7:F1} | {PistonEngine.AltitudePowerFraction(rho / Rho0),19:F3} | {rho / Rho0,17:F3} | {rpm,6:F0}");
        }
        _o.WriteLine("\nAnd the same thing held at constant TRUE airspeed, which is NOT what a pilot sees:");
        foreach (double ft in new[] { 0.0, 5000.0, 10000.0 })
        {
            double m = ft / 3.28084;
            double rho = Atmosphere.DensityAtAltitude(m);
            double pressureRatio = Atmosphere.PressureAtAltitude(m) / Atmosphere.SeaLevelPressurePa;
            double rpm = PistonEngine.BalancedRpm(g, p, 1.0, pressureRatio, 70.0 / MphPerMs, rho, rho / Rho0);
            _o.WriteLine($"  {ft,8:F0} ft, 70 mph TRUE: {rpm:F0} rpm");
        }
    }

    /// <summary>
    /// Ty 2026-11-09: "make sure the blades reduced drag with reduced density is also calculated so it is accurate.
    /// 50hp at 10000 ft would swing a prop faster than 50hp at sea level." The same SHAFT POWER, the same true
    /// airspeed, thinner air: the blade's torque goes as the dynamic pressure it works in, so it must spin faster.
    /// </summary>
    [Fact]
    public void TheSamePowerSwingsTheBladeFasterInThinnerAir()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", "j3-cub-like.json"));
        var p = c.Propulsion!;
        var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, p.BladePitch75Deg, p.ActivityFactor);
        const double hp = 50.0, powerW = hp * 745.7, tas = 70.0 / MphPerMs;

        _o.WriteLine($"The SAME {hp:F0} shaft horsepower at {70.0:F0} mph true, at different altitudes:");
        _o.WriteLine("   altitude |  rho   | rpm the blade settles at | torque it takes");
        double low = 0, high = 0;
        foreach (double ft in new[] { 0.0, 5000.0, 10000.0, 15000.0 })
        {
            double m = ft / 3.28084, rho = Atmosphere.DensityAtAltitude(m);
            // Find the rpm at which the blade absorbs exactly this power in THIS air.
            double lo = 500, hi = 4000;
            for (int k = 0; k < 40; k++)
            {
                double mid = 0.5 * (lo + hi);
                if (BladeElement.Solve(g, tas, 0.0, mid, rho).PowerW < powerW) lo = mid; else hi = mid;
            }
            double rpm = 0.5 * (lo + hi);
            var r = BladeElement.Solve(g, tas, 0.0, rpm, rho);
            if (ft == 0) low = rpm;
            if (ft == 10000) high = rpm;
            _o.WriteLine($"  {ft,8:F0} ft | {rho,6:F3} | {rpm,24:F0} | {r.TorqueNm,8:F1} N.m");
        }
        _o.WriteLine($"\n  10 000 ft turns {100 * (high / low - 1):F1} % faster on the same power.");
        Assert.True(high > low, $"the same power must swing the blade FASTER in thinner air: {low:F0} -> {high:F0}");
    }
}
