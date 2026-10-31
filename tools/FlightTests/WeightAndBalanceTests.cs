using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Loading;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Weight and balance (owner 2026-10-30: "load the airplane with occupants, luggage, or cargo and fuel, and the
/// weight and balance is plotted and the flight characteristics are adjusted appropriately").
///
/// Two things have to be true for this to be worth having. The arithmetic must be right, which these tests check
/// against hand-computable cases and against the published figures the data came from; and loading the aeroplane
/// must actually change how it flies, which the static-margin test checks by measuring it.
/// </summary>
public class WeightAndBalanceTests
{
    private readonly ITestOutputHelper _o;
    public WeightAndBalanceTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static string[] LoadableTypes() => Directory
        .GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(id => LoadSheet.Loadable(Load(id!)))
        .OrderBy(id => id)
        .ToArray()!;

    /// <summary>
    /// The guarantee that makes it safe to add a loading block to a type that was already calibrated against its
    /// published stall speeds and handling: at its DEFAULT load, the aeroplane is the one the project has been
    /// flying all along — the same mass, the same CG in all three axes, the same inertia. The default station loads
    /// were derived backwards from exactly these numbers, so if this drifts, the data is wrong and not the physics.
    /// </summary>
    [Fact]
    public void TheDefaultLoadIsTheAeroplaneWeHaveBeenFlying()
    {
        Assert.NotEmpty(LoadableTypes());
        foreach (string id in LoadableTypes())
        {
            AircraftConfig c = Load(id);
            // Captured BEFORE the load sheet exists, because constructing one applies the default load.
            double mass = c.Mass.MassKg;
            Vec3 cg = c.Mass.CgVec();
            InertiaConfig i = c.Mass.Inertia;

            var sheet = new LoadSheet(c);
            WeightAndBalance.Result r = sheet.Compute();
            _o.WriteLine($"{id}: {mass:F1} kg → {r.MassKg:F3} kg, cg {cg.X:+0.000;-0.000} → {r.Cg.X:+0.000;-0.000} m, "
                         + $"Ixx {i.Ixx:F0} → {r.Ixx:F0}, Iyy {i.Iyy:F0} → {r.Iyy:F0}, Izz {i.Izz:F0} → {r.Izz:F0}, Ixz {i.Ixz:F1} → {r.Ixz:F1}");

            Assert.Equal(mass, r.MassKg, 2);              // within 10 g
            Assert.Equal(cg.X, r.Cg.X, 4);                // within 0.1 mm
            Assert.Equal(cg.Y, r.Cg.Y, 4);
            Assert.Equal(cg.Z, r.Cg.Z, 4);
            Assert.Equal(i.Ixx, r.Ixx, 1);
            Assert.Equal(i.Iyy, r.Iyy, 1);
            Assert.Equal(i.Izz, r.Izz, 1);
            Assert.Equal(i.Ixz, r.Ixz, 1);
        }
    }

    /// <summary>
    /// The MAC this project measures from the strips must be the MAC on the type certificate, because every
    /// percent-MAC figure — the whole envelope — is built on it. The 172's type certificate says 58.8 in; the J-3's
    /// mean chord from its published span and area is 61 in.
    /// </summary>
    [Theory]
    [InlineData("c172-like", 58.8)]
    [InlineData("j3-cub-like", 61.0)]
    public void TheMeasuredMacIsTheTypesOwnMac(string id, double publishedInches)
    {
        double got = WeightAndBalance.MeanAerodynamicChord(Load(id)) / 0.0254;
        _o.WriteLine($"{id}: measured MAC {got:F1} in, published {publishedInches:F1} in ({(got / publishedInches - 1) * 100:+0.0;-0.0} %)");
        Assert.InRange(got, publishedInches * 0.96, publishedInches * 1.04);
    }

    /// <summary>Percent MAC and the body-x arm are the same statement, so the round trip must come back exactly.</summary>
    [Fact]
    public void PercentMacAndTheArmAreTheSameStatement()
    {
        AircraftConfig c = Load("c172-like");
        foreach (double pct in new[] { 0.0, 15.0, 27.0, 36.4, 100.0 })
            Assert.Equal(pct, WeightAndBalance.PercentMac(c, WeightAndBalance.ArmForPercentMac(c, pct)), 9);
        // 0 % is the MAC leading edge and 100 % its trailing edge, a MAC further aft.
        Assert.True(WeightAndBalance.ArmForPercentMac(c, 0) > WeightAndBalance.ArmForPercentMac(c, 100));
        Assert.Equal(WeightAndBalance.MeanAerodynamicChord(c),
                     WeightAndBalance.ArmForPercentMac(c, 0) - WeightAndBalance.ArmForPercentMac(c, 100), 9);
    }

    /// <summary>Every type that can be loaded starts legal: inside its CG envelope and not over gross.</summary>
    [Fact]
    public void EveryLoadedTypeStartsLegal()
    {
        foreach (string id in LoadableTypes())
        {
            AircraftConfig c = Load(id);
            var sheet = new LoadSheet(c);
            (double fwd, double aft) = WeightAndBalance.LimitsAt(sheet.Loading, sheet.Compute().MassKg);
            _o.WriteLine($"{id}: {sheet.Compute().MassKg:F0} kg at {sheet.PercentMac:F1} %MAC, limits {fwd:F1}..{aft:F1} %MAC, max gross {sheet.Loading.MaxTakeoffMassKg:F0} kg");
            Assert.False(sheet.OverGross, $"{id} starts over gross");
            Assert.True(sheet.InEnvelope, $"{id} starts outside its CG envelope at {sheet.PercentMac:F1} %MAC");
        }
    }

    /// <summary>
    /// Parallel axis, against a case anyone can do by hand: a bare 100 kg aeroplane with no inertia of its own,
    /// carrying 100 kg one metre forward, has Iyy = Izz = 2 x 100 x 0.5^2 = 50 about the CG that sits between them,
    /// and no Ixx at all because nothing is off the x axis.
    /// </summary>
    [Fact]
    public void ParallelAxisIsTheTextbookOne()
    {
        var loading = new LoadingConfig { EmptyMassKg = 100, EmptyCg = new double[] { 0, 0, 0 } };
        WeightAndBalance.Result r = WeightAndBalance.Solve(loading, new[] { (new Vec3(1, 0, 0), 100.0) });
        Assert.Equal(200.0, r.MassKg, 9);
        Assert.Equal(0.5, r.Cg.X, 9);
        Assert.Equal(0.0, r.Ixx, 9);
        Assert.Equal(50.0, r.Iyy, 9);
        Assert.Equal(50.0, r.Izz, 9);
        Assert.Equal(0.0, r.Ixz, 9);

        // And the product of inertia has the sign RigidBody6DOF is written for: Ixz = integral of x*z dm, so a mass
        // low (z + down) and ahead of the CG contributes positively.
        WeightAndBalance.Result q = WeightAndBalance.Solve(
            new LoadingConfig { EmptyMassKg = 1e-9, EmptyCg = new double[] { 0, 0, 0 } },
            new[] { (new Vec3(2, 0, 3), 10.0) });
        Assert.True(q.Ixz > 0, $"a mass low and forward should give positive Ixz, got {q.Ixz:F3}");
    }

    /// <summary>
    /// Burning fuel moves the CG, which is half of why weight and balance matters in flight rather than only on the
    /// ramp. The 172's tanks are at station 48, aft of its CG, so running them dry moves the CG FORWARD — and the
    /// two wing tanks must drain together, or the aeroplane would slowly roll off on its own.
    /// </summary>
    [Fact]
    public void BurningFuelMovesTheCgForwardAndKeepsTheWingsBalanced()
    {
        AircraftConfig c = Load("c172-like");
        var sheet = new LoadSheet(c);
        double fullMass = sheet.Compute().MassKg, fullCgX = sheet.Compute().Cg.X, fuel = sheet.TotalFuelKg;
        Assert.True(fuel > 100, $"the 172 should start with 40 gal, got {fuel:F1} kg");

        double burned = sheet.BurnFuelKg(0.5 * fuel);
        WeightAndBalance.Result half = sheet.Compute();
        Assert.Equal(0.5 * fuel, burned, 6);
        Assert.Equal(fullMass - burned, half.MassKg, 6);
        Assert.Equal(0.0, half.Cg.Y, 9);                  // still symmetric
        Assert.True(half.Cg.X > fullCgX, "the tanks are aft of the CG, so burning fuel moves it forward");

        // Run them dry, and ask for more than there is.
        double rest = sheet.BurnFuelKg(fuel);
        Assert.Equal(0.5 * fuel, rest, 4);
        Assert.Equal(0.0, sheet.TotalFuelKg, 4);
        Assert.Equal(fullMass - fuel, sheet.Compute().MassKg, 4);
        _o.WriteLine($"c172: {fullMass:F0} kg at {WeightAndBalance.PercentMac(c, fullCgX):F1} %MAC full of fuel, "
                     + $"{sheet.Compute().MassKg:F0} kg at {sheet.PercentMac:F1} %MAC dry");

        // One tank only, and the aeroplane is laterally out of balance — which is the point of tracking y at all.
        sheet.Reset();
        sheet.SetTankKg("right", 0);
        Assert.True(sheet.Compute().Cg.Y < -0.05, $"a full left tank alone should pull the CG left, got {sheet.Compute().Cg.Y:F3} m");
    }

    /// <summary>A station holds what it holds, and the aeroplane will not carry more than it is certificated for.</summary>
    [Fact]
    public void StationsClampAndOverGrossIsCaught()
    {
        AircraftConfig c = Load("c172-like");
        var sheet = new LoadSheet(c);
        Assert.Equal(54.43, sheet.SetStation("baggage1", 500), 2);   // 120 lb, the POH limit
        Assert.Equal(0.0, sheet.SetStation("baggage1", -40), 9);
        Assert.Equal(0.0, sheet.SetStation("no-such-station", 50), 9);

        sheet.Reset();
        sheet.SetStation("baggage1", 54.43);
        sheet.SetStation("baggage2", 22.68);
        Assert.True(sheet.OverGross, "a full aeroplane plus 170 lb of bags is over gross and should say so");
        Assert.False(sheet.InEnvelope);
    }

    /// <summary>
    /// The one that answers the owner's actual request. Load an aeroplane aft and it must become less stable in
    /// pitch, by the amount the CG moved — a loading mistake you can feel, not a number on a screen. Measured with
    /// the same probe that holds every type to a 5–20 % MAC static margin.
    /// </summary>
    [Fact]
    public void LoadingItAftCostsExactlyThatMuchStaticMargin()
    {
        AircraftConfig c = Load("c172-like");
        var sheet = new LoadSheet(c);
        double pctBefore = sheet.PercentMac;
        (double smBefore, _, _, _) = PitchStabilityTests.Measure(c);

        // Move the two front occupants into the back seats — the same mass at the same height, so only the
        // fore-and-aft arm changes and the comparison is clean. (Moving mass DOWN as well would also shift the
        // neutral point a little, because drag then acts further from the CG, which is real but is not what this
        // test is measuring.)
        double moved = sheet.StationKg("front-left") + sheet.StationKg("front-right");
        sheet.SetStation("front-left", 0);
        sheet.SetStation("front-right", 0);
        sheet.SetStation("rear-left", 0.5 * moved);
        sheet.SetStation("rear-right", 0.5 * moved);

        double pctAfter = sheet.PercentMac;
        (double smAfter, _, _, _) = PitchStabilityTests.Measure(c);
        _o.WriteLine($"c172: {moved:F0} kg moved from the front seats aft — CG {pctBefore:F1} → {pctAfter:F1} %MAC, "
                     + $"static margin {smBefore * 100:F1} → {smAfter * 100:F1} % MAC");

        Assert.Equal(sheet.Compute().MassKg, c.Mass.MassKg, 6);      // the config is what the physics reads
        Assert.True(pctAfter > pctBefore + 8, $"the CG should have moved a long way aft, {pctBefore:F1} → {pctAfter:F1}");

        // Static margin is the distance from the CG to the neutral point, so moving the CG aft by x percent of MAC
        // must take x percent of MAC off it. Anything else and the CG is not reaching the aerodynamics. The third
        // of a percent of slack is the finite-difference residual: the probe takes Cmα between 1 and 5 degrees, and
        // the fuselage's own pitching moment is not quite linear across that.
        double lost = (smBefore - smAfter) * 100, movedAft = pctAfter - pctBefore;
        _o.WriteLine($"       CG moved {movedAft:F2} %MAC aft, static margin lost {lost:F2} %MAC");
        Assert.InRange(lost - movedAft, -0.35, 0.35);
        Assert.False(sheet.InEnvelope, "and that load is outside the envelope, which is the lesson");
    }

    /// <summary>
    /// Why a Cub is soloed from the back seat. The two seats are at stations +9 and +36 on a 61-inch chord, so
    /// which one the pilot sits in is worth nearly half the whole CG range.
    /// </summary>
    [Fact]
    public void WhichSeatTheCubsPilotSitsInMovesTheCgHalfItsRange()
    {
        AircraftConfig c = Load("j3-cub-like");
        var sheet = new LoadSheet(c);
        (double fwd, double aft) = WeightAndBalance.LimitsAt(sheet.Loading, 0.9 * sheet.Loading.MaxTakeoffMassKg);

        sheet.Reset();
        sheet.SetStation("rear", 0);
        double front = sheet.PercentMac;
        sheet.Reset();
        sheet.SetStation("front", 0);
        double rear = sheet.PercentMac;
        _o.WriteLine($"J-3 solo: front seat {front:F1} %MAC, rear seat {rear:F1} %MAC; envelope {fwd:F1}..{aft:F1} %MAC");

        Assert.True(rear > front, "the rear seat is aft of the front one");
        Assert.True(rear - front > 0.4 * (aft - fwd), $"only {rear - front:F1} %MAC apart out of a {aft - fwd:F1} %MAC range");
    }

    /// <summary>
    /// The check that validates each type's datum mapping, and the reason a type is only given an envelope once it
    /// passes: where the certificate's limits are written into this project's frame, the aeroplane as tuned must
    /// land INSIDE them. Three independent types agreeing is what says the method is right — and it is what caught
    /// `pa28-archer-like`, whose POH limits map to a forward limit at 0.3 % MAC, on the MAC leading edge, which no
    /// aeroplane has. That one is left out rather than fudged (docs/WEIGHT-AND-BALANCE.md).
    /// </summary>
    [Fact]
    public void EveryTypeWithAnEnvelopeIsTunedInsideIt()
    {
        string[] ids = Directory
            .GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => LoadSheet.HasEnvelope(Load(id!)))
            .OrderBy(id => id)
            .ToArray()!;
        Assert.True(ids.Length >= 5, $"only {ids.Length} types have a CG envelope");

        foreach (string id in ids)
        {
            AircraftConfig c = Load(id);
            double pct = WeightAndBalance.PercentMac(c, c.Mass.CgVec().X);
            (double fwd, double aft) = WeightAndBalance.LimitsAt(c.Loading!, c.Mass.MassKg);
            bool loadable = LoadSheet.Loadable(c);
            _o.WriteLine($"{id,-22} {c.Mass.MassKg,7:F0} kg  tuned {pct,5:F1} %MAC  limits {fwd,5:F1}..{aft,5:F1}"
                         + $"  gross {c.Loading!.MaxTakeoffMassKg,7:F0} kg  {(loadable ? "loadable" : "limits only")}");
            Assert.True(WeightAndBalance.InEnvelope(c.Loading!, pct, c.Mass.MassKg),
                $"{id} is tuned at {pct:F1} %MAC and {c.Mass.MassKg:F0} kg, outside its own certificated envelope "
                + $"({fwd:F1}..{aft:F1} %MAC at that weight) — the datum mapping or the tuned CG is wrong");
            Assert.True(c.Mass.MassKg <= c.Loading!.MaxTakeoffMassKg + 1e-6,
                $"{id} is tuned above its own max take-off weight");
        }
    }

    /// <summary>
    /// What the published data says about each type as it is tuned today, printed rather than asserted. The gap
    /// between the tuned CG and where a realistic load at that weight would actually put it is a question for the
    /// owner, not something to fix silently: closing it would retune pitch stability on a calibrated aeroplane.
    /// </summary>
    [Fact]
    public void WhereTheTunedCgSitsInTheRealEnvelope()
    {
        foreach (string id in LoadableTypes())
        {
            AircraftConfig c = Load(id);
            double pct = WeightAndBalance.PercentMac(c, c.Mass.CgVec().X);
            var sheet = new LoadSheet(c);
            (double fwd, double aft) = WeightAndBalance.LimitsAt(sheet.Loading, c.Mass.MassKg);
            double through = (pct - fwd) / (aft - fwd) * 100;
            _o.WriteLine($"{id,-16} tuned {pct:F1} %MAC in a {fwd:F1}..{aft:F1} envelope ({through:F0} % of the way aft); "
                         + $"empty {sheet.Loading.EmptyMassKg:F0} kg of {c.Mass.MassKg:F0} kg, "
                         + $"payload {c.Mass.MassKg - sheet.Loading.EmptyMassKg - sheet.UsableFuelCapacityKg:F0} kg with full tanks");
            Assert.InRange(through, 0, 100);
        }
    }
}
