using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// TY'S OVER-REV LADDER, 2026-11-11, verbatim:
///
///   "Add this to the damage, an over speed of 10% or less causes no damage. Over 10% increases likelyhood of all
///    engine related failures by 50 x for the remainder of the flight but they remain random. An overspeed of 12%
///    causes a broken ring on one cylinder resulting in the loss of that cylinders power which affects sound (engine
///    sound should be calculated from engine pulses and one will be missing. An overspeed of 15 percent causes a
///    catatonic failure from a spun bearing. High vibration reduced power due to friction increasing oil temperature
///    and decreasing oil pressure. The time until actual failure depends on rpm and load so a pilot that 'nurses it
///    back' has more time with less load and rpm but a pilot that drives it like he stole it will quickly experience
///    a failure which ends in sudden stoppage/siezed"
///
/// Every threshold here is his number. What is measured rather than asserted is the part he described instead of
/// numbering: that nursing it home genuinely buys time.
/// </summary>
[Collection("WorldTerrainActive")]
public class OverspeedDamageTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public OverspeedDamageTests(ITestOutputHelper o) { _o = o; Calm(); }
    public void Dispose() => Calm();

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    // ---- the ladder itself -------------------------------------------------------------------------------------

    /// <summary>Each rung fires where Ty put it and nowhere else. 10 % is "or less causes no damage", so the rung
    /// above it is strictly greater; 12 and 15 are "an overspeed of", so those fire AT the figure.</summary>
    [Theory]
    [InlineData(0.00, 1.0)]
    [InlineData(0.05, 1.0)]
    [InlineData(0.10, 1.0)]       // "an over speed of 10% or less causes no damage"
    [InlineData(0.1001, 50.0)]    // "Over 10% increases likelyhood ... by 50 x"
    [InlineData(0.30, 50.0)]
    public void TheHazardRungFiresOnTheOverspeedAloneAndDoesNotGateOnTime(double excess, double hazard)
    {
        // The only rung with no time on it: Ty put none there, and it is the one that is not a mechanical failure.
        Assert.Equal(hazard, EngineOverspeed.UnreliabilityMultiplier(excess));
    }

    /// <summary>The three MECHANICAL rungs gate on cumulative time, and each one on its own threshold. Ty
    /// 2026-11-11: "overspeed greater than 12% for over a cumulative 10 seconds and overspeed greater than 15% for a
    /// cumulative of 5 seconds and ... an overspeed of 25% for more than 3 seconds throws a single prop blade".</summary>
    [Theory]
    [InlineData(0.0, 0, false, false)]
    [InlineData(9.9, 0, false, false)]
    [InlineData(10.0, 0, false, false)]     // "for OVER a cumulative 10 seconds"
    [InlineData(10.1, 1, false, false)]
    [InlineData(4.9, 0, false, false)]
    public void TheRingBreaksOnlyAfterTenCumulativeSeconds(double seconds, int rings, bool _, bool __)
    {
        Assert.Equal(rings, EngineOverspeed.BrokenRings(seconds));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(4.9, false)]
    [InlineData(5.0, false)]
    [InlineData(5.1, true)]
    public void TheBearingSpinsOnlyAfterFiveCumulativeSeconds(double seconds, bool spun)
        => Assert.Equal(spun, EngineOverspeed.BearingSpun(seconds));

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(2.9, false)]
    [InlineData(3.0, false)]
    [InlineData(3.1, true)]
    public void TheBladeGoesOnlyAfterThreeCumulativeSeconds(double seconds, bool thrown)
        => Assert.Equal(thrown, EngineOverspeed.BladeThrown(seconds));

    /// <summary>A broken ring is one cylinder of however many the engine has, and nothing else.</summary>
    [Fact]
    public void ABrokenRingCostsExactlyOneCylinder()
    {
        Assert.Equal(5.0 / 6.0, EngineOverspeed.PowerFactorForBrokenRings(1, 6), 12);
        Assert.Equal(3.0 / 4.0, EngineOverspeed.PowerFactorForBrokenRings(1, 4), 12);
        Assert.Equal(1.0, EngineOverspeed.PowerFactorForBrokenRings(0, 6), 12);
    }

    // ---- nursing it home ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE PART THAT MAKES IT A FLYING PROBLEM. Ty: "a pilot that nurses it back has more time with less load and rpm
    /// but a pilot that drives it like he stole it will quickly experience a failure". So the same spun bearing has
    /// to last materially longer when it is flown gently, and the difference has to come out of the rpm and the power
    /// rather than out of a difficulty setting.
    ///
    /// Measured here, same engine, same damage, different right hand.
    /// </summary>
    [Fact]
    public void NursingItHomeBuysRealTime()
    {
        const double red = 2700.0;
        double SecondsToSeize(double rpm, double power)
        {
            double rate = EngineOverspeed.BearingDamagePerSecond(rpm, red, power);
            return rate > 0.0 ? 1.0 / rate : double.PositiveInfinity;
        }
        double stolen = SecondsToSeize(red, 1.0);                 // firewalled, red line
        double cruise = SecondsToSeize(0.80 * red, 0.65);
        double nursed = SecondsToSeize(0.45 * red, 0.25);         // levers back, just enough to stay airborne
        double idle = SecondsToSeize(0.26 * red, 0.05);           // gliding it in
        _o.WriteLine($"time to seizure: firewalled {stolen:F0} s, cruise {cruise:F0} s, nursed {nursed:F0} s, idling {idle:F0} s");
        Assert.True(stolen < 150.0, $"driven like he stole it, it has to go quickly ({stolen:F0} s)");
        Assert.True(nursed > 4.0 * stolen, $"nursing it must buy a real multiple of the time ({nursed:F0} s against {stolen:F0} s)");
        Assert.True(cruise > stolen && nursed > cruise && idle > nursed, "and it must be monotone in how hard it is flown");
    }

    /// <summary>The two halves of the PV law are the two halves of what Ty said: LOAD (the power the engine is
    /// making) and RPM. Each on its own has to matter, or "less load and rpm" would be one lever.</summary>
    [Fact]
    public void BothRpmAndLoadMoveTheClock()
    {
        const double red = 2700.0;
        double baseline = EngineOverspeed.BearingDamagePerSecond(0.7 * red, 0.5, 0.5);
        Assert.True(EngineOverspeed.BearingDamagePerSecond(0.7 * red, red, 0.9) > 1.2 * EngineOverspeed.BearingDamagePerSecond(0.7 * red, red, 0.3),
                    "more load must wear it faster at the same rpm");
        Assert.True(EngineOverspeed.BearingDamagePerSecond(0.95 * red, red, 0.5) > 1.2 * EngineOverspeed.BearingDamagePerSecond(0.45 * red, red, 0.5),
                    "more rpm must wear it faster at the same load");
        Assert.True(baseline >= 0.0);
    }

    // ---- in the aeroplane --------------------------------------------------------------------------------------

    private static Aircraft Flying(AircraftConfig c, double tas, double altM)
        => new(c, new RigidBodyState(new Vec3(0, 0, -altM), Quat.Identity, new Vec3(tas, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    /// <summary>
    /// AND IT IS REACHABLE BY FLYING, which is the only reason any of this is worth modelling. The Pitts held past Vd
    /// at 10,000 ft density altitude with the power on runs the governor out of blade and the rpm goes — and the
    /// ladder reads it off the shaft without anybody telling it.
    ///
    /// MEASURED: 25 % past Vd is 3,132 rpm against a 2,700 red line, 18.5 % over — which is two rungs, the ring AND
    /// the bearing. That is worth knowing on its own: the margin between "no damage" and "the engine is going to
    /// seize" is 5 % of rpm, and in a Pitts pointed down with the power on, 5 % of rpm is nothing at all.
    /// </summary>
    [Fact]
    public void DivingPastVdAtAltitudeBreaksARingAndSpinsABearing()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        PropulsionConfig p = c.Propulsion!;
        double vd = 1.1 * (c.Limits?.VneMs ?? 100.0);
        double rho = Atmosphere.DensityAtAltitude(BladeElement.CoarseStopDensityAltitudeM);
        double tas = 1.25 * vd * Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / rho);
        var ac = Flying(c, tas, BladeElement.CoarseStopDensityAltitudeM);
        for (int i = 0; i < 3200; i++)      // 16 s held: past the ring's cumulative 10 and the bearing's 5
        {
            ac.Step(new ControlInputs(0, 0, 0, -1.0), SimLoop.DefaultFixedDtSec);
            ac.State = new RigidBodyState(new Vec3(0, 0, -BladeElement.CoarseStopDensityAltitudeM), Quat.Identity, new Vec3(tas, 0, 0), Vec3.Zero);
        }
        (double o12, double o15, double o25) = ac.OverspeedSeconds;
        _o.WriteLine($"Pitts held at 1.25 Vd, 10,000 ft DA: {ac.EngineRpm:F0} rpm against a {p.MaxRpm:F0} red line " +
                     $"= {ac.OverspeedPeakExcess * 100:F1} % over; seconds over 12/15/25 % {o12:F1}/{o15:F1}/{o25:F1}; " +
                     $"rings {ac.DeadCylinders}, bearing {ac.BearingSpun}, blade {ac.BladeThrown}, powerScale {ac.PowerScale:F4}");
        Assert.True(o12 > EngineOverspeed.BrokenRingSeconds, $"sixteen seconds held has to bank the ring's ten ({o12:F1} s)");
        Assert.True(o15 > EngineOverspeed.SpunBearingSeconds, $"and the bearing's five ({o15:F1} s)");
        Assert.Equal(0.0, o25);                     // 18.5 % never reaches the blade's 25
        Assert.True(ac.EngineRpm > p.MaxRpm * 1.12, $"the dive must over-rev it past 12 % ({ac.EngineRpm:F0} rpm)");
        Assert.True(ac.OverspeedPeakExcess >= EngineOverspeed.BrokenRingExcess);
        Assert.Equal(1, ac.DeadCylinders);
        Assert.True(ac.BearingSpun, "18.5 % over is past the bearing's 15 as well");
        Assert.Equal(EngineOverspeed.HazardMultiplier, ac.EngineUnreliabilityMultiplier);
        // Six cylinders, one of them gone — and the bearing has already started taking its own cut, which is why
        // this is derived from the model's two factors rather than written down as 5/6. Measured: 0.809, where
        // 5/6 alone would be 0.833, the difference being the eight seconds of full power the bearing has had.
        Assert.Equal(EngineOverspeed.PowerFactorForBrokenRings(1, 6) * EngineOverspeed.PowerFactorForBearing(ac.BearingDamage01),
                     ac.PowerScale, 6);
        Assert.True(ac.PowerScale < 5.0 / 6.0);
        Assert.False(ac.BladeThrown, "18.5 % over is well short of the blade's 25");
    }

    /// <summary>
    /// CUMULATIVE MEANS CUMULATIVE. Ty's gates are not "ten seconds in a row": four separate excursions past 12 %
    /// add up, and the fifth one breaks the ring. Flown here as six two-second bursts with the aeroplane brought
    /// back under the red line in between, so nothing is ever over for more than two seconds at a time.
    /// </summary>
    [Fact]
    public void SeparateExcursionsAddUpToTheGate()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        PropulsionConfig p = c.Propulsion!;
        double vd = 1.1 * (c.Limits?.VneMs ?? 100.0);
        double rho = Atmosphere.DensityAtAltitude(BladeElement.CoarseStopDensityAltitudeM);
        double fast = 1.25 * vd * Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / rho);
        double slow = 0.70 * vd * Math.Sqrt(Atmosphere.SeaLevelDensityKgM3 / rho);
        var ac = Flying(c, fast, BladeElement.CoarseStopDensityAltitudeM);

        void Hold(double tas, double seconds)
        {
            int steps = (int)Math.Round(seconds / SimLoop.DefaultFixedDtSec);
            for (int i = 0; i < steps; i++)
            {
                ac.Step(new ControlInputs(0, 0, 0, -1.0), SimLoop.DefaultFixedDtSec);
                ac.State = new RigidBodyState(new Vec3(0, 0, -BladeElement.CoarseStopDensityAltitudeM), Quat.Identity, new Vec3(tas, 0, 0), Vec3.Zero);
            }
        }

        for (int burst = 0; burst < 6; burst++)
        {
            Hold(fast, 2.0);
            double over12 = ac.OverspeedSeconds.Over12;
            Hold(slow, 3.0);                                   // back under the red line: nothing accumulates
            _o.WriteLine($"  after burst {burst + 1}: {over12:F1} s over 12 %, rings {ac.DeadCylinders}, rpm now {ac.EngineRpm:F0}");
            // The slow leg adds only the shaft's own spool-down — measured, 0.05 s — and then nothing.
            Assert.InRange(ac.OverspeedSeconds.Over12 - over12, 0.0, 0.2);
            if (burst < 4) Assert.Equal(0, ac.DeadCylinders);      // under ten seconds banked: still whole
        }
        _o.WriteLine($"six bursts: {ac.OverspeedSeconds.Over12:F1} s over 12 %, rings {ac.DeadCylinders}");
        Assert.True(ac.OverspeedSeconds.Over12 > EngineOverspeed.BrokenRingSeconds);
        Assert.Equal(1, ac.DeadCylinders);
    }

    /// <summary>
    /// THE THROWN BLADE'S LOAD, from Ty's own blade rule and nothing else. A whole blade on a constant-speed
    /// propeller; the OUTER HALF on a fixed-pitch one, which is 0.75 of a whole blade's unbalance rather than 0.5 —
    /// half the mass, but its centre of mass is at 0.75 R instead of 0.5 R.
    /// </summary>
    [Fact]
    public void AThrownBladeLeavesAnEnormousUnbalance()
    {
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double bladeKg = EngineOverspeed.BladeMassKg(p.PropInertia, p.PropDiameterM, p.PropBladeCount);
            double n = EngineOverspeed.ThrownBladeUnbalanceN(p.PropInertia, p.PropDiameterM, p.PropBladeCount, p.MaxRpm, p.ConstantSpeed);
            double slender = 3.0 * p.PropInertia / (0.25 * p.PropDiameterM * p.PropDiameterM);
            _o.WriteLine($"{id}: blade {bladeKg:F2} kg at {EngineOverspeed.BladeComRadiusM(p.PropDiameterM):F3} m " +
                         $"(propeller {bladeKg / EngineOverspeed.BladeMassFraction:F1} kg, against {slender:F1} kg from the slender-blade inversion), " +
                         $"{(p.ConstantSpeed ? "whole blade" : "outer half")} at {p.MaxRpm:F0} rpm = {n / 1000.0:F1} kN");
            Assert.True(n > 20000.0, $"{id}: a thrown blade at the red line is tens of kN ({n / 1000.0:F1} kN)");
            // The Pitts is the one type whose propeller build-up is written down: 36.3 kg, blades 2.668 kg.m2 of a
            // 3.06 total. Handed the whole authored figure the inversion comes back 4.7 % high, the crankshaft's
            // share being inside it and not separable.
            if (id == "pitts-s2b-like") Assert.InRange(bladeKg / EngineOverspeed.BladeMassFraction, 36.3, 38.5);
            // Half a blade is 0.75 of a whole one's unbalance, not half: the piece that goes is the outer half.
            double whole = EngineOverspeed.ThrownBladeUnbalanceN(p.PropInertia, p.PropDiameterM, p.PropBladeCount, p.MaxRpm, true);
            double half = EngineOverspeed.ThrownBladeUnbalanceN(p.PropInertia, p.PropDiameterM, p.PropBladeCount, p.MaxRpm, false);
            Assert.Equal(0.75, half / whole, 9);
        }
    }

    /// <summary>The hazard multiplier is "for the remainder of the flight": pull the power back and it stays 50.</summary>
    [Fact]
    public void TheFiftyTimesHazardStaysForTheRestOfTheFlight()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Flying(c, 60.0, 1500.0);
        ac.SetOverspeedForTesting(0.11);
        Assert.Equal(50.0, ac.EngineUnreliabilityMultiplier);
        var sim = new SimLoop(ac);
        sim.RunFor(10.0, new ControlInputs(0, 0, 0, 1.0));     // throttle shut, nursed, well under the red line
        _o.WriteLine($"ten seconds at the idle stop later: {ac.EngineRpm:F0} rpm, peak {ac.OverspeedPeakExcess * 100:F1} %, x{ac.EngineUnreliabilityMultiplier:F0}");
        Assert.Equal(50.0, ac.EngineUnreliabilityMultiplier);
        Assert.Equal(0, ac.DeadCylinders);                     // 11 % is below the ring's 12
        Assert.False(ac.BearingSpun);
        Assert.Equal(0.0, ac.BearingDamage01);
    }

    /// <summary>A 10 % over-rev costs nothing at all — no hazard, no cylinder, no bearing, and no power.</summary>
    [Fact]
    public void TenPerCentCostsNothing()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Flying(c, 60.0, 1500.0);
        ac.SetOverspeedForTesting(0.10);
        new SimLoop(ac).RunFor(5.0, new ControlInputs(0, 0, 0, -1.0));
        Assert.Equal(1.0, ac.EngineUnreliabilityMultiplier);
        Assert.Equal(0, ac.DeadCylinders);
        Assert.False(ac.BearingSpun);
        Assert.Equal(0.0, ac.BearingDamage01);
        Assert.Equal(0.0, ac.OilTempRiseF);
        Assert.False(ac.EngineSeized);
    }

    /// <summary>
    /// THE SPUN BEARING, FLOWN TO THE END. Oil pressure down, oil temperature up, vibration, power down, and then
    /// sudden stoppage with the propeller stopped rather than windmilling — the whole of Ty's last sentence, in the
    /// aeroplane, at full power so it does not take all day.
    /// </summary>
    [Fact]
    public void ASpunBearingEndsInSuddenStoppage()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Flying(c, 60.0, 1500.0);
        ac.SetOverspeedForTesting(0.16, 60.0);
        var sim = new SimLoop(ac);
        double psi0 = double.NaN, power0 = double.NaN;
        double t = 0.0;
        while (t < 400.0 && !ac.EngineSeized)
        {
            sim.RunFor(1.0, new ControlInputs(0, 0, 0, -1.0));
            ac.State = new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(60, 0, 0), Vec3.Zero);
            t += 1.0;
            if (double.IsNaN(psi0)) { psi0 = ac.OilPressPsi; power0 = ac.PowerScale; }
            if (Math.Abs(t - 60.0) < 1e-9)
                _o.WriteLine($"after 60 s at full power: damage {ac.BearingDamage01:F3}, oil {ac.OilPressPsi:F1} psi " +
                             $"(+{ac.OilTempRiseF:F0} F), shake {ac.BearingVibrationG:F2} g, powerScale {ac.PowerScale:F3}");
        }
        _o.WriteLine($"seized after {t:F0} s at full power; rpm {ac.EngineRpm:F2}, running {ac.EngineRunning}, stopped {ac.EngineStopped}");
        Assert.True(ac.EngineSeized, $"a spun bearing flown at full power has to let go ({t:F0} s, damage {ac.BearingDamage01:F3})");
        Assert.InRange(t, 60.0, 240.0);                 // ~2 minutes: EngineOverspeed.SeizeSeconds / 2
        Assert.False(ac.EngineRunning);
        Assert.True(ac.EngineStopped, "a seizure is damage, not a starved engine");
        // SUDDEN STOPPAGE, AND THE TACHOMETER HAS TO AGREE. This caught a real one: the needle read 717 rpm on an
        // engine the same step had declared seized, because AircraftEngine.WindmillRpm falls through to the
        // windmilling balance for any engine that is not RUNNING, and nothing had told it that a picked-up bearing
        // holds the propeller.
        Assert.Equal(0.0, ac.EngineRpm, 6);
        Assert.Equal(0.0, ac.WindmillRpm, 6);
        Assert.Equal(0.0, ac.ShaftRpm, 6);
        Assert.Equal(0.0, ac.OilPressPsi);
        Assert.True(ac.OilTempRiseF > 80.0, "the oil was cooking by the end");
        Assert.True(ac.BearingVibrationG > 0.3, "and it was shaking");
    }

    /// <summary>And the same bearing, nursed: it is still alive at the time the firewalled one was already dead.</summary>
    [Fact]
    public void TheSameBearingNursedIsStillRunningWhenTheOtherHasSeized()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Flying(c, 45.0, 1500.0);
        ac.SetOverspeedForTesting(0.16, 60.0);
        ac.PropLever = 0.0;                              // rpm back as far as the governor goes
        var sim = new SimLoop(ac);
        for (int i = 0; i < 240; i++)
        {
            sim.RunFor(1.0, new ControlInputs(0, 0, 0, 0.6));   // throttle well back
            ac.State = new RigidBodyState(new Vec3(0, 0, -1500), Quat.Identity, new Vec3(45, 0, 0), Vec3.Zero);
        }
        _o.WriteLine($"nursed for 240 s: {ac.EngineRpm:F0} rpm, power {ac.ShaftPower01:F3}, damage {ac.BearingDamage01:F3}, seized {ac.EngineSeized}");
        Assert.False(ac.EngineSeized, $"nursed, it has to still be running at 240 s (damage {ac.BearingDamage01:F3})");
        Assert.True(ac.BearingDamage01 > 0.0, "but it is still dying");
    }
}
