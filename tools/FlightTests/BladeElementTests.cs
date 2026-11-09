using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE PROPELLER BEHAVES LIKE A PROPELLER (Phase 1, BladeElement). These are the properties any real propeller chart
/// has — NACA Report 640's Clark-Y family, or any published Ct/Cq/eta against advance ratio — and they are asserted
/// here before the model is allowed near the sim, because a propeller that is wrong on the bench cannot be made right
/// by anything downstream of it.
/// </summary>
public class BladeElementTests
{
    private readonly ITestOutputHelper _out;
    public BladeElementTests(ITestOutputHelper o) { _out = o; }

    private static BladeElement.Geometry Cub => new(2, 0.89, 16.0, 95.0);
    private static BladeElement.Geometry Cruiser => new(2, 0.95, 24.0, 100.0);

    /// <summary>
    /// STATIC THRUST IS A SOLUTION, NOT A SPECIAL CASE. The old model needed a separate static-thrust branch with a
    /// figure of merit because thrust = eta*P/V divides by zero; here V = 0 is just another point on the curve. It is
    /// also the case that catches the classic blade-element mistake of writing the induced velocity as a fraction of
    /// the forward speed, which is degenerate at V = 0 and gave 24 N for a propeller that makes over two thousand.
    /// </summary>
    [Fact]
    public void ItPullsHardestStandingStill()
    {
        var s = BladeElement.Solve(Cub, 0.0, 0.0, 2300, 1.225);
        var m = BladeElement.Solve(Cub, 30.0, 0.0, 2300, 1.225);
        _out.WriteLine($"static {s.ThrustN:F0} N on {s.PowerW / 1000:F1} kW; at 30 m/s {m.ThrustN:F0} N");
        Assert.True(s.ThrustN > 1200, $"static thrust {s.ThrustN:F0} N is not a propeller");
        Assert.True(s.ThrustN > m.ThrustN, "thrust must fall with forward speed");
        Assert.Equal(0.0, s.Efficiency, 6);                  // useful power is zero when nothing moves
        Assert.True(s.TorqueNm > 0 && s.PowerW > 0);
    }

    /// <summary>
    /// THRUST HAS ONE MAXIMUM, THEN FALLS AWAY AND CROSSES ZERO: past that advance ratio the propeller drives the
    /// engine, and windmilling is the same solution rather than another branch.
    ///
    /// Not "falls monotonically from standstill", which is only true of a FINE blade. A coarse one is STALLED standing
    /// still — with no axial inflow the blade's angle of attack is its whole pitch angle — so as the aeroplane
    /// accelerates the blade unstalls and thrust RISES before it declines. Measured here: the 16 deg blade peaks at
    /// V = 0 and falls from there, while the 24 deg blade goes 3266 N at rest to 3556 N at 20 m/s and only then falls.
    /// Published charts for coarse-pitch propellers show the same dip at low advance ratio. The model found it; this
    /// test originally assumed it away.
    /// </summary>
    [Theory]
    [InlineData(2300.0, 0.89, 16.0, 95.0, true)]     // fine: peaks standing still
    [InlineData(2400.0, 0.95, 24.0, 100.0, false)]   // coarse: stalled standing still, peaks once moving
    public void ThrustPeaksOnceThenFallsAwayAndCrossesZero(double rpm, double radius, double pitch, double af, bool peaksAtRest)
    {
        var g = new BladeElement.Geometry(2, radius, pitch, af);
        double best = double.MinValue, bestV = 0, prev = double.MinValue;
        bool past = false, wentNegative = false;
        for (double v = 0; v <= 130; v += 2.5)
        {
            double t = BladeElement.Solve(g, v, 0.0, rpm, 1.225).ThrustN;
            if (t > best) { best = t; bestV = v; Assert.False(past, $"thrust rose again at {v:F0} m/s after falling"); }
            else past = true;
            if (past) Assert.True(t <= prev + 1e-6, $"thrust rose with speed at {v:F0} m/s ({prev:F0} -> {t:F0} N)");
            prev = t;
            if (t < 0) { wentNegative = true; break; }
        }
        _out.WriteLine($"{pitch:F0} deg blade: peak thrust {best:F0} N at {bestV:F0} m/s");
        Assert.True(wentNegative, "the propeller never reached zero thrust");
        if (peaksAtRest) Assert.Equal(0.0, bestV, 6);
        else Assert.True(bestV > 0, "a blade stalled at rest must make MORE thrust once it is moving");
    }

    /// <summary>
    /// EFFICIENCY IS A CURVE, which is the whole point of replacing the flat 0.75 every type carried. It rises from
    /// zero at the static point, peaks between 0.75 and 0.90 as any decent propeller does, and collapses as the blade
    /// approaches zero thrust.
    /// </summary>
    [Theory]
    [InlineData(2300.0, 0.89, 16.0, 95.0)]
    [InlineData(2400.0, 0.95, 24.0, 100.0)]
    public void EfficiencyRisesToARealisticPeakAndFallsAway(double rpm, double radius, double pitch, double af)
    {
        var g = new BladeElement.Geometry(2, radius, pitch, af);
        double best = 0, bestJ = 0, d = 2 * radius, n = rpm / 60.0;
        for (double v = 1; v <= 120; v += 1)
        {
            var r = BladeElement.Solve(g, v, 0.0, rpm, 1.225);
            if (r.ThrustN <= 0) break;
            if (r.Efficiency > best) { best = r.Efficiency; bestJ = v / (n * d); }
        }
        _out.WriteLine($"peak eta {best:F3} at J {bestJ:F2}");
        Assert.InRange(best, 0.75, 0.90);
        Assert.InRange(bestJ, 0.4, 1.1);
    }

    /// <summary>
    /// P-FACTOR WITHOUT A COEFFICIENT. At angle of attack the down-going blade meets the air faster and at a higher
    /// incidence than the up-going one, so the disc makes a force in its own plane and its thrust centre moves off the
    /// hub. Both used to be tuned numbers — PFactorK, the same 0.35 for every type in the fleet, and NormalForceK,
    /// which was 0 everywhere because nobody had a value. Here they are one azimuthal integral, and the in-plane force
    /// must grow from exactly zero at alpha 0 (where the disc is symmetric) and keep growing with alpha.
    /// </summary>
    [Fact]
    public void TheInPlaneForceGrowsWithAngleOfAttackAndIsZeroWhenSquareOn()
    {
        var g = Cub;
        double last = -1;
        foreach (double aDeg in new[] { 0.0, 5.0, 10.0, 15.0, 20.0 })
        {
            double a = aDeg * System.Math.PI / 180.0, V = 30.0;
            var r = BladeElement.Solve(g, V * System.Math.Cos(a), V * System.Math.Sin(a), 2300, 1.225);
            _out.WriteLine($"alpha {aDeg,4:F0}: thrust {r.ThrustN:F0} N, in-plane {r.NormalN:F1} N");
            if (aDeg == 0) Assert.Equal(0.0, r.NormalN, 6);
            else Assert.True(r.NormalN > last, $"in-plane force did not grow at alpha {aDeg:F0}");
            last = r.NormalN;
        }
    }

    /// <summary>The swirl the slipstream carries is the torque: C = Q/(rho A V_disc), which is exactly what the traced
    /// jet asks for (PropJet). It must fall as the propeller unloads toward zero thrust.</summary>
    [Fact]
    public void TheSwirlIsTheTorqueAndFallsAsThePropellerUnloads()
    {
        double prev = double.MaxValue;
        foreach (double v in new[] { 10.0, 20.0, 30.0, 40.0 })
        {
            var r = BladeElement.Solve(Cub, v, 0.0, 2300, 1.225);
            Assert.True(r.SwirlC > 0 && r.SwirlC < prev, $"swirl {r.SwirlC:F2} at {v:F0} m/s");
            prev = r.SwirlC;
        }
    }
}
