using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Owner requirement (Ty, 2026-09-06, revised same day): ~20:1 best glide (real 2-33 class), and drag must
/// follow a real polar — parasite plus induced varying with angle of attack — not a flat number.
/// Sweeping trim speed exercises exactly that: L/D must peak near 25 and fall away on BOTH sides
/// (induced-drag-dominated slow side, parasite-dominated fast side).
/// </summary>
public class BestGlidePolarTests
{
    private const double AltitudeM = 600.0;

    [Fact]
    public void GliderPolarPeaksNear25To1()
    {
        AircraftConfig config = TestAircraftConfig.Load();

        double bestLd = 0.0, bestSpeed = 0.0;
        var lds = new Dictionary<double, double>();
        for (double v = 17; v <= 30; v += 1)
        {
            TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, v, AltitudeM);
            Assert.True(trim.Converged, $"Trim failed to converge at {v} m/s.");
            lds[v] = trim.GlideRatio;
            if (trim.GlideRatio > bestLd)
            {
                bestLd = trim.GlideRatio;
                bestSpeed = v;
            }
        }

        Assert.InRange(bestLd, 19.0, 21.5); // ~20:1 target (owner revised from 25)
        Assert.InRange(bestSpeed, 19.0, 27.0);

        // Polar shape: performance degrades on both sides of best glide. A flat/crude drag model
        // (constant Cd) cannot produce this — it would make L/D monotonic in alpha.
        Assert.True(lds[17] < bestLd - 2.0, $"Slow side not induced-drag limited: L/D(17)={lds[17]:F1} vs best {bestLd:F1}");
        Assert.True(lds[30] < bestLd - 2.0, $"Fast side not parasite-drag limited: L/D(30)={lds[30]:F1} vs best {bestLd:F1}");
    }
}
