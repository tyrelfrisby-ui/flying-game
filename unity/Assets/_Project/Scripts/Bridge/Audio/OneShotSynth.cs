using System;
using System.Threading;

namespace FlyingGame.Bridge
{
    internal enum FxKind { WingFailure = 0, CanopyJettison, EjectionSeat, ChuteDeploy, ChuteInflate, Count }

    /// <summary>
    /// One procedural sound effect: a scripted envelope over noise / resonator / partial components.
    /// Gains and filter coefficients are re-targeted every 128 samples and smoothed per sample.
    /// </summary>
    internal sealed class FxVoice
    {
        public bool Active;
        public FxKind Kind;
        public float T;                      // seconds since trigger

        private readonly float _fs;
        private readonly Noise _n;
        private readonly Biquad _bpA = new Biquad(), _bpB = new Biquad(), _hp = new Biquad();
        private readonly OnePole _lp = new OnePole();
        private float _phA, _phB, _phC;
        private float _burst, _burstDec;
        private float _gA, _gAT, _gB, _gBT, _gC, _gCT;
        private float _gate = 1f, _gateLevel = 1f;
        private int _gateCount, _sub;
        private float _flutHz = 25f, _len, _ias;
        private bool _snapped;
        private const float Smooth = 1f / 48f;

        public FxVoice(float fs, uint seed) { _fs = fs; _n = new Noise(seed); }

        private static float Env(float t, float attack, float hold, float decayTau)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            t -= attack + hold;
            return t <= 0f ? 1f : (float)Math.Exp(-t / decayTau);
        }

        public void Start(FxKind kind, float ias)
        {
            Kind = kind;
            T = 0f;
            _ias = ias;
            _sub = 0;
            _snapped = false;
            _gA = _gB = _gC = 0f;
            _gate = _gateLevel = 1f;
            _gateCount = 0;
            _bpA.Reset(); _bpB.Reset(); _hp.Reset(); _lp.Reset();
            switch (kind)
            {
                case FxKind.WingFailure:
                    _len = 1.5f;
                    _bpA.BandPass(_fs, 120f, 5f);          // spar ring
                    _bpB.BandPass(_fs, 2600f, 1.5f);       // tearing (swept down)
                    _hp.HighPass(_fs, 1500f, 0.7f);        // crack
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.005f, _fs);
                    break;
                case FxKind.CanopyJettison:
                    _len = 1.3f;
                    _bpA.BandPass(_fs, 2200f, 8f);         // latch ring
                    _bpB.BandPass(_fs, 3400f, 10f);
                    _lp.SetCutoff(2500f, _fs);             // wind rush
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.003f, _fs);
                    break;
                case FxKind.EjectionSeat:
                    _len = 0.55f;
                    _bpA.BandPass(_fs, 300f, 3f);          // cartridge bang
                    _bpB.LowPass(_fs, 120f, 0.7f);         // rumble
                    _hp.HighPass(_fs, 200f, 0.7f);
                    _lp.SetCutoff(600f, _fs);              // rocket (swept)
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.008f, _fs);
                    break;
                case FxKind.ChuteDeploy:
                    _len = 1.1f;
                    _hp.HighPass(_fs, 1500f, 0.7f);        // rip
                    _bpA.BandPass(_fs, 1100f, 1.8f);       // fabric flutter
                    _bpB.BandPass(_fs, 4000f, 2f);         // line hiss
                    _burst = 0f;
                    break;
                case FxKind.ChuteInflate:
                    _len = 0.7f;
                    _hp.HighPass(_fs, 3000f, 0.7f);        // line snap
                    _lp.SetCutoff(220f, _fs);              // whump
                    _bpA.BandPass(_fs, 900f, 2f);          // after-flutter
                    _burst = 0f;
                    break;
            }
            Active = true;
        }

        private void Update()
        {
            float t = T;
            switch (Kind)
            {
                case FxKind.WingFailure:
                    _gAT = Env(t, 0.001f, 0.004f, 0.02f);
                    _gBT = Env(t - 0.02f, 0.01f, 0.05f, 0.3f) * 0.9f;
                    _bpB.BandPass(_fs, 400f + 2200f * (float)Math.Exp(-t / 0.45), 1.5f);
                    _gCT = 4f * (Env(t, 0.001f, 0f, 0.25f) + (t >= 0.22f ? Env(t - 0.22f, 0.001f, 0f, 0.3f) : 0f));
                    if (!_snapped && t >= 0.22f) { _snapped = true; _burst = 1f; }
                    break;
                case FxKind.CanopyJettison:
                    _gAT = 3f * Env(t, 0.001f, 0f, 0.03f);
                    _gBT = 0.3f * Env(t - 0.05f, 0.002f, 0f, 0.12f);
                    _gCT = 0.6f * Env(t - 0.05f, 0.2f, 0.05f, 0.45f) * (0.4f + Dsp.Clamp01(_ias / 80f));
                    break;
                case FxKind.EjectionSeat:
                    _gAT = 3f * Env(t, 0.001f, 0f, 0.08f);
                    _gBT = 0.9f * Env(t, 0.015f, 0.28f, 0.06f);
                    _gCT = 3f * _gBT;
                    _lp.SetCutoff(600f + 4000f * (t < 0.45f ? Dsp.Sin01(t / 0.9f) : 0f), _fs);
                    break;
                case FxKind.ChuteDeploy:
                    _gAT = 0.7f * Env(t, 0.002f, 0.02f, 0.03f);
                    _gBT = 0.5f * Env(t, 0.05f, 0.45f, 0.2f);
                    _gCT = 0.12f * Env(t, 0.05f, 0.3f, 0.25f);
                    _flutHz = Dsp.Clamp(_flutHz + _n.Next() * 3f, 18f, 40f);
                    break;
                case FxKind.ChuteInflate:
                    _gAT = 0.4f * Env(t, 0.001f, 0.004f, 0.006f);
                    _gBT = Env(t, 0.02f, 0f, 0.12f);
                    _gCT = 0.15f * Env(t - 0.05f, 0.02f, 0f, 0.2f);
                    break;
            }
        }

        public float Next()
        {
            if ((_sub++ & 127) == 0) Update();
            T += 1f / _fs;
            _gA += Smooth * (_gAT - _gA);
            _gB += Smooth * (_gBT - _gB);
            _gC += Smooth * (_gCT - _gC);
            float n = _n.Next();
            float outp = 0f;
            switch (Kind)
            {
                case FxKind.WingFailure:
                {
                    if (--_gateCount <= 0) { _gateCount = (int)(_fs * _n.Range(0.006f, 0.02f)); _gateLevel = _n.Range(0.25f, 1f); }
                    _gate += 0.01f * (_gateLevel - _gate);
                    _burst *= _burstDec;
                    outp = _hp.Process(n) * _gA + _bpB.Process(n) * _gB * _gate + _bpA.Process(n * _burst) * _gC;
                    break;
                }
                case FxKind.CanopyJettison:
                {
                    _burst *= _burstDec;
                    float ex = n * _burst;
                    float latch = (_bpA.Process(ex) + 0.7f * _bpB.Process(ex)) * _gA;
                    _phA += 1310f / _fs; _phB += 2090f / _fs; _phC += 3380f / _fs;
                    float clank = (Dsp.Sin01(_phA) + 0.6f * Dsp.Sin01(_phB) + 0.4f * Dsp.Sin01(_phC)) * _gB;
                    outp = latch + clank + _lp.Process(n) * _gC;
                    break;
                }
                case FxKind.EjectionSeat:
                {
                    _burst *= _burstDec;
                    float bang = _bpA.Process(n * _burst) * _gA;
                    float rocket = _hp.Process(_lp.Process(n)) * _gB;
                    float rumble = _bpB.Process(_n.Next()) * _gC;
                    outp = bang + rocket + rumble;
                    break;
                }
                case FxKind.ChuteDeploy:
                {
                    _phA += _flutHz / _fs;
                    if (_phA >= 1f) _phA -= 1f;
                    float am = 0.55f + 0.45f * Dsp.Sin01(_phA);
                    outp = _hp.Process(n) * _gA + _bpA.Process(n) * 2f * _gB * am + _bpB.Process(n) * 2f * _gC;
                    break;
                }
                case FxKind.ChuteInflate:
                {
                    float hz = 45f + 25f * _gB;          // thump pitch falls with the envelope
                    _phA += hz / _fs;
                    if (_phA >= 1f) _phA -= 1f;
                    _phB += 25f / _fs;
                    if (_phB >= 1f) _phB -= 1f;
                    float whump = (_lp.Process(n) * 2.5f + 0.5f * Dsp.Sin01(_phA)) * _gB;
                    float flutter = _bpA.Process(n) * 2f * _gC * (0.6f + 0.4f * Dsp.Sin01(_phB));
                    outp = _hp.Process(n) * _gA + whump + flutter;
                    break;
                }
            }
            if (T >= _len) Active = false;
            if (_phA >= 1f) _phA -= 1f;
            if (_phB >= 1f) _phB -= 1f;
            if (_phC >= 1f) _phC -= 1f;
            return outp;
        }
    }

    /// <summary>
    /// Continuous airframe stress: sparse stick-slip creak bursts through two randomly re-tuned
    /// resonators (rate, pitch and level rise with severity) over a slowly wandering low groan tone.
    /// Severity is smoothed so per-frame calls sound continuous and release cleanly.
    /// </summary>
    internal sealed class GroanVoice
    {
        private readonly float _fs;
        private readonly Noise _n = new Noise(0x6E0A11u);
        private readonly Biquad _resA = new Biquad(), _resB = new Biquad();
        private readonly float _sevA;
        private float _sev, _sevT;
        private int _countdown;
        private float _burst, _burstDec;
        private float _lowPhase, _lowHz = 80f, _tremPhase;

        public GroanVoice(float fs)
        {
            _fs = fs;
            _sevA = Dsp.Tau(0.06f, fs);
            _resA.BandPass(fs, 400f, 14f);
            _resB.BandPass(fs, 700f, 18f);
            _burstDec = Dsp.DecayCoef(0.015f, fs);
        }

        public void Prepare(float severity)
        {
            _sevT = Dsp.Clamp01(severity);
            float target = 70f + 50f * _sevT;
            _lowHz = Dsp.Clamp(_lowHz + _n.Next() * 6f - (_lowHz - target) * 0.1f, 50f, 150f);
        }

        public float Next()
        {
            _sev += _sevA * (_sevT - _sev);
            float sev = _sev;
            if (sev < 0.002f) return 0f;

            if (--_countdown <= 0)
            {
                float rate = 6f + 50f * sev;
                _countdown = (int)(_fs / rate * _n.Range(0.3f, 1.7f));
                if (_countdown < 64) _countdown = 64;
                _burst = _n.Range(0.5f, 1f);
                float hz = _n.Range(180f, 1200f) * (1f + 0.5f * sev);
                _resA.BandPass(_fs, hz, 14f);
                _resB.BandPass(_fs, hz * 1.6f + _n.Range(0f, 200f), 18f);
                _burstDec = Dsp.DecayCoef(_n.Range(0.008f, 0.03f), _fs);
            }
            _burst *= _burstDec;
            float ex = _n.Next() * _burst;
            float creak = (_resA.Process(ex) + 0.6f * _resB.Process(ex)) * 4f;

            _lowPhase += _lowHz / _fs;
            if (_lowPhase >= 1f) _lowPhase -= 1f;
            _tremPhase += 7f / _fs;
            if (_tremPhase >= 1f) _tremPhase -= 1f;
            float tone = Dsp.Sin01(_lowPhase) + 0.5f * Dsp.Sin01(_lowPhase * 2f) + 0.3f * Dsp.Sin01(_lowPhase * 3f) + 0.2f * Dsp.Sin01(_lowPhase * 4f);
            float groan = Dsp.SoftClip(tone * 2f) * (0.7f + 0.3f * Dsp.Sin01(_tremPhase)) * 0.35f * Dsp.Pow(sev, 1.5f);

            return (creak * (0.3f + 0.7f * sev) + groan) * Dsp.SmoothStep(0f, 0.15f, sev);
        }
    }

    /// <summary>Trigger mailbox + voice pool for the one-shots, plus the continuous groan.</summary>
    internal sealed class OneShotSynth
    {
        private readonly int[] _pending = new int[(int)FxKind.Count];
        private readonly FxVoice[] _pool;
        private readonly GroanVoice _groan;

        public OneShotSynth(float fs)
        {
            _pool = new FxVoice[4];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new FxVoice(fs, 0x51A7Eu + (uint)i * 0x9E3779B9u);
            _groan = new GroanVoice(fs);
        }

        /// <summary>Main thread: queue a one-shot (coalesces repeats within a block).</summary>
        public void Trigger(FxKind kind) => Interlocked.Exchange(ref _pending[(int)kind], 1);

        /// <summary>Audio thread, once per block: start queued voices, retarget the groan.</summary>
        public void Prepare(float ias, float groanSeverity)
        {
            for (int k = 0; k < _pending.Length; k++)
            {
                if (Interlocked.Exchange(ref _pending[k], 0) == 0) continue;
                FxVoice v = null;
                for (int i = 0; i < _pool.Length; i++) if (!_pool[i].Active) { v = _pool[i]; break; }
                if (v == null)
                {
                    v = _pool[0];
                    for (int i = 1; i < _pool.Length; i++) if (_pool[i].T > v.T) v = _pool[i];   // steal the oldest
                }
                v.Start((FxKind)k, ias);
            }
            _groan.Prepare(groanSeverity);
        }

        public float Next()
        {
            float s = _groan.Next();
            for (int i = 0; i < _pool.Length; i++) if (_pool[i].Active) s += _pool[i].Next();
            return s;
        }
    }
}
