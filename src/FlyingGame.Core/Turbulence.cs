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
public enum TurbulenceSpectrum { Legacy, VonKarman, Dryden }

public sealed class Turbulence
{
    // MIL-F-8785C-ish RMS gust intensities (m/s): light ~1.5, moderate ~4, severe ~9.
    public static Turbulence Light(double seed = 1) => new(1.5, 250.0, seed);
    public static Turbulence Moderate(double seed = 1) => new(4.0, 300.0, seed);
    public static Turbulence Severe(double seed = 1) => new(9.0, 350.0, seed);

    private const int Modes = 14;
    private readonly Vec3[] _k = new Vec3[Modes];   // wavevectors (rad/m)
    private readonly Vec3[] _amp = new Vec3[Modes];  // mode velocity amplitudes (perp to k); spectral mode: unit polarisations
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

    // ---- spectral mode (the regional atmosphere's turbulence) ---------------------------------------------------
    //
    // The same sum-of-Fourier-modes field as above, but with the modes laid out on a real turbulence spectrum and the
    // amplitudes chosen per SAMPLE for the local length scale. The mode set (wavevectors, polarisations, phases) is
    // fixed, log-spaced in |k| over four decades; what changes from place to place is only how much energy each mode
    // carries, read from a table built here for a ladder of length scales. So the length scale can vary smoothly over
    // the map (MIL-HDBK-1797: L ≈ h near the ground) and the field stays continuous, and a sample costs one sine per
    // mode whatever the scale. The field returned is UNIT-variance per component; the atmosphere multiplies by σ.
    //
    // 3-D energy spectra (isotropic, so the 1-D longitudinal spectra are the MIL-F-8785C forms with integral scale L):
    //   von Kármán  E(k) ∝ (ℓk)⁴ / (1 + (ℓk)²)^(17/6),  ℓ = 1.339 L
    //   Dryden      E(k) ∝ (Lk)⁴ / (1 + (Lk)²)³
    // Each mode n carries a_n² = 6·E(k_n)Δk_n / ΣEΔk (a sinusoid's mean square is a²/2, a third of it per axis); each
    // row is then rescaled per axis so the realised mode set gives exactly unit variance on each component.

    public TurbulenceSpectrum Spectrum { get; } = TurbulenceSpectrum.Legacy;
    private readonly double[]? _ladderLogL;          // ln L of each table row
    private readonly double[,]? _ampRow;             // [row, mode] amplitude for unit σ
    private readonly double[,]? _axisScale;          // [row, axis] per-axis renormalisation
    private readonly double[]? _omega;               // per-mode evolution rate (rad/s): eddies decay as they drift
    private const double EvolveMs = 0.6;             // eddy turnover speed: small eddies change faster than big ones

    /// <summary>A unit-variance spectral field (see above). The length scale is chosen per sample in
    /// <see cref="UnitGustAt"/>, between <paramref name="minLengthM"/> and <paramref name="maxLengthM"/>.</summary>
    public Turbulence(TurbulenceSpectrum spectrum, int modes, double seed, double minLengthM, double maxLengthM)
    {
        Spectrum = spectrum == TurbulenceSpectrum.Legacy ? TurbulenceSpectrum.VonKarman : spectrum;
        RmsMs = 1.0;
        LengthScaleM = maxLengthM;
        modes = System.Math.Clamp(modes, 8, 256);
        _k = new Vec3[modes]; _amp = new Vec3[modes]; _phase = new double[modes]; _omega = new double[modes];
        ulong st = (ulong)(seed * 2654435761.0) | 1UL;
        double Next() { st = st * 6364136223846793005UL + 1442695040888963407UL; return (st >> 11) / (double)(1UL << 53); }

        double kMin = 0.2 / (1.339 * maxLengthM), kMax = 60.0 / minLengthM;   // eddies from ~30 km down to ~1.6 m
        double dLn = System.Math.Log(kMax / kMin) / (modes - 1);
        var kMag = new double[modes];
        for (int i = 0; i < modes; i++)
        {
            // Jittered log spacing so no two fields (or two axes) share a lattice of wavelengths.
            kMag[i] = kMin * System.Math.Exp(dLn * (i + 0.5 * (Next() - 0.5)));
            Vec3 dir = RandUnit(Next, Next);
            _k[i] = dir * kMag[i];
            _amp[i] = Perp(dir, RandUnit(Next, Next));   // unit polarisation ⊥ k: divergence-free
            _phase[i] = Next() * 2.0 * System.Math.PI;
            _omega[i] = EvolveMs * kMag[i];
        }

        const int rows = 28;
        _ladderLogL = new double[rows];
        _ampRow = new double[rows, modes];
        _axisScale = new double[rows, 3];
        double ln0 = System.Math.Log(minLengthM), ln1 = System.Math.Log(System.Math.Max(maxLengthM, minLengthM * 1.01));
        for (int r = 0; r < rows; r++)
        {
            double lnL = ln0 + (ln1 - ln0) * r / (rows - 1);
            _ladderLogL[r] = lnL;
            double L = System.Math.Exp(lnL), sum = 0;
            var e = new double[modes];
            for (int i = 0; i < modes; i++) { e[i] = EnergySpectrumShape(Spectrum, kMag[i], L) * kMag[i] * dLn; sum += e[i]; }
            double[] axis = new double[3];
            for (int i = 0; i < modes; i++)
            {
                double a = System.Math.Sqrt(6.0 * e[i] / sum);
                _ampRow[r, i] = a;
                axis[0] += 0.5 * a * a * _amp[i].X * _amp[i].X;
                axis[1] += 0.5 * a * a * _amp[i].Y * _amp[i].Y;
                axis[2] += 0.5 * a * a * _amp[i].Z * _amp[i].Z;
            }
            for (int c = 0; c < 3; c++) _axisScale[r, c] = 1.0 / System.Math.Sqrt(System.Math.Max(axis[c], 1e-12));
        }
    }

    /// <summary>Shape of the 3-D energy spectrum E(k) (unnormalised).</summary>
    public static double EnergySpectrumShape(TurbulenceSpectrum s, double k, double lengthScaleM)
    {
        if (s == TurbulenceSpectrum.Dryden)
        {
            double x = k * lengthScaleM, x2 = x * x;
            return x2 * x2 / ((1 + x2) * (1 + x2) * (1 + x2));
        }
        double y = 1.339 * lengthScaleM * k, y2 = y * y;
        return y2 * y2 / System.Math.Pow(1 + y2, 17.0 / 6.0);
    }

    /// <summary>
    /// The spectral field at a point and time: unit variance per component, horizontal components at the
    /// longitudinal scale <paramref name="lengthHorizM"/>, vertical at <paramref name="lengthVertM"/> (MIL-HDBK-1797 has
    /// L_u ≠ L_w near the ground). The field drifts with <paramref name="advect"/> (Taylor's frozen turbulence) and slowly
    /// evolves. Deterministic: a pure function of (position, time, scales).
    /// </summary>
    public Vec3 UnitGustAt(Vec3 pos, double timeSec, double lengthHorizM, double lengthVertM, Vec3 advect)
    {
        if (_ampRow is null || _ladderLogL is null || _axisScale is null || _omega is null) return Vec3.Zero;
        (int rh, double fh) = Row(lengthHorizM);
        (int rv, double fv) = Row(lengthVertM);
        double px = pos.X - advect.X * timeSec, py = pos.Y - advect.Y * timeSec, pz = pos.Z - advect.Z * timeSec;
        double gx = 0, gy = 0, gz = 0;
        int n = _k.Length;
        for (int i = 0; i < n; i++)
        {
            double s = System.Math.Sin(_k[i].X * px + _k[i].Y * py + _k[i].Z * pz + _phase[i] + _omega[i] * timeSec);
            double ah = (_ampRow[rh, i] * (1 - fh) + _ampRow[rh + 1, i] * fh) * s;
            double av = (_ampRow[rv, i] * (1 - fv) + _ampRow[rv + 1, i] * fv) * s;
            gx += ah * _amp[i].X; gy += ah * _amp[i].Y; gz += av * _amp[i].Z;
        }
        double sx = _axisScale[rh, 0] * (1 - fh) + _axisScale[rh + 1, 0] * fh;
        double sy = _axisScale[rh, 1] * (1 - fh) + _axisScale[rh + 1, 1] * fh;
        double sz = _axisScale[rv, 2] * (1 - fv) + _axisScale[rv + 1, 2] * fv;
        return new Vec3(gx * sx, gy * sy, gz * sz);
    }

    private (int row, double frac) Row(double lengthM)
    {
        double[] l = _ladderLogL!;
        double ln = System.Math.Log(System.Math.Max(lengthM, 1e-3));
        double t = (ln - l[0]) / (l[l.Length - 1] - l[0]) * (l.Length - 1);
        t = System.Math.Clamp(t, 0, l.Length - 1 - 1e-9);
        int r = (int)t;
        return (r, t - r);
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

/// <summary>
/// Turbulence intensities and length scales after MIL-F-8785C / MIL-HDBK-1797 (the low-altitude
/// model), the numbers the spectral field above is scaled by. Heights in metres in and out; the formulas are the
/// handbook's, in feet, inside.
///   below 1,000 ft:  L_w = h,  L_u = L_v = h / (0.177 + 0.000823 h)^1.2,
///                    σ_w = 0.1 W20,  σ_u = σ_v = σ_w / (0.177 + 0.000823 h)^0.4
///   above 2,000 ft:  L = 1,750 ft (Dryden) or 2,500 ft (von Kármán), isotropic
///   between:         linear in height.
/// W20 is the wind at 20 ft. (The handbook's high-altitude σ comes from exceedance tables; above the boundary layer
/// this model hands over to a per-region clear-air σ instead — see AtmosphereField.)
/// </summary>
public static class TurbulenceScales
{
    public const double FtPerM = 3.280839895;

    public readonly struct Scales
    {
        public readonly double SigmaU, SigmaW, LengthU, LengthW;
        public Scales(double su, double sw, double lu, double lw) { SigmaU = su; SigmaW = sw; LengthU = lu; LengthW = lw; }
    }

    public static Scales LowAltitude(double aglM, double w20Ms, TurbulenceSpectrum spectrum)
    {
        double hFt = System.Math.Max(10.0, aglM * FtPerM);
        double highL = (spectrum == TurbulenceSpectrum.Dryden ? 1750.0 : 2500.0) / FtPerM;
        static (double su, double sw, double lu, double lw) Low(double h, double w20)
        {
            double d = 0.177 + 0.000823 * h;
            double sw = 0.1 * w20;
            return (sw / System.Math.Pow(d, 0.4), sw, h / System.Math.Pow(d, 1.2) / FtPerM, h / FtPerM);
        }
        if (hFt <= 1000.0)
        {
            var low = Low(hFt, w20Ms);
            return new Scales(low.su, low.sw, low.lu, low.lw);
        }
        var top = Low(1000.0, w20Ms);
        // Above 1,000 ft the boundary layer is isotropic at the top value; the length scales run on to the high-altitude one.
        double sigmaTop = top.su;   // σ_u = σ_w at 1,000 ft (d = 1.0)
        double f = System.Math.Clamp((hFt - 1000.0) / 1000.0, 0, 1);
        return new Scales(sigmaTop, sigmaTop, top.lu + (highL - top.lu) * f, top.lw + (highL - top.lw) * f);
    }
}
