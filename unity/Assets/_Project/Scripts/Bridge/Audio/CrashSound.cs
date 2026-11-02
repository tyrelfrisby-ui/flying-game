using System;
using System.Threading;

namespace FlyingGame.Bridge
{
    // Pure managed DSP, also compiled verbatim by tools/CrashAudio. SI inputs; presentation constants
    // below are exposed/documented as acoustic calibration, not claimed as a fluid solver.
    internal static class CrashScale
    {
        public static float Energy(float mass, float speed) => 0.5f * Math.Max(0f, mass) * speed * speed;
        // Gravity cavity length: E ~ rho*g*L^4. Gravity collapse time ~ sqrt(L/g).
        public static float Cavity(float mass, float speed) => Dsp.Clamp(Dsp.Pow(Energy(mass, speed) / 9810f, .25f), .06f, 6f);
        // Vent approximation: share rising flux equally between packet volume and detachment rate.
        // Thus volume ~ sqrt(Q), R ~ Q^(1/6), and packet frequency also ~ sqrt(Q).
        public static float BubbleRadius(float flow) => Dsp.Clamp(.004f * Dsp.Pow(Math.Max(flow, 0f) / .001f, 1f / 6f), .002f, .025f);
        public static float BubbleHz(float radius, float depth) => (float)(Math.Sqrt(3 * 1.4 * (101325 + 9810 * Math.Max(0, depth)) / 1000) / (2 * Math.PI * radius));
    }

    internal sealed class CrashSound
    {
        private readonly float _fs;
        private readonly Noise _noise;
        private readonly Biquad _high = new Biquad(), _tear = new Biquad();
        private readonly OnePole _low = new OnePole(), _sheet = new OnePole();
        private float _t, _duration, _level, _grain, _grainDecay, _rate, _gain, _cavity;
        private int _next, _sub;
        private float _tailEnv, _tailDecay, _slapEnv, _slapDecay, _weightEnv, _weightDecay;
        private bool _water;
        public bool Active { get; private set; }
        public float Age => _t;
        public CrashSound(float fs, uint seed) { _fs = fs; _noise = new Noise(seed); }
        public void Start(bool water, float severity, float mass, float normalSpeed, float gain = 1f)
        {
            _water = water; _t = 0; _sub = _next = 0; _grain = 0;
            _high.Reset(); _tear.Reset(); _low.Reset(); _sheet.Reset();
            _cavity = CrashScale.Cavity(mass, normalSpeed);
            // Pressure amplitude follows sqrt(energy / duration); reference is 20 kJ. Bounded for mobile mix headroom.
            _duration = water ? Dsp.Clamp(6f * (float)Math.Sqrt(_cavity / 9.81f), .35f, 4.5f)
                : Dsp.Clamp(.25f + .55f * severity + .12f * Dsp.Pow(mass / 800f, 1f / 3f), .2f, 1.5f);
            _level = water ? Dsp.Clamp((float)Math.Sqrt(CrashScale.Energy(mass, normalSpeed) / (20000f * _duration)), .01f, 1.3f)
                : .3f + .7f * Dsp.Clamp01(severity);
            _tailEnv = _slapEnv = _weightEnv = 1f;
            _tailDecay = Dsp.DecayCoef(_duration / 6f, _fs);
            _slapDecay = Dsp.DecayCoef(.012f, _fs);
            _weightDecay = Dsp.DecayCoef(water ? .025f + .025f * _cavity : .008f, _fs);
            _gain = gain; _rate = (110f + 240f * severity) * Dsp.Clamp(Dsp.Pow(mass / 800f, 1f / 3f), .35f, 1.5f);
            _high.HighPass(_fs, water ? 500f : 650f, .7f);
            _low.SetCutoff(water ? Dsp.Clamp(100f / _cavity, 35f, 220f) : 100f, _fs);
            _grainDecay = Dsp.DecayCoef(.002f, _fs);
            Active = true;
        }
        public float Next()
        {
            if (!Active) return 0;
            float u = _t / _duration;
            if ((_sub++ & 127) == 0)
            {
                // Descending sheet/tear spectrum with a residual rough upper band; no pitched oscillator.
                _sheet.SetCutoff(_water ? 900f + 6500f * (1f - u) * (1f - u) : 2200f + 6000f * (1f - u), _fs);
                _tear.BandPass(_fs, 950f + 3600f * (1f - u) * (1f - u), .65f);
            }
            float n = _noise.Next();
            float attack = Math.Min(_t / .0015f, 1f);
            float tail = _tailEnv * attack * Math.Min(1f, (1f - u) * 20f);
            float y;
            if (_water)
            {
                float slap = _high.Process(n) * _slapEnv;
                float spray = _sheet.Process(n) * tail * Math.Min(_t / .025f, 1f);
                float whump = _low.Process(n) * 4f * _weightEnv;
                y = .65f * slap + 1.6f * spray + .65f * whump;
            }
            else
            {
                if (--_next <= 0)
                {
                    _next = Math.Max(1, (int)(_fs / (_rate * (1f - u) + 15f) * _noise.Range(.2f, 1.8f)));
                    _grain = _noise.Range(.35f, 1f);
                    _grainDecay = Dsp.DecayCoef(_noise.Range(.0007f, .004f), _fs);
                }
                _grain *= _grainDecay;
                float grit = _high.Process(_sheet.Process(n)) * (1.1f * _grain + .12f);
                float tear = _tear.Process(n * _grain) * .7f;
                float weight = _low.Process(n) * .6f * _weightEnv;
                y = (grit + tear) * tail * 2.2f + weight;
            }
            _tailEnv *= _tailDecay; _slapEnv *= _slapDecay; _weightEnv *= _weightDecay;
            _t += 1f / _fs;
            if (_t >= _duration) Active = false;
            return y * _level * _gain;
        }
    }

    internal sealed class FloodSound
    {
        private readonly float _fs;
        private readonly Noise _noise;
        private float _rate, _radius, _depth, _target, _gain, _phase, _env, _decay, _hz, _wait, _stale;
        public int Id;
        public bool Active => _stale < .75f || _env > .0001f;
        public FloodSound(float fs, uint seed) { _fs = fs; _noise = new Noise(seed); _stale = 10; }
        public void Set(int id, float flow, float air, float depth, float gain)
        {
            if (Id != id || !Active) { _env = _phase = _wait = _gain = 0; }
            Id = id; _stale = 0; _depth = Math.Max(depth, 0);
            _radius = CrashScale.BubbleRadius(flow);
            float volume = (float)(4 * Math.PI / 3) * _radius * _radius * _radius;
            // One audible packet represents a cluster; full microscopic Q/V rates would alias and overload.
            _rate = Math.Min(80f, Math.Max(0, flow) / (volume * 256f));
            _target = flow <= 0 || air <= 0 ? 0 : gain * Dsp.Clamp01(air / .01f) * Dsp.Clamp(Dsp.Pow(flow / .001f, .5f), 0f, 1f) * (float)Math.Exp(-_depth / 12f);
        }
        public float Next()
        {
            _stale += 1f / _fs;
            float target = _stale < .75f ? _target : 0;
            _gain += (target - _gain) * (1f / (.03f * _fs));
            if (target > 0 && (_wait -= 1f / _fs) <= 0)
            {
                _wait = _noise.Range(.4f, 1.6f) / Math.Max(_rate, .01f);
                float radius = _radius * _noise.Range(.7f, 1.4f);
                _hz = Math.Min(_fs * .35f, CrashScale.BubbleHz(radius, _depth));
                _decay = Dsp.DecayCoef(12f / _hz, _fs); // damped bubble, about twelve cycles per e-fold
                _env = _noise.Range(.3f, .8f); _phase = 0;
            }
            _phase = Dsp.Wrap01(_phase + _hz / _fs); _env *= _decay;
            return Dsp.Sin01(_phase) * _env * _gain * .22f;
        }
    }

    // Single producer (sim/main), single consumer (audio), bounded queue. Publication uses acquire/release.
    internal sealed class CrashSoundMixer
    {
        private struct Command { public bool Flood, Water; public int Id, DelaySamples; public float Severity, Mass, Speed, Flow, Air, Depth, Gain; }
        private readonly Command[] _queue = new Command[256];
        private int _write, _read;
        private readonly CrashSound[] _hits = new CrashSound[8];
        private readonly FloodSound[] _floods = new FloodSound[12];
        private readonly Command[] _waiting = new Command[32];
        private readonly bool[] _waitActive = new bool[32];
        private readonly float _fs;
        public CrashSoundMixer(float fs)
        {
            _fs = fs;
            for (int i = 0; i < _hits.Length; i++) _hits[i] = new CrashSound(fs, 0x88A1u + (uint)i * 7919u);
            for (int i = 0; i < _floods.Length; i++) _floods[i] = new FloodSound(fs, 0x192Bu + (uint)i * 7919u);
        }
        private void Enqueue(Command c)
        {
            int w = _write, next = (w + 1) & 255;
            if (next == Volatile.Read(ref _read)) return; // bounded overload: drop newest
            _queue[w] = c; Volatile.Write(ref _write, next);
        }
        public void Impact(bool water, float severity, float mass, float speed, float gain, float delay) => Enqueue(new Command { Water = water, Severity = severity, Mass = mass, Speed = speed, Gain = gain, DelaySamples = Math.Max(0, (int)Math.Ceiling(delay * _fs)) });
        public void Flood(int id, float flow, float air, float depth, float gain) => Enqueue(new Command { Flood = true, Id = id, Flow = flow, Air = air, Depth = depth, Gain = gain });
        public void Prepare()
        {
            int end = Volatile.Read(ref _write), r = _read;
            while (r != end)
            {
                Command c = _queue[r]; r = (r + 1) & 255;
                if (c.Flood)
                {
                    FloodSound v = null;
                    for (int i = 0; i < _floods.Length; i++) if (_floods[i].Active && _floods[i].Id == c.Id) { v = _floods[i]; break; }
                    if (v == null) for (int i = 0; i < _floods.Length; i++) if (!_floods[i].Active) { v = _floods[i]; break; }
                    v?.Set(c.Id, c.Flow, c.Air, c.Depth, c.Gain);
                }
                else for (int i = 0; i < _waiting.Length; i++) if (!_waitActive[i]) { _waiting[i] = c; _waitActive[i] = true; break; }
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
                CrashSound v = _hits[0];
                for (int j = 0; j < _hits.Length; j++) { if (!_hits[j].Active) { v = _hits[j]; break; } if (_hits[j].Age > v.Age) v = _hits[j]; }
                v.Start(c.Water, c.Severity, c.Mass, c.Speed, c.Gain);
            }
            float sum = 0;
            for (int i = 0; i < _hits.Length; i++) if (_hits[i].Active) sum += _hits[i].Next();
            for (int i = 0; i < _floods.Length; i++) if (_floods[i].Active) sum += _floods[i].Next();
            return Dsp.Clamp(Dsp.SoftClip(sum * .65f), -1f, 1f); // fixed headroom, no gain pumping as pieces expire
        }
    }
}
