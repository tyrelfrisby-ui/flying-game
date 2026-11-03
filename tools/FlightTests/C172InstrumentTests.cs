using FlyingGame.Core;
using FlyingGame.Core.Instruments;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Nav;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The 1970s 172 panel (FlyingGame.Core.Instruments). Every expected number here is written out by hand from
/// the physics or the published figure it comes from — not read back from the code under test:
///
///  - Earth-rate drift: 15.041 °/h × sin 45° = 10.636 °/h.
///  - Dipole dip at 45°: tan I = 2 tan 45° → 63.43°.
///  - ADF loop bearing banked 30° with the beacon 45° right: atan(tan 45° · cos 30°) = 40.89°.
///  - Glideslope threshold crossing: antenna 305 m in and 120 m off → √(305² + 120²) = 327.76 m × tan 3° = 17.18 m.
///  - Ball in a 0.1 g side force: atan 0.1 = 5.711°, over a 12° arc = 0.476.
///  - Slant range 6,000 ft over the antenna: 1,828.8 m / 1,852 = 0.9875 NM.
///
/// No terrain is installed (the receivers are handed null and treat the path as clear) so nothing here touches
/// WorldTerrain.Active or any other process-wide state.
/// </summary>
public class C172InstrumentTests
{
    private const double Dt = 1.0 / 60.0;
    private const double D = Math.PI / 180.0;

    // ---------------------------------------------------------------- helpers

    private static Quat Euler(double yawDeg, double pitchDeg, double rollDeg)
    {
        Quat qz = new(0, 0, Math.Sin(yawDeg * D / 2), Math.Cos(yawDeg * D / 2));
        Quat qy = new(0, Math.Sin(pitchDeg * D / 2), 0, Math.Cos(pitchDeg * D / 2));
        Quat qx = new(Math.Sin(rollDeg * D / 2), 0, 0, Math.Cos(rollDeg * D / 2));
        return Quat.Multiply(Quat.Multiply(qz, qy), qx).Normalized();
    }

    private static double TrueHeading(Quat q)
    {
        Vec3 f = q.Rotate(new Vec3(1, 0, 0));
        double h = Math.Atan2(f.Y, f.X) / D;
        return h < 0 ? h + 360 : h;
    }

    /// <summary>A coordinated level turn at <paramref name="ktas"/>, rate <paramref name="degS"/> (+ right), heading <paramref name="hdg"/>.</summary>
    private static PanelInputs Turn(double hdg, double degS, double ktas = 90, double altM = 1500)
    {
        double w = degS * D, v = ktas * 1852.0 / 3600.0;
        double bank = Math.Atan(v * w / Earth.G);
        var x = PanelInputs.Level(0, altM);
        x.Attitude = Euler(hdg, 0, bank / D);
        x.RatesBody = new Vec3(0, w * Math.Sin(bank), w * Math.Cos(bank));
        x.SpecificForceBody = new Vec3(0, 0, -Earth.G / Math.Cos(bank));
        return x;
    }

    private static C172Panel Running(double hdg = 0, double altM = 1500)
    {
        var p = new C172Panel();
        p.Compass.DeviationScale = 0;
        p.Start(PanelInputs.Level(hdg, altM), gyrosRunning: true);
        return p;
    }

    /// <summary>Fly a standard-rate (or any) coordinated turn through <paramref name="degrees"/>, then roll out.</summary>
    private static double FlyTurn(C172Panel p, double hdg, double degrees, double degS = 3)
    {
        double t = 0, dur = Math.Abs(degrees / degS);
        double sign = Math.Sign(degrees);
        while (t < dur)
        {
            p.Step(Turn(hdg + sign * degS * t, sign * degS), Dt);
            t += Dt;
        }
        return hdg + degrees;
    }

    private static double Diff(double a, double b) => NavReception.AngleDiff(a, b);

    // ---------------------------------------------------------------- vacuum

    [Fact]
    public void SuctionIsRegulatedAtCruiseShortAtIdleAndZeroWithAFailedPump()
    {
        var v = new VacuumSystem();
        v.Step(2300, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        Assert.True(v.Regulating);
        Assert.Equal(5.0, v.SuctionInHg, 3);

        v.Step(700, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        Assert.False(v.Regulating);
        Assert.InRange(v.SuctionInHg, 1.5, 4.0);       // below the 4.5 green arc at idle

        v.Step(0, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        Assert.Equal(0.0, v.SuctionInHg, 6);

        v.PumpFailed = true;
        v.Step(2300, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        Assert.Equal(0.0, v.SuctionInHg, 6);
        Assert.Equal(0.0, v.AiFlowKgS, 9);
    }

    [Fact]
    public void RegulatorTakesOverAt1200RpmAndSuctionRisesWithRpmBelowIt()
    {
        var v = new VacuumSystem();
        v.Step(1150, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        Assert.False(v.Regulating);
        Assert.True(v.SuctionInHg < 5.0);
        v.Step(1250, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        Assert.True(v.Regulating);

        double last = 0;
        for (int rpm = 300; rpm <= 1200; rpm += 100)
        {
            v.Step(rpm, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
            Assert.True(v.SuctionInHg > last, $"suction must rise with rpm ({rpm})");
            last = v.SuctionInHg;
        }
    }

    [Fact]
    public void ThinAirMakesLessSuctionAtTheSameRpm()
    {
        var v = new VacuumSystem();
        v.Step(900, Pneumatics.StdPressurePa, Pneumatics.StdTempK);
        double sl = v.SuctionInHg;
        double alt = 3048;   // 10,000 ft
        v.Step(900, Atmosphere.PressureAtAltitude(alt), 288.15 - 0.0065 * alt);
        Assert.True(v.SuctionInHg < sl * 0.8, $"10,000 ft {v.SuctionInHg:F2} vs sea level {sl:F2}");
    }

    // ---------------------------------------------------------------- gyro rotors

    [Fact]
    public void AirGyrosTakeMinutesToSpinUpAndToRunDown()
    {
        var p = new C172Panel();
        p.Start(PanelInputs.Level(0, 0, rpm: 1000), gyrosRunning: false);
        Assert.Equal(0.0, p.Ai.Rotor.SpeedFraction, 6);
        var x = PanelInputs.Level(0, 0, rpm: 1700);
        double t = 0, at30 = 0;
        for (; t < 300; t += Dt) { p.Step(x, Dt); if (at30 == 0 && t >= 30) at30 = p.Ai.Rotor.SpeedFraction; }
        Assert.InRange(at30, 0.2, 0.9);                         // not instant…
        Assert.True(p.Ai.Rotor.SpeedFraction > 0.95, $"AI at 5 min {p.Ai.Rotor.SpeedFraction:F3}");   // …but there by "five minutes"
        Assert.True(p.Dg.Rotor.SpeedFraction > 0.95, $"DG at 5 min {p.Dg.Rotor.SpeedFraction:F3}");

        p.Failures.VacuumPump = true;
        for (t = 0; t < 60; t += Dt) p.Step(x, Dt);
        double at1 = p.Ai.Rotor.SpeedFraction;
        for (; t < 600; t += Dt) p.Step(x, Dt);
        Assert.InRange(at1, 0.3, 0.8);                          // still a gyro a minute after the pump quits
        Assert.True(p.Ai.Rotor.SpeedFraction < 0.15, $"AI after 10 min {p.Ai.Rotor.SpeedFraction:F3}");
    }

    // ---------------------------------------------------------------- attitude indicator

    [Fact]
    public void AttitudeIndicatorShowsBankAndPitchAndHoldsThroughTurns()
    {
        var p = Running();
        var x = PanelInputs.Level(0, 1500);
        x.Attitude = Euler(0, 8, 0);
        p.Step(x, Dt);
        Assert.Equal(8.0, p.Ai.PitchDeg, 1);
        Assert.Equal(0.0, p.Ai.BankDeg, 1);

        var turn = Turn(0, 3);
        p.Step(turn, Dt);
        double bankTrue = Math.Atan(90 * 1852.0 / 3600.0 * 3 * D / Earth.G) / D;
        Assert.Equal(bankTrue, p.Ai.BankDeg, 1);
    }

    [Fact]
    public void AfterA180TheHorizonShowsASlightClimbAndATurnTheOtherWay()
    {
        // FAA PHAK: "Following a 180° turn, the attitude indicator will show a slight climb and a turn in the
        // opposite direction." Nobody wrote that in: the vanes erected toward a tilted apparent vertical for a minute.
        var p = Running(0);
        double hdg = FlyTurn(p, 0, 180);
        p.Step(PanelInputs.Level(hdg, 1500), Dt);
        Assert.InRange(p.Ai.PitchDeg, 1.5, 4.5);
        Assert.InRange(p.Ai.BankDeg, -2.0, -0.1);
    }

    [Fact]
    public void AttitudeIndicatorErectsBackAtItsRatedRate()
    {
        var p = Running();
        p.Ai.SpinAxis = new Vec3(Math.Sin(5 * D), 0, Math.Cos(5 * D));      // 5° off
        var x = PanelInputs.Level(0, 1500);
        for (double t = 0; t < 30; t += Dt) p.Step(x, Dt);
        // 6 °/min saturated: half a minute takes off about 3° (a little less as the vanes come off saturation).
        Assert.InRange(p.Ai.ErrorFromVerticalDeg, 1.8, 2.6);
        for (double t = 0; t < 120; t += Dt) p.Step(x, Dt);
        Assert.True(p.Ai.ErrorFromVerticalDeg < 0.3);
    }

    /// <summary>
    /// A coordinated turn as it is flown: roll in at 10 °/s to the standard-rate bank, hold it, roll out — so the
    /// gimbals move in their races the way they do in the aeroplane. Returns the worst |indicated − true| bank.
    /// </summary>
    private static double FlyRolledTurn(C172Panel p, ref double hdg, double degrees, double ktas = 90)
    {
        double v = ktas * 1852.0 / 3600.0, sign = Math.Sign(degrees);
        double full = Math.Atan(v * 3 * D / Earth.G), rollRate = 10 * D;
        double bank = 0, turned = 0, worst = 0;
        bool rollingOut = false;
        while (true)
        {
            double remaining = Math.Abs(degrees) - turned;
            // Start rolling out when the turn left equals what the roll-out will still turn (half the roll-in's).
            if (!rollingOut && remaining <= 3.0 * (full / rollRate) / 2.0) rollingOut = true;
            double pRate = rollingOut ? -rollRate : (bank < full ? rollRate : 0);
            bank = Math.Clamp(bank + pRate * Dt, 0, full);
            if (rollingOut && bank <= 0) break;
            if (bank == full && !rollingOut) pRate = 0;
            double w = Earth.G * Math.Tan(bank) / v;
            var x = PanelInputs.Level(0, 1500);
            x.Attitude = Euler(hdg, 0, sign * bank / D);
            x.RatesBody = new Vec3(sign * pRate, sign * w * Math.Sin(bank), sign * w * Math.Cos(bank));
            x.SpecificForceBody = new Vec3(0, 0, -Earth.G / Math.Cos(bank));
            p.Step(x, Dt);
            worst = Math.Max(worst, Math.Abs(p.Ai.BankDeg - sign * bank / D));
            hdg += sign * w / D * Dt;
            turned += w / D * Dt;
        }
        return worst;
    }

    [Fact]
    public void WithoutVacuumTheHorizonWandersAndWithItDoesNot()
    {
        double Worst(bool fail)
        {
            var p = Running();
            p.Failures.VacuumPump = fail;
            double hdg = 0, worst = 0;
            // Fifteen minutes of S-turns; score the last five.
            for (int i = 0; i < 15; i++)
            {
                double w = FlyRolledTurn(p, ref hdg, i % 2 == 0 ? 180 : -180);
                for (double t = 0; t < 30; t += Dt) p.Step(PanelInputs.Level(hdg, 1500), Dt);
                if (i >= 10) worst = Math.Max(worst, w);
            }
            return worst;
        }
        double ok = Worst(false), failed = Worst(true);
        Assert.True(ok < 5.0, $"with vacuum the bank was out by {ok:F1}°");
        Assert.True(failed > 10.0, $"pump failed, bank out by only {failed:F1}° (vacuum {ok:F1}°)");
    }

    [Fact]
    public void AcceleratingLevelTheAiCreepsNoseUp()
    {
        var p = Running();
        var x = PanelInputs.Level(0, 1500);
        x.SpecificForceBody = new Vec3(2.0, 0, -Earth.G);    // 2 m/s² forward, wings level, nose level
        for (double t = 0; t < 60; t += Dt) p.Step(x, Dt);
        // Erecting toward an apparent vertical 11.5° aft of true at 6 °/min: about 6° in a minute.
        Assert.InRange(p.Ai.PitchDeg, 3.0, 7.0);
    }

    [Fact]
    public void PastThePitchStopTheAttitudeIndicatorTumbles()
    {
        var p = Running();
        // A loop: pitch up through the vertical at 30 °/s.
        double pitch = 0;
        var x = PanelInputs.Level(0, 1500);
        for (double t = 0; t < 6; t += Dt)
        {
            pitch += 30 * Dt;
            x.Attitude = Euler(0, Math.Min(pitch, 89.9), 0);
            x.RatesBody = new Vec3(0, 30 * D, 0);
            x.SpecificForceBody = new Vec3(0, 0, -3 * Earth.G);
            p.Step(x, Dt);
        }
        Assert.True(p.Ai.StopHits > 0, "the inner gimbal never reached its 70° stop");
        Assert.Equal(70.0, Math.Abs(p.Ai.PitchDeg), 0);     // pinned on the stop while the case keeps going
        // Level again: a tumbled gyro does not come back level.
        p.Step(PanelInputs.Level(0, 1500), Dt);
        Assert.True(p.Ai.ErrorFromVerticalDeg > 20, $"after the stop, error {p.Ai.ErrorFromVerticalDeg:F1}°");
    }

    // ---------------------------------------------------------------- directional gyro

    [Fact]
    public void DirectionalGyroDriftsAtEarthRateTimesSinLatitude()
    {
        var p = Running(0);
        p.Dg.UnbalanceScale = 0;
        double start = p.Dg.CardDeg;
        var x = PanelInputs.Level(0, 1500);
        for (double t = 0; t < 3600; t += 0.05) p.Step(x, 0.05);
        // Northern hemisphere: the reading DECREASES, 10.636° in the hour at 45° N.
        Assert.Equal(-10.636, Diff(start, p.Dg.CardDeg), 1);
    }

    [Fact]
    public void AsBuiltTheDgDriftsAFewDegreesInFifteenMinutes()
    {
        var p = Running(90);
        double start = p.Dg.CardDeg;
        var x = PanelInputs.Level(90, 1500);
        for (double t = 0; t < 900; t += 0.05) p.Step(x, 0.05);
        double drift = Diff(start, p.Dg.CardDeg);
        // (10.636 + 4.0) °/h / 4 = 3.66° per quarter hour.
        Assert.Equal(-3.66, drift, 1);

        var eq = new C172Panel { LatitudeDeg = 0 };
        eq.Start(x, gyrosRunning: true);
        start = eq.Dg.CardDeg;
        for (double t = 0; t < 900; t += 0.05) eq.Step(x, 0.05);
        Assert.Equal(-1.0, Diff(start, eq.Dg.CardDeg), 1);    // the unbalance alone at the equator
    }

    [Fact]
    public void TheKnobSetsTheDgToTheCompassAndItFollowsATurn()
    {
        var p = Running(0);
        double card0 = p.Dg.CardDeg;
        p.Dg.TurnKnob(Euler(0, 0, 0), 25);
        Assert.Equal(25.0, Diff(card0, p.Dg.CardDeg), 3);
        Assert.Equal(25.0, p.Dg.KnobDeg, 3);
        p.Dg.SetTo(Euler(0, 0, 0), p.Compass.ReadingDeg);
        Assert.Equal(0.0, Diff(p.Compass.ReadingDeg, p.Dg.CardDeg), 3);

        double hdg = FlyTurn(p, 0, 90);
        for (double t = 0; t < 2; t += Dt) p.Step(PanelInputs.Level(hdg, 1500), Dt);
        // The gimbals move in their races for the whole 30 s turn (the outer axis is banked): 3 °/min of friction
        // precession is up to 1.5°, which is why a DG wants checking after manoeuvring.
        Assert.InRange(Diff(90, p.Dg.CardDeg), -2.5, 2.5);
    }

    [Fact]
    public void ADeadDgTurnsWithTheAeroplane()
    {
        var p = Running(0);
        p.Failures.VacuumPump = true;
        var x = PanelInputs.Level(0, 1500);
        for (double t = 0; t < 1800; t += 0.05) p.Step(x, 0.05);
        Assert.True(p.Dg.Rotor.SpeedFraction < 0.05, $"DG rotor after 30 min {p.Dg.Rotor.SpeedFraction:F3}");
        double before = p.Dg.CardDeg;
        FlyTurn(p, 0, 90);
        Assert.True(Math.Abs(Diff(before, p.Dg.CardDeg)) < 20, $"a dead DG still showed {Diff(before, p.Dg.CardDeg):F1}° of a 90° turn");
    }

    // ---------------------------------------------------------------- magnetic compass

    [Fact]
    public void DipFromTheDipoleIsSixtyThreeDegreesAt45North()
    {
        Vec3 b = Earth.MagneticFieldNed(45);
        Assert.Equal(63.43, Math.Atan2(b.Z, b.X) / D, 2);
    }

    [Fact]
    public void CompassReadsTheHeadingLevelWithASmallDeviation()
    {
        double worst = 0;
        for (int h = 0; h < 360; h += 30)
        {
            var p = new C172Panel();
            p.Start(PanelInputs.Level(h, 1500), gyrosRunning: true);
            for (double t = 0; t < 10; t += Dt) p.Step(PanelInputs.Level(h, 1500), Dt);
            worst = Math.Max(worst, Math.Abs(Diff(h, p.Compass.ReadingDeg)));
        }
        Assert.InRange(worst, 0.5, 3.0);    // there IS a deviation card, and it is a small one
    }

    [Fact]
    public void TurningFromNorthTheCompassFirstShowsTheOppositeTurn()
    {
        var p = Running(0);
        double hdg = 0;
        for (double t = 0; t < 3; t += Dt) { hdg = 3 * t; p.Step(Turn(hdg, 3), Dt); }
        // 9° of right turn so far; a whisky compass at 63° dip is swinging the other way (UNOS: undershoot north).
        Assert.True(Diff(0, p.Compass.ReadingDeg) < 0, $"turning right off north the card read {p.Compass.ReadingDeg:F1}");
    }

    [Fact]
    public void TurningThroughSouthTheCompassLeads()
    {
        var p = Running(180);
        double hdg = 180;
        for (double t = 0; t < 3; t += Dt) { hdg = 180 + 3 * t; p.Step(Turn(hdg, 3), Dt); }
        Assert.True(Diff(hdg, p.Compass.ReadingDeg) > 5, $"true {hdg:F1}, card {p.Compass.ReadingDeg:F1}");
    }

    [Fact]
    public void AcceleratingEastShowsATurnToTheNorthAndDeceleratingToTheSouth()
    {
        double After(double hdg, double accel)
        {
            var p = Running(hdg);
            var x = PanelInputs.Level(hdg, 1500);
            x.SpecificForceBody = new Vec3(accel, 0, -Earth.G);
            for (double t = 0; t < 3; t += Dt) p.Step(x, Dt);
            return p.Compass.ReadingDeg;
        }
        Assert.True(Diff(90, After(90, 2.0)) < -3, "ANDS: accelerate east, card swings toward north");
        Assert.True(Diff(90, After(90, -2.0)) > 3, "ANDS: decelerate east, card swings toward south");
        Assert.True(Diff(270, After(270, 2.0)) > 3, "accelerate west, card swings toward north (up from 270)");
        Assert.InRange(Diff(0, After(0, 2.0)), -0.5, 0.5);        // none on a northerly heading
    }

    // ---------------------------------------------------------------- turn coordinator

    [Fact]
    public void StandardRateAtNinetyKnotsPutsTheWingOnTheMark()
    {
        var p = Running(0);
        for (double t = 0; t < 5; t += Dt) p.Step(Turn(3 * t, 3), Dt);
        Assert.Equal(1.0, p.Tc.Needle, 2);
        for (double t = 0; t < 5; t += Dt) p.Step(Turn(15 - 3 * t, -3), Dt);
        Assert.Equal(-1.0, p.Tc.Needle, 2);
        Assert.InRange(p.Tc.Ball, -0.01, 0.01);
    }

    [Fact]
    public void TheCantMakesTheNeedleMoveOnTheRollIn()
    {
        var p = Running(0);
        var x = PanelInputs.Level(0, 1500);
        x.RatesBody = new Vec3(10 * D, 0, 0);   // rolling right at 10 °/s, no yaw yet
        for (double t = 0; t < 2; t += Dt) p.Step(x, Dt);
        // p·sin 30° = 5 °/s of input against 3·cos 13.9°·cos 30° = 2.52 °/s at the mark → 1.98, at the 2.0 stop.
        Assert.InRange(p.Tc.Needle, 1.9, 2.0);
    }

    [Fact]
    public void TheTurnCoordinatorIgnoresTheVacuumAndDiesWithTheBus()
    {
        var p = Running(0);
        p.Failures.VacuumPump = true;
        for (double t = 0; t < 600; t += 0.05) p.Step(Turn(3 * t, 3), 0.05);
        Assert.Equal(1.0, p.Tc.Needle, 2);
        Assert.False(p.Tc.FlagInView);

        p.Failures.Electrical = true;
        p.Step(Turn(0, 3), 0.05);
        Assert.True(p.Tc.FlagInView);
        for (double t = 0; t < 180; t += 0.05) p.Step(Turn(3 * t, 3), 0.05);
        Assert.Equal(Math.Exp(-1), p.Tc.Needle, 2);    // the rotor coasts down on its 180 s time constant
    }

    [Fact]
    public void TheBallHangsAwayFromTheSideForce()
    {
        Assert.Equal(-0.476, TurnCoordinator.BallTarget(new Vec3(0, 0.1 * Earth.G, -Earth.G)) / TurnCoordinator.BallArcDeg, 3);
        Assert.Equal(0.0, TurnCoordinator.BallTarget(new Vec3(0, 0, -Earth.G / Math.Cos(30 * D))), 6);
    }

    // ---------------------------------------------------------------- NAV receivers

    private static Vec3 At(double x, double y, double altM) => new(x, y, -altM);

    [Fact]
    public void VorCdiToFromAndTheAbeamFlag()
    {
        Vor vly = Navaids.ById("VLY");
        var nav = new NavReceiver(true, vly.FreqMhz, 90);
        Vec3 pos = At(vly.X, vly.Y + 10000, 1500);   // 10 km east: the 090 radial
        nav.Settle(pos, 14, null);
        Assert.Equal(-1, nav.ToFrom);
        Assert.Equal(0.0, nav.Cdi, 3);

        nav.ObsDeg = 95; nav.Settle(pos, 14, null);
        Assert.Equal(0.5, nav.Cdi, 2);           // 5° of a 10° full scale, course to the right (south, flying east)
        nav.ObsDeg = 270; nav.Settle(pos, 14, null);
        Assert.Equal(1, nav.ToFrom);
        Assert.Equal(0.0, nav.Cdi, 3);

        nav.ObsDeg = 0; nav.Settle(pos, 14, null);
        Assert.True(nav.Flag, "abeam the station the TO and FROM signals are equal: OFF");
        Assert.Equal(0.0, nav.Cdi, 3);

        nav.ObsDeg = 90;
        nav.Settle(At(vly.X, vly.Y, vly.AntennaM + 900), 14, null);
        Assert.True(nav.Flag, "overhead the antenna's null flags the receiver");

        nav.Settle(pos, 9, null);
        Assert.True(nav.Flag, "below 10 V the receiver is off");
    }

    [Fact]
    public void LocalizerAndGlideslopeCentredOnTheIlsAndFlyUpBelowIt()
    {
        Ils ils = Approaches.All.First(a => a.Localizer.HasValue && a.Localizer.Value.Id == "I-VLY").Localizer!.Value;
        var site = NavReceiver.SiteOf(ils);
        // Course 270: final is to the EAST of the threshold. 4 NM out on the centreline.
        double x = ils.ThresholdX, y = ils.ThresholdY + 4 * 1852;
        double horiz = Math.Sqrt((x - site.GsX) * (x - site.GsX) + (y - site.GsY) * (y - site.GsY));
        double onPath = ils.TdzeM + horiz * Math.Tan(3 * D);
        var nav1 = new NavReceiver(true, ils.FreqMhz, 0);
        nav1.Settle(At(x, y, onPath), 14, null);
        Assert.False(nav1.Flag);
        Assert.False(nav1.GsFlag);
        Assert.Equal(0.0, nav1.Cdi, 3);
        Assert.Equal(0.0, nav1.Gs, 3);

        nav1.Settle(At(x, y, onPath - 60), 14, null);
        Assert.True(nav1.Gs > 0.3, $"60 m low: needle {nav1.Gs:F2} should be well up");

        // South of the centreline (to the LEFT of an aeroplane flying west): the course is to the right.
        nav1.Settle(At(x - 100, y, onPath), 14, null);
        Assert.True(nav1.Cdi > 0.1, $"left of the localizer, CDI {nav1.Cdi:F2}");

        var nav2 = new NavReceiver(false, ils.FreqMhz, 0);
        nav2.Settle(At(x, y, onPath - 60), 14, null);
        Assert.False(nav2.Flag);
        Assert.True(nav2.GsFlag);
        Assert.Equal(0.0, nav2.Gs, 6);
    }

    [Fact]
    public void ThresholdCrossingHeightComesFromTheAntennaSite()
    {
        Ils ils = Approaches.All.First(a => a.Localizer.HasValue && a.Localizer.Value.Id == "I-VLY").Localizer!.Value;
        var nav1 = new NavReceiver(true, ils.FreqMhz, 0);
        // Half a mile out the elevation from the antenna site must equal 3.00° at the height the geometry gives.
        double x = ils.ThresholdX, y = ils.ThresholdY + 926;
        var site = NavReceiver.SiteOf(ils);
        double horiz = Math.Sqrt((x - site.GsX) * (x - site.GsX) + (y - site.GsY) * (y - site.GsY));
        nav1.Settle(At(x, y, ils.TdzeM + horiz * Math.Tan(3 * D)), 14, null);
        Assert.Equal(0.0, nav1.RawGsDeg, 6);
        // And over the threshold itself the path is √(305² + 120²) × tan 3° = 17.18 m up.
        Assert.Equal(17.18, Math.Sqrt(305.0 * 305.0 + 120.0 * 120.0) * Math.Tan(3 * D), 2);
        Assert.Equal(305.0, Math.Abs(site.GsY - ils.ThresholdY), 6);
        Assert.Equal(120.0, Math.Abs(site.GsX - ils.ThresholdX), 6);
        // Tailored: ±350 ft at the threshold, no wider than ±3°.
        Assert.InRange(site.HalfWidthDeg, 1.5, 3.0);
    }

    // ---------------------------------------------------------------- DME

    [Fact]
    public void DmeReadsSlantRangeAndSearchesBeforeItLocks()
    {
        Vor vly = Navaids.ById("VLY");
        var dme = new DmeUnit();
        Vec3 over = At(vly.X, vly.Y, vly.AntennaM + 1828.8);
        dme.Step(1.0, vly.FreqMhz, 0, over, 14, null);
        Assert.False(dme.Valid);                          // still searching
        dme.Step(1.5, vly.FreqMhz, 0, over, 14, null);
        Assert.True(dme.Valid);
        Assert.Equal(0.9875, dme.DistanceNm, 4);

        // HOLD keeps the channel through a NAV1 retune.
        dme.Source = DmeSource.Hold;
        dme.Step(0.1, 113.40, 0, over, 14, null);
        Assert.Equal("VLY", dme.Ident);
        Assert.True(dme.Valid);
    }

    [Fact]
    public void DmeGroundSpeedIsTheClosureRate()
    {
        Vor vly = Navaids.ById("VLY");
        double v = 100 * 1852.0 / 3600.0;
        // Inbound from 10 NM east at 100 kt, low over the station's height.
        var dme = new DmeUnit();
        double y = vly.Y + 10 * 1852;
        for (double t = 0; t < 40; t += 0.1) { dme.Step(0.1, vly.FreqMhz, 0, At(vly.X, y, vly.AntennaM + 300), 14, null); y -= v * 0.1; }
        Assert.Equal(100.0, dme.GroundSpeedKt, 0);
        Assert.Equal(dme.DistanceNm / 100.0 * 60.0, dme.TimeToStationMin, 1);

        // Passing 5 NM abeam: at the closest point the range is not changing, so the "ground speed" is near zero.
        dme = new DmeUnit();
        for (double t = 0; t <= 200; t += 0.1)
        {
            double py = vly.Y + v * (t - 200);
            dme.Step(0.1, vly.FreqMhz, 0, At(vly.X + 5 * 1852, py, vly.AntennaM + 300), 14, null);
        }
        Assert.True(dme.GroundSpeedKt < 12, $"abeam GS {dme.GroundSpeedKt:F1}");
    }

    // ---------------------------------------------------------------- ADF

    [Fact]
    public void AdfPointsAtTheBeaconFromTheNoseAndTheCardIsTheKnobs()
    {
        Ndb bn = Navaids.NdbById("BN");
        var adf = new AdfReceiver(bn.FreqKhz);
        Vec3 pos = At(bn.X - 5000, bn.Y, 600);     // due south of it
        adf.Settle(Euler(0, 0, 0), pos, 14, null);
        Assert.True(adf.Receiving);
        Assert.Equal(0.0, Diff(0, adf.NeedleDeg), 1);
        adf.Settle(Euler(90, 0, 0), pos, 14, null);
        Assert.Equal(0.0, Diff(270, adf.NeedleDeg), 1);
        adf.CardDeg = 90;                               // heading set under the index: needle on the card = bearing
        Assert.Equal(0.0, Diff(0, adf.NeedleOnCardDeg), 1);

        // Unpowered, the needle stays where it was.
        double was = adf.NeedleDeg;
        adf.Step(1.0, Euler(0, 0, 0), pos, 0, null);
        Assert.Equal(was, adf.NeedleDeg, 6);
    }

    [Fact]
    public void BankedTheLoopReadsInTheAirframesPlane()
    {
        // Beacon 45° right of the nose, wings banked 30° right: atan(tan 45° · cos 30°) = 40.89°.
        Quat q = Euler(0, 0, 30);
        double brg = 45 * D;
        Vec3 pos = Vec3.Zero;
        double rb = AdfReceiver.LoopBearingDeg(q, pos, Math.Cos(brg) * 5000, Math.Sin(brg) * 5000, 0);
        Assert.Equal(40.89, rb, 2);
    }

    // ---------------------------------------------------------------- the rig contract

    [Fact]
    public void EveryPanelValueIsARigParam()
    {
        string[] names =
        {
            "suction_inhg", "ai_pitch_deg", "ai_bank_deg", "dg_card_deg", "dg_knob_deg",
            "compass_card_deg", "compass_card_pitch_deg", "compass_card_roll_deg", "tc_needle", "tc_flag", "slip_ball",
            "nav1_cdi", "nav1_gs", "nav1_tofrom", "nav1_flag", "nav1_gs_flag", "nav1_obs_deg", "nav1_freq_mhz",
            "nav2_cdi", "nav2_tofrom", "nav2_flag", "nav2_obs_deg", "nav2_freq_mhz",
            "adf_needle_deg", "adf_card_deg", "adf_freq_khz", "dme_nm", "dme_kt", "dme_min", "dme_valid",
            "ai_rotor", "dg_rotor", "tc_rotor",
        };
        var p = Running(30);
        var r = new RigInputs();
        p.WriteRig(ref r);
        foreach (string n in names) Assert.True(r.TryGet(n, out _), n);
        Assert.True(r.TryGet("dg_card_deg", out double dg));
        Assert.Equal(p.Dg.CardDeg, dg, 9);
        Assert.True(r.TryGet("suction_inhg", out double suction));
        Assert.Equal(5.0, suction, 3);
    }

    // ---------------------------------------------------------------- the own 172's rig table

    /// <summary>
    /// docs/models/c172-like-own-rig.json binds Codex's panel to these instruments. The node list and the
    /// sign-carrying map points are written out here by hand, so a regenerated table that renames a node,
    /// drops one, or reverses a card has to be changed in two places on purpose.
    /// </summary>
    [Fact]
    public void TheOwn172RigBindsEveryInstrumentNodeToAParamTheSimSupplies()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "c172-like-own-rig.json");
        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal("c172-like-own", root.GetProperty("model").GetString());
        var rows = root.GetProperty("rig").EnumerateArray().ToDictionary(r => r.GetProperty("node").GetString()!, r => r);
        var want = new Dictionary<string, string>
        {
            ["C172_AI_roll"] = "ai_bank_deg", ["C172_AI_pitch"] = "ai_pitch_deg",
            ["C172_DG_card"] = "dg_card_deg", ["C172_DG_sync_adjust"] = "dg_knob_deg",
            ["C172_TC_airplane"] = "tc_needle", ["C172_TC_slip_ball"] = "slip_ball",
            ["C172_NAV1_CDI"] = "nav1_cdi", ["C172_NAV1_GS"] = "nav1_gs", ["C172_NAV1_OBS_card"] = "nav1_obs_deg",
            ["C172_NAV1_OBS"] = "nav1_obs_deg", ["C172_NAV1_NAV_flag"] = "nav1_flag", ["C172_NAV1_TO_FROM_flag"] = "nav1_tofrom",
            ["C172_NAV1_GS_flag"] = "nav1_gs_flag",
            ["C172_NAV2_CDI"] = "nav2_cdi", ["C172_NAV2_OBS_card"] = "nav2_obs_deg", ["C172_NAV2_OBS"] = "nav2_obs_deg",
            ["C172_NAV2_NAV_flag"] = "nav2_flag", ["C172_NAV2_TO_FROM_flag"] = "nav2_tofrom",
            ["C172_ADF_bearing"] = "adf_needle_deg", ["C172_ADF_card_adjust"] = "adf_card_deg",
            ["C172_Compass_card"] = "compass_card_deg", ["C172_Suction_needle"] = "suction_inhg",
            ["C172_Airspeed_needle"] = "airspeed_mph", ["C172_Altimeter_needle"] = "altitude_ft",
            ["C172_Altimeter_1000"] = "altitude_ft", ["C172_Altimeter_10000"] = "altitude_ft",
            ["C172_VSI_needle"] = "vsi_fpm", ["C172_Tach_needle"] = "rpm", ["C172_Oil_press_needle"] = "oil_press_psi",
            ["C172_Oil_temp_needle"] = "oil_temp_f", ["C172_Fuel_L_needle"] = "fuel_centre_frac", ["C172_Fuel_R_needle"] = "fuel_centre_frac",
            ["C172_Throttle"] = "throttle", ["C172_Mixture"] = "mixture",
        };
        Assert.Equal(want.Count, rows.Count);
        var r = new RigInputs();
        foreach (var (node, param) in want)
        {
            Assert.True(rows.ContainsKey(node), node);
            Assert.Equal(param, rows[node].GetProperty("param").GetString());
            Assert.True(r.TryGet(param, out _), $"{node}: the sim supplies no '{param}'");
        }
        // The signs, from the drawn faces: a card under a top lubber turns AGAINST its reading, the horizon
        // AGAINST the bank, a needle WITH its value; the CDI slides right (glTF +Z) and the GS needle up (+Y).
        double ValueAt(string node, int i) => rows[node].GetProperty("map")[i][1].GetDouble();
        Assert.Equal(-360.0, ValueAt("C172_DG_card", 1));
        Assert.Equal(-180.0, ValueAt("C172_AI_roll", 1));
        Assert.Equal(395.0, ValueAt("C172_ADF_bearing", 1));
        Assert.Equal(1.0, rows["C172_NAV1_CDI"].GetProperty("axis_gltf")[2].GetDouble());
        Assert.Equal(1.0, rows["C172_NAV1_GS"].GetProperty("axis_gltf")[1].GetDouble());
        Assert.Equal(1.0, rows["C172_Compass_card"].GetProperty("axis_gltf")[1].GetDouble());
        Assert.Equal("C172_AI_roll", rows["C172_AI_pitch"].GetProperty("parent").GetString());
        // Fixed-pitch trainer plungers push 60 mm into the panel; neither can rotate.
        foreach (string node in new[] { "C172_Throttle", "C172_Mixture" })
        {
            Assert.Equal("move", rows[node].GetProperty("kind").GetString());
            Assert.Equal(1.0, rows[node].GetProperty("axis_gltf")[0].GetDouble());
            Assert.Equal(0.0, ValueAt(node, 0));
            Assert.Equal(0.06, ValueAt(node, 1));
        }
        Assert.DoesNotContain(rows.Keys, n => n.Contains("Prop", StringComparison.OrdinalIgnoreCase));
    }
}
