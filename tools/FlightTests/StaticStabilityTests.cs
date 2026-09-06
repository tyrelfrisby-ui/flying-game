using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Checks the SIGN of the two classic static-stability derivatives directly against the strip-theory
/// AeroModel — no rigid-body integration involved, so these are independent of the trim solver and simply
/// verify the airframe geometry (tail arm, fin position/area) produces restoring moments.
/// </summary>
public class StaticStabilityTests
{
    private static readonly Dictionary<string, AirfoilTable> Tables = Aircraft.BuildAirfoilTables(TestAircraftConfig.Load());
    private static readonly AircraftConfig Config = TestAircraftConfig.Load();
    private const double AirDensity = Atmosphere.SeaLevelDensityKgM3;

    [Fact]
    public void PitchingMomentDecreasesAsAngleOfAttackIncreases()
    {
        double moment3Deg = PitchingMomentAt(3.0 * Math.PI / 180.0);
        double moment8Deg = PitchingMomentAt(8.0 * Math.PI / 180.0);

        Assert.True(moment8Deg < moment3Deg,
            $"Expected nose-down restoring trend (dCm/dalpha<0): My(3deg)={moment3Deg:F3}, My(8deg)={moment8Deg:F3}.");
    }

    [Fact]
    public void YawingMomentOpposesSideslipDirection()
    {
        double yawAtZeroSideslip = YawMomentAt(sidewaysVelocity: 0.0);
        double yawAtPositiveSideslip = YawMomentAt(sidewaysVelocity: 2.0);

        // Weathercock stability: with this body-axis convention (x fwd, y right, z down, positive yaw
        // rotation swinging the nose toward +y), a positive sideslip (v>0) must produce a *larger* (more
        // positive) yawing moment so the nose weathercocks toward the relative wind and cancels the slip.
        Assert.True(yawAtPositiveSideslip > yawAtZeroSideslip,
            $"Expected weathercock restoring trend (dN/dbeta>0): N(v=0)={yawAtZeroSideslip:F3}, N(v=2)={yawAtPositiveSideslip:F3}.");
        Assert.True(Math.Abs(yawAtPositiveSideslip - yawAtZeroSideslip) > 1e-3, "Sideslip produced no meaningful yaw moment — check the vertical stabilizer strips.");
    }

    private static double PitchingMomentAt(double alphaRad)
    {
        Vec3 bodyVelocity = new(20.0 * Math.Cos(alphaRad), 0, 20.0 * Math.Sin(alphaRad));
        (_, Vec3 moment) = AeroModel.Compute(Config, Tables, bodyVelocity, Vec3.Zero, Vec3.Zero, AirDensity, ControlDeflections.Neutral);
        return moment.Y;
    }

    private static double YawMomentAt(double sidewaysVelocity)
    {
        Vec3 bodyVelocity = new(20.0, sidewaysVelocity, 0);
        (_, Vec3 moment) = AeroModel.Compute(Config, Tables, bodyVelocity, Vec3.Zero, Vec3.Zero, AirDensity, ControlDeflections.Neutral);
        return moment.Z;
    }
}
