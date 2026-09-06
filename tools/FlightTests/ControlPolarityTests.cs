using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// ABSOLUTE control polarity (owner squawk 2026-09-06: aileron reversed after a config rebuild —
/// the adverse-yaw test only checks relative signs and can't catch this). Body axes: x fwd, y right,
/// z down; positive Mx = roll right, positive Mz = yaw right, positive My = pitch up.
/// Convention: positive aileron deflection = roll RIGHT; positive rudder = yaw RIGHT;
/// positive (trailing-edge-down) elevator = pitch DOWN.
/// </summary>
public class ControlPolarityTests
{
    [Fact]
    public void DeflectionSignsMatchPilotConvention()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        var tables = Aircraft.BuildAirfoilTables(config);
        double rho = Atmosphere.SeaLevelDensityKgM3;
        Vec3 vel = new(20 * Math.Cos(0.09), 0, 20 * Math.Sin(0.09));

        (_, Vec3 aileron) = AeroModel.Compute(config, tables, vel, Vec3.Zero, Vec3.Zero, rho,
            new ControlDeflections(aileronRad: 0.2, elevatorRad: 0, rudderRad: 0, spoilerFraction: 0));
        Assert.True(aileron.X > 100, $"Positive aileron must roll RIGHT (Mx>0); got Mx={aileron.X:F0}.");

        (_, Vec3 rudder) = AeroModel.Compute(config, tables, vel, Vec3.Zero, Vec3.Zero, rho,
            new ControlDeflections(aileronRad: 0, elevatorRad: 0, rudderRad: 0.2, spoilerFraction: 0));
        Assert.True(rudder.Z > 10, $"Positive rudder must yaw RIGHT (Mz>0); got Mz={rudder.Z:F0}.");

        (_, Vec3 neutral) = AeroModel.Compute(config, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral);
        (_, Vec3 elevator) = AeroModel.Compute(config, tables, vel, Vec3.Zero, Vec3.Zero, rho,
            new ControlDeflections(aileronRad: 0, elevatorRad: 0.15, rudderRad: 0, spoilerFraction: 0));
        Assert.True(elevator.Y < neutral.Y - 10,
            $"Positive (TE-down) elevator must pitch DOWN (dMy<0); got {elevator.Y - neutral.Y:F0}.");
    }
}
