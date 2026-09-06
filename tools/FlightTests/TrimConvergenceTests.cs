using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

public class TrimConvergenceTests
{
    private const double TargetIasMs = 22.0;
    private const double SpawnAltitudeM = 600.0; // matches the A1C1/A1C2 spawn altitude in DATA-CONTRACTS.md

    [Fact]
    public void GliderTrimsAtTargetAirspeed()
    {
        AircraftConfig config = TestAircraftConfig.Load();

        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, TargetIasMs, SpawnAltitudeM);

        Assert.True(trim.Converged, $"Trim solver failed to converge (residual={trim.ResidualNorm:E3} after {trim.Iterations} iterations).");
        Assert.InRange(trim.AlphaRad, 0.0, 20.0 * Math.PI / 180.0);
        Assert.True(trim.ThetaRad < 0.0, $"Unpowered glider must trim in a descent (theta<0); got {trim.ThetaRad * 180.0 / Math.PI:F2} deg.");
        Assert.InRange(Math.Abs(trim.ElevatorRad), 0.0, config.Controls.Elevator.MaxDeflRad);
        Assert.False(double.IsNaN(trim.GlideRatio), "Glide ratio came out NaN — trim likely produced near-zero drag.");
        Assert.InRange(trim.GlideRatio, 5.0, 60.0); // config targets ~25:1; generous band around it
    }

    [Fact]
    public void TrimmedGliderStaysBoundedThroughSimLoop()
    {
        AircraftConfig config = TestAircraftConfig.Load();
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(config, TargetIasMs, SpawnAltitudeM);
        Assert.True(trim.Converged);

        // Wings-level attitude at the trimmed pitch angle (rotation about world Y).
        double half = trim.ThetaRad / 2.0;
        Quat attitude = new(0, Math.Sin(half), 0, Math.Cos(half));
        Vec3 velocityBody = new(TargetIasMs * Math.Cos(trim.AlphaRad), 0, TargetIasMs * Math.Sin(trim.AlphaRad));
        RigidBodyState initialState = new(new Vec3(0, 0, -SpawnAltitudeM), attitude, velocityBody, Vec3.Zero);

        ControlDeflections trimDeflections = new(0, trim.ElevatorRad, 0, 0);
        Aircraft aircraft = new(config, initialState, trimDeflections);
        SimLoop simLoop = new(aircraft);

        RigidBodyState finalState = simLoop.RunFor(5.0, trimDeflections);

        Assert.False(double.IsNaN(finalState.Velocity.X) || double.IsNaN(finalState.Rates.X), "Sim state went NaN while holding trim.");
        Assert.InRange(finalState.Rates.Length, 0.0, 1.0); // near-trim: body rates stay small, not diverging
        Assert.InRange(finalState.Velocity.Length, 5.0, 60.0); // airspeed stays sane over the run
    }
}
