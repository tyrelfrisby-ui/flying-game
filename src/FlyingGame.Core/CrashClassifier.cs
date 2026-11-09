using System.Collections.Generic;

namespace FlyingGame.Core;

/// <summary>What the aeroplane was doing when it all went wrong, so the right thing can be said about it.</summary>
public readonly struct CrashFacts
{
    public readonly bool OnWater, OnGround, EngineStopped, FuelDry, GearRetracted;
    public readonly DamageCause Cause;
    /// <summary>Degrees per second about the yaw axis, and the angle of attack, at the moment of the first impact.</summary>
    public readonly double YawRateDegS, AlphaDeg, IasKt, SinkFpm, BankDeg, AglM;
    public readonly int Impacts;
    public readonly IReadOnlyCollection<AirframeComponent> Lost;

    public CrashFacts(DamageCause cause, bool onWater, bool onGround, bool engineStopped, bool fuelDry, bool gearRetracted,
        double yawRateDegS, double alphaDeg, double iasKt, double sinkFpm, double bankDeg, double aglM, int impacts,
        IReadOnlyCollection<AirframeComponent> lost)
    {
        Cause = cause; OnWater = onWater; OnGround = onGround; EngineStopped = engineStopped; FuelDry = fuelDry;
        GearRetracted = gearRetracted; YawRateDegS = yawRateDegS; AlphaDeg = alphaDeg; IasKt = iasKt; SinkFpm = sinkFpm;
        BankDeg = bankDeg; AglM = aglM; Impacts = impacts; Lost = lost ?? System.Array.Empty<AirframeComponent>();
    }
}

/// <summary>
/// Which of <see cref="CrashKind"/> this was. The order of the tests is the whole design: the most specific and most
/// certain things are asked first, so an aeroplane that ran out of fuel AND then hit a tree is reported as the tree
/// strike — that is the thing that broke it, and the thing the pilot will remember.
/// </summary>
public static class CrashClassifier
{
    /// <summary>A flat spin: turning hard about the yaw axis, well past the stall, going down fast and barely forward.</summary>
    public const double FlatSpinYawRateDegS = 60, FlatSpinAlphaDeg = 25, FlatSpinMaxIasKt = 55;

    public static CrashKind Classify(in CrashFacts f)
    {
        bool lostWing = Has(f.Lost, AirframeComponent.WingLeft) || Has(f.Lost, AirframeComponent.WingRight)
                     || Has(f.Lost, AirframeComponent.WingLeftInner) || Has(f.Lost, AirframeComponent.WingRightInner);
        bool lostGear = Has(f.Lost, AirframeComponent.GearLeft) || Has(f.Lost, AirframeComponent.GearRight) || Has(f.Lost, AirframeComponent.GearNose);
        bool propGone = Has(f.Lost, AirframeComponent.Propeller);

        // Pulled the wings off: the airframe failed before anything was touched.
        if (f.Cause == DamageCause.Overstress) return CrashKind.StructuralFailure;
        // Still rotating at impact, past the stall, going down: a spin, flat if it was turning hard enough.
        if (!f.OnGround && f.AlphaDeg >= FlatSpinAlphaDeg && System.Math.Abs(f.YawRateDegS) >= FlatSpinYawRateDegS && f.IasKt <= FlatSpinMaxIasKt)
            return CrashKind.FlatSpin;
        if (!f.OnGround && f.AlphaDeg >= 16 && System.Math.Abs(f.YawRateDegS) >= 25 && f.AglM < 300)
            return CrashKind.StallSpinLow;
        if (f.OnWater) return CrashKind.Ditching;
        if (f.Cause == DamageCause.Tree) return CrashKind.TreeStrike;
        if (f.Cause == DamageCause.Solid && !f.OnGround) return CrashKind.ObstacleStrike;
        if (f.Cause == DamageCause.Solid) return CrashKind.TaxiCollision;
        // Low and fast and banked hard, or inverted: showing off rather than landing.
        if (!f.OnGround && f.AglM < 60 && f.IasKt > 70 && System.Math.Abs(f.BankDeg) > 60) return CrashKind.LowAerobatics;
        if (!f.OnGround && f.AglM < 60 && Wingtip(f.Lost)) return CrashKind.WingStrike;
        if (lostWing) return CrashKind.StructuralFailure;
        // On or near the ground: the landing's own catalogue of ways to go wrong.
        if (f.OnGround && System.Math.Abs(f.YawRateDegS) >= 40 && f.IasKt > 10) return CrashKind.GroundLoop;
        if (f.Impacts >= 3 && f.SinkFpm > 100) return CrashKind.Bounced;
        if (f.GearRetracted && f.OnGround) return CrashKind.GearUp;
        if (f.AlphaDeg < -45 || (f.OnGround && propGone && f.IasKt < 25)) return CrashKind.NoseOver;
        if (lostGear || f.SinkFpm > 700) return CrashKind.HardLanding;
        if (propGone || f.Cause == DamageCause.GroundContact && f.IasKt < 15) return CrashKind.PropStrike;
        // Nothing dramatic happened to it; it simply stopped flying, or never stopped going forwards.
        if (f.FuelDry || f.EngineStopped) return CrashKind.FuelExhaustion;
        return CrashKind.Cfit;
    }

    private static bool Has(IReadOnlyCollection<AirframeComponent> lost, AirframeComponent c)
    {
        foreach (AirframeComponent x in lost) if (x == c) return true;
        return false;
    }

    private static bool Wingtip(IReadOnlyCollection<AirframeComponent> lost) =>
        Has(lost, AirframeComponent.WingTipLeft) || Has(lost, AirframeComponent.WingTipRight)
        || Has(lost, AirframeComponent.WingTipLowerLeft) || Has(lost, AirframeComponent.WingTipLowerRight)
        || Has(lost, AirframeComponent.WingLeftOuter) || Has(lost, AirframeComponent.WingRightOuter);
}
