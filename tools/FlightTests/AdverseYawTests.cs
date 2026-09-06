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
        double alphaRad = 5.0 * Math.PI / 180.0;
        Vec3 bodyVelocity = new(20.0 * Math.Cos(alphaRad), 0, 20.0 * Math.Sin(alphaRad));
        ControlDeflections withAileron = new(aileronRad: 0.2, elevatorRad: 0, rudderRad: 0, spoilerFraction: 0);

        (_, Vec3 moment) = AeroModel.Compute(Config, Tables, bodyVelocity, Vec3.Zero, Vec3.Zero, AirDensity, withAileron);

        // Cn_da is defined in STABILITY axes (velocity-aligned). Reading yaw about the body z-axis
        // at nonzero alpha is contaminated by the (much larger) roll moment leaking through the
        // frame tilt: Mz_body picks up +Mx*sin(alpha)-scale crosstalk. Rotate the moment vector by
        // -alpha about y to get the aerodynamically meaningful roll/yaw pair.
        double sinA = Math.Sin(alphaRad), cosA = Math.Cos(alphaRad);
        double rollStab = moment.X * cosA + moment.Z * sinA;
        double yawStab = -moment.X * sinA + moment.Z * cosA;

        Assert.True(Math.Abs(rollStab) > 1.0, $"Aileron deflection produced negligible roll authority (L_stab={rollStab:F4}).");
        Assert.True(rollStab * yawStab < 0.0,
            $"Expected adverse yaw (stability-axes roll and yaw moments opposite sign): L_stab={rollStab:F4}, N_stab={yawStab:F4}.");
    }
}
