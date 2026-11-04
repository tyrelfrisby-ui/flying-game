using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

[Collection("WorldTerrainActive")]
public sealed class FuelSlickTests
{
    private readonly ITestOutputHelper _output;
    public FuelSlickTests(ITestOutputHelper output) { _output = output; }
    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(
        Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
    // Literal tank placards, independent of the formula/config under test.
    public static IEnumerable<object[]> PoweredTanks => new[] {
        new object[] { "c172-like", 108.0 }, new object[] { "pitts-s2b-like", 79.04 },
        new object[] { "great-lakes-like", 74.68 }, new object[] { "extra-300-like", 123.10 },
        new object[] { "j3-cub-like", 32.71 }, new object[] { "j3-cub-floats-like", 32.71 }
    };
    private static void Fuel(AircraftLoad load, double fraction)
    {
        foreach (var s in load.Slots) if (s.Kind == AircraftLoad.StationKind.Fuel) load.Set(s.Id, s.MaxKg * fraction);
    }

    [Theory]
    [MemberData(nameof(PoweredTanks))]
    public void FuelSlickFullAndHalfFuelFollowVolumeExactly(string id, double capacity)
    {
        var config = Load(id); var load = new AircraftLoad(config);
        Assert.Equal(capacity, load.FuelCapacityKg, 8);
        _output.WriteLine("aircraft | load | kg | litres | final radius m | final area m²");
        _output.WriteLine($"{id} | authored | {load.FuelKg:F3} | {load.FuelKg / .72:F3} | {Math.Sqrt(load.FuelKg / (720 * Math.PI * 1e-6)):F3} | {load.FuelKg / .00072:F3}");
        foreach (double fraction in new[] { 1.0, .5 })
        {
            Fuel(load, fraction);
            var slick = FuelSlick.TryCreate(config, load, true, true, Vec3.Zero, Vec3.Zero)!;
            double kg = capacity * fraction;
            double expectedArea = kg / .00072;
            double expectedR = Math.Sqrt(expectedArea / Math.PI);
            Assert.Equal(expectedR, slick.FinalRadiusM, 8);
            Assert.Equal(expectedArea, slick.FinalAreaM2, 6);
            Assert.Equal(0, slick.RadiusAt(0)); Assert.Equal(0, slick.RadiusAt(-5));
            Assert.Equal(expectedR / 5, slick.RadiusAt(60), 8);
            Assert.Equal(expectedR, slick.RadiusAt(300), 8); Assert.Equal(expectedR, slick.RadiusAt(900), 8);
            _output.WriteLine($"{id} | {fraction:P0} | {kg:F3} | {slick.VolumeM3 * 1000:F3} | {slick.FinalRadiusM:F3} | {slick.FinalAreaM2:F3}");
        }
    }

    [Fact]
    public void FuelSlickFleetTableCoversEveryPoweredConfigWithTanks()
    {
        var expected = PoweredTanks.Select(r => (string)r[0]).OrderBy(s => s).ToArray();
        var actual = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"TestData"),"*.json")
            .Where(p => { var c = AircraftConfigLoader.LoadFromFile(p); return c.Propulsion != null && new AircraftLoad(c).FuelCapacityKg > 0; })
            .Select(p => Path.GetFileNameWithoutExtension(p)).OrderBy(s => s).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FuelSlickDriftIsThreePercentHorizontalAndPauseSafe()
    {
        var c = Load("c172-like"); var load = new AircraftLoad(c);
        var origin = new Vec3(12,34,-10);
        var slick = FuelSlick.TryCreate(c,load,true,true,origin,new Vec3(10,-5,8))!;
        Assert.Equal(42, slick.CentreAt(100).X, 10); Assert.Equal(19, slick.CentreAt(100).Y, 10);
        Assert.Equal(-10, slick.CentreAt(100).Z); Assert.Equal(origin, slick.CentreAt(0));
        Assert.Equal(origin, slick.CentreAt(-100));
    }

    [Fact]
    public void FuelSlickRejectsGlidersEmptyNoStationsLandAndIntactDitching()
    {
        var c = Load("c172-like"); var load = new AircraftLoad(c);
        Assert.Null(FuelSlick.TryCreate(c,load,false,true,Vec3.Zero,Vec3.Zero));
        Assert.Null(FuelSlick.TryCreate(c,load,true,false,Vec3.Zero,Vec3.Zero));
        Fuel(load,0); Assert.Null(FuelSlick.TryCreate(c,load,true,true,Vec3.Zero,Vec3.Zero));
        Fuel(load, 1); c.Propulsion = null;
        Assert.Null(FuelSlick.TryCreate(c,load,true,true,Vec3.Zero,Vec3.Zero));
        var glider = Load("glider-2-33-like");
        Assert.Null(FuelSlick.TryCreate(glider,new AircraftLoad(glider),true,true,Vec3.Zero,Vec3.Zero));
        c = Load("c172-like"); c.Loading = new LoadingConfig();
        Assert.Null(FuelSlick.TryCreate(c,new AircraftLoad(c),true,true,Vec3.Zero,Vec3.Zero));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FuelSlickRelayRejectsIntactWaterDitchingAndLandBreakup(bool water)
    {
        var oldTerrain = WorldTerrain.Active; double? oldWater = FloatHydro.FlatWaterOverride;
        double oldTime = Atmosphere.SimTimeSec;
        try
        {
            WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = water ? 10 : null;
            double surface = water ? 10 : 0;
            var c = Load("c172-like");
            double angle = 8 * Math.PI / 360;
            var attitude = water ? new Quat(0,Math.Sin(angle),0,Math.Cos(angle)) : Quat.Identity;
            var a = new Aircraft(c,new RigidBodyState(new Vec3(0,0,-(surface+1.6)),attitude,
                attitude.Conjugate().Rotate(new Vec3(water ? 12 : 35,0,water ? .5 : 30)),Vec3.Zero));
            var relay = new CrashRelay(a);
            try
            {
                var sim = new SimLoop(a);
                // Observe the first intact water arrival. Later joint overloads may turn this into
                // a break-up (covered separately); the water contact alone must not create a slick.
                for(int i=0;i<300 && (!water || !a.LastImpact.OnWater);i++)
                { sim.RunFor(SimLoop.DefaultFixedDtSec,ControlInputs.Neutral); relay.Step(SimLoop.DefaultFixedDtSec); }
                Assert.Null(relay.Slick);
                if(water)
                {
                    Assert.True(a.Hydro.EverWet); Assert.True(a.LastImpact.OnWater);
                    Assert.DoesNotContain(relay.Wreck.Pieces, p => p.Component is not
                        (AirframeComponent.GearLeft or AirframeComponent.GearRight or AirframeComponent.GearNose or AirframeComponent.GearTail));
                    // An old intact water contact must not turn a later unrelated break into a spill.
                    Atmosphere.AdvanceTime(.001);
                    a.LoseComponent(AirframeComponent.Nose);
                    Assert.Null(relay.Slick);
                }
                else Assert.NotEmpty(relay.Wreck.Pieces);
            }
            finally { relay.Detach(); }
        }
        finally { FloatHydro.FlatWaterOverride = oldWater; WorldTerrain.Active = oldTerrain; Atmosphere.SimTimeSec = oldTime; }
    }

    [Theory]
    [InlineData(35, 40)]
    [InlineData(12, 1)]
    public void FuelSlickWaterBreakupCreatesOneFromFuelAtImpactAndRespawnClearsIt(double forwardMs, double downMs)
    {
        var oldTerrain = WorldTerrain.Active; var oldWind = Atmosphere.SteadyWind;
        var oldField = Atmosphere.Field; double oldTime = Atmosphere.SimTimeSec;
        double? oldWater = FloatHydro.FlatWaterOverride;
        try
        {
            WorldTerrain.Active = null; FloatHydro.FlatWaterOverride = 10;
            Atmosphere.Field = null; Atmosphere.SteadyWind = new Vec3(10,0,0);
            var c = Load("c172-like");
            var a = new Aircraft(c,new RigidBodyState(new Vec3(0,0,-14),Quat.Identity,new Vec3(forwardMs,0,downMs),Vec3.Zero));
            Fuel(a.Load,.5);
            var relay = new CrashRelay(a);
            try
            {
                var sim = new SimLoop(a); double captured = -1; Vec3 contact = Vec3.Zero;
                void Capture()
                {
                    if (relay.Slick != null && captured < 0) { captured = a.Load.FuelKg; contact = a.LastImpact.PositionWorld; }
                }
                a.HardImpact += (closing, point) => Capture();
                relay.PieceSpawned += piece => Capture();
                for (int i=0; i<500 && relay.Slick == null; i++)
                { sim.RunFor(SimLoop.DefaultFixedDtSec,ControlInputs.Neutral); relay.Step(SimLoop.DefaultFixedDtSec); }
                Assert.NotNull(relay.Slick);
                var first = relay.Slick!;
                Assert.Equal(captured,first.FuelKg,10); Assert.True(captured < 54);
                Assert.Equal(contact.X,first.Origin.X,10); Assert.Equal(contact.Y,first.Origin.Y,10);
                Assert.Equal(-10,first.Origin.Z); Assert.Equal(.3,first.DriftVelocity.X,10);
                for(int i=0;i<50;i++) { sim.RunFor(SimLoop.DefaultFixedDtSec,ControlInputs.Neutral); relay.Step(SimLoop.DefaultFixedDtSec); }
                Assert.Same(first,relay.Slick);
                var respawn = new CrashRelay(new Aircraft(c,new RigidBodyState(new Vec3(0,0,-100),Quat.Identity,Vec3.Zero,Vec3.Zero)));
                Assert.Null(respawn.Slick); respawn.Detach();
                _output.WriteLine($"Water break-up: fuel={captured:F9} kg, radius={first.FinalRadiusM:F6} m, pieces={relay.Wreck.Pieces.Count}");
            }
            finally { relay.Detach(); }
        }
        finally
        {
            FloatHydro.FlatWaterOverride = oldWater; WorldTerrain.Active = oldTerrain;
            Atmosphere.SteadyWind = oldWind; Atmosphere.Field = oldField; Atmosphere.SimTimeSec = oldTime;
        }
    }
}
