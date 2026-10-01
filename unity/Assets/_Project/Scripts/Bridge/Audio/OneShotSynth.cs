using System;
using System.Threading;

namespace FlyingGame.Bridge
{
    internal enum FxKind { WingFailure = 0, CanopyJettison, EjectionSeat, ChuteDeploy, ChuteInflate, PylonBurst, BodyThud, Grunt, Wince, Crash, TireChirp, BulletHit, Explosion, Count }

    internal static class FxSource
    {
        /// <summary>Sounds made AT THE AIRCRAFT (attenuated and delayed by distance once the pilot has left it); the
        /// rest happen at the listener (the pilot / seat / canopy).</summary>
        public static bool AtAircraft(FxKind k) => k is FxKind.WingFailure or FxKind.PylonBurst or FxKind.Crash or FxKind.TireChirp;   // BulletHit/Explosion: at the target, mixed at listener level
    }

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
        private float _distGain = 1f;        // distance attenuation fixed at trigger time (aircraft-bound sounds)
        private bool _snapped;
        private int _splinters;              // WingFailure: splinter clicks left after the main crack
        private const float Smooth = 1f / 48f;

        public FxVoice(float fs, uint seed) { _fs = fs; _n = new Noise(seed); }

        private static float Env(float t, float attack, float hold, float decayTau)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            t -= attack + hold;
            return t <= 0f ? 1f : (float)Math.Exp(-t / decayTau);
        }

        public void Start(FxKind kind, float ias) => Start(kind, ias, 1f, 1f);

        public void Start(FxKind kind, float ias, float level, float distGain)
        {
            Kind = kind;
            T = 0f;
            _ias = ias;
            _level = Dsp.Clamp01(level);
            _distGain = distGain;
            _sub = 0;
            _snapped = false;
            _gA = _gB = _gC = 0f;
            _gate = _gateLevel = 1f;
            _gateCount = 0;
            _bpA.Reset(); _bpB.Reset(); _hp.Reset(); _lp.Reset();
            switch (kind)
            {
                case FxKind.WingFailure:
                    // One clean SNAP, like a thick branch breaking (owner 2026-10-01): a 3 ms crack, two or three
                    // splinter clicks inside the first 25 ms, a short woody knock and a dull thock under it. ~0.35 s.
                    _len = 0.35f;
                    _hp.HighPass(_fs, 1800f, 0.7f);        // the crack
                    _bpA.BandPass(_fs, 620f, 2.2f);        // woody body
                    _bpB.BandPass(_fs, 1150f, 3.5f);       // second wood mode
                    _lp.SetCutoff(170f, _fs);              // thock
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.003f, _fs);
                    _gateCount = (int)(_fs * 0.006f); _gateLevel = 0f; _splinters = 3;
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
                    // Owner: "a splat crunch, like a pop can being crushed" — a soft wideband splat, then a dense
                    // crackle of small buckles (thin skin folding), dull body, hardly any ring or boom.
                    _len = 0.9f + 0.7f * _level;
                    _lp.SetCutoff(75f, _fs);               // a little weight under it
                    _bpA.BandPass(_fs, 330f, 1.4f);        // the can body: dull, wide
                    _bpB.BandPass(_fs, 750f, 0.8f);        // the splat (wideband thump at contact)
                    _hp.HighPass(_fs, 2800f, 0.7f);        // crinkle: each buckle is a tiny HF snap
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.035f, _fs);
                    _gateDec = Dsp.DecayCoef(0.004f, _fs);
                    _ringDec = Dsp.DecayCoef(0.10f, _fs);
                    _ring = 0f; _gate = 0f; _gateCount = 0; _crunchRate = 90f;
                    break;
                // ---- tyres meeting the runway: a short, subtle chirp (owner: "not a gong") ----
                case FxKind.TireChirp:
                    _len = 0.16f + 0.12f * _level;
                    _bpA.BandPass(_fs, 1900f, 9f);         // the squeal
                    _bpB.BandPass(_fs, 2700f, 12f);        // its upper partial
                    _lp.SetCutoff(140f, _fs);              // a very small thump under it
                    break;
                case FxKind.BulletHit:
                    _len = 0.22f;
                    _bpA.BandPass(_fs, 2400f, 14f);        // metallic ping
                    _bpB.BandPass(_fs, 640f, 3f);          // skin thump
                    _hp.HighPass(_fs, 3000f, 0.7f);
                    _lp.SetCutoff(200f, _fs);
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.004f, _fs);
                    break;
                case FxKind.Explosion:
                    _len = 2.4f;
                    _lp.SetCutoff(70f, _fs);               // the boom
                    _bpA.BandPass(_fs, 220f, 1.2f);        // fireball body
                    _hp.HighPass(_fs, 1800f, 0.7f);        // debris crackle
                    _bpB.BandPass(_fs, 900f, 2f);
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.06f, _fs);
                    _gateDec = Dsp.DecayCoef(0.006f, _fs); _gate = 0f; _gateCount = 0; _crunchRate = 50f;
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
                    _gAT = _gBT = _gCT = 1f;   // the burst / splinter envelopes shape the snap
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
                    _gAT = (0.8f + 1.4f * _level) * Env(t, 0.002f, 0.02f, 0.10f);                      // splat + weight
                    _gBT = (1.2f + 2.0f * _level) * Env(t, 0.003f, 0.18f + 0.35f * _level, 0.22f);     // crunch / crackle
                    _gCT = (0.5f + 0.9f * _level) * Env(t, 0.003f, 0.12f + 0.25f * _level, 0.18f);     // crinkle
                    _crunchRate = (60f + 110f * _level) * (float)Math.Exp(-t / (0.25f + 0.35f * _level)); // dense, dying fast
                    break;
                case FxKind.TireChirp:
                    _gAT = (0.10f + 0.22f * _level) * Env(t, 0.006f, 0.05f + 0.08f * _level, 0.035f);   // chirp
                    _gBT = (0.15f + 0.35f * _level) * Env(t, 0.002f, 0.01f, 0.03f);                    // thump
                    break;
                case FxKind.BulletHit:
                    _gAT = (0.4f + 0.6f * _level) * Env(t, 0.001f, 0.01f, 0.05f);
                    _gBT = (0.3f + 0.5f * _level) * Env(t, 0.002f, 0.02f, 0.04f);
                    break;
                case FxKind.Explosion:
                    _gAT = 5f * Env(t, 0.003f, 0.08f, 0.45f);                                   // boom
                    _gBT = 2.5f * Env(t, 0.01f, 0.4f, 0.6f);                                    // fireball roar
                    _gCT = 1.2f * Env(t - 0.05f, 0.02f, 0.5f, 0.7f);                            // crackle
                    _crunchRate = 50f * (float)Math.Exp(-t / 0.9);
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
                    if (_splinters > 0 && --_gateCount <= 0)
                    {
                        _gateLevel = _n.Range(0.25f, 0.55f);
                        _gateCount = (int)(_fs * _n.Range(0.004f, 0.011f));
                        _splinters--;
                    }
                    _gateLevel *= 1f - 1f / (0.002f * _fs);
                    float ex = n * (_burst + _gateLevel);
                    float crack = _hp.Process(ex) * 2.6f;
                    float wood = _bpA.Process(ex) * 5f + _bpB.Process(ex) * 3f;
                    float thock = _lp.Process(n * _burst) * 5f;
                    outp = Dsp.SoftClip((crack + wood + thock) * 1.3f);
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
                    _phA += 48f / _fs;                                   // a little low weight
                    _phB += 290f / _fs;                                  // short dull body tone (aluminium can, not a bell)
                    float splat = (_bpB.Process(n * _burst) * 4f + _lp.Process(n) * 1.5f + 0.3f * Dsp.Sin01(_phA) * _burst) * _gA;
                    float crunch = _bpA.Process(n * _gate * 12f) * _gB;                        // each buckle thumps the body
                    float crinkle = _hp.Process(n * _gate * 6f) * _gC;                         // ... and snaps
                    float body = Dsp.Sin01(_phB) * _ring * 0.15f * _gB;
                    outp = splat + crunch + crinkle + body;
                    break;
                }
                case FxKind.TireChirp:
                {
                    // Squeal: a pitched tone sliding up as the tyre spins up, lightly noisy, plus a tiny thump.
                    float hz = 1500f + 900f * Dsp.Clamp01(T / 0.12f);
                    _phA += hz / _fs;
                    float tone = Dsp.Sin01(_phA) * 0.6f + _bpA.Process(n) * 1.5f + _bpB.Process(n) * 0.8f;
                    outp = tone * _gA + _lp.Process(n) * 2f * _gB;
                    break;
                }
                case FxKind.BulletHit:
                {
                    _burst *= _burstDec;
                    outp = _bpA.Process(n * _burst * 6f) * _gA + _bpB.Process(n * _burst * 3f) * _gB + _hp.Process(n * _burst) * _gA * 0.5f;
                    break;
                }
                case FxKind.Explosion:
                {
                    if (--_gateCount <= 0) { float rate = _crunchRate < 3f ? 3f : _crunchRate; _gateCount = (int)(_fs / rate * (0.4f + 1.2f * (_n.Next() * 0.5f + 0.5f))); _gate = 0.5f + 0.5f * (_n.Next() * 0.5f + 0.5f); }
                    _gate *= _gateDec; _burst *= _burstDec;
                    _phA += 38f / _fs;
                    float boom = (_lp.Process(n) * 3f + 0.8f * Dsp.Sin01(_phA) * _burst) * _gA;
                    float roar = (_bpA.Process(n) * 2f + _bpB.Process(n) * 0.8f) * _gB;
                    float crackle = _hp.Process(n * _gate * 8f) * _gC;
                    outp = boom + roar + crackle;
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
            return outp * _distGain;
        }
    }

    /// <summary>
    /// Airframe stress tone (owner 2026-10-01): not a realistic creak — a plain buzzy HUM the pilot reads as stress
    /// building. Silent inside the limit load; from limit (severity 0) to ultimate (severity 1) it rises from a
    /// quiet-but-clear hum to loud, and its pitch climbs a little (95 → 150 Hz) so the build-up is unmistakable.
    /// Five harmonics at 1/k (a soft sawtooth) through a 1.6 kHz low-pass. Severity is smoothed over ~30 ms.
    /// </summary>
    internal sealed class StressHum
    {
        private readonly float _fs, _sevA;
        private readonly OnePole _lp = new OnePole();
        private float _sev, _sevT, _phase;

        public StressHum(float fs)
        {
            _fs = fs;
            _sevA = Dsp.Tau(0.03f, fs);
            _lp.SetCutoff(1600f, fs);
        }

        public void Prepare(float severity) { _sevT = Dsp.Clamp01(severity); }

        public float Next()
        {
            float target = _sevT;
            _sev += _sevA * (target - _sev);
            float sev = _sev;
            // Fully quiet only when back inside the limit; any overstress is audible at once.
            float gain = target <= 0f && sev < 0.002f ? 0f : (sev <= 0f ? 0f : 0.07f + 0.5f * sev);
            if (gain <= 0f) return 0f;
            _phase += (95f + 55f * sev) / _fs;
            if (_phase >= 1f) _phase -= 1f;
            float w = 0f;
            for (int k = 1; k <= 5; k++) w += Dsp.Sin01(_phase * k) / k;
            return _lp.Process(w * 0.6f) * gain;
        }
    }

    /// <summary>Trigger mailbox + voice pool for the one-shots, plus the continuous stress hum.</summary>
    internal sealed class OneShotSynth
    {
        private readonly int[] _pending = new int[(int)FxKind.Count];
        private readonly float[] _pendingLevel = new float[(int)FxKind.Count];
        private readonly FxVoice[] _pool;
        private readonly StressHum _groan;

        public OneShotSynth(float fs)
        {
            _pool = new FxVoice[4];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new FxVoice(fs, 0x51A7Eu + (uint)i * 0x9E3779B9u);
            _groan = new StressHum(fs);
        }

        /// <summary>Main thread: queue a one-shot (coalesces repeats within a block).</summary>
        public void Trigger(FxKind kind) => Trigger(kind, 1f);

        /// <summary>Main thread: queue a one-shot with a 0..1 severity (pilot thud weight / grunt effort).</summary>
        public void Trigger(FxKind kind, float level)
        {
            Interlocked.Exchange(ref _pendingLevel[(int)kind], level);
            Interlocked.Exchange(ref _pending[(int)kind], 1);
        }

        private float _aircraftGain = 1f;

        /// <summary>Audio thread, once per block: start queued voices, retarget the stress hum. <paramref name="aircraftGain"/>
        /// = distance attenuation of everything that happens at the aircraft (1 while the pilot is aboard).</summary>
        public void Prepare(float ias, float groanSeverity, float aircraftGain = 1f)
        {
            _aircraftGain = aircraftGain;
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
                v.Start((FxKind)k, ias, _pendingLevel[k], FxSource.AtAircraft((FxKind)k) ? aircraftGain : 1f);
            }
            _groan.Prepare(groanSeverity);
        }

        public float Next()
        {
            float s = _groan.Next() * _aircraftGain;
            for (int i = 0; i < _pool.Length; i++) if (_pool[i].Active) s += _pool[i].Next();
            return s;
        }
    }
}
