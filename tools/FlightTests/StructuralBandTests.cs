using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The owner's 2026-10-31 call: "make the wings pop and groan above 1.5 limit load but don't have the wings actually
/// break off until 2.0 limit load." Three thresholds, not two — the rated limit stays where certification puts it
/// and the syllabus still grades against it, the noise starts at 1.5 x limit, the wings go at 2.0 x limit.
/// </summary>
// FLIES an aeroplane, so it READS the global ground and weather (WorldTerrain.Active, Atmosphere.*,
// FloatHydro) through the sim even though it sets none of them. The collection was protecting the
// classes that WRITE that state from each other and leaving their victims outside it, so this class ran
// in parallel with 68 that install worlds and weather: StructuralTests went 39 of 40 NaN in one run and
// passed clean in the next on identical code (2026-11-01). A reader needs the same serialisation as a
// writer.
[Collection("WorldTerrainActive")]
public sealed class StructuralBandTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static StructuralState State(double gMax = 4.0, double gMin = -2.0) =>
        new Aircraft(Bare(gMax, gMin),
            new RigidBodyState(new Vec3(0, 0, -1000), new Quat(0, 0, 0, 1), new Vec3(50, 0, 0), Vec3.Zero),
            ControlDeflections.Neutral).Structure;

    private static AircraftConfig Bare(double gMax, double gMin)
    {
        AircraftConfig c = Load("c172-like");
        c.Limits.GMax = gMax; c.Limits.GMin = gMin;
        return c;
    }

    [Fact]
    public void TheThresholdsAreTheOnesTheOwnerAskedFor()
    {
        StructuralState st = State(gMax: 4.0, gMin: -2.0);
        Assert.Equal(4.0, st.LimitPosG, 6);
        Assert.Equal(6.0, st.GroanPosG, 6);        // 1.5 x limit
        Assert.Equal(8.0, st.UltimatePosG, 6);     // 2.0 x limit
        Assert.Equal(-2.0, st.LimitNegG, 6);
        Assert.Equal(-3.0, st.GroanNegG, 6);
        Assert.Equal(-4.0, st.UltimateNegG, 6);
    }

    /// <summary>The band between the rated limit and the groan threshold is SILENT but still an exceedance: the
    /// structure is past what it is rated for and the syllabus has to know, even though the pilot hears nothing.</summary>
    [Theory]
    [InlineData(1.0, false, false)]
    [InlineData(3.9, false, false)]
    [InlineData(4.1, true, false)]     // past the rated limit, nothing audible
    [InlineData(5.9, true, false)]
    [InlineData(6.1, true, true)]      // past 1.5 x limit: it starts to complain
    [InlineData(7.9, true, true)]
    public void BetweenTheLimitAndTheGroanThresholdItIsAnExceedanceButSaysNothing(double n, bool overLimit, bool audible)
    {
        StructuralState st = State();
        Assert.Equal(overLimit, st.OverLimit(n));
        Assert.Equal(audible, st.SeverityFor(n) > 0.0);
    }

    [Fact]
    public void SeverityRunsFromNothingAtTheGroanThresholdToFullAtUltimate()
    {
        StructuralState st = State();
        Assert.Equal(0.0, st.SeverityFor(6.0), 6);
        Assert.Equal(0.5, st.SeverityFor(7.0), 6);
        Assert.Equal(1.0, st.SeverityFor(8.0), 6);
        Assert.Equal(1.0, st.SeverityFor(99.0), 6);    // clamped, not extrapolated
        Assert.Equal(0.0, st.SeverityFor(-3.0), 6);
        Assert.Equal(1.0, st.SeverityFor(-4.0), 6);
    }

    /// <summary>The pops are the "pop" half of the request: discrete events, rising in rate with severity, and
    /// deterministic so this can count them at all.</summary>
    [Fact]
    public void ItPopsFasterTheHarderItIsPulledAndNotAtAllInsideTheBand()
    {
        Assert.Equal(0.0, StructuralState.PopsPerSec(0.0), 6);
        double justOver = StructuralState.PopsPerSec(0.01), atUltimate = StructuralState.PopsPerSec(1.0);
        Assert.True(justOver > 0.5, $"only {justOver:F2} pops/s just past the threshold");
        Assert.True(atUltimate > justOver * 4, $"{justOver:F2} -> {atUltimate:F2} is not much of a rise");
        Assert.Equal(atUltimate, StructuralState.PopsPerSec(5.0), 6);   // clamped
    }

    /// <summary>Flown, not asserted on the arithmetic: hold a load factor for a second at each level and count what
    /// the airframe actually did.</summary>
    [Theory]
    [InlineData(5.0, false, false)]    // past the 4 g limit, below the 6 g groan: quiet and intact
    [InlineData(7.0, true, false)]     // groaning and popping, still intact
    // 9 g is 2.25 × the 4 g limit, snatched from level flight. The airframe is through the whole complaining band
    // (1.5 × to 2.0 ×) in 55 ms, which at 1.5–12 pops per second does not earn a whole pop, and then the wings are gone
    // and a wing that is not there does not groan. It used to count pops here only because a BROKEN member went on
    // reporting full severity for the rest of the second — the aeroplane itself never did, because FailWings zeroes
    // OverLimitSeverity, and since the staggered failure (2026-11-10) each wing's severity stops with that wing.
    [InlineData(9.0, false, true)]     // past 8 g ultimate: wings, too fast to have popped on the way
    public void HeldAtALoadFactorTheAirframeDoesTheRightThing(double n, bool expectPops, bool expectFailure)
    {
        AircraftConfig c = Bare(4.0, -2.0);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(0, 0, 0, 1), new Vec3(70, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral);
        // Drive the structural bookkeeping directly at a held load factor: this is about the thresholds, not about
        // whether a 172 can pull 9 g, which it cannot.
        int pops = 0;
        ac.Structure.OnPop += _ => pops++;
        const double dt = 1.0 / 200.0;
        bool failed = false;
        for (int i = 0; i < 200; i++) failed |= ac.Structure.Step(n, dt);   // one second
        Assert.Equal(expectPops, pops > 0);
        if (expectPops && !expectFailure) Assert.True(pops >= 1 && pops <= 14, $"{pops} pops in a second is not a believable rate");
        Assert.Equal(expectFailure, failed);
    }

    /// <summary>The wings must survive a sustained pull at 1.9 x limit and let go at 2.1 x, which is the whole point
    /// of the owner's two numbers. Driven through the sim's own per-step structural path.</summary>
    [Theory]
    [InlineData(1.9, false)]
    [InlineData(2.1, true)]
    public void TheWingsStayOnBelowTwiceTheLimitAndComeOffAboveIt(double multipleOfLimit, bool expectFailure)
    {
        AircraftConfig c = Bare(4.0, -2.0);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -5000), new Quat(0, 0, 0, 1), new Vec3(80, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral);
        double n = 4.0 * multipleOfLimit;
        bool failed = false;
        for (int i = 0; i < 100; i++) failed |= ac.Structure.Step(n, 1.0 / 200.0);   // half a second, past the dwell
        Assert.Equal(expectFailure, failed);
    }

    /// <summary>Every shipped aeroplane keeps a sane band: ultimate strictly above groan, groan strictly above limit,
    /// and a config that inverts the two factors still gets a band rather than a divide by zero.</summary>
    [Fact]
    public void EveryAeroplaneHasABandAndABadConfigStillGetsOne()
    {
        foreach (string id in new[] { "c172-like", "pitts-s2b-like", "extra-300-like", "dc3-like", "p51d-like",
                                      "pa18-cub-like", "glider-2-33-like", "boeing-737-like" })
        {
            AircraftConfig c = Load(id);
            var st = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1000), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero),
                                  ControlDeflections.Neutral).Structure;
            Assert.True(st.GroanPosG > st.LimitPosG, $"{id}: groan {st.GroanPosG} is not above limit {st.LimitPosG}");
            Assert.True(st.UltimatePosG > st.GroanPosG, $"{id}: ultimate {st.UltimatePosG} is not above groan {st.GroanPosG}");
            Assert.True(st.GroanNegG < st.LimitNegG && st.UltimateNegG < st.GroanNegG, $"{id}: negative band is wrong way round");
        }

        AircraftConfig bad = Load("c172-like");
        bad.Limits.GroanFactor = 3.0; bad.Limits.UltimateFactor = 2.0;   // inverted on purpose
        var s2 = new Aircraft(bad, new RigidBodyState(new Vec3(0, 0, -1000), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral).Structure;
        Assert.True(s2.UltimatePosG > s2.GroanPosG, "an inverted config must still leave a band to groan in");
        Assert.True(double.IsFinite(s2.SeverityFor(s2.GroanPosG + 0.1)));
    }

    /// <summary>End to end through the real sim, no seam: an aeroplane given an absurd 0.2 g limit is past its 0.4 g
    /// ultimate in ordinary level flight, so the wings must actually come off and the sim must keep flying without
    /// them. This is what proves Aircraft still wires the threshold to FailWings.</summary>
    [Fact]
    public void AnAeroplaneFlownPastUltimateReallyLosesItsWingsAndKeepsFlying()
    {
        AircraftConfig c = Bare(0.2, -0.2);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(0, 0, 0, 1), new Vec3(60, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral);
        bool raised = false;
        ac.Structure.OnWingsFailed += () => raised = true;
        for (int i = 0; i < 400; i++) new SimLoop(ac, 1.0 / 200.0).RunFor(1.0 / 200.0, new ControlInputs(0, 0, 0, -1.0));
        Assert.True(ac.Structure.WingsFailed, "1 g against a 0.4 g ultimate did not take the wings off");
        Assert.True(raised, "OnWingsFailed never fired");
        Assert.Equal(0.0, ac.Structure.OverLimitSeverity, 6);          // nothing left to groan
        Assert.True(double.IsFinite(ac.State.Position.Z), "the sim stopped being finite without its wings");
    }
}
