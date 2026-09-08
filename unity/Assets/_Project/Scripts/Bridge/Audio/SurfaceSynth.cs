namespace FlyingGame.Bridge
{
    /// <summary>
    /// What the wheels sound like on the ground: on GRAVEL a crunching hiss — broadband noise band-passed
    /// around 1.5–3 kHz (stones grinding under the tyres) with a shower of short random crackles whose rate
    /// and brightness rise with speed; on ROUGH ground a low thudding rumble (the gear hammering over
    /// lumps, ~4–8 Hz at taxi speeds). Silent on pavement and grass. Audio-thread only, no allocation.
    /// </summary>
    internal sealed class SurfaceSynth
    {
        private readonly float _fs;
        private readonly Noise _n = new Noise(0x6A7E15u), _nc = new Noise(0x51C0DEu), _nr = new Noise(0x7B0B1Eu);
        private readonly Biquad _hiss = new Biquad(), _crack = new Biquad(), _thud = new Biquad();
        private readonly float _smoothA;
        private float _gGain, _gGainT, _crackRate, _crackEnv, _crackDecay;
        private float _rGain, _rGainT, _thudPhase, _thudHz = 5f, _thudEnv, _thudDecay;

        public SurfaceSynth(float fs)
        {
            _fs = fs;
            _smoothA = Dsp.Tau(0.05f, fs);
            _hiss.BandPass(fs, 2200f, 0.9f);
            _crack.BandPass(fs, 3800f, 2.0f);
            _thud.LowPass(fs, 90f, 0.8f);
            _crackDecay = 1f - 1f / (0.004f * fs);
            _thudDecay = 1f - 1f / (0.06f * fs);
        }

        /// <summary>Per-block update: ground speed (m/s) while on gravel / rough ground (0 = not on it).</summary>
        public void Prepare(float gravelSpeed, float roughSpeed)
        {
            float g = gravelSpeed < 0f ? 0f : gravelSpeed;
            _gGainT = 0.55f * Dsp.SmoothStep(0.4f, 14f, g);
            _crackRate = 40f + 60f * g;                       // crackles per second
            _hiss.BandPass(_fs, 1500f + 60f * g, 0.9f);
            float r = roughSpeed < 0f ? 0f : roughSpeed;
            _rGainT = 0.7f * Dsp.SmoothStep(0.5f, 10f, r);
            _thudHz = 2.5f + 0.35f * r;                       // lumps every ~3 m
        }

        public void Next(out float l, out float r)
        {
            _gGain += _smoothA * (_gGainT - _gGain);
            _rGain += _smoothA * (_rGainT - _rGain);
            float n = _n.Next();
            float hiss = _hiss.Process(n) * 1.6f;
            // Random crackles: Poisson-ish impulses into a fast-decaying envelope.
            if (_nc.Next() * 0.5f + 0.5f < _crackRate / _fs) _crackEnv = 0.6f + 0.4f * (_nc.Next() * 0.5f + 0.5f);
            _crackEnv *= _crackDecay;
            float crack = _crack.Process(n * _crackEnv) * 4f;
            float gravel = (hiss + crack) * _gGain;

            // Rough ground: thuds at the lump rate with random amplitude.
            _thudPhase += _thudHz / _fs;
            if (_thudPhase >= 1f) { _thudPhase -= 1f; _thudEnv = 0.5f + 0.5f * (_nr.Next() * 0.5f + 0.5f); }
            _thudEnv *= _thudDecay;
            float thud = _thud.Process(_nr.Next() * _thudEnv) * 6f * _rGain;

            l = gravel * 0.95f + thud;
            r = gravel * 1.05f + thud;
        }
    }
}
