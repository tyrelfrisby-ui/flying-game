using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// IS THERE A STEP IN THE LOADS? (Ty 2026-11-09, a clip of the 2-33: "There is a new jitter".) A marching wake or
/// shadow model samples a continuous flow at discrete points, so a surface can cross a boundary between one frame and
/// the next and take a sudden change in load. That shows up in the air as a jitter and is invisible to any test that
/// only checks one attitude. This sweeps alpha in fine steps and reports the biggest change between adjacent steps,
/// which for a smooth model must be small and roughly constant.
/// </summary>
public class LoadSmoothnessProbe
{
    private readonly ITestOutputHelper _out;
    public LoadSmoothnessProbe(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private void Sweep(string id, double shadowK, double betaDeg, double speedMs)
    {
        var c = Load(id);
        if (c.SpinAero != null) c.SpinAero.ShadowK = shadowK;
        var tables = Aircraft.BuildAirfoilTables(c);
        double worstL = 0, worstM = 0, worstN = 0; double atL = 0, atM = 0, atN = 0;
        double pL = double.NaN, pM = double.NaN, pN = double.NaN;
        double sumM = 0; int n = 0;
        for (double aDeg = -6.0; aDeg <= 22.0; aDeg += 0.05)
        {
            double a = aDeg * System.Math.PI / 180.0, b = betaDeg * System.Math.PI / 180.0;
            Vec3 v = new Vec3(speedMs * System.Math.Cos(a) * System.Math.Cos(b), speedMs * System.Math.Sin(b), speedMs * System.Math.Sin(a) * System.Math.Cos(b));
            var (_, m) = AeroModel.Compute(c, tables, v, Vec3.Zero, Vec3.Zero, 1.1, ControlDeflections.Neutral);
            if (!double.IsNaN(pM))
            {
                double dL = System.Math.Abs(m.X - pL), dM = System.Math.Abs(m.Y - pM), dN = System.Math.Abs(m.Z - pN);
                if (dL > worstL) { worstL = dL; atL = aDeg; }
                if (dM > worstM) { worstM = dM; atM = aDeg; }
                if (dN > worstN) { worstN = dN; atN = aDeg; }
                sumM += dM; n++;
            }
            pL = m.X; pM = m.Y; pN = m.Z;
        }
        double meanM = n > 0 ? sumM / n : 0;
        _out.WriteLine($"  shadowK {shadowK:F1} beta {betaDeg,4:F0}: worst step per 0.05 deg — roll {worstL,7:F1} N.m (a {atL:F2}), pitch {worstM,7:F1} (a {atM:F2}), yaw {worstN,7:F1} (a {atN:F2});  mean pitch step {meanM:F2}, worst/mean {(meanM > 1e-9 ? worstM / meanM : 0),6:F0}x");
    }

    [Fact]
    public void Probe()
    {
        foreach (string id in new[] { "glider-2-33-like", "pitts-s2b-like", "j3-cub-like" })
        {
            _out.WriteLine($"=== {id}  (30 m/s)");
            foreach (double beta in new[] { 0.0, 6.0 })
                foreach (double k in new[] { 0.0, 0.5 })
                    Sweep(id, k, beta, 30.0);
        }
    }
}
