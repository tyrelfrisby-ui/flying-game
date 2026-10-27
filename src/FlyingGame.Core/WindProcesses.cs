namespace FlyingGame.Core;

/// <summary>
/// Seeded, deterministic random numbers that do not depend on the order they are asked for: the n-th draw of a stream
/// is a hash of (seed, stream, n). A wind process built on these gives the same weather at the same sim time however
/// the frames fell, which is what makes it frame-rate independent, replayable and resume-safe.
/// </summary>
public static class SeededNoise
{
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public static double Uniform(int seed, int stream, long n) =>
        (Mix(Mix((ulong)(uint)seed * 0x100000001B3UL + (ulong)(uint)stream) ^ (ulong)n) >> 11) / (double)(1UL << 53);

    /// <summary>Standard normal (Box–Muller on two draws).</summary>
    public static double Gaussian(int seed, int stream, long n)
    {
        double u1 = System.Math.Max(1e-12, Uniform(seed, stream, 2 * n));
        double u2 = Uniform(seed, stream, 2 * n + 1);
        return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
    }

    /// <summary>Smooth 2-D value noise in [-1, 1] (quintic-faded lattice), for convective cells.</summary>
    public static double Value2D(double x, double y, int seed)
    {
        double fx = System.Math.Floor(x), fy = System.Math.Floor(y);
        long ix = (long)fx, iy = (long)fy;
        double tx = x - fx, ty = y - fy;
        tx = tx * tx * tx * (tx * (tx * 6 - 15) + 10);
        ty = ty * ty * ty * (ty * (ty * 6 - 15) + 10);
        double L(long i, long j) => 2.0 * Uniform(seed, (int)(i * 73856093L ^ j * 19349663L), i * 83492791L + j) - 1.0;
        double a = L(ix, iy), b = L(ix + 1, iy), c = L(ix, iy + 1), d = L(ix + 1, iy + 1);
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
    }
}

/// <summary>
/// LIGHT AND VARIABLE (airport 2, owner 2026-10-27: "variable wind of about 5 kt whose direction wanders randomly
/// through the full 360 degrees … a filtered random walk, not jumps").
///
/// The direction is the integral of a turn RATE that is itself an Ornstein–Uhlenbeck process (a random walk on a
/// spring): the rate has a standard deviation <c>TurnRateSigma</c>, forgets itself over <c>Correlation</c> seconds and is
/// hard-capped at <c>TurnRateMax</c>. So the wind backs and veers in smooth swings that last tens of seconds, never
/// jumps, and — because the direction itself is not tied to anything — drifts all the way round over minutes
/// (diffusion D = σ²τ: 2.5°/s and 40 s give a 1σ wander of ~1,300° an hour). The speed wanders the same way about its
/// mean. Knots every <see cref="KnotDtS"/> s are generated from hashed draws and joined by cubic Hermite curves whose
/// slopes are the turn rates, so the direction and its rate are continuous at any sample time.
/// </summary>
public sealed class VariableWind
{
    public const double KnotDtS = 1.0;
    public double MeanSpeedMs { get; }
    private readonly double _speedSigma, _rateSigma, _rateMax, _tau;
    private readonly double _fromRad0;
    private readonly int _seed;
    private readonly List<(double dir, double rate, double speed)> _knots = new();

    public VariableWind(double meanSpeedMs, double speedJitterMs, double rateSigmaDegS, double rateMaxDegS, double correlationS, double initialFromDeg, int seed)
    {
        MeanSpeedMs = meanSpeedMs; _speedSigma = speedJitterMs; _tau = System.Math.Max(1.0, correlationS);
        _rateSigma = rateSigmaDegS * System.Math.PI / 180; _rateMax = rateMaxDegS * System.Math.PI / 180;
        _fromRad0 = initialFromDeg * System.Math.PI / 180; _seed = seed;
    }

    private void Extend(long n)
    {
        if (_knots.Count == 0) _knots.Add((_fromRad0, 0.0, MeanSpeedMs));
        double a = System.Math.Exp(-KnotDtS / _tau), b = System.Math.Sqrt(1 - a * a);
        double aS = System.Math.Exp(-KnotDtS / (2 * _tau)), bS = System.Math.Sqrt(1 - aS * aS);
        while (_knots.Count <= n + 1)
        {
            int k = _knots.Count;
            var (d, r, s) = _knots[k - 1];
            double r1 = System.Math.Clamp(r * a + _rateSigma * b * SeededNoise.Gaussian(_seed, 11, k), -_rateMax, _rateMax);
            double s1 = MeanSpeedMs + (s - MeanSpeedMs) * aS + _speedSigma * bS * SeededNoise.Gaussian(_seed, 12, k);
            s1 = System.Math.Clamp(s1, 0.4 * MeanSpeedMs, 1.6 * MeanSpeedMs);
            _knots.Add((d + 0.5 * (r + r1) * KnotDtS, r1, s1));
        }
    }

    /// <summary>Direction the wind blows FROM (rad, unwrapped — it keeps counting past 2π) and its speed (m/s).</summary>
    public (double fromRad, double speedMs) At(double timeSec)
    {
        double t = System.Math.Max(0.0, timeSec) / KnotDtS;
        long n = (long)System.Math.Floor(t);
        if (n + 1 >= _knots.Count) Extend(n);
        var k0 = _knots[(int)n]; var k1 = _knots[(int)n + 1];
        double u = t - n, u2 = u * u, u3 = u2 * u;
        double h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
        double dir = h00 * k0.dir + h10 * KnotDtS * k0.rate + h01 * k1.dir + h11 * KnotDtS * k1.rate;
        double sm = u2 * (3 - 2 * u);
        return (dir, k0.speed + (k1.speed - k0.speed) * sm);
    }

    /// <summary>The wind vector (world, toward which it blows) at a time.</summary>
    public MathTypes.Vec3 VectorAt(double timeSec)
    {
        var (from, speed) = At(timeSec);
        return new MathTypes.Vec3(-speed * System.Math.Cos(from), -speed * System.Math.Sin(from), 0);
    }
}

/// <summary>
/// GUSTY (airport 3, owner 2026-10-27: "5 kt gusting 15 kt, with a steady base direction and realistic random gust
/// onset and decay"). A steady lull wind with discrete gusts on top, each the classic 1 − cos shape of the MIL-F-8785C
/// discrete-gust model and CS-25.341: u = A/2·(1 − cos 2πτ/T) over its duration T. Gusts arrive at random — quiet gaps
/// exponentially distributed about a mean, durations uniform in a range, amplitudes uniform from 75 % to 100 % of (peak − lull) — so
/// every gust tops out between ~12.5 and 15 kt. The lull is SOLVED from the statistics so the long-run mean is the
/// METAR's 5 kt: mean = lull + E[A]·E[T]/2 / (E[T] + E[gap]). Like the variable wind, the n-th gust is a hashed draw,
/// so the gust train is the same at the same sim time however the frames fall.
/// </summary>
public sealed class GustyWind
{
    public double MeanSpeedMs { get; }
    public double PeakSpeedMs { get; }
    public double LullSpeedMs { get; }
    public double FromRad { get; }
    private readonly double _meanGap, _tMin, _tMax;
    private readonly int _seed;
    private readonly List<(double start, double dur, double amp)> _gusts = new();
    private const double AmpMinFrac = 0.75;

    public GustyWind(double meanSpeedMs, double peakSpeedMs, double fromDeg, double meanGapS, double minDurS, double maxDurS, int seed)
    {
        MeanSpeedMs = meanSpeedMs; PeakSpeedMs = peakSpeedMs; FromRad = fromDeg * System.Math.PI / 180;
        _meanGap = System.Math.Max(1.0, meanGapS); _tMin = System.Math.Max(1.0, minDurS); _tMax = System.Math.Max(_tMin, maxDurS); _seed = seed;
        double eT = 0.5 * (_tMin + _tMax), duty = eT / 2.0 / (eT + _meanGap), eAfrac = 0.5 * (AmpMinFrac + 1.0);
        // mean = lull + eAfrac·(peak − lull)·duty  →  lull = (mean − eAfrac·duty·peak) / (1 − eAfrac·duty)
        LullSpeedMs = System.Math.Max(0.0, (meanSpeedMs - eAfrac * duty * peakSpeedMs) / (1 - eAfrac * duty));
    }

    private void Extend(double untilSec)
    {
        double t = _gusts.Count == 0 ? 0.0 : _gusts[^1].start + _gusts[^1].dur;
        while (_gusts.Count == 0 || _gusts[^1].start < untilSec + _tMax)
        {
            int k = _gusts.Count;
            double gap = -_meanGap * System.Math.Log(System.Math.Max(1e-9, 1 - SeededNoise.Uniform(_seed, 21, k)));
            double dur = _tMin + (_tMax - _tMin) * SeededNoise.Uniform(_seed, 22, k);
            double amp = (PeakSpeedMs - LullSpeedMs) * (AmpMinFrac + (1 - AmpMinFrac) * SeededNoise.Uniform(_seed, 23, k));
            _gusts.Add((t + gap, dur, amp));
            t += gap + dur;
        }
    }

    /// <summary>The gust increment on top of the lull (m/s, ≥ 0) at a time.</summary>
    public double GustAt(double timeSec)
    {
        double t = System.Math.Max(0.0, timeSec);
        if (_gusts.Count == 0 || _gusts[^1].start < t + _tMax) Extend(t);
        // Binary search for the last gust starting at or before t (gusts never overlap).
        int lo = 0, hi = _gusts.Count - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_gusts[mid].start <= t) lo = mid; else hi = mid - 1; }
        var g = _gusts[lo];
        if (t < g.start || t > g.start + g.dur) return 0.0;
        return 0.5 * g.amp * (1 - System.Math.Cos(2 * System.Math.PI * (t - g.start) / g.dur));
    }

    public double SpeedAt(double timeSec) => LullSpeedMs + GustAt(timeSec);

    /// <summary>Unit vector the wind blows TOWARD.</summary>
    public MathTypes.Vec3 Toward => new(-System.Math.Cos(FromRad), -System.Math.Sin(FromRad), 0);
}
