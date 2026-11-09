using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE SPEED AT WHICH A FIXED-PITCH AEROPLANE REACHES REDLINE AT FULL THROTTLE — the anchor Ty gave for fitting the
/// blades (2026-11-09): "I know the speed at which I reach redline in the cub full throttle in mph not in knots."
///
/// It is a far better anchor than book cruise: one number, sharply sensitive to blade pitch (a finer blade lets the
/// engine run away at a lower speed, a coarser one holds it back), and a number a pilot has actually seen. Reported
/// in each aeroplane's OWN units, because a number he cannot check is no anchor at all — mph on the J-3, 2-33, Great
/// Lakes and Pitts, knots on the Extra.
/// </summary>
public class RedlineSpeedProbe
{
    private readonly ITestOutputHelper _o;
    public RedlineSpeedProbe(ITestOutputHelper o) { _o = o; }

    private static bool UsesMph(string id) =>
        id.Contains("j3-cub") || id.Contains("2-33") || id.Contains("great-lakes") || id.Contains("pitts");

    [Fact]
    public void Probe()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        _o.WriteLine("THE SPEED AT WHICH FULL THROTTLE REACHES REDLINE, sea level, for a range of blade pitches.");
        _o.WriteLine("The pitch that matches what Ty has seen in the aeroplane is that aeroplane's blade.\n");
        foreach (string id in new[] { "j3-cub-like", "j3-cub-floats-like" })
        {
            var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
            var p = c.Propulsion!;
            bool mph = UsesMph(id);
            string unit = mph ? "mph" : "kt";
            double conv = mph ? 2.23694 : 1.94384;
            _o.WriteLine($"=== {id}: {p.MaxPowerW / 745.7:F0} hp, redline {p.MaxRpm:F0} rpm, {p.PropDiameterM:F2} m {p.PropBladeCount}-blade ({p.PropMaterial})");
            _o.WriteLine($"   pitch at 0.75R |  reaches {p.MaxRpm:F0} rpm at  |  static rpm  |  rpm at 75 {unit}");
            foreach (double pitch in new[] { 12.0, 14.0, 16.0, 18.0, 20.0, 22.0 })
            {
                var g = new BladeElement.Geometry(p.PropBladeCount, 0.5 * p.PropDiameterM, pitch, p.ActivityFactor);
                double redlineV = double.NaN;
                for (double v = 0; v <= 90; v += 0.25)
                {
                    double rpm = PistonEngine.BalancedRpm(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, 1.0, v, 1.225);
                    if (rpm >= p.MaxRpm) { redlineV = v; break; }
                }
                double statRpm = PistonEngine.BalancedRpm(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, 1.0, 0.0, 1.225);
                double at75 = PistonEngine.BalancedRpm(g, p.MaxPowerW, p.MaxRpm, p.IdleRpm, 1.0, 1.0, 75.0 / conv, 1.225);
                string at = double.IsNaN(redlineV) ? "never below 90 m/s" : $"{redlineV * conv,5:F0} {unit}";
                _o.WriteLine($"   {pitch,13:F0}° | {at,22} | {statRpm,10:F0}   | {at75,8:F0}");
            }
            _o.WriteLine("");
        }
    }
}
