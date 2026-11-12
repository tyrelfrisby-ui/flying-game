using System;
using System.Collections.Generic;
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
/// THE THREE NAMED RECOVERY PROCEDURES, pinned as measured on 2026-11-11 — INCLUDING WHERE THE MODEL IS WRONG.
///
/// Ty, 2026-11-11, gave the procedures and an acceptance rule for each:
///   "Beggs mueller is throttle idle let go of the stick (completely hands off) then step on the hard pedal which
///    is always the correct recovery rudder fully."
///   "The elevator matters too — both must come out."   (asked whether recovering the flat spin needs the aileron
///    out or the elevator too)
///   "The power idle all controls neutral recover will be called 'dart recovery' and it should also pass on all
///    airplanes even the decathlon."   (and on the ambiguity: rudder neutral too, everything centred)
///   "The only airplane that does not pass the begs mueller recovery I know of in real life that begs mueller
///    does not work on is the decathlon."
///
/// WHAT IS MEASURED AND WHAT IS PINNED. Everything here was flown by <see cref="SpinRecoveryProceduresProbe"/>
/// (RECOVERY=1) from the developed POWER-ON FLAT SPIN — Developed(rudder -1, lever -1, aileron +1), which is Ty's
/// own flat spin and the condition every flat-spin measurement in this repo now uses. "Recovered" means the
/// vertical rotation fell below 20 deg/s, the same test the other spin probes use.
///
/// Two of these assert the model where it DISAGREES with Ty's aeroplane. That is deliberate, and it is the pattern
/// 028b02e3 used: a measured number that is known to be wrong is still worth holding still, because the day
/// someone changes the term that is wrong the test fails and says what the pilot said it should do instead. A pin
/// like that is a receipt, not an endorsement — read the comment on each one.
///
/// Hands off is a FREE stick, not a centred one. A free reversible surface floats to where its hinge moment is
/// zero, which at spin attitude is nowhere near neutral; <see cref="Aircraft.FreeFloat"/> is that path, driven by
/// ControlInputs(..., aileronFree, elevatorFree, rudderFree). Forcing deflections to zero is NEUTRAL and is a
/// different procedure with a different answer — 2.70 turns against Beggs-Mueller's 4.29 on the Pitts.
/// </summary>
public class BeggsAndDartRecoveryTests
{
    private readonly ITestOutputHelper _o;
    public BeggsAndDartRecoveryTests(ITestOutputHelper o) { _o = o; }

    private const double D = 180.0 / Math.PI;
    private const double IDLE = 1.0, FULL = -1.0;
    /// <summary>The flat spin's pro-spin controls: left rudder, stick full aft, out-spin (right) aileron, full power.</summary>
    private const double ProRud = -1.0, ProStick = -1.0, ProAil = 1.0;

    /// <summary>The three shipping types that can be spun (TestFlightFleet's slim set, minus the gliders and Cubs).</summary>
    public static readonly string[] ShippingAerobats = { "pitts-s2b-like", "extra-300-like", "great-lakes-like" };

    private static AircraftConfig Load(string id) => SpinRecoveryProceduresProbe.Load(id);

    /// <summary>The developed power-on flat spin. Each call flies the 45 s entry; a cache keeps one per type.</summary>
    private static readonly Dictionary<string, (SpinBalanceProbe.SpinStats st, Aircraft ac)> _cache = new();
    private static (SpinBalanceProbe.SpinStats st, Aircraft ac) FlatSpin(string id)
    {
        if (!_cache.TryGetValue(id, out var v)) { v = SpinBalanceProbe.Developed(Load(id), ProRud, FULL, aileron: ProAil); _cache[id] = v; }
        return v;
    }

    private static SpinRecoveryProceduresProbe.Outcome FlyFresh(string id, SpinRecoveryProceduresProbe.Proc p)
    {
        var cfg = Load(id);
        var (_, ac) = SpinBalanceProbe.Developed(cfg, ProRud, FULL, aileron: ProAil);
        return SpinRecoveryProceduresProbe.Fly(cfg, ac, p, ProRud);
    }

    /// <summary>
    /// HANDS OFF, THE ELEVATOR GOES TO ITS AFT STOP AND STAYS THERE — on all three shipping aerobats — and that is
    /// RIGHT, because it is Ty's own report of his own aeroplane: "in a real pitts if i let go of the stick in an
    /// upright spin the stick will stay fully aft and slightly inspin aileron" (2026-11-09). The hinge moment at
    /// spin attitude is net destabilising, so <see cref="Aircraft.FreeFloat"/> runs the surface to the stop rather
    /// than to a balance point.
    ///
    /// It is also consistent with the other thing he reports about his own aeroplane — hands off, the stick lurches
    /// FORWARD when the stabiliser's leading-edge flow reattaches. There is no contradiction between the two: the
    /// float is aft while the stabiliser is stalled and swings forward when it reattaches as the nose drops. See
    /// <see cref="FloatingElevatorProbe"/>, which is his acceptance test for exactly that sequence.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    public void HandsOffTheElevatorFloatsToItsAftStopInTheFlatSpin(string id)
    {
        var (_, ac) = FlatSpin(id);
        var cfg = ac.Config;
        double eMax = cfg.Controls.Elevator.MaxDeflRad, held = ac.CurrentDeflections.ElevatorRad;
        double flt = ac.FreeFloat("elevator", cfg.Controls.Elevator, 0.0, held);
        var h = ac.HingeDebug("elevator", cfg.Controls.Elevator);
        _o.WriteLine($"{id}: held {held * D:F1} deg, free float {flt * D:F1} deg ({flt / eMax * 100:F0} % of travel); hinge a {h.A:F1} b {h.B:F1} k {h.K:F1}");
        Assert.True(Math.Sign(flt) == Math.Sign(held), $"the free elevator should float PRO-SPIN: held {held * D:F1}, float {flt * D:F1} deg");
        Assert.True(Math.Abs(flt) > 0.9 * eMax, $"it should reach the aft stop: float {flt * D:F1} of +/-{eMax * D:F1} deg");
    }

    /// <summary>
    /// AND THE AILERON FLOATS PRO-SPIN TOO IN THE FLAT SPIN, WHICH IS WHY BEGGS-MUELLER IS SLOW HERE. The pro-spin
    /// aileron is worth more yaw in the flat spin than the whole rudder (PittsSpinRudderAuthorityTests: 199 N.m
    /// against 154), so a hands-off aileron that stays at its pro-spin stop leaves the recovery fighting the same
    /// term it would have fought with the stick held. Measured 2026-11-11, hands off at the developed flat spin:
    /// Pitts +100 % of travel, Great Lakes +83 %, Extra +64 %.
    ///
    /// IT IS NOT LIKE THAT IN THE STEEP SPIN, and that is the part worth asking Ty about. In the idle,
    /// ailerons-neutral spin the same model floats the Pitts' aileron 35 % IN-spin, which is exactly his report
    /// ("slightly inspin aileron"). So the float model matches him in the condition he reported it in, and does
    /// something different in the flat spin he holds on out-spin aileron. Whether a real Pitts' spade-balanced,
    /// strut-joined ailerons stay out-spin when he lets go of a FLAT spin is his to answer, and the answer decides
    /// whether Beggs-Mueller should be this slow.
    /// </summary>
    [Fact]
    public void HandsOffThePittsAileronsStayProSpinInTheFlatSpin()
    {
        var (_, ac) = FlatSpin("pitts-s2b-like");
        var cfg = ac.Config;
        double aMax = cfg.Controls.Aileron.MaxDeflRad, held = ac.CurrentDeflections.AileronRad;
        double flt = ac.FreeFloat("aileron", cfg.Controls.Aileron, 0.0, held);
        _o.WriteLine($"aileron held {held * D:F1} deg, free float {flt * D:F1} deg ({flt / aMax * 100:F0} % of travel)");
        Assert.True(Math.Sign(flt) == Math.Sign(held) && Math.Abs(flt) > 0.8 * aMax,
            $"hands off, the aileron stays essentially full pro-spin: held {held * D:F1}, float {flt * D:F1} deg");
    }

    /// <summary>
    /// THE HARD PEDAL IS THE ANTI-SPIN PEDAL — Ty teaches it as a cue ("step on the hard pedal which is always the
    /// correct recovery rudder"), and the model's rudder hinge moment agrees, on every type measured. Pedal force
    /// is the hinge moment the foot has to hold, H(d) = a + b*d - k*d; in a developed spin the free rudder has
    /// already trailed hard toward the pro-spin side, so the pro-spin pedal is nearly free and the anti-spin pedal
    /// works against the whole float moment.
    ///
    /// Measured ratios, anti-spin over pro-spin, 2026-11-11 (flat spin / steep spin):
    ///   Pitts 11.3 / 1.8 — Extra 12.0 / 2.1 — Great Lakes 8.0 / 21.9 — Decathlon 2.3 / 6.3 — Stearman — / 61.4
    ///   Cassutt — / 1.8 — Pitts INVERTED flat 3.3.
    /// It is never the other way round and it is never marginal, so THE CUE IS REPRODUCIBLE IN THIS MODEL and it
    /// belongs in instructor mode.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    public void TheHardPedalIsTheAntiSpinPedalInTheDevelopedFlatSpin(string id)
    {
        var (_, ac) = FlatSpin(id);
        var cfg = ac.Config;
        var h = ac.HingeDebug("rudder", cfg.Controls.Rudder);
        double rMax = cfg.Controls.Rudder.MaxDeflRad;
        double H(double d) => h.A + h.B * d - h.K * d;
        double pro = Math.Abs(H(ProRud * rMax)), anti = Math.Abs(H(-ProRud * rMax));
        _o.WriteLine($"{id}: |H| pro-spin pedal {pro:F2}, anti-spin pedal {anti:F2} (ratio {anti / pro:F2}); hinge a {h.A:F2} b {h.B:F2} k {h.K:F2}");
        Assert.True(anti > 1.5 * pro, $"the anti-spin pedal must be the heavy one for Ty's cue to be teachable: anti {anti:F2} against pro {pro:F2}");
    }

    /// <summary>
    /// THE ELEVATOR GAP, PINNED AS IT IS AND KNOWN TO BE WRONG. Ty, 2026-11-11, asked whether recovering the flat
    /// spin needs the aileron out or the elevator too: <b>"The elevator matters too — both must come out."</b> In
    /// his aeroplane, neutral ailerons with the stick still full aft does NOT bring it out. In this model it
    /// recovers in 1.97 turns.
    ///
    /// THE MECHANISM, measured (SpinRecoveryProceduresProbe, RECOVERY_PARTS=gap, 2026-11-11). At the developed flat
    /// spin with full opposite rudder in, the pitching moment about the CG splits like this (N.m):
    ///     stick        wings    h-tail   fin+rud  fuselage     SUM    inertial couple
    ///     full AFT     -1083     -3671         3     -1680    -6432             +3042
    ///     neutral      -1083     -4405         3     -1680    -7165             +3042
    ///     full FWD     -1083     -4734         3     -1680    -7494             +3042
    /// The whole stick travel is worth 1062 N.m of the 6432 that is already nose-down with the stick full aft —
    /// <b>the elevator commands 17 % of the tail's own pitching moment at spin attitude</b>, because at alpha 74
    /// the stabiliser is broadside and its own normal force sets the sign. The nose therefore drops whatever the
    /// stick is doing, and the stick position cannot decide the outcome. That is the gap, and it is a SHARE
    /// problem, not a magnitude problem.
    ///
    /// WHAT WAS KILLED BY MEASUREMENT, so nobody re-opens it:
    ///   * THE TAIL SHADOW IS NOT IT. The fin's local dynamic pressure is eta_t 0.264 / 0.263 / 0.263 at stick
    ///     aft / neutral / forward — the elevator's entire travel moves it by 0.4 %, and the stabiliser's 1.092 not
    ///     at all. The two known-wrong shadow items (deflected geometry, lee-side-only sheet) cannot be the cause
    ///     of this because in the model as it stands the elevator's position does not reach the rudder at all.
    ///   * CONTROL SPOILER K IS NOT IT: setting it to 0 leaves this case at 1.97 turns, unchanged.
    ///   * NO SINGLE TERM IS IT. Removed one at a time, the case still recovers: StabHighAlphaK=1 -> 2.16 turns
    ///     (spin flattens to alpha 77.4), aft-fuselage camber gain 0 -> 2.50 turns (alpha 78.7), FinSidewashK=0 ->
    ///     2.31 turns (alpha 75.8). The two largest are both always-nose-down terms, and both are implicated in the
    ///     SHARE: StabHighAlphaK 1.9 multiplies the WHOLE horizontal tail, so it scales the restoring moment the
    ///     aft stick is fighting just as hard as it scales the aft stick.
    ///
    /// If you are here because this failed with "did not recover", that is very likely the fix landing, and the
    /// right action is to delete this test and pin the new behaviour against Ty's statement instead.
    /// </summary>
    [Fact]
    public void TheModelStillRecoversWithTheStickFullAftWhichTysAeroplaneDoesNot()
    {
        var o = FlyFresh("pitts-s2b-like", new SpinRecoveryProceduresProbe.Proc("", FULL, ProStick, 0.0, 1.0, false, false));
        _o.WriteLine($"stick AFT + ailerons NEUTRAL + full opposite rudder -> "
            + (double.IsNaN(o.StopSec) ? $"no recovery ({o.Turns20:F1} turns)" : $"recovered {o.StopTurns:F2} turns, {o.StopSec:F1} s"));
        Assert.False(double.IsNaN(o.StopSec), "the model recovered this case in 1.97 turns when this pin was written; if it "
            + "no longer does, the model has moved TOWARD Ty's aeroplane and this pin should be replaced");
    }

    /// <summary>
    /// DART RECOVERY (Ty's name, 2026-11-11): power idle, all controls neutral and HELD, rudder neutral too. His
    /// rule is that it must pass on every aeroplane. Measured from the developed flat spin on 2026-11-11:
    /// Pitts 3.16 turns / 7.0 s, Extra 2.46 / 5.7, Great Lakes 3.44 / 7.6 — and on the types outside the shipping
    /// set, Decathlon 0.13 turns, with every steep-spin case under one turn.
    ///
    /// Note what this means next to Beggs-Mueller: dart has strictly LESS anti-spin input (neutral rudder instead
    /// of full opposite) and is FASTER on all three — Pitts 3.16 against 4.29, Great Lakes 3.44 against 4.79. The
    /// difference is the free controls, not the rudder.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    public void DartRecoveryStopsTheFlatSpinOnEveryShippingAerobat(string id)
    {
        var o = FlyFresh(id, new SpinRecoveryProceduresProbe.Proc("", IDLE, 0, 0, 0.0, false, false));
        _o.WriteLine($"{id} dart recovery: {(double.IsNaN(o.StopSec) ? $"NO ({o.Turns20:F1} turns)" : $"{o.StopTurns:F2} turns, {o.StopSec:F1} s")}, end alpha {o.EndAlpha:F1}");
        Assert.False(double.IsNaN(o.StopSec), "dart recovery (idle, everything centred and held) must stop the rotation");
        Assert.True(o.StopTurns < 6.0, $"and it should not take six turns to do it: {o.StopTurns:F2}");
    }

    /// <summary>
    /// BEGGS-MUELLER RECOVERS, BUT SLOWLY, AND THE FREE CONTROLS ARE WHAT MAKES IT SLOW — which is exactly what Ty
    /// reported flying it on 2026-11-10 ("Good test it passed the beggs mueler albeit slower than the real
    /// airplane"). Measured with GENUINELY FREE controls from the developed flat spin on 2026-11-11:
    ///     Pitts        4.29 turns / 9.0 s   against stick-NEUTRAL-held 2.70 turns
    ///     Extra        2.85 / 6.9           against 2.34
    ///     Great Lakes  4.79 / 10.2          against 4.03
    /// NACA TN 1329's standard is two turns and the real aeroplane stops inside a turn or two, so all three are
    /// slow — and the cost is the free controls, which the float pins above locate precisely: the aileron stays at
    /// its pro-spin stop and the elevator stays on its aft stop.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    public void BeggsMuellerRecoversAndTheFreeControlsAreWhatMakesItSlow(string id)
    {
        var free = FlyFresh(id, new SpinRecoveryProceduresProbe.Proc("", IDLE, 0, 0, 1.0, true, true));
        var heldNeutral = FlyFresh(id, new SpinRecoveryProceduresProbe.Proc("", IDLE, 0, 0, 1.0, false, false));
        _o.WriteLine($"{id}: Beggs-Mueller (FREE) {free.StopTurns:F2} turns / {free.StopSec:F1} s;"
            + $"  stick NEUTRAL held {heldNeutral.StopTurns:F2} turns / {heldNeutral.StopSec:F1} s");
        Assert.False(double.IsNaN(free.StopSec), "Beggs-Mueller must recover these three — Ty has flown it in the sim and in the aeroplane");
        Assert.True(free.StopTurns > heldNeutral.StopTurns,
            $"and the free controls must COST turns, which is the whole reason the two procedures are different: "
            + $"free {free.StopTurns:F2} against held-neutral {heldNeutral.StopTurns:F2}");
    }

    /// <summary>
    /// RUDDER ALONE, WITH THE STICK AND AILERON LEFT WHERE THEY WERE, MUST NOT RECOVER — Ty confirmed that for his
    /// own aeroplane, and this is the one procedure of the three that is supposed to fail. Measured from the
    /// developed power-on flat spin on 2026-11-11: Pitts did NOT recover (10.3 turns in 20 s), Great Lakes did NOT
    /// (9.6 turns), <b>and the Extra DID, in 2.94 turns</b>.
    ///
    /// The Extra's row is pinned as measured, not asserted as correct. It is the one shipping aerobat that comes out
    /// of a held flat spin on the rudder alone, and whether that is right is a question for Ty — the Extra carries
    /// neither finSidewashK nor stabHighAlphaK in its config (only controlSpoilerK), so its flat spin is held by a
    /// different balance of terms from the Pitts' and the Great Lakes'.
    /// </summary>
    [Fact]
    public void RudderAloneDoesNotRecoverThePittsOrTheGreatLakesFlatSpinButDoesRecoverTheExtras()
    {
        var pitts = FlyFresh("pitts-s2b-like", new SpinRecoveryProceduresProbe.Proc("", FULL, ProStick, ProAil, 1.0, false, false));
        var gl = FlyFresh("great-lakes-like", new SpinRecoveryProceduresProbe.Proc("", FULL, ProStick, ProAil, 1.0, false, false));
        var extra = FlyFresh("extra-300-like", new SpinRecoveryProceduresProbe.Proc("", FULL, ProStick, ProAil, 1.0, false, false));
        _o.WriteLine($"Pitts {(double.IsNaN(pitts.StopSec) ? $"no recovery, {pitts.Turns20:F1} turns" : $"{pitts.StopTurns:F2} turns")}"
            + $" | Great Lakes {(double.IsNaN(gl.StopSec) ? $"no recovery, {gl.Turns20:F1} turns" : $"{gl.StopTurns:F2} turns")}"
            + $" | Extra {(double.IsNaN(extra.StopSec) ? $"no recovery, {extra.Turns20:F1} turns" : $"{extra.StopTurns:F2} turns")}");
        Assert.True(double.IsNaN(pitts.StopSec), "the Pitts must NOT come out of a held flat spin on the rudder alone — Ty's own report");
        Assert.True(double.IsNaN(gl.StopSec), "nor the Great Lakes, which is the flat-plate-tail control experiment");
        Assert.False(double.IsNaN(extra.StopSec), "the Extra DID recover this case (2.94 turns) when this was pinned; it is the "
            + "fleet's exception to Ty's rule and the pin exists so the exception cannot be forgotten");
    }

    /// <summary>
    /// PARKED, AND KEPT BECAUSE IT WAS FREE: the Decathlon is not in the shipping set, but it is Ty's one real
    /// counter-example ("the only airplane that ... begs mueller does not work on is the decathlon") and the model
    /// does not reproduce it — it recovers in 0.11 turns. The reason is upstream of the recovery: as authored the
    /// model's Decathlon will not HOLD a developed flat spin (the power-on, aileron-held entry settles at alpha
    /// 29.2 and collapses within 3 s of letting go), so there is nothing to recover from.
    ///
    /// The geometric criterion, by contrast, AGREES with Ty: on NACA TN 1329's construction the Decathlon's
    /// authored rudder has no area at all below its horizontal tail plane, where the Pitts has 28.3 % and the Extra
    /// 14.0 %, and for a short-coupled aeroplane that ventral area is essentially the whole unshielded rudder. Its
    /// URVC is therefore 0 and its TDPF 0. This pin holds the geometry, which is cheap and exact; the flown result
    /// is in <see cref="SpinRecoveryProceduresProbe"/>'s output. See <see cref="TailVolumeCriterionProbe"/> (TDPF=1).
    /// </summary>
    [Fact]
    public void TheDecathlonsRudderHasNoVentralAreaWhichIsTheGeometricFormOfTysException()
    {
        var pitts = TailVolumeCriterionProbe.Compute(Load("pitts-s2b-like"));
        var dec = TailVolumeCriterionProbe.Compute(Load("decathlon-8kcab-like"));
        _o.WriteLine($"Pitts     rudder {pitts.RudArea:F3} m^2, below the tail plane {pitts.Sr2:F3} ({pitts.Sr2 / pitts.RudArea * 100:F1} %), URVC {pitts.Urvc:F4}");
        _o.WriteLine($"Decathlon rudder {dec.RudArea:F3} m^2, below the tail plane {dec.Sr2:F3} ({dec.Sr2 / dec.RudArea * 100:F1} %), URVC {dec.Urvc:F4}");
        Assert.True(dec.Sr2 < 1e-6, "as authored the Decathlon has no rudder below its horizontal tail; if this now fails "
            + "someone has re-authored that tail and the comparison in this file's comment is stale");
        Assert.True(pitts.Sr2 / pitts.RudArea > 0.2, "and the Pitts does have it, which is the contrast the criterion rests on");
    }
}
