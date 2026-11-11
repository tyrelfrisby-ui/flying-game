using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE VERTICAL TAIL AS A STRUCTURE — Ty 2026-11-11, who answered the open fin question not with a number but with
/// the method to measure one: "At va measure the force on the vertical tail with a yaw one way followed by an
/// immediate yaw full deflection the other way then add 15% and call that the limit load with a 2.0 ultimate
/// strength before any fatigue".
///
/// The fin had no structure for exactly one day, and the reason it had none was good: a member that nothing drives
/// is a shadow that quietly disagrees with the members that are real. Two things had to arrive together, and this
/// file holds both of them.
///
///  * THE LOAD IS REAL AND PER-STEP. <see cref="Aircraft.FinSideLoadN"/> is the fin's and the rudder's own strips
///    summed by the aero model every evaluation — the fin's analogue of the normal load factor, read every step
///    whether anything is fluttering or not. Nothing here is a flutter special case.
///  * THE LIMIT IS MEASURED, NOT GUESSED. FinLoadProbe flies the rudder-reversal case (FAR 23.441) and the result is
///    authored per type; a type that has not been measured publishes no limit and has NO member, which is the
///    honest state rather than a convenient one.
/// </summary>
[Collection("WorldTerrainActive")]
public class FinPoolTests
{
    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Aircraft Level(AircraftConfig c, double tasMs, double alt = 900.0)
    {
        RigSolver.Result r = RigSolver.SolveGlide(c, tasMs, alt);
        Quat att = RigSolver.Attitude(r, 0.0);
        var vb = new Vec3(tasMs * Math.Cos(r.AlphaRad), 0, tasMs * Math.Sin(r.AlphaRad));
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -alt), att, vb, Vec3.Zero), new ControlDeflections(0, r.ElevatorRad, 0, 0));
    }

    /// <summary>The whole measured fleet carries a fin limit, and it is a load in newtons rather than a load factor
    /// — the one member here that is not driven by g, because a fin is not loaded by g.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("j3-cub-like")]
    [InlineData("c172-like")]
    public void AMeasuredTypeHasAFinMemberDrivenByANewtonLoad(string id)
    {
        AircraftConfig c = Load(id);
        Assert.True(c.Limits.FinLimitLoadN > 0.0, $"{id}: FinLoadProbe's measurement has not been authored");
        Assert.True(c.Limits.FinOrdinaryLoadN > 0.0, $"{id}: the fatigue endpoint has not been authored");
        // The reversal is always worse than the single deflection, so the limit must exceed the ordinary load by
        // more than Ty's 15 % alone. If this inverts, the two numbers have been swapped.
        Assert.True(c.Limits.FinLimitLoadN > c.Limits.FinOrdinaryLoadN * 1.15,
            $"{id}: limit {c.Limits.FinLimitLoadN:F0} N is not clear of the ordinary load {c.Limits.FinOrdinaryLoadN:F0} N");
        Aircraft ac = Level(c, Math.Max(30.0, c.SpawnIasMs));
        Assert.NotNull(ac.Structure.VerticalTail);
        Assert.Equal(c.Limits.FinLimitLoadN, ac.Structure.FinLimitLoadN, 6);
    }

    /// <summary>A type with no measurement has NO member — not a guessed one. The discipline the fin pool was
    /// declined on for a day, kept now that it exists.</summary>
    [Fact]
    public void AnUnmeasuredTypeHasNoFinMemberAtAll()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        c.Limits.FinLimitLoadN = 0.0;
        Aircraft ac = Level(c, 50.0);
        Assert.Null(ac.Structure.VerticalTail);
        Assert.Equal(0.0, ac.Structure.FinRatioAt(5000.0), 9);
    }

    /// <summary>
    /// THE LOAD SIGNAL IS REAL, and it is the aeroplane's every step rather than something flutter asks for: with
    /// the pedals centred the fin carries almost nothing, and full rudder loads it in the direction the pedal
    /// implies.
    /// </summary>
    [Fact]
    public void TheFinIsLoadedByTheRudderAndNotByLevelFlight()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        double va = c.Limits.VaMs;
        Assert.True(va > 1.0, "Va must be authored for this to mean anything");
        double tas = va * Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / Atmosphere.DensityAtAltitude(900.0));

        Aircraft straight = Level(c, tas);
        double stick = Aircraft.StickForDeflection(straight.CurrentDeflections.ElevatorRad, c.Controls.Elevator);
        for (int i = 0; i < 60; i++) straight.Step(new ControlInputs(0, stick, 0, 1.0), SimLoop.DefaultFixedDtSec);
        double quiet = Math.Abs(straight.FinLoadRatio);

        double Kick(int pedal)
        {
            Aircraft k = Level(c, tas);
            double peak = 0.0, signed = 0.0;
            for (int i = 0; i < 60; i++)
            {
                k.Step(new ControlInputs(0, stick, pedal, 1.0), SimLoop.DefaultFixedDtSec);
                if (Math.Abs(k.FinLoadRatio) > peak) { peak = Math.Abs(k.FinLoadRatio); signed = k.FinLoadRatio; }
            }
            return signed;
        }
        double right = Kick(1), left = Kick(-1);
        Assert.True(quiet < 0.1, $"straight and level the fin should carry almost nothing; it carried {quiet:F2} of its limit");
        Assert.True(Math.Abs(right) > 0.3, $"full rudder at Va should be a real fraction of the fin's limit; it was {Math.Abs(right):F2}");
        // THE SIGN IS ASSERTED AS A MIRROR RELATIONSHIP, not as a direction I decided was right: the two pedals
        // must load the fin in OPPOSITE directions and by a similar amount. Pinning "right pedal gives a negative
        // body-Y force" would be asserting a convention rather than measuring the model, and a sign convention
        // asserted from the armchair is how the aileron roll direction was got wrong on this branch once already.
        Assert.True(right * left < 0.0, $"the two pedals must load the fin opposite ways: right {right:F2}, left {left:F2}");
        Assert.InRange(Math.Abs(left / right), 0.7, 1.43);
    }

    /// <summary>Ty's ultimate is 2.0 x limit and needs no new constant: <see cref="OverstressMember.BreakRatio"/> is
    /// already 2.0, so the fin breaks by the same rule the wings do. Below it, it holds.</summary>
    [Fact]
    public void TheFinBreaksAtTwiceItsLimitAndNotBelow()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        double limit = c.Limits.FinLimitLoadN;

        Aircraft held = Level(c, 50.0);
        for (int i = 0; i < 200; i++) held.HoldFinSideLoad(1.9 * limit, SimLoop.DefaultFixedDtSec);
        Assert.False(held.Structure.VerticalTail!.Broken, "1.9 x limit must bend the fin and not break it");
        Assert.True(held.Structure.VerticalTail.DeflectionRad > 0.0, "past the limit the fin must flex visibly");

        Aircraft broken = Level(c, 50.0);
        for (int i = 0; i < 200; i++) broken.HoldFinSideLoad(2.2 * limit, SimLoop.DefaultFixedDtSec);
        Assert.True(broken.Structure.VerticalTail!.Broken, "2.2 x limit must take the fin off");
        Assert.True(broken.IsLost(AirframeComponent.TailVertical), "the fin that failed must leave the aeroplane");
        Assert.True(broken.IsLost(AirframeComponent.Rudder), "and the rudder goes with the fin it hangs on");
    }

    /// <summary>
    /// RUDDER FLUTTER NOW DEGRADES SOMETHING. It used to shed the rudder and weaken nothing — half of what Ty
    /// specified — because there was no fin member to weaken.
    /// </summary>
    [Fact]
    public void RudderFlutterWeakensTheFinAndLeavesTheWingsAlone()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        Aircraft ac = Level(c, 50.0);
        double before = ac.Structure.VerticalTail!.Strength;
        ac.SetFlutterForTesting(AirframeComponent.Rudder, 0.5 * ac.FlutterStructureLifeForTesting);
        Assert.True(ac.Structure.VerticalTail.Strength < before, "half a fatigue life must have cost the fin strength");
        Assert.Equal(1.0, ac.Structure.LeftWing.Member.Strength, 9);
        Assert.Equal(1.0, ac.Structure.RightWing.Member.Strength, 9);
        Assert.Equal(1.0, ac.Structure.HorizontalTail!.Strength, 9);
    }

    /// <summary>
    /// THE FIN'S FATIGUE ENDPOINT, which is the COORDINATOR'S reading and not Ty's, so this test is also the place
    /// it can be overturned in one line. The wing's endpoint is the load it carries in level flight, so a fully
    /// fatigued wing fails doing nothing; a fin carries almost nothing in balanced flight, so the analogue is the
    /// next ORDINARY use of the thing — one full rudder deflection at Va. A fully fatigued fin must survive being
    /// flown straight and fail the moment the pilot uses his feet.
    /// </summary>
    [Fact]
    public void AFullyFatiguedFinSurvivesStraightFlightAndFailsOnAnOrdinaryRudderInput()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        double ordinary = c.Limits.FinOrdinaryLoadN;

        Aircraft ac = Level(c, 50.0);
        ac.SetFlutterForTesting(AirframeComponent.Rudder, ac.FlutterStructureLifeForTesting);
        Assert.False(ac.Structure.VerticalTail!.Broken, "the fatigue itself must not break it; it lowers the strength");

        // THE ENDPOINT IS EXACT, so it is checked as a load and not as a knife edge. A full fatigue life brings the
        // fin's BREAKING load down to exactly the ordinary input -- that is what the endpoint says -- and a member
        // breaks when its effective ratio goes PAST its break ratio, not when it reaches it. So holding precisely
        // the ordinary load sits the fin on the boundary, which is a test that measures floating-point rounding
        // rather than physics. The breaking load itself is the thing to assert.
        double breaking = ac.Structure.VerticalTail.Strength * OverstressMember.BreakRatio * c.Limits.FinLimitLoadN;
        Assert.Equal(ordinary, breaking, 0);

        // Straight and level: the fin is carrying a few per cent of an ordinary input, and it holds.
        for (int i = 0; i < 200; i++) ac.HoldFinSideLoad(0.05 * ordinary, SimLoop.DefaultFixedDtSec);
        Assert.False(ac.Structure.VerticalTail.Broken, "a fatigued fin must not drop off an aeroplane flying straight");

        // A rudder input the size he always makes -- a real one varies by far more than five per cent -- and it is gone.
        for (int i = 0; i < 200; i++) ac.HoldFinSideLoad(1.05 * ordinary, SimLoop.DefaultFixedDtSec);
        Assert.True(ac.Structure.VerticalTail.Broken,
            "a fully fatigued fin must fail the next time the pilot uses the rudder the way he always does");

        // And an UNfatigued fin shrugs the same input off, or the endpoint has made ordinary flying dangerous.
        Aircraft fresh = Level(c, 50.0);
        for (int i = 0; i < 200; i++) fresh.HoldFinSideLoad(1.05 * ordinary, SimLoop.DefaultFixedDtSec);
        Assert.False(fresh.Structure.VerticalTail!.Broken, "an undamaged fin must take an ordinary rudder input all day");
        Assert.Equal(0.0, fresh.Structure.VerticalTail.DeflectionRad, 9);
    }

    /// <summary>Va is authored on every measured type and is what Va means: Vs1 x sqrt(n_limit), above the stall and
    /// at or below Vne. It is DERIVED rather than read off a manual, and this is where that is held.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("j3-cub-like")]
    [InlineData("c172-like")]
    [InlineData("great-lakes-like")]
    public void VaIsAuthoredAndIsVs1TimesRootNLimit(string id)
    {
        AircraftConfig c = Load(id);
        Assert.True(c.Limits.VaMs > 1.0, $"{id}: Va is not authored");
        double vs1 = FlyingGame.Sim.Practice.PracticeScenario.EstimateVso(c, 0.0, 0.0);
        double expect = Math.Min(vs1 * Math.Sqrt(c.Limits.GMax), c.Limits.VneMs);
        Assert.Equal(expect, c.Limits.VaMs, 1);
    }

    /// <summary>AUTHORING VA MUST NOT HAVE MOVED ANY AEROPLANE'S LIMIT LOAD FACTORS. LimitsConfig.LimitAt ramps the
    /// envelope from VA to VNE, and it is gated on vaMs — which until today only one type published. It is inert
    /// while gMaxAtVne is unset, because then it interpolates between a number and itself, but that is a sentence
    /// in a comment until something holds it.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("j3-cub-like")]
    [InlineData("c172-like")]
    [InlineData("dc3-like")]
    public void AuthoringVaDidNotNarrowAnyEnvelope(string id)
    {
        AircraftConfig c = Load(id);
        Assert.True(c.Limits.VaMs > 1.0);
        (double posVa, double negVa) = c.Limits.LimitAt(c.Limits.VaMs, 0.0, c.Mass.MassKg);
        (double posVne, double negVne) = c.Limits.LimitAt(c.Limits.VneMs, 0.0, c.Mass.MassKg);
        Assert.Equal(posVa, posVne, 9);
        Assert.Equal(negVa, negVne, 9);
    }
}
