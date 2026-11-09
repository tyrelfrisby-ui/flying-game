using System;

namespace FlyingGame.Core.Engine;

/// <summary>
/// THE TWO-BLADE PROPELLER IS A DUMBBELL, AND IT BEATS THE CRANKSHAFT TO DEATH.
///
/// Ty 2026-11-09: "The mt on the pitts prevents crank shaft failures from the pulsating gyroscopic load of the
/// Hartzell two blade metal prop. Add crank failure with propeller departure to the random failure list for the pitts
/// and increase its likelihood based on gyroscopic loads."
///
/// WHY A TWO-BLADE IS DIFFERENT, and why a three-blade simply is not. A propeller's gyroscopic reaction to a pitch or
/// yaw rate is Omega x h, and for a rotor with three or more blades that is STEADY, because such a rotor is polar
/// symmetric: its moment of inertia about any axis across the shaft is the same whatever the blades are pointing at.
/// A two-blade rotor is not. Its transverse inertia swings between nearly zero (blades lined up with the axis, two
/// rods end-on) and the full polar value (blades across it), twice per revolution. So the same manoeuvre that gives a
/// three-blade a constant couple gives a two-blade a couple that PULSATES at 2/rev, and the crank sees it as a
/// reversing bending load at 90 Hz at 2700 rpm. That is a high-cycle fatigue problem, and it is why this failure
/// belongs to aerobatics — large rates at high rpm — and not to cross-country hours.
///
///     amplitude = f * I_blades * Omega * |rate across the shaft|,   f = 1/2 for two blades, 0 for three or more
///
/// ITS OWN HAZARD, NOT A SHARE OF THE ENGINE'S. <see cref="EngineReliability"/>'s pool is drawn against engine hours
/// and its weights sum to one; this rate depends on what the pilot has been DOING, so it is independent, exactly as
/// the electrical fire is. An aeroplane flown gently never accumulates any of it, and a three-blade aeroplane cannot
/// accumulate it at all.
///
/// THE DAMAGE LAW is Miner's rule on a Basquin line, which is the standard treatment for steel in high-cycle fatigue:
/// damage per cycle goes as (S/S_e)^m, and below the endurance limit S_e it is zero. Steel really does have an
/// endurance limit, and that is what makes the model say the right thing — gentle flying is free, and it is sustained
/// hard manoeuvring at high rpm that spends the crank.
/// </summary>
public static class CrankshaftFatigue
{
    /// <summary>Transverse-inertia asymmetry: a two-blade rotor swings between ~0 and its polar inertia twice a turn,
    /// so the pulsating part is half the polar value. Three or more blades are polar symmetric: nothing pulsates.</summary>
    public static double AsymmetryFactor(int blades) => blades == 2 ? 0.5 : 0.0;

    /// <summary>
    /// The amplitude of the 2/rev gyroscopic couple, N·m. <paramref name="bladeInertia"/> is the PROPELLER's polar
    /// inertia; configs carry prop + crank together (PropulsionConfig.PropInertia), and the crank's own share is
    /// symmetric and does not pulsate, so passing the combined figure overstates this a little. Deliberately on the
    /// conservative side rather than carrying a second number nobody has measured.
    /// </summary>
    public static double PulseAmplitudeNm(int blades, double bladeInertia, double rpm, double crossShaftRateRadS)
    {
        double f = AsymmetryFactor(blades);
        if (f <= 0.0 || bladeInertia <= 0.0 || rpm <= 0.0) return 0.0;
        double omega = rpm * 2.0 * Math.PI / 60.0;
        return f * bladeInertia * omega * Math.Abs(crossShaftRateRadS);
    }

    /// <summary>
    /// The endurance limit, N·m: the pulsation a crank can take indefinitely. Anchored, not fitted, at the couple a
    /// Pitts-sized two-blade makes at a sustained 80 deg/s across the shaft at full rpm — brisk handling, the sort of
    /// thing that happens in any aerobatic sequence and that cranks plainly survive by the thousand hours. Harder
    /// than that spends life; gentler than that is free. This is the one number here that is a judgement, and it is
    /// the right place for it: everything else is geometry.
    /// </summary>
    public const double EnduranceNm = 700.0;

    /// <summary>Basquin exponent for steel in high-cycle fatigue. 5 is the conventional slope.</summary>
    public const double Exponent = 5.0;

    /// <summary>Fraction of the crank's fatigue life spent per second at this pulsation and rpm. Two cycles a
    /// revolution, and nothing at all below the endurance limit.</summary>
    public static double DamagePerSecond(double amplitudeNm, double rpm, double lifeCycles = 2.0e8)
    {
        if (amplitudeNm <= EnduranceNm || rpm <= 0.0 || lifeCycles <= 0.0) return 0.0;
        double cyclesPerSec = 2.0 * rpm / 60.0;
        return cyclesPerSec * Math.Pow(amplitudeNm / EnduranceNm, Exponent) / lifeCycles;
    }

    /// <summary>
    /// The hazard a crank with this much life spent carries, per hour. Zero while the crank is fresh, rising steeply
    /// as the accumulated damage approaches one: a cubic, so a half-spent crank is an eighth as likely to let go as a
    /// fully spent one. At damage 1 it is about one failure per ten hours — a crank that has been flown this hard is
    /// living on borrowed time, not guaranteed to break on the next loop.
    /// </summary>
    public static double RatePerHour(double damage)
    {
        double d = Math.Clamp(damage, 0.0, 4.0);
        return 0.1 * d * d * d;
    }

    /// <summary>The chance it lets go this step, from the accumulated damage.</summary>
    public static double StepProbability(double damage, double dtSeconds)
        => EngineReliability.StepProbability(RatePerHour(damage), dtSeconds);
}
