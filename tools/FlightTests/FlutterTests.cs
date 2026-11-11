using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// FLUTTER, flown. <see cref="FlyingGame.Core.Aero.Flutter"/> holds the laws and FlutterLawProbe prints the two
/// shapes that are mine; these pin the mechanism Ty specified — the arming, what excites it, which surface goes,
/// the cumulative accumulator, the two-outcome race, the degraded ultimate strength, the staggered shed and the
/// 1.5 Vd ceiling.
/// </summary>
[Collection("WorldTerrainActive")]
public class FlutterTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public FlutterTests(ITestOutputHelper o) { _o = o; Calm(); }
    public void Dispose() { Calm(); Flutter.RateExponent = 1.0; }

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>At a chosen multiple of Vd, at sea level so EAS is TAS.</summary>
    private static Aircraft At(AircraftConfig c, double overVd)
    {
        double v = overVd * Flutter.VdOverVne * (c.Limits?.VneMs ?? 100.0);
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, 0.0), Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero),
                            ControlDeflections.Neutral);
    }
    private static void Hold(Aircraft ac, double overVd)
    {
        double v = overVd * Flutter.VdOverVne * (ac.Config.Limits?.VneMs ?? 100.0);
        ac.State = new RigidBodyState(new Vec3(0, 0, 0.0), Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero);
    }

    /// <summary>Fly at a speed with the stick dead still, holding the state, for this many seconds.</summary>
    private static void FlyStill(Aircraft ac, double overVd, double seconds)
    {
        int steps = (int)Math.Round(seconds / SimLoop.DefaultFixedDtSec);
        for (int i = 0; i < steps; i++) { ac.Step(new ControlInputs(0, 0, 0, 0), SimLoop.DefaultFixedDtSec); Hold(ac, overVd); }
    }

    // ---- arming and initiation ------------------------------------------------------------------------------------

    /// <summary>Below 1.2 Vd nothing arms, however long you sit there.</summary>
    [Fact]
    public void BelowTheBoundaryNothingArms()
    {
        var ac = At(Load("pitts-s2b-like"), 1.15);
        FlyStill(ac, 1.15, 5.0);
        Assert.False(ac.FlutterArmed);
        Assert.False(ac.FlutterInitiated);
        Assert.Equal(0.0, ac.FlutterIndex);
    }

    /// <summary>
    /// ARMED AT 1.2 Vd AND NOT INITIATED. Ty: "'arm flutter' at 1.2 vd but don't initiate it until either [1.35] vd
    /// or a control input of over 1 degree per second". Held perfectly still at 1.25 Vd, nothing grows — which is
    /// the whole of the band he made survivable-if-you-do-not-touch-anything.
    /// </summary>
    [Fact]
    public void ArmedButNotInitiatedWithTheControlsStill()
    {
        var ac = At(Load("pitts-s2b-like"), 1.25);
        FlyStill(ac, 1.25, 10.0);
        _o.WriteLine($"Pitts held at 1.25 Vd for 10 s, stick still: armed {ac.FlutterArmed}, initiated {ac.FlutterInitiated}, index {ac.FlutterIndex:F4}");
        Assert.True(ac.FlutterArmed);
        Assert.False(ac.FlutterInitiated);
        Assert.Equal(0.0, ac.FlutterIndex);
    }

    /// <summary>
    /// MOVING A CONTROL STARTS IT, AND THAT CONTROL IS THE ONE THAT FLUTTERS. A degree a second is barely a twitch,
    /// which is Ty's intent — "it tends to be the control moved". Measured on the SURFACE, not the stick, because
    /// this model's free elevator and hinge moments make those different signals.
    /// </summary>
    [Fact]
    public void MovingAControlInitiatesItOnThatControl()
    {
        var ac = At(Load("pitts-s2b-like"), 1.25);
        FlyStill(ac, 1.25, 1.0);
        Assert.False(ac.FlutterInitiated);
        // A rudder input: the surface moves, and the rudder is what goes.
        for (int i = 0; i < 200; i++) { ac.Step(new ControlInputs(0, 0, 0.6, 0), SimLoop.DefaultFixedDtSec); Hold(ac, 1.25); }
        _o.WriteLine($"after a rudder input at 1.25 Vd: initiated {ac.FlutterInitiated}, surface {ac.FlutterSurface}, index {ac.FlutterIndex:F4}");
        Assert.True(ac.FlutterInitiated, "a control movement past 1.2 Vd has to start it");
        Assert.Equal(AirframeComponent.Rudder, ac.FlutterSurface);
        Assert.True(ac.FlutterIndex > 0.0, "and it must then be accumulating");
    }

    /// <summary>
    /// AND AT 1.35 Vd IT STARTS ON ITS OWN AND KILLS YOU IN ABOUT A MINUTE. Ty's fallback and Ty's calibration are
    /// the same speed, so this is one test of both: hands off, it initiates immediately and reaches a full fatigue
    /// life near 60 s. Nothing here arranges that — it falls out of the rate law being normalised at his point.
    /// </summary>
    [Fact]
    public void AtTheFallbackSpeedItStartsItselfAndKillsYouInTwoMinutes()
    {
        var ac = At(Load("pitts-s2b-like"), Flutter.FallbackInitiateVd);
        FlyStill(ac, Flutter.FallbackInitiateVd, 0.5);
        Assert.True(ac.FlutterInitiated, "the fallback speed initiates with everything held still");
        double t = 0.5;
        while (t < 400.0 && ac.FlutterIndex < 1.0) { FlyStill(ac, Flutter.FallbackInitiateVd, 1.0); t += 1.0; }
        _o.WriteLine($"Pitts at {Flutter.FallbackInitiateVd:F3} Vd hands off: index 1.0 at {t:F0} s (surface {ac.FlutterSurface})");
        // The linear law at the fallback speed: m = 0.5, so 120 s. Ty's 1.35 Vd = 60 s still sets the SLOPE even
        // though his later ceiling at 1.3 Vd put it out of reach - see Flutter.CalibrationVd.
        Assert.InRange(t, 110.0, 135.0);
        Assert.Equal(120.0, Flutter.SecondsToFailure(Flutter.FallbackInitiateVd), 6);
    }

    // ---- the accumulator is cumulative ------------------------------------------------------------------------------

    /// <summary>"a long exposure at 1.25 vd stil produces a failure" — once something has excited it. Four separate
    /// visits to 1.25 Vd add up; dropping below 1.2 between them stops the shaking and keeps the damage.</summary>
    [Fact]
    public void DamageIsCumulativeAcrossSeparateExcursions()
    {
        var ac = At(Load("pitts-s2b-like"), 1.25);
        for (int i = 0; i < 200; i++) { ac.Step(new ControlInputs(0, 0, 0.6, 0), SimLoop.DefaultFixedDtSec); Hold(ac, 1.25); }
        Assert.True(ac.FlutterInitiated);
        double banked = 0.0;
        for (int burst = 0; burst < 4; burst++)
        {
            FlyStill(ac, 1.25, 10.0);
            double after = ac.FlutterIndex;
            FlyStill(ac, 1.10, 5.0);                       // back below the boundary
            _o.WriteLine($"  burst {burst + 1}: index {after:F4}, after five seconds below 1.2 Vd {ac.FlutterIndex:F4}, shaking {ac.FlutterG:F2} g");
            // One step runs at the previous speed before Hold resets it, so a single step's worth leaks
            // in; what matters is that slowing does not UNDO anything and adds nothing ongoing.
            Assert.InRange(ac.FlutterIndex - after, 0.0, 1e-3);
            Assert.Equal(0.0, ac.FlutterG);                 // and stops the shaking
            Assert.False(ac.FlutterInitiated);              // it re-arms and needs a fresh excitation
            Assert.True(after > banked);
            banked = after;
            // Re-excite for the next burst.
            for (int i = 0; i < 200; i++) { ac.Step(new ControlInputs(0, 0, 0.6, 0), SimLoop.DefaultFixedDtSec); Hold(ac, 1.25); }
        }
        _o.WriteLine($"four 10 s visits to 1.25 Vd banked {ac.FlutterIndex:F4} of a life");
        Assert.True(ac.FlutterIndex > 0.07, "forty seconds at 1.25 Vd is a real bite out of it");
    }

    // ---- the degradation ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE FATIGUE LOWERS THE ULTIMATE STRENGTH AND THE EXISTING RULES DO THE REST. Ty: "the wing follows the same
    /// bend break rules just based on a lower 'limit load' with the theoretical fatigue failure being equivalent to
    /// an ultimate strength of 1g". Flown with HoldLoadFactor so the degraded wing is judged without having to fly
    /// a pull: a wing at a full fatigue life comes apart carrying the aeroplane in level flight.
    /// </summary>
    [Fact]
    public void AFullyFatiguedWingFailsAtOneG()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = At(c, 1.0);
        ac.SetFlutterForTesting(AirframeComponent.AileronLeft, 2.0);   // past any drawn life
        double strength = ac.Structure.LeftWing.Member.Strength;
        double breaksAt = OverstressMember.BreakRatio * ac.Structure.LimitPosG * strength;
        _o.WriteLine($"Pitts left wing at a full fatigue life: Strength {strength:F4}, breaks at {breaksAt:F2} g " +
                     $"(was {OverstressMember.BreakRatio * ac.Structure.LimitPosG:F1} g)");
        Assert.Equal(1.0, breaksAt, 2);
        // And 1 g level flight is now past it: hold it there and that wing goes. ASYMMETRICALLY — the flutter was
        // on the left aileron, so only the left wing was weakened, and the right carries 1 g without complaint.
        // That falls out of the per-wing load factors the integration tip already had; nothing here arranges it.
        for (int i = 0; i < 400 && !ac.Structure.LeftWing.Member.Broken; i++)
            ac.HoldLoadFactor(1.05, 1.05, SimLoop.DefaultFixedDtSec);
        _o.WriteLine($"  held at 1.05 g: left broken {ac.Structure.LeftWing.Member.Broken} " +
                     $"(effective {ac.Structure.LeftWing.Member.EffectiveRatio:F2}), right broken {ac.Structure.RightWing.Member.Broken}");
        Assert.True(ac.Structure.LeftWing.Member.Broken, "a 1 g ultimate means level flight takes that wing off");
        Assert.False(ac.Structure.RightWing.Member.Broken, "and the wing that did not flutter is untouched");
    }

    /// <summary>
    /// AND A PARTLY FLUTTERED WING IS A FLYABLE AEROPLANE THAT MUST BE FLOWN GENTLY. That is the whole point of the
    /// degradation: at half a life the Pitts breaks at 6.5 g, so 3 g holds and 7 g does not. The failure depends on
    /// the pilot, not on the flutter alone.
    /// </summary>
    [Fact]
    public void APartlyFlutteredWingHoldsIfItIsFlownGently()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        double Breaks(double pull)
        {
            var ac = At(c, 1.0);
            ac.SetFlutterForTesting(AirframeComponent.AileronLeft, 0.5 * ac.FlutterStructureLifeForTesting);
            for (int i = 0; i < 400 && !ac.Structure.LeftWing.Member.Broken; i++)
                ac.HoldLoadFactor(pull, pull, SimLoop.DefaultFixedDtSec);
            return ac.Structure.LeftWing.Member.Broken ? 1.0 : 0.0;
        }
        var probe = At(c, 1.0);
        probe.SetFlutterForTesting(AirframeComponent.AileronLeft, 0.5 * probe.FlutterStructureLifeForTesting);
        double strength = probe.Structure.LeftWing.Member.Strength;
        double breaksAt = OverstressMember.BreakRatio * probe.Structure.LimitPosG * strength;
        _o.WriteLine($"Pitts at half a fatigue life: breaks at {breaksAt:F2} g");
        Assert.InRange(breaksAt, 6.0, 7.0);                 // the table's 6.50 g
        Assert.Equal(0.0, Breaks(3.0));                     // flown gently it holds
        Assert.Equal(1.0, Breaks(8.0));                     // pulled hard it does not
    }

    // ---- the shed ------------------------------------------------------------------------------------------------

    /// <summary>Ty: the shed is staggered "by a random time ranging from one second to 5 seconds so it is less
    /// predictable", and the aeroplane is genuinely flying with nothing gone until the timer fires.</summary>
    [Fact]
    public void TheShedIsStaggeredAndTheSurfaceReallyGoes()
    {
        // ISOLATED FROM THE G-LOAD BREAK-UP, which is a correction the measurement forced. Flown at 1.29 Vd with a
        // rudder boot to excite it, the Pitts tore an aileron off through the ORDINARY overstress path in a tenth of
        // a second — it is being flown at 234 kt with full rudder — and that is realistic but it is not what this
        // test is about. Placed on the rung instead, the stagger is the only thing moving.
        // Flown SLOWLY and held, for a second reason the measurement found: putting the index past the surface's
        // life also weakens the wing (that is the point of the degradation), and at 1.0 Vd the aeroplane pitches
        // hard enough to break the weakened wing within the second — which then takes the aileron with it. Held at
        // half Vd the load stays near 1 g and the stagger is the only thing moving.
        var ac = At(Load("pitts-s2b-like"), 0.5);
        double life = ac.FlutterSurfaceLifeForTesting;
        ac.SetFlutterForTesting(AirframeComponent.AileronLeft, life + 0.01);   // just past the surface's own life
        Assert.True(ac.WasFlutterShed(AirframeComponent.AileronLeft), "the shed must be booked at once");
        double delay = ac.NextFlutterShedInSec;
        _o.WriteLine($"surface life {life:F3}; shed scheduled {delay:F2} s out");
        Assert.InRange(delay, 1.0, 5.0);                 // Ty: "a random time ranging from one second to 5 seconds"
        Assert.False(ac.IsLost(AirframeComponent.AileronLeft), "and NOT gone yet: the gap is real flight");

        // It is still attached a second in, and it has gone by six.
        FlyStill(ac, 0.5, 1.0);
        Assert.False(ac.IsLost(AirframeComponent.AileronLeft));
        FlyStill(ac, 0.5, 6.0);
        _o.WriteLine($"after six seconds: aileron lost {ac.IsLost(AirframeComponent.AileronLeft)}");
        Assert.True(ac.IsLost(AirframeComponent.AileronLeft), "the timer has to fire");
    }

    /// <summary>The 1.5 Vd ceiling is its own mechanism: duration does not come into it.</summary>
    [Fact]
    public void TheCeilingFailsTheStructureRegardlessOfDuration()
    {
        var ac = At(Load("pitts-s2b-like"), 1.31);   // one step past the 1.3 ceiling
        FlyStill(ac, 1.31, 0.05);
        double breaksAt = OverstressMember.BreakRatio * ac.Structure.LimitPosG * ac.Structure.LeftWing.Member.Strength;
        _o.WriteLine($"Pitts one step past the ceiling: index {ac.FlutterIndex:F2}, wing breaks at {breaksAt:F2} g");
        Assert.True(ac.FlutterIndex >= 1.0, "the ceiling is immediate, not accumulated");
        Assert.InRange(breaksAt, 0.5, 1.5);
    }

    /// <summary>The draws are seeded, so the same failure can be flown twice and a test can pin it.</summary>
    [Fact]
    public void TheDrawsAreReproducibleUnderASeed()
    {
        AirframeComponent First(int seed)
        {
            var ac = At(Load("pitts-s2b-like"), 1.40);
            ac.FlutterSeed = seed;
            FlyStill(ac, 1.40, 0.5);
            return ac.FlutterSurface;
        }
        Assert.Equal(First(12345), First(12345));
        _o.WriteLine($"seed 12345 -> {First(12345)}, seed 999 -> {First(999)}");
    }

    /// <summary>The exponent is a knob and the law moves with it: k = 1 bites in the 1.2-1.3 band where k = 2 does not.</summary>
    [Fact]
    public void TheRateExponentIsAKnob()
    {
        try
        {
            double k1 = Flutter.SecondsToFailure(1.25);      // shipping: Ty's choice
            Flutter.RateExponent = 2.0;
            double k2 = Flutter.SecondsToFailure(1.25);
            _o.WriteLine($"1.25 Vd: shipping k=1 gives {k1 / 60.0:F1} min, k=2 would give {k2 / 60.0:F1} min");
            Assert.InRange(k1 / 60.0, 2.5, 3.5);             // 3 minutes: bites, which is why he took it
            Assert.InRange(k2 / 60.0, 8.5, 9.5);
            // Ty's calibration survives any exponent, by construction.
            Assert.Equal(60.0, Flutter.SecondsToFailure(1.35), 6);
        }
        finally { Flutter.RateExponent = 1.0; }
    }
}
