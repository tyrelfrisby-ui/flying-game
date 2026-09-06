using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Adverse yaw: deflecting the ailerons (with no rudder input) must yaw the nose *opposite* the roll
/// direction, because the wing carrying more lift also carries more induced drag (ARCHITECTURE.md). This
/// must hold regardless of which absolute sign convention "positive aileron" happens to use — so the test
/// checks that the rolling and yawing moments come out with opposite signs, not a hard-coded direction.
/// </summary>
public class AdverseYawTests
{
    private static readonly AircraftConfig Config = TestAircraftConfig.Load();
    private static readonly Dictionary<string, AirfoilTable> Tables = Aircraft.BuildAirfoilTables(Config);
    private const double AirDensity = Atmosphere.SeaLevelDensityKgM3;

    [Fact]
    public void AileronDeflectionYawsOppositeTheRollDirection()
    {
        Vec3 bodyVelocity = new(20.0 * Math.Cos(5.0 * Math.PI / 180.0), 0, 20.0 * Math.Sin(5.0 * Math.PI / 180.0));
        ControlDeflections withAileron = new(aileronRad: 0.2, elevatorRad: 0, rudderRad: 0, spoilerFraction: 0);

        (_, Vec3 moment) = AeroModel.Compute(Config, Tables, bodyVelocity, Vec3.Zero, Vec3.Zero, AirDensity, withAileron);

        Assert.True(Math.Abs(moment.X) > 1.0, $"Aileron deflection produced negligible roll authority (Mx={moment.X:F4}).");
        Assert.True(moment.X * moment.Z < 0.0,
            $"Expected adverse yaw (roll and yaw moments opposite sign): Mx={moment.X:F4}, Mz(N)={moment.Z:F4}.");
    }
}
