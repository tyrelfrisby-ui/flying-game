using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// What the terrain does to the wind at one point: speed-up over a crest, the upslope lift in front of a bluff, the
/// downdraught, rotor and standing waves behind it, and the mechanical turbulence it stirs. Evaluated per sample from
/// the height field along the wind's own line (a 3.2 km upwind look and a short downwind one), so it works over any
/// terrain the world builds and needs no authored ridges.
///
/// Windward (owner 2026-10-27: "the wind strengthens progressively toward the bluff"):
///   speed-up  ΔS = MaxSpeedUp · clamp(Hw / SpeedUpRelief) · e^(−agl / SpeedUpDecay)
///             where Hw is how far the ground here stands above the (distance-weighted) ground upwind. Jackson &amp; Hunt
///             (1975) and the Taylor–Lee guidelines put the crest speed-up at ΔS ≈ 1.6 H/L for a ridge and ~0.8 H/L for an
///             escarpment, decaying with height over ~L; this world's walls are steeper than separation allows
///             (H/L &gt; 1), so the cap is what bites. Climbing the slope at a constant height above it, Hw grows and so
///             does the wind, to its peak at the crest.
///   upslope   w = V·∇h — <see cref="SlopeLift"/>, the kinematic boundary condition carried up tilted streamlines —
///             driven by the SPED-UP wind, so the lift band strengthens as it nears the crest too.
/// Lee (the side the wind leaves):
///   the upwind crest is found by a soft arg-max of the drop from each upwind sample, so it moves smoothly;
///   downdraught  w = −LeeSinkFraction · U · e^(−s/2H) · e^(−((z/H − 0.6)/0.7)²)   (s downwind of crest, H the drop)
///   rotor        a Lamb–Oseen-like vortex RotorDistanceDrops·H downwind, 0.45 H up, radius 0.45 H, turning so its top
///                runs downwind and its bottom back toward the hill (reversed surface flow), rising on the hill side;
///   lee waves    when the air is stable (N² = g/T·(Γd − Γ) &gt; 0) and the wind is over MinWaveWind: alternating
///                down/up bands of wavelength λ = 2πU/N (compressed — see WaveLengthCompression) above crest height,
///                sink first, decaying downstream.
/// Turbulence: σ_oro = (SlopeTurbulence·|∇h|·U + lee/rotor σ) · e^(−agl / max(150, relief)), so it is strongest
/// against the ground, in the rotor, and dies away above the ridge tops.
/// </summary>
public static class Orography
{
    public struct Result
    {
        public double SpeedUp;       // fractional horizontal speed-up (0 = none)
        public double SlopeLiftMs;   // w from V·∇h (+ up)
        public double LeeMs;         // lee downdraught (≤ 0)
        public double WaveMs;        // lee-wave w (±)
        public Vec3 Rotor;           // rotor velocity (world)
        public double SigmaMs;       // orographic turbulence σ
        public double LeeDropM;      // height of the upwind crest above here
        public double CrestDistanceM;// how far downwind of it
        public double ReliefM;       // local relief
    }

    private static readonly double[] Upwind = { 100, 200, 350, 550, 800, 1100, 1500, 2000, 2600, 3200 };

    public static Result Evaluate(System.Func<double, double, double> height, Vec3 pos, Vec3 windH, OrographyConfig c,
        double scale, double lapseKPerM, double temperatureK)
    {
        var r = new Result();
        double u = System.Math.Sqrt(windH.X * windH.X + windH.Y * windH.Y);
        if (u < 0.5 || scale <= 0 || !c.Enabled) return r;
        double dx = windH.X / u, dy = windH.Y / u;
        double alt = -pos.Z, h0 = height(pos.X, pos.Y), agl = System.Math.Max(0.0, alt - h0);

        // Upwind look: the distance-weighted mean ground (for speed-up), the soft arg-max crest (for the lee).
        double wSum = 0, hMean = 0, hMax = h0, hMin = h0;
        var drop = new double[Upwind.Length];
        double dropMax = 0;
        for (int i = 0; i < Upwind.Length; i++)
        {
            double hi = height(pos.X - dx * Upwind[i], pos.Y - dy * Upwind[i]);
            double wi = System.Math.Exp(-Upwind[i] / 700.0);
            wSum += wi; hMean += wi * hi;
            if (hi > hMax) hMax = hi; if (hi < hMin) hMin = hi;
            drop[i] = System.Math.Max(0.0, hi - h0);
            if (drop[i] > dropMax) dropMax = drop[i];
        }
        hMean /= wSum;
        double hDn1 = height(pos.X + dx * 100, pos.Y + dy * 100), hUp1 = height(pos.X - dx * 100, pos.Y - dy * 100);
        double hL = height(pos.X - dy * 100, pos.Y + dx * 100), hR = height(pos.X + dy * 100, pos.Y - dx * 100);
        hMax = System.Math.Max(hMax, System.Math.Max(hDn1, System.Math.Max(hL, hR)));
        hMin = System.Math.Min(hMin, System.Math.Min(hDn1, System.Math.Min(hL, hR)));
        double relief = hMax - hMin;
        r.ReliefM = relief;

        // ---- windward: speed-up and upslope lift ----
        double hw = System.Math.Max(0.0, h0 - hMean);
        r.SpeedUp = scale * c.MaxSpeedUp * System.Math.Clamp(hw / c.SpeedUpReliefM, 0, 1) * System.Math.Exp(-agl / c.SpeedUpDecayM);
        Vec3 fast = windH * (1 + r.SpeedUp);
        r.SlopeLiftMs = -SlopeLift.WindAt(height, pos, fast).Z * scale;

        // ---- lee: the crest upwind, smoothly ----
        double sw = 0, sDrop = 0, sDist = 0;
        for (int i = 0; i < Upwind.Length; i++)
        {
            double wi = System.Math.Exp((drop[i] - dropMax) / 40.0);
            sw += wi; sDrop += wi * drop[i]; sDist += wi * Upwind[i];
        }
        double H = sDrop / sw, s = sDist / sw;
        r.LeeDropM = H; r.CrestDistanceM = s;
        double gate = SmoothStep((H - 0.5 * c.MinLeeDropM) / (0.5 * c.MinLeeDropM));
        double slope = System.Math.Sqrt(System.Math.Pow((hDn1 - hUp1) / 200.0, 2) + System.Math.Pow((hL - hR) / 200.0, 2));
        double sigmaLee = 0;
        if (gate > 0)
        {
            double z = agl / H;
            double zs = (z - 0.6) / 0.7;
            r.LeeMs = -gate * scale * c.LeeSinkFraction * u * System.Math.Exp(-s / (2 * H)) * System.Math.Exp(-zs * zs);

            double sr = c.RotorDistanceDrops * H, zr = 0.45 * H, R = 0.45 * H;
            double ds = s - sr, dz = agl - zr, dist = System.Math.Sqrt(ds * ds + dz * dz);
            if (dist > 1e-6)
            {
                double rho = dist / R;
                double ut = gate * scale * c.RotorStrength * u * rho * System.Math.Exp(0.5 * (1 - rho * rho));
                double us = ut * dz / dist, wz = -ut * ds / dist;   // top downwind, bottom back upwind; down on the far side
                r.Rotor = new Vec3(dx * us, dy * us, -wz);
            }
            sigmaLee = gate * c.LeeTurbulence * u * System.Math.Exp(-s / (3 * H)) * System.Math.Exp(-System.Math.Max(0, z - 1) / 0.5);

            // Stable air, a good wind: standing lee waves above the crest.
            double n2 = Atmosphere.GravityMs2 / temperatureK * (0.0098 - lapseKPerM);
            if (n2 > 1e-7 && u > 0.5 * c.MinWaveWindMs)
            {
                double lambda = 2 * System.Math.PI * u / System.Math.Sqrt(n2) * c.WaveLengthCompression;
                double zc = agl - H;
                double env = System.Math.Exp(-(zc - 0.5 * H) * (zc - 0.5 * H) / (2 * (H + 400) * (H + 400))) * SmoothStep((zc + 0.3 * H) / (0.3 * H));
                double windGate = SmoothStep((u - 0.5 * c.MinWaveWindMs) / (0.5 * c.MinWaveWindMs));
                r.WaveMs = -gate * windGate * scale * c.WaveAmplitude * u * (H / (H + 300)) * System.Math.Sin(2 * System.Math.PI * s / lambda)
                           * System.Math.Exp(-s / (3 * lambda)) * env;
            }
        }
        double decay = System.Math.Exp(-agl / System.Math.Max(150.0, relief));
        r.SigmaMs = scale * (c.SlopeTurbulence * System.Math.Min(slope, 1.0) * u * decay + sigmaLee);
        return r;
    }

    /// <summary>C1 smoothstep on [0, 1] (clamped).</summary>
    public static double SmoothStep(double t) { t = System.Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
}
