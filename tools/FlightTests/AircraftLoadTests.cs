using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Weight and balance (owner 2026-10-31: "a configuration on the launch page for loading the airplane with fuel bags
/// and people then model the resulting weight and cg while they fly").
///
/// The first test is the one that matters most: every aeroplane in the fleet must fly with exactly the mass
/// properties it flew with before this existed, because every book speed, stall speed and takeoff roll in this suite
/// is tuned against them.
/// </summary>
public sealed class AircraftLoadTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static readonly string[] Fleet =
    {
        "c172-like", "pitts-s2b-like", "extra-300-like", "dc3-like", "p51d-like", "pa18-cub-like",
        "glider-2-33-like", "boeing-737-like", "pa18-floats-like", "cassutt-f1-like", "stearman-pt17-like",
    };

    [Theory]
    [InlineData("c172-like")]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("dc3-like")]
    [InlineData("p51d-like")]
    [InlineData("pa18-cub-like")]
    [InlineData("glider-2-33-like")]
    [InlineData("boeing-737-like")]
    public void TheLoadingModelChangesNothingAboutAnAeroplaneAsAuthored(string id)
    {
        AircraftConfig c = Load(id);
        var ac = new Aircraft(c, Level(), ControlDeflections.Neutral);
        MassProperties p = ac.MassProperties;
        Assert.Equal(c.Mass.MassKg, p.MassKg, 9);
        Assert.Equal(c.Mass.Inertia.Ixx, p.Ixx, 9);
        Assert.Equal(c.Mass.Inertia.Iyy, p.Iyy, 9);
        Assert.Equal(c.Mass.Inertia.Izz, p.Izz, 9);
        Assert.Equal(c.Mass.Inertia.Ixz, p.Ixz, 9);
    }

    /// <summary>And nothing burns off an aeroplane with no tanks authored, so a long flight cannot drift either.</summary>
    [Fact]
    public void AnAeroplaneWithNoTanksAuthoredBurnsNothingAndItsMassNeverMoves()
    {
        AircraftConfig c = Load("c172-like");
        c.Loading = new LoadingConfig();                       // explicitly nothing authored
        var ac = new Aircraft(c, Level(), ControlDeflections.Neutral);
        double m0 = ac.MassProperties.MassKg;
        Assert.Equal(0.0, ac.FuelFlowKgPerSec, 12);
        for (int i = 0; i < 2000; i++) new SimLoop(ac, 1.0 / 200.0).RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, -1.0));
        Assert.Equal(m0, ac.MassProperties.MassKg, 9);
    }

    private static RigidBodyState Level() =>
        new(new Vec3(0, 0, -1000), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero);

    // ---- a worked example, which also documents the schema ----

    /// <summary>A loaded 172: two front seats, two back, two wing tanks, a baggage locker. Stations are in the body
    /// reference frame, +x forward, +y right, +z down, the same frame as the gear and the strips.</summary>
    private static AircraftConfig Loaded()
    {
        AircraftConfig c = Load("c172-like");
        c.Loading = new LoadingConfig
        {
            MaxGrossMassKg = 1111,
            // +x is FORWARD, so the forward limit is the larger number. Getting this the wrong way round is exactly
            // the bug this example is here to stop: a test that encodes the same misunderstanding as the model
            // passes while the aeroplane is graded backwards.
            CgForwardLimitM = 0.08,
            CgAftLimitM = -0.24,
            Seats =
            {
                new StationConfig { Id = "pilot",   Label = "Pilot",      Station = new[] { 0.10, -0.28, 0.05 }, MaxKg = 140, ReferenceKg = 86 },
                new StationConfig { Id = "copilot", Label = "Right front", Station = new[] { 0.10, 0.28, 0.05 }, MaxKg = 140, ReferenceKg = 0 },
                new StationConfig { Id = "rearL",   Label = "Rear left",  Station = new[] { -0.75, -0.28, 0.05 }, MaxKg = 140, ReferenceKg = 0 },
                new StationConfig { Id = "rearR",   Label = "Rear right", Station = new[] { -0.75, 0.28, 0.05 }, MaxKg = 140, ReferenceKg = 0 },
            },
            Tanks =
            {
                new StationConfig { Id = "left",  Label = "Left wing",  Station = new[] { 0.0, -1.95, -0.30 }, MaxKg = 54, ReferenceKg = 40, BurnOrder = 0 },
                new StationConfig { Id = "right", Label = "Right wing", Station = new[] { 0.0, 1.95, -0.30 }, MaxKg = 54, ReferenceKg = 40, BurnOrder = 0 },
            },
            Baggage =
            {
                new StationConfig { Id = "aft", Label = "Baggage", Station = new[] { -1.25, 0.0, 0.10 }, MaxKg = 54, ReferenceKg = 0 },
            },
        };
        return c;
    }

    /// <summary>The invariant, with stations actually authored: loaded as authored, it is the authored aeroplane to
    /// the last decimal. This is what lets a loading block be added to any aeroplane without retuning it.</summary>
    [Fact]
    public void AtItsReferenceLoadingItIsExactlyTheAeroplaneThatWasAuthored()
    {
        AircraftConfig c = Loaded();
        var load = new AircraftLoad(c);
        Assert.False(load.Degenerate);
        MassProperties p = load.Properties;
        Assert.Equal(c.Mass.MassKg, p.MassKg, 6);
        Assert.Equal(c.Mass.Cg[0], load.Cg.X, 9);
        Assert.Equal(c.Mass.Cg[1], load.Cg.Y, 9);
        Assert.Equal(c.Mass.Cg[2], load.Cg.Z, 9);
        Assert.Equal(c.Mass.Inertia.Ixx, p.Ixx, 6);
        Assert.Equal(c.Mass.Inertia.Iyy, p.Iyy, 6);
        Assert.Equal(c.Mass.Inertia.Izz, p.Izz, 6);
        Assert.Equal(c.Mass.Inertia.Ixz, p.Ixz, 6);
        // And the empty mass is what is left when the reference people and fuel come out.
        Assert.Equal(c.Mass.MassKg - 86 - 80, load.EmptyMassKg, 6);
    }

    [Fact]
    public void PuttingSomeoneInTheBackMovesTheCgAftAndAddsTheirWeight()
    {
        var load = new AircraftLoad(Loaded());
        double m0 = load.TotalMassKg, x0 = load.Cg.X;
        load.Set("rearL", 90);
        Assert.Equal(m0 + 90, load.TotalMassKg, 6);
        Assert.True(load.Cg.X < x0, $"CG went from {x0:F3} to {load.Cg.X:F3}; +x is forward, so a rear seat must reduce it");
        // One person on the left also puts the CG left of the centreline.
        Assert.True(load.Cg.Y < -0.001, $"CG y is {load.Cg.Y:F4}, so a single left-seat passenger moved nothing sideways");
    }

    [Fact]
    public void BaggageInTheLockerMovesTheCgFurtherAftThanTheSameWeightInTheBackSeat()
    {
        var a = new AircraftLoad(Loaded()); a.Set("rearL", 50); a.Set("rearR", 0);
        var b = new AircraftLoad(Loaded()); b.Set("aft", 50);
        Assert.Equal(a.TotalMassKg, b.TotalMassKg, 6);
        Assert.True(b.Cg.X < a.Cg.X, "the locker is further aft than the rear seats, so it must move the CG further");
    }

    /// <summary>Mass far from the CG raises the inertia about the axes it is offset from — the parallel-axis terms.
    /// Weight in the wings is a roll-inertia problem; weight in the cabin barely touches it.</summary>
    [Fact]
    public void WeightOutInTheWingsRaisesRollInertiaFarMoreThanTheSameWeightInTheCabin()
    {
        var wings = new AircraftLoad(Loaded()); wings.FillFuel();
        var cabin = new AircraftLoad(Loaded());
        double added = wings.FuelKg - cabin.FuelKg;
        cabin.Set("copilot", added);   // the same extra mass, but on the centreline-ish
        Assert.Equal(wings.TotalMassKg, cabin.TotalMassKg, 3);
        Assert.True(wings.Properties.Ixx > cabin.Properties.Ixx + 10,
                    $"Ixx {wings.Properties.Ixx:F0} in the wings vs {cabin.Properties.Ixx:F0} in the cabin");
    }

    [Fact]
    public void FuelBurnsInOrderAndASetOfTanksDrainsTogetherSoTheCgDoesNotGoSideways()
    {
        var load = new AircraftLoad(Loaded());
        load.FillFuel();
        double y0 = load.Cg.Y;
        double drawn = load.BurnFuel(60);
        Assert.Equal(60, drawn, 6);
        Assert.Equal(48, load.FuelKg, 6);                        // 108 in the tanks, 60 out
        Assert.Equal(load.Find("left")!.Kg, load.Find("right")!.Kg, 6);
        Assert.Equal(y0, load.Cg.Y, 6);                          // both tanks together: no lateral shift
    }

    [Fact]
    public void BurningMoreThanIsInTheTanksDrawsWhatIsThereAndReportsIt()
    {
        var load = new AircraftLoad(Loaded());
        load.FillFuel();
        double drawn = load.BurnFuel(10_000);
        Assert.Equal(108, drawn, 6);
        Assert.Equal(0.0, load.FuelKg, 9);
        Assert.Equal(0.0, load.BurnFuel(5), 9);                  // dry
    }

    [Fact]
    public void TanksDrainInTheirAuthoredOrder()
    {
        AircraftConfig c = Loaded();
        c.Loading.Tanks[0].BurnOrder = 0;                        // left first, then right
        c.Loading.Tanks[1].BurnOrder = 1;
        var load = new AircraftLoad(c);
        load.FillFuel();
        load.BurnFuel(54);
        Assert.Equal(0.0, load.Find("left")!.Kg, 6);
        Assert.Equal(54.0, load.Find("right")!.Kg, 6);
    }

    [Fact]
    public void ThePlacardsCatchOverGrossAndAnOutOfLimitsCg()
    {
        var load = new AircraftLoad(Loaded());
        load.FillFuel();
        Assert.Null(load.Placard);

        load.Set("pilot", 140); load.Set("copilot", 140); load.Set("rearL", 140); load.Set("rearR", 140); load.Set("aft", 54);
        Assert.True(load.Overweight, $"{load.TotalMassKg:F0} kg against a 1111 kg placard is not over gross?");
        Assert.StartsWith("OVER GROSS", load.Placard);

        // The balance placard, against a deliberately narrow envelope. With the example's own -0.24 m aft limit you
        // cannot actually get this aeroplane out of balance without first being over gross, which is realistic for a
        // four-seat high wing but useless for testing the placard, so the envelope is tightened here on purpose.
        AircraftConfig narrow = Loaded();
        narrow.Loading.MaxGrossMassKg = 0;          // weight is tested above; this half is about balance alone
        narrow.Loading.CgForwardLimitM = 0.02;
        narrow.Loading.CgAftLimitM = -0.09;
        var aft = new AircraftLoad(narrow);
        aft.Set("pilot", 0); aft.Set("rearR", 100);
        Assert.True(aft.CgOutOfLimits, $"CG at {aft.Cg.X:F3} m is inside a -0.09..+0.02 envelope?");
        Assert.Equal("CG AFT of the limit", aft.Placard);

        // The forward branch, with an envelope placed aft of where this aeroplane balances. In a four-seat high wing
        // the front seats sit almost on the CG, so no legal load drives it forward of a realistic limit; the branch
        // still has to work for the types where it does.
        AircraftConfig fwd = Loaded();
        fwd.Loading.MaxGrossMassKg = 0;
        fwd.Loading.CgForwardLimitM = -0.10;
        fwd.Loading.CgAftLimitM = -0.30;
        var nose = new AircraftLoad(fwd);
        Assert.True(nose.CgOutOfLimits, $"CG at {nose.Cg.X:F3} m is inside a -0.30..-0.10 envelope?");
        Assert.Equal("CG FORWARD of the limit", nose.Placard);
    }

    [Fact]
    public void AnAeroplaneWithNoPlacardsIsNeverComplainedAbout()
    {
        AircraftConfig c = Loaded();
        c.Loading.MaxGrossMassKg = 0; c.Loading.CgForwardLimitM = 0; c.Loading.CgAftLimitM = 0;
        var load = new AircraftLoad(c);
        load.Set("pilot", 140); load.Set("rearL", 140); load.Set("aft", 54);
        Assert.False(load.Overweight);
        Assert.False(load.CgOutOfLimits);
        Assert.Null(load.Placard);
        Assert.True(double.IsNaN(load.CgFraction));
    }

    /// <summary>Authoring that does not add up must report the authored aeroplane unchanged and say so, rather than
    /// flying a negative empty mass.</summary>
    [Fact]
    public void AuthoringThatDoesNotAddUpFallsBackToTheAuthoredAeroplane()
    {
        AircraftConfig c = Loaded();
        c.Loading.Seats[0].ReferenceKg = 100_000;                // more in one seat than the whole aeroplane weighs
        var load = new AircraftLoad(c);
        Assert.True(load.Degenerate);
        Assert.Equal(c.Mass.MassKg, load.Properties.MassKg, 9);
        Assert.Equal(c.Mass.Inertia.Ixx, load.Properties.Ixx, 9);
        Assert.True(double.IsFinite(load.Cg.X));
    }

    // ---- and in the air ----

    /// <summary>Flown: a 172 at full throttle with wing tanks burns fuel, loses weight, and the CG moves — which is
    /// the owner's "model the resulting weight and cg while they fly".</summary>
    [Fact]
    public void FlyingItBurnsFuelAndTheWeightAndCgFollow()
    {
        AircraftConfig c = Loaded();
        // Put all the fuel on one side so the burn has somewhere obvious to move the CG to.
        c.Loading.Tanks[0].BurnOrder = 0; c.Loading.Tanks[1].BurnOrder = 1;
        // High up and only for a few minutes: an uncontrolled aeroplane flown for ten minutes stalls, spins, hits the
        // ground and loses parts, and then DAMAGE owns the mass tensor, which is a different test.
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -6000), new Quat(0, 0, 0, 1), new Vec3(55, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral);
        ac.Load.FillFuel();
        ac.ApplyLoad();

        double m0 = ac.MassProperties.MassKg, fuel0 = ac.Load.FuelKg, y0 = ac.Load.Cg.Y;

        for (int i = 0; i < 200 * 180; i++)   // three minutes at 200 Hz
        {
            new SimLoop(ac, 1.0 / 200.0).RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, -1.0));
            // The throttle only reaches the engine once a step has applied it, so check the flow after one.
            if (i == 10) Assert.True(ac.FuelFlowKgPerSec > 0.0, "full throttle and authored tanks, but no fuel flow");
        }

        Assert.False(ac.IsLost(AirframeComponent.WingLeftInner) || ac.IsLost(AirframeComponent.Nose),
                     "the aeroplane broke up, so this is measuring damage rather than fuel burn");
        double burned = fuel0 - ac.Load.FuelKg;
        Assert.True(burned > 1.0, $"only {burned:F2} kg burned in three minutes at full throttle");
        Assert.Equal(m0 - burned, ac.MassProperties.MassKg, 3);
        Assert.True(Math.Abs(ac.Load.Cg.Y - y0) > 1e-4, "the left tank drained first and the CG did not move at all");
    }

    /// <summary>A 160 hp four at cruise should burn something a pilot would recognise: the book is about 34 litres an
    /// hour, which is a shade under 25 kg. This is the BSFC default being sane, not a precise claim.</summary>
    [Fact]
    public void TheDefaultFuelBurnIsInTheRightPartOfTheBook()
    {
        AircraftConfig c = Loaded();
        var ac = new Aircraft(c, Level(), ControlDeflections.Neutral);
        for (int i = 0; i < 400; i++) new SimLoop(ac, 1.0 / 200.0).RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, Lever(0.75)));
        double kgPerHour = ac.FuelFlowKgPerSec * 3600.0;
        Assert.InRange(kgPerHour, 15.0, 40.0);
    }

    /// <summary>ControlInputs.ThrottleLever is -1 full forward .. +1 full aft.</summary>
    private static double Lever(double throttle01) => 1.0 - 2.0 * Math.Clamp(throttle01, 0, 1);

    /// <summary>Once the aeroplane is coming apart, DAMAGE owns the mass tensor. If the loading kept re-deriving it,
    /// the next step would quietly hand the wings' inertia back to an aeroplane that no longer has wings.</summary>
    [Fact]
    public void OnceSomethingBreaksOffTheLoadingStopsTouchingTheMassTensor()
    {
        AircraftConfig c = Loaded();
        var ac = new Aircraft(c, Level(), ControlDeflections.Neutral);
        ac.LoseComponent(AirframeComponent.WingLeftInner);
        MassProperties afterBreak = ac.MassProperties;
        ac.Load.Set("aft", 54);
        ac.ApplyLoad();
        Assert.Equal(afterBreak.MassKg, ac.MassProperties.MassKg, 9);
        Assert.Equal(afterBreak.Ixx, ac.MassProperties.Ixx, 9);
    }

    /// <summary>The envelope's direction, pinned. The body frame is +x FORWARD, so loading the back must move the CG
    /// toward the AFT limit and raise CgFraction toward 1, and loading the nose must do the opposite. The model and
    /// this test both had it backwards at first, and both passed, which is why it is asserted on its own.</summary>
    [Fact]
    public void LoadingTheBackMovesTheCgTowardTheAftLimitNotTheForwardOne()
    {
        var load = new AircraftLoad(Loaded());
        Assert.True(load.HasCgEnvelope, "the example has no usable envelope");
        double f0 = load.CgFraction;
        Assert.InRange(f0, 0.0, 1.0);

        load.Set("rearR", 100);
        Assert.True(load.Cg.X < 0.0, "a rear seat should pull the CG aft, which is -x");
        Assert.True(load.CgFraction > f0, $"CgFraction went {f0:F3} -> {load.CgFraction:F3} loading the BACK");

        var nose = new AircraftLoad(Loaded());
        nose.Set("pilot", 140); nose.Set("copilot", 140);
        Assert.True(nose.CgFraction < f0, $"CgFraction went {f0:F3} -> {nose.CgFraction:F3} loading the FRONT");
    }

    // ---- the authored fleet, as shipped ----

    /// <summary>Every aeroplane that DOES author a loading block must still fly exactly as authored at its reference
    /// loading. This is the test that protects the tuned book numbers as the fleet gets authored one type at a time:
    /// it needs no per-type expected values, only the invariant.</summary>
    [Fact]
    public void EveryAuthoredLoadingBlockReproducesItsOwnAeroplaneExactly()
    {
        int authored = 0;
        foreach (string id in Fleet)
        {
            AircraftConfig c = Load(id);
            var load = new AircraftLoad(c);
            if (load.Slots.Count == 0) continue;            // nothing authored for this type yet
            authored++;
            Assert.False(load.Degenerate, $"{id}: the loading block does not add up against mass.massKg");
            MassProperties p = load.Properties;
            Assert.Equal(c.Mass.MassKg, p.MassKg, 6);
            Assert.Equal(c.Mass.Cg[0], load.Cg.X, 8);
            Assert.Equal(c.Mass.Cg[1], load.Cg.Y, 8);
            Assert.Equal(c.Mass.Cg[2], load.Cg.Z, 8);
            Assert.Equal(c.Mass.Inertia.Ixx, p.Ixx, 5);
            Assert.Equal(c.Mass.Inertia.Iyy, p.Iyy, 5);
            Assert.Equal(c.Mass.Inertia.Izz, p.Izz, 5);
            Assert.Equal(c.Mass.Inertia.Ixz, p.Ixz, 5);
            // Every station must be somewhere an aeroplane actually is, not at the origin by omission.
            foreach (var slot in load.Slots)
            {
                Assert.True(slot.MaxKg > 0, $"{id}/{slot.Id} has no placard");
                Assert.InRange(slot.Station.X, -8.0, 8.0);
                Assert.InRange(slot.Station.Y, -8.0, 8.0);
                Assert.InRange(slot.Station.Z, -3.0, 3.0);
            }
        }
        Assert.True(authored > 0, "no aeroplane in the fleet authors a loading block, so this proves nothing");
    }

    /// <summary>The 172's authored stations, sanity-checked against the aeroplane's own geometry rather than against
    /// numbers copied back out of the file: the rear seats behind the front ones, the baggage behind those, the tanks
    /// out in the wings and above the cabin, because it is a high wing.</summary>
    [Fact]
    public void TheSkyhawksStationsSitWhereThatAeroplanesPartsAre()
    {
        AircraftConfig c = Load("c172-like");
        var load = new AircraftLoad(c);
        Assert.NotEmpty(load.Slots);

        double front = load.Find("pilot")!.Station.X;
        double rear = load.Find("rearL")!.Station.X;
        double bags = load.Find("aft")!.Station.X;
        Assert.True(rear < front, "the rear seats must be aft of the front ones (-x is aft)");
        Assert.True(bags < rear, "the baggage locker must be aft of the rear seats");

        // The wing: tanks inside the semi-span, and ABOVE the cabin floor, which is -z.
        double semiSpan = 0.0;
        foreach (SurfaceConfig sf in c.Surfaces)
            if (sf.Id.ToLowerInvariant().Contains("wing"))
                foreach (StripConfig st in sf.Strips) semiSpan = Math.Max(semiSpan, Math.Abs(st.Pos[1]));
        var tank = load.Find("left")!;
        Assert.True(Math.Abs(tank.Station.Y) < semiSpan, $"a tank at y={tank.Station.Y:F2} is outside the {semiSpan:F2} m semi-span");
        Assert.True(Math.Abs(tank.Station.Y) > semiSpan * 0.2, "a wing tank should be out in the wing, not at the root");
        Assert.True(tank.Station.Z < load.Find("pilot")!.Station.Z, "the high wing's tanks are above the seats (-z is up)");

        // 40 US gallons usable is about 109 kg of avgas.
        Assert.InRange(load.FuelCapacityKg, 95.0, 120.0);
    }
}
