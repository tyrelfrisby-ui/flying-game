using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;
namespace FlyingGame.FlightTests;
public class SpinTableDump
{
    private readonly ITestOutputHelper _out; public SpinTableDump(ITestOutputHelper o) { _out = o; }
    [Fact]
    public void Dump()
    {
        if (Environment.GetEnvironmentVariable("SPINBAL") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory); while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", (Environment.GetEnvironmentVariable("SPINBAL_ID") ?? "pitts-s2b-like") + ".json"));
        var tables = Aircraft.BuildAirfoilTables(cfg);
        for (int si = 0; si < cfg.Surfaces.Count; si++)
        {
            string key = AeroModel.SectionTableKey(cfg, si) ?? cfg.Surfaces[si].Strips[0].Airfoil;
            _out.WriteLine($"{cfg.Surfaces[si].Id} -> {key}");
        }
        foreach (string key in new[] { AeroModel.SectionTableKey(cfg, cfg.Surfaces.FindIndex(s => s.Id == "hStab"))!, "pitts-wing", "pitts-wing-upper" })
        {
            var t = tables[key];
            _out.WriteLine($"{key}: stall {t.AlphaClMaxRad * 57.3:F1}");
            for (int a = 0; a <= 90; a += 5)
            {
                var c = t.Sample(a / 57.2958);
                double ar = a / 57.2958, cn = c.Cl * Math.Cos(ar) + c.Cd * Math.Sin(ar), ca = c.Cd * Math.Cos(ar) - c.Cl * Math.Sin(ar);
                _out.WriteLine($"  {a,3} Cl {c.Cl,6:F3} Cd {c.Cd,6:F3} Cm {c.Cm,6:F3} | CN {cn,6:F3} CA {ca,6:F3}");
            }
        }
    }
}
