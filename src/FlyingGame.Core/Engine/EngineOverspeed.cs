using System;

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

    /// <summary>How far over the red line this rpm is, as a fraction (0 at or below it).</summary>
    public static double Excess(double rpm, double redlineRpm)
        => redlineRpm <= 0.0 ? 0.0 : Math.Max(0.0, rpm / redlineRpm - 1.0);

    /// <summary>The multiplier on <see cref="EngineReliability"/>'s hazard rate that this flight's worst over-rev has
    /// earned. 1 until the over-rev passes 10 %, then 50, and it does not come back down.</summary>
    public static double UnreliabilityMultiplier(double peakExcess)
        => peakExcess > NoDamageExcess ? HazardMultiplier : 1.0;

    /// <summary>How many cylinders have lost a ring. One, past 12 %: Ty names a single cylinder, and a second one
    /// would need a second number from him rather than an extrapolation of mine.</summary>
    public static int BrokenRings(double peakExcess) => peakExcess >= BrokenRingExcess ? 1 : 0;

    /// <summary>Has a bearing been spun? Past 15 %, and then it is only a matter of how hard you fly it.</summary>
    public static bool BearingSpun(double peakExcess) => peakExcess >= SpunBearingExcess;

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
