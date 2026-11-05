namespace FlyingGame.Core.Engine;

/// <summary>
/// Where the cylinder heads settle (the cockpit's CHT needle, CockpitRigState): presentation (P), aimed at the middle
/// of the dials' green arcs (docs/models/ea300-own-RIG.md: CHT green to 435 F), from the throttle, and since the
/// Pitts' cowl flap (Ty 2026-11-04 19:31) from the cooling air: a fully open flap takes <see cref="CowlFlapCoolingF"/>
/// off at any power. (E) figures, not a measured cooling curve.
/// </summary>
public static class EngineTemperatures
{
    public const double ChtColdF = 200.0, ChtIdleF = 260.0, ChtPerThrottleF = 160.0, CowlFlapCoolingF = 60.0;

    public static double ChtTargetF(bool running, double throttle01, double cowlFlap01) =>
        running ? ChtIdleF + ChtPerThrottleF * throttle01 - CowlFlapCoolingF * System.Math.Clamp(cowlFlap01, 0.0, 1.0) : ChtColdF;
}
