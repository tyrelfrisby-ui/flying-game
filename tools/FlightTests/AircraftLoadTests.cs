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
// FLIES an aeroplane, so it READS the global ground and weather (WorldTerrain.Active, Atmosphere.*,
// FloatHydro) through the sim even though it sets none of them. The collection was protecting the
// classes that WRITE that state from each other and leaving their victims outside it, so this class ran
// in parallel with 68 that install worlds and weather: StructuralTests went 39 of 40 NaN in one run and
// passed clean in the next on identical code (2026-11-01). A reader needs the same serialisation as a
// writer.
[Collection("WorldTerrainActive")]
public sealed class AircraftLoadTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static readonly string[] Fleet =
    {
        "c172-like", "pitts-s2b-like", "extra-300-like", "dc3-like", "p51d-like", "pa18-cub-like",
        "glider-2-33-like", "boeing-737-like", "pa18-floats-like", "cassutt-f1-like", "stearman-pt17-like",
        // The types that got published arms on 2026-11-01 (see ArmTable below).
        "great-lakes-like", "j3-cub-like", "j3-cub-floats-like", "glider-swift-s1-like",
    };

    /// <summary>Every type that authors a loading block, so the arm tests below cover the lot without naming them twice.</summary>
    private static readonly string[] Authored =
    {
        "c172-like", "extra-300-like", "glider-2-33-like", "glider-swift-s1-like",
        "great-lakes-like", "j3-cub-floats-like", "j3-cub-like", "pitts-s2b-like",
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

    // ---- the published arms (docs/pending/loading-arm-estimates.md, Codex lane ee088db, wired 2026-11-01) --------

    /// <summary>
    /// The arms, pinned. Every row here is the table's <c>primary_body_m</c>: a published TCDS/POH arm converted
    /// through that aircraft's anchor where the row is CONFIRMED, our seated-mannequin or geometry estimate where it
    /// is ESTIMATED (the Swift's pilot is the only one of those). Body frame, +x FORWARD, +y RIGHT, +z DOWN.
    ///
    /// Why the numbers are repeated here rather than read back out of the config: a test that reads the file it is
    /// checking passes whatever the file says. These came off the table by hand, so an edit to a config that moves a
    /// station has to be a deliberate edit here too.
    ///
    /// Two deliberate departures from the table's own y, both flagged in AGENT-LOG: the Great Lakes' 26 gal top-wing
    /// tank and the Pitts' 5 gal upper-wing tank are single CENTRE-SECTION tanks in their type certificates
    /// ("one 26.0 gal. tank in top wing"), and the table's lateral figure for them came from the wing-tank heuristic,
    /// not the source — so they sit on the centreline and only x and z are the table's.
    /// </summary>
    public static TheoryData<string, string, double, double, double> ArmTable => new()
    {
        // 172: already carried its POH arms; only baggage area 2 (sta 123) was missing.
        { "c172-like",            "pilot",     0.0260, -0.2800,  0.0500 },
        { "c172-like",            "copilot",   0.0260,  0.2800,  0.0500 },
        { "c172-like",            "rearL",    -0.8880, -0.2800,  0.0500 },
        { "c172-like",            "rearR",    -0.8880,  0.2800,  0.0500 },
        { "c172-like",            "left",     -0.2530, -2.1800, -0.5500 },
        { "c172-like",            "right",    -0.2530,  2.1800, -0.5500 },
        { "c172-like",            "aft",      -1.4470,  0.0000,  0.1000 },
        { "c172-like",            "aft2",     -2.1584,  0.0000,  0.1000 },
        // Extra 300: POH p.6-8 moment table, anchored on the firewall (MED - the anchor is soft by +-0.05 m).
        { "extra-300-like",       "rear",     -1.2801,  0.0000, -0.1043 },
        { "extra-300-like",       "front",    -0.1899,  0.0000, -0.0296 },
        { "extra-300-like",       "centre",    0.4921,  0.0000, -0.1296 },
        { "extra-300-like",       "wingLeft",  0.2891, -1.1500, -0.0700 },
        { "extra-300-like",       "wingRight", 0.2891,  1.1500, -0.0700 },
        // SGS 2-33: seats only, a glider has neither fuel nor a baggage placard.
        { "glider-2-33-like",     "front",     1.0342,  0.0000,  0.4092 },
        { "glider-2-33-like",     "rear",      0.2468,  0.0000,  0.4709 },
        // Swift S-1: ESTIMATED, the seated mannequin. No published sheet was found for this type.
        { "glider-swift-s1-like", "pilot",     0.8882,  0.0000,  0.1283 },
        // Great Lakes 2T-1A-2: TCDS A18EA.
        { "great-lakes-like",     "rear",     -1.7621,  0.0000,  0.0007 },
        { "great-lakes-like",     "front",    -0.8985,  0.0000,  0.0845 },
        { "great-lakes-like",     "topWing",  -0.5302,  0.0000, -0.6900 },
        { "great-lakes-like",     "header",   -0.3270,  0.0000, -0.2155 },
        { "great-lakes-like",     "aft",      -2.5368,  0.0000,  0.0007 },
        { "great-lakes-like",     "headrest", -2.3336,  0.0000,  0.0000 },
        // J-3: TCDS A-691, seats +9 / +36, baggage +49, fuel -18 off the wing leading edge.
        { "j3-cub-like",          "rear",     -0.5071,  0.0000,  0.0342 },
        { "j3-cub-like",          "front",     0.1787,  0.0000, -0.0713 },
        { "j3-cub-like",          "nose",      0.8645,  0.0000, -0.4213 },
        { "j3-cub-like",          "baggage",  -0.8373,  0.0000,  0.0342 },
        // On floats the same arms, with the whole aeroplane carried higher: only z differs.
        { "j3-cub-floats-like",   "rear",     -0.5071,  0.0000,  0.1346 },
        { "j3-cub-floats-like",   "front",     0.1787,  0.0000,  0.0291 },
        { "j3-cub-floats-like",   "nose",      0.8645,  0.0000, -0.3209 },
        { "j3-cub-floats-like",   "baggage",  -0.8373,  0.0000,  0.1346 },
        // Pitts S-2B: TCDS A8SO.
        { "pitts-s2b-like",       "rear",     -1.1351,  0.0000,  0.1503 },
        { "pitts-s2b-like",       "front",    -0.3388,  0.0000,  0.1895 },
        { "pitts-s2b-like",       "main",      0.2665,  0.0000, -0.0605 },
        { "pitts-s2b-like",       "aux",       0.2555,  0.0000, -0.6200 },
        { "pitts-s2b-like",       "baggage",  -1.6764,  0.0000,  0.1503 },
    };

    [Theory]
    [MemberData(nameof(ArmTable))]
    public void EveryAuthoredStationCarriesItsPublishedArm(string id, string station, double x, double y, double z)
    {
        var load = new AircraftLoad(Load(id));
        AircraftLoad.Slot? slot = load.Find(station);
        Assert.True(slot is not null, $"{id} has no station '{station}'");
        Assert.Equal(x, slot!.Station.X, 3);
        Assert.Equal(y, slot.Station.Y, 3);
        Assert.Equal(z, slot.Station.Z, 3);
    }

    /// <summary>A station outside the aeroplane is an arm converted through the wrong datum, which is the failure
    /// this whole table exists to risk. Checked against the airframe's OWN geometry: the fuselage's longitudinal
    /// extent, the wing's semi-span, and a couple of metres either side of the reference plane.</summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredType))]
    public void EveryAuthoredStationSitsInsideItsOwnAirframe(string id)
    {
        AircraftConfig c = Load(id);
        var load = new AircraftLoad(c);
        Assert.NotEmpty(load.Slots);

        CrossflowConfig x = c.Fuselage.Crossflow;
        Assert.True(x.LengthM > 0.5, $"{id}: no fuselage length authored, so there is nothing to check an arm against");
        double nose = x.PlanCenterX + 0.5 * x.LengthM, tail = x.PlanCenterX - 0.5 * x.LengthM;
        double semiSpan = 0.0;
        foreach (SurfaceConfig sf in c.Surfaces)
            if (sf.Id.ToLowerInvariant().Contains("wing"))
                foreach (StripConfig st in sf.Strips) semiSpan = Math.Max(semiSpan, Math.Abs(st.Pos[1]));

        foreach (AircraftLoad.Slot s in load.Slots)
        {
            Assert.True(s.Station.X <= nose + 0.05 && s.Station.X >= tail - 0.05,
                $"{id}/{s.Id} at x={s.Station.X:F3} is outside the fuselage ({tail:F3}..{nose:F3} m) — a datum conversion gone wrong");
            Assert.True(Math.Abs(s.Station.Y) <= semiSpan,
                $"{id}/{s.Id} at y={s.Station.Y:F3} is outboard of the {semiSpan:F3} m semi-span");
            Assert.InRange(s.Station.Z, -2.0, 2.0);
        }
    }

    /// <summary>The arms moved; the aeroplane must not. AircraftLoad solves the empty airframe backwards, so moving a
    /// station re-solves the empty CG and the reference loading still lands exactly on the authored mass, CG and
    /// inertia — which is what keeps every tuned book speed in this suite valid across a W&amp;B edit.</summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredType))]
    public void MovingTheArmsLeavesTheAeroplaneExactlyAsAuthored(string id)
    {
        AircraftConfig c = Load(id);
        var load = new AircraftLoad(c);
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
    }

    /// <summary>A tandem flies solo from the BACK, and every one of these is a centreline aeroplane: the rear station
    /// must be aft of the front one, and neither may sit off to one side. Swapping the two reads as a plausible
    /// loading sheet until the aeroplane is flown out of balance.</summary>
    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    [InlineData("great-lakes-like")]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("glider-2-33-like")]
    public void TheTandemsRearStationIsBehindItsFrontOneAndBothAreOnTheCentreline(string id)
    {
        var load = new AircraftLoad(Load(id));
        double rear = load.Find("rear")!.Station.X, front = load.Find("front")!.Station.X;
        Assert.True(rear < front, $"{id}: the rear seat station must be aft of the front one (-x is aft)");
        foreach (AircraftLoad.Slot s in load.Slots)
            if (s.Kind == AircraftLoad.StationKind.Seat)
                Assert.Equal(0.0, s.Station.Y, 9);
    }

    /// <summary>Fuel and baggage went in with referenceKg 0, so the aeroplane still spawns at exactly the mass it was
    /// authored with and a type that never burned fuel still never does. Filling the tanks must ADD mass, not
    /// rearrange it — the open W&amp;B decision in AGENT-LOG is about which of those is right.</summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredType))]
    public void TheNewTanksAndBaggageAreEmptyAtSpawn(string id)
    {
        var load = new AircraftLoad(Load(id));
        double spawn = load.TotalMassKg;
        foreach (AircraftLoad.Slot s in load.Slots)
        {
            if (s.Kind == AircraftLoad.StationKind.Baggage) Assert.Equal(0.0, s.Kg, 9);
            if (s.Kind == AircraftLoad.StationKind.Fuel && id != "c172-like") Assert.Equal(0.0, s.Kg, 9);
        }
        load.FillFuel();
        Assert.True(load.TotalMassKg >= spawn - 1e-9, $"{id}: filling the tanks took mass OUT of the aeroplane");
    }

    public static TheoryData<string> EveryAuthoredType
    {
        get { var d = new TheoryData<string>(); foreach (string id in Authored) d.Add(id); return d; }
    }
}
