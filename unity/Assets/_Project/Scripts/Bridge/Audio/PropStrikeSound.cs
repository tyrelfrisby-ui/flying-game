using System;
using System.Threading;

namespace FlyingGame.Bridge
{
    // Pure managed DSP, like CrashSound: SI inputs, no Unity types, so a console tool can render it.
    //
    // One voice per blade per pass through the ground or the water (Ty 2026-11-05 9:04: "prop hits should make accurate
    // sounds on both the ground and water too per blade strike"). The sim raises one ContactEventKind.PropBladeStrike for each
    // pass, timed within its step; FlightAudio turns each into a Strike with a delay in whole samples, so a prop chewing
    // the ground plays at blades × rpm, not once a frame. What a hit sounds like is what was hit and what hit it:
    //  - METAL on the ground: the blade rings. A blade is a tapered cantilever, and an impact at the tip excites its
    //    higher flatwise / edgewise / torsion modes, a few inharmonic partials between 0.6 and 4.5 kHz decaying in a
    //    tenth of a second (the "ting"), over a short gritty click of the ground itself (the "clank");
    //  - WOOD: a crack. Wood damps its modes in a few milliseconds: a hard broadband transient and one dull body knock;
    //  - COMPOSITE: a crunch. Fibres and resin fail as a burst of small cracks over a few tens of milliseconds;
    //  - WATER, whatever the blade: a slap and a thwack. The flat of the blade hits the surface (a short high slap), drives
    //    a cavity that collapses (a low thump), and throws spray (a hiss).
    // Loudness follows the blade's tip speed and how deep it went; bounded for the mobile mix.
    internal enum PropStrikeKind { MetalGround = 0, WoodGround, CompositeGround, Water, WoodSplinter, CompositeFray }

    internal sealed class PropStrikeVoice
    {
        private readonly float _fs;
        private readonly Noise _noise;
        private readonly Biquad _band = new Biquad(), _body = new Biquad(), _hiss = new Biquad();
        private readonly float[] _modeHz = new float[4], _modePh = new float[4], _modeEnv = new float[4], _modeDecay = new float[4], _modeAmp = new float[4];
        private PropStrikeKind _kind;
        private float _t, _duration, _level, _clickEnv, _clickDecay, _bodyEnv, _bodyDecay, _hissEnv, _hissDecay, _grain, _grainDecay;
        private int _nextGrain;
        public bool Active { get; private set; }
        public float Age => _t;
        public PropStrikeVoice(float fs, uint seed) { _fs = fs; _noise = new Noise(seed); }

        /// <param name="tipSpeed">The blade tip's speed, m/s (ω·R).</param>
        /// <param name="depth">How far the tip went in, m.</param>
        public void Start(PropStrikeKind kind, float tipSpeed, float depth, float gain)
        {
            _kind = kind; _t = 0; _grain = 0; _nextGrain = 0;
            _band.Reset(); _body.Reset(); _hiss.Reset();
            // Impact energy ∝ v²·(depth): pressure ∝ its square root; 220 m/s and 2 cm is full. Bounded.
            float e = Dsp.Clamp01(tipSpeed / 220f) * Dsp.Clamp01((float)Math.Sqrt(Math.Max(depth, 0f) / 0.02f) * 0.8f + 0.2f);
            _level = gain * (0.08f + 0.92f * e);
            _clickEnv = _bodyEnv = _hissEnv = 1f;
            for (int i = 0; i < 4; i++) { _modeEnv[i] = 0f; _modePh[i] = 0f; }
            switch (kind)
            {
                case PropStrikeKind.MetalGround:
                {
                    float[] hz = { 620f, 1480f, 2730f, 4310f }, tau = { .12f, .08f, .05f, .03f }, amp = { .5f, .8f, .55f, .3f };
                    for (int i = 0; i < 4; i++)
                    {
                        _modeHz[i] = hz[i] * _noise.Range(.97f, 1.03f);
                        _modeDecay[i] = Dsp.DecayCoef(tau[i], _fs); _modeEnv[i] = 1f; _modeAmp[i] = amp[i];
                    }
                    _clickDecay = Dsp.DecayCoef(.0012f, _fs);
                    _band.BandPass(_fs, 2600f, .9f);                       // the ground's grit under the ring
                    _bodyDecay = Dsp.DecayCoef(.012f, _fs); _body.BandPass(_fs, 240f, 1.2f);
                    _hissDecay = Dsp.DecayCoef(.02f, _fs); _hiss.BandPass(_fs, 3800f, .7f);
                    _duration = .45f;
                    break;
                }
                case PropStrikeKind.WoodGround:
                case PropStrikeKind.WoodSplinter:
                    _clickDecay = Dsp.DecayCoef(.0008f, _fs);
                    _band.BandPass(_fs, kind == PropStrikeKind.WoodSplinter ? 2400f : 1700f, .8f);
                    _bodyDecay = Dsp.DecayCoef(.016f, _fs); _body.BandPass(_fs, 360f, 1.6f);   // the knock: wood damps its own ring
                    _hissDecay = Dsp.DecayCoef(kind == PropStrikeKind.WoodSplinter ? .05f : .01f, _fs); _hiss.BandPass(_fs, 3000f, .8f);
                    _grainDecay = Dsp.DecayCoef(.0015f, _fs);
                    _duration = kind == PropStrikeKind.WoodSplinter ? .22f : .09f;
                    break;
                case PropStrikeKind.CompositeGround:
                case PropStrikeKind.CompositeFray:
                    _clickDecay = Dsp.DecayCoef(.0006f, _fs);
                    _band.BandPass(_fs, 1900f, 1.1f);
                    _bodyDecay = Dsp.DecayCoef(.02f, _fs); _body.BandPass(_fs, 260f, 1.4f);
                    _hissDecay = Dsp.DecayCoef(.03f, _fs); _hiss.BandPass(_fs, 4200f, .9f);
                    _grainDecay = Dsp.DecayCoef(.0009f, _fs);
                    _duration = kind == PropStrikeKind.CompositeFray ? .18f : .07f;
                    break;
                default:   // water
                    _clickDecay = Dsp.DecayCoef(.004f, _fs);
                    _band.HighPass(_fs, 900f, .7f);                        // the slap of the flat blade on the surface
                    _bodyDecay = Dsp.DecayCoef(.03f, _fs); _body.BandPass(_fs, 140f, 1.3f);    // the thwack: the cavity's collapse
                    _hissDecay = Dsp.DecayCoef(.06f, _fs); _hiss.BandPass(_fs, 5000f, .6f);   // thrown spray
                    _duration = .2f;
                    break;
            }
            Active = true;
        }

        public float Next()
        {
            if (!Active) return 0f;
            float n = _noise.Next(), y;
            float attack = _t < .0004f ? _t / .0004f : 1f;
            switch (_kind)
            {
                case PropStrikeKind.MetalGround:
                {
                    float ring = 0f;
                    for (int i = 0; i < 4; i++)
                    {
                        _modePh[i] += _modeHz[i] / _fs; if (_modePh[i] >= 1f) _modePh[i] -= 1f;
                        ring += _modeAmp[i] * _modeEnv[i] * Dsp.Sin01(_modePh[i]);
                        _modeEnv[i] *= _modeDecay[i];
                    }
                    y = .55f * ring * attack + 1.4f * _band.Process(n) * _clickEnv + .9f * _body.Process(n) * _bodyEnv + .25f * _hiss.Process(n) * _hissEnv;
                    break;
                }
                case PropStrikeKind.WoodGround:
                case PropStrikeKind.WoodSplinter:
                case PropStrikeKind.CompositeGround:
                case PropStrikeKind.CompositeFray:
                {
                    bool composite = _kind is PropStrikeKind.CompositeGround or PropStrikeKind.CompositeFray;
                    bool tearing = _kind is PropStrikeKind.WoodSplinter or PropStrikeKind.CompositeFray;
                    // The cracks: one big one at the hit, then (tearing, or a composite crunch) small ones at random spacing.
                    if ((composite || tearing) && --_nextGrain <= 0 && _t < _duration * .8f)
                    {
                        float rate = composite ? 700f : 260f;
                        _nextGrain = Math.Max(1, (int)(_fs / rate * _noise.Range(.3f, 1.7f)));
                        _grain = Math.Max(_grain, _noise.Range(.25f, .8f) * (1f - _t / _duration));
                    }
                    _grain *= _grainDecay;
                    float crack = _band.Process(n * (_clickEnv * 1.6f + _grain));
                    y = 1.6f * crack + 1.1f * _body.Process(n) * _bodyEnv * attack + (tearing ? .5f : .2f) * _hiss.Process(n) * _hissEnv;
                    break;
                }
                default:
                    y = 1.0f * _band.Process(n) * _clickEnv + 2.2f * _body.Process(n) * _bodyEnv * attack + .45f * _hiss.Process(n) * _hissEnv * Math.Min(1f, _t / .004f);
                    break;
            }
            _clickEnv *= _clickDecay; _bodyEnv *= _bodyDecay; _hissEnv *= _hissDecay;
            _t += 1f / _fs;
            if (_t >= _duration) Active = false;
            return y * _level;
        }
    }

    /// <summary>The blade strikes, from the main thread (Strike) to the audio thread (Prepare / Next): a bounded lock-free
    /// queue, each strike waiting its own whole number of samples so the passes keep their spacing.</summary>
    internal sealed class PropStrikeMixer
    {
        private struct Command { public PropStrikeKind Kind; public float TipSpeed, Depth, Gain; public int DelaySamples; }
        private readonly Command[] _queue = new Command[256];
        private int _write, _read;
        private readonly PropStrikeVoice[] _voices = new PropStrikeVoice[16];
        private readonly Command[] _waiting = new Command[64];
        private readonly bool[] _waitActive = new bool[64];
        private readonly float _fs;
        /// <summary>Strikes started on the audio thread so far (one per command that reached a voice).</summary>
        public int Started;

        public PropStrikeMixer(float fs)
        {
            _fs = fs;
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new PropStrikeVoice(fs, 0x51A7u + (uint)i * 7919u);
        }

        /// <summary>A blade strike to play <paramref name="delaySeconds"/> from now (rounded up to a whole sample).</summary>
        public void Strike(PropStrikeKind kind, float tipSpeed, float depth, float gain, float delaySeconds)
        {
            int w = _write, next = (w + 1) & 255;
            if (next == Volatile.Read(ref _read)) return;   // bounded overload: drop the newest
            _queue[w] = new Command { Kind = kind, TipSpeed = tipSpeed, Depth = depth, Gain = gain, DelaySamples = Math.Max(0, (int)Math.Ceiling(delaySeconds * _fs)) };
            Volatile.Write(ref _write, next);
        }

        public void Prepare()
        {
            int end = Volatile.Read(ref _write), r = _read;
            while (r != end)
            {
                Command c = _queue[r]; r = (r + 1) & 255;
                for (int i = 0; i < _waiting.Length; i++) if (!_waitActive[i]) { _waiting[i] = c; _waitActive[i] = true; break; }
            }
            Volatile.Write(ref _read, r);
        }

        public float Next()
        {
            for (int i = 0; i < _waiting.Length; i++)
            {
                if (!_waitActive[i]) continue;
                if (_waiting[i].DelaySamples-- > 0) continue;
                Command c = _waiting[i]; _waitActive[i] = false;
                PropStrikeVoice v = _voices[0];
                for (int j = 0; j < _voices.Length; j++) { if (!_voices[j].Active) { v = _voices[j]; break; } if (_voices[j].Age > v.Age) v = _voices[j]; }
                v.Start(c.Kind, c.TipSpeed, c.Depth, c.Gain);
                Started++;
            }
            float sum = 0f;
            for (int i = 0; i < _voices.Length; i++) if (_voices[i].Active) sum += _voices[i].Next();
            return Dsp.Clamp(Dsp.SoftClip(sum * .7f), -1f, 1f);
        }
    }
}
