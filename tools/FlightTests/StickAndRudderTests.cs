using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Protocol 8 ("Stick and Rudder") physics acceptance, at the sim level: weight and balance. The widget's lessons check the
/// flown concepts live (docs/STICK-AND-RUDDER.md); these pin the model's static truths.
/// </summary>
public class StickAndRudderTests
{
    private readonly ITestOutputHelper _out;
    public StickAndRudderTests(ITestOutputHelper o) { _out = o; }
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft", id + ".json"));

    [Fact]
    public void TwentyPercentWeightRaisesStallSpeedBySqrtW()
    {
        var c = Load("c172-like");
        double v0 = PracticeScenario.EstimateVso(c, 900, 0);
        c.Mass.MassKg *= 1.2;
        double v1 = PracticeScenario.EstimateVso(c, 900, 0);
        _out.WriteLine($"Vs {v0 * 1.944:0.0} → {v1 * 1.944:0.0} kt (×{v1 / v0:0.000}, √1.2 = 1.095)");
        Assert.InRange(v1 / v0, 1.065, 1.125);
    }

    private static (double ld, double v) BestGlide(AircraftConfig c, double alt)
    {
        double best = 0, bv = 0;
        double vso = PracticeScenario.EstimateVso(c, alt, 0);
        for (double v = vso * 1.1; v <= vso * 2.6; v += 0.25)
        {
            var t = TrimSolver.SolveGliderTrim(c, v, alt);
            if (t.Converged && t.GlideRatio > best) { best = t.GlideRatio; bv = v; }
        }
        return (best, bv);
    }

    [Fact]
    public void WeightChangesBestGlideSpeedNotGlideAngle()
    {
        var c = Load("c172-like");
        var (ld0, v0) = BestGlide(c, 900);
        c.Mass.MassKg *= 1.2;
        var (ld1, v1) = BestGlide(c, 900);
        _out.WriteLine($"best glide {ld0:0.00}:1 at {v0 * 1.944:0} kt → {ld1:0.00}:1 at {v1 * 1.944:0} kt");
        Assert.InRange(ld1 / ld0, 0.97, 1.03);            // the same glide angle
        Assert.InRange(v1 / v0, 1.04, 1.15);              // a faster best-glide speed (≈ √1.2)
    }

    [Fact]
    public void ForwardCgNeedsMoreNoseUpElevatorAndMoreTailDownForce()
    {
        var c = Load("c172-like");
        double v = 50; double x0 = c.Mass.Cg[0];
        (double e, double tail) At(double dx)
        {
            c.Mass.Cg[0] = x0 + dx;
            var t = TrimSolver.SolveGliderTrim(c, v, 900);
            Assert.True(t.Converged);
            var tables = Aircraft.BuildAirfoilTables(c);
            var list = new System.Collections.Generic.List<ForceSample>(); ForceDebug.Samples = list;
            AeroModel.Compute(c, tables, new Vec3(v * System.Math.Cos(t.AlphaRad), 0, v * System.Math.Sin(t.AlphaRad)), Vec3.Zero, Vec3.Zero, Atmosphere.DensityAtAltitude(900), new ControlDeflections(0, t.ElevatorRad, 0, 0, 0));
            ForceDebug.Samples = null;
            double tz = 0; foreach (var f in list) if ((f.Kind == "lift" || f.Kind == "drag") && f.PosBody.X < c.Mass.Cg[0] - 2.5 && System.Math.Abs(f.ForceBody.Z) >= System.Math.Abs(f.ForceBody.Y)) tz += f.ForceBody.Z;
            return (t.ElevatorRad, tz);
        }
        var fwd = At(+0.12); var aft = At(-0.12); c.Mass.Cg[0] = x0;
        _out.WriteLine($"CG fwd: elevator {fwd.e * 57.3:0.0}°, tail {fwd.tail:0} N (+ = down) · CG aft: elevator {aft.e * 57.3:0.0}°, tail {aft.tail:0} N");
        Assert.True(fwd.e < aft.e, "forward CG must need more trailing-edge-up (nose-up) elevator");
        Assert.True(fwd.tail > aft.tail, "forward CG must carry more tail DOWN-force");
    }

    [Fact]
    public void EveryTypeIsStaticallyStableAtItsDefaultCg()
    {
        var bad = new System.Collections.Generic.List<string>();
        foreach (string f in System.IO.Directory.GetFiles(System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../..")), "configs", "aircraft"), "*.json"))
        {
            var c = AircraftConfigLoader.LoadFromFile(f);
            if (c.Id.StartsWith("target")) continue;
            var tables = Aircraft.BuildAirfoilTables(c);
            (double L, double M) At(double aDeg) { double a = aDeg * System.Math.PI / 180, V = 40; var (F, Mo) = AeroModel.Compute(c, tables, new Vec3(V * System.Math.Cos(a), 0, V * System.Math.Sin(a)), Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral); return (F.X * System.Math.Sin(a) - F.Z * System.Math.Cos(a), Mo.Y); }
            var (l1, m1) = At(2); var (l2, m2) = At(6);
            double dMdL = (m2 - m1) / (l2 - l1);   // < 0: the NP is behind the CG (stable)
            _out.WriteLine($"{c.Id,-26} NP {dMdL:+0.00;-0.00} m from the CG");
            if (dMdL >= 0) bad.Add(c.Id);
        }
        Assert.True(bad.Count == 0, "statically unstable at the default CG: " + string.Join(", ", bad));
    }
}
