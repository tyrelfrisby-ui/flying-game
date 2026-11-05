namespace FlyingGame.Core.Aero;

/// <summary>
/// THE EXHAUST PULSES (Ty 2026-11-04: "each exhaust pulse leaves the stack as a TUBE … exit velocity is NOT constant: model
/// it as a series of bell curves, one per exhaust-valve event (valve open → blowdown peak → taper as the valve closes)").
///
/// The MEAN exit velocity is <see cref="ExhaustJet.ExitVelocity"/> — the engine's mass flow through the pipe's area. This is
/// how that same mass leaves in time. A four-stroke fires each cylinder once every two revolutions, so the pipe sees
/// <c>cylinders / 2</c> valve events per revolution (a six at 2,700 rpm: 135 a second). Each event is a bell over the
/// valve-open window, in crank angle:
///   g(x) = x^a (1 − x)^b / B(a+1, b+1),   x = (θ − θ_EVO) / θ_open ∈ [0, 1]
/// with a = 2, b = 8: the gas rushes out as the valve cracks (BLOWDOWN, the cylinder still at several atmospheres), peaks 20 %
/// of the way through (50° after the valve opens) and tapers through the displacement stroke as the valve closes. Each event
/// carries the same mass, and the events overlap (a 250° window every 120° on a six) — the velocity at the pipe is their sum,
/// scaled so its average over a revolution is the mean exit velocity: no gas is invented or lost.
///   u(θ) = Ū · Σ_j g(x_j) · (S / W)       S = 2 / cylinders rev between events,  W = θ_open / 360° rev
/// DEFAULTS (no config carries them): exhaust valve open 250° of crank — a Lycoming-class EVO ~60° before bottom centre to
/// EVC ~10° after top — and the shape exponents above. The cylinder count is <see cref="DataContracts.SmokeConfig.Cylinders"/>
/// (6 when a config does not say).
/// </summary>
public static class ExhaustPulse
{
    public const double ValveOpenDeg = 250;
    public const double ShapeA = 2, ShapeB = 8;
    /// <summary>The valve-open window as a fraction of a revolution.</summary>
    public const double WindowRev = ValveOpenDeg / 360.0;
    // B(3, 9) = 2! 8! / 11!
    private const double BetaNorm = 2.0 * 40320.0 / 39916800.0;

    /// <summary>Valve events at one pipe per crank revolution (a four-stroke: half the cylinders).</summary>
    public static double EventsPerRev(int cylinders) => System.Math.Max(1, cylinders) / 2.0;

    /// <summary>Pulses a second at <paramref name="rpm"/>.</summary>
    public static double PulseHz(int cylinders, double rpm) => EventsPerRev(cylinders) * System.Math.Max(0, rpm) / 60.0;

    /// <summary>One event's bell, normalised to unit area over x ∈ [0, 1]; 0 outside the window.</summary>
    public static double Shape(double x) => x <= 0 || x >= 1 ? 0 : x * x * System.Math.Pow(1 - x, ShapeB) / BetaNorm;

    /// <summary>The pipe's exit velocity at crank position <paramref name="crankRev"/> (revolutions since any event-aligned
    /// zero), for a mean exit velocity <paramref name="meanU"/>.</summary>
    public static double Velocity(double crankRev, double meanU, int cylinders)
    {
        if (meanU <= 0) return 0;
        double s = 1.0 / EventsPerRev(cylinders);
        long newest = (long)System.Math.Floor(crankRev / s);
        double sum = 0;
        for (long j = newest; j * s > crankRev - WindowRev; j--) sum += Shape((crankRev - j * s) / WindowRev);
        return meanU * sum * s / WindowRev;
    }

    /// <summary>Where in its window (x, 0…1) an event has let out <paramref name="q"/> of its gas: the tube's tail (0.1),
    /// middle (0.5) and nose (0.9) parcels. The regularised incomplete beta I_x(3, 9), inverted by bisection.</summary>
    public static double MassQuantile(double q)
    {
        q = System.Math.Clamp(q, 0, 1);
        double lo = 0, hi = 1;
        for (int i = 0; i < 50; i++) { double mid = 0.5 * (lo + hi); if (MassBefore(mid) < q) lo = mid; else hi = mid; }
        return 0.5 * (lo + hi);
    }

    /// <summary>I_x(3, 9): the share of an event's gas out by x. Closed form: 1 − Σ_{k=0}^{2} C(11, k) x^k (1 − x)^(11−k).</summary>
    public static double MassBefore(double x)
    {
        if (x <= 0) return 0; if (x >= 1) return 1;
        double y = 1 - x, y9 = System.Math.Pow(y, 9);
        return 1 - (y9 * y * y + 11 * x * y9 * y + 55 * x * x * y9);
    }

    /// <summary>How far (m) gas that leaves at <paramref name="excess"/> m/s faster than the air going its way has carried
    /// itself <paramref name="ageS"/> s later: the free round jet's Lagrangian law past its potential core (x_c = 6 d,
    /// centreline ∝ x_c / x, so x² = x_c² + 2 w x_c t) — capped at <paramref name="spentS"/>, by which time the airframe's
    /// flow has swamped it (<see cref="ExhaustJet"/>: "spent within a metre or so" in the spin's crossflow). Gas no faster
    /// than the air carries itself nowhere: the stream just lays it down.</summary>
    public static double Penetration(double excess, double ageS, double pipeD, double spentS = 0.15)
    {
        if (excess <= 0 || ageS <= 0 || pipeD <= 0) return 0;
        double xc = 6 * pipeD, t = System.Math.Min(ageS, spentS);
        return System.Math.Sqrt(xc * xc + 2 * excess * xc * t) - xc;
    }
}
