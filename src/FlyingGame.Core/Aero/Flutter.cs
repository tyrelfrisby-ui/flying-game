using System;

namespace FlyingGame.Core.Aero;

/// <summary>
/// FLUTTER. Ty 2026-11-11, in three messages that together specify the whole of it:
///
///   "Flutter- at 1.2 vd a vibration (high frequency low amplitude flutter) begins on one of 3 control surfaces
///    (random not always the same one) or the flaps if equipped. This increases in amplitude and decreases in
///    frequency until catastrophic failure occurs. At either 1.5 vd the absolute limit where the wing or tail
///    structure fails regardless of the duration or one of the two fails either the control surface separates or the
///    structure fails. This should be random ie the fatigue life of one is better than the other at random but the
///    idea is that the equivalent exposure at 1.35 vd at 1 minute equals failure on the weaker of the two. So a long
///    exposure at 1.25 vd stil produces a failure even though flutter doesn't start until 1.2 vd"
///
///   "After experiencing flutter the wing's structure is compromised which results in a lower ultimate strength so
///    the wing follows the same bend break rules just based on a lower 'limit load' with the theoretical fatigue
///    failure being equivalent to an ultimate strength of 1g"
///
///   "Also 'arm flutter' at 1.2 vd but don't initiate it until either 1.3 vd or a control input of over 1 degree per
///    second rate change and then make the control that exceeded the rate change the control that flutters so it
///    tends to be the control moved unless they are held very still and then it is random" — with 1.3 later
///    corrected to "Make it a 1.35 fallback".
///
/// WHAT IS HIS AND WHAT IS MINE. His: 1.2 Vd arming, 1.35 Vd fallback initiation, 1 deg/s on the surface, 1.5 Vd
/// ceiling, 1.35 Vd for 60 s as the calibration, 1 g ultimate at the end of the fatigue, amplitude up and frequency
/// down, and the two-outcome race. Mine, and declared rather than buried, are the two SHAPES that connect his
/// points: how the damage rate varies with speed (<see cref="RateExponent"/>) and how the ultimate strength falls
/// with the damage (<see cref="StrengthFalloff"/>). One calibration point fixes a rate at one speed and says nothing
/// about the curve through it; two endpoints fix a strength at each end and say nothing about the middle. Both are
/// printed as tables by FlutterLawProbe so he can look at the shape and move it.
///
/// ARMED AND INITIATED ARE TWO STATES, and that is physics rather than bookkeeping: a flutter mode above its
/// boundary is unstable but still needs a disturbance to grow from. Moving a control IS that disturbance, which is
/// why the control you moved is the one that goes. Holding everything still leaves only ambient excitation, which is
/// what the 1.35 Vd fallback stands for.
/// </summary>
public static class Flutter
{
    /// <summary>Vd itself, as this project means it: 1.1 x Vne (CAS). Ty 2026-11-09's rule, and NOT a certificated
    /// figure — no config authors a Vd. Everything below is a multiple of it, so if that convention is ever wrong
    /// every flutter speed moves with it.</summary>
    public const double VdOverVne = 1.1;

    /// <summary>Armed here: past the boundary, nothing growing yet. Ty.</summary>
    /// <remarks>A KNOB, not a const: Ty has already moved these twice in one day (the ceiling 1.5 -> 1.3,
    /// the self-initiation 1.35 -> 1.275), so they belong on the Developer page where he can move them in
    /// flight rather than by asking. The arm speed he has deliberately NOT moved.</remarks>
    public static double ArmVd = 1.20;

    /// <summary>Initiated here even with everything held still. Ty, revising twice: "Make it a 1.35 fallback" and
    /// then "decrease structural failure to 1.3 vd and the arm extends to 1.275". So the ARMED BAND is 1.20 to
    /// 1.275: past 1.20 it is armed and waiting for a control to move, and at 1.275 it starts whatever the pilot
    /// does.</summary>
    public static double FallbackInitiateVd = 1.275;

    /// <summary>A surface moving faster than this while armed initiates it, and that surface is the one that
    /// flutters. Ty. It is deliberately SLOW — a degree a second is barely a twitch — because his stated intent is
    /// "it tends to be the control moved", so it must fire easily. No tolerance band, no debounce.</summary>
    public const double InitiateRateDegPerSec = 1.0;

    /// <summary>The structure fails here regardless of duration. Ty, revised from 1.5: "decrease structural failure
    /// to 1.3 vd". Its own ceiling, not the fatigue law reaching infinity — they are two mechanisms and one law must
    /// not be asked to do both. The whole flutter event now lives inside a 10 % speed band.</summary>
    public static double CeilingVd = 1.30;

    /// <summary>
    /// The one calibration: at 1.35 Vd, one minute is a full fatigue life on the weaker of the two. Ty.
    ///
    /// IT IS A DEFINING POINT, NOT AN OPERATING ONE, and that has to be said because it stopped being reachable when
    /// he moved the ceiling to 1.3 Vd — the structure now fails outright below it, so 1.35 Vd cannot be sustained
    /// for one second, let alone sixty. That does NOT make it wrong and it must not be "fixed" by moving it: the
    /// statement's job is to set the SLOPE of the rate law, m = (v/Vd - 1.20) / 0.15 with rate = m / 60 s, which is
    /// well defined everywhere whether or not a pilot can fly it. Re-anchoring the law to a reachable speed would
    /// change the gradient he chose while appearing to tidy something up.
    /// </summary>
    public const double CalibrationVd = 1.35, CalibrationSeconds = 60.0;

    /// <summary>
    /// HOW THE DAMAGE RATE CLIMBS WITH SPEED, and this exponent is MINE. Ty's single calibration point fixes the
    /// rate at 1.35 Vd and nothing else; the curve through it is a choice. A power law in the margin above onset is
    /// the conventional stand-in — flutter amplitude diverges with the margin and damage goes roughly with its
    /// square — so the rate is m^RateExponent, where m is the margin normalised so that m = 1 at the calibration
    /// speed and m = 0 at onset.
    ///
    /// 1 IS TY'S OWN CHOICE, made against the measured table rather than left at a default, and it is worth knowing
    /// why a reader is looking at a bare exponent of 1. Both 2 and 3 were put to him with the observation that at
    /// k = 2 a pilot cannot hold 1.25 Vd — 1.375 Vne — for the nine minutes it gives, so the 1.2-to-1.35 band would
    /// come out survivable in PRACTICE, against his own sentence "a long exposure at 1.25 vd stil produces a
    /// failure". He took the faster one:
    ///
    ///     v/Vd    1.21     1.25     1.30     1.35     1.40    1.45
    ///     k = 1   22 min   3.0 min  1.5 min  1.0 min  45 s    35 s
    ///     k = 2   3.8 h    9.0 min  2.2 min  1.0 min  34 s    22 s
    ///
    /// Every exponent hits his calibration exactly, by construction — only the curvature below it moves, which is
    /// the part he was describing. AND k = 1 MEANS THE RATE IS LINEAR IN THE MARGIN above onset: the damage accrues
    /// in proportion to how far past it you are, with no curvature anyone has to justify. That is a simpler claim
    /// than a power law and an honest one. FlutterLawProbe prints the table.
    /// </summary>
    /// <remarks>NOT a const: it is a knob. The coordinator's observation when the table went to Ty is that a pilot
    /// cannot hold 1.25 Vd for the nine minutes k = 2 gives, so the 1.2-1.3 band may come out survivable in practice
    /// where his sentence wanted it lethal — k = 1 gives three minutes there and bites. That is a flying judgement,
    /// so this is settable and lives on the Developer page beside the other live knobs: he can fly the shape instead
    /// of reading it. Shipping value 2.</remarks>
    public static double RateExponent = 1.0;

    /// <summary>The margin above onset, normalised to 1 at the calibration speed. 0 at or below onset.</summary>
    public static double Margin(double vOverVd)
        => Math.Max(0.0, (vOverVd - ArmVd) / (CalibrationVd - ArmVd));

    /// <summary>Fatigue index accumulated per second at this speed. Index 1 is a full life.</summary>
    public static double DamagePerSecond(double vOverVd)
    {
        double m = Margin(vOverVd);
        return m <= 0.0 ? 0.0 : Math.Pow(m, RateExponent) / CalibrationSeconds;
    }

    /// <summary>Seconds at a steady speed to a full fatigue life, or infinity at or below onset.</summary>
    public static double SecondsToFailure(double vOverVd)
    {
        double r = DamagePerSecond(vOverVd);
        return r > 0.0 ? 1.0 / r : double.PositiveInfinity;
    }

    /// <summary>
    /// HOW ULTIMATE STRENGTH FALLS WITH THE FATIGUE, and this shape is MINE too. Ty gives both ENDS — the
    /// certificated ultimate at index 0, and "an ultimate strength of 1g" at index 1 — and nothing in between.
    ///
    /// LINEAR IN THE BREAKING LOAD, which is the same thing as linear in <see cref="OverstressMember.Strength"/>
    /// because the member's whole curve scales by it (EffectiveRatio = |LoadRatio| / Strength, breaking at
    /// BreakRatio). So the two obvious readings of "linear" coincide here, which is the main reason to prefer it:
    /// a shape that is linear in the quantity Ty NAMED (the ultimate strength, in g) is also linear in the field
    /// the model actually carries, and no third interpretation has to be argued about.
    ///
    /// The alternative worth knowing about is geometric — Strength = S1^index — which holds more strength early and
    /// collapses late, and would make a lightly fluttered wing nearly as good as new. FlutterLawProbe prints both at
    /// index 0.25, 0.5 and 0.75 so the difference is visible rather than asserted.
    /// </summary>
    public const bool StrengthFalloffIsLinear = true;
    public const string StrengthFalloff = "linear in the breaking load, which is linear in OverstressMember.Strength";

    /// <summary>
    /// What <see cref="OverstressMember.Strength"/> should be at this fatigue index, for a member whose published
    /// limit is <paramref name="limitG"/> and which breaks at <paramref name="breakRatio"/> times its limit.
    ///
    /// At index 0 it is 1 — untouched. At index 1 the member breaks at 1 g, which is Ty's endpoint: a wing that
    /// cannot carry the aeroplane in level flight. Both ends are HIS; only the line between them is mine.
    /// </summary>
    public static double StrengthForIndex(double index, double strengthAtOneG)
    {
        double atOne = Math.Clamp(strengthAtOneG, 0.0, 1.0);
        double i = Math.Clamp(index, 0.0, 1.0);
        return StrengthFalloffIsLinear ? 1.0 + i * (atOne - 1.0) : Math.Pow(atOne, i);
    }

    /// <summary>
    /// The <see cref="OverstressMember.Strength"/> at which a member breaks at 1 g — Ty's endpoint. It is derived
    /// from the load ratio that member is REALLY driven by at 1 g (StructuralState.WingRatioAt / TailRatioAt) rather
    /// than from a published limit, because the member's ratio is taken against the published limit scaled by the
    /// wing's weight share, and neither of those is the number a reader would guess. Breaking happens at
    /// EffectiveRatio = |LoadRatio| / Strength &gt; BreakRatio, so the strength that puts 1 g exactly on the break is
    /// ratio(1 g) / BreakRatio.
    /// </summary>
    public static double StrengthAtOneG(double ratioAtOneG, double breakRatio = OverstressMember.BreakRatio)
        => breakRatio > 0.0 ? Math.Clamp(ratioAtOneG / breakRatio, 0.0, 1.0) : 1.0;

    /// <summary>The same shape expressed against a published limit, for the TABLE a pilot reads: at index 0 the
    /// structure's certificated ultimate, at index 1 one g.</summary>
    public static double StrengthForIndexFromLimit(double index, double limitG, double breakRatio = OverstressMember.BreakRatio)
        => limitG <= 0.0 || breakRatio <= 0.0 ? 1.0 : StrengthForIndex(index, 1.0 / (breakRatio * limitG));

    /// <summary>The load factor a member with this much fatigue will break at, g — the reading of
    /// <see cref="StrengthForIndex"/> a pilot would recognise.</summary>
    public static double BreakingGForIndex(double index, double limitG, double breakRatio = OverstressMember.BreakRatio)
        => breakRatio * limitG * StrengthForIndexFromLimit(index, limitG, breakRatio);

    // ---- the signature: amplitude up, frequency down ------------------------------------------------------------

    /// <summary>
    /// WHERE THE FLUTTER STARTS AND WHERE IT ENDS, Hz. Ty: "high frequency low amplitude flutter ... increases in
    /// amplitude and decreases in frequency". That is the right description of a mode COALESCENCE — two modes,
    /// classically a surface's rotation against the structure it hangs on, whose frequencies converge as the
    /// coupling grows, so the diverging motion sits between them and walks down as it takes over.
    ///
    /// THESE TWO NUMBERS ARE AUTHORED, NOT DERIVED, and there is no structural dynamics model here to derive them
    /// from. Control-surface flutter on light aeroplanes is reported in the tens of hertz and comes down as it
    /// diverges, so 35 Hz to 8 Hz is the right ORDER and the shape between them is a choice like the two above.
    /// Scaling it off something the config carries was considered and rejected: a surface's frequency goes as
    /// sqrt(stiffness/inertia) and this model has neither a hinge stiffness nor a surface inertia, so anything built
    /// from area or chord alone would be a derivation in appearance only.
    /// </summary>
    public const double OnsetHz = 35.0, DivergentHz = 8.0;

    /// <summary>The flutter's frequency at this fatigue index, Hz: it falls as the mode takes over.</summary>
    public static double FrequencyHz(double index)
        => OnsetHz + Math.Clamp(index, 0.0, 1.0) * (DivergentHz - OnsetHz);

    /// <summary>
    /// The shake at the pilot, g, at this fatigue index. Zero at onset (it is "low amplitude" there and the pilot's
    /// cue is the NOISE, not the seat) rising to something unmistakable at failure. The band is chosen to sit above
    /// a bent propeller's imbalance (Aircraft.PropVibrationG works in tenths of a g) so the two can be told apart.
    /// </summary>
    public static double AmplitudeG(double index)
    {
        double i = Math.Clamp(index, 0.0, 1.0);
        return 0.05 + i * i * 1.45;                              // 0.05 g at onset, 1.5 g at failure
    }
}
