using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Engine;

/// <summary>
/// WHAT AN OVER-REV COSTS YOU. Ty 2026-11-11, giving the ladder himself:
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
/// THE LADDER IS HIS AND THE NUMBERS ARE HIS. 10, 12 and 15 per cent over the red line, 50x the hazard. Nothing in
/// this file fits them to anything; what the file adds is the one thing he described rather than numbered — the rate
/// a spun bearing destroys itself at, which has to depend on rpm and load so that nursing it home is a real choice.
///
/// IT IS A PEAK, NOT A DOSE. Each rung fires on the WORST over-rev the flight has seen, once, and never un-fires:
/// a ring that has broken stays broken and a bearing that has been spun stays spun, whatever the pilot does after.
/// It is also why the hazard multiplier is "for the remainder of the flight" rather than for a while — the engine has
/// been hurt, and backing off does not unhurt it.
///
/// WHY IT IS A PURE MODEL with no aeroplane in it, like <see cref="CrankshaftFatigue"/> beside it: the thresholds and
/// the damage law are properties of an engine, the state is the aeroplane's, and keeping them apart is what lets a
/// test drive the whole ladder in microseconds instead of flying it.
/// </summary>
public static class EngineOverspeed
{
    /// <summary>At or below this fraction OVER the red line, nothing at all happens. Ty: "an over speed of 10% or
    /// less causes no damage."</summary>
    public const double NoDamageExcess = 0.10;

    /// <summary>Past <see cref="NoDamageExcess"/>, every engine-related failure becomes this much more likely for the
    /// rest of the flight — and stays RANDOM. Ty: "Over 10% increases likelyhood of all engine related failures by
    /// 50 x for the remainder of the flight but they remain random."</summary>
    public const double HazardMultiplier = 50.0;

    /// <summary>A broken ring on ONE cylinder: that cylinder stops making power, and the exhaust note loses a pulse.</summary>
    public const double BrokenRingExcess = 0.12;

    /// <summary>A spun bearing: progressive, terminal, and the aeroplane is still saveable by flying it gently.</summary>
    public const double SpunBearingExcess = 0.15;

    /// <summary>A thrown blade — the whole of one on a constant-speed propeller, half of one on a fixed-pitch
    /// propeller. Ty 2026-11-11.</summary>
    public const double ThrownBladeExcess = 0.25;

    /// <summary>
    /// AND THE THREE MECHANICAL RUNGS ARE TIME-GATED, cumulatively. Ty 2026-11-11, revising his own ladder: "The
    /// overspeed damage of a broken ring and spin bearing should gate on a time so overspeed greater than 12% for
    /// over a cumulative 10 seconds and overspeed greater than 15% for a cumulative of 5 seconds and add a f[urther]
    /// overspeed of 25% for more than 3 seconds throws a single prop blade on a cs prop and 50% of a single prop
    /// blade on a fixed pitch prop".
    ///
    /// CUMULATIVE, NOT CONTINUOUS: four separate two-second excursions past 12 % add up to eight and the fifth one
    /// breaks the ring. Each rung counts its own time and only while the rpm is above ITS OWN threshold, so the
    /// counters nest — every second over 15 % is also a second over 12 %.
    ///
    /// The 50x hazard does NOT gate. Ty put no time on it, and it is the one rung that is not a mechanical failure:
    /// the engine has simply been hurt and is more likely to break from here on.
    /// </summary>
    public const double BrokenRingSeconds = 10.0, SpunBearingSeconds = 5.0, ThrownBladeSeconds = 3.0;

    /// <summary>How far over the red line this rpm is, as a fraction (0 at or below it).</summary>
    public static double Excess(double rpm, double redlineRpm)
        => redlineRpm <= 0.0 ? 0.0 : Math.Max(0.0, rpm / redlineRpm - 1.0);

    /// <summary>The multiplier on <see cref="EngineReliability"/>'s hazard rate that this flight's worst over-rev has
    /// earned. 1 until the over-rev passes 10 %, then 50, and it does not come back down.</summary>
    public static double UnreliabilityMultiplier(double peakExcess)
        => peakExcess > NoDamageExcess ? HazardMultiplier : 1.0;

    /// <summary>How many cylinders have lost a ring, from the CUMULATIVE seconds spent over 12 %. One, past ten
    /// seconds of it: Ty names a single cylinder, and a second one would need a second number from him rather than
    /// an extrapolation of mine.</summary>
    public static int BrokenRings(double secondsOver12) => secondsOver12 > BrokenRingSeconds ? 1 : 0;

    /// <summary>Has a bearing been spun? Five cumulative seconds over 15 %, and then it is only a matter of how hard
    /// you fly it.</summary>
    public static bool BearingSpun(double secondsOver15) => secondsOver15 > SpunBearingSeconds;

    /// <summary>Has it thrown a blade? Three cumulative seconds over 25 %.</summary>
    public static bool BladeThrown(double secondsOver25) => secondsOver25 > ThrownBladeSeconds;

    /// <summary>
    /// HOW MUCH OF A BLADE GOES. Ty 2026-11-11: "throws a single prop blade on a cs prop and 50% of a single prop
    /// blade on a fixed pitch prop". A constant-speed hub holds its blades in a socket and lets a whole one go; a
    /// fixed-pitch blade is part of one continuous spar, so it fails somewhere out along the span and takes the
    /// OUTER piece with it.
    /// </summary>
    public static double LostBladeFraction(bool constantSpeed) => constantSpeed ? 1.0 : 0.5;

    /// <summary>
    /// THE ROTATING UNBALANCE A THROWN BLADE LEAVES, newtons: F = m·r·omega^2, where m is the mass that went and r
    /// is where its centre of mass was.
    ///
    /// BOTH COME FROM TY'S OWN BLADE RULE (2026-11-09, tools/aircraft_builder/prop_inertia.py, and the note on every
    /// propInertia in the fleet): each blade is 15 % of the net propeller weight with its centre of mass at 25 % of
    /// the DIAMETER from the axis. The propeller's mass is not a new number either - it is backed out of the
    /// authored polar inertia the same way AirframeHydroModel does it, m = 3·I/R² for a slender blade.
    ///
    /// HALF A BLADE IS NOT HALF THE FORCE, and that is the part worth getting right. The piece that leaves a
    /// fixed-pitch blade is the OUTER half, so although it is half the mass its centre of mass sits further out: for
    /// a uniform slender blade the outer half's centroid is at 0.75 of the span against 0.50 for the whole blade,
    /// so the unbalance is 0.5 × 1.5 = 0.75 of a whole blade's, not 0.5. Applied to Ty's rule, whose whole-blade CoM
    /// is 0.25 D = 0.5 R, the outer half's is 0.75 R.
    /// </summary>
    public static double ThrownBladeUnbalanceN(double propInertiaKgM2, double propDiameterM, int blades,
                                               double rpm, bool constantSpeed)
    {
        if (propDiameterM <= 0.0 || propInertiaKgM2 <= 0.0 || rpm <= 0.0) return 0.0;
        double bladeKg = BladeMassKg(propInertiaKgM2, propDiameterM, blades);
        double rCom = BladeComRadiusM(propDiameterM);
        double omega = rpm * 2.0 * Math.PI / 60.0;
        // A whole blade: its own mass at its own centre of mass. The OUTER HALF of one: half the mass, but a
        // uniform slender blade's outer half has its centroid at 0.75 of the span where the whole blade's is at
        // 0.50, so the arm is 1.5x and the unbalance is 0.5 x 1.5 = 0.75 of a whole blade's rather than half.
        double m = constantSpeed ? bladeKg : 0.5 * bladeKg;
        double r = constantSpeed ? rCom : 1.5 * rCom;
        return m * r * omega * omega;
    }

    /// <summary>
    /// ONE BLADE'S MASS, kg, by INVERTING TY'S OWN BLADE RULE rather than guessing at a propeller's weight. The rule
    /// (2026-11-09, tools/aircraft_builder/prop_inertia.py, and the note on every propInertia in the fleet): each
    /// blade is 15 % of the net propeller weight with its centre of mass at 25 % of the DIAMETER, and the rest is
    /// hub near the axis at a radius of gyration of about 0.10 m. Written out, that is
    ///
    ///     I = M · [ (blades · 0.15) · (0.25 D)²  +  (1 − blades · 0.15) · k² ]
    ///
    /// and the authored propInertia is the left-hand side, so the mass falls straight out. CHECKED against the one
    /// type whose build-up is written down: the Pitts' note gives blades 2.668 + hub 0.254 + crank 0.14 = 3.06 kg·m²
    /// for a 36.3 kg propeller on 1.98 m, and this returns 36.3 kg to the digit from 2.92 (the propeller's own share).
    /// Handed the whole 3.06 it returns 38.0, 4.7 % high, because the crankshaft's share is inside the authored
    /// number and is not separable — which is the right error to have and is stated rather than hidden.
    ///
    /// AND IT IS NOT 3·I/R². <see cref="FlyingGame.Core.Wreck.AirframeHydroModel"/> backs the propeller's mass out
    /// with the slender-blade I = m R²/3, which assumes the mass is spread evenly along the blade. These propellers
    /// are not built that way in this model — Ty's rule deliberately concentrates most of the weight in a hub that
    /// contributes almost nothing to the polar moment — so that inversion comes out about FOUR TIMES light: 9.4 kg
    /// for the Pitts' 36.3 kg propeller and 1.7 kg for a wooden Cub blade pair that ought to be near 7. That is a
    /// real defect in the wreck model's mass build-up and it is flagged, not fixed here.
    /// </summary>
    public static double BladeMassKg(double propInertiaKgM2, double propDiameterM, int blades)
    {
        double rCom = BladeComRadiusM(propDiameterM);
        double bladeShare = Math.Clamp(Math.Max(1, blades) * BladeMassFraction, 0.0, 0.95);
        double denom = bladeShare * rCom * rCom + (1.0 - bladeShare) * HubRadiusOfGyrationM * HubRadiusOfGyrationM;
        return denom > 0.0 ? BladeMassFraction * propInertiaKgM2 / denom : 0.0;
    }

    /// <summary>Where a blade's centre of mass is: 25 % of the DIAMETER from the axis, which is half the radius.
    /// Ty's rule, and the "of DIAMETER" is the part that is easy to misread and worth writing out.</summary>
    public static double BladeComRadiusM(double propDiameterM) => 0.25 * propDiameterM;

    /// <summary>
    /// WHICH WAY A THROWN BLADE GOES, body axes, as a unit vector — and it is decided by WHERE THE BLADE WAS when
    /// the hub let go, not by a choice. The propeller is direct-drive, so the shaft angle the sim is already
    /// integrating (AircraftEngine.CrankAngleRad, modulo one turn) IS the blade's clock position, and the departure
    /// is tangential: straight along the velocity the blade already had.
    ///
    /// The azimuth convention is <see cref="BladeElement"/>'s own, so the two cannot disagree: psi = 0 is the blade
    /// out to the RIGHT and going DOWN. The radial direction is then (0, cos psi, sin psi) and the tangential one is
    /// its derivative, (0, −sin psi, cos psi), which at psi = 0 is +z — down — as it must be. A left-hand propeller
    /// reverses it through <paramref name="rotationSign"/>.
    ///
    /// So the same failure at a different crank angle throws the blade up, down, left or right, and on a biplane
    /// with two wings and a cabin within a metre of the disc that decides what it meets. Nothing here is random and
    /// nothing is chosen.
    /// </summary>
    public static Vec3 ThrowDirectionBody(double shaftAngleRad, double rotationSign)
    {
        double psi = shaftAngleRad % (2.0 * Math.PI);
        double s = rotationSign >= 0.0 ? 1.0 : -1.0;
        return new Vec3(0.0, -Math.Sin(psi) * s, Math.Cos(psi));
    }

    /// <summary>Where the blade's centre of mass was when it left, body axes, relative to the hub: out along the
    /// radius at the azimuth it was at.</summary>
    public static Vec3 ThrowRadiusBody(double shaftAngleRad, double propDiameterM)
    {
        double psi = shaftAngleRad % (2.0 * Math.PI);
        double r = BladeComRadiusM(propDiameterM);
        return new Vec3(0.0, Math.Cos(psi) * r, Math.Sin(psi) * r);
    }

    /// <summary>How fast it leaves, m/s: the tangential speed its centre of mass already had. The outer half of a
    /// fixed-pitch blade leaves FASTER than a whole constant-speed blade would, because its centre of mass is
    /// further out — measured on this fleet, 185 m/s for the Cub's half against 140 for the Pitts' whole.</summary>
    public static double ThrowSpeedMs(double propDiameterM, double rpm, bool constantSpeed)
    {
        double r = BladeComRadiusM(propDiameterM) * (constantSpeed ? 1.0 : 1.5);
        return Math.Abs(rpm) * 2.0 * Math.PI / 60.0 * r;
    }

    /// <summary>Ty's blade rule: each blade is 15 % of the net propeller weight, the rest being hub near the axis.
    /// Two-blade propellers only; the rule runs 60 % high on a three-blade one, which is recorded on every
    /// propInertia note in the fleet.</summary>
    public const double BladeMassFraction = 0.15;

    /// <summary>The hub's radius of gyration, m, from the same build-up (the Pitts' propInertia note: "hub 0.254
    /// (k 0.10 m)"). It barely matters — the hub is 0.9 % of the Pitts' polar moment — but leaving it out would
    /// make the inversion disagree with the rule it is inverting.</summary>
    public const double HubRadiusOfGyrationM = 0.10;

    /// <summary>
    /// What a broken ring costs, as a multiplier on shaft power: one cylinder of <paramref name="cylinders"/> stops
    /// contributing. A cylinder with a broken ring has lost its compression, so it makes no power — but it is still
    /// being driven round and still pumping, so the loss is slightly MORE than its share. That second part is not
    /// modelled: (n-1)/n is the share, it is what a pilot measures on a mag check or a lean test, and the pumping
    /// penalty would be a number nobody has given me.
    /// </summary>
    public static double PowerFactorForBrokenRings(int brokenRings, int cylinders)
    {
        int n = Math.Max(1, cylinders);
        return Math.Clamp((n - Math.Clamp(brokenRings, 0, n - 1)) / (double)n, 0.0, 1.0);
    }

    /// <summary>
    /// HOW FAST A SPUN BEARING DESTROYS ITSELF, as a fraction of the way to seizure per second. This is the part Ty
    /// described rather than numbered — "The time until actual failure depends on rpm and load so a pilot that
    /// 'nurses it back' has more time with less load and rpm but a pilot that drives it like he stole it will quickly
    /// experience a failure" — so the shape has to come from somewhere and the scale is a judgement.
    ///
    /// THE SHAPE IS THE PV LAW, which is the standard wear criterion for a bearing that has lost its oil film: wear
    /// rate goes with the product of specific load P and sliding velocity V (Archard; it is what every plain-bearing
    /// design chart is drawn in). On a con-rod bearing the two are:
    ///
    ///   V  the journal surface speed, straight proportional to rpm;
    ///   P  the gas load, which is the charge the cylinder compressed — proportional to the engine's TORQUE.
    ///
    /// Torque x rpm is POWER, so the gas-load term is simply the fraction of rated power the engine is making, which
    /// is exactly the "load" half of what Ty said. The second term is the one that keeps an idling engine from being
    /// free: the rotating and reciprocating assembly loads that bearing whether or not it is firing, and that load
    /// goes as rpm SQUARED. Con-rod bearings are sized for the vector sum of the two and the two are of the same
    /// order at rated power, which is why they are weighted equally here rather than by a fitted ratio.
    ///
    ///     rate = ( P/P_rated + (rpm/rpm_rated)^2 ) / SeizeSeconds
    ///
    /// AND THAT RATIO IS THE WHOLE POINT. At full power both terms are 1 and the engine seizes in
    /// <see cref="SeizeSeconds"/>/2; at the idle stop the power term is near zero and the rpm term is about a
    /// sixteenth, so the same engine lasts about seventeen times as long. Nursing it home genuinely buys time, and it
    /// buys it because of what the pilot does with the levers rather than because of a difficulty setting.
    /// </summary>
    public static double BearingDamagePerSecond(double rpm, double redlineRpm, double shaftPower01)
    {
        if (redlineRpm <= 0.0 || SeizeSeconds <= 0.0) return 0.0;
        double n = Math.Max(0.0, rpm) / redlineRpm;
        double load = Math.Clamp(shaftPower01, 0.0, 1.5);
        return (load + n * n) / SeizeSeconds;
    }

    /// <summary>
    /// The scale of the bearing's life, seconds — twice the time a seized bearing takes at FULL power, because the
    /// two terms of <see cref="BearingDamagePerSecond"/> are each 1 there.
    ///
    /// THE ONE JUDGEMENT IN THIS FILE, and it is Ty's to overturn. 240 s puts a firewalled engine at the bottom in
    /// two minutes and one nursed back at the idle stop at about half an hour, which is what "quickly" and "more
    /// time" have to mean if the choice is to be a flying problem rather than a formality. There is no published
    /// time-to-seizure for a bearing that has been spun by an over-rev — it depends on how much babbitt went — so
    /// this is a flyability number and is labelled as one.
    /// </summary>
    public const double SeizeSeconds = 240.0;

    /// <summary>
    /// What a bearing this far gone costs in power, as a multiplier. Ty: "High vibration reduced power due to
    /// friction". A bearing being destroyed is converting shaft power into heat in the bearing, and the loss grows
    /// with the damage; at seizure it is all of it. Linear to 0.6 over the life, because the friction that is doing
    /// the damage is also what the damage is measured by — anything steeper would be a second curve with nothing
    /// behind it.
    /// </summary>
    public static double PowerFactorForBearing(double damage01)
        => 1.0 - 0.4 * Math.Clamp(damage01, 0.0, 1.0);

    /// <summary>Oil pressure falls as the bearing goes: the clearance it has opened up is a leak straight out of the
    /// gallery. As a multiplier on the healthy pressure, down to a quarter at seizure.</summary>
    public static double OilPressureFactor(double damage01)
        => 1.0 - 0.75 * Math.Clamp(damage01, 0.0, 1.0);

    /// <summary>And the temperature rises, F above where it would otherwise sit: the friction's heat goes into the
    /// oil. 90 F at seizure takes a 185 F gauge to 275, which is off the top of the green arc and then some.</summary>
    public static double OilTemperatureRiseF(double damage01)
        => 90.0 * Math.Clamp(damage01, 0.0, 1.0);

    /// <summary>The shake at the pilot, g, from a bearing with this much of its life gone. Ty: "High vibration".
    /// Zero while it is healthy and 0.35 g at seizure, which is the band the propeller's own imbalance works in
    /// (Aircraft.PropVibrationG) so the two can be added without one swamping the other.</summary>
    public static double VibrationG(double damage01)
        => 0.35 * Math.Clamp(damage01, 0.0, 1.0);
}
