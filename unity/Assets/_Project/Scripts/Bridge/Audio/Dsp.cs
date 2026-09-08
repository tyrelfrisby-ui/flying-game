using System;
using System.Runtime.CompilerServices;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Allocation-free DSP primitives shared by the procedural flight audio. Everything here is plain
    /// managed math (no UnityEngine.Object access) so it is safe on the audio thread. Filters are
    /// classes allocated once at construction and mutated in place per sample.
    /// </summary>
    internal static class Dsp
    {
        public const int SineTableSize = 2048;
        public static readonly float[] SineTable = BuildSine();

        private static float[] BuildSine()
        {
            var t = new float[SineTableSize + 1];
            for (int i = 0; i <= SineTableSize; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / SineTableSize);
            return t;
        }

        /// <summary>sin(2π·phase) for any phase (cycles), linear-interpolated table lookup.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sin01(float phase)
        {
            phase -= (int)phase;
            if (phase < 0f) phase += 1f;
            float x = phase * SineTableSize;
            int i = (int)x;
            if (i >= SineTableSize) i = SineTableSize - 1;
            float f = x - i;
            float a = SineTable[i];
            return a + (SineTable[i + 1] - a) * f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Wrap01(float p)
        {
            p -= (int)p;
            if (p < 0f) p += 1f;
            return p;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>0 below e0, 1 above e1, smooth cubic between.</summary>
        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static float Pow(float x, float p) => x <= 0f ? 0f : (float)Math.Pow(x, p);

        /// <summary>One-pole smoothing coefficient for a time constant (seconds) at sample rate fs.</summary>
        public static float Tau(float seconds, float fs) =>
            seconds <= 0f ? 1f : 1f - (float)Math.Exp(-1.0 / (seconds * fs));

        /// <summary>Per-sample multiplier that decays by 1/e every <paramref name="seconds"/>.</summary>
        public static float DecayCoef(float seconds, float fs) =>
            seconds <= 0f ? 0f : (float)Math.Exp(-1.0 / (seconds * fs));

        /// <summary>Cheap cubic soft clipper: transparent below ~0.5, hard limit at ±1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SoftClip(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }
    }

    /// <summary>xorshift32 white noise in [-1, 1). One instance per voice so streams decorrelate.</summary>
    internal sealed class Noise
    {
        private uint _s;

        public Noise(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Next()
        {
            uint s = _s;
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            _s = s;
            return (s >> 8) * (1f / 8388608f) - 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Next01() => Next() * 0.5f + 0.5f;

        /// <summary>Uniform in [lo, hi).</summary>
        public float Range(float lo, float hi) => lo + (hi - lo) * Next01();
    }

    /// <summary>One-pole low-pass (also used as a parameter smoother).</summary>
    internal sealed class OnePole
    {
        public float Z;
        private float _a = 1f;

        public void SetCutoff(float hz, float fs)
        {
            if (hz <= 0f) { _a = 0f; return; }
            if (hz > fs * 0.45f) hz = fs * 0.45f;
            _a = 1f - (float)Math.Exp(-2.0 * Math.PI * hz / fs);
        }

        public void SetTime(float seconds, float fs) => _a = Dsp.Tau(seconds, fs);

        public void Reset(float v = 0f) => Z = v;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            Z += _a * (x - Z);
            return Z;
        }
    }

    /// <summary>RBJ biquad, transposed direct form II. Coefficient setters cost a few transcendentals —
    /// call them per block / sub-block, not per sample.</summary>
    internal sealed class Biquad
    {
        private float _b0 = 1f, _b1, _b2, _a1, _a2;
        private float _s1, _s2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float y = _b0 * x + _s1;
            _s1 = _b1 * x - _a1 * y + _s2;
            _s2 = _b2 * x - _a2 * y;
            return y;
        }

        public void Reset() { _s1 = 0f; _s2 = 0f; }

        public void Bypass() { _b0 = 1f; _b1 = 0f; _b2 = 0f; _a1 = 0f; _a2 = 0f; }

        private static void Prep(float fs, ref float f0, float q, out double cw, out double alpha)
        {
            if (f0 < 10f) f0 = 10f;
            float ny = fs * 0.45f;
            if (f0 > ny) f0 = ny;
            if (q < 0.05f) q = 0.05f;
            double w = 2.0 * Math.PI * f0 / fs;
            cw = Math.Cos(w);
            alpha = Math.Sin(w) / (2.0 * q);
        }

        public void LowPass(float fs, float f0, float q)
        {
            Prep(fs, ref f0, q, out double cw, out double alpha);
            double a0 = 1.0 + alpha;
            _b1 = (float)((1.0 - cw) / a0);
            _b0 = _b2 = _b1 * 0.5f;
            _a1 = (float)(-2.0 * cw / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }

        public void HighPass(float fs, float f0, float q)
        {
            Prep(fs, ref f0, q, out double cw, out double alpha);
            double a0 = 1.0 + alpha;
            _b0 = _b2 = (float)((1.0 + cw) / (2.0 * a0));
            _b1 = (float)(-(1.0 + cw) / a0);
            _a1 = (float)(-2.0 * cw / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }

        /// <summary>Constant 0 dB peak-gain band-pass.</summary>
        public void BandPass(float fs, float f0, float q)
        {
            Prep(fs, ref f0, q, out double cw, out double alpha);
            double a0 = 1.0 + alpha;
            _b0 = (float)(alpha / a0);
            _b1 = 0f;
            _b2 = -_b0;
            _a1 = (float)(-2.0 * cw / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }

        public void Peak(float fs, float f0, float q, float gainDb)
        {
            Prep(fs, ref f0, q, out double cw, out double alpha);
            double a = Math.Pow(10.0, gainDb / 40.0);
            double a0 = 1.0 + alpha / a;
            _b0 = (float)((1.0 + alpha * a) / a0);
            _b1 = (float)(-2.0 * cw / a0);
            _b2 = (float)((1.0 - alpha * a) / a0);
            _a1 = _b1;
            _a2 = (float)((1.0 - alpha / a) / a0);
        }
    }

    /// <summary>
    /// Main-thread → audio-thread telemetry mailbox. Plain volatile floats: each is written whole by
    /// Update and read whole by the render callback; no locks, no allocation.
    /// </summary>
    internal sealed class AudioTelemetry
    {
        public volatile float IasMs;
        public volatile float AlphaDeg;
        public volatile float BetaDeg;
        public volatile float Throttle01;
        public volatile float EngineRpm;
        public volatile float Spoiler01;
        public volatile float TurbulenceLevel;
        public volatile float WindSpeedMs;
        public volatile float MasterGain;   // MasterVolume × (menu open ? 0 : 1)
        public volatile float GroanSeverity;
        public volatile float GravelSpeed, RoughSpeed;   // ground speed while the wheels are on gravel / rough ground
        public volatile bool OnGround;
    }
}
