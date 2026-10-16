using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// A WHOLE-AIRCRAFT ballistic recovery parachute (owner 2026-10-16: "if an airplane has a brs like a cirrus
/// display deploy brs instead of bail out and model a whole airplane ballistic recovery chute instead").
///
/// This is not the pilot's parachute with a bigger canopy on it. Nobody leaves: a rocket drags a canopy out
/// of the airframe, the whole aeroplane ends up hanging underneath it, and it arrives on its undercarriage
/// at about 1,700 fpm — which the published figures put at "the equivalent of a drop from 13 feet".
///
/// THE NUMBERS are the Cirrus CAPS ones, and they agree with each other, which is why they are trusted:
///   • canopy 2,400 ft² (223 m²) projected, Cd ≈ 1.4 on projected area (a round canopy's Cd referred to its
///     PROJECTED area runs 1.3–1.5; referred to the flat nominal area it is ~0.75, and mixing the two is how
///     you end up with a descent rate half as fast again as the book's).
///   • CdS = 312 m². At the SR22's 1,542 kg that gives sqrt(2mg/ρ·CdS) = 8.9 m/s = 1,750 fpm, against a
///     published "approximately 1,700 fpm"; and 8.9 m/s is a fall from 4.0 m — 13 ft. Both figures, one CdS.
///   • Vpd (maximum demonstrated deployment speed) 140 KIAS. Above it the risers are being asked for more
///     than they were proven to take.
///   • 500 ft AGL in level flight, 920 ft from a spin: the altitude the sequence needs to finish in.
///
/// THE SEQUENCE, and it takes its time — about 8 seconds and 400 ft, which is most of the 500 ft minimum:
///   0 – 0.6 s   ROCKET: fires aft and up, drags the bag off its mount at about 100 mph.
///   0.6 – 2.0 s LINE STRETCH: the lines pay out; the canopy is still a bag and barely drags at all.
///   2.0 – 8.0 s REEFED INFLATION: the slider holds the skirt in and lets it open SLOWLY — which is the
///               whole trick, because a canopy this size opening fast would tear the aeroplane apart.
///   8.0 s       FULL, and the aeroplane has swung to hang under it.
///
/// THE ATTITUDE IS NOT SCRIPTED. The canopy's drag is applied at the harness bridle, which sits ABOVE the
/// centre of gravity — so the aeroplane hangs below it for the same reason any pendulum does, and swings and
/// settles on its own aerodynamic damping. Nothing here sets a pitch angle.
/// </summary>
public sealed class BallisticRecovery
{
    public enum Phase
    {
        Stowed,
        Rocket,        // the rocket is out, dragging the bag off
        LineStretch,   // lines paying out, canopy still bagged
        Inflating,     // the slider is letting it open
        Full,
        Failed,        // deployed too fast: the canopy is a streamer and will not carry anything
    }

    public const double RocketSec = 0.6, LineStretchSec = 2.0, FullSec = 8.0;
    /// <summary>Drag area while the lines are still paying out — a bag and a rocket, not a canopy.</summary>
    public const double BagCdS = 2.5;
    /// <summary>
    /// Deploying above Vpd does not fail instantly — the demonstrated speed has margin in it — but past this
    /// multiple of it the risers let go. Deterministic: the same speed gives the same answer every time.
    /// </summary>
    public const double FailAboveVpdFactor = 1.25;
    /// <summary>How much harder it opens when it is deployed over Vpd (the slider can only do so much).</summary>
    public const double OverspeedShock = 1.6;

    public Phase Current { get; private set; } = Phase.Stowed;
    public double TimeSec { get; private set; }
    /// <summary>Indicated airspeed it was fired at, and the height it had — what the outcome is judged on.</summary>
    public double DeployIasMs { get; private set; }
    public double DeployAglM { get; private set; }
    public bool Out => Current != Phase.Stowed;
    /// <summary>It carries the aeroplane only once there is a canopy there to carry it.</summary>
    public bool Carrying => Current == Phase.Inflating || Current == Phase.Full;

    private double _shock = 1.0;

    /// <summary>
    /// Fire it. <paramref name="iasMs"/> decides whether the canopy survives, and both figures are kept so
    /// the outcome can say WHY — "deployed at 168 kt" is the answer to most of the ways this ends badly.
    /// </summary>
    public void Deploy(BrsConfig cfg, double iasMs, double aglM)
    {
        if (Out) return;
        DeployIasMs = iasMs;
        DeployAglM = aglM;
        TimeSec = 0;
        Current = Phase.Rocket;
        _shock = iasMs > cfg.MaxDeployIasMs ? OverspeedShock : 1.0;
        if (iasMs > cfg.MaxDeployIasMs * FailAboveVpdFactor) Current = Phase.Failed;
    }

    public void Reset()
    {
        Current = Phase.Stowed;
        TimeSec = 0;
        DeployIasMs = DeployAglM = 0;
        _shock = 1.0;
    }

    public void Step(double dt)
    {
        if (!Out || Current == Phase.Failed) return;
        TimeSec += dt;
        Current = TimeSec < RocketSec ? Phase.Rocket
                : TimeSec < LineStretchSec ? Phase.LineStretch
                : TimeSec < FullSec ? Phase.Inflating
                : Phase.Full;
    }

    /// <summary>Drag area right now, m². Zero until the rocket has something out behind it.</summary>
    public double CdS(BrsConfig cfg)
    {
        double full = cfg.Cd * cfg.CanopyProjectedAreaM2;
        switch (Current)
        {
            case Phase.Stowed: return 0;
            // A streamer still drags a little — enough to be felt, nowhere near enough to carry anything.
            case Phase.Failed: return BagCdS * 1.6;
            case Phase.Rocket: return BagCdS * System.Math.Clamp(TimeSec / RocketSec, 0, 1);
            case Phase.LineStretch: return BagCdS;
            case Phase.Full: return full;
            default:
                // Reefed inflation on an S-curve, with the opening shock at the end of the fill: the canopy
                // over-inflates past its steady size before it settles back.
                double u = System.Math.Clamp((TimeSec - LineStretchSec) / (FullSec - LineStretchSec), 0, 1);
                double s = u * u * (3 - 2 * u);
                double cds = BagCdS + (full - BagCdS) * s;
                double peak = 1.0 + (_shock - 1.0 + 0.18) * System.Math.Exp(-System.Math.Pow((u - 0.86) / 0.1, 2));
                return cds * peak;
        }
    }

    /// <summary>
    /// What the canopy does to the aeroplane, in BODY axes. The drag opposes the aeroplane's motion through
    /// the air and is applied AT THE BRIDLE, which is above the centre of gravity — that offset is the only
    /// reason the thing hangs the right way up, and it is physics rather than a scripted attitude.
    /// </summary>
    public (Vec3 force, Vec3 moment) Forces(AircraftConfig c, RigidBodyState s, Vec3 windBody, double rho)
    {
        if (!Out || c.Brs is null) return (Vec3.Zero, Vec3.Zero);
        double cds = CdS(c.Brs);
        if (cds <= 0) return (Vec3.Zero, Vec3.Zero);

        Vec3 vAir = s.Velocity - windBody;             // body axes
        double v = vAir.Length;
        if (v < 0.05) return (Vec3.Zero, Vec3.Zero);
        Vec3 drag = vAir * (-0.5 * rho * v * cds);     // opposes the airflow, magnitude ½ρV²·CdS

        Vec3 bridle = c.Brs.BridleVec() - c.Mass.CgVec();
        return (drag, Vec3.Cross(bridle, drag));
    }

    /// <summary>Terminal descent under a full canopy at this density — the number the whole thing is for.</summary>
    public static double DescentRateMs(AircraftConfig c, double rho)
    {
        if (c.Brs is null) return 0;
        double cds = c.Brs.Cd * c.Brs.CanopyProjectedAreaM2;
        return System.Math.Sqrt(2 * c.Mass.MassKg * 9.81 / (rho * cds));
    }

    /// <summary>
    /// Did it have room? The published minimum is for LEVEL flight; out of a spin or a dive it needs far
    /// more, and what actually matters is whether the canopy filled before the ground arrived.
    /// </summary>
    public static bool EnoughHeight(AircraftConfig c, double aglM, double descentRateMs)
        => c.Brs is not null && aglM >= c.Brs.MinSafeAglM;
}
