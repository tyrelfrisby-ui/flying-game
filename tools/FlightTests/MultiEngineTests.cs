using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Multi-engine asymmetric-thrust sign pins (owner-directed true twin model).</summary>
public class MultiEngineTests
{
    private static (Aircraft, SimLoop) Spawn(string id)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var trim = TrimSolver.SolveGliderTrim(c, 50.0, 1000.0);
        double half = trim.ThetaRad / 2.0;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1000),
            new Quat(0, Math.Sin(half), 0, Math.Cos(half)),
            new Vec3(50, 0, 0), Vec3.Zero), new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        return (ac, new SimLoop(ac));
    }

    [Fact]
    public void LeftEngineOutYawsLeft()
    {
        var (ac, sim) = Spawn("seminole-like");
        ac.SetEngineThrottleScale(0, 0.0);      // fail LEFT engine (mount 0, -y)
        sim.RunFor(2.0, new ControlInputs(0, 0, 0, -1.0)); // full throttle both levers, no rudder
        // dead left engine -> right engine's thrust yaws the nose toward the dead (left) side.
        Assert.True(ac.State.Rates.Z < -0.02, $"Left engine out must yaw LEFT (r<0); got r={ac.State.Rates.Z:F3}");
    }

    [Fact]
    public void RightEngineOutYawsRight()
    {
        var (ac, sim) = Spawn("seminole-like");
        ac.SetEngineThrottleScale(1, 0.0);      // fail RIGHT engine (mount 1, +y)
        sim.RunFor(2.0, new ControlInputs(0, 0, 0, -1.0));
        Assert.True(ac.State.Rates.Z > 0.02, $"Right engine out must yaw RIGHT (r>0); got r={ac.State.Rates.Z:F3}");
    }

    [Fact]
    public void BothEnginesSymmetricNoYaw()
    {
        var (ac, sim) = Spawn("seminole-like");
        sim.RunFor(2.0, new ControlInputs(0, 0, 0, -1.0)); // both running full
        Assert.True(Math.Abs(ac.State.Rates.Z) < 0.03, $"Symmetric twin must not yaw from thrust; got r={ac.State.Rates.Z:F3}");
    }
}
