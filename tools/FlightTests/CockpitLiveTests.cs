using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The painted cockpit parts made live (Ty on the Quest, 2026-11-02: "none of the cockpit gauges move", "the stick
/// and rudder pedals don't move", "make the cockpit throttle lever move with the throttle"), the throttle readout,
/// and the headset's catch-up cap. Expected values are literals copied from the sources named beside them — the
/// EA 300 rig (docs/models/ea300-own-rig.json), the Great Lakes rig (great-lakes-own-rig.json) and the Pitts build
/// script — never read back from the table under test.
/// </summary>
public sealed class CockpitLiveTests
{
    private static LiveDial Dial(CockpitLiveSpec s, string face) => s.Dials.Single(d => d.Face == face);

    // ---- the needles land where the dial art says ----

    [Fact]
    public void AirspeedNeedlesFollowTheRigMaps()
    {
        // ea300-own-rig.json Needle_ASI_R: [[0,0],[40,25],[260,335]], ang0 0.
        DialNeedle kt = CockpitLive.AirspeedKt.Needles[0];
        Assert.Equal("airspeed_kias", kt.Param);
        Assert.Equal(0.0, kt.AngleDeg(0), 6);
        Assert.Equal(25.0, kt.AngleDeg(40), 6);
        Assert.Equal(180.0, kt.AngleDeg(150), 6);          // 25 + (110/220)·310
        Assert.Equal(335.0, kt.AngleDeg(400), 6);          // pegged
        // great-lakes-own-rig.json Needle_ASI_R: [[0,0],[40,30],[200,330]] mph.
        DialNeedle mph = CockpitLive.AirspeedMph.Needles[0];
        Assert.Equal("airspeed_mph", mph.Param);
        Assert.Equal(30.0, mph.AngleDeg(40), 6);
        Assert.Equal(180.0, mph.AngleDeg(120), 6);
        Assert.Equal(330.0, mph.AngleDeg(250), 6);
    }

    [Fact]
    public void AltimeterHandsGoRoundAndRound()
    {
        // Needle_Alt100_R [[0,0],[1000,360]] and Needle_Alt1000_R [[0,0],[10000,360]], both extrapolated.
        var alt = CockpitLive.Altimeter;
        DialNeedle hand = alt.Needles.Single(n => n.Shape == NeedleShape.Hand);
        DialNeedle pointer = alt.Needles.Single(n => n.Shape == NeedleShape.Pointer);
        Assert.Equal(90.0, pointer.AngleDeg(250), 6);
        Assert.Equal(360.0 * 3.5, pointer.AngleDeg(3500), 6);      // three and a half turns, not pegged at one
        Assert.Equal(126.0, hand.AngleDeg(3500), 6);
        Assert.Equal(-36.0, pointer.AngleDeg(-100), 6);             // below sea level it turns back
    }

    [Fact]
    public void GMeterReadsFromNineOClockWithTellTales()
    {
        // Needle_G_R [[-10,-120],[0,0],[10,120]], ang0 -90: 0 g at 9 o'clock, +1 g twelve degrees above it.
        var g = CockpitLive.GMeter;
        DialNeedle main = g.Needles.Single(n => n.Param == "g_load");
        Assert.Equal(-90.0, main.AngleDeg(0), 6);
        Assert.Equal(-78.0, main.AngleDeg(1), 6);
        Assert.Equal(30.0, main.AngleDeg(10), 6);
        Assert.Equal(-210.0, main.AngleDeg(-10), 6);
        Assert.Contains(g.Needles, n => n.Param == "g_max_seen" && n.Shape == NeedleShape.TellTale);
        Assert.Contains(g.Needles, n => n.Param == "g_min_seen" && n.Shape == NeedleShape.TellTale);
        // The main needle is drawn last, on top of its tell-tales.
        Assert.Equal("g_load", g.Needles[^1].Param);
    }

    [Fact]
    public void EngineDialsStartAtTheirArtsZero()
    {
        // Needle_Tach_R ang0 -150, [[0,0],[3500,300]]; Needle_MP_R ang0 -120, [[10,0],[35,300]];
        // Needle_OilP_R ang0 -80, [[0,0],[120,160]]; Needle_OilT_R ang0 100, [[100,0],[250,160]].
        Assert.Equal(-150.0, CockpitLive.Tachometer.Needles[0].AngleDeg(0), 6);
        Assert.Equal(90.0, CockpitLive.Tachometer.Needles[0].AngleDeg(2800), 6);
        LiveDial mp = CockpitLive.Manifold("Oil_temperature");
        Assert.Equal("RearPanel_Oil_temperature_Face", mp.Face);
        Assert.Equal(-120.0, mp.Needles[0].AngleDeg(5), 6);
        Assert.Equal(60.0, mp.Needles[0].AngleDeg(25), 6);
        LiveDial oil = CockpitLive.Oil("Oil_pressure");
        Assert.Equal(0.0, oil.Needles.Single(n => n.Param == "oil_press_psi").AngleDeg(60), 6);
        Assert.Equal(180.0, oil.Needles.Single(n => n.Param == "oil_temp_f").AngleDeg(175), 6);
    }

    [Fact]
    public void TheCompassCardAndTheHorizonTurnTheOtherWay()
    {
        LiveDial compass = CockpitLive.Compass;
        Assert.Equal("heading_deg", compass.CardParam);
        Assert.Equal(-90.0, compass.CardDeg(90), 6);     // heading east: the card's E comes round to the lubber line
        DialNeedle lubber = compass.Needles.Single();
        Assert.Equal(NeedleShape.Lubber, lubber.Shape);
        Assert.Equal(0.0, lubber.AngleDeg(123), 6);      // fixed at 12 o'clock whatever it is fed
        LiveDial ai = CockpitLive.AttitudeBank;
        Assert.Equal("bank_deg", ai.CardParam);
        Assert.Equal(-30.0, ai.CardDeg(30), 6);          // bank right: the horizon card turns left
    }

    // ---- which model gets what ----

    [Fact]
    public void ThePittsGetsSevenRearAndFourFrontLiveDials()
    {
        CockpitLiveSpec p = CockpitLive.For("pitts-s2b-like-own");
        // rear_panel.py LAYOUTS['pitts-s2b-like']: seven dials.
        Assert.Equal(11, p.Dials.Count);
        foreach (string name in new[] { "Airspeed", "Altimeter", "G_meter", "Tachometer" })
        {
            var front = Dial(p, $"FrontPanel_{name}_Face");
            var rear = Dial(p, $"RearPanel_{name}_Face");
            Assert.Equal(rear.Art, front.Art);
            // The same needle maps (CockpitLive's dials are built fresh on every read, so compare them, not the instances).
            Assert.Equal(rear.Needles.Select(n => (n.Param, n.Ang0Deg, n.Shape)), front.Needles.Select(n => (n.Param, n.Ang0Deg, n.Shape)));
            foreach (double at in new[] { 0.0, 1.0, 55.0, 120.0, 2500.0 })
                Assert.Equal(rear.Needles.Select(n => n.AngleDeg(at)), front.Needles.Select(n => n.AngleDeg(at)));
        }
        foreach (string n in new[] { "Airspeed", "Altimeter", "G_meter", "Tachometer", "Oil_pressure", "Oil_temperature", "Compass" })
            Assert.Contains(p.Dials, d => d.Face == $"RearPanel_{n}_Face");
        Assert.Equal("gl-dial-asi", Dial(p, "RearPanel_Airspeed_Face").Art);     // the Pitts' ASI is in mph
        Assert.Equal(2, p.Sticks.Count);
        Assert.Equal(4, p.Pedals.Count);
        Assert.Equal(2, p.Pedals.Count(x => x.Right));
        Assert.Single(p.Throttles);
        Assert.Equal(80, p.StickTris);
        Assert.Equal(12, p.PedalTris);
    }

    [Fact]
    public void TheExtraHidesItsOrphanedRearNeedlesAndKeepsItsRig()
    {
        CockpitLiveSpec e = CockpitLive.For("extra-300-like-own");
        Assert.Equal(9, e.Dials.Count);
        Assert.Equal("ea-dial-asi", Dial(e, "RearPanel_Airspeed_Face").Art);
        Assert.Contains("Needle_ASI_R", e.Hide);
        Assert.Contains("TC_Ball_R", e.Hide);
        Assert.DoesNotContain("Needle_ASI_F", e.Hide);    // the front panel's needles sit on their dials and stay
        Assert.Empty(e.Sticks);                           // its rig already moves its sticks, pedals and throttles
        Assert.Empty(e.Throttles);
    }

    [Fact]
    public void ModelsWithoutATableAreLeftAlone()
    {
        Assert.True(CockpitLive.For("great-lakes-like-own").IsEmpty);
        Assert.True(CockpitLive.For("glider-2-33-like-own").IsEmpty);
        Assert.True(CockpitLive.For("").IsEmpty);
    }

    // ---- the cut volumes, against the Pitts build script ----

    [Fact]
    public void PittsStickVolumesHoldTheShaftAndGripButNotTheBoot()
    {
        // build_pitts_s2b.py ck_stick(-0.93, ZFL = -0.58, 0.55), glTF x = -builder x, y = builder z.
        StickCut rear = CockpitLive.Pitts().Sticks.Single(s => s.Name == "Stick_R");
        Assert.True(rear.Contains(0.93 + 0.01, -0.53, 0.0));         // shaft foot, on its radius
        Assert.True(rear.Contains(0.975, -0.12, 0.0));               // shaft top
        Assert.True(rear.Contains(0.985, -0.03, 0.016));             // grip top, on its radius
        Assert.False(rear.Contains(0.93 + 0.034, -0.55, 0.0));       // the boot (r 0.034) stays in the floor
        Assert.False(rear.Contains(0.93, -0.58, 0.0));               // the floor
    }

    [Fact]
    public void PittsPedalBoxesSitEitherSideOfTheCentreline()
    {
        // ck_box('ck_stick', (xa + 0.30, ±0.13, ZFL + 0.10), ...) for the rear pedals: glTF (0.80, -0.48, ±0.13).
        var pedals = CockpitLive.Pitts().Pedals;
        PedalCut rl = pedals.Single(p => p.Name == "Pedal_R_L"), rr = pedals.Single(p => p.Name == "Pedal_R_R");
        Assert.True(rl.Contains(0.80, -0.48, 0.13));
        Assert.True(rr.Contains(0.80, -0.48, -0.13));
        Assert.False(rl.Right);                                       // +z is the pilot's LEFT in glTF
        Assert.True(rr.Right);
        Assert.False(rl.Contains(0.80, -0.48, -0.13));
        Assert.False(rl.Contains(0.80, -0.585, 0.13));                // the cockpit floor
    }

    // ---- motion signs ----

    [Fact]
    public void StickAftLeansTheStickBackAndRightRollLeansItRight()
    {
        var aft = new RigInputs { Elevator = 1.0 };
        var fwd = new RigInputs { Elevator = -1.0 };
        Assert.Equal(-18.0, CockpitLive.StickPitchFwdDeg(aft), 6);   // top AFT
        Assert.Equal(12.0, CockpitLive.StickPitchFwdDeg(fwd), 6);    // top forward, less travel
        Assert.Equal(18.0, CockpitLive.StickRightDeg(new RigInputs { Aileron = 1.0 }), 6);
        Assert.Equal(-9.0, CockpitLive.StickRightDeg(new RigInputs { Aileron = -0.5 }), 6);
    }

    [Fact]
    public void StickAftComesFromTheSimsTrailingEdgeUpElevator()
    {
        // The sim's elevator is + trailing-edge DOWN (nose down); RigInputs.FromDeflections owns the flip.
        RigInputs r = RigInputs.FromDeflections(0, -0.2, 0, 0, 0.4, 0.4, 0.4);    // TE up = stick aft
        Assert.True(CockpitLive.StickPitchFwdDeg(r) < 0);
    }

    [Fact]
    public void RightRudderPushesTheRightPedalForward()
    {
        var right = new RigInputs { Rudder = 1.0 };
        Assert.Equal(15.0, CockpitLive.PedalFwdDeg(right, right: true), 6);
        Assert.Equal(-15.0, CockpitLive.PedalFwdDeg(right, right: false), 6);
        Assert.Equal(0.0, CockpitLive.PedalFwdDeg(new RigInputs(), right: true), 6);
    }

    [Fact]
    public void TheThrottleLeverIsForwardForPower()
    {
        Assert.Equal(-25.0, CockpitLive.ThrottleFwdDeg(new RigInputs { Throttle = 0.0 }), 6);
        Assert.Equal(25.0, CockpitLive.ThrottleFwdDeg(new RigInputs { Throttle = 1.0 }), 6);
        Assert.Equal(0.0, CockpitLive.ThrottleFwdDeg(new RigInputs { Throttle = 0.5 }), 6);
    }

    [Fact]
    public void BankAndPitchAreReadableByName()
    {
        var r = new RigInputs { BankDeg = 30, PitchDeg = -5 };
        Assert.True(r.TryGet("bank_deg", out double b)); Assert.Equal(30.0, b);
        Assert.True(r.TryGet("pitch_deg", out double p)); Assert.Equal(-5.0, p);
    }

    // ---- the power readout ----

    [Fact]
    public void PowerReadoutShowsWhileTheLeverMovesThenFades()
    {
        Assert.Equal(1.0, QuestCockpit.PowerOpacity(0.0), 6);
        Assert.Equal(1.0, QuestCockpit.PowerOpacity(1.5), 6);
        Assert.Equal(0.5, QuestCockpit.PowerOpacity(1.75), 6);
        Assert.Equal(0.0, QuestCockpit.PowerOpacity(2.0), 6);
        Assert.Equal(0.0, QuestCockpit.PowerOpacity(-1.0), 6);
        Assert.True(QuestCockpit.LeverMoved(0.50, 0.51));
        Assert.False(QuestCockpit.LeverMoved(0.500, 0.501));          // the sim's own rounding never wakes it
    }

    [Fact]
    public void PowerLabelSaysPowerOrAirbrake()
    {
        Assert.Equal("POWER 65 %", QuestCockpit.PowerLabel(0.649, glider: false));
        Assert.Equal("POWER 100 %", QuestCockpit.PowerLabel(1.3, glider: false));
        Assert.Equal("AIRBRAKE 40 %", QuestCockpit.PowerLabel(0.4, glider: true));
        Assert.Contains("left stick", QuestCockpit.PowerHint);
        Assert.Contains("left grip", QuestCockpit.PowerHint);
    }

    // ---- the headset's catch-up cap ----

    [Fact]
    public void InAHeadsetTheSimCatchesUpOneDroppedFrameAndNoMore()
    {
        Assert.Equal(2.0 / 72.0, SimLoop.HeadsetCatchUpSec(72), 9);
        Assert.Equal(2.0 / 90.0, SimLoop.HeadsetCatchUpSec(90), 9);
        Assert.Equal(SimLoop.MaxCatchUpSec, SimLoop.HeadsetCatchUpSec(0), 9);   // the runtime will not say: the phone's cap
        Assert.Equal(SimLoop.MaxCatchUpSec, SimLoop.HeadsetCatchUpSec(30), 9);  // never looser than the phone's
    }

    [Fact]
    public void ACappedFrameRunsAtMostTheCapAndSaysSo()
    {
        var cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pitts-s2b-like.json"));
        var loop = new SimLoop(new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -1000), Quat.Identity, new Vec3(50, 0, 0), Vec3.Zero), ControlDeflections.Neutral));
        double acc = 0, cap = SimLoop.HeadsetCatchUpSec(72);
        var inputs = new ControlInputs(0, 0, 0, 0);
        double x0 = loop.Aircraft.State.Position.X;
        Assert.False(loop.Advance(1.0 / 72.0, inputs, ref acc, cap));             // a normal frame
        bool capped = loop.Advance(0.100, inputs, ref acc, cap);                   // a 100 ms hitch
        Assert.True(capped);
        Assert.True(acc < SimLoop.DefaultFixedDtSec);
        // About 1/72 + 2/72 s flown at 50 m/s, not 1/72 + 0.1 s.
        double flown = loop.Aircraft.State.Position.X - x0;
        Assert.InRange(flown, 50 * (3.0 / 72.0) - 0.6, 50 * (3.0 / 72.0) + 0.6);
    }
}
