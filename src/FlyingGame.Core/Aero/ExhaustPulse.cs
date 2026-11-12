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

    /// <summary>PUFFS A SECOND FROM ONE SMOKE PIPE (Ty 2026-11-11: "one puff per pipe every other combustion stroke").
    /// Derived from the engine, never a chosen rate: the combustion strokes a pipe could see
    /// (<see cref="PulseHz"/>) divided by <paramref name="strokesPerPuff"/>. A six at 2,700 rpm every other stroke is
    /// 67.5 Hz a pipe - unresolvable at cruise, which is the point: it pulses at idle and dies with the engine.</summary>
    public static double PuffHz(int cylinders, double rpm, int strokesPerPuff)
        => PulseHz(cylinders, rpm) / System.Math.Max(1, strokesPerPuff);

    /// <summary>HOW MANY CYLINDERS FEED ONE PIPE when a type's stacks split the engine: a flat six whose banks alternate,
    /// with one pipe per bank, puts three into each. A pipe's own event train is <see cref="EventsPerRev"/> of THAT many
    /// cylinders — 1.5 a revolution, not 3 — which is the rate Ty's "one puff per pipe every other combustion stroke"
    /// asks for, and the rate its smoke feathers at.</summary>
    public static int CylindersPerPipe(int cylinders, int strokesPerPuff)
        => System.Math.Max(1, System.Math.Max(1, cylinders) / System.Math.Max(1, strokesPerPuff));

    // ---- THE SMOKE'S DENSITY THROUGH THE CYCLE -----------------------------------------------------------------
    // Ty, 2026-11-11, having looked at the first cut: "The smoke oil is sprayed continuously but the exhaust pulses are
    // cyclical. The exhaust temp is rather co stent [constant] so rather than creating pulses with no smoke at all
    // feather the smoke in and out but never zero."
    //
    // His physics, and it is right. The oil is PUMPED in at a steady rate and the stack is hot and stays hot, so it
    // vaporises continuously — there is smoke in the pipe at every crank angle. What pulses is the CARRIER: the exhaust
    // mass flow surging with each firing sweeps the vaporised oil out in surges. So the density varies cyclically about
    // a FLOOR well above zero, and the floor is the hot stack's own standing contribution.
    //
    // The cyclical term is not a new oscillator: it is <see cref="Velocity"/>, the carrier's instantaneous-over-mean,
    // read off the same crank phase the tubes are already laid down on. Density = 1 − d + d·(u/ū), whose mean over a
    // cycle is EXACTLY 1 for any depth because the carrier's is — so the knob moves the TROUGH and never the mean.

    /// <summary>THE FEATHER'S DEPTH, 0 … 1 — the knob (Developer page, "Smoke"). 0 is a flat continuous column; 1 puts
    /// the trough exactly on <see cref="MinDensity"/>, the deepest feather the hot stack's floor allows. It never gates:
    /// at every depth and every phase the density is ≥ <see cref="MinDensity"/>.
    /// Default 1 — the carrier's own full modulation, limited only by the floor. Defensible rather than pretty: the
    /// surge is the engine's own, measured here, and nothing in it was chosen to look nice.</summary>
    public static double FeatherDepth = 1.0;

    /// <summary>THE FLOOR the feather never goes below, as a share of the mean. 0.28 is measured off this same model
    /// rather than picked: it is <see cref="TroughRatio"/> for a SIX into one collector — all the cylinders into one
    /// pipe, events 120° apart inside a 250° window, so the flow never actually stops between firings and only surges.
    /// A split pipe's own three cylinders are 240° apart and its carrier very nearly DOES stop, and the gap between
    /// those two numbers is exactly the hot stack Ty is describing: the oil goes on vaporising while the pipe is quiet.
    /// 0 would be the gate he rejected.</summary>
    public static double MinDensity = 0.28;

    /// <summary>The smoke's density at crank position <paramref name="crankRev"/> for a pipe fed by
    /// <paramref name="cylindersPerPipe"/> cylinders, as a share of its own mean. Mean 1 over a cycle, never below
    /// <see cref="MinDensity"/>, and it is a FACTOR — the oil flow is multiplied by it, so no smoke in, no smoke out.</summary>
    public static double SmokeDensity(double crankRev, int cylindersPerPipe)
        => SmokeDensity(crankRev, cylindersPerPipe, FeatherDepth, MinDensity);

    /// <summary>The same, with the depth and floor given rather than read off the knobs.</summary>
    public static double SmokeDensity(double crankRev, int cylindersPerPipe, double depth, double minDensity)
    {
        double floor = System.Math.Clamp(minDensity, 0.0, 1.0);
        double trough = TroughRatio(cylindersPerPipe);
        // The depth that would put the trough exactly on the floor; the knob is the fraction of it taken. Written this
        // way the trough is 1 − knob·(1 − floor) — linear in the knob and never under the floor — while the mean stays
        // 1, because the carrier's mean is 1 whatever the depth.
        double d = System.Math.Clamp(depth, 0.0, 1.0) * (1 - floor) / System.Math.Max(1e-9, 1 - trough);
        double m = 1 - d + d * Velocity(crankRev, 1.0, cylindersPerPipe);
        // The floor is a PROMISE, not an aim: TroughRatio is sampled, so a phase between its samples can read a hair
        // under it, and "never zero" has to hold at every phase and not at four thousand of them.
        return m < floor ? floor : m;
    }

    /// <summary>The trough the density reaches at depth <paramref name="depth"/>: 1 − depth·(1 − floor), and so exactly
    /// <see cref="MinDensity"/> at depth 1. Independent of the cylinder count, which is the point of the scaling.</summary>
    public static double FeatherTrough(double depth, double minDensity)
        => 1 - System.Math.Clamp(depth, 0.0, 1.0) * (1 - System.Math.Clamp(minDensity, 0.0, 1.0));

    private static readonly double[] _trough = new double[16], _peak = new double[16];

    /// <summary>The carrier's own minimum over a cycle, as a share of its mean: how far the exhaust flow at one pipe
    /// falls between that pipe's firings. Measured off <see cref="Velocity"/> and cached per cylinder count — 0.281 for
    /// a six into one collector (120° apart, 250° window, so they overlap), 0.007 for a four (180° apart, barely
    /// meeting), smaller still for three into one stack.</summary>
    public static double TroughRatio(int cylinders) { Ratios(cylinders, out double t, out _); return t; }
    /// <summary>The carrier's own maximum over a cycle, as a share of its mean. 1.607 for a six.</summary>
    public static double PeakRatio(int cylinders) { Ratios(cylinders, out _, out double p); return p; }

    private static void Ratios(int cylinders, out double trough, out double peak)
    {
        int c = System.Math.Clamp(cylinders, 1, 15);
        if (_trough[c] <= 0)
        {
            double s = 1.0 / EventsPerRev(c), lo = double.MaxValue, hi = 0;
            // One event's spacing is the train's whole period; sampled finely enough to find its neck. Started well
            // clear of crank 0, where Velocity's backward sum has not yet got a full window of earlier events behind it.
            for (int i = 0; i < 4001; i++)
            {
                double u = Velocity(20 * s + s * i / 4000.0, 1.0, c);
                if (u < lo) lo = u;
                if (u > hi) hi = u;
            }
            _trough[c] = System.Math.Max(1e-9, lo); _peak[c] = hi;
        }
        trough = _trough[c]; peak = _peak[c];
    }

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
