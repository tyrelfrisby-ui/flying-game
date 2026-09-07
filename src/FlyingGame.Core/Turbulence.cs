using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Continuous atmospheric turbulence as a divergence-free spatial gust field (incompressible
/// turbulence — every eddy's velocity is perpendicular to its wavevector, so the air neither piles up
/// nor rarefies). Intensity and length scale follow the Dryden/MIL-F-8785C picture: RMS gust velocity
/// sets the bumpiness, the length scale sets the eddy size. The field is FROZEN in space and drifts
/// with the mean wind (Taylor's hypothesis) — an aircraft flying through it feels time-varying gusts,
/// and bubbles fixed in the air ride the same local velocity so the turbulence is visible.
///
/// Sampling is a fixed sum of sinusoidal modes: deterministic (no per-call RNG — reproducible and
/// resume-safe), cheap enough to query per bubble per frame.
/// </summary>
public sealed class Turbulence
{
    // MIL-F-8785C-ish RMS gust intensities (m/s): light ~1.5, moderate ~4, severe ~9.
    public static Turbulence Light(double seed = 1) => new(1.5, 250.0, seed);
    public static Turbulence Moderate(double seed = 1) => new(4.0, 300.0, seed);
    public static Turbulence Severe(double seed = 1) => new(9.0, 350.0, seed);

    private const int Modes = 14;
    private readonly Vec3[] _k = new Vec3[Modes];   // wavevectors (rad/m)
    private readonly Vec3[] _amp = new Vec3[Modes];  // mode velocity amplitudes (perp to k)
    private readonly double[] _phase = new double[Modes];

    public double RmsMs { get; }
    public double LengthScaleM { get; }
    public Vec3 MeanWind { get; set; } = Vec3.Zero;   // steady wind that also convects the eddies

    public Turbulence(double rmsMs, double lengthScaleM, double seed)
    {
        RmsMs = rmsMs;
        LengthScaleM = lengthScaleM;

        // Deterministic pseudo-random mode set from the seed (a small LCG — no Random, resume-safe).
        ulong st = (ulong)(seed * 2654435761.0) | 1UL;
        double Next() { st = st * 6364136223846793005UL + 1442695040888963407UL; return (st >> 11) / (double)(1UL << 53); }

        double k0 = 2.0 * System.Math.PI / lengthScaleM;
        // Calibrated: the divergence-free (perp-to-k) projection removes ~1/3 of the energy, so boost
        // amplitude by ~1.74 to hit the target per-component RMS = rmsMs (verified numerically).
        double perMode = 1.74 * rmsMs / System.Math.Sqrt(Modes);
        for (int i = 0; i < Modes; i++)
        {
            // Random direction for the wavevector, magnitude spread around k0 (a crude spectrum).
            Vec3 dir = RandUnit(Next, Next);
            double kmag = k0 * (0.5 + 1.5 * Next());
            _k[i] = dir * kmag;

            // Velocity amplitude perpendicular to k (divergence-free), random orientation in that plane.
            Vec3 perp = Perp(dir, RandUnit(Next, Next));
            _amp[i] = perp * (perMode * System.Math.Sqrt(2.0)); // sqrt2: sinusoid RMS = amp/sqrt2
            _phase[i] = Next() * 2.0 * System.Math.PI;
        }
    }

    /// <summary>Gust velocity (m/s, world frame) at a position and time. Mean wind added.</summary>
    public Vec3 WindAt(Vec3 pos, double timeSec)
    {
        Vec3 p = pos - MeanWind * timeSec; // frozen field convecting downwind
        Vec3 g = Vec3.Zero;
        for (int i = 0; i < Modes; i++)
        {
            double arg = _k[i].X * p.X + _k[i].Y * p.Y + _k[i].Z * p.Z + _phase[i];
            g += _amp[i] * System.Math.Sin(arg);
        }

        return g + MeanWind;
    }

    private static Vec3 RandUnit(System.Func<double> a, System.Func<double> b)
    {
        double z = 2.0 * a() - 1.0;
        double t = 2.0 * System.Math.PI * b();
        double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - z * z));
        return new Vec3(r * System.Math.Cos(t), r * System.Math.Sin(t), z);
    }

    private static Vec3 Perp(Vec3 k, Vec3 hint)
    {
        Vec3 c = Vec3.Cross(k, hint);
        double len = c.Length;
        if (len < 1e-6)
        {
            c = Vec3.Cross(k, new Vec3(1, 0, 0));
            len = c.Length;
            if (len < 1e-6) { c = Vec3.Cross(k, new Vec3(0, 1, 0)); len = c.Length; }
        }

        return c / len;
    }
}
