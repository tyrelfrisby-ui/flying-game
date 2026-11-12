using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
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

    /// <summary>
    /// The weight goes, from the nose and not from the centre of gravity — so the CG moves AFT, which is the part
    /// that makes it frightening rather than merely quiet.
    ///
    /// The path is <see cref="CrashRelay.SeparateEngine"/>, which is where the engine and propeller REALLY leave the
    /// aeroplane (the other is <see cref="CrashRelay.ThrowPropellerBlade"/>, blade by blade, in ThrownBladeTests).
    /// It used to be LoseComponent(Propeller), and that was wrong twice over: in the live sim every caller of that
    /// component is a prop STOPPAGE, where the bent propeller is still bolted on — see
    /// <see cref="AStoppedPropellerKeepsItsWeightAndMakesNothing"/> — and it shed mass with no wreck piece to carry
    /// it, so the separation did not conserve momentum. Both ends are checked here: the LEDGER (the aeroplane loses
    /// exactly what the piece takes) and then where it left from.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("extra-300-like")]
    public void TheWeightLeavesFromTheNoseAndTheCgMovesAft(string id)
    {
        var c = Load(id);
        var ac = Fly(c);
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        double m0 = ac.MassProperties.MassKg, iyy0 = ac.MassProperties.Iyy;
        Vec3 cg0 = ac.CgShiftBody;
        Assert.NotEmpty(AirframeContact.PropHubsBody(c));

        WreckPiece? piece = relay.SeparateEngine();
        Assert.NotNull(piece);
        double m1 = ac.MassProperties.MassKg;
        Vec3 cg1 = ac.CgShiftBody;
        double armX = piece!.CgBody.X - c.Mass.CgVec().X;
        _out.WriteLine($"{id}: engine and prop {piece.MassKg:F1} kg at x {piece.CgBody.X:F2} (arm {armX:F2} m);  " +
                       $"mass {m0:F0} -> {m1:F0} kg;  CG shift {cg0.X:F3} -> {cg1.X:F3} m;  Iyy {iyy0:F0} -> {ac.MassProperties.Iyy:F0}");
        Assert.True(piece.MassKg > 5 && piece.MassKg < 400, $"{piece.MassKg:F1} kg is not an engine and propeller");
        Assert.Equal(m0 - piece.MassKg, m1, 6);   // the ledger: nothing vanished and nothing was charged twice
        Assert.True(armX > 0.5, "the engine must be well ahead of the CG");
        Assert.True(cg1.X < cg0.X - 0.01, $"the CG must move AFT when the nose weight goes: {cg0.X:F3} -> {cg1.X:F3}");
        Assert.True(ac.MassProperties.Iyy < iyy0, "pitch inertia must fall with the nose weight gone");
    }

    /// <summary>
    /// A PROP STOPPAGE IS NOT A PROP DEPARTURE. AircraftContactDamage and AircraftTreeStrike both raise
    /// AirframeComponent.Propeller straight after "_propStopped[e] = true; SettleDisc(e)" — the propeller is bent,
    /// stopped and still bolted to the crankshaft, and CrashRelay spawns no wreck piece for it for that reason. So
    /// nothing about the weight and balance may move: until 2026-11-11 a Pitts that stopped its prop on a branch
    /// lost 22.7 kg off its nose and 0.050 m of CG with it, while a TWIN in the same situation lost nothing, because
    /// it took the other half of the same branch. What DOES go is the thrust, the wash and the engine.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("extra-300-like")]
    public void AStoppedPropellerKeepsItsWeightAndMakesNothing(string id)
    {
        var c = Load(id);
        var ac = Fly(c);
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        MassProperties mp0 = ac.MassProperties;
        Vec3 cg0 = ac.CgShiftBody;

        ac.LoseComponent(AirframeComponent.Propeller);
        _out.WriteLine($"{id}: stopped — mass {mp0.MassKg:F1} -> {ac.MassProperties.MassKg:F1} kg, Iyy {mp0.Iyy:F0} -> {ac.MassProperties.Iyy:F0}, " +
                       $"CG shift {cg0.X:F3} -> {ac.CgShiftBody.X:F3} m, pieces {relay.Wreck.Pieces.Count}, stopped {ac.EngineStopped}");
        Assert.Equal(mp0.MassKg, ac.MassProperties.MassKg, 9);
        Assert.Equal(mp0.Iyy, ac.MassProperties.Iyy, 9);
        Assert.Equal(cg0.X, ac.CgShiftBody.X, 9);
        Assert.Equal(0.0, ac.ShedMassKg, 9);
        Assert.Empty(relay.Wreck.Pieces);          // nothing left the aeroplane, so there is nothing to fall
        Assert.True(ac.EngineStopped, "the engine must stop when its propeller does");
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
