using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Turbulence realism pins: correct RMS intensity, zero mean, divergence-free, bounded
/// aircraft response (buffets but doesn't blow up in moderate turbulence).</summary>
public class TurbulenceTests
{
    [Fact]
    public void RmsMatchesIntensityAndMeanIsZero()
    {
        var t = Turbulence.Moderate(3);
        double sum = 0, sumSq = 0; int n = 0;
        for (int i = 0; i < 4000; i++)
        {
            var p = new Vec3(i * 3.7 % 2000, i * 5.3 % 2000, -(i * 2.1 % 1000));
            Vec3 g = t.WindAt(p, 0);
            sum += g.X + g.Y + g.Z; sumSq += g.LengthSquared; n += 3;
        }
        double mean = sum / n;
        double rms = System.Math.Sqrt(sumSq / n);
        Assert.True(System.Math.Abs(mean) < 0.5, $"Turbulence mean must be ~0; got {mean:F2}");
        Assert.InRange(rms, 2.5, 5.5); // moderate ~4 m/s per component
    }

    [Fact]
    public void FieldIsApproximatelyDivergenceFree()
    {
        var t = Turbulence.Severe(7);
        double h = 0.5, maxDiv = 0;
        for (int i = 0; i < 200; i++)
        {
            var p = new Vec3(i * 11.0 % 1500, i * 7.0 % 1500, -(i * 3.0 % 800));
            double dudx = (t.WindAt(p + new Vec3(h,0,0), 0).X - t.WindAt(p - new Vec3(h,0,0), 0).X) / (2*h);
            double dvdy = (t.WindAt(p + new Vec3(0,h,0), 0).Y - t.WindAt(p - new Vec3(0,h,0), 0).Y) / (2*h);
            double dwdz = (t.WindAt(p + new Vec3(0,0,h), 0).Z - t.WindAt(p - new Vec3(0,0,h), 0).Z) / (2*h);
            maxDiv = System.Math.Max(maxDiv, System.Math.Abs(dudx + dvdy + dwdz));
        }
        Assert.True(maxDiv < 0.05, $"Incompressible field divergence must be ~0; got {maxDiv:F4}");
    }

    [Fact]
    public void GliderBuffetsButStaysStableInModerate()
    {
        Atmosphere.ActiveTurbulence = Turbulence.Moderate(2);
        Atmosphere.SimTimeSec = 0;
        try
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glider-2-33-like.json"));
            var trim = TrimSolver.SolveGliderTrim(c, 22, 600);
            double half = trim.ThetaRad / 2;
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0,0,-600),
                new Quat(0, System.Math.Sin(half), 0, System.Math.Cos(half)),
                new Vec3(22*System.Math.Cos(trim.AlphaRad),0,22*System.Math.Sin(trim.AlphaRad)), Vec3.Zero),
                new ControlDeflections(0, trim.ElevatorRad, 0, 0));
            var sim = new SimLoop(ac);
            double maxRate = 0;
            for (int i = 0; i < 400; i++)
            {
                sim.RunFor(0.1, new ControlInputs(0, trim.ElevatorRad/c.Controls.Elevator.MaxDeflRad, 0, 0));
                maxRate = System.Math.Max(maxRate, ac.State.Rates.Length);
                Assert.False(double.IsNaN(ac.State.Velocity.X), "Turbulence must not NaN the sim.");
            }
            Assert.InRange(ac.State.Velocity.Length, 12, 40); // buffeted but flying
            Assert.True(maxRate > 0.02, "Moderate turbulence must actually perturb the aircraft.");
        }
        finally { Atmosphere.ActiveTurbulence = null; Atmosphere.SimTimeSec = 0; }
    }
}
