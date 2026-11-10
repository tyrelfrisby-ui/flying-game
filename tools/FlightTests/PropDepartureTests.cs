using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A THROWN PROPELLER (Ty 2026-11-09): "A lost prop needs to do 3 things eliminate all prop effects both drag and
/// thrust. Eliminate the weight of the prop at the proper location/arm. And it needs to be accompanied by oil
/// spraying out the crankshaft forward and getting on the windshields/canopy for realism."
/// </summary>
public class PropDepartureTests
{
    private readonly ITestOutputHelper _out;
    public PropDepartureTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    private static Aircraft Fly(AircraftConfig c) =>
        new(c, new RigidBodyState(new Vec3(0, 0, -300), Quat.Identity, new Vec3(40, 0, 0), Vec3.Zero));

    /// <summary>The weight goes, from the hub and not from the centre of gravity — so the CG moves AFT, which is the
    /// part that makes it frightening rather than merely quiet.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("extra-300-like")]
    public void TheWeightLeavesFromTheHubAndTheCgMovesAft(string id)
    {
        var c = Load(id);
        var ac = Fly(c);
        double m0 = ac.MassProperties.MassKg, iyy0 = ac.MassProperties.Iyy;
        Vec3 cg0 = ac.CgShiftBody;
        double propKg = AirframeContact.PropMassKg(c);
        var hubs = AirframeContact.PropHubsBody(c);
        Assert.NotEmpty(hubs);
        Assert.True(propKg > 5 && propKg < 120, $"{propKg:F1} kg is not a propeller");

        ac.LoseComponent(AirframeComponent.Propeller);
        double m1 = ac.MassProperties.MassKg;
        Vec3 cg1 = ac.CgShiftBody;
        double armX = hubs[0].X - c.Mass.CgVec().X;
        _out.WriteLine($"{id}: prop {propKg:F1} kg at x {hubs[0].X:F2} (arm {armX:F2} m);  mass {m0:F0} -> {m1:F0} kg;  CG shift {cg0.X:F3} -> {cg1.X:F3} m;  Iyy {iyy0:F0} -> {ac.MassProperties.Iyy:F0}");
        Assert.Equal(m0 - propKg, m1, 3);
        Assert.True(armX > 0.5, "the hub must be well ahead of the CG");
        Assert.True(cg1.X < cg0.X - 0.01, $"the CG must move AFT when the nose weight goes: {cg0.X:F3} -> {cg1.X:F3}");
        Assert.True(ac.MassProperties.Iyy < iyy0, "pitch inertia must fall with the nose weight gone");
    }

    /// <summary>Thrust and the windmilling drag both stop: there is nothing on the front of the engine to make
    /// either. (This was already true through _noseLost; it is pinned here because it is one of the three things.)</summary>
    [Fact]
    public void NeitherThrustNorWindmillingDragSurvivesIt()
    {
        var c = Load("pitts-s2b-like");
        var ac = Fly(c);
        var before = Aircraft.TailSlipstream(c, 1.0, new Vec3(40, 0, 0), Vec3.Zero, 1.225);
        Assert.True(before.DeltaU > 1.0, "the propeller should be doing something to the air first");
        ac.LoseComponent(AirframeComponent.Propeller);
        // propGone: no wash, in either direction — neither a blast nor a windmilling deficit.
        var gone = Aircraft.TailSlipstream(c, 1.0, new Vec3(40, 0, 0), Vec3.Zero, 1.225, PropModel.PropDrive.Running, propGone: true);
        Assert.Equal(0.0, gone.DeltaU, 9);
        Assert.Equal(0.0, gone.Radius, 9);
        Assert.Equal(0.0, gone.Swirl, 9);
        _out.WriteLine($"wash before {before.DeltaU:F1} m/s, after the prop leaves {gone.DeltaU:F1} m/s");
    }
}
