using System;
using System.Threading;

namespace FlyingGame.Bridge
{
    internal enum FxKind { WingFailure = 0, CanopyJettison, EjectionSeat, ChuteDeploy, ChuteInflate, PylonBurst, BodyThud, Grunt, Wince, Crash, Count }

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
        private float _level = 1f;           // 0..1 severity for the pilot sounds (thud weight, grunt effort) / crash weight
        private float _ring, _ringDec, _gateDec, _crunchRate;   // Crash: metallic ring envelope, crunch-event rate
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

        public void Start(FxKind kind, float ias) => Start(kind, ias, 1f);

        public void Start(FxKind kind, float ias, float level)
        {
            Kind = kind;
            T = 0f;
            _ias = ias;
            _level = Dsp.Clamp01(level);
            _sub = 0;
            _snapped = false;
            _gA = _gB = _gC = 0f;
            _gate = _gateLevel = 1f;
            _gateCount = 0;
            _bpA.Reset(); _bpB.Reset(); _hp.Reset(); _lp.Reset();
            switch (kind)
            {
                case FxKind.WingFailure:
                    _len = 3.2f;
                    _lp.SetCutoff(60f, _fs);               // the boom
                    _bpA.BandPass(_fs, 63f, 14f);          // girder ring
                    _bpB.BandPass(_fs, 900f, 8f);          // snap resonator (retuned per snap)
                    _hp.HighPass(_fs, 1200f, 0.7f);        // crack
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.02f, _fs);
                    _gateCount = (int)(_fs * 0.06f); _gateLevel = 0f;
                    _phA = _phB = _phC = 0f;
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
                case FxKind.PylonBurst:
                    _len = 2.2f;
                    _bpA.BandPass(_fs, 90f, 2f);           // the fabric slap / boom
                    _bpB.BandPass(_fs, 1600f, 1.2f);       // tearing seam
                    _hp.HighPass(_fs, 700f, 0.7f);         // compressed-air blast (swept down as it empties)
                    _lp.SetCutoff(3500f, _fs);
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.01f, _fs);
                    break;
                // ---- the pilot hitting the ground: body thud, grunt ("oof"), wince (hiss through the teeth + "ahh") ----
                case FxKind.BodyThud:
                    _len = 0.4f;
                    _lp.SetCutoff(110f, _fs);              // the thump
                    _hp.HighPass(_fs, 900f, 0.7f);         // dry crunch of kit and gravel
                    _bpA.BandPass(_fs, 55f, 3f);           // ground boom
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.03f, _fs);
                    break;
                case FxKind.Grunt:
                    _len = 0.5f;
                    _bpA.BandPass(_fs, 520f, 6f);          // formant F1 ("uh")
                    _bpB.BandPass(_fs, 1050f, 8f);         // formant F2
                    _hp.HighPass(_fs, 2200f, 0.7f);        // breath
                    _lp.SetCutoff(3000f, _fs);
                    break;
                // ---- the airframe hitting the ground / a building: crunching, tearing, ringing metal (owner: "not popcorn") ----
                case FxKind.Crash:
                    _len = 1.6f + 1.0f * _level;
                    _lp.SetCutoff(90f, _fs);               // the impact boom
                    _bpA.BandPass(_fs, 460f, 2.5f);        // sheet metal buckling (each crunch event rings this)
                    _bpB.BandPass(_fs, 1500f, 5f);         // clang / stressed skin partial
                    _hp.HighPass(_fs, 2600f, 0.8f);        // tearing and scraping
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.05f, _fs);
                    _gateDec = Dsp.DecayCoef(0.009f, _fs);
                    _ringDec = Dsp.DecayCoef(0.22f, _fs);
                    _ring = 0f; _gate = 0f; _gateCount = 0; _crunchRate = 40f;
                    break;
                case FxKind.Wince:
                    _len = 0.85f;
                    _bpA.BandPass(_fs, 720f, 6f);          // formant F1 ("ah")
                    _bpB.BandPass(_fs, 1350f, 8f);         // formant F2
                    _hp.HighPass(_fs, 3800f, 0.8f);        // the hiss through clenched teeth
                    _lp.SetCutoff(6500f, _fs);
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
                    // Steel bridge letting go: boom, a cascade of metallic snaps over the first second, the girders
                    // ringing out for two seconds beneath.
                    _gAT = 3.5f * Env(t, 0.002f, 0.05f, 0.5f);
                    _gBT = 1.3f * Env(t - 0.05f, 0.01f, 0.9f, 0.5f);
                    _gCT = 0.9f * Env(t, 0.005f, 0.4f, 2.2f);
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
                case FxKind.PylonBurst:
                    _gAT = 4f * Env(t, 0.002f, 0.03f, 0.25f);                 // boom
                    _gBT = 1.2f * Env(t, 0.003f, 0.02f, 0.15f);               // rip
                    _gCT = 1.5f * Env(t - 0.02f, 0.03f, 0.5f, 1.2f);          // air rushing out, long tail
                    _lp.SetCutoff(3500f * (float)Math.Exp(-t / 0.9) + 300f, _fs);
                    break;
                case FxKind.BodyThud:
                    _gAT = (1.5f + 3.5f * _level) * Env(t, 0.002f, 0.02f, 0.09f);        // thump
                    _gBT = (0.3f + 1.2f * _level) * Env(t, 0.001f, 0.01f, 0.05f);        // crunch
                    _gCT = 2.5f * _level * Env(t, 0.004f, 0.03f, 0.16f);                 // boom (heavy hits only)
                    break;
                case FxKind.Grunt:
                    // "Oof/ugh": voiced pulse train whose pitch drops through the grunt, a puff of breath on top.
                    _gAT = (0.9f + 0.6f * _level) * Env(t, 0.015f, 0.10f + 0.12f * _level, 0.07f);
                    _gBT = 0.25f * Env(t, 0.005f, 0.04f, 0.06f);
                    break;
                case FxKind.Crash:
                    _gAT = (2.0f + 4.0f * _level) * Env(t, 0.002f, 0.04f, 0.24f);                     // boom
                    _gBT = (1.0f + 1.8f * _level) * Env(t, 0.004f, 0.30f + 0.45f * _level, 0.35f);     // crunch
                    _gCT = (0.35f + 0.9f * _level) * Env(t - 0.04f, 0.02f, 0.25f + 0.35f * _level, 0.45f);   // tear / scrape
                    _crunchRate = (22f + 50f * _level) * (float)Math.Exp(-t / (0.5f + 0.6f * _level)); // events/s, thinning out
                    _flutHz = Dsp.Clamp(_flutHz + _n.Next() * 4f, 18f, 45f);
                    break;
                case FxKind.Wince:
                    // Sharp inhale through the teeth (hiss) then a pained "ahh" with a little shake in it.
                    _gAT = (0.35f + 0.35f * _level) * Env(t, 0.03f, 0.16f, 0.06f);       // hiss
                    _gBT = (0.7f + 0.5f * _level) * Env(t - 0.24f, 0.03f, 0.22f + 0.2f * _level, 0.12f);   // "ahh"
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
                    _burst *= _burstDec;
                    float boom = _lp.Process(n) * 3f * _gA + _bpA.Process(n * _burst) * 4f * _gA;
                    // Cascade of snaps: random intervals, each retunes the resonator and fires a bright crack.
                    if (--_gateCount <= 0) { _gateCount = (int)(_fs * _n.Range(0.04f, 0.22f)); _gateLevel = 1f; _bpB.BandPass(_fs, _n.Range(350f, 1800f), 8f); }
                    _gateLevel *= 1f - 1f / (0.03f * _fs);
                    float snap = (_bpB.Process(n * _gateLevel) * 6f + _hp.Process(n * _gateLevel) * 2f) * _gB;
                    _phA += 41f / _fs; if (_phA >= 1f) _phA -= 1f;
                    _phB += 66f / _fs; if (_phB >= 1f) _phB -= 1f;
                    _phC += 103f / _fs; if (_phC >= 1f) _phC -= 1f;
                    float ring = (Dsp.Sin01(_phA) + 0.7f * Dsp.Sin01(_phB) + 0.4f * Dsp.Sin01(_phC)) * _gC;
                    outp = Dsp.SoftClip(boom + snap + ring);
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
                case FxKind.BodyThud:
                {
                    _burst *= _burstDec;
                    float hz = 38f + 40f * _gA / (1.5f + 3.5f * _level + 1e-3f);        // pitch sinks with the thump
                    _phA += hz / _fs;
                    float thump = (_lp.Process(n) * 3f + 0.6f * Dsp.Sin01(_phA)) * _gA;
                    float crunch = _hp.Process(n * _burst) * _gB;
                    float boom = _bpA.Process(_n.Next()) * _gC;
                    outp = thump + crunch + boom;
                    break;
                }
                case FxKind.Grunt:
                {
                    float f0 = (150f + 40f * _level) * (float)Math.Exp(-T / 0.35) + 75f;   // ~190 → 90 Hz
                    _phA += f0 / _fs;
                    float glottal = _phA < 0.4f ? (float)Math.Sin(_phA / 0.4f * Math.PI) : 0f;   // one puff per period
                    float voice = (_bpA.Process(glottal) * 1.6f + _bpB.Process(glottal) * 0.9f) * _gA;
                    float breath = _lp.Process(_hp.Process(n)) * _gB;
                    outp = voice + breath;
                    break;
                }
                case FxKind.Crash:
                {
                    // Crunch events: an irregular train of buckling snaps, each ringing the 460 Hz panel mode and
                    // kicking the metallic ring; a boom under it and tearing noise on top.
                    if (--_gateCount <= 0)
                    {
                        float rate = _crunchRate < 3f ? 3f : _crunchRate;
                        _gateCount = (int)(_fs / rate * (0.4f + 1.2f * (_n.Next() * 0.5f + 0.5f)));
                        _gate = 0.5f + 0.5f * (_n.Next() * 0.5f + 0.5f);
                        _ring += _gate * 0.45f;
                    }
                    _gate *= _gateDec;
                    _ring *= _ringDec;
                    _burst *= _burstDec;
                    _phA += 42f / _fs;                                   // boom body
                    _phB += 385f / _fs;                                  // ring partials (inharmonic pair)
                    _phC += 1130f / _fs;
                    float boom = (_lp.Process(n) * 3f + 0.6f * Dsp.Sin01(_phA)) * _gA;
                    float crunch = _bpA.Process(n * _gate * 10f) * _gB;
                    float clang = _bpB.Process(n * _burst * 3f + n * _gate * 2f) * _gB * 0.6f;
                    float ring = (Dsp.Sin01(_phB) * 0.7f + Dsp.Sin01(_phC) * 0.3f) * _ring * 0.35f * _gB;
                    float am = 0.55f + 0.45f * Dsp.Sin01(_flutHz * T);
                    float tear = _hp.Process(n) * _gC * am;
                    outp = boom + crunch + clang + ring + tear;
                    break;
                }
                case FxKind.Wince:
                {
                    float shake = 1f + 0.06f * Dsp.Sin01(_phB);                         // pained tremor ~9 Hz
                    _phB += 9f / _fs;
                    float f0 = (215f - 45f * Dsp.Clamp01((T - 0.24f) / 0.5f)) * shake;   // "ahh" sliding down
                    _phA += f0 / _fs;
                    float glottal = _phA < 0.45f ? (float)Math.Sin(_phA / 0.45f * Math.PI) : 0f;
                    float ah = (_bpA.Process(glottal) * 1.5f + _bpB.Process(glottal) * 1.0f) * _gB;
                    float hiss = _lp.Process(_hp.Process(n)) * _gA;
                    outp = ah + hiss;
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
    /// Continuous airframe stress, voiced like a STEEL BRIDGE under load (owner): three inharmonic girder modes
    /// (55 / 82 / 137 Hz — the 1 : 1.5 : 2.5 partials of a struck steel member) rung by slow stick-slip events and
    /// left to ring for over a second so they beat against each other, a slow sub-bass moan underneath, and
    /// sparse, long, metallic creaks (gliding friction pulse trains through high-Q resonators) on top. Event
    /// rate, ring level and creak pitch all rise with severity; severity is smoothed so per-frame calls flow.
    /// </summary>
    internal sealed class GroanVoice
    {
        private readonly float _fs;
        private readonly Noise _n = new Noise(0x6E0A11u);
        private readonly Biquad _resA = new Biquad(), _resB = new Biquad(), _bodyLp = new Biquad();
        private readonly float _sevA;
        private float _sev, _sevT;
        // girder modes
        private float _pA, _pB, _pC, _eA, _eB, _eC, _ringDecay; private int _ringCount;
        // sub moan
        private float _moanPhase, _moanHz = 32f, _moanHzT = 32f, _moanGlide;
        // creak event
        private int _countdown;
        private float _evLen, _evT, _evAmp, _pulseHz0, _pulseHz1, _pulsePhase, _pulseEnv, _pulseDec;

        public GroanVoice(float fs)
        {
            _fs = fs;
            _sevA = Dsp.Tau(0.08f, fs);
            _resA.BandPass(fs, 1100f, 30f);
            _resB.BandPass(fs, 1700f, 34f);
            _bodyLp.LowPass(fs, 2600f, 0.7f);
            _pulseDec = Dsp.DecayCoef(0.003f, fs);
            _ringDecay = 1f - 1f / (1.3f * fs);        // girders ring ~1.3 s
            _moanGlide = Dsp.Tau(1.2f, fs);
            _evLen = 0f; _evT = 1f;
        }

        public void Prepare(float severity) { _sevT = Dsp.Clamp01(severity); }

        public float Next()
        {
            _sev += _sevA * (_sevT - _sev);
            float sev = _sev;
            if (sev < 0.002f) return 0f;

            // ---- girder modes: stick-slip strikes ring three inharmonic partials
            if (--_ringCount <= 0)
            {
                float rate = 0.6f + 2.4f * sev;
                _ringCount = (int)(_fs / rate * _n.Range(0.5f, 1.5f));
                float hit = _n.Range(0.4f, 1f) * (0.4f + 0.6f * sev);
                _eA += hit; _eB += hit * _n.Range(0.4f, 0.9f); _eC += hit * _n.Range(0.2f, 0.6f);
                if (_eA > 1.6f) _eA = 1.6f; if (_eB > 1.2f) _eB = 1.2f; if (_eC > 0.8f) _eC = 0.8f;
            }
            _eA *= _ringDecay; _eB *= _ringDecay; _eC *= _ringDecay * 0.99995f;
            _pA += 55f / _fs; if (_pA >= 1f) _pA -= 1f;
            _pB += 82.5f / _fs; if (_pB >= 1f) _pB -= 1f;       // 1.5× → slow beating against A's harmonics
            _pC += 137f / _fs; if (_pC >= 1f) _pC -= 1f;
            float girder = (Dsp.Sin01(_pA) * _eA + Dsp.Sin01(_pB) * _eB + Dsp.Sin01(_pC) * _eC) * 0.32f;

            // ---- sub moan: a slow bending 28–38 Hz tone, level with severity
            if (_n.Next() * 0.5f + 0.5f < 0.5f / _fs) _moanHzT = _n.Range(28f, 38f);
            _moanHz += _moanGlide * (_moanHzT - _moanHz);
            _moanPhase += _moanHz / _fs; if (_moanPhase >= 1f) _moanPhase -= 1f;
            float moan = Dsp.Sin01(_moanPhase) * 0.22f * Dsp.Pow(sev, 1.5f);

            // ---- creaks: sparse, long, metallic
            if (_evT >= _evLen && --_countdown <= 0)
            {
                float rate = 0.5f + 2.0f * sev;
                _countdown = (int)(_fs / rate * _n.Range(0.4f, 1.6f));
                _evLen = _n.Range(0.2f, 0.7f); _evT = 0f; _evAmp = _n.Range(0.5f, 1f);
                bool rising = _n.Next() > 0f;
                float f0 = _n.Range(18f, 50f) * (1f + 0.6f * sev), f1 = f0 * _n.Range(1.5f, 2.6f);
                _pulseHz0 = rising ? f0 : f1; _pulseHz1 = rising ? f1 : f0;
                float hz = _n.Range(700f, 2200f) * (1f + 0.3f * sev);
                _resA.BandPass(_fs, hz, 30f); _resB.BandPass(_fs, hz * _n.Range(1.3f, 2.0f), 34f);
            }
            float creak = 0f;
            if (_evT < _evLen)
            {
                float u = _evT / _evLen;
                float env = Dsp.SmoothStep(0f, 0.15f, u) * (1f - Dsp.SmoothStep(0.5f, 1f, u));
                float pulseHz = _pulseHz0 + (_pulseHz1 - _pulseHz0) * u;
                _pulsePhase += pulseHz / _fs;
                if (_pulsePhase >= 1f) { _pulsePhase -= 1f; _pulseEnv = 1f; }
                _pulseEnv *= _pulseDec;
                float ex = (_pulseEnv * 0.8f + _n.Next() * 0.05f * _pulseEnv) * env * _evAmp;
                creak = Dsp.SoftClip((_resA.Process(ex) + 0.7f * _resB.Process(ex)) * 7f) * (0.3f + 0.6f * sev);
                _evT += 1f / _fs;
            }

            float mix = _bodyLp.Process(Dsp.SoftClip(girder * 1.6f + moan) + creak);
            return mix * Dsp.SmoothStep(0f, 0.12f, sev);
        }
    }

    /// <summary>Trigger mailbox + voice pool for the one-shots, plus the continuous groan.</summary>
    internal sealed class OneShotSynth
    {
        private readonly int[] _pending = new int[(int)FxKind.Count];
        private readonly float[] _pendingLevel = new float[(int)FxKind.Count];
        private readonly FxVoice[] _pool;
        private readonly GroanVoice _groan;

        public OneShotSynth(float fs)
        {
            _pool = new FxVoice[4];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new FxVoice(fs, 0x51A7Eu + (uint)i * 0x9E3779B9u);
            _groan = new GroanVoice(fs);
        }

        /// <summary>Main thread: queue a one-shot (coalesces repeats within a block).</summary>
        public void Trigger(FxKind kind) => Trigger(kind, 1f);

        /// <summary>Main thread: queue a one-shot with a 0..1 severity (pilot thud weight / grunt effort).</summary>
        public void Trigger(FxKind kind, float level)
        {
            Interlocked.Exchange(ref _pendingLevel[(int)kind], level);
            Interlocked.Exchange(ref _pending[(int)kind], 1);
        }

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
                v.Start((FxKind)k, ias, _pendingLevel[k]);
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
