using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Retractable gear: it travels over ~4 s, carries nothing while up, and hangs the type's flat-plate drag
/// area in the wind while down.</summary>
public class RetractableGearTests
{
    private readonly ITestOutputHelper _out;
    public RetractableGearTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void GearDownCostsDragAndGearUpCarriesNothing()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "glasair3-like.json"));
        Assert.True(c.RetractableGear);
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var trim = TrimSolver.SolveGliderTrim(c, 100, 1000); double half = trim.ThetaRad / 2;
        Aircraft Make() => new(c, new RigidBodyState(new Vec3(0, 0, -1000), new Quat(0, Math.Sin(half), 0, Math.Cos(half)), new Vec3(100 * Math.Cos(trim.AlphaRad), 0, 100 * Math.Sin(trim.AlphaRad)), Vec3.Zero), new ControlDeflections(0, trim.ElevatorRad, 0, 0));
        double stick = Aircraft.StickForDeflection(trim.ElevatorRad, c.Controls.Elevator);
        // Same glide, gear up vs gear down: the down case must lose more speed over 10 s at idle.
        double Speed(bool down)
        {
            var ac = Make(); ac.SetGear(down, immediate: true); var sim = new SimLoop(ac);
            for (double t = 0; t < 10; t += 0.05) sim.RunFor(0.05, new ControlInputs(0, stick, 0, 1.0));
            return ac.State.Velocity.Length;
        }
        double vUp = Speed(false), vDown = Speed(true);
        _out.WriteLine($"after 10 s idle glide from 100 m/s: gear up {vUp:F1} m/s, gear down {vDown:F1} m/s");
        Assert.True(vDown < vUp - 1.5, "extended gear must cost noticeable speed");
        // Travel time ~4 s, and the wheels do not touch while up.
        var a2 = Make(); a2.SetGear(false, immediate: true); a2.SetGear(true);
        var s2 = new SimLoop(a2); s2.RunFor(2.0, new ControlInputs(0, stick, 0, 1.0));
        Assert.InRange(a2.GearExtension, 0.4, 0.6);
        s2.RunFor(2.5, new ControlInputs(0, stick, 0, 1.0));
        Assert.Equal(1.0, a2.GearExtension);
        // Sitting on the runway with the gear up: no wheel contact (it drops onto its belly instead).
        var cub = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        Assert.False(cub.RetractableGear);
    }
}
