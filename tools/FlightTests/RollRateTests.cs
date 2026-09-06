using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Roll-rate sanity: a rolling wing must generate a restoring (damping) rolling moment with no aileron
/// input at all — this falls straight out of strip theory (each strip's local AoA shifts by p*y/V) and is
/// one of the most basic sanity checks that the strip loop is wired up correctly.
/// </summary>
public class RollRateTests
{
    private static readonly AircraftConfig Config = TestAircraftConfig.Load();
    private static readonly Dictionary<string, AirfoilTable> Tables = Aircraft.BuildAirfoilTables(Config);
    private const double AirDensity = Atmosphere.SeaLevelDensityKgM3;

    [Fact]
    public void PositiveRollRateProducesRestoringRollingMoment()
    {
        Vec3 bodyVelocity = new(20.0 * Math.Cos(5.0 * Math.PI / 180.0), 0, 20.0 * Math.Sin(5.0 * Math.PI / 180.0));

        double rollMomentNoRate = RollingMomentAt(bodyVelocity, rollRateRadPerSec: 0.0);
        double rollMomentWithRate = RollingMomentAt(bodyVelocity, rollRateRadPerSec: 0.3);

        Assert.True(rollMomentWithRate < rollMomentNoRate,
            $"Expected roll damping (dMx/dp<0): Mx(p=0)={rollMomentNoRate:F3}, Mx(p=0.3)={rollMomentWithRate:F3}.");

        double damping = rollMomentNoRate - rollMomentWithRate;
        Assert.True(damping > 1.0, $"Roll damping moment is implausibly small ({damping:F4} N*m) — check wing strip spanwise positions.");
    }

    private static double RollingMomentAt(Vec3 bodyVelocity, double rollRateRadPerSec)
    {
        Vec3 rates = new(rollRateRadPerSec, 0, 0);
        (_, Vec3 moment) = AeroModel.Compute(Config, Tables, bodyVelocity, rates, Vec3.Zero, AirDensity, ControlDeflections.Neutral);
        return moment.X;
    }
}
