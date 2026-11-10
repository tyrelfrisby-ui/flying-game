using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>What the floats carry, against the angle they are rigged at.</summary>
public class FloatLiftProbe
{
    private readonly ITestOutputHelper _o;
    public FloatLiftProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (!System.IO.Directory.Exists(System.IO.Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(d.FullName, "configs", "aircraft", "j3-cub-floats-like.json"));
        var f = c.Floats!;
        double wFloat = (589.7 - 553.0) * 9.80665;
        double v = 75.0 / 2.23694, alphaCruise = 0.95 * System.Math.PI / 180.0;   // the level-flight alpha at 75 mph
        _o.WriteLine($"floats {f.LengthM:F2} x {f.BeamM:F2} m, AR {f.BeamM / f.LengthM:F3}, planform {2 * f.LengthM * f.BeamM:F2} m2");
        _o.WriteLine($"their own weight {wFloat:F0} N; cruise 75 mph at alpha {alphaCruise * 180 / System.Math.PI:F2} deg\n");
        _o.WriteLine("  rigged at | float alpha |     CL    |  lift N  | share of their weight | drag N | pitching moment N.m");
        foreach (double inc in new[] { 0.0, 2.0, 4.0, 6.0, 8.0, 10.0 })
        {
            f.IncidenceDeg = inc;
            Vec3 vb = new(v * System.Math.Cos(alphaCruise), 0, v * System.Math.Sin(alphaCruise));
            var (force, moment) = FloatAero.Compute(c, vb, 1.225);
            double lift = -(force.Z * System.Math.Cos(alphaCruise) - force.X * System.Math.Sin(alphaCruise));
            double drag = -(force.X * System.Math.Cos(alphaCruise) + force.Z * System.Math.Sin(alphaCruise));
            double a = alphaCruise + inc * System.Math.PI / 180.0;
            var (cl, _) = FloatAero.Coefficients(f.BeamM / f.LengthM, a);
            _o.WriteLine($"  {inc,9:F0}° | {a * 180 / System.Math.PI,10:F2}° | {cl,9:F4} | {lift,8:F0} | {100 * lift / wFloat,20:F0} % | {drag,6:F0} | {moment.Y,12:F0}");
        }
    }
}
