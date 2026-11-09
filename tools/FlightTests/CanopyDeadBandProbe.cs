using System;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Diagnostic (CANOPYQ=1): how long the bail-out canopy takes to leave, against the dynamic pressure it is in.
/// A flat spin sits at about 50 kt of airspeed with almost no flow over the canopy's hinge line.</summary>
public class CanopyDeadBandProbe
{
    private readonly ITestOutputHelper _out;
    public CanopyDeadBandProbe(ITestOutputHelper o) { _out = o; }
    private static readonly Vec3 OneG = new(0, -9.80665, 0);

    [Fact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("CANOPYQ") == null) return;
        foreach (string id in new[] { "pitts-s2b-like", "extra-300-like", "great-lakes-like", "j3-cub-like", "glider-2-33-like" })
        {
            CanopySpec spec = CanopySpec.For(id);
            _out.WriteLine($"=== {id}  {spec.Motion}  {spec.MassKg:F1} kg   (DepartMinQ {CanopyDynamics.DepartMinQ:F0} Pa)");
            foreach (double kt in new[] { 0.0, 10, 19, 21, 30, 40, 51, 60, 80, 100 })
            {
                double ms = kt * 0.514444;
                double q = 0.5 * 1.225 * ms * ms;
                var b = new CanopyBailout(new CanopyDynamics(spec));
                double t = 0.0;
                while (!b.Departed && t < 30.0) { b.Step(0.01, q, OneG); t += 0.01; }
                _out.WriteLine($"  {kt,5:F0} kt  q {q,7:F0} Pa  ->  {(b.Departed ? $"left after {t,6:F2} s at {b.Canopy.AngleDeg,5:F0} deg, slid {b.Canopy.SlideM * 1000,4:F0} mm ({b.Canopy.DepartedBy})" : $"STILL ON after 30 s ({b.Canopy.AngleDeg:F0} deg, slid {b.Canopy.SlideM * 1000:F0} mm)")}");
            }
        }
    }
}
