using System;

namespace FlyingGame.Core.Engine;

/// <summary>
/// THE IMPULSE COUPLING, AND THE BOTTOM OF THE RPM RANGE. Ty 2026-11-11:
///
///   "Sometimes lower like a tail slide where the reversed direction works against the prop and can actually stop the
///    engine if rpm falls below around 300 rpm. Model impulse couplings that make a snapping sound and retard
///    ignition to TDC at rpm below 400. Once on[,] the impulse coupling[,] if rpm drops below 300 the friction should
///    be too high for the engine to overcome static and the engine simply winds down and stops"
///
/// WHAT THE DEVICE IS. A spring-loaded coupling in the magneto drive. Below its cut-out speed the flyweights hang in
/// and the coupling HOLDS the magneto while the engine turns on, winding the spring; at the firing position it lets
/// go, and the magneto is flicked through its rotation far faster than the engine is turning. That does two things at
/// once, and both of them are why an engine starts at all: the fast rotation makes a hot spark at cranking speed,
/// where a direct-driven magneto would barely fire, and the hold-and-release RETARDS the spark to about top dead
/// centre so the engine cannot kick back against the starter or the hand. Above cut-out the flyweights fly out, the
/// coupling drives through, and the magneto runs direct at normal advance. The release is audible: it is the
/// characteristic snap of a hand-propped engine coming round.
///
/// TY'S NUMBERS, AND THEY ARE HIS. 400 rpm for the cut-out and 300 for where static friction wins. Real cut-out
/// speeds vary by installation — the Bendix/Slick units on this fleet's engines sit nearer 150 to 350 — so 400 is a
/// little high for the hardware and exactly right as the owner's call about his own aeroplanes. It is labelled, not
/// fitted.
///
/// NOTHING HERE IS A COEFFICIENT. The retard angle is a property of the coupling (it lets go at the retard stop, by
/// construction), the snap rate is the firing rate, and the stop speed is Ty's.
/// </summary>
public static class ImpulseCoupling
{
    /// <summary>Below this the coupling is engaged: retarded spark, and a snap per impulse. Ty 2026-11-11.</summary>
    public const double CutOutRpm = 400.0;

    /// <summary>
    /// Below this a decelerating engine stops dead. Ty 2026-11-11: "if rpm drops below 300 the friction should be too
    /// high for the engine to overcome static and the engine simply winds down and stops."
    ///
    /// IT IS A STATIC-FRICTION STATEMENT AND THAT IS WHY IT LATCHES. Static friction is higher than the sliding
    /// friction the engine was overcoming on the way down, so once it has stopped it stays stopped until something
    /// can break it away — which is what <see cref="PistonEngine.BoundaryFrictionFactor"/>'s dry excess and
    /// <see cref="PistonCompression"/>'s peak already describe between them.
    /// </summary>
    public const double StaticFrictionStopRpm = 300.0;

    /// <summary>Is the coupling engaged at this speed — retarded, snapping, and able to fire at cranking speed?</summary>
    public static bool Engaged(double rpm) => rpm > 0.0 && rpm < CutOutRpm;

    /// <summary>
    /// Spark advance, degrees before top dead centre. Engaged, the coupling lets go at its retard stop, which is
    /// about TDC — Ty: "retard ignition to TDC at rpm below 400". Above cut-out the magneto runs direct at the
    /// installation's normal advance, which on this fleet's engines is 25 degrees BTDC.
    /// </summary>
    public static double AdvanceDeg(double rpm) => Engaged(rpm) ? RetardedAdvanceDeg : NormalAdvanceDeg;

    /// <summary>Lycoming and Continental fixed-timing installations run 25 degrees BTDC; the Continental C-85 on the
    /// Cub is 28 and the Lycoming 540s are 25, so one number is within three degrees of every engine in this fleet.
    /// PUBLISHED (type certificate data sheets), not fitted — and nothing reads it for power yet.</summary>
    public const double NormalAdvanceDeg = 25.0, RetardedAdvanceDeg = 0.0;

    /// <summary>
    /// How many impulses a second at this speed: one per cylinder firing, which on a four-stroke is
    /// cylinders/2 per crank revolution — the same rate the exhaust pulse train already runs at, because it is the
    /// same event. Zero when the coupling is not engaged, because then there is nothing to release.
    /// </summary>
    public static double SnapsPerSecond(double rpm, int cylinders)
        => Engaged(rpm) ? rpm / 60.0 * Math.Max(1, cylinders) * 0.5 : 0.0;
}
