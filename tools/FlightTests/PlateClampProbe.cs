using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>PLATE_CLAMP=1: the Pitts tail's derived normal force and its elevator authority with
/// AeroModel.ClampNormalForcePastStall on and off, plus the pitch damping dM/dq the clamp was added to fix.</summary>
public class PlateClampProbe
{
    private readonly ITestOutputHelper _out;
    public PlateClampProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("PLATE_CLAMP") == null) return;
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string root = d.FullName;
        foreach (bool clamp in new[] { true, false })
        {
            AeroModel.ClampNormalForcePastStall = clamp;
            var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(root, "configs", "aircraft", "pitts-s2b-like.json"));
            var tables = Aircraft.BuildAirfoilTables(cfg);
            AirfoilTable t = tables[AeroModel.SectionTableKey(cfg, cfg.Surfaces.FindIndex(s => s.Id == "hStab"))!];
            double Cn(double deg) { var c = t.Sample(deg * Math.PI / 180); double a = deg * Math.PI / 180; return c.Cl * Math.Cos(a) + c.Cd * Math.Sin(a); }
            double e = cfg.Controls.Elevator.MaxDeflRad;
            double DCm(double deg)
            {
                double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                double m(double de) => AeroModel.Compute(cfg, tables, v, Vec3.Zero, Vec3.Zero, 1.0, new ControlDeflections(0, de, 0, 0)).Moment.Y;
                return Math.Abs(m(-e) - m(e));
            }
            // Pitch damping: dM/dq about the trim, at the alphas a spin sits at. POSITIVE is anti-damping.
            double dMdq(double deg)
            {
                double a = deg * Math.PI / 180; Vec3 v = new(30 * Math.Cos(a), 0, 30 * Math.Sin(a));
                double m(double q) => AeroModel.Compute(cfg, tables, v, new Vec3(0, q, 0), Vec3.Zero, 1.0, new ControlDeflections(0, 0, 0, 0)).Moment.Y;
                return (m(0.1) - m(-0.1)) / 0.2;
            }
            var cn = string.Join("  ", new[] { 20, 30, 45, 60, 90 }.Select(x => $"{x}:{Cn(x):F2}"));
            var au = string.Join("  ", new[] { 20, 30, 45, 60 }.Select(x => $"{x}:{DCm(x) / DCm(0):F3}"));
            var dq = string.Join("  ", new[] { 10, 20, 30, 45, 60 }.Select(x => $"{x}:{dMdq(x):+0;-0}"));
            _out.WriteLine($"clamp {(clamp ? "ON " : "OFF")}  CN {cn}\n          elevator authority / at α0  {au}\n          dM/dq N·m per rad/s (+ is ANTI-damping)  {dq}");
        }
        AeroModel.ClampNormalForcePastStall = true;
    }
}
