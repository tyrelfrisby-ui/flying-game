using System;
using System.Threading;

namespace FlyingGame.Bridge
{
    // CanopyImpact is appended rather than filed next to CanopyJettison on purpose: FxKind's values are indices into
    // OneShotSynth's pending arrays and are persisted nowhere, but inserting one would silently renumber every kind
    // after it, and this enum is read by the Editor FX checks by value.
    internal enum FxKind { WingFailure = 0, CanopyJettison, EjectionSeat, ChuteDeploy, ChuteInflate, PylonBurst, BodyThud, Grunt, Wince, Crash, TireChirp, BulletHit, Explosion, StructurePop, CanopyImpact, Count }

    internal static class FxSource
    {
        /// <summary>Sounds made AT THE AIRCRAFT (attenuated and delayed by distance once the pilot has left it); the
        /// rest happen at the listener (the pilot / seat / canopy).</summary>
        public static bool AtAircraft(FxKind k) => k is FxKind.WingFailure or FxKind.PylonBurst or FxKind.Crash or FxKind.TireChirp or FxKind.StructurePop or FxKind.CanopyImpact;   // BulletHit/Explosion: at the target, mixed at listener level
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
        private readonly CrashSound _land;
        private readonly Biquad _bpA = new Biquad(), _bpB = new Biquad(), _hp = new Biquad();
        private readonly OnePole _lp = new OnePole();
        private float _phA, _phB, _phC;
        private float _burst, _burstDec;
        private float _gA, _gAT, _gB, _gBT, _gC, _gCT;
        private float _gate = 1f, _gateLevel = 1f;
        private int _gateCount, _sub;
        private float _flutHz = 25f, _len, _ias;
        private float _level = 1f;           // 0..1 severity for the pilot sounds (thud weight, grunt effort) / crash weight
        private float _gateDec, _crunchRate;   // Explosion grain envelope/rate
        private float _distGain = 1f;        // distance attenuation fixed at trigger time (aircraft-bound sounds)
        private bool _snapped;
        private int _splinters;              // WingFailure: splinter clicks left after the main crack
        private const float Smooth = 1f / 48f;

        public FxVoice(float fs, uint seed) { _fs = fs; _n = new Noise(seed); _land = new CrashSound(fs, seed); }

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
                case FxKind.StructurePop:
                    // One rivet or spar fitting letting go under load (owner 2026-10-31: "pop and groan"). Very
                    // short and METALLIC, nothing woody about it: a 1 ms tick, then a thin ring that dies in about
                    // a tenth of a second. The ring pitch rises with severity so the airframe sounds more frantic
                    // the harder it is being pulled, and the whole thing has to cut through the groan under it.
                    _len = 0.13f;
                    _hp.HighPass(_fs, 3400f, 0.7f);                        // the tick
                    _bpA.BandPass(_fs, 1500f + 900f * _level, 14f);        // the ring: high Q, so it sings
                    _bpB.BandPass(_fs, 2600f + 1400f * _level, 9f);        // a second mode, slightly detuned
                    _lp.SetCutoff(420f, _fs);                              // a touch of body so it is not just fizz
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.001f, _fs);
                    break;
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
                    // AIR ONLY. Ty 2026-11-10: "ditch the mechanical sound and just use an air sound as it flys off".
                    // The latch ring at 2,200 and 3,400 Hz and the three clank partials under it are gone: what the
                    // pilot hears is the hole the canopy left, a broad rush that opens as the shell goes and fades as
                    // it falls behind. The mechanical sound is now FxKind.CanopyImpact and only plays if it hits
                    // something.
                    _len = 1.6f;
                    _hp.HighPass(_fs, 220f, 0.7f);         // the rush, kept off the bottom end
                    _bpA.BandPass(_fs, 700f, 0.9f);        // the cockpit's own cavity, broad
                    _lp.SetCutoff(1800f, _fs);             // slipstream over the sill
                    break;
                case FxKind.CanopyImpact:
                    // IT HIT THE TAIL. Ty 2026-11-10: "if it hits the tail or other structure use a mechanical sound
                    // for impact and shatter the glass both audibly and visually". So: one struck-metal bang off the
                    // aeroplane's own structure, and the glass going with it — a bright crackle, then shards falling
                    // away for the best part of a second. The level carries how hard it hit (FlightAudio.CanopyImpact).
                    _len = 1.1f;
                    _bpA.BandPass(_fs, 420f, 3.5f);        // the struck panel: a dull aluminium bang
                    _bpB.BandPass(_fs, 2600f, 1.6f);       // the glass letting go, broad and bright
                    _hp.HighPass(_fs, 4500f, 0.7f);        // shards
                    _lp.SetCutoff(190f, _fs);              // the thump through the airframe
                    _burst = 1f; _burstDec = Dsp.DecayCoef(0.0025f, _fs);
                    _gateCount = (int)(_fs * 0.004f); _gateLevel = 0f; _splinters = 4;   // the first few shards, as the snap's splinters
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
                    _land.Start(false, _level, 800f, 10f);
                    _len = 2f; // actual lifetime comes from the shared land synth
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
                case FxKind.StructurePop:
                case FxKind.WingFailure:
                    _gAT = _gBT = _gCT = 1f;   // the burst / splinter envelopes shape the snap
                    break;
                case FxKind.CanopyJettison:
                    // It OPENS rather than cracks: 60 ms of swell as the shell lifts, then the rush settling back. All
                    // three parts scale with airspeed, because at a taxi there is barely any noise to make.
                    float air = 0.35f + 1.1f * Dsp.Clamp01(_ias / 80f);
                    _gAT = 0.9f * air * Env(t, 0.06f, 0.08f, 0.45f);
                    _gBT = 0.7f * air * Env(t, 0.09f, 0.05f, 0.5f);
                    _gCT = 1.1f * air * Env(t - 0.02f, 0.12f, 0.1f, 0.55f);
                    break;
                case FxKind.CanopyImpact:
                    _gAT = 2.2f * Env(t, 0.001f, 0f, 0.05f);                 // the bang
                    _gBT = 1.6f * Env(t, 0.002f, 0.01f, 0.18f);              // the glass going
                    _gCT = 0.8f * Env(t, 0.004f, 0.03f, 0.40f);              // shards away
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
                    break; // shared CrashSound owns its envelope
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
                case FxKind.StructurePop:
                {
                    _burst *= _burstDec;
                    float exc = n * _burst;
                    float tick = _hp.Process(exc) * 2.2f;
                    float ring = _bpA.Process(exc) * 9f + _bpB.Process(exc) * 5f;
                    float body = _lp.Process(exc) * 2f;
                    outp = Dsp.SoftClip((tick + ring + body) * (0.5f + 0.7f * _level));
                    break;
                }
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
                    // Noise through three shapes and nothing tonal: no burst, no resonator ring, no partials.
                    float rush = _hp.Process(n) * _gA;
                    float cavity = _bpA.Process(n) * 2.2f * _gB;
                    float over = _lp.Process(n) * 2.4f * _gC;
                    outp = Dsp.SoftClip(rush + cavity + over);
                    break;
                }
                case FxKind.CanopyImpact:
                {
                    _burst *= _burstDec;
                    if (_gateCount > 0 && --_gateCount == 0 && _splinters > 0)
                    {
                        _splinters--; _gateLevel = 0.7f;
                        _gateCount = (int)(_fs * (0.004f + 0.006f * Dsp.Clamp01(_n.Next() * 0.5f + 0.5f)));
                    }
                    _gateLevel *= 1f - 1f / (0.003f * _fs);
                    float ex = n * (_burst + _gateLevel);
                    float bang = _bpA.Process(ex) * 5f * _gA;
                    float breaking = _bpB.Process(ex) * 3f * _gB;
                    float shards = _hp.Process(n * (_burst + _gateLevel)) * 2.4f * _gC;
                    float thump = _lp.Process(n * _burst) * 4f * _gA;
                    outp = Dsp.SoftClip(bang + breaking + shards + thump);
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
                    outp = _land.Next();
                    if (!_land.Active) Active = false;
                    break;
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

    /// <summary>
    /// FLUTTER, AS THE PILOT HEARS IT — and it is the ONLY warning he gets before the aeroplane comes apart, so it
    /// is not garnish. Ty's description is the specification: "high frequency low amplitude flutter ... increases in
    /// amplitude and decreases in frequency until catastrophic failure occurs", from
    /// <see cref="FlyingGame.Core.Aero.Flutter.OnsetHz"/> 35 Hz down to <see cref="FlyingGame.Core.Aero.Flutter.DivergentHz"/> 8.
    ///
    /// IT IS A PULSE TRAIN, NOT A TONE, and that choice is what makes the sweep legible. Eight to thirty-five hertz
    /// is below the bottom of pitch hearing: rendered as a sine the whole sweep would be a sub-rumble the phone's
    /// speaker cannot reproduce and a pilot could not read. Rendered as a surface slamming against its stops that
    /// many times a second, it is a hard rasp at 35 Hz — heard through its harmonics, which is how a real buzz at
    /// that rate reaches the ear — that coarsens into separate HAMMER BLOWS as the rate falls. So the warning gets
    /// slower and bigger at the same time, which is exactly the thing to be frightened of, and the pilot can tell
    /// how far gone it is by counting the bangs.
    ///
    /// The body resonance falls with it (a coalescing mode takes the structure with it) and each blow lasts longer,
    /// so the timbre goes from buzz to boom without a second parameter. Nothing here is random: one pulse per
    /// cycle, deterministically, with the noise only colouring the strike.
    /// </summary>
    internal sealed class FlutterBuzz
    {
        private readonly float _fs;
        private readonly Noise _n = new Noise(0xF1A77E2u);
        private readonly Biquad _body = new Biquad();
        private float _hz, _hzT, _amp, _ampT, _phase, _env, _dec;
        private float _bodyHz = -1f;
        private readonly float _smooth;

        public FlutterBuzz(float fs)
        {
            _fs = fs;
            _smooth = Dsp.Tau(0.05f, fs);     // the sweep is slow; 50 ms keeps a block boundary from clicking
        }

        /// <summary>Audio thread, once per block. <paramref name="hz"/> is the flutter's own frequency
        /// (Aircraft.FlutterHz, 0 when nothing is fluttering) and <paramref name="g"/> the shake at the pilot
        /// (Aircraft.FlutterG), which is the amplitude.</summary>
        public void Prepare(float hz, float g)
        {
            _hzT = hz > 0f ? Dsp.Clamp(hz, 3f, 60f) : 0f;
            _ampT = hz > 0f ? Dsp.Clamp01(g / 1.5f) : 0f;   // Flutter.AmplitudeG tops out at 1.5 g
        }

        public float Next()
        {
            _hz += _smooth * (_hzT - _hz);
            _amp += _smooth * (_ampT - _amp);
            if (_hzT <= 0f && _amp < 0.001f) { _env = 0f; return 0f; }
            if (_hz <= 0.5f) return 0f;

            _phase += _hz / _fs;
            if (_phase >= 1f)
            {
                _phase -= 1f;
                _env = 1f;
                // The strike's decay is a fixed FRACTION of the period, so a slower flutter rings longer and the
                // blows stay separate instead of smearing into each other as the rate falls.
                _dec = Dsp.DecayCoef(0.22f / _hz, _fs);
                // The body note falls with the flutter: 420 Hz at onset down to 120 as it diverges.
                float bh = 120f + 300f * Dsp.Clamp01((_hz - 8f) / 27f);
                if (_bodyHz < 0f || Math.Abs(bh - _bodyHz) > 2f) { _body.BandPass(_fs, bh, 2.2f); _bodyHz = bh; }
            }
            if (_env <= 1e-5f) return 0f;
            _env *= _dec;
            // The strike itself: a noise crack through the body resonance, plus a little of the raw crack so the
            // high end does not vanish on a phone speaker.
            float x = _n.Next() * _env;
            float s = _body.Process(x * 3f) + 0.25f * x;
            // 0.07 at onset (audible, not alarming) rising to a sound he cannot ignore.
            return s * (0.07f + 0.93f * _amp * _amp) * 0.9f;
        }
    }

    /// <summary>Trigger mailbox + voice pool for the one-shots, plus the continuous stress hum and flutter buzz.</summary>
    internal sealed class OneShotSynth
    {
        private readonly int[] _pending = new int[(int)FxKind.Count];
        private readonly float[] _pendingLevel = new float[(int)FxKind.Count];
        private readonly FxVoice[] _pool;
        private readonly StressHum _groan;
        private readonly FlutterBuzz _flutter;

        public OneShotSynth(float fs)
        {
            _pool = new FxVoice[4];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new FxVoice(fs, 0x51A7Eu + (uint)i * 0x9E3779B9u);
            _groan = new StressHum(fs);
            _flutter = new FlutterBuzz(fs);
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
        public void Prepare(float ias, float groanSeverity, float aircraftGain = 1f, float flutterHz = 0f, float flutterG = 0f)
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
            _flutter.Prepare(flutterHz, flutterG);
        }

        public float Next()
        {
            float s = (_groan.Next() + _flutter.Next()) * _aircraftGain;
            for (int i = 0; i < _pool.Length; i++) if (_pool[i].Active) s += _pool[i].Next();
            return s;
        }
    }
}
