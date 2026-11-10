namespace FlyingGame.Core;

/// <summary>
/// THE COMPRESSION STROKES OF A DEAD ENGINE, so a propeller LUMPS to a stop instead of decaying smoothly, and once
/// stopped stays stopped until the air can shove it over a compression. Ty, 2026-11-10:
///
///   "Cranking resistance should be calibrated such that at fine pitch the prop stops right at level flight stall
///    speed. Use real 'compression strokes' for the number of cyl of the engine so the prop lumps to a stop instead
///    of stopping smoothly. To get prop windmilling again should take 10% under vne but occasional flips through a
///    compression stroke can occur at speeds below (simulate slow leak down on each cyl to accommodate the tick over
///    but give each cylinder a different leak down rate to keep it from being predictable"
///
/// <see cref="PistonEngine.MotoringTorqueNm"/> is the MEAN of what it takes to turn the engine — pumping and friction,
/// out of the power curve's own friction intercept. This is the RIPPLE on top of it, and the ripple is the whole
/// character: the mean alone gives a propeller that slows smoothly and creeps to a halt, where a real one stops with a
/// thud against a cylinder on compression.
///
/// THE SHAPE IS THE SLIDER-CRANK AND A POLYTROPIC GAS, not a chosen waveform. For one cylinder at crank angle phi from
/// top dead centre the swept volume is
///
///     V(phi) = Vc · (1 + (CR − 1)/2 · (1 − cos phi))
///
/// (Vc at TDC, Vc·CR at BDC), the trapped gas follows p = p_amb · (V_bdc / V)^n with n = 1.3, and the torque the gas
/// makes on the crank is (p − p_amb)·dV/dphi. Compressing, phi runs DOWN to zero against the rotation, so that torque
/// opposes it; expanding it runs up with it, so the gas gives the work back. A sealed cylinder therefore costs nothing
/// net over a cycle — which is correct, and is why the mean comes from the friction intercept rather than from here.
/// What this contributes is the lumpiness, and a hard lump near TDC is what stops a propeller.
///
/// A FOUR-STROKE FIRES EACH CYLINDER ONCE PER TWO REVOLUTIONS, so the compression and expansion together occupy one
/// revolution of every two, and the cylinders are evenly spaced through 720 degrees. Four cylinders therefore give two
/// compressions per revolution with gaps between them; a nine-cylinder radial gives four and a half, overlapping, and
/// it hardly lumps at all. That spread is free — it is the cylinder count and nothing else.
///
/// LEAK-DOWN IS WHAT LETS IT TICK OVER. A cylinder stopped on compression bleeds past its rings, the torque holding
/// the propeller falls, and it creeps over onto the NEXT cylinder's compression, which is fresh and sealed. Each
/// cylinder gets its own leak rate (<see cref="LeakTauSec"/>), so the aeroplane never ticks in a rhythm — Ty's "give
/// each cylinder a different leak down rate to keep it from being predictable".
/// </summary>
public static class PistonCompression
{
    /// <summary>Polytropic exponent for the trapped charge: 1.3 is the standard value for a cylinder with heat loss
    /// (1.4 is the adiabatic ideal, which a real cylinder does not reach).</summary>
    public const double PolytropicN = 1.3;

    /// <summary>
    /// ONE CYLINDER'S GAS TORQUE, as a share of the peak a sealed cylinder makes: negative while it is being
    /// compressed, positive while it expands, zero over the rest of the four-stroke cycle.
    /// <paramref name="cycleRad"/> is this cylinder's own position in its 720-degree cycle (0 .. 4π); the gas spring
    /// occupies the last revolution of it, with top dead centre at 3π.
    /// <paramref name="leak01"/> is 1 for a sealed cylinder and 0 for one that has bled down completely, and it
    /// multiplies the gauge pressure — so a leaked cylinder is both easier to compress and gives less back.
    /// </summary>
    public static double CylinderTorqueFactor(double cycleRad, double compressionRatio, double leak01 = 1.0)
    {
        double cycle = Mod(cycleRad, 4.0 * Math.PI);
        if (cycle < 2.0 * Math.PI) return 0.0;                 // induction and exhaust: the valves are open, no spring
        double cr = Math.Max(1.5, compressionRatio);
        double leak = Math.Clamp(leak01, 0.0, 1.0);

        // phi from TDC, which sits at 3π: compressing on the way up (phi falling to 0), expanding after.
        double phi = cycle - 3.0 * Math.PI;                     // −π .. +π
        bool compressing = phi < 0.0;
        double p = Math.Abs(phi);                               // π at BDC, 0 at TDC
        double vOverVc = 1.0 + (cr - 1.0) * 0.5 * (1.0 - Math.Cos(p));
        double gauge = leak * (Math.Pow(cr / vOverVc, PolytropicN) - 1.0);        // (p − p_amb) / p_amb
        double dVdPhi = (cr - 1.0) * 0.5 * Math.Sin(p);                           // per Vc, always ≥ 0 on 0..π
        double q = gauge * dVdPhi;
        return (compressing ? -q : q) / PeakFor(cr);
    }

    /// <summary>
    /// THE WHOLE ENGINE'S RIPPLE at this crank angle, as a share of the peak one cylinder makes sealed — so −1 is a
    /// single cylinder squarely on compression and a four-cylinder lumps near that while a nine-cylinder radial, whose
    /// strokes overlap, never gets far from zero.
    /// <paramref name="leak"/> is each cylinder's seal, 1 .. 0, in firing order; pass nothing for a sealed engine.
    /// </summary>
    public static double RippleFactor(double crankRad, int cylinders, double compressionRatio, double[]? leak = null)
    {
        int n = Math.Max(1, cylinders);
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            // Evenly spaced through the 720-degree cycle, which is what makes the gaps close up as cylinders are added.
            double phase = crankRad + 4.0 * Math.PI * i / n;
            sum += CylinderTorqueFactor(phase, compressionRatio, leak is { Length: > 0 } ? leak[i % leak.Length] : 1.0);
        }
        return sum;
    }

    /// <summary>The most a whole sealed engine of this many cylinders resists, as a share of one cylinder's peak.
    /// Swept rather than derived: where the cylinders' strokes overlap the peaks add, and how much they add is the
    /// cylinder count's own business.</summary>
    public static double WorstRipple(int cylinders, double compressionRatio)
    {
        double worst = 0.0;
        for (int i = 0; i <= 1440; i++)
        {
            double r = RippleFactor(4.0 * Math.PI * i / 1440.0, cylinders, compressionRatio);
            if (r < worst) worst = r;
        }
        return -worst;
    }

    /// <summary>One cylinder's peak |gas torque| over its cycle, sealed, in the same units as the expression above —
    /// the normaliser, so <see cref="CylinderTorqueFactor"/> comes back as a share of it.</summary>
    private static double PeakFor(double cr)
    {
        if (Math.Abs(cr - _peakCr) < 1e-9) return _peak;
        double best = 1e-9;
        for (int i = 1; i < 720; i++)
        {
            double p = Math.PI * i / 720.0;
            double vOverVc = 1.0 + (cr - 1.0) * 0.5 * (1.0 - Math.Cos(p));
            double q = (Math.Pow(cr / vOverVc, PolytropicN) - 1.0) * (cr - 1.0) * 0.5 * Math.Sin(p);
            if (q > best) best = q;
        }
        _peakCr = cr; _peak = best;
        return best;
    }
    private static double _peakCr = double.NaN, _peak = 1.0;

    /// <summary>
    /// HOW LONG EACH CYLINDER TAKES TO BLEED DOWN, seconds, deliberately all different. A healthy cylinder holds
    /// compression for a few seconds and a tired one for well under one, which is what a leak-down check measures; the
    /// spread here is that, dealt out from the aeroplane's own id so a given aeroplane is consistent from flight to
    /// flight while no two cylinders and no two types agree. 0.6 s to 4.5 s.
    /// </summary>
    public static double[] LeakTauSec(int cylinders, int seed)
    {
        int n = Math.Max(1, cylinders);
        var taus = new double[n];
        uint s = (uint)seed | 1u;
        for (int i = 0; i < n; i++)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;           // xorshift: deterministic, and no two neighbours alike
            taus[i] = 0.6 + 3.9 * ((s & 0xFFFFu) / 65535.0);
        }
        return taus;
    }

    private static double Mod(double a, double m)
    {
        double r = a % m;
        return r < 0.0 ? r + m : r;
    }
}
